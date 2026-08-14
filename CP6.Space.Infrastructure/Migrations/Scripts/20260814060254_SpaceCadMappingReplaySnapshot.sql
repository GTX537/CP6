BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814060254_SpaceCadMappingReplaySnapshot'
)
BEGIN
    ALTER TABLE [Space_CadParsePreparation] ADD [MappingReplaySnapshotJson] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814060254_SpaceCadMappingReplaySnapshot'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260814060254_SpaceCadMappingReplaySnapshot', N'8.0.12');
END;
GO

COMMIT;
GO
