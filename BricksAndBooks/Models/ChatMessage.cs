using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string SenderUserId { get; set; }

        [ForeignKey("SenderUserId")]
        public ApplicationUser Sender { get; set; }

        [Required]
        public string RecipientUserId { get; set; }

        [ForeignKey("RecipientUserId")]
        public ApplicationUser Recipient { get; set; }

        [Required]
        public string Content { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }
    }
}