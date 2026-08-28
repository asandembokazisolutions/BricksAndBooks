using System;
using System.Linq;
using System.Threading.Tasks;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers
{
    [Authorize(Roles = "Admin,FinancialOfficer")]
    public class UserController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService _notificationService;
        private readonly AppDbContext _context;

        public UserController(
            UserManager<ApplicationUser> userManager,
            NotificationService notificationService,
            AppDbContext context)
        {
            _userManager = userManager;
            _notificationService = notificationService;
            _context = context;
        }

        public IActionResult Index()
        {
            var users = _userManager.Users.AsQueryable();
            if (!CanManageRoles)
            {
                users = users.Where(user => !user.IsAdmin);
            }
            return View(users.ToList());
        }

        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || (!CanManageRoles && user.IsAdmin)) return NotFound();
            return View(user);
        }

        public IActionResult Create()
        {
            ViewData["CanManageRoles"] = CanManageRoles;
            return View(new ApplicationUser());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string firstName,
            string lastName,
            string username,
            string email,
            string? studentNumber,
            string? employeeNumber,
            string password,
            string confirmPassword,
            bool isStudent,
            bool isStaff,
            bool isFinancialOfficer,
            bool isAdmin)
        {
            if (!CanManageRoles)
            {
                isFinancialOfficer = false;
                isAdmin = false;
            }

            var formUser = new ApplicationUser
            {
                FirstName = firstName,
                LastName = lastName,
                UserName = username,
                Email = email,
                StudentNumber = studentNumber,
                EmployeeNumber = employeeNumber,
                IsStudent = isStudent,
                IsStaff = isStaff,
                IsFinancialOfficer = isFinancialOfficer,
                IsAdmin = isAdmin
            };

            ValidateNewUser(formUser, password, confirmPassword);
            if (!ModelState.IsValid)
            {
                ViewData["CanManageRoles"] = CanManageRoles;
                return View(formUser);
            }

            formUser.Email = formUser.Email!.Trim().ToLowerInvariant();
            formUser.UserName = string.IsNullOrWhiteSpace(formUser.UserName) ? formUser.Email : formUser.UserName.Trim();
            formUser.StudentNumber = formUser.IsStudent ? formUser.StudentNumber?.Trim() : null;
            formUser.EmployeeNumber = formUser.IsStaff ? formUser.EmployeeNumber?.Trim() : null;

            var result = await _userManager.CreateAsync(formUser, password);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                ViewData["CanManageRoles"] = CanManageRoles;
                return View(formUser);
            }

            await SyncRolesAsync(formUser);
            _notificationService.CreateActivityNotification(formUser.Id, "Account created", "An authorised team member created your Bricks And Books account.", "/Account/Manage");
            _notificationService.CreateRoleActivityNotification("Admin", "Customer account created", $"{formUser.FirstName} {formUser.LastName} was added by {User.Identity?.Name}.", $"/User/Details/{formUser.Id}");
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            ViewData["CanManageRoles"] = CanManageRoles;
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            string id,
            string firstName,
            string lastName,
            string userName,
            string email,
            string? studentNumber,
            string? employeeNumber,
            bool isFinancialOfficer,
            bool isAdmin)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            if (!CanManageRoles)
            {
                isFinancialOfficer = user.IsFinancialOfficer;
                isAdmin = user.IsAdmin;
            }

            if (!IsUniversityEmail(email))
                ModelState.AddModelError(nameof(email), "Use an official University of the Free State email address ending in @ufs.ac.za.");
            if (user.IsStudent && string.IsNullOrWhiteSpace(studentNumber))
                ModelState.AddModelError(nameof(studentNumber), "Student number is required for a student account.");
            if (user.IsStaff && string.IsNullOrWhiteSpace(employeeNumber))
                ModelState.AddModelError(nameof(employeeNumber), "Employee number is required for a staff account.");

            user.FirstName = firstName;
            user.LastName = lastName;
            user.UserName = userName;
            user.Email = email;
            user.StudentNumber = user.IsStudent ? studentNumber : null;
            user.EmployeeNumber = user.IsStaff ? employeeNumber : null;
            user.IsFinancialOfficer = isFinancialOfficer;
            user.IsAdmin = isAdmin;

            if (!ModelState.IsValid)
            {
                ViewData["CanManageRoles"] = CanManageRoles;
                return View(user);
            }

            user.FirstName = user.FirstName.Trim();
            user.LastName = user.LastName.Trim();
            user.UserName = string.IsNullOrWhiteSpace(user.UserName) ? email.Trim() : user.UserName.Trim();
            user.Email = email.Trim().ToLowerInvariant();
            user.StudentNumber = user.IsStudent ? studentNumber?.Trim() : null;
            user.EmployeeNumber = user.IsStaff ? employeeNumber?.Trim() : null;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                ViewData["CanManageRoles"] = CanManageRoles;
                return View(user);
            }

            await SyncRolesAsync(user);
            _notificationService.CreateActivityNotification(user.Id, "Account updated", "Your account information was updated by an authorised team member.", "/Account/Manage");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || (!CanManageRoles && user.IsAdmin)) return NotFound();
            return View(new AdminPasswordResetViewModel { UserId = user.Id, UserName = user.UserName ?? user.Email ?? "User" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(AdminPasswordResetViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null || (!CanManageRoles && user.IsAdmin)) return NotFound();

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, resetToken, model.NewPassword);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return View(model);
            }

            _notificationService.CreateActivityNotification(user.Id, "Password reset", "An authorised team member reset your account password. Sign in with the new password and change it immediately.", "/Account/Manage");
            TempData["StatusMessage"] = $"Password reset successfully for {user.UserName}.";
            return RedirectToAction(nameof(Details), new { id = user.Id });
        }

        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || (!CanManageRoles && user.IsAdmin)) return NotFound();
            return View(user);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null || (!CanManageRoles && user.IsAdmin))
            {
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Firestore writes are applied by the persistence interceptor. The SQL transaction
                // and ExecuteDelete APIs are intentionally not used by the Firestore-backed cache.
                await RemoveUserDependenciesAsync(user.Id);

                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    AddIdentityErrors(result);
                    return View("Delete", user);
                }

                TempData["StatusMessage"] = $"The account for {user.UserName} and its linked customer records were deleted successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "The account could not be deleted because one or more linked records could not be removed.");
                return View("Delete", user);
            }
        }

        private async Task RemoveUserDependenciesAsync(string userId)
        {
            // Preserve audit history but remove the deleted account as its actor.
            var auditLogs = await _context.AuditLogs.Where(log => log.UserId == userId).ToListAsync();
            foreach (var log in auditLogs)
            {
                log.UserId = null;
            }

            // A staff member can be unassigned without deleting customer requests.
            var assignedRequests = await _context.MaintenanceRequests.Where(request => request.AssignedToUserId == userId).ToListAsync();
            foreach (var request in assignedRequests)
            {
                request.AssignedToUserId = null;
                request.AssignedAt = null;
                request.UpdatedAt = DateTime.UtcNow;
            }

            // Records owned by the deleted account must be removed before Identity deletes the user.
            _context.Notifications.RemoveRange(await _context.Notifications.Where(notification => notification.UserId == userId).ToListAsync());
            _context.ChatMessages.RemoveRange(await _context.ChatMessages.Where(message => message.SenderUserId == userId || message.RecipientUserId == userId).ToListAsync());
            _context.MaintenanceRequests.RemoveRange(await _context.MaintenanceRequests.Where(request => request.UserId == userId).ToListAsync());
            _context.Reviews.RemoveRange(await _context.Reviews.Where(review => review.UserId == userId).ToListAsync());
            _context.Vehicles.RemoveRange(await _context.Vehicles.Where(vehicle => vehicle.UserId == userId).ToListAsync());
            _context.AccommodationApplications.RemoveRange(await _context.AccommodationApplications.Where(application => application.UserId == userId).ToListAsync());
            _context.Occupancies.RemoveRange(await _context.Occupancies.Where(occupancy => occupancy.UserId == userId).ToListAsync());

            await _context.SaveChangesAsync();
        }

        private bool CanManageRoles => User.IsInRole("Admin");

        private void ValidateNewUser(ApplicationUser user, string password, string confirmPassword)
        {
            if (!IsUniversityEmail(user.Email)) ModelState.AddModelError(nameof(user.Email), "Use an official University of the Free State email address ending in @ufs.ac.za.");
            if (user.IsStudent == user.IsStaff) ModelState.AddModelError(string.Empty, "Select exactly one account type: Student or Staff.");
            else if (user.IsStudent && string.IsNullOrWhiteSpace(user.StudentNumber)) ModelState.AddModelError(nameof(user.StudentNumber), "Student number is required for a student account.");
            else if (user.IsStaff && string.IsNullOrWhiteSpace(user.EmployeeNumber)) ModelState.AddModelError(nameof(user.EmployeeNumber), "Employee number is required for a staff account.");
            if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName)) ModelState.AddModelError(string.Empty, "First name and last name are required.");
            if (string.IsNullOrWhiteSpace(password) || password != confirmPassword) ModelState.AddModelError(string.Empty, "Passwords do not match.");
        }

        private async Task SyncRolesAsync(ApplicationUser user)
        {
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any()) await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (user.IsStudent) await _userManager.AddToRoleAsync(user, "Student");
            if (user.IsStaff) await _userManager.AddToRoleAsync(user, "Staff");
            if (user.IsFinancialOfficer) await _userManager.AddToRoleAsync(user, "FinancialOfficer");
            if (user.IsAdmin) await _userManager.AddToRoleAsync(user, "Admin");
        }

        private static bool IsUniversityEmail(string? email) => !string.IsNullOrWhiteSpace(email) && email.Trim().EndsWith("@ufs.ac.za", StringComparison.OrdinalIgnoreCase);

        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}