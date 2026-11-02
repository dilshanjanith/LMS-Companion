using LmsCompanion.Application.Abstractions;
using LmsCompanion.Domain;

namespace LmsCompanion.Infrastructure.Persistence;

public sealed class EfAuditEventRepository(CompanionDbContext database) : IAuditEventRepository
{
    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(auditEvent);
        await database.SaveChangesAsync(cancellationToken);
    }
}
