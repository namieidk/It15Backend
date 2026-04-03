using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data; 
using YourProject.Models;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─── 1. HR EXECUTIVE DASHBOARD STATS ──────────────────────────────────
        // GET: api/Dashboard/hr-stats
        [HttpGet("hr-stats")]
        public async Task<IActionResult> GetHRDashboardStats()
        {
            try
            {
                // 1. Calculate Active Headcount
                var activeHeadcount = await _context.Users
                    .CountAsync(u => u.Status == "ACTIVE");

                // 2. Count New Applicants (Pending Review)
                var newApplicantsCount = await _context.Applicants
                    .CountAsync(a => a.Status == "PENDING");

                // 3. Open Requisitions
                // (Counting unique Job Titles currently being recruited for)
                var openRequisitions = await _context.Applicants
                    .Where(a => a.Status == "PENDING")
                    .Select(a => a.JobTitle)
                    .Distinct()
                    .CountAsync();

                // 4. Attrition Rate Calculation
                // (Inactive Users / Total Users)
                var totalUsers = await _context.Users.CountAsync();
                double attritionPercent = 0;
                if (totalUsers > 0)
                {
                    var inactiveCount = await _context.Users.CountAsync(u => u.Status == "INACTIVE");
                    attritionPercent = ((double)inactiveCount / totalUsers) * 100;
                }

                // 5. Fetch Recent Applicants for the Pipeline List
                var recentApplicants = await _context.Applicants
                    .Where(a => a.Status == "PENDING")
                    .OrderByDescending(a => a.Id) // Assuming higher ID is newer
                    .Take(4)
                    .Select(a => new {
                        name = (a.FirstName + " " + a.LastName).ToUpper(),
                        role = a.JobTitle.ToUpper(),
                        date = "RECENT", 
                        source = "PORTAL"
                    })
                    .ToListAsync();

                return Ok(new
                {
                    metrics = new {
                        headcount = activeHeadcount.ToString("N0"),
                        requisitions = openRequisitions.ToString(),
                        applicants = newApplicantsCount.ToString(),
                        attrition = $"{attritionPercent:F1}%"
                    },
                    recentApplicants
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    message = "KERNEL_HR_SYNC_ERROR", 
                    details = ex.Message 
                });
            }
        }

        // ─── 2. INDIVIDUAL EMPLOYEE DASHBOARD STATS ────────────────────────────
        // GET: api/Dashboard/stats/{employeeId}
        [HttpGet("stats/{employeeId}")]
        public async Task<IActionResult> GetEmployeeStats(string employeeId)
        {
            try
            {
                // 1. Attendance Rate Logic
                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.IsActive);
                
                int expectedDays = 20; 
                if (schedule != null && !string.IsNullOrEmpty(schedule.WorkingDays))
                {
                    expectedDays = (schedule.WorkingDays.Split(',').Length) * 4;
                }

                var actualDays = await _context.Attendance
                    .Where(a => a.EmployeeId == employeeId && a.Status == "PRESENT")
                    .CountAsync();

                double attendancePercentage = expectedDays > 0 
                    ? Math.Min((double)actualDays / expectedDays * 100, 100) 
                    : 0;

                // 2. CSAT Score (Average from Evaluations)
                var avgEvalScore = await _context.Evaluations
                    .Where(e => e.TargetEmployeeId.ToString() == employeeId)
                    .AverageAsync(e => (double?)e.Score) ?? 0.0;

                // 3. KPI Reports Count
                var reportCount = await _context.Evaluations
                    .CountAsync(e => e.TargetEmployeeId.ToString() == employeeId);

                // 4. Average Handle Time (Placeholder Logic)
                string handleTime = "4m 12s"; 

                return Ok(new
                {
                    attendanceRate = $"{attendancePercentage:F1}%",
                    avgHandleTime = handleTime,
                    csatScore = $"{avgEvalScore:F1}/5",
                    kpiReports = reportCount.ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    message = "INTERNAL_METRIC_ERROR", 
                    details = ex.Message 
                });
            }
        }

        // ─── 3. MANAGER DASHBOARD STATS ────────────────────────────────────────────
// GET: api/Dashboard/manager-stats/{employeeId}
[HttpGet("manager-stats/{employeeId}")]
public async Task<IActionResult> GetManagerDashboardStats(string employeeId)
{
    try
    {
        // Get manager's department
        var manager = await _context.Users
            .FirstOrDefaultAsync(u => u.EmployeeId == employeeId);

        if (manager == null)
            return NotFound(new { message = "Manager not found" });

        var dept = manager.Department.ToUpper();

        // 1. Active Headcount in department
        var activeHeadcount = await _context.Users
            .CountAsync(u => u.Department.ToUpper() == dept
                          && u.Status == "ACTIVE"
                          && u.Role.ToUpper() != "ADMIN");

        // 2. Get employee IDs in this department for leave join
        var deptEmployeeIds = await _context.Users
            .Where(u => u.Department.ToUpper() == dept
                     && u.Role.ToUpper() == "EMPLOYEE"
                     && u.Status == "ACTIVE")
            .Select(u => u.EmployeeId)
            .ToListAsync();

        // 3. Pending Leaves in department (join LeaveReq with Users)
        var pendingLeaves = await _context.LeaveReq
            .CountAsync(l => deptEmployeeIds.Contains(l.EmployeeId.ToString())
                          && l.Status == "PENDING");

        // 4. Team SLA — attendance rate of department employees this month
        var firstDayOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        int totalExpected = deptEmployeeIds.Count * DateTime.Now.Day;
        int totalPresent  = await _context.Attendance
            .CountAsync(a => deptEmployeeIds.Contains(a.EmployeeId)
                          && a.Status == "PRESENT"
                          && a.ClockInTime >= firstDayOfMonth);

        double sla = totalExpected > 0
            ? Math.Min((double)totalPresent / totalExpected * 100, 100)
            : 0;

        // 5. Recent pending leave requests in department
        var pendingApprovals = await _context.LeaveReq
            .Where(l => deptEmployeeIds.Contains(l.EmployeeId.ToString())
                     && l.Status == "PENDING")
            .OrderByDescending(l => l.Id)
            .Take(5)
            .Join(
                _context.Users,
                l => l.EmployeeId.ToString(),
                u => u.EmployeeId,
                (l, u) => new {
                    name = u.Name.ToUpper(),
                    type = l.LeaveType.ToUpper(),
                    date = l.StartDate.ToString("MMM dd") +
                           (l.EndDate.Date != l.StartDate.Date
                               ? " - " + l.EndDate.ToString("MMM dd")
                               : "")
                }
            )
            .ToListAsync();

        return Ok(new
        {
            headcount        = activeHeadcount.ToString(),
            pendingLeaves    = pendingLeaves.ToString(),
            teamSla          = $"{sla:F1}%",
            pendingApprovals
        });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { message = "MANAGER_STATS_ERROR", details = ex.Message });
    }
}
    }
}