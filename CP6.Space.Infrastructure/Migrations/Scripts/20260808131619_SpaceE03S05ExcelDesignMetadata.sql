BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    ALTER TABLE [Space_LocationRevision] ADD [LocationType] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE TABLE [Space_DesignAttribute] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [ObjectType] nvarchar(20) NOT NULL,
        [ObjectLogicalId] uniqueidentifier NOT NULL,
        [Namespace] nvarchar(100) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(4000) NOT NULL,
        [Unit] nvarchar(50) NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [SourceRef] nvarchar(500) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_DesignAttribute] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_DesignAttribute_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [CK_Space_DesignAttribute_ObjectType] CHECK ([ObjectType] IN ('Rack', 'RackLevel', 'Location')),
        CONSTRAINT [FK_Space_DesignAttribute_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_DesignAttribute_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE TABLE [Space_LocationExternalBinding] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LocationLogicalId] uniqueidentifier NOT NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [WarehouseCode] nvarchar(100) NOT NULL,
        [ExternalLocationId] nvarchar(200) NOT NULL,
        [BindingMode] smallint NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [SourceRef] nvarchar(500) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_LocationExternalBinding] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_LocationExternalBinding_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [CK_Space_LocationExternalBinding_Mode] CHECK ([BindingMode] IN (0, 1)),
        CONSTRAINT [FK_Space_LocationExternalBinding_Location_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [LocationLogicalId]) REFERENCES [Space_LocationRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationExternalBinding_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationExternalBinding_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE INDEX [IX_Space_DesignAttribute_TenantId_ModelVersionId_SourceId] ON [Space_DesignAttribute] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_DesignAttribute_Target_Key_Active] ON [Space_DesignAttribute] ([TenantId], [ModelVersionId], [ObjectType], [ObjectLogicalId], [Namespace], [Key]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    CREATE INDEX [IX_Space_LocationExternalBinding_TenantId_ModelVersionId_SourceId] ON [Space_LocationExternalBinding] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_LocationExternalBinding_External_Active] ON [Space_LocationExternalBinding] ([TenantId], [ModelVersionId], [AdapterId], [WarehouseCode], [ExternalLocationId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_LocationExternalBinding_Primary_Active] ON [Space_LocationExternalBinding] ([TenantId], [ModelVersionId], [LocationLogicalId]) WHERE [BindingMode] = 0 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808131619_SpaceE03S05ExcelDesignMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260808131619_SpaceE03S05ExcelDesignMetadata', N'8.0.12');
END;
GO

COMMIT;
GO

