namespace CP6.Core.Persistence;

/// <summary>Forward SQL Server repair of Issue #139's frozen, nonunique query indexes.</summary>
public static class OrderModelIndexRepairSql
{
    private sealed record Definition(string Table, string Name, string[] Columns);

    // Ordered SQL column names from the unchanged d6074aaa Core snapshot.
    // The two unique indexes with equivalent historical names are excluded.
    private static readonly Definition[] Definitions =
    [
        new("T_Order", "IX_T_Order_OrderType_IsDeleted", ["OrderType", "IsDeleted"]),
        new("T_Order", "IX_T_Order_Status_IsDeleted", ["Status", "IsDeleted"]),
        new("T_OrderDetail", "IX_T_OrderDetail_ApprovalStatus_IsDeleted", ["ApprovalStatus", "IsDeleted"]),
        new("T_OrderDetail", "IX_T_OrderDetail_CustomerDeliveryDate_IsDeleted", ["CustomerDeliveryDate", "IsDeleted"]),
        new("T_OrderDetail", "IX_T_OrderDetail_HaibaiNo2_HaibaiNo3", ["HaibaiNo2", "HaibaiNo3"]),
        new("T_OrderDetail", "IX_T_OrderDetail_ItemCd", ["ItemCd"]),
        new("T_OrderDetail", "IX_T_OrderDetail_McTransferFlg_IsDeleted", ["McTransferFlg", "IsDeleted"]),
        new("T_OrderDetail", "IX_T_OrderDetail_ProductCatBig_ProductCatMid_ProductCatSml", ["ProductCatBig", "ProductCatMid", "ProductCatSml"]),
        new("T_OrderMaterial", "IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_SortOrder", ["WebOrderNo", "WebOrderDetailNo", "SortOrder"]),
        new("T_OrderProcess", "IX_T_OrderProcess_ProcessCd", ["ProcessCd"]),
        new("T_OrderProcess", "IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_SortOrder", ["WebOrderNo", "WebOrderDetailNo", "SortOrder"]),
        new("T_PlateMold", "IX_T_PlateMold_BaseCd_IsDeleted", ["BaseCd", "IsDeleted"]),
        new("T_PlateMold", "IX_T_PlateMold_PlaceCd", ["PlaceCd"]),
        new("T_PlateMold", "IX_T_PlateMold_ProcessCd", ["ProcessCd"]),
        new("T_PlateMold", "IX_T_PlateMold_RepresentativeProductCd", ["RepresentativeProductCd"]),
        new("T_PlateMold", "IX_T_PlateMold_StDate_EndDate", ["StDate", "EndDate"]),
        new("T_PlateMold", "IX_T_PlateMold_TypeClass", ["TypeClass"]),
        new("T_SheetUnitPrice", "IX_T_SheetUnitPrice_BaseCd_CustomerCd_IsDeleted", ["BaseCd", "CustomerCd", "IsDeleted"]),
        new("T_SheetUnitPrice", "IX_T_SheetUnitPrice_RevisionDate", ["RevisionDate"]),
        new("T_SheetUnitPriceEstimate", "IX_T_SheetUnitPriceEstimate_BaseCd_CustomerCd_IsDeleted", ["BaseCd", "CustomerCd", "IsDeleted"]),
        new("T_SheetUnitPriceEstimate", "IX_T_SheetUnitPriceEstimate_RevisionDate", ["RevisionDate"])
    ];

    /// <summary>Each statement is a separate migration command in the same EF transaction.</summary>
    public static IReadOnlyList<string> CreateStatements() => Array.AsReadOnly(Definitions.Select(CreateStatement).ToArray());

    private static string CreateStatement(Definition definition)
    {
        var keys = string.Join(", ", definition.Columns.Select(column => $"[{column}] ASC"));
        var expected = string.Join(", ", definition.Columns.Select((column, position) => $"({position + 1}, N'{column}')"));
        return $$"""
            DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[{{definition.Table}}]', N'U');
            IF @cp6_object_id IS NULL
                THROW 51039, N'BUG139 missing table dbo.{{definition.Table}}; index repair refused.', 1;

            DECLARE @cp6_index_id int = (
                SELECT index_id FROM sys.indexes
                WHERE object_id = @cp6_object_id AND name = N'{{definition.Name}}');
            IF @cp6_index_id IS NULL
            BEGIN
                CREATE NONCLUSTERED INDEX [{{definition.Name}}] ON [dbo].[{{definition.Table}}] ({{keys}});
            END
            ELSE IF NOT EXISTS (
                SELECT 1 FROM sys.indexes AS i
                WHERE i.object_id = @cp6_object_id AND i.index_id = @cp6_index_id
                  AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
                  AND i.has_filter = 0 AND i.filter_definition IS NULL
                  AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.ignore_dup_key = 0
                  AND (SELECT COUNT(*) FROM sys.index_columns AS ic
                       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = {{definition.Columns.Length}}
                  AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
                       WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
                  AND NOT EXISTS (
                      SELECT 1 FROM (VALUES {{expected}}) AS expected(key_ordinal, column_name)
                      LEFT JOIN sys.index_columns AS ic
                        ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = expected.key_ordinal
                      LEFT JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE c.column_id IS NULL OR ic.is_descending_key <> 0
                         OR c.name COLLATE Latin1_General_100_BIN2 <> expected.column_name COLLATE Latin1_General_100_BIN2))
                THROW 51039, N'BUG139 index dbo.{{definition.Table}}.{{definition.Name}} conflicts with the frozen definition; existing object preserved.', 1;
            """;
    }
}
