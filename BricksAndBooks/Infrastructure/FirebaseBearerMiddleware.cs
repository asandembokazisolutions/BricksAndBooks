using BricksAndBooks.Infrastructure;
using FirebaseAdmin.Auth;

namespace BricksAndBooks.Infrastructure;

public sealed class FirebaseBearerMiddleware
{
    private readonly RequestDelegate _next;
    public FirebaseBearerMiddleware(RequestDelegate next) => _next = next;
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/firebase") || context.Request.Path == "/api/firebase/session" || context.Request.Path == "/api/firebase/public" || context.Request.Path == "/api/firebase/health")
        {
            await _next(context);
            return;
        }
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Firebase sign-in is required." });
            return;
        }
        try
        {
            var service = context.RequestServices.GetRequiredService<FirebaseIdentityService>();
            context.User = await service.VerifyAsync(header[7..].Trim());
            context.Items["FirebaseVerified"] = true;
        }
        catch (Exception exception) when (exception is FirebaseAuthException or UnauthorizedAccessException or ArgumentException)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "The Firebase session is invalid or expired. Sign in again." });
            return;
        }
        await _next(context);
    }
}
