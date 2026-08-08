BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    ALTER TABLE [Space_File] ADD [ContentDeletedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    ALTER TABLE [Space_File] ADD [DeletionRequestedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    ALTER TABLE [Space_File] ADD [RetainUntilUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    UPDATE [Space_File]
    SET [DeletionRequestedAtUtc] =
        COALESCE([ModifiedAtUtc], [CreatedAtUtc], SYSUTCDATETIME())
    WHERE ([State] = 5 OR [IsDeleted] = 1)
      AND [DeletionRequestedAtUtc] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_File_Tenant_PendingObjectDeletion] ON [Space_File] ([TenantId], [DeletionRequestedAtUtc], [ContentDeletedAtUtc]) WHERE [State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [ContentDeletedAtUtc] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_File_Tenant_Retention] ON [Space_File] ([TenantId], [RetainUntilUtc], [State]) WHERE [RetainUntilUtc] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_File] ADD CONSTRAINT [CK_Space_File_ContentDeletion] CHECK ([ContentDeletedAtUtc] IS NULL OR ([State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [IsDeleted] = 1))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730152005_SpaceE01S06FileSafetyRetention'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260730152005_SpaceE01S06FileSafetyRetention', N'8.0.12');
END;
GO

COMMIT;
GO
