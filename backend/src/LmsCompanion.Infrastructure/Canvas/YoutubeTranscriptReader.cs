using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LmsCompanion.Infrastructure.Canvas;

internal static partial class YoutubeTranscriptReader
{
    internal sealed record Result(string Status, string? Content, string? Detail);

    public static async Task<Result> ReadAsync(Uri videoUrl, CancellationToken ct)
    {
        var id = videoUrl.Host == "youtu.be"
            ? videoUrl.AbsolutePath.Trim('/')
            : videoUrl.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal)
                ? videoUrl.AbsolutePath[7..]
                : System.Web.HttpUtility.ParseQueryString(videoUrl.Query)["v"];
        if (id is null || id == "videoseries" || !VideoIdRegex().IsMatch(id))
            return new("transcriptRequired", null, "This link does not identify one YouTube video.");
        var script = Path.Combine(AppContext.BaseDirectory, "Canvas", "youtube_transcript.py");
        if (!File.Exists(script))
            return new("transcriptRequired", null, "The server's YouTube caption helper is not installed.");
        var start = new ProcessStartInfo("python")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(id);
        try
        {
            using var process = new Process { StartInfo = start };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(35));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await error;
                var result = JsonSerializer.Deserialize<Result>(await output,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return process.ExitCode == 0 && result?.Status is "available" or "transcriptRequired" or "tooLarge"
                    ? result!
                    : new("transcriptRequired", null, "The YouTube caption helper could not read this video.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return new("transcriptRequired", null, "YouTube caption retrieval timed out.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or JsonException)
        {
            return new("transcriptRequired", null, "The server needs Python and yt-dlp for automatic YouTube captions.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdRegex();
}
