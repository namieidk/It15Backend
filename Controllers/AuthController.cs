using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using YourProject.Data;
using YourProject.Models;
using BCrypt.Net;

namespace YourProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly HttpClient _http;

        private const int MAX_FAILED_ATTEMPTS = 5;
        private const int LOCKOUT_MINUTES = 15;

        public AuthController(ApplicationDbContext context, IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _config = config;
            _http = httpClientFactory.CreateClient();
        }

        // ─── LOGIN ────────────────────────────────────────────────────────────
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            // 1. Verify reCAPTCHA token first
            var captchaValid = await VerifyRecaptcha(model.RecaptchaToken);
            if (!captchaValid)
                return BadRequest(new { message = "CAPTCHA verification failed. Please try again." });

            string cleanId = model.EmployeeId?.Trim().ToUpper() ?? "";
            string attempt = model.Password ?? "";
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            if (clientIp == "::1") clientIp = "127.0.0.1";

            var user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == cleanId);

            // 2. Check lockout before doing anything else
            if (user != null && user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow)
            {
                var remaining = (int)(user.LockoutUntil.Value - DateTime.UtcNow).TotalMinutes + 1;
                await WriteAuditLog(cleanId, clientIp, "LOCKED");
                return Unauthorized(new { message = $"Account locked. Try again in {remaining} minute(s)." });
            }

            // 3. Verify password — exact match only (no case variants)
            bool isValid = user != null && BCrypt.Net.BCrypt.Verify(attempt, user.PasswordHash);

            // 4. Handle failed attempt
            if (user != null && !isValid)
            {
                user.FailedLoginAttempts += 1;

                if (user.FailedLoginAttempts >= MAX_FAILED_ATTEMPTS)
                {
                    user.LockoutUntil = DateTime.UtcNow.AddMinutes(LOCKOUT_MINUTES);
                    user.FailedLoginAttempts = 0;
                    await _context.SaveChangesAsync();
                    await WriteAuditLog(cleanId, clientIp, "FAILED");
                    return Unauthorized(new { message = $"Too many failed attempts. Account locked for {LOCKOUT_MINUTES} minutes." });
                }

                await _context.SaveChangesAsync();
            }

            await WriteAuditLog(cleanId, clientIp, isValid ? "SUCCESS" : "FAILED");

            if (user == null) return Unauthorized(new { message = "Identity Not Found" });
            if (!isValid)
            {
                int attemptsLeft = MAX_FAILED_ATTEMPTS - user.FailedLoginAttempts;
                return Unauthorized(new { message = $"Password Incorrect. {attemptsLeft} attempt(s) remaining." });
            }
            if (user.Status == "INACTIVE") return Unauthorized(new { message = "Access Revoked" });

            // 5. Reset failed attempts on success
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            await _context.SaveChangesAsync();

            // 6. Issue JWT
            var token = GenerateJwt(user);

            // 7. Set JWT as HttpOnly, Secure, SameSite=Strict cookie
            Response.Cookies.Append("jwt", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,           // HTTPS only — remove only for local dev
                SameSite = SameSiteMode.None,
                MaxAge = TimeSpan.FromMinutes(30),
                Path = "/"
            });

            return Ok(new
            {
                message = "Success",
                user = new
                {
                    name = user.Name,
                    role = user.Role,
                    employeeId = user.EmployeeId,
                    department = user.Department
                }
            });
        }

        // ─── LOGOUT ───────────────────────────────────────────────────────────
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("jwt", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            });
            return Ok(new { message = "Logged out" });
        }

        // ─── PROVISION (create any user — admin only) ─────────────────────────
        [HttpPost("provision")]
        public async Task<IActionResult> Provision([FromBody] ProvisionModel model)
        {
            // Verify caller is ADMIN via JWT
            var callerRole = User.FindFirstValue(ClaimTypes.Role);
            if (callerRole?.ToUpper() != "ADMIN")
                return Forbid();

            try
            {
                if (string.IsNullOrWhiteSpace(model.Password))
                    return BadRequest(new { message = "Password required." });

                string cleanId = model.EmployeeId?.Trim().ToUpper() ?? "";
                if (await _context.Users.AnyAsync(u => u.EmployeeId == cleanId))
                    return BadRequest(new { message = "ID already exists." });

                var user = new User
                {
                    Name = model.Name?.Trim().ToUpper(),
                    EmployeeId = cleanId,
                    Role = model.Role?.ToUpper(),
                    Department = model.Department?.ToUpper(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password.Trim()),
                    CreatedAt = DateTime.UtcNow,
                    Status = "ACTIVE"
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                return Ok(new { message = "User Provisioned Successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Server Error", detail = ex.Message });
            }
        }

        // ─── REGISTER ADMIN ───────────────────────────────────────────────────
        [HttpPost("register-admin")]
        public async Task<IActionResult> RegisterAdmin([FromBody] RegisterAdminModel model)
        {
            // Validate the secret key server-side
            var expectedKey = _config["AdminRegistration:SecretKey"];
            if (string.IsNullOrWhiteSpace(expectedKey) || model.SecretKey != expectedKey)
                return Unauthorized(new { message = "Invalid provisioning key." });

            if (string.IsNullOrWhiteSpace(model.Password))
                return BadRequest(new { message = "Password required." });

            string cleanId = model.EmployeeId?.Trim().ToUpper() ?? "";
            if (await _context.Users.AnyAsync(u => u.EmployeeId == cleanId))
                return BadRequest(new { message = "ID already exists." });

            var user = new User
            {
                Name = model.Name?.Trim().ToUpper(),
                EmployeeId = cleanId,
                Role = "ADMIN",
                Department = "ADMINISTRATION",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password.Trim()),
                CreatedAt = DateTime.UtcNow,
                Status = "ACTIVE"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Admin Initialized Successfully" });
        }

        // ─── HELPERS ──────────────────────────────────────────────────────────

        private string GenerateJwt(User user)
        {
            var jwtKey = _config["Jwt:Key"] ?? throw new Exception("JWT Key missing in config");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.EmployeeId),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role.ToUpper()),
                new Claim("department", user.Department ?? ""),
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task<bool> VerifyRecaptcha(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            var secret = _config["Recaptcha:SecretKey"];
            if (string.IsNullOrWhiteSpace(secret)) return false;

            var response = await _http.PostAsync(
                $"https://www.google.com/recaptcha/api/siteverify?secret={secret}&response={token}",
                null
            );

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            bool success = doc.RootElement.GetProperty("success").GetBoolean();
            // For v3 reCAPTCHA, also check score (0.0 - 1.0). 0.5+ is human.
            if (success && doc.RootElement.TryGetProperty("score", out var scoreEl))
            {
                double score = scoreEl.GetDouble();
                return score >= 0.5;
            }

            return success;
        }

        private async Task WriteAuditLog(string employeeId, string ip, string status)
        {
            try
            {
                _context.LoginLogs.Add(new LoginLog
                {
                    EmployeeId = employeeId,
                    IpAddress = ip,
                    Status = status,
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AUDIT ERROR] {ex.Message}");
            }
        }
    }

    // ─── REQUEST MODELS ───────────────────────────────────────────────────────

    public class LoginModel
    {
        public string EmployeeId { get; set; } = "";
        public string Password { get; set; } = "";
        public string? RecaptchaToken { get; set; }
    }

    public class ProvisionModel
    {
        public string Name { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string Role { get; set; } = "";
        public string Department { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class RegisterAdminModel
    {
        public string Name { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string Password { get; set; } = "";
        public string SecretKey { get; set; } = "";
    }
}