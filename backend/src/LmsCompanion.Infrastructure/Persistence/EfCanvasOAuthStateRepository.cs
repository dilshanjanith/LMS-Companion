using System.Data;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using Microsoft.EntityFrameworkCore;

namespace LmsCompanion.Infrastructure.Persistence;

public sealed class EfCanvasOAuthStateRepository(CompanionDbContext database) : ICanvasOAuthStateRepository
{
    public async Task AddAsync(CanvasOAuthState state, CancellationToken cancellationToken)
    {
        database.CanvasOAuthStates.Add(state);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<CanvasOAuthState?> ConsumeAsync(
        string stateHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var savedState = await database.CanvasOAuthStates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.StateHash == stateHash, cancellationToken);
        if (savedState is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var deletedCount = await database.CanvasOAuthStates
            .Where(item => item.StateHash == stateHash)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return deletedCount == 1 && savedState.ExpiresAt > now ? savedState : null;
    }
}
