using System.Text.Json;
using CP6.DatabaseCompatibility.Probe;

if (args.Contains("--help") || args.Length == 0)
{
    Console.WriteLine("WP1 limited-table probe. Connection strings are read only from an environment variable.");
    Console.WriteLine("--self-test [--output PATH] [--source-sha SHA]");
    Console.WriteLine("--provider SqlServer|PostgreSql [--connection-env CP6_TEST_SQLSERVER|CP6_TEST_POSTGRES] --output PATH [--source-sha SHA]");
    Console.WriteLine("Add --negative-control to deliberately omit a stale-write predicate; this fixture must fail and clean up.");
    Console.WriteLine("Add --setup-negative-control to fail during transactional setup; cleanup must verify schema absence.");
    Console.WriteLine("Requires loopback and a dedicated CP6Compat_ database; creates and cleans only its unique marked schema.");
    Console.WriteLine("CP6_TEST_DATABASE_OWNER must match the owner's 32-hex receipt and the selected database metadata.");
    return 0;
}

string? Value(string key)
{
    var index = Array.IndexOf(args, key);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var report = new ProbeReport { SourceSha = Value("--source-sha"), SourceState = Value("--source-state") ?? "SourceSha identifies base HEAD; experiment input includes the uncommitted task working tree.", Provider = Value("--provider") ?? "SelfTest" };
report.RecordDependencies();
var output = Path.GetFullPath(Value("--output") ?? Path.Combine(Path.GetTempPath(), $"CP6Compat_probe_{Guid.NewGuid():N}.json"));
if (args.Contains("--restart-generation"))
{
    await report.CheckAsync("Identity.RealProcessRestart", async () =>
    {
        Expect.True(Value("--provider") is "SqlServer" or "PostgreSql", "Restart requires the exact provider name.");
        Expect.True(Enum.TryParse<ProbeProvider>(Value("--provider"), false, out var provider), "Restart provider is required.");
        var variable = Value("--connection-env") ?? (provider == ProbeProvider.PostgreSql ? "CP6_TEST_POSTGRES" : "CP6_TEST_SQLSERVER");
        var connectionString = Environment.GetEnvironmentVariable(variable);
        Expect.True(!string.IsNullOrEmpty(connectionString), "Restart connection environment is unavailable.");
        var schema = Value("--schema") ?? throw new ProbeAssertionException("Restart schema is unavailable.");
        await using var database = new ProbeDatabase(provider, connectionString!, schema);
        await using var connection = database.NewConnection();
        await connection.OpenAsync();
        await ProbeSafety.VerifyDatabaseOwnerAsync(provider, connection, ProbeSafety.ValidateDatabaseOwner(Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER")));
        var owner = Guid.Parse(Value("--owner")!);
        var cursor = new DirectoryCursor(Guid.Parse(Value("--tenant")!), long.Parse(Value("--generation")!), long.Parse(Value("--position")!));
        var owned = await Dapper.SqlMapper.QuerySingleAsync<int>(connection, $"SELECT COUNT(*) FROM {database.Table("__cp6_compat_owner")} WHERE {database.Column("OwnerId")}=@Owner AND {database.Column("Task")}=@Task", new { Owner = owner, Task = "DB-COMPAT-01/WP1" });
        Expect.True(owned == 1, "Restart schema owner must match.");
        var rejected = false;
        DirectoryPage? page = null;
        try { page = await IdentityExperiments.ReadPageAsync(database, connection, cursor.TenantId, cursor, 2); }
        catch (SnapshotChangedException) { rejected = true; }
        var expectReject = Value("--restart-mode") == "reject";
        Expect.True(rejected == expectReject, "Restart must honor the persisted generation boundary.");
        if (!expectReject)
        {
            var expected = (Value("--expected-positions") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray();
            Expect.True(expected.Length == 2 && page is not null && page.Rows.Count == 2 && page.Rows.Select(row => row.Position).SequenceEqual(expected), "Restart continuation must return the two expected directory positions in exact order, never an empty page.");
        }
        return expectReject ? "New process rejected invalidated persisted cursor." : $"New process resumed persisted cursor: count={page!.Rows.Count}, positions={string.Join(',', page.Rows.Select(row => row.Position))}.";
    });
}
else if (args.Contains("--self-test"))
{
    await report.CheckAsync("Safety.SelfTest", ProbeSafety.SelfTestAsync);
}
else
{
    ProbeDatabase? database = null;
    try
    {
        Expect.True(Value("--provider") is "SqlServer" or "PostgreSql", "Choose the exact provider SqlServer or PostgreSql.");
        Expect.True(Enum.TryParse<ProbeProvider>(Value("--provider"), false, out var provider) && Enum.IsDefined(provider), "Choose the exact provider SqlServer or PostgreSql.");
        var variable = Value("--connection-env") ?? (provider == ProbeProvider.PostgreSql ? "CP6_TEST_POSTGRES" : "CP6_TEST_SQLSERVER");
        var connectionString = Environment.GetEnvironmentVariable(variable);
        Expect.True(!string.IsNullOrWhiteSpace(connectionString), "Requested connection environment variable is unavailable; no connection was attempted.");
        database = new ProbeDatabase(provider, connectionString!) { ConnectionEnvironment = variable, InjectSetupFailure = args.Contains("--setup-negative-control") };
        report.Schema = database.Schema;
        report.OwnershipId = database.OwnershipId.ToString();
        await database.InitializeAsync();
        report.DatabaseVersion = database.ServerVersion;
        report.Schema = database.Schema;
        report.OwnershipId = database.OwnershipId.ToString();
        report.Add("Isolation.CreateOwnedSchema", "Passed", "Unique schema and transactionally installed ownership marker; existing database objects were not used.");
        if (args.Contains("--negative-control")) await TokenExperiments.RunNegativeControlAsync(database, report);
        else
        {
            await TokenExperiments.RunAsync(database, report);
            await IdentityExperiments.RunAsync(database, report);
            await TransactionExperiments.RunAsync(database, report);
            await PlatformExperiments.RunAsync(database, report);
        }
    }
    catch (Exception exception) { report.Add("Probe.SetupOrRun", "Failed", SafeErrors.Describe(exception)); }
    finally
    {
        if (database is not null)
        {
            try
            {
                var detail = await database.CleanupAsync();
                report.Add("Isolation.Cleanup", "Passed", detail);
            }
            catch (Exception exception) { report.Add("Isolation.Cleanup", "Failed", SafeErrors.Describe(exception)); }
            await database.DisposeAsync();
        }
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Report: {output}");
return report.Checks.Any(check => check.Status == "Failed") ? 1 : report.Checks.Any(check => check.Status == "Blocked") ? 2 : 0;
