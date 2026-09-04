using System.ComponentModel.DataAnnotations;

namespace BricksAndBooks.Models
{
    public class Announcement
    {
        [Key] public int Id { get; set; }
        [Required, StringLength(160)] public string Title { get; set; } = string.Empty;
        [Required, StringLength(4000)] public string Message { get; set; } = string.Empty;
        [StringLength(30)] public string Audience { get; set; } = "Everyone";
        public bool IsPublished { get; set; } = true;
        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public string? CreatedByUserId { get; set; }
    }
}
