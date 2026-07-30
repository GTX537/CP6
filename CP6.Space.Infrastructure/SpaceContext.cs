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
    public DbSet<SpaceFloorRevision> FloorRevisions => Set<SpaceFloorRevision>();
    public DbSet<SpaceZoneRevision> ZoneRevisions => Set<SpaceZoneRevision>();
    public DbSet<SpaceAisleRevision> AisleRevisions => Set<SpaceAisleRevision>();
    public DbSet<SpaceRackRevision> RackRevisions => Set<SpaceRackRevision>();
    public DbSet<SpaceRackLevelRevision> RackLevelRevisions =>
        Set<SpaceRackLevelRevision>();
    public DbSet<SpaceLocationRevision> LocationRevisions =>
        Set<SpaceLocationRevision>();
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
        ConfigureElementRevision(modelBuilder);
        ConfigureElementAttribute(modelBuilder);
        ConfigureFile(modelBuilder);
        ConfigureSource(modelBuilder);
        ConfigureJob(modelBuilder);
        ConfigureJobAttempt(modelBuilder);
        ConfigureJobStep(modelBuilder);
        ConfigureArtifact(modelBuilder);
        ConfigureIssue(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectPublishedHistory();
        ProtectPublishedSnapshotWrites();
        StampAndValidateTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ProtectPublishedHistory();
        ProtectPublishedSnapshotWrites();
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
                "[LevelNo] > 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)"));
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

    private void ConfigureElementRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceElementRevision>(
            modelBuilder,
            "Space_ElementRevision");
        entity.ToTable(
            "Space_ElementRevision",
            table => table.HasCheckConstraint(
                "CK_Space_ElementRevision_Geometry",
                "[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Height] >= 0 AND [Depth] >= 0"));
        entity.Property(x => x.ElementType).HasMaxLength(100).IsRequired();
        entity.Property(x => x.GeometryJson).HasColumnType("nvarchar(max)").IsRequired();
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
        entity.ToTable("Space_File");
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
