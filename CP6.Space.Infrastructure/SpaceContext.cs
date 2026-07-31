using CP6.Space.Application;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CP6.Space.Infrastructure;

public sealed class SpaceContext : DbContext
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_Space";

    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;

    public SpaceContext(
        DbContextOptions<SpaceContext> options,
        ISpaceExecutionContext execution,
        ISpaceClock clock)
        : base(options)
    {
        _execution = execution;
        _clock = clock;
    }

    public Guid CurrentTenantId => _execution.TenantId;

    public DbSet<SpaceModel> Models => Set<SpaceModel>();
    public DbSet<SpaceModelVersion> Versions => Set<SpaceModelVersion>();
    public DbSet<SpaceFile> Files => Set<SpaceFile>();
    public DbSet<SpaceModelSource> Sources => Set<SpaceModelSource>();
    public DbSet<SpaceArtifact> Artifacts => Set<SpaceArtifact>();
    public DbSet<SpaceJob> Jobs => Set<SpaceJob>();
    public DbSet<SpaceJobAttempt> JobAttempts => Set<SpaceJobAttempt>();
    public DbSet<SpaceJobStep> JobSteps => Set<SpaceJobStep>();
    public DbSet<SpaceModelIssue> Issues => Set<SpaceModelIssue>();
    public DbSet<SpaceIdempotencyRecord> IdempotencyRecords =>
        Set<SpaceIdempotencyRecord>();
    public DbSet<SpaceGenerationRun> GenerationRuns =>
        Set<SpaceGenerationRun>();
    public DbSet<SpaceGenerationProposal> GenerationProposals =>
        Set<SpaceGenerationProposal>();
    public DbSet<SpaceProposalDecision> ProposalDecisions =>
        Set<SpaceProposalDecision>();
    public DbSet<SpaceAiUsageRecord> AiUsageRecords =>
        Set<SpaceAiUsageRecord>();
    public DbSet<SpaceTenantAiWorkSlot> TenantAiWorkSlots =>
        Set<SpaceTenantAiWorkSlot>();
    public DbSet<SpaceAiBudgetReservation> AiBudgetReservations =>
        Set<SpaceAiBudgetReservation>();
    public DbSet<SpaceFloorRevision> FloorRevisions => Set<SpaceFloorRevision>();
    public DbSet<SpaceZoneRevision> ZoneRevisions => Set<SpaceZoneRevision>();
    public DbSet<SpaceAisleRevision> AisleRevisions => Set<SpaceAisleRevision>();
    public DbSet<SpaceRackRevision> RackRevisions => Set<SpaceRackRevision>();
    public DbSet<SpaceRackLevelRevision> RackLevelRevisions =>
        Set<SpaceRackLevelRevision>();
    public DbSet<SpaceLocationRevision> LocationRevisions =>
        Set<SpaceLocationRevision>();
    public DbSet<SpaceAsset> Assets => Set<SpaceAsset>();
    public DbSet<SpaceAssetVersion> AssetVersions => Set<SpaceAssetVersion>();
    public DbSet<SpaceElementRevision> ElementRevisions =>
        Set<SpaceElementRevision>();
    public DbSet<SpaceElementAttribute> ElementAttributes =>
        Set<SpaceElementAttribute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
        ConfigureVersion(modelBuilder);
        ConfigureFloorRevision(modelBuilder);
        ConfigureZoneRevision(modelBuilder);
        ConfigureAisleRevision(modelBuilder);
        ConfigureRackRevision(modelBuilder);
        ConfigureRackLevelRevision(modelBuilder);
        ConfigureLocationRevision(modelBuilder);
        ConfigureAsset(modelBuilder);
        ConfigureAssetVersion(modelBuilder);
        ConfigureElementRevision(modelBuilder);
        ConfigureElementAttribute(modelBuilder);
        ConfigureFile(modelBuilder);
        ConfigureSource(modelBuilder);
        ConfigureJob(modelBuilder);
        ConfigureJobAttempt(modelBuilder);
        ConfigureJobStep(modelBuilder);
        ConfigureArtifact(modelBuilder);
        ConfigureIssue(modelBuilder);
        ConfigureIdempotencyRecord(modelBuilder);
        ConfigureGenerationRun(modelBuilder);
        ConfigureGenerationProposal(modelBuilder);
        ConfigureProposalDecision(modelBuilder);
        ConfigureAiUsageRecord(modelBuilder);
        ConfigureTenantAiWorkSlot(modelBuilder);
        ConfigureAiBudgetReservation(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectPublishedHistory();
        ProtectPublishedSnapshotWrites();
        ProtectProposalDecisionHistory();
        ProtectAiCapacityLedger();
        ProtectAssetLibrary();
        StampAndValidateTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ProtectPublishedHistory();
        ProtectPublishedSnapshotWrites();
        ProtectProposalDecisionHistory();
        ProtectAiCapacityLedger();
        ProtectAssetLibrary();
        StampAndValidateTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureModel(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceModel>();
        entity.ToTable("Space_Model");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_Model_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Mode)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.CutoverState)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.LastMaterializedHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.SiteId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_Model_Tenant_Site_Active");
        entity.HasIndex(x => new { x.TenantId, x.ActiveDraftVersionId })
            .IsUnique()
            .HasFilter("[ActiveDraftVersionId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Space_Model_Tenant_ActiveDraft");
        entity.HasIndex(x => new { x.TenantId, x.CurrentPublishedVersionId })
            .IsUnique()
            .HasFilter("[CurrentPublishedVersionId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Space_Model_Tenant_CurrentPublished");

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.Id, x.ActiveDraftVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_Model_ActiveDraft_Tenant_Model_Version");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.Id, x.CurrentPublishedVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_Model_CurrentPublished_Tenant_Model_Version");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureVersion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceModelVersion>();
        entity.ToTable("Space_ModelVersion");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.ModelId, x.Id })
            .HasName("AK_Space_ModelVersion_TenantId_ModelId_Id");
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ModelVersion_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ContentHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.ValidatedHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.WmsCapabilityHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.RuleSetVersion).HasMaxLength(50);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.ModelId, x.VersionNo })
            .IsUnique()
            .HasDatabaseName("UX_Space_ModelVersion_Tenant_Model_VersionNo");
        entity.HasIndex(x => new { x.TenantId, x.ModelId, x.Status })
            .HasDatabaseName("IX_Space_ModelVersion_Tenant_Model_Status");
        entity.HasIndex(x => new { x.TenantId, x.BasedOnVersionId })
            .HasFilter("[BasedOnVersionId] IS NOT NULL")
            .HasDatabaseName("IX_Space_ModelVersion_Tenant_BasedOn");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.ModelId,
            x.CloneOperationId,
        })
            .IsUnique()
            .HasFilter("[CloneOperationId] IS NOT NULL")
            .HasDatabaseName("UX_Space_ModelVersion_Tenant_Model_CloneOperation");

        entity.HasOne<SpaceModel>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelVersion_Space_Model_Tenant_Model");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelId, x.BasedOnVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelVersion_BasedOn_Tenant_Model_Version");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureFloorRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceFloorRevision>(
            modelBuilder,
            "Space_FloorRevision");
        entity.Property(x => x.FloorCode).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.BoundaryJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.CoordinateSystem).HasMaxLength(100).IsRequired();
        entity.Property(x => x.UnderlayScale).HasColumnType("decimal(18,8)");
        entity.Property(x => x.UnderlayRotationZ).HasColumnType("decimal(9,4)");

        entity.HasIndex(x => new { x.TenantId, x.ModelVersionId, x.FloorCode })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_FloorRevision_Version_Code_Active");
        entity.HasIndex(x => new { x.TenantId, x.ModelVersionId, x.Level })
            .HasDatabaseName("IX_Space_FloorRevision_Version_Level");
        entity.HasOne<SpaceModelSource>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.UnderlaySourceId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelVersionId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_FloorRevision_UnderlaySource_Tenant_Version");
    }

    private void ConfigureZoneRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceZoneRevision>(
            modelBuilder,
            "Space_ZoneRevision");
        entity.Property(x => x.ZoneCode).HasMaxLength(100).IsRequired();
        entity.Property(x => x.ZoneType).HasColumnType("smallint");
        entity.Property(x => x.PolygonJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.Color).HasMaxLength(50);
        entity.Property(x => x.CapabilityFlags).HasMaxLength(1000);

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.FloorLogicalId,
                    x.ZoneCode,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ZoneRevision_Floor_Code_Active");
        entity.HasOne<SpaceFloorRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ZoneRevision_Floor_Tenant_Version_Logical");
    }

    private void ConfigureAisleRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceAisleRevision>(
            modelBuilder,
            "Space_AisleRevision");
        entity.Property(x => x.AisleCode).HasMaxLength(100).IsRequired();
        entity.Property(x => x.PolygonJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.CenterlineJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.Direction).HasColumnType("smallint");

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.ZoneLogicalId,
                    x.AisleCode,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_AisleRevision_Zone_Code_Active");
        entity.HasOne<SpaceZoneRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.ZoneLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_AisleRevision_Zone_Tenant_Version_Logical");
    }

    private void ConfigureRackRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceRackRevision>(
            modelBuilder,
            "Space_RackRevision");
        entity.ToTable(
            "Space_RackRevision",
            table => table.HasCheckConstraint(
                "CK_Space_RackRevision_Geometry",
                "[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Depth] >= 0 AND [Height] >= 0"));
        entity.Property(x => x.RackCode).HasMaxLength(100).IsRequired();
        entity.Property(x => x.RotationZ).HasColumnType("decimal(9,4)");

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.ZoneLogicalId,
                    x.RackCode,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_RackRevision_Zone_Code_Active");
        entity.HasOne<SpaceFloorRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_RackRevision_Floor_Tenant_Version_Logical");
        entity.HasOne<SpaceZoneRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.ZoneLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_RackRevision_Zone_Tenant_Version_Logical");
        entity.HasOne<SpaceAisleRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.AisleLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_RackRevision_Aisle_Tenant_Version_Logical");
    }

    private void ConfigureRackLevelRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceRackLevelRevision>(
            modelBuilder,
            "Space_RackLevelRevision");
        entity.ToTable(
            "Space_RackLevelRevision",
            table => table.HasCheckConstraint(
                "CK_Space_RackLevelRevision_Dimensions",
                "[LevelNo] > 0 AND [BottomZ] >= 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND [BeamHeight] >= 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)"));
        entity.Property(x => x.MaxLoad).HasColumnType("decimal(18,4)");

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.RackLogicalId,
                    x.LevelNo,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_RackLevelRevision_Rack_Level_Active");
        entity.HasOne<SpaceRackRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.RackLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_RackLevelRevision_Rack_Tenant_Version_Logical");
    }

    private void ConfigureLocationRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceLocationRevision>(
            modelBuilder,
            "Space_LocationRevision");
        entity.ToTable(
            "Space_LocationRevision",
            table => table.HasCheckConstraint(
                "CK_Space_LocationRevision_Dimensions",
                "[ColumnNo] > 0 AND [LevelNo] > 0 AND [DepthNo] > 0 AND [Width] > 0 AND [Height] > 0 AND [Depth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)"));
        entity.Property(x => x.LocationCode).HasMaxLength(200);
        entity.Property(x => x.MaxLoad).HasColumnType("decimal(18,4)");
        entity.Property(x => x.CodeOrigin)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ExternalBindingState)
            .HasConversion<short>()
            .HasColumnType("smallint");

        entity.HasIndex(x => new { x.TenantId, x.ModelVersionId, x.LocationCode })
            .IsUnique()
            .HasFilter("[LocationCode] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Space_LocationRevision_Version_Code_Active");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.RackLogicalId,
                    x.LevelNo,
                    x.ColumnNo,
                    x.DepthNo,
                })
            .HasFilter("[RackLogicalId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Space_LocationRevision_Rack_Position_Active");
        entity.HasOne<SpaceFloorRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_LocationRevision_Floor_Tenant_Version_Logical");
        entity.HasOne<SpaceRackRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.RackLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_LocationRevision_Rack_Tenant_Version_Logical");
    }

    private void ConfigureAsset(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceAsset>();
        entity.ToTable(
            "Space_Asset",
            table => table.HasCheckConstraint(
                "CK_Space_Asset_ScopeOwner",
                "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR " +
                "([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new
        {
            x.Scope,
            x.OwnerTenantId,
            x.Id,
        })
            .HasName("AK_Space_Asset_Scope_Owner_Id");
        entity.Property(x => x.Scope)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.AssetCode).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
        {
            x.Scope,
            x.OwnerTenantId,
            x.AssetCode,
        })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_Asset_Scope_Owner_Code_Active");
        entity.HasIndex(x => new
        {
            x.Scope,
            x.OwnerTenantId,
            x.Category,
        })
            .HasDatabaseName("IX_Space_Asset_Scope_Owner_Category");

        entity.HasQueryFilter(
            x => !x.IsDeleted &&
                 (x.Scope == SpaceAssetScope.System ||
                  x.OwnerTenantId == CurrentTenantId));
    }

    private void ConfigureAssetVersion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceAssetVersion>();
        entity.ToTable(
            "Space_AssetVersion",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_AssetVersion_ScopeOwner",
                    "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR " +
                    "([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                table.HasCheckConstraint(
                    "CK_Space_AssetVersion_VersionNo",
                    "[VersionNo] > 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new
        {
            x.Scope,
            x.OwnerTenantId,
            x.Id,
        })
            .HasName("AK_Space_AssetVersion_Scope_Owner_Id");
        entity.Property(x => x.Scope)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.Format)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ParameterSchemaJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.PreviewRef).HasMaxLength(500);
        entity.Property(x => x.RenderArtifactRef).HasMaxLength(500);
        entity.Property(x => x.ContentHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
        {
            x.Scope,
            x.OwnerTenantId,
            x.AssetId,
            x.VersionNo,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_AssetVersion_Scope_Owner_Asset_VersionNo");
        entity.HasOne<SpaceAsset>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.Scope,
                x.OwnerTenantId,
                x.AssetId,
            })
            .HasPrincipalKey(x => new
            {
                x.Scope,
                x.OwnerTenantId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_AssetVersion_Asset_Scope_Owner_Asset");

        entity.HasQueryFilter(
            x => x.Scope == SpaceAssetScope.System ||
                 x.OwnerTenantId == CurrentTenantId);
    }

    private void ConfigureElementRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceElementRevision>(
            modelBuilder,
            "Space_ElementRevision");
        entity.ToTable(
            "Space_ElementRevision",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_ElementRevision_Geometry",
                    "[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Height] >= 0 AND [Depth] >= 0");
                table.HasCheckConstraint(
                    "CK_Space_ElementRevision_ModelAssetScope",
                    "([ModelAssetId] IS NULL AND [ModelAssetScope] IS NULL AND [ModelAssetOwnerTenantId] IS NULL) OR " +
                    "([ModelAssetId] IS NOT NULL AND [ModelAssetScope] IS NOT NULL AND [ModelAssetOwnerTenantId] IS NOT NULL AND " +
                    "(([ModelAssetScope] = 0 AND [ModelAssetOwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR " +
                    "([ModelAssetScope] = 1 AND [ModelAssetOwnerTenantId] = [TenantId])))");
            });
        entity.Property(x => x.ElementType).HasMaxLength(100).IsRequired();
        entity.Property(x => x.GeometryJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.ModelAssetScope)
            .HasConversion<short?>()
            .HasColumnType("smallint");
        entity.Property(x => x.RotationZ).HasColumnType("decimal(9,4)");
        entity.Property(x => x.BusinessCode).HasMaxLength(200);
        entity.Property(x => x.LinkedEntityType).HasMaxLength(100);

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.FloorLogicalId,
                    x.ElementType,
                })
            .HasDatabaseName("IX_Space_ElementRevision_Floor_Type");
        entity.HasOne<SpaceFloorRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ElementRevision_Floor_Tenant_Version_Logical");
        entity.HasOne<SpaceElementRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.ParentLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ElementRevision_Parent_Tenant_Version_Logical");
        entity.HasOne<SpaceAssetVersion>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.ModelAssetScope,
                x.ModelAssetOwnerTenantId,
                x.ModelAssetId,
            })
            .HasPrincipalKey(x => new
            {
                x.Scope,
                x.OwnerTenantId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ElementRevision_AssetVersion_Scope_Owner_Version");
    }

    private void ConfigureElementAttribute(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceElementAttribute>();
        entity.ToTable("Space_ElementAttribute");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.Property(x => x.Namespace).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
        entity.Property(x => x.ValueType).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Value).HasMaxLength(8000);
        entity.Property(x => x.Unit).HasMaxLength(50);

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.ElementRevisionId,
                    x.Namespace,
                    x.Key,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ElementAttribute_Element_Key_Active");
        entity.HasOne<SpaceElementRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.ElementRevisionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ElementAttribute_Element_Tenant_Version");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity>
        ConfigureRevision<TEntity>(
            ModelBuilder modelBuilder,
            string tableName)
        where TEntity : SpaceRevisionEntity
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.ToTable(tableName);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.ModelVersionId, x.Id });
        entity.HasAlternateKey(x => new
        {
            x.TenantId,
            x.ModelVersionId,
            x.LogicalId,
        });
        ConfigureTenantEntity(entity);
        entity.Property(x => x.SourceRef).HasMaxLength(500);
        entity.Property(x => x.LifecycleState)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<SpaceModelSource>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.SourceId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelVersionId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
        return entity;
    }

    private void ConfigureFile(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceFile>();
        entity.ToTable(
            "Space_File",
            table => table.HasCheckConstraint(
                "CK_Space_File_ContentDeletion",
                "[ContentDeletedAtUtc] IS NULL OR ([State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [IsDeleted] = 1)"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_File_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        entity.Property(x => x.OriginalName).HasMaxLength(260).IsRequired();
        entity.Property(x => x.DeclaredContentType).HasMaxLength(200);
        entity.Property(x => x.DetectedContentType).HasMaxLength(200);
        entity.Property(x => x.Extension).HasMaxLength(20);
        entity.Property(x => x.Sha256)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.State)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ScanEngine).HasMaxLength(100);
        entity.Property(x => x.SignatureVersion).HasMaxLength(100);
        entity.Property(x => x.ScanResultCode).HasMaxLength(100);
        entity.Property(x => x.RetentionClass)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.RetainUntilUtc).HasColumnType("datetime2");
        entity.Property(x => x.DeletionRequestedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.ContentDeletedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => x.StorageKey)
            .IsUnique()
            .HasDatabaseName("UX_Space_File_StorageKey");
        entity.HasIndex(x => new { x.TenantId, x.Sha256, x.RetentionClass })
            .IsUnique()
            .HasFilter("[Sha256] IS NOT NULL AND [State] IN (1, 2, 3) AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Space_File_Tenant_Hash_Retention_Reusable");
        entity.HasIndex(x => new { x.TenantId, x.State })
            .HasDatabaseName("IX_Space_File_Tenant_State");
        entity.HasIndex(x => new { x.TenantId, x.RetainUntilUtc, x.State })
            .HasFilter("[RetainUntilUtc] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Space_File_Tenant_Retention");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.DeletionRequestedAtUtc,
                    x.ContentDeletedAtUtc,
                })
            .HasFilter(
                "[State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [ContentDeletedAtUtc] IS NULL")
            .HasDatabaseName("IX_Space_File_Tenant_PendingObjectDeletion");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureSource(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceModelSource>();
        entity.ToTable("Space_ModelSource");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.ModelVersionId, x.Id })
            .HasName("AK_Space_ModelSource_TenantId_ModelVersionId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.SourceType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.DisplayName).HasMaxLength(260).IsRequired();
        entity.Property(x => x.Sha256)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.ParserVersion).HasMaxLength(100);
        entity.Property(x => x.Unit).HasMaxLength(50);
        entity.Property(x => x.ScaleToMillimeters).HasColumnType("decimal(18,8)");
        entity.Property(x => x.TransformJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.State)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.Sha256 })
            .HasDatabaseName("IX_Space_ModelSource_Tenant_SourceHash");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.Sha256,
                    x.SourceType,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ModelSource_Version_Hash_Type_Active");
        entity.HasIndex(x => new { x.TenantId, x.FileId })
            .HasFilter("[FileId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Space_ModelSource_Tenant_File_Active");

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelSource_Version_Tenant");
        entity.HasOne<SpaceFile>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FileId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelSource_File_Tenant");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureArtifact(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceArtifact>();
        entity.ToTable("Space_Artifact");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_Artifact_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.ArtifactType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.SchemaVersion).HasMaxLength(50).IsRequired();

        entity.HasIndex(x => new { x.TenantId, x.ModelVersionId })
            .HasDatabaseName("IX_Space_Artifact_Tenant_Version");
        entity.HasIndex(x => new { x.TenantId, x.JobId })
            .HasFilter("[JobId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Space_Artifact_Tenant_Job_Active");
        entity.HasIndex(x => new { x.TenantId, x.FileId })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Space_Artifact_Tenant_File_Active");
        entity.HasIndex(x => new { x.TenantId, x.ModelVersionId, x.SourceId })
            .HasFilter("[SourceId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Space_Artifact_Tenant_Version_Source_Active");

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_Artifact_Version_Tenant");
        entity.HasOne<SpaceFile>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FileId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_Artifact_File_Tenant");
        entity.HasOne<SpaceModelSource>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId, x.SourceId })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelVersionId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_Artifact_Source_Tenant_Version");
        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.JobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_Artifact_Job_Tenant");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureJob(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceJob>();
        entity.ToTable(
            "Space_Job",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_Job_Attempts",
                    "[AttemptCount] >= 0 AND [MaxAttempts] BETWEEN 1 AND 20 AND [AttemptCount] <= [MaxAttempts]");
                table.HasCheckConstraint(
                    "CK_Space_Job_Progress",
                    "[ProgressDone] >= 0 AND [ProgressTotal] >= 0 AND ([ProgressTotal] = 0 OR [ProgressDone] <= [ProgressTotal])");
                table.HasCheckConstraint(
                    "CK_Space_Job_Lease",
                    "([Status] = 1 AND [LockedBy] IS NOT NULL AND [LockedAtUtc] IS NOT NULL AND [LockExpiresAtUtc] IS NOT NULL AND [ActiveAttemptId] IS NOT NULL) OR ([Status] <> 1 AND [LockedBy] IS NULL AND [LockedAtUtc] IS NULL AND [LockExpiresAtUtc] IS NULL AND [ActiveAttemptId] IS NULL)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_Job_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.JobType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.SubjectType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.BusinessKey)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.InputHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.LockedBy).HasMaxLength(200);
        entity.Property(x => x.ProgressStage).HasMaxLength(100);
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.ResultSummaryJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.LastFailureKind)
            .HasConversion<short?>()
            .HasColumnType("smallint");
        entity.Property(x => x.LastErrorCode).HasMaxLength(100);
        entity.Property(x => x.LastErrorSummary).HasMaxLength(1000);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.JobType, x.BusinessKey })
            .IsUnique()
            .HasFilter("[Status] IN (0, 1) AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Space_Job_Tenant_Type_BusinessKey_Active");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.Status,
                    x.NextAttemptAtUtc,
                    x.LockExpiresAtUtc,
                    x.Priority,
                    x.RequestedAtUtc,
                })
            .HasDatabaseName("IX_Space_Job_Tenant_Claim");
        entity.HasIndex(x => new { x.TenantId, x.SubjectType, x.SubjectId })
            .HasDatabaseName("IX_Space_Job_Tenant_Subject");
        entity.HasIndex(x => new { x.TenantId, x.CorrelationId })
            .HasDatabaseName("IX_Space_Job_Tenant_Correlation");

        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RetryOfJobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_Job_RetryOf_Tenant");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureJobAttempt(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceJobAttempt>();
        entity.ToTable(
            "Space_JobAttempt",
            table => table.HasCheckConstraint(
                "CK_Space_JobAttempt_OutcomeTime",
                "([Outcome] = 0 AND [FinishedAtUtc] IS NULL) OR ([Outcome] <> 0 AND [FinishedAtUtc] IS NOT NULL)"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_JobAttempt_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.WorkerId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Outcome)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.InputHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.ProcessorVersion).HasMaxLength(100).IsRequired();
        entity.Property(x => x.ResourceUsageJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.FailureKind)
            .HasConversion<short?>()
            .HasColumnType("smallint");
        entity.Property(x => x.ErrorCode).HasMaxLength(100);
        entity.Property(x => x.SanitizedError).HasMaxLength(1000);

        entity.HasIndex(x => new { x.TenantId, x.JobId, x.AttemptNo })
            .IsUnique()
            .HasDatabaseName("UX_Space_JobAttempt_Tenant_Job_AttemptNo");
        entity.HasIndex(x => new { x.TenantId, x.JobId, x.StartedAtUtc })
            .HasDatabaseName("IX_Space_JobAttempt_Tenant_Job_Started");

        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.JobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_JobAttempt_Job_Tenant");
        entity.HasOne<SpaceArtifact>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiagnosticArtifactId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_JobAttempt_DiagnosticArtifact_Tenant");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureJobStep(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceJobStep>();
        entity.ToTable(
            "Space_JobStep",
            table => table.HasCheckConstraint(
                "CK_Space_JobStep_StatusTime",
                "([Status] = 0 AND [FinishedAtUtc] IS NULL) OR ([Status] <> 0 AND [FinishedAtUtc] IS NOT NULL)"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);

        entity.Property(x => x.StepCode).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.CheckpointJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.OutputHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);

        entity.HasIndex(x => new { x.TenantId, x.AttemptId, x.StepCode })
            .IsUnique()
            .HasDatabaseName("UX_Space_JobStep_Tenant_Attempt_StepCode");
        entity.HasIndex(x => new { x.TenantId, x.AttemptId, x.StepNo })
            .IsUnique()
            .HasDatabaseName("UX_Space_JobStep_Tenant_Attempt_StepNo");

        entity.HasOne<SpaceJobAttempt>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AttemptId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_JobStep_Attempt_Tenant");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureIssue(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceModelIssue>();
        entity.ToTable(
            "Space_ModelIssue",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_ModelIssue_Context",
                    "[ModelVersionId] IS NOT NULL OR [SourceId] IS NOT NULL OR [JobId] IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_Space_ModelIssue_SourceVersion",
                    "[SourceId] IS NULL OR [ModelVersionId] IS NOT NULL");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Severity)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.Code).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SourceRef).HasMaxLength(500);
        entity.Property(x => x.MessageArgsJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.SuggestedActionCode).HasMaxLength(100);
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.AcknowledgementReason).HasMaxLength(1000);

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.Status,
                    x.Severity,
                    x.Code,
                })
            .HasDatabaseName("IX_Space_ModelIssue_Tenant_Version_Status");
        entity.HasIndex(x => new { x.TenantId, x.JobId, x.Status })
            .HasFilter("[JobId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Space_ModelIssue_Tenant_Job_Status");

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelIssue_Version_Tenant");
        entity.HasOne<SpaceModelSource>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId, x.SourceId })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelVersionId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelIssue_Source_Tenant_Version");
        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.JobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelIssue_Job_Tenant");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureIdempotencyRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceIdempotencyRecord>();
        entity.ToTable("Space_IdempotencyRecord");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Operation).HasMaxLength(100).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.RequestHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.ResponseJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.ReplayUntilUtc).HasColumnType("datetime2");
        entity.Property(x => x.RetainUntilUtc).HasColumnType("datetime2");

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.PrincipalId,
                    x.Operation,
                    x.IdempotencyKeyHash,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_IdempotencyRecord_Tenant_Principal_Operation_Key");
        entity.HasIndex(x => new { x.TenantId, x.RetainUntilUtc })
            .HasDatabaseName(
                "IX_Space_IdempotencyRecord_Tenant_Retention");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureGenerationRun(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceGenerationRun>();
        entity.ToTable(
            "Space_GenerationRun",
            table => table.HasCheckConstraint(
                "CK_Space_GenerationRun_Progress",
                "[Progress] >= 0 AND [Progress] <= 100"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_GenerationRun_TenantId_Id");
        ConfigureTenantEntity(entity);

        ConfigureHash(entity.Property(x => x.SourceHash));
        ConfigureHash(entity.Property(x => x.IdempotencyKeyHash));
        ConfigureHash(entity.Property(x => x.BusinessKeyHash));
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.PolicySnapshot)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.RuleVersion)
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.ProviderCode).HasMaxLength(64);
        entity.Property(x => x.ProviderModel).HasMaxLength(128);
        entity.Property(x => x.InputSchemaVersion)
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(x => x.OutputSchemaVersion).HasMaxLength(32);
        entity.Property(x => x.FailureCode).HasMaxLength(64);
        entity.Property(x => x.FailureSummary).HasMaxLength(1024);
        entity.Property(x => x.DegradedReason).HasMaxLength(64);
        entity.Property(x => x.CancelRequestedAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.CancelledAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.ReviewCompletedAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.BusinessKeyHash })
            .IsUnique()
            .HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName(
                "UX_GenerationRun_Tenant_Business_Current");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.SiteId,
                    x.Status,
                    x.CreatedAtUtc,
                })
            .IsDescending(false, false, false, true)
            .HasDatabaseName(
                "IX_GenerationRun_Tenant_Site_Status_Created");
        entity.HasIndex(x => new { x.TenantId, x.JobId })
            .HasDatabaseName("IX_GenerationRun_Tenant_Job");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.IsCurrent,
                })
            .HasDatabaseName(
                "IX_GenerationRun_Tenant_Version_Current");

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationRun_Version_Tenant");
        entity.HasOne<SpaceModelSource>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.SourceId,
                })
            .HasPrincipalKey(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.Id,
                })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationRun_Source_Tenant_Version");
        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.JobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationRun_Job_Tenant");
        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BasedOnRunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationRun_BasedOn_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureGenerationProposal(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceGenerationProposal>();
        entity.ToTable(
            "Space_GenerationProposal",
            table => table.HasCheckConstraint(
                "CK_Space_GenerationProposal_Confidence",
                "[ConfidenceScore] >= 0 AND [ConfidenceScore] <= 1"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName(
                "AK_Space_GenerationProposal_TenantId_Id");
        entity.HasAlternateKey(
                x => new { x.TenantId, x.RunId, x.Id })
            .HasName(
                "AK_Space_GenerationProposal_Tenant_Run_Id");
        ConfigureTenantEntity(entity);

        ConfigureHash(entity.Property(x => x.SourceHash));
        entity.Property(x => x.SourceKey)
            .HasMaxLength(256)
            .IsRequired();
        entity.Property(x => x.ProposalType)
            .HasMaxLength(64)
            .IsRequired();
        ConfigureJson(entity.Property(x => x.SuggestedGeometryJson));
        ConfigureJson(entity.Property(x => x.SuggestedAttributesJson));
        ConfigureJson(entity.Property(x => x.SuggestedRelationsJson));
        ConfigureJson(entity.Property(x => x.SourceRefsJson));
        ConfigureJson(entity.Property(x => x.EvidenceJson));
        ConfigureJson(entity.Property(x => x.FieldProvenanceJson));
        entity.Property(x => x.HumanPatchJson)
            .HasColumnType("nvarchar(max)");
        entity.Property(x => x.LockedFieldsJson)
            .HasColumnType("nvarchar(max)");
        entity.Property(x => x.ConfidenceScore)
            .HasColumnType("decimal(6,5)");
        entity.Property(x => x.ConfidenceBand)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.RunId,
                    x.SourceKey,
                    x.ProposalType,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Proposal_Tenant_Run_Source_Type");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.RunId,
                    x.Status,
                    x.ConfidenceBand,
                    x.ProposalType,
                    x.Id,
                })
            .HasDatabaseName(
                "IX_Proposal_Tenant_Run_Status_Band_Type");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationProposal_Run_Tenant");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationProposal_Version_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureProposalDecision(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceProposalDecision>();
        entity.ToTable("Space_ProposalDecision");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ProposalDecision_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.DecisionType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        ConfigureJson(entity.Property(x => x.BeforeJson));
        entity.Property(x => x.AfterJson)
            .HasColumnType("nvarchar(max)");
        entity.Property(x => x.LockedFieldsJson)
            .HasColumnType("nvarchar(max)");
        entity.Property(x => x.ReasonCode).HasMaxLength(64);
        entity.Property(x => x.Comment).HasMaxLength(512);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.RunId,
                    x.ProposalId,
                    x.CreatedAtUtc,
                })
            .HasDatabaseName(
                "IX_ProposalDecision_Tenant_Run_Proposal_Created");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.DecisionBatchId,
                    x.Id,
                })
            .HasDatabaseName(
                "IX_ProposalDecision_Tenant_Batch");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ProposalDecision_Run_Tenant");
        entity.HasOne<SpaceGenerationProposal>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.TenantId,
                    x.RunId,
                    x.ProposalId,
                })
            .HasPrincipalKey(
                x => new
                {
                    x.TenantId,
                    x.RunId,
                    x.Id,
                })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ProposalDecision_Proposal_Tenant_Run");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureAiUsageRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceAiUsageRecord>();
        entity.ToTable(
            "Space_AiUsageRecord",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_AiUsageRecord_Units",
                    "[InputUnits] >= 0 AND [OutputUnits] >= 0");
                table.HasCheckConstraint(
                    "CK_Space_AiUsageRecord_Cost",
                    "[EstimatedCostMinor] >= 0 AND " +
                    "([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)");
                table.HasCheckConstraint(
                    "CK_Space_AiUsageRecord_Latency",
                    "[LatencyMs] >= 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_AiUsageRecord_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.ProviderCode)
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.ProviderModel)
            .HasMaxLength(128)
            .IsRequired();
        ConfigureHash(entity.Property(x => x.ProviderRequestIdHash));
        entity.Property(x => x.Currency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(3);
        entity.Property(x => x.Outcome)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.RecordedAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ProviderRequestIdHash,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_AiUsage_Tenant_ProviderRequest");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.RunId,
                    x.RecordedAtUtc,
                })
            .HasDatabaseName(
                "IX_AiUsage_Tenant_Run_Recorded");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_AiUsageRecord_Run_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureTenantAiWorkSlot(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceTenantAiWorkSlot>();
        entity.ToTable(
            "Space_TenantAiWorkSlot",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_TenantAiWorkSlot_SlotNo",
                    "[SlotNo] >= 1 AND [SlotNo] <= 3");
                table.HasCheckConstraint(
                    "CK_Space_TenantAiWorkSlot_Lease",
                    "([RunId] IS NULL AND [LeaseOwner] IS NULL AND " +
                    "[LeaseExpiresAtUtc] IS NULL) OR " +
                    "([RunId] IS NOT NULL AND [LeaseOwner] IS NOT NULL AND " +
                    "[LeaseExpiresAtUtc] IS NOT NULL)");
            });
        entity.HasKey(x => new { x.TenantId, x.SlotNo });
        entity.Property(x => x.TenantId).ValueGeneratedNever();
        entity.Property(x => x.LeaseOwner)
            .HasMaxLength(128);
        entity.Property(x => x.LeaseExpiresAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.RunId })
            .IsUnique()
            .HasFilter("[RunId] IS NOT NULL")
            .HasDatabaseName(
                "UX_TenantAiWorkSlot_Tenant_Run");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.LeaseExpiresAtUtc,
                    x.SlotNo,
                })
            .HasDatabaseName(
                "IX_TenantAiWorkSlot_Tenant_Expiry");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_TenantAiWorkSlot_Run_Tenant");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId);
    }

    private void ConfigureAiBudgetReservation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceAiBudgetReservation>();
        entity.ToTable(
            "Space_AiBudgetReservation",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_AiBudgetReservation_Cost",
                    "[ReservedCostMinor] >= 0 AND " +
                    "([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)");
                table.HasCheckConstraint(
                    "CK_Space_AiBudgetReservation_Period",
                    "[PeriodMonth] = YEAR([PeriodDay]) * 100 + " +
                    "MONTH([PeriodDay])");
                table.HasCheckConstraint(
                    "CK_Space_AiBudgetReservation_Currency",
                    "[ReservedCostMinor] = 0 OR [Currency] IS NOT NULL");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName(
                "AK_Space_AiBudgetReservation_TenantId_Id");
        ConfigureTenantEntity(entity);

        ConfigureHash(entity.Property(x => x.ProviderRequestKey));
        entity.Property(x => x.PeriodDay)
            .HasColumnType("date");
        entity.Property(x => x.Currency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(3);
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ExpiresAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ProviderRequestKey,
                })
            .IsUnique()
            .HasDatabaseName(
                "UX_AiBudgetReservation_Tenant_Request");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.Currency,
                    x.PeriodDay,
                    x.Status,
                })
            .HasDatabaseName(
                "IX_AiBudgetReservation_Tenant_Day");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.Currency,
                    x.PeriodMonth,
                    x.Status,
                })
            .HasDatabaseName(
                "IX_AiBudgetReservation_Tenant_Month");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_AiBudgetReservation_Run_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private static void ConfigureHash(
        Microsoft.EntityFrameworkCore.Metadata.Builders
            .PropertyBuilder<string> property)
    {
        property
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
    }

    private static void ConfigureJson(
        Microsoft.EntityFrameworkCore.Metadata.Builders
            .PropertyBuilder<string> property)
    {
        property
            .HasColumnType("nvarchar(max)")
            .IsRequired();
    }

    private static void ConfigureTenantEntity<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : SpaceTenantEntity
    {
        entity.Property(x => x.TenantId).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.ModifiedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.IsDeleted).HasDefaultValue(false);
    }

    private void ProtectPublishedHistory()
    {
        foreach (var entry in ChangeTracker.Entries<SpaceModelVersion>())
        {
            if (entry.State == EntityState.Deleted)
                throw new SpaceVersionStateException("Version history cannot be physically deleted.");

            if (entry.State != EntityState.Modified)
                continue;

            var original = entry.Property(x => x.Status).OriginalValue;
            var current = entry.Property(x => x.Status).CurrentValue;

            var allowedTerminalTransition =
                original == SpaceVersionStatus.Published &&
                current == SpaceVersionStatus.Superseded;

            if (original is SpaceVersionStatus.Published or SpaceVersionStatus.Superseded &&
                !allowedTerminalTransition)
            {
                throw new SpaceVersionStateException("Published and Superseded versions are immutable.");
            }
        }
    }

    private void ProtectPublishedSnapshotWrites()
    {
        var versionIds = ChangeTracker.Entries()
            .Where(entry => entry.State is
                EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity switch
            {
                SpaceRevisionEntity revision => revision.ModelVersionId,
                SpaceElementAttribute attribute => attribute.ModelVersionId,
                SpaceModelSource source => source.ModelVersionId,
                _ => Guid.Empty,
            })
            .Where(versionId => versionId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (versionIds.Length == 0)
            return;

        var trackedStatuses = ChangeTracker.Entries<SpaceModelVersion>()
            .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity.Status);
        var missingIds = versionIds
            .Where(versionId => !trackedStatuses.ContainsKey(versionId))
            .ToArray();
        var persistedStatuses = missingIds.Length == 0
            ? new Dictionary<Guid, SpaceVersionStatus>()
            : Versions
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(version =>
                    version.TenantId == CurrentTenantId &&
                    missingIds.Contains(version.Id))
                .ToDictionary(version => version.Id, version => version.Status);

        foreach (var versionId in versionIds)
        {
            var status = trackedStatuses.TryGetValue(versionId, out var tracked)
                ? tracked
                : persistedStatuses.TryGetValue(versionId, out var persisted)
                    ? persisted
                    : throw new SpaceVersionStateException(
                        "Snapshot content references an unknown model version.");
            if (status is SpaceVersionStatus.Published or SpaceVersionStatus.Superseded)
            {
                throw new SpaceVersionStateException(
                    "Published and Superseded snapshots are immutable.");
            }
        }
    }

    private void ProtectProposalDecisionHistory()
    {
        foreach (var entry in ChangeTracker
            .Entries<SpaceProposalDecision>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new SpaceProposalStateException(
                    "Proposal decisions are append-only.");
            }
        }
    }

    private void ProtectAiCapacityLedger()
    {
        foreach (var entry in ChangeTracker
            .Entries<SpaceTenantAiWorkSlot>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new SpaceAiCapacityStateException(
                    "AI work-slot rows cannot be deleted.");
            }
            if (entry.State == EntityState.Detached)
                continue;
            if (entry.Entity.TenantId != CurrentTenantId ||
                entry.State == EntityState.Modified &&
                entry.Property(x => x.TenantId).OriginalValue !=
                CurrentTenantId)
            {
                throw new SpaceTenantScopeException(
                    "A cross-tenant AI work-slot write was rejected.");
            }
        }

        foreach (var entry in ChangeTracker
            .Entries<SpaceAiBudgetReservation>())
        {
            if (entry.State == EntityState.Deleted ||
                entry.State == EntityState.Modified &&
                entry.Property(x => x.IsDeleted).IsModified)
            {
                throw new SpaceAiCapacityStateException(
                    "AI budget reservations cannot be deleted.");
            }
        }
    }

    private void ProtectAssetLibrary()
    {
        foreach (var entry in ChangeTracker.Entries<SpaceAsset>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Space assets cannot be physically deleted.");
            }
            if (entry.State is not (
                    EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            EnsureAssetOwner(entry.Entity.Scope, entry.Entity.OwnerTenantId);
            if (entry.State == EntityState.Modified &&
                (entry.Property(x => x.Scope).OriginalValue !=
                    entry.Entity.Scope ||
                 entry.Property(x => x.OwnerTenantId).OriginalValue !=
                    entry.Entity.OwnerTenantId))
            {
                throw new SpaceTenantScopeException(
                    "Space asset scope and owner cannot be reassigned.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<SpaceAssetVersion>())
        {
            if (entry.State is EntityState.Deleted or EntityState.Modified)
            {
                throw new InvalidOperationException(
                    "Space asset versions are immutable.");
            }
            if (entry.State == EntityState.Added)
            {
                EnsureAssetOwner(
                    entry.Entity.Scope,
                    entry.Entity.OwnerTenantId);
            }
        }

        foreach (var entry in ChangeTracker.Entries<SpaceElementRevision>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.EnsureAssetReferenceConsistency();
        }
    }

    private void EnsureAssetOwner(
        SpaceAssetScope scope,
        Guid ownerTenantId)
    {
        if (scope == SpaceAssetScope.System &&
            ownerTenantId == Guid.Empty)
        {
            return;
        }
        if (scope == SpaceAssetScope.Tenant &&
            ownerTenantId == CurrentTenantId)
        {
            return;
        }

        throw new SpaceTenantScopeException(
            "A cross-tenant Space asset write was rejected.");
    }

    private void StampAndValidateTenant()
    {
        if (CurrentTenantId == Guid.Empty)
            throw new SpaceTenantScopeException("A verified Space tenant context is required.");

        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");

        foreach (var entry in ChangeTracker.Entries<SpaceTenantEntity>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Space entities use explicit soft deletion.");

            if (entry.State == EntityState.Added)
            {
                EnsureCurrentTenant(entry, includeOriginal: false);
                entry.Property(nameof(SpaceTenantEntity.CreatedAtUtc)).CurrentValue = now;
                entry.Property(nameof(SpaceTenantEntity.CreatedBy)).CurrentValue =
                    _execution.ActorId == Guid.Empty ? null : _execution.ActorId;
                continue;
            }

            if (entry.State == EntityState.Modified)
            {
                EnsureCurrentTenant(entry, includeOriginal: true);
                entry.Property(nameof(SpaceTenantEntity.ModifiedAtUtc)).CurrentValue = now;
                entry.Property(nameof(SpaceTenantEntity.ModifiedBy)).CurrentValue =
                    _execution.ActorId == Guid.Empty ? null : _execution.ActorId;
            }
        }
    }

    private void EnsureCurrentTenant(
        EntityEntry<SpaceTenantEntity> entry,
        bool includeOriginal)
    {
        var tenant = entry.Property(nameof(SpaceTenantEntity.TenantId));
        if (tenant.CurrentValue is not Guid current || current != CurrentTenantId)
            throw new SpaceTenantScopeException("A cross-tenant Space write was rejected.");

        if (includeOriginal &&
            (tenant.OriginalValue is not Guid original || original != CurrentTenantId))
        {
            throw new SpaceTenantScopeException("Space TenantId cannot be reassigned.");
        }
    }
}
