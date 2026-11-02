using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Application.Services;
using LmsCompanion.Domain;

namespace LmsCompanion.UnitTests;

public sealed class LmsQueryServiceTests
{
    private static readonly AuthenticatedUserContext User = new(
        Guid.Parse("6d0c3926-cfc5-45df-a138-f5c4050d6ffb"),
        Guid.Parse("bff5434f-9ac3-4dc6-aa60-10b291e4a8fc"));

    private static readonly Guid ConnectionId = Guid.Parse("1857d3bb-11da-495f-b6dc-b981bb14c177");
    private static readonly DateTimeOffset FromUtc = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Upcoming_assignments_are_filtered_using_utc_boundaries()
    {
        var provider = new TestLmsProvider
        {
            Assignments =
            [
                Assignment("at-start", FromUtc),
                Assignment("inside", FromUtc.AddDays(2)),
                Assignment("at-end", FromUtc.AddDays(5)),
                Assignment("before", FromUtc.AddTicks(-1)),
                Assignment("no-due-date", null)
            ]
        };
        var service = CreateService(provider, Connection(User));
        var fromWithOffset = new DateTimeOffset(2026, 10, 28, 5, 30, 0, TimeSpan.FromHours(5.5));
        var toWithOffset = fromWithOffset.AddDays(5);

        var result = await service.GetUpcomingAssignmentsAsync(
            User,
            ConnectionId,
            fromWithOffset,
            toWithOffset,
            CancellationToken.None);

        Assert.Equal(["at-start", "inside"], result.Select(item => item.Id));
        Assert.Equal(FromUtc, provider.LastFrom);
        Assert.Equal(FromUtc.AddDays(5), provider.LastTo);
    }

    [Fact]
    public async Task Date_range_longer_than_ninety_days_is_rejected_before_provider_call()
    {
        var provider = new TestLmsProvider();
        var service = CreateService(provider, Connection(User));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetUpcomingAssignmentsAsync(
            User,
            ConnectionId,
            FromUtc,
            FromUtc.AddDays(LmsDateRange.MaximumDays).AddTicks(1),
            CancellationToken.None));

        Assert.Equal(0, provider.AssignmentCalls);
    }

    [Fact]
    public async Task Multiple_active_connections_require_explicit_selection()
    {
        var provider = new TestLmsProvider();
        var secondConnection = Connection(User) with { Id = Guid.NewGuid() };
        var service = CreateService(provider, Connection(User), secondConnection);

        await Assert.ThrowsAsync<ConnectionSelectionRequiredException>(() => service.GetMyCoursesAsync(
            User,
            connectionId: null,
            CancellationToken.None));

        Assert.Null(provider.LastContext);
    }

    [Fact]
    public async Task A_connection_returned_for_another_tenant_or_user_is_denied()
    {
        var provider = new TestLmsProvider();
        var foreignConnection = Connection(User) with
        {
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid()
        };
        var service = CreateService(provider, foreignConnection);

        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(() => service.GetMyCoursesAsync(
            User,
            foreignConnection.Id,
            CancellationToken.None));

        Assert.Null(provider.LastContext);
    }

    [Fact]
    public async Task Insecure_connection_url_is_rejected_before_provider_call()
    {
        var provider = new TestLmsProvider();
        var insecureConnection = Connection(User) with { BaseUrl = "http://mock.lms.invalid" };
        var service = CreateService(provider, insecureConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetMyCoursesAsync(
            User,
            ConnectionId,
            CancellationToken.None));

        Assert.Null(provider.LastContext);
    }

    [Fact]
    public async Task Unsupported_progress_is_returned_as_null_with_explanation()
    {
        var provider = new TestLmsProvider
        {
            Courses = [new CourseDto("course-1", "Mock", "Course", null, null, null, null)],
            Progress = new CourseProgressDto("course-1", null, null, null, "The provider does not expose a reliable completion metric.")
        };
        var service = CreateService(provider, Connection(User));

        var result = await service.GetCourseProgressAsync(
            User,
            ConnectionId,
            "course-1",
            CancellationToken.None);

        Assert.Null(result.CompletionPercent);
        Assert.Null(result.CompletedItems);
        Assert.Null(result.TotalItems);
        Assert.Contains("does not expose", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Course_progress_cannot_be_requested_for_a_course_outside_the_connection()
    {
        var provider = new TestLmsProvider
        {
            Courses = [new CourseDto("owned-course", "Mock", "Owned", null, null, null, null)]
        };
        var service = CreateService(provider, Connection(User));

        var exception = await Assert.ThrowsAsync<LmsProviderException>(() => service.GetCourseProgressAsync(
            User,
            ConnectionId,
            "another-users-course",
            CancellationToken.None));

        Assert.Equal(LmsProviderFailureCode.NotFound, exception.Code);
        Assert.Equal(0, provider.ProgressCalls);
    }

    [Fact]
    public async Task Schedule_results_include_events_that_overlap_the_requested_interval()
    {
        var provider = new TestLmsProvider
        {
            Schedule =
            [
                new("inside", "course-1", "Inside", FromUtc.AddHours(1), FromUtc.AddHours(2), null, null),
                new("overlaps-start", "course-1", "Overlap", FromUtc.AddHours(-1), FromUtc.AddMinutes(30), null, null),
                new("at-end", "course-1", "Excluded", FromUtc.AddDays(1), null, null, null)
            ]
        };
        var service = CreateService(provider, Connection(User));

        var result = await service.GetScheduleAsync(
            User,
            ConnectionId,
            FromUtc,
            FromUtc.AddDays(1),
            CancellationToken.None);

        Assert.Equal(["inside", "overlaps-start"], result.Select(item => item.Id));
    }

    private static LmsQueryService CreateService(TestLmsProvider provider, params LmsConnection[] connections) =>
        new(new TestConnectionRepository(connections), [provider]);

    private static LmsConnection Connection(AuthenticatedUserContext user) => new(
        ConnectionId,
        user.TenantId,
        user.UserId,
        LmsProviderType.Mock,
        "https://mock.lms.invalid",
        "mock:test",
        [],
        LmsConnectionStatus.Connected,
        ExpiresAt: null);

    private static AssignmentDto Assignment(string id, DateTimeOffset? dueAt) =>
        new(id, "course-1", id, dueAt, null, null, null);

    private sealed class TestConnectionRepository(IReadOnlyList<LmsConnection> connections)
        : ILmsConnectionRepository
    {
        public Task<IReadOnlyList<LmsConnection>> GetForUserAsync(
            Guid userId,
            Guid tenantId,
            CancellationToken cancellationToken) => Task.FromResult(connections);

        public Task<LmsConnection?> GetOwnedAsync(
            Guid connectionId,
            Guid userId,
            Guid tenantId,
            CancellationToken cancellationToken) => Task.FromResult(connections.FirstOrDefault(connection =>
                connection.Id == connectionId
                && connection.UserId == userId
                && connection.TenantId == tenantId));

        public Task AddAsync(LmsConnection connection, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateAsync(LmsConnection connection, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestLmsProvider : ILmsProvider
    {
        public IReadOnlyList<CourseDto> Courses { get; init; } = [];

        public IReadOnlyList<AssignmentDto> Assignments { get; init; } = [];

        public IReadOnlyList<ScheduleEventDto> Schedule { get; init; } = [];

        public CourseProgressDto Progress { get; init; } = new("course-1", null, null, null, "Unavailable.");

        public LmsRequestContext? LastContext { get; private set; }

        public DateTimeOffset? LastFrom { get; private set; }

        public DateTimeOffset? LastTo { get; private set; }

        public int AssignmentCalls { get; private set; }

        public int ProgressCalls { get; private set; }

        public LmsProviderType ProviderType => LmsProviderType.Mock;

        public Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(
            LmsRequestContext context,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(Courses);
        }

        public Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(
            LmsRequestContext context,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            LastFrom = from;
            LastTo = to;
            AssignmentCalls++;
            return Task.FromResult(Assignments);
        }

        public Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(
            LmsRequestContext context,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(Schedule);
        }

        public Task<CourseProgressDto> GetCourseProgressAsync(
            LmsRequestContext context,
            string courseId,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            ProgressCalls++;
            return Task.FromResult(Progress);
        }
    }
}
