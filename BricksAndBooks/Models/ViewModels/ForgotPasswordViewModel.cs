using System.ComponentModel.DataAnnotations;

namespace BricksAndBooks.Models.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "University email")]
        public string Email { get; set; } = string.Empty;
    }
}
