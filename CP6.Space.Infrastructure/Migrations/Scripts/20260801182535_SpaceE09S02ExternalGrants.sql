BEGIN TRANSACTION;
GO

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrant] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [FieldPolicyId] uniqueidentifier NULL,
        [CanExport] bit NOT NULL,
        [ValidFromUtc] datetime2 NOT NULL,
        [ValidToUtc] datetime2 NULL,
        [Status] smallint NOT NULL,
        [GrantVersion] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExternalGrant] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrant_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExternalGrant_Status] CHECK ([Status] >= 0 AND [Status] <= 2),
        CONSTRAINT [CK_Space_ExternalGrant_Validity] CHECK ([ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]),
        CONSTRAINT [CK_Space_ExternalGrant_Version] CHECK ([GrantVersion] > 0),
        CONSTRAINT [FK_Space_ExternalGrant_Organization_Tenant] FOREIGN KEY ([TenantId], [OrganizationId]) REFERENCES [Space_ExternalOrganization] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantFloor] (
        [Id] uniqueidentifier NOT NULL,
        [FloorLogicalId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantFloor] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantFloor_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantFloor_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantObject] (
        [Id] uniqueidentifier NOT NULL,
        [BusinessObjectType] nvarchar(50) NOT NULL,
        [NormalizedBusinessObjectType] varchar(50) NOT NULL,
        [BusinessObjectId] nvarchar(200) NOT NULL,
        [NormalizedBusinessObjectId] varchar(200) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantObject] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantObject_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantObject_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantOwner] (
        [Id] uniqueidentifier NOT NULL,
        [OwnerId] nvarchar(100) NOT NULL,
        [NormalizedOwnerId] varchar(100) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantOwner] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantOwner_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantOwner_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE TABLE [Space_ExternalGrantZone] (
        [Id] uniqueidentifier NOT NULL,
        [ZoneLogicalId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GrantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_ExternalGrantZone] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalGrantZone_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Space_ExternalGrantZone_Grant_Tenant] FOREIGN KEY ([TenantId], [GrantId]) REFERENCES [Space_ExternalGrant] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalGrant_Organization_Site_Status] ON [Space_ExternalGrant] ([TenantId], [OrganizationId], [SiteId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalGrant_Organization_Status_Validity] ON [Space_ExternalGrant] ([TenantId], [OrganizationId], [Status], [ValidFromUtc], [ValidToUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantFloor_Current] ON [Space_ExternalGrantFloor] ([TenantId], [GrantId], [FloorLogicalId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantObject_Current] ON [Space_ExternalGrantObject] ([TenantId], [GrantId], [NormalizedBusinessObjectType], [NormalizedBusinessObjectId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantOwner_Current] ON [Space_ExternalGrantOwner] ([TenantId], [GrantId], [NormalizedOwnerId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalGrantZone_Current] ON [Space_ExternalGrantZone] ([TenantId], [GrantId], [ZoneLogicalId]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801182535_SpaceE09S02ExternalGrants'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260801182535_SpaceE09S02ExternalGrants', N'8.0.12');
END;
GO

COMMIT;
GO
