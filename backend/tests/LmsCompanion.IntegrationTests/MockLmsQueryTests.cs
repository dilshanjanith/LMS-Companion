using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using LmsCompanion.Infrastructure;
using LmsCompanion.Infrastructure.Mock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LmsCompanion.IntegrationTests;

public sealed class MockLmsQueryTests
{
    [Fact]
    public async Task Local_mock_configuration_returns_courses_and_honest_unsupported_progress()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LmsCompanion"] = "Host=localhost;Database=lms_companion;Username=test;Password=test",
                ["Lms:Provider"] = "Mock",
                ["Lms:AllowMockProvider"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<ILmsConnectionRepository, MockLmsConnectionRepository>();
        services.AddApplication();
        using var serviceProvider = services.BuildServiceProvider();
        var queryService = serviceProvider.GetRequiredService<ILmsQueryService>();
        var user = new AuthenticatedUserContext(MockLmsIdentity.UserId, MockLmsIdentity.TenantId);
        var from = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var to = from.AddDays(7);

        var courses = await queryService.GetMyCoursesAsync(user, null, CancellationToken.None);
        var assignments = await queryService.GetUpcomingAssignmentsAsync(
            user,
            null,
            from,
            to,
            CancellationToken.None);
        var schedule = await queryService.GetScheduleAsync(user, null, from, to, CancellationToken.None);
        var progress = await queryService.GetCourseProgressAsync(
            user,
            MockLmsIdentity.ConnectionId,
            "course-205",
            CancellationToken.None);

        Assert.Equal(2, courses.Count);
        Assert.Contains(courses, course => course.Id == "course-101");
        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, assignment => assignment.Id == "assignment-101-1");
        Assert.DoesNotContain(assignments, assignment => assignment.Id == "assignment-205-1");
        Assert.Equal(2, schedule.Count);
        Assert.Null(progress.CompletionPercent);
        Assert.Null(progress.CompletedItems);
        Assert.Null(progress.TotalItems);
        Assert.Contains("does not expose", progress.Explanation, StringComparison.Ordinal);
    }
}
