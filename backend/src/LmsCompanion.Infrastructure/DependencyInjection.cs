using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using LmsCompanion.Infrastructure.Canvas;
using LmsCompanion.Infrastructure.Persistence;
using LmsCompanion.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;

namespace LmsCompanion.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LmsCompanion");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'LmsCompanion' is required. Configure it with user-secrets or the ConnectionStrings__LmsCompanion environment variable.");
        }

        services.AddDbContext<CompanionDbContext>(options => options.UseNpgsql(connectionString));

        var dataProtectionBuilder = services.AddDataProtection().SetApplicationName("LmsCompanion");
        var keyDirectory = configuration["DataProtection:KeyDirectory"];
        var certificatePath = configuration["DataProtection:CertificatePath"];
        var certificatePassword = configuration["DataProtection:CertificatePassword"];
        if (!string.IsNullOrWhiteSpace(keyDirectory) || !string.IsNullOrWhiteSpace(certificatePath))
        {
            if (string.IsNullOrWhiteSpace(keyDirectory) || string.IsNullOrWhiteSpace(certificatePath))
            {
                throw new InvalidOperationException(
                    "Configure both DataProtection:KeyDirectory and DataProtection:CertificatePath to persist and encrypt the key ring.");
            }

            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                string.IsNullOrWhiteSpace(certificatePassword) ? null : certificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException("The Data Protection certificate must include a private key.");
            }

            services.AddSingleton(certificate);
            dataProtectionBuilder
                .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
                .ProtectKeysWithCertificate(certificate);
        }
        services.AddHttpClient<ICanvasOAuthClient, CanvasOAuthClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                CanvasHttpClientDefaults.Configure(client);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.AddHttpClient("CanvasPersonalTokenValidation", client =>
            { client.Timeout = TimeSpan.FromSeconds(15); CanvasHttpClientDefaults.Configure(client); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<ICanvasInstitutionCatalog, CanvasInstitutionCatalog>();
        services.AddSingleton<IPersonalTokenPolicy, PersonalTokenPolicy>();
        services.AddScoped<IPersonalCanvasConnectionService, PersonalCanvasConnectionService>();
        services.AddScoped<ILmsConnectionRepository, EfLmsConnectionRepository>();
        services.AddScoped<ITenantMembershipRepository, EfTenantMembershipRepository>();
        services.AddScoped<IAuditEventRepository, EfAuditEventRepository>();
        services.AddScoped<ICredentialVault, DataProtectionCredentialVault>();
        services.AddScoped<ICanvasOAuthStateRepository, EfCanvasOAuthStateRepository>();

        var providerName = configuration["Lms:Provider"];
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException("Lms:Provider must be explicitly configured.");
        }

        if (!Enum.TryParse<LmsProviderType>(providerName, ignoreCase: true, out var providerType))
        {
            throw new InvalidOperationException($"LMS provider '{providerName}' is not configured.");
        }

        var mockEnabled = bool.TryParse(configuration["Lms:AllowMockProvider"], out var allowMockProvider)
            && allowMockProvider;
        if (providerType == LmsProviderType.Mock && !mockEnabled)
        {
            throw new InvalidOperationException("The mock provider must be explicitly enabled for local development.");
        }

        if (providerType is not (LmsProviderType.Mock or LmsProviderType.Canvas))
        {
            throw new InvalidOperationException($"LMS provider '{providerType}' is not implemented yet.");
        }

        if (providerType == LmsProviderType.Mock && mockEnabled)
        {
            services.AddSingleton<ILmsProvider, Mock.MockLmsProvider>();
        }

        if (providerType == LmsProviderType.Canvas)
        {
            services.AddHttpClient<ILmsProvider, CanvasLmsProvider>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    CanvasHttpClientDefaults.Configure(client);
                })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                });
        }

        return services;
    }
}
