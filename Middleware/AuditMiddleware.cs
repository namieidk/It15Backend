using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YourProject.Data;
using YourProject.Models;

namespace YourProject.Middleware
{
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;

        public AuditMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            await _next(context);

            var method = context.Request.Method.ToUpper();
            var path   = context.Request.Path.Value?.ToLower() ?? string.Empty;
            var status = context.Response.StatusCode;

            // Only log 2xx responses
            if (status < 200 || status >= 300) return;

            // Resolve module + action from route
            var (resolvedModule, resolvedAction, resolvedTarget) = ResolveRoute(method, path);
            if (resolvedModule == null) return;

            // ── Extract actor from X-Employee-Id header + X-Employee-* headers ──
            // Your app does NOT use JWT — it passes employeeId via X-Employee-Id header
            // and stores user info in the session cookie on the frontend.
            // We read X-Employee-Id (used by ReportsController etc.) plus
            // X-Employee-Name and X-Employee-Role if your frontend sends them.
            var (actorId, actorName, actorRole, actorDept) = ExtractActor(context);

            // If no employee ID header at all, skip logging
            if (string.IsNullOrEmpty(actorName) && actorId == 0) return;

            try
            {
                var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();

                // If we only have the string EmployeeId but not the int,
                // look up the user to get their name/role/dept
                if (actorId == 0 || string.IsNullOrEmpty(actorName))
                {
                    var rawId = context.Request.Headers["X-Employee-Id"].FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(rawId))
                    {
                        var user = await db.Users
                            .FirstOrDefaultAsync(u => u.EmployeeId == rawId);

                        if (user != null)
                        {
                            // Try to parse numeric portion (e.g. "EMP001" → 1, or "5" → 5)
                            actorId   = TryParseId(user.EmployeeId);
                            actorName = user.Name       ?? rawId;
                            actorRole = user.Role       ?? "UNKNOWN";
                            actorDept = user.Department ?? string.Empty;
                        }
                        else
                        {
                            // Still log with whatever we have
                            actorId   = TryParseId(rawId);
                            actorName = rawId;
                        }
                    }
                }

                db.AuditLogs.Add(new AuditLog
                {
                    ActorEmployeeId = actorId,
                    ActorName       = actorName.ToUpper(),
                    ActorRole       = actorRole.ToUpper(),
                    ActorDepartment = actorDept.ToUpper(),
                    Module          = resolvedModule,
                    Action          = resolvedAction,
                    Target          = resolvedTarget,
                    HttpMethod      = method,
                    Endpoint        = context.Request.Path.Value,
                    IpAddress       = context.Connection.RemoteIpAddress?.ToString(),
                    Timestamp       = DateTime.UtcNow,
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuditMiddleware] Log write failed: {ex.Message}");
            }
        }

        // ── Route → (Module, Action, Target) ──────────────────────────────────
        private static (string? resolvedModule, string resolvedAction, string? resolvedTarget)
            ResolveRoute(string method, string path)
        {
            // ── ACCOUNTS — UserController (/api/user/...) ─────────────────────
            if (path == "/api/user/update-profile" && method == "PUT")
                return ("ACCOUNTS", "Updated Profile", null);

            if (path.StartsWith("/api/user/profile/") && method == "GET")
                return ("ACCOUNTS", "Viewed Profile", ExtractLastSegment(path));

            // ── REPORTS — ReportsController (/api/reports/...) ────────────────
            if (path.StartsWith("/api/reports/download/") && method == "GET")
                return ("REPORTS", "Downloaded Report", ExtractLastSegment(path));

            if (path == "/api/reports/my-reports" && method == "GET")
                return ("REPORTS", "Viewed My Reports", null);

            if (path == "/api/reports/summary" && method == "GET")
                return ("REPORTS", "Viewed Reports Summary", null);

            if (path == "/api/reports/admin/all-reports" && method == "GET")
                return ("REPORTS", "Viewed All Reports (Admin)", null);

            if (path == "/api/reports/admin/summary" && method == "GET")
                return ("REPORTS", "Viewed Org Reports Summary", null);

            if (path == "/api/reports/admin/stats" && method == "GET")
                return ("REPORTS", "Viewed Admin Stats", null);

            if (path == "/api/reports/user-profile" && method == "GET")
                return ("REPORTS", "Viewed User Profile (Reports)", null);

            // ── ATTENDANCE — AttendanceController (/api/attendance/...) ───────
            if (path == "/api/attendance/clockin" && method == "POST")
                return ("ATTENDANCE", "Clocked In", null);

            if (path == "/api/attendance/clockout" && method == "POST")
                return ("ATTENDANCE", "Clocked Out", null);

            if (path == "/api/attendance/all" && method == "GET")
                return ("ATTENDANCE", "Viewed All Attendance", null);

            if (path.StartsWith("/api/attendance/department/") && method == "GET")
                return ("ATTENDANCE", "Viewed Department Attendance", ExtractLastSegment(path));

            if (path.StartsWith("/api/attendance/my-logs/") && method == "GET")
                return ("ATTENDANCE", "Viewed Own Attendance Logs", ExtractLastSegment(path));

            // ── ATTENDANCE — ScheduleController (/api/schedule/...) ───────────
            if (path == "/api/schedule/save" && method == "POST")
                return ("ATTENDANCE", "Saved Employee Schedule", null);

            if (path == "/api/schedule/roster" && method == "GET")
                return ("ATTENDANCE", "Viewed Schedule Roster", null);

            // ── LEAVE — LeaveController (/api/leave/...) ──────────────────────
            if (path == "/api/leave/apply" && method == "POST")
                return ("LEAVE", "Applied for Leave", null);

            if (path == "/api/leave/manager-action" && method == "POST")
                return ("LEAVE", "Manager Actioned Leave Request", null);

            if (path == "/api/leave/hr-action" && method == "POST")
                return ("LEAVE", "HR Actioned Leave Request", null);

            if (path == "/api/leave/pending" && method == "GET")
                return ("LEAVE", "Viewed Pending Leave Queue", null);

            if (path == "/api/leave/hr-pending" && method == "GET")
                return ("LEAVE", "Viewed HR Leave Queue", null);

            if (path.StartsWith("/api/leave/credits/") && method == "GET")
                return ("LEAVE", "Viewed Leave Credits", ExtractLastSegment(path));

            if (path.StartsWith("/api/leave/history/") && method == "GET")
                return ("LEAVE", "Viewed Leave History", ExtractLastSegment(path));

            // ── PAYROLL — PayrollController (/api/payroll/...) ────────────────
            if (path == "/api/payroll/enroll" && method == "POST")
                return ("PAYROLL", "Enrolled Employee in Payroll", null);

            if (path == "/api/payroll/pay-period" && method == "POST")
                return ("PAYROLL", "Created Pay Period", null);

            if (path == "/api/payroll/batch-process" && method == "POST")
                return ("PAYROLL", "Batch Processed Payroll", null);

            if (path == "/api/payroll/roster" && method == "GET")
                return ("PAYROLL", "Viewed Payroll Roster", null);

            if (path == "/api/payroll/pay-periods" && method == "GET")
                return ("PAYROLL", "Viewed Pay Periods", null);

            if (path == "/api/payroll/payslips" && method == "GET")
                return ("PAYROLL", "Viewed Payslips", null);

            if (path.StartsWith("/api/payroll/payslips/") && method == "GET")
                return ("PAYROLL", "Viewed Payslip", ExtractLastSegment(path));

            // ── EVALUATIONS — EvaluationController (/api/evaluation/...) ──────
            if (path == "/api/evaluation/submit" && method == "POST")
                return ("EVALUATIONS", "Submitted Evaluation", null);

            if (path.StartsWith("/api/evaluation/agents-with-status") && method == "GET")
                return ("EVALUATIONS", "Viewed Agents for Evaluation", null);

            if (path.StartsWith("/api/evaluation/peer-results/") && method == "GET")
                return ("EVALUATIONS", "Viewed Peer Evaluation Results", ExtractLastSegment(path));

            // ── ACCOUNTS — AuthController (/api/auth/...) ─────────────────────
            if (path == "/api/auth/login" && method == "POST")
                return ("ACCOUNTS", "User Logged In", null);

            if (path == "/api/auth/logout" && method == "POST")
                return ("ACCOUNTS", "User Logged Out", null);

            return (null, string.Empty, null);
        }

        // ── Pull the last path segment ─────────────────────────────────────────
        private static string? ExtractLastSegment(string path)
        {
            var last = path.TrimEnd('/').Split('/').LastOrDefault();
            return string.IsNullOrEmpty(last) ? null : last.ToUpper();
        }

        // ── Try to get a numeric ID from a string EmployeeId ──────────────────
        // "EMP001" → strips letters → 1
        // "5"      → 5
        // "ABC"    → 0
        private static int TryParseId(string? rawId)
        {
            if (string.IsNullOrEmpty(rawId)) return 0;
            // Try direct int parse first
            if (int.TryParse(rawId, out int direct)) return direct;
            // Strip non-digits and try again
            var digits = System.Text.RegularExpressions.Regex.Replace(rawId, @"[^\d]", "");
            return int.TryParse(digits, out int stripped) ? stripped : 0;
        }

        // ── Extract actor from request headers ────────────────────────────────
        // Your app uses X-Employee-Id header (set by frontend from session cookie).
        // No JWT is used.
        private static (int actorId, string actorName, string actorRole, string actorDept)
            ExtractActor(HttpContext context)
        {
            var rawId = context.Request.Headers["X-Employee-Id"].FirstOrDefault()?.Trim();

            if (string.IsNullOrEmpty(rawId))
                return (0, string.Empty, string.Empty, string.Empty);

            // These optional headers can be sent by the frontend for richer logs
            // If not present, the middleware will DB-lookup the user instead
            var name = context.Request.Headers["X-Employee-Name"].FirstOrDefault()?.Trim() ?? string.Empty;
            var role = context.Request.Headers["X-Employee-Role"].FirstOrDefault()?.Trim() ?? string.Empty;
            var dept = context.Request.Headers["X-Employee-Dept"].FirstOrDefault()?.Trim() ?? string.Empty;

            return (TryParseId(rawId), name, role, dept);
        }
    }

    // ── Extension method so Program.cs can call app.UseAuditLogging() ─────────
    public static class AuditMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder app)
            => app.UseMiddleware<AuditMiddleware>();
    }
}