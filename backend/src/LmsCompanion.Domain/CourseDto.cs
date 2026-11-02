namespace LmsCompanion.Domain;

public sealed record CourseDto(
    string Id,
    string Provider,
    string Name,
    string? Code,
    string? Term,
    string? Role,
    string? Url);
