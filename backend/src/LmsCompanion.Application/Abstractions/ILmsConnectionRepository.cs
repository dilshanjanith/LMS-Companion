using LmsCompanion.Domain;

namespace LmsCompanion.Application.Abstractions;

public interface ILmsConnectionRepository
{
    Task<IReadOnlyList<LmsConnection>> GetForUserAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<LmsConnection?> GetOwnedAsync(
        Guid connectionId,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task AddAsync(LmsConnection connection, CancellationToken cancellationToken);

    Task UpdateAsync(LmsConnection connection, CancellationToken cancellationToken);
}
