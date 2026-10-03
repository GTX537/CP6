using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

public static class Wp2RawOrderGates
{
    // Independent oracle: RestoreMissingOrderTablesPA070, not the PostgreSQL DDL helper.
    // Original migration SHA256: 94C390AF6D2A918C0295D97B58BC51B2C792710E266260EB16A4F831525147E4.
    // Populated SQL136 catalog SHA256: D2AF57B4A7F4EA18AD38AB02F3BBDF6E26D2076012D0D077861F3959C75B8AAB.
    // These 65 defaults remain live. Later defaults and the replaced Sheet indexes are outside this gate.
    private static readonly DefaultRule[] Defaults =
    [
        new("T_Order", "Status", ValueKind.Integer, "0"),
        new("T_Order", "McTransferFlg", ValueKind.Boolean, "0"),
        new("T_Order", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_Order", "IsDeleted", ValueKind.Boolean, "0"),
        new("T_OrderDetail", "Status", ValueKind.Integer, "0"),
        new("T_OrderDetail", "WfApprovalFlg", ValueKind.Boolean, "0"),
        new("T_OrderDetail", "McTransferFlg", ValueKind.Boolean, "0"),
        new("T_OrderDetail", "ProvisionalPriceFlg", ValueKind.Boolean, "0"),
        new("T_OrderDetail", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_OrderDetail", "IsDeleted", ValueKind.Boolean, "0"),
        new("T_OrderProcess", "MachineFixedFlg", ValueKind.Boolean, "0"),
        new("T_OrderProcess", "LossRate", ValueKind.Numeric, "0", 21, 8),
        new("T_OrderProcess", "MachineCount", ValueKind.Numeric, "0", 21, 8),
        new("T_OrderProcess", "LeadTimeDays", ValueKind.Integer, "0"),
        new("T_OrderProcess", "SortOrder", ValueKind.Integer, "0"),
        new("T_OrderProcess", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_OrderProcess", "IsDeleted", ValueKind.Boolean, "0"),
        new("T_OrderProcessNote", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_OrderProcessNote", "IsDeleted", ValueKind.Boolean, "0"),
        new("T_OrderMaterial", "MaterialTypeDiv", ValueKind.Text, "3"),
        new("T_OrderMaterial", "SupplyDiv", ValueKind.Text, "1"),
        new("T_OrderMaterial", "SupplyUnitPrice", ValueKind.Numeric, "0", 21, 8),
        new("T_OrderMaterial", "SortOrder", ValueKind.Integer, "0"),
        new("T_OrderMaterial", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_OrderMaterial", "IsDeleted", ValueKind.Boolean, "0"),
        new("T_SheetUnitPrice", "UnitPrice", ValueKind.Numeric, "0", 15, 4),
        new("T_SheetUnitPrice", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_SheetUnitPrice", "IsDeleted", ValueKind.Boolean, "0"),
        new("T_SheetUnitPriceEstimate", "UnitPrice", ValueKind.Numeric, "0", 15, 4),
        new("T_SheetUnitPriceEstimate", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_SheetUnitPriceEstimate", "IsDeleted", ValueKind.Boolean, "0"),
        new("T_PlateMold", "WdRev", ValueKind.Integer, "1"),
        new("T_PlateMold", "DuplicatePlateFlg", ValueKind.Boolean, "0"),
        new("T_PlateMold", "SheetWidth", ValueKind.Numeric, "0", 15, 4),
        new("T_PlateMold", "SheetFlow", ValueKind.Numeric, "0", 15, 4),
        new("T_PlateMold", "BladeWidth", ValueKind.Numeric, "0", 15, 4),
        new("T_PlateMold", "BladeFlow", ValueKind.Numeric, "0", 15, 4),
        new("T_PlateMold", "CompositionQty", ValueKind.Integer, "0"),
        new("T_PlateMold", "MfgQty", ValueKind.Integer, "1"),
        new("T_PlateMold", "ColorQty", ValueKind.Integer, "0"),
        new("T_PlateMold", "BookQty", ValueKind.Integer, "0"),
        new("T_PlateMold", "EstimateAmount", ValueKind.Numeric, "0", 15, 4),
        new("T_PlateMold", "DecisionAmount", ValueKind.Numeric, "0", 15, 4),
        new("T_PlateMold", "PurchaseAmount", ValueKind.Numeric, "0", 15, 4),
        new("T_PlateMold", "WdQty", ValueKind.Integer, "0"),
        new("T_PlateMold", "LimitWdQty", ValueKind.Integer, "0"),
        new("T_PlateMold", "AtachInfoSheetFront", ValueKind.Boolean, "0"),
        new("T_PlateMold", "AtachInfoSheetBack", ValueKind.Boolean, "0"),
        new("T_PlateMold", "AtachInfoActual", ValueKind.Boolean, "0"),
        new("T_PlateMold", "AtachInfoBaseplate", ValueKind.Boolean, "0"),
        new("T_PlateMold", "AtachInfoPositive", ValueKind.Boolean, "0"),
        new("T_PlateMold", "AtachInfoNegative", ValueKind.Boolean, "0"),
        new("T_PlateMold", "AtachInfoMo", ValueKind.Boolean, "0"),
        new("T_PlateMold", "AtachInfoFd", ValueKind.Boolean, "0"),
        new("T_PlateMold", "NeedDraft", ValueKind.Integer, "0"),
        new("T_PlateMold", "NeedMylar", ValueKind.Integer, "0"),
        new("T_PlateMold", "NeedGalley", ValueKind.Integer, "0"),
        new("T_PlateMold", "NeedProof", ValueKind.Integer, "0"),
        new("T_PlateMold", "NeedBlueprint", ValueKind.Integer, "0"),
        new("T_PlateMold", "NeedComp", ValueKind.Integer, "0"),
        new("T_PlateMold", "NeedDesignSheet", ValueKind.Integer, "0"),
        new("T_PlateMold", "Status", ValueKind.Integer, "0"),
        new("T_PlateMold", "McTransferFlg", ValueKind.Boolean, "0"),
        new("T_PlateMold", "CreateDate", ValueKind.WallClock, "GETDATE()"),
        new("T_PlateMold", "IsDeleted", ValueKind.Boolean, "0")
    ];

    private static readonly UniqueRule[] Uniques =
    [
        new("T_OrderProcess", "UX_T_OrderProcess_Pk", ["WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd"]),
        new("T_OrderProcessNote", "UX_T_OrderProcessNote_Pk", ["WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd"]),
        new("T_OrderMaterial", "UX_T_OrderMaterial_Pk", ["WebOrderNo", "WebOrderDetailNo", "ProductCd", "ProcessCd", "MaterialCd"]),
        new("T_PlateMold", "UX_T_PlateMold_NoRev", ["WdPtnNo", "WdRev"])
    ];

    // The caller verifies ownership first. This gate reads catalogs and evaluates known defaults;
    // it never inserts Order graphs, changes history, or creates database objects.
    public static async Task<string> VerifyAsync(DbConnection connection, bool pg)
    {
        Require(connection.State == ConnectionState.Open, "Raw Order gate requires the already verified open task connection.");
        Require(pg ? connection is NpgsqlConnection : connection is SqlConnection, "Raw Order provider must match the verified connection.");
        Require(Regex.IsMatch(connection.Database, "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"), "Raw Order gate requires a dedicated WP2 database.");
        Require(Defaults.Length == 65 && Defaults.Select(x => (x.Table, x.Column)).Distinct().Count() == 65,
            "Raw Order verifier must contain 65 distinct frozen defaults.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var schema = pg ? "public" : "dbo";
        var tables = Defaults.Select(x => x.Table).Distinct().ToArray();
        var columns = (await connection.QueryAsync<ColumnRow>(Command(pg ? PgColumns : SqlColumns,
            new { Schema = schema, Tables = tables }, token: timeout.Token))).ToArray();
        var indexes = (await connection.QueryAsync<IndexRow>(Command(pg ? PgIndexes : SqlIndexes,
            new { Schema = schema, Tables = tables }, token: timeout.Token))).ToArray();
        var failures = new List<string>();
        foreach (var rule in Defaults)
        {
            var name = $"{schema}.{rule.Table}.{rule.Column}";
            var matches = columns.Where(x => x.TableName == rule.Table && x.ColumnName == rule.Column).ToArray();
            if (matches.Length != 1)
            {
                failures.Add(columns.Any(x => x.TableName == rule.Table) ? $"Missing or ambiguous column {name}." : $"Missing table {schema}.{rule.Table} (default {rule.Column}).");
                continue;
            }
            var column = matches[0];
            if (!MatchesType(rule, column, pg)) failures.Add($"Unsupported or changed type for {name}.");
            if (column.IsNullable) failures.Add($"Frozen required column became nullable: {name}.");
            if (string.IsNullOrWhiteSpace(column.Definition)) failures.Add($"Missing default {name}.");
            else if (!AcceptsExpression(rule, column.Definition, pg)) failures.Add($"Changed or unsupported default {name}.");
        }
        foreach (var rule in Uniques)
        {
            var name = $"{schema}.{rule.Table}.{rule.Name}";
            var rows = indexes.Where(x => x.TableName == rule.Table && x.IndexName == rule.Name).OrderBy(x => x.Ordinal).ToArray();
            if (rows.Length == 0) { failures.Add($"Missing global unique index {name}."); continue; }
            if (!rows.Select(x => x.ColumnName).SequenceEqual(rule.Keys, StringComparer.Ordinal)
                || rows.Where((x, ordinal) => x.Ordinal != ordinal + 1).Any()
                || rows.Any(x => !x.IsUnique || !x.Enabled || !x.Validated || !x.Immediate || x.Included || x.Descending
                    || x.HasFilter || x.IgnoreDuplicateKeys || x.AccessMethod != "btree" || (pg && !x.NullsNotDistinct)))
                failures.Add($"Changed, disabled, filtered, or unsafe global unique index {name}; exact ordered global keys and {(pg ? "NULLS NOT DISTINCT" : "SQL unfiltered uniqueness")} required.");
        }
        Require(failures.Count == 0, string.Join(" ", failures));

        // Only expressions accepted by the small constant/clock grammar reach SELECT.
        // In particular, catalog contents cannot introduce function calls into scalar evaluation.
        foreach (var rule in Defaults.Where(x => x.Kind != ValueKind.WallClock))
        {
            var column = columns.Single(x => x.TableName == rule.Table && x.ColumnName == rule.Column);
            var sql = $"SELECT CAST(({column.Definition}) AS {TargetType(rule, pg)})";
            var value = await connection.ExecuteScalarAsync<object>(Command(sql, token: timeout.Token));
            var matches = value is not null && value is not DBNull && (rule.Kind switch
            {
                ValueKind.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture) == (rule.Literal == "1"),
                ValueKind.Text => string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), rule.Literal, StringComparison.Ordinal),
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture) == decimal.Parse(rule.Literal, CultureInfo.InvariantCulture)
            });
            Require(matches, $"Actual default evaluation differs for {schema}.{rule.Table}.{rule.Column}.");
        }
        await VerifyWallClocksAsync(connection, pg, columns, timeout.Token);
        return "65 original raw Order defaults on eight tables matched native catalog types and evaluated values; all eight CreateDate expressions advanced within one transaction as local wall clocks. Four exact global unique indexes are enabled, valid, unfiltered, and preserve ordered keys; PostgreSQL uses NULLS NOT DISTINCT. Read-only gate; no full Order business-graph acceptance implied.";
    }

    private static bool MatchesType(DefaultRule rule, ColumnRow column, bool pg)
    {
        if (!pg) return rule.Kind switch
        {
            ValueKind.Integer => column.TypeName == "int",
            ValueKind.Boolean => column.TypeName == "bit",
            ValueKind.Numeric => column.TypeName == "decimal" && column.Precision == rule.Precision && column.Scale == rule.Scale,
            ValueKind.Text => column.TypeName == "nvarchar" && column.Length == 2,
            ValueKind.WallClock => column.TypeName == "datetime2" && column.Scale == 7,
            _ => false
        };
        return rule.Kind switch
        {
            ValueKind.Integer => column.TypeName == "int4" && column.TypeModifier == -1,
            ValueKind.Boolean => column.TypeName == "bool" && column.TypeModifier == -1,
            ValueKind.Numeric => column.TypeName == "numeric" && column.FormattedType == $"numeric({rule.Precision},{rule.Scale})",
            // The PG adapter preserves SQL PAD SPACE behavior with bpchar. Capacity checks are
            // covered elsewhere; both native character(1) and unbounded bpchar store these literals.
            ValueKind.Text => column.TypeName == "bpchar" && column.TypeModifier is -1 or 5,
            ValueKind.WallClock => column.TypeName == "timestamp" && column.TypeModifier is -1 or 6,
            _ => false
        };
    }

    private static bool AcceptsExpression(DefaultRule rule, string expression, bool pg)
    {
        var value = StripParentheses(expression);
        if (rule.Kind == ValueKind.WallClock)
        {
            var normalized = Regex.Replace(value, @"\s+", " ").ToLowerInvariant();
            if (!pg) return normalized == "getdate()";
            // Deparsed :: syntax only. No transaction_timestamp/LOCALTIMESTAMP substitutes.
            var cast = Regex.Match(normalized, @"\A(?<clock>.+)::timestamp without time zone\z");
            return cast.Success && Regex.IsMatch(StripParentheses(cast.Groups["clock"].Value), @"\A(?:pg_catalog\.)?clock_timestamp\(\)\z");
        }
        if (pg)
        {
            // pg_get_expr can add constant casts and parentheses. Only casts to harmless,
            // built-in types in the frozen column's family are accepted, at most four layers.
            for (var layer = 0; layer < 4; layer++)
            {
                var cast = Regex.Match(value, @"\A(?<value>.+)::(?<type>(?:pg_catalog\.)?(?:integer|int4|boolean|bool|numeric(?:\(\d+,\s*\d+\))?|decimal(?:\(\d+,\s*\d+\))?|bpchar|character(?:\(\d+\))?|text|character varying(?:\(\d+\))?))\z", RegexOptions.IgnoreCase);
                if (!cast.Success) break;
                var type = Regex.Replace(cast.Groups["type"].Value.ToLowerInvariant(), @"\s+", " ").Replace("pg_catalog.", "", StringComparison.Ordinal);
                var allowed = rule.Kind switch
                {
                    ValueKind.Integer => type is "integer" or "int4",
                    ValueKind.Boolean => type is "boolean" or "bool",
                    ValueKind.Numeric => type.StartsWith("numeric", StringComparison.Ordinal) || type.StartsWith("decimal", StringComparison.Ordinal),
                    ValueKind.Text => type is "bpchar" or "text" || type.StartsWith("character", StringComparison.Ordinal),
                    _ => false
                };
                if (!allowed) return false;
                value = StripParentheses(cast.Groups["value"].Value);
            }
        }
        return rule.Kind switch
        {
            ValueKind.Boolean => value == rule.Literal || (pg && (value.Equals(rule.Literal == "0" ? "false" : "true", StringComparison.OrdinalIgnoreCase)
                || value.Equals(rule.Literal == "0" ? "'false'" : "'true'", StringComparison.OrdinalIgnoreCase))),
            ValueKind.Text => value == $"'{rule.Literal}'" || (!pg && value == $"N'{rule.Literal}'"),
            _ => value == rule.Literal || (pg && value == $"'{rule.Literal}'")
        };
    }

    private static string StripParentheses(string expression)
    {
        var value = expression.Trim();
        while (value.Length > 1 && value[0] == '(' && value[^1] == ')')
        {
            var depth = 0;
            var wrapsWhole = true;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '(') depth++;
                if (value[index] == ')') depth--;
                if (depth < 0 || (depth == 0 && index < value.Length - 1)) { wrapsWhole = false; break; }
            }
            if (!wrapsWhole || depth != 0) break;
            value = value[1..^1].Trim();
        }
        return value;
    }

    private static string TargetType(DefaultRule rule, bool pg) => rule.Kind switch
    {
        ValueKind.Integer => pg ? "integer" : "int",
        ValueKind.Boolean => pg ? "boolean" : "bit",
        ValueKind.Numeric => $"{(pg ? "numeric" : "decimal")}({rule.Precision},{rule.Scale})",
        ValueKind.Text => pg ? "bpchar" : "nvarchar(1)",
        ValueKind.WallClock => pg ? "timestamp without time zone" : "datetime2(7)",
        _ => throw new MigrationAssertionException("Unknown frozen raw Order type.")
    };

    private static async Task VerifyWallClocksAsync(DbConnection connection, bool pg, ColumnRow[] columns, CancellationToken token)
    {
        var rules = Defaults.Where(x => x.Kind == ValueKind.WallClock).ToArray();
        Require(rules.Length == 8, "Raw Order verifier requires eight original GETDATE defaults.");
        var wallClock = pg ? "clock_timestamp()::timestamp without time zone" : "CAST(GETDATE() AS datetime2(7))";
        var expressions = rules.Select(rule => $"CAST(({columns.Single(x => x.TableName == rule.Table && x.ColumnName == rule.Column).Definition}) AS {TargetType(rule, pg)})");
        var select = "SELECT " + string.Join(",", new[] { wallClock }.Concat(expressions).Append(wallClock));
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        if (pg) await connection.ExecuteAsync(Command("SET TRANSACTION READ ONLY", transaction: transaction, token: token));
        var first = await ReadClockValuesAsync(connection, transaction, select, token);
        await Task.Delay(TimeSpan.FromMilliseconds(150), token);
        var second = await ReadClockValuesAsync(connection, transaction, select, token);
        Require(first.Length == 10 && second.Length == 10, "Raw Order clock query must return eight defaults and wall-clock bounds.");
        Require(second[0] - first[0] >= TimeSpan.FromMilliseconds(50), "Database wall clock did not advance sufficiently for the raw Order check.");
        for (var index = 0; index < rules.Length; index++)
        {
            var name = $"{(pg ? "public" : "dbo")}.{rules[index].Table}.{rules[index].Column}";
            var a = first[index + 1];
            var b = second[index + 1];
            Require(a.Kind == DateTimeKind.Unspecified && b.Kind == DateTimeKind.Unspecified, $"Raw Order clock must preserve local wall-clock timestamp type for {name}.");
            Require(a >= first[0].AddSeconds(-2) && a <= first[^1].AddSeconds(2)
                && b >= second[0].AddSeconds(-2) && b <= second[^1].AddSeconds(2), $"Actual default clock differs from database local wall clock for {name}.");
            Require(b - a >= TimeSpan.FromMilliseconds(50), $"Actual default is transaction-fixed rather than a moving wall clock for {name}.");
        }
        await transaction.RollbackAsync(token);
    }

    private static async Task<DateTime[]> ReadClockValuesAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 15;
        command.Transaction = transaction;
        await using var reader = await command.ExecuteReaderAsync(token);
        Require(await reader.ReadAsync(token), "Raw Order default clock SELECT returned no row.");
        var values = Enumerable.Range(0, reader.FieldCount).Select(reader.GetDateTime).ToArray();
        Require(!await reader.ReadAsync(token), "Raw Order default clock SELECT returned multiple rows.");
        return values;
    }

    private static CommandDefinition Command(string sql, object? args = null, DbTransaction? transaction = null, CancellationToken token = default)
        => new(sql, args, transaction, commandTimeout: 15, cancellationToken: token);
    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
    private enum ValueKind { Integer, Boolean, Numeric, Text, WallClock }
    private sealed record DefaultRule(string Table, string Column, ValueKind Kind, string Literal, int Precision = 0, int Scale = 0);
    private sealed record UniqueRule(string Table, string Name, string[] Keys);

    private const string SqlColumns = """
        SELECT t.name AS TableName,c.name AS ColumnName,ty.name AS TypeName,
          c.max_length AS Length,c.precision AS Precision,c.scale AS Scale,c.is_nullable AS IsNullable,
          d.definition AS Definition
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
        LEFT JOIN sys.default_constraints d ON d.object_id=c.default_object_id AND d.parent_object_id=t.object_id
        WHERE s.name=@Schema AND t.name IN @Tables
        """;
    private const string PgColumns = """
        SELECT t.relname AS TableName,a.attname AS ColumnName,ty.typname AS TypeName,
          a.atttypmod AS TypeModifier,format_type(a.atttypid,a.atttypmod) AS FormattedType,
          NOT a.attnotnull AS IsNullable,pg_get_expr(d.adbin,d.adrelid) AS Definition
        FROM pg_class t JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped
        JOIN pg_type ty ON ty.oid=a.atttypid LEFT JOIN pg_attrdef d ON d.adrelid=t.oid AND d.adnum=a.attnum
        WHERE n.nspname=@Schema AND t.relkind IN('r','p') AND t.relname=ANY(@Tables)
        """;
    private const string SqlIndexes = """
        SELECT t.name AS TableName,i.name AS IndexName,c.name AS ColumnName,
          ic.key_ordinal AS Ordinal,i.is_unique AS IsUnique,ic.is_included_column AS Included,
          ic.is_descending_key AS Descending,CONVERT(bit,CASE WHEN i.is_disabled=0 AND i.is_hypothetical=0 THEN 1 ELSE 0 END) AS Enabled,
          CONVERT(bit,1) AS Validated,CONVERT(bit,1) AS Immediate,CONVERT(bit,0) AS NullsNotDistinct,
          i.has_filter AS HasFilter,i.ignore_dup_key AS IgnoreDuplicateKeys,
          CASE WHEN i.type IN(1,2) THEN 'btree' ELSE 'unsupported' END AS AccessMethod
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id
        JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=ic.column_id
        WHERE s.name=@Schema AND t.name IN @Tables AND i.name IS NOT NULL
        """;
    private const string PgIndexes = """
        SELECT t.relname AS TableName,ix.relname AS IndexName,a.attname AS ColumnName,k.ordinal::integer AS Ordinal,
          i.indisunique AS IsUnique,k.ordinal>i.indnkeyatts AS Included,
          CASE WHEN k.ordinal<=i.indnkeyatts THEN ((i.indoption::smallint[])[k.ordinal::integer-1]&1)=1 ELSE false END AS Descending,
          i.indisready AND i.indislive AS Enabled,i.indisvalid AS Validated,i.indimmediate AS Immediate,
          i.indnullsnotdistinct AS NullsNotDistinct,i.indpred IS NOT NULL AS HasFilter,false AS IgnoreDuplicateKeys,am.amname AS AccessMethod
        FROM pg_index i JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_class ix ON ix.oid=i.indexrelid JOIN pg_am am ON am.oid=ix.relam
        CROSS JOIN LATERAL unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ordinal)
        LEFT JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=k.attnum
        WHERE n.nspname=@Schema AND t.relname=ANY(@Tables)
        """;

    private sealed class ColumnRow
    {
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string FormattedType { get; set; } = "";
        public int TypeModifier { get; set; }
        public int Length { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
        public bool IsNullable { get; set; }
        public string? Definition { get; set; }
    }
    private sealed class IndexRow
    {
        public string TableName { get; set; } = "";
        public string IndexName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string AccessMethod { get; set; } = "";
        public int Ordinal { get; set; }
        public bool IsUnique { get; set; }
        public bool Included { get; set; }
        public bool Descending { get; set; }
        public bool Enabled { get; set; }
        public bool Validated { get; set; }
        public bool Immediate { get; set; }
        public bool NullsNotDistinct { get; set; }
        public bool HasFilter { get; set; }
        public bool IgnoreDuplicateKeys { get; set; }
    }
}
