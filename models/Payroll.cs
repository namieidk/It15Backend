public class Payroll {
    public int Id { get; set; }
    public string EmployeeId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime PayoutDate { get; set; }
    
    // Earnings
    public decimal BasicSalary { get; set; }
    public decimal NightDiff { get; set; }
    public decimal Overtime { get; set; }
    public decimal Allowances { get; set; }
    
    // Deductions
    public decimal SSS { get; set; }
    public decimal PhilHealth { get; set; }
    public decimal PagIBIG { get; set; }
    public decimal WithholdingTax { get; set; }
        public decimal TotalDeductions { get; set; }

        public decimal GrossPay { get; set; }
        public decimal NetPay   { get; set; }

    public string Status { get; set; } = "PROCESSED";
}