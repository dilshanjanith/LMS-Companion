using LmsCompanion.Domain;

namespace LmsCompanion.Application.Contracts;

/// <summary>Connection metadata safe to return to an app client; credential references are excluded.</summary>
public sealed record ConnectionSummaryDto(
    Guid Id,
    LmsProviderType Provider,
    string BaseUrl,
    LmsConnectionStatus Status,
    DateTimeOffset? ExpiresAt);
