using System.Security.Claims;
using System.Text.Json;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Extensions;

namespace BricksAndBooks.Controllers;

/// <summary>
/// Native Firebase gateway. Firebase bearer validation and principal-to-Identity mapping are
/// performed by the application middleware before this controller is reached. The controller
/// deliberately accepts only a bearer-authenticated mapped Identity user; it never treats a
/// mobile supplied UID as authority.
/// </summary>
[ApiController]
[Route("api/firebase")]
public sealed class FirebaseActionController : ControllerBase
{
    private const long MaximumUploadBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedUploadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp"
    };

    private readonly FirebaseMvcActions _actions;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FirebaseActionController> _logger;

    public FirebaseActionController(
        FirebaseMvcActions actions,
        UserManager<ApplicationUser> users,
        IWebHostEnvironment environment,
        ILogger<FirebaseActionController> logger)
    {
        _actions = actions;
        _users = users;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("action")]
    public async Task<IActionResult> Action(
        [FromBody] FirebaseActionRequest? request,
        CancellationToken cancellationToken)
    {
        var user = await GetBearerMappedUserAsync(cancellationToken);
        if (user == null)
        {
            return Unauthorized(new { success = false, message = "A valid Firebase bearer token is required." });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new { success = false, message = "An action is required." });
        }

        var result = await _actions.ExecuteAsync(
            request.Action.Trim(),
            request.Fields ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            user,
            cancellationToken);

        return StatusCode(result.StatusCode, new
        {
            success = result.Success,
            message = result.Message,
            item = result.Item,
            items = result.Items,
            summary = result.Summary
        });
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaximumUploadBytes + 64 * 1024)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var user = await GetBearerMappedUserAsync(cancellationToken);
        if (user == null)
        {
            return Unauthorized(new { success = false, message = "A valid Firebase bearer token is required." });
        }

        if (!Request.HasFormContentType)
        {
            return BadRequest(new { success = false, message = "Upload requests must use multipart/form-data." });
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { success = false, message = "The upload could not be read." });
        }

        var files = form.Files.Where(file => file.Length > 0).ToList();
        if (files.Count != 1)
        {
            return BadRequest(new { success = false, message = "Upload exactly one file." });
        }

        var file = files[0];
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedUploadExtensions.Contains(extension))
        {
            return BadRequest(new { success = false, message = "Only PDF, JPG, JPEG, PNG, and WEBP files are allowed." });
        }
        if (file.Length > MaximumUploadBytes)
        {
            return BadRequest(new { success = false, message = "Files must be 5 MB or smaller." });
        }

        var uid = SafeUidSegment(user.Id);
        if (string.IsNullOrWhiteSpace(uid))
        {
            return BadRequest(new { success = false, message = "The authenticated account does not have a usable identifier." });
        }

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "firebase", uid);
        Directory.CreateDirectory(uploadRoot);
        var generatedName = $"{Guid.NewGuid():N}{extension}";
        var targetPath = Path.Combine(uploadRoot, generatedName);

        try
        {
            await using var destination = System.IO.File.Create(targetPath);
            await file.CopyToAsync(destination, cancellationToken);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Firebase upload could not be stored for user {UserId}.", user.Id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "The upload could not be stored." });
        }

        var relativePath = $"/uploads/firebase/{uid}/{generatedName}";
        var absoluteUrl = UriHelper.BuildAbsolute(Request.Scheme, Request.Host, Request.PathBase, relativePath);
        return Ok(new
        {
            success = true,
            message = "File uploaded.",
            item = new { url = absoluteUrl, path = relativePath, fileName = generatedName }
        });
    }

    private async Task<ApplicationUser?> GetBearerMappedUserAsync(CancellationToken cancellationToken)
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || contextNotVerified() || !(User.Identity?.IsAuthenticated ?? false))
        {
            return null;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _users.FindByIdAsync(userId);
    }

    private bool contextNotVerified() => HttpContext.Items["FirebaseVerified"] is not true;

    private static string SafeUidSegment(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        var safe = new string(uid.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return safe.Length is > 0 and <= 128 ? safe : string.Empty;
    }
}

public sealed class FirebaseActionRequest
{
    public string? Action { get; init; }
    public Dictionary<string, JsonElement>? Fields { get; init; }
}
