namespace LmsCompanion.Application.Contracts;

/// <summary>Server-side configuration for one approved Canvas institution.</summary>
public sealed record CanvasInstitutionConfiguration(
    string Id,
    Guid TenantId,
    Uri BaseUri,
    string ClientId,
    string ClientSecret,
    Uri RedirectUri,
    IReadOnlyList<string> Scopes);
