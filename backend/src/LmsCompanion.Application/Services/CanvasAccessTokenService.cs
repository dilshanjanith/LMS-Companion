using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Application.Services;

/// <summary>Returns owner-scoped Canvas access tokens and refreshes them shortly before expiry.</summary>
public sealed class CanvasAccessTokenService(
    ILmsConnectionRepository connections,
    ICredentialVault credentials,
    ICanvasInstitutionCatalog institutions,
    ICanvasOAuthClient oauthClient,
    IAuditEventRepository auditEvents,
    TimeProvider timeProvider,
    IPersonalTokenPolicy personalTokenPolicy) : ICanvasAccessTokenService
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);

    public async Task<string> GetAccessTokenAsync(
        AuthenticatedUserContext user,
        Guid connectionId,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var connection = await connections.GetOwnedAsync(connectionId, user.UserId, user.TenantId, cancellationToken)
            ?? throw new ConnectionAccessDeniedException();
        if (connection.Provider != LmsProviderType.Canvas
            || connection.Status != LmsConnectionStatus.Connected
            || string.IsNullOrWhiteSpace(connection.CredentialReference))
        {
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized, "The Canvas connection requires reconnection.");
        }

        // Recheck institutional approval even while an existing token is still valid.
        // Disabling an institution must stop reads, not only new connections/refreshes.
        var institution = Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri)
            && baseUri.Scheme == Uri.UriSchemeHttps
                ? institutions.FindForTenantByBaseUri(baseUri, user.TenantId) : null;
        var personalToken = connection.Scopes.Contains("personal-token", StringComparer.Ordinal)
            && personalTokenPolicy.Enabled
            && institutions.ListForTenant(user.TenantId).Any(item => item.BaseUrl == connection.BaseUrl);
        if (institution is null && !personalToken)
        {
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized, "The Canvas institution is not enabled.");
        }

        var credentialReference = connection.CredentialReference;
        var token = await credentials.RetrieveAsync(credentialReference, user.TenantId, user.UserId, cancellationToken);
        if (token is null)
        {
            await RequireReconnectionAsync(connection, cancellationToken);
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized, "The Canvas connection requires reconnection.");
        }

        var now = timeProvider.GetUtcNow();
        if (!forceRefresh && (connection.ExpiresAt is null || connection.ExpiresAt > now.Add(RefreshWindow)))
        {
            return token.AccessToken;
        }

        if (personalToken || institution is null || string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            await RequireReconnectionAsync(connection, cancellationToken);
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized, "The Canvas connection requires reconnection.");
        }

        CanvasTokenSet refreshed;
        try
        {
            refreshed = await oauthClient.RefreshAsync(institution, token.RefreshToken, cancellationToken);
        }
        catch (LmsProviderException exception) when (exception.Code == LmsProviderFailureCode.Unauthorized)
        {
            await RequireReconnectionAsync(connection, cancellationToken);
            throw;
        }

        await credentials.ReplaceAsync(
            credentialReference,
            user.TenantId,
            user.UserId,
            refreshed.AccessToken,
            refreshed.RefreshToken ?? token.RefreshToken,
            cancellationToken);
        await connections.UpdateAsync(connection with
        {
            ExpiresAt = refreshed.ExpiresAt,
            Scopes = refreshed.Scopes.Length == 0 ? connection.Scopes : refreshed.Scopes
        }, cancellationToken);
        await auditEvents.AppendAsync(new AuditEvent(
            Guid.NewGuid(),
            user.TenantId,
            user.UserId,
            "canvas.token.refreshed",
            connection.Id.ToString("N"),
            now), cancellationToken);

        return refreshed.AccessToken;
    }

    private async Task RequireReconnectionAsync(LmsConnection connection, CancellationToken cancellationToken)
    {
        await connections.UpdateAsync(connection with { Status = LmsConnectionStatus.ReconnectRequired }, cancellationToken);
        await auditEvents.AppendAsync(new AuditEvent(
            Guid.NewGuid(),
            connection.TenantId,
            connection.UserId,
            "canvas.connection.reconnect_required",
            connection.Id.ToString("N"),
            timeProvider.GetUtcNow()), cancellationToken);
    }
}
