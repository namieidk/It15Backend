// ─── Models/Payslip.cs ────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class Payslip
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string EmployeeId { get; set; } = string.Empty;

        // ── Period ──────────────────────────────────────────────────────────
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd   { get; set; }
        public DateTime PayDate     { get; set; }

        // ── Earnings ────────────────────────────────────────────────────────
        public decimal BasicSalary  { get; set; }
        public decimal NightDiff    { get; set; } = 0;
        public decimal Overtime     { get; set; } = 0;
        public decimal Allowances   { get; set; } = 0;

        // ── Deductions ──────────────────────────────────────────────────────
        public decimal SssDeduction         { get; set; }
        public decimal PhilHealthDeduction  { get; set; }
        public decimal PagIbigDeduction     { get; set; }
        public decimal WithholdingTax       { get; set; } = 0;
        public decimal TotalDeductions      { get; set; }

        // ── Net ─────────────────────────────────────────────────────────────
        public decimal GrossPay  { get; set; }   // BasicSalary + NightDiff + OT + Allowances
        public decimal NetPay    { get; set; }   // GrossPay - TotalDeductions

        // ── Meta ────────────────────────────────────────────────────────────
        public string   Status      { get; set; } = "PROCESSED";
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public DateTime? NotifiedAt { get; set; }  // Set by background email job on PayDate
    }
}