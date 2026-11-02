using System.Security.Claims;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LmsCompanion.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class LmsController(
    ILmsQueryService queries,
    IAuthenticatedUserContextResolver userContextResolver,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("courses")]
    public async Task<IActionResult> GetCourses(
        [FromQuery] Guid? connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!ValidPage(page, pageSize)) return BadRequest(new { error = "invalid_pagination" });
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null) return Forbid();

        try
        {
            var items = await queries.GetMyCoursesAsync(user, connectionId, cancellationToken);
            return Ok(CreatePage(items, page, pageSize));
        }
        catch (Exception exception) when (TryMap(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("assignments/upcoming")]
    public async Task<IActionResult> GetUpcomingAssignments(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (from is null || to is null) return BadRequest(new { error = "date_range_required" });
        if (!ValidPage(page, pageSize)) return BadRequest(new { error = "invalid_pagination" });
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null) return Forbid();

        try
        {
            var items = await queries.GetUpcomingAssignmentsAsync(user, connectionId, from.Value, to.Value, cancellationToken);
            return Ok(CreatePage(items, page, pageSize));
        }
        catch (Exception exception) when (TryMap(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (from is null || to is null) return BadRequest(new { error = "date_range_required" });
        if (!ValidPage(page, pageSize)) return BadRequest(new { error = "invalid_pagination" });
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null) return Forbid();

        try
        {
            var items = await queries.GetScheduleAsync(user, connectionId, from.Value, to.Value, cancellationToken);
            return Ok(CreatePage(items, page, pageSize));
        }
        catch (Exception exception) when (TryMap(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("courses/{courseId}/progress")]
    public async Task<IActionResult> GetCourseProgress(
        string courseId,
        [FromQuery] Guid? connectionId,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null) return Forbid();

        try
        {
            return Ok(await queries.GetCourseProgressAsync(user, connectionId, courseId, cancellationToken));
        }
        catch (Exception exception) when (TryMap(exception, out var result))
        {
            return result;
        }
    }

    private async Task<AuthenticatedUserContext?> ResolveUserAsync(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue("sub");
        var tenantClaimName = configuration["Authentication:TenantClaim"] ?? "tenant_id";
        var tenantClaim = User.FindFirstValue(tenantClaimName);
        if (string.IsNullOrWhiteSpace(subject)
            || !Guid.TryParse(tenantClaim, out var tenantId)
            || tenantId == Guid.Empty)
        {
            return null;
        }

        return await userContextResolver.ResolveAsync(subject, tenantId, cancellationToken);
    }

    private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 100;

    private static ApiPage<T> CreatePage<T>(IReadOnlyList<T> items, int page, int pageSize)
    {
        var offset = ((long)page - 1) * pageSize;
        var pageItems = offset >= items.Count
            ? Array.Empty<T>()
            : items.Skip((int)offset).Take(pageSize).ToArray();
        return new ApiPage<T>(pageItems, page, pageSize, items.Count);
    }

    private bool TryMap(Exception exception, out IActionResult result)
    {
        switch (exception)
        {
            case ConnectionAccessDeniedException:
                result = NotFound(new { error = "connection_not_found" });
                return true;
            case NoActiveLmsConnectionException:
                result = Conflict(new { error = "lms_connection_required" });
                return true;
            case ConnectionSelectionRequiredException:
                result = Conflict(new { error = "connection_selection_required" });
                return true;
            case ArgumentException:
                result = BadRequest(new { error = "invalid_query" });
                return true;
            case UnauthorizedAccessException:
                result = Forbid();
                return true;
            case LmsProviderException providerException:
                result = MapProviderFailure(providerException);
                return true;
            default:
                result = null!;
                return false;
        }
    }

    private IActionResult MapProviderFailure(LmsProviderException exception)
    {
        var (status, error) = exception.Code switch
        {
            LmsProviderFailureCode.NotFound => (StatusCodes.Status404NotFound, "lms_data_not_found"),
            LmsProviderFailureCode.NotSupported => (StatusCodes.Status422UnprocessableEntity, "lms_data_not_supported"),
            LmsProviderFailureCode.Unauthorized => (StatusCodes.Status401Unauthorized, "lms_authorization_required"),
            LmsProviderFailureCode.RateLimited => (StatusCodes.Status429TooManyRequests, "lms_rate_limited"),
            _ => (StatusCodes.Status503ServiceUnavailable, "lms_provider_unavailable")
        };

        if (exception.RetryAfter is { } retryAfter)
        {
            Response.Headers.RetryAfter = Math.Ceiling(Math.Clamp(retryAfter.TotalSeconds, 0, 300)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return StatusCode(status, new { error });
    }
}

public sealed record ApiPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems);
