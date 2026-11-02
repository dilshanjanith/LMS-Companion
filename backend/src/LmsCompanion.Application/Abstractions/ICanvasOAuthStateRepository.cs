using LmsCompanion.Domain;

namespace LmsCompanion.Application.Abstractions;

public interface ICanvasOAuthStateRepository
{
    Task AddAsync(CanvasOAuthState state, CancellationToken cancellationToken);

    /// <summary>Atomically consumes a state record; a value can be returned at most once.</summary>
    Task<CanvasOAuthState?> ConsumeAsync(
        string stateHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
