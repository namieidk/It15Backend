using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Models;
using YourProject.Data;
using System.ComponentModel.DataAnnotations;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApplicantsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ApplicantsController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // 1. GET: api/applicants OR api/applicants?status=APPROVED
        // Used by HR (to see PENDING) and Admin (to see APPROVED)
        [HttpGet]
        public async Task<IActionResult> GetApplicants([FromQuery] string? status)
        {
            try
            {
                var query = _context.Applicants.AsQueryable();

                // If a status is provided in the URL, filter the results
                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(a => a.Status == status.ToUpper());
                }

                var list = await query
                    .OrderByDescending(a => a.Id)
                    .ToListAsync();

                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Read Error", message = ex.Message });
            }
        }

        // 2. POST: api/applicants
        // Used by the public Application Form
        [HttpPost]
        public async Task<IActionResult> PostApplicant([FromForm] ApplicantUploadDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                // Resolve Path for Resume
                string webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                string fileName = "no-resume.pdf";

                if (dto.Resume != null && dto.Resume.Length > 0)
                {
                    string folder = Path.Combine(webRootPath, "uploads", "resumes");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                    fileName = $"{Guid.NewGuid()}_{Path.GetFileName(dto.Resume.FileName)}";
                    string fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await dto.Resume.CopyToAsync(stream);
                    }
                }

                // Generate unique reference
                string refCode = $"AX-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";

                var applicant = new Applicant
                {
                    JobId = dto.JobId,
                    JobTitle = dto.JobTitle,
                    Department = dto.Department,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    Age = dto.Age,
                    Sex = dto.Sex,
                    Email = dto.Email,
                    Phone = dto.Phone,
                    Address = dto.Address,
                    ZipCode = dto.ZipCode,
                    CoverLetter = dto.CoverLetter ?? "",
                    ResumePath = fileName,
                    ReferenceCode = refCode,
                    Status = "PENDING" // Default status for new applications
                };

                _context.Applicants.Add(applicant);
                await _context.SaveChangesAsync();

                return Ok(new { referenceCode = refCode });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Submission Error", message = ex.Message });
            }
        }

        // 3. PUT: api/applicants/{id}/status
        // Used by HR to APPROVE/REJECT and Admin to mark ACCOUNT_CREATED
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            var applicant = await _context.Applicants.FindAsync(id);
            if (applicant == null)
            {
                return NotFound(new { message = "Applicant not found" });
            }

            // Update the status (e.g., PENDING -> APPROVED -> ACCOUNT_CREATED)
            applicant.Status = dto.Status.ToUpper();

            try
            {
                await _context.SaveChangesAsync();
                return Ok(new { message = $"Status updated to {applicant.Status}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Update Error", message = ex.Message });
            }
        }
    }

    // --- Data Transfer Objects (DTOs) ---

    public class ApplicantUploadDto
    {
        [Required] public string JobId { get; set; } = "";
        [Required] public string JobTitle { get; set; } = "";
        [Required] public string Department { get; set; } = "";
        [Required] public string FirstName { get; set; } = "";
        [Required] public string LastName { get; set; } = "";
        [Required] public int Age { get; set; } 
        [Required] public string Sex { get; set; } = "";
        [Required] public string Email { get; set; } = "";
        [Required] public string Phone { get; set; } = "";
        [Required] public string Address { get; set; } = "";
        [Required] public string ZipCode { get; set; } = "";
        public string? CoverLetter { get; set; }
        public IFormFile? Resume { get; set; }
    }

    public class StatusUpdateDto
    {
        [Required] public string Status { get; set; } = "";
    }
}