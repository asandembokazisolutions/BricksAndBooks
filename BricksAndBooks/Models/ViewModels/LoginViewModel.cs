using System.ComponentModel.DataAnnotations;

namespace BricksAndBooks.Models.ViewModels
{
    public class LoginViewModel
    {
        public LoginViewModel()
        {
            Email = string.Empty;
            Password = string.Empty;
        }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }
}