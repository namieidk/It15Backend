using YourProject.Data;
using YourProject.Models;

namespace YourProject.Services
{
    public class ReportService
    {
        private readonly ApplicationDbContext _context;

        public ReportService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─── SHARED HELPER ────────────────────────────────────────────────────
        private async Task CreateAsync(
            string employeeId,
            string department,
            string type,
            string name,
            string status = "APPROVED")
        {
            var count = _context.Reports.Count(r => r.EmployeeId == employeeId) + 1;

            _context.Reports.Add(new Report
            {
                ReportNumber = $"{type.ToUpper()[..3]}-{employeeId}-{DateTime.UtcNow:yyyyMMdd}-{count:D3}",
                Name         = name,
                Type         = type.ToUpper(),
                Status       = status,
                CreatedAt    = DateTime.UtcNow,
                EmployeeId   = employeeId,
                Department   = department.ToUpper(),
                DownloadUrl  = "#"
            });

            await _context.SaveChangesAsync();
        }

        // ─── EVALUATION ───────────────────────────────────────────────────────
        // Called after an evaluation is submitted.
        // Creates a report for the TARGET employee being evaluated.
        public async Task CreateEvaluationReportAsync(
            string targetEmployeeId,
            string department,
            double score,
            DateTime date)
        {
            await CreateAsync(
                employeeId:  targetEmployeeId,
                department:  department,
                type:        "EVALUATION",
                name:        $"Evaluation Report — {date:MMMM yyyy} (Score: {score:F1})",
                status:      "APPROVED"
            );
        }

        // ─── PAYROLL ──────────────────────────────────────────────────────────
        // Called after a payslip is successfully generated.
        public async Task CreatePayrollReportAsync(
            string  employeeId,
            string  department,
            decimal netPay,
            DateTime periodStart,
            DateTime periodEnd)
        {
            await CreateAsync(
                employeeId:  employeeId,
                department:  department,
                type:        "PAYROLL",
                name:        $"Payslip — {periodStart:MMM dd} to {periodEnd:MMM dd, yyyy} (Net: ₱{netPay:N2})",
                status:      "APPROVED"
            );
        }

        // ─── ATTENDANCE ───────────────────────────────────────────────────────
        // Called after clock-out to log the daily attendance record.
        public async Task CreateAttendanceReportAsync(
            string   employeeId,
            string   department,
            string   attendanceStatus,
            DateTime date,
            double   hoursWorked)
        {
            await CreateAsync(
                employeeId:  employeeId,
                department:  department,
                type:        "ATTENDANCE",
                name:        $"Attendance Log — {date:MMM dd, yyyy} ({attendanceStatus}, {hoursWorked:F1}h)",
                status:      attendanceStatus.ToUpper() == "LATE" ? "PENDING" : "APPROVED"
            );
        }
    }
}