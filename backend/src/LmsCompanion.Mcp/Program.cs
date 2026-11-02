using LmsCompanion.Infrastructure;
using LmsCompanion.Infrastructure.Security;
using LmsCompanion.Application;
using LmsCompanion.Mcp;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
var publicResource = builder.Configuration["Mcp:PublicResourceUri"];
Uri? publicResourceUri = null;
if (publicResource is not null && (!Uri.TryCreate(publicResource, UriKind.Absolute, out publicResourceUri)
    || publicResourceUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(publicResourceUri.UserInfo)
    || !string.IsNullOrEmpty(publicResourceUri.Query) || !string.IsNullOrEmpty(publicResourceUri.Fragment)
    || publicResourceUri.AbsolutePath != "/mcp"))
    throw new InvalidOperationException("Mcp:PublicResourceUri must be the public HTTPS /mcp endpoint.");
if (!builder.Environment.IsDevelopment() && publicResourceUri is null)
    throw new InvalidOperationException("Configure Mcp:PublicResourceUri before deploying the MCP host.");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddLmsBearerAuthentication(builder.Configuration);
builder.Services.AddAuthentication(options =>
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme)
    .AddMcp(options =>
    {
        var authority = builder.Configuration["Authentication:Authority"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri)
            || authorityUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Configure an HTTPS Authentication:Authority for MCP OAuth discovery.");
        }

        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = publicResourceUri?.AbsoluteUri!,
            AuthorizationServers = { authorityUri.AbsoluteUri },
            ScopesSupported = ["read:lms"]
        };
    });
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddHealthChecks();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .AddAuthorizationFilters()
    .WithTools<LmsTools>()
    .WithRequestFilters(filters => filters.AddCallToolFilter(McpErrorFilter.Handle));

var app = builder.Build();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health", new HealthCheckOptions()).AllowAnonymous();
app.MapMcp("/mcp").RequireAuthorization();
app.MapGet("/", () => Results.Ok(new { service = "LmsCompanion.Mcp", status = "ready" })).RequireAuthorization();
app.Run();

public partial class Program;
