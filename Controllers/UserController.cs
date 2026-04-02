using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Models;

namespace YourProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public UserController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/User/profile/{employeeId}
        [HttpGet("profile/{employeeId}")]
        public async Task<IActionResult> GetProfile(string employeeId)
        {
            var empId = employeeId.Trim();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.EmployeeId == empId);

            if (user == null)
                return NotFound(new { message = $"Identity {empId} not found." });

            return Ok(new
            {
                name         = user.Name,
                employeeId   = user.EmployeeId,
                role         = user.Role,
                department   = user.Department,
                email        = user.Email        ?? "NOT CONFIGURED",
                phone        = user.Phone        ?? "NOT CONFIGURED",
                workstation  = user.Workstation  ?? "UNASSIGNED",
                profileImage = user.ProfileImage,
                bannerImage  = user.BannerImage,
                status       = user.Status       ?? "ACTIVE",

                // Government IDs
                sssId        = user.SssId        ?? string.Empty,
                philHealthId = user.PhilHealthId ?? string.Empty,
                pagIbigId    = user.PagIbigId    ?? string.Empty,
            });
        }

        // PUT: api/User/update-profile
        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UserProfileUpdateDto dto)
        {
            if (dto == null) return BadRequest("Invalid payload.");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.EmployeeId == dto.EmployeeId.Trim());

            if (user == null)
                return NotFound(new { message = "User record not found." });

            // Standard fields
            user.Email      = dto.Email;
            user.Phone      = dto.Phone;
            user.Workstation = dto.Workstation;

            // Government IDs
            user.SssId        = dto.SssId;
            user.PhilHealthId = dto.PhilHealthId;
            user.PagIbigId    = dto.PagIbigId;

            // Avatar
            if (!string.IsNullOrEmpty(dto.ProfileImage))
                user.ProfileImage = dto.ProfileImage;

            // Banner
            if (!string.IsNullOrEmpty(dto.BannerImage))
                user.BannerImage = dto.BannerImage;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new { message = "Database sync failed.", error = ex.Message });
            }

            return Ok(new
            {
                message      = "Profile updated successfully.",
                profileImage = user.ProfileImage,
                bannerImage  = user.BannerImage,
                workstation  = user.Workstation,
                sssId        = user.SssId,
                philHealthId = user.PhilHealthId,
                pagIbigId    = user.PagIbigId,
            });
        }
    }

    // ── DTO ───────────────────────────────────────────────────────────────────
    public class UserProfileUpdateDto
    {
        public string EmployeeId  { get; set; } = string.Empty;
        public string Email       { get; set; } = string.Empty;
        public string Phone       { get; set; } = string.Empty;
        public string Workstation { get; set; } = string.Empty;

        // Visual assets
        public string? ProfileImage { get; set; }
        public string? BannerImage  { get; set; }

        // Government IDs
        public string? SssId        { get; set; }
        public string? PhilHealthId { get; set; }
        public string? PagIbigId    { get; set; }
    }
}