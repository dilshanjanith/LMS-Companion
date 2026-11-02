using System.Security.Claims;
using LmsCompanion.Api.Controllers;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace LmsCompanion.IntegrationTests;

public sealed class LmsControllerTests
{
    private static readonly Guid UserId = Guid.Parse("6d0c3926-cfc5-45df-a138-f5c4050d6ffb");
    private static readonly Guid TenantId = Guid.Parse("bff5434f-9ac3-4dc6-aa60-10b291e4a8fc");

    [Fact]
    public async Task Courses_route_returns_a_bounded_page_after_resolving_authenticated_tenant()
    {
        var queries = new TestQueries();
        var controller = CreateController(queries);

        var response = await controller.GetCourses(null, page: 2, pageSize: 2, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response);
        var page = Assert.IsType<ApiPage<CourseDto>>(result.Value);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(3, page.TotalItems);
        Assert.Equal("course-3", Assert.Single(page.Items).Id);
        Assert.Equal(new AuthenticatedUserContext(UserId, TenantId), queries.LastUser);
    }

    [Fact]
    public async Task Invalid_page_size_is_rejected_before_querying_lms()
    {
        var queries = new TestQueries();
        var controller = CreateController(queries);

        var response = await controller.GetCourses(null, page: 1, pageSize: 101, CancellationToken.None);

        var result = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(0, queries.CourseCalls);
        Assert.Equal("invalid_pagination", result.Value!.GetType().GetProperty("error")!.GetValue(result.Value));
    }

    private static LmsController CreateController(TestQueries queries)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:TenantClaim"] = "tenant_id" })
            .Build();
        var controller = new LmsController(queries, new TestUserResolver(), configuration);
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "external-subject"),
            new Claim("tenant_id", TenantId.ToString())
        ],
        authenticationType: "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private sealed class TestUserResolver : IAuthenticatedUserContextResolver
    {
        public Task<AuthenticatedUserContext?> ResolveAsync(string externalSubject, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedUserContext?>(externalSubject == "external-subject" && tenantId == TenantId
                ? new AuthenticatedUserContext(UserId, TenantId)
                : null);
    }

    private sealed class TestQueries : ILmsQueryService
    {
        private readonly CourseDto[] _courses =
        [
            new("course-1", "Canvas", "One", null, null, null, null),
            new("course-2", "Canvas", "Two", null, null, null, null),
            new("course-3", "Canvas", "Three", null, null, null, null)
        ];

        public int CourseCalls { get; private set; }
        public AuthenticatedUserContext? LastUser { get; private set; }

        public Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(AuthenticatedUserContext user, Guid? connectionId, CancellationToken cancellationToken)
        {
            CourseCalls++;
            LastUser = user;
            return Task.FromResult<IReadOnlyList<CourseDto>>(_courses);
        }

        public Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(AuthenticatedUserContext user, Guid? connectionId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignmentDto>>([]);

        public Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(AuthenticatedUserContext user, Guid? connectionId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduleEventDto>>([]);

        public Task<CourseProgressDto> GetCourseProgressAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, CancellationToken cancellationToken) =>
            Task.FromResult(new CourseProgressDto(courseId, null, null, null, "not available"));
    }
}
