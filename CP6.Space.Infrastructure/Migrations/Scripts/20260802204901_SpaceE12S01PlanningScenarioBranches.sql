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
