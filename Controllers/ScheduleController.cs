using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data; 
using YourProject.Models;

namespace YourProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScheduleController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ScheduleController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("roster")]
        public async Task<ActionResult> GetRoster()
        {
            // Fetch users including the ProfileImage field
            var users = await _context.Users
                .Where(u => u.Role == "EMPLOYEE")
                .ToListAsync();

            var schedules = await _context.Schedules.ToListAsync();

            var roster = users.Select(u => {
                var schedule = schedules.FirstOrDefault(s => s.EmployeeId == u.EmployeeId);

                return new {
                    id = u.EmployeeId, 
                    name = u.Name.ToUpper(),
                    dept = u.Department ?? "PRODUCTION",
                    profileImage = u.ProfileImage, // Added ProfileImage from Users table
                    currentShift = schedule == null ? "UNASSIGNED" : "ACTIVE",
                    details = schedule != null ? new {
                        start = schedule.ShiftStart.ToString(@"hh\:mm"),
                        end = schedule.ShiftEnd.ToString(@"hh\:mm"),
                        days = schedule.WorkingDays
                    } : null
                };
            });

            return Ok(roster);
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveSchedule([FromBody] ScheduleSaveDto dto)
        {
            if (!TimeSpan.TryParse(dto.Start, out var startTime))
            {
                return BadRequest(new { message = "Invalid start time format." });
            }

            // Start Transaction to ensure Schedule and Employee record stay in sync
            using var transaction = await _context.Database.BeginTransactionAsync();

            try 
            {
                // 1. Calculate 9-hour shift duration
                var duration = TimeSpan.FromHours(9);
                var endTime = startTime.Add(duration);
                if (endTime.Days > 0) endTime = endTime.Subtract(TimeSpan.FromDays(1));

                // 2. Update or Create Schedule
                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.EmployeeId == dto.EmployeeId);
                
                if (schedule == null) 
                {
                    schedule = new Schedule { EmployeeId = dto.EmployeeId, IsActive = true };
                    _context.Schedules.Add(schedule);
                }

                schedule.ShiftStart = startTime;
                schedule.ShiftEnd = endTime; 
                schedule.WorkingDays = dto.WorkingDays;

                // 3. INTEGRATION: Ensure Employee Record exists for Leave Credits
                var user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == dto.EmployeeId);
                
                // Using singular _context.Employee as requested
                var employeeRecord = await _context.Employee
                    .FirstOrDefaultAsync(e => e.EmployeeId.ToString() == dto.EmployeeId);

                if (employeeRecord == null && user != null)
                {
                    // Create the leave credit profile if it doesn't exist
                    var newEmployee = new Employee
                    {
                        EmployeeId = int.Parse(dto.EmployeeId),
                        Name = user.Name,
                        LeaveBalance = 15.0 // Initial credit
                    };
                    _context.Employee.Add(newEmployee);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { 
                    message = $"Deployment Synced. Leave Profile Initialized.",
                    solvedEnd = endTime.ToString(@"hh\:mm")
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Sync Failure", error = ex.Message });
            }
        }
    }

    public class ScheduleSaveDto 
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string Start { get; set; } = string.Empty;
        public string WorkingDays { get; set; } = string.Empty;
    }
}