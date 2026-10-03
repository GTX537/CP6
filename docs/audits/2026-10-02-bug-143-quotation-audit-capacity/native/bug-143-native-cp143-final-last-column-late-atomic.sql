SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
IF DB_NAME()<>N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'36ef9cae14704ac88eb994416f628e24')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Exact owned SQL36ef backup required before any fixture.',1;
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>140
 OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261002193500_RestoreQuotationAuditColumnCapacity')
 THROW 51005,'Exact canonical Core140 required before any guard fixture.',1;
DECLARE @cp6_fixture_action nvarchar(20)=N'cleanup';
DECLARE @cp6_provided TABLE(TableName sysname,ColumnName sysname,TypeName sysname,Bytes int,Nullable bit,
 PRIMARY KEY(TableName,ColumnName));
INSERT @cp6_provided VALUES
 (N'T_Quotation',N'Id',N'uniqueidentifier',16,0),(N'T_Quotation',N'QtnNo',N'nvarchar',40,0),
 (N'T_Quotation',N'QtnNoMain',N'int',4,0),(N'T_Quotation',N'QtnNoBranch',N'int',4,0),
 (N'T_Quotation',N'BaseCd',N'nvarchar',20,0),(N'T_Quotation',N'StaffCd',N'nvarchar',20,0),
 (N'T_Quotation',N'CustomerCd',N'nvarchar',40,0),(N'T_Quotation',N'PrintTotalFlg',N'bit',1,0),
 (N'T_Quotation',N'EstimateCheckFlg',N'int',4,0),(N'T_Quotation',N'MasterConfirmFlg',N'int',4,0),
 (N'T_Quotation',N'IsDeleted',N'bit',1,0),(N'T_Quotation',N'CreateDate',N'datetime2',8,0),
 (N'T_Quotation',N'TenantId',N'uniqueidentifier',16,0),
 (N'T_Quotation',N'Creator',N'nvarchar',-2,1),(N'T_Quotation',N'Modifier',N'nvarchar',-2,1),
 (N'T_QuotationCalc',N'Id',N'uniqueidentifier',16,0),(N'T_QuotationCalc',N'QtnNo',N'nvarchar',40,0),
 (N'T_QuotationCalc',N'QtnCalcNo',N'nvarchar',40,0),(N'T_QuotationCalc',N'EstimateCheckFlg',N'int',4,0),
 (N'T_QuotationCalc',N'MasterConfirmFlg',N'int',4,0),(N'T_QuotationCalc',N'IsDeleted',N'bit',1,0),
 (N'T_QuotationCalc',N'CreateDate',N'datetime2',8,0),(N'T_QuotationCalc',N'TenantId',N'uniqueidentifier',16,0),
 (N'T_QuotationCalc',N'Creator',N'nvarchar',-2,1),(N'T_QuotationCalc',N'Modifier',N'nvarchar',-2,1),
 (N'T_QuotationDetail',N'Id',N'uniqueidentifier',16,0),(N'T_QuotationDetail',N'QtnNo',N'nvarchar',40,0),
 (N'T_QuotationDetail',N'DetailNo',N'int',4,0),(N'T_QuotationDetail',N'PrintTotalFlg',N'bit',1,0),
 (N'T_QuotationDetail',N'IsDeleted',N'bit',1,0),(N'T_QuotationDetail',N'CreateDate',N'datetime2',8,0),
 (N'T_QuotationDetail',N'TenantId',N'uniqueidentifier',16,0),
 (N'T_QuotationDetail',N'Creator',N'nvarchar',-2,1),(N'T_QuotationDetail',N'Modifier',N'nvarchar',-2,1);
IF EXISTS(
 SELECT 1 FROM @cp6_provided expected
 LEFT JOIN sys.tables tab ON tab.schema_id=SCHEMA_ID(N'dbo') AND tab.name=expected.TableName
 LEFT JOIN sys.columns col ON col.object_id=tab.object_id AND col.name=expected.ColumnName
 LEFT JOIN sys.types typ ON typ.user_type_id=col.user_type_id
 WHERE col.column_id IS NULL OR typ.name<>expected.TypeName OR typ.is_user_defined<>0
  OR col.is_nullable<>expected.Nullable OR col.is_identity<>0 OR col.is_computed<>0
  OR col.generated_always_type<>0 OR col.encryption_type IS NOT NULL
  OR (expected.Bytes<>-2 AND col.max_length<>expected.Bytes)
  OR (expected.Bytes=-2 AND col.max_length<>CASE WHEN @cp6_fixture_action=N'cleanup' THEN 200 ELSE -1 END))
 THROW 51005,'Required fixture column definitions differ; no fixture DML attempted.',1;
IF EXISTS(
 SELECT 1 FROM sys.tables tab JOIN sys.columns col ON col.object_id=tab.object_id
 WHERE tab.schema_id=SCHEMA_ID(N'dbo') AND tab.name IN(N'T_Quotation',N'T_QuotationCalc',N'T_QuotationDetail')
  AND col.is_nullable=0 AND col.default_object_id=0 AND col.is_identity=0 AND col.is_computed=0
  AND col.generated_always_type=0 AND col.system_type_id<>TYPE_ID(N'timestamp')
  AND NOT EXISTS(SELECT 1 FROM @cp6_provided expected WHERE expected.TableName=tab.name AND expected.ColumnName=col.name))
 THROW 51005,'An additional required column has no default; fixture must be reviewed rather than guessed.',1;
IF (SELECT COUNT(*) FROM sys.columns col JOIN sys.tables tab ON tab.object_id=col.object_id
 WHERE tab.schema_id=SCHEMA_ID(N'dbo') AND tab.name IN(N'T_Quotation',N'T_QuotationCalc',N'T_QuotationDetail')
  AND col.name=N'RowVersion' AND col.system_type_id=TYPE_ID(N'timestamp') AND col.max_length=8)<>3
 THROW 51005,'Three native generated rowversion columns required; tokens must be omitted from INSERT.',1;


CREATE TABLE #cp6_bug143_case(Done bit NOT NULL,ActualError int NOT NULL,ActualOrdinal int NOT NULL,PrefixProved bit NOT NULL,SetupError int NOT NULL);
INSERT #cp6_bug143_case VALUES(0,0,0,0,0);
CREATE TABLE #cp6_bug143_columns(TableName sysname,ColumnName sysname,ColumnId int,CollationName sysname);
INSERT #cp6_bug143_columns
 SELECT tab.name,col.name,col.column_id,col.collation_name FROM sys.tables tab JOIN sys.columns col ON col.object_id=tab.object_id
 WHERE tab.schema_id=SCHEMA_ID(N'dbo') AND tab.name IN(N'T_Quotation',N'T_QuotationCalc',N'T_QuotationDetail') AND col.name IN(N'Creator',N'Modifier');
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @cp6_qtn_id uniqueidentifier='14399999-0000-0000-0000-000000000001';
DECLARE @cp6_calc_id uniqueidentifier='14399999-0000-0000-0000-000000000002';
DECLARE @cp6_detail_id uniqueidentifier='14399999-0000-0000-0000-000000000003';
DECLARE @cp6_tenant uniqueidentifier='14399999-0000-0000-0000-0000000000a1';
DECLARE @cp6_qtn_no nvarchar(20)=N'CP6-BUG143-GQ-01';
DECLARE @cp6_calc_no nvarchar(20)=N'CP6-BUG143-GC-01';
DECLARE @cp6_emoji nvarchar(max)=REPLICATE(N'😀',50);
DECLARE @cp6_spaces nvarchar(max)=REPLICATE(N' ',100);
IF DATALENGTH(@cp6_emoji)<>200 OR DATALENGTH(@cp6_spaces)<>200
 THROW 51005,'UTF16/space fixture construction failed before DML.',1;
IF EXISTS(SELECT 1 FROM dbo.T_Quotation WHERE Id=@cp6_qtn_id OR QtnNo=@cp6_qtn_no)
 OR EXISTS(SELECT 1 FROM dbo.T_QuotationCalc WHERE Id=@cp6_calc_id OR QtnCalcNo=@cp6_calc_no)
 OR EXISTS(SELECT 1 FROM dbo.T_QuotationDetail WHERE Id=@cp6_detail_id OR QtnNo=@cp6_qtn_no)
 THROW 51005,'Pre-existing guard fixture identifiers cannot be overwritten.',1;  INSERT dbo.T_Quotation(Id,QtnNo,QtnNoMain,QtnNoBranch,BaseCd,StaffCd,CustomerCd,PrintTotalFlg,
   EstimateCheckFlg,MasterConfirmFlg,IsDeleted,CreateDate,TenantId,Creator,Modifier)
  VALUES(@cp6_qtn_id,@cp6_qtn_no,143000001,1,N'WP2143',N'WP2143',N'WP2143',0,0,0,0,
   '2026-10-02T12:00:00',@cp6_tenant,REPLICATE(N'A',100),@cp6_emoji);
  INSERT dbo.T_QuotationCalc(Id,QtnNo,QtnCalcNo,EstimateCheckFlg,MasterConfirmFlg,IsDeleted,CreateDate,TenantId,Creator,Modifier)
  VALUES(@cp6_calc_id,@cp6_qtn_no,@cp6_calc_no,0,0,0,'2026-10-02T12:00:00',@cp6_tenant,@cp6_spaces,REPLICATE(N'B',100));
  INSERT dbo.T_QuotationDetail(Id,QtnNo,DetailNo,PrintTotalFlg,IsDeleted,CreateDate,TenantId,Creator,Modifier)
  VALUES(@cp6_detail_id,@cp6_qtn_no,1,0,0,'2026-10-02T12:00:00',@cp6_tenant,REPLICATE(N'C',100),@cp6_emoji);
ALTER TABLE dbo.[T_Quotation] ALTER COLUMN [Creator] nvarchar(max) NULL;
ALTER TABLE dbo.[T_Quotation] ALTER COLUMN [Modifier] nvarchar(max) NULL;
ALTER TABLE dbo.[T_QuotationCalc] ALTER COLUMN [Creator] nvarchar(max) NULL;
ALTER TABLE dbo.[T_QuotationCalc] ALTER COLUMN [Modifier] nvarchar(max) NULL;
ALTER TABLE dbo.[T_QuotationDetail] ALTER COLUMN [Creator] nvarchar(max) NULL;
ALTER TABLE dbo.[T_QuotationDetail] ALTER COLUMN [Modifier] nvarchar(max) NULL; UPDATE dbo.T_QuotationDetail SET Modifier=REPLICATE(N'X',101) WHERE Id=@cp6_detail_id; IF COALESCE((SELECT DATALENGTH([Modifier]) FROM dbo.[T_QuotationDetail] WHERE Id='14399999-0000-0000-0000-000000000003'),-1)<>202 THROW 51005,'Native boundary fixture must have exactly its planned UTF16 byte length before guard execution.',1;
END TRY
BEGIN CATCH
 DECLARE @setupError int=ERROR_NUMBER();
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 UPDATE #cp6_bug143_case SET Done=1,SetupError=@setupError;
END CATCH;
GO
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
 
 
DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_Quotation]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_Quotation' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_Quotation; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''
   OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
   OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                  WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 dbo.T_Quotation.Creator has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    -- Bind the data column only after the metadata guard; a missing column must
    -- reach 51043 rather than static batch compilation failing with SQL 207.
    DECLARE @cp6_overlong bit = 0;
    DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong = CASE WHEN EXISTS (
        SELECT 1 FROM [dbo].[T_Quotation] WITH (TABLOCKX, HOLDLOCK)
        WHERE DATALENGTH([Creator]) > 200) THEN 1 ELSE 0 END;';
    EXEC sys.sp_executesql @cp6_check, N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT;
    IF @cp6_overlong = 1
        THROW 51043, N'BUG143 dbo.T_Quotation.Creator contains more than 100 UTF-16 units; no data changed.', 1;

    -- The locked name must still identify the table whose metadata was captured.
    IF OBJECT_ID(N'[dbo].[T_Quotation]', N'U') IS NULL
       OR OBJECT_ID(N'[dbo].[T_Quotation]', N'U') <> @cp6_object_id
       OR NOT EXISTS (
           SELECT 1 FROM sys.tables
           WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
             AND name COLLATE Latin1_General_100_BIN2 = N'T_Quotation' COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_Quotation identity changed; audit capacity repair refused.', 1;

    -- Revalidate the captured column before using its collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_Quotation.Creator metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_Quotation] ALTER COLUMN [Creator] nvarchar(100) COLLATE '
        + @cp6_collation + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;

 END TRY
 BEGIN CATCH
  DECLARE @guardError int=ERROR_NUMBER();
  DECLARE @prefixWasProved bit=(SELECT PrefixProved FROM #cp6_bug143_case);
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,ActualError=@guardError,ActualOrdinal=1,PrefixProved=@prefixWasProved;
 END CATCH;
END;
GO
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
 
 
DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_Quotation]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_Quotation' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_Quotation; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''
   OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
   OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                  WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 dbo.T_Quotation.Modifier has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    -- Bind the data column only after the metadata guard; a missing column must
    -- reach 51043 rather than static batch compilation failing with SQL 207.
    DECLARE @cp6_overlong bit = 0;
    DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong = CASE WHEN EXISTS (
        SELECT 1 FROM [dbo].[T_Quotation] WITH (TABLOCKX, HOLDLOCK)
        WHERE DATALENGTH([Modifier]) > 200) THEN 1 ELSE 0 END;';
    EXEC sys.sp_executesql @cp6_check, N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT;
    IF @cp6_overlong = 1
        THROW 51043, N'BUG143 dbo.T_Quotation.Modifier contains more than 100 UTF-16 units; no data changed.', 1;

    -- The locked name must still identify the table whose metadata was captured.
    IF OBJECT_ID(N'[dbo].[T_Quotation]', N'U') IS NULL
       OR OBJECT_ID(N'[dbo].[T_Quotation]', N'U') <> @cp6_object_id
       OR NOT EXISTS (
           SELECT 1 FROM sys.tables
           WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
             AND name COLLATE Latin1_General_100_BIN2 = N'T_Quotation' COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_Quotation identity changed; audit capacity repair refused.', 1;

    -- Revalidate the captured column before using its collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_Quotation.Modifier metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_Quotation] ALTER COLUMN [Modifier] nvarchar(100) COLLATE '
        + @cp6_collation + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;

 END TRY
 BEGIN CATCH
  DECLARE @guardError int=ERROR_NUMBER();
  DECLARE @prefixWasProved bit=(SELECT PrefixProved FROM #cp6_bug143_case);
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,ActualError=@guardError,ActualOrdinal=2,PrefixProved=@prefixWasProved;
 END CATCH;
END;
GO
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
 
 
DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationCalc' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationCalc; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''
   OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
   OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                  WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 dbo.T_QuotationCalc.Creator has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    -- Bind the data column only after the metadata guard; a missing column must
    -- reach 51043 rather than static batch compilation failing with SQL 207.
    DECLARE @cp6_overlong bit = 0;
    DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong = CASE WHEN EXISTS (
        SELECT 1 FROM [dbo].[T_QuotationCalc] WITH (TABLOCKX, HOLDLOCK)
        WHERE DATALENGTH([Creator]) > 200) THEN 1 ELSE 0 END;';
    EXEC sys.sp_executesql @cp6_check, N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT;
    IF @cp6_overlong = 1
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Creator contains more than 100 UTF-16 units; no data changed.', 1;

    -- The locked name must still identify the table whose metadata was captured.
    IF OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U') IS NULL
       OR OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U') <> @cp6_object_id
       OR NOT EXISTS (
           SELECT 1 FROM sys.tables
           WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
             AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationCalc' COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc identity changed; audit capacity repair refused.', 1;

    -- Revalidate the captured column before using its collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Creator metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationCalc] ALTER COLUMN [Creator] nvarchar(100) COLLATE '
        + @cp6_collation + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;

 END TRY
 BEGIN CATCH
  DECLARE @guardError int=ERROR_NUMBER();
  DECLARE @prefixWasProved bit=(SELECT PrefixProved FROM #cp6_bug143_case);
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,ActualError=@guardError,ActualOrdinal=3,PrefixProved=@prefixWasProved;
 END CATCH;
END;
GO
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
 
 
DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationCalc' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationCalc; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''
   OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
   OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                  WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 dbo.T_QuotationCalc.Modifier has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    -- Bind the data column only after the metadata guard; a missing column must
    -- reach 51043 rather than static batch compilation failing with SQL 207.
    DECLARE @cp6_overlong bit = 0;
    DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong = CASE WHEN EXISTS (
        SELECT 1 FROM [dbo].[T_QuotationCalc] WITH (TABLOCKX, HOLDLOCK)
        WHERE DATALENGTH([Modifier]) > 200) THEN 1 ELSE 0 END;';
    EXEC sys.sp_executesql @cp6_check, N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT;
    IF @cp6_overlong = 1
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Modifier contains more than 100 UTF-16 units; no data changed.', 1;

    -- The locked name must still identify the table whose metadata was captured.
    IF OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U') IS NULL
       OR OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U') <> @cp6_object_id
       OR NOT EXISTS (
           SELECT 1 FROM sys.tables
           WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
             AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationCalc' COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc identity changed; audit capacity repair refused.', 1;

    -- Revalidate the captured column before using its collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Modifier metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationCalc] ALTER COLUMN [Modifier] nvarchar(100) COLLATE '
        + @cp6_collation + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;

 END TRY
 BEGIN CATCH
  DECLARE @guardError int=ERROR_NUMBER();
  DECLARE @prefixWasProved bit=(SELECT PrefixProved FROM #cp6_bug143_case);
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,ActualError=@guardError,ActualOrdinal=4,PrefixProved=@prefixWasProved;
 END CATCH;
END;
GO
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
 
 
DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationDetail' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationDetail; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''
   OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
   OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                  WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 dbo.T_QuotationDetail.Creator has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    -- Bind the data column only after the metadata guard; a missing column must
    -- reach 51043 rather than static batch compilation failing with SQL 207.
    DECLARE @cp6_overlong bit = 0;
    DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong = CASE WHEN EXISTS (
        SELECT 1 FROM [dbo].[T_QuotationDetail] WITH (TABLOCKX, HOLDLOCK)
        WHERE DATALENGTH([Creator]) > 200) THEN 1 ELSE 0 END;';
    EXEC sys.sp_executesql @cp6_check, N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT;
    IF @cp6_overlong = 1
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Creator contains more than 100 UTF-16 units; no data changed.', 1;

    -- The locked name must still identify the table whose metadata was captured.
    IF OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U') IS NULL
       OR OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U') <> @cp6_object_id
       OR NOT EXISTS (
           SELECT 1 FROM sys.tables
           WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
             AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationDetail' COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail identity changed; audit capacity repair refused.', 1;

    -- Revalidate the captured column before using its collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Creator metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationDetail] ALTER COLUMN [Creator] nvarchar(100) COLLATE '
        + @cp6_collation + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;

 END TRY
 BEGIN CATCH
  DECLARE @guardError int=ERROR_NUMBER();
  DECLARE @prefixWasProved bit=(SELECT PrefixProved FROM #cp6_bug143_case);
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,ActualError=@guardError,ActualOrdinal=5,PrefixProved=@prefixWasProved;
 END CATCH;
END;
GO
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
  IF (SELECT COUNT(*) FROM sys.tables tab JOIN sys.columns col ON col.object_id=tab.object_id
  JOIN #cp6_bug143_columns expected ON expected.TableName=tab.name AND expected.ColumnName=col.name
  WHERE tab.schema_id=SCHEMA_ID(N'dbo') AND col.max_length=200
   AND NOT(tab.name=N'T_QuotationDetail' AND col.name=N'Modifier'))<>5
  THROW 51006,'Late atomic fixture must have executed and narrowed all five preceding commands.',1;
 UPDATE #cp6_bug143_case SET PrefixProved=1;
 
DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationDetail' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationDetail; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''
   OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
   OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                  WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 dbo.T_QuotationDetail.Modifier has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    -- Bind the data column only after the metadata guard; a missing column must
    -- reach 51043 rather than static batch compilation failing with SQL 207.
    DECLARE @cp6_overlong bit = 0;
    DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong = CASE WHEN EXISTS (
        SELECT 1 FROM [dbo].[T_QuotationDetail] WITH (TABLOCKX, HOLDLOCK)
        WHERE DATALENGTH([Modifier]) > 200) THEN 1 ELSE 0 END;';
    EXEC sys.sp_executesql @cp6_check, N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT;
    IF @cp6_overlong = 1
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Modifier contains more than 100 UTF-16 units; no data changed.', 1;

    -- The locked name must still identify the table whose metadata was captured.
    IF OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U') IS NULL
       OR OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U') <> @cp6_object_id
       OR NOT EXISTS (
           SELECT 1 FROM sys.tables
           WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
             AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationDetail' COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail identity changed; audit capacity repair refused.', 1;

    -- Revalidate the captured column before using its collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Modifier metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationDetail] ALTER COLUMN [Modifier] nvarchar(100) COLLATE '
        + @cp6_collation + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;

 END TRY
 BEGIN CATCH
  DECLARE @guardError int=ERROR_NUMBER();
  DECLARE @prefixWasProved bit=(SELECT PrefixProved FROM #cp6_bug143_case);
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,ActualError=@guardError,ActualOrdinal=6,PrefixProved=@prefixWasProved;
 END CATCH;
END;
GO
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
  IF EXISTS(SELECT 1 FROM #cp6_bug143_columns expected LEFT JOIN sys.tables tab ON tab.schema_id=SCHEMA_ID(N'dbo') AND tab.name=expected.TableName
   LEFT JOIN sys.columns col ON col.object_id=tab.object_id AND col.name=expected.ColumnName
   WHERE col.column_id IS NULL OR col.column_id<>expected.ColumnId OR col.max_length<>200 OR col.is_nullable<>1
    OR col.collation_name COLLATE Latin1_General_100_BIN2<>expected.CollationName COLLATE Latin1_General_100_BIN2)
   THROW 51006,'Successful guards must preserve six physical columns/collations and finish at nullable Unicode100.',1;
  
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1;
 END TRY
 BEGIN CATCH
  DECLARE @successError int=ERROR_NUMBER();
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,SetupError=@successError;
 END CATCH;
END;
IF @@TRANCOUNT<>0 THROW 51006,'Guard fixture transaction must be fully rolled back.',1;
SELECT Done,ActualError,ActualOrdinal,PrefixProved,SetupError FROM #cp6_bug143_case FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
GO