using LmsCompanion.Application.Abstractions;
using LmsCompanion.Application.Contracts;
using Microsoft.Extensions.Configuration;

namespace LmsCompanion.Infrastructure.Canvas;

public sealed class CanvasInstitutionCatalog : ICanvasInstitutionCatalog
{
    private readonly IReadOnlyDictionary<string, CanvasInstitutionConfiguration> _institutions;
    private readonly List<(Guid TenantId, InstitutionSummaryDto Summary)> _summaries = [];

    public CanvasInstitutionCatalog(IConfiguration configuration)
    {
        var entries = configuration.GetSection("Canvas:Institutions").GetChildren();
        var institutions = new Dictionary<string, CanvasInstitutionConfiguration>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var id = entry["Id"];
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128
                || !Guid.TryParse(entry["TenantId"], out var tenantId) || tenantId == Guid.Empty
                || !Uri.TryCreate(entry["BaseUrl"], UriKind.Absolute, out var baseUri)
                || !IsCleanHttpsUri(baseUri, requireRootPath: true))
            {
                throw new InvalidOperationException("Canvas institutions require an ID, nonempty tenant ID, and HTTPS origin.");
            }

            if (_summaries.Any(item => item.Summary.Id == id
                || (item.TenantId == tenantId && item.Summary.BaseUrl == baseUri.AbsoluteUri)))
            {
                throw new InvalidOperationException("Canvas institution identifiers and tenant base URLs must be unique.");
            }

            var enabledText = entry["Enabled"];
            if (enabledText is not null && !bool.TryParse(enabledText, out _))
                throw new InvalidOperationException("Canvas institution Enabled must be true or false.");
            var enabled = enabledText is null || bool.Parse(enabledText);
            _summaries.Add((tenantId, new InstitutionSummaryDto(
                id, string.IsNullOrWhiteSpace(entry["Name"]) ? id : entry["Name"]!,
                "Canvas", baseUri.AbsoluteUri, enabled)));
            // Pending institutions can be shown without creating fake OAuth credentials.
            if (!enabled) continue;

            var institution = Parse(entry);
            if (!institutions.TryAdd(institution.Id, institution))
            {
                throw new InvalidOperationException("Canvas institution identifiers must be unique.");
            }
        }

        if (institutions.Values
            .GroupBy(institution => (institution.TenantId, institution.BaseUri.AbsoluteUri))
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Canvas base URLs must be unique within each tenant.");
        }

        _institutions = institutions;
    }

    public IReadOnlyList<InstitutionSummaryDto> ListForTenant(Guid tenantId) =>
        _summaries.Where(item => item.TenantId == tenantId)
            .Select(item => item.Summary).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();

    public CanvasInstitutionConfiguration? FindForTenant(string institutionId, Guid tenantId) =>
        _institutions.TryGetValue(institutionId, out var institution) && institution.TenantId == tenantId
            ? institution
            : null;

    public CanvasInstitutionConfiguration? FindForTenantByBaseUri(Uri baseUri, Guid tenantId) =>
        _institutions.Values.SingleOrDefault(institution =>
            institution.TenantId == tenantId
            && string.Equals(institution.BaseUri.AbsoluteUri, baseUri.AbsoluteUri, StringComparison.Ordinal));

    private static CanvasInstitutionConfiguration Parse(IConfigurationSection section)
    {
        var id = section["Id"];
        var tenantIdText = section["TenantId"];
        var baseUrlText = section["BaseUrl"];
        var clientId = section["ClientId"];
        var clientSecret = section["ClientSecret"];
        var redirectUriText = section["RedirectUri"];
        var scopes = section.GetSection("Scopes").GetChildren()
            .Select(item => item.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        if (string.IsNullOrWhiteSpace(id)
            || id.Length > 128
            || !Guid.TryParse(tenantIdText, out var tenantId)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret)
            || !Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUri)
            || !IsCleanHttpsUri(baseUri, requireRootPath: true)
            || !Uri.TryCreate(redirectUriText, UriKind.Absolute, out var redirectUri)
            || !IsCleanHttpsUri(redirectUri, requireRootPath: false)
            || scopes.Length == 0
            || scopes.Any(scope => !scope.StartsWith("url:GET|", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Each Canvas institution requires an ID, tenant ID, HTTPS base URL, client ID and secret, HTTPS callback URI, and explicit read-only GET scopes.");
        }

        return new CanvasInstitutionConfiguration(
            id,
            tenantId,
            baseUri,
            clientId,
            clientSecret,
            redirectUri,
            scopes);
    }

    private static bool IsCleanHttpsUri(Uri uri, bool requireRootPath) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && (!requireRootPath || uri.AbsolutePath == "/");
}
