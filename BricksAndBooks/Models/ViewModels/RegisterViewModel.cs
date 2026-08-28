using System.ComponentModel.DataAnnotations;

namespace BricksAndBooks.Models.ViewModels
{
    public class RegisterViewModel
    {
        public RegisterViewModel()
        {
            Email = string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            Password = string.Empty;
            ConfirmPassword = string.Empty;
        }

        [Required]
        [EmailAddress]
        [RegularExpression(@"(?i)^[^@\s]+@ufs\.ac\.za$", ErrorMessage = "Use your official University of the Free State email address ending in @ufs.ac.za.")]
        [Display(Name = "University email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(50, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 2)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(50, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 2)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [StringLength(20, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.")]
        [Display(Name = "Student Number")]
        public string? StudentNumber { get; set; }

        [StringLength(20, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.")]
        [Display(Name = "Employee Number")]
        public string? EmployeeNumber { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "I am a student")]
        public bool IsStudent { get; set; }

        [Display(Name = "I am a staff member")]
        public bool IsStaff { get; set; }

        [Display(Name = "I am a financial officer (requires admin approval)")]
        public bool IsFinancialOfficer { get; set; }

        [Display(Name = "I am an administrator (requires admin approval)")]
        public bool IsAdmin { get; set; }
    }
}