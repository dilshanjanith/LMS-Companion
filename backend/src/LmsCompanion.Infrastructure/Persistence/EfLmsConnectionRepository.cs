using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using Microsoft.EntityFrameworkCore;

namespace LmsCompanion.Infrastructure.Persistence;

public sealed class EfLmsConnectionRepository(CompanionDbContext database) : ILmsConnectionRepository
{
    public async Task<IReadOnlyList<LmsConnection>> GetForUserAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await database.LmsConnections
            .AsNoTracking()
            .Where(connection => connection.UserId == userId && connection.TenantId == tenantId)
            .OrderBy(connection => connection.Provider)
            .ThenBy(connection => connection.BaseUrl)
            .ToArrayAsync(cancellationToken);

    public Task<LmsConnection?> GetOwnedAsync(
        Guid connectionId,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        database.LmsConnections.AsNoTracking().SingleOrDefaultAsync(
            connection => connection.Id == connectionId
                && connection.UserId == userId
                && connection.TenantId == tenantId,
            cancellationToken);

    public async Task AddAsync(LmsConnection connection, CancellationToken cancellationToken)
    {
        database.LmsConnections.Add(connection);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(LmsConnection connection, CancellationToken cancellationToken)
    {
        var existing = await database.LmsConnections.SingleOrDefaultAsync(
            item => item.Id == connection.Id
                && item.UserId == connection.UserId
                && item.TenantId == connection.TenantId,
            cancellationToken)
            ?? throw new InvalidOperationException("The LMS connection no longer exists.");

        database.Entry(existing).CurrentValues.SetValues(connection);
        await database.SaveChangesAsync(cancellationToken);
    }
}
