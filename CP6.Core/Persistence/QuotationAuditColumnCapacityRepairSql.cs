namespace CP6.Core.Persistence;

/// <summary>SQL Server forward repair of Issue #143's six frozen quotation audit capacities.</summary>
public static class QuotationAuditColumnCapacityRepairSql
{
    private sealed record Definition(string Table, string Column);

    private static readonly Definition[] Definitions =
    [
        new("T_Quotation", "Creator"), new("T_Quotation", "Modifier"),
        new("T_QuotationCalc", "Creator"), new("T_QuotationCalc", "Modifier"),
        new("T_QuotationDetail", "Creator"), new("T_QuotationDetail", "Modifier")
    ];

    /// <summary>Run these six separate commands in the same EF migration transaction.</summary>
    public static IReadOnlyList<string> CreateStatements() => Array.AsReadOnly(Definitions.Select(CreateStatement).ToArray());

    private static string CreateStatement(Definition definition) => $$"""
        DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[{{definition.Table}}]', N'U');
        IF @cp6_object_id IS NULL OR NOT EXISTS (
            SELECT 1 FROM sys.tables
            WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
              AND name COLLATE Latin1_General_100_BIN2 = N'{{definition.Table}}' COLLATE Latin1_General_100_BIN2)
            THROW 51043, N'BUG143 missing exact dbo.{{definition.Table}}; audit capacity repair refused.', 1;

        DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
        SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
        FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = @cp6_object_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'{{definition.Column}}' COLLATE Latin1_General_100_BIN2
          AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
          AND c.system_type_id = TYPE_ID(N'nvarchar')
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.max_length IN (-1, 200);
        IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N''
           OR @cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
           OR NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations()
                          WHERE name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
            THROW 51043, N'BUG143 dbo.{{definition.Table}}.{{definition.Column}} has an unknown definition or collation; existing column preserved.', 1;

        IF @cp6_max_length = -1
        BEGIN
            -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
            -- Retain the exclusive table lock until the outer migration transaction finishes.
            -- Bind the data column only after the metadata guard; a missing column must
            -- reach 51043 rather than static batch compilation failing with SQL 207.
            DECLARE @cp6_overlong bit = 0;
            DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong = CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[{{definition.Table}}] WITH (TABLOCKX, HOLDLOCK)
                WHERE DATALENGTH([{{definition.Column}}]) > 200) THEN 1 ELSE 0 END;';
            EXEC sys.sp_executesql @cp6_check, N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT;
            IF @cp6_overlong = 1
                THROW 51043, N'BUG143 dbo.{{definition.Table}}.{{definition.Column}} contains more than 100 UTF-16 units; no data changed.', 1;

            -- Revalidate after acquiring the data lock, before using the captured collation.
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns AS c
                WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
                  AND c.name COLLATE Latin1_General_100_BIN2 = N'{{definition.Column}}' COLLATE Latin1_General_100_BIN2
                  AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
                  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
                  AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
                THROW 51043, N'BUG143 dbo.{{definition.Table}}.{{definition.Column}} metadata changed; audit capacity repair refused.', 1;

            DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[{{definition.Table}}] ALTER COLUMN [{{definition.Column}}] nvarchar(100) COLLATE '
                + @cp6_collation + N' NULL;';
            EXEC sys.sp_executesql @cp6_alter;
        END;
        """;
}
