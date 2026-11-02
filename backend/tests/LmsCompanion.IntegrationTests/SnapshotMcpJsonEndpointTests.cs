using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LmsCompanion.Mcp.Local;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace LmsCompanion.IntegrationTests;

[CollectionDefinition("Snapshot MCP", DisableParallelization = true)]
public sealed class SnapshotMcpCollection;

[Collection("Snapshot MCP")]
public sealed class SnapshotMcpJsonEndpointTests
{
    [Fact]
    public void Remote_bearer_authentication_rejects_missing_and_wrong_keys()
    {
        const string key = "0123456789abcdef0123456789abcdef";
        Assert.False(RemoteMcpAuthorization.IsValidBearer(null, key));
        Assert.False(RemoteMcpAuthorization.IsValidBearer("Basic " + key, key));
        Assert.False(RemoteMcpAuthorization.IsValidBearer("Bearer wrong", key));
        Assert.True(RemoteMcpAuthorization.IsValidBearer("Bearer " + key, key));
        Assert.True(RemoteMcpAuthorization.IsValid(null, key, key));
    }

    [Fact]
    public async Task Json_transport_negotiates_and_returns_imported_courses()
    {
        var home = Path.Combine(Path.GetTempPath(), "lms-companion-test-" + Guid.NewGuid().ToString("N"));
        var previousHome = Environment.GetEnvironmentVariable("LMS_COMPANION_HOME");
        Directory.CreateDirectory(home);
        try
        {
            Environment.SetEnvironmentVariable("LMS_COMPANION_HOME", home);
            var input = Path.Combine(home, "canvas.json");
            await File.WriteAllTextAsync(input,
                """[{"id":42,"name":"Test Course","course_code":"TEST","html_url":"https://seedonline.stanford.edu/courses/42"}]""");
            LocalCanvasSnapshotStore.ImportCourses("https://seedonline.stanford.edu/", input);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            await using var app = builder.Build();
            const string key = "0123456789abcdef0123456789abcdef";
            const string urlKey = "abcdef0123456789abcdef0123456789";
            SnapshotMcpJsonEndpoint.Map(app, key, urlKey);
            await app.StartAsync();
            using var client = app.GetTestClient();

            using var initialize = await PostAsync(client, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
            Assert.Equal("2025-11-25", initialize.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

            using var tools = await PostAsync(client, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var names = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()).ToArray();
            Assert.Contains("get_my_courses", names);
            Assert.Contains("get_upcoming_events", names);

            const string courseRequest = """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_my_courses","arguments":{}}}""";
            using var anonymous = await PostAsync(client, courseRequest);
            Assert.True(anonymous.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

            client.DefaultRequestHeaders.Add("x-api-key", key);
            using var call = await PostAsync(client, courseRequest);
            var result = call.RootElement.GetProperty("result");
            Assert.False(result.GetProperty("isError").GetBoolean());
            var course = result.GetProperty("structuredContent").GetProperty("items")[0];
            Assert.Equal("Test Course", course.GetProperty("name").GetString());
            Assert.Equal("42", course.GetProperty("id").GetString());

            client.DefaultRequestHeaders.Remove("x-api-key");
            using var viaUrl = await PostAsync(client, courseRequest, "/mcp/" + urlKey);
            Assert.False(viaUrl.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            using var wrongUrl = await client.PostAsJsonAsync("/mcp/wrong", JsonDocument.Parse(courseRequest).RootElement);
            Assert.Equal(HttpStatusCode.NotFound, wrongUrl.StatusCode);

            client.DefaultRequestHeaders.Add("x-api-key", key);
            using var invalid = await PostAsync(client, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_my_courses","arguments":{"unexpected":1}}}""");
            Assert.Equal(-32602, invalid.RootElement.GetProperty("error").GetProperty("code").GetInt32());

            var get = await client.GetAsync("/mcp");
            Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LMS_COMPANION_HOME", previousHome);
            Directory.Delete(home, recursive: true);
        }
    }

    private static async Task<JsonDocument> PostAsync(HttpClient client, string request, string path = "/mcp")
    {
        using var response = await client.PostAsJsonAsync(path, JsonDocument.Parse(request).RootElement);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
