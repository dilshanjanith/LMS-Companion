using System.Security.Cryptography;
using System.Text;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Application.Services;

public sealed class CanvasOAuthFlow(
    ICanvasInstitutionCatalog institutions,
    ICanvasOAuthStateRepository states,
    ITenantMembershipRepository memberships,
    ICanvasOAuthClient oauthClient,
    ICredentialVault credentials,
    ILmsConnectionRepository connections,
    IAuditEventRepository auditEvents,
    TimeProvider timeProvider) : ICanvasOAuthFlow
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public async Task<Uri> StartAsync(
        AuthenticatedUserContext user,
        string institutionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId);
        if (!await memberships.IsActiveAsync(user.UserId, user.TenantId, cancellationToken))
        {
            throw new UnauthorizedAccessException("The tenant membership is not active.");
        }

        var institution = institutions.FindForTenant(institutionId, user.TenantId)
            ?? throw new LmsProviderException(
                LmsProviderFailureCode.NotFound,
                "The requested Canvas institution is not configured for this tenant.");

        var rawState = CreateState();
        var now = timeProvider.GetUtcNow();
        await states.AddAsync(new CanvasOAuthState(
            HashState(rawState),
            user.TenantId,
            user.UserId,
            institution.Id,
            institution.BaseUri.AbsoluteUri,
            institution.RedirectUri.AbsoluteUri,
            now,
            now.Add(StateLifetime)), cancellationToken);

        await auditEvents.AppendAsync(new AuditEvent(
            Guid.NewGuid(),
            user.TenantId,
            user.UserId,
            "canvas.oauth.started",
            institution.Id,
            now), cancellationToken);

        return oauthClient.CreateAuthorizationUri(institution, rawState);
    }

    public async Task<ConnectionSummaryDto> CompleteAsync(
        string? code,
        string state,
        string? providerError,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length > 128)
        {
            throw new ConnectionAccessDeniedException();
        }

        var now = timeProvider.GetUtcNow();
        var savedState = await states.ConsumeAsync(HashState(state), now, cancellationToken)
            ?? throw new ConnectionAccessDeniedException();

        if (!await memberships.IsActiveAsync(savedState.UserId, savedState.TenantId, cancellationToken))
        {
            throw new ConnectionAccessDeniedException();
        }

        if (!string.IsNullOrWhiteSpace(providerError))
        {
            await auditEvents.AppendAsync(new AuditEvent(
                Guid.NewGuid(),
                savedState.TenantId,
                savedState.UserId,
                "canvas.oauth.denied",
                savedState.InstitutionId,
                now), cancellationToken);
            throw new CanvasAuthorizationDeniedException();
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096)
        {
            throw new ConnectionAccessDeniedException();
        }

        var institution = institutions.FindForTenant(savedState.InstitutionId, savedState.TenantId)
            ?? throw new ConnectionAccessDeniedException();
        if (!string.Equals(institution.BaseUri.AbsoluteUri, savedState.BaseUrl, StringComparison.Ordinal)
            || !string.Equals(institution.RedirectUri.AbsoluteUri, savedState.RedirectUri, StringComparison.Ordinal))
        {
            throw new ConnectionAccessDeniedException();
        }

        var tokenSet = await oauthClient.ExchangeCodeAsync(institution, code, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenSet.AccessToken))
        {
            throw new LmsProviderException(
                LmsProviderFailureCode.Unauthorized,
                "Canvas did not return a usable access token.");
        }

        var credentialReference = await credentials.StoreAsync(
            savedState.TenantId,
            savedState.UserId,
            tokenSet.AccessToken,
            tokenSet.RefreshToken,
            cancellationToken);

        var connection = new LmsConnection(
            Guid.NewGuid(),
            savedState.TenantId,
            savedState.UserId,
            LmsProviderType.Canvas,
            institution.BaseUri.AbsoluteUri,
            credentialReference,
            tokenSet.Scopes.Length == 0 ? institution.Scopes.ToArray() : tokenSet.Scopes,
            LmsConnectionStatus.Connected,
            tokenSet.ExpiresAt);

        var connectionAdded = false;
        try
        {
            await connections.AddAsync(connection, cancellationToken);
            connectionAdded = true;
            await auditEvents.AppendAsync(new AuditEvent(
                Guid.NewGuid(),
                savedState.TenantId,
                savedState.UserId,
                "canvas.connection.connected",
                connection.Id.ToString("N"),
                now), cancellationToken);
        }
        catch
        {
            if (connectionAdded)
            {
                await connections.UpdateAsync(connection with
                {
                    CredentialReference = null,
                    Status = LmsConnectionStatus.Disconnected
                }, cancellationToken);
            }

            await credentials.DeleteAsync(credentialReference, savedState.TenantId, savedState.UserId, cancellationToken);
            throw;
        }

        return ToSummary(connection);
    }

    private static string CreateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashState(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))).ToLowerInvariant();

    internal static ConnectionSummaryDto ToSummary(LmsConnection connection) => new(
        connection.Id,
        connection.Provider,
        connection.BaseUrl,
        connection.Status,
        connection.ExpiresAt);
}
