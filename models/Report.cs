using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class Report
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string ReportNumber { get; set; } = string.Empty; 
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        public string Type { get; set; } = string.Empty;
        
        public string Status { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [Required]
        public string EmployeeId { get; set; } = string.Empty; 

        [Required]
        public string Department { get; set; } = string.Empty;
        
        public string DownloadUrl { get; set; } = "#";
    }
}