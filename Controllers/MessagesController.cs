using Microsoft.AspNetCore.Mvc;
using IO.Ably; 
using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Models;

namespace YourProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly AblyRest _ably;

        public MessagesController(ApplicationDbContext context, AblyRest ably)
        {
            _context = context;
            _ably = ably;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] Models.Message msg)
        {
            try 
            {
                msg.Timestamp = DateTime.UtcNow;
                _context.Messages.Add(msg);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(msg.GroupId))
                {
                    await _ably.Channels.Get($"group-{msg.GroupId}").PublishAsync("message", msg);
                }
                else
                {
                    await _ably.Channels.Get($"user-{msg.ReceiverId}").PublishAsync("message", msg);
                    await _ably.Channels.Get($"user-{msg.SenderId}").PublishAsync("message", msg);
                }

                return Ok(msg);
            }
            catch (Exception)
            {
                return StatusCode(500, "SECURE COMMS ERROR: UPLOAD FAILED");
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] string senderId, [FromQuery] string? receiverId, [FromQuery] string? groupId)
        {
            if (!string.IsNullOrEmpty(groupId))
            {
                return Ok(await _context.Messages
                    .Where(m => m.GroupId == groupId)
                    .OrderBy(m => m.Timestamp)
                    .ToListAsync());
            }

            return Ok(await _context.Messages
                .Where(m => (m.SenderId == senderId && m.ReceiverId == receiverId) || 
                            (m.SenderId == receiverId && m.ReceiverId == senderId))
                .OrderBy(m => m.Timestamp)
                .ToListAsync());
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            // Specifically selecting from the User model properties you provided
            var users = await _context.Users
                .Select(u => new 
                { 
                    u.EmployeeId, 
                    u.Name, 
                    u.Role, 
                    u.Department,
                    u.ProfileImage, // Added as requested
                    u.Status 
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}