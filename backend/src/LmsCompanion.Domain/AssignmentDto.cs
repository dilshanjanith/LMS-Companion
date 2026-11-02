namespace LmsCompanion.Domain;

public sealed record AssignmentDto(
    string Id,
    string CourseId,
    string Title,
    DateTimeOffset? DueAt,
    string? Status,
    decimal? PointsPossible,
    string? Url);
