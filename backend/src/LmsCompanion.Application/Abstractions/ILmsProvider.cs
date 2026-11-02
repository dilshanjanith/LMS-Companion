using LmsCompanion.Domain;

namespace LmsCompanion.Application.Abstractions;

/// <summary>Read-only provider boundary implemented by each LMS adapter.</summary>
public interface ILmsProvider
{
    LmsProviderType ProviderType { get; }

    Task<IReadOnlyList<CourseModuleDto>> GetCourseModulesAsync(LmsRequestContext context, string courseId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This LMS does not provide module content.");
    Task<IReadOnlyList<ModuleItemDto>> GetModuleItemsAsync(LmsRequestContext context, string courseId, string moduleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This LMS does not provide module content.");
    Task<LearningMaterialDto> GetLearningMaterialAsync(LmsRequestContext context, string courseId, string moduleId, string itemId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This LMS does not provide module content.");

    Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(
        LmsRequestContext context,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(
        LmsRequestContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(
        LmsRequestContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<CourseProgressDto> GetCourseProgressAsync(
        LmsRequestContext context,
        string courseId,
        CancellationToken cancellationToken);
}
