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

