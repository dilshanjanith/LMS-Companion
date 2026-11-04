namespace LmsCompanion.Domain;

public sealed record CourseModuleDto(string Id, string CourseId, string Name, string? Url);
public sealed record ModuleItemDto(string Id, string ModuleId, string CourseId, string Title, string Type, string? Url);
public sealed record LearningMaterialDto(string CourseId, string ModuleId, string ItemId, string Title,
    string Content, string? Url, DateTimeOffset RetrievedAt);

public sealed record CoursePageDto(string CourseId, string PageSlug, string Title, string Content,
    string Url, IReadOnlyList<CoursePageResourceDto> Resources, bool ContentTruncated,
    bool ResourcesTruncated, string ContentScope, DateTimeOffset RetrievedAt);

public sealed record CoursePageResourceDto(string Title, string Url, string Type, string? PageSlug);
public sealed record LinkedResourceDto(string PageUrl, string Url, string Title, string Status,
    string? Content, string? Detail, bool Truncated, int? NextOffset, DateTimeOffset RetrievedAt);
