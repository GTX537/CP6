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

