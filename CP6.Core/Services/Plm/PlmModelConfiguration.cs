using CP6.Entity.DomainModels.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plm;

public static class PlmModelConfiguration
{
    private const string Bin2 = "Latin1_General_100_BIN2";

    public static void Configure(ModelBuilder model)
    {
        model.HasDefaultSchema("plm");
        model.Entity<PlmIterationCandidate>(e =>
        {
            e.ToTable("IterationCandidate", "plm", t => { t.HasTrigger("TR_PlmIteration_Immutable"); t.UseSqlOutputClause(false); });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.Property(x => x.IterationKey).HasMaxLength(80).UseCollation(Bin2).IsRequired();
            e.Property(x => x.ReviewPackageDigest).HasColumnType("char(64)").UseCollation(Bin2);
            e.Property(x => x.Status).HasColumnType("varchar(12)").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.IterationKey, x.IterationVersion }).IsUnique().HasDatabaseName("UX_PlmIteration_Tenant_Key_Version");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PlmIteration_Version", "[IterationVersion] > 0");
                t.HasCheckConstraint("CK_PlmIteration_Status", "[Status] COLLATE Latin1_General_100_BIN2 = 'OPEN'");
                t.HasCheckConstraint("CK_PlmIteration_Key", "LEN([IterationKey]) > 0");
                t.HasCheckConstraint("CK_PlmIteration_ReviewDigest", "[ReviewPackageDigest] IS NULL OR (LEN([ReviewPackageDigest]) = 64 AND [ReviewPackageDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')");
            });
        });
        model.Entity<PlmEngineeringCandidate>(e =>
        {
            e.ToTable("EngineeringCandidate", "plm", t => { t.HasTrigger("TR_PlmCandidate_Protected"); t.UseSqlOutputClause(false); });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.Property(x => x.CandidateKey).HasMaxLength(80).UseCollation(Bin2).IsRequired();
            e.Property(x => x.ProductCd).HasMaxLength(20).UseCollation(Bin2).IsRequired();
            e.Property(x => x.Status).HasColumnType("varchar(12)").IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
            e.Property(x => x.UpdatedBy).HasMaxLength(100).IsRequired();
            e.HasOne<PlmIterationCandidate>().WithMany().HasForeignKey(x => new { x.TenantId, x.IterationCandidateId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.IterationCandidateId, x.CandidateKey }).IsUnique().HasDatabaseName("UX_PlmCandidate_Tenant_Iteration_Key");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PlmCandidate_Version", "[CandidateVersion] > 0");
                t.HasCheckConstraint("CK_PlmCandidate_Status", "[Status] COLLATE Latin1_General_100_BIN2 IN ('DRAFT','VALIDATED','FROZEN')");
                t.HasCheckConstraint("CK_PlmCandidate_Key", "LEN([CandidateKey]) > 0 AND LEN([ProductCd]) > 0");
            });
        });
        model.Entity<PlmTechnicalManifest>(e =>
        {
            e.ToTable("TechnicalManifest", "plm", t => { t.HasTrigger("TR_PlmManifest_Immutable"); t.UseSqlOutputClause(false); });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasAlternateKey(x => new { x.TenantId, x.Id, x.EngineeringCandidateId, x.CandidateVersion });
            e.Property(x => x.ProductCd).HasMaxLength(20).UseCollation(Bin2).IsRequired();
            e.Property(x => x.SchemaVersion).HasColumnType("varchar(40)").IsRequired();
            e.Property(x => x.CanonicalBytes).HasColumnType("varbinary(max)").IsRequired();
            e.Property(x => x.ManifestDigest).HasColumnType("char(64)").UseCollation(Bin2).IsRequired();
            e.Property(x => x.CaptureInputHash).HasColumnType("char(64)").UseCollation(Bin2).IsRequired();
            e.Property(x => x.CapturedAtUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.CapturedBy).HasMaxLength(100).IsRequired();
            e.HasOne<PlmEngineeringCandidate>().WithMany().HasForeignKey(x => new { x.TenantId, x.EngineeringCandidateId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.EngineeringCandidateId, x.CandidateVersion }).IsUnique().HasDatabaseName("UX_PlmManifest_Tenant_Candidate_Version");
            e.HasIndex(x => new { x.TenantId, x.CaptureRequestId }).IsUnique().HasDatabaseName("UX_PlmManifest_Tenant_CaptureRequest");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PlmManifest_Version", "[CandidateVersion] > 0 AND [ItemCount] > 0");
                t.HasCheckConstraint("CK_PlmManifest_Schema", "[SchemaVersion] COLLATE Latin1_General_100_BIN2 = 'plm-technical-manifest-v1' AND DATALENGTH([CanonicalBytes]) > 0");
                t.HasCheckConstraint("CK_PlmManifest_Digests", "LEN([ManifestDigest]) = 64 AND [ManifestDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([CaptureInputHash]) = 64 AND [CaptureInputHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
            });
        });
        model.Entity<PlmTechnicalManifestItem>(e =>
        {
            e.ToTable("TechnicalManifestItem", "plm", t => { t.HasTrigger("TR_PlmItem_Immutable"); t.UseSqlOutputClause(false); });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.Property(x => x.ItemType).HasColumnType("varchar(10)").IsRequired();
            e.Property(x => x.SourceOwnerType).HasColumnType("varchar(32)").IsRequired();
            e.Property(x => x.IdentityKey).HasMaxLength(256).UseCollation(Bin2).IsRequired();
            e.Property(x => x.ProductCd).HasMaxLength(20).UseCollation(Bin2).IsRequired();
            e.Property(x => x.ProcessCd).HasMaxLength(10).UseCollation(Bin2);
            e.Property(x => x.MaterialCd).HasMaxLength(20).UseCollation(Bin2);
            e.Property(x => x.TaskCd).HasMaxLength(10).UseCollation(Bin2);
            e.Property(x => x.ItemCanonicalBytes).HasColumnType("varbinary(max)").IsRequired();
            e.Property(x => x.ItemDigest).HasColumnType("char(64)").UseCollation(Bin2).IsRequired();
            e.Property(x => x.SourceRowVersion).HasColumnType("varbinary(8)");
            e.Property(x => x.CapturedAtUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.CapturedBy).HasMaxLength(100).IsRequired();
            e.HasOne<PlmTechnicalManifest>().WithMany().HasForeignKey(x => new { x.TenantId, x.ManifestId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.ManifestId, x.ItemType, x.IdentityKey }).IsUnique().HasDatabaseName("UX_PlmItem_Tenant_Manifest_Type_Identity");
            e.HasIndex(x => new { x.TenantId, x.ManifestId, x.ItemType, x.SemanticSortOrder }).IsUnique().HasFilter("[SemanticSortOrder] IS NOT NULL").HasDatabaseName("UX_PlmItem_Tenant_Manifest_Type_SortOrder");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PlmItem_Type", "([ItemType] COLLATE Latin1_General_100_BIN2='PRODUCT' AND [SourceOwnerType] COLLATE Latin1_General_100_BIN2='ProductMaster' AND [ProcessCd] IS NULL AND [MaterialCd] IS NULL AND [TaskCd] IS NULL AND [SemanticSortOrder] IS NULL) OR ([ItemType] COLLATE Latin1_General_100_BIN2='BOM' AND [SourceOwnerType] COLLATE Latin1_General_100_BIN2='ProductMaterial' AND [ProcessCd] IS NOT NULL AND [MaterialCd] IS NOT NULL AND [TaskCd] IS NULL AND [SemanticSortOrder]>0) OR ([ItemType] COLLATE Latin1_General_100_BIN2='ROUTING' AND [SourceOwnerType] COLLATE Latin1_General_100_BIN2='ProductProcess' AND [ProcessCd] IS NULL AND [MaterialCd] IS NULL AND [TaskCd] IS NOT NULL AND [SemanticSortOrder]>0)");
                t.HasCheckConstraint("CK_PlmItem_Bytes", "DATALENGTH([ItemCanonicalBytes]) > 0 AND LEN([IdentityKey]) > 0");
                t.HasCheckConstraint("CK_PlmItem_Digest", "LEN([ItemDigest]) = 64 AND [ItemDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
            });
        });
        model.Entity<PlmEngineeringBaseline>(e =>
        {
            e.ToTable("EngineeringBaseline", "plm", t => { t.HasTrigger("TR_PlmBaseline_Protected"); t.UseSqlOutputClause(false); });
            e.HasKey(x => x.Id);
            e.Property(x => x.ManifestDigest).HasColumnType("char(64)").UseCollation(Bin2).IsRequired();
            e.Property(x => x.FreezeInputHash).HasColumnType("char(64)").UseCollation(Bin2).IsRequired();
            e.Property(x => x.SupersedeInputHash).HasColumnType("char(64)").UseCollation(Bin2);
            e.Property(x => x.Status).HasColumnType("varchar(16)").IsRequired();
            e.Property(x => x.FrozenAtUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.SupersededAtUtc).HasColumnType("datetime2(7)");
            e.Property(x => x.FrozenBy).HasMaxLength(100).IsRequired();
            e.Property(x => x.SupersededBy).HasMaxLength(100);
            e.Property(x => x.SupersedeReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<PlmEngineeringCandidate>().WithMany().HasForeignKey(x => new { x.TenantId, x.EngineeringCandidateId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PlmTechnicalManifest>().WithMany().HasForeignKey(x => new { x.TenantId, x.ManifestId, x.EngineeringCandidateId, x.CandidateVersion }).HasPrincipalKey(x => new { x.TenantId, x.Id, x.EngineeringCandidateId, x.CandidateVersion }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.EngineeringCandidateId, x.CandidateVersion }).IsUnique().HasDatabaseName("UX_PlmBaseline_Tenant_Candidate_Version");
            e.HasIndex(x => new { x.TenantId, x.FreezeRequestId }).IsUnique().HasDatabaseName("UX_PlmBaseline_Tenant_FreezeRequest");
            e.HasIndex(x => new { x.TenantId, x.SupersedeRequestId }).IsUnique().HasFilter("[SupersedeRequestId] IS NOT NULL").HasDatabaseName("UX_PlmBaseline_Tenant_SupersedeRequest");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PlmBaseline_Version", "[CandidateVersion] > 0");
                t.HasCheckConstraint("CK_PlmBaseline_Status", "[Status] COLLATE Latin1_General_100_BIN2 IN ('FROZEN','SUPERSEDED')");
                t.HasCheckConstraint("CK_PlmBaseline_Digests", "LEN([ManifestDigest])=64 AND [ManifestDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([FreezeInputHash])=64 AND [FreezeInputHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                t.HasCheckConstraint("CK_PlmBaseline_SupersedeAudit", "([Status] COLLATE Latin1_General_100_BIN2='FROZEN' AND [SupersededAtUtc] IS NULL AND [SupersededBy] IS NULL AND [SupersedeReason] IS NULL AND [SupersedeRequestId] IS NULL AND [SupersedeInputHash] IS NULL) OR ([Status] COLLATE Latin1_General_100_BIN2='SUPERSEDED' AND [SupersededAtUtc] IS NOT NULL AND [SupersededBy] IS NOT NULL AND [SupersedeReason] IS NOT NULL AND [SupersedeRequestId] IS NOT NULL AND [SupersedeInputHash] IS NOT NULL)");
            });
        });
    }
}
