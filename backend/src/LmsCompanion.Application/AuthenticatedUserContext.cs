namespace LmsCompanion.Application;

/// <summary>Identity established by the authenticated host, never by LMS tool arguments.</summary>
public sealed record AuthenticatedUserContext(Guid UserId, Guid TenantId);
