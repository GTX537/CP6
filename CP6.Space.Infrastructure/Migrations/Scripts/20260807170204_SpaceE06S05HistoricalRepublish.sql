BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE TABLE [Space_HistoricalRepublish] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [ModelId] uniqueidentifier NOT NULL,
        [HistoricalVersionId] uniqueidentifier NOT NULL,
        [ExpectedPublishedVersionId] uniqueidentifier NOT NULL,
        [TargetVersionId] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [ValidationRunId] uniqueidentifier NULL,
        [PublishAttemptId] uniqueidentifier NULL,
        [BusinessIdempotencyKey] nvarchar(128) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [ApprovalReference] nvarchar(500) NULL,
        [RequestedBy] uniqueidentifier NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [Status] smallint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_HistoricalRepublish] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_HistoricalRepublish_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_HistoricalRepublish_Status] CHECK ([Status] IN (0, 1, 2, 3, 4)),
        CONSTRAINT [FK_Space_HistoricalRepublish_ExpectedVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [ExpectedPublishedVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_HistoricalVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [HistoricalVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_Model_Tenant] FOREIGN KEY ([TenantId], [ModelId]) REFERENCES [Space_Model] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_PublishAttempt_Tenant] FOREIGN KEY ([TenantId], [PublishAttemptId]) REFERENCES [Space_PublishAttempt] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_TargetVersion_Tenant] FOREIGN KEY ([TenantId], [ModelId], [TargetVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [ModelId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_HistoricalRepublish_Validation_Tenant] FOREIGN KEY ([TenantId], [ValidationRunId]) REFERENCES [Space_ValidationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_Tenant_Site_Requested] ON [Space_HistoricalRepublish] ([TenantId], [SiteId], [RequestedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_JobId] ON [Space_HistoricalRepublish] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ModelId_ExpectedPublishedVersionId] ON [Space_HistoricalRepublish] ([TenantId], [ModelId], [ExpectedPublishedVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ModelId_HistoricalVersionId] ON [Space_HistoricalRepublish] ([TenantId], [ModelId], [HistoricalVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ModelId_TargetVersionId] ON [Space_HistoricalRepublish] ([TenantId], [ModelId], [TargetVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE INDEX [IX_Space_HistoricalRepublish_TenantId_ValidationRunId] ON [Space_HistoricalRepublish] ([TenantId], [ValidationRunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_HistoricalRepublish_Tenant_Idempotency] ON [Space_HistoricalRepublish] ([TenantId], [BusinessIdempotencyKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_HistoricalRepublish_Tenant_PublishAttempt] ON [Space_HistoricalRepublish] ([TenantId], [PublishAttemptId]) WHERE [PublishAttemptId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807170204_SpaceE06S05HistoricalRepublish'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260807170204_SpaceE06S05HistoricalRepublish', N'8.0.12');
END;
GO

COMMIT;
GO
