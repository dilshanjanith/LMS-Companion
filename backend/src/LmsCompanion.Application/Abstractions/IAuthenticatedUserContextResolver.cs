namespace LmsCompanion.Application.Abstractions;

public interface IAuthenticatedUserContextResolver
{
    Task<AuthenticatedUserContext?> ResolveAsync(
        string externalSubject,
        Guid tenantId,
        CancellationToken cancellationToken);
}
