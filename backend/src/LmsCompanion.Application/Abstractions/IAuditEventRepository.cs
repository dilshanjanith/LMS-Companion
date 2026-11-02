using LmsCompanion.Domain;

namespace LmsCompanion.Application.Abstractions;

public interface IAuditEventRepository
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
