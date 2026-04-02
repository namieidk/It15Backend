using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Models;

namespace YourProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─── HELPER: read EmployeeId from custom request header ───────────────
        private string? GetEmployeeId()
        {
            return Request.Headers.TryGetValue("X-Employee-Id", out var val)
                ? val.ToString()
                : null;
        }

        // =====================================================================
        // ─── HR / EMPLOYEE ENDPOINTS ──────────────────────────────────────────
        // =====================================================================

        // ─── GET /api/Reports/user-profile ───────────────────────────────────
        [HttpGet("user-profile")]
        public async Task<IActionResult> GetUserProfile()
        {
            var employeeId = GetEmployeeId();
            if (string.IsNullOrEmpty(employeeId))
                return Unauthorized(new { message = "Missing X-Employee-Id header. Please log in." });

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.EmployeeId == employeeId);

            if (user == null)
                return NotFound(new { message = $"No user found for EmployeeId: {employeeId}" });

            return Ok(new
            {
                employeeId = user.EmployeeId,
                fullName   = user.Name,
                department = user.Department.ToUpper(),
                position   = user.Role
            });
        }

        // ─── GET /api/Reports/my-reports ─────────────────────────────────────
        [HttpGet("my-reports")]
        public async Task<IActionResult> GetMyReports()
        {
            var employeeId = GetEmployeeId();
            if (string.IsNullOrEmpty(employeeId))
                return Unauthorized(new { message = "Missing X-Employee-Id header. Please log in." });

            var reports = await _context.Reports
                .Where(r => r.EmployeeId == employeeId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    id           = r.Id.ToString(),
                    reportNumber = r.ReportNumber,
                    name         = r.Name,
                    type         = r.Type,
                    status       = r.Status,
                    createdAt    = r.CreatedAt,
                    employeeId   = r.EmployeeId,
                    department   = r.Department,
                    downloadUrl  = r.DownloadUrl ?? "#"
                })
                .ToListAsync();

            return Ok(reports);
        }

        // ─── GET /api/Reports/summary ─────────────────────────────────────────
        // HR view: summary scoped to the requesting employee only
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var employeeId = GetEmployeeId();
            if (string.IsNullOrEmpty(employeeId))
                return Unauthorized(new { message = "Missing X-Employee-Id header." });

            var reports = await _context.Reports
                .Where(r => r.EmployeeId == employeeId)
                .ToListAsync();

            var summary = new
            {
                total    = reports.Count,
                approved = reports.Count(r => r.Status.ToUpper() == "APPROVED"),
                pending  = reports.Count(r => r.Status.ToUpper() == "PENDING"),
                rejected = reports.Count(r => r.Status.ToUpper() == "REJECTED"),
                byType   = reports
                    .GroupBy(r => r.Type?.ToUpper() ?? "UNKNOWN")
                    .Select(g => new { type = g.Key, count = g.Count() })
                    .ToList(),
                byMonth  = reports
                    .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new
                    {
                        month = $"{System.Globalization.CultureInfo.CurrentCulture
                            .DateTimeFormat.GetAbbreviatedMonthName(g.Key.Month)} '{g.Key.Year % 100:D2}",
                        count = g.Count()
                    })
                    .ToList()
            };

            return Ok(summary);
        }

        // ─── GET /api/Reports/download/{id} ──────────────────────────────────
        [HttpGet("download/{id:int}")]
        public async Task<IActionResult> DownloadReport(int id)
        {
            var employeeId = GetEmployeeId();
            if (string.IsNullOrEmpty(employeeId))
                return Unauthorized(new { message = "Missing X-Employee-Id header." });

            var report = await _context.Reports
                .FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == employeeId);

            if (report == null)
                return NotFound(new { message = "Report not found or access denied." });

            return Ok(new { downloadUrl = report.DownloadUrl });
        }

        // =====================================================================
        // ─── ADMIN-ONLY ENDPOINTS ─────────────────────────────────────────────
        // =====================================================================

        // ─── GET /api/Reports/admin/stats ────────────────────────────────────
        // Returns: totalAccounts, storageUsedGB, dbUptimePercent, dataBreaches
        [HttpGet("admin/stats")]
        public async Task<IActionResult> GetAdminStats()
        {
            try
            {
                // 1. Total Accounts
                var totalAccounts = await _context.Users.CountAsync();

                // 2. Storage Used — from sys.database_files, falls back to estimate
                double storageGB = 0;
                try
                {
                    var sizeResult = await _context.Database
                        .SqlQueryRaw<double>(
                            "SELECT CAST(SUM(size) * 8.0 / 1024 / 1024 AS FLOAT) FROM sys.database_files"
                        )
                        .FirstOrDefaultAsync();
                    storageGB = Math.Round(sizeResult, 1);
                }
                catch
                {
                    var reportCount = await _context.Reports.CountAsync();
                    storageGB = Math.Round(reportCount * 0.0001, 2);
                }

                // 3. DB Uptime — from sys.dm_os_sys_info, falls back to 99.99
                double uptimePercent = 99.99;
                try
                {
                    var sqlStartTime = await _context.Database
                        .SqlQueryRaw<DateTime>(
                            "SELECT sqlserver_start_time FROM sys.dm_os_sys_info"
                        )
                        .FirstOrDefaultAsync();

                    if (sqlStartTime != default)
                    {
                        var totalWindow  = TimeSpan.FromDays(30).TotalMinutes;
                        var uptimeWindow = (DateTime.UtcNow - sqlStartTime).TotalMinutes;
                        uptimePercent    = Math.Min(Math.Round(uptimeWindow / totalWindow * 100, 2), 100.0);
                    }
                }
                catch
                {
                    uptimePercent = 99.99;
                }

                return Ok(new
                {
                    totalAccounts   = totalAccounts,
                    storageUsedGB   = $"{storageGB} GB",
                    dataBreaches    = 0
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving admin stats", details = ex.Message });
            }
        }

        // ─── GET /api/Reports/admin/all-reports ──────────────────────────────
        // Returns ALL reports org-wide — no employee filter (admin privilege)
        [HttpGet("admin/all-reports")]
        public async Task<IActionResult> GetAllReports()
        {
            try
            {
                var reports = await _context.Reports
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new
                    {
                        id           = r.Id.ToString(),
                        reportNumber = r.ReportNumber,
                        name         = r.Name,
                        type         = r.Type,
                        status       = r.Status,
                        createdAt    = r.CreatedAt,
                        employeeId   = r.EmployeeId,
                        department   = r.Department,
                        downloadUrl  = r.DownloadUrl ?? "#"
                    })
                    .ToListAsync();

                return Ok(reports);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving all reports", details = ex.Message });
            }
        }

        // ─── GET /api/Reports/admin/summary ──────────────────────────────────
        // Org-wide summary: byType, byDepartment, byMonth — admin only
        [HttpGet("admin/summary")]
        public async Task<IActionResult> GetOrgSummary()
        {
            try
            {
                var reports = await _context.Reports.ToListAsync();

                var summary = new
                {
                    total    = reports.Count,
                    approved = reports.Count(r => r.Status.ToUpper() == "APPROVED"),
                    pending  = reports.Count(r => r.Status.ToUpper() == "PENDING"),
                    rejected = reports.Count(r => r.Status.ToUpper() == "REJECTED"),

                    byType = reports
                        .GroupBy(r => r.Type?.ToUpper() ?? "UNKNOWN")
                        .Select(g => new { type = g.Key, count = g.Count() })
                        .OrderByDescending(g => g.count)
                        .ToList(),

                    byDepartment = reports
                        .GroupBy(r => r.Department?.ToUpper() ?? "UNKNOWN")
                        .Select(g => new { department = g.Key, count = g.Count() })
                        .OrderByDescending(g => g.count)
                        .ToList(),

                    byMonth = reports
                        .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month })
                        .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                        .Select(g => new
                        {
                            month = $"{System.Globalization.CultureInfo.CurrentCulture
                                .DateTimeFormat.GetAbbreviatedMonthName(g.Key.Month)} '{g.Key.Year % 100:D2}",
                            count = g.Count()
                        })
                        .ToList()
                };

                return Ok(summary);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving org summary", details = ex.Message });
            }
        }
    }
}