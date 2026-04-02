// ─── Models/PayPeriod.cs ──────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class PayPeriod
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string   Label       { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd   { get; set; }
        public DateTime CutoffDate  { get; set; }
        public DateTime PayDate     { get; set; }
        public string   Status      { get; set; } = "SCHEDULED";  // SCHEDULED | PROCESSED
        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    }
}