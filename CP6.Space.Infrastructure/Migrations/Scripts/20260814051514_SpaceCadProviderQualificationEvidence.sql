BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [DataRegionApproved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [DeletionRetentionApproved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [FrozenEnvironmentSha256] varchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [GoldenDatasetSha256] varchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [LicensingApproved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [QualificationEvidenceReference] varchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [QualificationRubricVersion] varchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [QualificationScore] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    ALTER TABLE [Space_CadSiteProviderCertification] ADD [SecurityApproved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_CadSiteProviderCertification] ADD CONSTRAINT [CK_Space_CadProviderCertification_QualificationScore] CHECK ([QualificationScore] IS NULL OR ([QualificationScore] >= 0 AND [QualificationScore] <= 100))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260814051514_SpaceCadProviderQualificationEvidence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260814051514_SpaceCadProviderQualificationEvidence', N'8.0.12');
END;
GO

COMMIT;
GO
