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
