using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Application.Services;

public sealed class LmsQueryService(
    ILmsConnectionRepository connections,
    IEnumerable<ILmsProvider> providers) : ILmsQueryService
{
    private readonly IReadOnlyDictionary<LmsProviderType, ILmsProvider> _providers =
        providers.ToDictionary(provider => provider.ProviderType);

    public async Task<IReadOnlyList<CourseModuleDto>> GetCourseModulesAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, CancellationToken ct)
    {
        var (context, provider) = await ResolveCourseAsync(user, connectionId, courseId, ct);
        return await provider.GetCourseModulesAsync(context, courseId, ct);
    }

    public async Task<IReadOnlyList<ModuleItemDto>> GetModuleItemsAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, string moduleId, CancellationToken ct)
    {
        ValidateId(moduleId);
        var (context, provider) = await ResolveCourseAsync(user, connectionId, courseId, ct);
        return await provider.GetModuleItemsAsync(context, courseId, moduleId, ct);
    }

    public async Task<LearningMaterialDto> GetLearningMaterialAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, string moduleId, string itemId, CancellationToken ct)
    {
        ValidateId(moduleId);
        ValidateId(itemId);
        var (context, provider) = await ResolveCourseAsync(user, connectionId, courseId, ct);
        return await provider.GetLearningMaterialAsync(context, courseId, moduleId, itemId, ct);
    }

    private async Task<(LmsRequestContext, ILmsProvider)> ResolveCourseAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, CancellationToken ct)
    {
        ValidateId(courseId);
        var (context, provider) = await ResolveProviderAsync(user, connectionId, ct);
        var courses = await provider.GetMyCoursesAsync(context, ct);
        if (!courses.Any(course => course.Id == courseId))
            throw new LmsProviderException(LmsProviderFailureCode.NotFound, "The course is not available to this learner.");
        return (context, provider);
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            throw new ArgumentException("Invalid LMS identifier.");
    }

    public async Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        CancellationToken cancellationToken)
    {
        var (context, provider) = await ResolveProviderAsync(user, connectionId, cancellationToken);
        return await provider.GetMyCoursesAsync(context, cancellationToken);
    }

    public async Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var range = LmsDateRange.Create(from, to);
        var (context, provider) = await ResolveProviderAsync(user, connectionId, cancellationToken);
        var assignments = await provider.GetUpcomingAssignmentsAsync(
            context,
            range.From,
            range.To,
            cancellationToken);

        return assignments
            .Where(assignment => assignment.DueAt is { } dueAt && dueAt >= range.From && dueAt < range.To)
            .ToArray();
    }

    public async Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var range = LmsDateRange.Create(from, to);
        var (context, provider) = await ResolveProviderAsync(user, connectionId, cancellationToken);
        var events = await provider.GetScheduleAsync(context, range.From, range.To, cancellationToken);

        return events
            .Where(scheduleEvent => scheduleEvent.StartsAt < range.To
                && (scheduleEvent.EndsAt ?? scheduleEvent.StartsAt) >= range.From)
            .ToArray();
    }

    public async Task<CourseProgressDto> GetCourseProgressAsync(
        AuthenticatedUserContext user,
        Guid? connectionId,
        string courseId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courseId);

        var (context, provider) = await ResolveProviderAsync(user, connectionId, cancellationToken);
        var courses = await provider.GetMyCoursesAsync(context, cancellationToken);
        if (!courses.Any(course => string.Equals(course.Id, courseId, StringComparison.Ordinal)))
        {
            throw new LmsProviderException(
                LmsProviderFailureCode.NotFound,
                "The requested course is not available.");
        }

        var progress = await provider.GetCourseProgressAsync(context, courseId, cancellationToken);
        return progress with { CourseId = courseId };
    }

    private async Task<(LmsRequestContext Context, ILmsProvider Provider)> ResolveProviderAsync(
        AuthenticatedUserContext user,
        Guid? requestedConnectionId,
        CancellationToken cancellationToken)
    {
        if (user.UserId == Guid.Empty || user.TenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("An authenticated user and tenant are required.");
        }

        var userConnections = await connections.GetForUserAsync(user.UserId, user.TenantId, cancellationToken);
        var ownedConnections = userConnections
            .Where(connection => connection.UserId == user.UserId && connection.TenantId == user.TenantId)
            .ToArray();

        LmsConnection connection;
        if (requestedConnectionId is { } requestedId)
        {
            connection = ownedConnections.FirstOrDefault(item => item.Id == requestedId)
                ?? throw new ConnectionAccessDeniedException();
            if (connection.Status != LmsConnectionStatus.Connected)
            {
                throw new NoActiveLmsConnectionException();
            }
        }
        else
        {
            var activeConnections = ownedConnections
                .Where(item => item.Status == LmsConnectionStatus.Connected)
                .ToArray();

            connection = activeConnections.Length switch
            {
                0 => throw new NoActiveLmsConnectionException(),
                1 => activeConnections[0],
                _ => throw new ConnectionSelectionRequiredException()
            };
        }

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("The configured LMS connection must use a valid HTTPS base URL.");
        }

        if (string.IsNullOrWhiteSpace(connection.CredentialReference))
        {
            throw new NoActiveLmsConnectionException();
        }

        if (!_providers.TryGetValue(connection.Provider, out var provider))
        {
            throw new LmsProviderException(
                LmsProviderFailureCode.NotSupported,
                "The configured LMS provider is not available.");
        }

        var context = new LmsRequestContext(
            user.UserId,
            user.TenantId,
            connection.Id,
            connection.Provider,
            baseUri,
            connection.CredentialReference);

        return (context, provider);
    }
}
