using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Infrastructure.Canvas;

/// <summary>Read-only Canvas adapter. It never follows pagination links outside the configured Canvas origin.</summary>
public sealed partial class CanvasLmsProvider(
    HttpClient httpClient,
    ICanvasAccessTokenService accessTokens) : ILmsProvider
{
    private const int PageSize = 100;
    private const int MaxPages = 10;
    private const int MaxItems = 1000;
    private const int MaxCalendarCourses = 100;
    private const int MaxPageResponseBytes = 1024 * 1024;
    private const int CalendarContextLimit = 10;
    private const int MaxTransientAttempts = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LmsProviderType ProviderType => LmsProviderType.Canvas;

    public async Task<IReadOnlyList<CourseModuleDto>> GetCourseModulesAsync(LmsRequestContext context, string courseId, CancellationToken ct)
    {
        EnsureCanvasContext(context);
        var uri = BuildUri(context.ProviderBaseUri, $"/api/v1/courses/{Uri.EscapeDataString(courseId)}/modules", [new("per_page", "100")]);
        var modules = await GetAllPagesAsync<CanvasModule>(context, uri, ct);
        return modules.Where(module => module.Id.ValueKind != JsonValueKind.Undefined && module.Name is not null)
            .Where(module => module.WorkflowState is not "deleted")
            .Select(module => new CourseModuleDto(IdValue(module.Id)!, courseId, module.Name!, null)).Take(100).ToArray();
    }

    public async Task<IReadOnlyList<ModuleItemDto>> GetModuleItemsAsync(LmsRequestContext context, string courseId, string moduleId, CancellationToken ct)
    {
        EnsureCanvasContext(context);
        var uri = BuildUri(context.ProviderBaseUri,
            $"/api/v1/courses/{Uri.EscapeDataString(courseId)}/modules/{Uri.EscapeDataString(moduleId)}/items", [new("per_page", "100")]);
        var items = await GetAllPagesAsync<CanvasModuleItem>(context, uri, ct);
        return items.Where(item => IdValue(item.Id) is not null && item.Title is not null)
            .Select(item => new ModuleItemDto(IdValue(item.Id)!, moduleId, courseId, item.Title!, item.Type ?? "Unknown",
                SafeCanvasUrl(context.ProviderBaseUri, item.HtmlUrl))).Take(100).ToArray();
    }

    public async Task<LearningMaterialDto> GetLearningMaterialAsync(LmsRequestContext context, string courseId, string moduleId, string itemId, CancellationToken ct)
    {
        var items = await GetModuleItemsAsync(context, courseId, moduleId, ct);
        var item = items.SingleOrDefault(candidate => candidate.Id == itemId)
            ?? throw new LmsProviderException(LmsProviderFailureCode.NotFound, "The module item is not available.");
        var itemUri = new Uri(context.ProviderBaseUri,
            $"/api/v1/courses/{Uri.EscapeDataString(courseId)}/modules/{Uri.EscapeDataString(moduleId)}/items/{Uri.EscapeDataString(itemId)}");
        var detail = await GetSingleAsync<CanvasModuleItem>(context, itemUri, ct);
        string? html = null;
        if (detail.Type == "Page" && !string.IsNullOrWhiteSpace(detail.PageUrl))
        {
            var pageUri = new Uri(context.ProviderBaseUri,
                $"/api/v1/courses/{Uri.EscapeDataString(courseId)}/pages/{Uri.EscapeDataString(detail.PageUrl)}");
            html = (await GetSingleAsync<CanvasLearningPage>(context, pageUri, ct)).Body;
        }
        else if (detail.Type == "Assignment" && IdValue(detail.ContentId) is { } assignmentId)
        {
            var assignmentUri = new Uri(context.ProviderBaseUri,
                $"/api/v1/courses/{Uri.EscapeDataString(courseId)}/assignments/{Uri.EscapeDataString(assignmentId)}");
            html = (await GetSingleAsync<CanvasLearningAssignment>(context, assignmentUri, ct)).Description;
        }
        else
        {
            throw new LmsProviderException(LmsProviderFailureCode.NotSupported, "Text is not available for this module item type.");
        }
        var withoutActiveMarkup = Regex.Replace(html ?? string.Empty,
            @"<(script|style)\b[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100));
        var plain = System.Net.WebUtility.HtmlDecode(Regex.Replace(withoutActiveMarkup,
            "<[^>]+>", " ", RegexOptions.Singleline, TimeSpan.FromMilliseconds(100)));
        plain = Regex.Replace(plain, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
        if (plain.Length > 20000) plain = plain[..20000];
        return new LearningMaterialDto(courseId, moduleId, itemId, item.Title, plain, item.Url, DateTimeOffset.UtcNow);
    }

    private sealed record CanvasModule(JsonElement Id, string? Name, [property: JsonPropertyName("workflow_state")] string? WorkflowState);
    private sealed record CanvasModuleItem(JsonElement Id, string? Title, string? Type,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("page_url")] string? PageUrl,
        [property: JsonPropertyName("content_id")] JsonElement ContentId);
    private sealed record CanvasLearningPage(string? Body);
    private sealed record CanvasLearningAssignment(string? Description);

    public async Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(
        LmsRequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureCanvasContext(context);
        var uri = BuildUri(context.ProviderBaseUri, "/api/v1/courses", [
            new("enrollment_state", "active"),
            new("include[]", "term"),
            new("per_page", PageSize.ToString(CultureInfo.InvariantCulture))
        ]);
        var courses = await GetAllPagesAsync<CanvasCourseResponse>(context, uri, cancellationToken);
        return courses
            .Where(course => !string.IsNullOrWhiteSpace(IdValue(course.Id)) && !string.IsNullOrWhiteSpace(course.Name))
            .Select(course =>
            {
                var id = IdValue(course.Id)!;
                return new CourseDto(
                    id,
                    "Canvas",
                    course.Name!,
                    course.CourseCode,
                    course.Term?.Name,
                    Role: null,
                    SafeCanvasUrl(context.ProviderBaseUri, course.HtmlUrl));
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(
        LmsRequestContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureCanvasContext(context);
        var courses = await GetMyCoursesAsync(context, cancellationToken);
        if (courses.Count == 0)
        {
            return [];
        }

        if (courses.Count > MaxCalendarCourses)
        {
            throw new LmsProviderException(
                LmsProviderFailureCode.Transient,
                "Canvas has more courses than the assignment query limit supports.");
        }

        using var concurrencyGate = new SemaphoreSlim(4, 4);
        var courseAssignments = courses.Select(async course =>
        {
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                var parameters = new List<KeyValuePair<string, string>>
                {
                    new("bucket", "upcoming"),
                    new("include[]", "submission"),
                    new("per_page", PageSize.ToString(CultureInfo.InvariantCulture))
                };
                var uri = BuildUri(
                    context.ProviderBaseUri,
                    $"/api/v1/courses/{Uri.EscapeDataString(course.Id)}/assignments",
                    parameters);
                return await GetAllPagesAsync<CanvasAssignmentResponse>(context, uri, cancellationToken);
            }
            finally
            {
                concurrencyGate.Release();
            }
        }).ToArray();

        var allAssignments = (await Task.WhenAll(courseAssignments))
            .SelectMany((assignments, index) => assignments.Select(assignment => (Course: courses[index], Assignment: assignment)));
        return allAssignments
            .Where(item => !string.Equals(item.Assignment.WorkflowState, "deleted", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Assignment.DueAt is { } dueAt && dueAt >= from && dueAt < to)
            .Select(item => string.IsNullOrWhiteSpace(IdValue(item.Assignment.Id)) || string.IsNullOrWhiteSpace(item.Assignment.Name)
                ? null
                : new AssignmentDto(
                    IdValue(item.Assignment.Id)!,
                    item.Course.Id,
                    item.Assignment.Name!,
                    item.Assignment.DueAt,
                    item.Assignment.Submission?.WorkflowState,
                    item.Assignment.PointsPossible,
                    SafeCanvasUrl(context.ProviderBaseUri, item.Assignment.HtmlUrl)))
            .Where(assignment => assignment is not null)
            .Cast<AssignmentDto>()
            .DistinctBy(assignment => assignment.Id, StringComparer.Ordinal)
            .OrderBy(assignment => assignment.DueAt)
            .ToArray();
    }

    public async Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(
        LmsRequestContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureCanvasContext(context);
        var courses = await GetMyCoursesAsync(context, cancellationToken);
        var allEvents = new List<CanvasCalendarEventResponse>();
        if (courses.Count > 0)
        {
            allEvents.AddRange(await GetCalendarEventsForCoursesAsync(context, courses, "event", from, to, cancellationToken));
        }

        allEvents.AddRange(await GetPersonalCalendarEventsAsync(context, from, to, cancellationToken));
        return allEvents
            .Where(item => !string.Equals(item.WorkflowState, "deleted", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.StartAt is not null)
            .Select(item =>
            {
                var id = IdValue(item.Id);
                return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(item.Title)
                    ? null
                    : new ScheduleEventDto(
                        id,
                        ParseCourseId(item.ContextCode),
                        item.Title!,
                        item.StartAt!.Value,
                        item.EndAt,
                        item.LocationName,
                        SafeCanvasUrl(context.ProviderBaseUri, item.HtmlUrl));
            })
            .Where(item => item is not null)
            .Cast<ScheduleEventDto>()
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .OrderBy(item => item.StartsAt)
            .ToArray();
    }

    public async Task<CourseProgressDto> GetCourseProgressAsync(
        LmsRequestContext context,
        string courseId,
        CancellationToken cancellationToken)
    {
        EnsureCanvasContext(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(courseId);
        if (courseId.Length > 128)
        {
            throw new LmsProviderException(LmsProviderFailureCode.NotFound, "The requested Canvas course is not available.");
        }

        var uri = BuildUri(context.ProviderBaseUri, $"/api/v1/courses/{Uri.EscapeDataString(courseId)}", [
            new("include[]", "course_progress")
        ]);
        CanvasCourseResponse course;
        try
        {
            course = await GetSingleAsync<CanvasCourseResponse>(context, uri, cancellationToken);
        }
        catch (LmsProviderException exception) when (exception.Code == LmsProviderFailureCode.NotSupported)
        {
            return ProgressUnavailable(courseId);
        }

        var progress = course.CourseProgress;
        if (progress?.RequirementCount is not > 0
            || progress.RequirementCompletedCount is not { } completed
            || completed < 0
            || completed > progress.RequirementCount)
        {
            return ProgressUnavailable(courseId);
        }

        var total = progress.RequirementCount.Value;
        return new CourseProgressDto(
            courseId,
            decimal.Round((decimal)completed * 100m / total, 1, MidpointRounding.AwayFromZero),
            completed,
            total,
            completed == total
                ? "All required module items are complete."
                : "Based on completed required module items reported by Canvas.");
    }

    private static CourseProgressDto ProgressUnavailable(string courseId) => new(
        courseId,
        null,
        null,
        null,
        "Canvas does not provide module-based progress for this course or enrollment.");

    private async Task<IReadOnlyList<CanvasCalendarEventResponse>> GetCalendarEventsForCoursesAsync(
        LmsRequestContext context,
        IReadOnlyList<CourseDto> courses,
        string type,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var courseContexts = courses.Select(course => $"course_{course.Id}").ToArray();
        if (courseContexts.Length > MaxCalendarCourses)
        {
            throw new LmsProviderException(
                LmsProviderFailureCode.Transient,
                "Canvas has more courses than the calendar query limit supports.");
        }
        var pages = new List<IReadOnlyList<CanvasCalendarEventResponse>>();
        using var concurrencyGate = new SemaphoreSlim(4, 4);
        var requests = courseContexts.Chunk(CalendarContextLimit).Select(async group =>
        {
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                var parameters = new List<KeyValuePair<string, string>>
                {
                    new("type", type),
                    new("start_date", CanvasTimestamp(from)),
                    new("end_date", CanvasTimestamp(to)),
                    new("per_page", PageSize.ToString(CultureInfo.InvariantCulture))
                };
                parameters.AddRange(group.Select(code => new KeyValuePair<string, string>("context_codes[]", code)));
                var uri = BuildUri(context.ProviderBaseUri, "/api/v1/calendar_events", parameters);
                return await GetAllPagesAsync<CanvasCalendarEventResponse>(context, uri, cancellationToken);
            }
            finally
            {
                concurrencyGate.Release();
            }
        }).ToArray();

        pages.AddRange(await Task.WhenAll(requests));
        return pages.SelectMany(page => page).ToArray();
    }

    private Task<IReadOnlyList<CanvasCalendarEventResponse>> GetPersonalCalendarEventsAsync(
        LmsRequestContext context,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var uri = BuildUri(context.ProviderBaseUri, "/api/v1/calendar_events", [
            new("type", "event"),
            new("start_date", CanvasTimestamp(from)),
            new("end_date", CanvasTimestamp(to)),
            new("per_page", PageSize.ToString(CultureInfo.InvariantCulture))
        ]);
        return GetAllPagesAsync<CanvasCalendarEventResponse>(context, uri, cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetAllPagesAsync<T>(
        LmsRequestContext context,
        Uri firstPage,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Uri? next = firstPage;
        var pageCount = 0;
        while (next is not null)
        {
            ValidateCanvasUri(context.ProviderBaseUri, next);
            if (!seen.Add(next.AbsoluteUri) || ++pageCount > MaxPages)
            {
                throw PaginationLimitExceeded();
            }

            using var response = await SendGetAsync(context, next, cancellationToken);
            EnsureSuccess(response);
            T[]? page;
            try
            {
                page = await ReadBoundedJsonAsync<T[]>(response, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an invalid response.", innerException: exception);
            }
            catch (IOException exception)
            {
                throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an invalid response.", innerException: exception);
            }

            if (page is null)
            {
                throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an invalid response.");
            }

            results.AddRange(page);
            if (results.Count > MaxItems)
            {
                throw PaginationLimitExceeded();
            }

            next = FindNextPage(response, context.ProviderBaseUri);
        }

        return results;
    }

    private async Task<T> GetSingleAsync<T>(LmsRequestContext context, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(context, uri, cancellationToken);
        EnsureSuccess(response);
        try
        {
            return await ReadBoundedJsonAsync<T>(response, cancellationToken)
                ?? throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an invalid response.");
        }
        catch (JsonException exception)
        {
            throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an invalid response.", innerException: exception);
        }
        catch (IOException exception)
        {
            throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an invalid response.", innerException: exception);
        }
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        LmsRequestContext context,
        Uri uri,
        CancellationToken cancellationToken)
    {
        ValidateCanvasUri(context.ProviderBaseUri, uri);
        var accessToken = await accessTokens.GetAccessTokenAsync(
            new AuthenticatedUserContext(context.AuthenticatedUserId, context.TenantId),
            context.ConnectionId,
            cancellationToken);
        var forcedRefresh = false;
        var transientAttempts = 0;

        while (true)
        {
            using var request = CreateGetRequest(uri, accessToken);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                if (transientAttempts++ < MaxTransientAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                    continue;
                }

                throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas is temporarily unavailable.", innerException: exception);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (transientAttempts++ < MaxTransientAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                    continue;
                }

                throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas is temporarily unavailable.", innerException: exception);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !forcedRefresh)
            {
                response.Dispose();
                accessToken = await accessTokens.GetAccessTokenAsync(
                    new AuthenticatedUserContext(context.AuthenticatedUserId, context.TenantId),
                    context.ConnectionId,
                    cancellationToken,
                    forceRefresh: true);
                forcedRefresh = true;
                continue;
            }

            if (IsTransient(response.StatusCode) && transientAttempts++ < MaxTransientAttempts - 1)
            {
                var delay = GetRetryDelay(response);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static HttpRequestMessage CreateGetRequest(Uri uri, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json+canvas-string-ids");
        return request;
    }

    private static async Task<T?> ReadBoundedJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxPageResponseBytes)
        {
            throw new IOException("Canvas response exceeded the configured page size limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var bytesRemaining = MaxPageResponseBytes - output.Length;
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, bytesRemaining + 1)), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxPageResponseBytes)
            {
                throw new IOException("Canvas response exceeded the configured page size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        output.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(output, JsonOptions, cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw MapFailure(response.StatusCode, response.Headers.RetryAfter);
    }

    private static LmsProviderException MapFailure(
        HttpStatusCode statusCode,
        RetryConditionHeaderValue? retryAfter) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new(LmsProviderFailureCode.Unauthorized, "Canvas denied access to the requested data."),
        HttpStatusCode.NotFound =>
            new(LmsProviderFailureCode.NotFound, "The requested Canvas data is not available."),
        HttpStatusCode.BadRequest =>
            new(LmsProviderFailureCode.NotSupported, "Canvas does not support this data request for the current course."),
        HttpStatusCode.TooManyRequests =>
            new(LmsProviderFailureCode.RateLimited, "Canvas is temporarily rate limiting requests.", ParseRetryAfter(retryAfter)),
        _ when (int)statusCode >= 500 =>
            new(LmsProviderFailureCode.Transient, "Canvas is temporarily unavailable."),
        _ => new(LmsProviderFailureCode.Transient, "Canvas could not complete the data request.")
    };

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var serverDelay = ParseRetryAfter(response.Headers.RetryAfter);
        return serverDelay is { } delay ? TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0, 1500)) : TimeSpan.FromMilliseconds(200);
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return TimeSpan.FromMilliseconds(Math.Clamp(delta.TotalMilliseconds, 0, 5000));
        }

        if (retryAfter?.Date is { } date)
        {
            return TimeSpan.FromMilliseconds(Math.Clamp((date - DateTimeOffset.UtcNow).TotalMilliseconds, 0, 5000));
        }

        return null;
    }

    private static Uri? FindNextPage(HttpResponseMessage response, Uri canvasBaseUri)
    {
        if (!response.Headers.TryGetValues("Link", out var headerValues))
        {
            return null;
        }

        foreach (var link in headerValues.SelectMany(value => LinkEntryRegex().Matches(value).Select(match => match.Groups[1].Value)))
        {
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
            {
                ValidateCanvasUri(canvasBaseUri, uri);
                return uri;
            }
        }

        return null;
    }

    private static Uri BuildUri(Uri baseUri, string path, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var query = string.Join('&', parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new UriBuilder(new Uri(baseUri, path)) { Query = query }.Uri;
    }

    private static string CanvasTimestamp(DateTimeOffset dateTime) =>
        dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void EnsureCanvasContext(LmsRequestContext context)
    {
        if (context.ProviderType != LmsProviderType.Canvas
            || context.ConnectionId == Guid.Empty
            || context.AuthenticatedUserId == Guid.Empty
            || context.TenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.CredentialReference)
            || context.ProviderBaseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(context.ProviderBaseUri.UserInfo))
        {
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized, "The Canvas connection is not available.");
        }
    }

    private static void ValidateCanvasUri(Uri canvasBaseUri, Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, canvasBaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != canvasBaseUri.Port
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal))
        {
            throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an unsafe pagination link.");
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query.AllKeys.Any(key => string.Equals(key, "access_token", StringComparison.OrdinalIgnoreCase)))
        {
            throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas returned an unsafe pagination link.");
        }
    }

    private static string? IdValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static string? ParseCourseId(string? contextCode) =>
        contextCode is not null && contextCode.StartsWith("course_", StringComparison.Ordinal)
            ? contextCode["course_".Length..]
            : null;

    private static string? SafeCanvasUrl(Uri canvasBaseUri, string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var candidate)
        && candidate.Scheme == Uri.UriSchemeHttps
        && string.Equals(candidate.Host, canvasBaseUri.Host, StringComparison.OrdinalIgnoreCase)
        && candidate.Port == canvasBaseUri.Port
        && string.IsNullOrEmpty(candidate.UserInfo)
            ? candidate.AbsoluteUri
            : null;

    private static LmsProviderException PaginationLimitExceeded() =>
        new(LmsProviderFailureCode.Transient, "Canvas returned more data than the configured page limit.");

    [GeneratedRegex("(?:^|,)\\s*<([^>]+)>\\s*;\\s*rel=\"?next\"?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkEntryRegex();
}
