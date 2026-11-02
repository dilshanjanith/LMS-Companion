using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using System.Collections.Concurrent;

namespace LmsCompanion.Infrastructure.Mock;

public sealed class MockLmsConnectionRepository : ILmsConnectionRepository
{
    private static readonly LmsConnection DemoConnection = new(
        MockLmsIdentity.ConnectionId,
        MockLmsIdentity.TenantId,
        MockLmsIdentity.UserId,
        LmsProviderType.Mock,
        "https://mock.lms.invalid",
        "mock:demo-account",
        [],
        LmsConnectionStatus.Connected,
        ExpiresAt: null);

    private readonly ConcurrentDictionary<Guid, LmsConnection> _connections =
        new(new[] { new KeyValuePair<Guid, LmsConnection>(DemoConnection.Id, DemoConnection) });

    public Task<IReadOnlyList<LmsConnection>> GetForUserAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<LmsConnection> result = _connections.Values
            .Where(connection => connection.UserId == userId && connection.TenantId == tenantId)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<LmsConnection?> GetOwnedAsync(
        Guid connectionId,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_connections.TryGetValue(connectionId, out var connection)
            && connection.UserId == userId
            && connection.TenantId == tenantId
                ? connection
                : null);
    }

    public Task AddAsync(LmsConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connections[connection.Id] = connection;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(LmsConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connections[connection.Id] = connection;
        return Task.CompletedTask;
    }
}
