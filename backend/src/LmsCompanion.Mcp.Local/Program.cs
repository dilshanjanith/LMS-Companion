using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Services;
using LmsCompanion.Infrastructure.Canvas;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace LmsCompanion.Mcp.Local;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if ((args.Length > 1 && !(args.Length == 3 && args[0] is "import-courses" or "import-upcoming"))
            || (args.Length == 1 && args[0] is not ("setup" or "serve" or "serve-snapshot" or "serve-http-snapshot" or "serve-http-sse"))
            || (args.Length == 2))
        {
            Console.Error.WriteLine("Usage: LmsCompanion.Mcp.Local [setup|serve|serve-snapshot|serve-http-snapshot|serve-http-sse|import-courses <Canvas URL> <JSON file>|import-upcoming <Canvas URL> <JSON file>]");
            return 2;
        }

        if (args.Length == 3)
        {
            try
            {
                if (args[0] == "import-courses")
                    LocalCanvasSnapshotStore.ImportCourses(args[1], args[2]);
                else
                    LocalCanvasSnapshotStore.ImportUpcoming(args[1], args[2]);
                Console.WriteLine("Canvas snapshot imported for MCP clients.");
                return 0;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or System.Text.Json.JsonException)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        if (args.Length == 1 && args[0] is "serve-http-snapshot" or "serve-http-sse")
        {
            var key = Environment.GetEnvironmentVariable("LMS_COMPANION_REMOTE_KEY")?.Trim();
            var urlKey = Environment.GetEnvironmentVariable("LMS_COMPANION_URL_KEY")?.Trim();
            if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            {
                Console.Error.WriteLine("Set LMS_COMPANION_REMOTE_KEY to a random secret of at least 32 characters.");
                return 1;
            }
            if (!string.IsNullOrEmpty(urlKey) && urlKey.Length < 32)
            {
                Console.Error.WriteLine("LMS_COMPANION_URL_KEY must be a random secret of at least 32 characters.");
                return 1;
            }

            var webBuilder = WebApplication.CreateBuilder(args);
            // The private URL is a credential; suppress access logs containing request paths.
            webBuilder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            webBuilder.WebHost.UseUrls("http://127.0.0.1:5188");
            webBuilder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
            if (args[0] == "serve-http-sse")
            {
                webBuilder.Services.AddMcpServer()
                    .WithHttpTransport(options => options.Stateless = true)
                    .WithTools<LocalCanvasSnapshotTools>();
            }
            var app = webBuilder.Build();
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/health")
                {
                    await next();
                    return;
                }
                if (!string.IsNullOrEmpty(context.Request.Headers.Origin))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                if (args[0] == "serve-http-sse" &&
                    !RemoteMcpAuthorization.IsValid(context.Request.Headers.Authorization,
                        context.Request.Headers["x-api-key"], key))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next();
            });
            app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
            if (args[0] == "serve-http-sse") app.MapMcp("/mcp");
            else SnapshotMcpJsonEndpoint.Map(app, key, urlKey);
            await app.RunAsync();
            return 0;
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders(); // MCP reserves stdout for protocol messages.
        if (args.Length == 1 && args[0] == "serve-snapshot")
        {
            builder.Services.AddMcpServer()
                .WithStdioServerTransport()
                .WithTools<LocalCanvasSnapshotTools>();
            using var snapshotServer = builder.Build();
            await snapshotServer.RunAsync();
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The local Canvas token pilot requires Windows DPAPI.");
            return 1;
        }

        Directory.CreateDirectory(LocalCanvasPaths.KeyDirectory);
        builder.Services.AddDataProtection()
            .SetApplicationName("LmsCompanion.LocalMcp")
            .PersistKeysToFileSystem(new DirectoryInfo(LocalCanvasPaths.KeyDirectory))
            .ProtectKeysWithDpapi();
        builder.Services.AddSingleton<LocalCanvasConnectionStore>();

        if (args.Length == 1 && args[0] == "setup")
        {
            using var host = builder.Build();
            try
            {
                await LocalCanvasSetup.RunAsync(host.Services.GetRequiredService<LocalCanvasConnectionStore>());
                return 0;
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        builder.Services.AddSingleton(provider =>
            provider.GetRequiredService<LocalCanvasConnectionStore>().Load());
        builder.Services.AddSingleton<ILmsConnectionRepository, LocalConnectionRepository>();
        builder.Services.AddSingleton<ICanvasAccessTokenService, LocalCanvasAccessTokenService>();
        builder.Services.AddHttpClient<ILmsProvider, CanvasLmsProvider>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                CanvasHttpClientDefaults.Configure(client);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        builder.Services.AddScoped<ILmsQueryService, LmsQueryService>();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<LocalLmsTools>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(LocalMcpErrorFilter.Handle));

        using var server = builder.Build();
        try
        {
            _ = server.Services.GetRequiredService<LocalCanvasConnection>();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        await server.RunAsync();
        return 0;
    }
}
