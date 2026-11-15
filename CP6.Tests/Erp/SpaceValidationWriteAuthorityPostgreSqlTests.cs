using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("submit", true)]
    [InlineData("submit", false)]
    [InlineData("start", true)]
    [InlineData("start", false)]
    [InlineData("finish", true)]
    [InlineData("finish", false)]
    [InlineData("blocked", true)]
    [InlineData("blocked", false)]
    [InlineData("ack", true)]
    [InlineData("stale", true)]
    [InlineData("stale", false)]
    public async Task PGT017_space_validation_writer_and_pause_share_native_authority(string phase, bool businessFirst)
    {
        var caseName = phase;
        var dropAcknowledgement = phase == "ack";
        if (dropAcknowledgement)
            phase = "finish";
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:validate", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedPublishVersions(writer, space, site, historical: false);
        if (phase is "finish" or "blocked")
        {
            space.Issues.Add(SpaceModelIssue.Create(writer.Tenant, versions.Candidate, null, null,
                phase == "blocked" ? SpaceIssueSeverity.Blocking : SpaceIssueSeverity.Info, "VALIDATION_FIXTURE_ISSUE"));
            await space.SaveChangesAsync();
            space.ChangeTracker.Clear();
        }
        await using var host = await StartPublishSubmissionHost(connection, writer.Tenant, site);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var path = $"{SpaceDesignPath}/versions/{versions.Candidate:D}/validations";
        var clock = new SystemSpaceClock();
        await using var leaseContext = SpaceDesignContext(connection, execution);
        var workerConnection = new NpgsqlConnectionStringBuilder(connection) { SslMode = SslMode.Disable };
        var relayPort = int.TryParse(Environment.GetEnvironmentVariable("CP6_W646_RELAY_PORT"), out var reserved) ? reserved : 0;
        await using var relay = dropAcknowledgement ? new StockPostgresCommitRelay(workerConnection.Port, workerConnection.Username!, relayPort) : null;
        if (relay is not null)
            workerConnection.Port = relay.Port;
        await using var workerContext = SpaceDesignContext(relay is not null ? workerConnection.ConnectionString : connection, execution);
        await using var workerMain = ValidationMainServices(connection);
        using var localWms = new ServiceCollection()
            .AddSingleton<ISpaceWarehouseResolver>(new Cp6SpaceWarehouseResolver(writer.Db))
            .AddSingleton<ISpaceWmsAdapter>(new Cp6SpaceWmsAdapter(writer.Db))
            .BuildServiceProvider();
        var profiles = new ValidationWorkerProfiles(workerContext, new DefaultSpaceValidationProfileProvider(localWms), phase == "stale");
        var processor = ActivatorUtilities.CreateInstance<SpaceValidationJobProcessor>(
            workerMain, workerContext, clock, profiles, new SpaceValidationEngine());
        var ledger = new EfSpaceJobLeaseStore(leaseContext, clock);
        var runner = new SpaceJobProcessorRunner(ledger, [processor]);
        var submission = phase == "submit";
        SpaceValidationRunDto? original = null;
        SpaceJobLease? lease = null;
        if (!submission)
        {
            using var accepted = await Submit(actor);
            original = (await ReadValidationSubmission(accepted)).Validation;
            if (phase is "finish" or "blocked")
            {
                // Resume the real Job from an earlier committed Running state.
                var run = await space.ValidationRuns.SingleAsync(row => row.Id == original.Id);
                run.Start(DateTime.UtcNow);
                await space.SaveChangesAsync();
                space.ChangeTracker.Clear();
            }
            lease = await ledger.TryClaimNextAsync("space-validation-native", processor.ProcessorVersion,
                TimeSpan.FromMinutes(2), [SpaceJobType.Validate]);
            Assert.NotNull(lease);
            Assert.Equal(original.JobId, lease.JobId);
        }

        await using var control = await OpenHistoricalDatabase(connection);
        await using var business = await OpenHistoricalDatabase(connection);
        await using var observer = await OpenHistoricalDatabase(connection);
        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
        var before = await ValidationWriteSet(space, versions.Candidate);

        FenceWait businessWait, pauseWait;
        Task work;
        Task<HttpResponseMessage> pause;
        await using (var heldBusiness = await HoldUnderlayTable(business, "Space_ValidationRun"))
        await using (var heldTenant = await HoldUnderlayTenant(control, writer.Tenant))
        {
            if (businessFirst)
            {
                relay?.Arm();
                work = Run(actor);
                businessWait = await WaitForFence(observer, business.ProcessID, "%Space_ValidationRun%", null, work);
                pause = Pause();
                pauseWait = await WaitForValidationWriteOrRoot(observer, businessWait.Backend, control.ProcessID,
                    "%UPDATE%Sys_Tenants%", pause);
            }
            else
            {
                pause = Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                work = Run(actor);
                businessWait = await WaitForValidationWriteOrRoot(observer, pauseWait.Backend, business.ProcessID,
                    "%Space_ValidationRun%", work);
            }
            await heldTenant.RollbackAsync();
            if (pauseWait.Blocker == control.ProcessID)
                await pause;
            await heldBusiness.RollbackAsync();
        }

        using var paused = await pause;
        Assert.False((await NumberHttpData<TenantManagementReceiptV1>(paused)).Snapshot.Enable);
        await work;
        if (relay is not null)
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
        using var result = (work as Task<HttpResponseMessage>)?.Result;
        var after = await ValidationWriteSet(space, versions.Candidate);
        await DeviceEvidence($"space-validation-{caseName}-{(businessFirst ? "business-first" : "pause-first")}.json",
            new { BusinessWait = businessWait, PauseWait = pauseWait, Before = before, After = after, Http = result?.StatusCode, dropAcknowledgement });
        if (businessFirst)
            Assert.Equal(businessWait.Backend, pauseWait.Blocker);
        else
            Assert.Equal(pauseWait.Backend, businessWait.Blocker);
        if (submission)
        {
            if (businessFirst)
            {
                original = (await ReadValidationSubmission(result!)).Validation;
                Assert.Equal(SpaceVersionStatus.Validating, after.Version.Status);
                Assert.Equal(original.Id, Assert.Single(after.Runs).Id);
                Assert.Equal(original.JobId, Assert.Single(after.Jobs).Id);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Unauthorized, result!.StatusCode);
                Assert.Equal(ValidationBusinessWrites(before), ValidationBusinessWrites(after));
                Assert.Empty(after.Jobs);
            }
        }
        else if (!businessFirst)
        {
            Assert.Equal(ValidationBusinessWrites(before), ValidationBusinessWrites(after));
            Assert.Equal(SpaceJobStatus.Queued, Assert.Single(after.Jobs).Status);
            Assert.Equal("SPACE_ARTIFACT_TENANT_INACTIVE", Assert.Single(after.Jobs).LastErrorCode);
        }
        else
        {
            var run = Assert.Single(after.Runs);
            Assert.Equal(original!.Id, run.Id);
            Assert.Equal(phase switch
            {
                "start" => SpaceValidationStatus.Running,
                "stale" => SpaceValidationStatus.Failed,
                "blocked" => SpaceValidationStatus.Blocked,
                _ => SpaceValidationStatus.Passed,
            }, run.Status);
            if (phase == "start")
            {
                Assert.NotNull(run.StartedAtUtc);
                Assert.Equal(SpaceVersionStatus.Validating, after.Version.Status);
                Assert.Empty(after.Issues);
            }
            else
                AssertTerminalValidation(after, original!, phase);
        }

        await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused);
        actor = await Login(host, writer);
        if (submission)
        {
            using var recovered = await Submit(actor);
            var replay = await ReadValidationSubmission(recovered);
            Assert.Equal(businessFirst, replay.Reused);
            if (original is not null)
            {
                Assert.Equal((original.Id, original.JobId), (replay.Validation.Id, replay.Validation.JobId));
                Assert.Equal(ValidationBusinessWrites(after), ValidationBusinessWrites(await ValidationWriteSet(space, versions.Candidate)));
            }
            return;
        }

        if (!businessFirst || phase == "start")
        {
            await WaitPublishJobDue(observer, Assert.Single(after.Jobs).NextAttemptAtUtc);
            Assert.True(await runner.RunNextAsync(SpaceJobType.Validate, "space-validation-resumed"));
        }
        var completed = await ValidationWriteSet(space, versions.Candidate);
        AssertTerminalValidation(completed, original!, phase);
        if (businessFirst && phase == "start")
            Assert.Equal(Assert.Single(after.Runs).StartedAtUtc, Assert.Single(completed.Runs).StartedAtUtc);
        using var originalRead = await host.Send(OperationsRequest(actor, HttpMethod.Get, $"{SpaceDesignPath}/validations/{original!.Id:D}"));
        Assert.Equal(HttpStatusCode.OK, originalRead.StatusCode);
        var readback = Assert.IsType<SpaceValidationRunDto>(await originalRead.Content.ReadFromJsonAsync<SpaceValidationRunDto>());
        Assert.Equal(original.JobId, readback.JobId);
        Assert.Equal(Assert.Single(completed.Runs).Status.ToString(), readback.Status);
        Assert.False(await runner.RunNextAsync(SpaceJobType.Validate, "space-validation-no-duplicate"));
        Assert.Equal(ValidationBusinessWrites(completed), ValidationBusinessWrites(await ValidationWriteSet(space, versions.Candidate)));
        var writes = await ReadValidationNativeWrites(observer, versions.Candidate, original.Id);
        Assert.Equal(2 + completed.Issues.Count(row => row.ValidationRunId == original.Id), writes.Count);
        Assert.Single(writes.Values.Distinct());
        await DeviceEvidence($"space-validation-{caseName}-{(businessFirst ? "business-first" : "pause-first")}-completed.json",
            new { Completed = completed, NativeTransaction = writes, Original = readback });

        Task Run(Dictionary<string, string> session) => submission ? Submit(session) : runner.RunClaimedAsync(lease!);
        Task<HttpResponseMessage> Submit(Dictionary<string, string> session) => host.Send(OperationsRequest(session, HttpMethod.Post, path));
        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private static async Task<FenceWait> WaitForValidationWriteOrRoot(NpgsqlConnection observer, int root, int fallback,
        string writeStatement, Task request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var command = new NpgsqlCommand("""
            SELECT pid, CASE WHEN @root=ANY(pg_blocking_pids(pid)) THEN @root ELSE @fallback END, usename, wait_event, clock_timestamp()
            FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND
                ((@root=ANY(pg_blocking_pids(pid)) AND query LIKE '%cp6_authentication_v1.acquire%')
                 OR (@fallback=ANY(pg_blocking_pids(pid)) AND query LIKE @write))
            ORDER BY pid LIMIT 1
            """, observer);
        command.Parameters.AddWithValue("root", root);
        command.Parameters.AddWithValue("fallback", fallback);
        command.Parameters.AddWithValue("write", writeStatement);
        while (true)
        {
            await using (var rows = await command.ExecuteReaderAsync(deadline.Token))
                if (await rows.ReadAsync(deadline.Token))
                    return new(rows.GetInt32(0), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4));
            if (request.IsCompleted)
            {
                await request;
                Assert.Fail("Validation request completed before the expected native wait.");
            }
            await Task.Delay(35, deadline.Token);
        }
    }

    private static async Task<CreateSpaceValidationResponse> ReadValidationSubmission(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            response.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(response));
        return Assert.IsType<CreateSpaceValidationResponse>(
            await response.Content.ReadFromJsonAsync<CreateSpaceValidationResponse>());
    }

    private static async Task<ValidationWriteState> ValidationWriteSet(SpaceContext space, Guid version) => new(
        await space.Versions.AsNoTracking().SingleAsync(row => row.Id == version),
        await space.ValidationRuns.AsNoTracking().Where(row => row.ModelVersionId == version).OrderBy(row => row.Id).ToArrayAsync(),
        await space.Issues.AsNoTracking().Where(row => row.ModelVersionId == version).OrderBy(row => row.Id).ToArrayAsync(),
        await space.Jobs.AsNoTracking().Where(row => row.SubjectId == version).OrderBy(row => row.Id).ToArrayAsync());

    private static string ValidationBusinessWrites(ValidationWriteState state) =>
        JsonSerializer.Serialize(new { state.Version, state.Runs, state.Issues });

    private static void AssertTerminalValidation(ValidationWriteState state, SpaceValidationRunDto original, string phase)
    {
        var run = Assert.Single(state.Runs);
        var job = Assert.Single(state.Jobs);
        Assert.Equal((original.Id, original.JobId, original.ContentHash), (run.Id, run.JobId, run.ContentHash));
        Assert.Equal(original.JobId, job.Id);
        Assert.Equal(phase switch
        {
            "stale" => SpaceValidationStatus.Failed,
            "blocked" => SpaceValidationStatus.Blocked,
            _ => SpaceValidationStatus.Passed,
        }, run.Status);
        Assert.Equal(phase is "stale" or "blocked" ? SpaceVersionStatus.Draft : SpaceVersionStatus.Ready, state.Version.Status);
        Assert.Equal(phase == "stale" ? SpaceJobStatus.Failed : SpaceJobStatus.Succeeded, job.Status);
        Assert.NotNull(run.FinishedAtUtc);
        if (phase == "stale")
        {
            Assert.Equal(SpaceErrorCodes.ValidationStale, run.FailureCode);
            Assert.Empty(state.Issues);
        }
        else if (phase is "finish" or "blocked")
        {
            var finding = Assert.Single(state.Issues, issue => issue.ValidationRunId == run.Id);
            Assert.Equal("VALIDATION_FIXTURE_ISSUE", finding.Code);
            Assert.Equal(phase == "blocked" ? SpaceIssueSeverity.Blocking : SpaceIssueSeverity.Info, finding.Severity);
            Assert.Equal(2, state.Issues.Length);
        }
        if (run.Status == SpaceValidationStatus.Passed)
            Assert.Equal(run.ContentHash, state.Version.ContentHash);
    }

    private static async Task<Dictionary<string, string>> ReadValidationNativeWrites(NpgsqlConnection observer, Guid version, Guid run)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 'version', xmin::text FROM public."Space_ModelVersion" WHERE "Id"=@version
            UNION ALL SELECT 'run', xmin::text FROM public."Space_ValidationRun" WHERE "Id"=@run
            UNION ALL SELECT 'issue/' || "Id"::text, xmin::text FROM public."Space_ModelIssue" WHERE "ValidationRunId"=@run
            """, observer);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("run", run);
        await using var reader = await command.ExecuteReaderAsync();
        var writes = new Dictionary<string, string>();
        while (await reader.ReadAsync())
            writes.Add(reader.GetString(0), reader.GetString(1));
        return writes;
    }

    private static ServiceProvider ValidationMainServices(string connection) => new ServiceCollection()
        .AddScoped<ITenantContext, TenantContext>()
        .AddScoped(services => new CP6Context(
            new DbContextOptionsBuilder<CP6Context>().UseNpgsql(connection).Options,
            services.GetRequiredService<ITenantContext>()))
        .AddSingleton(TimeProvider.System)
        .AddScoped<ISpaceArtifactWriteAuthority, SpaceArtifactWriteAuthority>()
        .BuildServiceProvider();

    private sealed class ValidationWorkerProfiles(SpaceContext worker, ISpaceValidationProfileProvider current, bool stale)
        : ISpaceValidationProfileProvider
    {
        public async Task<SpaceValidationProfile> GetProfileAsync(Guid tenantId, Guid siteId, Guid correlationId,
            CancellationToken cancellationToken = default)
        {
            Assert.Null(worker.Database.CurrentTransaction);
            var profile = await current.GetProfileAsync(tenantId, siteId, correlationId, cancellationToken);
            return stale ? SpaceValidationProfile.Create(profile.AdapterId, profile.MaxLocationCodeLength,
                profile.LocationCodePattern, profile.MaxObjectCount + 1) : profile;
        }
    }

    private sealed record ValidationWriteState(SpaceModelVersion Version, SpaceValidationRun[] Runs, SpaceModelIssue[] Issues, SpaceJob[] Jobs);
}
