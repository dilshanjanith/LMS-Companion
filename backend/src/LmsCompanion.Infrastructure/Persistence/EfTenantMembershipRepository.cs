using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LmsCompanion.Infrastructure.Persistence;

public sealed class EfTenantMembershipRepository(CompanionDbContext database) : ITenantMembershipRepository
{
    public async Task<AuthenticatedUserContext?> ResolveAsync(
        string externalSubject,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var member = await database.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId && membership.IsActive)
            .Join(
                database.AppUsers.AsNoTracking(),
                membership => membership.UserId,
                user => user.Id,
                (membership, user) => new { membership.UserId, user.ExternalSubject })
            .SingleOrDefaultAsync(member => member.ExternalSubject == externalSubject, cancellationToken);

        return member is null ? null : new AuthenticatedUserContext(member.UserId, tenantId);
    }

    public Task<bool> IsActiveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        database.TenantMemberships.AsNoTracking().AnyAsync(
            membership => membership.UserId == userId
                && membership.TenantId == tenantId
                && membership.IsActive,
            cancellationToken);
}
