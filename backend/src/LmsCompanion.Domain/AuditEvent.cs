namespace LmsCompanion.Domain;

/// <summary>Safe audit metadata. This record intentionally has no credential or course-content fields.</summary>
public sealed record AuditEvent(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string EventType,
    string? SubjectId,
    DateTimeOffset OccurredAt);
