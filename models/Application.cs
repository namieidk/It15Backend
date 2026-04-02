using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class Applicant
    {
        [Key]
        public int Id { get; set; }
        
        // Job Context
        public string JobId { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;

        // Personal Data
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Sex { get; set; } = string.Empty;

        // Contact
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;

        // Application Specifics
        public string ResumePath { get; set; } = string.Empty;
        public string CoverLetter { get; set; } = string.Empty;
        public string ReferenceCode { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Default status
        public DateTime AppliedAt { get; set; } = DateTime.Now;
    }
}