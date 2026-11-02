using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using LmsCompanion.Infrastructure;
using LmsCompanion.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LmsCompanion.IntegrationTests;

public sealed class CredentialVaultTests
{
    [Fact]
    public async Task Credential_payload_is_encrypted_and_retrieval_is_owner_scoped()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), "lms-companion-test-keys", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyPath);
        try
        {
            var services = new ServiceCollection();
            services.AddDataProtection()
                .SetApplicationName("LmsCompanion.Tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
            using var provider = services.BuildServiceProvider();
            var options = new DbContextOptionsBuilder<CompanionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var database = new CompanionDbContext(options);
            var vault = new DataProtectionCredentialVault(database, provider.GetRequiredService<IDataProtectionProvider>(), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var reference = await vault.StoreAsync(tenantId, userId, "canvas-access-secret", "canvas-refresh-secret", CancellationToken.None);
            var saved = await database.LmsCredentials.SingleAsync();

            Assert.DoesNotContain("canvas-access-secret", saved.ProtectedPayload, StringComparison.Ordinal);
            Assert.DoesNotContain("canvas-refresh-secret", saved.ProtectedPayload, StringComparison.Ordinal);
            Assert.Equal(new ProviderCredential("canvas-access-secret", "canvas-refresh-secret"),
                await vault.RetrieveAsync(reference, tenantId, userId, CancellationToken.None));
            Assert.Null(await vault.RetrieveAsync(reference, tenantId, Guid.NewGuid(), CancellationToken.None));
            Assert.Null(await vault.RetrieveAsync(reference, Guid.NewGuid(), userId, CancellationToken.None));

            await vault.ReplaceAsync(reference, tenantId, userId, "refreshed-access-secret", "canvas-refresh-secret", CancellationToken.None);
            var replaced = await database.LmsCredentials.SingleAsync();
            Assert.DoesNotContain("refreshed-access-secret", replaced.ProtectedPayload, StringComparison.Ordinal);
            Assert.Equal(new ProviderCredential("refreshed-access-secret", "canvas-refresh-secret"),
                await vault.RetrieveAsync(reference, tenantId, userId, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(keyPath, recursive: true);
        }
    }
}
