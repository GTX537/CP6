-- Task-owned SQL36ef only. Root supplies -v FixtureAction=prepare|poison|restore|cleanup.
-- prepare commits one owned row in each table before full BUG143 capture.
-- poison makes the last migration column exactly 101 ASCII UTF-16 units.
-- restore returns only that owned field to its original 50-emoji / 100-unit value.
-- cleanup requires Core140 and deletes only the three exact owned IDs, child first.
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
IF DB_NAME()<>N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'36ef9cae14704ac88eb994416f628e24')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Exact owned isolated SQL36ef backup required.',1;
DECLARE @cp6_fixture_action nvarchar(20)=N'$(FixtureAction)';
IF @cp6_fixture_action NOT IN(N'prepare',N'poison',N'restore',N'cleanup')
 THROW 51005,'Explicit supported BUG143 fixture action required.',1;
IF (@cp6_fixture_action<>N'cleanup' AND ((SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>139
 OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261002184500_RestoreMissingOrderModelForeignKeys')))
 OR (@cp6_fixture_action=N'cleanup' AND ((SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>140
 OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261002193500_RestoreQuotationAuditColumnCapacity')))
 THROW 51005,'Fixture action requires the exact intended canonical Core139/Core140 history.',1;

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

DECLARE @cp6_qtn_id uniqueidentifier='14300000-0000-0000-0000-000000000001';
DECLARE @cp6_calc_id uniqueidentifier='14300000-0000-0000-0000-000000000002';
DECLARE @cp6_detail_id uniqueidentifier='14300000-0000-0000-0000-000000000003';
DECLARE @cp6_tenant uniqueidentifier='14300000-0000-0000-0000-0000000000a1';
DECLARE @cp6_qtn_no nvarchar(20)=N'CP6-BUG143-QTN-01';
DECLARE @cp6_calc_no nvarchar(20)=N'CP6-BUG143-CALC-01';
DECLARE @cp6_emoji nvarchar(max)=REPLICATE(N'😀',50);
DECLARE @cp6_spaces nvarchar(max)=REPLICATE(N' ',100);
IF DATALENGTH(@cp6_emoji)<>200 OR DATALENGTH(@cp6_spaces)<>200
 THROW 51005,'UTF16/space fixture construction failed before DML.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF @cp6_fixture_action=N'prepare'
 BEGIN
  IF EXISTS(SELECT 1 FROM dbo.T_Quotation WHERE Id=@cp6_qtn_id OR QtnNo=@cp6_qtn_no)
   OR EXISTS(SELECT 1 FROM dbo.T_QuotationCalc WHERE Id=@cp6_calc_id OR QtnCalcNo=@cp6_calc_no)
   OR EXISTS(SELECT 1 FROM dbo.T_QuotationDetail WHERE Id=@cp6_detail_id OR QtnNo=@cp6_qtn_no)
   THROW 51005,'Existing fixture identifiers must never be overwritten.',1;
  INSERT dbo.T_Quotation(Id,QtnNo,QtnNoMain,QtnNoBranch,BaseCd,StaffCd,CustomerCd,PrintTotalFlg,
   EstimateCheckFlg,MasterConfirmFlg,IsDeleted,CreateDate,TenantId,Creator,Modifier)
  VALUES(@cp6_qtn_id,@cp6_qtn_no,143000001,1,N'WP2143',N'WP2143',N'WP2143',0,0,0,0,
   '2026-10-02T12:00:00',@cp6_tenant,REPLICATE(N'A',100),@cp6_emoji);
  INSERT dbo.T_QuotationCalc(Id,QtnNo,QtnCalcNo,EstimateCheckFlg,MasterConfirmFlg,IsDeleted,CreateDate,TenantId,Creator,Modifier)
  VALUES(@cp6_calc_id,@cp6_qtn_no,@cp6_calc_no,0,0,0,'2026-10-02T12:00:00',@cp6_tenant,@cp6_spaces,REPLICATE(N'B',100));
  INSERT dbo.T_QuotationDetail(Id,QtnNo,DetailNo,PrintTotalFlg,IsDeleted,CreateDate,TenantId,Creator,Modifier)
  VALUES(@cp6_detail_id,@cp6_qtn_no,1,0,0,'2026-10-02T12:00:00',@cp6_tenant,REPLICATE(N'C',100),@cp6_emoji);
 END
 ELSE
 BEGIN
  IF (SELECT COUNT(*) FROM dbo.T_Quotation WHERE Id=@cp6_qtn_id AND QtnNo=@cp6_qtn_no AND TenantId=@cp6_tenant)<>1
   OR (SELECT COUNT(*) FROM dbo.T_QuotationCalc WHERE Id=@cp6_calc_id AND QtnNo=@cp6_qtn_no AND QtnCalcNo=@cp6_calc_no AND TenantId=@cp6_tenant)<>1
   OR (SELECT COUNT(*) FROM dbo.T_QuotationDetail WHERE Id=@cp6_detail_id AND QtnNo=@cp6_qtn_no AND TenantId=@cp6_tenant)<>1
   THROW 51005,'Exact existing owned three-row graph required for mutation/cleanup.',1;
  IF @cp6_fixture_action=N'poison'
  BEGIN
   IF (SELECT DATALENGTH(Modifier) FROM dbo.T_QuotationDetail WHERE Id=@cp6_detail_id)<>200
    THROW 51005,'Poison must begin from the exact legal boundary length.',1;
   UPDATE dbo.T_QuotationDetail SET Modifier=REPLICATE(N'X',101) WHERE Id=@cp6_detail_id AND QtnNo=@cp6_qtn_no AND TenantId=@cp6_tenant;
   IF @@ROWCOUNT<>1 THROW 51005,'Expected exactly one owned poison update.',1;
  END
  ELSE IF @cp6_fixture_action=N'restore'
  BEGIN
   IF (SELECT DATALENGTH(Modifier) FROM dbo.T_QuotationDetail WHERE Id=@cp6_detail_id)<>202
    THROW 51005,'Restore requires the committed 101-unit negative fixture.',1;
   UPDATE dbo.T_QuotationDetail SET Modifier=@cp6_emoji WHERE Id=@cp6_detail_id AND QtnNo=@cp6_qtn_no AND TenantId=@cp6_tenant;
   IF @@ROWCOUNT<>1 THROW 51005,'Expected exactly one owned boundary restore.',1;
  END
  ELSE
  BEGIN
   IF EXISTS(SELECT 1 FROM dbo.T_QuotationDetail WHERE QtnNo=@cp6_qtn_no AND Id<>@cp6_detail_id)
    OR EXISTS(SELECT 1 FROM dbo.T_QuotationCalc WHERE QtnNo=@cp6_qtn_no AND Id<>@cp6_calc_id)
    THROW 51005,'Unexpected rows reference the fixture parent; cleanup refused.',1;
   DELETE dbo.T_QuotationDetail WHERE Id=@cp6_detail_id AND QtnNo=@cp6_qtn_no AND TenantId=@cp6_tenant;
   IF @@ROWCOUNT<>1 THROW 51005,'Expected exactly one owned detail delete.',1;
   DELETE dbo.T_QuotationCalc WHERE Id=@cp6_calc_id AND QtnNo=@cp6_qtn_no AND QtnCalcNo=@cp6_calc_no AND TenantId=@cp6_tenant;
   IF @@ROWCOUNT<>1 THROW 51005,'Expected exactly one owned calc delete.',1;
   DELETE dbo.T_Quotation WHERE Id=@cp6_qtn_id AND QtnNo=@cp6_qtn_no AND TenantId=@cp6_tenant;
   IF @@ROWCOUNT<>1 THROW 51005,'Expected exactly one owned parent delete.',1;
  END
 END;
 IF @cp6_fixture_action<>N'cleanup'
 BEGIN
  IF EXISTS(SELECT 1 FROM dbo.T_Quotation WHERE Id=@cp6_qtn_id AND (DATALENGTH(Creator)<>200 OR DATALENGTH(Modifier)<>200))
   OR EXISTS(SELECT 1 FROM dbo.T_QuotationCalc WHERE Id=@cp6_calc_id AND (DATALENGTH(Creator)<>200 OR DATALENGTH(Modifier)<>200))
   OR EXISTS(SELECT 1 FROM dbo.T_QuotationDetail WHERE Id=@cp6_detail_id AND
    (DATALENGTH(Creator)<>200 OR DATALENGTH(Modifier)<>CASE WHEN @cp6_fixture_action=N'poison' THEN 202 ELSE 200 END))
   THROW 51005,'Owned fixture boundary lengths differ; transaction rollback required.',1;
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
SELECT @cp6_fixture_action AS FixtureAction,CONVERT(bit,1) AS OwnerVerified,
 CASE WHEN @cp6_fixture_action=N'cleanup' THEN 0 ELSE 3 END AS OwnedGraphRows,
 CASE WHEN @cp6_fixture_action=N'poison' THEN 202 ELSE 200 END AS LastModifierBytes,
 CONVERT(bit,0) AS BusinessValuesReturned
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
