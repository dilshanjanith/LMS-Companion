namespace LmsCompanion.Application.Errors;

public sealed class ConnectionAccessDeniedException()
    : Exception("The requested LMS connection is not available to the authenticated user.");
