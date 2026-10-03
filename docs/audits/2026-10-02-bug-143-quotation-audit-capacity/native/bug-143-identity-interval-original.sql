SET NOCOUNT ON; SET ANSI_WARNINGS ON; SET XACT_ABORT OFF;
IF DB_NAME()<>N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'36ef9cae14704ac88eb994416f628e24')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Exact owned backup required.',1;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>139 OR COL_LENGTH(N'dbo.T_Quotation',N'Creator')<>-1
 OR OBJECT_ID(N'dbo.T_Quotation_Bug143IdentityOriginal') IS NOT NULL
 THROW 51009,'Original Core139/max column and unused interval fixture name required.',1;
DECLARE @originalObject int=OBJECT_ID(N'dbo.T_Quotation',N'U'),@error int=NULL,@maxLength smallint=NULL,@nullable bit=NULL;
BEGIN TRY
 BEGIN TRANSACTION;
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
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''''
   OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N''%[^A-Za-z0-9_]%''
   OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                  WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
    THROW 51043, N''BUG143 dbo.T_Quotation.Creator has an unknown definition or collation; existing column preserved.'', 1;

-- Explicit interval injection after actual initial metadata guard, before its data lock.
-- This is not a claim that a real two-session schema race was run.
EXEC sys.sp_rename N''dbo.T_Quotation'',N''T_Quotation_Bug143IdentityOriginal'',N''OBJECT'';
CREATE TABLE dbo.T_Quotation(Id uniqueidentifier NOT NULL PRIMARY KEY,Creator nvarchar(300) NOT NULL);
INSERT dbo.T_Quotation(Id,Creator) VALUES(''14300000-0000-0000-0000-0000000000b1'',N''identity fixture'');
IF OBJECT_ID(N''dbo.T_Quotation'',N''U'')=@cp6_object_id
 OR OBJECT_ID(N''dbo.T_Quotation_Bug143IdentityOriginal'',N''U'')<>@cp6_object_id
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.T_Quotation'') AND name=N''Creator'' AND max_length=600 AND is_nullable=0)
 THROW 51009,''Exact old identity/new unknown definition interval fixture was not established.'',1;
PRINT ''BUG143_IDENTITY_INTERVAL_FIXTURE_ESTABLISHED'';
IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    -- Bind the data column only after the metadata guard; a missing column must
    -- reach 51043 rather than static batch compilation failing with SQL 207.
    DECLARE @cp6_overlong bit = 0;
    DECLARE @cp6_check nvarchar(max) = N''SELECT @overlong = CASE WHEN EXISTS (
        SELECT 1 FROM [dbo].[T_Quotation] WITH (TABLOCKX, HOLDLOCK)
        WHERE DATALENGTH([Creator]) > 200) THEN 1 ELSE 0 END;'';
    EXEC sys.sp_executesql @cp6_check, N''@overlong bit OUTPUT'', @overlong = @cp6_overlong OUTPUT;
    IF @cp6_overlong = 1
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
        + @cp6_collation + N'' NULL;'';
    EXEC sys.sp_executesql @cp6_alter;
END;
';
 END TRY
 BEGIN CATCH
  SET @error=ERROR_NUMBER();
 END CATCH;
 IF @error=51009 THROW 51009,'Interval fixture setup failed, not a guard result.',1;
 SELECT @maxLength=max_length,@nullable=is_nullable FROM sys.columns
 WHERE object_id=OBJECT_ID(N'dbo.T_Quotation',N'U') AND name=N'Creator';
 IF @maxLength IS NULL THROW 51009,'Interval replacement metadata not available.',1;
 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
IF OBJECT_ID(N'dbo.T_Quotation',N'U')<>@originalObject OR OBJECT_ID(N'dbo.T_Quotation_Bug143IdentityOriginal') IS NOT NULL
 OR COL_LENGTH(N'dbo.T_Quotation',N'Creator')<>-1 OR (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>139
 THROW 51009,'Original table identity/column/history must be restored.',1;
SELECT @error AS ActualGuardError,51043 AS ExpectedGuardError,@maxLength AS ReplacementColumnBytes,@nullable AS ReplacementColumnNullable,
 CONVERT(bit,CASE WHEN @error=51043 AND @maxLength=600 AND @nullable=0 THEN 1 ELSE 0 END) AS UnknownReplacementRejectedAndPreserved,
 CONVERT(bit,1) AS OriginalIdentityColumnAndHistoryRestored
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;