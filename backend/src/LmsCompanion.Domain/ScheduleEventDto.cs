namespace LmsCompanion.Domain;

public sealed record ScheduleEventDto(
    string Id,
    string? CourseId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string? Location,
    string? Url);
