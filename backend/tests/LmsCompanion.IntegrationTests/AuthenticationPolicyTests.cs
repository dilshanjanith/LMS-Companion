using LmsCompanion.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace LmsCompanion.IntegrationTests;

public sealed class AuthenticationPolicyTests
{
    [Theory]
    [InlineData("scope", "openid profile", true)]
    [InlineData("scope", "", true)]
    [InlineData("scope", "offline_access", true)]
    [InlineData("scope", "read:lms-extra", false)]
    [InlineData("scope", "openid read:lms", true)]
    [InlineData("permissions", "read:lms", true)]
    [InlineData("scp", "read:lms offline_access", true)]
    public async Task Default_and_fallback_policies_require_exact_read_scope(
        string claimType, string scopes, bool allowed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://identity.example.test/",
            ["Authentication:Audience"] = "lms-companion-api"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLmsBearerAuthentication(configuration);
        using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, scopes)], "test"));
        foreach (var policy in new[] { await policies.GetDefaultPolicyAsync(), (await policies.GetFallbackPolicyAsync())! })
            Assert.Equal(allowed, (await authorization.AuthorizeAsync(user, null, policy)).Succeeded);
    }

    [Fact]
    public async Task Default_policy_requires_an_authenticated_user()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.test/",
                ["Authentication:Audience"] = "lms-companion-api"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLmsBearerAuthentication(configuration);
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallbackPolicy = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(fallbackPolicy);
        Assert.Contains(fallbackPolicy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData("http://identity.example.test/")]
    [InlineData("")]
    public void Invalid_authority_is_rejected_at_startup(string authority)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = authority,
                ["Authentication:Audience"] = "lms-companion-api"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLmsBearerAuthentication(configuration));

        Assert.Contains("HTTPS OIDC issuer", exception.Message, StringComparison.Ordinal);
    }
}
