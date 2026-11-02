using System.Text.Json;
using System.Text.Json.Serialization;

namespace LmsCompanion.Infrastructure.Canvas;

internal sealed record CanvasCourseResponse(
    [property: JsonPropertyName("id")] JsonElement Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("course_code")] string? CourseCode,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("term")] CanvasTermResponse? Term,
    [property: JsonPropertyName("course_progress")] CanvasCourseProgressResponse? CourseProgress);

internal sealed record CanvasTermResponse(
    [property: JsonPropertyName("name")] string? Name);

internal sealed record CanvasCourseProgressResponse(
    [property: JsonPropertyName("requirement_count")] int? RequirementCount,
    [property: JsonPropertyName("requirement_completed_count")] int? RequirementCompletedCount,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt);

internal sealed record CanvasAssignmentResponse(
    [property: JsonPropertyName("id")] JsonElement Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
    [property: JsonPropertyName("workflow_state")] string? WorkflowState,
    [property: JsonPropertyName("points_possible")] decimal? PointsPossible,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("submission")] CanvasSubmissionResponse? Submission);

internal sealed record CanvasSubmissionResponse(
    [property: JsonPropertyName("workflow_state")] string? WorkflowState);

internal sealed record CanvasCalendarEventResponse(
    [property: JsonPropertyName("id")] JsonElement Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("start_at")] DateTimeOffset? StartAt,
    [property: JsonPropertyName("end_at")] DateTimeOffset? EndAt,
    [property: JsonPropertyName("location_name")] string? LocationName,
    [property: JsonPropertyName("context_code")] string? ContextCode,
    [property: JsonPropertyName("workflow_state")] string? WorkflowState,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);
