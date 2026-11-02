namespace LmsCompanion.Domain;

public sealed record TenantMembership(Guid Id, Guid UserId, Guid TenantId, string Role, bool IsActive);
