BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814063519_SpaceCadProviderVersionFence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [ProviderVersion] varchar(100) NOT NULL DEFAULT '';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814063519_SpaceCadProviderVersionFence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260814063519_SpaceCadProviderVersionFence', N'8.0.12');
END;
GO

COMMIT;
GO
