using LmsCompanion.Infrastructure;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LmsCompanion.IntegrationTests;

public sealed class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registers_postgres_context()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LmsCompanion"] = "Host=localhost;Database=lms_companion;Username=test;Password=test",
                ["Lms:Provider"] = "Mock",
                ["Lms:AllowMockProvider"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<CompanionDbContext>().Database;

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", database.ProviderName);
    }

    [Fact]
    public void AddInfrastructure_rejects_missing_connection_string()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(new ConfigurationBuilder().Build()));

        Assert.Contains("ConnectionStrings__LmsCompanion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_requires_explicit_opt_in_for_mock_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LmsCompanion"] = "Host=localhost;Database=lms_companion;Username=test;Password=test",
                ["Lms:Provider"] = "Mock"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("explicitly enabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_registers_the_canvas_provider_when_selected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LmsCompanion"] = "Host=localhost;Database=lms_companion;Username=test;Password=test",
                ["Lms:Provider"] = "Canvas"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        Assert.Equal(LmsProviderType.Canvas, provider.GetRequiredService<ILmsProvider>().ProviderType);
    }
}
