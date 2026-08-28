using System.ComponentModel.DataAnnotations;

namespace BricksAndBooks.Models.ViewModels
{
    public class ManageViewModel
    {
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Display(Name = "Email")]
        [EmailAddress]
        public string Email { get; set; }

        [Display(Name = "Phone Number")]
        [Phone]
        public string PhoneNumber { get; set; }

        [Display(Name = "Is Student")]
        public bool IsStudent { get; set; }

        [Display(Name = "Is Staff")]
        public bool IsStaff { get; set; }

        [Display(Name = "Is Financial Officer")]
        public bool IsFinancialOfficer { get; set; }

        [Display(Name = "Is Admin")]
        public bool IsAdmin { get; set; }
    }
}