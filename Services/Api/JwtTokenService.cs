using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SRXPanel.Models;

namespace SRXPanel.Services.Api;

public interface IJwtTokenService
{
    string CreateToken(ApplicationUser user, int hours = 24);
    TokenValidationParameters ValidationParameters { get; }
}

public class JwtTokenService : IJwtTokenService
{
    public const string Issuer = "SRXPanel";
    public const string Audience = "SRXPanel.Api";
    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(IConfiguration config)
    {
        _key = BuildKey(config);
    }

    /// <summary>Shared key derivation so Program.cs JwtBearer uses the same secret.</summary>
    public static SymmetricSecurityKey BuildKey(IConfiguration config)
    {
        var secret = config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            // Deterministic dev fallback so tokens survive restarts in simulation.
            secret = "srxpanel-dev-signing-key-please-override-in-production-1234567890";
        }
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public string CreateToken(ApplicationUser user, int hours = 24)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("username", user.UserName ?? "")
        };
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            expires: DateTime.UtcNow.AddHours(hours), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = _key
    };
}
