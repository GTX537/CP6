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
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd',N'F') AND is_disabled=0 AND is_not_trusted=1 AND is_not_for_replication=1)
 THROW 51003,N'Native combined NOT FOR REPLICATION/untrusted fixture not established.',1;
PRINT N'CP6_BUG141_FIXTURE_READY';
GO
PRINT N'CP6_BUG141_GUARD_1_BEGIN';
GO

DECLARE @cp6_child_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
DECLARE @cp6_principal_object_id int = OBJECT_ID(N'[dbo].[T_Order]', N'U');
IF @cp6_child_object_id IS NULL OR @cp6_principal_object_id IS NULL
    THROW 51041, N'BUG141 missing dbo.T_OrderDetail or dbo.T_Order; FK repair refused.', 1;

DECLARE @cp6_named_object_id int = (
    SELECT object_id FROM sys.objects
    WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'FK_T_OrderDetail_T_Order_WebOrderNo');
IF @cp6_named_object_id IS NULL
BEGIN
    ALTER TABLE [dbo].[T_OrderDetail] WITH CHECK ADD CONSTRAINT [FK_T_OrderDetail_T_Order_WebOrderNo]
        FOREIGN KEY ([WebOrderNo]) REFERENCES [dbo].[T_Order] ([WebOrderNo])
        ON DELETE CASCADE ON UPDATE NO ACTION;
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys AS fk
    WHERE fk.object_id = @cp6_named_object_id AND fk.schema_id = SCHEMA_ID(N'dbo')
      AND fk.parent_object_id = @cp6_child_object_id AND fk.referenced_object_id = @cp6_principal_object_id
      AND fk.delete_referential_action = 1 AND fk.update_referential_action = 0
      AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS fkc
           WHERE fkc.constraint_object_id = fk.object_id) = 1
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'WebOrderNo', N'WebOrderNo')) AS expected(ordinal, child_column, principal_column)
          LEFT JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = expected.ordinal
          LEFT JOIN sys.columns AS child
            ON child.object_id = fkc.parent_object_id AND child.column_id = fkc.parent_column_id
          LEFT JOIN sys.columns AS principal
            ON principal.object_id = fkc.referenced_object_id AND principal.column_id = fkc.referenced_column_id
          WHERE child.column_id IS NULL OR principal.column_id IS NULL
             OR child.object_id <> @cp6_child_object_id OR principal.object_id <> @cp6_principal_object_id
             OR child.name COLLATE Latin1_General_100_BIN2 <> expected.child_column COLLATE Latin1_General_100_BIN2
             OR principal.name COLLATE Latin1_General_100_BIN2 <> expected.principal_column COLLATE Latin1_General_100_BIN2))
    THROW 51041, N'BUG141 object dbo.FK_T_OrderDetail_T_Order_WebOrderNo conflicts with the frozen FK definition; existing object preserved.', 1;

GO
PRINT N'CP6_BUG141_GUARD_1_END';
GO
PRINT N'CP6_BUG141_GUARD_2_BEGIN';
GO

DECLARE @cp6_child_object_id int = OBJECT_ID(N'[dbo].[T_OrderMaterial]', N'U');
DECLARE @cp6_principal_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_child_object_id IS NULL OR @cp6_principal_object_id IS NULL
    THROW 51041, N'BUG141 missing dbo.T_OrderMaterial or dbo.T_OrderDetail; FK repair refused.', 1;

DECLARE @cp6_named_object_id int = (
    SELECT object_id FROM sys.objects
    WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd');
IF @cp6_named_object_id IS NULL
BEGIN
    ALTER TABLE [dbo].[T_OrderMaterial] WITH CHECK ADD CONSTRAINT [FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd]
        FOREIGN KEY ([WebOrderNo], [WebOrderDetailNo], [ProductCd]) REFERENCES [dbo].[T_OrderDetail] ([WebOrderNo], [WebOrderDetailNo], [ProductCd])
        ON DELETE CASCADE ON UPDATE NO ACTION;
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys AS fk
    WHERE fk.object_id = @cp6_named_object_id AND fk.schema_id = SCHEMA_ID(N'dbo')
      AND fk.parent_object_id = @cp6_child_object_id AND fk.referenced_object_id = @cp6_principal_object_id
      AND fk.delete_referential_action = 1 AND fk.update_referential_action = 0
      AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS fkc
           WHERE fkc.constraint_object_id = fk.object_id) = 3
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'WebOrderNo', N'WebOrderNo'), (2, N'WebOrderDetailNo', N'WebOrderDetailNo'), (3, N'ProductCd', N'ProductCd')) AS expected(ordinal, child_column, principal_column)
          LEFT JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = expected.ordinal
          LEFT JOIN sys.columns AS child
            ON child.object_id = fkc.parent_object_id AND child.column_id = fkc.parent_column_id
          LEFT JOIN sys.columns AS principal
            ON principal.object_id = fkc.referenced_object_id AND principal.column_id = fkc.referenced_column_id
          WHERE child.column_id IS NULL OR principal.column_id IS NULL
             OR child.object_id <> @cp6_child_object_id OR principal.object_id <> @cp6_principal_object_id
             OR child.name COLLATE Latin1_General_100_BIN2 <> expected.child_column COLLATE Latin1_General_100_BIN2
             OR principal.name COLLATE Latin1_General_100_BIN2 <> expected.principal_column COLLATE Latin1_General_100_BIN2))
    THROW 51041, N'BUG141 object dbo.FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd conflicts with the frozen FK definition; existing object preserved.', 1;

GO
PRINT N'CP6_BUG141_GUARD_2_END';
GO
PRINT N'CP6_BUG141_GUARD_3_BEGIN';
GO

DECLARE @cp6_child_object_id int = OBJECT_ID(N'[dbo].[T_OrderProcess]', N'U');
DECLARE @cp6_principal_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_child_object_id IS NULL OR @cp6_principal_object_id IS NULL
    THROW 51041, N'BUG141 missing dbo.T_OrderProcess or dbo.T_OrderDetail; FK repair refused.', 1;

DECLARE @cp6_named_object_id int = (
    SELECT object_id FROM sys.objects
    WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd');
IF @cp6_named_object_id IS NULL
BEGIN
    ALTER TABLE [dbo].[T_OrderProcess] WITH CHECK ADD CONSTRAINT [FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd]
        FOREIGN KEY ([WebOrderNo], [WebOrderDetailNo], [ProductCd]) REFERENCES [dbo].[T_OrderDetail] ([WebOrderNo], [WebOrderDetailNo], [ProductCd])
        ON DELETE CASCADE ON UPDATE NO ACTION;
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys AS fk
    WHERE fk.object_id = @cp6_named_object_id AND fk.schema_id = SCHEMA_ID(N'dbo')
      AND fk.parent_object_id = @cp6_child_object_id AND fk.referenced_object_id = @cp6_principal_object_id
      AND fk.delete_referential_action = 1 AND fk.update_referential_action = 0
      AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS fkc
           WHERE fkc.constraint_object_id = fk.object_id) = 3
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'WebOrderNo', N'WebOrderNo'), (2, N'WebOrderDetailNo', N'WebOrderDetailNo'), (3, N'ProductCd', N'ProductCd')) AS expected(ordinal, child_column, principal_column)
          LEFT JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = expected.ordinal
          LEFT JOIN sys.columns AS child
            ON child.object_id = fkc.parent_object_id AND child.column_id = fkc.parent_column_id
          LEFT JOIN sys.columns AS principal
            ON principal.object_id = fkc.referenced_object_id AND principal.column_id = fkc.referenced_column_id
          WHERE child.column_id IS NULL OR principal.column_id IS NULL
             OR child.object_id <> @cp6_child_object_id OR principal.object_id <> @cp6_principal_object_id
             OR child.name COLLATE Latin1_General_100_BIN2 <> expected.child_column COLLATE Latin1_General_100_BIN2
             OR principal.name COLLATE Latin1_General_100_BIN2 <> expected.principal_column COLLATE Latin1_General_100_BIN2))
    THROW 51041, N'BUG141 object dbo.FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd conflicts with the frozen FK definition; existing object preserved.', 1;

GO
PRINT N'CP6_BUG141_GUARD_3_END';
GO
PRINT N'CP6_BUG141_GUARD_4_BEGIN';
GO

DECLARE @cp6_child_object_id int = OBJECT_ID(N'[dbo].[T_OrderProcessNote]', N'U');
DECLARE @cp6_principal_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_child_object_id IS NULL OR @cp6_principal_object_id IS NULL
    THROW 51041, N'BUG141 missing dbo.T_OrderProcessNote or dbo.T_OrderDetail; FK repair refused.', 1;

DECLARE @cp6_named_object_id int = (
    SELECT object_id FROM sys.objects
    WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd');
IF @cp6_named_object_id IS NULL
BEGIN
    ALTER TABLE [dbo].[T_OrderProcessNote] WITH CHECK ADD CONSTRAINT [FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd]
        FOREIGN KEY ([WebOrderNo], [WebOrderDetailNo], [ProductCd]) REFERENCES [dbo].[T_OrderDetail] ([WebOrderNo], [WebOrderDetailNo], [ProductCd])
        ON DELETE CASCADE ON UPDATE NO ACTION;
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys AS fk
    WHERE fk.object_id = @cp6_named_object_id AND fk.schema_id = SCHEMA_ID(N'dbo')
      AND fk.parent_object_id = @cp6_child_object_id AND fk.referenced_object_id = @cp6_principal_object_id
      AND fk.delete_referential_action = 1 AND fk.update_referential_action = 0
      AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS fkc
           WHERE fkc.constraint_object_id = fk.object_id) = 3
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'WebOrderNo', N'WebOrderNo'), (2, N'WebOrderDetailNo', N'WebOrderDetailNo'), (3, N'ProductCd', N'ProductCd')) AS expected(ordinal, child_column, principal_column)
          LEFT JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = expected.ordinal
          LEFT JOIN sys.columns AS child
            ON child.object_id = fkc.parent_object_id AND child.column_id = fkc.parent_column_id
          LEFT JOIN sys.columns AS principal
            ON principal.object_id = fkc.referenced_object_id AND principal.column_id = fkc.referenced_column_id
          WHERE child.column_id IS NULL OR principal.column_id IS NULL
             OR child.object_id <> @cp6_child_object_id OR principal.object_id <> @cp6_principal_object_id
             OR child.name COLLATE Latin1_General_100_BIN2 <> expected.child_column COLLATE Latin1_General_100_BIN2
             OR principal.name COLLATE Latin1_General_100_BIN2 <> expected.principal_column COLLATE Latin1_General_100_BIN2))
    THROW 51041, N'BUG141 object dbo.FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd conflicts with the frozen FK definition; existing object preserved.', 1;

GO
PRINT N'CP6_BUG141_GUARD_4_END';
GO
IF @@TRANCOUNT<>1 THROW 51005,N'Exactly one outer fixture transaction required.',1;
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
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>139
 THROW 51005,N'Generated FK-only batches must not append migration history.',1;
PRINT N'CP6_BUG141_ALL_FOUR_NATIVE_DEFINITIONS_VERIFIED';
ROLLBACK TRANSACTION;
IF @@TRANCOUNT<>0 THROW 51005,N'Outer fixture rollback required.',1;
PRINT N'CP6_BUG141_EXPLICIT_ROLLBACK_COMPLETE';
GO