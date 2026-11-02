namespace LmsCompanion.Application.Contracts;

/// <summary>Public, tenant-scoped institution information. Never includes OAuth credentials.</summary>
public sealed record InstitutionSummaryDto(
    string Id, string Name, string Provider, string BaseUrl, bool CanConnect);
