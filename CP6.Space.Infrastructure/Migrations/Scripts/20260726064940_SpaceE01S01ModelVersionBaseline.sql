IF OBJECT_ID(N'[__EFMigrationsHistory_Space]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory_Space] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory_Space] PRIMARY KEY ([MigrationId])
    );
END;
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
