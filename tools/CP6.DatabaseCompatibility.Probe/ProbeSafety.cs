using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Npgsql;
using Dapper;

namespace CP6.DatabaseCompatibility.Probe;

internal enum ProbeProvider { SqlServer, PostgreSql }

internal sealed record ProbeOptions(ProbeProvider Provider, string ConnectionEnvironment, string OutputPath, string? SourceSha);

internal static partial class ProbeSafety
{
    public static string NewSchema() => $"CP6Compat_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid():N}";

    [GeneratedRegex(@"\ACP6Compat_[A-Za-z0-9_]{1,52}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OwnedName();

    public static string Quote(string identifier, ProbeProvider provider)
    {
        Expect.True(OwnedName().IsMatch(identifier) || KnownObjects.Contains(identifier), "Identifier is outside the probe-owned object allowlist.");
        return provider == ProbeProvider.PostgreSql ? $"\"{identifier}\"" : $"[{identifier}]";
    }

    internal static readonly HashSet<string> KnownObjects = new(StringComparer.Ordinal)
    {
        "__cp6_compat_owner", "DatabaseTokenRows", "ApplicationTokenRows", "DirectoryRecords",
        "TenantGenerations", "BusinessRecords", "OutboxRecords", "AuditRecords",
        "TokenSequence", "SetToken", "AdvanceTenantGeneration", "DatabaseTokenRowsToken",
        "DirectoryGeneration", "TriggerSourceRows", "TriggerWrite", "TriggerSourceWrite",
        "Cp6_OutboxMessage", "Cp6_InboxMessage", "Cp6_InboxAggregateCheckpoint", "Cp6_DeadLetterRecord"
    };

    public static DbConnection CreateConnection(ProbeProvider provider, string connectionString)
    {
        if (provider == ProbeProvider.PostgreSql)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            Expect.True(IsLoopbackHost(builder.Host), "PostgreSQL probe requires a single literal loopback host or localhost.");
            RequireDatabase(builder.Database);
            builder.ApplicationName = "CP6Compat.DatabaseCompatibilityProbe";
            builder.Timeout = 5;
            builder.CommandTimeout = 20;
            builder.IncludeErrorDetail = false;
            builder.Pooling = false;
            return new NpgsqlConnection(builder.ConnectionString);
        }

        var sql = new SqlConnectionStringBuilder(connectionString);
        Expect.True(IsSqlLoopback(sql.DataSource), "SQL Server probe requires a loopback host or LocalDB instance.");
        RequireDatabase(sql.InitialCatalog);
        sql.ApplicationName = "CP6Compat.DatabaseCompatibilityProbe";
        sql.ConnectTimeout = 5;
        sql.Pooling = false;
        return new SqlConnection(sql.ConnectionString);
    }

    [GeneratedRegex(@"\A[0-9a-fA-F]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerIdentity();

    public static string ValidateDatabaseOwner(string? owner)
    {
        Expect.True(owner is not null && OwnerIdentity().IsMatch(owner), "CP6_TEST_DATABASE_OWNER must contain the task owner's 32-hex receipt identity; no schema was created.");
        return owner!;
    }

    public static async Task VerifyDatabaseOwnerAsync(ProbeProvider provider, DbConnection connection, string owner)
    {
        ValidateDatabaseOwner(owner);
        var sql = provider == ProbeProvider.PostgreSql
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP1')";
        var actual = await connection.QuerySingleOrDefaultAsync<string>(sql);
        var expected = provider == ProbeProvider.PostgreSql ? $"DB-COMPAT-01-WP1:{owner}" : owner;
        Expect.True(string.Equals(actual, expected, StringComparison.Ordinal), "Selected database ownership metadata did not match CP6_TEST_DATABASE_OWNER; no schema was created.");
    }

    private static void RequireDatabase(string? name) =>
        Expect.True(name is not null && OwnedName().IsMatch(name), "Probe connection must select a dedicated CP6Compat_ database prepared by the task owner.");

    internal static bool IsLoopbackHost(string? host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "::1" || host == "[::1]";

    private static bool IsSqlLoopback(string dataSource)
    {
        if (dataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)) return dataSource.Length > 10;
        var host = dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? dataSource[4..] : dataSource;
        if (host.StartsWith("np:", StringComparison.OrdinalIgnoreCase)) return false;
        host = host.Split('\\')[0].Split(',')[0];
        return host is "." or "(local)" || IsLoopbackHost(host);
    }

    public static async Task<string> SelfTestAsync()
    {
        foreach (var invalid in new[] { "db.example.com", "localhost,db.example.com", "127.0.0.2", "/tmp/socket", "localhost.example.com" })
            Expect.True(!IsLoopbackHost(invalid), "Remote/multiple/socket PostgreSQL hosts must be rejected.");
        foreach (var provider in Enum.GetValues<ProbeProvider>())
        {
            var invalid = provider == ProbeProvider.PostgreSql
                ? "Host=127.0.0.1;Database=Business;Username=probe"
                : "Server=127.0.0.1;Database=Business;Integrated Security=true";
            var rejected = false;
            try { using var connection = CreateConnection(provider, invalid); }
            catch (ProbeAssertionException) { rejected = true; }
            Expect.True(rejected, "Existing business database must be rejected before any connection is opened.");
            var remote = provider == ProbeProvider.PostgreSql
                ? "Host=db.example.com;Database=CP6Compat_selftest;Username=probe"
                : "Server=db.example.com;Database=CP6Compat_selftest;Integrated Security=true";
            var remoteRejected = false;
            try { using var connection = CreateConnection(provider, remote); }
            catch (ProbeAssertionException) { remoteRejected = true; }
            Expect.True(remoteRejected, "Remote server must be rejected before any connection is opened.");
            var identifierRejected = false;
            try { Quote("CP6Compat_x];DROP DATABASE x;--", provider); }
            catch (ProbeAssertionException) { identifierRejected = true; }
            Expect.True(identifierRejected, "Injected or unowned identifier must be rejected.");
            var valid = provider == ProbeProvider.PostgreSql
                ? "Host=127.0.0.1;Database=CP6Compat_selftest;Username=probe"
                : "Server=(localdb)\\MSSQLLocalDB;Database=CP6Compat_selftest;Integrated Security=true";
            using var unopened = CreateConnection(provider, valid);
            Expect.True(unopened.State == System.Data.ConnectionState.Closed, "Validation must not open a connection.");
        }
        Expect.Token([0, 1, 2, 3, 254, 255, 16, 32]);
        foreach (var invalidOwner in new string?[] { null, "", "not-an-owner", "00000000-0000-0000-0000-000000000000", new string('g', 32) })
        {
            var rejected = false;
            try { ValidateDatabaseOwner(invalidOwner); }
            catch (ProbeAssertionException) { rejected = true; }
            Expect.True(rejected, "Missing or malformed database receipt identity must be rejected before connection/schema creation.");
        }
        Expect.True(ValidateDatabaseOwner(new string('a', 32)).Length == 32, "A canonical 32-hex receipt must validate without database access.");
        Expect.True(NewSchema().Length <= 63, "PostgreSQL schema must fit the 63-byte identifier limit.");
        await Task.CompletedTask;
        return "Loopback/database/identifier guards and byte[]/Base64 contract checks passed without database access.";
    }
}
