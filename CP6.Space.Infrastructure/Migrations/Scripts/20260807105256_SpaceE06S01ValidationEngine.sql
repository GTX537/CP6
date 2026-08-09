BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [Category] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [EvidenceJson] nvarchar(max) NOT NULL DEFAULT N'{}';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [FieldPath] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [ValidationRunId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    CREATE TABLE [Space_ValidationRun] (
        [Id] uniqueidentifier NOT NULL,
        [ModelVersionId] uniqueidentifier NOT NULL,
        [ContentRevision] bigint NOT NULL,
        [ContentHash] char(64) NOT NULL,
        [RuleSetVersion] nvarchar(50) NOT NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [CapabilityHash] char(64) NOT NULL,
        [Status] smallint NOT NULL,
        [BlockingCount] int NOT NULL,
        [WarningCount] int NOT NULL,
        [InfoCount] int NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [RequestedBy] uniqueidentifier NOT NULL,
        [StartedAtUtc] datetime2 NULL,
        [FinishedAtUtc] datetime2 NULL,
        [JobId] uniqueidentifier NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [FailureCode] nvarchar(100) NULL,
        [FailureSummary] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_ValidationRun] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_ValidationRun_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_ValidationRun_Counts] CHECK ([BlockingCount] >= 0 AND [WarningCount] >= 0 AND [InfoCount] >= 0 AND ([Status] <> 2 OR [BlockingCount] = 0) AND ([Status] <> 3 OR [BlockingCount] > 0)),
        CONSTRAINT [CK_Space_ValidationRun_StatusTime] CHECK (([Status] = 0 AND [StartedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([Status] IN (2, 3, 4) AND [FinishedAtUtc] IS NOT NULL)),
        CONSTRAINT [FK_Space_ValidationRun_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_ValidationRun_Version_Tenant] FOREIGN KEY ([TenantId], [ModelVersionId]) REFERENCES [Space_ModelVersion] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelIssue_Tenant_Validation_Severity_Code] ON [Space_ModelIssue] ([TenantId], [ValidationRunId], [Severity], [Code], [Id]) WHERE [ValidationRunId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [CK_Space_ModelIssue_ValidationScope] CHECK ([ValidationRunId] IS NULL OR ([ModelVersionId] IS NOT NULL AND [JobId] IS NOT NULL))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    CREATE INDEX [IX_Space_ValidationRun_Tenant_Version_Requested] ON [Space_ValidationRun] ([TenantId], [ModelVersionId], [RequestedAtUtc], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_ValidationRun_Tenant_Input_ActiveOrReusable] ON [Space_ValidationRun] ([TenantId], [ModelVersionId], [ContentHash], [RuleSetVersion], [AdapterId], [CapabilityHash]) WHERE [Status] <> 4 AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_ValidationRun_Tenant_Job] ON [Space_ValidationRun] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_ValidationRun_Tenant] FOREIGN KEY ([TenantId], [ValidationRunId]) REFERENCES [Space_ValidationRun] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807105256_SpaceE06S01ValidationEngine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260807105256_SpaceE06S01ValidationEngine', N'8.0.12');
END;
GO

COMMIT;
GO
