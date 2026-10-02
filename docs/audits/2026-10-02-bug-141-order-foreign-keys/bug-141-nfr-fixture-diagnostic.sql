SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON; SET LOCK_TIMEOUT 10000;
IF DB_NAME()<>N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'36ef9cae14704ac88eb994416f628e24')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,N'Exact owned isolated backup SQL database required.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>139
 OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261002184500_RestoreMissingOrderModelForeignKeys' AND ProductVersion=N'8.0.30')
 THROW 51002,N'Actual accepted Core139 canonical history required.',1;
IF EXISTS(SELECT 1 FROM dbo.T_Order) OR EXISTS(SELECT 1 FROM dbo.T_OrderDetail)
 OR EXISTS(SELECT 1 FROM dbo.T_OrderMaterial) OR EXISTS(SELECT 1 FROM dbo.T_OrderProcess) OR EXISTS(SELECT 1 FROM dbo.T_OrderProcessNote)
 THROW 51003,N'This representative guard fixture requires the five isolated Order tables to be empty.',1;
IF NOT EXISTS (
 SELECT 1 FROM sys.foreign_keys fk
 JOIN sys.tables childTable ON childTable.object_id=fk.parent_object_id
 JOIN sys.schemas childSchema ON childSchema.schema_id=childTable.schema_id
 JOIN sys.tables parentTable ON parentTable.object_id=fk.referenced_object_id
 JOIN sys.schemas parentSchema ON parentSchema.schema_id=parentTable.schema_id
 JOIN sys.indexes principalIndex ON principalIndex.object_id=fk.referenced_object_id AND principalIndex.index_id=fk.key_index_id
 WHERE childSchema.name=N'dbo' AND childTable.name=N'T_OrderDetail' AND fk.name=N'FK_T_OrderDetail_T_Order_WebOrderNo'
  AND parentSchema.name=N'dbo' AND parentTable.name=N'T_Order' AND principalIndex.name=N'AK_T_Order_WebOrderNo'
  AND fk.delete_referential_action=1 AND fk.update_referential_action=0
  AND fk.is_disabled=0 AND fk.is_not_trusted=0 AND fk.is_not_for_replication=0 AND fk.is_system_named=0
  AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id)=1
  AND NOT EXISTS (
   SELECT 1 FROM (VALUES (1,N'WebOrderNo',N'WebOrderNo')) expected(ordinal,dependent_name,principal_name)
   LEFT JOIN sys.foreign_key_columns actual ON actual.constraint_object_id=fk.object_id AND actual.constraint_column_id=expected.ordinal
   LEFT JOIN sys.columns dependentColumn ON dependentColumn.object_id=actual.parent_object_id AND dependentColumn.column_id=actual.parent_column_id
   LEFT JOIN sys.columns principalColumn ON principalColumn.object_id=actual.referenced_object_id AND principalColumn.column_id=actual.referenced_column_id
   WHERE dependentColumn.column_id IS NULL OR principalColumn.column_id IS NULL
    OR dependentColumn.name COLLATE Latin1_General_100_BIN2<>expected.dependent_name COLLATE Latin1_General_100_BIN2
    OR principalColumn.name COLLATE Latin1_General_100_BIN2<>expected.principal_name COLLATE Latin1_General_100_BIN2))
 THROW 51004,N'Independent frozen four-FK native definition assertion failed.',1;
IF NOT EXISTS (
 SELECT 1 FROM sys.foreign_keys fk
 JOIN sys.tables childTable ON childTable.object_id=fk.parent_object_id
 JOIN sys.schemas childSchema ON childSchema.schema_id=childTable.schema_id
 JOIN sys.tables parentTable ON parentTable.object_id=fk.referenced_object_id
 JOIN sys.schemas parentSchema ON parentSchema.schema_id=parentTable.schema_id
 JOIN sys.indexes principalIndex ON principalIndex.object_id=fk.referenced_object_id AND principalIndex.index_id=fk.key_index_id
 WHERE childSchema.name=N'dbo' AND childTable.name=N'T_OrderMaterial' AND fk.name=N'FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd'
  AND parentSchema.name=N'dbo' AND parentTable.name=N'T_OrderDetail' AND principalIndex.name=N'UX_T_OrderDetail_OrderProduct'
  AND fk.delete_referential_action=1 AND fk.update_referential_action=0
  AND fk.is_disabled=0 AND fk.is_not_trusted=0 AND fk.is_not_for_replication=0 AND fk.is_system_named=0
  AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id)=3
  AND NOT EXISTS (
   SELECT 1 FROM (VALUES (1,N'WebOrderNo',N'WebOrderNo'),(2,N'WebOrderDetailNo',N'WebOrderDetailNo'),(3,N'ProductCd',N'ProductCd')) expected(ordinal,dependent_name,principal_name)
   LEFT JOIN sys.foreign_key_columns actual ON actual.constraint_object_id=fk.object_id AND actual.constraint_column_id=expected.ordinal
   LEFT JOIN sys.columns dependentColumn ON dependentColumn.object_id=actual.parent_object_id AND dependentColumn.column_id=actual.parent_column_id
   LEFT JOIN sys.columns principalColumn ON principalColumn.object_id=actual.referenced_object_id AND principalColumn.column_id=actual.referenced_column_id
   WHERE dependentColumn.column_id IS NULL OR principalColumn.column_id IS NULL
    OR dependentColumn.name COLLATE Latin1_General_100_BIN2<>expected.dependent_name COLLATE Latin1_General_100_BIN2
    OR principalColumn.name COLLATE Latin1_General_100_BIN2<>expected.principal_name COLLATE Latin1_General_100_BIN2))
 THROW 51004,N'Independent frozen four-FK native definition assertion failed.',1;
IF NOT EXISTS (
 SELECT 1 FROM sys.foreign_keys fk
 JOIN sys.tables childTable ON childTable.object_id=fk.parent_object_id
 JOIN sys.schemas childSchema ON childSchema.schema_id=childTable.schema_id
 JOIN sys.tables parentTable ON parentTable.object_id=fk.referenced_object_id
 JOIN sys.schemas parentSchema ON parentSchema.schema_id=parentTable.schema_id
 JOIN sys.indexes principalIndex ON principalIndex.object_id=fk.referenced_object_id AND principalIndex.index_id=fk.key_index_id
 WHERE childSchema.name=N'dbo' AND childTable.name=N'T_OrderProcess' AND fk.name=N'FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd'
  AND parentSchema.name=N'dbo' AND parentTable.name=N'T_OrderDetail' AND principalIndex.name=N'UX_T_OrderDetail_OrderProduct'
  AND fk.delete_referential_action=1 AND fk.update_referential_action=0
  AND fk.is_disabled=0 AND fk.is_not_trusted=0 AND fk.is_not_for_replication=0 AND fk.is_system_named=0
  AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id)=3
  AND NOT EXISTS (
   SELECT 1 FROM (VALUES (1,N'WebOrderNo',N'WebOrderNo'),(2,N'WebOrderDetailNo',N'WebOrderDetailNo'),(3,N'ProductCd',N'ProductCd')) expected(ordinal,dependent_name,principal_name)
   LEFT JOIN sys.foreign_key_columns actual ON actual.constraint_object_id=fk.object_id AND actual.constraint_column_id=expected.ordinal
   LEFT JOIN sys.columns dependentColumn ON dependentColumn.object_id=actual.parent_object_id AND dependentColumn.column_id=actual.parent_column_id
   LEFT JOIN sys.columns principalColumn ON principalColumn.object_id=actual.referenced_object_id AND principalColumn.column_id=actual.referenced_column_id
   WHERE dependentColumn.column_id IS NULL OR principalColumn.column_id IS NULL
    OR dependentColumn.name COLLATE Latin1_General_100_BIN2<>expected.dependent_name COLLATE Latin1_General_100_BIN2
    OR principalColumn.name COLLATE Latin1_General_100_BIN2<>expected.principal_name COLLATE Latin1_General_100_BIN2))
 THROW 51004,N'Independent frozen four-FK native definition assertion failed.',1;
IF NOT EXISTS (
 SELECT 1 FROM sys.foreign_keys fk
 JOIN sys.tables childTable ON childTable.object_id=fk.parent_object_id
 JOIN sys.schemas childSchema ON childSchema.schema_id=childTable.schema_id
 JOIN sys.tables parentTable ON parentTable.object_id=fk.referenced_object_id
 JOIN sys.schemas parentSchema ON parentSchema.schema_id=parentTable.schema_id
 JOIN sys.indexes principalIndex ON principalIndex.object_id=fk.referenced_object_id AND principalIndex.index_id=fk.key_index_id
 WHERE childSchema.name=N'dbo' AND childTable.name=N'T_OrderProcessNote' AND fk.name=N'FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd'
  AND parentSchema.name=N'dbo' AND parentTable.name=N'T_OrderDetail' AND principalIndex.name=N'UX_T_OrderDetail_OrderProduct'
  AND fk.delete_referential_action=1 AND fk.update_referential_action=0
  AND fk.is_disabled=0 AND fk.is_not_trusted=0 AND fk.is_not_for_replication=0 AND fk.is_system_named=0
  AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id)=3
  AND NOT EXISTS (
   SELECT 1 FROM (VALUES (1,N'WebOrderNo',N'WebOrderNo'),(2,N'WebOrderDetailNo',N'WebOrderDetailNo'),(3,N'ProductCd',N'ProductCd')) expected(ordinal,dependent_name,principal_name)
   LEFT JOIN sys.foreign_key_columns actual ON actual.constraint_object_id=fk.object_id AND actual.constraint_column_id=expected.ordinal
   LEFT JOIN sys.columns dependentColumn ON dependentColumn.object_id=actual.parent_object_id AND dependentColumn.column_id=actual.parent_column_id
   LEFT JOIN sys.columns principalColumn ON principalColumn.object_id=actual.referenced_object_id AND principalColumn.column_id=actual.referenced_column_id
   WHERE dependentColumn.column_id IS NULL OR principalColumn.column_id IS NULL
    OR dependentColumn.name COLLATE Latin1_General_100_BIN2<>expected.dependent_name COLLATE Latin1_General_100_BIN2
    OR principalColumn.name COLLATE Latin1_General_100_BIN2<>expected.principal_name COLLATE Latin1_General_100_BIN2))
 THROW 51004,N'Independent frozen four-FK native definition assertion failed.',1;
PRINT N'CP6_BUG141_OWNED_BASELINE_VERIFIED';
GO
ALTER TABLE [dbo].[T_OrderProcessNote] DROP CONSTRAINT [FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd];
ALTER TABLE [dbo].[T_OrderProcessNote] WITH CHECK ADD CONSTRAINT [FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd] FOREIGN KEY ([WebOrderNo],[WebOrderDetailNo],[ProductCd]) REFERENCES [dbo].[T_OrderDetail] ([WebOrderNo],[WebOrderDetailNo],[ProductCd]) ON DELETE CASCADE ON UPDATE NO ACTION NOT FOR REPLICATION;
SELECT is_disabled,is_not_trusted,is_not_for_replication FROM sys.foreign_keys WHERE name=N'FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';
ALTER TABLE dbo.T_OrderProcessNote WITH CHECK CHECK CONSTRAINT FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd;
SELECT is_disabled,is_not_trusted,is_not_for_replication FROM sys.foreign_keys WHERE name=N'FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';
ROLLBACK TRANSACTION;