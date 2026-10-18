using System.Diagnostics;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Npgsql;

using CP6.Core.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests;

// Opt-in follows the existing native fixture pattern. An incomplete requested run fails setup.
internal sealed class CurrentPermissionPostgresFactAttribute : FactAttribute
{
    internal const string RootVariable = "CP6_PERMISSION_PG_ROOT";
    internal const string BinVariable = "CP6_PERMISSION_PG_BIN";
    internal static bool Selected => Environment.GetEnvironmentVariable(RootVariable) is not null
        || Environment.GetEnvironmentVariable(BinVariable) is not null;
    public CurrentPermissionPostgresFactAttribute()
    {
        if (!Selected) Skip = "Explicit owned PostgreSQL component run requires CP6_PERMISSION_PG_ROOT and CP6_PERMISSION_PG_BIN.";
    }
}

[CollectionDefinition("CurrentPermissionPostgres", DisableParallelization = true)]
public sealed class CurrentPermissionPostgresCollection : ICollectionFixture<CurrentPermissionPostgresFixture>;

public sealed class CurrentPermissionPostgresFixture : IAsyncLifetime
{
    internal const int Port = 55435;
    internal const string Database = "permission_component";
    private readonly string _ownerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private string _root = "", _bin = "", _data = "";
    private bool _started;

    public async Task InitializeAsync()
    {
        if (!CurrentPermissionPostgresFactAttribute.Selected) return;
        var requestedRoot = Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable);
        _bin = Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.BinVariable) ?? "";
        if (string.IsNullOrWhiteSpace(requestedRoot) || !Path.IsPathFullyQualified(requestedRoot)
            || !Path.IsPathFullyQualified(_bin) || !File.Exists(Binary("initdb")))
            throw new InvalidOperationException("Requested PostgreSQL run requires a new absolute owner root and installed PostgreSQL binaries.");
        _root = Path.GetFullPath(requestedRoot);
        if (!Path.GetFileName(_root).StartsWith("permission-consumption-", StringComparison.Ordinal)
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
                await Sql(admin, $"CREATE DATABASE {Database}");
            }
            await using var connection = await OpenOwnerAsync();
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

    internal async Task<NpgsqlConnection> OpenRawAsync(string username, string password, string database = Database, string searchPath = "pg_catalog")
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

    internal CP6Context Context(Guid tenant, string databaseName, params IInterceptor[] interceptors)
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = Port, Database = databaseName, Username = "fixture_owner", Password = _ownerPassword,
            Pooling = false, SearchPath = "public", IncludeErrorDetail = false
        };
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),
            new DatabaseOptions(DatabaseProvider.PostgreSql), connection.ConnectionString);
        options.AddInterceptors(interceptors);
        return new CP6Context(options.Options, new TenantContext { CurrentTenantId = tenant });
    }

    internal async Task<string> CreateTables(Guid tenant)
    {
        var databaseName = "permission_" + Guid.NewGuid().ToString("N");
        await using var connection = await OpenOwnerAsync();
        await Sql(connection, $"CREATE DATABASE {databaseName}");
        await using var db = Context(tenant, databaseName);
        var model = db.GetService<IDesignTimeModel>().Model;
        Type[] types = [typeof(Sys_User), typeof(Sys_Role), typeof(Sys_UserRole), typeof(Sys_Menu),
            typeof(Sys_RoleMenu), typeof(Sys_RoleAction), typeof(Sys_Dept), typeof(Sys_RoleDataScope), typeof(Sys_RoleFieldPerm),
            typeof(Sys_FieldAuditLog)]; // CP6Context.SaveChanges writes actual field audit rows during seeding/changes.
        var names = types.Select(t => model.FindEntityType(t)!.GetTableName()).ToHashSet();
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel())
            .OfType<CreateTableOperation>().Where(o => names.Contains(o.Name)).Cast<MigrationOperation>().ToArray();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        return databaseName;
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

[Collection("CurrentPermissionPostgres")]
public sealed class CurrentPermissionConsumptionPostgreSqlTests(CurrentPermissionPostgresFixture fixture)
{
    [CurrentPermissionPostgresFact]
    public async Task Concurrent_role_revocation_cannot_create_a_permission_that_never_existed()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var seed = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(seed);
        var actionTable = seed.Model.FindEntityType(typeof(Sys_RoleAction))!.GetTableName()!;
        var sql = seed.GetService<ISqlGenerationHelper>();
        var interleave = new BeforeActionRead(sql.DelimitIdentifier(actionTable), async cancellationToken =>
        {
            await using var writer = fixture.Context(tenant, databaseName);
            (await writer.Sys_Roles.SingleAsync(r => r.RoleId == 1, cancellationToken)).Enable = false;
            writer.Sys_RoleActions.Add(new Sys_RoleAction
            {
                Id = Guid.NewGuid(), RoleId = 1, MenuId = 10, ActionCode = "export"
            });
            // Both changes commit together: export has never belonged to an enabled role.
            await writer.SaveChangesAsync(cancellationToken);
        });
        await using var reader = fixture.Context(tenant, databaseName, interleave);
        var current = CurrentPermissionContextTests.Context(reader,
            CurrentPermissionContextTests.Principal(uid, tenant));

        var snapshot = await current.GetAsync();

        Assert.True(interleave.Executed);
        Assert.DoesNotContain("order:export", snapshot.ActionKeys);
        // Either committed view is valid: the old read-only role or the new disabled role.
        Assert.All(snapshot.ActionKeys, action => Assert.Equal("order:read", action));
        // The next read must open a fresh view rather than retain the prior transaction.
        Assert.Empty((await current.GetAsync()).ActionKeys);
        Assert.Null(reader.Database.CurrentTransaction);
    }

    [CurrentPermissionPostgresFact]
    public async Task Action_scope_and_field_visibility_follow_the_same_committed_view()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var seed = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(seed);
        seed.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { RoleId = 1, ResourceKey = "order", ScopeType = 5 });
        seed.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { RoleId = 1, ResourceKey = "order", FieldName = "Cost", Access = 3 });
        await seed.SaveChangesAsync();
        var fieldTable = seed.Model.FindEntityType(typeof(Sys_RoleFieldPerm))!.GetTableName()!;
        var interleave = new BeforeActionRead(seed.GetService<ISqlGenerationHelper>().DelimitIdentifier(fieldTable), async ct =>
        {
            await using var writer = fixture.Context(tenant, databaseName);
            (await writer.Sys_RoleActions.SingleAsync(ct)).ActionCode = "export";
            (await writer.Sys_RoleDataScopes.SingleAsync(ct)).ScopeType = 1;
            (await writer.Sys_RoleFieldPerms.SingleAsync(ct)).Access = 1;
            await writer.SaveChangesAsync(ct);
        });
        await using var reader = fixture.Context(tenant, databaseName, interleave);
        var snapshot = await new PermissionAggregator(reader).BuildEnabledRolesAsync(uid);
        Assert.True(interleave.Executed);
        if (snapshot.ActionKeys.Contains("order:read"))
        {
            Assert.Equal(5, snapshot.DataScopes["order"]);
            Assert.Equal(3, snapshot.FieldPerms["order"]["Cost"]);
        }
        else
        {
            Assert.Equal(new[] { "order:export" }, snapshot.ActionKeys);
            Assert.Equal(1, snapshot.DataScopes["order"]);
            Assert.Equal(1, snapshot.FieldPerms["order"]["Cost"]);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Caller_transaction_is_not_committed_and_read_committed_refreshes_each_call()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var db = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(db);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        (await db.Sys_Users.SingleAsync()).NickName = "uncommitted caller value";
        await db.SaveChangesAsync();
        var current = CurrentPermissionContextTests.Context(db, CurrentPermissionContextTests.Principal(uid, tenant));
        Assert.Contains("order:read", (await current.PrewarmAsync(uid)).ActionKeys);
        await using (var other = fixture.Context(tenant, databaseName))
        {
            Assert.Null((await other.Sys_Users.AsNoTracking().SingleAsync()).NickName);
            (await other.Sys_Roles.SingleAsync()).Enable = false;
            await other.SaveChangesAsync();
        }
        Assert.Empty((await current.GetAsync()).ActionKeys);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        await transaction.RollbackAsync();
        await using var observer = fixture.Context(tenant, databaseName);
        Assert.Null((await observer.Sys_Users.AsNoTracking().SingleAsync()).NickName);
    }

    [CurrentPermissionPostgresFact]
    public async Task Caller_repeatable_read_keeps_its_snapshot_without_mixing_later_permissions()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var db = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(db);
        var current = CurrentPermissionContextTests.Context(db, CurrentPermissionContextTests.Principal(uid, tenant));
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            Assert.Contains("order:read", (await current.GetAsync()).ActionKeys);
            await using var writer = fixture.Context(tenant, databaseName);
            (await writer.Sys_Roles.SingleAsync()).Enable = false;
            writer.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = 10, ActionCode = "export" });
            await writer.SaveChangesAsync();
            // This is the caller's old coherent snapshot, explicitly not commit-time authority.
            Assert.Equal(new[] { "order:read" }, (await current.GetAsync()).ActionKeys);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            await transaction.RollbackAsync();
        }
        Assert.Empty((await current.GetAsync()).ActionKeys);
    }

    [CurrentPermissionPostgresFact]
    public async Task Stored_text_and_original_aggregation_are_preserved_without_cross_tenant_fields()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var db = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(db);
        var department = Guid.NewGuid();
        const string resource = "资源'\"  ", field = "Cost  ";
        var user = await db.Sys_Users.SingleAsync();
        user.UserName = "用户'\"  "; user.DeptId = department;
        db.Sys_Depts.Add(new Sys_Dept { Id = department, DeptCode = "A", DeptName = "A", Path = "/部门/  " });
        (await db.Sys_Menus.SingleAsync()).MenuKey = "menu  ";
        (await db.Sys_RoleActions.SingleAsync()).ActionCode = "read  ";
        db.Sys_Roles.Add(new Sys_Role { RoleId = 2, RoleName = "additional" });
        db.Sys_UserRoles.AddRange(new Sys_UserRole { UserId = uid, RoleId = 1 }, new Sys_UserRole { UserId = uid, RoleId = 2 });
        db.Sys_RoleDataScopes.AddRange(
            new Sys_RoleDataScope { RoleId = 1, ResourceKey = resource, ScopeType = 4, CustomDeptIds = $"{department},invalid" },
            new Sys_RoleDataScope { RoleId = 2, ResourceKey = resource, ScopeType = 2 });
        db.Sys_RoleFieldPerms.AddRange(
            new Sys_RoleFieldPerm { RoleId = 1, ResourceKey = resource, FieldName = field, Access = 3 },
            new Sys_RoleFieldPerm { RoleId = 2, ResourceKey = resource, FieldName = field, Access = 2 });
        await db.SaveChangesAsync();
        await using (var foreign = fixture.Context(Guid.NewGuid(), databaseName))
        {
            foreign.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "foreign" });
            foreign.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { RoleId = 1, ResourceKey = resource, ScopeType = 5 });
            foreign.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { RoleId = 1, ResourceKey = resource, FieldName = field, Access = 1 });
            await foreign.SaveChangesAsync();
        }
        var actual = await new PermissionAggregator(db).BuildAsync(uid);
        Assert.Equal(await db.Sys_Users.AsNoTracking().Select(x => x.UserName).SingleAsync(), actual.UserName);
        Assert.Equal(await db.Sys_Depts.AsNoTracking().Select(x => x.Path).SingleAsync(), actual.DeptPath);
        Assert.Contains((await db.Sys_Menus.Select(x => x.MenuKey).SingleAsync())!, actual.MenuKeys);
        var originalAction = await (from a in db.Sys_RoleActions
            join m in db.Sys_Menus on a.MenuId equals m.MenuId
            select m.MenuKey + ":" + a.ActionCode).SingleAsync();
        Assert.Equal(new[] { originalAction }, actual.ActionKeys);
        Assert.Equal(new[] { 1, 2 }, actual.RoleIds);
        Assert.Equal(4, actual.DataScopes[resource]);
        Assert.Equal(new[] { department }, actual.CustomDeptIds[resource]);
        Assert.Equal(2, actual.FieldPerms[resource][field]);
    }

    [CurrentPermissionPostgresFact]
    public async Task Request_cancellation_interrupts_a_native_lock_wait_and_releases_the_reader()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var seed = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(seed);
        var table = seed.Model.FindEntityType(typeof(Sys_RoleAction))!.GetTableName()!;
        var quoted = seed.GetService<ISqlGenerationHelper>().DelimitIdentifier(table);
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var locker = await fixture.OpenOwnerAsync(databaseName);
        await using var locked = await locker.BeginTransactionAsync();
        await CurrentPermissionPostgresFixture.Sql(locker, "LOCK TABLE public." + quoted + " IN ACCESS EXCLUSIVE MODE");
        await using var reader = fixture.Context(tenant, databaseName, new CaptureCommand(quoted, started));
        using var cancelled = new CancellationTokenSource();
        var http = new DefaultHttpContext { User = CurrentPermissionContextTests.Principal(uid, tenant), RequestAborted = cancelled.Token };
        var current = new CurrentPermissionContext(new HttpContextAccessor { HttpContext = http },
            CurrentPermissionContextTests.Cache(), reader, new PermissionAggregator(reader));
        var reading = current.GetAsync();
        var released = false;
        try
        {
            var pid = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var waiting = false;
            for (var attempt = 0; attempt < 100 && !waiting; attempt++)
            {
                await using var probe = new NpgsqlCommand("SELECT wait_event_type='Lock' FROM pg_catalog.pg_stat_activity WHERE pid=@pid", locker);
                probe.Parameters.AddWithValue("pid", pid);
                waiting = await probe.ExecuteScalarAsync() is true;
                if (!waiting) await Task.Delay(20);
            }
            Assert.True(waiting, "The actual PostgreSQL permission query must be waiting on the owned relation lock.");
            await cancelled.CancelAsync();
            var finished = await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(3))) == reading;
            await locked.RollbackAsync();
            released = true;
            if (!finished) await reading; // Always release and drain the native query before the assertion.
            Assert.True(finished, "Request cancellation must interrupt the blocked permission read.");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reading);
            Assert.Null(reader.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, reader.Database.GetDbConnection().State);
            http.RequestAborted = CancellationToken.None;
            Assert.Contains("order:read", (await current.GetAsync()).ActionKeys);
        }
        finally
        {
            if (!released) await locked.RollbackAsync();
            try { await reading; } catch (OperationCanceledException) { }
        }
    }

    private sealed class CaptureCommand(string quotedTable, TaskCompletionSource<int> started) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(quotedTable, StringComparison.Ordinal))
                started.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BeforeActionRead(string quotedTable, Func<CancellationToken, Task> change) : DbCommandInterceptor
    {
        private int _executed;
        internal bool Executed => _executed != 0;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(quotedTable, StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _executed, 1, 0) == 0)
                await change(cancellationToken);
            return result;
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Committed_changes_are_observed_despite_tracked_entities_and_old_cache()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var reader = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(reader);
        var dept = new Sys_Dept { Id = Guid.NewGuid(), DeptCode = "A", DeptName = "A", Path = "/old/" };
        reader.Sys_Depts.Add(dept);
        (await reader.Sys_Users.SingleAsync()).DeptId = dept.Id;
        reader.Sys_Roles.Add(new Sys_Role { RoleId = 2, RoleName = "additional" });
        reader.Sys_UserRoles.Add(new Sys_UserRole { UserId = uid, RoleId = 2 });
        reader.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 2, MenuId = 10, ActionCode = "export" });
        reader.Sys_RoleDataScopes.AddRange(
            new Sys_RoleDataScope { RoleId = 1, ResourceKey = "order", ScopeType = 2 },
            new Sys_RoleDataScope { RoleId = 2, ResourceKey = "order", ScopeType = 5 });
        reader.Sys_RoleFieldPerms.AddRange(
            new Sys_RoleFieldPerm { RoleId = 1, ResourceKey = "order", FieldName = "Cost", Access = 3 },
            new Sys_RoleFieldPerm { RoleId = 2, ResourceKey = "order", FieldName = "Cost", Access = 1 });
        await reader.SaveChangesAsync();
        var cache = CurrentPermissionContextTests.Cache();
        var current = CurrentPermissionContextTests.Context(reader, CurrentPermissionContextTests.Principal(uid, tenant), cache);
        var first = await current.PrewarmAsync(uid);
        Assert.Equal(2, first.RoleIds.Count);
        Assert.Equal(5, first.DataScopes["order"]);
        Assert.Equal(1, first.FieldPerms["order"]["Cost"]);
        await Microsoft.Extensions.Caching.Distributed.DistributedCacheExtensions.SetStringAsync(cache, $"perm-ctx:{uid}",
            System.Text.Json.JsonSerializer.Serialize(first, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        async Task Change(Func<CP6Context, Task> change)
        {
            await using var writer = fixture.Context(tenant, databaseName);
            await change(writer);
            await writer.SaveChangesAsync();
        }
        await Change(async db => (await db.Sys_Roles.SingleAsync(r => r.RoleId == 2)).Enable = false);
        var disabled = await current.GetAsync();
        Assert.Equal(new[] { 1 }, disabled.RoleIds);
        Assert.DoesNotContain("order:export", disabled.ActionKeys);
        Assert.Equal(2, disabled.DataScopes["order"]);
        Assert.Equal(3, disabled.FieldPerms["order"]["Cost"]);
        await Change(async db => (await db.Sys_Roles.SingleAsync(r => r.RoleId == 2)).Enable = true);
        Assert.Contains("order:export", (await current.GetAsync()).ActionKeys);
        await Change(async db => db.Remove(await db.Sys_UserRoles.SingleAsync()));
        Assert.DoesNotContain("order:export", (await current.GetAsync()).ActionKeys);
        await Change(async db =>
        {
            (await db.Sys_Depts.SingleAsync()).Path = "/new/";
            (await db.Sys_RoleDataScopes.SingleAsync(r => r.RoleId == 1)).ScopeType = 1;
            (await db.Sys_RoleFieldPerms.SingleAsync(r => r.RoleId == 1)).Access = 2;
        });
        var narrowed = await current.GetAsync();
        Assert.Equal("/new/", narrowed.DeptPath);
        Assert.Equal(1, narrowed.DataScopes["order"]);
        Assert.Equal(2, narrowed.FieldPerms["order"]["Cost"]);
        await Change(async db => db.Remove(await db.Sys_RoleActions.SingleAsync(r => r.RoleId == 1)));
        Assert.Empty((await current.GetAsync()).ActionKeys);
        await Change(async db => db.Remove(await db.Sys_Roles.SingleAsync(r => r.RoleId == 1)));
        Assert.Empty((await current.GetAsync()).RoleIds);
        Assert.Empty((await current.GetAsync()).MenuKeys);
        await Change(async db => (await db.Sys_Users.SingleAsync()).RoleId = 2);
        Assert.Contains("order:export", (await current.GetAsync()).ActionKeys);
        await Change(async db => (await db.Sys_Users.SingleAsync()).Enable = false);
        Assert.False(await new PermissionService(current).HasActionAsync("order", "export"));
        await Change(async db => (await db.Sys_Users.SingleAsync()).Enable = true);
        Assert.True(await new PermissionService(current).HasActionAsync("order", "export"));
        await Change(async db => db.Remove(await db.Sys_Users.SingleAsync()));
        Assert.False(await new PermissionService(current).HasActionAsync("order", "export"));
        // Reader retains its original tracked values throughout; no ChangeTracker.Clear/Reload hid the regression.
        Assert.True(reader.Sys_Users.Local.Single().Enable);
        Assert.Equal(1, reader.Sys_Users.Local.Single().RoleId);
        Assert.Equal("/old/", reader.Sys_Depts.Local.Single().Path);
    }

    [CurrentPermissionPostgresFact]
    public async Task Same_name_and_role_numbers_do_not_cross_tenants_even_with_foreign_user_tracked()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(a);
        await using var dbA = fixture.Context(a, databaseName);
        var uidA = await CurrentPermissionContextTests.Seed(dbA);
        await using var dbB = fixture.Context(b, databaseName);
        var uidB = Guid.NewGuid();
        dbB.Sys_Users.Add(new Sys_User { Id = uidB, UserName = "u", Password = "x", RoleId = 1 });
        dbB.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "B" });
        dbB.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = 10, ActionCode = "write" });
        await dbB.SaveChangesAsync();
        // Simulate an earlier administrative lookup in this DbContext.
        await dbA.Sys_Users.IgnoreQueryFilters().SingleAsync(u => u.Id == uidB);
        var validA = CurrentPermissionContextTests.Context(dbA, CurrentPermissionContextTests.Principal(uidA, a));
        Assert.Equal(new[] { "order:read" }, (await validA.GetAsync()).ActionKeys);
        var validB = CurrentPermissionContextTests.Context(dbB, CurrentPermissionContextTests.Principal(uidB, b));
        Assert.Equal(new[] { "order:write" }, (await validB.GetAsync()).ActionKeys);
        var wrongSubject = CurrentPermissionContextTests.Context(dbA, CurrentPermissionContextTests.Principal(uidB, a));
        Assert.False(await new PermissionService(wrongSubject).HasActionAsync("order", "read"));
        var wrongTenant = CurrentPermissionContextTests.Context(dbA, CurrentPermissionContextTests.Principal(uidA, b));
        Assert.False(await new PermissionService(wrongTenant).HasActionAsync("order", "read"));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => new PermissionAggregator(dbA).BuildAsync(uidB));
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_query_failure_after_allow_is_controlled_403_through_real_filter()
    {
        var tenant = Guid.NewGuid();
        var databaseName = await fixture.CreateTables(tenant);
        await using var db = fixture.Context(tenant, databaseName);
        var uid = await CurrentPermissionContextTests.Seed(db);
        var current = CurrentPermissionContextTests.Context(db, CurrentPermissionContextTests.Principal(uid, tenant));
        Assert.Contains("order:read", (await current.GetAsync()).ActionKeys);
        var table = db.Model.FindEntityType(typeof(Sys_RoleAction))!.GetTableName()!;
        var sql = db.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE " + sql.DelimitIdentifier(table));
        using var provider = new ServiceCollection().AddSingleton<IPermissionService>(new PermissionService(current)).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        var filterContext = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        await new RequirePermissionAttribute("order", "read").OnAuthorizationAsync(filterContext);
        Assert.Equal(403, Assert.IsType<ObjectResult>(filterContext.Result).StatusCode);
        Assert.False(await new PermissionService(current).HasMenuAsync("order"));
    }
}
