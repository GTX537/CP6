using System.Globalization;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

// One local, owned snapshot -> one newly created PostgreSQL test database.
// Credentials are read from a private settings file, never command-line arguments.
if (args.Length == 1 && args[0] == "--self-test")
{
    PolicyTests.Run();
    return;
}
if (args.Length != 2) throw new InvalidOperationException("Expected mode and private settings path.");
var cfg = JsonSerializer.Deserialize<Settings>(File.ReadAllText(args[1]))!;
Guard.Validate(cfg);
var run = new Transfer(cfg);
try
{
    switch (args[0])
    {
        case "--create-pg": await run.CreatePg(); break;
        case "--inventory": await run.Inventory(); break;
        case "--inspect-pg": await run.InspectPg(); break;
        case "--reset-tokens": await run.ResetTokens(); break;
        case "--copy": await run.Copy(); break;
        default: throw new InvalidOperationException("Unknown mode.");
    }
}
catch (Exception ex)
{
    // Exception detail can contain a data value. Keep full text in the ignored private directory.
    File.WriteAllText(Path.Combine(cfg.RunDirectory, "private", "transfer-failure-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".log"), ex.ToString());
    Console.Error.WriteLine("Transfer failed: " + ex.GetType().Name + "; private diagnostic retained.");
    Environment.ExitCode = 1;
}

record Settings(string Task, string RunId, string RunDirectory, string SqlSnapshotDatabase,
    string SqlSnapshotConnection, string PostgreSqlConnection, string PgDatabase);
record Column(string Name, string Type, bool Nullable, bool Identity, string? Default = null);
record Table(string Schema, string Name, List<Column> Columns);
record Mapping(string SourceSchema, string SourceTable, string TargetSchema, string TargetTable,
    List<Column> Columns, bool Archive);
record Fk(string Schema, string Table, string Name, bool Deferrable, bool Deferred);
static class Guard
{
    public static void Validate(Settings c)
    {
        if (c.Task != "CP6DB-PG-LOCAL-COPY" || !System.Text.RegularExpressions.Regex.IsMatch(c.RunId, "^[a-f0-9]{8}$"))
            throw new InvalidOperationException("Task identity missing.");
        var sql = new SqlConnectionStringBuilder(c.SqlSnapshotConnection);
        var pg = new NpgsqlConnectionStringBuilder(c.PostgreSqlConnection);
        if (!System.Text.RegularExpressions.Regex.IsMatch(c.SqlSnapshotDatabase, "^CP6MIG_SNAPSHOT_[0-9]{8}_" + c.RunId + "$") || sql.InitialCatalog != c.SqlSnapshotDatabase ||
            sql.DataSource.Split('\\')[0] is not ("localhost" or "127.0.0.1" or "::1") || !sql.IntegratedSecurity ||
            !System.Text.RegularExpressions.Regex.IsMatch(c.PgDatabase, "^cp6db_pg_test_[0-9]{8}_" + c.RunId + "$") || pg.Database != c.PgDatabase ||
            pg.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Only exact owned local snapshot and test database are allowed.");
    }
}
sealed class Transfer(Settings cfg)
{
    static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    static string S(string s) => "[" + s.Replace("]", "]]") + "]";
    static string P(string schema, string table) => Q(schema) + "." + Q(table);
    string Marker => "CP6DB-PG-LOCAL-COPY:" + cfg.RunId;
    async Task VerifyPg(NpgsqlConnection pg)
    {
        var actual = await new NpgsqlCommand("SELECT current_database() || ':' || COALESCE(shobj_description(oid,'pg_database'),'') FROM pg_database WHERE datname=current_database()", pg).ExecuteScalarAsync();
        if ((string?)actual != cfg.PgDatabase + ":" + Marker) throw new InvalidOperationException("PostgreSQL ownership marker mismatch.");
    }
    public async Task CreatePg()
    {
        var b = new NpgsqlConnectionStringBuilder(cfg.PostgreSqlConnection) { Database = "postgres" };
        await using var pg = new NpgsqlConnection(b.ConnectionString); await pg.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname=@n", pg);
        exists.Parameters.AddWithValue("n", cfg.PgDatabase);
        if (Convert.ToInt64(await exists.ExecuteScalarAsync()) != 0) throw new InvalidOperationException("Existing target cannot be overwritten.");
        await new NpgsqlCommand("CREATE DATABASE " + Q(cfg.PgDatabase), pg).ExecuteNonQueryAsync();
        await new NpgsqlCommand("COMMENT ON DATABASE " + Q(cfg.PgDatabase) + " IS '" + Marker + "'", pg).ExecuteNonQueryAsync();
        Console.WriteLine("Created new PostgreSQL test database: " + cfg.PgDatabase);
    }
    async Task<List<Table>> SqlTables(SqlConnection sql)
    {
        const string query = "SELECT sc.name,t.name,c.name,ty.name,c.is_nullable,c.is_identity FROM sys.tables t JOIN sys.schemas sc ON sc.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE t.is_ms_shipped=0 ORDER BY sc.name,t.name,c.column_id";
        await using var cmd = new SqlCommand(query, sql); await using var r = await cmd.ExecuteReaderAsync();
        var tables = new List<Table>();
        while (await r.ReadAsync())
        {
            if (tables.Count == 0 || tables[^1].Schema != r.GetString(0) || tables[^1].Name != r.GetString(1))
                tables.Add(new(r.GetString(0), r.GetString(1), []));
            tables[^1].Columns.Add(new(r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5)));
        }
        return tables;
    }
    async Task<List<Table>> PgTables(NpgsqlConnection pg)
    {
        const string query = "SELECT n.nspname,c.relname,a.attname,t.typname,NOT a.attnotnull,a.attidentity <> '',pg_get_expr(d.adbin,d.adrelid) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_attribute a ON a.attrelid=c.oid JOIN pg_type t ON t.oid=a.atttypid LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum WHERE c.relkind='r' AND n.nspname NOT IN('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND a.attnum>0 AND NOT a.attisdropped ORDER BY n.nspname,c.relname,a.attnum";
        await using var cmd = new NpgsqlCommand(query, pg); await using var r = await cmd.ExecuteReaderAsync();
        var tables = new List<Table>();
        while (await r.ReadAsync())
        {
            if (tables.Count == 0 || tables[^1].Schema != r.GetString(0) || tables[^1].Name != r.GetString(1))
                tables.Add(new(r.GetString(0), r.GetString(1), []));
            tables[^1].Columns.Add(new(r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5), r.IsDBNull(6) ? null : r.GetString(6)));
        }
        return tables;
    }
    static string ArchiveType(string sqlType) => sqlType switch
    {
        "nvarchar" or "varchar" or "char" or "nchar" or "text" or "ntext" or "xml" => "text",
        "timestamp" or "binary" or "varbinary" or "image" => "bytea",
        "int" => "int4", "bigint" => "int8", "smallint" or "tinyint" => "int2",
        "bit" => "bool", "decimal" or "numeric" or "money" or "smallmoney" => "numeric",
        "float" => "float8", "real" => "float4", "uniqueidentifier" => "uuid",
        // SQL datetime2 retains 100ns and datetimeoffset retains its original offset in the archive.
        "datetime2" or "datetime" or "smalldatetime" or "datetimeoffset" => "text", "date" => "date", "time" => "time",
        _ => throw new InvalidOperationException("Unsupported source type: " + sqlType)
    };
    static NpgsqlDbType PgType(string type) => type switch
    {
        "text" => NpgsqlDbType.Text, "varchar" => NpgsqlDbType.Varchar, "bpchar" => NpgsqlDbType.Char,
        "bytea" => NpgsqlDbType.Bytea, "int4" => NpgsqlDbType.Integer, "int8" => NpgsqlDbType.Bigint,
        "int2" => NpgsqlDbType.Smallint, "bool" => NpgsqlDbType.Boolean, "numeric" => NpgsqlDbType.Numeric,
        "float8" => NpgsqlDbType.Double, "float4" => NpgsqlDbType.Real, "uuid" => NpgsqlDbType.Uuid,
        "timestamp" => NpgsqlDbType.Timestamp, "timestamptz" => NpgsqlDbType.TimestampTz,
        "date" => NpgsqlDbType.Date, "time" => NpgsqlDbType.Time,
        "jsonb" => NpgsqlDbType.Jsonb, "json" => NpgsqlDbType.Json,
        _ => throw new InvalidOperationException("Unsupported target type: " + type)
    };
    public static object Normalize(object value, string type)
    {
        if (value is DBNull) return value;
        return type switch
        {
            "text" when value is DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            "text" when value is DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            "timestamptz" when value is DateTime dt => new DateTime(dt.Ticks - dt.Ticks % 10, DateTimeKind.Utc),
            "timestamptz" when value is DateTimeOffset dto => new DateTime(dto.UtcTicks - dto.UtcTicks % 10, DateTimeKind.Utc),
            "timestamp" when value is DateTime dt => new DateTime(dt.Ticks - dt.Ticks % 10, DateTimeKind.Unspecified),
            "int2" => Convert.ToInt16(value), "int4" => Convert.ToInt32(value), "int8" => Convert.ToInt64(value),
            "numeric" => Convert.ToDecimal(value), "float8" => Convert.ToDouble(value), "float4" => Convert.ToSingle(value),
            _ => value
        };
    }
    public static string HashRow(object[] values)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms, Encoding.UTF8, true);
        foreach (var v in values)
        {
            switch (v)
            {
                case DBNull: w.Write((byte)0); break;
                case byte[] b: w.Write((byte)1); w.Write(b.Length); w.Write(b); break;
                case DateTime dt: w.Write((byte)2); w.Write(dt.Ticks); break;
                case DateTimeOffset dto: w.Write((byte)2); w.Write(dto.UtcTicks); break;
                case DateOnly d: w.Write((byte)3); w.Write(d.DayNumber); break;
                case decimal d: w.Write((byte)4); w.Write(d.ToString("G29", CultureInfo.InvariantCulture)); break;
                case string s: w.Write((byte)5); w.Write(s); break;
                case Guid g: w.Write((byte)6); w.Write(g.ToByteArray()); break;
                case bool b: w.Write((byte)7); w.Write(b); break;
                default: w.Write((byte)8); w.Write(Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""); break;
            }
        }
        w.Flush(); return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }
    static string HashSet(List<string> hashes)
    {
        hashes.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join("\n", hashes))));
    }
    void Save(string name, object value) => File.WriteAllText(Path.Combine(cfg.RunDirectory, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    public async Task Inventory()
    {
        await using var sql = new SqlConnection(cfg.SqlSnapshotConnection); await sql.OpenAsync();
        var tables = await SqlTables(sql); var counts = new List<object>();
        foreach (var t in tables)
        {
            await using var cmd = new SqlCommand("SELECT COUNT_BIG(*) FROM " + S(t.Schema) + "." + S(t.Name), sql);
            counts.Add(new { t.Schema, t.Name, Rows = Convert.ToInt64(await cmd.ExecuteScalarAsync()) });
        }
        Save("sql-inventory-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json", new { Tables = tables, Counts = counts });
        Console.WriteLine("SQL snapshot inventory: " + tables.Count + " tables.");
    }
    public async Task Copy()
    {
        if (File.Exists(Path.Combine(cfg.RunDirectory, "transfer-result.json"))) throw new InvalidOperationException("A completed migration cannot be overwritten.");
        var initialized = new[] { "pg-target-initialize.json", "pg-target-initialize-retry.json" }
            .Where(n => File.Exists(Path.Combine(cfg.RunDirectory, n))).Any(n =>
                JsonDocument.Parse(File.ReadAllText(Path.Combine(cfg.RunDirectory, n))).RootElement.GetProperty("Success").GetBoolean());
        if (!initialized) throw new InvalidOperationException("Actual PostgreSQL initializer must complete successfully before any import.");
        await using var sql = new SqlConnection(cfg.SqlSnapshotConnection); await sql.OpenAsync();
        var copyConnection = new NpgsqlConnectionStringBuilder(cfg.PostgreSqlConnection) { CommandTimeout = 600 };
        await using var pg = new NpgsqlConnection(copyConnection.ConnectionString); await pg.OpenAsync(); await VerifyPg(pg);
        var source = await SqlTables(sql); var target = await PgTables(pg); var maps = new List<Mapping>();
        await using var tx = await pg.BeginTransactionAsync();
        var fks = new List<Fk>();
        await using (var cmd = new NpgsqlCommand("SELECT n.nspname,c.relname,k.conname,k.condeferrable,k.condeferred FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE k.contype='f'", pg))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) fks.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4)));
        async Task Exec(string query) { await using var c = new NpgsqlCommand(query, pg) { CommandTimeout = 180 }; await c.ExecuteNonQueryAsync(); }
        foreach (var f in fks) await Exec("ALTER TABLE " + P(f.Schema, f.Table) + " ALTER CONSTRAINT " + Q(f.Name) + " DEFERRABLE INITIALLY DEFERRED");
        foreach (var t in target) await Exec("ALTER TABLE " + P(t.Schema, t.Name) + " DISABLE TRIGGER USER");
        foreach (var s in source)
        {
            var schema = s.Schema == "dbo" ? "public" : s.Schema;
            var match = s.Name.StartsWith("__EFMigrationsHistory", StringComparison.Ordinal) ? null : target.SingleOrDefault(t => t.Schema == schema && t.Name == s.Name);
            if (match is not null)
            {
                var columns = match.Columns.Where(c => s.Columns.Any(sc => sc.Name == c.Name)).ToList();
                var missingRequired = match.Columns.Where(c => !columns.Contains(c) && !c.Nullable && c.Default is null && !c.Identity).ToList();
                if (missingRequired.Count != 0) throw new InvalidOperationException("Missing required target columns for " + s.Name + ": " + string.Join(",", missingRequired.Select(c => c.Name)));
                maps.Add(new(s.Schema, s.Name, schema, s.Name, columns, false));
                // Every source table also has a lossless historical archive, including all original columns.
            }
            var archiveSchema = "legacy_sql_" + s.Schema;
            await Exec("CREATE SCHEMA IF NOT EXISTS " + Q(archiveSchema));
            var archiveColumns = s.Columns.Select(c => c with { Type = ArchiveType(c.Type), Identity = false }).ToList();
            await Exec("CREATE TABLE " + P(archiveSchema, s.Name) + " (" + string.Join(",", archiveColumns.Select(c => Q(c.Name) + " " + c.Type + (c.Nullable ? "" : " NOT NULL"))) + ")");
            maps.Add(new(s.Schema, s.Name, archiveSchema, s.Name, archiveColumns, true));
        }
        Save("column-mapping.json", maps);
        var liveTargets = target.Where(t => !t.Name.StartsWith("__EFMigrationsHistory", StringComparison.Ordinal)).Select(t => P(t.Schema, t.Name)).ToList();
        if (liveTargets.Count != 0) await Exec("TRUNCATE " + string.Join(",", liveTargets) + " RESTART IDENTITY");
        // Reject retries into populated archives; CREATE TABLE above already rejects existing archive objects.
        var results = new List<object>(); long total = 0;
        foreach (var m in maps)
        {
            var columns = string.Join(",", m.Columns.Select(c => S(c.Name)));
            await using var sc = new SqlCommand("SELECT " + columns + " FROM " + S(m.SourceSchema) + "." + S(m.SourceTable), sql) { CommandTimeout = 180 };
            await using var reader = await sc.ExecuteReaderAsync();
            var sourceHashes = new List<string>();
            await using (var copy = await pg.BeginBinaryImportAsync("COPY " + P(m.TargetSchema, m.TargetTable) + " (" + string.Join(",", m.Columns.Select(c => Q(c.Name))) + ") FROM STDIN (FORMAT BINARY)"))
            {
                copy.Timeout = TimeSpan.FromMinutes(10);
                while (await reader.ReadAsync())
                {
                    await copy.StartRowAsync(); var values = new object[m.Columns.Count];
                    for (var i = 0; i < values.Length; i++)
                    {
                        var v = Normalize(reader.GetValue(i), m.Columns[i].Type); values[i] = v;
                        if (v is DBNull) await copy.WriteNullAsync(); else await copy.WriteAsync(v, PgType(m.Columns[i].Type));
                    }
                    sourceHashes.Add(HashRow(values));
                }
                await copy.CompleteAsync();
            }
            await reader.DisposeAsync();
            var expressions = m.Columns.Select(c => c.Type == "bpchar" ? "convert_from(pg_catalog.bpcharsend(" + Q(c.Name) + "),'UTF8')" : Q(c.Name));
            await using var pc = new NpgsqlCommand("SELECT " + string.Join(",", expressions) + " FROM " + P(m.TargetSchema, m.TargetTable), pg) { CommandTimeout = 180 };
            await using var pr = await pc.ExecuteReaderAsync(); var targetHashes = new List<string>();
            while (await pr.ReadAsync())
            {
                var values = new object[m.Columns.Count];
                for (var i = 0; i < values.Length; i++) values[i] = Normalize(pr.GetValue(i), m.Columns[i].Type);
                targetHashes.Add(HashRow(values));
            }
            var sh = HashSet(sourceHashes); var th = HashSet(targetHashes);
            if (sourceHashes.Count != targetHashes.Count || sh != th) throw new InvalidOperationException("Exact mapped row multiset mismatch: " + m.SourceTable);
            results.Add(new { m.SourceSchema, m.SourceTable, m.TargetSchema, m.TargetTable, m.Archive, Rows = sourceHashes.Count, Columns = m.Columns.Count, SourceSha256 = sh, TargetSha256 = th, Passed = true });
            if (!m.Archive) total += sourceHashes.Count;
            Console.WriteLine("Verified " + P(m.TargetSchema, m.TargetTable) + ": " + sourceHashes.Count + " rows.");
        }
        await Exec("SET CONSTRAINTS ALL IMMEDIATE");
        foreach (var f in fks) await Exec("ALTER TABLE " + P(f.Schema, f.Table) + " ALTER CONSTRAINT " + Q(f.Name) + (f.Deferrable ? " DEFERRABLE INITIALLY " + (f.Deferred ? "DEFERRED" : "IMMEDIATE") : " NOT DEFERRABLE"));
        foreach (var t in target) await Exec("ALTER TABLE " + P(t.Schema, t.Name) + " ENABLE TRIGGER USER");
        var sequences = new List<object>();
        foreach (var t in target)
        foreach (var c in t.Columns.Where(c => c.Identity || (c.Default?.StartsWith("nextval(", StringComparison.Ordinal) ?? false)))
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_get_serial_sequence(@t,@c)", pg);
            cmd.Parameters.AddWithValue("t", P(t.Schema, t.Name)); cmd.Parameters.AddWithValue("c", c.Name);
            var sequence = (string?)await cmd.ExecuteScalarAsync(); if (sequence is null) continue;
            await using var maxCmd = new NpgsqlCommand("SELECT max(" + Q(c.Name) + ") FROM " + P(t.Schema, t.Name), pg);
            var maximum = await maxCmd.ExecuteScalarAsync(); var hasRows = maximum is not null && maximum is not DBNull;
            await using var setCmd = new NpgsqlCommand("SELECT setval(@s::regclass,@v,@called)", pg);
            setCmd.Parameters.AddWithValue("s", sequence); setCmd.Parameters.AddWithValue("v", hasRows ? Convert.ToInt64(maximum) : 1L); setCmd.Parameters.AddWithValue("called", hasRows);
            await setCmd.ExecuteScalarAsync(); sequences.Add(new { t.Schema, Table = t.Name, Column = c.Name, Sequence = sequence, Value = hasRows ? Convert.ToInt64(maximum) : 1L, Called = hasRows });
        }
        var tokenSequences = await SyncTokenSequences(pg);
        await tx.CommitAsync();
        Save("transfer-result.json", new { Status = "Passed", cfg.PgDatabase, SourceTables = source.Count, MappedTables = maps.Count(m => !m.Archive), ArchiveTables = maps.Count(m => m.Archive), OperationalRows = total, ForeignKeysChecked = fks.Count, SequencesReset = sequences, TokenSequencesReset = tokenSequences, Tables = results, SourceDatabaseWritten = false, TimestampPolicy = "Operational PostgreSQL timestamps have 1us precision; full original SQL datetime2 text and datetimeoffset with original offset are preserved in every legacy_sql_* table. SQL datetime2 mapped to timestamptz follows CP6 UTC storage convention.", CompletedUtc = DateTime.UtcNow });
        Console.WriteLine("Copy committed and all mapped rows verified.");
    }
    public async Task InspectPg()
    {
        await using var pg = new NpgsqlConnection(cfg.PostgreSqlConnection); await pg.OpenAsync(); await VerifyPg(pg);
        var tables = await PgTables(pg);
        Console.WriteLine(JsonSerializer.Serialize(new { Database = cfg.PgDatabase, Tables = tables.Count, Archives = tables.Count(t => t.Schema.StartsWith("legacy_sql_")), HistoryTables = tables.Where(t => t.Name.StartsWith("__EFMigrationsHistory")).Select(t => new { t.Schema, t.Name }), DisabledTriggers = await new NpgsqlCommand("SELECT count(*) FROM pg_trigger WHERE NOT tgisinternal AND tgenabled='D'", pg).ExecuteScalarAsync() }));
    }
    async Task<List<object>> SyncTokenSequences(NpgsqlConnection pg)
    {
        var groups = new Dictionary<string, List<(string Schema, string Table)>>();
        await using (var c = new NpgsqlCommand("SELECT pn.nspname,n.nspname,c.relname FROM pg_trigger g JOIN pg_proc p ON p.oid=g.tgfoid JOIN pg_namespace pn ON pn.oid=p.pronamespace JOIN pg_class c ON c.oid=g.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE NOT g.tgisinternal AND g.tgname='cp6_rowversion_v1' AND p.proname='set_rowversion_v1'", pg))
        await using (var r = await c.ExecuteReaderAsync())
            while (await r.ReadAsync()) { var ns = r.GetString(0); if (!groups.ContainsKey(ns)) groups[ns] = []; groups[ns].Add((r.GetString(1), r.GetString(2))); }
        if (groups.Count != 4) throw new InvalidOperationException("Four current provider token namespaces required.");
        var results = new List<object>();
        foreach (var (schema, tables) in groups)
        {
            long maximum = 0; long tokens = 0;
            foreach (var table in tables)
            {
                await using var c = new NpgsqlCommand("SELECT \"RowVersion\" FROM " + P(table.Schema, table.Table), pg);
                await using var r = await c.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var bytes = (byte[])r.GetValue(0);
                    if (bytes.Length != 8) throw new InvalidOperationException("Invalid imported token length.");
                    var value = BinaryPrimitives.ReadInt64BigEndian(bytes);
                    if (value < 0) throw new InvalidOperationException("Imported unsigned SQL token exceeds PostgreSQL signed sequence range.");
                    maximum = Math.Max(maximum, value); tokens++;
                }
            }
            var sequence = P(schema, "rowversion_v1");
            var current = Convert.ToInt64(await new NpgsqlCommand("SELECT last_value FROM " + sequence, pg).ExecuteScalarAsync());
            var nextBase = Math.Max(current, maximum);
            await using var set = new NpgsqlCommand("SELECT setval(@s::regclass,@n,true)", pg);
            set.Parameters.AddWithValue("s", sequence); set.Parameters.AddWithValue("n", Math.Max(1L, nextBase)); await set.ExecuteScalarAsync();
            var observed = Convert.ToInt64(await new NpgsqlCommand("SELECT nextval('" + sequence.Replace("'", "''") + "'::regclass)", pg).ExecuteScalarAsync());
            if (observed <= maximum) throw new InvalidOperationException("Future PG token could collide with imported SQL tokens.");
            results.Add(new { Namespace = schema, ImportedTokenRows = tokens, MaximumImportedToken = maximum, ObservedNext = observed, Passed = true });
        }
        return results;
    }
    public async Task ResetTokens()
    {
        var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(cfg.RunDirectory, "transfer-result.json"))).RootElement;
        if (result.GetProperty("Status").GetString() != "Passed" || result.GetProperty("PgDatabase").GetString() != cfg.PgDatabase) throw new InvalidOperationException("Completed owned data copy required.");
        await using var pg = new NpgsqlConnection(cfg.PostgreSqlConnection); await pg.OpenAsync(); await VerifyPg(pg);
        await using var tx = await pg.BeginTransactionAsync(); var checks = await SyncTokenSequences(pg); await tx.CommitAsync();
        Save("token-sequence-verification.json", new { Status = "Passed", Database = cfg.PgDatabase, Changes = "Only four provider token sequences advanced; imported data and original tokens unchanged.", Checks = checks });
        Console.WriteLine("Verified future token generation above all imported SQL tokens in four namespaces.");
    }
}
static class PolicyTests
{
    public static void Run()
    {
        var passed = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Failed: " + name); passed++; }
        var c = new Settings("CP6DB-PG-LOCAL-COPY", "1234abcd", "unused", "CP6MIG_SNAPSHOT_20261004_1234abcd",
            "Server=localhost\\KOUSQLSERVER;Database=CP6MIG_SNAPSHOT_20261004_1234abcd;Integrated Security=True",
            "Host=127.0.0.1;Port=5432;Database=cp6db_pg_test_20261004_1234abcd;Username=fixture", "cp6db_pg_test_20261004_1234abcd");
        Guard.Validate(c); passed++;
        void Reject(Settings v) { try { Guard.Validate(v); } catch (InvalidOperationException) { passed++; return; } throw new InvalidOperationException("Protected target accepted."); }
        Reject(c with { SqlSnapshotConnection = "Server=localhost\\KOUSQLSERVER;Database=CP6DB;Integrated Security=True" });
        Reject(c with { PostgreSqlConnection = "Host=127.0.0.1;Database=postgres;Username=fixture" });
        Reject(c with { PostgreSqlConnection = "Host=remote.example.invalid;Database=cp6db_pg_test_20261004_1234abcd;Username=fixture" });
        Reject(c with { RunId = "deadbeef" });
        Reject(c with { Task = "other" });
        Check(Transfer.HashRow([DBNull.Value]) != Transfer.HashRow([""]), "NULL differs from empty string");
        Check(Transfer.HashRow(["日本語中文\n", new byte[] { 0, 255 }]) == Transfer.HashRow(["日本語中文\n", new byte[] { 0, 255 }]), "Unicode and raw bytes retained");
        Check(Transfer.HashRow([1.2300m]) == Transfer.HashRow([1.23m]), "Numeric equality across scale");
        var d = new DateTime(638500000000000007, DateTimeKind.Unspecified);
        Check(((DateTime)Transfer.Normalize(d, "timestamp")).Ticks == 638500000000000000, "PG microsecond normalization");
        Check((string)Transfer.Normalize(d, "text") == d.ToString("O", CultureInfo.InvariantCulture), "Archive retains original 100ns timestamp");
        var offset = new DateTimeOffset(2026, 10, 4, 9, 1, 2, TimeSpan.FromHours(9));
        Check(((string)Transfer.Normalize(offset, "text")).EndsWith("+09:00"), "Archive retains offset");
        Check(((DateTime)Transfer.Normalize(offset, "timestamptz")).Ticks == offset.UtcTicks, "Operational timestamp retains instant");
        Check(BinaryPrimitives.ReadInt64BigEndian(new byte[] { 0, 0, 0, 0, 0, 0, 1, 0 }) == 256, "SQL and PG tokens use network byte order");
        Console.WriteLine("Transfer guard/canonicalization checks passed: " + passed);
    }
}
