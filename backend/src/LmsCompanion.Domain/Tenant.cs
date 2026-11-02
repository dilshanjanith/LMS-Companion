namespace LmsCompanion.Domain;

public sealed record Tenant(Guid Id, string Name, string? Configuration = null);
