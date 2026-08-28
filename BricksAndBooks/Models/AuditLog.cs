using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        [StringLength(200)]
        public string? EntityType { get; set; } // e.g., "Accommodation", "User"
        public int EntityId { get; set; } // primary key of the entity

        [StringLength(50)]
        public string? Action { get; set; } // Insert, Update, Delete

        [StringLength(450)]
        public string? UserId { get; set; } // who performed the action
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Optional: store a JSON representation of the changes
        public string? Changes { get; set; }
    }
}