namespace LmsCompanion.Infrastructure.Canvas;

public static class CanvasHttpClientDefaults
{
    // Canvas requires an identifiable User-Agent on API requests.
    public static void Configure(HttpClient client) =>
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LmsCompanion/0.1");
}
