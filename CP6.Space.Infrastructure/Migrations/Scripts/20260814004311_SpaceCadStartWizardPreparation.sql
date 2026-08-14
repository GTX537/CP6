BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814004311_SpaceCadStartWizardPreparation'
)
BEGIN
    CREATE TABLE [Space_CadParsePreparation] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [SourceSha256] char(64) NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [ConfirmedUnit] nvarchar(50) NOT NULL,
        [ConfirmedScaleToMillimeters] decimal(18,9) NOT NULL,
        [CoordinateMetadataJson] nvarchar(max) NOT NULL,
        [CoordinateTransformSha256] char(64) NOT NULL,
        [MappingProfileId] uniqueidentifier NOT NULL,
        [MappingProfileVersion] int NOT NULL,
        [MappingDefinitionSha256] char(64) NOT NULL,
        [MappingPreviewSha256] char(64) NOT NULL,
        [SemanticPreviewSha256] char(64) NOT NULL,
        [ReadyForParsing] bit NOT NULL,
        [BaseContentRevision] bigint NOT NULL,
        [BaseContentHash] char(64) NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_CadParsePreparation] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_CadParsePreparation_Source_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId], [SourceId]) REFERENCES [Space_ModelSource] ([TenantId], [ModelVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_CadParsePreparation_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814004311_SpaceCadStartWizardPreparation'
)
BEGIN
    CREATE INDEX [IX_Space_CadParsePreparation_Source_Expiry] ON [Space_CadParsePreparation] ([TenantId], [ModelVersionId], [SourceId], [ExpiresAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814004311_SpaceCadStartWizardPreparation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260814004311_SpaceCadStartWizardPreparation', N'8.0.12');
END;
GO

COMMIT;
GO
