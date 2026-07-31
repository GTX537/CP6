BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    ALTER TABLE [Space_RackLevelRevision] DROP CONSTRAINT [CK_Space_RackLevelRevision_Dimensions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    ALTER TABLE [Space_RackLevelRevision] ADD [BeamHeight] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_RackLevelRevision] ADD CONSTRAINT [CK_Space_RackLevelRevision_Dimensions] CHECK ([LevelNo] > 0 AND [BottomZ] >= 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND [BeamHeight] >= 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260731001924_SpaceE05S02RackLevelSpecification'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260731001924_SpaceE05S02RackLevelSpecification', N'8.0.12');
END;
GO

COMMIT;
GO
