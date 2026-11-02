using LmsCompanion.Application.Abstractions;

namespace LmsCompanion.Application.Services;

public sealed class AuthenticatedUserContextResolver(ITenantMembershipRepository memberships)
    : IAuthenticatedUserContextResolver
{
    public Task<AuthenticatedUserContext?> ResolveAsync(
        string externalSubject,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        memberships.ResolveAsync(externalSubject, tenantId, cancellationToken);
}
