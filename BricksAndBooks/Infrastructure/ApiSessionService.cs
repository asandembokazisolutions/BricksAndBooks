using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BricksAndBooks.Infrastructure;

public sealed class ApiSessionService
{
    private sealed record Session(string UserId, DateTime ExpiresAt);
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public string Create(string userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        _sessions[token] = new Session(userId, DateTime.UtcNow.AddDays(14));
        return token;
    }

    public bool TryGetUserId(string token, out string userId)
    {
        userId = string.Empty;
        if (!_sessions.TryGetValue(token, out var session)) return false;
        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }
        userId = session.UserId;
        return true;
    }

    public void Revoke(string token) => _sessions.TryRemove(token, out _);
}
