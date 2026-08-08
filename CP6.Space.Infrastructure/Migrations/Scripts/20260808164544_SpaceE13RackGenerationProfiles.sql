BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    CREATE TABLE [Space_RackGenerationProfile] (
        [Id] uniqueidentifier NOT NULL,
        [Scope] smallint NOT NULL,
        [OwnerTenantId] uniqueidentifier NOT NULL,
        [ProfileCode] nvarchar(100) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Status] smallint NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackGenerationProfile] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackGenerationProfile_Scope_Owner_Id] UNIQUE ([Scope], [OwnerTenantId], [Id]),
        CONSTRAINT [CK_Space_RackGenerationProfile_ScopeOwner] CHECK (([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000'))
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    CREATE TABLE [Space_RackGenerationProfileVersion] (
        [Id] uniqueidentifier NOT NULL,
        [Scope] smallint NOT NULL,
        [OwnerTenantId] uniqueidentifier NOT NULL,
        [ProfileId] uniqueidentifier NOT NULL,
        [VersionNo] bigint NOT NULL,
        [RackWidthMillimeters] int NOT NULL,
        [RackDepthMillimeters] int NOT NULL,
        [RackHeightMillimeters] int NOT NULL,
        [LevelsJson] nvarchar(max) NOT NULL,
        [LocationCount] bigint NOT NULL,
        [ContentHash] char(64) NOT NULL,
        [Status] smallint NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_RackGenerationProfileVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_RackGenerationProfileVersion_Scope_Owner_Id] UNIQUE ([Scope], [OwnerTenantId], [Id]),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_Dimensions] CHECK ([RackWidthMillimeters] > 0 AND [RackDepthMillimeters] > 0 AND [RackHeightMillimeters] > 0),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_LocationCount] CHECK ([LocationCount] > 0 AND [LocationCount] <= 10000000),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_ScopeOwner] CHECK (([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')),
        CONSTRAINT [CK_Space_RackGenerationProfileVersion_VersionNo] CHECK ([VersionNo] > 0),
        CONSTRAINT [FK_Space_RackGenerationProfileVersion_Profile_Scope_Owner] FOREIGN KEY ([Scope], [OwnerTenantId], [ProfileId]) REFERENCES [Space_RackGenerationProfile] ([Scope], [OwnerTenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_RackGenerationProfile_Scope_Owner_Code_Active] ON [Space_RackGenerationProfile] ([Scope], [OwnerTenantId], [ProfileCode]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_RackGenerationProfileVersion_Scope_Owner_Profile_VersionNo] ON [Space_RackGenerationProfileVersion] ([Scope], [OwnerTenantId], [ProfileId], [VersionNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260808164544_SpaceE13RackGenerationProfiles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260808164544_SpaceE13RackGenerationProfiles', N'8.0.12');
END;
GO

COMMIT;
GO

