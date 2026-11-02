using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LmsCompanion.Api.Controllers;
using LmsCompanion.Application;
using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;
using LmsCompanion.Infrastructure;
using LmsCompanion.Infrastructure.Canvas;
using LmsCompanion.Infrastructure.Persistence;
using LmsCompanion.Infrastructure.Security;
using LmsCompanion.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Server;

namespace LmsCompanion.IntegrationTests;

/// <summary>
/// Exercises the real controllers, application services, EF repositories, credential vault,
/// Canvas HTTP adapter and MCP transport together. Identity, Canvas HTTP and state persistence
/// are test substitutes; this is not evidence of Stanford, Auth0, PostgreSQL or Claude access.
/// </summary>
public sealed class CanvasConnectionJourneyTests
{
    private static readonly Guid Tenant = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Owner = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Connect_read_from_mcp_deny_other_user_and_disconnect()
    {
        await using var app = await CreateAsync();
        using var client = app.GetTestClient();
        using (var anonymous = await client.GetAsync("/api/v1/connections/institutions"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        SignIn(client, "owner");
        var institutions = await client.GetFromJsonAsync<JsonElement>("/api/v1/connections/institutions");
        Assert.True(institutions[0].GetProperty("canConnect").GetBoolean());
        using var start = await client.PostAsJsonAsync("/api/v1/connections/canvas/start", new { institutionId = "test-campus" });
        start.EnsureSuccessStatusCode();
        var startJson = await start.Content.ReadFromJsonAsync<JsonElement>();
        var authorization = new Uri(startJson.GetProperty("authorizationUrl").GetString()!);
        Assert.Equal("canvas.example.test", authorization.Host);
        var query = System.Web.HttpUtility.ParseQueryString(authorization.Query);
        Assert.Equal("https://api.example.test/api/v1/connections/canvas/callback", query["redirect_uri"]);
        var callback = "/api/v1/connections/canvas/callback?code=test-code&state=" + Uri.EscapeDataString(query["state"]!);

        // Canvas returns in a top-level navigation without an application bearer header.
        client.DefaultRequestHeaders.Authorization = null;
        using var completed = await client.GetAsync(callback);
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        Assert.True(completed.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(completed.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("https://portal.example.test/dashboard?canvas=connected", completed.Headers.Location!.AbsoluteUri);
        using var replay = await client.GetAsync(callback);
        Assert.EndsWith("canvas=error", replay.Headers.Location!.AbsoluteUri);
        Assert.Equal(1, app.Services.GetRequiredService<CanvasHandler>().Exchanges);

        Guid connectionId;
        using (var scope = app.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var connection = Assert.Single(await database.LmsConnections.ToListAsync());
            connectionId = connection.Id;
            Assert.Equal(Owner, connection.UserId);
            var stored = Assert.Single(await database.LmsCredentials.ToListAsync());
            Assert.DoesNotContain("synthetic-access", stored.ProtectedPayload);
            Assert.DoesNotContain("synthetic-refresh", stored.ProtectedPayload);
        }

        SignIn(client, "owner");
        using var courses = await CallTool(client, "get_my_courses", new { connectionId });
        Assert.Contains("Synthetic leadership course", courses.RootElement.GetRawText());
        Assert.DoesNotContain("synthetic-access", courses.RootElement.GetRawText());
        Assert.Equal(1, app.Services.GetRequiredService<CanvasHandler>().CourseReads);

        using var modules = await CallTool(client, "get_course_modules", new { connectionId, courseId = "1" });
        Assert.Contains("Strategy foundations", modules.RootElement.GetRawText());
        using var items = await CallTool(client, "get_module_items", new { connectionId, courseId = "1", moduleId = "7" });
        Assert.Contains("Strategic choices", items.RootElement.GetRawText());
        using var material = await CallTool(client, "get_learning_material", new { connectionId, courseId = "1", moduleId = "7", itemId = "9" });
        Assert.False(material.RootElement.GetProperty("result").TryGetProperty("isError", out var materialError) && materialError.GetBoolean(), material.RootElement.GetRawText());
        Assert.Contains("A strategy identifies the choices", material.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-access", material.RootElement.GetRawText());
        using var foreignCourse = await CallTool(client, "get_learning_material",
            new { connectionId, courseId = "999", moduleId = "7", itemId = "9" });
        Assert.True(foreignCourse.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        using var assignments = await CallTool(client, "get_upcoming_assignments", new { connectionId });
        Assert.Contains("Synthetic preparation assignment", assignments.RootElement.GetRawText());
        Assert.DoesNotContain("synthetic-access", assignments.RootElement.GetRawText());
        var permittedReads = app.Services.GetRequiredService<CanvasHandler>().CourseReads;

        SignIn(client, "other");
        var foreignConnections = await client.GetFromJsonAsync<JsonElement>("/api/v1/connections");
        Assert.Equal(0, foreignConnections.GetArrayLength());
        using var denied = await CallTool(client, "get_my_courses", new { connectionId });
        Assert.True(denied.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        using var foreignDelete = await client.DeleteAsync($"/api/v1/connections/{connectionId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(permittedReads, app.Services.GetRequiredService<CanvasHandler>().CourseReads);

        SignIn(client, "owner");
        using var disconnected = await client.DeleteAsync($"/api/v1/connections/{connectionId}");
        Assert.Equal(HttpStatusCode.NoContent, disconnected.StatusCode);
        Assert.Equal(1, app.Services.GetRequiredService<CanvasHandler>().Revocations);
        using var afterDisconnect = await CallTool(client, "get_my_courses", new { connectionId });
        Assert.True(afterDisconnect.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(permittedReads, app.Services.GetRequiredService<CanvasHandler>().CourseReads);
        using var finalScope = app.Services.CreateScope();
        Assert.Empty(await finalScope.ServiceProvider.GetRequiredService<CompanionDbContext>().LmsCredentials.ToListAsync());
    }

    [Fact]
    public async Task Missing_read_scope_cannot_discover_institutions_or_call_mcp()
    {
        await using var app = await CreateAsync();
        using var client = app.GetTestClient();
        SignIn(client, "no-scope");
        using var apiResponse = await client.GetAsync("/api/v1/connections/institutions");
        Assert.Equal(HttpStatusCode.Forbidden, apiResponse.StatusCode);
        using var request = RpcRequest("get_connection_status", new { });
        using var mcpResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, mcpResponse.StatusCode);
        Assert.Equal(0, app.Services.GetRequiredService<CanvasHandler>().CourseReads);
    }

    [Fact]
    public async Task Personal_token_is_validated_then_stored_only_for_the_signed_in_user()
    {
        await using var app = await CreateAsync(personalTokens: true);
        using var client = app.GetTestClient();
        SignIn(client, "owner");
        using var rejected = await client.PostAsJsonAsync("/api/v1/connections/canvas/personal-token",
            new { institutionId = "test-campus", token = "bad-token" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        using var accepted = await client.PostAsJsonAsync("/api/v1/connections/canvas/personal-token",
            new { institutionId = "test-campus", token = "synthetic-access" });
        accepted.EnsureSuccessStatusCode();
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain("synthetic-access", result.GetRawText());
        var id = result.GetProperty("id").GetGuid();
        using var courses = await CallTool(client, "get_my_courses", new { connectionId = id });
        Assert.Contains("Synthetic leadership course", courses.RootElement.GetRawText());
        SignIn(client, "other");
        using var denied = await CallTool(client, "get_my_courses", new { connectionId = id });
        Assert.True(denied.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Invalid_institution_input_returns_bad_request_without_provider_calls(string? institutionId)
    {
        await using var app = await CreateAsync();
        using var client = app.GetTestClient();
        SignIn(client, "owner");
        using var response = await client.PostAsJsonAsync("/api/v1/connections/canvas/start", new { institutionId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Services.GetRequiredService<CanvasHandler>().Exchanges);
    }

    private static async Task<WebApplication> CreateAsync(bool personalTokens = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://issuer.example.test/",
            ["Authentication:Audience"] = "https://mcp.example.test/mcp",
            ["Canvas:AllowPersonalTokenTesting"] = personalTokens.ToString(),
            ["Frontend:CanvasCallbackUri"] = "https://portal.example.test/dashboard",
            ["Canvas:Institutions:0:Id"] = "test-campus",
            ["Canvas:Institutions:0:TenantId"] = Tenant.ToString(),
            ["Canvas:Institutions:0:BaseUrl"] = "https://canvas.example.test/",
            ["Canvas:Institutions:0:ClientId"] = "synthetic-client",
            ["Canvas:Institutions:0:ClientSecret"] = "synthetic-secret",
            ["Canvas:Institutions:0:RedirectUri"] = "https://api.example.test/api/v1/connections/canvas/callback",
            ["Canvas:Institutions:0:Scopes:0"] = "url:GET|/api/v1/courses"
        });
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<CompanionDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddDataProtection();
        builder.Services.AddApplication();
        builder.Services.AddSingleton<ICanvasInstitutionCatalog, CanvasInstitutionCatalog>();
        builder.Services.AddSingleton<IPersonalTokenPolicy>(new TestPersonalPolicy(personalTokens));
        builder.Services.AddScoped<IPersonalCanvasConnectionService, PersonalCanvasConnectionService>();
        builder.Services.AddScoped<ITenantMembershipRepository, EfTenantMembershipRepository>();
        builder.Services.AddScoped<ILmsConnectionRepository, EfLmsConnectionRepository>();
        builder.Services.AddScoped<ICredentialVault, DataProtectionCredentialVault>();
        builder.Services.AddScoped<IAuditEventRepository, EfAuditEventRepository>();
        builder.Services.AddSingleton<ICanvasOAuthStateRepository, TestStates>();
        builder.Services.AddSingleton<CanvasHandler>();
        builder.Services.AddHttpClient<ICanvasOAuthClient, CanvasOAuthClient>()
            .ConfigurePrimaryHttpMessageHandler(services => services.GetRequiredService<CanvasHandler>());
        builder.Services.AddHttpClient("CanvasPersonalTokenValidation")
            .ConfigurePrimaryHttpMessageHandler(services => services.GetRequiredService<CanvasHandler>());
        builder.Services.AddHttpClient<ILmsProvider, CanvasLmsProvider>()
            .ConfigurePrimaryHttpMessageHandler(services => services.GetRequiredService<CanvasHandler>());
        builder.Services.AddLmsBearerAuthentication(builder.Configuration);
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, IdentityHandler>("Test", _ => { })
            .AddMcp(options => options.ResourceMetadata = new ProtectedResourceMetadata
            { AuthorizationServers = { "https://issuer.example.test/" }, ScopesSupported = ["read:lms"] });
        builder.Services.AddControllers().AddApplicationPart(typeof(ConnectionsController).Assembly);
        builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true)
            .AddAuthorizationFilters().WithTools<LmsTools>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpErrorFilter.Handle));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapMcp("/mcp").RequireAuthorization();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            db.Tenants.Add(new Tenant(Tenant, "Test campus"));
            db.AppUsers.AddRange(new AppUser(Owner, "owner"), new AppUser(Other, "other"));
            db.TenantMemberships.AddRange(new TenantMembership(Guid.NewGuid(), Owner, Tenant, "student", true),
                new TenantMembership(Guid.NewGuid(), Other, Tenant, "student", true));
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        return app;
    }

    private static void SignIn(HttpClient client, string user) =>
        client.DefaultRequestHeaders.Authorization = new("Bearer", user);

    private static HttpRequestMessage RpcRequest(string name, object arguments)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments } }), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        return request;
    }

    private static async Task<JsonDocument> CallTool(HttpClient client, string name, object arguments)
    {
        using var request = RpcRequest(name, arguments);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            body = body.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
        return JsonDocument.Parse(body);
    }

    private sealed class IdentityHandler(Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal);
            if (subject is not ("owner" or "other" or "no-scope")) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("sub", subject), new Claim("tenant_id", Tenant.ToString()),
                new Claim("scope", subject == "no-scope" ? "openid" : "read:lms")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestPersonalPolicy(bool enabled) : IPersonalTokenPolicy { public bool Enabled => enabled; }

    private sealed class TestStates : ICanvasOAuthStateRepository
    {
        private readonly ConcurrentDictionary<string, CanvasOAuthState> _items = new();
        public Task AddAsync(CanvasOAuthState state, CancellationToken cancellationToken)
        { _items[state.StateHash] = state; return Task.CompletedTask; }
        public Task<CanvasOAuthState?> ConsumeAsync(string hash, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(_items.TryRemove(hash, out var state) && state.ExpiresAt > now ? state : null);
    }

    private sealed class CanvasHandler : HttpMessageHandler
    {
        public int Exchanges { get; private set; }
        public int CourseReads { get; private set; }
        public int Revocations { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("canvas.example.test", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath == "/api/v1/courses" && request.RequestUri.Query == "?per_page=1")
                return Task.FromResult(new HttpResponseMessage(
                    request.Headers.Authorization?.Parameter == "synthetic-access" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
                { Content = JsonContent.Create(new { id = 1 }) });
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/login/oauth2/token")
            {
                Exchanges++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new { access_token = "synthetic-access", refresh_token = "synthetic-refresh", expires_in = 3600 }) });
            }
            Assert.Equal("synthetic-access", request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Delete && request.RequestUri.AbsolutePath == "/login/oauth2/token")
            { Revocations++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri.AbsolutePath == "/api/v1/courses/1/modules")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new[] { new { id = 7, name = "Strategy foundations" } }) });
            if (request.RequestUri.AbsolutePath == "/api/v1/courses/1/modules/7/items")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new[] { new { id = 9, title = "Strategic choices", type = "Page", html_url = "https://canvas.example.test/courses/1/pages/strategic-choices" } }) });
            if (request.RequestUri.AbsolutePath == "/api/v1/courses/1/modules/7/items/9")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new { id = 9, title = "Strategic choices", type = "Page", page_url = "strategic-choices" }) });
            if (request.RequestUri.AbsolutePath == "/api/v1/courses/1/pages/strategic-choices")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new { body = "<p>A strategy identifies the choices an organization makes.</p>" }) });
            if (request.RequestUri.AbsolutePath == "/api/v1/courses/1/assignments")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new[] { new { id = 10, course_id = 1,
                    name = "Synthetic preparation assignment", due_at = DateTimeOffset.UtcNow.AddDays(1),
                    points_possible = 10 } }) });
            Assert.Equal("/api/v1/courses", request.RequestUri.AbsolutePath);
            CourseReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new[] { new { id = 1, name = "Synthetic leadership course" } }) });
        }
    }
}
