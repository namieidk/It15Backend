using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourProject.Models
{
    [Table("AuditLogs")]
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        public int ActorEmployeeId { get; set; }

        [MaxLength(150)]
        public string ActorName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string ActorRole { get; set; } = string.Empty;

        // Matches the extra column visible in your DB screenshot
        [MaxLength(100)]
        public string ActorDepartment { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Module { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Target { get; set; }

        // Matches the extra column visible in your DB screenshot
        [MaxLength(10)]
        public string? HttpMethod { get; set; }

        // Matches the extra column visible in your DB screenshot
        [MaxLength(300)]
        public string? Endpoint { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}