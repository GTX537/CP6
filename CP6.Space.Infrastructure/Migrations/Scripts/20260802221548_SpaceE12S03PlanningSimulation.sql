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

