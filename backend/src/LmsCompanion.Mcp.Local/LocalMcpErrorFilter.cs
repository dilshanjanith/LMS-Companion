using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LmsCompanion.Mcp.Local;

public static class LocalMcpErrorFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Handle(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (Exception exception)
            {
                var message = exception switch
                {
                    ConnectionAccessDeniedException => "The Canvas connection is unavailable.",
                    NoActiveLmsConnectionException => "Run local Canvas setup before requesting LMS data.",
                    LmsProviderException { Code: LmsProviderFailureCode.Unauthorized } =>
                        "The Canvas token was rejected. Generate a new token and run setup again.",
                    LmsProviderException { Code: LmsProviderFailureCode.RateLimited } =>
                        "Canvas is rate limiting requests. Try again later.",
                    LmsProviderException { Code: LmsProviderFailureCode.NotFound } =>
                        "The requested Canvas data is unavailable.",
                    ArgumentException => "The requested input is invalid.",
                    _ => "Canvas data is temporarily unavailable."
                };
                Console.Error.WriteLine($"Local LMS MCP tool failed: {exception.GetType().Name}");
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = message }]
                };
            }
        };
}
