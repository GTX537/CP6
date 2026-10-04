using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CP6.DatabaseCompatibility.Testing;

internal enum DatabaseFixtureRole
{
    Schema, Runtime, OidcIdentity, Erp, CoreWms, Space, Reports, SpaceHistory,
    SqlUpgrade, Application, Restore
}

/// <summary>
/// Test-only linked source. Validates a runner-created database without creating,
/// marking, migrating or deleting it. Legacy fixtures opt in only through WP6 inputs.
/// </summary>
internal sealed class OwnedTestDatabase
{
    internal const string ScopeVariable = "CP6_COMPAT_SCOPE";
    internal const string NameVariable = "CP6_COMPAT_DATABASE_NAME";
    internal const string OwnerVariable = "CP6_TEST_DATABASE_OWNER";
    internal const string TaskName = "DB-COMPAT-01-WP6";
    private const string DatabaseNamePattern = "\\ACP6Compat_WP6_(?<date>[0-9]{8})_(?<owner>[a-f0-9]{8})_(?<role>[a-z]+)\\z";

    private OwnedTestDatabase(DatabaseOptions database, string connection, string name, string owner, DatabaseFixtureRole role)
        => (Database, ConnectionString, DatabaseName, Owner, Role) = (database, connection, name, owner, role);

    public DatabaseOptions Database { get; }
    public string ConnectionString { get; }
    public string DatabaseName { get; }
    public string Owner { get; }
    public DatabaseFixtureRole Role { get; }

    // Never render the connection or credentials through implicit formatting.
    public override string ToString() => $"{TaskName}:{Database.Provider}:{DatabaseName}";

    public static bool IsRequested(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        return environment(ScopeVariable) is not null || environment(NameVariable) is not null;
    }

    // Select only the matching fixture during discovery of unrelated filtered cases.
    // An incomplete global selection remains required and is rejected by Parse.
    public static bool IsRequestedForRole(DatabaseFixtureRole role, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        if (!IsRequested(environment)) return false;
        var match = Regex.Match(environment(NameVariable) ?? "", DatabaseNamePattern);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return true;
        var roleText = match.Groups["role"].Value;
        if (!Enum.GetValues<DatabaseFixtureRole>().Any(value => RoleText(value) == roleText)) return true;
        return RoleText(role) == roleText;
    }

    public static OwnedTestDatabase? FromEnvironment(DatabaseOptions database, string? connection,
        DatabaseFixtureRole[] allowedRoles, string applicationName, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        if (!IsRequested(environment)) return null;
        return Parse(database, connection, environment(ScopeVariable), environment(NameVariable),
            environment(OwnerVariable), allowedRoles, applicationName);
    }

    public static OwnedTestDatabase Parse(DatabaseOptions database, string? connection, string? scope,
        string? expectedDatabaseName, string? owner, DatabaseFixtureRole[] allowedRoles, string applicationName)
    {
        Require(scope == "WP6", "CP6_COMPAT_SCOPE_INVALID");
        Require(owner is not null && Regex.IsMatch(owner, "\\A[a-f0-9]{32}\\z"), "CP6_COMPAT_OWNER_INVALID");
        var match = Regex.Match(expectedDatabaseName ?? "", DatabaseNamePattern);
        Require(match.Success && DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "CP6_COMPAT_DATABASE_NAME_INVALID");
        var roleText = match.Groups["role"].Value;
        var roles = Enum.GetValues<DatabaseFixtureRole>();
        var role = roles.FirstOrDefault(value => RoleText(value) == roleText);
        Require(roles.Any(value => RoleText(value) == roleText), "CP6_COMPAT_DATABASE_NAME_INVALID");
        Require(match.Groups["owner"].Value == owner![..8], "CP6_COMPAT_DATABASE_OWNER_MISMATCH");
        Require(allowedRoles is not null && allowedRoles.Contains(role), "CP6_COMPAT_DATABASE_ROLE_MISMATCH");
        Require(database is not null, "CP6_COMPAT_PROVIDER_REQUIRED");
        Require(!string.IsNullOrWhiteSpace(connection), "CP6_COMPAT_CONNECTION_REQUIRED");

        string canonical;
        if (database!.Provider == DatabaseProvider.PostgreSql)
        {
            var pg = ParsePostgreSql(connection!);
            Require(pg.Host is "localhost" or "127.0.0.1" or "::1", "CP6_COMPAT_LOOPBACK_REQUIRED");
            Require(pg.Database == expectedDatabaseName, "CP6_COMPAT_CONNECTION_DATABASE_MISMATCH");
            Require(!pg.Multiplexing, "CP6_COMPAT_PG_MULTIPLEXING_FORBIDDEN");
            Require(pg.SearchPath is null or "public", "CP6_COMPAT_PG_SEARCH_PATH_INVALID");
            pg.IncludeErrorDetail = false;
            pg.SearchPath = "public";
            pg.ApplicationName = applicationName;
            canonical = pg.ConnectionString;
        }
        else
        {
            var sql = ParseSqlServer(connection!);
            Require(IsSqlLoopback(sql.DataSource), "CP6_COMPAT_LOOPBACK_REQUIRED");
            Require(sql.InitialCatalog == expectedDatabaseName, "CP6_COMPAT_CONNECTION_DATABASE_MISMATCH");
            Require(string.IsNullOrEmpty(sql.AttachDBFilename), "CP6_COMPAT_SQL_ATTACH_FORBIDDEN");
            Require(!sql.MultipleActiveResultSets, "CP6_COMPAT_SQL_MARS_FORBIDDEN");
            sql.ApplicationName = applicationName;
            canonical = sql.ConnectionString;
        }
        return new(database, canonical, expectedDatabaseName!, owner!, role);
    }

    public async Task<OwnedDatabaseIdentity> VerifyAsync(CancellationToken cancellationToken = default)
    {
        string? actualName;
        string? actualTask;
        string? actualOwner;
        string version;
        try
        {
            await using var connection = new DatabaseConnectionFactory(Database).Create(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            version = connection.ServerVersion;
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 30;
            command.CommandText = Database.Provider == DatabaseProvider.PostgreSql
                ? "SELECT current_database(), shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
                : "SELECT DB_NAME(), (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask'), (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner')";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                actualName = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (Database.Provider == DatabaseProvider.PostgreSql)
                {
                    var marker = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var parts = marker?.Split(':');
                    actualTask = parts?.Length == 2 ? parts[0] : null;
                    actualOwner = parts?.Length == 2 ? parts[1] : null;
                }
                else
                {
                    actualTask = reader.IsDBNull(1) ? null : reader.GetString(1);
                    actualOwner = reader.IsDBNull(2) ? null : reader.GetString(2);
                }
            }
            else (actualName, actualTask, actualOwner) = (null, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Authentication and provider diagnostics can contain private connection details.
            throw new InvalidOperationException("CP6_COMPAT_DATABASE_UNAVAILABLE");
        }

        Require(actualName == DatabaseName, "CP6_COMPAT_ACTUAL_DATABASE_MISMATCH");
        Require(actualTask == TaskName && actualOwner == Owner, "CP6_COMPAT_ACTUAL_OWNER_MISMATCH");
        return new(Database.Provider.ToString(), DatabaseName, version);
    }

    private static string RoleText(DatabaseFixtureRole role) => role.ToString().ToLowerInvariant();

    private static bool IsSqlLoopback(string source)
    {
        if (source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) source = source[4..];
        // Accept only a literal loopback host with an optional named instance or TCP port.
        return Regex.IsMatch(source,
            "\\A(?:localhost|127\\.0\\.0\\.1|\\[::1\\]|::1)(?:\\\\[A-Za-z0-9_]+)?(?:,[0-9]{1,5})?\\z");
    }

    private static SqlConnectionStringBuilder ParseSqlServer(string input)
    {
        try { return new(input); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        { throw new InvalidOperationException("CP6_COMPAT_CONNECTION_INVALID"); }
    }

    private static NpgsqlConnectionStringBuilder ParsePostgreSql(string input)
    {
        try { return new(input); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        { throw new InvalidOperationException("CP6_COMPAT_CONNECTION_INVALID"); }
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidOperationException(code);
    }
}

internal sealed record OwnedDatabaseIdentity(string Provider, string DatabaseName, string ServerVersion);
