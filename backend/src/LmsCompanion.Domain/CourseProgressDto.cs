namespace LmsCompanion.Domain;

public sealed record CourseProgressDto(
    string CourseId,
    decimal? CompletionPercent,
    int? CompletedItems,
    int? TotalItems,
    string? Explanation);
