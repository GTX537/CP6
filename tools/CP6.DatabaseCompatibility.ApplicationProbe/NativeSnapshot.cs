using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal static partial class AppProbe
{
    private static async Task<SnapshotState> ReadSnapshotAsync(OwnedTestDatabase owned, SafeReport report, CancellationToken ct)
    {
        await using var connection = new DatabaseConnectionFactory(owned.Database).Create(owned.ConnectionString);
        await connection.OpenAsync(ct);
        // Root stops all API workers before capture and before restored-state verification.
        // This transaction still makes every table/history read one consistent database view.
        await using var transaction = await connection.BeginTransactionAsync(
            owned.Database.Provider == DatabaseProvider.PostgreSql ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, ct);
        var histories = await ReadHistoriesAsync(owned, connection, transaction, ct);
        report.HistoryProfiles = histories;
        report.Counts["EffectiveProfiles"] = histories.Count;
        report.Assertions.Add(new("four-effective-history-profiles-complete", true));
        var tables = await ReadTableCatalogAsync(owned, connection, transaction, ct);
        Require(tables.Count > 0, "native-table-catalog-nonempty");
        var captured = new List<TableState>();
        foreach (var table in tables)
        {
            var (rows, content) = await ReadRowsAsync(owned, connection, transaction, table, ct);
            captured.Add(table with { Rows = rows, ContentSha256 = content });
        }
        var sequences = await ReadSequencesAsync(owned, connection, transaction, ct);
        await transaction.CommitAsync(ct);
        return new(1, owned.Database.Provider.ToString(), owned.DatabaseName, owned.Role.ToString(),
            owned.Database.Provider == DatabaseProvider.PostgreSql ? "pg-type-send-v1" : "sql-convert-varbinary-v1",
            DateTimeOffset.UtcNow, histories, captured, sequences);
    }

    private static async Task<List<TableState>> ReadTableCatalogAsync(OwnedTestDatabase owned,
        DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        var sql = owned.Database.Provider == DatabaseProvider.PostgreSql ? """
            SELECT ns.nspname, t.relname, a.attnum::integer, a.attname,
                pg_catalog.format_type(a.atttypid,a.atttypmod), a.atttypmod::integer,
                0::integer, 0::integer, NOT a.attnotnull,
                CASE WHEN a.attcollation=0 THEN NULL ELSE cns.nspname || '.' || col.collname END,
                pg_catalog.pg_get_expr(ad.adbin,ad.adrelid), a.attgenerated::text,
                a.attidentity::text, pns.nspname, p.proname
            FROM pg_catalog.pg_class t
            JOIN pg_catalog.pg_namespace ns ON ns.oid=t.relnamespace
            JOIN pg_catalog.pg_attribute a ON a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped
            JOIN pg_catalog.pg_type typ ON typ.oid=a.atttypid
            LEFT JOIN pg_catalog.pg_attrdef ad ON ad.adrelid=t.oid AND ad.adnum=a.attnum
            LEFT JOIN pg_catalog.pg_collation col ON col.oid=a.attcollation
            LEFT JOIN pg_catalog.pg_namespace cns ON cns.oid=col.collnamespace
            LEFT JOIN pg_catalog.pg_proc p ON p.oid=typ.typsend
            LEFT JOIN pg_catalog.pg_namespace pns ON pns.oid=p.pronamespace
            WHERE t.relkind IN ('r','p') AND ns.nspname NOT IN ('pg_catalog','information_schema')
                AND ns.nspname NOT LIKE 'pg_toast%' AND ns.nspname NOT LIKE 'pg_temp_%'
            ORDER BY ns.nspname COLLATE "C", t.relname COLLATE "C", a.attnum
            """ : """
            SELECT s.name, t.name, c.column_id, c.name,
                ts.name+'.'+ty.name, CONVERT(int,c.max_length), CONVERT(int,c.precision), CONVERT(int,c.scale),
                c.is_nullable, c.collation_name, dc.definition,
                CASE WHEN cc.object_id IS NULL THEN CONVERT(nvarchar(max),c.generated_always_type)
                    ELSE cc.definition+N';persisted='+CONVERT(nvarchar(1),cc.is_persisted) END,
                CASE WHEN ic.object_id IS NULL THEN NULL ELSE
                    CONVERT(nvarchar(100),ic.seed_value)+N':'+CONVERT(nvarchar(100),ic.increment_value) END,
                CAST(NULL AS nvarchar(128)), CAST(NULL AS nvarchar(128))
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id
            JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            JOIN sys.schemas ts ON ts.schema_id=ty.schema_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
            WHERE t.is_ms_shipped=0
            ORDER BY s.name COLLATE Latin1_General_100_BIN2, t.name COLLATE Latin1_General_100_BIN2, c.column_id
            """;
        var rows = new List<(string Schema, string Table, ColumnState Column)>();
        await using (var command = Command(connection, transaction, sql))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var column = new ColumnState(reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetBoolean(8),
                    Text(reader, 9), Text(reader, 10), Text(reader, 11), Text(reader, 12), Text(reader, 13), Text(reader, 14));
                if (owned.Database.Provider == DatabaseProvider.PostgreSql) Require(column.SendSchema is not null && column.SendFunction is not null,
                    "native-column-binary-send-supported");
                rows.Add((reader.GetString(0), reader.GetString(1), column));
            }
        var result = rows.GroupBy(x => (x.Schema, x.Table))
            .OrderBy(x => x.Key.Schema, StringComparer.Ordinal).ThenBy(x => x.Key.Table, StringComparer.Ordinal)
            .Select(group =>
            {
                var columns = group.Select(x => x.Column).OrderBy(x => x.Ordinal).ToList();
                return new TableState(group.Key.Schema, group.Key.Table, columns, ObjectHash(columns), 0, "");
            }).ToList();
        var countSql = owned.Database.Provider == DatabaseProvider.PostgreSql ? """
            SELECT count(*) FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace ns ON ns.oid=t.relnamespace
            WHERE t.relkind IN ('r','p') AND ns.nspname NOT IN ('pg_catalog','information_schema')
                AND ns.nspname NOT LIKE 'pg_toast%' AND ns.nspname NOT LIKE 'pg_temp_%'
            """ : "SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped=0";
        await using var count = Command(connection, transaction, countSql);
        Require(Convert.ToInt64(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == result.Count,
            "native-all-user-base-table-catalog-complete");
        return result;
    }

    private static async Task<(long Rows, string Sha256)> ReadRowsAsync(OwnedTestDatabase owned,
        DbConnection connection, DbTransaction transaction, TableState table, CancellationToken ct)
    {
        var pg = owned.Database.Provider == DatabaseProvider.PostgreSql;
        var columns = table.Columns.Select(c => pg
            ? $"{Identifier(c.SendSchema!, true)}.{Identifier(c.SendFunction!, true)}({Identifier(c.Name, true)})"
            : $"CONVERT(varbinary(max),{Identifier(c.Name, false)})");
        // SQL identifiers originate only from this owned database's catalog, and are quoted.
        // ONLY avoids counting PostgreSQL partition/inheritance rows twice: each real leaf is also catalogued.
        var sql = $"SELECT {string.Join(",", columns)} FROM {(pg ? "ONLY " : "")}" +
            $"{Identifier(table.Schema, pg)}.{Identifier(table.Table, pg)}";
        var rowHashes = new List<string>();
        await using var command = Command(connection, transaction, sql);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var i in Enumerable.Range(0, table.Columns.Count))
            {
                if (await reader.IsDBNullAsync(i, ct)) hash.AppendData(new byte[] { 0 });
                else
                {
                    // Cells are native binary bytes, including trailing spaces and generated row versions.
                    var bytes = await reader.GetFieldValueAsync<byte[]>(i, ct);
                    var frame = new byte[9];
                    frame[0] = 1;
                    BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(1), bytes.LongLength);
                    hash.AppendData(frame);
                    hash.AppendData(bytes);
                }
            }
            rowHashes.Add(Convert.ToHexString(hash.GetHashAndReset()));
        }
        rowHashes.Sort(StringComparer.Ordinal);
        using var tableHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rowHashes) tableHash.AppendData(Convert.FromHexString(row));
        return (rowHashes.Count, Convert.ToHexString(tableHash.GetHashAndReset()));
    }

    private static async Task<List<SequenceState>> ReadSequencesAsync(OwnedTestDatabase owned,
        DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        var states = new List<SequenceState>();
        if (owned.Database.Provider == DatabaseProvider.PostgreSql)
        {
            var catalog = new List<(string Schema, string Name, string Definition)>();
            await using (var command = Command(connection, transaction, """
                SELECT ns.nspname,c.relname,pg_catalog.format_type(s.seqtypid,-1),
                    s.seqstart::text,s.seqincrement::text,s.seqmax::text,s.seqmin::text,
                    s.seqcache::text,s.seqcycle
                FROM pg_catalog.pg_sequence s
                JOIN pg_catalog.pg_class c ON c.oid=s.seqrelid
                JOIN pg_catalog.pg_namespace ns ON ns.oid=c.relnamespace
                WHERE ns.nspname NOT IN ('pg_catalog','information_schema')
                    AND ns.nspname NOT LIKE 'pg_toast%' AND ns.nspname NOT LIKE 'pg_temp_%'
                ORDER BY ns.nspname COLLATE "C", c.relname COLLATE "C"
                """))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    var definition = string.Join(":", Enumerable.Range(2, 7).Select(i =>
                        Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)));
                    catalog.Add((reader.GetString(0), reader.GetString(1), definition));
                }
            foreach (var sequence in catalog)
            {
                await using var command = Command(connection, transaction,
                    $"SELECT last_value::text,is_called FROM {Identifier(sequence.Schema, true)}.{Identifier(sequence.Name, true)}");
                await using var reader = await command.ExecuteReaderAsync(ct);
                Require(await reader.ReadAsync(ct), "sequence-state-readable");
                states.Add(new("Sequence", sequence.Schema, sequence.Name, null, sequence.Definition,
                    reader.GetString(0), reader.GetBoolean(1)));
            }
        }
        else
        {
            await using (var command = Command(connection, transaction, """
                SELECT s.name,t.name,c.name,CONVERT(nvarchar(100),c.seed_value),
                    CONVERT(nvarchar(100),c.increment_value),CONVERT(nvarchar(100),c.last_value)
                FROM sys.identity_columns c JOIN sys.tables t ON t.object_id=c.object_id
                JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0
                ORDER BY s.name COLLATE Latin1_General_100_BIN2,t.name COLLATE Latin1_General_100_BIN2,c.column_id
                """))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    states.Add(new("Identity", reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3) + ":" + reader.GetString(4), Text(reader, 5), null));
            await using (var command = Command(connection, transaction, """
                SELECT s.name,q.name,ty.name,CONVERT(nvarchar(100),q.start_value),CONVERT(nvarchar(100),q.increment),
                    CONVERT(nvarchar(100),q.minimum_value),CONVERT(nvarchar(100),q.maximum_value),q.is_cycling,q.is_cached,
                    CONVERT(nvarchar(100),q.cache_size),CONVERT(nvarchar(100),q.current_value)
                FROM sys.sequences q JOIN sys.schemas s ON s.schema_id=q.schema_id
                JOIN sys.types ty ON ty.user_type_id=q.user_type_id
                ORDER BY s.name COLLATE Latin1_General_100_BIN2,q.name COLLATE Latin1_General_100_BIN2
                """))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    var definition = string.Join(":", Enumerable.Range(2, 8).Select(i =>
                        reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)));
                    states.Add(new("Sequence", reader.GetString(0), reader.GetString(1), null, definition, Text(reader, 10), null));
                }
        }
        // Sequence counters are nontransactional in PostgreSQL. The runner must quiesce all writers.
        return states.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Schema, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Column, StringComparer.Ordinal).ToList();
    }

    private static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 180;
        return command;
    }

    private static string? Text(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string Identifier(string text, bool pg) => pg
        ? '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
        : '[' + text.Replace("]", "]]", StringComparison.Ordinal) + ']';
}
