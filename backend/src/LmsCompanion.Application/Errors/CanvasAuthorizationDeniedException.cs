namespace LmsCompanion.Application.Errors;

public sealed class CanvasAuthorizationDeniedException()
    : Exception("Canvas authorization was not completed.");
