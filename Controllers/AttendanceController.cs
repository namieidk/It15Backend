using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Hubs;
using YourProject.Models;
using YourProject.Services;
using System.Text.Json;

namespace YourProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AttendanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<AttendanceHub> _hub;
        private readonly ReportService _reports;
        private readonly TimeZoneInfo _phZone =
            TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

        public AttendanceController(
            ApplicationDbContext context,
            IHubContext<AttendanceHub> hub,
            ReportService reports)
        {
            _context = context;
            _hub = hub;
            _reports = reports;
        }

        // ── GET BY DEPARTMENT ─────────────────────────────────────────────────
        [HttpGet("department/{department}")]
        public async Task<IActionResult> GetByDepartment(string department)
        {
            try
            {
                DateTime phNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _phZone);
                DateTime today = phNow.Date;
                var deptUpper  = department.ToUpper();

                var records = await _context.Attendance
                    .Where(a => a.Department.ToUpper() == deptUpper && a.ClockInTime >= today)
                    .OrderByDescending(a => a.ClockInTime)
                    .ToListAsync();

                var uiData = records.Select(r => new {
                    id     = r.EmployeeId,
                    name   = r.Name?.ToUpper()       ?? "UNKNOWN",
                    dept   = r.Department?.ToUpper() ?? "N/A",
                    shift  = "SCHEDULED",
                    login  = r.ClockInTime.HasValue ? r.ClockInTime.Value.ToString("HH:mm") : "--:--",
                    status = r.Status?.ToUpper()     ?? "PRESENT",
                    health = r.Status == "LATE" ? "WARNING" : "GOOD"
                });

                return Ok(uiData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching department attendance", error = ex.Message });
            }
        }

        // ── WEEKLY SUMMARY ────────────────────────────────────────────────────
        [HttpGet("weekly-summary/{employeeId}")]
        public async Task<IActionResult> GetWeeklySummary(string employeeId)
        {
            try
            {
                DateTime phNow     = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _phZone);
                int      diff      = (7 + (phNow.Date.DayOfWeek - DayOfWeek.Monday)) % 7;
                DateTime weekStart = phNow.Date.AddDays(-diff);
                DateTime weekEnd   = weekStart.AddDays(7);

                var weeklyRecords = await _context.Attendance
                    .Where(a => a.EmployeeId == employeeId &&
                                a.ClockInTime >= weekStart &&
                                a.ClockInTime <  weekEnd)
                    .ToListAsync();

                return Ok(new {
                    employeeId   = employeeId,
                    totalRegular = Math.Round(weeklyRecords.Sum(r => r.RegularHours),  2),
                    totalOT      = Math.Round(weeklyRecords.Sum(r => r.OvertimeHours), 2),
                    daysWorked   = weeklyRecords.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "ERROR CALCULATING WEEKLY STATS", error = ex.Message });
            }
        }

        // ── ALL ATTENDANCE (HR view) ───────────────────────────────────────────
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAttendance()
        {
            try
            {
                DateTime phNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _phZone);
                DateTime today = phNow.Date;

                var records = await _context.Attendance
                    .Where(a => a.ClockInTime >= today)
                    .OrderByDescending(a => a.ClockInTime)
                    .ToListAsync();

                var uiData = records.Select(r => new {
                    id     = r.EmployeeId,
                    name   = r.Name?.ToUpper()       ?? "UNKNOWN",
                    dept   = r.Department?.ToUpper() ?? "N/A",
                    shift  = "SCHEDULED",
                    login  = r.ClockInTime.HasValue ? r.ClockInTime.Value.ToString("HH:mm") : "--:--",
                    status = r.Status?.ToUpper()     ?? "PRESENT",
                    health = r.Status == "LATE" ? "WARNING" : "GOOD"
                });

                return Ok(uiData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching global logs", error = ex.Message });
            }
        }

        // ── CLOCK IN ──────────────────────────────────────────────────────────
        [HttpPost("clockin")]
        public async Task<IActionResult> ClockIn([FromBody] JsonElement body)
        {
            try
            {
                string empId = body.GetProperty("employeeId").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(empId))
                    return BadRequest(new { message = "EMPLOYEE ID IS REQUIRED." });

                DateTime phNow       = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _phZone);
                TimeSpan currentTime = phNow.TimeOfDay;

                var user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == empId);
                if (user == null)
                    return BadRequest(new { message = "USER PROFILE NOT FOUND." });

                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.EmployeeId == empId && s.IsActive);
                if (schedule == null)
                    return BadRequest(new { message = "NO ACTIVE SCHEDULE FOUND. CONTACT HR." });

                // Earliest allowed = ShiftStart minus 30-minute buffer
                TimeSpan earliestAllowed = schedule.ShiftStart.Subtract(TimeSpan.FromMinutes(30));

                // Block anyone trying to clock in before the buffer window
                if (currentTime < earliestAllowed)
                    return BadRequest(new {
                        message = $"TOO EARLY. SHIFT STARTS AT {schedule.ShiftStart:hh\\:mm}. CLOCK-IN ALLOWED FROM {earliestAllowed:hh\\:mm}."
                    });

                // Block duplicate open shifts
                bool alreadyClockedIn = await _context.Attendance
                    .AnyAsync(a => a.EmployeeId == empId && a.ClockOutTime == null);
                if (alreadyClockedIn)
                    return BadRequest(new { message = "ACTIVE SHIFT ALREADY EXISTS. CLOCK OUT FIRST." });

                var record = new Attendance
                {
                    EmployeeId  = empId,
                    Name        = user.Name,
                    Role        = user.Role,
                    Department  = user.Department,
                    ClockInTime = phNow,
                    Status      = (currentTime > schedule.ShiftStart) ? "LATE" : "PRESENT"
                };

                _context.Attendance.Add(record);
                await _context.SaveChangesAsync();

                var newRecord = new {
                    id     = record.EmployeeId,
                    name   = record.Name?.ToUpper()       ?? "UNKNOWN",
                    dept   = record.Department?.ToUpper() ?? "N/A",
                    shift  = "SCHEDULED",
                    login  = phNow.ToString("HH:mm"),
                    status = record.Status?.ToUpper()     ?? "PRESENT",
                    health = record.Status == "LATE" ? "WARNING" : "GOOD"
                };

                await _hub.Clients.Group(record.Department ?? "GENERAL").SendAsync("NewClockIn", newRecord);
                await _hub.Clients.Group("HR_GLOBAL").SendAsync("NewClockIn", newRecord);

                return Ok(new { message = "CLOCK-IN SUCCESSFUL", status = record.Status });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal Server Error", error = ex.Message });
            }
        }

        // ── CLOCK OUT ─────────────────────────────────────────────────────────
        [HttpPost("clockout")]
        public async Task<IActionResult> ClockOut([FromBody] JsonElement body)
        {
            try
            {
                string empId  = body.GetProperty("employeeId").GetString() ?? "";
                var    record = await _context.Attendance
                    .Where(a => a.EmployeeId == empId && a.ClockOutTime == null)
                    .OrderByDescending(a => a.ClockInTime)
                    .FirstOrDefaultAsync();

                if (record == null)
                    return NotFound(new { message = "NO ACTIVE SHIFT FOUND." });

                DateTime phNow      = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _phZone);
                record.ClockOutTime = phNow;

                double workHrs          = (record.ClockOutTime.Value - record.ClockInTime!.Value).TotalHours - 1;
                record.TotalHoursWorked = Math.Max(0, Math.Round(workHrs, 2));
                record.RegularHours     = Math.Min(8, record.TotalHoursWorked);
                record.OvertimeHours    = Math.Max(0, record.TotalHoursWorked - 8);

                await _context.SaveChangesAsync();

                var user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == empId);
                if (user != null)
                {
                    await _reports.CreateAttendanceReportAsync(
                        employeeId:       empId,
                        department:       user.Department,
                        attendanceStatus: record.Status ?? "PRESENT",
                        date:             record.ClockInTime.Value.Date,
                        hoursWorked:      record.TotalHoursWorked
                    );
                }

                return Ok(new { message = "CLOCK-OUT SUCCESSFUL" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Clock out failed.", error = ex.Message });
            }
        }

        // ── MY LOGS ───────────────────────────────────────────────────────────
        [HttpGet("my-logs/{employeeId}")]
        public async Task<IActionResult> GetMyLogs(string employeeId)
        {
            var logs = await _context.Attendance
                .Where(a => a.EmployeeId == employeeId)
                .OrderByDescending(a => a.ClockInTime)
                .ToListAsync();
            return Ok(logs);
        }
    }
}