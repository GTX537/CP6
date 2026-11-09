using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class StockCapacity
{
    private const string DatabaseName = "tenant_management_component";
    private const string PoolName = "cp6-tenant-storage-v1";
    private const int MaxConnections = 160;
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly DatabaseOptions PostgreSql = new(DatabaseProvider.PostgreSql);
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static string RunRoot => Required("CP6_CAPACITY_RUN");
    private static string Binaries => Required("CP6_CAPACITY_BINARIES");
    private static int Port => int.Parse(Required("CP6_PERMISSION_PG_PORT"), CultureInfo.InvariantCulture);

    private sealed record Login(Guid Tenant, Guid StockId, decimal Quantity, string Role, string Password);
    private sealed record WorkerInput(string Owner, Login[] Tenants);
    private sealed record PoolView(int Pools, int Reserved, int Borrowers, int Draining);
    private sealed record Backend(int Pid, string Login, string State);
    private sealed record Physical(int AllClientBackends, Backend[] Ordinary);
    private sealed record Sample(DateTimeOffset At, PoolView Pool, int AllClientBackends, int Ordinary);
    private sealed record Signal(int Pid, double BeforeQueryMs);
    private sealed record Read(string Phase, int Tenant, DateTimeOffset At, int? Pid, double? BeforeQueryMs, double TotalMs, string? Error);
    private sealed record Ready(int ProcessId, int[] HeldPids, PoolView Pool, int Models, int Entities, long WorkingSetBytes);
    private sealed record CliResult(int Exit, string Output, string Error);

    public static async Task<int> Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var file = Path.Combine(Binaries, name.Name + ".dll");
            return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
        };
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            if (args.SequenceEqual(new[] { "--worker" }))
                await RunWorker(timeout.Token);
            else
            {
                Need(args.Length == 2 && (args[0] is "10" or "100") && (args[1] is "1" or "2"), "Expected tenant count and process count.");
                await RunProfile(int.Parse(args[0]), int.Parse(args[1]), timeout.Token);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}\n{error.StackTrace}");
            return 1;
        }
    }

    private static async Task RunProfile(int count, int processes, CancellationToken ct)
    {
        var root = Path.GetFullPath(Required("CP6_PERMISSION_PG_ROOT"));
        Need(Path.GetDirectoryName(root) == Path.GetFullPath(RunRoot) && !Directory.Exists(root)
            && Path.GetFileName(root).StartsWith("permission-consumption-", StringComparison.Ordinal), "A new run-owned fixture is required.");
        var fixtureType = Type(Load("CP6.Tests"), "CP6.Tests.Platform.TenantManagementPostgresFixture");
        var fixture = Activator.CreateInstance(fixtureType)!;
        var tenants = Enumerable.Range(0, count).Select(NewLogin).ToArray();
        var evidence = new Dictionary<string, object?>
        {
            ["tenants"] = count, ["requestedProcesses"] = processes, ["processes"] = 1, ["port"] = Port, ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["cpuCount"] = Environment.ProcessorCount, ["npgsql"] = typeof(NpgsqlConnection).Assembly.GetName().Version?.ToString(),
            ["efCore"] = typeof(DbContext).Assembly.GetName().Version?.ToString(),
            ["poolLimits"] = new { Pools = 32, Reserved = 64, ConnectionsPerPool = 2 }
        };

        try
        {
            await (Task)Invoke(fixtureType.GetMethod("InitializeAsync", Members)!, fixture)!;
            string owner;
            await using (var db = FixtureContext(fixture, tenants[0].Tenant))
                owner = db.Database.GetConnectionString()!;
            CheckTarget(owner);
            await RaiseOwnedCapacity(root, ct);
            await using var control = new NpgsqlConnection(owner);
            await control.OpenAsync(ct);
            var dbLimit = Convert.ToInt32(await Scalar(control, "SHOW max_connections", ct));
            Need(dbLimit == MaxConnections, "The owned cluster did not start at the requested max_connections.");
            evidence["maxConnections"] = dbLimit;
            evidence["postgres"] = await Scalar(control, "SHOW server_version", ct);
            evidence["reservedConnections"] = await Scalar(control, "SHOW superuser_reserved_connections", ct);
            evidence["sharedBuffers"] = await Scalar(control, "SHOW shared_buffers", ct);
            Console.WriteLine($"Owned PostgreSQL migrated for N={count}, max_connections={dbLimit}.");

            var oid = (uint)(await Scalar(control, "SELECT oid FROM pg_catalog.pg_database WHERE datname=pg_catalog.current_database()", ct))!;
            var system = (string)(await Scalar(control, "SELECT system_identifier::text FROM pg_catalog.pg_control_system()", ct))!;
            if (count == 10) evidence["admissionBoundary"] = await AdmissionBoundary(owner, oid, system, ct);

            await Seed(fixture, control, tenants, ct);
            var registration = CliInput(owner, oid, system, tenants);
            var installed = await RunCli("install-stock-read-v1", registration, ct);
            Need(installed.Exit == 0, "Official Stock install rejected the profile: " + installed.Error);
            var inspected = await RunCli("inspect-stock-read-v1", registration, ct);
            Need(inspected.Exit == 0, "Official Stock inspect rejected the profile: " + inspected.Error);
            var installJson = JsonSerializer.Deserialize<JsonElement>(installed.Output);
            var inspectJson = JsonSerializer.Deserialize<JsonElement>(inspected.Output);
            evidence["install"] = installJson;
            evidence["inspect"] = inspectJson;
            Need(inspectJson.GetProperty("Outcome").GetString() == "VERIFIED"
                && inspectJson.GetProperty("InstallationId").GetGuid() == installJson.GetProperty("InstallationId").GetGuid(),
                "The inspection did not recover the installed identity.");
            evidence["directory"] = await Catalog(control, tenants, ct);
            Console.WriteLine($"Official install/inspect verified {count} ordinary roles on the owned database.");

            var input = new WorkerInput(owner, tenants);
            await using (var runner = new Runner(input))
                await MeasureProfile(input, processes, runner, control, evidence, ct);
            var afterDrain = await PhysicalPools(control, ct);
            Need(afterDrain.Ordinary.Length == 0, "Ordinary backends remained after process-local pool disposal.");
            evidence["afterDrain"] = afterDrain;
            Console.WriteLine($"N={count}, P={evidence["processes"]} completed with physical sessions, Stock models and timing samples.");
        }
        finally
        {
            try { await (Task)Invoke(fixtureType.GetMethod("DisposeAsync", Members)!, fixture)!; }
            finally { await File.WriteAllTextAsync(Path.Combine(RunRoot, "observations.json"), JsonSerializer.Serialize(evidence, Pretty)); }
        }
    }

    private static async Task<object> AdmissionBoundary(string owner, uint oid, string system, CancellationToken ct)
    {
        var refused = new List<object>();
        foreach (var count in new[] { 129, 1000 })
        {
            var excessive = Enumerable.Range(0, count).Select(NewLogin).ToArray();
            var input = CliInput(owner, oid, system, excessive);
            var result = await RunCli("install-stock-read-v1", input, ct);
            Need(result.Exit == 1 && JsonSerializer.Deserialize<JsonElement>(result.Error).GetProperty("Outcome").GetString() == "NOT_APPLIED",
                "Oversized CLI input was not rejected.");
            var hostRejected = false;
            try { await using var services = Host(new(owner, excessive)); }
            catch (InvalidOperationException e) when (e.Message == "STOCK_READ_TENANCY_CONFIGURATION_REJECTED") { hostRejected = true; }
            Need(hostRejected, "The normal host accepted an oversized tenant directory.");
            refused.Add(new { Tenants = count, InputChars = input.Length, result.Exit, result.Error, HostRejected = hostRejected });
        }
        return refused;
    }

    private static async Task MeasureProfile(WorkerInput input, int processes, Runner runner, NpgsqlConnection control,
        Dictionary<string, object?> evidence, CancellationToken ct)
    {
        var count = input.Tenants.Length;
        var samples = new ConcurrentQueue<Sample>();
        using var stopSample = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sampler = SampleDuring(input.Owner, runner, samples, stopSample.Token);
        try
        {
            evidence["memoryBefore"] = Memory();
            for (var i = 0; i < count; i++)
            {
                var first = await runner.ReadTenant("cold-login", i, ct);
                var warm = await runner.ReadTenant("warm-login", i, ct);
                RequireSuccess(first);
                RequireSuccess(warm);
                Need(first.Pid == warm.Pid, "A retained warm login did not reuse its physical backend.");
            }
            evidence["afterFirstSweep"] = new { Manager = runner.Pools, Physical = await PhysicalPools(control, ct) };
            for (var i = 0; i < count; i++)
                RequireSuccess(await runner.ReadTenant("full-sweep", i, ct));
            Need(runner.Pools.Pools == Math.Min(count, 32), "The retained pool count did not match the Stock budget.");
            evidence["afterFullSweep"] = new { Manager = runner.Pools, Physical = await PhysicalPools(control, ct) };
            evidence["loginWait"] = await MeasureLoginWait(runner, control, count - 1, ct);
            if (count == 100)
                evidence["poolRejection"] = await MeasurePoolRejection(runner, control, ct);
            if (processes == 2)
            {
                const ulong minimum = 1100UL * 1024 * 1024;
                var available = AvailablePhysicalMemory();
                evidence["twoProcessMemory"] = new { AvailableBytes = available, MinimumBytes = minimum };
                if (available >= minimum)
                {
                    evidence["twoProcesses"] = await MeasureTwoProcesses(input, runner, control, ct);
                    evidence["processes"] = 2;
                }
                else
                {
                    evidence["twoProcesses"] = "NOT_RUN_MEMORY";
                    Console.WriteLine("The second process was deferred at the measured host memory boundary.");
                }
            }
            evidence["memoryAfter"] = Memory();
            Need(runner.Models == 1, "Stock reads used more than one runtime model in the same process.");
        }
        finally
        {
            stopSample.Cancel();
            await sampler;
            evidence["requests"] = runner.Reads.ToArray();
            evidence["models"] = new { Count = runner.Models, Entities = runner.Entities };
            evidence["physicalSamples"] = samples.ToArray();
            evidence["timings"] = runner.Reads.GroupBy(x => x.Phase).Select(g => new
            {
                Phase = g.Key, Requests = g.Count(), Errors = g.Count(r => r.Error is not null),
                P50Ms = Percentile(g.Select(r => r.TotalMs), .50),
                P95Ms = Percentile(g.Select(r => r.TotalMs), .95),
                BeforeQueryP95Ms = Percentile(g.Where(r => r.BeforeQueryMs.HasValue).Select(r => r.BeforeQueryMs!.Value), .95)
            }).ToArray();
        }
    }

    private static async Task Seed(object fixture, NpgsqlConnection control, Login[] tenants, CancellationToken ct)
    {
        await using (var db = FixtureContext(fixture, tenants[0].Tenant))
        {
            db.Sys_Tenants.AddRange(tenants.Select(t => new Sys_Tenant
                { Id = t.Tenant, TenantCode = t.Tenant.ToString("N"), TenantName = "Stock capacity", Enable = true }));
            await db.SaveChangesAsync(ct);
        }
        foreach (var tenant in tenants)
        {
            await using var db = FixtureContext(fixture, tenant.Tenant);
            db.Stocks.Add(new Stock
            {
                Id = tenant.StockId, WarehouseCd = "W01", LocationCd = "L1", ProductCd = "CAPACITY", LotNo = "LOT",
                PhysicalQty = tenant.Quantity, AvailableQty = tenant.Quantity
            });
            await db.SaveChangesAsync(ct);
            // Role names and hex passwords are generated here. Credentials are passed on redirected stdin.
            await using var create = new NpgsqlCommand($"CREATE ROLE {tenant.Role} LOGIN PASSWORD '{tenant.Password}'", control);
            await create.ExecuteNonQueryAsync(ct);
        }
    }

    private sealed class Runner(WorkerInput input) : IAsyncDisposable
    {
        private readonly ServiceProvider _services = Host(input);
        private readonly DbContextOptions<CP6Context> _authority = DatabaseContextOptions.Configure(
            new DbContextOptionsBuilder<CP6Context>(), PostgreSql, input.Owner).Options;
        private readonly HashSet<IModel> _models = new(ReferenceEqualityComparer.Instance);
        private readonly object _modelLock = new();
        internal ConcurrentQueue<Read> Reads { get; } = new();
        internal int Models { get { lock (_modelLock) return _models.Count; } }
        internal int Entities { get { lock (_modelLock) return _models.FirstOrDefault()?.GetEntityTypes().Count() ?? 0; } }
        private object Store => _services.GetRequiredService(Type(typeof(CP6Context).Assembly, "CP6.Core.Persistence.Tenancy.TenantStockReadStore"));

        internal PoolView Pools
        {
            get
            {
                var store = Store;
                var manager = store.GetType().GetField("_connections", Members)!.GetValue(store)!;
                var (pools, reserved, borrowers, draining) =
                    ((int, int, int, int))Invoke(manager.GetType().GetMethod("Snapshot", Members)!, manager)!;
                return new(pools, reserved, borrowers, draining);
            }
        }

        internal async Task<Read> ReadTenant(string phase, int index, CancellationToken ct, Hold? hold = null)
        {
            var tenant = input.Tenants[index];
            var accessor = _services.GetRequiredService<IHttpContextAccessor>();
            var actor = Guid.NewGuid();
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("jti", Guid.NewGuid().ToString("D")),
                    new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
                }, "owned-capacity-fixture"))
            };
            var at = DateTimeOffset.UtcNow;
            var start = Stopwatch.GetTimestamp();
            Signal? signal = null;
            string? failure = null;
            try
            {
                await using var authority = new CP6Context(_authority, new TenantContext { CurrentTenantId = tenant.Tenant });
                await using var tx = await authority.Database.BeginTransactionAsync(ct);
                var beforeStore = Stopwatch.GetTimestamp();
                Func<CP6Context, CancellationToken, Task<Signal>> measure = async (db, token) =>
                {
                    var beforeQuery = Stopwatch.GetElapsedTime(beforeStore).TotalMilliseconds;
                    var stock = await db.Stocks.AsNoTracking().Where(s => s.ProductCd == "CAPACITY")
                        .Select(s => new { s.Id, s.TenantId, s.PhysicalQty }).SingleAsync(token);
                    Need(stock.Id == tenant.StockId && stock.TenantId == tenant.Tenant && stock.PhysicalQty == tenant.Quantity,
                        "An ordinary Stock query returned a different tenant's original.");
                    lock (_modelLock) _models.Add(db.Model);
                    var pid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
                    if (hold is not null) await hold.Pause(pid, token);
                    return new Signal(pid, beforeQuery);
                };
                var store = Store;
                var method = store.GetType().GetMethod("ReadAsync", Members)!.MakeGenericMethod(typeof(Signal));
                var entry = Enum.Parse(Type(typeof(CP6Context).Assembly, "CP6.Core.Persistence.Tenancy.StockReadEntry"), "Query");
                signal = await (Task<Signal>)Invoke(method, store, authority, actor, measure, ct, false, entry)!;
                await tx.CommitAsync(ct);
            }
            catch (Exception e) when (e.GetType().FullName == "CP6.Core.Persistence.Tenancy.TenantStorageException")
            { failure = e.GetType().GetProperty("Failure")!.GetValue(e)!.ToString(); }
            finally { accessor.HttpContext = null; }

            var reading = new Read(phase, index, at, signal?.Pid, signal?.BeforeQueryMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds, failure);
            Reads.Enqueue(reading);
            return reading;
        }

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }

    private sealed class Hold(int target)
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<int> _pids = new();
        private int _entered;
        internal int[] Pids => _pids.ToArray();

        internal async Task Pause(int pid, CancellationToken ct)
        {
            _pids.Enqueue(pid);
            if (Interlocked.Increment(ref _entered) == target) _ready.TrySetResult();
            await _release.Task.WaitAsync(ct);
        }

        internal async Task Ready(Task<Read>[] running, CancellationToken ct)
        {
            var first = await Task.WhenAny(running.Cast<Task>().Append(_ready.Task)).WaitAsync(TimeSpan.FromSeconds(45), ct);
            if (first != _ready.Task)
            {
                await first;
                throw new InvalidOperationException("A Stock reader finished before reaching the held transaction.");
            }
        }

        internal void Release() => _release.TrySetResult();
    }

    private static async Task<object> MeasureLoginWait(Runner runner, NpgsqlConnection control, int tenant, CancellationToken ct)
    {
        var hold = new Hold(2);
        var holders = new[] { runner.ReadTenant("login-holder", tenant, ct, hold), runner.ReadTenant("login-holder", tenant, ct, hold) };
        Task<Read>? third = null;
        PoolView? waiting = null;
        Physical? physical = null;
        try
        {
            await hold.Ready(holders, ct);
            third = runner.ReadTenant("login-wait", tenant, ct);
            for (var i = 0; i < 40 && !third.IsCompleted && runner.Pools.Borrowers != 3; i++)
                await Task.Delay(25, ct);
            waiting = runner.Pools;
            physical = await PhysicalPools(control, ct);
            Need(!third.IsCompleted && waiting.Borrowers == 3 && hold.Pids.Distinct().Count() == 2
                && hold.Pids.All(p => physical.Ordinary.Any(b => b.Pid == p)), "A third borrower did not wait behind two real sessions.");
            await Task.Delay(200, ct);
            Need(!third.IsCompleted, "A third borrower passed the pool before a connection was returned.");
        }
        finally { hold.Release(); }
        foreach (var read in await Task.WhenAll(holders)) RequireSuccess(read);
        var completed = await third!;
        RequireSuccess(completed);
        Need(hold.Pids.Contains(completed.Pid!.Value), "The waiting borrower did not reuse a returned physical connection.");
        return new { Waiting = waiting, Physical = physical, HeldPids = hold.Pids, Read = completed, ControlledHoldMs = 200 };
    }

    private static async Task<object> MeasurePoolRejection(Runner runner, NpgsqlConnection control, CancellationToken ct)
    {
        var hold = new Hold(32);
        var holders = Enumerable.Range(0, 32).Select(i => runner.ReadTenant("capacity-holder", i, ct, hold)).ToArray();
        PoolView? full = null;
        Physical? physical = null;
        Read? rejected = null;
        try
        {
            await hold.Ready(holders, ct);
            full = runner.Pools;
            physical = await PhysicalPools(control, ct);
            Need(full == new PoolView(32, 64, 32, 0) && hold.Pids.Distinct().Count() == 32
                && hold.Pids.All(p => physical.Ordinary.Any(b => b.Pid == p)), "The real 32-pool boundary was not held.");
            rejected = await runner.ReadTenant("capacity-overflow", 32, ct);
            Need(rejected.Error == "CapacityExceeded" && rejected.Pid is null, "The extra tenant did not fail at the application pool limit.");
        }
        finally { hold.Release(); }
        foreach (var read in await Task.WhenAll(holders)) RequireSuccess(read);
        var retried = await runner.ReadTenant("capacity-after-drain", 32, ct);
        RequireSuccess(retried);
        return new { Full = full, Physical = physical, Rejected = rejected, AfterRelease = retried };
    }

    private static async Task<object> MeasureTwoProcesses(WorkerInput input, Runner runner, NpgsqlConnection control, CancellationToken ct)
    {
        using var child = StartDotnet(ExecutionArgs().Append(typeof(StockCapacity).Assembly.Location).Append("--worker"));
        var errors = child.StandardError.ReadToEndAsync(ct);
        try
        {
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(input).AsMemory(), ct);
            await child.StandardInput.FlushAsync(ct);
            var cohort = Cohort(input);
            foreach (var i in cohort) RequireSuccess(await runner.ReadTenant("p2-warm", i, ct));
            var concurrency = cohort.Length;
            var hold = new Hold(concurrency);
            var reads = cohort.Select(i => runner.ReadTenant("p2-held", i, ct, hold)).ToArray();
            Ready other;
            Physical physical;
            Ready local;
            try
            {
                await hold.Ready(reads, ct);
                var line = await child.StandardOutput.ReadLineAsync(ct);
                Need(!string.IsNullOrEmpty(line), "The second measurement process ended before readiness.");
                other = JsonSerializer.Deserialize<Ready>(line!)!;
                local = Readiness(runner, hold);
                physical = await PhysicalPools(control, ct);
                var allHeld = local.HeldPids.Concat(other.HeldPids).ToArray();
                Need(local.ProcessId != other.ProcessId && other.Models == 1 && other.HeldPids.Length == concurrency
                    && allHeld.Distinct().Count() == 2 * concurrency
                    && allHeld.All(p => physical.Ordinary.Any(b => b.Pid == p)), "Independent processes did not hold the declared ordinary work together.");
            }
            finally
            {
                hold.Release();
                if (!child.HasExited)
                {
                    await child.StandardInput.WriteLineAsync("release".AsMemory(), ct);
                    child.StandardInput.Close();
                }
            }
            foreach (var read in await Task.WhenAll(reads)) RequireSuccess(read);
            var observations = await child.StandardOutput.ReadToEndAsync(ct);
            await child.WaitForExitAsync(ct);
            var stderr = await errors;
            Need(child.ExitCode == 0, "The second measurement process failed: " + stderr);
            return new { Local = local, Other = other, Physical = physical, ConcurrentPerProcess = concurrency,
                ChildReads = JsonSerializer.Deserialize<JsonElement>(observations) };
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
    }

    private static async Task RunWorker(CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<WorkerInput>((await Console.In.ReadLineAsync(ct))!)!;
        CheckTarget(input.Owner);
        await using var runner = new Runner(input);
        var cohort = Cohort(input);
        foreach (var i in cohort) RequireSuccess(await runner.ReadTenant("p2-warm", i, ct));
        var concurrency = cohort.Length;
        var hold = new Hold(concurrency);
        var reads = cohort.Select(i => runner.ReadTenant("p2-held", i, ct, hold)).ToArray();
        try
        {
            await hold.Ready(reads, ct);
            Console.WriteLine(JsonSerializer.Serialize(Readiness(runner, hold)));
            Need(await Console.In.ReadLineAsync(ct) == "release", "The coordinator did not release the second process.");
        }
        finally { hold.Release(); }
        foreach (var read in await Task.WhenAll(reads)) RequireSuccess(read);
        Console.WriteLine(JsonSerializer.Serialize(runner.Reads.ToArray()));
    }

    private static int[] Cohort(WorkerInput input) => Enumerable.Range(0, input.Tenants.Length).TakeLast(input.Tenants.Length == 10 ? 6 : 8).ToArray();

    private static Ready Readiness(Runner runner, Hold hold)
    {
        using var process = Process.GetCurrentProcess();
        return new(Environment.ProcessId, hold.Pids, runner.Pools, runner.Models, runner.Entities, process.WorkingSet64);
    }

    private static ServiceProvider Host(WorkerInput input)
    {
        var settings = new Dictionary<string, string?>
        {
            ["StockReadTenancy:Enabled"] = "true", ["StockReadTenancy:Revision"] = "owned-capacity-v1", ["StockReadTenancy:SslMode"] = "Disable"
        };
        for (var i = 0; i < input.Tenants.Length; i++)
        {
            var login = input.Tenants[i];
            var key = $"StockReadTenancy:Tenants:{i}:";
            settings[key + "TenantId"] = login.Tenant.ToString("D");
            settings[key + "Login"] = login.Role;
            settings[key + "Password"] = login.Password;
            settings[key + "CredentialVersion"] = "owned-v1";
        }
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var registration = Type(Load("CP6.WebApi"), "CP6.WebApi.Configuration.StockReadTenancyConfiguration");
        Invoke(registration.GetMethod("Add", Members)!, null, services, configuration, PostgreSql, input.Owner);
        return services.BuildServiceProvider();
    }

    private static async Task SampleDuring(string owner, Runner runner, ConcurrentQueue<Sample> samples, CancellationToken ct)
    {
        try
        {
            await using var observer = new NpgsqlConnection(owner);
            await observer.OpenAsync(ct);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(ct))
            {
                var physical = await PhysicalPools(observer, ct);
                samples.Enqueue(new(DateTimeOffset.UtcNow, runner.Pools, physical.AllClientBackends, physical.Ordinary.Length));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static async Task<Physical> PhysicalPools(NpgsqlConnection observer, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pid, COALESCE(usename::text,''), COALESCE(application_name,''), COALESCE(state,'')
            FROM pg_catalog.pg_stat_activity
            WHERE datname=pg_catalog.current_database() AND backend_type='client backend'
            ORDER BY pid
            """, observer);
        await using var rows = await command.ExecuteReaderAsync(ct);
        var backends = new List<Backend>();
        var total = 0;
        while (await rows.ReadAsync(ct))
        {
            total++;
            if (rows.GetString(2) == PoolName)
                backends.Add(new(rows.GetInt32(0), rows.GetString(1), rows.GetString(3)));
        }
        return new(total, backends.ToArray());
    }

    private static async Task<JsonElement> Catalog(NpgsqlConnection observer, Login[] tenants, CancellationToken ct)
    {
        var json = (string)(await Scalar(observer, """
            SELECT pg_catalog.jsonb_build_object(
                'bindings', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'tenant', d.tenant_id, 'oid', d.role_oid::bigint, 'login', d.role_name,
                    'ordinary', r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication))
                    ORDER BY d.role_name)
                    FROM cp6_tenancy_v1.login_tenant d JOIN pg_catalog.pg_roles r ON r.oid=d.role_oid AND r.rolname=d.role_name),
                'directoryBytes', pg_catalog.pg_total_relation_size('cp6_tenancy_v1.login_tenant'),
                'databaseBytes', pg_catalog.pg_database_size(pg_catalog.current_database()),
                'objects', (SELECT pg_catalog.jsonb_object_agg(c.relkind::text, c.count) FROM (
                    SELECT c.relkind, count(*) FROM pg_catalog.pg_class c
                    JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' GROUP BY c.relkind) c),
                'profileObjects', (SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname IN ('cp6_tenancy_v1','cp6_stock_read_v1')),
                'policyRoleCounts', (SELECT pg_catalog.jsonb_agg(pg_catalog.cardinality(polroles) ORDER BY polrelid)
                    FROM pg_catalog.pg_policy WHERE polname='cp6_stock_read_tenant_v1'))::text
            """, ct))!;
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        var rows = result.GetProperty("bindings").EnumerateArray().ToDictionary(r => r.GetProperty("tenant").GetGuid());
        Need(rows.Count == tenants.Length && tenants.All(t => rows.TryGetValue(t.Tenant, out var row)
            && row.GetProperty("ordinary").GetBoolean() && row.GetProperty("login").GetString() == t.Role
            && row.GetProperty("oid").GetUInt32() != 0), "The native directory did not match the full ordinary population.");
        Need(result.GetProperty("policyRoleCounts").GetArrayLength() == 6
            && result.GetProperty("policyRoleCounts").EnumerateArray().All(p => p.GetInt32() == tenants.Length),
            "The six native Stock policies did not bind the full ordinary population.");
        return result;
    }

    private static async Task RaiseOwnedCapacity(string root, CancellationToken ct)
    {
        var data = Path.Combine(root, "data");
        var port = Port.ToString(CultureInfo.InvariantCulture);
        var ownership = await File.ReadAllLinesAsync(Path.Combine(root, "ownership.txt"), ct);
        var running = await File.ReadAllLinesAsync(Path.Combine(data, "postmaster.pid"), ct);
        Need(ownership.Length == 3 && ownership[0] == root && ownership[1] == data && ownership[2] == port
            && running.Length > 3 && string.Equals(Path.GetFullPath(running[1]), data, StringComparison.OrdinalIgnoreCase)
            && running[3] == port, "Only the exact run-owned PostgreSQL can be restarted.");
        var start = new ProcessStartInfo(Path.Combine(Required("CP6_PERMISSION_PG_BIN"), "pg_ctl.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-D", data, "-l", Path.Combine(root, "postgres.log"), "-w", "-m", "fast",
            "-o", "-c max_connections=" + MaxConnections.ToString(CultureInfo.InvariantCulture), "restart" })
            start.ArgumentList.Add(arg);
        using var restart = Process.Start(start)!;
        await restart.WaitForExitAsync(ct);
        Need(restart.ExitCode == 0, "The owned PostgreSQL restart failed.");
    }

    private static string CliInput(string owner, uint oid, string system, Login[] tenants)
    {
        var settings = new NpgsqlConnectionStringBuilder(owner);
        return JsonSerializer.Serialize(new
        {
            Server = settings.Host, settings.Port, settings.Database, DatabaseOid = oid, SystemIdentifier = system,
            AdminUsername = settings.Username, AdminPassword = settings.Password, SslMode = "Disable",
            Tenants = tenants.Select(t => new { TenantId = t.Tenant, Login = t.Role })
        });
    }

    private static async Task<CliResult> RunCli(string action, string input, CancellationToken ct)
    {
        using var process = StartDotnet(new[] { Required("CP6_CAPACITY_CLI"), action, "--stdin" });
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        try
        {
            try
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), ct);
                process.StandardInput.Close();
            }
            catch (IOException) when (input.Length > 16384) { }
            await process.WaitForExitAsync(ct);
            return new(process.ExitCode, (await output).Trim(), (await errors).Trim());
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    private static IEnumerable<string> ExecutionArgs() => new[]
    {
        "exec", "--runtimeconfig", Path.Combine(Binaries, "CP6.Tests.runtimeconfig.json"),
        "--depsfile", Path.Combine(Binaries, "CP6.Tests.deps.json"), "--additionalprobingpath", Required("CP6_CAPACITY_PACKAGES")
    };

    private static Process StartDotnet(IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(Required("CP6_CAPACITY_DOTNET"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RunRoot,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    private static void CheckTarget(string owner)
    {
        var connection = new NpgsqlConnectionStringBuilder(owner);
        Need(connection.Host == "127.0.0.1" && connection.Port == Port && connection.Database == DatabaseName
            && connection.Username == "fixture_owner" && !connection.Pooling, "Only the run-owned loopback fixture database is supported.");
    }

    private static CP6Context FixtureContext(object fixture, Guid tenant) =>
        (CP6Context)Invoke(fixture.GetType().GetMethod("Context", Members)!, fixture,
            new TenantContext { CurrentTenantId = tenant }, null, Array.Empty<IInterceptor>())!;

    private static async Task<object?> Scalar(NpgsqlConnection db, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, db);
        return await command.ExecuteScalarAsync(ct);
    }

    private static Login NewLogin(int i) =>
        new(Guid.NewGuid(), Guid.NewGuid(), i + .12345678m, $"stock_capacity_{i:D4}", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus state);

    private static ulong AvailablePhysicalMemory()
    {
        var state = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        Need(GlobalMemoryStatusEx(ref state), "Windows did not report available physical memory.");
        return state.AvailablePhysical;
    }

    private static object Memory()
    {
        using var process = Process.GetCurrentProcess();
        return new { WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64, ManagedBytes = GC.GetTotalMemory(false) };
    }

    private static double? Percentile(IEnumerable<double> samples, double p)
    {
        var sorted = samples.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(sorted.Length * p) - 1];
    }

    private static void RequireSuccess(Read read) => Need(read.Error is null && read.Pid.HasValue, $"Stock read failed at {read.Phase}/{read.Tenant}: {read.Error}.");
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Missing {name}.");
    private static Assembly Load(string name) => AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Binaries, name + ".dll"));
    private static Type Type(Assembly assembly, string name) => assembly.GetType(name, throwOnError: true)!;

    private static object? Invoke(MethodInfo method, object? instance, params object?[] args)
    {
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
