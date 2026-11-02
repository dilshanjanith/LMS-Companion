using System.Security.Cryptography;
using System.Text.Json;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace LmsCompanion.Infrastructure.Security;

public sealed class DataProtectionCredentialVault(
    CompanionDbContext database,
    IDataProtectionProvider protectionProvider,
    TimeProvider timeProvider) : ICredentialVault
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("LmsCompanion.ProviderCredentials.v1");

    public async Task<string> StoreAsync(
        Guid tenantId,
        Guid userId,
        string accessToken,
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var now = timeProvider.GetUtcNow();
        var reference = Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(new ProviderCredential(accessToken, refreshToken));
        var protectedPayload = _protector.Protect(json);

        database.LmsCredentials.Add(new LmsCredential(
            reference,
            tenantId,
            userId,
            protectedPayload,
            now,
            now));
        await database.SaveChangesAsync(cancellationToken);
        return reference;
    }

    public async Task<ProviderCredential?> RetrieveAsync(
        string reference,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var credential = await database.LmsCredentials.AsNoTracking().SingleOrDefaultAsync(
            item => item.Reference == reference
                && item.TenantId == tenantId
                && item.UserId == userId,
            cancellationToken);
        if (credential is null)
        {
            return null;
        }

        try
        {
            var json = _protector.Unprotect(credential.ProtectedPayload);
            return JsonSerializer.Deserialize<ProviderCredential>(json);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("The stored LMS credential could not be decrypted.", exception);
        }
    }

    public async Task ReplaceAsync(
        string reference,
        Guid tenantId,
        Guid userId,
        string accessToken,
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var credential = await database.LmsCredentials.SingleOrDefaultAsync(
            item => item.Reference == reference
                && item.TenantId == tenantId
                && item.UserId == userId,
            cancellationToken)
            ?? throw new InvalidOperationException("The stored LMS credential no longer exists.");

        var updatedCredential = credential with
        {
            ProtectedPayload = _protector.Protect(
                JsonSerializer.Serialize(new ProviderCredential(accessToken, refreshToken))),
            UpdatedAt = timeProvider.GetUtcNow()
        };
        database.Entry(credential).CurrentValues.SetValues(updatedCredential);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        string reference,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var credential = await database.LmsCredentials.SingleOrDefaultAsync(
            item => item.Reference == reference
                && item.TenantId == tenantId
                && item.UserId == userId,
            cancellationToken);
        if (credential is null)
        {
            return;
        }

        database.LmsCredentials.Remove(credential);
        await database.SaveChangesAsync(cancellationToken);
    }
}
