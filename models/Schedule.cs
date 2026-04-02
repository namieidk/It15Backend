using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class Schedule
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string EmployeeId { get; set; } = "";
        
        // The time they are expected to hit the button
        public TimeSpan ShiftStart { get; set; } // e.g., 08:00:00
        public TimeSpan ShiftEnd { get; set; }   // e.g., 17:00:00
        
        // Which days they work (e.g., "Mon,Tue,Wed,Thu,Fri")
        public string WorkingDays { get; set; } = "Mon,Tue,Wed,Thu,Fri";
        
        public bool IsActive { get; set; } = true;
    }
}