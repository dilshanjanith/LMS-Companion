using LmsCompanion.Domain;
using Microsoft.EntityFrameworkCore;

namespace LmsCompanion.Infrastructure;

public sealed class CompanionDbContext(DbContextOptions<CompanionDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<LmsConnection> LmsConnections => Set<LmsConnection>();

    public DbSet<LmsCredential> LmsCredentials => Set<LmsCredential>();

    public DbSet<CanvasOAuthState> CanvasOAuthStates => Set<CanvasOAuthState>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(tenant => tenant.Id);
            entity.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
            entity.Property(tenant => tenant.Configuration).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.ExternalSubject).HasMaxLength(255).IsRequired();
            entity.HasIndex(user => user.ExternalSubject).IsUnique();
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.HasKey(membership => membership.Id);
            entity.Property(membership => membership.Role).HasMaxLength(64).IsRequired();
            entity.HasIndex(membership => new { membership.UserId, membership.TenantId }).IsUnique();
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(membership => membership.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(membership => new { membership.UserId, membership.TenantId });
        });

        modelBuilder.Entity<LmsConnection>(entity =>
        {
            entity.HasKey(connection => connection.Id);
            entity.Property(connection => connection.Provider).HasConversion<string>().HasMaxLength(32);
            entity.Property(connection => connection.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(connection => connection.BaseUrl).HasMaxLength(2048).IsRequired();
            entity.Property(connection => connection.CredentialReference).HasMaxLength(64);
            entity.Property(connection => connection.Scopes).HasColumnType("text[]").IsRequired();
            entity.HasIndex(connection => new { connection.TenantId, connection.UserId, connection.Status });
            entity.HasOne<TenantMembership>()
                .WithMany()
                .HasForeignKey(connection => new { connection.UserId, connection.TenantId })
                .HasPrincipalKey(membership => new { membership.UserId, membership.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LmsCredential>(entity =>
        {
            entity.HasKey(credential => credential.Reference);
            entity.Property(credential => credential.Reference).HasMaxLength(64);
            entity.Property(credential => credential.ProtectedPayload).HasColumnType("text").IsRequired();
            entity.HasOne<TenantMembership>()
                .WithMany()
                .HasForeignKey(credential => new { credential.UserId, credential.TenantId })
                .HasPrincipalKey(membership => new { membership.UserId, membership.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CanvasOAuthState>(entity =>
        {
            entity.HasKey(state => state.StateHash);
            entity.Property(state => state.StateHash).HasMaxLength(64);
            entity.Property(state => state.InstitutionId).HasMaxLength(128).IsRequired();
            entity.Property(state => state.BaseUrl).HasMaxLength(2048).IsRequired();
            entity.Property(state => state.RedirectUri).HasMaxLength(2048).IsRequired();
            entity.HasIndex(state => state.ExpiresAt);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.EventType).HasMaxLength(96).IsRequired();
            entity.Property(auditEvent => auditEvent.SubjectId).HasMaxLength(128);
            entity.HasIndex(auditEvent => new { auditEvent.TenantId, auditEvent.OccurredAt });
        });
    }
}
