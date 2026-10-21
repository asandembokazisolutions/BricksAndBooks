using System.Security.Claims;
using BricksAndBooks.Models;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Identity;

namespace BricksAndBooks.Infrastructure;

/// <summary>
/// Bridges the existing ASP.NET Identity store to Firebase Authentication without copying,
/// replacing, or trusting client-side account data. Register this class as scoped because it uses UserManager;
/// it owns a named FirebaseAdmin application instance.
/// </summary>
public sealed class FirebaseIdentityService
{
    private static readonly object AppLock = new();
    private const string FirebaseAppName = "bricks-and-books-identity";

    // These are the only application roles seeded and authorized by the legacy application.
    // A verified Firebase token never supplies a role to the backend; roles are rebuilt from
    // the existing Identity assignment on every verification.
    private static readonly HashSet<string> TrustedRoles = new(StringComparer.Ordinal)
    {
        "Admin",
        "FinancialOfficer",
        "Student",
        "Staff",
        "MaintenanceWorker"
    };

    private readonly FirebaseAuth _firebaseAuth;
    private readonly UserManager<ApplicationUser> _userManager;

    public FirebaseIdentityService(
        IConfiguration configuration,
        UserManager<ApplicationUser> userManager)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));

        var projectId = configuration["Firebase:ProjectId"]?.Trim();
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException("Firebase:ProjectId must be configured.");
        }

        FirebaseApp app;
        lock (AppLock) {
        app = FirebaseApp.GetInstance(FirebaseAppName)
            ?? FirebaseApp.Create(new AppOptions
            {
                Credential = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST")) ? GoogleCredential.GetApplicationDefault() : GoogleCredential.FromAccessToken("owner"),
                ProjectId = projectId
            }, FirebaseAppName);
        }

        _firebaseAuth = FirebaseAuth.GetAuth(app);
    }

    /// <summary>
    /// Mints a Firebase custom token with the unchanged ASP.NET Identity user ID as its UID.
    /// Claims contain only server-derived roles and a server-derived primary role; protected
    /// actions still call <see cref="VerifyAsync"/> and re-read Identity roles.
    /// </summary>
    public async Task<string> MintAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.Id))
        {
            throw new InvalidOperationException("An Identity user must have an ID before a Firebase token can be minted.");
        }

        var roles = await GetTrustedRolesAsync(user);
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["roles"] = roles.ToArray(),
            ["role"] = GetPrimaryRole(roles)
        };

        return await _firebaseAuth.CreateCustomTokenAsync(user.Id, claims);
    }

    /// <summary>
    /// Verifies a Firebase ID token and reconstructs a principal from the legacy Identity user.
    /// Token custom claims, including any client-provided role data, are intentionally ignored.
    /// </summary>
    public async Task<ClaimsPrincipal> VerifyAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedAccessException("A Firebase ID token is required.");
        }

        var firebaseToken = await _firebaseAuth.VerifyIdTokenAsync(token, checkRevoked: true);
        var user = await _userManager.FindByIdAsync(firebaseToken.Uid);
        if (user is null || await _userManager.IsLockedOutAsync(user))
        {
            throw new UnauthorizedAccessException("The Firebase account is not authorized by Bricks And Books.");
        }

        var roles = await GetTrustedRolesAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("firebase_uid", firebaseToken.Uid)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Firebase"));
    }

    /// <summary>
    /// Returns a response-safe profile built exclusively from the Identity user and its assigned
    /// legacy roles. It deliberately omits password hashes, stamps, security data, and claims.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object>> GetPublicProfileAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var roles = await GetTrustedRolesAsync(user);

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["uid"] = user.Id,
            ["email"] = user.Email ?? string.Empty,
            ["userName"] = user.UserName ?? string.Empty,
            ["firstName"] = user.FirstName ?? string.Empty,
            ["lastName"] = user.LastName ?? string.Empty,
            ["studentNumber"] = user.StudentNumber ?? string.Empty,
            ["employeeNumber"] = user.EmployeeNumber ?? string.Empty,
            ["isStudent"] = roles.Contains("Student", StringComparer.Ordinal),
            ["isStaff"] = roles.Contains("Staff", StringComparer.Ordinal),
            ["isMaintenanceWorker"] = roles.Contains("MaintenanceWorker", StringComparer.Ordinal),
            ["isFinancialOfficer"] = roles.Contains("FinancialOfficer", StringComparer.Ordinal),
            ["isAdmin"] = roles.Contains("Admin", StringComparer.Ordinal),
            ["roles"] = roles.ToArray(),
            ["role"] = GetPrimaryRole(roles)
        };
    }

    private async Task<IReadOnlyList<string>> GetTrustedRolesAsync(ApplicationUser user)
    {
        var assignedRoles = await _userManager.GetRolesAsync(user);
        return assignedRoles
            .Where(role => TrustedRoles.Contains(role))
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetPrimaryRole(IEnumerable<string> roles)
    {
        var roleSet = roles.ToHashSet(StringComparer.Ordinal);
        if (roleSet.Contains("Admin")) return "admin";
        if (roleSet.Contains("MaintenanceWorker")) return "maintenanceWorker";
        if (roleSet.Contains("FinancialOfficer")) return "financialOfficer";
        if (roleSet.Contains("Staff")) return "staff";
        return "student";
    }
}
