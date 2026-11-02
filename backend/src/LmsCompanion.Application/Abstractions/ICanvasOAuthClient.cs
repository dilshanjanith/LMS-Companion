using LmsCompanion.Application.Contracts;

namespace LmsCompanion.Application.Abstractions;

public interface ICanvasOAuthClient
{
    Uri CreateAuthorizationUri(CanvasInstitutionConfiguration institution, string state);

    Task<CanvasTokenSet> ExchangeCodeAsync(
        CanvasInstitutionConfiguration institution,
        string code,
        CancellationToken cancellationToken);

    Task<CanvasTokenSet> RefreshAsync(
        CanvasInstitutionConfiguration institution,
        string refreshToken,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        Uri canvasBaseUri,
        string accessToken,
        CancellationToken cancellationToken);
}
