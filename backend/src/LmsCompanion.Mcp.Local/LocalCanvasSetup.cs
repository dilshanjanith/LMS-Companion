using System.Net.Http.Headers;
using System.Text;
using LmsCompanion.Infrastructure.Canvas;

namespace LmsCompanion.Mcp.Local;

public static class LocalCanvasSetup
{
    public static async Task RunAsync(LocalCanvasConnectionStore store)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Run setup in an interactive terminal so the Canvas token can be entered privately.");

        Console.Write("Canvas URL (for example, https://school.instructure.com/): ");
        var text = Console.ReadLine();
        if (!LocalCanvasConnectionStore.TryCanvasBaseUri(text, out var baseUri))
            throw new InvalidOperationException("Enter the root HTTPS URL of your Canvas site.");

        Console.Write("Canvas access token (input hidden): ");
        var token = ReadHiddenLine().Trim();
        Console.WriteLine();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("No Canvas token was entered.");

        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        CanvasHttpClientDefaults.Configure(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "api/v1/courses?per_page=1"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var reason = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Canvas rejected this access token. Check that it is active and was created in this Canvas site."
                : "Canvas rejected the token or URL.";
            throw new InvalidOperationException($"{reason} (HTTP {(int)response.StatusCode}). Nothing was saved.");
        }

        store.Save(baseUri, token);
        Console.WriteLine("Canvas connection saved for this Windows account. The token is encrypted at rest.");
    }

    private static string ReadHiddenLine()
    {
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) return value.ToString();
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0) value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
    }
}
