using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Infrastructure.Mock;

/// <summary>Deterministic, read-only sample data for local development and tests.</summary>
public sealed class MockLmsProvider : ILmsProvider
{
    private static readonly CourseDto[] Courses =
    [
        new("course-101", "Mock", "Introduction to Computer Science", "CS101", "Fall 2026", "Student", "https://mock.lms.invalid/courses/101"),
        new("course-205", "Mock", "Environmental Systems", "ENV205", "Fall 2026", "Student", "https://mock.lms.invalid/courses/205")
    ];

    private static readonly IReadOnlyDictionary<string, CourseProgressDto> Progress =
        new Dictionary<string, CourseProgressDto>(StringComparer.Ordinal)
        {
            ["course-101"] = new("course-101", 72.5m, 8, 11, "Based on completed required module items reported by the mock provider."),
            ["course-205"] = new("course-205", null, null, null, "This provider does not expose a reliable course completion metric.")
        };

    private readonly AssignmentDto[] _assignments;
    private readonly ScheduleEventDto[] _schedule;

    public MockLmsProvider()
    {
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        _assignments =
        [
            new("assignment-101-1", "course-101", "Reading reflection", today.AddDays(2).AddHours(23), "Not submitted", 20m, "https://mock.lms.invalid/courses/101/assignments/1"),
            new("assignment-101-2", "course-101", "Programming exercise", today.AddDays(6).AddHours(23), "Not submitted", 50m, "https://mock.lms.invalid/courses/101/assignments/2"),
            new("assignment-205-1", "course-205", "Field notes", today.AddDays(10).AddHours(16), "Submitted", 30m, "https://mock.lms.invalid/courses/205/assignments/1"),
            new("assignment-101-old", "course-101", "Previous quiz", today.AddDays(-3).AddHours(23), "Graded", 10m, "https://mock.lms.invalid/courses/101/assignments/3"),
            new("assignment-205-no-date", "course-205", "Optional reading", null, null, null, "https://mock.lms.invalid/courses/205/assignments/2")
        ];

        _schedule =
        [
            new("event-cs-lecture", "course-101", "Computer Science lecture", today.AddDays(1).AddHours(9), today.AddDays(1).AddHours(10), "Science Hall 204", "https://mock.lms.invalid/calendar/events/1"),
            new("event-env-lab", "course-205", "Environmental systems lab", today.AddDays(3).AddHours(13), today.AddDays(3).AddHours(15), "Field Laboratory", "https://mock.lms.invalid/calendar/events/2")
        ];
    }

    public LmsProviderType ProviderType => LmsProviderType.Mock;

    public Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(
        LmsRequestContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMockConnection(context);
        return Task.FromResult<IReadOnlyList<CourseDto>>(Courses);
    }

    public Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(
        LmsRequestContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMockConnection(context);
        IReadOnlyList<AssignmentDto> assignments = _assignments
            .Where(assignment => assignment.DueAt is { } dueAt && dueAt >= from && dueAt < to)
            .ToArray();
        return Task.FromResult(assignments);
    }

    public Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(
        LmsRequestContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMockConnection(context);
        IReadOnlyList<ScheduleEventDto> events = _schedule
            .Where(scheduleEvent => scheduleEvent.StartsAt < to
                && (scheduleEvent.EndsAt ?? scheduleEvent.StartsAt) >= from)
            .ToArray();
        return Task.FromResult(events);
    }

    public Task<CourseProgressDto> GetCourseProgressAsync(
        LmsRequestContext context,
        string courseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMockConnection(context);

        if (!Progress.TryGetValue(courseId, out var progress))
        {
            throw new LmsProviderException(LmsProviderFailureCode.NotFound, "The requested course is not available.");
        }

        return Task.FromResult(progress);
    }

    private static void EnsureMockConnection(LmsRequestContext context)
    {
        if (context.ProviderType != LmsProviderType.Mock
            || context.ConnectionId != MockLmsIdentity.ConnectionId
            || context.AuthenticatedUserId != MockLmsIdentity.UserId
            || context.TenantId != MockLmsIdentity.TenantId)
        {
            throw new LmsProviderException(
                LmsProviderFailureCode.Unauthorized,
                "The mock LMS connection is not available to this user.");
        }
    }
}
