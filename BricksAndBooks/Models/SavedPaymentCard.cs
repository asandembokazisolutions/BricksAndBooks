using System.ComponentModel.DataAnnotations;
namespace BricksAndBooks.Models
{
    /// <summary>Demo-only stored card presentation data. PAN is discarded; CVC is salted and hashed for demo re-entry checks only.</summary>
    public class SavedPaymentCard
    {
        [Key] public int Id { get; set; }
        [Required, StringLength(450)] public string UserId { get; set; } = string.Empty;
        [Required, StringLength(100)] public string CardholderName { get; set; } = string.Empty;
        [Required, StringLength(4)] public string LastFour { get; set; } = string.Empty;
        [Required, StringLength(12)] public string Expiry { get; set; } = string.Empty;
        [Required, StringLength(32)] public string CvcSalt { get; set; } = string.Empty;
        [Required, StringLength(128)] public string CvcHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
