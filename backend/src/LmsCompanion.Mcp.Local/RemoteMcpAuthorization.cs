using System.Security.Cryptography;
using System.Text;

namespace LmsCompanion.Mcp.Local;

public static class RemoteMcpAuthorization
{
    public static bool IsValid(string? authorization, string? apiKey, string expectedKey) =>
        IsValidBearer(authorization, expectedKey) || IsValidKey(apiKey, expectedKey);

    public static bool IsValidBearer(string? authorization, string expectedKey)
    {
        var supplied = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization[7..] : string.Empty;
        return IsValidKey(supplied, expectedKey);
    }

    private static bool IsValidKey(string? supplied, string expectedKey)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied ?? string.Empty);
        return suppliedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
