using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class LoginLog
{
    public int Id { get; set; }
    public string? EmployeeId { get; set; } 
    public string? IpAddress { get; set; }  
    public string? Status { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
}