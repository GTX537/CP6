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
