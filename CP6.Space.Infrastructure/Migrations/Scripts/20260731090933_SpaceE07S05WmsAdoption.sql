BEGIN TRANSACTION;
GO

CREATE TABLE [Space_WmsAdoption] (
    [Id] uniqueidentifier NOT NULL,
    [SiteId] uniqueidentifier NOT NULL,
    [AdapterId] nvarchar(100) NOT NULL,
    [DataSource] nvarchar(100) NOT NULL,
    [DataSourceKind] nvarchar(20) NOT NULL,
    [WmsLogicalId] uniqueidentifier NOT NULL,
    [ExternalLocationId] nvarchar(200) NULL,
    [WmsLocationCode] nvarchar(200) NOT NULL,
    [WmsIsActive] bit NOT NULL,
    [ExternalVersion] nvarchar(100) NOT NULL,
    [WmsStateHash] char(64) NOT NULL,
    [LastObservedAtUtc] datetime2 NOT NULL,
    [Status] smallint NOT NULL,
    [ModelVersionId] uniqueidentifier NULL,
    [LocationLogicalId] uniqueidentifier NULL,
    [BoundLocationCode] nvarchar(200) NULL,
    [BoundAtUtc] datetime2 NULL,
    [RowVersion] rowversion NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] uniqueidentifier NULL,
    [ModifiedAtUtc] datetime2 NULL,
    [ModifiedBy] uniqueidentifier NULL,
    [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
    CONSTRAINT [PK_Space_WmsAdoption] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_Space_WmsAdoption_TenantId_Id] UNIQUE ([TenantId], [Id]),
    CONSTRAINT [FK_Space_WmsAdoption_ModelVersion_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Space_WmsAdoption_Tenant_Site_Adapter_Status_Code] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [Status], [WmsLocationCode]);
GO

CREATE INDEX [IX_Space_WmsAdoption_TenantId_ModelVersionId] ON [Space_WmsAdoption] ([TenantId], [ModelVersionId]);
GO

CREATE UNIQUE INDEX [UX_Space_WmsAdoption_Tenant_Site_Adapter_External] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [ExternalLocationId]) WHERE [ExternalLocationId] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [UX_Space_WmsAdoption_Tenant_Site_Adapter_Location] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [LocationLogicalId]) WHERE [LocationLogicalId] IS NOT NULL AND [IsDeleted] = 0;
GO

CREATE UNIQUE INDEX [UX_Space_WmsAdoption_Tenant_Site_Adapter_WmsLogical] ON [Space_WmsAdoption] ([TenantId], [SiteId], [AdapterId], [WmsLogicalId]) WHERE [IsDeleted] = 0;
GO

INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
VALUES (N'20260731090933_SpaceE07S05WmsAdoption', N'8.0.12');
GO

COMMIT;
GO

