namespace LmsCompanion.Application.Errors;

public sealed class ConnectionSelectionRequiredException()
    : Exception("Select an LMS connection to continue.");
