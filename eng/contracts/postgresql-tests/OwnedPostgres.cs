using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using Xunit;

namespace CP6.ExactValues.PostgreSqlTests;

/// <summary>Task-local disposable cluster. No external connection string or shared service is accepted.</summary>
public sealed class OwnedPostgres : IAsyncLifetime
{
    private const string Root = "D:/CP6-local-test/S01-10";
    private const string Bin = "C:/Program Files/PostgreSQL/18/bin";
    private const int Port = 55410;
    private const string Bootstrap = "s0110_bootstrap", OwnerRole = "s0110_owner", Writer = "s0110_writer";
    public string Owner { get; } = Guid.NewGuid().ToString("N");
    public string Database { get; }
    public string Marker => "CP6-S01-10:" + Owner;
    public string RunRoot { get; }
    public string Repository { get; } = FindRepository();
    private string Data => Path.Combine(RunRoot, "data");
    private string PasswordFile => Path.Combine(RunRoot, "private", "initdb-password.txt");
    private string LeasePath => Path.Combine(Root, "lease.lock");
    private string bootstrapPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private string writerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private string? systemIdentifier;
    private uint databaseOid;
    private bool leaseOwned, clusterOwned, databaseCreated, stopped;
    private readonly List<object> events = [];
    private readonly Dictionary<string, object?> receipt = [];

    public OwnedPostgres()
    {
        Database = $"cp6_s0110_{DateTime.UtcNow:yyyyMMdd}_{Owner[..8]}_exact";
        RunRoot = Path.GetFullPath(Path.Combine(Root, Owner));
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(Bin, "initdb.exe")))
                throw new InvalidOperationException("RESERVED_POSTGRES_RESOURCE_UNAVAILABLE");
            AssertPath(RunRoot);
            if (Listening()) throw new InvalidOperationException("RESERVED_PORT_ALREADY_IN_USE");
            Directory.CreateDirectory(Root);
            RejectReparsePoints(Root);
            using (var lease = new FileStream(LeasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(lease)) writer.Write(Marker);
            leaseOwned = true;
            if (Directory.Exists(RunRoot)) throw new InvalidOperationException("RUN_PATH_ALREADY_EXISTS");
            foreach (var child in new[] { "private", "logs", "receipts" }) Directory.CreateDirectory(Path.Combine(RunRoot, child));
            File.WriteAllText(Path.Combine(RunRoot, "owner.marker"), Marker);
            receipt["task"] = "S01-10"; receipt["owner"] = Owner; receipt["marker"] = Marker;
            receipt["database"] = Database; receipt["endpoint"] = "127.0.0.1:55410"; receipt["run_root"] = RunRoot;
            receipt["owner_role"] = OwnerRole; receipt["writer_role"] = Writer;
            receipt["npgsql_version"] = typeof(NpgsqlConnection).Assembly.GetName().Version?.ToString();
            Record("PLANNED");
            ProtectPrivateDirectory();
            File.WriteAllText(PasswordFile, bootstrapPassword + Environment.NewLine);
            _ = await Run("initdb.exe", "-D", Data, "--encoding=UTF8", "--no-locale", "--username=" + Bootstrap,
                "--auth=scram-sha-256", "--pwfile=" + PasswordFile);
            File.AppendAllText(Path.Combine(Data, "postgresql.conf"), """

                listen_addresses = '127.0.0.1'
                port = 55410
                password_encryption = 'scram-sha-256'
                logging_collector = off
                log_statement = 'none'
                log_min_error_statement = 'panic'
                log_parameter_max_length_on_error = 0
                log_connections = off
                log_disconnections = off
                """ + Environment.NewLine);
            if (Listening()) throw new InvalidOperationException("RESERVED_PORT_BECAME_BUSY");
            _ = await Run("pg_ctl.exe", "-D", Data, "-l", Path.Combine(RunRoot, "logs", "postgres.log"), "-w", "-t", "30", "start");
            await using (var admin = await Open(Bootstrap, "postgres", bootstrapPassword))
            {
                await VerifyCluster(admin, capture: true);
                clusterOwned = true;
                await AssertDatabaseAbsent(admin, Database);
                // Passwords are generated hex, never arguments, environment, stdout, receipts or SQL logs.
                await Execute(admin, $"CREATE ROLE {OwnerRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;");
                await Execute(admin, $"CREATE ROLE {Writer} LOGIN PASSWORD '{writerPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;");
                await Execute(admin, $"CREATE DATABASE {Database} OWNER {OwnerRole} TEMPLATE template0 ENCODING 'UTF8';");
                databaseCreated = true;
                await Execute(admin, $"COMMENT ON DATABASE {Database} IS '{Marker}'; REVOKE ALL ON DATABASE {Database} FROM PUBLIC; GRANT CONNECT ON DATABASE {Database} TO {Writer};");
            }
            await using (var setup = await Open(Bootstrap, Database, bootstrapPassword))
            {
                await VerifyOwned(setup, capture: true);
                receipt["mapping_path"] = TestInputs.MappingPath;
                receipt["mapping_adoption"] = TestInputs.Mapping.GetProperty("adoption").GetString();
                receipt["mapping_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Repository, TestInputs.MappingPath)))).ToLowerInvariant();
                Record("OWNED_BEFORE_DDL");
                await Execute(setup, $"REVOKE CREATE ON SCHEMA public FROM PUBLIC; CREATE SCHEMA exact_fixture AUTHORIZATION {OwnerRole}; SET ROLE {OwnerRole};");
                var fixtureSql = FixtureSql.Build(Repository);
                receipt["fixture_sql_sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fixtureSql))).ToLowerInvariant();
                await Execute(setup, fixtureSql);
                await Execute(setup, $"RESET ROLE; REVOKE ALL ON ALL FUNCTIONS IN SCHEMA exact_fixture FROM PUBLIC; GRANT USAGE ON SCHEMA exact_fixture TO {Writer}; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA exact_fixture TO {Writer}; GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA exact_fixture TO {Writer};");
            }
            await using (var writer = await OpenWriterAsync()) { }
            Record("READY");
        }
        catch (Exception exception)
        {
            receipt["setup_failure"] = SafeFailure(exception);
            Record("BLOCKED");
            if (clusterOwned)
            {
                try { await DisposeAsync(); }
                catch (Exception cleanup) { receipt["cleanup_failure"] = SafeFailure(cleanup); Record("CLEANUP_BLOCKED"); }
            }
            throw new InvalidOperationException("S01-10 PostgreSQL setup BLOCKED: " + SafeFailure(exception));
        }
    }

    public async Task<NpgsqlConnection> OpenWriterAsync()
    {
        await using (var admin = await Open(Bootstrap, Database, bootstrapPassword)) await VerifyOwned(admin);
        var connection = await Open(Writer, Database, writerPassword);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT current_database(), session_user, current_user, d.oid, pg_get_userbyid(d.datdba),
                  shobj_description(d.oid,'pg_database'), inet_server_addr()::text, inet_server_port(),
                  r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolbypassrls,
                  has_database_privilege(current_user,current_database(),'CREATE'),
                  has_schema_privilege(current_user,'exact_fixture','CREATE'),
                  pg_has_role(current_user,'s0110_owner','MEMBER')
                FROM pg_database d JOIN pg_roles r ON r.rolname=session_user WHERE d.datname=current_database()
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Require(await reader.ReadAsync(), "WRITER_IDENTITY_MISSING");
            Require(reader.GetString(0) == Database && reader.GetString(1) == Writer && reader.GetString(2) == Writer &&
                reader.GetFieldValue<uint>(3) == databaseOid && reader.GetString(4) == OwnerRole && reader.GetString(5) == Marker &&
                reader.GetString(6).Split('/')[0] == "127.0.0.1" && reader.GetInt32(7) == Port &&
                !reader.GetBoolean(8) && !reader.GetBoolean(9) && !reader.GetBoolean(10) && !reader.GetBoolean(11), "WRITER_IDENTITY_OR_PRIVILEGE_MISMATCH");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task AssertNoOtherSessionsAsync()
    {
        await using var admin = await Open(Bootstrap, Database, bootstrapPassword);
        await VerifyOwned(admin);
        await RequireNoSessions(admin);
    }

    public async Task AssertExistingNameRejectedAsync()
    {
        await using var admin = await Open(Bootstrap, "postgres", bootstrapPassword);
        await VerifyCluster(admin);
        await AssertDatabaseAbsent(admin, Database);
    }

    public async Task DisposeAsync()
    {
        if (stopped || !clusterOwned) return;
        CheckLocalMarker();
        await using (var admin = await Open(Bootstrap, databaseCreated ? Database : "postgres", bootstrapPassword))
        {
            if (databaseCreated) await VerifyOwned(admin); else await VerifyCluster(admin);
            await RequireNoSessions(admin);
        }
        if (databaseCreated)
        {
            await using var admin = await Open(Bootstrap, "postgres", bootstrapPassword);
            await VerifyCluster(admin);
            await RequireNoSessions(admin);
            // Re-read the full database identity immediately before normal DROP (never FORCE).
            await VerifyDatabaseIdentity(admin, capture: false);
            await Execute(admin, $"DROP DATABASE {Database};");
            await AssertDatabaseAbsent(admin, Database);
            databaseCreated = false;
            Record("DATABASE_DROPPED");
        }
        CheckLocalMarker();
        var control = await Run("pg_controldata.exe", "-D", Data);
        Require(systemIdentifier is not null && control.Split('\n').Any(line => line.Contains("system identifier", StringComparison.OrdinalIgnoreCase) && line.TrimEnd().EndsWith(systemIdentifier, StringComparison.Ordinal)), "CLUSTER_CONTROL_IDENTITY_MISMATCH");
        _ = await Run("pg_ctl.exe", "-D", Data, "-w", "-t", "30", "-m", "smart", "stop");
        Require(!Listening(), "RESERVED_PORT_STILL_LISTENING");
        stopped = true;
        AssertPath(PasswordFile); RejectReparsePoints(PasswordFile);
        File.Delete(PasswordFile);
        bootstrapPassword = ""; writerPassword = "";
        Require(leaseOwned && File.ReadAllText(LeasePath) == Marker, "LEASE_OWNER_MISMATCH");
        File.Delete(LeasePath);
        leaseOwned = false;
        receipt["password_file_removed"] = true;
        receipt["retained_resource"] = "Stopped owned cluster and local logs; no recursive deletion; no business data";
        Record("CLEANED");
    }

    private async Task VerifyOwned(NpgsqlConnection connection, bool capture = false)
    {
        await VerifyCluster(connection);
        await VerifyDatabaseIdentity(connection, capture);
    }

    private async Task VerifyCluster(NpgsqlConnection connection, bool capture = false)
    {
        CheckLocalMarker();
        await using var command = new NpgsqlCommand("SELECT current_setting('data_directory'), system_identifier::text, inet_server_addr()::text, inet_server_port(), session_user, current_user, current_setting('server_version'), current_setting('password_encryption') FROM pg_control_system()", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "CLUSTER_IDENTITY_MISSING");
        Require(Path.GetFullPath(reader.GetString(0)).Equals(Path.GetFullPath(Data), StringComparison.OrdinalIgnoreCase) &&
            reader.GetString(2).Split('/')[0] == "127.0.0.1" && reader.GetInt32(3) == Port && reader.GetString(4) == Bootstrap &&
            reader.GetString(5) == Bootstrap && reader.GetString(7) == "scram-sha-256", "CLUSTER_IDENTITY_MISMATCH");
        if (capture) systemIdentifier = reader.GetString(1);
        Require(systemIdentifier == reader.GetString(1), "CLUSTER_SYSTEM_IDENTIFIER_MISMATCH");
        receipt["system_identifier"] = systemIdentifier; receipt["server_version"] = reader.GetString(6);
        receipt["data_directory"] = reader.GetString(0);
    }

    private async Task VerifyDatabaseIdentity(NpgsqlConnection connection, bool capture)
    {
        await using var command = new NpgsqlCommand("""
            SELECT d.oid, pg_get_userbyid(d.datdba), shobj_description(d.oid,'pg_database'),
              r.rolcanlogin, r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolbypassrls
            FROM pg_database d JOIN pg_roles r ON r.oid=d.datdba WHERE d.datname=@name
            """, connection);
        command.Parameters.AddWithValue("name", Database);
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "OWNED_DATABASE_MISSING");
        if (capture) databaseOid = reader.GetFieldValue<uint>(0);
        ValidateDatabaseSnapshot(databaseOid, reader.GetFieldValue<uint>(0), Marker, reader.GetString(2),
            reader.GetString(1), reader.GetBoolean(3), reader.GetBoolean(4));
        receipt["database_oid"] = databaseOid;
    }

    internal static void ValidateDatabaseSnapshot(uint expectedOid, uint actualOid, string expectedMarker, string actualMarker,
        string actualOwner, bool ownerLogin, bool ownerElevated)
    {
        Require(expectedOid != 0 && expectedOid == actualOid && expectedMarker.StartsWith("CP6-S01-10:", StringComparison.Ordinal) &&
            expectedMarker == actualMarker && actualOwner == OwnerRole && !ownerLogin && !ownerElevated, "DATABASE_OWNER_IDENTITY_MISMATCH");
    }

    private static async Task RequireNoSessions(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE backend_type='client backend' AND pid<>pg_backend_pid()", connection);
        Require((long)(await command.ExecuteScalarAsync())! == 0, "OWNED_CLUSTER_HAS_OTHER_SESSIONS");
    }

    private static async Task AssertDatabaseAbsent(NpgsqlConnection connection, string name)
    {
        Require(Regex.IsMatch(name, @"\Acp6_s0110_[0-9]{8}_[a-f0-9]{8}_exact\z"), "DATABASE_NAME_INVALID");
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname=@name)", connection);
        command.Parameters.AddWithValue("name", name);
        Require(!(bool)(await command.ExecuteScalarAsync())!, "DATABASE_ALREADY_EXISTS");
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<NpgsqlConnection> Open(string user, string database, string password)
    {
        var options = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = Port, Database = database,
            Username = user, Password = password, Pooling = false, Multiplexing = false, Timeout = 10,
            CommandTimeout = 15, IncludeErrorDetail = false, ApplicationName = "CP6-S01-10", SearchPath = "pg_catalog" };
        var connection = new NpgsqlConnection(options.ConnectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private void ProtectPrivateDirectory()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WINDOWS_PRIVATE_ACL_REQUIRED");
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("CURRENT_USER_SID_UNAVAILABLE");
        var security = new DirectorySecurity();
        // Updating the DACL needs the existing owner's WRITE_DAC; do not request WRITE_OWNER.
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(Path.Combine(RunRoot, "private"));
        directory.SetAccessControl(security);
        var actual = directory.GetAccessControl();
        Require(actual.AreAccessRulesProtected, "PRIVATE_ACL_NOT_EXCLUSIVE");
        foreach (FileSystemAccessRule rule in actual.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            Require(rule.IdentityReference == sid, "PRIVATE_ACL_NOT_EXCLUSIVE");
        receipt["private_acl_current_user_only"] = true;
    }

    private void CheckLocalMarker()
    {
        AssertPath(RunRoot); RejectReparsePoints(RunRoot); RejectReparsePoints(Data);
        Require(File.ReadAllText(Path.Combine(RunRoot, "owner.marker")) == Marker &&
            leaseOwned && File.ReadAllText(LeasePath) == Marker, "LOCAL_OWNER_MARKER_MISMATCH");
    }

    internal static void AssertPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Require(full.StartsWith(root, StringComparison.OrdinalIgnoreCase), "PATH_OUTSIDE_RESERVED_ROOT");
    }

    private static void RejectReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if (File.Exists(current) || Directory.Exists(current))
                Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "REPARSE_POINT_REFUSED");
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool Listening() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == Port);

    private async Task<string> Run(string executable, params string[] arguments)
    {
        // A daemon can inherit redirected pipe handles after pg_ctl exits. Its starter must not
        // wait for EOF from those pipes. ShellExecute launches the EXE directly in a hidden window;
        // PostgreSQL's own output goes to the explicit -l file. No command shell or credentials.
        var startsDaemon = executable == "pg_ctl.exe" && arguments.LastOrDefault() == "start";
        var start = new ProcessStartInfo(Path.Combine(Bin, executable)) { UseShellExecute = startsDaemon,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !startsDaemon, RedirectStandardError = !startsDaemon };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("POSTGRES_PROCESS_START_FAILED");
        if (startsDaemon)
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            File.WriteAllText(Path.Combine(RunRoot, "logs", executable + "-start.log"), "Hidden pg_ctl start exit code: " + process.ExitCode);
            Require(process.ExitCode == 0, "POSTGRES_START_FAILED");
            return "";
        }
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        var output = (await stdout) + (await stderr);
        Require(!output.Contains(bootstrapPassword, StringComparison.Ordinal) && !output.Contains(writerPassword, StringComparison.Ordinal), "SECRET_IN_PROCESS_OUTPUT");
        File.WriteAllText(Path.Combine(RunRoot, "logs", executable + "-" + events.Count + ".log"), output);
        if (process.ExitCode != 0) throw new InvalidOperationException("POSTGRES_PROCESS_FAILED:" + executable + ":" + process.ExitCode);
        return output;
    }

    private void Record(string status)
    {
        receipt["status"] = status;
        events.Add(new { utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), status }); receipt["events"] = events;
        if (!Directory.Exists(Path.Combine(RunRoot, "receipts"))) return;
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(RunRoot, "receipts", "lifecycle.json"), json);
        var evidence = Path.Combine(Repository, "docs/development/evidence/S01-10/lifecycle"); Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, Owner + ".json"), json);
    }

    private static string SafeFailure(Exception exception) => exception is PostgresException pg
        ? $"PostgreSQL:{pg.SqlState}:position={pg.Position}:routine={pg.Routine}" : exception is InvalidOperationException
        ? exception.Message : exception.GetType().Name;
    private static void Require(bool condition, string code) { if (!condition) throw new InvalidOperationException(code); }
    private static string FindRepository()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(current.FullName, "eng/contracts/s01"))) return current.FullName;
        throw new InvalidOperationException("REPOSITORY_ROOT_NOT_FOUND");
    }
}
