using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class Attendance
    {
        [Key]
        public int Id { get; set; }
        public string EmployeeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string Department { get; set; } = "";
        
        // Time Tracking
        public DateTime? ClockInTime { get; set; }
        public DateTime? ClockOutTime { get; set; }
        
        // Hour Breakdowns (For Payroll)
        public double RegularHours { get; set; } = 0;   // Max 8 hours
        public double OvertimeHours { get; set; } = 0;  // Anything after 8 hours
        public double TotalHoursWorked { get; set; } = 0; // Actual work (Total - 1hr break)
        
        public string Status { get; set; } = "PRESENT"; 
    }
}