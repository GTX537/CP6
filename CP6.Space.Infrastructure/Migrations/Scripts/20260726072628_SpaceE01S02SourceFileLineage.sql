BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    ALTER TABLE [Space_ModelVersion] ADD CONSTRAINT [AK_Space_ModelVersion_TenantId_Id] UNIQUE ([TenantId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE TABLE [Space_File] (
        [Id] uniqueidentifier NOT NULL,
        [StorageKey] nvarchar(500) NOT NULL,
        [OriginalName] nvarchar(260) NOT NULL,
        [DeclaredContentType] nvarchar(200) NULL,
        [DetectedContentType] nvarchar(200) NULL,
        [Extension] nvarchar(20) NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] char(64) NULL,
        [State] smallint NOT NULL,
        [ScanEngine] nvarchar(100) NULL,
        [SignatureVersion] nvarchar(100) NULL,
        [ScanResultCode] nvarchar(100) NULL,
        [RetentionClass] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_File] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_File_TenantId_Id] UNIQUE ([TenantId], [Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE TABLE [Space_ModelSource] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [SourceType] smallint NOT NULL,
        [FileId] uniqueidentifier NULL,
        [DisplayName] nvarchar(260) NOT NULL,
        [Sha256] char(64) NOT NULL,
        [ParserVersion] nvarchar(100) NULL,
        [MappingProfileId] uniqueidentifier NULL,
        [MappingProfileVersion] bigint NULL,
        [Unit] nvarchar(50) NULL,
        [ScaleToMillimeters] decimal(18,8) NULL,
        [TransformJson] nvarchar(max) NULL,
        [State] smallint NOT NULL,
        [ImportedCommandBatchId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ModelSource] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ModelSource_TenantId_ModelVersionId_Id] UNIQUE ([TenantId], [ModelVersionId], [Id]),
        CONSTRAINT [FK_Space_ModelSource_File_Tenant] FOREIGN KEY ([TenantId], [FileId]) REFERENCES [Space_File] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ModelSource_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE TABLE [Space_Artifact] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NULL,
        [FileId] uniqueidentifier NOT NULL,
        [ArtifactType] smallint NOT NULL,
        [SchemaVersion] nvarchar(50) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_Artifact] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_Artifact_File_Tenant] FOREIGN KEY ([TenantId], [FileId]) REFERENCES [Space_File] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_Artifact_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_Artifact_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_Artifact_Tenant_File_Active] ON [Space_Artifact] ([TenantId], [FileId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE INDEX [IX_Space_Artifact_Tenant_Version] ON [Space_Artifact] ([TenantId], [ModelVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_Artifact_Tenant_Version_Source_Active] ON [Space_Artifact] ([TenantId], [ModelVersionId], [SourceId]) WHERE [SourceId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE INDEX [IX_Space_File_Tenant_State] ON [Space_File] ([TenantId], [State]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_File_StorageKey] ON [Space_File] ([StorageKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_File_Tenant_Hash_Retention_Reusable] ON [Space_File] ([TenantId], [Sha256], [RetentionClass]) WHERE [Sha256] IS NOT NULL AND [State] IN (1, 2, 3) AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelSource_Tenant_File_Active] ON [Space_ModelSource] ([TenantId], [FileId]) WHERE [FileId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    CREATE INDEX [IX_Space_ModelSource_Tenant_SourceHash] ON [Space_ModelSource] ([TenantId], [Sha256]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ModelSource_Version_Hash_Type_Active] ON [Space_ModelSource] ([TenantId], [ModelVersionId], [Sha256], [SourceType]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726072628_SpaceE01S02SourceFileLineage'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726072628_SpaceE01S02SourceFileLineage', N'8.0.12');
END;
GO

COMMIT;
GO
