using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Services;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public string Key { get; set; } = default!;
    public int ExpiryMinutes { get; set; } = 60;
}

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) Create(ApplicationUser user, IList<string> roles);
}

public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    public const string SecurityStampClaimType = "security_stamp";

    private readonly JwtOptions _o = (options ?? throw new ArgumentNullException(nameof(options))).Value
        ?? throw new ArgumentNullException(nameof(options), "JwtOptions value cannot be null.");

    public (string Token, DateTime ExpiresAt) Create(ApplicationUser user, IList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new InvalidOperationException("Cannot issue a JWT token for a user without an email address.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName ?? string.Empty),
            new(SecurityStampClaimType, user.SecurityStamp ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var expires = DateTime.UtcNow.AddMinutes(_o.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _o.Issuer,
            audience: _o.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

