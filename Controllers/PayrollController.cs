using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Models;
using YourProject.Services;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PayrollController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ReportService        _reports;

        public PayrollController(ApplicationDbContext context, ReportService reports)
        {
            _context = context;
            _reports = reports;
        }

        // 1. GET ROSTER
        [HttpGet("roster")]
        public async Task<IActionResult> GetPayrollRoster()
        {
            try
            {
                var users = await _context.Users
                    .Where(u => u.Role != "ADMIN" && u.Role != "HR")
                    .Select(u => new
                    {
                        id        = u.EmployeeId,
                        name      = u.Name,
                        role      = u.Role,
                        dept      = u.Department,
                        sssId     = u.SssId        ?? "UNSET",
                        philId    = u.PhilHealthId ?? "UNSET",
                        pagibigId = u.PagIbigId    ?? "UNSET",
                        HasProfile = _context.EmployeeProfiles.Any(p => p.EmployeeId == u.EmployeeId)
                    })
                    .ToListAsync();

                var result = users.Select(u =>
                {
                    var profile    = _context.EmployeeProfiles.FirstOrDefault(p => p.EmployeeId == u.id);
                    var salary     = profile?.BasicMonthlySalary ?? 0;
                    var deductions = CalculateDeductions(salary);
                    var grossPay   = salary;
                    var netPay     = Math.Round(grossPay - deductions.Total, 2);

                    return new
                    {
                        u.id, u.name, u.role, u.dept,
                        u.sssId, u.philId, u.pagibigId,
                        status              = u.HasProfile ? "PROCESSED" : "PENDING",
                        basicSalary         = Math.Round(salary,           2),
                        estimatedDeductions = Math.Round(deductions.Total, 2),
                        grossPay            = Math.Round(grossPay,         2),
                        netTakeHome         = netPay
                    };
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "LEDGER_SYNC_FAILURE", details = ex.Message });
            }
        }

        // 2. ENROLL
        [HttpPost("enroll")]
        public async Task<IActionResult> EnrollPayroll([FromBody] PayrollEnrollmentRequest req)
        {
            if (string.IsNullOrEmpty(req.EmployeeId) || req.BasicSalary <= 0)
                return BadRequest(new { message = "INVALID DATA" });

            var profile = await _context.EmployeeProfiles
                .FirstOrDefaultAsync(x => x.EmployeeId == req.EmployeeId);

            if (profile != null)
            {
                profile.BasicMonthlySalary = req.BasicSalary;
                profile.HourlyRate         = req.BasicSalary / 160;
                _context.EmployeeProfiles.Update(profile);
            }
            else
            {
                _context.EmployeeProfiles.Add(new EmployeeProfile
                {
                    EmployeeId         = req.EmployeeId,
                    BasicMonthlySalary = req.BasicSalary,
                    HourlyRate         = req.BasicSalary / 160
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "LEDGER SECURED" });
        }

        // 3. CREATE PAY PERIOD
        [HttpPost("pay-period")]
        public async Task<IActionResult> CreatePayPeriod([FromBody] PayPeriodRequest req)
        {
            if (string.IsNullOrEmpty(req.Label)  ||
                req.PeriodStart == default        ||
                req.PeriodEnd   == default        ||
                req.CutoffDate  == default        ||
                req.PayDate     == default)
                return BadRequest(new { message = "ALL FIELDS REQUIRED" });

            try
            {
                _context.PayPeriods.Add(new PayPeriod
                {
                    Label       = req.Label,
                    PeriodStart = req.PeriodStart,
                    PeriodEnd   = req.PeriodEnd,
                    CutoffDate  = req.CutoffDate,
                    PayDate     = req.PayDate,
                    Status      = "SCHEDULED",
                    CreatedAt   = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                return Ok(new { message = "PAY_PERIOD_SCHEDULED" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "SCHEDULE_FAILURE", details = ex.Message });
            }
        }

        // 4. GET PAY PERIODS
        [HttpGet("pay-periods")]
        public async Task<IActionResult> GetPayPeriods()
        {
            try
            {
                var periods = await _context.PayPeriods
                    .OrderByDescending(p => p.PayDate)
                    .Select(p => new
                    {
                        p.Id, p.Label,
                        periodStart = p.PeriodStart.ToString("yyyy-MM-dd"),
                        periodEnd   = p.PeriodEnd.ToString("yyyy-MM-dd"),
                        cutoffDate  = p.CutoffDate.ToString("yyyy-MM-dd"),
                        payDate     = p.PayDate.ToString("yyyy-MM-dd"),
                        p.Status
                    })
                    .ToListAsync();

                return Ok(periods);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "FETCH_FAILURE", details = ex.Message });
            }
        }

        // 5. BATCH PROCESS ── auto-creates a Report per employee after payslip
        [HttpPost("batch-process")]
        public async Task<IActionResult> BatchProcess([FromBody] BatchProcessRequest req)
        {
            if (req.EmployeeIds == null || !req.EmployeeIds.Any())
                return BadRequest(new { message = "NO_EMPLOYEES" });

            var results = new List<BatchResult>();

            foreach (var empId in req.EmployeeIds)
            {
                try
                {
                    var profile = await _context.EmployeeProfiles
                        .FirstOrDefaultAsync(p => p.EmployeeId == empId);

                    var user = await _context.Users
                        .FirstOrDefaultAsync(u => u.EmployeeId == empId);

                    if (profile == null || user == null)
                    {
                        results.Add(new BatchResult
                        {
                            EmployeeId = empId,
                            Name       = user?.Name ?? "UNKNOWN",
                            Status     = "FAILED",
                            NetPay     = 0
                        });
                        continue;
                    }

                    var attendanceRecords = await _context.Attendance
                        .Where(a => a.EmployeeId == empId
                                 && a.ClockInTime  >= req.PeriodStart
                                 && a.ClockOutTime <= req.PeriodEnd)
                        .ToListAsync();

                    var salary     = profile.BasicMonthlySalary;
                    var hourlyRate = profile.HourlyRate;

                    double totalOtHours    = attendanceRecords.Sum(a => a.OvertimeHours);
                    double totalNightHours = attendanceRecords
                        .Sum(a => ComputeNightDiffHours(a.ClockInTime, a.ClockOutTime));

                    decimal overtimePay  = Math.Round((decimal)totalOtHours    * hourlyRate * 1.25m, 2);
                    decimal nightDiffPay = Math.Round((decimal)totalNightHours * hourlyRate * 0.10m, 2);
                    decimal allowances   = 0m;

                    var     deductions      = CalculateDeductions(salary);
                    decimal grossPay        = salary + overtimePay + nightDiffPay + allowances;
                    decimal withholdingTax  = CalculateWithholdingTax(grossPay);
                    decimal totalDeductions = Math.Round(deductions.Total + withholdingTax, 2);
                    decimal netPay          = Math.Round(grossPay - totalDeductions, 2);

                    _context.Payslips.Add(new Payslip
                    {
                        EmployeeId          = empId,
                        PeriodStart         = req.PeriodStart,
                        PeriodEnd           = req.PeriodEnd,
                        PayDate             = req.PayDate,
                        BasicSalary         = Math.Round(salary,               2),
                        NightDiff           = nightDiffPay,
                        Overtime            = overtimePay,
                        Allowances          = allowances,
                        GrossPay            = Math.Round(grossPay,             2),
                        SssDeduction        = Math.Round(deductions.Sss,       2),
                        PhilHealthDeduction = Math.Round(deductions.Philhealth, 2),
                        PagIbigDeduction    = Math.Round(deductions.Pagibig,   2),
                        WithholdingTax      = Math.Round(withholdingTax,       2),
                        TotalDeductions     = totalDeductions,
                        NetPay              = netPay,
                        Status              = "PROCESSED",
                        GeneratedAt         = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    // ── Auto-generate Payroll Report for this employee ────────
                    await _reports.CreatePayrollReportAsync(
                        employeeId:  empId,
                        department:  user.Department,
                        netPay:      netPay,
                        periodStart: req.PeriodStart,
                        periodEnd:   req.PeriodEnd
                    );

                    results.Add(new BatchResult
                    {
                        EmployeeId = empId,
                        Name       = user.Name,
                        Status     = "SUCCESS",
                        NetPay     = netPay
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new BatchResult
                    {
                        EmployeeId = empId,
                        Name       = "ERROR",
                        Status     = "FAILED - " + ex.Message,
                        NetPay     = 0
                    });
                }
            }

            return Ok(results);
        }

        // 6. GET PAYSLIPS
        [HttpGet("payslips")]
        public async Task<IActionResult> GetPayslips([FromQuery] string? employeeId)
        {
            try
            {
                var query = _context.Payslips.AsQueryable();
                if (!string.IsNullOrEmpty(employeeId))
                    query = query.Where(p => p.EmployeeId == employeeId);

                var payslips = await query
                    .OrderByDescending(p => p.PayDate)
                    .Select(p => new
                    {
                        p.Id, p.EmployeeId,
                        periodStart         = p.PeriodStart.ToString("yyyy-MM-dd"),
                        periodEnd           = p.PeriodEnd.ToString("yyyy-MM-dd"),
                        payDate             = p.PayDate.ToString("yyyy-MM-dd"),
                        p.BasicSalary, p.NightDiff, p.Overtime, p.Allowances,
                        p.GrossPay, p.SssDeduction, p.PhilHealthDeduction,
                        p.PagIbigDeduction, p.WithholdingTax, p.TotalDeductions,
                        p.NetPay, p.Status,
                        generatedAt = p.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        notifiedAt  = p.NotifiedAt.HasValue
                                        ? p.NotifiedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                                        : null as string
                    })
                    .ToListAsync();

                return Ok(payslips);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "FETCH_FAILURE", details = ex.Message });
            }
        }

        // 7. GET SINGLE PAYSLIP
        [HttpGet("payslips/{id}")]
        public async Task<IActionResult> GetPayslip(int id)
        {
            try
            {
                var p = await _context.Payslips.FindAsync(id);
                if (p == null) return NotFound(new { message = "PAYSLIP_NOT_FOUND" });

                var user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == p.EmployeeId);

                return Ok(new
                {
                    p.Id, p.EmployeeId,
                    employeeName = user?.Name       ?? "UNKNOWN",
                    department   = user?.Department ?? "UNKNOWN",
                    role         = user?.Role       ?? "UNKNOWN",
                    periodStart  = p.PeriodStart.ToString("yyyy-MM-dd"),
                    periodEnd    = p.PeriodEnd.ToString("yyyy-MM-dd"),
                    payDate      = p.PayDate.ToString("yyyy-MM-dd"),
                    p.BasicSalary, p.NightDiff, p.Overtime, p.Allowances,
                    p.GrossPay, p.SssDeduction, p.PhilHealthDeduction,
                    p.PagIbigDeduction, p.WithholdingTax, p.TotalDeductions,
                    p.NetPay, p.Status,
                    generatedAt = p.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    notifiedAt  = p.NotifiedAt.HasValue
                                    ? p.NotifiedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                                    : null as string
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "FETCH_FAILURE", details = ex.Message });
            }
        }

        // ─── HELPERS ──────────────────────────────────────────────────────────

        private static DeductionResult CalculateDeductions(decimal salary)
        {
            decimal sss        = Math.Round(salary * 0.045m, 2);
            decimal pagibig    = salary <= 1500m
                                    ? Math.Round(salary * 0.01m, 2)
                                    : Math.Min(Math.Round(salary * 0.02m, 2), 100m);
            decimal philhealth = Math.Round(salary * 0.025m, 2);
            return new DeductionResult { Sss = sss, Pagibig = pagibig, Philhealth = philhealth, Total = sss + pagibig + philhealth };
        }

        private static decimal CalculateWithholdingTax(decimal monthlyGross)
        {
            if (monthlyGross <= 20833m) return 0m;
            if (monthlyGross <= 33332m) return Math.Round((monthlyGross - 20833m)  * 0.15m, 2);
            if (monthlyGross <= 66666m) return Math.Round(1875m + (monthlyGross - 33333m)  * 0.20m, 2);
            if (monthlyGross <= 166666m) return Math.Round(8541.80m + (monthlyGross - 66667m)  * 0.25m, 2);
            if (monthlyGross <= 666666m) return Math.Round(33541.80m + (monthlyGross - 166667m) * 0.30m, 2);
            return Math.Round(183541.80m + (monthlyGross - 666667m) * 0.35m, 2);
        }

        private static double ComputeNightDiffHours(DateTime? clockIn, DateTime? clockOut)
        {
            if (clockIn == null || clockOut == null) return 0;
            var nightStart = TimeSpan.FromHours(22);
            var nightEnd   = TimeSpan.FromHours(6);
            double nightHours = 0;
            var current = clockIn.Value;
            while (current < clockOut.Value)
            {
                var next = current.AddMinutes(1);
                if (next > clockOut.Value) next = clockOut.Value;
                var t = current.TimeOfDay;
                if (t >= nightStart || t < nightEnd) nightHours += (next - current).TotalHours;
                current = next;
            }
            return nightHours;
        }
    }

    public class PayrollEnrollmentRequest { public string EmployeeId { get; set; } = ""; public decimal BasicSalary { get; set; } }
    public class PayPeriodRequest { public string Label { get; set; } = ""; public DateTime PeriodStart { get; set; } public DateTime PeriodEnd { get; set; } public DateTime CutoffDate { get; set; } public DateTime PayDate { get; set; } }
    public class BatchProcessRequest { public DateTime PeriodStart { get; set; } public DateTime PeriodEnd { get; set; } public DateTime PayDate { get; set; } public List<string> EmployeeIds { get; set; } = new(); }
    public class BatchResult { public string EmployeeId { get; set; } = ""; public string Name { get; set; } = ""; public string Status { get; set; } = ""; public decimal NetPay { get; set; } }
    public class DeductionResult { public decimal Sss { get; set; } public decimal Pagibig { get; set; } public decimal Philhealth { get; set; } public decimal Total { get; set; } }
}