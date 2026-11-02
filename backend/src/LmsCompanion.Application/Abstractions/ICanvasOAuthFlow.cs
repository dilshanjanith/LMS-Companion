using LmsCompanion.Application.Contracts;

namespace LmsCompanion.Application.Abstractions;

public interface ICanvasOAuthFlow
{
    Task<Uri> StartAsync(
        AuthenticatedUserContext user,
        string institutionId,
        CancellationToken cancellationToken);

    Task<ConnectionSummaryDto> CompleteAsync(
        string? code,
        string state,
        string? providerError,
        CancellationToken cancellationToken);
}
