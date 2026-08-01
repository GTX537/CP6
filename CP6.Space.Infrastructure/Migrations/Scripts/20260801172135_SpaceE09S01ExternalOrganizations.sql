BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE TABLE [Space_ExternalOrganization] (
        [Id] uniqueidentifier NOT NULL,
        [Type] smallint NOT NULL,
        [BusinessPartnerType] varchar(50) NULL,
        [BusinessPartnerId] uniqueidentifier NULL,
        [Code] nvarchar(50) NOT NULL,
        [NormalizedCode] varchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Status] smallint NOT NULL,
        [SecurityStamp] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExternalOrganization] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalOrganization_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExternalOrganization_BusinessPartner] CHECK (([BusinessPartnerType] IS NULL AND [BusinessPartnerId] IS NULL) OR ([BusinessPartnerType] IS NOT NULL AND [BusinessPartnerId] IS NOT NULL)),
        CONSTRAINT [CK_Space_ExternalOrganization_Status] CHECK ([Status] >= 0 AND [Status] <= 2),
        CONSTRAINT [CK_Space_ExternalOrganization_Type] CHECK ([Type] >= 0 AND [Type] <= 2)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE TABLE [Space_ExternalMembership] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Role] smallint NOT NULL,
        [ValidFromUtc] datetime2 NOT NULL,
        [ValidToUtc] datetime2 NULL,
        [Status] smallint NOT NULL,
        [InvitedBy] uniqueidentifier NULL,
        [AcceptedAtUtc] datetime2 NULL,
        [SecurityStamp] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ExternalMembership] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ExternalMembership_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ExternalMembership_Role] CHECK ([Role] >= 0 AND [Role] <= 2),
        CONSTRAINT [CK_Space_ExternalMembership_Status] CHECK ([Status] >= 0 AND [Status] <= 3),
        CONSTRAINT [CK_Space_ExternalMembership_Validity] CHECK ([ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]),
        CONSTRAINT [FK_Space_ExternalMembership_Organization_Tenant] FOREIGN KEY ([TenantId], [OrganizationId]) REFERENCES [Space_ExternalOrganization] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalMembership_Tenant_User_Status_Validity] ON [Space_ExternalMembership] ([TenantId], [UserId], [Status], [ValidFromUtc], [ValidToUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalMembership_Tenant_Organization_User_Current] ON [Space_ExternalMembership] ([TenantId], [OrganizationId], [UserId]) WHERE [Status] <> 3 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    CREATE INDEX [IX_Space_ExternalOrganization_Tenant_Status_Name] ON [Space_ExternalOrganization] ([TenantId], [Status], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalOrganization_Tenant_Type_Code] ON [Space_ExternalOrganization] ([TenantId], [Type], [NormalizedCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ExternalOrganization_Tenant_Type_Partner] ON [Space_ExternalOrganization] ([TenantId], [Type], [BusinessPartnerType], [BusinessPartnerId]) WHERE [BusinessPartnerId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260801172135_SpaceE09S01ExternalOrganizations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260801172135_SpaceE09S01ExternalOrganizations', N'8.0.12');
END;
GO

COMMIT;
GO
