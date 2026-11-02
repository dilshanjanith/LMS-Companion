using LmsCompanion.Application.Contracts;

namespace LmsCompanion.Application.Abstractions;

public interface ICanvasInstitutionCatalog
{
    IReadOnlyList<InstitutionSummaryDto> ListForTenant(Guid tenantId);

    CanvasInstitutionConfiguration? FindForTenant(string institutionId, Guid tenantId);

    CanvasInstitutionConfiguration? FindForTenantByBaseUri(Uri baseUri, Guid tenantId);
}
