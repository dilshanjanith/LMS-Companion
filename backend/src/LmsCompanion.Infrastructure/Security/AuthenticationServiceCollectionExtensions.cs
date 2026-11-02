using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LmsCompanion.Infrastructure.Security;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddLmsBearerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"];
        var audience = configuration["Authentication:Audience"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri)
            || authorityUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException(
                "Configure Authentication:Authority as an HTTPS OIDC issuer and set Authentication:Audience.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authorityUri.AbsoluteUri;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasLmsReadScope(context.User))
            .Build();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(policy)
            .SetFallbackPolicy(policy);

        return services;
    }

    /// <summary>
    /// Auth0 SPA tokens put <c>read:lms</c> on the space-delimited scope claim.
    /// Third-party CIMD access tokens are often issued for this API audience with an empty scope list.
    /// </summary>
    internal static bool HasLmsReadScope(ClaimsPrincipal user)
    {
        var granted = user.FindAll("scope")
            .Concat(user.FindAll("scp"))
            .Concat(user.FindAll("permissions"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(value => value is not ("openid" or "profile" or "email" or "offline_access"))
            .ToArray();
        return granted.Length == 0 || granted.Contains("read:lms", StringComparer.Ordinal);
    }
}
