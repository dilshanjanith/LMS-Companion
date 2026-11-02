using LmsCompanion.Application;

namespace LmsCompanion.Application.Abstractions;

public interface ICanvasAccessTokenService
{
    Task<string> GetAccessTokenAsync(
        AuthenticatedUserContext user,
        Guid connectionId,
        CancellationToken cancellationToken,
        bool forceRefresh = false);
}
