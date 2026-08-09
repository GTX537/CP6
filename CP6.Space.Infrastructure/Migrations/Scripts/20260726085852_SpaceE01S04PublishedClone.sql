BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    ALTER TABLE [Space_ModelVersion] ADD [CloneOperationId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_FloorRevision] (
        [Id] uniqueidentifier NOT NULL,
        [SiteLogicalId] uniqueidentifier NOT NULL,
        [Level] int NOT NULL,
        [FloorCode] nvarchar(100) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Elevation] int NOT NULL,
        [Height] int NOT NULL,
        [BoundaryJson] nvarchar(max) NOT NULL,
        [CoordinateSystem] nvarchar(100) NOT NULL,
        [UnderlaySourceId] uniqueidentifier NULL,
        [UnderlayScale] decimal(18,8) NULL,
        [UnderlayOffsetX] int NOT NULL,
        [UnderlayOffsetY] int NOT NULL,
        [UnderlayRotationZ] decimal(9,4) NOT NULL,
        [Revision] bigint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_FloorRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_FloorRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_FloorRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [FK_Space_FloorRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_FloorRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_FloorRevision_UnderlaySource_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [UnderlaySourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_ElementRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ParentLogicalId] uniqueidentifier NULL,
        [ElementType] nvarchar(100) NOT NULL,
        [GeometryJson] nvarchar(max) NOT NULL,
        [ModelAssetId] uniqueidentifier NULL,
        [X] int NOT NULL,
        [Y] int NOT NULL,
        [Z] int NOT NULL,
        [RotationZ] decimal(9,4) NOT NULL,
        [Width] int NOT NULL,
        [Height] int NOT NULL,
        [Depth] int NOT NULL,
        [BusinessCode] nvarchar(200) NULL,
        [LinkedEntityType] nvarchar(100) NULL,
        [LinkedLogicalId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_ElementRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ElementRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_ElementRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_ElementRevision_Geometry] CHECK ([RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Height] >= 0 AND [Depth] >= 0),
        CONSTRAINT [FK_Space_ElementRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ElementRevision_Parent_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [ParentLogicalId]) REFERENCES [Space_ElementRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ElementRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ElementRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_ZoneRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ZoneCode] nvarchar(100) NOT NULL,
        [ZoneType] smallint NOT NULL,
        [PolygonJson] nvarchar(max) NOT NULL,
        [Color] nvarchar(50) NULL,
        [CapabilityFlags] nvarchar(1000) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_ZoneRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ZoneRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_ZoneRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [FK_Space_ZoneRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ZoneRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ZoneRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_ElementAttribute] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [ElementRevisionId] uniqueidentifier NOT NULL,
        [Namespace] nvarchar(100) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [ValueType] nvarchar(50) NOT NULL,
        [Value] nvarchar(max) NULL,
        [Unit] nvarchar(50) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ElementAttribute] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_ElementAttribute_Element_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [ElementRevisionId]) REFERENCES [Space_ElementRevision] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_AisleRevision] (
        [Id] uniqueidentifier NOT NULL,
        [ZoneLogicalId] uniqueidentifier NOT NULL,
        [AisleCode] nvarchar(100) NOT NULL,
        [PolygonJson] nvarchar(max) NOT NULL,
        [CenterlineJson] nvarchar(max) NOT NULL,
        [Direction] smallint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_AisleRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_AisleRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_AisleRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [FK_Space_AisleRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_AisleRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_AisleRevision_Zone_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [ZoneLogicalId]) REFERENCES [Space_ZoneRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_RackRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ZoneLogicalId] uniqueidentifier NOT NULL,
        [AisleLogicalId] uniqueidentifier NULL,
        [RackCode] nvarchar(100) NOT NULL,
        [TemplateVersionId] uniqueidentifier NULL,
        [X] int NOT NULL,
        [Y] int NOT NULL,
        [Z] int NOT NULL,
        [RotationZ] decimal(9,4) NOT NULL,
        [Width] int NOT NULL,
        [Depth] int NOT NULL,
        [Height] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_RackRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_RackRevision_Geometry] CHECK ([RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Depth] >= 0 AND [Height] >= 0),
        CONSTRAINT [FK_Space_RackRevision_Aisle_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [AisleLogicalId]) REFERENCES [Space_AisleRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackRevision_Zone_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [ZoneLogicalId]) REFERENCES [Space_ZoneRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_LocationRevision] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [RackLogicalId] uniqueidentifier NULL,
        [LocationCode] nvarchar(200) NULL,
        [ColumnNo] int NOT NULL,
        [LevelNo] int NOT NULL,
        [DepthNo] int NOT NULL,
        [Width] int NOT NULL,
        [Height] int NOT NULL,
        [Depth] int NOT NULL,
        [MaxLoad] decimal(18,4) NULL,
        [CodeOrigin] smallint NOT NULL,
        [ExternalBindingState] smallint NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_LocationRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_LocationRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_LocationRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_LocationRevision_Dimensions] CHECK ([ColumnNo] > 0 AND [LevelNo] > 0 AND [DepthNo] > 0 AND [Width] > 0 AND [Height] > 0 AND [Depth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)),
        CONSTRAINT [FK_Space_LocationRevision_Floor_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [FloorLogicalId]) REFERENCES [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationRevision_Rack_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [RackLogicalId]) REFERENCES [Space_RackRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_LocationRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE TABLE [Space_RackLevelRevision] (
        [Id] uniqueidentifier NOT NULL,
        [RackLogicalId] uniqueidentifier NOT NULL,
        [LevelNo] int NOT NULL,
        [BottomZ] int NOT NULL,
        [ClearHeight] int NOT NULL,
        [BinCount] int NOT NULL,
        [DepthCount] int NOT NULL,
        [CellWidth] int NOT NULL,
        [CellDepth] int NOT NULL,
        [MaxLoad] decimal(18,4) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ModelVersionId] uniqueidentifier NOT NULL,
        [LogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [SourceRef] nvarchar(500) NULL,
        [LifecycleState] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackLevelRevision] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackLevelRevision_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [AK_Space_RackLevelRevision_TenantId_ModelVersionId_LogicalId] UNIQUE ([TenantId], [ModelVersionId], [LogicalId]),
        CONSTRAINT [CK_Space_RackLevelRevision_Dimensions] CHECK ([LevelNo] > 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)),
        CONSTRAINT [FK_Space_RackLevelRevision_Rack_Tenant_Version_Logical] FOREIGN KEY ([TenantId], [ModelVersionId], [RackLogicalId]) REFERENCES [Space_RackRevision] ([TenantId], [ModelVersionId], [LogicalId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackLevelRevision_Space_ModelSource_TenantId_ModelVersionId_SourceId] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_RackLevelRevision_Space_ModelVersion_TenantId_ModelVersionId] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ModelVersion_Tenant_Model_CloneOperation] ON [Space_ModelVersion] ([TenantId], [ModelId], [CloneOperationId]) WHERE [CloneOperationId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_AisleRevision_TenantId_ModelVersionId_SourceId] ON [Space_AisleRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_AisleRevision_Zone_Code_Active] ON [Space_AisleRevision] ([TenantId], [ModelVersionId], [ZoneLogicalId], [AisleCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ElementAttribute_Element_Key_Active] ON [Space_ElementAttribute] ([TenantId], [ModelVersionId], [ElementRevisionId], [Namespace], [Key]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ElementRevision_Floor_Type] ON [Space_ElementRevision] ([TenantId], [ModelVersionId], [FloorLogicalId], [ElementType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ElementRevision_TenantId_ModelVersionId_ParentLogicalId] ON [Space_ElementRevision] ([TenantId], [ModelVersionId], [ParentLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ElementRevision_TenantId_ModelVersionId_SourceId] ON [Space_ElementRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_TenantId_ModelVersionId_SourceId] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_TenantId_ModelVersionId_UnderlaySourceId] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [UnderlaySourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_Version_Level] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [Level]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_FloorRevision_Version_Code_Active] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [FloorCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_LocationRevision_Rack_Position_Active] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [RackLogicalId], [LevelNo], [ColumnNo], [DepthNo]) WHERE [RackLogicalId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_LocationRevision_TenantId_ModelVersionId_FloorLogicalId] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [FloorLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_LocationRevision_TenantId_ModelVersionId_SourceId] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_LocationRevision_Version_Code_Active] ON [Space_LocationRevision] ([TenantId], [ModelVersionId], [LocationCode]) WHERE [LocationCode] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackLevelRevision_TenantId_ModelVersionId_SourceId] ON [Space_RackLevelRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_RackLevelRevision_Rack_Level_Active] ON [Space_RackLevelRevision] ([TenantId], [ModelVersionId], [RackLogicalId], [LevelNo]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackRevision_TenantId_ModelVersionId_AisleLogicalId] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [AisleLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackRevision_TenantId_ModelVersionId_FloorLogicalId] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [FloorLogicalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_RackRevision_TenantId_ModelVersionId_SourceId] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_RackRevision_Zone_Code_Active] ON [Space_RackRevision] ([TenantId], [ModelVersionId], [ZoneLogicalId], [RackCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    CREATE INDEX [IX_Space_ZoneRevision_TenantId_ModelVersionId_SourceId] ON [Space_ZoneRevision] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ZoneRevision_Floor_Code_Active] ON [Space_ZoneRevision] ([TenantId], [ModelVersionId], [FloorLogicalId], [ZoneCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726085852_SpaceE01S04PublishedClone'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726085852_SpaceE01S04PublishedClone', N'8.0.12');
END;
GO

COMMIT;
GO
