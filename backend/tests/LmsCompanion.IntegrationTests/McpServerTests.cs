using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using LmsCompanion.Infrastructure.Canvas;
using LmsCompanion.Domain;
using LmsCompanion.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Server;

namespace LmsCompanion.IntegrationTests;

public sealed class McpServerTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Authenticated_tool_discovery_exposes_learning_and_connection_tools()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();

        using var response = await SendRpcAsync(client, "tools/list", new { }, "tenant-a");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadRpcJsonAsync(response);
        var names = json.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal(
            new[] { "get_connection_status", "get_my_courses", "get_upcoming_assignments", "get_class_schedule", "get_course_progress", "get_course_modules", "get_module_items", "get_learning_material", "get_course_page", "get_linked_resource" }.Order(),
            names.Order());

        foreach (var tool in json.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray())
        {
            Assert.True(tool.TryGetProperty("inputSchema", out _), tool.GetRawText());
            Assert.True(tool.TryGetProperty("outputSchema", out _), tool.GetRawText());
        }
    }

    [Fact]
    public async Task Connection_status_returns_portal_and_pending_institution_without_credentials()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();
        using var response = await SendRpcAsync(client, "tools/call",
            new { name = "get_connection_status", arguments = new { } }, "tenant-a");
        using var json = await ReadRpcJsonAsync(response);
        var result = json.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.GetRawText());
        var payload = result.GetProperty("structuredContent").GetRawText();
        Assert.Contains("https://companion.example.test/dashboard", payload);
        Assert.Contains("Stanford SEED", payload);
        Assert.DoesNotContain("ClientSecret", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new AuthenticatedUserContext(UserId, TenantId),
            server.Services.GetRequiredService<FakeConnectionManagement>().LastUser);
    }

    [Fact]
    public async Task Connection_status_rejects_unprovisioned_identity_before_reading_connections()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();
        using var response = await SendRpcAsync(client, "tools/call",
            new { name = "get_connection_status", arguments = new { } }, "tenant-b");
        using var json = await ReadRpcJsonAsync(response);
        Assert.True(json.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Null(server.Services.GetRequiredService<FakeConnectionManagement>().LastUser);
    }

    [Fact]
    public async Task Valid_tool_call_uses_authenticated_tenant_and_returns_structured_course_data()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();

        using var response = await SendRpcAsync(
            client,
            "tools/call",
            new { name = "get_my_courses", arguments = new { } },
            "tenant-a");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadRpcJsonAsync(response);
        var result = json.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), result.GetRawText());
        Assert.True(result.TryGetProperty("structuredContent", out var content), result.GetRawText());
        Assert.Contains("course-a", content.GetRawText(), StringComparison.Ordinal);

        var query = server.Services.GetRequiredService<FakeLmsQueryService>();
        Assert.Equal(new AuthenticatedUserContext(UserId, TenantId), query.LastUser);
    }

    [Fact]
    public async Task Anonymous_tool_discovery_is_rejected()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();

        using var response = await SendRpcAsync(client, "tools/list", new { }, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protected_resource_metadata_advertises_the_authorization_server()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();

        using var response = await client.GetAsync("/.well-known/oauth-protected-resource/mcp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("http://localhost/mcp", metadata.RootElement.GetProperty("resource").GetString(), StringComparison.Ordinal);
        Assert.Equal("https://issuer.example.test/", metadata.RootElement.GetProperty("authorization_servers")[0].GetString());
    }

    [Fact]
    public async Task Cross_tenant_identity_is_denied_without_exposing_internal_details()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();

        using var response = await SendRpcAsync(
            client,
            "tools/call",
            new { name = "get_my_courses", arguments = new { } },
            "tenant-b");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadRpcJsonAsync(response);
        var result = json.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var errorText = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("not authorized", errorText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TenantId.ToString(), errorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Date_ranges_over_90_days_are_rejected_with_a_safe_tool_error()
    {
        await using var server = await CreateServerAsync();
        using var client = server.GetTestClient();
        var from = DateTimeOffset.UtcNow;

        using var response = await SendRpcAsync(
            client,
            "tools/call",
            new
            {
                name = "get_upcoming_assignments",
                arguments = new { from, to = from.AddDays(91) }
            },
            "tenant-a");

        using var json = await ReadRpcJsonAsync(response);
        var result = json.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("at most 90 days", text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ArgumentException), text, StringComparison.Ordinal);
    }

    private static async Task<WebApplication> CreateServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:TenantClaim"] = "tenant_id",
            ["Authentication:Authority"] = "https://issuer.example.test/",
            ["Frontend:ConnectionManagementUri"] = "https://companion.example.test/dashboard",
            ["Canvas:Institutions:0:Id"] = "stanford-seed",
            ["Canvas:Institutions:0:Name"] = "Stanford SEED",
            ["Canvas:Institutions:0:TenantId"] = TenantId.ToString(),
            ["Canvas:Institutions:0:BaseUrl"] = "https://seedonline.stanford.edu/",
            ["Canvas:Institutions:0:Enabled"] = "false"
        });
        builder.Services.AddSingleton<IAuthenticatedUserContextResolver, FakeUserResolver>();
        builder.Services.AddSingleton<ICanvasInstitutionCatalog, CanvasInstitutionCatalog>();
        builder.Services.AddSingleton<FakeConnectionManagement>();
        builder.Services.AddSingleton<IConnectionManagementService>(services => services.GetRequiredService<FakeConnectionManagement>());
        builder.Services.AddSingleton<FakeLmsQueryService>();
        builder.Services.AddSingleton<ILmsQueryService>(services => services.GetRequiredService<FakeLmsQueryService>());
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { })
            .AddMcp(options => options.ResourceMetadata = new ProtectedResourceMetadata
            {
                AuthorizationServers = { "https://issuer.example.test/" }
            });
        builder.Services.AddAuthorization();
        builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .AddAuthorizationFilters()
            .WithTools<LmsTools>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpErrorFilter.Handle));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcp("/mcp").RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> SendRpcAsync(
        HttpClient client,
        string method,
        object parameters,
        string? token)
    {
        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params = parameters
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadRpcJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            payload = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith("data:", StringComparison.Ordinal))["data:".Length..].Trim();
        }

        return JsonDocument.Parse(payload);
    }

    private sealed class FakeUserResolver : IAuthenticatedUserContextResolver
    {
        public Task<AuthenticatedUserContext?> ResolveAsync(
            string externalSubject,
            Guid tenantId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedUserContext?>(externalSubject == "tenant-a" && tenantId == TenantId
                ? new AuthenticatedUserContext(UserId, TenantId)
                : null);
    }

    private sealed class FakeConnectionManagement : IConnectionManagementService
    {
        public AuthenticatedUserContext? LastUser { get; private set; }
        public Task<IReadOnlyList<ConnectionSummaryDto>> GetConnectionsAsync(AuthenticatedUserContext user, CancellationToken cancellationToken)
        {
            LastUser = user;
            return Task.FromResult<IReadOnlyList<ConnectionSummaryDto>>([]);
        }
        public Task DisconnectAsync(AuthenticatedUserContext user, Guid connectionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers.Authorization.ToString().Replace("Bearer ", string.Empty, StringComparison.Ordinal);
            if (subject is not ("tenant-a" or "tenant-b"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var tenantId = subject == "tenant-a" ? TenantId : Guid.Parse("30000000-0000-0000-0000-000000000003");
            var identity = new ClaimsIdentity(
            [
                new Claim("sub", subject),
                new Claim("tenant_id", tenantId.ToString())
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class FakeLmsQueryService : ILmsQueryService
    {
        public AuthenticatedUserContext? LastUser { get; private set; }

        public Task<IReadOnlyList<CourseDto>> GetMyCoursesAsync(AuthenticatedUserContext user, Guid? connectionId, CancellationToken cancellationToken)
        {
            LastUser = user;
            return Task.FromResult<IReadOnlyList<CourseDto>>([new CourseDto("course-a", "Canvas", "Biology", "BIO", "Fall", "Student", null)]);
        }

        public Task<IReadOnlyList<AssignmentDto>> GetUpcomingAssignmentsAsync(AuthenticatedUserContext user, Guid? connectionId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            LastUser = user;
            return Task.FromResult<IReadOnlyList<AssignmentDto>>([]);
        }

        public Task<IReadOnlyList<ScheduleEventDto>> GetScheduleAsync(AuthenticatedUserContext user, Guid? connectionId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            LastUser = user;
            return Task.FromResult<IReadOnlyList<ScheduleEventDto>>([]);
        }

        public Task<CourseProgressDto> GetCourseProgressAsync(AuthenticatedUserContext user, Guid? connectionId, string courseId, CancellationToken cancellationToken)
        {
            LastUser = user;
            return Task.FromResult(new CourseProgressDto(courseId, null, null, null, "Not supported."));
        }
    }
}
