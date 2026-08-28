using System.ComponentModel.DataAnnotations;

namespace BricksAndBooks.Models.ViewModels
{
    public class AdminPasswordResetViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at most {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Temporary password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm temporary password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The temporary password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
