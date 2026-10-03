using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("Owned WP2 real text probe: --provider SqlServer|PostgreSql --output PATH [--adapted].");
    Console.WriteLine("Reads CP6_TEST_SQLSERVER/CP6_TEST_POSTGRES and CP6_TEST_DATABASE_OWNER; never prints credentials.");
    return 0;
}
string? Arg(string name) => Array.IndexOf(args, name) is var index && index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
var provider = Arg("--provider");
var pg = provider == "PostgreSql";
var adapted = args.Contains("--adapted");
var output = Path.GetFullPath(Arg("--output") ?? throw new ArgumentException("Output is required."));
var checks = new List<TextCheck>();
var schema = $"CP6Compat_text_{Guid.NewGuid():N}";
var collation = $"cp6_probe_{Guid.NewGuid():N}";
var owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER");
DbConnection? connection = null;
DbTransaction? transaction = null;
TextContext? context = null;
bool created = false;
string? serverVersion = null;
async Task Check(string name, Func<Task> action)
{
    try
    {
        if (transaction is not null) await transaction.SaveAsync("TextProbeCase");
        try { await action(); }
        finally { if (transaction is not null) await transaction.RollbackAsync("TextProbeCase"); }
        checks.Add(new(name, "Passed", "Observed expected SQL Server contract."));
    }
    catch (Exception ex) { checks.Add(new(name, "Failed", SafeError(ex))); }
    Console.WriteLine($"{checks[^1].Status}: {name}: {checks[^1].Detail}");
}
try
{
    Require(provider is "SqlServer" or "PostgreSql", "Exact provider is required.");
    Require(owner is not null && Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "Task owner receipt is required.");
    var connectionInput = Environment.GetEnvironmentVariable(pg ? "CP6_TEST_POSTGRES" : "CP6_TEST_SQLSERVER");
    Require(!string.IsNullOrWhiteSpace(connectionInput), "Test connection environment is required.");
    if (pg)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionInput);
        Require(builder.Host is "localhost" or "127.0.0.1" or "::1", "Literal loopback is required.");
        Require(Regex.IsMatch(builder.Database ?? "", "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"), "Dedicated WP2 database is required.");
        builder.Pooling = false; builder.IncludeErrorDetail = false; builder.Timeout = 5; builder.CommandTimeout = 20;
        connection = new NpgsqlConnection(builder.ConnectionString);
    }
    else
    {
        var builder = new SqlConnectionStringBuilder(connectionInput);
        Require(builder.DataSource == "localhost\\KOUSQLSERVER", "Recorded loopback SQL instance is required.");
        Require(Regex.IsMatch(builder.InitialCatalog, "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"), "Dedicated WP2 database is required.");
        builder.Pooling = false; builder.ConnectTimeout = 5;
        connection = new SqlConnection(builder.ConnectionString);
    }
    await connection.OpenAsync();
    serverVersion = connection.ServerVersion;
    var marker = await connection.QuerySingleOrDefaultAsync<string>(pg
        ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
        : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')");
    Require(marker == (pg ? $"DB-COMPAT-01-WP2:{owner}" : owner), "Database ownership metadata must match before DDL.");
    transaction = await connection.BeginTransactionAsync();
    await connection.ExecuteAsync(pg ? $"CREATE SCHEMA \"{schema}\"; CREATE COLLATION public.\"{collation}\" (provider=icu,locale='und-u-ks-level2',deterministic=false);"
        : $"CREATE SCHEMA [{schema}];", transaction: transaction);
    created = true;
    if (pg) await connection.ExecuteAsync(PostgreSqlManagedTextV1.CreateFunctions(schema, collation), transaction: transaction);
    var options = new DbContextOptionsBuilder<TextContext>();
    if (pg)
    {
        options.UseNpgsql(connection);
        if (adapted) options.ReplaceService<IRelationalTypeMappingSource, PostgreSqlTextTypeMappingSource>()
            .ReplaceService<IQuerySqlGeneratorFactory, PostgreSqlTextQuerySqlGeneratorFactory>();
    }
    else options.UseSqlServer(connection);
    context = new TextContext(options.Options, schema, collation, adapted);
    await context.Database.UseTransactionAsync(transaction);
    var create = context.Database.GenerateCreateScript();
    // The schema/collation were installed inside the same rollback transaction.
    create = Regex.Replace(create, pg ? @"CREATE SCHEMA IF NOT EXISTS [^;]+;" : @"IF SCHEMA_ID\([^\r\n]+\r?\n\s*EXEC\([^\r\n]+", "");
    create = Regex.Replace(create, @"CREATE COLLATION [^;]+;", "");
    if (!pg) create = Regex.Replace(create, @"(?m)^GO\s*$", "");
    await connection.ExecuteAsync(create, transaction: transaction);

    var samples = new[] { "abc  ", "ab c", "tab\t", "中 ", "😀  ", "é", "e\u0301", "Ａ" };
    context.Rows.AddRange(samples.Select((text, index) => new TextRow { Id = index + 1, Value = text, FixedCode = "q1" }));
    context.Keys.Add(new TextKey { Value = "link  " });
    await context.SaveChangesAsync();
    // Insert native binary keys without assuming the reference provider's ORM
    // comparer honors an explicit per-property SQL collation.
    await connection.ExecuteAsync(pg ? $"INSERT INTO \"{schema}\".\"BinaryKeys\" (\"Value\") VALUES ('MessageA'),('messagea')"
        : $"INSERT INTO [{schema}].[BinaryKeys] ([Value]) VALUES ('MessageA'),('messagea')", transaction: transaction);
    context.ChangeTracker.Clear();
    await Check("Text.RawRoundTrip", async () => Require((await context.Rows.OrderBy(row => row.Id).Select(row => row.Value).ToArrayAsync()).SequenceEqual(samples), "Every stored scalar and whitespace must round-trip exactly."));
    await Check("Text.FixedCharPaddingRoundTrip", async () => Require(await context.Rows.Where(row => row.Id == 1).Select(row => row.FixedCode).SingleAsync() == "q1      ", "Fixed ASCII code must preserve eight bytes including padding."));
    foreach (var (name, lookup, expectedId) in new[] { ("PadSpaceParameter", "abc", 1), ("CaseParameter", "ABC", 1), ("WidthParameter", "A", 8), ("NfcParameter", "é", 6) })
        await Check($"Text.{name}", async () => Require((await context.Rows.Where(row => row.Value == lookup).Select(row => row.Id).ToArrayAsync()).Contains(expectedId), "Parameter equality must preserve the source collation contract."));
    await Check("Text.AccentDistinct", async () => Require(!await context.Rows.AnyAsync(row => row.Value == "e"), "Accent-sensitive keys must stay distinct."));
    await Check("Text.MiddleSpaceDistinct", async () => Require(!await context.Rows.AnyAsync(row => row.Value == "abc" && row.Id == 2), "Interior spaces must stay significant."));
    await Check("Text.ArrayParameterPadSpace", async () => { var lookup = new[] { "abc", "A" }; Require((await context.Rows.Where(row => lookup.Contains(row.Value)).Select(row => row.Id).ToArrayAsync()).Order().SequenceEqual(new[] { 1, 8 }), "Array membership must retain scalar comparison semantics."); });
    await Check("Text.ContainsTailSpace", async () => Require(await context.Rows.AnyAsync(row => row.Id == 1 && row.Value.Contains(" ")), "Contains must see stored trailing whitespace."));
    await Check("Text.SubstringTailSpace", async () => Require(await context.Rows.Where(row => row.Id == 1).Select(row => row.Value.Substring(3, 2)).SingleAsync() == "  ", "Substring must retain stored trailing whitespace."));
    await Check("Text.ConcatTailSpace", async () => Require(await context.Rows.Where(row => row.Id == 1).Select(row => row.Value + "d").SingleAsync() == "abc  d", "Concatenation must retain stored trailing whitespace."));
    await Check("Text.LenUtf16", async () => Require(await context.Rows.Where(row => row.Id == 5).Select(row => row.Value.Length).SingleAsync() == 2, "SQL LEN counts the astral UTF-16 pair and excludes U+0020 tails."));
    await Check("Text.LenUtf16NoTail", async () => Require(await context.Rows.Where(row => row.Id == 5).Select(row => row.Value.TrimEnd().Length).SingleAsync() == 2, "Astral UTF-16 pair must count as two after trimming."));
    await Check("Text.ConcatThenEquality", async () => Require(await context.Rows.AnyAsync(row => row.Id == 1 && row.Value + "d" == "abc  d"), "String operation result must retain correct equality typing."));
    await Check("Text.ConcatThenCaseEquality", async () => Require(await context.Rows.AnyAsync(row => row.Id == 1 && row.Value + "d" == "ABC  D"), "String operation result must retain case-insensitive collation."));
    await Check("Text.SubstringThenWidthEquality", async () => Require(await context.Rows.AnyAsync(row => row.Id == 8 && row.Value.Substring(0, 1) == "A"), "Substring result must retain width-insensitive collation."));
    await Check("Text.ConcatThenNfcEquality", async () => Require(await context.Rows.AnyAsync(row => row.Id == 7 && row.Value + "d" == "éd"), "Concatenation result must retain NFC equivalence."));
    await Check("Text.TrimTail", async () => Require(await context.Rows.Where(row => row.Id == 1).Select(row => row.Value.Trim()).SingleAsync() == "abc", "Trim must remove source whitespace intentionally."));
    if (pg)
    {
        foreach (var (label, value, expected) in new[] { ("Emoji", "😀", 2), ("Euro", "€", 1), ("Chinese", "中", 2), ("Fullwidth", "Ａ", 2), ("SharpS", "ß", 1), ("RareHan", "𠀀", 2), ("Variation", "☃", 1) })
            await Check($"Capacity.Cp936.{label}", async () => Require(await connection.QuerySingleAsync<int>($"SELECT \"{schema}\".cp6_cp936_length_v1(@value)", new { value }, transaction) == expected, "Capacity must match the observed SQL CP936 byte count."));
        foreach (var (value, expected) in new[] { ("A", true), ("Ｆ", true), ("ä", true), ("é", true), ("ć", true), ("ß", false), ("g", false), (" ", false) })
            await Check($"Range.Hex.U{char.ConvertToUtf32(value, 0):X}", async () => Require(await connection.QuerySingleAsync<bool>($"SELECT \"{schema}\".cp6_text_range_v1(@value,'hex')", new { value }, transaction) == expected, "Managed linguistic range must match the observed SQL rule."));
        await Check("Range.AlphaLowerCase", async () => Require(await connection.QuerySingleAsync<bool>($"SELECT \"{schema}\".cp6_text_range_v1('usd','alpha')", transaction: transaction), "CI currency check must accept source lowercase value."));
        await Check("Range.NullPropagation", async () => Require(await connection.QuerySingleAsync<bool?>($"SELECT \"{schema}\".cp6_text_range_v1(NULL,'hex')", transaction: transaction) is null, "NULL must preserve SQL check three-valued logic."));
        await Check("Range.EmptyPropagation", async () => Require(await connection.QuerySingleAsync<bool>($"SELECT \"{schema}\".cp6_text_range_v1('','hex') AND \"{schema}\".cp6_text_range_v1('','alpha')", transaction: transaction), "Empty strings contain no invalid range characters, matching SQL NOT LIKE."));
    }
    await Check("Text.ForeignKeyPadSpace", async () =>
    {
        await connection.ExecuteAsync(pg ? $"INSERT INTO \"{schema}\".\"Children\" (\"Id\",\"Key\") VALUES (1,@key)" : $"INSERT INTO [{schema}].[Children] ([Id],[Key]) VALUES (1,@key)",
            new { key = "LINK" }, transaction);
        // Default raw Dapper text typing is intentionally a separate provider gate.
    });
    await Check("Text.UniquePadSpace", async () =>
    {
        await transaction.SaveAsync("UniqueCheck");
        var rejected = false;
        try { await connection.ExecuteAsync(pg ? $"INSERT INTO \"{schema}\".\"Keys\" (\"Value\") VALUES ('LINK')" : $"INSERT INTO [{schema}].[Keys] ([Value]) VALUES ('LINK')", transaction: transaction); }
        catch (PostgresException ex) when (ex.SqlState == "23505") { rejected = true; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { rejected = true; }
        await transaction.RollbackAsync("UniqueCheck");
        Require(rejected, "Native unique key must reject the case/tail-space equivalent value.");
    });
    if (pg && adapted)
    {
        await Check("Orm.PadSpaceForeignKeyFixup", async () =>
        {
            context.ChangeTracker.Clear();
            var parent = await context.Keys.SingleAsync();
            var child = new TextChild { Id = 2, Key = "LINK" };
            context.Add(child);
            Require(ReferenceEquals(child.Parent, parent), "Actual EF fixup must resolve CI/PAD SPACE foreign key to its tracked principal.");
            context.ChangeTracker.Clear();
        });
        await Check("Orm.KeyEqualityAndHash", () =>
        {
            var keyComparer = context.Model.FindEntityType(typeof(TextKey))!.FindProperty("Value")!.GetKeyValueComparer();
            foreach (var (left, right) in new[] { ("link  ", "LINK"), ("A", "Ａ"), ("é", "e\u0301") })
            {
                Require(keyComparer.Equals(left, right), "Equivalent native business keys must resolve to one tracked key.");
                Require(keyComparer.GetHashCode(left) == keyComparer.GetHashCode(right), "Equivalent tracked keys require equal hashes.");
            }
            Require(!keyComparer.Equals("a", "ä"), "Accent distinct keys must not collapse in identity resolution.");
            return Task.CompletedTask;
        });
        await Check("Orm.BinaryCaseDistinct", () =>
        {
            var binaryComparer = new PostgreSqlBusinessTextComparer(binary: true);
            Require(!binaryComparer.Equals("MessageA", "messagea"), "Binary key identity must preserve case.");
            Require(binaryComparer.Equals("MessageA  ", "MessageA"), "Binary PAD SPACE keys must retain native tail semantics.");
            return Task.CompletedTask;
        });
        await Check("Orm.BinaryNativeKeysCoexist", async () =>
        {
            context.ChangeTracker.Clear();
            var rows = await context.BinaryKeys.OrderBy(row => row.Value).ToArrayAsync();
            Require(rows.Length == 2 && context.ChangeTracker.Entries<TextBinaryKey>().Count() == 2,
                "Both native C-collated keys must materialize as separate tracked entities in the same context.");
            var comparer = context.Model.FindEntityType(typeof(TextBinaryKey))!.FindProperty("Value")!.GetKeyValueComparer();
            Require(!comparer.Equals(rows[0].Value, rows[1].Value), "Actual property key mapping must retain binary case distinction.");
            context.ChangeTracker.Clear();
        });
        await Check("Orm.CaseChangeValueComparison", () =>
        {
            var row = context.Rows.Local.FirstOrDefault() ?? new TextRow { Id = 90, Value = "before", FixedCode = "q1" };
            context.Attach(row);
            row.Value = "BEFORE";
            context.ChangeTracker.DetectChanges();
            Require(context.Entry(row).Property(item => item.Value).IsModified, "Normal case-only property changes must remain saved changes.");
            context.ChangeTracker.Clear();
            return Task.CompletedTask;
        });
    }
}
catch (Exception ex) { checks.Add(new("Probe.SetupOrRun", "Failed", SafeError(ex))); Console.WriteLine($"Failed: Probe.SetupOrRun: {SafeError(ex)}"); }
finally
{
    if (transaction is not null)
    {
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();
        transaction = null;
    }
    if (connection is not null && connection.State == System.Data.ConnectionState.Open && created)
    {
        await Check("Isolation.RollbackCleanup", async () => Require(await connection.QuerySingleAsync<int>(pg
            ? "SELECT count(*)::integer FROM pg_namespace WHERE nspname=@schema"
            : "SELECT COUNT(*) FROM sys.schemas WHERE name=@schema", new { schema }) == 0, "All created schema objects must be absent after rollback."));
        if (pg) await Check("Isolation.CollationCleanup", async () => Require(await connection.QuerySingleAsync<int>("SELECT count(*)::integer FROM pg_collation WHERE collname=@collation", new { collation }) == 0, "Created collation must be absent after rollback."));
    }
    if (context is not null) await context.DisposeAsync();
    if (connection is not null) await connection.DisposeAsync();
    var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var file in new[] { "Program.cs", "CP6.DatabaseCompatibility.TextProbe.csproj", "packages.lock.json", "../../CP6.Core/Persistence/PostgreSqlTextTypeMappingSource.cs", "../../CP6.Core/Persistence/PostgreSqlTextQuerySqlGenerator.cs", "../../CP6.Core/Persistence/PostgreSqlManagedTextV1.cs", "../../CP6.Core/Persistence/PostgreSqlBusinessTextComparer.cs" })
        if (File.Exists(file)) inputs[file] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)));
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Scope = "WP2 real isolated text contract, not full migrations or production acceptance", Provider = provider, Adapted = adapted, DatabaseVersion = serverVersion,
        SourceBase = Arg("--source-sha"), SourceState = "Uncommitted WP2 source identified by SHA256 inputs; applicable build success log is required.", SourceInputs = inputs,
        ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(TextContext).Assembly.Location))), Schema = schema, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
}
return checks.Any(check => check.Status == "Failed") ? 1 : 0;

static void Require(bool condition, string detail) { if (!condition) throw new TextAssertionException(detail); }
static string SafeError(Exception exception) => exception switch
{
    TextAssertionException expected => expected.Message,
    DbUpdateException { InnerException: not null } update => "DbUpdateException / " + SafeError(update.InnerException!),
    PostgresException postgres => "PostgresException SQLSTATE=" + postgres.SqlState,
    SqlException sql => "SqlException Number=" + sql.Number,
    _ => exception.GetType().Name
};
record TextCheck(string Name, string Status, string Detail);
sealed class TextAssertionException(string detail) : Exception(detail);
sealed class TextRow { public int Id { get; set; } public string Value { get; set; } = ""; public string FixedCode { get; set; } = ""; }
sealed class TextKey { public string Value { get; set; } = ""; public List<TextChild> Children { get; set; } = []; }
sealed class TextBinaryKey { public string Value { get; set; } = ""; }
sealed class TextChild { public int Id { get; set; } public string Key { get; set; } = ""; public TextKey Parent { get; set; } = null!; }
sealed class TextContext(DbContextOptions<TextContext> options, string schema, string collation, bool adapted) : DbContext(options)
{
    public DbSet<TextRow> Rows => Set<TextRow>();
    public DbSet<TextKey> Keys => Set<TextKey>();
    public DbSet<TextBinaryKey> BinaryKeys => Set<TextBinaryKey>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TextRow>().ToTable("Rows", schema).Property(row => row.Id).ValueGeneratedNever();
        model.Entity<TextRow>().Property(row => row.FixedCode).HasColumnType(Database.IsNpgsql() ? "character(8)" : "char(8)").IsUnicode(false).IsFixedLength().HasMaxLength(8);
        model.Entity<TextKey>().ToTable("Keys", schema).HasKey(row => row.Value);
        model.Entity<TextBinaryKey>().ToTable("BinaryKeys", schema).HasKey(row => row.Value);
        model.Entity<TextBinaryKey>().Property(row => row.Value).UseCollation(Database.IsNpgsql() ? "C" : "Latin1_General_100_BIN2");
        model.Entity<TextChild>().ToTable("Children", schema).Property(row => row.Id).ValueGeneratedNever();
        model.Entity<TextChild>().HasOne(row => row.Parent).WithMany(row => row.Children).HasForeignKey(row => row.Key);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(property => property.ClrType == typeof(string)))
            {
                if (property.Name != "FixedCode")
                {
                    property.SetMaxLength(100);
                    if (Database.IsNpgsql()) property.SetColumnType(adapted ? "bpchar" : "character varying(100)");
                }
                if (Database.IsNpgsql())
                {
                    if (entity.ClrType != typeof(TextBinaryKey)) property.SetCollation(collation);
                }
            }
    }
}
