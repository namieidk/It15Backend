using System.ComponentModel.DataAnnotations;

namespace YourProject.Models
{
    public class Message
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string SenderId { get; set; } = string.Empty;
        
        [Required]
        public string ReceiverId { get; set; } = string.Empty;
        public string? GroupId { get; set; }
        
        [Required]
        public string Content { get; set; } = string.Empty;
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public bool IsRead { get; set; } = false;
    }
}