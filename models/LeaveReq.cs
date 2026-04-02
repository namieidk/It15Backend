using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
public class LeaveRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; 
    public DateTime DateSubmitted { get; set; } = DateTime.Now;
}

}