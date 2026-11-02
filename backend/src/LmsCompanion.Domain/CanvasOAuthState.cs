namespace LmsCompanion.Domain;

/// <summary>Single-use, hashed Canvas OAuth state bound to an authenticated tenant member.</summary>
public sealed record CanvasOAuthState(
    string StateHash,
    Guid TenantId,
    Guid UserId,
    string InstitutionId,
    string BaseUrl,
    string RedirectUri,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
