using System.Text.Json;
using LmsCompanion.Infrastructure.Canvas;
using Microsoft.Extensions.Configuration;

namespace LmsCompanion.IntegrationTests;

public sealed class CanvasInstitutionCatalogTests
{
    private static readonly Guid Tenant = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void Pending_institution_is_visible_only_to_its_tenant_and_cannot_authorize()
    {
        var catalog = Create(false);
        var item = Assert.Single(catalog.ListForTenant(Tenant));
        Assert.False(item.CanConnect);
        Assert.Empty(catalog.ListForTenant(Guid.NewGuid()));
        Assert.Null(catalog.FindForTenant(item.Id, Tenant));
        Assert.Null(catalog.FindForTenantByBaseUri(new Uri(item.BaseUrl), Tenant));
    }

    [Fact]
    public void Enabled_institution_requires_real_configuration_and_does_not_expose_secrets()
    {
        Assert.Throws<InvalidOperationException>(() => Create(true));
        var catalog = Create(true, new Dictionary<string, string?>
        {
            ["ClientId"] = "test-client", ["ClientSecret"] = "test-secret",
            ["RedirectUri"] = "https://app.example.test/api/v1/connections/canvas/callback",
            ["Scopes:0"] = "url:GET|/api/v1/courses"
        });
        Assert.True(Assert.Single(catalog.ListForTenant(Tenant)).CanConnect);
        Assert.NotNull(catalog.FindForTenant("stanford-seed", Tenant));
        Assert.Null(catalog.FindForTenant("stanford-seed", Guid.NewGuid()));
        var json = JsonSerializer.Serialize(catalog.ListForTenant(Tenant));
        Assert.DoesNotContain("test-secret", json);
        Assert.DoesNotContain("test-client", json);
    }

    [Theory]
    [InlineData("BaseUrl", "http://seedonline.stanford.edu/")]
    [InlineData("BaseUrl", "https://seedonline.stanford.edu/?token=bad")]
    [InlineData("TenantId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("Enabled", "typo")]
    public void Invalid_pending_configuration_fails_closed(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Create(false, new Dictionary<string, string?> { [key] = value }));

    private static CanvasInstitutionCatalog Create(bool enabled, Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Id"] = "stanford-seed", ["Name"] = "Stanford SEED",
            ["TenantId"] = Tenant.ToString(), ["BaseUrl"] = "https://seedonline.stanford.edu/",
            ["Enabled"] = enabled.ToString()
        };
        foreach (var item in overrides ?? []) values[item.Key] = item.Value;
        return new CanvasInstitutionCatalog(new ConfigurationBuilder().AddInMemoryCollection(
            values.ToDictionary(item => "Canvas:Institutions:0:" + item.Key, item => item.Value)).Build());
    }
}
