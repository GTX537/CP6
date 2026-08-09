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
