namespace LmsCompanion.Domain;

/// <summary>Encrypted provider-token payload owned by one user in one tenant.</summary>
public sealed record LmsCredential(
    string Reference,
    Guid TenantId,
    Guid UserId,
    string ProtectedPayload,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
