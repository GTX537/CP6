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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
        ConfigureVersion(modelBuilder);
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
        StampAndValidateTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ProtectPublishedHistory();
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
