IF OBJECT_ID(N'[__EFMigrationsHistory_Space]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory_Space] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory_Space] PRIMARY KEY ([MigrationId])
    );
END;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    CREATE TABLE [Space_Model] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [Mode] smallint NOT NULL,
        [CutoverState] smallint NOT NULL,
        [CutoverOperationId] uniqueidentifier NULL,
        [ActiveDraftVersionId] uniqueidentifier NULL,
        [CurrentPublishedVersionId] uniqueidentifier NULL,
        [LastMaterializedHash] char(64) NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_Model] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_Model_TenantId_Id] UNIQUE ([TenantId], [Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    CREATE TABLE [Space_ModelVersion] (
        [Id] uniqueidentifier NOT NULL,
        [ModelId] uniqueidentifier NOT NULL,
        [VersionNo] bigint NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Status] smallint NOT NULL,
        [BasedOnVersionId] uniqueidentifier NULL,
        [ContentRevision] bigint NOT NULL,
        [ContentHash] char(64) NULL,
        [RuleSetVersion] nvarchar(50) NULL,
        [ValidatedHash] char(64) NULL,
        [WmsCapabilityHash] char(64) NULL,
        [PublishedAtUtc] datetime2 NULL,
        [PublishedBy] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ModelVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ModelVersion_TenantId_ModelId_Id] UNIQUE ([TenantId], [ModelId], [Id]),
        CONSTRAINT [FK_Space_ModelVersion_BasedOn_Tenant_Model_Version] FOREIGN KEY ([TenantId], [ModelId], [BasedOnVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ModelVersion_Space_Model_Tenant_Model] FOREIGN KEY ([TenantId], [ModelId]) REFERENCES [Space_Model] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    CREATE INDEX [IX_Space_Model_TenantId_Id_ActiveDraftVersionId] ON [Space_Model] ([TenantId], [Id], [ActiveDraftVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    CREATE INDEX [IX_Space_Model_TenantId_Id_CurrentPublishedVersionId] ON [Space_Model] ([TenantId], [Id], [CurrentPublishedVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_Model_Tenant_ActiveDraft] ON [Space_Model] ([TenantId], [ActiveDraftVersionId]) WHERE [ActiveDraftVersionId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_Model_Tenant_CurrentPublished] ON [Space_Model] ([TenantId], [CurrentPublishedVersionId]) WHERE [CurrentPublishedVersionId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_Model_Tenant_Site_Active] ON [Space_Model] ([TenantId], [SiteId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelVersion_Tenant_BasedOn] ON [Space_ModelVersion] ([TenantId], [BasedOnVersionId]) WHERE [BasedOnVersionId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    CREATE INDEX [IX_Space_ModelVersion_Tenant_Model_Status] ON [Space_ModelVersion] ([TenantId], [ModelId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    CREATE INDEX [IX_Space_ModelVersion_TenantId_ModelId_BasedOnVersionId] ON [Space_ModelVersion] ([TenantId], [ModelId], [BasedOnVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_ModelVersion_Tenant_Model_VersionNo] ON [Space_ModelVersion] ([TenantId], [ModelId], [VersionNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    ALTER TABLE [Space_Model] ADD CONSTRAINT [FK_Space_Model_ActiveDraft_Tenant_Model_Version] FOREIGN KEY ([TenantId], [Id], [ActiveDraftVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    ALTER TABLE [Space_Model] ADD CONSTRAINT [FK_Space_Model_CurrentPublished_Tenant_Model_Version] FOREIGN KEY ([TenantId], [Id], [CurrentPublishedVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726064940_SpaceE01S01ModelVersionBaseline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726064940_SpaceE01S01ModelVersionBaseline', N'8.0.12');
END;
GO

COMMIT;
GO
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    ALTER TABLE [Space_ModelVersion] ADD CONSTRAINT [AK_Space_ModelVersion_TenantId_Id] UNIQUE ([TenantId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE TABLE [Space_File] (
        [Id] uniqueidentifier NOT NULL,
        [StorageKey] nvarchar(500) NOT NULL,
        [OriginalName] nvarchar(260) NOT NULL,
        [DeclaredContentType] nvarchar(200) NULL,
        [DetectedContentType] nvarchar(200) NULL,
        [Extension] nvarchar(20) NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] char(64) NULL,
        [State] smallint NOT NULL,
        [ScanEngine] nvarchar(100) NULL,
        [SignatureVersion] nvarchar(100) NULL,
        [ScanResultCode] nvarchar(100) NULL,
        [RetentionClass] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_File] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_File_TenantId_Id] UNIQUE ([TenantId], [Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE TABLE [Space_ModelSource] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [SourceType] smallint NOT NULL,
        [FileId] uniqueidentifier NULL,
        [DisplayName] nvarchar(260) NOT NULL,
        [Sha256] char(64) NOT NULL,
        [ParserVersion] nvarchar(100) NULL,
        [MappingProfileId] uniqueidentifier NULL,
        [MappingProfileVersion] bigint NULL,
        [Unit] nvarchar(50) NULL,
        [ScaleToMillimeters] decimal(18,8) NULL,
        [TransformJson] nvarchar(max) NULL,
        [State] smallint NOT NULL,
        [ImportedCommandBatchId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ModelSource] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ModelSource_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [FK_Space_ModelSource_File_Tenant] FOREIGN KEY ([TenantId], [FileId]) REFERENCES [Space_File] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ModelSource_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE TABLE [Space_Artifact] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [FileId] uniqueidentifier NOT NULL,
        [ArtifactType] smallint NOT NULL,
        [SchemaVersion] nvarchar(50) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_Artifact] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_Artifact_File_Tenant] FOREIGN KEY ([TenantId], [FileId]) REFERENCES [Space_File] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_Artifact_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_Artifact_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_Artifact_Tenant_File_Active] ON [Space_Artifact] ([TenantId], [FileId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE INDEX [IX_Space_Artifact_Tenant_Version] ON [Space_Artifact] ([TenantId], [ModelVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_Artifact_Tenant_Version_Source_Active] ON [Space_Artifact] ([TenantId], [ModelVersionId], [SourceId]) WHERE [SourceId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE INDEX [IX_Space_File_Tenant_State] ON [Space_File] ([TenantId], [State]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_File_StorageKey] ON [Space_File] ([StorageKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_File_Tenant_Hash_Retention_Reusable] ON [Space_File] ([TenantId], [Sha256], [RetentionClass]) WHERE [Sha256] IS NOT NULL AND [State] IN (1, 2, 3) AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelSource_Tenant_File_Active] ON [Space_ModelSource] ([TenantId], [FileId]) WHERE [FileId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE INDEX [IX_Space_ModelSource_Tenant_SourceHash] ON [Space_ModelSource] ([TenantId], [Sha256]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ModelSource_Version_Hash_Type_Active] ON [Space_ModelSource] ([TenantId], [ModelVersionId], [Sha256], [SourceType]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726072628_SpaceE01S02SourceFileLineage', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    ALTER TABLE [Space_Artifact] ADD [JobId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    ALTER TABLE [Space_Artifact] ADD CONSTRAINT [AK_Space_Artifact_TenantId_Id] UNIQUE ([TenantId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE TABLE [Space_Job] (
        [Id] uniqueidentifier NOT NULL,
        [JobType] smallint NOT NULL,
        [SubjectType] smallint NOT NULL,
        [SubjectId] uniqueidentifier NOT NULL,
        [BusinessKey] char(64) NOT NULL,
        [InputHash] char(64) NOT NULL,
        [Status] smallint NOT NULL,
        [Priority] smallint NOT NULL,
        [AttemptCount] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [NextAttemptAtUtc] datetime2 NOT NULL,
        [LockedBy] nvarchar(200) NULL,
        [LockedAtUtc] datetime2 NULL,
        [LockExpiresAtUtc] datetime2 NULL,
        [ActiveAttemptId] uniqueidentifier NULL,
        [LeaseRevision] bigint NOT NULL,
        [ProgressDone] bigint NOT NULL,
        [ProgressTotal] bigint NOT NULL,
        [ProgressStage] nvarchar(100) NULL,
        [RequestedBy] uniqueidentifier NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [StartedAtUtc] datetime2 NULL,
        [FinishedAtUtc] datetime2 NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [ResultSummaryJson] nvarchar(max) NULL,
        [LastFailureKind] smallint NULL,
        [LastErrorCode] nvarchar(100) NULL,
        [LastErrorSummary] nvarchar(1000) NULL,
        [RetryOfJobId] uniqueidentifier NULL,
        [CancellationRequestedAtUtc] datetime2 NULL,
        [CancellationRequestedBy] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_Job] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_Job_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_Job_Attempts] CHECK ([AttemptCount] >= 0 AND [MaxAttempts] BETWEEN 1 AND 20 AND [AttemptCount] <= [MaxAttempts]),
        CONSTRAINT [CK_Space_Job_Lease] CHECK (([Status] = 1 AND [LockedBy] IS NOT NULL AND [LockedAtUtc] IS NOT NULL AND [LockExpiresAtUtc] IS NOT NULL AND [ActiveAttemptId] IS NOT NULL) OR ([Status] <> 1 AND [LockedBy] IS NULL AND [LockedAtUtc] IS NULL AND [LockExpiresAtUtc] IS NULL AND [ActiveAttemptId] IS NULL)),
        CONSTRAINT [CK_Space_Job_Progress] CHECK ([ProgressDone] >= 0 AND [ProgressTotal] >= 0 AND ([ProgressTotal] = 0 OR [ProgressDone] <= [ProgressTotal])),
        CONSTRAINT [FK_Space_Job_RetryOf_Tenant] FOREIGN KEY ([TenantId], [RetryOfJobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE TABLE [Space_JobAttempt] (
        [Id] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [AttemptNo] int NOT NULL,
        [WorkerId] nvarchar(200) NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [FinishedAtUtc] datetime2 NULL,
        [Outcome] smallint NOT NULL,
        [InputHash] char(64) NOT NULL,
        [ProcessorVersion] nvarchar(100) NOT NULL,
        [ResourceUsageJson] nvarchar(max) NULL,
        [FailureKind] smallint NULL,
        [ErrorCode] nvarchar(100) NULL,
        [SanitizedError] nvarchar(1000) NULL,
        [DiagnosticArtifactId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_JobAttempt] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_JobAttempt_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_JobAttempt_OutcomeTime] CHECK (([Outcome] = 0 AND [FinishedAtUtc] IS NULL) OR ([Outcome] <> 0 AND [FinishedAtUtc] IS NOT NULL)),
        CONSTRAINT [FK_Space_JobAttempt_DiagnosticArtifact_Tenant] FOREIGN KEY ([TenantId], [DiagnosticArtifactId]) REFERENCES [Space_Artifact] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_JobAttempt_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE TABLE [Space_ModelIssue] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NULL,
        [SourceId] uniqueidentifier NULL,
        [JobId] uniqueidentifier NULL,
        [Severity] smallint NOT NULL,
        [Code] nvarchar(100) NOT NULL,
        [SourceRef] nvarchar(500) NULL,
        [TargetLogicalId] uniqueidentifier NULL,
        [MessageArgsJson] nvarchar(max) NOT NULL,
        [SuggestedActionCode] nvarchar(100) NULL,
        [Status] smallint NOT NULL,
        [ResolutionCommandBatchId] uniqueidentifier NULL,
        [AcknowledgedBy] uniqueidentifier NULL,
        [AcknowledgedAtUtc] datetime2 NULL,
        [AcknowledgementReason] nvarchar(1000) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ModelIssue] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_ModelIssue_Context] CHECK ([ModelVersionId] IS NOT NULL OR [SourceId] IS NOT NULL OR [JobId] IS NOT NULL),
        CONSTRAINT [CK_Space_ModelIssue_SourceVersion] CHECK ([SourceId] IS NULL OR [ModelVersionId] IS NOT NULL),
        CONSTRAINT [FK_Space_ModelIssue_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ModelIssue_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ModelIssue_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE TABLE [Space_JobStep] (
        [Id] uniqueidentifier NOT NULL,
        [AttemptId] uniqueidentifier NOT NULL,
        [StepNo] int NOT NULL,
        [StepCode] nvarchar(100) NOT NULL,
        [Status] smallint NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [FinishedAtUtc] datetime2 NULL,
        [CheckpointJson] nvarchar(max) NULL,
        [OutputHash] char(64) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_JobStep] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_JobStep_StatusTime] CHECK (([Status] = 0 AND [FinishedAtUtc] IS NULL) OR ([Status] <> 0 AND [FinishedAtUtc] IS NOT NULL)),
        CONSTRAINT [FK_Space_JobStep_Attempt_Tenant] FOREIGN KEY ([TenantId], [AttemptId]) REFERENCES [Space_JobAttempt] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_Artifact_Tenant_Job_Active] ON [Space_Artifact] ([TenantId], [JobId]) WHERE [JobId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_Job_Tenant_Claim] ON [Space_Job] ([TenantId], [Status], [NextAttemptAtUtc], [LockExpiresAtUtc], [Priority], [RequestedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_Job_Tenant_Correlation] ON [Space_Job] ([TenantId], [CorrelationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_Job_Tenant_Subject] ON [Space_Job] ([TenantId], [SubjectType], [SubjectId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_Job_TenantId_RetryOfJobId] ON [Space_Job] ([TenantId], [RetryOfJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_Job_Tenant_Type_BusinessKey_Active] ON [Space_Job] ([TenantId], [JobType], [BusinessKey]) WHERE [Status] IN (0, 1) AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_JobAttempt_Tenant_Job_Started] ON [Space_JobAttempt] ([TenantId], [JobId], [StartedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_JobAttempt_TenantId_DiagnosticArtifactId] ON [Space_JobAttempt] ([TenantId], [DiagnosticArtifactId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_JobAttempt_Tenant_Job_AttemptNo] ON [Space_JobAttempt] ([TenantId], [JobId], [AttemptNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_JobStep_Tenant_Attempt_StepCode] ON [Space_JobStep] ([TenantId], [AttemptId], [StepCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_JobStep_Tenant_Attempt_StepNo] ON [Space_JobStep] ([TenantId], [AttemptId], [StepNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelIssue_Tenant_Job_Status] ON [Space_ModelIssue] ([TenantId], [JobId], [Status]) WHERE [JobId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_ModelIssue_Tenant_Version_Status] ON [Space_ModelIssue] ([TenantId], [ModelVersionId], [Status], [Severity], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    CREATE INDEX [IX_Space_ModelIssue_TenantId_ModelVersionId_SourceId] ON [Space_ModelIssue] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    ALTER TABLE [Space_Artifact] ADD CONSTRAINT [FK_Space_Artifact_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726080918_SpaceE01S03JobLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726080918_SpaceE01S03JobLedger', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    ALTER TABLE [Space_ModelVersion] ADD [CloneOperationId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_FloorRevision] (
        [Id] uniqueidentifier NOT NULL,
        [SiteLogicalId] uniqueidentifier NOT NULL,
        [Level] int NOT NULL,
        [FloorCode] nvarchar(100) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Elevation] int NOT NULL,
        [Height] int NOT NULL,
        [BoundaryJson] nvarchar(max) NOT NULL,
        [CoordinateSystem] nvarchar(100) NOT NULL,
        [UnderlaySourceId] uniqueidentifier NULL,
        [UnderlayScale] decimal(18,8) NULL,
        [UnderlayOffsetX] int NOT NULL,
        [UnderlayOffsetY] int NOT NULL,
        [UnderlayRotationZ] decimal(9,4) NOT NULL,
        [Revision] bigint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_FloorRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_FloorRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_FloorRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [FK_Space_FloorRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_FloorRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_FloorRevision_UnderlaySource_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [UnderlaySourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_ElementRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ParentLogicalId] uniqueidentifier NULL,
        [ElementType] nvarchar(100) NOT NULL,
        [GeometryJson] nvarchar(max) NOT NULL,
        [ModelAssetId] uniqueidentifier NULL,
        [X] int NOT NULL,
        [Y] int NOT NULL,
        [Z] int NOT NULL,
        [RotationZ] decimal(9,4) NOT NULL,
        [Width] int NOT NULL,
        [Height] int NOT NULL,
        [Depth] int NOT NULL,
        [BusinessCode] nvarchar(200) NULL,
        [LinkedEntityType] nvarchar(100) NULL,
        [LinkedLogicalId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_ElementRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ElementRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_ElementRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_ElementRevision_Geometry] CHECK ([RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Height] >= 0 AND [Depth] >= 0),
        CONSTRAINT [FK_Space_ElementRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ElementRevision_Parent_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [ParentLogicalId]) REFERENCES [Space_ElementRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ElementRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ElementRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_ZoneRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ZoneCode] nvarchar(100) NOT NULL,
        [ZoneType] smallint NOT NULL,
        [PolygonJson] nvarchar(max) NOT NULL,
        [Color] nvarchar(50) NULL,
        [CapabilityFlags] nvarchar(1000) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_ZoneRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ZoneRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_ZoneRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [FK_Space_ZoneRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ZoneRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ZoneRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_ElementAttribute] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [ElementRevisionId] uniqueidentifier NOT NULL,
        [Namespace] nvarchar(100) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [ValueType] nvarchar(50) NOT NULL,
        [Value] nvarchar(max) NULL,
        [Unit] nvarchar(50) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ElementAttribute] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_ElementAttribute_Element_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [ElementRevisionId]) REFERENCES [Space_ElementRevision] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_AisleRevision] (
        [Id] uniqueidentifier NOT NULL,
        [ZoneLogicalId] uniqueidentifier NOT NULL,
        [AisleCode] nvarchar(100) NOT NULL,
        [PolygonJson] nvarchar(max) NOT NULL,
        [CenterlineJson] nvarchar(max) NOT NULL,
        [Direction] smallint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_AisleRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_AisleRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_AisleRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [FK_Space_AisleRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_AisleRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_AisleRevision_Zone_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [ZoneLogicalId]) REFERENCES [Space_ZoneRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_RackRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ZoneLogicalId] uniqueidentifier NOT NULL,
        [AisleLogicalId] uniqueidentifier NULL,
        [RackCode] nvarchar(100) NOT NULL,
        [TemplateVersionId] uniqueidentifier NULL,
        [X] int NOT NULL,
        [Y] int NOT NULL,
        [Z] int NOT NULL,
        [RotationZ] decimal(9,4) NOT NULL,
        [Width] int NOT NULL,
        [Depth] int NOT NULL,
        [Height] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_RackRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_RackRevision_Geometry] CHECK ([RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Depth] >= 0 AND [Height] >= 0),
        CONSTRAINT [FK_Space_RackRevision_Aisle_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [AisleLogicalId]) REFERENCES [Space_AisleRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Zone_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [ZoneLogicalId]) REFERENCES [Space_ZoneRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_LocationRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [RackLogicalId] uniqueidentifier NULL,
        [LocationCode] nvarchar(200) NULL,
        [ColumnNo] int NOT NULL,
        [LevelNo] int NOT NULL,
        [DepthNo] int NOT NULL,
        [Width] int NOT NULL,
        [Height] int NOT NULL,
        [Depth] int NOT NULL,
        [MaxLoad] decimal(18,4) NULL,
        [CodeOrigin] smallint NOT NULL,
        [ExternalBindingState] smallint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_LocationRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_LocationRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_LocationRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_LocationRevision_Dimensions] CHECK ([ColumnNo] > 0 AND [LevelNo] > 0 AND [DepthNo] > 0 AND [Width] > 0 AND [Height] > 0 AND [Depth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)),
        CONSTRAINT [FK_Space_LocationRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationRevision_Rack_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [RackLogicalId]) REFERENCES [Space_RackRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_RackLevelRevision] (
        [Id] uniqueidentifier NOT NULL,
        [RackLogicalId] uniqueidentifier NOT NULL,
        [LevelNo] int NOT NULL,
        [BottomZ] int NOT NULL,
        [ClearHeight] int NOT NULL,
        [BinCount] int NOT NULL,
        [DepthCount] int NOT NULL,
        [CellWidth] int NOT NULL,
        [CellDepth] int NOT NULL,
        [MaxLoad] decimal(18,4) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackLevelRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackLevelRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_RackLevelRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_RackLevelRevision_Dimensions] CHECK ([LevelNo] > 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)),
        CONSTRAINT [FK_Space_RackLevelRevision_Rack_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [RackLogicalId]) REFERENCES [Space_RackRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackLevelRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackLevelRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ModelVersion_Tenant_Model_CloneOperation] ON [Space_ModelVersion] ([TenantId], [ModelId], [CloneOperationId]) WHERE [CloneOperationId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_AisleRevision_TenantId_ModelVersionId_SourceId] ON [Space_AisleRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_AisleRevision_Zone_Code_Active] ON [Space_AisleRevision] ([TenantId], [ModelVersionId], [ZoneLogicalId], [AisleCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ElementAttribute_Element_Key_Active] ON [Space_ElementAttribute] ([TenantId], [ModelVersionId], [ElementRevisionId], [Namespace], [Key]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ElementRevision_Floor_Type] ON [Space_ElementRevision] ([TenantId], [ModelVersionId], [FloorLogicalId], [ElementType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ElementRevision_TenantId_ModelVersionId_ParentLogicalId] ON [Space_ElementRevision] ([TenantId], [ModelVersionId], [ParentLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ElementRevision_TenantId_ModelVersionId_SourceId] ON [Space_ElementRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_TenantId_ModelVersionId_SourceId] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_TenantId_ModelVersionId_UnderlaySourceId] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [UnderlaySourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_Version_Level] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [Level]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_FloorRevision_Version_Code_Active] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [FloorCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_LocationRevision_Rack_Position_Active] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [RackLogicalId], [LevelNo], [ColumnNo], [DepthNo]) WHERE [RackLogicalId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_LocationRevision_TenantId_ModelVersionId_FloorLogicalId] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [FloorLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_LocationRevision_TenantId_ModelVersionId_SourceId] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_LocationRevision_Version_Code_Active] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [LocationCode]) WHERE [LocationCode] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackLevelRevision_TenantId_ModelVersionId_SourceId] ON [Space_RackLevelRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_RackLevelRevision_Rack_Level_Active] ON [Space_RackLevelRevision] ([TenantId], [ModelVersionId], [RackLogicalId], [LevelNo]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackRevision_TenantId_ModelVersionId_AisleLogicalId] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [AisleLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackRevision_TenantId_ModelVersionId_FloorLogicalId] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [FloorLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackRevision_TenantId_ModelVersionId_SourceId] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_RackRevision_Zone_Code_Active] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [ZoneLogicalId], [RackCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ZoneRevision_TenantId_ModelVersionId_SourceId] ON [Space_ZoneRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ZoneRevision_Floor_Code_Active] ON [Space_ZoneRevision] ([TenantId], [ModelVersionId], [FloorLogicalId], [ZoneCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726085852_SpaceE01S04PublishedClone', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    CREATE TABLE [Space_IdempotencyRecord] (
        [Id] uniqueidentifier NOT NULL,
        [PrincipalId] uniqueidentifier NOT NULL,
        [Operation] nvarchar(100) NOT NULL,
        [IdempotencyKeyHash] char(64) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [HttpStatusCode] int NOT NULL,
        [ReplayUntilUtc] datetime2 NOT NULL,
        [RetainUntilUtc] datetime2 NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_IdempotencyRecord] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    CREATE INDEX [IX_Space_IdempotencyRecord_Tenant_Retention] ON [Space_IdempotencyRecord] ([TenantId], [RetainUntilUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_IdempotencyRecord_Tenant_Principal_Operation_Key] ON [Space_IdempotencyRecord] ([TenantId], [PrincipalId], [Operation], [IdempotencyKeyHash]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726092519_SpaceE01S05DesignApiIdempotency', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    ALTER TABLE [Space_File] ADD [ContentDeletedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    ALTER TABLE [Space_File] ADD [DeletionRequestedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    ALTER TABLE [Space_File] ADD [RetainUntilUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    UPDATE [Space_File]
    SET [DeletionRequestedAtUtc] =
        COALESCE([ModifiedAtUtc], [CreatedAtUtc], SYSUTCDATETIME())
    WHERE ([State] = 5 OR [IsDeleted] = 1)
      AND [DeletionRequestedAtUtc] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_File_Tenant_PendingObjectDeletion] ON [Space_File] ([TenantId], [DeletionRequestedAtUtc], [ContentDeletedAtUtc]) WHERE [State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [ContentDeletedAtUtc] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_File_Tenant_Retention] ON [Space_File] ([TenantId], [RetainUntilUtc], [State]) WHERE [RetainUntilUtc] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_File] ADD CONSTRAINT [CK_Space_File_ContentDeletion] CHECK ([ContentDeletedAtUtc] IS NULL OR ([State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [IsDeleted] = 1))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260730152005_SpaceE01S06FileSafetyRetention', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE TABLE [Space_GenerationRun] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [SourceHash] char(64) NOT NULL,
        [BaseContentRevision] bigint NOT NULL,
        [Status] smallint NOT NULL,
        [Progress] int NOT NULL,
        [IdempotencyKeyHash] char(64) NOT NULL,
        [BusinessKeyHash] char(64) NOT NULL,
        [BasedOnRunId] uniqueidentifier NULL,
        [IsCurrent] bit NOT NULL,
        [MappingProfileVersionId] uniqueidentifier NULL,
        [RackGenerationProfileVersionId] uniqueidentifier NULL,
        [RuleVersion] nvarchar(64) NOT NULL,
        [PolicySnapshot] smallint NOT NULL,
        [ProviderConfigVersionId] uniqueidentifier NULL,
        [ProviderCode] nvarchar(64) NULL,
        [ProviderModel] nvarchar(128) NULL,
        [InputSchemaVersion] nvarchar(32) NOT NULL,
        [OutputSchemaVersion] nvarchar(32) NULL,
        [JobId] uniqueidentifier NOT NULL,
        [FailureCode] nvarchar(64) NULL,
        [FailureSummary] nvarchar(1024) NULL,
        [DegradedReason] nvarchar(64) NULL,
        [CancelRequestedAtUtc] datetime2 NULL,
        [CancelPending] bit NOT NULL,
        [CancelledAtUtc] datetime2 NULL,
        [ReviewCompletedAtUtc] datetime2 NULL,
        [AppliedContentRevision] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_GenerationRun] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_GenerationRun_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_GenerationRun_Progress] CHECK ([Progress] >= 0 AND [Progress] <= 100),
        CONSTRAINT [FK_Space_GenerationRun_BasedOn_Tenant] FOREIGN KEY ([TenantId], [BasedOnRunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationRun_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationRun_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationRun_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE TABLE [Space_AiUsageRecord] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [ProviderCode] nvarchar(64) NOT NULL,
        [ProviderModel] nvarchar(128) NOT NULL,
        [ProviderRequestIdHash] char(64) NOT NULL,
        [InputUnits] bigint NOT NULL,
        [OutputUnits] bigint NOT NULL,
        [EstimatedCostMinor] bigint NOT NULL,
        [ActualCostMinor] bigint NULL,
        [Currency] char(3) NULL,
        [LatencyMs] bigint NOT NULL,
        [Outcome] smallint NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_AiUsageRecord] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_AiUsageRecord_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_AiUsageRecord_Cost] CHECK ([EstimatedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)),
        CONSTRAINT [CK_Space_AiUsageRecord_Latency] CHECK ([LatencyMs] >= 0),
        CONSTRAINT [CK_Space_AiUsageRecord_Units] CHECK ([InputUnits] >= 0 AND [OutputUnits] >= 0),
        CONSTRAINT [FK_Space_AiUsageRecord_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE TABLE [Space_GenerationProposal] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [BaseContentRevision] bigint NOT NULL,
        [SourceHash] char(64) NOT NULL,
        [SourceKey] nvarchar(256) NOT NULL,
        [ProposalType] nvarchar(64) NOT NULL,
        [SuggestedGeometryJson] nvarchar(max) NOT NULL,
        [SuggestedAttributesJson] nvarchar(max) NOT NULL,
        [SuggestedRelationsJson] nvarchar(max) NOT NULL,
        [SourceRefsJson] nvarchar(max) NOT NULL,
        [EvidenceJson] nvarchar(max) NOT NULL,
        [FieldProvenanceJson] nvarchar(max) NOT NULL,
        [ConfidenceScore] decimal(6,5) NOT NULL,
        [ConfidenceBand] smallint NOT NULL,
        [Status] smallint NOT NULL,
        [HasBlockingIssue] bit NOT NULL,
        [HumanPatchJson] nvarchar(max) NULL,
        [LockedFieldsJson] nvarchar(max) NULL,
        [AppliedLogicalId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_GenerationProposal] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_GenerationProposal_Tenant_Run_Id] UNIQUE ([TenantId], [RunId], [Id]),
        CONSTRAINT [AK_Space_GenerationProposal_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_GenerationProposal_Confidence] CHECK ([ConfidenceScore] >= 0 AND [ConfidenceScore] <= 1),
        CONSTRAINT [FK_Space_GenerationProposal_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationProposal_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE TABLE [Space_ProposalDecision] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [ProposalId] uniqueidentifier NOT NULL,
        [DecisionType] smallint NOT NULL,
        [BeforeJson] nvarchar(max) NOT NULL,
        [AfterJson] nvarchar(max) NULL,
        [LockedFieldsJson] nvarchar(max) NULL,
        [ReasonCode] nvarchar(64) NULL,
        [Comment] nvarchar(512) NULL,
        [DecisionBatchId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ProposalDecision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ProposalDecision_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ProposalDecision_Proposal_Tenant_Run] FOREIGN KEY ([TenantId], [RunId], [ProposalId]) REFERENCES [Space_GenerationProposal] ([TenantId], [RunId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ProposalDecision_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_AiUsage_Tenant_Run_Recorded] ON [Space_AiUsageRecord] ([TenantId], [RunId], [RecordedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AiUsage_Tenant_ProviderRequest] ON [Space_AiUsageRecord] ([TenantId], [ProviderRequestIdHash]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_Proposal_Tenant_Run_Status_Band_Type] ON [Space_GenerationProposal] ([TenantId], [RunId], [Status], [ConfidenceBand], [ProposalType], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationProposal_TenantId_ModelVersionId] ON [Space_GenerationProposal] ([TenantId], [ModelVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Proposal_Tenant_Run_Source_Type] ON [Space_GenerationProposal] ([TenantId], [RunId], [SourceKey], [ProposalType]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_GenerationRun_Tenant_Job] ON [Space_GenerationRun] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_GenerationRun_Tenant_Site_Status_Created] ON [Space_GenerationRun] ([TenantId], [SiteId], [Status], [CreatedAtUtc] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_GenerationRun_Tenant_Version_Current] ON [Space_GenerationRun] ([TenantId], [ModelVersionId], [IsCurrent]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationRun_TenantId_BasedOnRunId] ON [Space_GenerationRun] ([TenantId], [BasedOnRunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationRun_TenantId_ModelVersionId_SourceId] ON [Space_GenerationRun] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_GenerationRun_Tenant_Business_Current] ON [Space_GenerationRun] ([TenantId], [BusinessKeyHash]) WHERE [IsCurrent] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_ProposalDecision_Tenant_Batch] ON [Space_ProposalDecision] ([TenantId], [DecisionBatchId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    CREATE INDEX [IX_ProposalDecision_Tenant_Run_Proposal_Created] ON [Space_ProposalDecision] ([TenantId], [RunId], [ProposalId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730174231_SpaceE13S02GenerationDataModel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260730174231_SpaceE13S02GenerationDataModel', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE TABLE [Space_AiBudgetReservation] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [ProviderRequestKey] char(64) NOT NULL,
        [PeriodDay] date NOT NULL,
        [PeriodMonth] int NOT NULL,
        [ReservedCostMinor] bigint NOT NULL,
        [ActualCostMinor] bigint NULL,
        [Currency] char(3) NULL,
        [Status] smallint NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_AiBudgetReservation] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_AiBudgetReservation_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_AiBudgetReservation_Cost] CHECK ([ReservedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)),
        CONSTRAINT [CK_Space_AiBudgetReservation_Currency] CHECK ([ReservedCostMinor] = 0 OR [Currency] IS NOT NULL),
        CONSTRAINT [CK_Space_AiBudgetReservation_Period] CHECK ([PeriodMonth] = YEAR([PeriodDay]) * 100 + MONTH([PeriodDay])),
        CONSTRAINT [FK_Space_AiBudgetReservation_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE TABLE [Space_TenantAiWorkSlot] (
        [TenantId] uniqueidentifier NOT NULL,
        [SlotNo] int NOT NULL,
        [RunId] uniqueidentifier NULL,
        [LeaseOwner] nvarchar(128) NULL,
        [LeaseExpiresAtUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_TenantAiWorkSlot] PRIMARY KEY ([TenantId], [SlotNo]),
        CONSTRAINT [CK_Space_TenantAiWorkSlot_Lease] CHECK (([RunId] IS NULL AND [LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL) OR ([RunId] IS NOT NULL AND [LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_Space_TenantAiWorkSlot_SlotNo] CHECK ([SlotNo] >= 1 AND [SlotNo] <= 3),
        CONSTRAINT [FK_Space_TenantAiWorkSlot_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_AiBudgetReservation_Tenant_Day] ON [Space_AiBudgetReservation] ([TenantId], [Currency], [PeriodDay], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_AiBudgetReservation_Tenant_Month] ON [Space_AiBudgetReservation] ([TenantId], [Currency], [PeriodMonth], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_Space_AiBudgetReservation_TenantId_RunId] ON [Space_AiBudgetReservation] ([TenantId], [RunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_AiBudgetReservation_Tenant_Request] ON [Space_AiBudgetReservation] ([TenantId], [ProviderRequestKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_TenantAiWorkSlot_Tenant_Expiry] ON [Space_TenantAiWorkSlot] ([TenantId], [LeaseExpiresAtUtc], [SlotNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TenantAiWorkSlot_Tenant_Run] ON [Space_TenantAiWorkSlot] ([TenantId], [RunId]) WHERE [RunId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260730183757_SpaceE13S12AiCapacityLedger', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    ALTER TABLE [Space_RackLevelRevision] DROP CONSTRAINT [CK_Space_RackLevelRevision_Dimensions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    ALTER TABLE [Space_RackLevelRevision] ADD [BeamHeight] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_RackLevelRevision] ADD CONSTRAINT [CK_Space_RackLevelRevision_Dimensions] CHECK ([LevelNo] > 0 AND [BottomZ] >= 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND [BeamHeight] >= 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260731001924_SpaceE05S02RackLevelSpecification', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM [Space_ElementRevision]
        WHERE [ModelAssetId] IS NOT NULL
    )
    BEGIN
        THROW 51000,
            'E05-S04 requires all legacy ModelAssetId values to be audited and cleared before asset-version enforcement.',
            1;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    ALTER TABLE [Space_ElementRevision] ADD [ModelAssetOwnerTenantId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    ALTER TABLE [Space_ElementRevision] ADD [ModelAssetScope] smallint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    CREATE TABLE [Space_Asset] (
        [Id] uniqueidentifier NOT NULL,
        [Scope] smallint NOT NULL,
        [OwnerTenantId] uniqueidentifier NOT NULL,
        [AssetCode] nvarchar(100) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Category] nvarchar(100) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Status] smallint NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_Asset] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_Asset_Scope_Owner_Id] UNIQUE ([Scope], [OwnerTenantId], [Id]),
        CONSTRAINT [CK_Space_Asset_ScopeOwner] CHECK (([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000'))
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    CREATE TABLE [Space_AssetVersion] (
        [Id] uniqueidentifier NOT NULL,
        [Scope] smallint NOT NULL,
        [OwnerTenantId] uniqueidentifier NOT NULL,
        [AssetId] uniqueidentifier NOT NULL,
        [VersionNo] bigint NOT NULL,
        [Format] smallint NOT NULL,
        [ParameterSchemaJson] nvarchar(max) NOT NULL,
        [PreviewRef] nvarchar(500) NULL,
        [RenderArtifactRef] nvarchar(500) NULL,
        [ContentHash] char(64) NOT NULL,
        [Status] smallint NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_AssetVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_AssetVersion_Scope_Owner_Id] UNIQUE ([Scope], [OwnerTenantId], [Id]),
        CONSTRAINT [CK_Space_AssetVersion_ScopeOwner] CHECK (([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')),
        CONSTRAINT [CK_Space_AssetVersion_VersionNo] CHECK ([VersionNo] > 0),
        CONSTRAINT [FK_Space_AssetVersion_Asset_Scope_Owner_Asset] FOREIGN KEY ([Scope], [OwnerTenantId], [AssetId]) REFERENCES [Space_Asset] ([Scope], [OwnerTenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    CREATE INDEX [IX_Space_ElementRevision_ModelAssetScope_ModelAssetOwnerTenantId_ModelAssetId] ON [Space_ElementRevision] ([ModelAssetScope], [ModelAssetOwnerTenantId], [ModelAssetId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ElementRevision] ADD CONSTRAINT [CK_Space_ElementRevision_ModelAssetScope] CHECK (([ModelAssetId] IS NULL AND [ModelAssetScope] IS NULL AND [ModelAssetOwnerTenantId] IS NULL) OR ([ModelAssetId] IS NOT NULL AND [ModelAssetScope] IS NOT NULL AND [ModelAssetOwnerTenantId] IS NOT NULL AND (([ModelAssetScope] = 0 AND [ModelAssetOwnerTenantId] = ''00000000-0000-0000-0000-000000000000'') OR ([ModelAssetScope] = 1 AND [ModelAssetOwnerTenantId] = [TenantId]))))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    CREATE INDEX [IX_Space_Asset_Scope_Owner_Category] ON [Space_Asset] ([Scope], [OwnerTenantId], [Category]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_Asset_Scope_Owner_Code_Active] ON [Space_Asset] ([Scope], [OwnerTenantId], [AssetCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_AssetVersion_Scope_Owner_Asset_VersionNo] ON [Space_AssetVersion] ([Scope], [OwnerTenantId], [AssetId], [VersionNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    ALTER TABLE [Space_ElementRevision] ADD CONSTRAINT [FK_Space_ElementRevision_AssetVersion_Scope_Owner_Version] FOREIGN KEY ([ModelAssetScope], [ModelAssetOwnerTenantId], [ModelAssetId]) REFERENCES [Space_AssetVersion] ([Scope], [OwnerTenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731010047_SpaceE05S04AssetLibrary'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260731010047_SpaceE05S04AssetLibrary', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    ALTER TABLE [Space_FloorRevision] ADD [UnderlayCalibrationId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE TABLE [Space_UnderlayCalibration] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [PageNumber] int NOT NULL,
        [PixelWidth] int NOT NULL,
        [PixelHeight] int NOT NULL,
        [Point1PixelX] decimal(18,6) NOT NULL,
        [Point1PixelY] decimal(18,6) NOT NULL,
        [Point1WorldX] int NOT NULL,
        [Point1WorldY] int NOT NULL,
        [Point2PixelX] decimal(18,6) NOT NULL,
        [Point2PixelY] decimal(18,6) NOT NULL,
        [Point2WorldX] int NOT NULL,
        [Point2WorldY] int NOT NULL,
        [ValidationPixelX] decimal(18,6) NOT NULL,
        [ValidationPixelY] decimal(18,6) NOT NULL,
        [ValidationWorldX] int NOT NULL,
        [ValidationWorldY] int NOT NULL,
        [MillimetersPerPixel] decimal(18,8) NOT NULL,
        [OffsetX] int NOT NULL,
        [OffsetY] int NOT NULL,
        [RotationZ] decimal(9,4) NOT NULL,
        [ValidationErrorMillimeters] decimal(18,4) NOT NULL,
        [ErrorThresholdMillimeters] decimal(18,4) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_UnderlayCalibration] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_UnderlayCalibration_Tenant_Version_Floor_Source_Id] UNIQUE ([TenantId], [ModelVersionId], [FloorLogicalId], [SourceId], [Id]),
        CONSTRAINT [FK_Space_UnderlayCalibration_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_TenantId_ModelVersionId_LogicalId_UnderlaySourceId_UnderlayCalibrationId] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId], [UnderlaySourceId], [UnderlayCalibrationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE INDEX [IX_Space_UnderlayCalibration_Version_Floor_Created] ON [Space_UnderlayCalibration] ([TenantId], [ModelVersionId], [FloorLogicalId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE INDEX [IX_Space_UnderlayCalibration_Version_Source] ON [Space_UnderlayCalibration] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    ALTER TABLE [Space_FloorRevision] ADD CONSTRAINT [FK_Space_FloorRevision_UnderlayCalibration_Tenant_Version_Floor_Source] FOREIGN KEY ([TenantId], [ModelVersionId], [LogicalId], [UnderlaySourceId], [UnderlayCalibrationId]) REFERENCES [Space_UnderlayCalibration] ([TenantId], [ModelVersionId], [FloorLogicalId], [SourceId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260731032506_SpaceE04S02UnderlayCalibration', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731035237_SpaceE04S03ElementCommands'
)
BEGIN
    CREATE TABLE [Space_ElementCommandBatch] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ClientInstanceId] uniqueidentifier NOT NULL,
        [ExpectedFloorRevision] bigint NOT NULL,
        [ResultFloorRevision] bigint NULL,
        [ResultVersionContentRevision] bigint NULL,
        [RequestHash] char(64) NOT NULL,
        [ResponseJson] nvarchar(max) NULL,
        [AppliedAtUtc] datetime2 NOT NULL,
        [AppliedBy] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ElementCommandBatch] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ElementCommandBatch_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ElementCommandBatch_Result] CHECK (([ResultFloorRevision] IS NULL AND [ResultVersionContentRevision] IS NULL AND [ResponseJson] IS NULL) OR ([ResultFloorRevision] IS NOT NULL AND [ResultVersionContentRevision] IS NOT NULL AND [ResponseJson] IS NOT NULL)),
        CONSTRAINT [FK_Space_ElementCommandBatch_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ElementCommandBatch_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731035237_SpaceE04S03ElementCommands'
)
BEGIN
    CREATE TABLE [Space_ElementCommandRecord] (
        [Id] uniqueidentifier NOT NULL,
        [CommandBatchId] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [SequenceNo] int NOT NULL,
        [CommandType] nvarchar(100) NOT NULL,
        [TargetLogicalId] uniqueidentifier NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [BeforeJson] nvarchar(max) NOT NULL,
        [AfterJson] nvarchar(max) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ElementCommandRecord] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_ElementCommandRecord_Batch_Tenant] FOREIGN KEY ([TenantId], [CommandBatchId]) REFERENCES [Space_ElementCommandBatch] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731035237_SpaceE04S03ElementCommands'
)
BEGIN
    CREATE INDEX [IX_Space_ElementCommandBatch_Floor_Applied] ON [Space_ElementCommandBatch] ([TenantId], [ModelVersionId], [FloorLogicalId], [AppliedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731035237_SpaceE04S03ElementCommands'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_ElementCommandRecord_Batch_Sequence] ON [Space_ElementCommandRecord] ([TenantId], [CommandBatchId], [SequenceNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731035237_SpaceE04S03ElementCommands'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260731035237_SpaceE04S03ElementCommands', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731090933_SpaceE07S05WmsAdoption'
)
BEGIN
    CREATE TABLE [Space_WmsAdoption] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [DataSource] nvarchar(100) NOT NULL,
        [DataSourceKind] nvarchar(20) NOT NULL,
        [WmsLogicalId] uniqueidentifier NOT NULL,
        [ExternalLocationId] nvarchar(200) NULL,
        [WmsLocationCode] nvarchar(200) NOT NULL,
        [WmsIsActive] bit NOT NULL,
        [ExternalVersion] nvarchar(100) NOT NULL,
        [WmsStateHash] char(64) NOT NULL,
        [LastObservedAtUtc] datetime2 NOT NULL,
        [Status] smallint NOT NULL,
        [ModelVersionId] uniqueidentifier NULL,
        [LocationLogicalId] uniqueidentifier NULL,
        [BoundLocationCode] nvarchar(200) NULL,
        [BoundAtUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_WmsAdoption] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_WmsAdoption_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_WmsAdoption_ModelVersion_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731090933_SpaceE07S05WmsAdoption'
)
BEGIN
    CREATE INDEX [IX_Space_WmsAdoption_Tenant_Site_Adapter_Status_Code] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [Status], [WmsLocationCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731090933_SpaceE07S05WmsAdoption'
)
BEGIN
    CREATE INDEX [IX_Space_WmsAdoption_TenantId_ModelVersionId] ON [Space_WmsAdoption] ([TenantId], [ModelVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731090933_SpaceE07S05WmsAdoption'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_WmsAdoption_Tenant_Site_Adapter_External] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [ExternalLocationId]) WHERE [ExternalLocationId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731090933_SpaceE07S05WmsAdoption'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_WmsAdoption_Tenant_Site_Adapter_Location] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [LocationLogicalId]) WHERE [LocationLogicalId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731090933_SpaceE07S05WmsAdoption'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_WmsAdoption_Tenant_Site_Adapter_WmsLogical] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [WmsLogicalId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731090933_SpaceE07S05WmsAdoption'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260731090933_SpaceE07S05WmsAdoption', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE TABLE [Space_ExternalOrganization] (
        [Id] uniqueidentifier NOT NULL,
        [Type] smallint NOT NULL,
        [BusinessPartnerType] varchar(50) NULL,
        [BusinessPartnerId] uniqueidentifier NULL,
        [Code] nvarchar(50) NOT NULL,
        [NormalizedCode] varchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Status] smallint NOT NULL,
        [SecurityStamp] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExternalOrganization] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalOrganization_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExternalOrganization_BusinessPartner] CHECK (([BusinessPartnerType] IS NULL AND [BusinessPartnerId] IS NULL) OR ([BusinessPartnerType] IS NOT NULL AND [BusinessPartnerId] IS NOT NULL)),
        CONSTRAINT [CK_Space_ExternalOrganization_Status] CHECK ([Status] >= 0 AND [Status] <= 2),
        CONSTRAINT [CK_Space_ExternalOrganization_Type] CHECK ([Type] >= 0 AND [Type] <= 2)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE TABLE [Space_ExternalMembership] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Role] smallint NOT NULL,
        [ValidFromUtc] datetime2 NOT NULL,
        [ValidToUtc] datetime2 NULL,
        [Status] smallint NOT NULL,
        [InvitedBy] uniqueidentifier NULL,
        [AcceptedAtUtc] datetime2 NULL,
        [SecurityStamp] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExternalMembership] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalMembership_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExternalMembership_Role] CHECK ([Role] >= 0 AND [Role] <= 2),
        CONSTRAINT [CK_Space_ExternalMembership_Status] CHECK ([Status] >= 0 AND [Status] <= 3),
        CONSTRAINT [CK_Space_ExternalMembership_Validity] CHECK ([ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]),
        CONSTRAINT [FK_Space_ExternalMembership_Organization_Tenant] FOREIGN KEY ([TenantId], [OrganizationId]) REFERENCES [Space_ExternalOrganization] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalMembership_Tenant_User_Status_Validity] ON [Space_ExternalMembership] ([TenantId], [UserId], [Status], [ValidFromUtc], [ValidToUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalMembership_Tenant_Organization_User_Current] ON [Space_ExternalMembership] ([TenantId], [OrganizationId], [UserId]) WHERE [Status] <> 3 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalOrganization_Tenant_Status_Name] ON [Space_ExternalOrganization] ([TenantId], [Status], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalOrganization_Tenant_Type_Code] ON [Space_ExternalOrganization] ([TenantId], [Type], [NormalizedCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalOrganization_Tenant_Type_Partner] ON [Space_ExternalOrganization] ([TenantId], [Type], [BusinessPartnerType], [BusinessPartnerId]) WHERE [BusinessPartnerId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260801172135_SpaceE09S01ExternalOrganizations', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrant] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [FieldPolicyId] uniqueidentifier NULL,
        [CanExport] bit NOT NULL,
        [ValidFromUtc] datetime2 NOT NULL,
        [ValidToUtc] datetime2 NULL,
        [Status] smallint NOT NULL,
        [GrantVersion] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExternalGrant] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrant_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExternalGrant_Status] CHECK ([Status] >= 0 AND [Status] <= 2),
        CONSTRAINT [CK_Space_ExternalGrant_Validity] CHECK ([ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]),
        CONSTRAINT [CK_Space_ExternalGrant_Version] CHECK ([GrantVersion] > 0),
        CONSTRAINT [FK_Space_ExternalGrant_Organization_Tenant] FOREIGN KEY ([TenantId], [OrganizationId]) REFERENCES [Space_ExternalOrganization] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantFloor] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantFloor] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantFloor_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantFloor_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantObject] (
        [Id] uniqueidentifier NOT NULL,
        [BusinessObjectType] nvarchar(50) NOT NULL,
        [NormalizedBusinessObjectType] varchar(50) NOT NULL,
        [BusinessObjectId] nvarchar(200) NOT NULL,
        [NormalizedBusinessObjectId] varchar(200) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantObject] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantObject_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantObject_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantOwner] (
        [Id] uniqueidentifier NOT NULL,
        [OwnerId] nvarchar(100) NOT NULL,
        [NormalizedOwnerId] varchar(100) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantOwner] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantOwner_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantOwner_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantZone] (
        [Id] uniqueidentifier NOT NULL,
        [ZoneLogicalId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantZone] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantZone_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantZone_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalGrant_Organization_Site_Status] ON [Space_ExternalGrant] ([TenantId], [OrganizationId], [SiteId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalGrant_Organization_Status_Validity] ON [Space_ExternalGrant] ([TenantId], [OrganizationId], [Status], [ValidFromUtc], [ValidToUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantFloor_Current] ON [Space_ExternalGrantFloor] ([TenantId], [GrantId], [FloorLogicalId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantObject_Current] ON [Space_ExternalGrantObject] ([TenantId], [GrantId], [NormalizedBusinessObjectType], [NormalizedBusinessObjectId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantOwner_Current] ON [Space_ExternalGrantOwner] ([TenantId], [GrantId], [NormalizedOwnerId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantZone_Current] ON [Space_ExternalGrantZone] ([TenantId], [GrantId], [ZoneLogicalId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260801182535_SpaceE09S02ExternalGrants', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801191107_SpaceE09S03ExternalPortal'
)
BEGIN
    CREATE TABLE [Space_FieldPolicy] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [NormalizedName] nvarchar(200) NOT NULL,
        [AudienceType] smallint NOT NULL,
        [CanExport] bit NOT NULL,
        [Status] smallint NOT NULL,
        [PolicyVersion] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_FieldPolicy] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_FieldPolicy_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_FieldPolicy_AudienceType] CHECK ([AudienceType] >= 0 AND [AudienceType] <= 2),
        CONSTRAINT [CK_Space_FieldPolicy_Status] CHECK ([Status] >= 0 AND [Status] <= 1),
        CONSTRAINT [CK_Space_FieldPolicy_Version] CHECK ([PolicyVersion] > 0)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801191107_SpaceE09S03ExternalPortal'
)
BEGIN
    CREATE TABLE [Space_FieldPolicyField] (
        [Id] uniqueidentifier NOT NULL,
        [PolicyId] uniqueidentifier NOT NULL,
        [ResourceType] smallint NOT NULL,
        [FieldName] nvarchar(100) NOT NULL,
        [NormalizedFieldName] nvarchar(100) NOT NULL,
        [MaskingRule] smallint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_FieldPolicyField] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_FieldPolicyField_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_FieldPolicyField_MaskingRule] CHECK ([MaskingRule] >= 0 AND [MaskingRule] <= 3),
        CONSTRAINT [CK_Space_FieldPolicyField_ResourceType] CHECK ([ResourceType] >= 0 AND [ResourceType] <= 2),
        CONSTRAINT [FK_Space_FieldPolicyField_Policy_Tenant] FOREIGN KEY ([TenantId], [PolicyId]) REFERENCES [Space_FieldPolicy] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801191107_SpaceE09S03ExternalPortal'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalGrant_TenantId_FieldPolicyId] ON [Space_ExternalGrant] ([TenantId], [FieldPolicyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801191107_SpaceE09S03ExternalPortal'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_FieldPolicy_CurrentName] ON [Space_FieldPolicy] ([TenantId], [AudienceType], [NormalizedName]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801191107_SpaceE09S03ExternalPortal'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_FieldPolicyField_Current] ON [Space_FieldPolicyField] ([TenantId], [PolicyId], [ResourceType], [NormalizedFieldName]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801191107_SpaceE09S03ExternalPortal'
)
BEGIN
    ALTER TABLE [Space_ExternalGrant] ADD CONSTRAINT [FK_Space_ExternalGrant_FieldPolicy_Tenant] FOREIGN KEY ([TenantId], [FieldPolicyId]) REFERENCES [Space_FieldPolicy] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801191107_SpaceE09S03ExternalPortal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260801191107_SpaceE09S03ExternalPortal', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802103430_SpaceE03S02ExcelMappingProfiles'
)
BEGIN
    CREATE TABLE [Space_ExcelMappingProfile] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [NormalizedName] nvarchar(200) NOT NULL,
        [CurrentVersion] int NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExcelMappingProfile] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExcelMappingProfile_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExcelMappingProfile_CurrentVersion] CHECK ([CurrentVersion] > 0)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802103430_SpaceE03S02ExcelMappingProfiles'
)
BEGIN
    CREATE TABLE [Space_ExcelMappingProfileVersion] (
        [Id] uniqueidentifier NOT NULL,
        [ProfileId] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [DefinitionJson] nvarchar(max) NOT NULL,
        [DefinitionHash] char(64) NOT NULL,
        [BasedOnProfileId] uniqueidentifier NULL,
        [BasedOnVersion] int NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExcelMappingProfileVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExcelMappingProfileVersion_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExcelMappingProfileVersion_Base] CHECK (([BasedOnProfileId] IS NULL AND [BasedOnVersion] IS NULL) OR ([BasedOnProfileId] IS NOT NULL AND [BasedOnVersion] > 0)),
        CONSTRAINT [CK_Space_ExcelMappingProfileVersion_Version] CHECK ([Version] > 0),
        CONSTRAINT [FK_Space_ExcelMappingProfileVersion_Profile_Tenant] FOREIGN KEY ([TenantId], [ProfileId]) REFERENCES [Space_ExcelMappingProfile] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802103430_SpaceE03S02ExcelMappingProfiles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExcelMappingProfile_CurrentName] ON [Space_ExcelMappingProfile] ([TenantId], [NormalizedName]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802103430_SpaceE03S02ExcelMappingProfiles'
)
BEGIN
    CREATE INDEX [IX_Space_ExcelMappingProfileVersion_DefinitionHash] ON [Space_ExcelMappingProfileVersion] ([TenantId], [DefinitionHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802103430_SpaceE03S02ExcelMappingProfiles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExcelMappingProfileVersion_Profile_Version] ON [Space_ExcelMappingProfileVersion] ([TenantId], [ProfileId], [Version]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802103430_SpaceE03S02ExcelMappingProfiles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802103430_SpaceE03S02ExcelMappingProfiles', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802115537_SpaceE13S16AiPolicyManagement'
)
BEGIN
    CREATE TABLE [Space_AiTenantPolicy] (
        [Id] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [DataPolicy] varchar(32) NOT NULL,
        [AllowedSiteIdsJson] nvarchar(max) NOT NULL,
        [AllowedProviderAliasesJson] nvarchar(max) NOT NULL,
        [MaxConcurrentRuns] int NOT NULL,
        [ExternalProviderEnabled] bit NOT NULL,
        [DailyBudgetMinor] bigint NULL,
        [MonthlyBudgetMinor] bigint NULL,
        [Currency] char(3) NULL,
        [IsActive] bit NOT NULL,
        [UpdatedBy] uniqueidentifier NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_AiTenantPolicy] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_AiTenantPolicy_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_AiTenantPolicy_Budget] CHECK (([DailyBudgetMinor] IS NULL OR [DailyBudgetMinor] >= 0) AND ([MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= 0) AND ([DailyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= [DailyBudgetMinor])),
        CONSTRAINT [CK_Space_AiTenantPolicy_Concurrency] CHECK ([MaxConcurrentRuns] >= 1 AND [MaxConcurrentRuns] <= 3),
        CONSTRAINT [CK_Space_AiTenantPolicy_Currency] CHECK (([DailyBudgetMinor] IS NULL AND [MonthlyBudgetMinor] IS NULL) OR [Currency] IS NOT NULL),
        CONSTRAINT [CK_Space_AiTenantPolicy_Version] CHECK ([Version] >= 1)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802115537_SpaceE13S16AiPolicyManagement'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AiTenantPolicy_Tenant_Active] ON [Space_AiTenantPolicy] ([TenantId]) WHERE [IsActive] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802115537_SpaceE13S16AiPolicyManagement'
)
BEGIN
    CREATE UNIQUE INDEX [UX_AiTenantPolicy_Tenant_Version] ON [Space_AiTenantPolicy] ([TenantId], [Version]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802115537_SpaceE13S16AiPolicyManagement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802115537_SpaceE13S16AiPolicyManagement', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802125928_SpaceE10S01PersonnelEvents'
)
BEGIN
    CREATE TABLE [Space_PersonnelEvent] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [SourceId] nvarchar(100) NOT NULL,
        [SourceKind] smallint NOT NULL,
        [SourceEventId] nvarchar(200) NOT NULL,
        [PersonExternalId] nvarchar(200) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [EventKind] smallint NOT NULL,
        [WorkState] smallint NULL,
        [FloorLogicalId] uniqueidentifier NULL,
        [LocationLogicalId] uniqueidentifier NULL,
        [XMillimeters] decimal(18,3) NULL,
        [YMillimeters] decimal(18,3) NULL,
        [ZMillimeters] decimal(18,3) NULL,
        [AccuracyMillimeters] decimal(18,3) NULL,
        [SourceSequence] bigint NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [ReceivedAtUtc] datetime2 NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PersonnelEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PersonnelEvent_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_PersonnelEvent_Accuracy] CHECK ([AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)),
        CONSTRAINT [CK_Space_PersonnelEvent_Kind] CHECK ([EventKind] IN (0, 1)),
        CONSTRAINT [CK_Space_PersonnelEvent_Shape] CHECK (([EventKind] = 0 AND [WorkState] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [WorkState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL)),
        CONSTRAINT [CK_Space_PersonnelEvent_SourceKind] CHECK ([SourceKind] IN (0, 1)),
        CONSTRAINT [CK_Space_PersonnelEvent_SourceSequence] CHECK ([SourceSequence] IS NULL OR [SourceSequence] >= 0),
        CONSTRAINT [CK_Space_PersonnelEvent_WorkState] CHECK ([WorkState] IS NULL OR [WorkState] BETWEEN 0 AND 4)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802125928_SpaceE10S01PersonnelEvents'
)
BEGIN
    CREATE TABLE [Space_PersonnelState] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [SourceId] nvarchar(100) NOT NULL,
        [SourceKind] smallint NOT NULL,
        [PersonExternalId] nvarchar(200) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [FloorLogicalId] uniqueidentifier NULL,
        [LocationLogicalId] uniqueidentifier NULL,
        [XMillimeters] decimal(18,3) NULL,
        [YMillimeters] decimal(18,3) NULL,
        [ZMillimeters] decimal(18,3) NULL,
        [AccuracyMillimeters] decimal(18,3) NULL,
        [PositionOccurredAtUtc] datetime2 NULL,
        [PositionReceivedAtUtc] datetime2 NULL,
        [PositionSourceSequence] bigint NULL,
        [PositionSourceEventId] nvarchar(200) NULL,
        [PositionEventId] uniqueidentifier NULL,
        [WorkState] smallint NOT NULL,
        [WorkStateOccurredAtUtc] datetime2 NULL,
        [WorkStateReceivedAtUtc] datetime2 NULL,
        [WorkStateSourceSequence] bigint NULL,
        [WorkStateSourceEventId] nvarchar(200) NULL,
        [WorkStateEventId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PersonnelState] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PersonnelState_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_PersonnelState_SourceKind] CHECK ([SourceKind] IN (0, 1)),
        CONSTRAINT [CK_Space_PersonnelState_WorkState] CHECK ([WorkState] BETWEEN 0 AND 4)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802125928_SpaceE10S01PersonnelEvents'
)
BEGIN
    CREATE INDEX [IX_Space_PersonnelEvent_Tenant_Site_Source_Person_Time] ON [Space_PersonnelEvent] ([TenantId], [SiteId], [SourceId], [PersonExternalId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802125928_SpaceE10S01PersonnelEvents'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PersonnelEvent_Tenant_Site_Source_Event] ON [Space_PersonnelEvent] ([TenantId], [SiteId], [SourceId], [SourceEventId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802125928_SpaceE10S01PersonnelEvents'
)
BEGIN
    CREATE INDEX [IX_Space_PersonnelState_Tenant_Site_WorkState_Time] ON [Space_PersonnelState] ([TenantId], [SiteId], [WorkState], [WorkStateOccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802125928_SpaceE10S01PersonnelEvents'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PersonnelState_Tenant_Site_Source_Person] ON [Space_PersonnelState] ([TenantId], [SiteId], [SourceId], [PersonExternalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802125928_SpaceE10S01PersonnelEvents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802125928_SpaceE10S01PersonnelEvents', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE TABLE [Space_DeviceMapping] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [SourceId] nvarchar(100) NOT NULL,
        [SourceKind] smallint NOT NULL,
        [DeviceExternalId] nvarchar(200) NOT NULL,
        [DeviceKind] smallint NOT NULL,
        [ElementLogicalId] uniqueidentifier NOT NULL,
        [ElementType] nvarchar(50) NOT NULL,
        [ValidatedModelVersionId] uniqueidentifier NOT NULL,
        [ValidatedFloorLogicalId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_DeviceMapping] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_DeviceMapping_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_DeviceMapping_DeviceKind] CHECK ([DeviceKind] BETWEEN 0 AND 7),
        CONSTRAINT [CK_Space_DeviceMapping_SourceKind] CHECK ([SourceKind] IN (0, 1)),
        CONSTRAINT [FK_Space_DeviceMapping_Element_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ValidatedModelVersionId], [ElementLogicalId]) REFERENCES [Space_ElementRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_DeviceMapping_ModelVersion_Tenant] FOREIGN KEY ([TenantId], [ValidatedModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE TABLE [Space_DeviceEvent] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [SourceId] nvarchar(100) NOT NULL,
        [SourceKind] smallint NOT NULL,
        [SourceEventId] nvarchar(200) NOT NULL,
        [DeviceMappingId] uniqueidentifier NOT NULL,
        [DeviceExternalId] nvarchar(200) NOT NULL,
        [DeviceKind] smallint NOT NULL,
        [ElementLogicalId] uniqueidentifier NOT NULL,
        [EventKind] smallint NOT NULL,
        [OperatingState] smallint NULL,
        [FloorLogicalId] uniqueidentifier NULL,
        [LocationLogicalId] uniqueidentifier NULL,
        [XMillimeters] decimal(18,3) NULL,
        [YMillimeters] decimal(18,3) NULL,
        [ZMillimeters] decimal(18,3) NULL,
        [AccuracyMillimeters] decimal(18,3) NULL,
        [AlarmExternalId] nvarchar(200) NULL,
        [AlarmCode] nvarchar(100) NULL,
        [AlarmSeverity] smallint NULL,
        [AlarmMessage] nvarchar(500) NULL,
        [SourceSequence] bigint NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [ReceivedAtUtc] datetime2 NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_DeviceEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_DeviceEvent_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_DeviceEvent_Accuracy] CHECK ([AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)),
        CONSTRAINT [CK_Space_DeviceEvent_AlarmSeverity] CHECK ([AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2),
        CONSTRAINT [CK_Space_DeviceEvent_CoordinateTriple] CHECK (([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)),
        CONSTRAINT [CK_Space_DeviceEvent_DeviceKind] CHECK ([DeviceKind] BETWEEN 0 AND 7),
        CONSTRAINT [CK_Space_DeviceEvent_Kind] CHECK ([EventKind] BETWEEN 0 AND 3),
        CONSTRAINT [CK_Space_DeviceEvent_OperatingState] CHECK ([OperatingState] IS NULL OR [OperatingState] BETWEEN 0 AND 6),
        CONSTRAINT [CK_Space_DeviceEvent_Shape] CHECK (([EventKind] = 0 AND [OperatingState] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [OperatingState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL) OR ([EventKind] = 2 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL) OR ([EventKind] = 3 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL)),
        CONSTRAINT [CK_Space_DeviceEvent_SourceKind] CHECK ([SourceKind] IN (0, 1)),
        CONSTRAINT [CK_Space_DeviceEvent_SourceSequence] CHECK ([SourceSequence] IS NULL OR [SourceSequence] >= 0),
        CONSTRAINT [FK_Space_DeviceEvent_Mapping_Tenant] FOREIGN KEY ([TenantId], [DeviceMappingId]) REFERENCES [Space_DeviceMapping] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_DeviceEvent_Tenant_Site_Alarm_Time] ON [Space_DeviceEvent] ([TenantId], [SiteId], [AlarmExternalId], [OccurredAtUtc]) WHERE [AlarmExternalId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE INDEX [IX_Space_DeviceEvent_Tenant_Site_Source_Device_Time] ON [Space_DeviceEvent] ([TenantId], [SiteId], [SourceId], [DeviceExternalId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE INDEX [IX_Space_DeviceEvent_TenantId_DeviceMappingId] ON [Space_DeviceEvent] ([TenantId], [DeviceMappingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_DeviceEvent_Tenant_Site_Source_Event] ON [Space_DeviceEvent] ([TenantId], [SiteId], [SourceId], [SourceEventId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE INDEX [IX_Space_DeviceMapping_TenantId_ValidatedModelVersionId_ElementLogicalId] ON [Space_DeviceMapping] ([TenantId], [ValidatedModelVersionId], [ElementLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_DeviceMapping_Tenant_Site_Source_Device] ON [Space_DeviceMapping] ([TenantId], [SiteId], [SourceId], [DeviceExternalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_DeviceMapping_Tenant_Site_Source_Element] ON [Space_DeviceMapping] ([TenantId], [SiteId], [SourceId], [ElementLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802141148_SpaceE10S03DeviceEvents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802141148_SpaceE10S03DeviceEvents', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE TABLE [Space_DeviceAlarmState] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [SourceId] nvarchar(100) NOT NULL,
        [SourceKind] smallint NOT NULL,
        [DeviceExternalId] nvarchar(200) NOT NULL,
        [DeviceMappingId] uniqueidentifier NOT NULL,
        [AlarmExternalId] nvarchar(200) NOT NULL,
        [AlarmCode] nvarchar(100) NULL,
        [AlarmSeverity] smallint NULL,
        [AlarmMessage] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [ReceivedAtUtc] datetime2 NOT NULL,
        [SourceSequence] bigint NULL,
        [SourceEventId] nvarchar(200) NOT NULL,
        [EventId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_DeviceAlarmState] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_DeviceAlarmState_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_DeviceAlarmState_ActiveShape] CHECK ([IsActive] = 0 OR ([AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL)),
        CONSTRAINT [CK_Space_DeviceAlarmState_Severity] CHECK ([AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2),
        CONSTRAINT [CK_Space_DeviceAlarmState_SourceKind] CHECK ([SourceKind] IN (0, 1)),
        CONSTRAINT [CK_Space_DeviceAlarmState_SourceSequence] CHECK ([SourceSequence] IS NULL OR [SourceSequence] >= 0),
        CONSTRAINT [FK_Space_DeviceAlarmState_Mapping_Tenant] FOREIGN KEY ([TenantId], [DeviceMappingId]) REFERENCES [Space_DeviceMapping] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE TABLE [Space_DeviceState] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [SourceId] nvarchar(100) NOT NULL,
        [SourceKind] smallint NOT NULL,
        [DeviceExternalId] nvarchar(200) NOT NULL,
        [DeviceMappingId] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NULL,
        [LocationLogicalId] uniqueidentifier NULL,
        [XMillimeters] decimal(18,3) NULL,
        [YMillimeters] decimal(18,3) NULL,
        [ZMillimeters] decimal(18,3) NULL,
        [AccuracyMillimeters] decimal(18,3) NULL,
        [PositionOccurredAtUtc] datetime2 NULL,
        [PositionReceivedAtUtc] datetime2 NULL,
        [PositionSourceSequence] bigint NULL,
        [PositionSourceEventId] nvarchar(200) NULL,
        [PositionEventId] uniqueidentifier NULL,
        [OperatingState] smallint NOT NULL,
        [OperatingStateOccurredAtUtc] datetime2 NULL,
        [OperatingStateReceivedAtUtc] datetime2 NULL,
        [OperatingStateSourceSequence] bigint NULL,
        [OperatingStateSourceEventId] nvarchar(200) NULL,
        [OperatingStateEventId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_DeviceState] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_DeviceState_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_DeviceState_Accuracy] CHECK ([AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)),
        CONSTRAINT [CK_Space_DeviceState_CoordinateTriple] CHECK (([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)),
        CONSTRAINT [CK_Space_DeviceState_OperatingState] CHECK ([OperatingState] BETWEEN 0 AND 6),
        CONSTRAINT [CK_Space_DeviceState_SourceKind] CHECK ([SourceKind] IN (0, 1)),
        CONSTRAINT [FK_Space_DeviceState_Mapping_Tenant] FOREIGN KEY ([TenantId], [DeviceMappingId]) REFERENCES [Space_DeviceMapping] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE INDEX [IX_Space_DeviceAlarmState_Tenant_Site_Active_Severity_Time] ON [Space_DeviceAlarmState] ([TenantId], [SiteId], [IsActive], [AlarmSeverity], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE INDEX [IX_Space_DeviceAlarmState_TenantId_DeviceMappingId] ON [Space_DeviceAlarmState] ([TenantId], [DeviceMappingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_DeviceAlarmState_Tenant_Site_Source_Device_Alarm] ON [Space_DeviceAlarmState] ([TenantId], [SiteId], [SourceId], [DeviceExternalId], [AlarmExternalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE INDEX [IX_Space_DeviceState_Tenant_Site_State_Time] ON [Space_DeviceState] ([TenantId], [SiteId], [OperatingState], [OperatingStateOccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE INDEX [IX_Space_DeviceState_TenantId_DeviceMappingId] ON [Space_DeviceState] ([TenantId], [DeviceMappingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_DeviceState_Tenant_Site_Source_Device] ON [Space_DeviceState] ([TenantId], [SiteId], [SourceId], [DeviceExternalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802144027_SpaceE10S04DeviceRuntime'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802144027_SpaceE10S04DeviceRuntime', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802172258_SpaceE11S02PutawayRecommendations'
)
BEGIN
    CREATE TABLE [Space_PutawayRecommendation] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [PublishedVersionId] uniqueidentifier NOT NULL,
        [WarehouseCode] varchar(100) NOT NULL,
        [GeneratedAtUtc] datetime2 NOT NULL,
        [GeneratedBy] uniqueidentifier NOT NULL,
        [DefinitionVersion] varchar(50) NOT NULL,
        [Outcome] varchar(30) NOT NULL,
        [ExaminedLocationCount] int NOT NULL,
        [EligibleCandidateCount] int NOT NULL,
        [ReturnedCandidateCount] int NOT NULL,
        [IsTruncated] bit NOT NULL,
        [ExclusionSamplesTruncated] bit NOT NULL,
        [RequestJson] nvarchar(max) NOT NULL,
        [SourcesJson] nvarchar(max) NOT NULL,
        [ExclusionsJson] nvarchar(max) NOT NULL,
        [ExclusionSamplesJson] nvarchar(max) NOT NULL,
        [CandidatesJson] nvarchar(max) NOT NULL,
        [LimitationsJson] nvarchar(max) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PutawayRecommendation] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PutawayRecommendation_Tenant_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_PutawayRecommendation_Counts] CHECK ([ExaminedLocationCount] >= 0 AND [EligibleCandidateCount] >= 0 AND [ReturnedCandidateCount] >= 0 AND [EligibleCandidateCount] <= [ExaminedLocationCount] AND [ReturnedCandidateCount] <= [EligibleCandidateCount] AND (([IsTruncated] = 1 AND [ReturnedCandidateCount] < [EligibleCandidateCount]) OR ([IsTruncated] = 0 AND [ReturnedCandidateCount] = [EligibleCandidateCount]))),
        CONSTRAINT [CK_Space_PutawayRecommendation_Evidence] CHECK ([Outcome] IN ('NoCandidate', 'CandidatesGenerated') AND ISJSON([RequestJson]) = 1 AND ISJSON([SourcesJson]) = 1 AND ISJSON([ExclusionsJson]) = 1 AND ISJSON([ExclusionSamplesJson]) = 1 AND ISJSON([CandidatesJson]) = 1 AND ISJSON([LimitationsJson]) = 1),
        CONSTRAINT [CK_Space_PutawayRecommendation_Immutable] CHECK (LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PutawayRecommendation_Version_Tenant] FOREIGN KEY ([TenantId], [PublishedVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802172258_SpaceE11S02PutawayRecommendations'
)
BEGIN
    CREATE INDEX [IX_Space_PutawayRecommendation_Tenant_Site_Generated] ON [Space_PutawayRecommendation] ([TenantId], [SiteId], [GeneratedAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802172258_SpaceE11S02PutawayRecommendations'
)
BEGIN
    CREATE INDEX [IX_Space_PutawayRecommendation_TenantId_PublishedVersionId] ON [Space_PutawayRecommendation] ([TenantId], [PublishedVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802172258_SpaceE11S02PutawayRecommendations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802172258_SpaceE11S02PutawayRecommendations', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802180049_SpaceE11S03DispatchRecommendations'
)
BEGIN
    CREATE TABLE [Space_DispatchRecommendation] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [PublishedVersionId] uniqueidentifier NOT NULL,
        [WarehouseCode] varchar(100) NOT NULL,
        [GeneratedAtUtc] datetime2 NOT NULL,
        [GeneratedBy] uniqueidentifier NOT NULL,
        [DefinitionVersion] varchar(50) NOT NULL,
        [Outcome] varchar(30) NOT NULL,
        [ExaminedTaskCount] int NOT NULL,
        [EligibleTaskCount] int NOT NULL,
        [ExaminedPersonCount] int NOT NULL,
        [EligiblePersonCount] int NOT NULL,
        [EligiblePairCount] int NOT NULL,
        [MatchableAssignmentCount] int NOT NULL,
        [ReturnedAssignmentCount] int NOT NULL,
        [IsTruncated] bit NOT NULL,
        [ExclusionSamplesTruncated] bit NOT NULL,
        [RequestJson] nvarchar(max) NOT NULL,
        [SourcesJson] nvarchar(max) NOT NULL,
        [ExclusionsJson] nvarchar(max) NOT NULL,
        [ExclusionSamplesJson] nvarchar(max) NOT NULL,
        [AssignmentsJson] nvarchar(max) NOT NULL,
        [LimitationsJson] nvarchar(max) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_DispatchRecommendation] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_DispatchRecommendation_Tenant_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_DispatchRecommendation_Counts] CHECK ([ExaminedTaskCount] >= 0 AND [EligibleTaskCount] >= 0 AND [ExaminedPersonCount] >= 0 AND [EligiblePersonCount] >= 0 AND [EligiblePairCount] >= 0 AND [MatchableAssignmentCount] >= 0 AND [ReturnedAssignmentCount] >= 0 AND [EligibleTaskCount] <= [ExaminedTaskCount] AND [EligiblePersonCount] <= [ExaminedPersonCount] AND [MatchableAssignmentCount] <= [EligibleTaskCount] AND [MatchableAssignmentCount] <= [EligiblePersonCount] AND [MatchableAssignmentCount] <= [EligiblePairCount] AND [ReturnedAssignmentCount] <= [MatchableAssignmentCount] AND (([IsTruncated] = 1 AND [ReturnedAssignmentCount] < [MatchableAssignmentCount]) OR ([IsTruncated] = 0 AND [ReturnedAssignmentCount] = [MatchableAssignmentCount]))),
        CONSTRAINT [CK_Space_DispatchRecommendation_Evidence] CHECK ([Outcome] IN ('NoAssignment', 'AssignmentsGenerated') AND ISJSON([RequestJson]) = 1 AND ISJSON([SourcesJson]) = 1 AND ISJSON([ExclusionsJson]) = 1 AND ISJSON([ExclusionSamplesJson]) = 1 AND ISJSON([AssignmentsJson]) = 1 AND ISJSON([LimitationsJson]) = 1),
        CONSTRAINT [CK_Space_DispatchRecommendation_Immutable] CHECK (LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_DispatchRecommendation_Version_Tenant] FOREIGN KEY ([TenantId], [PublishedVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802180049_SpaceE11S03DispatchRecommendations'
)
BEGIN
    CREATE INDEX [IX_Space_DispatchRecommendation_Tenant_Site_Generated] ON [Space_DispatchRecommendation] ([TenantId], [SiteId], [GeneratedAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802180049_SpaceE11S03DispatchRecommendations'
)
BEGIN
    CREATE INDEX [IX_Space_DispatchRecommendation_TenantId_PublishedVersionId] ON [Space_DispatchRecommendation] ([TenantId], [PublishedVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802180049_SpaceE11S03DispatchRecommendations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802180049_SpaceE11S03DispatchRecommendations', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    ALTER TABLE [Space_ModelVersion] ADD [Purpose] smallint NOT NULL DEFAULT CAST(0 AS smallint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    CREATE TABLE [Space_PlanningScenarioBranch] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelId] uniqueidentifier NOT NULL,
        [BasePublishedVersionId] uniqueidentifier NOT NULL,
        [ScenarioVersionId] uniqueidentifier NOT NULL,
        [CloneJobId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [DefinitionVersion] varchar(100) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningScenarioBranch] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PlanningScenarioBranch_Tenant_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_PlanningScenarioBranch_Immutable] CHECK ([BasePublishedVersionId] <> [ScenarioVersionId] AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningScenarioBranch_BaseVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [BasePublishedVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningScenarioBranch_CloneJob_Tenant] FOREIGN KEY ([TenantId], [CloneJobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningScenarioBranch_Model_Tenant] FOREIGN KEY ([TenantId], [ModelId]) REFERENCES [Space_Model] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningScenarioBranch_ScenarioVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [ScenarioVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ModelVersion] ADD CONSTRAINT [CK_Space_ModelVersion_Purpose] CHECK ([Purpose] IN (0, 1) AND ([Purpose] = 0 OR ([Status] NOT IN (3, 4, 5, 6) AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL)))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningScenarioBranch_Site_Created] ON [Space_PlanningScenarioBranch] ([TenantId], [SiteId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningScenarioBranch_TenantId_ModelId_BasePublishedVersionId] ON [Space_PlanningScenarioBranch] ([TenantId], [ModelId], [BasePublishedVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningScenarioBranch_TenantId_ModelId_ScenarioVersionId] ON [Space_PlanningScenarioBranch] ([TenantId], [ModelId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PlanningScenarioBranch_CloneJob] ON [Space_PlanningScenarioBranch] ([TenantId], [CloneJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PlanningScenarioBranch_ScenarioVersion] ON [Space_PlanningScenarioBranch] ([TenantId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802204901_SpaceE12S01PlanningScenarioBranches'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802204901_SpaceE12S01PlanningScenarioBranches', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802212845_SpaceE12S02HistoricalReplayDataset'
)
BEGIN
    CREATE TABLE [Space_PlanningHistoricalDataset] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelId] uniqueidentifier NOT NULL,
        [BranchId] uniqueidentifier NOT NULL,
        [ScenarioVersionId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [HistoricalFromUtc] datetimeoffset(7) NOT NULL,
        [HistoricalToUtc] datetimeoffset(7) NOT NULL,
        [ReplayStartUtc] datetimeoffset(7) NOT NULL,
        [ReplaySpeedFactor] decimal(9,4) NOT NULL,
        [TaskCount] int NOT NULL,
        [SourceDatasetHash] char(64) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [DefinitionVersion] varchar(100) NOT NULL,
        [DeidentificationVersion] varchar(100) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningHistoricalDataset] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PlanningHistoricalDataset_Tenant_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_PlanningHistoricalDataset_Invariants] CHECK ([HistoricalFromUtc] < [HistoricalToUtc] AND [ReplaySpeedFactor] > 0 AND [ReplaySpeedFactor] <= 1000 AND [TaskCount] BETWEEN 1 AND 10000 AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningHistoricalDataset_Branch_Tenant] FOREIGN KEY ([TenantId], [BranchId]) REFERENCES [Space_PlanningScenarioBranch] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningHistoricalDataset_Model_Tenant] FOREIGN KEY ([TenantId], [ModelId]) REFERENCES [Space_Model] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningHistoricalDataset_ScenarioVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [ScenarioVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802212845_SpaceE12S02HistoricalReplayDataset'
)
BEGIN
    CREATE TABLE [Space_PlanningHistoricalTask] (
        [Id] uniqueidentifier NOT NULL,
        [DatasetId] uniqueidentifier NOT NULL,
        [SequenceNo] int NOT NULL,
        [TaskToken] char(64) NOT NULL,
        [WorkerToken] char(64) NULL,
        [TaskType] smallint NOT NULL,
        [Outcome] smallint NOT NULL,
        [OriginalCreatedAtUtc] datetimeoffset(7) NOT NULL,
        [OriginalCompletedAtUtc] datetimeoffset(7) NOT NULL,
        [ReplayCreatedAtUtc] datetimeoffset(7) NOT NULL,
        [ReplayCompletedAtUtc] datetimeoffset(7) NOT NULL,
        [FromLocationLogicalId] uniqueidentifier NULL,
        [ToLocationLogicalId] uniqueidentifier NOT NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningHistoricalTask] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_PlanningHistoricalTask_Invariants] CHECK ([SequenceNo] > 0 AND [Quantity] > 0 AND [OriginalCreatedAtUtc] <= [OriginalCompletedAtUtc] AND [ReplayCreatedAtUtc] <= [ReplayCompletedAtUtc] AND [ToLocationLogicalId] <> '00000000-0000-0000-0000-000000000000' AND ([FromLocationLogicalId] IS NULL OR [FromLocationLogicalId] <> '00000000-0000-0000-0000-000000000000') AND LEN([TaskToken]) = 64 AND [TaskToken] NOT LIKE '%[^0-9a-f]%' AND ([WorkerToken] IS NULL OR (LEN([WorkerToken]) = 64 AND [WorkerToken] NOT LIKE '%[^0-9a-f]%')) AND [TaskType] BETWEEN 0 AND 4 AND [Outcome] BETWEEN 0 AND 2 AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningHistoricalTask_Dataset_Tenant] FOREIGN KEY ([TenantId], [DatasetId]) REFERENCES [Space_PlanningHistoricalDataset] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802212845_SpaceE12S02HistoricalReplayDataset'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningHistoricalDataset_Branch_Created] ON [Space_PlanningHistoricalDataset] ([TenantId], [BranchId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802212845_SpaceE12S02HistoricalReplayDataset'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningHistoricalDataset_TenantId_ModelId_ScenarioVersionId] ON [Space_PlanningHistoricalDataset] ([TenantId], [ModelId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802212845_SpaceE12S02HistoricalReplayDataset'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PlanningHistoricalTask_Dataset_Sequence] ON [Space_PlanningHistoricalTask] ([TenantId], [DatasetId], [SequenceNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802212845_SpaceE12S02HistoricalReplayDataset'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PlanningHistoricalTask_Dataset_Token] ON [Space_PlanningHistoricalTask] ([TenantId], [DatasetId], [TaskToken]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802212845_SpaceE12S02HistoricalReplayDataset'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802212845_SpaceE12S02HistoricalReplayDataset', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    ALTER TABLE [Space_PlanningHistoricalDataset] ADD CONSTRAINT [AK_Space_PlanningHistoricalDataset_Tenant_Id_Branch_Model_Version] UNIQUE ([TenantId], [Id], [BranchId], [ModelId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE TABLE [Space_PlanningSimulationRun] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelId] uniqueidentifier NOT NULL,
        [BranchId] uniqueidentifier NOT NULL,
        [ScenarioVersionId] uniqueidentifier NOT NULL,
        [ScenarioContentRevision] bigint NOT NULL,
        [DatasetId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [DefinitionVersion] varchar(100) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [DatasetRequestHash] char(64) NOT NULL,
        [ResultHash] char(64) NOT NULL,
        [GeometryBasis] varchar(100) NOT NULL,
        [CurrencyCode] char(3) NOT NULL,
        [DefaultQuantityCapacity] decimal(18,4) NOT NULL,
        [DefaultConcurrentTaskCapacity] int NOT NULL,
        [LocationCapacityOverrideCount] int NOT NULL,
        [ThroughputWindowMinutes] int NOT NULL,
        [DistanceCostPerMeter] decimal(19,6) NOT NULL,
        [LaborCostPerHour] decimal(19,6) NOT NULL,
        [CongestionCostPerTaskHour] decimal(19,6) NOT NULL,
        [TaskCount] int NOT NULL,
        [CompletedTaskCount] int NOT NULL,
        [CompletedQuantity] decimal(28,6) NOT NULL,
        [DistanceEligibleTaskCount] int NOT NULL,
        [TotalDistanceMeters] decimal(28,6) NOT NULL,
        [DistanceCoveragePercent] decimal(9,4) NOT NULL,
        [PeakConcurrentTasks] int NOT NULL,
        [CongestionSeconds] bigint NOT NULL,
        [CongestionTaskSeconds] bigint NOT NULL,
        [OverloadedLocationCount] int NOT NULL,
        [PeakCapacityUtilizationPercent] decimal(38,4) NOT NULL,
        [AverageCompletedTasksPerHour] decimal(28,6) NOT NULL,
        [PeakCompletedTasksPerHour] decimal(28,6) NOT NULL,
        [AverageCompletedQuantityPerHour] decimal(28,6) NOT NULL,
        [PeakCompletedQuantityPerHour] decimal(28,6) NOT NULL,
        [LaborHours] decimal(28,6) NOT NULL,
        [DistanceCost] decimal(28,6) NOT NULL,
        [LaborCost] decimal(28,6) NOT NULL,
        [CongestionCost] decimal(28,6) NOT NULL,
        [TotalCost] decimal(28,6) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningSimulationRun] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PlanningSimulationRun_Tenant_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [AK_Space_PlanningSimulationRun_Tenant_Id_Version] UNIQUE ([TenantId], [Id], [ScenarioVersionId]),
        CONSTRAINT [CK_Space_PlanningSimulationRun_Invariants] CHECK ([ScenarioContentRevision] >= 0 AND [DefaultQuantityCapacity] > 0 AND [DefaultConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [LocationCapacityOverrideCount] BETWEEN 0 AND 10000 AND [ThroughputWindowMinutes] BETWEEN 1 AND 1440 AND [DistanceCostPerMeter] >= 0 AND [LaborCostPerHour] >= 0 AND [CongestionCostPerTaskHour] >= 0 AND [TaskCount] BETWEEN 1 AND 10000 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [CompletedQuantity] >= 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [PeakConcurrentTasks] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [AverageCompletedQuantityPerHour] >= 0 AND [PeakCompletedQuantityPerHour] >= 0 AND [LaborHours] >= 0 AND [DistanceCost] >= 0 AND [LaborCost] >= 0 AND [CongestionCost] >= 0 AND [TotalCost] >= 0 AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([DatasetRequestHash]) = 64 AND [DatasetRequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ResultHash]) = 64 AND [ResultHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningSimulationRun_Branch_Tenant] FOREIGN KEY ([TenantId], [BranchId]) REFERENCES [Space_PlanningScenarioBranch] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningSimulationRun_Dataset_Tenant] FOREIGN KEY ([TenantId], [DatasetId], [BranchId], [ModelId], [ScenarioVersionId]) REFERENCES [Space_PlanningHistoricalDataset] ([TenantId], [Id], [BranchId], [ModelId], [ScenarioVersionId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningSimulationRun_Model_Tenant] FOREIGN KEY ([TenantId], [ModelId]) REFERENCES [Space_Model] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningSimulationRun_ScenarioVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [ScenarioVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE TABLE [Space_PlanningSimulationLocationResult] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [ScenarioVersionId] uniqueidentifier NOT NULL,
        [LocationLogicalId] uniqueidentifier NOT NULL,
        [TaskCount] int NOT NULL,
        [CompletedTaskCount] int NOT NULL,
        [TotalQuantity] decimal(28,6) NOT NULL,
        [DistanceEligibleTaskCount] int NOT NULL,
        [TotalDistanceMeters] decimal(28,6) NOT NULL,
        [QuantityCapacity] decimal(18,4) NOT NULL,
        [ConcurrentTaskCapacity] int NOT NULL,
        [PeakConcurrentTasks] int NOT NULL,
        [PeakConcurrentQuantity] decimal(28,6) NOT NULL,
        [CapacityUtilizationPercent] decimal(38,4) NOT NULL,
        [CongestionSeconds] bigint NOT NULL,
        [CongestionTaskSeconds] bigint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningSimulationLocationResult] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_PlanningSimulationLocationResult_Invariants] CHECK ([TaskCount] > 0 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalQuantity] > 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [QuantityCapacity] > 0 AND [ConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [PeakConcurrentTasks] >= 0 AND [PeakConcurrentQuantity] >= 0 AND [CapacityUtilizationPercent] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningSimulationLocation_Location_Tenant] FOREIGN KEY ([TenantId], [ScenarioVersionId], [LocationLogicalId]) REFERENCES [Space_LocationRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningSimulationLocation_Run_Tenant] FOREIGN KEY ([TenantId], [RunId], [ScenarioVersionId]) REFERENCES [Space_PlanningSimulationRun] ([TenantId], [Id], [ScenarioVersionId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningSimulationLocationResult_TenantId_RunId_ScenarioVersionId] ON [Space_PlanningSimulationLocationResult] ([TenantId], [RunId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningSimulationLocationResult_TenantId_ScenarioVersionId_LocationLogicalId] ON [Space_PlanningSimulationLocationResult] ([TenantId], [ScenarioVersionId], [LocationLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PlanningSimulationLocation_Run_Location] ON [Space_PlanningSimulationLocationResult] ([TenantId], [RunId], [LocationLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningSimulationRun_Branch_Created] ON [Space_PlanningSimulationRun] ([TenantId], [BranchId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningSimulationRun_Dataset_Created] ON [Space_PlanningSimulationRun] ([TenantId], [DatasetId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningSimulationRun_TenantId_DatasetId_BranchId_ModelId_ScenarioVersionId] ON [Space_PlanningSimulationRun] ([TenantId], [DatasetId], [BranchId], [ModelId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningSimulationRun_TenantId_ModelId_ScenarioVersionId] ON [Space_PlanningSimulationRun] ([TenantId], [ModelId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802221548_SpaceE12S03PlanningSimulation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802221548_SpaceE12S03PlanningSimulation', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    ALTER TABLE [Space_PlanningSimulationRun] ADD CONSTRAINT [AK_Space_PlanningSimulationRun_Tenant_Id_Branch_Version] UNIQUE ([TenantId], [Id], [BranchId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    ALTER TABLE [Space_PlanningSimulationRun] ADD CONSTRAINT [AK_Space_PlanningSimulationRun_Tenant_Id_Site] UNIQUE ([TenantId], [Id], [SiteId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE TABLE [Space_PlanningComparison] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelId] uniqueidentifier NOT NULL,
        [BasePublishedVersionId] uniqueidentifier NOT NULL,
        [BaselineRunId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [DefinitionVersion] varchar(100) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [ComparisonHash] char(64) NOT NULL,
        [SourceDatasetHash] char(64) NOT NULL,
        [CurrencyCode] char(3) NOT NULL,
        [HistoricalFromUtc] datetimeoffset(7) NOT NULL,
        [HistoricalToUtc] datetimeoffset(7) NOT NULL,
        [RunCount] int NOT NULL,
        [MinimumDistanceCoveragePercent] decimal(9,4) NOT NULL,
        [MaximumPeakCapacityUtilizationPercent] decimal(38,4) NOT NULL,
        [MaximumCongestionTaskHours] decimal(28,6) NOT NULL,
        [MaximumTotalCost] decimal(28,6) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningComparison] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PlanningComparison_Tenant_Id_Site] UNIQUE ([TenantId], [Id], [SiteId]),
        CONSTRAINT [CK_Space_PlanningComparison_Invariants] CHECK ([RunCount] BETWEEN 2 AND 10 AND [HistoricalFromUtc] < [HistoricalToUtc] AND [MinimumDistanceCoveragePercent] BETWEEN 0 AND 100 AND [MaximumPeakCapacityUtilizationPercent] >= 0 AND [MaximumCongestionTaskHours] >= 0 AND ([MaximumTotalCost] IS NULL OR [MaximumTotalCost] >= 0) AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningComparison_BaseVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [BasePublishedVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningComparison_BaselineRun_Tenant] FOREIGN KEY ([TenantId], [BaselineRunId], [SiteId]) REFERENCES [Space_PlanningSimulationRun] ([TenantId], [Id], [SiteId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningComparison_Model_Tenant] FOREIGN KEY ([TenantId], [ModelId]) REFERENCES [Space_Model] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE TABLE [Space_PlanningComparisonEntry] (
        [Id] uniqueidentifier NOT NULL,
        [ComparisonId] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [SequenceNo] int NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [BranchId] uniqueidentifier NOT NULL,
        [ScenarioVersionId] uniqueidentifier NOT NULL,
        [ScenarioContentRevision] bigint NOT NULL,
        [RunName] nvarchar(200) NOT NULL,
        [RunResultHash] char(64) NOT NULL,
        [IsBaseline] bit NOT NULL,
        [DistanceCoveragePercent] decimal(9,4) NOT NULL,
        [TotalDistanceMeters] decimal(28,6) NOT NULL,
        [CongestionTaskSeconds] bigint NOT NULL,
        [OverloadedLocationCount] int NOT NULL,
        [PeakCapacityUtilizationPercent] decimal(38,4) NOT NULL,
        [AverageCompletedTasksPerHour] decimal(28,6) NOT NULL,
        [PeakCompletedTasksPerHour] decimal(28,6) NOT NULL,
        [TotalCost] decimal(28,6) NOT NULL,
        [DistanceDeltaMeters] decimal(28,6) NOT NULL,
        [CongestionTaskSecondsDelta] bigint NOT NULL,
        [OverloadedLocationCountDelta] int NOT NULL,
        [PeakCapacityUtilizationDeltaPercentagePoints] decimal(38,4) NOT NULL,
        [AverageCompletedTasksPerHourDelta] decimal(28,6) NOT NULL,
        [TotalCostDelta] decimal(28,6) NOT NULL,
        [RiskCount] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningComparisonEntry] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PlanningComparisonEntry_Comparison_Id_Run] UNIQUE ([TenantId], [ComparisonId], [Id], [RunId]),
        CONSTRAINT [AK_Space_PlanningComparisonEntry_Comparison_Run] UNIQUE ([TenantId], [ComparisonId], [RunId]),
        CONSTRAINT [CK_Space_PlanningComparisonEntry_Invariants] CHECK ([SequenceNo] BETWEEN 1 AND 10 AND [ScenarioContentRevision] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [TotalDistanceMeters] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [TotalCost] >= 0 AND [RiskCount] BETWEEN 0 AND 10 AND LEN([RunResultHash]) = 64 AND [RunResultHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningComparisonEntry_Comparison_Tenant] FOREIGN KEY ([TenantId], [ComparisonId], [SiteId]) REFERENCES [Space_PlanningComparison] ([TenantId], [Id], [SiteId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningComparisonEntry_Run_Tenant] FOREIGN KEY ([TenantId], [RunId], [BranchId], [ScenarioVersionId]) REFERENCES [Space_PlanningSimulationRun] ([TenantId], [Id], [BranchId], [ScenarioVersionId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE TABLE [Space_PlanningComparisonRisk] (
        [Id] uniqueidentifier NOT NULL,
        [ComparisonId] uniqueidentifier NOT NULL,
        [EntryId] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [Code] varchar(100) NOT NULL,
        [Severity] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningComparisonRisk] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_PlanningComparisonRisk_Invariants] CHECK ([Severity] BETWEEN 1 AND 3 AND LEN([Code]) BETWEEN 1 AND 100 AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningComparisonRisk_Entry_Tenant] FOREIGN KEY ([TenantId], [ComparisonId], [EntryId], [RunId]) REFERENCES [Space_PlanningComparisonEntry] ([TenantId], [ComparisonId], [Id], [RunId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE TABLE [Space_PlanningDecisionRecord] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ComparisonId] uniqueidentifier NOT NULL,
        [SelectedRunId] uniqueidentifier NULL,
        [SupersedesDecisionId] uniqueidentifier NULL,
        [Outcome] int NOT NULL,
        [Rationale] nvarchar(2000) NOT NULL,
        [ComparisonHash] char(64) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [DefinitionVersion] varchar(100) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PlanningDecisionRecord] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PlanningDecisionRecord_Comparison_Id] UNIQUE ([TenantId], [ComparisonId], [Id]),
        CONSTRAINT [CK_Space_PlanningDecisionRecord_Invariants] CHECK ([Outcome] BETWEEN 1 AND 3 AND (([Outcome] = 1 AND [SelectedRunId] IS NOT NULL) OR ([Outcome] IN (2, 3) AND [SelectedRunId] IS NULL)) AND ([SupersedesDecisionId] IS NULL OR [SupersedesDecisionId] <> [Id]) AND LEN([Rationale]) BETWEEN 1 AND 2000 AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0),
        CONSTRAINT [FK_Space_PlanningDecisionRecord_Comparison_Tenant] FOREIGN KEY ([TenantId], [ComparisonId], [SiteId]) REFERENCES [Space_PlanningComparison] ([TenantId], [Id], [SiteId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningDecisionRecord_SelectedRun_Tenant] FOREIGN KEY ([TenantId], [ComparisonId], [SelectedRunId]) REFERENCES [Space_PlanningComparisonEntry] ([TenantId], [ComparisonId], [RunId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PlanningDecisionRecord_Supersedes_Tenant] FOREIGN KEY ([TenantId], [ComparisonId], [SupersedesDecisionId]) REFERENCES [Space_PlanningDecisionRecord] ([TenantId], [ComparisonId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningComparison_Site_Created] ON [Space_PlanningComparison] ([TenantId], [SiteId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningComparison_TenantId_BaselineRunId_SiteId] ON [Space_PlanningComparison] ([TenantId], [BaselineRunId], [SiteId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningComparison_TenantId_ModelId_BasePublishedVersionId] ON [Space_PlanningComparison] ([TenantId], [ModelId], [BasePublishedVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningComparisonEntry_TenantId_ComparisonId_SiteId] ON [Space_PlanningComparisonEntry] ([TenantId], [ComparisonId], [SiteId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningComparisonEntry_TenantId_RunId_BranchId_ScenarioVersionId] ON [Space_PlanningComparisonEntry] ([TenantId], [RunId], [BranchId], [ScenarioVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_PlanningComparisonEntry_Comparison_Baseline] ON [Space_PlanningComparisonEntry] ([TenantId], [ComparisonId], [IsBaseline]) WHERE [IsBaseline] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PlanningComparisonEntry_Comparison_Sequence] ON [Space_PlanningComparisonEntry] ([TenantId], [ComparisonId], [SequenceNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningComparisonRisk_TenantId_ComparisonId_EntryId_RunId] ON [Space_PlanningComparisonRisk] ([TenantId], [ComparisonId], [EntryId], [RunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PlanningComparisonRisk_Entry_Code] ON [Space_PlanningComparisonRisk] ([TenantId], [EntryId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningDecisionRecord_Comparison_Created] ON [Space_PlanningDecisionRecord] ([TenantId], [ComparisonId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningDecisionRecord_TenantId_ComparisonId_SelectedRunId] ON [Space_PlanningDecisionRecord] ([TenantId], [ComparisonId], [SelectedRunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    CREATE INDEX [IX_Space_PlanningDecisionRecord_TenantId_ComparisonId_SiteId] ON [Space_PlanningDecisionRecord] ([TenantId], [ComparisonId], [SiteId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_PlanningDecisionRecord_Supersedes] ON [Space_PlanningDecisionRecord] ([TenantId], [ComparisonId], [SupersedesDecisionId]) WHERE [SupersedesDecisionId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260802224514_SpaceE12S04PlanningComparisonDecision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260802224514_SpaceE12S04PlanningComparisonDecision', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [GenerationProposalId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [GenerationRunId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [ResolutionDecisionId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [ResolutionKind] smallint NOT NULL DEFAULT CAST(0 AS smallint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    UPDATE [Space_ModelIssue] SET [ResolutionKind] = 1 WHERE [Status] = 1 AND [ResolutionCommandBatchId] IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE TABLE [Space_GenerationLockedFact] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [BasedOnRunId] uniqueidentifier NOT NULL,
        [SourceProposalId] uniqueidentifier NOT NULL,
        [SourceDecisionId] uniqueidentifier NOT NULL,
        [SourceHash] char(64) NOT NULL,
        [SourceKey] nvarchar(256) NOT NULL,
        [ProposalType] nvarchar(64) NOT NULL,
        [FieldPath] nvarchar(256) NOT NULL,
        [ValueJson] nvarchar(max) NOT NULL,
        [MatchMethod] smallint NOT NULL,
        [MatchScore] decimal(6,5) NOT NULL,
        [IsConfirmed] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_GenerationLockedFact] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_GenerationLockedFact_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_GenerationLockedFact_Match] CHECK ([MatchScore] >= 0 AND [MatchScore] <= 1 AND [RunId] <> [BasedOnRunId]),
        CONSTRAINT [FK_Space_GenerationLockedFact_BasedOnRun_Tenant] FOREIGN KEY ([TenantId], [BasedOnRunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationLockedFact_Decision_Tenant] FOREIGN KEY ([TenantId], [SourceDecisionId]) REFERENCES [Space_ProposalDecision] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationLockedFact_Proposal_Tenant_Run] FOREIGN KEY ([TenantId], [BasedOnRunId], [SourceProposalId]) REFERENCES [Space_GenerationProposal] ([TenantId], [RunId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationLockedFact_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelIssue_Tenant_Run_Proposal_Status] ON [Space_ModelIssue] ([TenantId], [GenerationRunId], [GenerationProposalId], [Status], [Severity]) WHERE [GenerationRunId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE INDEX [IX_Space_ModelIssue_TenantId_ResolutionDecisionId] ON [Space_ModelIssue] ([TenantId], [ResolutionDecisionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [CK_Space_ModelIssue_GenerationScope] CHECK (([GenerationProposalId] IS NULL OR [GenerationRunId] IS NOT NULL) AND ([ResolutionDecisionId] IS NULL OR [GenerationProposalId] IS NOT NULL))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [CK_Space_ModelIssue_Resolution] CHECK (([Status] <> 1 AND [ResolutionKind] = 0 AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NULL) OR ([Status] = 1 AND (([ResolutionKind] = 1 AND [ResolutionCommandBatchId] IS NOT NULL AND [ResolutionDecisionId] IS NULL) OR ([ResolutionKind] IN (2, 3) AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NOT NULL))))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE INDEX [IX_GenerationLockedFact_Tenant_Decision_Run] ON [Space_GenerationLockedFact] ([TenantId], [SourceDecisionId], [RunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationLockedFact_TenantId_BasedOnRunId_SourceProposalId] ON [Space_GenerationLockedFact] ([TenantId], [BasedOnRunId], [SourceProposalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_GenerationLockedFact_Tenant_Run_Source_Type_Field] ON [Space_GenerationLockedFact] ([TenantId], [RunId], [SourceKey], [ProposalType], [FieldPath]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_GenerationRun_Tenant] FOREIGN KEY ([TenantId], [GenerationRunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_Proposal_Tenant_Run] FOREIGN KEY ([TenantId], [GenerationRunId], [GenerationProposalId]) REFERENCES [Space_GenerationProposal] ([TenantId], [RunId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_ResolutionDecision_Tenant] FOREIGN KEY ([TenantId], [ResolutionDecisionId]) REFERENCES [Space_ProposalDecision] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260806054950_SpaceE13S09ProposalDecisions', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_ZoneRevision] ADD [Name] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_RackRevision] ADD [Name] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_RackRevision] ADD [RackType] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [AppliedCountsJson] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [ApplyCommandBatchId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [ApplyExpectedRunRowVersion] nvarchar(128) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [ApplyJobId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [ApplyPlanHash] char(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [ApplyPreparedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [ApplyReviewEtag] char(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [TargetFloorLogicalId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_AisleRevision] ADD [Name] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    UPDATE [Space_ZoneRevision] SET [Name] = [ZoneCode] WHERE [Name] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    UPDATE [Space_AisleRevision] SET [Name] = [AisleCode] WHERE [Name] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    UPDATE [Space_RackRevision] SET [Name] = [RackCode] WHERE [Name] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Space_ZoneRevision]') AND [c].[name] = N'Name');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Space_ZoneRevision] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Space_ZoneRevision] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Space_AisleRevision]') AND [c].[name] = N'Name');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Space_AisleRevision] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [Space_AisleRevision] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Space_RackRevision]') AND [c].[name] = N'Name');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Space_RackRevision] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [Space_RackRevision] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    CREATE TABLE [Space_GenerationStagingElement] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [ProposalId] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [SequenceNo] int NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ElementType] nvarchar(64) NOT NULL,
        [NormalizedPayloadJson] nvarchar(max) NOT NULL,
        [ValidationStatus] smallint NOT NULL,
        [ValidationHash] char(64) NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_GenerationStagingElement] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_GenerationStagingElement_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_GenerationStagingElement_Validation] CHECK (([ValidationStatus] = 0 AND [ValidationHash] IS NULL) OR ([ValidationStatus] = 1 AND [ValidationHash] IS NOT NULL)),
        CONSTRAINT [FK_Space_GenerationStaging_Floor_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationStaging_Proposal_Tenant_Run] FOREIGN KEY ([TenantId], [RunId], [ProposalId]) REFERENCES [Space_GenerationProposal] ([TenantId], [RunId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationStaging_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationRun_TenantId_ApplyJobId] ON [Space_GenerationRun] ([TenantId], [ApplyJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationRun_TenantId_ModelVersionId_TargetFloorLogicalId] ON [Space_GenerationRun] ([TenantId], [ModelVersionId], [TargetFloorLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationStagingElement_TenantId_ModelVersionId_FloorLogicalId] ON [Space_GenerationStagingElement] ([TenantId], [ModelVersionId], [FloorLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_GenerationStaging_Tenant_Run_Logical] ON [Space_GenerationStagingElement] ([TenantId], [RunId], [LogicalId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_GenerationStaging_Tenant_Run_Proposal] ON [Space_GenerationStagingElement] ([TenantId], [RunId], [ProposalId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_GenerationStaging_Tenant_Run_Sequence] ON [Space_GenerationStagingElement] ([TenantId], [RunId], [SequenceNo]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD CONSTRAINT [FK_Space_GenerationRun_ApplyJob_Tenant] FOREIGN KEY ([TenantId], [ApplyJobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD CONSTRAINT [FK_Space_GenerationRun_TargetFloor_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [TargetFloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806110504_SpaceE13S10AtomicApply'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260806110504_SpaceE13S10AtomicApply', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [PayloadPurgedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [PayloadPurgedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [RetentionHoldUntilUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_GenerationProposal] ADD [PayloadPurgedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_AiUsageRecord] ADD [ArchivedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ModelIssue_Tenant_Purge_Run] ON [Space_ModelIssue] ([TenantId], [PayloadPurgedAtUtc], [GenerationRunId], [Id]) WHERE [GenerationRunId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_GenerationRun_Tenant_Retention] ON [Space_GenerationRun] ([TenantId], [PayloadPurgedAtUtc], [IsCurrent], [Status], [CreatedAtUtc], [Id]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Proposal_Tenant_Purge_Run] ON [Space_GenerationProposal] ([TenantId], [PayloadPurgedAtUtc], [RunId], [Id]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_AiUsage_Tenant_Retention] ON [Space_AiUsageRecord] ([TenantId], [ArchivedAtUtc], [RecordedAtUtc], [Id]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260806160931_SpaceE13S17AiRetention', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [Category] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [EvidenceJson] nvarchar(max) NOT NULL DEFAULT N'{}';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [FieldPath] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [ValidationRunId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    CREATE TABLE [Space_ValidationRun] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [ContentRevision] bigint NOT NULL,
        [ContentHash] char(64) NOT NULL,
        [RuleSetVersion] nvarchar(50) NOT NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [CapabilityHash] char(64) NOT NULL,
        [Status] smallint NOT NULL,
        [BlockingCount] int NOT NULL,
        [WarningCount] int NOT NULL,
        [InfoCount] int NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [RequestedBy] uniqueidentifier NOT NULL,
        [StartedAtUtc] datetime2 NULL,
        [FinishedAtUtc] datetime2 NULL,
        [JobId] uniqueidentifier NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [FailureCode] nvarchar(100) NULL,
        [FailureSummary] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ValidationRun] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ValidationRun_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ValidationRun_Counts] CHECK ([BlockingCount] >= 0 AND [WarningCount] >= 0 AND [InfoCount] >= 0 AND ([Status] <> 2 OR [BlockingCount] = 0) AND ([Status] <> 3 OR [BlockingCount] > 0)),
        CONSTRAINT [CK_Space_ValidationRun_StatusTime] CHECK (([Status] = 0 AND [StartedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([Status] IN (2, 3, 4) AND [FinishedAtUtc] IS NOT NULL)),
        CONSTRAINT [FK_Space_ValidationRun_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ValidationRun_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelIssue_Tenant_Validation_Severity_Code] ON [Space_ModelIssue] ([TenantId], [ValidationRunId], [Severity], [Code], [Id]) WHERE [ValidationRunId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [CK_Space_ModelIssue_ValidationScope] CHECK ([ValidationRunId] IS NULL OR ([ModelVersionId] IS NOT NULL AND [JobId] IS NOT NULL))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    CREATE INDEX [IX_Space_ValidationRun_Tenant_Version_Requested] ON [Space_ValidationRun] ([TenantId], [ModelVersionId], [RequestedAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ValidationRun_Tenant_Input_ActiveOrReusable] ON [Space_ValidationRun] ([TenantId], [ModelVersionId], [ContentHash], [RuleSetVersion], [AdapterId], [CapabilityHash]) WHERE [Status] <> 4 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_ValidationRun_Tenant_Job] ON [Space_ValidationRun] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_ValidationRun_Tenant] FOREIGN KEY ([TenantId], [ValidationRunId]) REFERENCES [Space_ValidationRun] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260807105256_SpaceE06S01ValidationEngine', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE TABLE [Space_PublishPlan] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [TargetVersionId] uniqueidentifier NOT NULL,
        [BaseVersionId] uniqueidentifier NULL,
        [ValidationRunId] uniqueidentifier NOT NULL,
        [ContentHash] char(64) NOT NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [CapabilityHash] char(64) NOT NULL,
        [PlanHash] char(64) NOT NULL,
        [ItemCount] int NOT NULL,
        [PlanJson] nvarchar(max) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PublishPlan] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PublishPlan_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_PublishPlan_TargetVersion_Tenant] FOREIGN KEY ([TenantId], [TargetVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PublishPlan_ValidationRun_Tenant] FOREIGN KEY ([TenantId], [ValidationRunId]) REFERENCES [Space_ValidationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE TABLE [Space_RuntimeElement] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [IsActive] bit NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_RuntimeElement] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE TABLE [Space_PublishAttempt] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [PublishPlanId] uniqueidentifier NOT NULL,
        [TargetVersionId] uniqueidentifier NOT NULL,
        [BaseVersionId] uniqueidentifier NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [Status] smallint NOT NULL,
        [CurrentStep] smallint NOT NULL,
        [BusinessIdempotencyKey] nvarchar(200) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [OwnsPublishSlot] bit NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [FinishedAtUtc] datetime2 NULL,
        [RequestedBy] uniqueidentifier NOT NULL,
        [ApprovedBy] uniqueidentifier NULL,
        [ApprovalReference] nvarchar(500) NULL,
        [WmsCommittedAtUtc] datetime2 NULL,
        [RuntimeActivatedAtUtc] datetime2 NULL,
        [LastErrorCode] nvarchar(100) NULL,
        [Summary] nvarchar(2000) NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PublishAttempt] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PublishAttempt_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_PublishAttempt_Slot] CHECK (([OwnsPublishSlot] = 1 AND [FinishedAtUtc] IS NULL) OR ([OwnsPublishSlot] = 0)),
        CONSTRAINT [FK_Space_PublishAttempt_Plan_Tenant] FOREIGN KEY ([TenantId], [PublishPlanId]) REFERENCES [Space_PublishPlan] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE TABLE [Space_PublishBatch] (
        [Id] uniqueidentifier NOT NULL,
        [AttemptId] uniqueidentifier NOT NULL,
        [BatchNo] int NOT NULL,
        [OperationKey] nvarchar(300) NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [Status] smallint NOT NULL,
        [AttemptCount] int NOT NULL,
        [ExternalOperationId] nvarchar(200) NULL,
        [ResultJson] nvarchar(max) NULL,
        [ObservedAtUtc] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PublishBatch] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_PublishBatch_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_PublishBatch_Attempt_Tenant] FOREIGN KEY ([TenantId], [AttemptId]) REFERENCES [Space_PublishAttempt] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE TABLE [Space_ReconciliationIssue] (
        [Id] uniqueidentifier NOT NULL,
        [AttemptId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NULL,
        [ExpectedStateHash] char(64) NULL,
        [WmsStateHash] char(64) NULL,
        [RuntimeStateHash] char(64) NULL,
        [Classification] smallint NOT NULL,
        [Status] smallint NOT NULL,
        [Summary] nvarchar(2000) NOT NULL,
        [Resolution] nvarchar(4000) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ReconciliationIssue] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_ReconciliationIssue_Attempt_Tenant] FOREIGN KEY ([TenantId], [AttemptId]) REFERENCES [Space_PublishAttempt] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE TABLE [Space_WmsReceipt] (
        [Id] uniqueidentifier NOT NULL,
        [BatchId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [LocationCode] nvarchar(256) NOT NULL,
        [Action] smallint NOT NULL,
        [Outcome] smallint NOT NULL,
        [ExternalLocationId] nvarchar(200) NULL,
        [ExternalVersion] nvarchar(100) NULL,
        [ResponseHash] char(64) NULL,
        [ErrorCode] nvarchar(100) NULL,
        [ReceivedAtUtc] datetime2 NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_WmsReceipt] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_WmsReceipt_Batch_Tenant] FOREIGN KEY ([TenantId], [BatchId]) REFERENCES [Space_PublishBatch] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE INDEX [IX_Space_PublishAttempt_Tenant_Site_Started] ON [Space_PublishAttempt] ([TenantId], [SiteId], [StartedAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE INDEX [IX_Space_PublishAttempt_TenantId_PublishPlanId] ON [Space_PublishAttempt] ([TenantId], [PublishPlanId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishAttempt_Tenant_Idempotency] ON [Space_PublishAttempt] ([TenantId], [BusinessIdempotencyKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_PublishAttempt_Tenant_Site_Active] ON [Space_PublishAttempt] ([TenantId], [SiteId]) WHERE [OwnsPublishSlot] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishBatch_Tenant_Attempt_BatchNo] ON [Space_PublishBatch] ([TenantId], [AttemptId], [BatchNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishBatch_Tenant_OperationKey] ON [Space_PublishBatch] ([TenantId], [OperationKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE INDEX [IX_Space_PublishPlan_Tenant_Site_Target_Created] ON [Space_PublishPlan] ([TenantId], [SiteId], [TargetVersionId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE INDEX [IX_Space_PublishPlan_TenantId_TargetVersionId] ON [Space_PublishPlan] ([TenantId], [TargetVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE INDEX [IX_Space_PublishPlan_TenantId_ValidationRunId] ON [Space_PublishPlan] ([TenantId], [ValidationRunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishPlan_Tenant_PlanHash] ON [Space_PublishPlan] ([TenantId], [PlanHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE INDEX [IX_Space_ReconciliationIssue_Tenant_Attempt_Status] ON [Space_ReconciliationIssue] ([TenantId], [AttemptId], [Status], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE INDEX [IX_Space_RuntimeElement_Tenant_Site_Version_Active] ON [Space_RuntimeElement] ([TenantId], [SiteId], [ModelVersionId], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_RuntimeElement_Tenant_Site_LogicalId] ON [Space_RuntimeElement] ([TenantId], [SiteId], [LogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_WmsReceipt_Tenant_Batch_LogicalId] ON [Space_WmsReceipt] ([TenantId], [BatchId], [LogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807135544_SpaceE06S03PublishOrchestration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260807135544_SpaceE06S03PublishOrchestration', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM [Space_PublishAttempt]
        WHERE [OwnsPublishSlot] = 1 AND [IsDeleted] = 0
    )
    BEGIN
        THROW 51020, 'Resolve every active E06-S03 publish attempt before applying E06-S04 recovery.', 1;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishBatch] ADD [BatchAttemptNo] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishBatch] ADD [RequestJson] nvarchar(max) NOT NULL DEFAULT N'{"items":[]}';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [JobId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [LastRetriedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [LastRetriedBy] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [ManualRetryCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [QueuedAtUtc] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [RequestJson] nvarchar(max) NOT NULL DEFAULT N'{}';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    UPDATE [Space_PublishAttempt]
    SET [QueuedAtUtc] = [StartedAtUtc]
    WHERE [QueuedAtUtc] = '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE TABLE [Space_PublishAuditEvent] (
        [Id] uniqueidentifier NOT NULL,
        [AttemptId] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [BatchId] uniqueidentifier NULL,
        [EventNo] int NOT NULL,
        [EventType] smallint NOT NULL,
        [AttemptStatus] smallint NOT NULL,
        [Step] smallint NOT NULL,
        [ActorId] uniqueidentifier NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [DeduplicationKey] nvarchar(300) NOT NULL,
        [Summary] nvarchar(2000) NOT NULL,
        [ErrorCode] nvarchar(100) NULL,
        [EvidenceJson] nvarchar(max) NOT NULL,
        [EvidenceHash] char(64) NOT NULL,
        [PreviousEventHash] char(64) NULL,
        [EventHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PublishAuditEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_PublishAuditEvent_Invariants] CHECK ([EventNo] > 0 AND ISJSON([EvidenceJson]) = 1 AND LEN([EvidenceHash]) = 64 AND [EvidenceHash] NOT LIKE '%[^0-9a-f]%' AND LEN([EventHash]) = 64 AND [EventHash] NOT LIKE '%[^0-9a-f]%' AND ([PreviousEventHash] IS NULL OR (LEN([PreviousEventHash]) = 64 AND [PreviousEventHash] NOT LIKE '%[^0-9a-f]%'))),
        CONSTRAINT [FK_Space_PublishAuditEvent_Attempt_Tenant] FOREIGN KEY ([TenantId], [AttemptId]) REFERENCES [Space_PublishAttempt] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PublishAuditEvent_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_PublishBatch] ADD CONSTRAINT [CK_Space_PublishBatch_Recovery] CHECK ([AttemptCount] >= 0 AND [BatchAttemptNo] >= 0 AND ISJSON([RequestJson]) = 1)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE INDEX [IX_Space_PublishAttempt_TenantId_JobId] ON [Space_PublishAttempt] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_PublishAttempt] ADD CONSTRAINT [CK_Space_PublishAttempt_Recovery] CHECK ([ManualRetryCount] >= 0 AND ISJSON([RequestJson]) = 1 AND (([ManualRetryCount] = 0 AND [LastRetriedAtUtc] IS NULL AND [LastRetriedBy] IS NULL) OR ([ManualRetryCount] > 0 AND [LastRetriedAtUtc] IS NOT NULL AND [LastRetriedBy] IS NOT NULL)))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE INDEX [IX_Space_PublishAuditEvent_Tenant_Job_Occurred] ON [Space_PublishAuditEvent] ([TenantId], [JobId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishAuditEvent_Tenant_Attempt_Dedupe] ON [Space_PublishAuditEvent] ([TenantId], [AttemptId], [DeduplicationKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishAuditEvent_Tenant_Attempt_EventNo] ON [Space_PublishAuditEvent] ([TenantId], [AttemptId], [EventNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD CONSTRAINT [FK_Space_PublishAttempt_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260807144532_SpaceE06S04PublishRecovery', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE TABLE [Space_HistoricalRepublish] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelId] uniqueidentifier NOT NULL,
        [HistoricalVersionId] uniqueidentifier NOT NULL,
        [ExpectedPublishedVersionId] uniqueidentifier NOT NULL,
        [TargetVersionId] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [ValidationRunId] uniqueidentifier NULL,
        [PublishAttemptId] uniqueidentifier NULL,
        [BusinessIdempotencyKey] nvarchar(128) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [ApprovalReference] nvarchar(500) NULL,
        [RequestedBy] uniqueidentifier NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [Status] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_HistoricalRepublish] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_HistoricalRepublish_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_HistoricalRepublish_Status] CHECK ([Status] IN (0, 1, 2, 3, 4)),
        CONSTRAINT [FK_Space_HistoricalRepublish_ExpectedVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [ExpectedPublishedVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_HistoricalVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [HistoricalVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_Model_Tenant] FOREIGN KEY ([TenantId], [ModelId]) REFERENCES [Space_Model] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_PublishAttempt_Tenant] FOREIGN KEY ([TenantId], [PublishAttemptId]) REFERENCES [Space_PublishAttempt] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_TargetVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [TargetVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_Validation_Tenant] FOREIGN KEY ([TenantId], [ValidationRunId]) REFERENCES [Space_ValidationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_Tenant_Site_Requested] ON [Space_HistoricalRepublish] ([TenantId], [SiteId], [RequestedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_JobId] ON [Space_HistoricalRepublish] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ModelId_ExpectedPublishedVersionId] ON [Space_HistoricalRepublish] ([TenantId], [ModelId], [ExpectedPublishedVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ModelId_HistoricalVersionId] ON [Space_HistoricalRepublish] ([TenantId], [ModelId], [HistoricalVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ModelId_TargetVersionId] ON [Space_HistoricalRepublish] ([TenantId], [ModelId], [TargetVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ValidationRunId] ON [Space_HistoricalRepublish] ([TenantId], [ValidationRunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_HistoricalRepublish_Tenant_Idempotency] ON [Space_HistoricalRepublish] ([TenantId], [BusinessIdempotencyKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_HistoricalRepublish_Tenant_PublishAttempt] ON [Space_HistoricalRepublish] ([TenantId], [PublishAttemptId]) WHERE [PublishAttemptId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260807170204_SpaceE06S05HistoricalRepublish', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    ALTER TABLE [Space_LocationRevision] ADD [LocationType] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE TABLE [Space_DesignAttribute] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [ObjectType] nvarchar(20) NOT NULL,
        [ObjectLogicalId] uniqueidentifier NOT NULL,
        [Namespace] nvarchar(100) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(4000) NOT NULL,
        [Unit] nvarchar(50) NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [SourceRef] nvarchar(500) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_DesignAttribute] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_DesignAttribute_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [CK_Space_DesignAttribute_ObjectType] CHECK ([ObjectType] IN ('Rack', 'RackLevel', 'Location')),
        CONSTRAINT [FK_Space_DesignAttribute_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_DesignAttribute_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE TABLE [Space_LocationExternalBinding] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LocationLogicalId] uniqueidentifier NOT NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [WarehouseCode] nvarchar(100) NOT NULL,
        [ExternalLocationId] nvarchar(200) NOT NULL,
        [BindingMode] smallint NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [SourceRef] nvarchar(500) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_LocationExternalBinding] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_LocationExternalBinding_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [CK_Space_LocationExternalBinding_Mode] CHECK ([BindingMode] IN (0, 1)),
        CONSTRAINT [FK_Space_LocationExternalBinding_Location_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [LocationLogicalId]) REFERENCES [Space_LocationRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationExternalBinding_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationExternalBinding_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE INDEX [IX_Space_DesignAttribute_TenantId_ModelVersionId_SourceId] ON [Space_DesignAttribute] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_DesignAttribute_Target_Key_Active] ON [Space_DesignAttribute] ([TenantId], [ModelVersionId], [ObjectType], [ObjectLogicalId], [Namespace], [Key]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE INDEX [IX_Space_LocationExternalBinding_TenantId_ModelVersionId_SourceId] ON [Space_LocationExternalBinding] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_LocationExternalBinding_External_Active] ON [Space_LocationExternalBinding] ([TenantId], [ModelVersionId], [AdapterId], [WarehouseCode], [ExternalLocationId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_LocationExternalBinding_Primary_Active] ON [Space_LocationExternalBinding] ([TenantId], [ModelVersionId], [LocationLogicalId]) WHERE [BindingMode] = 0 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260808131619_SpaceE03S05ExcelDesignMetadata', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    CREATE TABLE [Space_RackGenerationProfile] (
        [Id] uniqueidentifier NOT NULL,
        [Scope] smallint NOT NULL,
        [OwnerTenantId] uniqueidentifier NOT NULL,
        [ProfileCode] nvarchar(100) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Status] smallint NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackGenerationProfile] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackGenerationProfile_Scope_Owner_Id] UNIQUE ([Scope], [OwnerTenantId], [Id]),
        CONSTRAINT [CK_Space_RackGenerationProfile_ScopeOwner] CHECK (([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000'))
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    CREATE TABLE [Space_RackGenerationProfileVersion] (
        [Id] uniqueidentifier NOT NULL,
        [Scope] smallint NOT NULL,
        [OwnerTenantId] uniqueidentifier NOT NULL,
        [ProfileId] uniqueidentifier NOT NULL,
        [VersionNo] bigint NOT NULL,
        [RackWidthMillimeters] int NOT NULL,
        [RackDepthMillimeters] int NOT NULL,
        [RackHeightMillimeters] int NOT NULL,
        [LevelsJson] nvarchar(max) NOT NULL,
        [LocationCount] bigint NOT NULL,
        [ContentHash] char(64) NOT NULL,
        [Status] smallint NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackGenerationProfileVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackGenerationProfileVersion_Scope_Owner_Id] UNIQUE ([Scope], [OwnerTenantId], [Id]),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_Dimensions] CHECK ([RackWidthMillimeters] > 0 AND [RackDepthMillimeters] > 0 AND [RackHeightMillimeters] > 0),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_LocationCount] CHECK ([LocationCount] > 0 AND [LocationCount] <= 10000000),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_ScopeOwner] CHECK (([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_VersionNo] CHECK ([VersionNo] > 0),
        CONSTRAINT [FK_Space_RackGenerationProfileVersion_Profile_Scope_Owner] FOREIGN KEY ([Scope], [OwnerTenantId], [ProfileId]) REFERENCES [Space_RackGenerationProfile] ([Scope], [OwnerTenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_RackGenerationProfile_Scope_Owner_Code_Active] ON [Space_RackGenerationProfile] ([Scope], [OwnerTenantId], [ProfileCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_RackGenerationProfileVersion_Scope_Owner_Profile_VersionNo] ON [Space_RackGenerationProfileVersion] ([Scope], [OwnerTenantId], [ProfileId], [VersionNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260808164544_SpaceE13RackGenerationProfiles', N'8.0.12');
END;
GO

COMMIT;
GO
