using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Api;

namespace SRXPanel.Services.Developer;

public record TerminalTicket(string Token, DateTime ExpiresAt);

public interface ITerminalService
{
    /// <summary>Maximum simultaneous terminals per user.</summary>
    const int MaxConcurrentSessions = 2;

    /// <summary>Mints a five-minute connection token for the WebSocket endpoint.</summary>
    Task<TerminalTicket> CreateTicketAsync(string userId);

    /// <summary>Validates a connection token and returns the owning user id.</summary>
    (bool ok, string? userId, string? tokenId, string? error) ValidateTicket(string token);

    Task<int> OpenSessionAsync(string userId, string tokenId, string ip, string? userAgent);
    Task CloseSessionAsync(int sessionId);
    Task TouchAsync(int sessionId, int commandsRun);

    /// <summary>True when an admin has asked for this session to be killed.</summary>
    Task<bool> IsTerminationRequestedAsync(int sessionId);

    Task<int> CountActiveAsync(string userId);

    Task<List<TerminalSession>> GetActiveSessionsAsync();
    Task<List<TerminalSession>> GetSessionHistoryAsync(int limit = 50);
    Task TerminateAsync(int sessionId);

    /// <summary>Marks sessions idle beyond the timeout as ended.</summary>
    Task<int> ReapIdleSessionsAsync(TimeSpan idleTimeout);
}

public class TerminalService : ITerminalService
{
    /// <summary>Inactivity window before the server drops a terminal.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    public TerminalService(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public Task<TerminalTicket> CreateTicketAsync(string userId)
    {
        var expires = DateTime.UtcNow.AddMinutes(5);
        var tokenId = Guid.NewGuid().ToString("N");

        var credentials = new SigningCredentials(JwtTokenService.BuildKey(_config), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: JwtTokenService.Issuer,
            audience: TerminalAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Jti, tokenId)
            },
            expires: expires,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return Task.FromResult(new TerminalTicket(encoded, expires));
    }

    /// <summary>A dedicated audience keeps terminal tickets from being replayed against the REST API.</summary>
    public const string TerminalAudience = "SRXPanel.Terminal";

    public (bool ok, string? userId, string? tokenId, string? error) ValidateTicket(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = JwtTokenService.Issuer,
                ValidAudience = TerminalAudience,
                IssuerSigningKey = JwtTokenService.BuildKey(_config),
                ClockSkew = TimeSpan.FromSeconds(10)
            }, out _);

            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                         ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tokenId = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

            if (string.IsNullOrEmpty(userId)) return (false, null, null, "Token has no subject.");
            return (true, userId, tokenId, null);
        }
        catch (SecurityTokenExpiredException)
        {
            return (false, null, null, "This terminal token has expired. Reload the page.");
        }
        catch (Exception)
        {
            return (false, null, null, "Invalid terminal token.");
        }
    }

    public Task<int> CountActiveAsync(string userId) =>
        _db.TerminalSessions.CountAsync(s => s.UserId == userId && s.IsActive);

    public async Task<int> OpenSessionAsync(string userId, string tokenId, string ip, string? userAgent)
    {
        var session = new TerminalSession
        {
            UserId = userId,
            TokenId = tokenId,
            IpAddress = ip,
            UserAgent = userAgent?.Length > 300 ? userAgent[..300] : userAgent,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            IsActive = true
        };
        _db.TerminalSessions.Add(session);
        await _db.SaveChangesAsync();
        return session.Id;
    }

    public async Task CloseSessionAsync(int sessionId)
    {
        var session = await _db.TerminalSessions.FindAsync(sessionId);
        if (session == null || !session.IsActive) return;

        session.IsActive = false;
        session.EndedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task TouchAsync(int sessionId, int commandsRun)
    {
        var session = await _db.TerminalSessions.FindAsync(sessionId);
        if (session == null) return;
        session.LastActivityAt = DateTime.UtcNow;
        session.CommandCount = commandsRun;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> IsTerminationRequestedAsync(int sessionId)
    {
        var session = await _db.TerminalSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId);
        return session is null || session.TerminationRequested || !session.IsActive;
    }

    public Task<List<TerminalSession>> GetActiveSessionsAsync() =>
        _db.TerminalSessions.Include(s => s.User)
            .Where(s => s.IsActive).OrderByDescending(s => s.StartedAt).ToListAsync();

    public Task<List<TerminalSession>> GetSessionHistoryAsync(int limit = 50) =>
        _db.TerminalSessions.Include(s => s.User)
            .OrderByDescending(s => s.StartedAt).Take(limit).ToListAsync();

    public async Task TerminateAsync(int sessionId)
    {
        var session = await _db.TerminalSessions.FindAsync(sessionId);
        if (session == null) return;

        // The socket loop polls this flag and closes itself, then marks the session ended.
        session.TerminationRequested = true;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ReapIdleSessionsAsync(TimeSpan idleTimeout)
    {
        var cutoff = DateTime.UtcNow - idleTimeout;
        var stale = await _db.TerminalSessions.Where(s => s.IsActive && s.LastActivityAt < cutoff).ToListAsync();
        foreach (var session in stale)
        {
            session.IsActive = false;
            session.EndedAt = DateTime.UtcNow;
            session.TerminationRequested = true;
        }
        if (stale.Count > 0) await _db.SaveChangesAsync();
        return stale.Count;
    }
}
