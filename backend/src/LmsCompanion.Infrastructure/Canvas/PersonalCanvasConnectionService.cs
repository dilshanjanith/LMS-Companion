using System.Net;
using System.Net.Http.Headers;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;
using Microsoft.Extensions.Configuration;

namespace LmsCompanion.Infrastructure.Canvas;

/// <summary>Single-user testing path. Canvas OAuth remains required for other participants.</summary>
public sealed class PersonalCanvasConnectionService(ICanvasInstitutionCatalog institutions,
    ITenantMembershipRepository memberships, ICredentialVault credentials,
    ILmsConnectionRepository connections, IAuditEventRepository audit,
    IHttpClientFactory clients, IPersonalTokenPolicy policy, TimeProvider clock) : IPersonalCanvasConnectionService
{
    public async Task<ConnectionSummaryDto> ConnectAsync(AuthenticatedUserContext user, string institutionId,
        string token, CancellationToken ct)
    {
        if (!policy.Enabled)
            throw new LmsProviderException(LmsProviderFailureCode.NotSupported, "Personal Canvas tokens are disabled.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
            throw new ArgumentException("A valid token is required.");
        if (!await memberships.IsActiveAsync(user.UserId, user.TenantId, ct))
            throw new UnauthorizedAccessException();
        var institution = institutions.ListForTenant(user.TenantId).SingleOrDefault(item => item.Id == institutionId)
            ?? throw new LmsProviderException(LmsProviderFailureCode.NotFound, "Institution unavailable.");
        // Profile access is optional in some Canvas configurations. Validate against
        // the course endpoint this connection actually needs.
        var uri = new Uri(new Uri(institution.BaseUrl), "/api/v1/courses?per_page=1");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await clients.CreateClient("CanvasPersonalTokenValidation")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized,
                "Canvas rejected the personal token. No connection was saved.");
        if (!response.IsSuccessStatusCode)
            throw new LmsProviderException(LmsProviderFailureCode.Transient,
                "Canvas validation is unavailable. No connection was saved.");

        var reference = await credentials.StoreAsync(user.TenantId, user.UserId, token, null, ct);
        var connection = new LmsConnection(Guid.NewGuid(), user.TenantId, user.UserId, LmsProviderType.Canvas,
            institution.BaseUrl, reference, ["personal-token"], LmsConnectionStatus.Connected, null);
        try
        {
            await connections.AddAsync(connection, ct);
            await audit.AppendAsync(new AuditEvent(Guid.NewGuid(), user.TenantId, user.UserId,
                "canvas.personal_token.connected", connection.Id.ToString("N"), clock.GetUtcNow()), ct);
        }
        catch
        {
            await credentials.DeleteAsync(reference, user.TenantId, user.UserId, ct);
            throw;
        }
        return new ConnectionSummaryDto(connection.Id, connection.Provider, connection.BaseUrl,
            connection.Status, connection.ExpiresAt);
    }
}
