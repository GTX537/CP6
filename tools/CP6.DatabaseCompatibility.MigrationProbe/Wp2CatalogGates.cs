using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

/// <summary>
/// Reads installed catalogs against the actual design relational model. Extra historical
/// objects are allowed. Check presence, trust/validation and referenced columns are checked;
/// arbitrary check-expression equivalence remains a separate real-write invariant gate.
/// </summary>
public static class Wp2CatalogGates
{
    public static Task VerifyAsync(DbConnection connection, bool pg, IModel model) =>
        VerifyAsync(connection, pg, model, transaction: null);

    // A SQL Server local transaction must be explicitly assigned to each command.
    // This overload supports the root-owned rollback-only negative catalog fixtures.
    public static async Task VerifyAsync(DbConnection connection, bool pg, IModel model, DbTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(model);
        Require(connection.State == ConnectionState.Open, "An already open task database connection is required.");

        var columns = (await connection.QueryAsync<ColumnRow>(pg ? PgColumns : SqlColumns, transaction: transaction)).ToArray();
        var keys = (await connection.QueryAsync<KeyRow>(pg ? PgKeys : SqlKeys, transaction: transaction)).ToArray();
        var indexes = (await connection.QueryAsync<IndexRow>(pg ? PgIndexes : SqlIndexes, transaction: transaction)).ToArray();
        var foreignKeys = (await connection.QueryAsync<ForeignRow>(pg ? PgForeignKeys : SqlForeignKeys, transaction: transaction)).ToArray();
        var checks = (await connection.QueryAsync<CheckRow>(pg ? PgChecks : SqlChecks, transaction: transaction)).ToArray();
        var nativeTokenDifferences = new List<string>();
        var tables = model.GetRelationalModel().Tables.ToArray();

        foreach (var table in tables)
        {
            var schema = table.Schema ?? (pg ? "public" : "dbo");
            var label = $"{schema}.{table.Name}";
            var actualColumns = columns.Where(c => c.SchemaName == schema && c.TableName == table.Name)
                .ToDictionary(c => c.ColumnName, StringComparer.Ordinal);
            var boolColumns = table.Columns.Where(c => c.PropertyMappings.Any(m =>
                (Nullable.GetUnderlyingType(m.Property.ClrType) ?? m.Property.ClrType) == typeof(bool)))
                .Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var nonNullColumns = table.Columns.Where(c => !c.IsNullable
                    && actualColumns.TryGetValue(c.Name, out var actual) && !actual.IsNullable)
                .Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var column in table.Columns)
            {
                Require(actualColumns.TryGetValue(column.Name, out var actual), $"Missing column {label}.{column.Name}.");
                VerifyType(column, actual!, pg, label);
                VerifyCollation(column, actual!, pg, model, label);
                if (column.IsNullable != actual!.IsNullable)
                {
                    // Native SQL rowversion catalog nullability can differ from the immutable EF snapshot.
                    // Its generation/type/byte width is checked here and by the separate token writer gate.
                    if (!pg && column.IsRowVersion && actual.TypeName is "timestamp" or "rowversion")
                        nativeTokenDifferences.Add($"{label}.{column.Name}: model={column.IsNullable}, catalog={actual.IsNullable}");
                    else Require(false, $"Column nullability differs for {label}.{column.Name}.");
                }
            }

            foreach (var key in table.UniqueConstraints)
            {
                var actual = keys.Where(k => k.SchemaName == schema && k.TableName == table.Name && k.ObjectName == key.Name)
                    .OrderBy(k => k.Ordinal).ToArray();
                if (!pg && actual.Length == 0 && schema == "dbo" && !key.MappedKeys.Any(k => k.IsPrimaryKey()))
                {
                    // These immutable SQL migrations enforce the exact global alternate
                    // key through a standalone unique index, also a valid SQL FK target.
                    // No generic name/column fallback or filtered uniqueness is accepted.
                    var historicalName = (table.Name, key.Name) switch
                    {
                        ("T_OrderDetail", "AK_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd") => "UX_T_OrderDetail_OrderProduct",
                        ("T_Quotation", "AK_T_Quotation_QtnNo") => "IX_T_Quotation_QtnNo",
                        _ => null
                    };
                    if (historicalName is not null)
                    {
                        var backing = indexes.Where(i => i.SchemaName == schema && i.TableName == table.Name && i.ObjectName == historicalName)
                            .OrderBy(i => i.Ordinal).ToArray();
                        Require(backing.All(i => i.IsUnique && i.TypeKind == 2 && !i.Included && !i.Descending && i.Definition is null),
                            $"Historical alternate-key index definition differs for {label}.{key.Name}.");
                        actual = backing.Select(i => new KeyRow { SchemaName = i.SchemaName, TableName = i.TableName,
                            ObjectName = i.ObjectName, ColumnName = i.ColumnName, Ordinal = i.Ordinal,
                            Enabled = i.Enabled, Validated = i.Validated, IgnoreDuplicateKeys = i.IgnoreDuplicateKeys }).ToArray();
                    }
                }
                Require(actual.Length != 0, $"Missing key {label}.{key.Name}.");
                Require(actual.All(k => k.Enabled && k.Validated && !k.Deferrable), $"Disabled/unvalidated/deferred key {label}.{key.Name}.");
                if (!pg)
                    Require(actual.All(k => !k.IgnoreDuplicateKeys), $"Key duplicate rejection differs for {label}.{key.Name}.");
                Require(actual.All(k => k.IsPrimary == key.MappedKeys.Any(k => k.IsPrimaryKey())), $"Key kind differs for {label}.{key.Name}.");
                EqualColumns(key.Columns.Select(c => c.Name), actual.Select(k => k.ColumnName), $"key {label}.{key.Name}");
            }

            foreach (var index in table.Indexes)
            {
                var actual = indexes.Where(i => i.SchemaName == schema && i.TableName == table.Name && i.ObjectName == index.Name)
                    .OrderBy(i => i.Ordinal).ToArray();
                if (!pg && actual.Length == 0 && schema == "dbo")
                {
                    // Immutable restored-order SQL DDL uses these two historical names.
                    // All following key/unique/filter/storage checks still apply to the alias.
                    var historicalName = (table.Name, index.Name) switch
                    {
                        ("T_Order", "IX_T_Order_WebOrderNo") => "AK_T_Order_WebOrderNo",
                        ("T_OrderDetail", "UX_OrderDetail_OrderProduct") => "UX_T_OrderDetail_OrderProduct",
                        _ => null
                    };
                    if (historicalName is not null)
                        actual = indexes.Where(i => i.SchemaName == schema && i.TableName == table.Name && i.ObjectName == historicalName)
                            .OrderBy(i => i.Ordinal).ToArray();
                }
                Require(actual.Length != 0, $"Missing index {label}.{index.Name}.");
                Require(actual.All(i => i.Enabled && i.Validated), $"Disabled/unvalidated index {label}.{index.Name}.");
                Require(actual.All(i => i.IsUnique == index.IsUnique), $"Index uniqueness differs for {label}.{index.Name}.");
                if (!pg && index.IsUnique)
                    Require(actual.All(i => !i.IgnoreDuplicateKeys), $"Index duplicate rejection differs for {label}.{index.Name}.");
                var keyColumns = actual.Where(i => !i.Included).ToArray();
                EqualColumns(index.Columns.Select(c => c.Name), keyColumns.Select(i => i.ColumnName), $"index {label}.{index.Name}");
                var descending = index.IsDescending;
                for (var i = 0; i < keyColumns.Length; i++)
                {
                    var expected = descending is not null && (descending.Count == 0 || descending[i]);
                    Require(expected == keyColumns[i].Descending, $"Index direction differs for {label}.{index.Name}.");
                }
                var actualFilter = actual[0].Definition;
                Require(NormalizeFilter(index.Filter, pg, boolColumns, nonNullColumns) == NormalizeFilter(actualFilter, pg, boolColumns, nonNullColumns),
                    $"Index filter differs for {label}.{index.Name}.");
                if (pg && index.IsUnique)
                {
                    var expectedNotDistinct = index.MappedIndexes.Any(i => i.FindAnnotation("Npgsql:NullsDistinct")?.Value is false);
                    Require(actual.All(i => i.NullsNotDistinct == expectedNotDistinct), $"Index NULL uniqueness differs for {label}.{index.Name}.");
                }
                var includeNames = index.MappedIndexes.SelectMany(i =>
                    (i.FindAnnotation(pg ? "Npgsql:IndexInclude" : "SqlServer:Include")?.Value as IEnumerable<string>) ?? [])
                    .Distinct(StringComparer.Ordinal).Select(name =>
                        index.Table.Columns.Single(c => c.PropertyMappings.Any(m => m.Property.Name == name)).Name).ToArray();
                Require(includeNames.Order(StringComparer.Ordinal).SequenceEqual(actual.Where(i => i.Included)
                    .Select(i => i.ColumnName).Order(StringComparer.Ordinal)), $"Index included columns differ for {label}.{index.Name}.");
            }

            foreach (var foreignKey in table.ForeignKeyConstraints)
            {
                var actual = foreignKeys.Where(f => f.SchemaName == schema && f.TableName == table.Name && f.ObjectName == foreignKey.Name)
                    .OrderBy(f => f.Ordinal).ToArray();
                Require(actual.Length != 0, $"Missing FK {label}.{foreignKey.Name}.");
                Require(actual.All(f => f.Enabled && f.Validated && !f.Deferrable), $"Disabled/unvalidated/deferred FK {label}.{foreignKey.Name}.");
                var principalSchema = foreignKey.PrincipalTable.Schema ?? (pg ? "public" : "dbo");
                Require(actual.All(f => f.PrincipalSchema == principalSchema && f.PrincipalTable == foreignKey.PrincipalTable.Name),
                    $"FK principal differs for {label}.{foreignKey.Name}.");
                EqualColumns(foreignKey.Columns.Select(c => c.Name), actual.Select(f => f.ColumnName), $"FK {label}.{foreignKey.Name}");
                EqualColumns(foreignKey.PrincipalColumns.Select(c => c.Name), actual.Select(f => f.PrincipalColumn), $"FK principal {label}.{foreignKey.Name}");
                var deleteAction = foreignKey.OnDeleteAction.ToString().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                if (!pg && deleteAction == "RESTRICT") deleteAction = "NOACTION";
                Require(actual.All(f => f.DeleteAction.Replace("_", "", StringComparison.Ordinal) == deleteAction),
                    $"FK delete action differs for {label}.{foreignKey.Name}.");
                Require(actual.All(f => f.UpdateAction == "NO_ACTION"), $"Unexpected FK update action for {label}.{foreignKey.Name}.");
            }

            foreach (var check in table.CheckConstraints)
            {
                var name = check.Name ?? check.ModelName;
                var actual = checks.SingleOrDefault(c => c.SchemaName == schema && c.TableName == table.Name && c.ObjectName == name);
                Require(actual is not null, $"Missing check {label}.{name}.");
                Require(actual!.Enabled && actual.Validated, $"Disabled/unvalidated check {label}.{name}.");
                var expectedReferences = ReferencedColumns(check.Sql, actualColumns.Keys, pg);
                var actualReferences = ReferencedColumns(actual.Definition, actualColumns.Keys, pg);
                EqualColumns(expectedReferences, actualReferences, $"check references {label}.{name}");
            }
        }
        if (nativeTokenDifferences.Count != 0)
            Console.WriteLine("Observed immutable SQL native rowversion nullability differences: " + string.Join("; ", nativeTokenDifferences));
        Console.WriteLine($"Verified catalog model objects in {tables.Length} tables; check-expression behavior remains the separate invariant gate.");
    }

    private static void VerifyType(IColumn column, ColumnRow actual, bool pg, string table)
    {
        var declared = Regex.Match(column.StoreType.Trim(), @"\A([^()]+?)(?:\(([^)]+)\))?\z");
        Require(declared.Success, $"Unclassified model type for {table}.{column.Name}.");
        var family = declared.Groups[1].Value.Trim().ToLowerInvariant();
        var size = declared.Groups[2].Value.Replace(" ", "", StringComparison.Ordinal);
        string Alias(string value) => value switch
        {
            "rowversion" => "timestamp", "numeric" when !pg => "decimal", "decimal" when pg => "numeric",
            "int" when pg => "integer", "float8" => "double precision", "float4" => "real", "bool" => "boolean",
            "varchar" when pg => "character varying", "char" when pg => "character", "timestamptz" => "timestamp with time zone",
            "timestamp" when pg => "timestamp without time zone", _ => value
        };
        Require(Alias(family) == Alias(actual.TypeName.ToLowerInvariant()), $"Column type differs for {table}.{column.Name}.");
        if (!pg && column.IsRowVersion)
            Require(actual.Length == 8, $"Native SQL rowversion width differs for {table}.{column.Name}.");
        if (family is "decimal" or "numeric")
        {
            var parts = size.Length == 0 ? Array.Empty<string>() : size.Split(',');
            var precision = parts.Length > 0 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : column.Precision;
            var scale = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : column.Scale;
            Require(precision is null || precision == actual.Precision, $"Numeric precision differs for {table}.{column.Name}.");
            Require(scale is null || scale == actual.Scale, $"Numeric scale differs for {table}.{column.Name}.");
        }
        if (family is "nvarchar" or "nchar" or "varchar" or "char" or "binary" or "varbinary" or "character" or "character varying")
        {
            var expected = size == "max" ? -1 : size.Length == 0 ? column.MaxLength : int.Parse(size, CultureInfo.InvariantCulture);
            var actualLength = !pg && family is "nvarchar" or "nchar" ? (actual.Length == -1 ? -1 : actual.Length / 2) : actual.Length;
            Require(expected is null || expected == actualLength, $"Declared column capacity differs for {table}.{column.Name}.");
        }
        if (family is "datetime2" or "datetimeoffset" or "time" or "timestamp with time zone" or "timestamp without time zone")
        {
            var expectedScale = size.Length == 0 ? column.Precision ?? (pg ? 6 : 7) : int.Parse(size, CultureInfo.InvariantCulture);
            Require(expectedScale == actual.Scale, $"Time precision differs for {table}.{column.Name}.");
        }
    }

    private static void VerifyCollation(IColumn column, ColumnRow actual, bool pg, IModel model, string table)
    {
        var text = actual.TypeName is "nvarchar" or "nchar" or "varchar" or "char" or "ntext" or "text"
            or "bpchar" or "character" or "character varying";
        if (!text)
        {
            Require(actual.Collation is null, $"Unexpected nontext column collation for {table}.{column.Name}.");
            return;
        }

        var expected = column.Collation ?? model.GetCollation();
        if (pg) expected ??= PostgreSqlModelConfiguration.BusinessTextCollation;
        // SQL columns without explicit model/column collation retain their database default.
        if (expected is not null)
            Require(actual.Collation == expected, $"Column collation differs for {table}.{column.Name}.");
    }

    private static string[] ReferencedColumns(string? expression, IEnumerable<string> columnNames, bool pg)
    {
        var known = columnNames.ToHashSet(StringComparer.Ordinal);
        var masked = Regex.Replace(expression ?? "", "'(?:''|[^'])*'", " ");
        var pattern = pg ? "\"((?:\"\"|[^\"])*)\"" : @"\[((?:\]\]|[^\]])*)\]";
        return Regex.Matches(masked, pattern).Select(m => m.Groups[1].Value.Replace(pg ? "\"\"" : "]]", pg ? "\"" : "]", StringComparison.Ordinal))
            .Where(known.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    // The current index filters are conjunctions of comparison, IN and NULL predicates.
    // This grammar fails closed on OR/functions, rather than deleting grouping and claiming arbitrary equivalence.
    private static string NormalizeFilter(string? expression, bool pg, IReadOnlySet<string> boolColumns, IReadOnlySet<string> nonNullColumns)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "";
        var pattern = pg
            ? "\\s+|\"(?:\"\"|[^\"])*\"|'(?:''|[^'])*'|::|<>|!=|>=|<=|[=<>(),\\[\\]]|[-+]?[0-9]+(?:\\.[0-9]+)?|[A-Za-z_][A-Za-z_0-9]*"
            : "\\s+|\\[(?:\\]\\]|[^\\]])*\\]|N?'(?:''|[^'])*'|<>|!=|>=|<=|[=<>(),]|[-+]?[0-9]+(?:\\.[0-9]+)?|[A-Za-z_][A-Za-z_0-9]*";
        var tokens = new List<string>();
        var position = 0;
        foreach (Match match in Regex.Matches(expression, pattern))
        {
            Require(match.Index == position, "Unclassified installed index-filter token.");
            position += match.Length;
            var token = match.Value;
            if (string.IsNullOrWhiteSpace(token)) continue;
            if (token[0] == '[' && !pg) token = "I:" + token[1..^1].Replace("]]", "]", StringComparison.Ordinal);
            else if (token[0] == '"') token = "I:" + token[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            else if (token[0] == '\'' || token.StartsWith("N'", StringComparison.OrdinalIgnoreCase))
                token = "S:" + (token[0] == '\'' ? token[1..^1] : token[2..^1]).Replace("''", "'", StringComparison.Ordinal);
            else token = token.ToUpperInvariant();
            tokens.Add(token);
        }
        Require(position == expression.Length, "Unclassified installed index-filter suffix.");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] != "::") continue;
            Require(i + 1 < tokens.Count && new[] { "SMALLINT", "INTEGER", "BIGINT", "NUMERIC", "BPCHAR", "TEXT", "UUID", "BOOLEAN" }.Contains(tokens[i + 1]),
                "Unclassified installed index-filter cast.");
            tokens.RemoveRange(i, 2);
            if (i + 1 < tokens.Count && tokens[i] == "[" && tokens[i + 1] == "]") tokens.RemoveRange(i, 2);
            i--;
        }
        Require(!tokens.Any(t => t is "OR" or "BETWEEN" or "LIKE"), "Unclassified installed index-filter grammar.");
        tokens.RemoveAll(t => t is "(" or ")");
        var terms = new List<string>();
        foreach (var term in Split(tokens, "AND"))
        {
            Require(term.Count > 1 && term[0].StartsWith("I:", StringComparison.Ordinal), "Unclassified installed index-filter predicate.");
            var name = term[0][2..];
            if (term.Count == 3 && term[1] == "IS" && term[2] == "NULL") terms.Add($"{term[0]} IS NULL");
            else if (term.Count == 4 && term[1] == "IS" && term[2] == "NOT" && term[3] == "NULL")
            {
                // Historical SQL queue migrations retain redundant NOT NULL filters.
                // Drop only a proven tautology: both model and installed column are NOT NULL.
                if (!nonNullColumns.Contains(name)) terms.Add($"{term[0]} IS NOT NULL");
            }
            else if (term.Count == 3 && new[] { "=", "<>", "!=", ">", "<", ">=", "<=" }.Contains(term[1]))
            {
                var value = term[2];
                Require(Literal(value), "Unclassified installed index-filter comparison.");
                if (boolColumns.Contains(name) && value is "0" or "1") value = value == "0" ? "FALSE" : "TRUE";
                terms.Add($"{term[0]} {(term[1] == "!=" ? "<>" : term[1])} {value}");
            }
            else
            {
                var negate = term.Count > 2 && term[1] == "NOT" && term[2] == "IN";
                var start = term[1] == "IN" ? 2 : negate ? 3 :
                    term.Count > 5 && term[1] == "=" && term[2] == "ANY" && term[3] == "ARRAY" && term[4] == "[" ? 5 : -1;
                Require(start >= 0, "Unclassified installed index-filter membership.");
                var values = term.Skip(start).Where(t => t is not "," and not "]").ToArray();
                Require(values.Length != 0 && values.All(Literal), "Unclassified installed index-filter membership values.");
                terms.Add($"{term[0]} {(negate ? "NOT IN" : "IN")} {string.Join(',', values.Order(StringComparer.Ordinal))}");
            }
        }
        return string.Join(" AND ", terms.Order(StringComparer.Ordinal));
        static bool Literal(string token) => token.StartsWith("S:", StringComparison.Ordinal) || token is "TRUE" or "FALSE" or "NULL" ||
            Regex.IsMatch(token, @"\A[-+]?[0-9]+(?:\.[0-9]+)?\z");
    }

    private static IEnumerable<List<string>> Split(List<string> tokens, string separator)
    {
        var current = new List<string>();
        foreach (var token in tokens)
        {
            if (token == separator) { yield return current; current = []; }
            else current.Add(token);
        }
        yield return current;
    }
    private static void EqualColumns(IEnumerable<string> expected, IEnumerable<string> actual, string label) =>
        Require(expected.SequenceEqual(actual, StringComparer.Ordinal), $"Ordered columns differ for {label}.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new CatalogAssertionException(message);
    }

    private const string PgColumns = """
        SELECT n.nspname AS SchemaName,t.relname AS TableName,a.attname AS ColumnName,
          CASE ty.typname WHEN 'bpchar' THEN CASE WHEN a.atttypmod>=4 THEN 'character' ELSE 'bpchar' END
           WHEN 'varchar' THEN 'character varying' WHEN 'int4' THEN 'integer' WHEN 'int8' THEN 'bigint'
           WHEN 'int2' THEN 'smallint' WHEN 'float8' THEN 'double precision' WHEN 'float4' THEN 'real'
           WHEN 'bool' THEN 'boolean' WHEN 'timestamptz' THEN 'timestamp with time zone'
           WHEN 'timestamp' THEN 'timestamp without time zone' ELSE ty.typname END AS TypeName,
          CASE WHEN ty.typname IN('bpchar','varchar') AND a.atttypmod>=4 THEN a.atttypmod-4 ELSE -1 END AS Length,
          CASE WHEN ty.typname='numeric' AND a.atttypmod>=4 THEN ((a.atttypmod-4)>>16)&65535 ELSE 0 END AS Precision,
          CASE WHEN ty.typname='numeric' AND a.atttypmod>=4 THEN (a.atttypmod-4)&65535
               WHEN ty.typname IN('timestamp','timestamptz','time','timetz') THEN CASE WHEN a.atttypmod<0 THEN 6 ELSE a.atttypmod END ELSE 0 END AS Scale,
          NOT a.attnotnull AS IsNullable,co.collname AS Collation
        FROM pg_attribute a JOIN pg_class t ON t.oid=a.attrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_type ty ON ty.oid=a.atttypid LEFT JOIN pg_collation co ON co.oid=a.attcollation
        WHERE a.attnum>0 AND NOT a.attisdropped AND t.relkind IN('r','p')
          AND n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'
        """;
    private const string SqlColumns = """
        SELECT s.name AS SchemaName,t.name AS TableName,c.name AS ColumnName,ty.name AS TypeName,
          CONVERT(int,c.max_length) AS Length,CONVERT(int,c.precision) AS Precision,CONVERT(int,c.scale) AS Scale,
          c.is_nullable AS IsNullable,c.collation_name AS Collation
        FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.types ty ON ty.user_type_id=c.user_type_id
        """;
    private const string PgKeys = """
        SELECT n.nspname AS SchemaName,t.relname AS TableName,c.conname AS ObjectName,a.attname AS ColumnName,
          k.ordinal::integer AS Ordinal,c.contype='p' AS IsPrimary,
          c.conenforced AND i.indisready AND i.indislive AS Enabled,c.convalidated AND i.indisvalid AS Validated,
          c.condeferrable AS Deferrable,false AS IgnoreDuplicateKeys
        FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_index i ON i.indexrelid=c.conindid CROSS JOIN LATERAL unnest(c.conkey) WITH ORDINALITY k(attnum,ordinal)
        JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=k.attnum WHERE c.contype IN('p','u')
        """;
    private const string SqlKeys = """
        SELECT s.name AS SchemaName,t.name AS TableName,k.name AS ObjectName,c.name AS ColumnName,
          CONVERT(int,ic.key_ordinal) AS Ordinal,CONVERT(bit,CASE WHEN k.type='PK' THEN 1 ELSE 0 END) AS IsPrimary,
          CONVERT(bit,CASE WHEN i.is_disabled=0 AND i.is_hypothetical=0 THEN 1 ELSE 0 END) AS Enabled,CONVERT(bit,1) AS Validated,
          CONVERT(bit,0) AS Deferrable,i.ignore_dup_key AS IgnoreDuplicateKeys
        FROM sys.key_constraints k JOIN sys.tables t ON t.object_id=k.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id=k.unique_index_id
        JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        """;
    private const string PgIndexes = """
        SELECT n.nspname AS SchemaName,t.relname AS TableName,ix.relname AS ObjectName,a.attname AS ColumnName,
          k.ordinal::integer AS Ordinal,i.indisunique AS IsUnique,k.ordinal>i.indnkeyatts AS Included,
          CASE WHEN k.ordinal<=i.indnkeyatts THEN ((i.indoption::smallint[])[k.ordinal::integer-1]&1)=1 ELSE false END AS Descending,
          i.indisready AND i.indislive AS Enabled,i.indisvalid AS Validated,i.indnullsnotdistinct AS NullsNotDistinct,
          false AS IgnoreDuplicateKeys,
          pg_get_expr(i.indpred,i.indrelid) AS Definition
        FROM pg_index i JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_class ix ON ix.oid=i.indexrelid CROSS JOIN LATERAL unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ordinal)
        LEFT JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=k.attnum
        """;
    private const string SqlIndexes = """
        SELECT s.name AS SchemaName,t.name AS TableName,i.name AS ObjectName,c.name AS ColumnName,
          CONVERT(int,CASE WHEN ic.is_included_column=0 THEN ic.key_ordinal ELSE i.key_count+ic.index_column_id END) AS Ordinal,
          i.is_unique AS IsUnique,i.type AS TypeKind,ic.is_included_column AS Included,ic.is_descending_key AS Descending,
          CONVERT(bit,CASE WHEN i.is_disabled=0 AND i.is_hypothetical=0 THEN 1 ELSE 0 END) AS Enabled,
          CONVERT(bit,1) AS Validated,CONVERT(bit,0) AS NullsNotDistinct,
          i.ignore_dup_key AS IgnoreDuplicateKeys,i.filter_definition AS Definition
        FROM (SELECT ix.*, (SELECT COUNT(*) FROM sys.index_columns z WHERE z.object_id=ix.object_id AND z.index_id=ix.index_id AND z.key_ordinal>0) AS key_count FROM sys.indexes ix) i
        JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.name IS NOT NULL
        """;
    private const string PgForeignKeys = """
        SELECT n.nspname AS SchemaName,t.relname AS TableName,c.conname AS ObjectName,a.attname AS ColumnName,k.ordinal::integer AS Ordinal,
          pn.nspname AS PrincipalSchema,pt.relname AS PrincipalTable,pa.attname AS PrincipalColumn,
          c.conenforced AND EXISTS(SELECT 1 FROM pg_trigger tr WHERE tr.tgconstraint=c.oid)
            AND NOT EXISTS(SELECT 1 FROM pg_trigger tr WHERE tr.tgconstraint=c.oid AND tr.tgenabled NOT IN('O','A')) AS Enabled,
          c.convalidated AS Validated,c.condeferrable AS Deferrable,
          CASE c.confdeltype WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET_NULL' WHEN 'd' THEN 'SET_DEFAULT' WHEN 'r' THEN 'RESTRICT' ELSE 'NO_ACTION' END AS DeleteAction,
          CASE c.confupdtype WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET_NULL' WHEN 'd' THEN 'SET_DEFAULT' WHEN 'r' THEN 'RESTRICT' ELSE 'NO_ACTION' END AS UpdateAction
        FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_class pt ON pt.oid=c.confrelid JOIN pg_namespace pn ON pn.oid=pt.relnamespace
        CROSS JOIN LATERAL unnest(c.conkey,c.confkey) WITH ORDINALITY k(attnum,principalnum,ordinal)
        JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=k.attnum JOIN pg_attribute pa ON pa.attrelid=pt.oid AND pa.attnum=k.principalnum
        WHERE c.contype='f'
        """;
    private const string SqlForeignKeys = """
        SELECT s.name AS SchemaName,t.name AS TableName,f.name AS ObjectName,c.name AS ColumnName,fc.constraint_column_id AS Ordinal,
          ps.name AS PrincipalSchema,pt.name AS PrincipalTable,pc.name AS PrincipalColumn,
          CONVERT(bit,CASE WHEN f.is_disabled=0 THEN 1 ELSE 0 END) AS Enabled,CONVERT(bit,CASE WHEN f.is_not_trusted=0 THEN 1 ELSE 0 END) AS Validated,
          CONVERT(bit,0) AS Deferrable,f.delete_referential_action_desc AS DeleteAction,f.update_referential_action_desc AS UpdateAction
        FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id=f.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.tables pt ON pt.object_id=f.referenced_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
        JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
        JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=fc.parent_column_id
        JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fc.referenced_column_id
        """;
    private const string PgChecks = """
        SELECT n.nspname AS SchemaName,t.relname AS TableName,c.conname AS ObjectName,
          c.conenforced AS Enabled,c.convalidated AS Validated,pg_get_expr(c.conbin,c.conrelid) AS Definition
        FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE c.contype='c'
        """;
    private const string SqlChecks = """
        SELECT s.name AS SchemaName,t.name AS TableName,c.name AS ObjectName,
          CONVERT(bit,CASE WHEN c.is_disabled=0 THEN 1 ELSE 0 END) AS Enabled,
          CONVERT(bit,CASE WHEN c.is_not_trusted=0 THEN 1 ELSE 0 END) AS Validated,c.definition AS Definition
        FROM sys.check_constraints c JOIN sys.tables t ON t.object_id=c.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        """;

    private class CatalogRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int Ordinal { get; set; }
        public bool Enabled { get; set; }
        public bool Validated { get; set; }
    }
    private sealed class ColumnRow : CatalogRow
    {
        public string TypeName { get; set; } = "";
        public int Length { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
        public bool IsNullable { get; set; }
        public string? Collation { get; set; }
    }
    private sealed class KeyRow : CatalogRow
    {
        public bool IsPrimary { get; set; }
        public bool Deferrable { get; set; }
        public bool IgnoreDuplicateKeys { get; set; }
    }
    private sealed class IndexRow : CatalogRow
    {
        public int TypeKind { get; set; }
        public bool IsUnique { get; set; }
        public bool Included { get; set; }
        public bool Descending { get; set; }
        public bool NullsNotDistinct { get; set; }
        public bool IgnoreDuplicateKeys { get; set; }
        public string? Definition { get; set; }
    }
    private sealed class ForeignRow : CatalogRow
    {
        public string PrincipalSchema { get; set; } = "";
        public string PrincipalTable { get; set; } = "";
        public string PrincipalColumn { get; set; } = "";
        public string DeleteAction { get; set; } = "";
        public string UpdateAction { get; set; } = "";
        public bool Deferrable { get; set; }
    }
    private sealed class CheckRow : CatalogRow { public string? Definition { get; set; } }
}

// Contains only verifier-owned object names and fixed assertion text.
internal sealed class CatalogAssertionException(string message) : InvalidOperationException(message);
