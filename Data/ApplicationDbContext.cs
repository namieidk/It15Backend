using Microsoft.EntityFrameworkCore;
using YourProject.Models;

namespace YourProject.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options) { }

        // This represents the "Users" table
        public DbSet<User> Users { get; set; }
        public DbSet<LoginLog> LoginLogs { get; set; }
        public DbSet<Schedule> Schedules { get; set; }
        public DbSet<Attendance> Attendance { get; set; }

        public DbSet<Employee> Employee { get; set; }
        public DbSet<LeaveRequest> LeaveReq { get; set; }

        public DbSet<Evaluation> Evaluations { get; set; }
        public DbSet<PeerFeedback> PeerFeedbacks { get; set; }
        public DbSet<EmployeeProfile> EmployeeProfiles { get; set; }
        public DbSet<Payroll> Payroll { get; set; }
        public DbSet<PayPeriod> PayPeriods { get; set; }
        public DbSet<Payslip> Payslips { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<SystemSettings> SystemSettings { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Applicant> Applicants { get; set; }
        
    }
}