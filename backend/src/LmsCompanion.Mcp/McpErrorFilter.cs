using LmsCompanion.Application.Errors;
using LmsCompanion.Domain;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LmsCompanion.Mcp;

public static class McpErrorFilter
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
                var message = ToSafeMessage(exception);
                if (message == "The LMS service is temporarily unavailable.")
                {
                    context.Services?.GetService<ILoggerFactory>()
                        ?.CreateLogger("LmsCompanion.Mcp")
                        .LogError("An MCP tool failed. Exception type: {ExceptionType}", exception.GetType().Name);
                }

                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = message }]
                };
            }
        };

    private static string ToSafeMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "This authenticated account is not authorized for LMS data.",
        ConnectionAccessDeniedException => "The requested LMS connection is unavailable.",
        NoActiveLmsConnectionException => "Call get_connection_status and open its managementUrl to connect an LMS account, then return to this chat.",
        ConnectionSelectionRequiredException => "Call get_connection_status and ask the user to choose a connectionId from their active connections.",
        ArgumentException => "The requested input is invalid. Provide a valid date range of at most 90 days.",
        LmsProviderException { Code: LmsProviderFailureCode.NotFound } => "The requested LMS data is unavailable.",
        LmsProviderException { Code: LmsProviderFailureCode.NotSupported } => "This LMS does not provide the requested data.",
        LmsProviderException { Code: LmsProviderFailureCode.Unauthorized } => "Reconnect the LMS account before requesting this data.",
        LmsProviderException { Code: LmsProviderFailureCode.RateLimited } => "The LMS is rate limiting requests. Try again later.",
        LmsProviderException => "The LMS service is temporarily unavailable.",
        _ => "The LMS service is temporarily unavailable."
    };
}
