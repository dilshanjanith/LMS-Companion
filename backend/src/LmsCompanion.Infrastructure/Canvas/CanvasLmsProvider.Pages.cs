using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LmsCompanion.Application;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;

namespace LmsCompanion.Infrastructure.Canvas;

public sealed partial class CanvasLmsProvider
{
    public async Task<CoursePageDto> GetCoursePageAsync(LmsRequestContext context, string courseId, string pageSlug, CancellationToken ct)
    {
        EnsureCanvasContext(context);
        if (string.IsNullOrWhiteSpace(pageSlug) || pageSlug.Length > 512
            || pageSlug is "." or ".." || pageSlug.IndexOfAny(['/', '\\', '?', '#']) >= 0)
            throw new ArgumentException("Supply the page slug, not a full URL.", nameof(pageSlug));

        // Canvas links can carry an encoded character as either part of the saved
        // slug or as URL syntax. Try the literal slug first, then its decoded form
        // only when Canvas reports that the first page does not exist.
        CanvasDirectPage page;
        try
        {
            page = await ReadPageAsync(context, courseId, pageSlug, ct);
        }
        catch (LmsProviderException error) when (error.Code == LmsProviderFailureCode.NotFound
            && PageEscapeRegex().IsMatch(pageSlug))
        {
            var decoded = Uri.UnescapeDataString(pageSlug);
            if (decoded == pageSlug || decoded is "." or ".." || decoded.IndexOfAny(['/', '\\', '?', '#']) >= 0)
                throw;
            page = await ReadPageAsync(context, courseId, decoded, ct);
        }
        if (page.LockedForUser || page.Published == false)
            throw new LmsProviderException(LmsProviderFailureCode.NotFound, "The requested Canvas page is locked or unpublished.");
        if (page.Body is null || string.IsNullOrWhiteSpace(page.Title) || string.IsNullOrWhiteSpace(page.Url)
            || page.Url is "." or ".." || page.Url.IndexOfAny(['/', '\\', '?', '#']) >= 0)
            throw new LmsProviderException(LmsProviderFailureCode.Transient, "Canvas did not return readable page content.");

        var source = new Uri(SafeCanvasUrl(context.ProviderBaseUri, page.HtmlUrl)
            ?? new Uri(context.ProviderBaseUri,
                $"/courses/{Uri.EscapeDataString(courseId)}/pages/{page.Url}").AbsoluteUri);
        var html = InactivePageMarkupRegex().Replace(page.Body, " ");
        var content = PagePlainText(html);
        var resources = ExtractPageResources(context.ProviderBaseUri, source, courseId, html);
        return new CoursePageDto(courseId, page.Url, page.Title, content[..Math.Min(content.Length, 20000)],
            source.AbsoluteUri, resources.Take(100).ToArray(), content.Length > 20000, resources.Count > 100,
            "Page text only. Linked documents, slides, videos, and external tools have not been retrieved; resource links are not their contents.",
            DateTimeOffset.UtcNow);
    }

    private Task<CanvasDirectPage> ReadPageAsync(LmsRequestContext context, string courseId, string pageSlug, CancellationToken ct)
    {
        var path = $"/api/v1/courses/{Uri.EscapeDataString(courseId)}/pages/{Uri.EscapeDataString(pageSlug)}";
        return GetSingleAsync<CanvasDirectPage>(context, new Uri(context.ProviderBaseUri, path), ct);
    }

    private static List<CoursePageResourceDto> ExtractPageResources(Uri canvasBase, Uri source, string courseId, string html)
    {
        var resources = new List<CoursePageResourceDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pagePrefix = $"/courses/{Uri.EscapeDataString(courseId)}/pages/";
        foreach (Match match in PageResourceRegex().Matches(html))
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var attributes = PageAttributeRegex().Matches(match.Groups["attrs"].Value)
                .Cast<Match>().GroupBy(attribute => attribute.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => WebUtility.HtmlDecode(group.First().Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
            var raw = attributes.GetValueOrDefault(tag == "a" ? "href" : "src");
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('#')
                || !Uri.TryCreate(source, raw, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo)) continue;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query.AllKeys.Any(key => string.Equals(key, "access_token", StringComparison.OrdinalIgnoreCase))) continue;
            if (!seen.Add(uri.AbsoluteUri)) continue;

            string? linkedPage = null;
            if (SafeCanvasUrl(canvasBase, uri.AbsoluteUri) is not null
                && uri.AbsolutePath.StartsWith(pagePrefix, StringComparison.Ordinal))
            {
                var segment = uri.AbsolutePath[pagePrefix.Length..];
                if (segment.Length > 0 && !segment.Contains('/')) linkedPage = segment;
            }
            var title = tag == "a" ? PagePlainText(match.Groups["text"].Value) : attributes.GetValueOrDefault("title");
            if (string.IsNullOrWhiteSpace(title)) title = uri.Host;
            resources.Add(new CoursePageResourceDto(title[..Math.Min(title.Length, 300)], uri.AbsoluteUri,
                linkedPage is not null ? "CanvasPage" : tag == "a" ? "Link" : "Embed", linkedPage));
            if (resources.Count > 100) break;
        }
        return resources;
    }

    private static string PagePlainText(string html)
    {
        var text = WebUtility.HtmlDecode(PageTagRegex().Replace(html, " "));
        return PageWhitespaceRegex().Replace(text, " ").Trim();
    }

    private sealed record CanvasDirectPage(string? Url, string? Title, string? Body,
        bool? Published, [property: JsonPropertyName("locked_for_user")] bool LockedForUser,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    [GeneratedRegex(@"<!--.*?-->|<(script|style|noscript)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 100)]
    private static partial Regex InactivePageMarkupRegex();

    [GeneratedRegex("<(?<tag>a)\\b(?<attrs>(?:[^>\"']|\"[^\"]*\"|'[^']*')*)>(?<text>.*?)</a\\s*>|<(?<tag>iframe|video|audio|source)\\b(?<attrs>(?:[^>\"']|\"[^\"]*\"|'[^']*')*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 100)]
    private static partial Regex PageResourceRegex();

    [GeneratedRegex("(?:^|\\s)(?<name>[\\w-]+)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.Singleline, 100)]
    private static partial Regex PageAttributeRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline, 100)]
    private static partial Regex PageTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, 100)]
    private static partial Regex PageWhitespaceRegex();

    [GeneratedRegex(@"%[0-9a-f]{2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex PageEscapeRegex();
}
