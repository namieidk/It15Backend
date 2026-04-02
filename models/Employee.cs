using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
public class Employee
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double LeaveBalance { get; set; } = 15.0; 
}

}