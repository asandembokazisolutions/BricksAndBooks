using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum PasswordResetRequestStatus
    {
        Pending,
        Completed
    }

    // Raised when a staff member or student can't sign in and requests a password reset
    // from the login page. An administrator reviews it and issues a new temporary password.
    public class PasswordResetRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        public PasswordResetRequestStatus Status { get; set; } = PasswordResetRequestStatus.Pending;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }

        public string? ResolvedByUserId { get; set; }

        [ForeignKey("ResolvedByUserId")]
        public ApplicationUser? ResolvedByUser { get; set; }
    }
}
