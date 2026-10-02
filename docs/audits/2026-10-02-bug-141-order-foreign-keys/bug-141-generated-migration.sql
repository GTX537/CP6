BEGIN TRANSACTION;
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

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261002184500_RestoreMissingOrderModelForeignKeys', N'8.0.30');
GO

COMMIT;
GO

