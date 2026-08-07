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
    public DbSet<SpaceValidationRun> ValidationRuns =>
        Set<SpaceValidationRun>();
    public DbSet<SpaceIdempotencyRecord> IdempotencyRecords =>
        Set<SpaceIdempotencyRecord>();
    public DbSet<SpaceGenerationRun> GenerationRuns =>
        Set<SpaceGenerationRun>();
    public DbSet<SpaceGenerationProposal> GenerationProposals =>
        Set<SpaceGenerationProposal>();
    public DbSet<SpaceProposalDecision> ProposalDecisions =>
        Set<SpaceProposalDecision>();
    public DbSet<SpaceGenerationLockedFact> GenerationLockedFacts =>
        Set<SpaceGenerationLockedFact>();
    public DbSet<SpaceGenerationStagingElement> GenerationStagingElements =>
        Set<SpaceGenerationStagingElement>();
    public DbSet<SpaceAiUsageRecord> AiUsageRecords =>
        Set<SpaceAiUsageRecord>();
    public DbSet<SpaceTenantAiWorkSlot> TenantAiWorkSlots =>
        Set<SpaceTenantAiWorkSlot>();
    public DbSet<SpaceAiBudgetReservation> AiBudgetReservations =>
        Set<SpaceAiBudgetReservation>();
    public DbSet<SpaceAiTenantPolicyConfiguration> AiTenantPolicies =>
        Set<SpaceAiTenantPolicyConfiguration>();
    public DbSet<SpaceFloorRevision> FloorRevisions => Set<SpaceFloorRevision>();
    public DbSet<SpaceUnderlayCalibration> UnderlayCalibrations =>
        Set<SpaceUnderlayCalibration>();
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
    public DbSet<SpaceElementCommandBatch> ElementCommandBatches =>
        Set<SpaceElementCommandBatch>();
    public DbSet<SpaceElementCommandRecord> ElementCommandRecords =>
        Set<SpaceElementCommandRecord>();
    public DbSet<SpaceWmsAdoption> WmsAdoptions =>
        Set<SpaceWmsAdoption>();
    public DbSet<SpacePersonnelEvent> PersonnelEvents =>
        Set<SpacePersonnelEvent>();
    public DbSet<SpacePersonnelCurrentState> PersonnelStates =>
        Set<SpacePersonnelCurrentState>();
    public DbSet<SpaceDeviceMapping> DeviceMappings =>
        Set<SpaceDeviceMapping>();
    public DbSet<SpaceDeviceEvent> DeviceEvents =>
        Set<SpaceDeviceEvent>();
    public DbSet<SpaceDeviceCurrentState> DeviceStates =>
        Set<SpaceDeviceCurrentState>();
    public DbSet<SpaceDeviceAlarmState> DeviceAlarmStates =>
        Set<SpaceDeviceAlarmState>();
    public DbSet<SpaceExternalOrganization> ExternalOrganizations =>
        Set<SpaceExternalOrganization>();
    public DbSet<SpaceExternalMembership> ExternalMemberships =>
        Set<SpaceExternalMembership>();
    public DbSet<SpaceExternalGrant> ExternalGrants =>
        Set<SpaceExternalGrant>();
    public DbSet<SpaceExternalGrantFloor> ExternalGrantFloors =>
        Set<SpaceExternalGrantFloor>();
    public DbSet<SpaceExternalGrantZone> ExternalGrantZones =>
        Set<SpaceExternalGrantZone>();
    public DbSet<SpaceExternalGrantOwner> ExternalGrantOwners =>
        Set<SpaceExternalGrantOwner>();
    public DbSet<SpaceExternalGrantObject> ExternalGrantObjects =>
        Set<SpaceExternalGrantObject>();
    public DbSet<SpaceFieldPolicy> FieldPolicies =>
        Set<SpaceFieldPolicy>();
    public DbSet<SpaceFieldPolicyField> FieldPolicyFields =>
        Set<SpaceFieldPolicyField>();
    public DbSet<SpaceExcelMappingProfile> ExcelMappingProfiles =>
        Set<SpaceExcelMappingProfile>();
    public DbSet<SpaceExcelMappingProfileVersion> ExcelMappingProfileVersions =>
        Set<SpaceExcelMappingProfileVersion>();
    public DbSet<SpacePutawayRecommendation> PutawayRecommendations =>
        Set<SpacePutawayRecommendation>();
    public DbSet<SpaceDispatchRecommendation> DispatchRecommendations =>
        Set<SpaceDispatchRecommendation>();
    public DbSet<SpacePlanningScenarioBranch> PlanningScenarioBranches =>
        Set<SpacePlanningScenarioBranch>();
    public DbSet<SpacePlanningHistoricalDataset> PlanningHistoricalDatasets =>
        Set<SpacePlanningHistoricalDataset>();
    public DbSet<SpacePlanningHistoricalTask> PlanningHistoricalTasks =>
        Set<SpacePlanningHistoricalTask>();
    public DbSet<SpacePlanningSimulationRun> PlanningSimulationRuns =>
        Set<SpacePlanningSimulationRun>();
    public DbSet<SpacePlanningSimulationLocationResult>
        PlanningSimulationLocationResults =>
            Set<SpacePlanningSimulationLocationResult>();
    public DbSet<SpacePlanningComparison> PlanningComparisons =>
        Set<SpacePlanningComparison>();
    public DbSet<SpacePlanningComparisonEntry> PlanningComparisonEntries =>
        Set<SpacePlanningComparisonEntry>();
    public DbSet<SpacePlanningComparisonRisk> PlanningComparisonRisks =>
        Set<SpacePlanningComparisonRisk>();
    public DbSet<SpacePlanningDecisionRecord> PlanningDecisionRecords =>
        Set<SpacePlanningDecisionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
        ConfigureVersion(modelBuilder);
        ConfigureFloorRevision(modelBuilder);
        ConfigureUnderlayCalibration(modelBuilder);
        ConfigureZoneRevision(modelBuilder);
        ConfigureAisleRevision(modelBuilder);
        ConfigureRackRevision(modelBuilder);
        ConfigureRackLevelRevision(modelBuilder);
        ConfigureLocationRevision(modelBuilder);
        ConfigureAsset(modelBuilder);
        ConfigureAssetVersion(modelBuilder);
        ConfigureElementRevision(modelBuilder);
        ConfigureElementAttribute(modelBuilder);
        ConfigureElementCommandBatch(modelBuilder);
        ConfigureElementCommandRecord(modelBuilder);
        ConfigureWmsAdoption(modelBuilder);
        ConfigurePersonnelEvent(modelBuilder);
        ConfigurePersonnelState(modelBuilder);
        ConfigureDeviceMapping(modelBuilder);
        ConfigureDeviceEvent(modelBuilder);
        ConfigureDeviceState(modelBuilder);
        ConfigureDeviceAlarmState(modelBuilder);
        ConfigureExternalOrganization(modelBuilder);
        ConfigureExternalMembership(modelBuilder);
        ConfigureExternalGrant(modelBuilder);
        ConfigureExternalGrantFloor(modelBuilder);
        ConfigureExternalGrantZone(modelBuilder);
        ConfigureExternalGrantOwner(modelBuilder);
        ConfigureExternalGrantObject(modelBuilder);
        ConfigureFieldPolicy(modelBuilder);
        ConfigureFieldPolicyField(modelBuilder);
        ConfigureExcelMappingProfile(modelBuilder);
        ConfigureExcelMappingProfileVersion(modelBuilder);
        ConfigurePutawayRecommendation(modelBuilder);
        ConfigureDispatchRecommendation(modelBuilder);
        ConfigurePlanningScenarioBranch(modelBuilder);
        ConfigurePlanningHistoricalDataset(modelBuilder);
        ConfigurePlanningHistoricalTask(modelBuilder);
        ConfigurePlanningSimulationRun(modelBuilder);
        ConfigurePlanningSimulationLocationResult(modelBuilder);
        ConfigurePlanningComparison(modelBuilder);
        ConfigurePlanningComparisonEntry(modelBuilder);
        ConfigurePlanningComparisonRisk(modelBuilder);
        ConfigurePlanningDecisionRecord(modelBuilder);
        ConfigureFile(modelBuilder);
        ConfigureSource(modelBuilder);
        ConfigureJob(modelBuilder);
        ConfigureJobAttempt(modelBuilder);
        ConfigureJobStep(modelBuilder);
        ConfigureArtifact(modelBuilder);
        ConfigureIssue(modelBuilder);
        ConfigureValidationRun(modelBuilder);
        ConfigureIdempotencyRecord(modelBuilder);
        ConfigureGenerationRun(modelBuilder);
        ConfigureGenerationProposal(modelBuilder);
        ConfigureProposalDecision(modelBuilder);
        ConfigureGenerationLockedFact(modelBuilder);
        ConfigureGenerationStagingElement(modelBuilder);
        ConfigureAiUsageRecord(modelBuilder);
        ConfigureTenantAiWorkSlot(modelBuilder);
        ConfigureAiBudgetReservation(modelBuilder);
        ConfigureAiTenantPolicy(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectPublishedHistory();
        ProtectPublishedSnapshotWrites();
        ProtectProposalDecisionHistory();
        ProtectGenerationLockedFactHistory();
        ProtectAiCapacityLedger();
        ProtectAiPolicyHistory();
        ProtectAssetLibrary();
        ProtectUnderlayCalibrationHistory();
        ProtectElementCommandHistory();
        ProtectExcelMappingVersionHistory();
        ProtectPersonnelEventHistory();
        ProtectDeviceEventHistory();
        ProtectPutawayRecommendationHistory();
        ProtectDispatchRecommendationHistory();
        ProtectPlanningScenarioHistory();
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
        ProtectGenerationLockedFactHistory();
        ProtectAiCapacityLedger();
        ProtectAiPolicyHistory();
        ProtectAssetLibrary();
        ProtectUnderlayCalibrationHistory();
        ProtectElementCommandHistory();
        ProtectExcelMappingVersionHistory();
        ProtectPersonnelEventHistory();
        ProtectDeviceEventHistory();
        ProtectPutawayRecommendationHistory();
        ProtectDispatchRecommendationHistory();
        ProtectPlanningScenarioHistory();
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
        entity.ToTable(
            "Space_ModelVersion",
            table => table.HasCheckConstraint(
                "CK_Space_ModelVersion_Purpose",
                "[Purpose] IN (0, 1) AND " +
                "([Purpose] = 0 OR (" +
                "[Status] NOT IN (3, 4, 5, 6) AND " +
                "[PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL))"));
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
        entity.Property(x => x.Purpose)
            .HasConversion<short>()
            .HasColumnType("smallint")
            .HasDefaultValue(SpaceModelVersionPurpose.Production);
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
        entity.HasOne<SpaceUnderlayCalibration>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
                x.UnderlaySourceId,
                x.UnderlayCalibrationId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
                x.SourceId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_FloorRevision_UnderlayCalibration_Tenant_Version_Floor_Source");
    }

    private void ConfigureUnderlayCalibration(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceUnderlayCalibration>();
        entity.ToTable("Space_UnderlayCalibration");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
                x.SourceId,
                x.Id,
            })
            .HasName(
                "AK_Space_UnderlayCalibration_Tenant_Version_Floor_Source_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Point1PixelX).HasColumnType("decimal(18,6)");
        entity.Property(x => x.Point1PixelY).HasColumnType("decimal(18,6)");
        entity.Property(x => x.Point2PixelX).HasColumnType("decimal(18,6)");
        entity.Property(x => x.Point2PixelY).HasColumnType("decimal(18,6)");
        entity.Property(x => x.ValidationPixelX).HasColumnType("decimal(18,6)");
        entity.Property(x => x.ValidationPixelY).HasColumnType("decimal(18,6)");
        entity.Property(x => x.MillimetersPerPixel)
            .HasColumnType("decimal(18,8)");
        entity.Property(x => x.RotationZ).HasColumnType("decimal(9,4)");
        entity.Property(x => x.ValidationErrorMillimeters)
            .HasColumnType("decimal(18,4)");
        entity.Property(x => x.ErrorThresholdMillimeters)
            .HasColumnType("decimal(18,4)");

        entity.HasIndex(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
                x.CreatedAtUtc,
            })
            .HasDatabaseName(
                "IX_Space_UnderlayCalibration_Version_Floor_Created");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.SourceId,
            })
            .HasDatabaseName(
                "IX_Space_UnderlayCalibration_Version_Source");
        entity.HasOne<SpaceModelSource>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.SourceId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_UnderlayCalibration_Source_Tenant_Version");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureZoneRevision(ModelBuilder modelBuilder)
    {
        var entity = ConfigureRevision<SpaceZoneRevision>(
            modelBuilder,
            "Space_ZoneRevision");
        entity.Property(x => x.ZoneCode).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
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
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
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
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.RackType).HasMaxLength(64);
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

    private void ConfigureWmsAdoption(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceWmsAdoption>();
        entity.ToTable("Space_WmsAdoption");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_WmsAdoption_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.AdapterId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.DataSource).HasMaxLength(100).IsRequired();
        entity.Property(x => x.DataSourceKind).HasMaxLength(20).IsRequired();
        entity.Property(x => x.ExternalLocationId).HasMaxLength(200);
        entity.Property(x => x.WmsLocationCode).HasMaxLength(200).IsRequired();
        entity.Property(x => x.ExternalVersion).HasMaxLength(100).IsRequired();
        entity.Property(x => x.WmsStateHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.LastObservedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.BoundLocationCode).HasMaxLength(200);
        entity.Property(x => x.BoundAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.AdapterId,
            x.WmsLogicalId,
        })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_WmsAdoption_Tenant_Site_Adapter_WmsLogical");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.AdapterId,
            x.ExternalLocationId,
        })
            .IsUnique()
            .HasFilter(
                "[ExternalLocationId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_WmsAdoption_Tenant_Site_Adapter_External");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.AdapterId,
            x.LocationLogicalId,
        })
            .IsUnique()
            .HasFilter(
                "[LocationLogicalId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_WmsAdoption_Tenant_Site_Adapter_Location");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.AdapterId,
            x.Status,
            x.WmsLocationCode,
        })
            .HasDatabaseName(
                "IX_Space_WmsAdoption_Tenant_Site_Adapter_Status_Code");

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_WmsAdoption_ModelVersion_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePersonnelEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePersonnelEvent>();
        entity.ToTable(
            "Space_PersonnelEvent",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_PersonnelEvent_SourceSequence",
                    "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                table.HasCheckConstraint(
                    "CK_Space_PersonnelEvent_Accuracy",
                    "[AccuracyMillimeters] IS NULL OR " +
                    "([AccuracyMillimeters] >= 0 AND " +
                    "[XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND " +
                    "[ZMillimeters] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Space_PersonnelEvent_SourceKind",
                    "[SourceKind] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Space_PersonnelEvent_Kind",
                    "[EventKind] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Space_PersonnelEvent_WorkState",
                    "[WorkState] IS NULL OR [WorkState] BETWEEN 0 AND 4");
                table.HasCheckConstraint(
                    "CK_Space_PersonnelEvent_Shape",
                    "([EventKind] = 0 AND [WorkState] IS NULL AND " +
                    "([LocationLogicalId] IS NOT NULL OR " +
                    "([FloorLogicalId] IS NOT NULL AND " +
                    "[XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND " +
                    "[ZMillimeters] IS NOT NULL))) OR " +
                    "([EventKind] = 1 AND [WorkState] IS NOT NULL AND " +
                    "[FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND " +
                    "[XMillimeters] IS NULL AND [YMillimeters] IS NULL AND " +
                    "[ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_PersonnelEvent_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SourceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.SourceEventId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.PersonExternalId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.EventKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.WorkState)
            .HasConversion<short?>()
            .HasColumnType("smallint");
        entity.Property(x => x.XMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.YMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.ZMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.AccuracyMillimeters)
            .HasColumnType("decimal(18,3)");
        entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.PayloadHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();

        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.SourceEventId,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PersonnelEvent_Tenant_Site_Source_Event");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.PersonExternalId,
            x.OccurredAtUtc,
        })
            .HasDatabaseName(
                "IX_Space_PersonnelEvent_Tenant_Site_Source_Person_Time");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePersonnelState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePersonnelCurrentState>();
        entity.ToTable(
            "Space_PersonnelState",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_PersonnelState_SourceKind",
                    "[SourceKind] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Space_PersonnelState_WorkState",
                    "[WorkState] BETWEEN 0 AND 4");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_PersonnelState_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SourceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.PersonExternalId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.XMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.YMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.ZMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.AccuracyMillimeters)
            .HasColumnType("decimal(18,3)");
        entity.Property(x => x.PositionOccurredAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.PositionReceivedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.PositionSourceEventId).HasMaxLength(200);
        entity.Property(x => x.WorkState)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.WorkStateOccurredAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.WorkStateReceivedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.WorkStateSourceEventId).HasMaxLength(200);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.PersonExternalId,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PersonnelState_Tenant_Site_Source_Person");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.WorkState,
            x.WorkStateOccurredAtUtc,
        })
            .HasDatabaseName(
                "IX_Space_PersonnelState_Tenant_Site_WorkState_Time");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureDeviceMapping(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceDeviceMapping>();
        entity.ToTable(
            "Space_DeviceMapping",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_DeviceMapping_SourceKind",
                    "[SourceKind] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Space_DeviceMapping_DeviceKind",
                    "[DeviceKind] BETWEEN 0 AND 7");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_DeviceMapping_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SourceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.DeviceExternalId)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.DeviceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ElementType).HasMaxLength(50).IsRequired();
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.DeviceExternalId,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_DeviceMapping_Tenant_Site_Source_Device");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.ElementLogicalId,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_DeviceMapping_Tenant_Site_Source_Element");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ValidatedModelVersionId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_DeviceMapping_ModelVersion_Tenant");
        entity.HasOne<SpaceElementRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ValidatedModelVersionId,
                x.ElementLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_DeviceMapping_Element_Tenant_Version_Logical");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureDeviceEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceDeviceEvent>();
        entity.ToTable(
            "Space_DeviceEvent",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_SourceKind",
                    "[SourceKind] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_DeviceKind",
                    "[DeviceKind] BETWEEN 0 AND 7");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_Kind",
                    "[EventKind] BETWEEN 0 AND 3");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_OperatingState",
                    "[OperatingState] IS NULL OR [OperatingState] BETWEEN 0 AND 6");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_AlarmSeverity",
                    "[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_SourceSequence",
                    "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_CoordinateTriple",
                    "([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR " +
                    "([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_Accuracy",
                    "[AccuracyMillimeters] IS NULL OR " +
                    "([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Space_DeviceEvent_Shape",
                    "([EventKind] = 0 AND [OperatingState] IS NULL AND " +
                    "[AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND " +
                    "[AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL AND " +
                    "([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL))) OR " +
                    "([EventKind] = 1 AND [OperatingState] IS NOT NULL AND " +
                    "[FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND " +
                    "[XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND " +
                    "[AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NULL AND " +
                    "[AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL) OR " +
                    "([EventKind] = 2 AND [OperatingState] IS NULL AND " +
                    "[FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND " +
                    "[XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND " +
                    "[AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND " +
                    "[AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL) OR " +
                    "([EventKind] = 3 AND [OperatingState] IS NULL AND " +
                    "[FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND " +
                    "[XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND " +
                    "[AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND " +
                    "[AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_DeviceEvent_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SourceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.SourceEventId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.DeviceExternalId)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.DeviceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.EventKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.OperatingState)
            .HasConversion<short?>()
            .HasColumnType("smallint");
        entity.Property(x => x.XMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.YMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.ZMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.AccuracyMillimeters)
            .HasColumnType("decimal(18,3)");
        entity.Property(x => x.AlarmExternalId).HasMaxLength(200);
        entity.Property(x => x.AlarmCode).HasMaxLength(100);
        entity.Property(x => x.AlarmSeverity)
            .HasConversion<short?>()
            .HasColumnType("smallint");
        entity.Property(x => x.AlarmMessage).HasMaxLength(500);
        entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.PayloadHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();

        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.SourceEventId,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_DeviceEvent_Tenant_Site_Source_Event");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.DeviceExternalId,
            x.OccurredAtUtc,
        })
            .HasDatabaseName(
                "IX_Space_DeviceEvent_Tenant_Site_Source_Device_Time");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.AlarmExternalId,
            x.OccurredAtUtc,
        })
            .HasFilter("[AlarmExternalId] IS NOT NULL")
            .HasDatabaseName(
                "IX_Space_DeviceEvent_Tenant_Site_Alarm_Time");
        entity.HasOne<SpaceDeviceMapping>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DeviceMappingId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_DeviceEvent_Mapping_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureDeviceState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceDeviceCurrentState>();
        entity.ToTable(
            "Space_DeviceState",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_DeviceState_SourceKind",
                    "[SourceKind] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Space_DeviceState_OperatingState",
                    "[OperatingState] BETWEEN 0 AND 6");
                table.HasCheckConstraint(
                    "CK_Space_DeviceState_CoordinateTriple",
                    "([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR " +
                    "([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Space_DeviceState_Accuracy",
                    "[AccuracyMillimeters] IS NULL OR " +
                    "([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_DeviceState_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SourceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.DeviceExternalId)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.XMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.YMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.ZMillimeters).HasColumnType("decimal(18,3)");
        entity.Property(x => x.AccuracyMillimeters)
            .HasColumnType("decimal(18,3)");
        entity.Property(x => x.PositionOccurredAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.PositionReceivedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.PositionSourceEventId).HasMaxLength(200);
        entity.Property(x => x.OperatingState)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.OperatingStateOccurredAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.OperatingStateReceivedAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.OperatingStateSourceEventId).HasMaxLength(200);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.DeviceExternalId,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_DeviceState_Tenant_Site_Source_Device");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.OperatingState,
            x.OperatingStateOccurredAtUtc,
        })
            .HasDatabaseName(
                "IX_Space_DeviceState_Tenant_Site_State_Time");
        entity.HasOne<SpaceDeviceMapping>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DeviceMappingId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_DeviceState_Mapping_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureDeviceAlarmState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceDeviceAlarmState>();
        entity.ToTable(
            "Space_DeviceAlarmState",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_DeviceAlarmState_SourceKind",
                    "[SourceKind] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Space_DeviceAlarmState_Severity",
                    "[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2");
                table.HasCheckConstraint(
                    "CK_Space_DeviceAlarmState_SourceSequence",
                    "[SourceSequence] IS NULL OR [SourceSequence] >= 0");
                table.HasCheckConstraint(
                    "CK_Space_DeviceAlarmState_ActiveShape",
                    "[IsActive] = 0 OR ([AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_DeviceAlarmState_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.SourceKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.DeviceExternalId)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.AlarmExternalId)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.AlarmCode).HasMaxLength(100);
        entity.Property(x => x.AlarmSeverity)
            .HasConversion<short?>()
            .HasColumnType("smallint");
        entity.Property(x => x.AlarmMessage).HasMaxLength(500);
        entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.SourceEventId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.SourceId,
            x.DeviceExternalId,
            x.AlarmExternalId,
        })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_DeviceAlarmState_Tenant_Site_Source_Device_Alarm");
        entity.HasIndex(x => new
        {
            x.TenantId,
            x.SiteId,
            x.IsActive,
            x.AlarmSeverity,
            x.OccurredAtUtc,
        })
            .HasDatabaseName(
                "IX_Space_DeviceAlarmState_Tenant_Site_Active_Severity_Time");
        entity.HasOne<SpaceDeviceMapping>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DeviceMappingId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_DeviceAlarmState_Mapping_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
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

    private void ConfigureElementCommandBatch(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceElementCommandBatch>();
        entity.ToTable(
            "Space_ElementCommandBatch",
            table => table.HasCheckConstraint(
                "CK_Space_ElementCommandBatch_Result",
                "([ResultFloorRevision] IS NULL AND [ResultVersionContentRevision] IS NULL AND [ResponseJson] IS NULL) OR " +
                "([ResultFloorRevision] IS NOT NULL AND [ResultVersionContentRevision] IS NOT NULL AND [ResponseJson] IS NOT NULL)"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ElementCommandBatch_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.Property(x => x.RequestHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.AppliedAtUtc).HasColumnType("datetime2");

        entity.HasIndex(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.FloorLogicalId,
                x.AppliedAtUtc,
            })
            .HasDatabaseName("IX_Space_ElementCommandBatch_Floor_Applied");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ElementCommandBatch_Version_Tenant");
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
            .HasConstraintName(
                "FK_Space_ElementCommandBatch_Floor_Tenant_Version_Logical");

        entity.HasQueryFilter(x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureElementCommandRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceElementCommandRecord>();
        entity.ToTable("Space_ElementCommandRecord");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.Property(x => x.CommandType).HasMaxLength(100).IsRequired();
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.AfterJson).HasColumnType("nvarchar(max)").IsRequired();

        entity.HasIndex(x => new
            {
                x.TenantId,
                x.CommandBatchId,
                x.SequenceNo,
            })
            .IsUnique()
            .HasDatabaseName("UX_Space_ElementCommandRecord_Batch_Sequence");
        entity.HasOne<SpaceElementCommandBatch>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CommandBatchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ElementCommandRecord_Batch_Tenant");

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

    private void ConfigureValidationRun(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceValidationRun>();
        entity.ToTable(
            "Space_ValidationRun",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_ValidationRun_StatusTime",
                    "([Status] = 0 AND [StartedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR " +
                    "([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR " +
                    "([Status] IN (2, 3, 4) AND [FinishedAtUtc] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Space_ValidationRun_Counts",
                    "[BlockingCount] >= 0 AND [WarningCount] >= 0 AND [InfoCount] >= 0 AND " +
                    "([Status] <> 2 OR [BlockingCount] = 0) AND " +
                    "([Status] <> 3 OR [BlockingCount] > 0)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ValidationRun_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.ContentHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.RuleSetVersion).HasMaxLength(50).IsRequired();
        entity.Property(x => x.AdapterId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.CapabilityHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.RequestedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.StartedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.FinishedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.FailureCode).HasMaxLength(100);
        entity.Property(x => x.FailureSummary).HasMaxLength(1000);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.ContentHash,
                    x.RuleSetVersion,
                    x.AdapterId,
                    x.CapabilityHash,
                })
            .IsUnique()
            .HasFilter("[Status] <> 4 AND [IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_ValidationRun_Tenant_Input_ActiveOrReusable");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.RequestedAtUtc,
                    x.Id,
                })
            .HasDatabaseName(
                "IX_Space_ValidationRun_Tenant_Version_Requested");
        entity.HasIndex(x => new { x.TenantId, x.JobId })
            .IsUnique()
            .HasDatabaseName("UX_Space_ValidationRun_Tenant_Job");

        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ValidationRun_Version_Tenant");
        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.JobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ValidationRun_Job_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
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
                table.HasCheckConstraint(
                    "CK_Space_ModelIssue_GenerationScope",
                    "([GenerationProposalId] IS NULL OR [GenerationRunId] IS NOT NULL) AND " +
                    "([ResolutionDecisionId] IS NULL OR [GenerationProposalId] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Space_ModelIssue_Resolution",
                    "([Status] <> 1 AND [ResolutionKind] = 0 AND " +
                    "[ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NULL) OR " +
                    "([Status] = 1 AND (([ResolutionKind] = 1 AND " +
                    "[ResolutionCommandBatchId] IS NOT NULL AND [ResolutionDecisionId] IS NULL) OR " +
                    "([ResolutionKind] IN (2, 3) AND [ResolutionCommandBatchId] IS NULL AND " +
                    "[ResolutionDecisionId] IS NOT NULL)))");
                table.HasCheckConstraint(
                    "CK_Space_ModelIssue_ValidationScope",
                    "[ValidationRunId] IS NULL OR " +
                    "([ModelVersionId] IS NOT NULL AND [JobId] IS NOT NULL)");
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
        entity.Property(x => x.Category).HasMaxLength(50);
        entity.Property(x => x.FieldPath).HasMaxLength(500);
        entity.Property(x => x.EvidenceJson)
            .HasColumnType("nvarchar(max)")
            .HasDefaultValue("{}")
            .IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ResolutionKind)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.AcknowledgementReason).HasMaxLength(1000);
        entity.Property(x => x.PayloadPurgedAtUtc)
            .HasColumnType("datetime2");

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
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ValidationRunId,
                    x.Severity,
                    x.Code,
                    x.Id,
                })
            .HasFilter(
                "[ValidationRunId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName(
                "IX_Space_ModelIssue_Tenant_Validation_Severity_Code");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.GenerationRunId,
                    x.GenerationProposalId,
                    x.Status,
                    x.Severity,
                })
            .HasFilter("[GenerationRunId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName(
                "IX_Space_ModelIssue_Tenant_Run_Proposal_Status");
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.PayloadPurgedAtUtc,
                    x.GenerationRunId,
                    x.Id,
                })
            .HasFilter("[GenerationRunId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName(
                "IX_ModelIssue_Tenant_Purge_Run");

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
        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.GenerationRunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ModelIssue_GenerationRun_Tenant");
        entity.HasOne<SpaceValidationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ValidationRunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ModelIssue_ValidationRun_Tenant");
        entity.HasOne<SpaceGenerationProposal>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.TenantId,
                    x.GenerationRunId,
                    x.GenerationProposalId,
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
                "FK_Space_ModelIssue_Proposal_Tenant_Run");
        entity.HasOne<SpaceProposalDecision>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ResolutionDecisionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ModelIssue_ResolutionDecision_Tenant");

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
        entity.Property(x => x.ApplyReviewEtag)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.ApplyExpectedRunRowVersion)
            .HasMaxLength(128);
        entity.Property(x => x.ApplyPlanHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.ApplyPreparedAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.AppliedCountsJson)
            .HasColumnType("nvarchar(max)");
        entity.Property(x => x.RetentionHoldUntilUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.PayloadPurgedAtUtc)
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
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.PayloadPurgedAtUtc,
                    x.IsCurrent,
                    x.Status,
                    x.CreatedAtUtc,
                    x.Id,
                })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "IX_GenerationRun_Tenant_Retention");

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
        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ApplyJobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationRun_ApplyJob_Tenant");
        entity.HasOne<SpaceFloorRevision>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    LogicalId = x.TargetFloorLogicalId,
                })
            .HasPrincipalKey(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.LogicalId,
                })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationRun_TargetFloor_Tenant_Version");
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
        entity.Property(x => x.PayloadPurgedAtUtc)
            .HasColumnType("datetime2");
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
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.PayloadPurgedAtUtc,
                    x.RunId,
                    x.Id,
                })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "IX_Proposal_Tenant_Purge_Run");

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

    private void ConfigureGenerationLockedFact(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceGenerationLockedFact>();
        entity.ToTable(
            "Space_GenerationLockedFact",
            table => table.HasCheckConstraint(
                "CK_Space_GenerationLockedFact_Match",
                "[MatchScore] >= 0 AND [MatchScore] <= 1 AND [RunId] <> [BasedOnRunId]"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_GenerationLockedFact_TenantId_Id");
        ConfigureTenantEntity(entity);

        ConfigureHash(entity.Property(x => x.SourceHash));
        entity.Property(x => x.SourceKey).HasMaxLength(256).IsRequired();
        entity.Property(x => x.ProposalType).HasMaxLength(64).IsRequired();
        entity.Property(x => x.FieldPath).HasMaxLength(256).IsRequired();
        ConfigureJson(entity.Property(x => x.ValueJson));
        entity.Property(x => x.MatchMethod)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.MatchScore).HasColumnType("decimal(6,5)");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.RunId,
                    x.SourceKey,
                    x.ProposalType,
                    x.FieldPath,
                })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_GenerationLockedFact_Tenant_Run_Source_Type_Field");
        entity.HasIndex(
                x => new { x.TenantId, x.SourceDecisionId, x.RunId })
            .HasDatabaseName(
                "IX_GenerationLockedFact_Tenant_Decision_Run");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationLockedFact_Run_Tenant");
        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BasedOnRunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationLockedFact_BasedOnRun_Tenant");
        entity.HasOne<SpaceGenerationProposal>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.TenantId,
                    x.BasedOnRunId,
                    x.SourceProposalId,
                })
            .HasPrincipalKey(
                x => new { x.TenantId, x.RunId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationLockedFact_Proposal_Tenant_Run");
        entity.HasOne<SpaceProposalDecision>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SourceDecisionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationLockedFact_Decision_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureGenerationStagingElement(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceGenerationStagingElement>();
        entity.ToTable(
            "Space_GenerationStagingElement",
            table => table.HasCheckConstraint(
                "CK_Space_GenerationStagingElement_Validation",
                "([ValidationStatus] = 0 AND [ValidationHash] IS NULL) OR " +
                "([ValidationStatus] = 1 AND [ValidationHash] IS NOT NULL)"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName(
                "AK_Space_GenerationStagingElement_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.ElementType)
            .HasMaxLength(64)
            .IsRequired();
        ConfigureJson(entity.Property(x => x.NormalizedPayloadJson));
        entity.Property(x => x.ValidationStatus)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ValidationHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.RunId, x.ProposalId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_GenerationStaging_Tenant_Run_Proposal");
        entity.HasIndex(x => new { x.TenantId, x.RunId, x.SequenceNo })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_GenerationStaging_Tenant_Run_Sequence");
        entity.HasIndex(x => new { x.TenantId, x.RunId, x.LogicalId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_GenerationStaging_Tenant_Run_Logical");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationStaging_Run_Tenant");
        entity.HasOne<SpaceGenerationProposal>()
            .WithMany()
            .HasForeignKey(
                x => new { x.TenantId, x.RunId, x.ProposalId })
            .HasPrincipalKey(
                x => new { x.TenantId, x.RunId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationStaging_Proposal_Tenant_Run");
        entity.HasOne<SpaceFloorRevision>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    LogicalId = x.FloorLogicalId,
                })
            .HasPrincipalKey(
                x => new
                {
                    x.TenantId,
                    x.ModelVersionId,
                    x.LogicalId,
                })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_GenerationStaging_Floor_Tenant_Version");
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
        entity.Property(x => x.ArchivedAtUtc)
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
        entity.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.ArchivedAtUtc,
                    x.RecordedAtUtc,
                    x.Id,
                })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "IX_AiUsage_Tenant_Retention");

        entity.HasOne<SpaceGenerationRun>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_AiUsageRecord_Run_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId &&
                 !x.IsDeleted &&
                 !x.ArchivedAtUtc.HasValue);
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

    private void ConfigureAiTenantPolicy(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceAiTenantPolicyConfiguration>();
        entity.ToTable(
            "Space_AiTenantPolicy",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_AiTenantPolicy_Version",
                    "[Version] >= 1");
                table.HasCheckConstraint(
                    "CK_Space_AiTenantPolicy_Concurrency",
                    "[MaxConcurrentRuns] >= 1 AND [MaxConcurrentRuns] <= 3");
                table.HasCheckConstraint(
                    "CK_Space_AiTenantPolicy_Budget",
                    "([DailyBudgetMinor] IS NULL OR [DailyBudgetMinor] >= 0) AND " +
                    "([MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= 0) AND " +
                    "([DailyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] IS NULL OR " +
                    "[MonthlyBudgetMinor] >= [DailyBudgetMinor])");
                table.HasCheckConstraint(
                    "CK_Space_AiTenantPolicy_Currency",
                    "([DailyBudgetMinor] IS NULL AND [MonthlyBudgetMinor] IS NULL) OR " +
                    "[Currency] IS NOT NULL");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_AiTenantPolicy_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.DataPolicy)
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        ConfigureJson(entity.Property(x => x.AllowedSiteIdsJson));
        ConfigureJson(entity.Property(x => x.AllowedProviderAliasesJson));
        entity.Property(x => x.Currency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(3);
        entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new { x.TenantId, x.Version })
            .IsUnique()
            .HasDatabaseName("UX_AiTenantPolicy_Tenant_Version");
        entity.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_AiTenantPolicy_Tenant_Active");
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

    private void ConfigureExternalOrganization(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExternalOrganization>();
        entity.ToTable(
            "Space_ExternalOrganization",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_ExternalOrganization_BusinessPartner",
                    "([BusinessPartnerType] IS NULL AND [BusinessPartnerId] IS NULL) OR " +
                    "([BusinessPartnerType] IS NOT NULL AND [BusinessPartnerId] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Space_ExternalOrganization_Type",
                    "[Type] >= 0 AND [Type] <= 2");
                table.HasCheckConstraint(
                    "CK_Space_ExternalOrganization_Status",
                    "[Status] >= 0 AND [Status] <= 2");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExternalOrganization_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Type)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.BusinessPartnerType)
            .HasMaxLength(50)
            .IsUnicode(false);
        entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
        entity.Property(x => x.NormalizedCode)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.SecurityStamp)
            .HasColumnType("bigint");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
            {
                x.TenantId,
                x.Type,
                x.NormalizedCode,
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_ExternalOrganization_Tenant_Type_Code");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.Type,
                x.BusinessPartnerType,
                x.BusinessPartnerId,
            })
            .IsUnique()
            .HasFilter(
                "[BusinessPartnerId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_ExternalOrganization_Tenant_Type_Partner");
        entity.HasIndex(x => new { x.TenantId, x.Status, x.Name })
            .HasDatabaseName(
                "IX_Space_ExternalOrganization_Tenant_Status_Name");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExternalMembership(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExternalMembership>();
        entity.ToTable(
            "Space_ExternalMembership",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_ExternalMembership_Validity",
                    "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                table.HasCheckConstraint(
                    "CK_Space_ExternalMembership_Role",
                    "[Role] >= 0 AND [Role] <= 2");
                table.HasCheckConstraint(
                    "CK_Space_ExternalMembership_Status",
                    "[Status] >= 0 AND [Status] <= 3");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExternalMembership_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Role)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ValidFromUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.ValidToUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.AcceptedAtUtc)
            .HasColumnType("datetime2");
        entity.Property(x => x.SecurityStamp)
            .HasColumnType("bigint");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasIndex(x => new
            {
                x.TenantId,
                x.OrganizationId,
                x.UserId,
            })
            .IsUnique()
            .HasFilter("[Status] <> 3 AND [IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_ExternalMembership_Tenant_Organization_User_Current");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.UserId,
                x.Status,
                x.ValidFromUtc,
                x.ValidToUtc,
            })
            .HasDatabaseName(
                "IX_Space_ExternalMembership_Tenant_User_Status_Validity");

        entity.HasOne<SpaceExternalOrganization>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OrganizationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ExternalMembership_Organization_Tenant");

        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExternalGrant(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExternalGrant>();
        entity.ToTable(
            "Space_ExternalGrant",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_ExternalGrant_Status",
                    "[Status] >= 0 AND [Status] <= 2");
                table.HasCheckConstraint(
                    "CK_Space_ExternalGrant_Validity",
                    "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                table.HasCheckConstraint(
                    "CK_Space_ExternalGrant_Version",
                    "[GrantVersion] > 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExternalGrant_TenantId_Id");
        ConfigureTenantEntity(entity);

        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.ValidFromUtc).HasColumnType("datetime2");
        entity.Property(x => x.ValidToUtc).HasColumnType("datetime2");
        entity.Property(x => x.GrantVersion).HasColumnType("bigint");
        entity.Property(x => x.RowVersion).IsRowVersion();

        entity.HasOne<SpaceExternalOrganization>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OrganizationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ExternalGrant_Organization_Tenant");
        entity.HasOne<SpaceFieldPolicy>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FieldPolicyId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ExternalGrant_FieldPolicy_Tenant");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.OrganizationId,
                x.Status,
                x.ValidFromUtc,
                x.ValidToUtc,
            })
            .HasDatabaseName(
                "IX_Space_ExternalGrant_Organization_Status_Validity");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.OrganizationId,
                x.SiteId,
                x.Status,
            })
            .HasDatabaseName(
                "IX_Space_ExternalGrant_Organization_Site_Status");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExternalGrantFloor(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExternalGrantFloor>();
        entity.ToTable("Space_ExternalGrantFloor");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExternalGrantFloor_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.HasOne<SpaceExternalGrant>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.GrantId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ExternalGrantFloor_Grant_Tenant");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.GrantId,
                x.FloorLogicalId,
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ExternalGrantFloor_Current");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExternalGrantZone(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExternalGrantZone>();
        entity.ToTable("Space_ExternalGrantZone");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExternalGrantZone_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.HasOne<SpaceExternalGrant>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.GrantId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ExternalGrantZone_Grant_Tenant");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.GrantId,
                x.ZoneLogicalId,
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ExternalGrantZone_Current");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExternalGrantOwner(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExternalGrantOwner>();
        entity.ToTable("Space_ExternalGrantOwner");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExternalGrantOwner_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.Property(x => x.OwnerId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.NormalizedOwnerId)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.HasOne<SpaceExternalGrant>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.GrantId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ExternalGrantOwner_Grant_Tenant");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.GrantId,
                x.NormalizedOwnerId,
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ExternalGrantOwner_Current");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExternalGrantObject(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExternalGrantObject>();
        entity.ToTable("Space_ExternalGrantObject");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExternalGrantObject_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.Property(x => x.BusinessObjectType)
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(x => x.NormalizedBusinessObjectType)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.BusinessObjectId)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.NormalizedBusinessObjectId)
            .HasMaxLength(200)
            .IsUnicode(false)
            .IsRequired();
        entity.HasOne<SpaceExternalGrant>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.GrantId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_ExternalGrantObject_Grant_Tenant");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.GrantId,
                x.NormalizedBusinessObjectType,
                x.NormalizedBusinessObjectId,
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ExternalGrantObject_Current");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureFieldPolicy(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceFieldPolicy>();
        entity.ToTable(
            "Space_FieldPolicy",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_FieldPolicy_AudienceType",
                    "[AudienceType] >= 0 AND [AudienceType] <= 2");
                table.HasCheckConstraint(
                    "CK_Space_FieldPolicy_Status",
                    "[Status] >= 0 AND [Status] <= 1");
                table.HasCheckConstraint(
                    "CK_Space_FieldPolicy_Version",
                    "[PolicyVersion] > 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_FieldPolicy_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.NormalizedName)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.AudienceType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.PolicyVersion).HasColumnType("bigint");
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.AudienceType,
                x.NormalizedName,
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_FieldPolicy_CurrentName");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureFieldPolicyField(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceFieldPolicyField>();
        entity.ToTable(
            "Space_FieldPolicyField",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_FieldPolicyField_ResourceType",
                    "[ResourceType] >= 0 AND [ResourceType] <= 2");
                table.HasCheckConstraint(
                    "CK_Space_FieldPolicyField_MaskingRule",
                    "[MaskingRule] >= 0 AND [MaskingRule] <= 3");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_FieldPolicyField_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.Property(x => x.ResourceType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.FieldName).HasMaxLength(100).IsRequired();
        entity.Property(x => x.NormalizedFieldName)
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(x => x.MaskingRule)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.HasOne<SpaceFieldPolicy>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PolicyId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_FieldPolicyField_Policy_Tenant");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.PolicyId,
                x.ResourceType,
                x.NormalizedFieldName,
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_FieldPolicyField_Current");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExcelMappingProfile(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExcelMappingProfile>();
        entity.ToTable(
            "Space_ExcelMappingProfile",
            table => table.HasCheckConstraint(
                "CK_Space_ExcelMappingProfile_CurrentVersion",
                "[CurrentVersion] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExcelMappingProfile_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.NormalizedName)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.TenantId, x.NormalizedName })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Space_ExcelMappingProfile_CurrentName");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureExcelMappingProfileVersion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceExcelMappingProfileVersion>();
        entity.ToTable(
            "Space_ExcelMappingProfileVersion",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_ExcelMappingProfileVersion_Version",
                    "[Version] > 0");
                table.HasCheckConstraint(
                    "CK_Space_ExcelMappingProfileVersion_Base",
                    "([BasedOnProfileId] IS NULL AND [BasedOnVersion] IS NULL) OR " +
                    "([BasedOnProfileId] IS NOT NULL AND [BasedOnVersion] > 0)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_ExcelMappingProfileVersion_TenantId_Id");
        ConfigureTenantEntity(entity);
        entity.Property(x => x.DefinitionJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.DefinitionHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.HasIndex(x => new { x.TenantId, x.ProfileId, x.Version })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName(
                "UX_Space_ExcelMappingProfileVersion_Profile_Version");
        entity.HasIndex(x => new { x.TenantId, x.DefinitionHash })
            .HasDatabaseName(
                "IX_Space_ExcelMappingProfileVersion_DefinitionHash");
        entity.HasOne<SpaceExcelMappingProfile>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProfileId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_ExcelMappingProfileVersion_Profile_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePutawayRecommendation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePutawayRecommendation>();
        entity.ToTable(
            "Space_PutawayRecommendation",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_PutawayRecommendation_Counts",
                    "[ExaminedLocationCount] >= 0 AND " +
                    "[EligibleCandidateCount] >= 0 AND " +
                    "[ReturnedCandidateCount] >= 0 AND " +
                    "[EligibleCandidateCount] <= [ExaminedLocationCount] AND " +
                    "[ReturnedCandidateCount] <= [EligibleCandidateCount] AND " +
                    "(([IsTruncated] = 1 AND " +
                    "[ReturnedCandidateCount] < [EligibleCandidateCount]) OR " +
                    "([IsTruncated] = 0 AND " +
                    "[ReturnedCandidateCount] = [EligibleCandidateCount]))");
                table.HasCheckConstraint(
                    "CK_Space_PutawayRecommendation_Evidence",
                    "[Outcome] IN ('NoCandidate', 'CandidatesGenerated') AND " +
                    "ISJSON([RequestJson]) = 1 AND " +
                    "ISJSON([SourcesJson]) = 1 AND " +
                    "ISJSON([ExclusionsJson]) = 1 AND " +
                    "ISJSON([ExclusionSamplesJson]) = 1 AND " +
                    "ISJSON([CandidatesJson]) = 1 AND " +
                    "ISJSON([LimitationsJson]) = 1");
                table.HasCheckConstraint(
                    "CK_Space_PutawayRecommendation_Immutable",
                    "LEN([RequestHash]) = 64 AND " +
                    "[RequestHash] NOT LIKE '%[^0-9a-f]%' AND " +
                    "[IsDeleted] = 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_PutawayRecommendation_Tenant_Id");
        entity.Property(x => x.WarehouseCode)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.GeneratedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.DefinitionVersion)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.Outcome)
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.RequestJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.SourcesJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.ExclusionsJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.ExclusionSamplesJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.CandidatesJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.LimitationsJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.RequestHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.SiteId,
                x.GeneratedAtUtc,
                x.Id,
            })
            .HasDatabaseName(
                "IX_Space_PutawayRecommendation_Tenant_Site_Generated");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PublishedVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PutawayRecommendation_Version_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigureDispatchRecommendation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpaceDispatchRecommendation>();
        entity.ToTable(
            "Space_DispatchRecommendation",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Space_DispatchRecommendation_Counts",
                    "[ExaminedTaskCount] >= 0 AND " +
                    "[EligibleTaskCount] >= 0 AND " +
                    "[ExaminedPersonCount] >= 0 AND " +
                    "[EligiblePersonCount] >= 0 AND " +
                    "[EligiblePairCount] >= 0 AND " +
                    "[MatchableAssignmentCount] >= 0 AND " +
                    "[ReturnedAssignmentCount] >= 0 AND " +
                    "[EligibleTaskCount] <= [ExaminedTaskCount] AND " +
                    "[EligiblePersonCount] <= [ExaminedPersonCount] AND " +
                    "[MatchableAssignmentCount] <= [EligibleTaskCount] AND " +
                    "[MatchableAssignmentCount] <= [EligiblePersonCount] AND " +
                    "[MatchableAssignmentCount] <= [EligiblePairCount] AND " +
                    "[ReturnedAssignmentCount] <= [MatchableAssignmentCount] AND " +
                    "(([IsTruncated] = 1 AND " +
                    "[ReturnedAssignmentCount] < [MatchableAssignmentCount]) OR " +
                    "([IsTruncated] = 0 AND " +
                    "[ReturnedAssignmentCount] = [MatchableAssignmentCount]))");
                table.HasCheckConstraint(
                    "CK_Space_DispatchRecommendation_Evidence",
                    "[Outcome] IN ('NoAssignment', 'AssignmentsGenerated') AND " +
                    "ISJSON([RequestJson]) = 1 AND " +
                    "ISJSON([SourcesJson]) = 1 AND " +
                    "ISJSON([ExclusionsJson]) = 1 AND " +
                    "ISJSON([ExclusionSamplesJson]) = 1 AND " +
                    "ISJSON([AssignmentsJson]) = 1 AND " +
                    "ISJSON([LimitationsJson]) = 1");
                table.HasCheckConstraint(
                    "CK_Space_DispatchRecommendation_Immutable",
                    "LEN([RequestHash]) = 64 AND " +
                    "[RequestHash] NOT LIKE '%[^0-9a-f]%' AND " +
                    "[IsDeleted] = 0");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_DispatchRecommendation_Tenant_Id");
        entity.Property(x => x.WarehouseCode)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.GeneratedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.DefinitionVersion)
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.Outcome)
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.RequestJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.SourcesJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.ExclusionsJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.ExclusionSamplesJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.AssignmentsJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.LimitationsJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(x => x.RequestHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.SiteId,
                x.GeneratedAtUtc,
                x.Id,
            })
            .HasDatabaseName(
                "IX_Space_DispatchRecommendation_Tenant_Site_Generated");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PublishedVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_DispatchRecommendation_Version_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningScenarioBranch(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningScenarioBranch>();
        entity.ToTable(
            "Space_PlanningScenarioBranch",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningScenarioBranch_Immutable",
                "[BasePublishedVersionId] <> [ScenarioVersionId] AND " +
                "LEN([RequestHash]) = 64 AND " +
                "[RequestHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "[IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_PlanningScenarioBranch_Tenant_Id");
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.DefinitionVersion)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.RequestHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.HasIndex(x => new { x.TenantId, x.SiteId, x.CreatedAtUtc })
            .HasDatabaseName(
                "IX_Space_PlanningScenarioBranch_Site_Created");
        entity.HasIndex(x => new { x.TenantId, x.ScenarioVersionId })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningScenarioBranch_ScenarioVersion");
        entity.HasIndex(x => new { x.TenantId, x.CloneJobId })
            .IsUnique()
            .HasDatabaseName("UX_Space_PlanningScenarioBranch_CloneJob");
        entity.HasOne<SpaceModel>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningScenarioBranch_Model_Tenant");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.BasePublishedVersionId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningScenarioBranch_BaseVersion_Tenant");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.ScenarioVersionId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.ModelId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningScenarioBranch_ScenarioVersion_Tenant");
        entity.HasOne<SpaceJob>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CloneJobId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningScenarioBranch_CloneJob_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningHistoricalDataset(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningHistoricalDataset>();
        entity.ToTable(
            "Space_PlanningHistoricalDataset",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningHistoricalDataset_Invariants",
                "[HistoricalFromUtc] < [HistoricalToUtc] AND " +
                "[ReplaySpeedFactor] > 0 AND " +
                "[ReplaySpeedFactor] <= 1000 AND " +
                "[TaskCount] BETWEEN 1 AND 10000 AND " +
                "LEN([SourceDatasetHash]) = 64 AND " +
                "[SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([RequestHash]) = 64 AND " +
                "[RequestHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "[IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_PlanningHistoricalDataset_Tenant_Id");
        entity.HasAlternateKey(x => new
            {
                x.TenantId,
                x.Id,
                x.BranchId,
                x.ModelId,
                x.ScenarioVersionId,
            })
            .HasName(
                "AK_Space_PlanningHistoricalDataset_Tenant_Id_Branch_Model_Version");
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.HistoricalFromUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.HistoricalToUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.ReplayStartUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.ReplaySpeedFactor).HasPrecision(9, 4);
        entity.Property(x => x.SourceDatasetHash)
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
        entity.Property(x => x.DefinitionVersion)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.DeidentificationVersion)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.BranchId,
                x.CreatedAtUtc,
            })
            .HasDatabaseName(
                "IX_Space_PlanningHistoricalDataset_Branch_Created");
        entity.HasOne<SpacePlanningScenarioBranch>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningHistoricalDataset_Branch_Tenant");
        entity.HasOne<SpaceModel>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningHistoricalDataset_Model_Tenant");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.ScenarioVersionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningHistoricalDataset_ScenarioVersion_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningHistoricalTask(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningHistoricalTask>();
        entity.ToTable(
            "Space_PlanningHistoricalTask",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningHistoricalTask_Invariants",
                "[SequenceNo] > 0 AND [Quantity] > 0 AND " +
                "[OriginalCreatedAtUtc] <= [OriginalCompletedAtUtc] AND " +
                "[ReplayCreatedAtUtc] <= [ReplayCompletedAtUtc] AND " +
                "[ToLocationLogicalId] <> " +
                "'00000000-0000-0000-0000-000000000000' AND " +
                "([FromLocationLogicalId] IS NULL OR " +
                "[FromLocationLogicalId] <> " +
                "'00000000-0000-0000-0000-000000000000') AND " +
                "LEN([TaskToken]) = 64 AND " +
                "[TaskToken] NOT LIKE '%[^0-9a-f]%' AND " +
                "([WorkerToken] IS NULL OR (LEN([WorkerToken]) = 64 AND " +
                "[WorkerToken] NOT LIKE '%[^0-9a-f]%')) AND " +
                "[TaskType] BETWEEN 0 AND 4 AND " +
                "[Outcome] BETWEEN 0 AND 2 AND [IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.Property(x => x.TaskToken)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.WorkerToken)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64);
        entity.Property(x => x.TaskType)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.Outcome)
            .HasConversion<short>()
            .HasColumnType("smallint");
        entity.Property(x => x.OriginalCreatedAtUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.OriginalCompletedAtUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.ReplayCreatedAtUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.ReplayCompletedAtUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.Quantity).HasPrecision(18, 4);
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.DatasetId,
                x.SequenceNo,
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningHistoricalTask_Dataset_Sequence");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.DatasetId,
                x.TaskToken,
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningHistoricalTask_Dataset_Token");
        entity.HasOne<SpacePlanningHistoricalDataset>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DatasetId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningHistoricalTask_Dataset_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningSimulationRun(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningSimulationRun>();
        entity.ToTable(
            "Space_PlanningSimulationRun",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningSimulationRun_Invariants",
                "[ScenarioContentRevision] >= 0 AND " +
                "[DefaultQuantityCapacity] > 0 AND " +
                "[DefaultConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND " +
                "[LocationCapacityOverrideCount] BETWEEN 0 AND 10000 AND " +
                "[ThroughputWindowMinutes] BETWEEN 1 AND 1440 AND " +
                "[DistanceCostPerMeter] >= 0 AND [LaborCostPerHour] >= 0 AND " +
                "[CongestionCostPerTaskHour] >= 0 AND " +
                "[TaskCount] BETWEEN 1 AND 10000 AND " +
                "[CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND " +
                "[CompletedQuantity] >= 0 AND " +
                "[DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND " +
                "[TotalDistanceMeters] >= 0 AND " +
                "[DistanceCoveragePercent] BETWEEN 0 AND 100 AND " +
                "[PeakConcurrentTasks] >= 0 AND " +
                "[CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND " +
                "[OverloadedLocationCount] >= 0 AND " +
                "[PeakCapacityUtilizationPercent] >= 0 AND " +
                "[AverageCompletedTasksPerHour] >= 0 AND " +
                "[PeakCompletedTasksPerHour] >= 0 AND " +
                "[AverageCompletedQuantityPerHour] >= 0 AND " +
                "[PeakCompletedQuantityPerHour] >= 0 AND " +
                "[LaborHours] >= 0 AND [DistanceCost] >= 0 AND " +
                "[LaborCost] >= 0 AND [CongestionCost] >= 0 AND " +
                "[TotalCost] >= 0 AND " +
                "LEN([RequestHash]) = 64 AND " +
                "[RequestHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([DatasetRequestHash]) = 64 AND " +
                "[DatasetRequestHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([ResultHash]) = 64 AND " +
                "[ResultHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([CurrencyCode]) = 3 AND " +
                "[CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new { x.TenantId, x.Id })
            .HasName("AK_Space_PlanningSimulationRun_Tenant_Id");
        entity.HasAlternateKey(x => new { x.TenantId, x.Id, x.SiteId })
            .HasName("AK_Space_PlanningSimulationRun_Tenant_Id_Site");
        entity.HasAlternateKey(x => new
            {
                x.TenantId,
                x.Id,
                x.BranchId,
                x.ScenarioVersionId,
            })
            .HasName(
                "AK_Space_PlanningSimulationRun_Tenant_Id_Branch_Version");
        entity.HasAlternateKey(x => new
            {
                x.TenantId,
                x.Id,
                x.ScenarioVersionId,
            })
            .HasName(
                "AK_Space_PlanningSimulationRun_Tenant_Id_Version");
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.DefinitionVersion)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.GeometryBasis)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.CurrencyCode)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(3)
            .IsRequired();
        foreach (var property in new[]
                 {
                     nameof(SpacePlanningSimulationRun.RequestHash),
                     nameof(SpacePlanningSimulationRun.DatasetRequestHash),
                     nameof(SpacePlanningSimulationRun.ResultHash),
                 })
        {
            entity.Property(property)
                .HasColumnType("char(64)")
                .IsUnicode(false)
                .IsFixedLength()
                .HasMaxLength(64)
                .IsRequired();
        }
        entity.Property(x => x.DefaultQuantityCapacity).HasPrecision(18, 4);
        entity.Property(x => x.DistanceCostPerMeter).HasPrecision(19, 6);
        entity.Property(x => x.LaborCostPerHour).HasPrecision(19, 6);
        entity.Property(x => x.CongestionCostPerTaskHour).HasPrecision(19, 6);
        entity.Property(x => x.CompletedQuantity).HasPrecision(28, 6);
        entity.Property(x => x.TotalDistanceMeters).HasPrecision(28, 6);
        entity.Property(x => x.DistanceCoveragePercent).HasPrecision(9, 4);
        entity.Property(x => x.PeakCapacityUtilizationPercent)
            .HasPrecision(38, 4);
        entity.Property(x => x.AverageCompletedTasksPerHour)
            .HasPrecision(28, 6);
        entity.Property(x => x.PeakCompletedTasksPerHour)
            .HasPrecision(28, 6);
        entity.Property(x => x.AverageCompletedQuantityPerHour)
            .HasPrecision(28, 6);
        entity.Property(x => x.PeakCompletedQuantityPerHour)
            .HasPrecision(28, 6);
        entity.Property(x => x.LaborHours).HasPrecision(28, 6);
        entity.Property(x => x.DistanceCost).HasPrecision(28, 6);
        entity.Property(x => x.LaborCost).HasPrecision(28, 6);
        entity.Property(x => x.CongestionCost).HasPrecision(28, 6);
        entity.Property(x => x.TotalCost).HasPrecision(28, 6);
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.BranchId,
                x.CreatedAtUtc,
            })
            .HasDatabaseName(
                "IX_Space_PlanningSimulationRun_Branch_Created");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.DatasetId,
                x.CreatedAtUtc,
            })
            .HasDatabaseName(
                "IX_Space_PlanningSimulationRun_Dataset_Created");
        entity.HasOne<SpacePlanningScenarioBranch>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningSimulationRun_Branch_Tenant");
        entity.HasOne<SpacePlanningHistoricalDataset>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.DatasetId,
                x.BranchId,
                x.ModelId,
                x.ScenarioVersionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.Id,
                x.BranchId,
                x.ModelId,
                x.ScenarioVersionId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningSimulationRun_Dataset_Tenant");
        entity.HasOne<SpaceModel>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningSimulationRun_Model_Tenant");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.ScenarioVersionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningSimulationRun_ScenarioVersion_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningSimulationLocationResult(
        ModelBuilder modelBuilder)
    {
        var entity =
            modelBuilder.Entity<SpacePlanningSimulationLocationResult>();
        entity.ToTable(
            "Space_PlanningSimulationLocationResult",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningSimulationLocationResult_Invariants",
                "[TaskCount] > 0 AND " +
                "[CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND " +
                "[TotalQuantity] > 0 AND " +
                "[DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND " +
                "[TotalDistanceMeters] >= 0 AND [QuantityCapacity] > 0 AND " +
                "[ConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND " +
                "[PeakConcurrentTasks] >= 0 AND " +
                "[PeakConcurrentQuantity] >= 0 AND " +
                "[CapacityUtilizationPercent] >= 0 AND " +
                "[CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 " +
                "AND [IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.Property(x => x.TotalQuantity).HasPrecision(28, 6);
        entity.Property(x => x.TotalDistanceMeters).HasPrecision(28, 6);
        entity.Property(x => x.QuantityCapacity).HasPrecision(18, 4);
        entity.Property(x => x.PeakConcurrentQuantity).HasPrecision(28, 6);
        entity.Property(x => x.CapacityUtilizationPercent)
            .HasPrecision(38, 4);
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.RunId,
                x.LocationLogicalId,
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningSimulationLocation_Run_Location");
        entity.HasOne<SpacePlanningSimulationRun>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.RunId,
                x.ScenarioVersionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.Id,
                x.ScenarioVersionId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningSimulationLocation_Run_Tenant");
        entity.HasOne<SpaceLocationRevision>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ScenarioVersionId,
                x.LocationLogicalId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelVersionId,
                x.LogicalId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningSimulationLocation_Location_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningComparison(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningComparison>();
        entity.ToTable(
            "Space_PlanningComparison",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningComparison_Invariants",
                "[RunCount] BETWEEN 2 AND 10 AND " +
                "[HistoricalFromUtc] < [HistoricalToUtc] AND " +
                "[MinimumDistanceCoveragePercent] BETWEEN 0 AND 100 AND " +
                "[MaximumPeakCapacityUtilizationPercent] >= 0 AND " +
                "[MaximumCongestionTaskHours] >= 0 AND " +
                "([MaximumTotalCost] IS NULL OR [MaximumTotalCost] >= 0) AND " +
                "LEN([RequestHash]) = 64 AND " +
                "[RequestHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([ComparisonHash]) = 64 AND " +
                "[ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([SourceDatasetHash]) = 64 AND " +
                "[SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([CurrencyCode]) = 3 AND " +
                "[CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new { x.TenantId, x.Id, x.SiteId })
            .HasName("AK_Space_PlanningComparison_Tenant_Id_Site");
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.DefinitionVersion)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        foreach (var property in new[]
                 {
                     nameof(SpacePlanningComparison.RequestHash),
                     nameof(SpacePlanningComparison.ComparisonHash),
                     nameof(SpacePlanningComparison.SourceDatasetHash),
                 })
        {
            entity.Property(property)
                .HasColumnType("char(64)")
                .IsUnicode(false)
                .IsFixedLength()
                .HasMaxLength(64)
                .IsRequired();
        }
        entity.Property(x => x.CurrencyCode)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(3)
            .IsRequired();
        entity.Property(x => x.HistoricalFromUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.HistoricalToUtc)
            .HasColumnType("datetimeoffset(7)");
        entity.Property(x => x.MinimumDistanceCoveragePercent)
            .HasPrecision(9, 4);
        entity.Property(x => x.MaximumPeakCapacityUtilizationPercent)
            .HasPrecision(38, 4);
        entity.Property(x => x.MaximumCongestionTaskHours)
            .HasPrecision(28, 6);
        entity.Property(x => x.MaximumTotalCost).HasPrecision(28, 6);
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.SiteId,
                x.CreatedAtUtc,
            })
            .HasDatabaseName("IX_Space_PlanningComparison_Site_Created");
        entity.HasOne<SpaceModel>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Space_PlanningComparison_Model_Tenant");
        entity.HasOne<SpaceModelVersion>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.BasePublishedVersionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ModelId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningComparison_BaseVersion_Tenant");
        entity.HasOne<SpacePlanningSimulationRun>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.BaselineRunId,
                x.SiteId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.SiteId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningComparison_BaselineRun_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningComparisonEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningComparisonEntry>();
        entity.ToTable(
            "Space_PlanningComparisonEntry",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningComparisonEntry_Invariants",
                "[SequenceNo] BETWEEN 1 AND 10 AND " +
                "[ScenarioContentRevision] >= 0 AND " +
                "[DistanceCoveragePercent] BETWEEN 0 AND 100 AND " +
                "[TotalDistanceMeters] >= 0 AND " +
                "[CongestionTaskSeconds] >= 0 AND " +
                "[OverloadedLocationCount] >= 0 AND " +
                "[PeakCapacityUtilizationPercent] >= 0 AND " +
                "[AverageCompletedTasksPerHour] >= 0 AND " +
                "[PeakCompletedTasksPerHour] >= 0 AND " +
                "[TotalCost] >= 0 AND [RiskCount] BETWEEN 0 AND 10 AND " +
                "LEN([RunResultHash]) = 64 AND " +
                "[RunResultHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "[IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.RunId,
            })
            .HasName(
                "AK_Space_PlanningComparisonEntry_Comparison_Run");
        entity.HasAlternateKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.Id,
                x.RunId,
            })
            .HasName(
                "AK_Space_PlanningComparisonEntry_Comparison_Id_Run");
        entity.Property(x => x.RunName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.RunResultHash)
            .HasColumnType("char(64)")
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(x => x.DistanceCoveragePercent).HasPrecision(9, 4);
        entity.Property(x => x.TotalDistanceMeters).HasPrecision(28, 6);
        entity.Property(x => x.PeakCapacityUtilizationPercent)
            .HasPrecision(38, 4);
        entity.Property(x => x.AverageCompletedTasksPerHour)
            .HasPrecision(28, 6);
        entity.Property(x => x.PeakCompletedTasksPerHour)
            .HasPrecision(28, 6);
        entity.Property(x => x.TotalCost).HasPrecision(28, 6);
        entity.Property(x => x.DistanceDeltaMeters).HasPrecision(28, 6);
        entity.Property(x => x.PeakCapacityUtilizationDeltaPercentagePoints)
            .HasPrecision(38, 4);
        entity.Property(x => x.AverageCompletedTasksPerHourDelta)
            .HasPrecision(28, 6);
        entity.Property(x => x.TotalCostDelta).HasPrecision(28, 6);
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.SequenceNo,
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningComparisonEntry_Comparison_Sequence");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.IsBaseline,
            })
            .HasFilter("[IsBaseline] = 1")
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningComparisonEntry_Comparison_Baseline");
        entity.HasOne<SpacePlanningComparison>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.SiteId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.SiteId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningComparisonEntry_Comparison_Tenant");
        entity.HasOne<SpacePlanningSimulationRun>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.RunId,
                x.BranchId,
                x.ScenarioVersionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.Id,
                x.BranchId,
                x.ScenarioVersionId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningComparisonEntry_Run_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningComparisonRisk(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningComparisonRisk>();
        entity.ToTable(
            "Space_PlanningComparisonRisk",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningComparisonRisk_Invariants",
                "[Severity] BETWEEN 1 AND 3 AND " +
                "LEN([Code]) BETWEEN 1 AND 100 AND [IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.Property(x => x.Code)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.EntryId,
                x.Code,
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningComparisonRisk_Entry_Code");
        entity.HasOne<SpacePlanningComparisonEntry>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.EntryId,
                x.RunId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.Id,
                x.RunId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningComparisonRisk_Entry_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
    }

    private void ConfigurePlanningDecisionRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SpacePlanningDecisionRecord>();
        entity.ToTable(
            "Space_PlanningDecisionRecord",
            table => table.HasCheckConstraint(
                "CK_Space_PlanningDecisionRecord_Invariants",
                "[Outcome] BETWEEN 1 AND 3 AND " +
                "(([Outcome] = 1 AND [SelectedRunId] IS NOT NULL) OR " +
                "([Outcome] IN (2, 3) AND [SelectedRunId] IS NULL)) AND " +
                "([SupersedesDecisionId] IS NULL OR " +
                "[SupersedesDecisionId] <> [Id]) AND " +
                "LEN([Rationale]) BETWEEN 1 AND 2000 AND " +
                "LEN([ComparisonHash]) = 64 AND " +
                "[ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND " +
                "LEN([RequestHash]) = 64 AND " +
                "[RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        ConfigureTenantEntity(entity);
        entity.HasAlternateKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.Id,
            })
            .HasName(
                "AK_Space_PlanningDecisionRecord_Comparison_Id");
        entity.Property(x => x.Rationale).HasMaxLength(2_000).IsRequired();
        entity.Property(x => x.DefinitionVersion)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();
        foreach (var property in new[]
                 {
                     nameof(SpacePlanningDecisionRecord.ComparisonHash),
                     nameof(SpacePlanningDecisionRecord.RequestHash),
                 })
        {
            entity.Property(property)
                .HasColumnType("char(64)")
                .IsUnicode(false)
                .IsFixedLength()
                .HasMaxLength(64)
                .IsRequired();
        }
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.CreatedAtUtc,
            })
            .HasDatabaseName(
                "IX_Space_PlanningDecisionRecord_Comparison_Created");
        entity.HasIndex(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.SupersedesDecisionId,
            })
            .HasFilter("[SupersedesDecisionId] IS NOT NULL")
            .IsUnique()
            .HasDatabaseName(
                "UX_Space_PlanningDecisionRecord_Supersedes");
        entity.HasOne<SpacePlanningComparison>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.SiteId,
            })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.SiteId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningDecisionRecord_Comparison_Tenant");
        entity.HasOne<SpacePlanningComparisonEntry>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.SelectedRunId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.RunId,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningDecisionRecord_SelectedRun_Tenant");
        entity.HasOne<SpacePlanningDecisionRecord>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.SupersedesDecisionId,
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ComparisonId,
                x.Id,
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "FK_Space_PlanningDecisionRecord_Supersedes_Tenant");
        entity.HasQueryFilter(
            x => x.TenantId == CurrentTenantId && !x.IsDeleted);
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

    private void ProtectPutawayRecommendationHistory()
    {
        if (ChangeTracker.Entries<SpacePutawayRecommendation>()
            .Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Putaway recommendations are immutable.");
        }
    }

    private void ProtectDispatchRecommendationHistory()
    {
        if (ChangeTracker.Entries<SpaceDispatchRecommendation>()
            .Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Dispatch recommendations are immutable.");
        }
    }

    private void ProtectPlanningScenarioHistory()
    {
        var changed =
            ChangeTracker.Entries<SpacePlanningScenarioBranch>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningHistoricalDataset>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningHistoricalTask>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningSimulationRun>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningSimulationLocationResult>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningComparison>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningComparisonEntry>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningComparisonRisk>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<SpacePlanningDecisionRecord>()
                .Any(entry => entry.State is
                    EntityState.Modified or EntityState.Deleted);
        if (changed)
        {
            throw new InvalidOperationException(
                "Planning scenario, simulation, comparison and decision " +
                "evidence is immutable.");
        }
    }

    private void ProtectPersonnelEventHistory()
    {
        foreach (var entry in ChangeTracker.Entries<SpacePersonnelEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Personnel events are append-only.");
            }
        }

        var identityProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(SpacePersonnelCurrentState.TenantId),
            nameof(SpacePersonnelCurrentState.SiteId),
            nameof(SpacePersonnelCurrentState.SourceId),
            nameof(SpacePersonnelCurrentState.SourceKind),
            nameof(SpacePersonnelCurrentState.PersonExternalId),
            nameof(SpacePersonnelCurrentState.IsDeleted),
        };
        foreach (var entry in ChangeTracker
                     .Entries<SpacePersonnelCurrentState>()
                     .Where(value => value.State == EntityState.Modified))
        {
            if (entry.Properties.Any(property =>
                    property.IsModified &&
                    identityProperties.Contains(property.Metadata.Name)) ||
                entry.Property(x => x.UserId).IsModified &&
                entry.Property(x => x.UserId).OriginalValue.HasValue)
            {
                throw new InvalidOperationException(
                    "Personnel current-state identity cannot be reassigned.");
            }
        }
    }

    private void ProtectDeviceEventHistory()
    {
        foreach (var entry in ChangeTracker.Entries<SpaceDeviceEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Device events are append-only.");
            }
        }

        var immutableMappingProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(SpaceDeviceMapping.TenantId),
            nameof(SpaceDeviceMapping.SiteId),
            nameof(SpaceDeviceMapping.SourceId),
            nameof(SpaceDeviceMapping.SourceKind),
            nameof(SpaceDeviceMapping.DeviceExternalId),
            nameof(SpaceDeviceMapping.IsDeleted),
        };
        foreach (var entry in ChangeTracker.Entries<SpaceDeviceMapping>())
        {
            if (entry.State == EntityState.Deleted ||
                entry.State == EntityState.Modified &&
                entry.Properties.Any(property =>
                    property.IsModified &&
                    immutableMappingProperties.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException(
                    "Device mapping source identity cannot be reassigned or deleted.");
            }
        }

        var immutableStateProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(SpaceDeviceCurrentState.TenantId),
            nameof(SpaceDeviceCurrentState.SiteId),
            nameof(SpaceDeviceCurrentState.SourceId),
            nameof(SpaceDeviceCurrentState.SourceKind),
            nameof(SpaceDeviceCurrentState.DeviceExternalId),
            nameof(SpaceDeviceCurrentState.DeviceMappingId),
            nameof(SpaceDeviceCurrentState.IsDeleted),
        };
        foreach (var entry in ChangeTracker
                     .Entries<SpaceDeviceCurrentState>())
        {
            if (entry.State == EntityState.Deleted ||
                entry.State == EntityState.Modified &&
                entry.Properties.Any(property =>
                    property.IsModified &&
                    immutableStateProperties.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException(
                    "Device current-state identity cannot be reassigned or deleted.");
            }
        }

        var immutableAlarmProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(SpaceDeviceAlarmState.TenantId),
            nameof(SpaceDeviceAlarmState.SiteId),
            nameof(SpaceDeviceAlarmState.SourceId),
            nameof(SpaceDeviceAlarmState.SourceKind),
            nameof(SpaceDeviceAlarmState.DeviceExternalId),
            nameof(SpaceDeviceAlarmState.DeviceMappingId),
            nameof(SpaceDeviceAlarmState.AlarmExternalId),
            nameof(SpaceDeviceAlarmState.IsDeleted),
        };
        foreach (var entry in ChangeTracker.Entries<SpaceDeviceAlarmState>())
        {
            if (entry.State == EntityState.Deleted ||
                entry.State == EntityState.Modified &&
                entry.Properties.Any(property =>
                    property.IsModified &&
                    immutableAlarmProperties.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException(
                    "Device alarm-state identity cannot be reassigned or deleted.");
            }
        }
    }

    private void ProtectExcelMappingVersionHistory()
    {
        foreach (var entry in ChangeTracker
            .Entries<SpaceExcelMappingProfileVersion>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Excel mapping profile versions are append-only.");
            }
        }
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
                SpaceUnderlayCalibration calibration =>
                    calibration.ModelVersionId,
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

    private void ProtectGenerationLockedFactHistory()
    {
        foreach (var entry in ChangeTracker.Entries<SpaceGenerationLockedFact>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new SpaceProposalStateException(
                    "Generation locked facts are immutable.");
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

    private void ProtectAiPolicyHistory()
    {
        foreach (var entry in ChangeTracker
            .Entries<SpaceAiTenantPolicyConfiguration>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "AI policy versions are append-only.");
            }
            if (entry.State != EntityState.Modified)
                continue;

            var active = entry.Property(x => x.IsActive);
            var onlyDeactivation =
                active.IsModified &&
                active.OriginalValue &&
                !active.CurrentValue &&
                entry.Properties.All(property =>
                    !property.IsModified ||
                    property.Metadata.Name == nameof(
                        SpaceAiTenantPolicyConfiguration.IsActive));
            if (!onlyDeactivation)
            {
                throw new InvalidOperationException(
                    "AI policy versions are immutable after creation.");
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

    private void ProtectUnderlayCalibrationHistory()
    {
        foreach (var entry in ChangeTracker
            .Entries<SpaceUnderlayCalibration>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new SpaceUnderlayCalibrationException(
                    "Underlay calibration records are append-only.");
            }
        }
    }

    private void ProtectElementCommandHistory()
    {
        foreach (var entry in ChangeTracker
            .Entries<SpaceElementCommandRecord>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Element command audit records are append-only.");
            }
        }

        foreach (var entry in ChangeTracker
            .Entries<SpaceElementCommandBatch>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Element command batches cannot be deleted.");
            }
            if (entry.State != EntityState.Modified)
                continue;

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SpaceElementCommandBatch.ResultFloorRevision),
                nameof(SpaceElementCommandBatch.ResultVersionContentRevision),
                nameof(SpaceElementCommandBatch.ResponseJson),
            };
            if (entry.Properties.Any(property =>
                    property.IsModified &&
                    !allowed.Contains(property.Metadata.Name)) ||
                entry.Property(x => x.ResponseJson).OriginalValue is not null)
            {
                throw new InvalidOperationException(
                    "Completed element command batches are immutable.");
            }
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
