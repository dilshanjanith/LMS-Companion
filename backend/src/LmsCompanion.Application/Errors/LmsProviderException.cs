namespace LmsCompanion.Application.Errors;

/// <summary>Safe provider failure abstraction; raw provider responses must not cross this boundary.</summary>
public sealed class LmsProviderException(
    LmsProviderFailureCode code,
    string safeMessage,
    TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public LmsProviderFailureCode Code { get; } = code;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
