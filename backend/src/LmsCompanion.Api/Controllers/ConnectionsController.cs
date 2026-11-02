using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Application.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LmsCompanion.Api.Controllers;

[ApiController]
[Route("api/v1/connections")]
public sealed class ConnectionsController(
    IAuthenticatedUserContextResolver userContextResolver,
    IConnectionManagementService connections,
    ICanvasOAuthFlow canvasOAuthFlow,
    IPersonalCanvasConnectionService personalTokens,
    ICanvasInstitutionCatalog institutions,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("canvas/personal-token")]
    public async Task<IActionResult> ConnectPersonalToken([FromBody] PersonalCanvasTokenRequest request,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null) return Forbid();
        try { return Ok(await personalTokens.ConnectAsync(user, request.InstitutionId, request.Token, cancellationToken)); }
        catch (LmsProviderException exception) when (exception.Code == LmsProviderFailureCode.Unauthorized)
        { return UnprocessableEntity(new { error = "canvas_token_rejected" }); }
        catch (LmsProviderException exception) when (exception.Code == LmsProviderFailureCode.NotSupported)
        { return NotFound(); }
        catch (LmsProviderException exception) when (exception.Code == LmsProviderFailureCode.NotFound)
        { return NotFound(); }
    }

    [HttpGet("institutions")]
    public async Task<ActionResult<IReadOnlyList<InstitutionSummaryDto>>> GetInstitutions(
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null) return Forbid();
        return Ok(institutions.ListForTenant(user.TenantId));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectionSummaryDto>>> GetConnections(
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        return Ok(await connections.GetConnectionsAsync(user, cancellationToken));
    }

    [HttpPost("canvas/start")]
    public async Task<IActionResult> StartCanvasConnection(
        [FromBody] StartCanvasConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        try
        {
            var authorizationUri = await canvasOAuthFlow.StartAsync(user, request.InstitutionId, cancellationToken);
            return Ok(new { authorizationUrl = authorizationUri.AbsoluteUri });
        }
        catch (LmsProviderException exception) when (exception.Code == LmsProviderFailureCode.NotFound)
        {
            return NotFound(new { error = "canvas_institution_not_configured" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [AllowAnonymous]
    [HttpGet("canvas/callback")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CanvasCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        // The callback URL contains an authorization code and state. Do not carry it
        // to the portal as a browser referrer or cache the redirect response.
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(new { error = "canvas_authorization_failed" });
        }

        try
        {
            var connection = await canvasOAuthFlow.CompleteAsync(code, state, error, cancellationToken);
            if (TryGetCanvasCallbackUri("connected", out var callbackUri))
            {
                return Redirect(callbackUri.AbsoluteUri);
            }

            return Ok(connection);
        }
        catch (CanvasAuthorizationDeniedException)
        {
            if (TryGetCanvasCallbackUri("error", out var callbackUri)) return Redirect(callbackUri.AbsoluteUri);
            return BadRequest(new { error = "canvas_authorization_denied" });
        }
        catch (ConnectionAccessDeniedException)
        {
            if (TryGetCanvasCallbackUri("error", out var callbackUri)) return Redirect(callbackUri.AbsoluteUri);
            return BadRequest(new { error = "canvas_authorization_failed" });
        }
        catch (LmsProviderException)
        {
            if (TryGetCanvasCallbackUri("error", out var callbackUri)) return Redirect(callbackUri.AbsoluteUri);
            return BadRequest(new { error = "canvas_authorization_failed" });
        }
    }

    [HttpDelete("{connectionId:guid}")]
    public async Task<IActionResult> Disconnect(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        try
        {
            await connections.DisconnectAsync(user, connectionId, cancellationToken);
            return NoContent();
        }
        catch (ConnectionAccessDeniedException)
        {
            return NotFound();
        }
    }

    private async Task<AuthenticatedUserContext?> ResolveUserAsync(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue("sub");
        var tenantClaimName = configuration["Authentication:TenantClaim"] ?? "tenant_id";
        var tenantClaim = User.FindFirstValue(tenantClaimName);
        if (string.IsNullOrWhiteSpace(subject)
            || !Guid.TryParse(tenantClaim, out var tenantId)
            || tenantId == Guid.Empty)
        {
            return null;
        }

        return await userContextResolver.ResolveAsync(subject, tenantId, cancellationToken);
    }

    private bool TryGetCanvasCallbackUri(string result, out Uri callbackUri)
    {
        callbackUri = null!;
        var configuredUri = configuration["Frontend:CanvasCallbackUri"];
        if (!Uri.TryCreate(configuredUri, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback))
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return false;
        }

        callbackUri = new UriBuilder(baseUri)
        {
            Query = $"canvas={Uri.EscapeDataString(result)}"
        }.Uri;
        return true;
    }
}

public sealed record StartCanvasConnectionRequest(
    [Required, StringLength(128, MinimumLength = 1)] string InstitutionId);
public sealed record PersonalCanvasTokenRequest(
    [Required, StringLength(128, MinimumLength = 1)] string InstitutionId,
    [Required, StringLength(4096, MinimumLength = 1)] string Token);
