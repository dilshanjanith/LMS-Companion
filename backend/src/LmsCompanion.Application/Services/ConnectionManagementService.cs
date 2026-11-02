using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Application.Services;

public sealed class ConnectionManagementService(
    ILmsConnectionRepository connections,
    ICredentialVault credentials,
    ICanvasOAuthClient canvasOAuthClient,
    IAuditEventRepository auditEvents,
    TimeProvider timeProvider) : IConnectionManagementService
{
    public async Task<IReadOnlyList<ConnectionSummaryDto>> GetConnectionsAsync(
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        var ownedConnections = await connections.GetForUserAsync(user.UserId, user.TenantId, cancellationToken);
        return ownedConnections
            .Where(connection => connection.UserId == user.UserId && connection.TenantId == user.TenantId)
            .Select(CanvasOAuthFlow.ToSummary)
            .ToArray();
    }

    public async Task DisconnectAsync(
        AuthenticatedUserContext user,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var connection = await connections.GetOwnedAsync(
            connectionId,
            user.UserId,
            user.TenantId,
            cancellationToken)
            ?? throw new ConnectionAccessDeniedException();

        if (connection.Status == LmsConnectionStatus.Disconnected)
        {
            return;
        }

        var revoked = false;
        var credentialReference = connection.CredentialReference;
        if (connection.Provider == LmsProviderType.Canvas && !string.IsNullOrWhiteSpace(credentialReference))
        {
            var storedCredential = await credentials.RetrieveAsync(
                credentialReference,
                user.TenantId,
                user.UserId,
                cancellationToken);

            if (storedCredential is not null
                && Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri)
                && baseUri.Scheme == Uri.UriSchemeHttps)
            {
                try
                {
                    revoked = await canvasOAuthClient.RevokeAsync(baseUri, storedCredential.AccessToken, cancellationToken);
                }
                catch (HttpRequestException)
                {
                    revoked = false;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    revoked = false;
                }
            }
        }

        var disconnected = connection with
        {
            CredentialReference = null,
            Status = LmsConnectionStatus.Disconnected
        };
        await connections.UpdateAsync(disconnected, cancellationToken);

        if (!string.IsNullOrWhiteSpace(credentialReference))
        {
            await credentials.DeleteAsync(credentialReference, user.TenantId, user.UserId, cancellationToken);
        }

        await auditEvents.AppendAsync(new AuditEvent(
            Guid.NewGuid(),
            user.TenantId,
            user.UserId,
            revoked ? "lms.connection.disconnected_revoked" : "lms.connection.disconnected",
            connection.Id.ToString("N"),
            timeProvider.GetUtcNow()), cancellationToken);
    }
}
