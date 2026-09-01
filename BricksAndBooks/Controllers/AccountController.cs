using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;
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
        private readonly IRepositoryWrapper _repositoryWrapper;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, NotificationService notificationService, IRepositoryWrapper repositoryWrapper)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _notificationService = notificationService;
            _repositoryWrapper = repositoryWrapper;
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
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
            {
                var alreadyPending = _repositoryWrapper.PasswordResetRequest
                    .FindByCondition(r => r.UserId == user.Id && r.Status == PasswordResetRequestStatus.Pending)
                    .Any();
                if (!alreadyPending)
                {
                    _repositoryWrapper.PasswordResetRequest.Create(new PasswordResetRequest
                    {
                        UserId = user.Id
                    });
                    _repositoryWrapper.Save();

                    // Let every admin/financial officer know a reset is waiting on them.
                    var reviewers = (await _userManager.GetUsersInRoleAsync("Admin"))
                        .Concat(await _userManager.GetUsersInRoleAsync("FinancialOfficer"))
                        .DistinctBy(u => u.Id);
                    foreach (var reviewer in reviewers)
                    {
                        _notificationService.CreateActivityNotification(
                            reviewer.Id,
                            "Password reset requested",
                            $"{user.FirstName} {user.LastName} ({user.Email}) can't sign in and requested a password reset.",
                            "/Account/PasswordResetRequests");
                    }
                }
            }

            // Always show the same confirmation, whether or not the email matched an account,
            // so the form can't be used to check which emails are registered.
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        // GET: Account/PasswordResetRequests — pending reset requests waiting for review.
        [HttpGet]
        [Authorize(Roles = "Admin,FinancialOfficer")]
        public IActionResult PasswordResetRequests()
        {
            var requests = _repositoryWrapper.PasswordResetRequest.FindAll()
                .Where(r => r.Status == PasswordResetRequestStatus.Pending)
                .OrderBy(r => r.RequestedAt)
                .ToList();
            return View(requests);
        }

        // POST: Account/ResolvePasswordResetRequest/5 — issues a new temporary password.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,FinancialOfficer")]
        public async Task<IActionResult> ResolvePasswordResetRequest(int id)
        {
            var request = _repositoryWrapper.PasswordResetRequest.FindByCondition(r => r.Id == id).FirstOrDefault();
            if (request == null)
            {
                return NotFound();
            }

            var targetUser = await _userManager.FindByIdAsync(request.UserId);
            var reviewer = await _userManager.GetUserAsync(User);
            if (targetUser == null || reviewer == null)
            {
                return NotFound();
            }

            var temporaryPassword = GenerateTemporaryPassword();
            var token = await _userManager.GeneratePasswordResetTokenAsync(targetUser);
            var result = await _userManager.ResetPasswordAsync(targetUser, token, temporaryPassword);
            if (!result.Succeeded)
            {
                TempData["StatusMessage"] = "Could not reset the password: " + string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(PasswordResetRequests));
            }

            request.Status = PasswordResetRequestStatus.Completed;
            request.ResolvedAt = DateTime.UtcNow;
            request.ResolvedByUserId = reviewer.Id;
            _repositoryWrapper.PasswordResetRequest.Update(request);
            _repositoryWrapper.Save();

            _notificationService.CreateActivityNotification(
                targetUser.Id,
                "Password reset",
                "An administrator issued you a new temporary password. Sign in with it and change it right away from Account settings.",
                "/Account/Login");

            // The temporary password is shown once, here, so the admin can pass it on securely
            // (in person or by phone) — it is never stored in plain text or emailed.
            TempData["StatusMessage"] = $"Temporary password for {targetUser.Email}: {temporaryPassword}";
            return RedirectToAction(nameof(PasswordResetRequests));
        }

        private static string GenerateTemporaryPassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnpqrstuvwxyz";
            const string digits = "23456789";
            const string special = "!@#$%?";
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();

            char Pick(string alphabet)
            {
                var bytes = new byte[1];
                rng.GetBytes(bytes);
                return alphabet[bytes[0] % alphabet.Length];
            }

            var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(special) };
            for (var i = 0; i < 6; i++)
            {
                chars.Add(Pick(upper + lower + digits));
            }

            // Shuffle so the fixed character classes aren't always in the same position.
            var bytesForShuffle = new byte[chars.Count];
            rng.GetBytes(bytesForShuffle);
            return new string(chars.OrderBy(c => bytesForShuffle[chars.IndexOf(c)]).ToArray());
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