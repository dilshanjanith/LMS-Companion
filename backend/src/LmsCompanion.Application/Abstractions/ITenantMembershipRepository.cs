using LmsCompanion.Domain;

namespace LmsCompanion.Application.Abstractions;

public interface ITenantMembershipRepository
{
    Task<AuthenticatedUserContext?> ResolveAsync(
        string externalSubject,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
}
