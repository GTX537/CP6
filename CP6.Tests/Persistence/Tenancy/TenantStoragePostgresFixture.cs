using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

// Opt-in follows the existing native fixture pattern. An incomplete requested run fails setup.
internal sealed class TenantStoragePostgresFactAttribute : FactAttribute
{
    internal const string RootVariable = "CP6_TENANCY_PG_ROOT";
    internal const string BinVariable = "CP6_TENANCY_PG_BIN";
    internal static bool Selected => Environment.GetEnvironmentVariable(RootVariable) is not null
        || Environment.GetEnvironmentVariable(BinVariable) is not null;
    public TenantStoragePostgresFactAttribute()
    {
        if (!Selected) Skip = "Explicit owned PostgreSQL component run requires CP6_TENANCY_PG_ROOT and CP6_TENANCY_PG_BIN.";
    }
}

[CollectionDefinition("TenantStoragePostgres", DisableParallelization = true)]
public sealed class TenantStoragePostgresCollection : ICollectionFixture<TenantStoragePostgresFixture>;

public sealed class TenantStoragePostgresFixture : IAsyncLifetime
{
    internal const int Port = 55436;
    internal const string Database = "tenant_component";
    internal readonly string PasswordA = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal readonly string PasswordB = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal readonly string PasswordAdmin = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _ownerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal string ProvisioningAdminPassword => _ownerPassword;
    private string _root = "", _bin = "", _data = "";
    private bool _started;

    public async Task InitializeAsync()
    {
        if (!TenantStoragePostgresFactAttribute.Selected) return;
        var requestedRoot = Environment.GetEnvironmentVariable(TenantStoragePostgresFactAttribute.RootVariable);
        _bin = Environment.GetEnvironmentVariable(TenantStoragePostgresFactAttribute.BinVariable) ?? "";
        if (string.IsNullOrWhiteSpace(requestedRoot) || !Path.IsPathFullyQualified(requestedRoot)
            || !Path.IsPathFullyQualified(_bin) || !File.Exists(Binary("initdb")))
            throw new InvalidOperationException("Requested PostgreSQL run requires a new absolute owner root and installed PostgreSQL binaries.");
        _root = Path.GetFullPath(requestedRoot);
        if (!Path.GetFileName(_root).StartsWith("tenant-connections-", StringComparison.Ordinal)
            || Directory.Exists(_root) || File.Exists(_root))
            throw new InvalidOperationException("PostgreSQL owner root must be new and task named; existing files are never reused.");
        for (var ancestor = Directory.GetParent(_root); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("PostgreSQL owner root must not traverse a reparse point.");
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();
        listener.Stop();
        Directory.CreateDirectory(_root);
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(_root).SetAccessControl(security);
        }
        else File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _data = Path.Combine(_root, "data");
        var passwordFile = Path.Combine(_root, "owner-password.private");
        await File.WriteAllTextAsync(passwordFile, _ownerPassword);
        await File.WriteAllTextAsync(Path.Combine(_root, "ownership.txt"), $"{_root}\n{_data}\n{Port}\n");
        await RunAsync("initdb", ["-D", _data, "-U", "fixture_owner", "--pwfile", passwordFile,
            "--auth-host=scram-sha-256", "--auth-local=scram-sha-256", "--no-locale", "-E", "UTF8"]);
        await File.AppendAllTextAsync(Path.Combine(_data, "postgresql.conf"), $"""

            listen_addresses = '127.0.0.1'
            port = {Port}
            max_connections = 40
            log_statement = 'none'
            log_min_error_statement = 'panic'
            log_parameter_max_length = 0
            log_parameter_max_length_on_error = 0
            """);
        await RunAsync("pg_ctl", ["-D", _data, "-l", Path.Combine(_root, "postgres.log"), "-w", "start"]);
        _started = true;
        try
        {
            await using (var admin = await OpenOwnerAsync("postgres"))
            {
                await Sql(admin, $"CREATE ROLE tenant_a LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{PasswordA}'");
                await Sql(admin, $"CREATE ROLE tenant_b LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{PasswordB}'");
                await Sql(admin, $"CREATE ROLE privileged_fixture LOGIN SUPERUSER PASSWORD '{PasswordAdmin}'");
                await Sql(admin, $"CREATE DATABASE {Database}");
                await Sql(admin, "CREATE DATABASE tenant_other");
                await Sql(admin, $"REVOKE ALL ON DATABASE {Database} FROM PUBLIC; GRANT CONNECT ON DATABASE {Database} TO tenant_a, tenant_b");
                await Sql(admin, "REVOKE ALL ON DATABASE tenant_other FROM PUBLIC; GRANT CONNECT ON DATABASE tenant_other TO tenant_a, tenant_b");
            }
            await using var connection = await OpenOwnerAsync();
            await Sql(connection, "REVOKE ALL ON SCHEMA public FROM PUBLIC");
            foreach (var (tenant, role) in new[] { (TenantStorageContext.A, "tenant_a"), (TenantStorageContext.B, "tenant_b") })
            {
                var schema = "t_" + tenant.ToString("N");
                await Sql(connection, $"CREATE SCHEMA {schema}; GRANT USAGE ON SCHEMA {schema} TO {role}; "
                    + $"CREATE TABLE {schema}.component_probe (id integer PRIMARY KEY, marker text NOT NULL); "
                    + $"INSERT INTO {schema}.component_probe VALUES (1, '{role}'); "
                    + $"GRANT SELECT, INSERT, UPDATE, DELETE ON {schema}.component_probe TO {role}");
            }
            var version = await Scalar(connection, "SHOW server_version");
            await File.WriteAllTextAsync(Path.Combine(_root, "server-version.txt"), version?.ToString());
        }
        catch
        {
            // Keep the failed cluster and logs, but stop only this instance after checking identity.
            await DisposeAsync();
            throw new InvalidOperationException("Owned PostgreSQL fixture initialization failed; inspect private owner-root evidence.");
        }
    }

    internal async Task<NpgsqlConnection> OpenOwnerAsync(string database = Database, string searchPath = "pg_catalog")
        => await OpenRawAsync("fixture_owner", _ownerPassword, database, searchPath);

    internal static async Task<NpgsqlConnection> OpenRawAsync(string username, string password, string database = Database, string searchPath = "pg_catalog")
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = Port, Database = database, Username = username, Password = password,
            Pooling = false, SearchPath = searchPath, SslMode = SslMode.Disable, Enlist = false,
            IncludeErrorDetail = false, Timeout = 5, CommandTimeout = 5, Options = " "
        }.ConnectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw new InvalidOperationException("Owned fixture connection failed."); }
    }

    internal static async Task<int> Sql(NpgsqlConnection connection, string sql, CancellationToken ct = default)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<object?> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct = default)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(ct);
    }

    public async Task DisposeAsync()
    {
        if (!_started) return;
        // No global process kill or DROP cleanup: verify server and local postmaster identity first.
        await using (var owner = await OpenOwnerAsync("postgres"))
        {
            var actual = (string)(await Scalar(owner, "SHOW data_directory"))!;
            if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(_data), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to stop a PostgreSQL instance with a different data directory.");
        }
        var pidLines = await File.ReadAllLinesAsync(Path.Combine(_data, "postmaster.pid"));
        if (pidLines.Length < 4 || !string.Equals(Path.GetFullPath(pidLines[1]), _data, StringComparison.OrdinalIgnoreCase)
            || pidLines[3] != Port.ToString() || !File.Exists(Path.Combine(_root, "ownership.txt")))
            throw new InvalidOperationException("Refusing PostgreSQL stop without matching owner and postmaster identity.");
        await RunAsync("pg_ctl", ["-D", _data, "-m", "fast", "-w", "stop"]);
        _started = false;
        await File.WriteAllTextAsync(Path.Combine(_root, "stopped.txt"), "Owned instance stopped; database and failure logs retained.");
    }

    private string Binary(string name) => Path.Combine(_bin, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
    private async Task RunAsync(string executable, string[] arguments)
    {
        // On Windows postgres inherits pg_ctl's redirected pipe handles even with -l. Waiting for
        // ReadToEnd after pg_ctl exits would wait for the server's lifetime, not for startup.
        var capture = executable != "pg_ctl";
        var info = new ProcessStartInfo(Binary(executable))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = capture, RedirectStandardError = capture
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("PostgreSQL fixture tool did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stdout = capture ? process.StandardOutput.ReadToEndAsync(timeout.Token) : Task.FromResult("");
        var stderr = capture ? process.StandardError.ReadToEndAsync(timeout.Token) : Task.FromResult("");
        await process.WaitForExitAsync(timeout.Token);
        await File.AppendAllTextAsync(Path.Combine(_root, executable + ".log"), await stdout + await stderr + $"\nexit={process.ExitCode}\n");
        if (process.ExitCode != 0) throw new InvalidOperationException("PostgreSQL fixture tool failed; see private owner-root log.");
    }
}
