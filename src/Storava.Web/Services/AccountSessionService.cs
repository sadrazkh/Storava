using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Storava.Web.Data;

namespace Storava.Web.Services;

public interface IAccountSessionService
{
    Task<AccountSession> CreateAsync(
        ApplicationUser user,
        string? userAgent,
        bool isPersistent,
        CancellationToken cancellationToken);

    Task<bool> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountSession>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task RevokeCurrentAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);

    Guid? GetCurrentSessionId(ClaimsPrincipal principal);
}

public sealed class AccountSessionService(
    ApplicationDbContext database,
    TimeProvider timeProvider) : IAccountSessionService
{
    public const string SessionIdClaim = "storava:session_id";

    public async Task<AccountSession> CreateAsync(
        ApplicationUser user,
        string? userAgent,
        bool isPersistent,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var session = new AccountSession
        {
            UserId = user.Id,
            ClientLabel = DescribeClient(userAgent),
            IsPersistent = isPersistent,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            ExpiresAtUtc = now.Add(isPersistent ? TimeSpan.FromDays(30) : TimeSpan.FromHours(12))
        };

        database.AccountSessions.Add(session);
        var cleanupCutoff = now.AddDays(-30);
        var cleanupCandidates = await database.AccountSessions
            .Where(candidate => candidate.UserId == user.Id)
            .OrderBy(candidate => candidate.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        database.AccountSessions.RemoveRange(cleanupCandidates.Where(candidate =>
            (candidate.ExpiresAtUtc < now || candidate.RevokedAtUtc is not null)
            && candidate.CreatedAtUtc < cleanupCutoff));
        await database.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<bool> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var sessionId = GetCurrentSessionId(principal);
        var userId = UserId(principal);
        if (sessionId is null || userId is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var session = await database.AccountSessions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == sessionId && candidate.UserId == userId,
                cancellationToken);

        if (session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now)
        {
            return false;
        }

        if (now - session.LastSeenAtUtc >= TimeSpan.FromMinutes(5))
        {
            session.LastSeenAtUtc = now;
            await database.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<IReadOnlyList<AccountSession>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var sessions = await database.AccountSessions
            .AsNoTracking()
            .Where(session =>
                session.UserId == userId &&
                session.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        return sessions
            .Where(session => session.ExpiresAtUtc > now)
            .OrderByDescending(session => session.LastSeenAtUtc)
            .ToList();
    }

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await database.AccountSessions
            .SingleOrDefaultAsync(
                candidate => candidate.Id == sessionId && candidate.UserId == userId,
                cancellationToken);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return false;
        }

        session.RevokedAtUtc = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RevokeCurrentAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var sessionId = GetCurrentSessionId(principal);
        var userId = UserId(principal);
        if (sessionId is not null && userId is not null)
        {
            await RevokeAsync(userId.Value, sessionId.Value, cancellationToken);
        }
    }

    public Guid? GetCurrentSessionId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(SessionIdClaim);
        return Guid.TryParse(value, out var sessionId) ? sessionId : null;
    }

    private static Guid? UserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string DescribeClient(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown browser";
        }

        var browser = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge"
            : userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "Browser";
        var platform = userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iOS"
            : "device";
        return $"{browser} on {platform}";
    }
}
