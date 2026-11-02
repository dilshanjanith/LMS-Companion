using LmsCompanion.Domain;

namespace LmsCompanion.Application.Abstractions;

public interface ILmsQueryService
{
    Task<IReadOnlyList<CourseModuleDto>> GetCourseModulesAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    Task<IReadOnlyList<ModuleItemDto>> GetModuleItemsAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, string moduleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    Task<LearningMaterialDto> GetLearningMaterialAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, string moduleId, string itemId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<CourseProgressDto> GetCourseProgressAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        string courseId,
        CancellationToken cancellationToken);
}
