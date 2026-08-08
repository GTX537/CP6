BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    ALTER TABLE [Space_FloorRevision] ADD [UnderlayCalibrationId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE TABLE [Space_UnderlayCalibration] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [PageNumber] int NOT NULL,
        [PixelWidth] int NOT NULL,
        [PixelHeight] int NOT NULL,
        [Point1PixelX] decimal(18,6) NOT NULL,
        [Point1PixelY] decimal(18,6) NOT NULL,
        [Point1WorldX] int NOT NULL,
        [Point1WorldY] int NOT NULL,
        [Point2PixelX] decimal(18,6) NOT NULL,
        [Point2PixelY] decimal(18,6) NOT NULL,
        [Point2WorldX] int NOT NULL,
        [Point2WorldY] int NOT NULL,
        [ValidationPixelX] decimal(18,6) NOT NULL,
        [ValidationPixelY] decimal(18,6) NOT NULL,
        [ValidationWorldX] int NOT NULL,
        [ValidationWorldY] int NOT NULL,
        [MillimetersPerPixel] decimal(18,8) NOT NULL,
        [OffsetX] int NOT NULL,
        [OffsetY] int NOT NULL,
        [RotationZ] decimal(9,4) NOT NULL,
        [ValidationErrorMillimeters] decimal(18,4) NOT NULL,
        [ErrorThresholdMillimeters] decimal(18,4) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_UnderlayCalibration] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_UnderlayCalibration_Tenant_Version_Floor_Source_Id] UNIQUE ([TenantId], [ModelVersionId], [FloorLogicalId], [SourceId], [Id]),
        CONSTRAINT [FK_Space_UnderlayCalibration_Source_Tenant_Version] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE INDEX [IX_Space_FloorRevision_TenantId_ModelVersionId_LogicalId_UnderlaySourceId_UnderlayCalibrationId] ON [Space_FloorRevision] ([TenantId], [ModelVersionId], [LogicalId], [UnderlaySourceId], [UnderlayCalibrationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE INDEX [IX_Space_UnderlayCalibration_Version_Floor_Created] ON [Space_UnderlayCalibration] ([TenantId], [ModelVersionId], [FloorLogicalId], [CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    CREATE INDEX [IX_Space_UnderlayCalibration_Version_Source] ON [Space_UnderlayCalibration] ([TenantId], [ModelVersionId], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    ALTER TABLE [Space_FloorRevision] ADD CONSTRAINT [FK_Space_FloorRevision_UnderlayCalibration_Tenant_Version_Floor_Source] FOREIGN KEY ([TenantId], [ModelVersionId], [LogicalId], [UnderlaySourceId], [UnderlayCalibrationId]) REFERENCES [Space_UnderlayCalibration] ([TenantId], [ModelVersionId], [FloorLogicalId], [SourceId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731032506_SpaceE04S02UnderlayCalibration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260731032506_SpaceE04S02UnderlayCalibration', N'8.0.12');
END;
GO

COMMIT;
GO
