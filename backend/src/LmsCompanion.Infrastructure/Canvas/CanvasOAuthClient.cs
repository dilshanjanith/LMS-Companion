using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Application.Errors;

namespace LmsCompanion.Infrastructure.Canvas;

public sealed class CanvasOAuthClient(HttpClient httpClient, TimeProvider timeProvider) : ICanvasOAuthClient
{
    public Uri CreateAuthorizationUri(CanvasInstitutionConfiguration institution, string state)
    {
        var endpoint = new Uri(institution.BaseUri, "/login/oauth2/auth");
        var query = new Dictionary<string, string>
        {
            ["client_id"] = institution.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = institution.RedirectUri.AbsoluteUri,
            ["state"] = state,
            ["scope"] = string.Join(' ', institution.Scopes)
        };
        var encodedQuery = string.Join('&', query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new UriBuilder(endpoint) { Query = encodedQuery }.Uri;
    }

    public async Task<CanvasTokenSet> ExchangeCodeAsync(
        CanvasInstitutionConfiguration institution,
        string code,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(institution.BaseUri, "/login/oauth2/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = institution.ClientId,
                ["client_secret"] = institution.ClientSecret,
                ["redirect_uri"] = institution.RedirectUri.AbsoluteUri,
                ["code"] = code
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw MapFailure(response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<CanvasTokenResponse>(cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized, "Canvas returned an invalid authorization response.");
        }

        return ToTokenSet(payload, institution.Scopes, payload.RefreshToken);
    }

    public async Task<CanvasTokenSet> RefreshAsync(
        CanvasInstitutionConfiguration institution,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(institution.BaseUri, "/login/oauth2/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = institution.ClientId,
                ["client_secret"] = institution.ClientSecret,
                ["redirect_uri"] = institution.RedirectUri.AbsoluteUri,
                ["refresh_token"] = refreshToken
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw MapFailure(response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<CanvasTokenResponse>(cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new LmsProviderException(LmsProviderFailureCode.Unauthorized, "Canvas returned an invalid refresh response.");
        }

        return ToTokenSet(payload, institution.Scopes, refreshToken);
    }

    private CanvasTokenSet ToTokenSet(CanvasTokenResponse payload, IReadOnlyList<string> defaultScopes, string? fallbackRefreshToken)
    {
        var scopes = string.IsNullOrWhiteSpace(payload.Scope)
            ? defaultScopes.ToArray()
            : payload.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DateTimeOffset? expiresAt = payload.ExpiresIn is > 0
            ? timeProvider.GetUtcNow().AddSeconds(payload.ExpiresIn.Value)
            : null;

        return new CanvasTokenSet(payload.AccessToken!, payload.RefreshToken ?? fallbackRefreshToken, expiresAt, scopes);
    }

    public async Task<bool> RevokeAsync(
        Uri canvasBaseUri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(canvasBaseUri, "/login/oauth2/token"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static LmsProviderException MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest =>
            new LmsProviderException(LmsProviderFailureCode.Unauthorized, "Canvas authorization could not be completed."),
        HttpStatusCode.TooManyRequests =>
            new LmsProviderException(LmsProviderFailureCode.RateLimited, "Canvas is temporarily rate limiting requests."),
        _ when (int)statusCode >= 500 =>
            new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas is temporarily unavailable."),
        _ => new LmsProviderException(LmsProviderFailureCode.Unauthorized, "Canvas authorization could not be completed.")
    };

    private sealed record CanvasTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);
}
