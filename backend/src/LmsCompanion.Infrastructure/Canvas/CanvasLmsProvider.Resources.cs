using System.Net;
using System.Text;
using LmsCompanion.Application;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;
using UglyToad.PdfPig;

namespace LmsCompanion.Infrastructure.Canvas;

public sealed partial class CanvasLmsProvider
{
    private const int MaxResourceBytes = 8 * 1024 * 1024;
    private const int MaxResourceText = 30000;
    private const int MaxTotalResourceText = 1_000_000;

    public async Task<LinkedResourceDto> GetLinkedResourceAsync(LmsRequestContext context, string courseId,
        string pageSlug, string resourceUrl, CancellationToken ct, int startOffset = 0)
    {
        if (startOffset < 0 || startOffset > MaxTotalResourceText)
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        var page = await GetCoursePageAsync(context, courseId, pageSlug, ct);
        var resource = page.Resources.SingleOrDefault(item => item.Url == resourceUrl)
            ?? throw new LmsProviderException(LmsProviderFailureCode.NotFound, "The resource is not linked from this page.");
        var uri = new Uri(resource.Url);
        LinkedResourceDto Result(string status, string? content = null, string? detail = null, bool truncated = false,
            int? nextOffset = null) =>
            new(page.Url, resource.Url, resource.Title, status, content, detail, truncated, nextOffset, DateTimeOffset.UtcNow);
        LinkedResourceDto Segment(string text, string detail)
        {
            var limited = text.Length > MaxTotalResourceText;
            var availableLength = Math.Min(text.Length, MaxTotalResourceText);
            if (startOffset >= availableLength)
                return Result("unavailable", detail: limited
                    ? "The remaining text exceeds the 1,000,000-character extraction limit."
                    : "The requested offset is past the end of this resource.");
            var length = Math.Min(MaxResourceText, availableLength - startOffset);
            var nextOffset = startOffset + length < availableLength ? startOffset + length : (int?)null;
            return Result("available", text.Substring(startOffset, length), limited && nextOffset is null
                ? "Text beyond the 1,000,000-character extraction limit is unavailable."
                : detail, nextOffset is not null || limited, nextOffset);
        }

        if (resource.PageSlug is not null)
        {
            var linked = await GetCoursePageAsync(context, courseId, resource.PageSlug, ct);
            return Result("available", linked.Content, linked.ContentScope, linked.ContentTruncated);
        }
        if (uri.Host is "youtube.com" or "www.youtube.com" or "youtu.be" or "www.youtube-nocookie.com")
        {
            var captions = await YoutubeTranscriptReader.ReadAsync(uri, ct);
            return captions.Status == "available" && !string.IsNullOrWhiteSpace(captions.Content)
                ? Segment(captions.Content, captions.Detail ?? "YouTube captions.")
                : Result(captions.Status, detail: captions.Detail);
        }
        if (!AllowedResourceHost(uri.Host))
            return Result("unsupported", detail: "This resource host needs a separate authorized integration or a supplied file.");

        var download = DownloadUri(uri);
        if (download is null)
            return Result("unsupported", detail: "This Google resource type cannot be exported as text by this connection.");
        try
        {
            for (var redirects = 0; redirects < 4; redirects++)
            {
                if (!AllowedResourceHost(download.Host))
                    return Result("accessRequired", detail: download.Host == "accounts.google.com"
                        ? "Google sign-in is required to read this resource."
                        : "The resource redirected outside the supported Google download hosts.");
                using var request = new HttpRequestMessage(HttpMethod.Get, download);
                request.Headers.Accept.ParseAdd("application/pdf, text/plain, text/html");
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (response.Headers.Location is null) return Result("accessRequired", detail: "Google did not provide a usable download URL.");
                    download = new Uri(download, response.Headers.Location);
                    continue;
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return Result("accessRequired", detail: "Sign in to the Google account that can read this resource.");
                if (!response.IsSuccessStatusCode)
                    return Result("unavailable", detail: "The linked resource could not be downloaded.");
                if (response.Content.Headers.ContentLength is > MaxResourceBytes)
                    return Result("tooLarge", detail: "This resource exceeds the 8 MB retrieval limit.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var block = new byte[32768];
                while (true)
                {
                    var count = await input.ReadAsync(block, ct);
                    if (count == 0) break;
                    if (buffer.Length + count > MaxResourceBytes)
                        return Result("tooLarge", detail: "This resource exceeds the 8 MB retrieval limit.");
                    buffer.Write(block, 0, count);
                }
                var bytes = buffer.ToArray();
                string text;
                if (bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual("%PDF"u8))
                {
                    try
                    {
                        using var pdf = PdfDocument.Open(new MemoryStream(bytes));
                        var output = new StringBuilder();
                        foreach (var pdfPage in pdf.GetPages())
                        {
                            output.AppendLine(pdfPage.Text);
                            if (output.Length > MaxTotalResourceText) break;
                        }
                        text = output.ToString();
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        return Result("unavailable", detail: "The PDF text could not be extracted.");
                    }
                }
                else
                {
                    var mediaType = response.Content.Headers.ContentType?.MediaType;
                    if (mediaType is not ("text/plain" or "text/html"))
                        return Result("unsupported", detail: "The download format does not contain supported text.");
                    text = Encoding.UTF8.GetString(bytes);
                    if (mediaType == "text/html")
                    {
                        if (uri.Host == "drive.google.com" || text.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
                            return Result("accessRequired", detail: "Google requires a separate account connection to read this resource.");
                        if (IsPublishedGooglePresentation(uri))
                            return Result("unavailable", detail: "This published Google Slides viewer did not provide a text export. An original Google Slides link with download access is needed to read the slides.");
                        text = PagePlainText(InactivePageMarkupRegex().Replace(text, " "));
                    }
                }
                if (string.IsNullOrWhiteSpace(text))
                    return Result("unavailable", detail: "The resource has no extractable text; it may be scanned or image based.");
                return Segment(text, "Extracted text only; images, layout, speaker notes, and embedded media are not included.");
            }
            return Result("accessRequired", detail: "The resource redirected too many times.");
        }
        catch (HttpRequestException)
        {
            return Result("unavailable", detail: "The resource download failed.");
        }
    }

    private static bool AllowedResourceHost(string host) => host is "drive.google.com" or "docs.google.com" or "drive.usercontent.google.com"
        || host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static Uri? DownloadUri(Uri uri)
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (IsPublishedGooglePresentation(uri)) return uri;
        if (uri.Host == "drive.google.com" && parts is ["file", "d", var fileId, ..] && SafeGoogleId(fileId))
            return new Uri($"https://drive.google.com/uc?export=download&id={Uri.EscapeDataString(fileId)}");
        if (uri.Host == "docs.google.com" && parts.Length >= 3 && parts[1] == "d" && SafeGoogleId(parts[2]))
        {
            var kind = parts[0];
            var format = kind == "document" ? "txt" : kind == "presentation" ? "pdf" : null;
            if (format is not null) return new Uri($"https://docs.google.com/{kind}/d/{parts[2]}/export?format={format}");
        }
        if (uri.Host == "docs.google.com" && parts is ["forms", ..] && uri.AbsolutePath.EndsWith("/viewform", StringComparison.Ordinal))
            return uri;
        return null;
    }

    private static bool IsPublishedGooglePresentation(Uri uri)
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return uri.Host == "docs.google.com"
            && parts is ["presentation", "d", "e", var publishedId, "pubembed" or "pub"]
            && SafeGoogleId(publishedId);
    }

    private static bool SafeGoogleId(string id) => id.Length is >= 10 and <= 256
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
