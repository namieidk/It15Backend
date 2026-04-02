using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Models;
using BCrypt.Net;

namespace YourProject.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "ADMIN")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─── 1. FETCH APPROVED APPLICANTS ────────────────────────────────────
        [HttpGet("approved-applicants")]
        public async Task<IActionResult> GetApprovedApplicants()
        {
            try
            {
                var approved = await _context.Applicants
                    .Where(a => a.Status == "APPROVED")
                    .Select(a => new {
                        id         = a.Id,
                        fullName   = (a.FirstName + " " + a.LastName).ToUpper(),
                        email      = a.Email,
                        department = a.Department,
                        jobTitle   = a.JobTitle
                    })
                    .OrderBy(a => a.fullName)
                    .ToListAsync();

                return Ok(approved);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "FETCH_ERROR", details = ex.Message });
            }
        }

        // ─── 2. PROVISION ACCOUNT ─────────────────────────────────────────────
        [HttpPost("provision")]
        public async Task<IActionResult> ProvisionAccount([FromBody] UserRegistrationDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.EmployeeId))
                return BadRequest(new { message = "ALL FIELDS ARE REQUIRED." });

            var cleanId   = model.EmployeeId.Trim().ToUpper();
            var cleanName = model.Name.Trim().ToUpper();

            if (await _context.Users.AnyAsync(u => u.EmployeeId == cleanId))
                return BadRequest(new { message = "EMPLOYEE ID ALREADY REGISTERED IN SYSTEM." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = new User {
                    Name         = cleanName,
                    EmployeeId   = cleanId,
                    Role         = model.Role.Trim().ToUpper(),
                    Department   = model.Department.Trim().ToUpper(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                    Status       = "ACTIVE",
                    CreatedAt    = DateTime.UtcNow
                };

                _context.Users.Add(user);

                var applicant = await _context.Applicants
                    .FirstOrDefaultAsync(a =>
                        (a.FirstName + " " + a.LastName).ToUpper() == cleanName
                        && a.Status == "APPROVED");

                if (applicant != null)
                    applicant.Status = "ACCOUNT_CREATED";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "PROVISIONED" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "KERNEL_PROVISIONING_FAILURE", details = ex.Message });
            }
        }

        // ─── 3. SYSTEM SETTINGS (GET) ─────────────────────────────────────────
        [HttpGet("syssetting")]
        public async Task<IActionResult> GetSettings()
        {
            try
            {
                var settings = await _context.SystemSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    settings = new SystemSettings { SessionTimeout = 30, PasswordExpiry = 90, StorageUsage = 84 };
                    _context.SystemSettings.Add(settings);
                    await _context.SaveChangesAsync();
                }
                return Ok(settings);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "DB_SYNC_ERROR", details = ex.Message });
            }
        }

        // ─── 4. SYSTEM SETTINGS (UPDATE) ──────────────────────────────────────
        [HttpPut("syssetting")]
        public async Task<IActionResult> UpdateSettings([FromBody] SystemSettings model)
        {
            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            if (settings == null) return NotFound();

            settings.SessionTimeout = model.SessionTimeout;
            settings.MfaRequired    = model.MfaRequired;
            settings.AlertCritical  = model.AlertCritical;

            await _context.SaveChangesAsync();
            return Ok(settings);
        }

        // ─── 5. FETCH ALL ACCOUNTS ────────────────────────────────────────────
        [HttpGet("accounts")]
        public async Task<IActionResult> GetAllAccounts()
        {
            var users = await _context.Users
                .Select(u => new {
                    id         = u.Id,
                    name       = u.Name,
                    employeeId = u.EmployeeId,
                    role       = u.Role,
                    department = u.Department,
                    status     = u.Status ?? "ACTIVE"
                }).ToListAsync();
            return Ok(users);
        }

        // ─── 6. LOGIN AUDIT LOGS ──────────────────────────────────────────────
        [HttpGet("login-logs")]
        public async Task<IActionResult> GetLoginLogs()
        {
            try
            {
                var logs = await _context.LoginLogs
                    .OrderByDescending(l => l.Timestamp)
                    .Take(100)
                    .Select(l => new {
                        id        = l.Id,
                        user      = l.EmployeeId ?? "UNKNOWN",
                        role      = (string?)null,
                        ipAddress = l.IpAddress  ?? "—",
                        device    = (string?)null,
                        timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        status    = l.Status     ?? "—"
                    })
                    .ToListAsync();

                return Ok(logs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "LOG_FETCH_ERROR", details = ex.Message });
            }
        }

        // ─── 7. ACTIVITY LOGS ─────────────────────────────────────────────────
        // Projection keys match the ActivityLog interface in ActivityLogTable.tsx exactly:
        //   user      ← AuditLog.ActorName
        //   role      ← AuditLog.ActorRole
        //   dept      ← AuditLog.ActorDepartment
        //   action    ← AuditLog.Action
        //   module    ← AuditLog.Module
        //   target    ← AuditLog.Target
        //   ipAddress ← AuditLog.IpAddress
        //   timestamp ← AuditLog.Timestamp
        [HttpGet("activity-logs")]
        public async Task<IActionResult> GetActivityLogs(
            [FromQuery] int    page   = 1,
            [FromQuery] int    limit  = 50,
            [FromQuery] string module = "")
        {
            try
            {
                var query = _context.AuditLogs.AsQueryable();

                if (!string.IsNullOrWhiteSpace(module) && module.ToUpper() != "ALL")
                    query = query.Where(a => a.Module.ToUpper() == module.ToUpper());

                var total = await query.CountAsync();

                var logs = await query
                    .OrderByDescending(a => a.Timestamp)
                    .Skip((page - 1) * limit)
                    .Take(limit)
                    .Select(a => new {
                        id        = a.Id.ToString(),
                        user      = a.ActorName,           // ← matches interface field "user"
                        role      = a.ActorRole,           // ← matches interface field "role"
                        dept      = a.ActorDepartment,     // ← matches interface field "dept"
                        module    = a.Module,
                        action    = a.Action,
                        target    = a.Target    ?? "—",
                        ipAddress = a.IpAddress ?? "—",
                        timestamp = a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToListAsync();

                return Ok(new { logs, total });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "ACTIVITY_LOG_FETCH_ERROR", details = ex.Message });
            }
        }

        // ─── 8. UPDATE / REVOKE / REACTIVATE ──────────────────────────────────
        [HttpPut("update-account/{id}")]
        public async Task<IActionResult> UpdateAccount(int id, [FromBody] UpdateAccountDto model)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.Name = model.Name.ToUpper();
            user.Role = model.Role.ToUpper();
            await _context.SaveChangesAsync();
            return Ok(new { message = "UPDATED" });
        }

        [HttpPut("revoke-account/{id}")]
        public async Task<IActionResult> RevokeAccount(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.Status = "INACTIVE";
            await _context.SaveChangesAsync();
            return Ok(new { message = "REVOKED" });
        }

        [HttpPut("reactivate-account/{id}")]
        public async Task<IActionResult> ReactivateAccount(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.Status = "ACTIVE";
            await _context.SaveChangesAsync();
            return Ok(new { message = "RESTORED" });
        }
    }

    // ─── DTOs ─────────────────────────────────────────────────────────────────
    public class UserRegistrationDto
    {
        public string Name       { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string Role       { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Password   { get; set; } = string.Empty;
    }

    public class UpdateAccountDto
    {
        public string Name       { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string Role       { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
    }
}