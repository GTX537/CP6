BEGIN TRANSACTION;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_Order]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_Order; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_Order_OrderType_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_Order_OrderType_IsDeleted] ON [dbo].[T_Order] ([OrderType] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'OrderType'), (2, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_Order.IX_T_Order_OrderType_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_Order]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_Order; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_Order_Status_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_Order_Status_IsDeleted] ON [dbo].[T_Order] ([Status] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'Status'), (2, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_Order.IX_T_Order_Status_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderDetail; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderDetail_ApprovalStatus_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderDetail_ApprovalStatus_IsDeleted] ON [dbo].[T_OrderDetail] ([ApprovalStatus] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'ApprovalStatus'), (2, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderDetail.IX_T_OrderDetail_ApprovalStatus_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderDetail; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderDetail_CustomerDeliveryDate_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderDetail_CustomerDeliveryDate_IsDeleted] ON [dbo].[T_OrderDetail] ([CustomerDeliveryDate] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'CustomerDeliveryDate'), (2, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderDetail.IX_T_OrderDetail_CustomerDeliveryDate_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderDetail; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderDetail_HaibaiNo2_HaibaiNo3');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderDetail_HaibaiNo2_HaibaiNo3] ON [dbo].[T_OrderDetail] ([HaibaiNo2] ASC, [HaibaiNo3] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'HaibaiNo2'), (2, N'HaibaiNo3')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderDetail.IX_T_OrderDetail_HaibaiNo2_HaibaiNo3 conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderDetail; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderDetail_ItemCd');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderDetail_ItemCd] ON [dbo].[T_OrderDetail] ([ItemCd] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'ItemCd')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderDetail.IX_T_OrderDetail_ItemCd conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderDetail; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderDetail_McTransferFlg_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderDetail_McTransferFlg_IsDeleted] ON [dbo].[T_OrderDetail] ([McTransferFlg] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'McTransferFlg'), (2, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderDetail.IX_T_OrderDetail_McTransferFlg_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderDetail]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderDetail; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderDetail_ProductCatBig_ProductCatMid_ProductCatSml');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderDetail_ProductCatBig_ProductCatMid_ProductCatSml] ON [dbo].[T_OrderDetail] ([ProductCatBig] ASC, [ProductCatMid] ASC, [ProductCatSml] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'ProductCatBig'), (2, N'ProductCatMid'), (3, N'ProductCatSml')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderDetail.IX_T_OrderDetail_ProductCatBig_ProductCatMid_ProductCatSml conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderMaterial]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderMaterial; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_SortOrder');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_SortOrder] ON [dbo].[T_OrderMaterial] ([WebOrderNo] ASC, [WebOrderDetailNo] ASC, [SortOrder] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'WebOrderNo'), (2, N'WebOrderDetailNo'), (3, N'SortOrder')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderMaterial.IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_SortOrder conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderProcess]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderProcess; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderProcess_ProcessCd');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderProcess_ProcessCd] ON [dbo].[T_OrderProcess] ([ProcessCd] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'ProcessCd')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderProcess.IX_T_OrderProcess_ProcessCd conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_OrderProcess]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_OrderProcess; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_SortOrder');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_SortOrder] ON [dbo].[T_OrderProcess] ([WebOrderNo] ASC, [WebOrderDetailNo] ASC, [SortOrder] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'WebOrderNo'), (2, N'WebOrderDetailNo'), (3, N'SortOrder')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_OrderProcess.IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_SortOrder conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_PlateMold]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_PlateMold; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_PlateMold_BaseCd_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_PlateMold_BaseCd_IsDeleted] ON [dbo].[T_PlateMold] ([BaseCd] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'BaseCd'), (2, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_PlateMold.IX_T_PlateMold_BaseCd_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_PlateMold]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_PlateMold; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_PlateMold_PlaceCd');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_PlateMold_PlaceCd] ON [dbo].[T_PlateMold] ([PlaceCd] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'PlaceCd')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_PlateMold.IX_T_PlateMold_PlaceCd conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_PlateMold]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_PlateMold; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_PlateMold_ProcessCd');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_PlateMold_ProcessCd] ON [dbo].[T_PlateMold] ([ProcessCd] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'ProcessCd')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_PlateMold.IX_T_PlateMold_ProcessCd conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_PlateMold]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_PlateMold; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_PlateMold_RepresentativeProductCd');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_PlateMold_RepresentativeProductCd] ON [dbo].[T_PlateMold] ([RepresentativeProductCd] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'RepresentativeProductCd')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_PlateMold.IX_T_PlateMold_RepresentativeProductCd conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_PlateMold]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_PlateMold; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_PlateMold_StDate_EndDate');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_PlateMold_StDate_EndDate] ON [dbo].[T_PlateMold] ([StDate] ASC, [EndDate] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'StDate'), (2, N'EndDate')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_PlateMold.IX_T_PlateMold_StDate_EndDate conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_PlateMold]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_PlateMold; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_PlateMold_TypeClass');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_PlateMold_TypeClass] ON [dbo].[T_PlateMold] ([TypeClass] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'TypeClass')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_PlateMold.IX_T_PlateMold_TypeClass conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_SheetUnitPrice]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_SheetUnitPrice; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_SheetUnitPrice_BaseCd_CustomerCd_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_SheetUnitPrice_BaseCd_CustomerCd_IsDeleted] ON [dbo].[T_SheetUnitPrice] ([BaseCd] ASC, [CustomerCd] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'BaseCd'), (2, N'CustomerCd'), (3, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_SheetUnitPrice.IX_T_SheetUnitPrice_BaseCd_CustomerCd_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_SheetUnitPrice]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_SheetUnitPrice; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_SheetUnitPrice_RevisionDate');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_SheetUnitPrice_RevisionDate] ON [dbo].[T_SheetUnitPrice] ([RevisionDate] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'RevisionDate')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_SheetUnitPrice.IX_T_SheetUnitPrice_RevisionDate conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_SheetUnitPriceEstimate]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_SheetUnitPriceEstimate; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_SheetUnitPriceEstimate_BaseCd_CustomerCd_IsDeleted');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_SheetUnitPriceEstimate_BaseCd_CustomerCd_IsDeleted] ON [dbo].[T_SheetUnitPriceEstimate] ([BaseCd] ASC, [CustomerCd] ASC, [IsDeleted] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'BaseCd'), (2, N'CustomerCd'), (3, N'IsDeleted')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_SheetUnitPriceEstimate.IX_T_SheetUnitPriceEstimate_BaseCd_CustomerCd_IsDeleted conflicts with the frozen definition; existing object preserved.', 1;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_SheetUnitPriceEstimate]', N'U');
IF @cp6_object_id IS NULL
    THROW 51039, N'BUG139 missing table dbo.T_SheetUnitPriceEstimate; index repair refused.', 1;

DECLARE @cp6_index_id int = (
    SELECT index_id FROM sys.indexes
    WHERE object_id = @cp6_object_id AND name = N'IX_T_SheetUnitPriceEstimate_RevisionDate');
IF @cp6_index_id IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_T_SheetUnitPriceEstimate_RevisionDate] ON [dbo].[T_SheetUnitPriceEstimate] ([RevisionDate] ASC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
      AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.has_filter = 0 AND i.filter_definition IS NULL
      AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
      AND NOT EXISTS (
          SELECT 1 FROM (VALUES (1, N'RevisionDate')) AS expected(key_ordinal, column_name)
          LEFT JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
          LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
             OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
    THROW 51039, N'BUG139 index dbo.T_SheetUnitPriceEstimate.IX_T_SheetUnitPriceEstimate_RevisionDate conflicts with the frozen definition; existing object preserved.', 1;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261002175000_RestoreMissingOrderModelIndexes', N'8.0.30');
GO

COMMIT;
GO

