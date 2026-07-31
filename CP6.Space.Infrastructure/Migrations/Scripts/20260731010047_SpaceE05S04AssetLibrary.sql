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
