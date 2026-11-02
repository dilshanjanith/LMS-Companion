namespace LmsCompanion.Domain;

public sealed record LmsConnection(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    LmsProviderType Provider,
    string BaseUrl,
    string? CredentialReference,
    string[] Scopes,
    LmsConnectionStatus Status,
    DateTimeOffset? ExpiresAt);
