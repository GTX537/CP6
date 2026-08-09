SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51080, 'Run the CP6 main-sync preflight against the application database only.', 1;

IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL
    THROW 51081, 'CP6 migration history is missing; restore the approved main database baseline first.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714075419_WfsSubFlow'
)
    THROW 51082, 'Required origin/main migration 20260714075419_WfsSubFlow is missing.', 1;

IF OBJECT_ID(N'[Space_Model]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[__EFMigrationsHistory_Space]', N'U') IS NULL
    THROW 51083, 'Space tables exist without the Space migration history table; reconcile schema drift before deployment.', 1;

IF OBJECT_ID(N'[Space_ElementRevision]', N'U') IS NOT NULL
   AND COL_LENGTH(N'Space_ElementRevision', N'ModelAssetId') IS NOT NULL
BEGIN
    EXEC(N'
        IF EXISTS (
            SELECT 1
            FROM [Space_ElementRevision]
            WHERE [ModelAssetId] IS NOT NULL
        )
            THROW 51000, ''Audit and clear every legacy Space_ElementRevision.ModelAssetId before E05-S04.'', 1;
    ');
END;

IF OBJECT_ID(N'[Space_PublishAttempt]', N'U') IS NOT NULL
   AND COL_LENGTH(N'Space_PublishAttempt', N'OwnsPublishSlot') IS NOT NULL
   AND COL_LENGTH(N'Space_PublishAttempt', N'IsDeleted') IS NOT NULL
BEGIN
    EXEC(N'
        IF EXISTS (
            SELECT 1
            FROM [Space_PublishAttempt]
            WHERE [OwnsPublishSlot] = 1
              AND [IsDeleted] = 0
        )
            THROW 51020, ''Resolve every active E06-S03 publish attempt before E06-S04.'', 1;
    ');
END;

SELECT
    DB_NAME() AS [DatabaseName],
    (SELECT COUNT_BIG(*) FROM [__EFMigrationsHistory]) AS [CoreMigrationCount],
    CASE
        WHEN OBJECT_ID(N'[__EFMigrationsHistory_Space]', N'U') IS NULL THEN 0
        ELSE 1
    END AS [SpaceHistoryPresent],
    N'PASS' AS [PreflightStatus];
