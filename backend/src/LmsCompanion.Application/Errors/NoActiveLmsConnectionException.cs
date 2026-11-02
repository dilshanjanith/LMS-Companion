namespace LmsCompanion.Application.Errors;

public sealed class NoActiveLmsConnectionException()
    : Exception("No active LMS connection is available for the authenticated user.");
