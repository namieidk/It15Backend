using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }

        [Required, StringLength(50)] 
        public string EmployeeId { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        public string Status { get; set; } = "ACTIVE";

        [Required]
        public string Role { get; set; } = "ADMIN"; 

        [Required, StringLength(100)]
        public string Department { get; set; } = "ADMINISTRATION";
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        
        // Defaulting to your office location
        public string Workstation { get; set; } = "FLOOR 1 // MAIN OFFICE";
        public string? ProfileImage { get; set; }
        public string? BannerImage { get; set; }
        public string? SssId { get; set; }
        public string? PhilHealthId { get; set; }
        public string? PagIbigId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Lockout fields ────────────────────────────────────────────────────
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutUntil { get; set; } = null;
    }
}