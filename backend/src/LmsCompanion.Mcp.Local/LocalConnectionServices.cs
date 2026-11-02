using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Mcp.Local;

public sealed class LocalConnectionRepository(LocalCanvasConnection local) : ILmsConnectionRepository
{
    public Task<IReadOnlyList<LmsConnection>> GetForUserAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LmsConnection> result = userId == local.UserId && tenantId == local.TenantId
            ? [local.Connection]
            : [];
        return Task.FromResult(result);
    }

    public Task<LmsConnection?> GetOwnedAsync(
        Guid connectionId, Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<LmsConnection?>(
            connectionId == local.ConnectionId && userId == local.UserId && tenantId == local.TenantId
                ? local.Connection
                : null);
    }

    public Task AddAsync(LmsConnection connection, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Local Canvas connections are configured through setup.");

    public Task UpdateAsync(LmsConnection connection, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Local Canvas connections are configured through setup.");
}

public sealed class LocalCanvasAccessTokenService(LocalCanvasConnection local) : ICanvasAccessTokenService
{
    public Task<string> GetAccessTokenAsync(
        AuthenticatedUserContext user,
        Guid connectionId,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (user.UserId != local.UserId || user.TenantId != local.TenantId || connectionId != local.ConnectionId)
            throw new ConnectionAccessDeniedException();
        if (forceRefresh)
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized,
                "The manually generated Canvas token must be replaced through setup.");
        return Task.FromResult(local.AccessToken);
    }
}
