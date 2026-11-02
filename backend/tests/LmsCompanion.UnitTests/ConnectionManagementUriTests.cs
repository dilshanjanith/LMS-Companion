using LmsCompanion.Mcp;
using Microsoft.Extensions.Configuration;

namespace LmsCompanion.UnitTests;

public sealed class ConnectionManagementUriTests
{
    [Fact]
    public void Https_portal_url_is_returned()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:ConnectionManagementUri"] = "https://portal.example.test/dashboard"
            })
            .Build();

        Assert.Equal("https://portal.example.test/dashboard", ConnectionManagementUri.Resolve(configuration));
    }

    [Fact]
    public void Localhost_http_portal_is_allowed_when_flag_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:ConnectionManagementUri"] = "http://localhost:4200/dashboard",
                ["Frontend:AllowLocalhostManagementUri"] = "true"
            })
            .Build();

        Assert.Equal("http://localhost:4200/dashboard", ConnectionManagementUri.Resolve(configuration));
    }

    [Fact]
    public void Localhost_http_portal_is_rejected_without_flag()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:ConnectionManagementUri"] = "http://localhost:4200/dashboard"
            })
            .Build();

        Assert.Null(ConnectionManagementUri.Resolve(configuration));
    }
}
