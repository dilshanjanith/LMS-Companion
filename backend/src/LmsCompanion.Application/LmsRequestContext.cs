using LmsCompanion.Domain;

namespace LmsCompanion.Application;

/// <summary>Server-created provider context, constructed only after connection ownership is checked.</summary>
public sealed record LmsRequestContext(
    Guid AuthenticatedUserId,
    Guid TenantId,
    Guid ConnectionId,
    LmsProviderType ProviderType,
    Uri ProviderBaseUri,
    string CredentialReference);
