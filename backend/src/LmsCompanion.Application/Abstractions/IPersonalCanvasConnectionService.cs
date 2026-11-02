using LmsCompanion.Application.Contracts;

namespace LmsCompanion.Application.Abstractions;

public interface IPersonalCanvasConnectionService
{
    Task<ConnectionSummaryDto> ConnectAsync(AuthenticatedUserContext user, string institutionId,
        string token, CancellationToken cancellationToken);
}
