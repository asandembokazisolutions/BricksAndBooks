using System.ComponentModel.DataAnnotations;
using BricksAndBooks.Models;
namespace BricksAndBooks.Models.ViewModels
{
    public class MaintenanceWorkerCreateViewModel
    {
        public int RequestId { get; set; }
        [Required, StringLength(50, MinimumLength = 2)] public string FirstName { get; set; } = string.Empty;
        [Required, StringLength(50, MinimumLength = 2)] public string LastName { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = string.Empty;
        [Required] public MaintenanceCategory Specialty { get; set; }
    }
}
