using Microsoft.AspNetCore.Identity;

namespace BricksAndBooks.Models
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PersonalEmail { get; set; } // contact and account-recovery address
        public string? StudentNumber { get; set; } // for students
        public string? EmployeeNumber { get; set; } // for staff
        public bool IsStudent { get; set; }
        public bool IsStaff { get; set; }
        public bool IsMaintenanceWorker { get; set; }
        public MaintenanceCategory? MaintenanceSpecialty { get; set; }
        public bool IsFinancialOfficer { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
