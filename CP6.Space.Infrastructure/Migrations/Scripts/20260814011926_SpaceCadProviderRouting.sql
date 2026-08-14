BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    ALTER TABLE [Space_CadParsePreparation] ADD [ProviderKey] varchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    ALTER TABLE [Space_CadParsePreparation] ADD [ProviderVersion] varchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    CREATE TABLE [Space_CadSiteProviderConfiguration] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ConfigurationRevision] bigint NOT NULL,
        [IsCurrent] bit NOT NULL,
        [ChangeReason] nvarchar(500) NOT NULL,
        [ApprovedAtUtc] datetime2 NOT NULL,
        [ApprovedBy] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_CadSiteProviderConfiguration] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_CadSiteProviderConfiguration_TenantId_Id] UNIQUE ([TenantId], [Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    CREATE TABLE [Space_CadSiteProviderCertification] (
        [Id] uniqueidentifier NOT NULL,
        [ConfigurationId] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ProviderKey] varchar(64) NOT NULL,
        [Role] smallint NOT NULL,
        [DeploymentMode] smallint NOT NULL,
        [DataBoundary] smallint NOT NULL,
        [ApprovalEvidenceReference] varchar(500) NOT NULL,
        [SecretReference] varchar(256) NULL,
        [ValidFromUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [SupportsDwg] bit NOT NULL,
        [SupportsDxf] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_CadSiteProviderCertification] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Space_CadProviderCertification_Configuration_Tenant] FOREIGN KEY ([TenantId], [ConfigurationId]) REFERENCES [Space_CadSiteProviderConfiguration] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    CREATE INDEX [IX_Space_CadProviderCertification_Site_Expiry] ON [Space_CadSiteProviderCertification] ([TenantId], [SiteId], [ExpiresAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_CadProviderCertification_Provider] ON [Space_CadSiteProviderCertification] ([TenantId], [ConfigurationId], [ProviderKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_CadProviderCertification_Role] ON [Space_CadSiteProviderCertification] ([TenantId], [ConfigurationId], [Role]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_CadProviderConfiguration_Current] ON [Space_CadSiteProviderConfiguration] ([TenantId], [SiteId]) WHERE [IsCurrent] = 1 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_CadProviderConfiguration_Site_Revision] ON [Space_CadSiteProviderConfiguration] ([TenantId], [SiteId], [ConfigurationRevision]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814011926_SpaceCadProviderRouting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260814011926_SpaceCadProviderRouting', N'8.0.12');
END;
GO

COMMIT;
GO
