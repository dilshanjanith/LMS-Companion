namespace LmsCompanion.Domain;

public sealed record CourseModuleDto(string Id, string CourseId, string Name, string? Url);
public sealed record ModuleItemDto(string Id, string ModuleId, string CourseId, string Title, string Type, string? Url);
public sealed record LearningMaterialDto(string CourseId, string ModuleId, string ItemId, string Title,
    string Content, string? Url, DateTimeOffset RetrievedAt);
