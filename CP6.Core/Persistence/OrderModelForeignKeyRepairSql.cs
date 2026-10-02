namespace CP6.Core.Persistence;

/// <summary>SQL Server forward repair of Issue #141's four frozen order relationships.</summary>
public static class OrderModelForeignKeyRepairSql
{
    private sealed record Definition(string Table, string Name, string Principal, string[] Columns);

    // These global business keys are intentionally unchanged; no TenantId or new unique key.
    private static readonly Definition[] Definitions =
    [
        new("T_OrderDetail", "FK_T_OrderDetail_T_Order_WebOrderNo", "T_Order", ["WebOrderNo"]),
        new("T_OrderMaterial", "FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd", "T_OrderDetail", ["WebOrderNo", "WebOrderDetailNo", "ProductCd"]),
        new("T_OrderProcess", "FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd", "T_OrderDetail", ["WebOrderNo", "WebOrderDetailNo", "ProductCd"]),
        new("T_OrderProcessNote", "FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd", "T_OrderDetail", ["WebOrderNo", "WebOrderDetailNo", "ProductCd"])
    ];

    /// <summary>Run as separate commands in the same EF migration transaction.</summary>
    public static IReadOnlyList<string> CreateStatements() => Array.AsReadOnly(Definitions.Select(CreateStatement).ToArray());

    private static string CreateStatement(Definition definition)
    {
        var columns = string.Join(", ", definition.Columns.Select(column => $"[{column}]"));
        var expected = string.Join(", ", definition.Columns.Select((column, position) => $"({position + 1}, N'{column}', N'{column}')"));
        return $$"""
            DECLARE @cp6_child_object_id int = OBJECT_ID(N'[dbo].[{{definition.Table}}]', N'U');
            DECLARE @cp6_principal_object_id int = OBJECT_ID(N'[dbo].[{{definition.Principal}}]', N'U');
            IF @cp6_child_object_id IS NULL OR @cp6_principal_object_id IS NULL
                THROW 51041, N'BUG141 missing dbo.{{definition.Table}} or dbo.{{definition.Principal}}; FK repair refused.', 1;

            DECLARE @cp6_named_object_id int = (
                SELECT object_id FROM sys.objects
                WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'{{definition.Name}}');
            IF @cp6_named_object_id IS NULL
            BEGIN
                ALTER TABLE [dbo].[{{definition.Table}}] WITH CHECK ADD CONSTRAINT [{{definition.Name}}]
                    FOREIGN KEY ({{columns}}) REFERENCES [dbo].[{{definition.Principal}}] ({{columns}})
                    ON DELETE CASCADE ON UPDATE NO ACTION;
            END
            ELSE IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys AS fk
                WHERE fk.object_id = @cp6_named_object_id AND fk.schema_id = SCHEMA_ID(N'dbo')
                  AND fk.parent_object_id = @cp6_child_object_id AND fk.referenced_object_id = @cp6_principal_object_id
                  AND fk.delete_referential_action = 1 AND fk.update_referential_action = 0
                  AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
                  AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS fkc
                       WHERE fkc.constraint_object_id = fk.object_id) = {{definition.Columns.Length}}
                  AND NOT EXISTS (
                      SELECT 1 FROM (VALUES {{expected}}) AS expected(ordinal, child_column, principal_column)
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
                THROW 51041, N'BUG141 object dbo.{{definition.Name}} conflicts with the frozen FK definition; existing object preserved.', 1;
            """;
    }
}
