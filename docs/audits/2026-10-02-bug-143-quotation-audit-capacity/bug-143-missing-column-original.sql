SET NOCOUNT ON;
SET XACT_ABORT OFF;
IF DB_NAME()<>N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'36ef9cae14704ac88eb994416f628e24')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Exact owned backup required.',1;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>139 OR COL_LENGTH(N'dbo.T_Quotation',N'Creator')<>-1
 THROW 51005,'Exact original Core139 and Creator metadata required.',1;
DECLARE @number int=NULL,@fixtureEstablished bit=0;
BEGIN TRY
 BEGIN TRANSACTION;
 ALTER TABLE dbo.T_Quotation DROP COLUMN Creator;
 IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.T_Quotation') AND name=N'Creator')
  THROW 51005,'Missing-column fixture not established.',1;
 SET @fixtureEstablished=1;
 BEGIN TRY
  EXEC sys.sp_executesql N'
DECLARE @cp6_object_id int = OBJECT_ID(N''[dbo].[T_Quotation]'', N''U'');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N''dbo'')
      AND name COLLATE Latin1_General_100_BIN2 = N''T_Quotation'' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N''BUG143 missing exact dbo.T_Quotation; audit capacity repair refused.'', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N''Creator'' COLLATE Latin1_General_100_BIN2
  AND t.name = N''nvarchar'' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N''nvarchar'')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N'''' OR QUOTENAME(@cp6_collation) IS NULL
    THROW 51043, N''BUG143 dbo.T_Quotation.Creator has an unknown definition or collation; existing column preserved.'', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    IF EXISTS (SELECT 1 FROM [dbo].[T_Quotation] WITH (TABLOCKX, HOLDLOCK)
               WHERE DATALENGTH([Creator]) > 200)
        THROW 51043, N''BUG143 dbo.T_Quotation.Creator contains more than 100 UTF-16 units; no data changed.'', 1;

    -- Revalidate after acquiring the data lock, before using the captured collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N''Creator'' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N''nvarchar'') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N''BUG143 dbo.T_Quotation.Creator metadata changed; audit capacity repair refused.'', 1;

    DECLARE @cp6_alter nvarchar(max) = N''ALTER TABLE [dbo].[T_Quotation] ALTER COLUMN [Creator] nvarchar(100) COLLATE ''
        + QUOTENAME(@cp6_collation) + N'' NULL;'';
    EXEC sys.sp_executesql @cp6_alter;
END;
';
 END TRY
 BEGIN CATCH
  SET @number=ERROR_NUMBER();
 END CATCH;
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
IF COL_LENGTH(N'dbo.T_Quotation',N'Creator')<>-1 OR (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>139
 THROW 51005,'Original column/history was not restored.',1;
SELECT @fixtureEstablished AS FixtureEstablished,@number AS ActualGuardError,51043 AS ExpectedGuardError,
 CONVERT(bit,CASE WHEN @number=51043 THEN 1 ELSE 0 END) AS ExpectedErrorObserved,
 CONVERT(bit,1) AS OriginalColumnAndHistoryRestored
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;