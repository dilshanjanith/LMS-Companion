using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;
using LmsCompanion.Infrastructure.Canvas;

namespace LmsCompanion.IntegrationTests;

public sealed class CanvasLmsProviderTests
{
    private static readonly Uri CanvasBase = new("https://canvas.example.edu/");
    private static readonly LmsRequestContext Context = new(
        Guid.Parse("6d0c3926-cfc5-45df-a138-f5c4050d6ffb"),
        Guid.Parse("bff5434f-9ac3-4dc6-aa60-10b291e4a8fc"),
        Guid.Parse("1857d3bb-11da-495f-b6dc-b981bb14c177"),
        LmsProviderType.Canvas,
        CanvasBase,
        "credential-reference");

    [Fact]
    public async Task Courses_follow_canvas_link_pagination_and_map_safe_normalized_fields()
    {
        var requests = new ConcurrentBag<HttpRequestMessage>();
        var handler = new TestHandler((request, _) =>
        {
            requests.Add(request);
            var response = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)
                ? Json("[{\"id\":\"course-2\",\"name\":\"Second\",\"html_url\":\"https://canvas.example.edu/courses/course-2\"}]")
                : Json("[{\"id\":101,\"name\":\"Biology\",\"course_code\":\"BIO101\",\"html_url\":\"https://canvas.example.edu/courses/101\",\"term\":{\"name\":\"Fall\"}}]");
            if (!request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal))
            {
                response.Headers.Add("Link", $"<{CanvasBase}api/v1/courses?page=2>; rel=\"next\"");
            }
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var tokenService = new TestAccessTokens();
        var provider = new CanvasLmsProvider(httpClient, tokenService);

        var courses = await provider.GetMyCoursesAsync(Context, CancellationToken.None);

        Assert.Equal(2, courses.Count);
        Assert.Equal("101", courses[0].Id);
        Assert.Equal("Canvas", courses[0].Provider);
        Assert.Equal("Fall", courses[0].Term);
        Assert.Equal("https://canvas.example.edu/courses/101", courses[0].Url);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("access-token", request.Headers.Authorization.Parameter);
            Assert.Contains(request.Headers.Accept, item => item.MediaType == "application/json+canvas-string-ids");
        });
    }

    [Fact]
    public async Task Pagination_link_to_another_origin_is_rejected_before_following_it()
    {
        var requestCount = 0;
        var handler = new TestHandler((_, _) =>
        {
            requestCount++;
            var response = Json("[]");
            response.Headers.Add("Link", "<https://attacker.example/api/v1/courses?page=2>; rel=\"next\"");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var provider = new CanvasLmsProvider(httpClient, new TestAccessTokens());

        var exception = await Assert.ThrowsAsync<LmsProviderException>(() =>
            provider.GetMyCoursesAsync(Context, CancellationToken.None));

        Assert.Equal(LmsProviderFailureCode.Transient, exception.Code);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task Calendar_contexts_are_chunked_at_ten_and_schedule_events_are_normalized()
    {
        var calendarRequests = new ConcurrentBag<Uri>();
        var handler = new TestHandler((request, _) =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("/courses", StringComparison.Ordinal))
            {
                return Task.FromResult(Json("[" + string.Join(',', Enumerable.Range(1, 11).Select(id =>
                    $"{{\"id\":{id},\"name\":\"Course {id}\"}}")) + "]"));
            }

            calendarRequests.Add(uri);
            var decoded = Uri.UnescapeDataString(uri.Query);
            if (!decoded.Contains("context_codes[]", StringComparison.Ordinal))
            {
                return Task.FromResult(Json("[{\"id\":47,\"title\":\"Personal event\",\"start_at\":\"2026-10-29T11:00:00Z\",\"context_code\":\"user_500\"}]"));
            }

            var courseId = decoded.Contains("course_11", StringComparison.Ordinal) ? "course_11" : "course_1";
            var eventId = courseId == "course_11" ? 46 : 45;
            return Task.FromResult(Json($"[{{\"id\":{eventId},\"title\":\"Lecture\",\"start_at\":\"2026-10-29T09:00:00Z\",\"end_at\":\"2026-10-29T10:00:00Z\",\"location_name\":\"Room 4\",\"context_code\":\"{courseId}\",\"html_url\":\"https://canvas.example.edu/calendar?event_id={eventId}\"}}]"));
        });
        using var httpClient = new HttpClient(handler);
        var provider = new CanvasLmsProvider(httpClient, new TestAccessTokens());

        var events = await provider.GetScheduleAsync(
            Context,
            new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(3, calendarRequests.Count);
        var courseCalendarRequests = calendarRequests
            .Where(uri => Uri.UnescapeDataString(uri.Query).Contains("context_codes[]", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, courseCalendarRequests.Length);
        Assert.All(courseCalendarRequests, uri =>
        {
            Assert.Contains("type=event", uri.Query, StringComparison.Ordinal);
            Assert.InRange(Uri.UnescapeDataString(uri.Query).Split("context_codes[]=").Length - 1, 1, 10);
        });
        Assert.Equal(3, events.Count);
        Assert.Equal(2, events.Count(item => item.Title == "Lecture"));
        Assert.Contains(events, item => item.CourseId == "1");
        Assert.Contains(events, item => item.CourseId == "11");
        Assert.Contains(events, item => item.CourseId is null && item.Title == "Personal event");
        Assert.Equal("Room 4", events[0].Location);
    }

    [Fact]
    public async Task Assignment_events_preserve_canvas_ids_course_and_due_dates()
    {
        var handler = new TestHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/courses", StringComparison.Ordinal))
            {
                return Task.FromResult(Json("[{\"id\":20,\"name\":\"Math\"}]"));
            }

            Assert.Contains("bucket=upcoming", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("include%5B%5D=submission", request.RequestUri.Query, StringComparison.Ordinal);
            return Task.FromResult(Json("[{\"id\":77,\"name\":\"Problem set\",\"due_at\":\"2026-10-30T12:30:00Z\",\"workflow_state\":\"published\",\"points_possible\":25,\"submission\":{\"workflow_state\":\"submitted\"},\"html_url\":\"https://canvas.example.edu/courses/20/assignments/77\"}]"));
        });
        using var httpClient = new HttpClient(handler);
        var provider = new CanvasLmsProvider(httpClient, new TestAccessTokens());

        var assignments = await provider.GetUpcomingAssignmentsAsync(
            Context,
            new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var assignment = Assert.Single(assignments);
        Assert.Equal("77", assignment.Id);
        Assert.Equal("20", assignment.CourseId);
        Assert.Equal("Problem set", assignment.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 12, 30, 0, TimeSpan.Zero), assignment.DueAt);
        Assert.Equal("submitted", assignment.Status);
        Assert.Equal(25m, assignment.PointsPossible);
        Assert.Equal("https://canvas.example.edu/courses/20/assignments/77", assignment.Url);
    }

    [Fact]
    public async Task Course_progress_maps_only_module_requirement_counts()
    {
        var handler = new TestHandler((_, _) => Task.FromResult(Json(
            "{\"id\":20,\"name\":\"Math\",\"course_progress\":{\"requirement_count\":8,\"requirement_completed_count\":3,\"completed_at\":null}}")));
        using var httpClient = new HttpClient(handler);
        var provider = new CanvasLmsProvider(httpClient, new TestAccessTokens());

        var result = await provider.GetCourseProgressAsync(Context, "20", CancellationToken.None);

        Assert.Equal(37.5m, result.CompletionPercent);
        Assert.Equal(3, result.CompletedItems);
        Assert.Equal(8, result.TotalItems);
        Assert.Contains("module items", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_canvas_progress_is_returned_as_an_explicit_unavailable_value()
    {
        var handler = new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("raw Canvas error detail")
        }));
        using var httpClient = new HttpClient(handler);
        var provider = new CanvasLmsProvider(httpClient, new TestAccessTokens());

        var result = await provider.GetCourseProgressAsync(Context, "20", CancellationToken.None);

        Assert.Null(result.CompletionPercent);
        Assert.Null(result.CompletedItems);
        Assert.Null(result.TotalItems);
        Assert.Contains("does not provide", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_response_forces_one_token_refresh_before_retry()
    {
        var calls = 0;
        var handler = new TestHandler((request, _) =>
        {
            calls++;
            if (request.Headers.Authorization!.Parameter == "access-token")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(Json("[]"));
        });
        using var httpClient = new HttpClient(handler);
        var tokens = new TestAccessTokens();
        var provider = new CanvasLmsProvider(httpClient, tokens);

        var courses = await provider.GetMyCoursesAsync(Context, CancellationToken.None);

        Assert.Empty(courses);
        Assert.Equal(2, calls);
        Assert.Equal(1, tokens.ForcedRefreshes);
    }

    [Fact]
    public async Task Rate_limit_is_retried_once_then_mapped_to_safe_error()
    {
        var calls = 0;
        var handler = new TestHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("sensitive raw provider detail")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var provider = new CanvasLmsProvider(httpClient, new TestAccessTokens());

        var exception = await Assert.ThrowsAsync<LmsProviderException>(() =>
            provider.GetMyCoursesAsync(Context, CancellationToken.None));

        Assert.Equal(2, calls);
        Assert.Equal(LmsProviderFailureCode.RateLimited, exception.Code);
        Assert.DoesNotContain("sensitive raw provider detail", exception.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, exception.RetryAfter);
    }

    [Fact]
    public async Task Oversized_provider_page_is_rejected_with_a_safe_error()
    {
        var handler = new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 1024 * 1024 + 1))
        }));
        using var httpClient = new HttpClient(handler);
        var provider = new CanvasLmsProvider(httpClient, new TestAccessTokens());

        var exception = await Assert.ThrowsAsync<LmsProviderException>(() =>
            provider.GetMyCoursesAsync(Context, CancellationToken.None));

        Assert.Equal(LmsProviderFailureCode.Transient, exception.Code);
        Assert.DoesNotContain(new string('x', 32), exception.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class TestHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class TestAccessTokens : ICanvasAccessTokenService
    {
        public int ForcedRefreshes { get; private set; }

        public Task<string> GetAccessTokenAsync(
            AuthenticatedUserContext user,
            Guid connectionId,
            CancellationToken cancellationToken,
            bool forceRefresh = false)
        {
            if (forceRefresh)
            {
                ForcedRefreshes++;
                return Task.FromResult("refreshed-access-token");
            }

            return Task.FromResult("access-token");
        }
    }
}
