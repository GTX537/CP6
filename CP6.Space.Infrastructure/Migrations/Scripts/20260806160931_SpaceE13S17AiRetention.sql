BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [PayloadPurgedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [PayloadPurgedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_GenerationRun] ADD [RetentionHoldUntilUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_GenerationProposal] ADD [PayloadPurgedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    ALTER TABLE [Space_AiUsageRecord] ADD [ArchivedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ModelIssue_Tenant_Purge_Run] ON [Space_ModelIssue] ([TenantId], [PayloadPurgedAtUtc], [GenerationRunId], [Id]) WHERE [GenerationRunId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_GenerationRun_Tenant_Retention] ON [Space_GenerationRun] ([TenantId], [PayloadPurgedAtUtc], [IsCurrent], [Status], [CreatedAtUtc], [Id]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Proposal_Tenant_Purge_Run] ON [Space_GenerationProposal] ([TenantId], [PayloadPurgedAtUtc], [RunId], [Id]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_AiUsage_Tenant_Retention] ON [Space_AiUsageRecord] ([TenantId], [ArchivedAtUtc], [RecordedAtUtc], [Id]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806160931_SpaceE13S17AiRetention'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260806160931_SpaceE13S17AiRetention', N'8.0.12');
END;
GO

COMMIT;
GO

