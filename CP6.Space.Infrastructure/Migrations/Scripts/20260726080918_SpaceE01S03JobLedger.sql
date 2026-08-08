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
