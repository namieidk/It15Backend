using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Models;

namespace YourProjectName.Controllers
{
    [ApiController]
    [Route("api/leave")]
    public class LeaveController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LeaveController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // MANAGER ENDPOINTS
        // ==========================================

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingRequests()
        {
            try
            {
                var pending = await (from l in _context.LeaveReq
                                     join e in _context.Employee on l.EmployeeId equals e.EmployeeId
                                     where l.Status == "PENDING"
                                     orderby l.DateSubmitted descending
                                     select new {
                                         id         = l.Id,
                                         employeeId = l.EmployeeId,
                                         name       = e.Name.ToUpper(),
                                         type       = l.LeaveType.ToUpper(),
                                         date       = l.StartDate.ToString("MMM dd") + " - " + l.EndDate.ToString("MMM dd"),
                                         reason     = l.Reason,
                                         priority   = l.LeaveType == "SICK LEAVE" ? "HIGH" : "NORMAL"
                                     }).ToListAsync();

                return Ok(pending);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching pending queue", error = ex.Message });
            }
        }

        [HttpPost("manager-action")]
        public async Task<IActionResult> ManagerAction([FromBody] LeaveActionModel model)
        {
            try
            {
                var request = await _context.LeaveReq.FindAsync(model.RequestId);
                if (request == null) return NotFound(new { message = "Request not found" });

                request.Status = model.Status == "APPROVED" ? "MANAGER_APPROVED" : "REJECTED";

                await _context.SaveChangesAsync();
                return Ok(new { message = $"Request {model.Status} by manager" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Manager action failed", error = ex.Message });
            }
        }

        // ==========================================
        // HR ENDPOINTS
        // ==========================================

        [HttpGet("hr-pending")]
        public async Task<IActionResult> GetHRPendingRequests()
        {
            try
            {
                var pending = await (from l in _context.LeaveReq
                                     join e in _context.Employee on l.EmployeeId equals e.EmployeeId
                                     where l.Status == "MANAGER_APPROVED"
                                     orderby l.DateSubmitted descending
                                     select new {
                                         id         = l.Id,
                                         employeeId = l.EmployeeId,
                                         name       = e.Name.ToUpper(),
                                         type       = l.LeaveType.ToUpper(),
                                         date       = l.StartDate.ToString("MMM dd") + " - " + l.EndDate.ToString("MMM dd"),
                                         reason     = l.Reason,
                                         priority   = l.LeaveType == "SICK LEAVE" ? "HIGH" : "NORMAL"
                                     }).ToListAsync();

                return Ok(pending);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching HR queue", error = ex.Message });
            }
        }

        [HttpPost("hr-action")]
        public async Task<IActionResult> HRAction([FromBody] LeaveActionModel model)
        {
            try
            {
                var request = await _context.LeaveReq.FindAsync(model.RequestId);
                if (request == null) return NotFound(new { message = "Request not found" });

                if (request.Status != "MANAGER_APPROVED")
                    return BadRequest(new { message = "Request is not in the HR review queue" });

                var employee = await _context.Employee
                    .FirstOrDefaultAsync(e => e.EmployeeId == request.EmployeeId);

                if (model.Status == "APPROVED")
                {
                    if (employee != null)
                    {
                        double requestedDays = (request.EndDate.Date - request.StartDate.Date).TotalDays + 1;
                        employee.LeaveBalance -= (int)requestedDays;
                    }
                    request.Status = "APPROVED";
                }
                else
                {
                    request.Status = "REJECTED";
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = $"Request {model.Status} by HR" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "HR action failed", error = ex.Message });
            }
        }

        // ==========================================
        // EMPLOYEE ENDPOINTS
        // ==========================================

        // ✅ Back to _context.Employee — this is where LeaveBalance lives
        [HttpGet("credits/{employeeId}")]
        public async Task<IActionResult> GetCredits(string employeeId)
        {
            var employee = await _context.Employee
                .FirstOrDefaultAsync(e => e.EmployeeId.ToString() == employeeId);

            if (employee == null)
                return NotFound(new { message = $"Employee {employeeId} not found." });

            return Ok(new { balance = employee.LeaveBalance });
        }

        [HttpGet("history/{employeeId}")]
        public async Task<IActionResult> GetHistory(string employeeId)
        {
            // LeaveReq.EmployeeId type must match — convert to int if needed
            if (!int.TryParse(employeeId, out int empId))
                return BadRequest(new { message = "Invalid employee ID format." });

            var history = await _context.LeaveReq
                .Where(l => l.EmployeeId == empId)
                .OrderByDescending(l => l.StartDate)
                .Select(l => new {
                    type   = l.LeaveType,
                    date   = l.StartDate.ToString("MMM dd, yyyy") + " - " + l.EndDate.ToString("MMM dd, yyyy"),
                    status = l.Status ?? "PENDING"
                })
                .ToListAsync();

            return Ok(history);
        }

        [HttpPost("apply")]
        public async Task<IActionResult> ApplyForLeave([FromBody] LeaveRequest request)
        {
            var employee = await _context.Employee
                .FirstOrDefaultAsync(e => e.EmployeeId == request.EmployeeId);

            if (employee == null)
                return NotFound(new { message = "Employee record missing" });

            double requestedDays = (request.EndDate.Date - request.StartDate.Date).TotalDays + 1;
            if (requestedDays <= 0)
                return BadRequest(new { message = "Invalid date range" });

            request.Status        = "PENDING";
            request.DateSubmitted = DateTime.Now;

            _context.LeaveReq.Add(request);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Transmitted to system" });
        }
    }

    public class LeaveActionModel
    {
        public int    RequestId { get; set; }
        public string Status    { get; set; } = string.Empty;
    }
}