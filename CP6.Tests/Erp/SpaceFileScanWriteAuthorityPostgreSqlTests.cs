using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.Platform;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("begin", true)]
    [InlineData("begin", false)]
    [InlineData("finish", true)]
    [InlineData("finish", false)]
    public Task PGT017_space_isolated_file_scan_uses_current_commit_authority(string phase, bool scanFirst) =>
        WithFileScanFixture(async fixture =>
        {
            await using var worker = new IsolatedFileScanWorker(fixture, holdScanner: true);
            var lease = await worker.Claim();
            var initial = await ReadScanState(fixture, lease);
            Task<ScanCall> scanning;
            ScanState beforeEffect;
            if (phase == "finish")
            {
                scanning = ObserveScan(worker, lease);
                await worker.WaitForScanner();
                beforeEffect = await ReadScanState(fixture, lease);
                CheckScanning(beforeEffect);
            }
            else
            {
                beforeEffect = initial;
                scanning = null!;
            }

            await using var control = new NpgsqlConnection(fixture.Connection);
            await using var table = new NpgsqlConnection(fixture.Connection);
            await using var observer = new NpgsqlConnection(fixture.Connection);
            await control.OpenAsync();
            await table.OpenAsync();
            await observer.OpenAsync();
            FenceWait scanWait;
            FenceWait pauseWait;
            Task<HttpResponseMessage> pausing;
            var order = scanFirst ? "scan-first" : "pause-first";
            await using (var blocked = await HoldUnderlayTable(table, "Space_File"))
            await using (var held = await HoldUnderlayTenant(control, fixture.Execution.TenantId))
            {
                if (scanFirst)
                {
                    StartEffect();
                    scanWait = await WaitForFence(observer, table.ProcessID, "%UPDATE%Space_File%", null, scanning);
                    pausing = fixture.Pause();
                    pauseWait = await SerialNativeWait(observer, scanWait.Backend, control.ProcessID,
                        $"space-file-scan-{phase}-{order}", pausing);
                }
                else
                {
                    pausing = fixture.Pause();
                    pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
                    StartEffect();
                    scanWait = await WaitForScanNativeLock(observer, pauseWait.Backend, table.ProcessID, scanning);
                }

                await held.RollbackAsync();
                if (pauseWait.Blocker == control.ProcessID)
                    await pausing;
                await blocked.RollbackAsync();
            }

            using var paused = await pausing;
            Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
            ScanState? midpoint = null;
            if (phase == "begin" && scanFirst)
            {
                await worker.WaitForScanner();
                midpoint = await ReadScanState(fixture, lease);
                CheckScanning(midpoint);
                worker.ReleaseScanner();
            }
            else if (phase == "begin")
            {
                worker.ReleaseScanner();
            }

            var actual = await scanning;
            var after = await ReadScanState(fixture, lease);
            await DeviceEvidence($"space-file-scan-{phase}-{order}.json",
                new { ScanWait = scanWait, PauseWait = pauseWait, Before = beforeEffect, Midpoint = midpoint, After = after, Result = actual });

            if (scanFirst)
                Assert.Equal(scanWait.Backend, pauseWait.Blocker);
            else
                Assert.Equal(pauseWait.Backend, scanWait.Blocker);

            if (phase == "finish" && scanFirst)
            {
                CheckSafeScan(fixture, after, actual);
                var readBack = await ObserveScan(worker, lease);
                Assert.Equal(actual, readBack);
                Assert.Equal(1, worker.ScannerCalls);
                Assert.Equal(after, await ReadScanState(fixture, lease));
                return;
            }

            Assert.Equal("SPACE_FILE_SCAN_TENANT_INACTIVE", actual.Code);
            Assert.Equal(midpoint ?? beforeEffect, after);
            if (phase == "begin" && !scanFirst)
                Assert.Equal(0, worker.ScannerCalls);
            await fixture.Resume(paused);
            var recovered = await ObserveScan(worker, lease);
            var end = await ReadScanState(fixture, lease);
            CheckSafeScan(fixture, end, recovered);
            Assert.Equal(after.JobId, end.JobId);
            Assert.Equal(after.AttemptId, end.AttemptId);
            Assert.Equal(beforeEffect.Blobs, end.Blobs);
            await DeviceEvidence($"space-file-scan-{phase}-{order}-recovery.json", new { Result = recovered, End = end });

            void StartEffect()
            {
                if (phase == "begin")
                    scanning = ObserveScan(worker, lease);
                else
                    worker.ReleaseScanner();
            }
        });

    [TenantManagementPostgresTheory]
    [InlineData(1)]
    [InlineData(2)]
    public Task PGT017_space_isolated_file_scan_preserves_unknown_commit(int phase) =>
        WithFileScanFixture(async fixture =>
        {
            var fault = new ScanCommitFault(phase);
            await using var worker = new IsolatedFileScanWorker(fixture, fault: fault, outcome: phase == 2 ? "spaces" : null);
            var lease = await worker.Claim();
            var original = await ReadScanState(fixture, lease);
            var lost = await ObserveScan(worker, lease);
            var committed = await ReadScanState(fixture, lease);
            await DeviceEvidence($"space-file-scan-ack-{phase}.json", new
            {
                Injected = fault.Injected, Original = original, Committed = committed, Result = lost,
            });
            Assert.True(fault.Injected);
            Assert.Equal(original.Blobs, committed.Blobs);
            if (phase == 1)
            {
                CheckScanning(committed);
                Assert.Equal(0, worker.ScannerCalls);
            }
            else
            {
                Assert.Equal(SpaceFileState.Clean, committed.FileState);
                Assert.Equal(SpaceJobStatus.Succeeded, committed.JobState);
                using var paused = await fixture.Pause();
                Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
            }

            await using var recovery = new IsolatedFileScanWorker(fixture);
            var restored = await ObserveScan(recovery, lease);
            var after = await ReadScanState(fixture, lease);
            await DeviceEvidence($"space-file-scan-ack-{phase}-recovery.json", new { Result = restored, After = after });
            Assert.Equal("SPACE_FILE_SCAN_COMMIT_UNKNOWN", lost.Code);
            CheckSafeScan(fixture, after, restored);
            Assert.Equal(committed.JobId, after.JobId);
            Assert.Equal(committed.AttemptId, after.AttemptId);
            Assert.Equal(phase == 1 ? 1 : 0, recovery.ScannerCalls);
            if (phase == 2)
            {
                Assert.Equal(committed, after);
                await recovery.RepeatFinish(lease, FileSafetyResult.Clean(" fixture-isolated-engine ", " w616 "));
                Assert.Equal(committed, await ReadScanState(fixture, lease));
            }
        });

    [TenantManagementPostgresTheory]
    [InlineData("reject")]
    [InlineData("defer")]
    [InlineData("cancel-before")]
    [InlineData("cancel-after")]
    public Task PGT017_space_isolated_file_scan_preserves_normal_outcomes(string action) =>
        WithFileScanFixture(async fixture =>
        {
            await using var worker = new IsolatedFileScanWorker(fixture, holdScanner: action == "cancel-after", outcome: action);
            var lease = await worker.Claim();
            if (action == "cancel-before")
                await RequestPersistedScanCancellation(fixture, lease);
            var pending = ObserveScan(worker, lease);
            if (action == "cancel-after")
            {
                await worker.WaitForScanner();
                await RequestPersistedScanCancellation(fixture, lease);
                worker.ReleaseScanner();
            }
            var result = await pending;
            var after = await ReadScanState(fixture, lease);
            if (action.StartsWith("cancel", StringComparison.Ordinal))
            {
                Assert.Equal(SpaceJobStatus.Cancelled, after.JobState);
                Assert.Equal(SpaceJobAttemptOutcome.Cancelled, after.AttemptState);
                Assert.Equal(SpaceFileState.Quarantined, after.FileState);
                Assert.Equal(action == "cancel-before" ? 0 : 1, worker.ScannerCalls);
                if (action == "cancel-before")
                    Assert.Equal(nameof(OperationCanceledException), result.Error);
                Assert.Equal(2, after.ScanningSources);
                return;
            }

            Assert.Null(result.Error);
            Assert.Equal(action == "reject" ? FileSafetyDisposition.Rejected : FileSafetyDisposition.Deferred, result.Result!.Disposition);
            Assert.Equal(action == "reject" ? SpaceJobStatus.Failed : SpaceJobStatus.Queued, after.JobState);
            Assert.Equal(SpaceJobAttemptOutcome.Failed, after.AttemptState);
            Assert.Equal(action == "reject" ? SpaceFileState.Rejected : SpaceFileState.Quarantined, after.FileState);
            Assert.Equal(action == "reject" ? 2 : 0, after.RejectedSources);
            Assert.Equal(action == "defer" ? 2 : 0, after.ScanningSources);
        });

    [CurrentPermissionPostgresFact]
    public Task PGT017_space_isolated_file_scan_rechecks_expiry_after_saving() =>
        WithFileScanFixture(async fixture =>
        {
            var barrier = new ScanExpiryBarrier();
            await using var worker = new IsolatedFileScanWorker(fixture, holdScanner: true, fault: barrier);
            var lease = await worker.Claim();
            var pending = ObserveScan(worker, lease);
            await worker.WaitForScanner();
            var before = await ReadScanState(fixture, lease);
            var expiry = DateTime.Now.AddSeconds(7);
            await SetExpiry(expiry);
            barrier.Expiry = expiry;
            worker.ReleaseScanner();
            var blocked = await pending;
            var after = await ReadScanState(fixture, lease);
            await DeviceEvidence("space-file-scan-expiry.json", new
            {
                Expiry = expiry, barrier.ObservedAt, Before = before, After = after, Result = blocked,
            });
            Assert.True(barrier.ObservedAt > expiry);
            Assert.Equal("SPACE_FILE_SCAN_TENANT_INACTIVE", blocked.Code);
            Assert.Equal(before, after);

            await SetExpiry(null);
            var recovered = await ObserveScan(worker, lease);
            CheckSafeScan(fixture, await ReadScanState(fixture, lease), recovered);

            async Task SetExpiry(DateTime? value)
            {
                await using var connection = new NpgsqlConnection(fixture.Connection);
                await connection.OpenAsync();
                await using var update = new NpgsqlCommand("""
                    UPDATE public."Sys_Tenants" SET "ExpireDate" = @expiry WHERE "Id" = @tenant
                    """, connection);
                update.Parameters.AddWithValue("expiry", NpgsqlTypes.NpgsqlDbType.Timestamp,
                    value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : (object)DBNull.Value);
                update.Parameters.AddWithValue("tenant", fixture.Execution.TenantId);
                Assert.Equal(1, await update.ExecuteNonQueryAsync());
            }
        });

    private async Task WithFileScanFixture(Func<FileScanFixture, Task> run)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        var root = NewUnderlayFileRoot();
        await using var state = SpaceDesignContext(connection, execution);
        await state.Database.MigrateAsync();
        var (version, _, _) = await SeedSpaceDesignDraft(state, execution, site, storeLease: false);
        var (second, _, _) = await SeedSpaceDesignDraft(state, execution, Guid.NewGuid(), storeLease: false);
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = root;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_FILE_SCAN_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var sample = SourceUploadSample.For("cad", version.Id);
        using var uploaded = await SendSourceUpload(host, actor, sample);
        Assert.True(uploaded.StatusCode == HttpStatusCode.Accepted,
            uploaded.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(uploaded));
        var content = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = content.GetProperty("file").GetProperty("id").GetGuid();
        var jobId = content.GetProperty("scanJobId").GetGuid();
        var file = await state.Files.SingleAsync(row => row.Id == fileId);
        state.Sources.Add(SpaceModelSource.CreatePendingFileSource(execution.TenantId, second.Id, SpaceSourceType.Dxf, file, file.OriginalName));
        await state.SaveChangesAsync();
        state.ChangeTracker.Clear();
        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
        await run(new FileScanFixture(connection, execution, root, fileId, jobId, sample.Sha256, host, manager, active.Revision));
    }

    private static async Task<ScanCall> ObserveScan(IsolatedFileScanWorker worker, SpaceJobLease lease)
    {
        try { return new(await worker.Processor.ProcessAsync(lease), null, null, null); }
        catch (SpaceJobProcessingException error) { return new(null, error.GetType().Name, error.ErrorCode, error.Message); }
        catch (Exception error) { return new(null, error.GetType().Name, null, error.Message); }
    }

    private static async Task RequestPersistedScanCancellation(FileScanFixture fixture, SpaceJobLease lease)
    {
        await using var state = SpaceDesignContext(fixture.Connection, fixture.Execution);
        var job = await state.Jobs.SingleAsync(row => row.Id == lease.JobId);
        job.RequestCancellation(fixture.Execution.ActorId, DateTime.UtcNow);
        await state.SaveChangesAsync();
    }

    private static async Task<ScanState> ReadScanState(FileScanFixture fixture, SpaceJobLease lease)
    {
        await using var state = SpaceDesignContext(fixture.Connection, fixture.Execution);
        var file = await state.Files.AsNoTracking().SingleAsync(row => row.Id == fixture.FileId);
        var job = await state.Jobs.AsNoTracking().SingleAsync(row => row.Id == fixture.JobId);
        var attempt = await state.JobAttempts.AsNoTracking().SingleAsync(row => row.Id == lease.AttemptId);
        var sources = await state.Sources.AsNoTracking().Where(row => row.FileId == file.Id).OrderBy(row => row.Id).ToArrayAsync();
        Assert.Equal(2, sources.Length);
        return new(file.Id, job.Id, attempt.Id, file.State, job.Status, attempt.Outcome,
            sources.Count(row => row.State == SpaceSourceState.Scanning),
            sources.Count(row => row.State == SpaceSourceState.Ready),
            sources.Count(row => row.State == SpaceSourceState.Rejected),
            JsonSerializer.Serialize(new { File = file, Job = job, Attempt = attempt, Sources = sources }),
            JsonSerializer.Serialize(UnderlayFiles(fixture.Root, "*").Order(StringComparer.Ordinal).Select(path =>
                new { Path = Path.GetRelativePath(fixture.Root, path), Sha256 = ExcelCadHash(File.ReadAllBytes(path)) })));
    }

    private static void CheckScanning(ScanState state)
    {
        Assert.Equal(SpaceFileState.Scanning, state.FileState);
        Assert.Equal(2, state.ScanningSources);
        Assert.Equal(SpaceJobStatus.Running, state.JobState);
        Assert.Equal(SpaceJobAttemptOutcome.Running, state.AttemptState);
    }

    private static void CheckSafeScan(FileScanFixture fixture, ScanState state, ScanCall result)
    {
        Assert.Null(result.Error);
        Assert.Equal(FileSafetyResult.Clean("fixture-isolated-engine", "w616"), result.Result);
        Assert.Equal(fixture.FileId, state.FileId);
        Assert.Equal(fixture.JobId, state.JobId);
        Assert.Equal(SpaceFileState.Clean, state.FileState);
        Assert.Equal(2, state.ReadySources);
        Assert.Equal(SpaceJobStatus.Succeeded, state.JobState);
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, state.AttemptState);
    }

    private static async Task<FenceWait> WaitForScanNativeLock(NpgsqlConnection observer, int pause, int table, Task<ScanCall> pending)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var query = new NpgsqlCommand("""
            SELECT a.pid, b.blocker, a.usename, a.wait_event, clock_timestamp()
            FROM pg_stat_activity a CROSS JOIN LATERAL unnest(pg_blocking_pids(a.pid)) b(blocker)
            WHERE a.datname=current_database() AND a.wait_event_type='Lock' AND b.blocker IN (@pause,@table)
            ORDER BY a.pid LIMIT 1
            """, observer);
        query.Parameters.AddWithValue("pause", pause);
        query.Parameters.AddWithValue("table", table);
        while (true)
        {
            await using (var rows = await query.ExecuteReaderAsync(timeout.Token))
                if (await rows.ReadAsync(timeout.Token))
                    return new(rows.GetInt32(0), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4));
            if (pending.IsCompleted)
                Assert.Fail("File scan completed before the native wait: " + JsonSerializer.Serialize(await pending));
            await Task.Delay(35, timeout.Token);
        }
    }

    private sealed record FileScanFixture(string Connection, SpaceDesignExecution Execution, string Root, Guid FileId, Guid JobId,
        string Sha256, AuthenticationApiProcess Host, Dictionary<string, string> Manager, long Revision)
    {
        internal Task<HttpResponseMessage> Pause() => Host.Send(OperationsRequest(Manager, HttpMethod.Post,
            ManagementPath(Execution.TenantId, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), Revision, "suspend", null)));

        internal Task Resume(HttpResponseMessage response) => ResumeSourceUploadTenant(Host, Manager, Execution.TenantId, response);
    }

    private sealed record ScanState(Guid FileId, Guid JobId, Guid AttemptId, SpaceFileState FileState, SpaceJobStatus JobState,
        SpaceJobAttemptOutcome AttemptState, int ScanningSources, int ReadySources, int RejectedSources, string Rows, string Blobs);

    private sealed record ScanCall(FileSafetyResult? Result, string? Error, string? Code, string? Message);

    private sealed class IsolatedFileScanWorker : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly FileScanFixture _fixture;
        private readonly FileScanAdapter _adapter;

        internal IsolatedFileScanWorker(FileScanFixture fixture, bool holdScanner = false, IInterceptor? fault = null, string? outcome = null)
        {
            _fixture = fixture;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<ISpaceExecutionContext>(_ => new SpaceDesignExecution(fixture.Execution.TenantId, Guid.NewGuid()));
            services.AddSpaceDesignV1Persistence(fixture.Connection, new DatabaseOptions(DatabaseProvider.PostgreSql));
            services.AddSpaceFileSystemStorage(fixture.Root);
            if (fault is not null)
                services.AddScoped(provider => new SpaceContext(
                    new DbContextOptionsBuilder<SpaceContext>(provider.GetRequiredService<DbContextOptions<SpaceContext>>())
                        .AddInterceptors(fault).Options, provider.GetRequiredService<ISpaceExecutionContext>(), provider.GetRequiredService<ISpaceClock>()));
            services.AddScoped<ISpaceMalwareScanner>(provider => new FileScanAdapter(provider.GetRequiredService<SpaceContext>(), fixture, outcome));
            services.AddScoped<IFileSafetyScanner, ManagedFileSafetyScanner>();
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateAsyncScope();
            _adapter = (FileScanAdapter)_scope.ServiceProvider.GetRequiredService<ISpaceMalwareScanner>();
            Processor = _scope.ServiceProvider.GetRequiredService<ISpaceFileScanProcessor>();
            if (!holdScanner)
                ReleaseScanner();
        }

        internal ISpaceFileScanProcessor Processor { get; }
        internal Task RepeatFinish(SpaceJobLease lease, FileSafetyResult result) =>
            _scope.ServiceProvider.GetRequiredService<ISpaceFileScanStateStore>().FinishScanAsync(lease, result);

        internal int ScannerCalls => _adapter.Calls;
        internal Task WaitForScanner() => _adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(12));
        internal void ReleaseScanner() => _adapter.Released.TrySetResult();

        internal async Task<SpaceJobLease> Claim()
        {
            var lease = Assert.IsType<SpaceJobLease>(await _scope.ServiceProvider.GetRequiredService<ISpaceJobLeaseStore>()
                .TryClaimNextAsync("w616-isolated-operator", SpaceFileScanProcessor.ProcessorVersion, TimeSpan.FromMinutes(2), [SpaceJobType.FileScan]));
            Assert.Equal(_fixture.JobId, lease.JobId);
            return lease;
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseScanner();
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class FileScanAdapter(SpaceContext context, FileScanFixture fixture, string? outcome) : ISpaceMalwareScanner
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SpaceMalwareScanResult> ScanAsync(FileScanRequest request, Stream content, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            Assert.Null(context.Database.CurrentTransaction);
            request.Sandbox.Validate();
            Assert.Equal(fixture.Execution.TenantId, request.TenantId);
            Assert.Equal(fixture.FileId, request.FileId);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Assert.Equal(fixture.Sha256, ExcelCadHash(copy.ToArray()));
            Started.TrySetResult();
            await Released.Task.WaitAsync(ct);
            return outcome switch
            {
                "reject" => SpaceMalwareScanResult.Detected("fixture-isolated-engine", "w616"),
                "defer" => SpaceMalwareScanResult.Unavailable(),
                "spaces" => SpaceMalwareScanResult.Clean(" fixture-isolated-engine ", " w616 "),
                _ => SpaceMalwareScanResult.Clean("fixture-isolated-engine", "w616"),
            };
        }
    }

    private sealed class ScanExpiryBarrier : SaveChangesInterceptor
    {
        internal DateTime? Expiry { get; set; }
        internal DateTime? ObservedAt { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        {
            if (Expiry is { } expiry)
            {
                while (DateTime.Now <= expiry)
                    await Task.Delay(50, ct);
                ObservedAt = DateTime.Now;
                Expiry = null;
            }
            return result;
        }
    }

    private sealed class ScanCommitFault(int phase) : DbTransactionInterceptor
    {
        private int _commits;
        internal bool Injected { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            if (++_commits == phase)
            {
                Injected = true;
                throw new IOException("W616 injected a lost native file-scan COMMIT acknowledgement.");
            }
            return Task.CompletedTask;
        }
    }
}
