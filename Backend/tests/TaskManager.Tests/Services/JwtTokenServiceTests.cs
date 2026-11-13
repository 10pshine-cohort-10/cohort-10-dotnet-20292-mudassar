using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TaskManager.Api.Entities;
using TaskManager.Api.Services;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Services;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = TestEnv.JwtIssuer,
        Audience = TestEnv.JwtAudience,
        Key = TestEnv.JwtKey,
        ExpiryMinutes = TestEnv.JwtExpiryMinutes
    };

    private static readonly ApplicationUser User = new()
    {
        Id = Guid.NewGuid(),
        Email = TestEnv.UserEmail,
        FullName = TestEnv.UserName
    };

    private static JwtSecurityToken Read(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ReadJwtToken(token);
    }

    [Fact]
    public void Create_ExpiresInConfiguredMinutes()
    {
        var sut = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options));

        var (token, expiresAt) = sut.Create(User, [Roles.User]);
        var jwt = Read(token);

        var expected = DateTime.UtcNow.AddMinutes(Options.ExpiryMinutes);
        Assert.InRange(jwt.ValidTo, expected.AddSeconds(-10), expected.AddSeconds(10));
        Assert.InRange(expiresAt, expected.AddSeconds(-10), expected.AddSeconds(10));
    }

    [Fact]
    public void Create_ContainsIdentityAndRoleClaims()
    {
        var sut = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options));

        var (token, _) = sut.Create(User, [Roles.Admin, Roles.User]);
        var jwt = Read(token);

        Assert.Equal(TestEnv.JwtIssuer, jwt.Issuer);
        Assert.Contains(TestEnv.JwtAudience, jwt.Audiences);
        Assert.Equal(User.Id.ToString(), jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(User.Email, jwt.Claims.Single(c => c.Type == ClaimTypes.Email).Value);
        var roles = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains(Roles.Admin, roles);
        Assert.Contains(Roles.User, roles);
        Assert.Single(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);
    }
}
