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
                    var deductions = CalculateBenefitDeductions(salary);
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

        // 5. BATCH PROCESS
        // ─────────────────────────────────────────────────────────────────────
        // BI-WEEKLY PAYROLL RULES:
        //   1st payroll (1st–15th)  → Basic salary half + OT + Night Diff
        //                             Deduct: SSS, PhilHealth, Pag-IBIG only
        //   2nd payroll (16th–EOM)  → Basic salary half + OT + Night Diff
        //                             Deduct: Withholding Tax only (if monthly gross > 20,833)
        //
        // OVERTIME RULES (DOLE):
        //   Regular OT  → hourly rate × 1.25
        //   Rest day OT → hourly rate × 1.30
        //   Holiday OT  → hourly rate × 2.60
        //   (We use 1.25 as the default multiplier stored in OvertimeHours)
        //
        // NIGHT DIFFERENTIAL RULES (DOLE):
        //   Hours worked 10:00 PM – 6:00 AM → +10% of hourly rate per night-diff hour
        //
        // WITHHOLDING TAX (BIR Train Law — monthly bracket applied to monthly gross,
        //   deducted only on the 2nd payroll of the month):
        //   ≤ ₱20,833           → 0%
        //   ₱20,834 – ₱33,332  → 15% of excess over ₱20,833
        //   ₱33,333 – ₱66,666  → ₱1,875 + 20% of excess over ₱33,333
        //   ₱66,667 – ₱166,666 → ₱8,541.80 + 25% of excess over ₱66,667
        //   ₱166,667 – ₱666,666→ ₱33,541.80 + 30% of excess over ₱166,667
        //   > ₱666,667          → ₱183,541.80 + 35% of excess over ₱666,667
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost("batch-process")]
        public async Task<IActionResult> BatchProcess([FromBody] BatchProcessRequest req)
        {
            if (req.EmployeeIds == null || !req.EmployeeIds.Any())
                return BadRequest(new { message = "NO_EMPLOYEES" });

            // Determine which half of the month this pay period covers
            // 1st payroll = period starts on the 1st; 2nd payroll = period starts on the 16th
            bool isFirstPayroll = req.PeriodStart.Day <= 15;

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
                        .Where(a => a.EmployeeId    == empId
                                 && a.ClockInTime   >= req.PeriodStart
                                 && a.ClockOutTime  <= req.PeriodEnd)
                        .ToListAsync();

                    decimal monthlySalary = profile.BasicMonthlySalary;
                    decimal hourlyRate    = profile.HourlyRate;

                    // Half-month basic (bi-weekly split)
                    decimal halfBasic = Math.Round(monthlySalary / 2, 2);

                    // ── OVERTIME ──────────────────────────────────────────────
                    // Regular OT: hourly rate × 1.25
                    double  totalOtHours = attendanceRecords.Sum(a => a.OvertimeHours);
                    decimal overtimePay  = Math.Round((decimal)totalOtHours * hourlyRate * 1.25m, 2);

                    // ── NIGHT DIFFERENTIAL ────────────────────────────────────
                    // 10 PM – 6 AM hours → +10% of hourly rate per hour
                    double  totalNightHours = attendanceRecords
                        .Sum(a => ComputeNightDiffHours(a.ClockInTime, a.ClockOutTime));
                    decimal nightDiffPay    = Math.Round((decimal)totalNightHours * hourlyRate * 0.10m, 2);

                    decimal allowances = 0m;

                    // ── GROSS PAY (this payroll period) ───────────────────────
                    decimal grossPay = halfBasic + overtimePay + nightDiffPay + allowances;

                    // ── DEDUCTIONS — split across the two payrolls ────────────
                    decimal sssDeduction        = 0m;
                    decimal philHealthDeduction  = 0m;
                    decimal pagIbigDeduction     = 0m;
                    decimal withholdingTax       = 0m;

                    if (isFirstPayroll)
                    {
                        // 1st payroll: deduct government benefits only
                        var benefits        = CalculateBenefitDeductions(monthlySalary);
                        sssDeduction        = Math.Round(benefits.Sss,        2);
                        philHealthDeduction = Math.Round(benefits.Philhealth,  2);
                        pagIbigDeduction    = Math.Round(benefits.Pagibig,    2);
                        withholdingTax      = 0m;
                    }
                    else
                    {
                        // 2nd payroll: deduct withholding tax only (based on full monthly gross)
                        // Estimate monthly gross = (halfBasic × 2) + OT + NightDiff (this period)
                        decimal estimatedMonthlyGross = (halfBasic * 2) + overtimePay + nightDiffPay;
                        withholdingTax = CalculateWithholdingTax(estimatedMonthlyGross);
                        // No benefits deduction on 2nd payroll — already taken on 1st
                    }

                    decimal totalDeductions = sssDeduction + philHealthDeduction + pagIbigDeduction + withholdingTax;
                    decimal netPay          = Math.Round(grossPay - totalDeductions, 2);

                    _context.Payslips.Add(new Payslip
                    {
                        EmployeeId          = empId,
                        PeriodStart         = req.PeriodStart,
                        PeriodEnd           = req.PeriodEnd,
                        PayDate             = req.PayDate,
                        BasicSalary         = halfBasic,
                        NightDiff           = nightDiffPay,
                        Overtime            = overtimePay,
                        Allowances          = allowances,
                        GrossPay            = Math.Round(grossPay,          2),
                        SssDeduction        = sssDeduction,
                        PhilHealthDeduction = philHealthDeduction,
                        PagIbigDeduction    = pagIbigDeduction,
                        WithholdingTax      = Math.Round(withholdingTax,    2),
                        TotalDeductions     = Math.Round(totalDeductions,   2),
                        NetPay              = netPay,
                        Status              = "PROCESSED",
                        GeneratedAt         = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

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

        /// <summary>
        /// Government benefit deductions (SSS, PhilHealth, Pag-IBIG).
        /// These are deducted only on the 1st payroll of the month.
        /// Rates are based on current PH mandated contribution tables.
        /// </summary>
        private static DeductionResult CalculateBenefitDeductions(decimal monthlySalary)
        {
            // SSS: 4.5% employee share (approximate; actual uses a bracket table)
            decimal sss = Math.Round(monthlySalary * 0.045m, 2);

            // Pag-IBIG: 1% if salary ≤ ₱1,500; 2% if above, capped at ₱100
            decimal pagibig = monthlySalary <= 1500m
                ? Math.Round(monthlySalary * 0.01m, 2)
                : Math.Min(Math.Round(monthlySalary * 0.02m, 2), 100m);

            // PhilHealth: 5% of monthly salary, split 50/50 → employee pays 2.5%
            decimal philhealth = Math.Round(monthlySalary * 0.025m, 2);

            return new DeductionResult
            {
                Sss        = sss,
                Pagibig    = pagibig,
                Philhealth = philhealth,
                Total      = sss + pagibig + philhealth
            };
        }

        /// <summary>
        /// BIR TRAIN Law withholding tax based on monthly gross income.
        /// Employees earning ₱20,833/month (₱250,000/year) or below are tax-exempt.
        /// Deducted only on the 2nd payroll of the month.
        /// </summary>
        private static decimal CalculateWithholdingTax(decimal monthlyGross)
        {
            if (monthlyGross <= 20833m)   return 0m;   // tax-exempt
            if (monthlyGross <= 33332m)   return Math.Round((monthlyGross - 20833m)   * 0.15m,                          2);
            if (monthlyGross <= 66666m)   return Math.Round(1875m   + (monthlyGross - 33333m)  * 0.20m,                 2);
            if (monthlyGross <= 166666m)  return Math.Round(8541.80m  + (monthlyGross - 66667m)  * 0.25m,               2);
            if (monthlyGross <= 666666m)  return Math.Round(33541.80m + (monthlyGross - 166667m) * 0.30m,               2);
            return                               Math.Round(183541.80m + (monthlyGross - 666667m) * 0.35m,              2);
        }

        /// <summary>
        /// Computes night differential hours worked between 10:00 PM and 6:00 AM.
        /// DOLE rules: employees working during these hours earn an additional 10%
        /// of their hourly rate for every night-diff hour rendered.
        /// Uses minute-by-minute iteration to handle overnight shifts correctly.
        /// </summary>
        private static double ComputeNightDiffHours(DateTime? clockIn, DateTime? clockOut)
        {
            if (clockIn == null || clockOut == null) return 0;

            var    nightStart  = TimeSpan.FromHours(22); // 10 PM
            var    nightEnd    = TimeSpan.FromHours(6);  // 6 AM
            double nightHours  = 0;
            var    current     = clockIn.Value;

            while (current < clockOut.Value)
            {
                var next = current.AddMinutes(1);
                if (next > clockOut.Value) next = clockOut.Value;

                var t = current.TimeOfDay;
                // Night diff applies if time is >= 10 PM OR < 6 AM
                if (t >= nightStart || t < nightEnd)
                    nightHours += (next - current).TotalHours;

                current = next;
            }

            return nightHours;
        }
    }

    // ─── REQUEST / RESULT MODELS ──────────────────────────────────────────────

    public class PayrollEnrollmentRequest
    {
        public string  EmployeeId  { get; set; } = "";
        public decimal BasicSalary { get; set; }
    }

    public class PayPeriodRequest
    {
        public string   Label       { get; set; } = "";
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd   { get; set; }
        public DateTime CutoffDate  { get; set; }
        public DateTime PayDate     { get; set; }
    }

    public class BatchProcessRequest
    {
        public DateTime      PeriodStart { get; set; }
        public DateTime      PeriodEnd   { get; set; }
        public DateTime      PayDate     { get; set; }
        public List<string>  EmployeeIds { get; set; } = new();
    }

    public class BatchResult
    {
        public string  EmployeeId { get; set; } = "";
        public string  Name       { get; set; } = "";
        public string  Status     { get; set; } = "";
        public decimal NetPay     { get; set; }
    }

    public class DeductionResult
    {
        public decimal Sss        { get; set; }
        public decimal Pagibig    { get; set; }
        public decimal Philhealth { get; set; }
        public decimal Total      { get; set; }
    }
}