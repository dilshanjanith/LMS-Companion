using System.Net;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Infrastructure.Canvas;

namespace LmsCompanion.IntegrationTests;

public sealed class CanvasOAuthClientTests
{
    [Fact]
    public async Task Refresh_uses_the_official_form_grant_and_reuses_existing_refresh_token()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new CanvasOAuthClient(httpClient, new FixedTimeProvider(
            new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)));
        var institution = new CanvasInstitutionConfiguration(
            "north-campus",
            Guid.NewGuid(),
            new Uri("https://canvas.example.edu/"),
            "client-id",
            "client-secret",
            new Uri("https://companion.example.edu/api/v1/connections/canvas/callback"),
            ["url:GET|/api/v1/courses"]);

        var result = await client.RefreshAsync(institution, "existing-refresh-token", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://canvas.example.edu/login/oauth2/token"), handler.RequestUri);
        Assert.Contains("grant_type=refresh_token", handler.Body, StringComparison.Ordinal);
        Assert.Contains("refresh_token=existing-refresh-token", handler.Body, StringComparison.Ordinal);
        Assert.Contains("client_secret=client-secret", handler.Body, StringComparison.Ordinal);
        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal("existing-refresh-token", result.RefreshToken);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero), result.ExpiresAt);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"new-access-token\",\"expires_in\":3600,\"scope\":\"url:GET|/api/v1/courses\"}")
            };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
