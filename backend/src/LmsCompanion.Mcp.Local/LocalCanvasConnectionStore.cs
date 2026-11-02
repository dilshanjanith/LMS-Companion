using System.Text.Json;
using LmsCompanion.Application;
using LmsCompanion.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace LmsCompanion.Mcp.Local;

public sealed record LocalCanvasConnection(
    Guid ConnectionId,
    Guid UserId,
    Guid TenantId,
    Uri BaseUri,
    string AccessToken)
{
    public AuthenticatedUserContext User => new(UserId, TenantId);

    public LmsConnection Connection => new(
        ConnectionId, TenantId, UserId, LmsProviderType.Canvas,
        BaseUri.AbsoluteUri, "local-encrypted-token", [], LmsConnectionStatus.Connected, null);
}

internal sealed record StoredLocalCanvasConnection(
    Guid ConnectionId,
    Guid UserId,
    Guid TenantId,
    string BaseUrl,
    string ProtectedToken);

public sealed class LocalCanvasConnectionStore(IDataProtectionProvider protectionProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string FilePath => Path.Combine(
        LocalCanvasPaths.Directory,
        "local-canvas-connection.json");

    public LocalCanvasConnection Load()
    {
        if (!File.Exists(FilePath))
        {
            throw new InvalidOperationException("Canvas is not configured. Run the local MCP setup command first.");
        }

        StoredLocalCanvasConnection saved;
        try
        {
            saved = JsonSerializer.Deserialize<StoredLocalCanvasConnection>(
                File.ReadAllText(FilePath), JsonOptions)
                ?? throw new InvalidOperationException("Local Canvas configuration is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Local Canvas configuration is invalid.", exception);
        }

        if (saved.ConnectionId == Guid.Empty || saved.UserId == Guid.Empty || saved.TenantId == Guid.Empty
            || !TryCanvasBaseUri(saved.BaseUrl, out var baseUri))
        {
            throw new InvalidOperationException("Local Canvas configuration is invalid. Run setup again.");
        }

        string token;
        try
        {
            token = ProtectorFor(baseUri).Unprotect(saved.ProtectedToken);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("The stored Canvas token cannot be decrypted by this Windows account.", exception);
        }

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The stored Canvas token is empty. Run setup again.");

        return new LocalCanvasConnection(saved.ConnectionId, saved.UserId, saved.TenantId, baseUri, token);
    }

    public void Save(Uri baseUri, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (!TryCanvasBaseUri(baseUri.AbsoluteUri, out _))
            throw new ArgumentException("Canvas URL must be an HTTPS origin, such as https://school.instructure.com/.");

        var existing = File.Exists(FilePath) ? Load() : null;
        var saved = new StoredLocalCanvasConnection(
            existing?.ConnectionId ?? Guid.NewGuid(),
            existing?.UserId ?? Guid.NewGuid(),
            existing?.TenantId ?? Guid.NewGuid(),
            baseUri.AbsoluteUri,
            ProtectorFor(baseUri).Protect(accessToken));
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(saved, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static bool TryCanvasBaseUri(string? value, out Uri baseUri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            && candidate.Scheme == Uri.UriSchemeHttps
            && candidate.AbsolutePath == "/"
            && string.IsNullOrEmpty(candidate.UserInfo)
            && string.IsNullOrEmpty(candidate.Query)
            && string.IsNullOrEmpty(candidate.Fragment))
        {
            baseUri = candidate;
            return true;
        }

        baseUri = null!;
        return false;
    }

    private IDataProtector ProtectorFor(Uri baseUri) => protectionProvider.CreateProtector(
        "LmsCompanion.LocalMcp.CanvasToken.v1", baseUri.AbsoluteUri);
}
