using LmsCompanion.Application.Contracts;

namespace LmsCompanion.Application.Abstractions;

public interface IConnectionManagementService
{
    Task<IReadOnlyList<ConnectionSummaryDto>> GetConnectionsAsync(
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task DisconnectAsync(
        AuthenticatedUserContext user,
        Guid connectionId,
        CancellationToken cancellationToken);
}
