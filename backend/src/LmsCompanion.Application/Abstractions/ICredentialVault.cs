namespace LmsCompanion.Application.Abstractions;

/// <summary>Stores and retrieves provider tokens using an opaque reference scoped to an owner.</summary>
public interface ICredentialVault
{
    Task<string> StoreAsync(
        Guid tenantId,
        Guid userId,
        string accessToken,
        string? refreshToken,
        CancellationToken cancellationToken);

    Task<ProviderCredential?> RetrieveAsync(
        string reference,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken);

    Task ReplaceAsync(
        string reference,
        Guid tenantId,
        Guid userId,
        string accessToken,
        string? refreshToken,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string reference,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record ProviderCredential(string AccessToken, string? RefreshToken);
