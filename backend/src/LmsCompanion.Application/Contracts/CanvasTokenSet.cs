namespace LmsCompanion.Application.Contracts;

/// <summary>Short-lived OAuth result. This type must never be serialized to a client.</summary>
public sealed record CanvasTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string[] Scopes);
