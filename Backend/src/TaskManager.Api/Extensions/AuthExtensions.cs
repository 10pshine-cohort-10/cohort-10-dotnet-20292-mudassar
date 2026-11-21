using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using TaskManager.Api.Data;
using TaskManager.Api.Entities;
using TaskManager.Api.Services;

namespace TaskManager.Api.Extensions;

public static class AuthExtensions
{
    public const string AdminOnlyPolicy = "AdminOnly";
    public const string AuthRateLimitPolicy = "auth";

    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Key must be set (user-secrets) and be at least 32 characters.");

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = true;
                o.User.RequireUniqueEmail = true;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = true;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var userIdStr = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (!Guid.TryParse(userIdStr, out var userId))
                        {
                            ctx.Fail("Invalid user identifier in token.");
                            return;
                        }

                        var userManager = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                        var user = await userManager.FindByIdAsync(userId.ToString());
                        if (user is null || !user.IsActive)
                        {
                            ctx.Fail("User account is inactive or no longer exists.");
                            return;
                        }

                        var tokenStamp = ctx.Principal?.FindFirstValue(JwtTokenService.SecurityStampClaimType);
                        var currentStamp = user.SecurityStamp ?? string.Empty;
                        if (!string.Equals(tokenStamp, currentStamp, StringComparison.Ordinal))
                        {
                            ctx.Fail("Security stamp mismatch; token has been revoked.");
                            return;
                        }

                        if (ctx.Principal?.Identity is ClaimsIdentity identity)
                        {
                            foreach (var existingRole in identity.FindAll(ClaimTypes.Role).ToList())
                                identity.RemoveClaim(existingRole);

                            var currentRoles = await userManager.GetRolesAsync(user);
                            foreach (var role in currentRoles)
                                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                        }
                    },
                    OnChallenge = async ctx =>                     // 401 -> ProblemDetails
                    {
                        ctx.HandleResponse();
                        await ProblemWriter.WriteAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized,
                            "Missing, invalid or expired token.");
                    },
                    OnForbidden = ctx =>                           // 403 -> ProblemDetails
                        ProblemWriter.WriteAsync(ctx.HttpContext, StatusCodes.Status403Forbidden,
                            "You do not have permission to perform this action.")
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminOnlyPolicy, p => p.RequireRole(Roles.Admin))
            // Fallback: every endpoint requires login unless marked [AllowAnonymous]
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            var permitLimit = config.GetValue("RateLimiting:AuthPermitLimit", 10);
            o.AddPolicy(AuthRateLimitPolicy, httpContext =>
            {
                var clientKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: clientKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });
        });

        return services;
    }
}
