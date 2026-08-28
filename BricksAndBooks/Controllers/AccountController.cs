using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using BricksAndBooks.Infrastructure;

namespace BricksAndBooks.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly NotificationService _notificationService;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, NotificationService notificationService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _notificationService = notificationService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            returnUrl ??= Url.Content("~/");

            if (ModelState.IsValid)
            {
                // This doesn't count login failures towards account lockout
                // To enable password failures to trigger account lockout, set lockoutOnFailure: true
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = model.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    return View("Lockout");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return View(model);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet]
        public IActionResult Register(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            returnUrl ??= Url.Content("~/");

            if (string.IsNullOrWhiteSpace(model.Email) || !model.Email.Trim().EndsWith("@ufs.ac.za", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Email), "Use your official University of the Free State email address ending in @ufs.ac.za.");
            }
            else
            {
                model.Email = model.Email.Trim().ToLowerInvariant();
            }

            // Exactly one applicant type is required. The matching identifier is
            // validated and the other identifier is deliberately discarded.
            if (model.IsStudent == model.IsStaff)
            {
                ModelState.AddModelError(string.Empty, "Select exactly one applicant type: student or staff.");
            }
            else if (model.IsStudent)
            {
                model.EmployeeNumber = null;
                if (string.IsNullOrWhiteSpace(model.StudentNumber))
                {
                    ModelState.AddModelError("StudentNumber", "Student number is required for students.");
                }
            }
            else
            {
                model.StudentNumber = null;
                if (string.IsNullOrWhiteSpace(model.EmployeeNumber))
                {
                    ModelState.AddModelError("EmployeeNumber", "Employee number is required for staff.");
                }
            }

            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    StudentNumber = model.StudentNumber,
                    EmployeeNumber = model.EmployeeNumber,
                    IsStudent = model.IsStudent,
                    IsStaff = model.IsStaff
                };
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    // Assign roles based on checkboxes
                    if (model.IsStudent)
                    {
                        await _userManager.AddToRoleAsync(user, "Student");
                    }
                    if (model.IsStaff)
                    {
                        await _userManager.AddToRoleAsync(user, "Staff");
                    }
                    // Privileged roles are assigned only by an administrator through User management.
                    _notificationService.CreateActivityNotification(user.Id, "Account created", "Your Bricks And Books account has been created successfully.", "/Account/Manage");
                    _notificationService.CreateRoleActivityNotification("Admin", "New customer registration", $"{user.FirstName} {user.LastName} registered a new {((user.IsStudent) ? "Student" : "Staff")} account.", $"/User/Details/{user.Id}");
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return LocalRedirect(returnUrl);
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Manage()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var model = new ManageViewModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                IsStudent = await _userManager.IsInRoleAsync(user, "Student"),
                IsStaff = await _userManager.IsInRoleAsync(user, "Staff"),
                IsFinancialOfficer = await _userManager.IsInRoleAsync(user, "FinancialOfficer"),
                IsAdmin = await _userManager.IsInRoleAsync(user, "Admin")
            };

            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(ManageViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (string.IsNullOrWhiteSpace(model.Email) || !model.Email.Trim().EndsWith("@ufs.ac.za", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Email), "Use your official University of the Free State email address ending in @ufs.ac.za.");
            }
            else
            {
                model.Email = model.Email.Trim().ToLowerInvariant();
            }

            if (ModelState.IsValid)
            {
                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                user.Email = model.Email;
                user.PhoneNumber = model.PhoneNumber;

                var result = await _userManager.UpdateAsync(user);
                if (result.Succeeded)
                {
                    // Roles are managed by administrators, never by a user's own profile form.
                    _notificationService.CreateActivityNotification(user.Id, "Profile updated", "Your personal and account information has been updated.", "/Account/Manage");
                    await _signInManager.RefreshSignInAsync(user);
                    return RedirectToAction(nameof(Manage));
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);
                _notificationService.CreateActivityNotification(user.Id, "Password changed", "Your account password was changed successfully.", "/Account/Manage");
                TempData["StatusMessage"] = "Your password has been changed successfully.";
                return RedirectToAction(nameof(Manage));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }
    }
}