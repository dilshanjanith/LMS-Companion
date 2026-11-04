using System.Net;
using System.Net.Http.Json;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;
using LmsCompanion.Infrastructure.Canvas;

namespace LmsCompanion.IntegrationTests;

public sealed class CanvasCoursePageTests
{
    private static readonly LmsRequestContext Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        LmsProviderType.Canvas, new Uri("https://canvas.example.test"), "test-reference");

    [Fact]
    public async Task Direct_page_works_without_modules_or_page_listing_and_preserves_links_and_embeds()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            if (request.RequestUri!.AbsolutePath.EndsWith("/modules")) return Response(Array.Empty<object>());
            Assert.Equal("/api/v1/courses/322/pages/learning-path", request.RequestUri.AbsolutePath);
            return Response(new { url = "learning-path", title = "Learning Path", published = true, body = """
                <h2>Learn &amp; Apply</h2><script>privateScript()</script><style>.hidden {}</style>
                <a href="/courses/322/pages/part-1-%7C-learn"><b>Learn</b> Part 1</a>
                <a href='/courses/322/pages/part-1-apply'>Apply Part 1</a>
                <iframe src="https://slides.example.test/embed?a=1&amp;b=2" title="Session slides"></iframe>
                <a href="https://reading.example.test/chapter.pdf">Read chapter</a>
                <a href="/courses/999/pages/other">Other course</a>
                <a href="javascript:alert(1)">Unsafe</a><a href="#section">Section</a>
                <a href="https://example.test/?access_token=secret">Token</a>
                <a href="https://user:password@example.test/">Credentials</a>
                <a href="/courses/322/pages/part-1-apply">Duplicate</a>
                """ });
        }));
        var provider = new CanvasLmsProvider(http, new Tokens());

        Assert.Empty(await provider.GetCourseModulesAsync(Context, "322", default));
        var page = await provider.GetCoursePageAsync(Context, "322", "learning-path", default);

        Assert.StartsWith("Learn & Apply", page.Content);
        Assert.DoesNotContain("privateScript", page.Content);
        Assert.DoesNotContain(".hidden", page.Content);
        Assert.Equal("https://canvas.example.test/courses/322/pages/learning-path", page.Url);
        Assert.Equal(5, page.Resources.Count);
        Assert.Equal("part-1-%7C-learn", page.Resources[0].PageSlug);
        Assert.Equal("Learn Part 1", page.Resources[0].Title);
        Assert.Equal("CanvasPage", page.Resources[0].Type);
        Assert.Equal("part-1-apply", page.Resources[1].PageSlug);
        Assert.Equal("https://slides.example.test/embed?a=1&b=2", page.Resources[2].Url);
        Assert.Equal("Embed", page.Resources[2].Type);
        Assert.Null(page.Resources[4].PageSlug);
        Assert.Contains("not been retrieved", page.ContentScope);
        Assert.False(page.ContentTruncated);
        Assert.False(page.ResourcesTruncated);
        Assert.Equal(2, requests.Count); // No page listing or external-resource fetch.
    }

    [Theory]
    [InlineData("part-1-%7C-learn", "part-1-%257C-learn")]
    [InlineData("part-1-apply", "part-1-apply")]
    public async Task Slugs_are_encoded_once_without_losing_literal_percent_escapes(string slug, string encoded)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal($"/api/v1/courses/322/pages/{encoded}", request.RequestUri!.AbsolutePath);
            return Response(new { url = slug, title = "Page", body = "<p>Content</p>" });
        }));
        var result = await new CanvasLmsProvider(http, new Tokens()).GetCoursePageAsync(Context, "322", slug, default);
        Assert.Equal(slug, result.PageSlug);
        Assert.Equal($"https://canvas.example.test/courses/322/pages/{slug}", result.Url);
    }

    [Fact]
    public async Task Encoded_navigation_slug_retries_decoded_identifier_after_not_found()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return paths.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Response(new { url = "part-1-|-learn", title = "Part 1 Learn", body = "<p>Learning path</p>" });
        }));
        var page = await new CanvasLmsProvider(http, new Tokens()).GetCoursePageAsync(Context, "322", "part-1-%7C-learn", default);
        Assert.Equal("Part 1 Learn", page.Title);
        Assert.Equal(["/api/v1/courses/322/pages/part-1-%257C-learn",
            "/api/v1/courses/322/pages/part-1-%7C-learn"], paths);
    }

    [Fact]
    public async Task Authorization_failure_on_encoded_slug_does_not_retry()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }));
        var error = await Assert.ThrowsAsync<LmsProviderException>(() =>
            new CanvasLmsProvider(http, new Tokens()).GetCoursePageAsync(Context, "322", "part-1-%7C-learn", default));
        Assert.Equal(LmsProviderFailureCode.Unauthorized, error.Code);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Locked_or_unpublished_pages_do_not_expose_the_body(bool locked, bool published)
    {
        using var http = new HttpClient(new Handler(_ => Response(new { url = "page", title = "Page",
            body = "Hidden content", locked_for_user = locked, published })));
        var error = await Assert.ThrowsAsync<LmsProviderException>(() =>
            new CanvasLmsProvider(http, new Tokens()).GetCoursePageAsync(Context, "322", "page", default));
        Assert.Equal(LmsProviderFailureCode.NotFound, error.Code);
        Assert.DoesNotContain("Hidden content", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, LmsProviderFailureCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, LmsProviderFailureCode.NotFound)]
    public async Task Denied_or_missing_page_remains_an_error_without_fallback(HttpStatusCode status, LmsProviderFailureCode code)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            return new HttpResponseMessage(status) { Content = new StringContent("Private provider details") };
        }));
        var error = await Assert.ThrowsAsync<LmsProviderException>(() =>
            new CanvasLmsProvider(http, new Tokens()).GetCoursePageAsync(Context, "322", "page", default));
        Assert.Equal(code, error.Code);
        Assert.Equal(1, calls);
        Assert.DoesNotContain("Private provider details", error.Message);
    }

    [Fact]
    public async Task Missing_body_is_unavailable_rather_than_an_empty_success()
    {
        using var http = new HttpClient(new Handler(_ => Response(new { url = "page", title = "Page" })));
        await Assert.ThrowsAsync<LmsProviderException>(() =>
            new CanvasLmsProvider(http, new Tokens()).GetCoursePageAsync(Context, "322", "page", default));
    }

    [Fact]
    public async Task Text_and_resource_limits_are_reported()
    {
        var body = "<p>" + new string('x', 20001) + "</p>" + string.Concat(Enumerable.Range(0, 105)
            .Select(i => $"<a href='https://reading.example.test/{i}'>Reading {i}</a>"));
        using var http = new HttpClient(new Handler(_ => Response(new { url = "page", title = "Page", body })));
        var page = await new CanvasLmsProvider(http, new Tokens()).GetCoursePageAsync(Context, "322", "page", default);
        Assert.Equal(20000, page.Content.Length);
        Assert.Equal(100, page.Resources.Count);
        Assert.True(page.ContentTruncated);
        Assert.True(page.ResourcesTruncated);
    }

    [Fact]
    public async Task Linked_google_document_uses_page_membership_and_never_sends_canvas_token_to_google()
    {
        const string link = "https://docs.google.com/document/d/1234567890abcdef/edit";
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "canvas.example.test")
                return Response(new { url = "page", title = "Page", body = $"<a href='{link}'>Reading</a>" });
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("/document/d/1234567890abcdef/export", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Actual document text") };
        }));
        var result = await new CanvasLmsProvider(http, new Tokens()).GetLinkedResourceAsync(Context, "322", "page", link, default);
        Assert.Equal("available", result.Status);
        Assert.Equal("Actual document text", result.Content);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task Unlinked_resource_is_rejected_before_any_external_request()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Response(new { url = "page", title = "Page", body = "<p>No links</p>" });
        }));
        await Assert.ThrowsAsync<LmsProviderException>(() => new CanvasLmsProvider(http, new Tokens())
            .GetLinkedResourceAsync(Context, "322", "page", "https://docs.google.com/document/d/1234567890abcdef/edit", default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Private_google_resource_reports_access_required_without_guessing_content()
    {
        const string link = "https://drive.google.com/file/d/1234567890abcdef/view";
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Host == "canvas.example.test"
            ? Response(new { url = "page", title = "Page", body = $"<a href='{link}'>PDF</a>" })
            : new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var result = await new CanvasLmsProvider(http, new Tokens()).GetLinkedResourceAsync(Context, "322", "page", link, default);
        Assert.Equal("accessRequired", result.Status);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Public_drive_download_follows_the_googleusercontent_download_host_without_a_canvas_token()
    {
        const string link = "https://drive.google.com/file/d/1234567890abcdef/view";
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "canvas.example.test")
                return Response(new { url = "page", title = "Page", body = $"<a href='{link}'>Reading</a>" });
            Assert.Null(request.Headers.Authorization);
            if (request.RequestUri.Host == "drive.google.com")
                return new HttpResponseMessage(HttpStatusCode.SeeOther)
                { Headers = { Location = new Uri("https://drive.usercontent.google.com/download?id=1234567890abcdef") } };
            Assert.Equal("drive.usercontent.google.com", request.RequestUri.Host);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Downloaded text") };
        }));
        var result = await new CanvasLmsProvider(http, new Tokens()).GetLinkedResourceAsync(Context, "322", "page", link, default);
        Assert.Equal("available", result.Status);
        Assert.Equal("Downloaded text", result.Content);
    }

    [Fact]
    public async Task Published_google_slides_embed_reports_google_sign_in_instead_of_unsupported()
    {
        const string link = "https://docs.google.com/presentation/d/e/2PACX-1vTQf8K3TMEajpFMNHOdV2Sd43qgYWKaccM2sxyGAneTy-WqKuR8tKY14hYt4DS0N1tpJEU_eU1RJx1P/pubembed?start=false&loop=false&delayms=3000";
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "canvas.example.test")
                return Response(new { url = "business-model-canvas", title = "BMC", body = $"<iframe src='{link.Replace("&", "&amp;")}' title='South Asia regional deck'></iframe>" });
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(link, request.RequestUri.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.Found)
                { Headers = { Location = new Uri("https://accounts.google.com/v3/signin/accountchooser") } };
        }));
        var result = await new CanvasLmsProvider(http, new Tokens())
            .GetLinkedResourceAsync(Context, "322", "business-model-canvas", link, default);
        Assert.Equal("accessRequired", result.Status);
        Assert.Null(result.Content);
        Assert.Contains("Google sign-in", result.Detail);
    }

    [Fact]
    public async Task Published_slide_viewer_markup_is_not_misrepresented_as_slide_content()
    {
        const string link = "https://docs.google.com/presentation/d/e/2PACX-1234567890abcdef/pubembed";
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Host == "canvas.example.test"
            ? Response(new { url = "page", title = "Page", body = $"<iframe src='{link}'></iframe>" })
            : new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("<html><body>Google Slides viewer controls</body></html>", System.Text.Encoding.UTF8, "text/html") }));
        var result = await new CanvasLmsProvider(http, new Tokens())
            .GetLinkedResourceAsync(Context, "322", "page", link, default);
        Assert.Equal("unavailable", result.Status);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Long_document_can_be_read_in_order_without_losing_the_middle_or_end()
    {
        const string link = "https://docs.google.com/document/d/1234567890abcdef/edit";
        var full = new string('a', 30000) + new string('b', 30000) + "THE END";
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Host == "canvas.example.test"
            ? Response(new { url = "page", title = "Page", body = $"<a href='{link}'>Chapter</a>" })
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(full) }));
        var provider = new CanvasLmsProvider(http, new Tokens());
        var first = await provider.GetLinkedResourceAsync(Context, "322", "page", link, default);
        var secondOffset = Assert.IsType<int>(first.NextOffset);
        Assert.Equal(30000, secondOffset);
        Assert.True(first.Truncated);
        var second = await provider.GetLinkedResourceAsync(Context, "322", "page", link, default, secondOffset);
        var thirdOffset = Assert.IsType<int>(second.NextOffset);
        Assert.Equal(60000, thirdOffset);
        var third = await provider.GetLinkedResourceAsync(Context, "322", "page", link, default, thirdOffset);
        Assert.Null(third.NextOffset);
        Assert.False(third.Truncated);
        Assert.Equal(full, first.Content + second.Content + third.Content);
    }

    [Fact]
    public async Task YouTube_playlist_without_an_individual_video_reports_missing_transcript()
    {
        const string link = "https://www.youtube.com/embed/videoseries?list=example";
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("canvas.example.test", request.RequestUri!.Host);
            return Response(new { url = "page", title = "Page", body = $"<iframe src='{link}'></iframe>" });
        }));
        var result = await new CanvasLmsProvider(http, new Tokens()).GetLinkedResourceAsync(Context, "322", "page", link, default);
        Assert.Equal("transcriptRequired", result.Status);
        Assert.Null(result.Content);
        Assert.Contains("one YouTube video", result.Detail);
    }

    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
    private sealed class Tokens : ICanvasAccessTokenService
    {
        public Task<string> GetAccessTokenAsync(AuthenticatedUserContext user, Guid connectionId, CancellationToken cancellationToken, bool forceRefresh = false) =>
            Task.FromResult("test-token");
    }
}
