using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Models;
using YourProject.Services;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EvaluationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ReportService        _reports;

        public EvaluationController(ApplicationDbContext context, ReportService reports)
        {
            _context = context;
            _reports = reports;
        }

        // 1. GET AGENTS WITH STATUS
        [HttpGet("agents-with-status")]
        public async Task<IActionResult> GetAgents(
            [FromQuery] string department,
            [FromQuery] string excludeId,
            [FromQuery] string viewerRole,
            [FromQuery] string mode)
        {
            try
            {
                var firstDayOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                var deptUpper       = department?.Trim().ToUpper() ?? "";
                var roleUpper       = viewerRole?.Trim().ToUpper() ?? "";

                var query = _context.Users.Where(u => u.Role.ToUpper() != "ADMIN");

                // Always exclude the viewer themselves
                if (!string.IsNullOrWhiteSpace(excludeId))
                    query = query.Where(u => u.EmployeeId != excludeId.Trim());

                // Always scope to the same department
                if (!string.IsNullOrEmpty(deptUpper))
                    query = query.Where(u => u.Department.ToUpper() == deptUpper);

                // Filter by mode — determines WHO is eligible to be evaluated
                switch (mode?.ToLower())
                {
                    case "peer":
                        // Viewer evaluates people with the same role
                        query = query.Where(u => u.Role.ToUpper() == roleUpper);
                        break;

                    case "managerial":
                        // Employees evaluate upward — only managers are targets
                        query = query.Where(u => u.Role.ToUpper() == "MANAGER");
                        break;

                    case "hr":
                        // HR compliance — only HR-role users in the same department
                        query = query.Where(u => u.Role.ToUpper() == "HR");
                        break;

                    default:
                        return Ok(new List<object>());
                }

                var agents = await query
                    .Select(u => new {
                        id         = u.EmployeeId,
                        name       = u.Name.ToUpper(),
                        role       = u.Role.ToUpper(),
                        department = u.Department.ToUpper(),
                        alreadyEvaluated = _context.Evaluations.Any(e =>
                            e.EvaluatorId.ToString()      == excludeId &&
                            e.TargetEmployeeId.ToString() == u.EmployeeId &&
                            e.DateSubmitted >= firstDayOfMonth),
                        peerScoreValue = _context.Evaluations
                            .Where(e => e.TargetEmployeeId.ToString() == u.EmployeeId)
                            .Select(e => (double?)e.Score)
                            .Average() ?? 0.0
                    })
                    .ToListAsync();

                var result = agents.Select(a => new {
                    a.id,
                    a.name,
                    a.role,
                    a.department,
                    a.alreadyEvaluated,
                    peerScore = a.peerScoreValue.ToString("F1")
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching agents", error = ex.Message });
            }
        }

        // 2. GET RESULTS
        [HttpGet("peer-results/{employeeId}")]
        public async Task<IActionResult> GetPeerResults(string employeeId)
        {
            try
            {
                var feedbacks = await _context.Evaluations
                    .Where(e => e.TargetEmployeeId.ToString() == employeeId)
                    .OrderByDescending(e => e.DateSubmitted)
                    .Select(e => new {
                        id          = e.Id,
                        peerDisplay = "Anonymous",
                        score       = e.Score.ToString("F1"),
                        comment     = e.Comments,
                        date        = e.DateSubmitted.ToString("MMM dd, yyyy")
                    })
                    .ToListAsync();

                return Ok(feedbacks);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving results", error = ex.Message });
            }
        }

        // 3. SUBMIT EVALUATION
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitEvaluation([FromBody] EvaluationSubmitModel model)
        {
            try
            {
                var firstDayOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

                bool exists = await _context.Evaluations.AnyAsync(e =>
                    e.EvaluatorId.ToString()      == model.EvaluatorId &&
                    e.TargetEmployeeId.ToString() == model.TargetEmployeeId &&
                    e.DateSubmitted >= firstDayOfMonth);

                if (exists)
                    return BadRequest(new { message = "Monthly limit reached for this target." });

                var evaluation = new Evaluation
                {
                    TargetEmployeeId = int.Parse(model.TargetEmployeeId),
                    EvaluatorId      = int.Parse(model.EvaluatorId),
                    Score            = model.Score,
                    Comments         = model.Comments ?? "",
                    DateSubmitted    = DateTime.Now
                };

                _context.Evaluations.Add(evaluation);
                await _context.SaveChangesAsync();

                // Auto-generate Report for the target employee
                var targetUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.EmployeeId == model.TargetEmployeeId);

                if (targetUser != null)
                {
                    await _reports.CreateEvaluationReportAsync(
                        targetEmployeeId: targetUser.EmployeeId,
                        department:       targetUser.Department,
                        score:            model.Score,
                        date:             DateTime.Now
                    );
                }

                return Ok(new { message = "Audit committed." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class EvaluationSubmitModel
    {
        public string  TargetEmployeeId { get; set; } = string.Empty;
        public string  EvaluatorId      { get; set; } = string.Empty;
        public double  Score            { get; set; }
        public string? Comments         { get; set; }
    }
}