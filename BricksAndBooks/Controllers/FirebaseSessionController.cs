using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BricksAndBooks.Data;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BricksAndBooks.Controllers;

/// <summary>
/// Anonymous bootstrap endpoint for the native Firebase application. It validates the existing
/// ASP.NET Identity account store and returns a Firebase custom token; it does not create a
/// parallel password store or accept client-selected privileged roles.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/firebase/session")]
public sealed class FirebaseSessionController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly FirebaseIdentityService _firebaseIdentity;
    private readonly NotificationService _notificationService;
    private readonly IRepositoryWrapper _repositoryWrapper;

    public FirebaseSessionController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        FirebaseIdentityService firebaseIdentity,
        NotificationService notificationService,
        IRepositoryWrapper repositoryWrapper)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _firebaseIdentity = firebaseIdentity;
        _notificationService = notificationService;
        _repositoryWrapper = repositoryWrapper;
    }

    [HttpPost]
    public async Task<IActionResult> CreateSession([FromBody] FirebaseSessionRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new { message = "An action is required." });
        }

        var fields = request.Fields ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        try
        {
            return request.Action switch
            {
                "legacyLogin" => await LegacyLoginAsync(fields),
                "register" => await RegisterAsync(fields),
                "passwordResetRequest" => await PasswordResetRequestAsync(fields),
                _ => BadRequest(new { message = "Unsupported Firebase session action." })
            };
        }
        catch (FieldValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task<IActionResult> LegacyLoginAsync(IReadOnlyDictionary<string, JsonElement> fields)
    {
        var model = new LoginViewModel
        {
            Email = RequiredText(fields, "email"),
            Password = RequiredText(fields, "password")
        };
        if (TryValidate(model, out var validationMessage))
        {
            return BadRequest(new { message = validationMessage });
        }

        // This uses the same Identity password and lockout checks as the web account flow, but
        // does not create an ASP.NET cookie: Firebase owns the native client session.
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (signInResult.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked, new { message = "This account is locked out." });
        }
        if (!signInResult.Succeeded)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(await CreateAuthResponseAsync(user));
    }

    private async Task<IActionResult> RegisterAsync(IReadOnlyDictionary<string, JsonElement> fields)
    {
        var password = RequiredText(fields, "password");
        var model = new RegisterViewModel
        {
            Email = RequiredText(fields, "email"),
            FirstName = RequiredText(fields, "firstName"),
            LastName = RequiredText(fields, "lastName"),
            Password = password,
            // The native contract has one password field. Supplying the same value applies the
            // original RegisterViewModel password constraints without inventing another input.
            ConfirmPassword = password,
            StudentNumber = OptionalText(fields, "studentNumber"),
            EmployeeNumber = OptionalText(fields, "employeeNumber"),
            IsStudent = RequiredBoolean(fields, "isStudent"),
            IsStaff = RequiredBoolean(fields, "isStaff")
        };

        // This is the same normalization and exactly-one-applicant-type rule used by
        // AccountController.Register. Privileged role fields are not read from the request.
        model.Email = model.Email.Trim().ToLowerInvariant();
        if (model.IsStudent == model.IsStaff)
        {
            return BadRequest(new { message = "Select exactly one applicant type: student or staff." });
        }
        if (model.IsStudent)
        {
            model.EmployeeNumber = null;
            if (string.IsNullOrWhiteSpace(model.StudentNumber))
            {
                return BadRequest(new { message = "Student number is required for students." });
            }
        }
        else
        {
            model.StudentNumber = null;
            if (string.IsNullOrWhiteSpace(model.EmployeeNumber))
            {
                return BadRequest(new { message = "Employee number is required for staff." });
            }
        }

        if (TryValidate(model, out var validationMessage))
        {
            return BadRequest(new { message = validationMessage });
        }

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
        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(new { message = string.Join(" ", createResult.Errors.Select(error => error.Description)) });
        }

        // Only normal registration roles may be granted here. Admin and financial-officer roles
        // remain assignable solely through the existing administrator-managed user workflow.
        if (model.IsStudent)
        {
            await _userManager.AddToRoleAsync(user, "Student");
        }
        else
        {
            await _userManager.AddToRoleAsync(user, "Staff");
        }

        _notificationService.CreateActivityNotification(
            user.Id,
            "Account created",
            "Your Bricks And Books account has been created successfully.",
            "/Account/Manage");
        _notificationService.CreateRoleActivityNotification(
            "Admin",
            "New customer registration",
            $"{user.FirstName} {user.LastName} registered a new {(user.IsStudent ? "Student" : "Staff")} account.",
            $"/User/Details/{user.Id}");

        return Ok(await CreateAuthResponseAsync(user));
    }

    private async Task<IActionResult> PasswordResetRequestAsync(IReadOnlyDictionary<string, JsonElement> fields)
    {
        var model = new ForgotPasswordViewModel { Email = RequiredText(fields, "email") };
        if (TryValidate(model, out var validationMessage))
        {
            return BadRequest(new { message = validationMessage });
        }

        // Keep the original non-enumerating workflow: an identical confirmation is returned
        // whether an account exists, while an existing account gets one pending request and the
        // authorized reviewers are notified.
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null)
        {
            var alreadyPending = _repositoryWrapper.PasswordResetRequest
                .FindByCondition(reset => reset.UserId == user.Id && reset.Status == PasswordResetRequestStatus.Pending)
                .Any();
            if (!alreadyPending)
            {
                _repositoryWrapper.PasswordResetRequest.Create(new PasswordResetRequest
                {
                    UserId = user.Id
                });
                _repositoryWrapper.Save();

                var reviewers = (await _userManager.GetUsersInRoleAsync("Admin"))
                    .Concat(await _userManager.GetUsersInRoleAsync("FinancialOfficer"))
                    .GroupBy(reviewer => reviewer.Id, StringComparer.Ordinal)
                    .Select(group => group.First());
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

        return Ok(new { message = "If an account matches that email, a password reset request has been submitted for review." });
    }

    private async Task<object> CreateAuthResponseAsync(ApplicationUser user)
    {
        var customToken = await _firebaseIdentity.MintAsync(user);
        var profile = await _firebaseIdentity.GetPublicProfileAsync(user);
        return new
        {
            customToken,
            uid = user.Id,
            profile
        };
    }

    private static bool TryValidate(object model, out string validationMessage)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);
        validationMessage = isValid
            ? string.Empty
            : results.FirstOrDefault()?.ErrorMessage ?? "One or more fields are invalid.";
        return !isValid;
    }

    private static string RequiredText(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(value.GetString()))
        {
            throw new FieldValidationException($"{name} is required.");
        }

        return value.GetString()!;
    }

    private static string? OptionalText(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FieldValidationException($"{name} must be a string.");
        }

        return value.GetString();
    }

    private static bool RequiredBoolean(IReadOnlyDictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value)
            || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw new FieldValidationException($"{name} must be true or false.");
        }

        return value.GetBoolean();
    }

    public sealed class FirebaseSessionRequest
    {
        public string? Action { get; init; }
        public Dictionary<string, JsonElement>? Fields { get; init; }
    }

    private sealed class FieldValidationException : Exception
    {
        public FieldValidationException(string message) : base(message)
        {
        }
    }
}
