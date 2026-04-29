using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class Schedule
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string EmployeeId { get; set; } = "";
        
        public TimeSpan ShiftStart { get; set; } 
        public TimeSpan ShiftEnd { get; set; } 
        
        public string WorkingDays { get; set; } = "Mon,Tue,Wed,Thu,Fri";
        
        public bool IsActive { get; set; } = true;
    }
}