using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plm;

public sealed class PlmContext : DbContext
{
    private readonly ITenantContext _tenant;
    public PlmContext(DbContextOptions<PlmContext> options, ITenantContext tenant) : base(options) => _tenant = tenant;
    public Guid CurrentTenantId => _tenant.CurrentTenantId;

    public DbSet<PlmIterationCandidate> IterationCandidates => Set<PlmIterationCandidate>();
    public DbSet<PlmEngineeringCandidate> EngineeringCandidates => Set<PlmEngineeringCandidate>();
    public DbSet<PlmTechnicalManifest> TechnicalManifests => Set<PlmTechnicalManifest>();
    public DbSet<PlmTechnicalManifestItem> TechnicalManifestItems => Set<PlmTechnicalManifestItem>();
    public DbSet<PlmEngineeringBaseline> EngineeringBaselines => Set<PlmEngineeringBaseline>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        PlmModelConfiguration.Configure(builder);
        builder.Entity<PlmIterationCandidate>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        builder.Entity<PlmEngineeringCandidate>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        builder.Entity<PlmTechnicalManifest>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        builder.Entity<PlmTechnicalManifestItem>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        builder.Entity<PlmEngineeringBaseline>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
    }

    public override int SaveChanges() { GuardChanges(); return base.SaveChanges(); }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { GuardChanges(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { GuardChanges(); return base.SaveChangesAsync(cancellationToken); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) { GuardChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }

    private void GuardChanges()
    {
        if (CurrentTenantId == Guid.Empty) throw new InvalidOperationException("PLM tenant is required.");
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;
            if (entry.Metadata.FindProperty("TenantId") != null && (Guid)entry.Property("TenantId").CurrentValue! != CurrentTenantId)
                throw new InvalidOperationException("PLM tenant mismatch.");
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("PLM deletion is prohibited.");
            if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is PlmIterationCandidate or PlmTechnicalManifest or PlmTechnicalManifestItem)
                    throw new InvalidOperationException("PLM immutable record cannot be changed.");
                if (entry.Entity is PlmEngineeringBaseline baseline)
                {
                    if ((string)entry.Property(nameof(baseline.Status)).OriginalValue! != "FROZEN" || baseline.Status != "SUPERSEDED")
                        throw new InvalidOperationException("PLM Baseline can only be superseded.");
                    foreach (var property in entry.Properties.Where(x => x.IsModified))
                        if (property.Metadata.Name is not (nameof(baseline.Status) or nameof(baseline.SupersededAtUtc) or nameof(baseline.SupersededBy) or nameof(baseline.SupersedeReason) or nameof(baseline.SupersedeRequestId) or nameof(baseline.SupersedeInputHash)))
                            throw new InvalidOperationException("PLM Baseline identity is immutable.");
                    if (baseline.SupersededAtUtc is null || string.IsNullOrEmpty(baseline.SupersededBy) || string.IsNullOrEmpty(baseline.SupersedeReason) || baseline.SupersedeRequestId is null || string.IsNullOrEmpty(baseline.SupersedeInputHash))
                        throw new InvalidOperationException("PLM supersede audit is incomplete.");
                }
            }
        }
    }
}
