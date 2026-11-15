using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public Task W647_ordinary_clone_waits_for_signed_pause_and_recovers_its_original_lease() =>
        ObserveW647CloneOrder(planning: false, ownerFirst: false);

    [TenantManagementPostgresTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task W647_version_clone_effect_and_signed_pause_share_native_commit_order(bool planning, bool ownerFirst) =>
        ObserveW647CloneOrder(planning, ownerFirst);

    private async Task ObserveW647CloneOrder(bool planning, bool ownerFirst)
    {
        await using var writer = await Seed();
        await using var admin = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:read", "model:edit", "planning:scenario:create");
        var connection = writer.Db.Database.GetConnectionString()!;
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, new(writer.Tenant, writer.User));
        var (model, source) = await SeedW647Published(writer, space, site);
        await using var host = await StartW647CloneApi(connection, writer.Tenant, site);
        var manager = await Login(host, admin);
        var reservation = await ReserveW647Clone(host, await Login(host, writer), site, source, planning);
        await using var services = CreateW647CloneServices(connection, writer.Tenant, writer.User);
        await using var scope = services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<ISpaceVersionCloneProcessor>();
        var lease = await ClaimW647Clone(scope.ServiceProvider, reservation.Job);
        var beforeAuth = await ReadPauseAuthority(writer, admin.User);
        var pause = new TenantManagementCommandV1("w647-clone-pause", beforeAuth.Revision, "suspend", null);
        await using var observer = await OpenHistoricalDatabase(connection);
        await using var floorBlocker = await OpenHistoricalDatabase(connection);
        await using var tenantBlocker = await OpenHistoricalDatabase(connection);
        var before = await ReadW647CloneWrites(observer, writer.Tenant);
        Assert.Empty(before.Where(row => row.Kind == "step" || row.Version == reservation.Version));
        Assert.Equal(SpaceJobStatus.Running,
            (await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == reservation.Job)).Status);

        await using var floorLock = await floorBlocker.BeginTransactionAsync();
        await using (var sql = new NpgsqlCommand("""LOCK TABLE public."Space_FloorRevision" IN SHARE MODE""", floorBlocker, floorLock))
            await sql.ExecuteNonQueryAsync();
        await using var tenantLock = await tenantBlocker.BeginTransactionAsync();
        await HoldSpaceVersionTenant(tenantBlocker, tenantLock, writer.Tenant);

        Task<HttpResponseMessage> pausing;
        Task<SpaceVersionCloneCounts> working;
        FenceWait workerWait;
        FenceWait pauseWait;
        W647CloneRow[] beforePauseCommit;
        if (ownerFirst)
        {
            working = worker.ProcessAsync(lease);
            workerWait = await WaitForFence(observer, floorBlocker.ProcessID, "%Space_FloorRevision%", null, working);
            pausing = Pause();
            pauseWait = await WaitForFence(observer, workerWait.Backend, "%cp6_authentication_v1.acquire%", null, pausing);
            Assert.Equal(before, await ReadW647CloneWrites(observer, writer.Tenant));
            await floorLock.RollbackAsync();
            var completed = await working.WaitAsync(TimeSpan.FromSeconds(12));
            await WaitForFence(observer, tenantBlocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            beforePauseCommit = await ReadW647CloneWrites(observer, writer.Tenant);
            await AssertW647CloneCompleted(space, model, source, reservation, lease, beforePauseCommit, completed, planning);
            Assert.True((await ReadPauseAuthority(writer, admin.User)).Enabled);
            await tenantLock.RollbackAsync();
        }
        else
        {
            pausing = Pause();
            pauseWait = await WaitForFence(observer, tenantBlocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            working = worker.ProcessAsync(lease);
            workerWait = await WaitW647CloneRootOrFloor(observer, pauseWait.Backend, floorBlocker.ProcessID);
            beforePauseCommit = await ReadW647CloneWrites(observer, writer.Tenant);
            Assert.Equal(before, beforePauseCommit);
            await tenantLock.RollbackAsync();
            await pausing.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.False(working.IsCompletedSuccessfully);
            await floorLock.RollbackAsync();
        }

        using var response = await pausing;
        var paused = await NumberHttpData<TenantManagementReceiptV1>(response);
        var afterAuth = await ReadPauseAuthority(writer, admin.User);
        Assert.True(beforeAuth.Enabled);
        Assert.False(afterAuth.Enabled);
        Assert.False(paused.Snapshot.Enable);
        Assert.True(afterAuth.RootRevision > beforeAuth.RootRevision);
        Assert.NotEqual(beforeAuth.PolicyRevision, afterAuth.PolicyRevision);
        Assert.Equal(beforeAuth.Revision + 1, paused.Snapshot.Revision);
        Assert.Equal(paused.Snapshot.Revision, afterAuth.Revision);
        Assert.Equal(beforeAuth.AuditCount + 1, afterAuth.AuditCount);
        Assert.Equal(beforeAuth.ReceiptCount + 1, afterAuth.ReceiptCount);
        Assert.Equal(pause.OperationRef, paused.OperationRef);
        SpaceVersionCloneCounts? counts = null;
        string? failure = null;
        try { counts = await working.WaitAsync(TimeSpan.FromSeconds(12)); }
        catch (SpaceJobProcessingException error) { failure = error.ErrorCode; }
        var afterPause = await ReadW647CloneWrites(observer, writer.Tenant);
        await DeviceEvidence($"w647-clone-{planning}-{ownerFirst}-paused.json", new
        {
            writer.Tenant, Site = site, Model = model, Source = source, Reservation = reservation, Lease = lease,
            Planning = planning, OwnerFirst = ownerFirst, WorkerWait = workerWait, PauseWait = pauseWait,
            PauseReceipt = paused, BeforeAuthority = beforeAuth, AfterAuthority = afterAuth, Failure = failure, Counts = counts, Before = before,
            BeforePauseCommit = beforePauseCommit, AfterPause = afterPause
        });
        if (ownerFirst)
        {
            Assert.Null(failure);
            Assert.Equal(beforePauseCommit, afterPause);
        }
        else
        {
            Assert.Equal(pauseWait.Backend, workerWait.Blocker);
            Assert.Equal("SPACE_ARTIFACT_TENANT_INACTIVE", failure);
            Assert.Null(counts);
            Assert.Equal(before, afterPause);
        }

        await ReactivateSpaceVersionTenant(host, manager, writer.Tenant, paused.Snapshot.Revision);
        counts ??= await worker.ProcessAsync(lease);
        var finished = await ReadW647CloneWrites(observer, writer.Tenant);
        await AssertW647CloneCompleted(space, model, source, reservation, lease, finished, counts, planning);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ISpaceJobLeaseStore>().TryClaimNextAsync(
            "w647-duplicate", SpaceVersionCloneContract.ProcessorVersion, TimeSpan.FromMinutes(2), [SpaceJobType.CloneVersion]));
        Assert.Equal(finished, await ReadW647CloneWrites(observer, writer.Tenant));
        Assert.Equal(9, SpaceProcessingJobWorker.JobTypes.Count);
        Assert.DoesNotContain(SpaceJobType.CloneVersion, SpaceProcessingJobWorker.JobTypes);
        await DeviceEvidence($"w647-clone-{planning}-{ownerFirst}-recovered.json", new { Finished = finished, Counts = counts });

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"), pause));
    }

    [TenantManagementPostgresTheory]
    [InlineData("acknowledgement")]
    [InlineData("cancel")]
    [InlineData("late-failure")]
    public async Task W647_ordinary_clone_keeps_atomic_original_outcome(string outcome)
    {
        await using var writer = await Seed();
        await GrantSpaceDesignActions(writer, "model:read", "model:edit");
        var direct = new NpgsqlConnectionStringBuilder(writer.Db.Database.GetConnectionString()) { SslMode = SslMode.Disable };
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(direct.ConnectionString, new(writer.Tenant, writer.User));
        var (modelId, source) = await SeedW647Published(writer, space, site);
        await using var host = await StartW647CloneApi(direct.ConnectionString, writer.Tenant, site);
        var reservation = await ReserveW647Clone(host, await Login(host, writer), site, source, planning: false);
        await using var observer = await OpenHistoricalDatabase(direct.ConnectionString);
        await using var relay = outcome == "acknowledgement"
            ? new StockPostgresCommitRelay(direct.Port, direct.Username!,
                Environment.GetEnvironmentVariable("CP6_SPACE_CLONE_FIXED_PORTS") == "true" ? 57195 : 0)
            : null;
        var workerConnection = relay is null ? direct.ConnectionString
            : new NpgsqlConnectionStringBuilder(direct.ConnectionString) { Port = relay.Port }.ConnectionString;
        await using var services = CreateW647CloneServices(workerConnection, writer.Tenant, writer.User);
        await using var scope = services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<ISpaceVersionCloneProcessor>();
        var lease = await ClaimW647Clone(scope.ServiceProvider, reservation.Job);
        var original = await ReadW647CloneWrites(observer, writer.Tenant);

        if (relay is not null)
        {
            relay.Arm();
            var processing = worker.ProcessAsync(lease);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(12));
            var counts = await processing.WaitAsync(TimeSpan.FromSeconds(12));
            var committed = await ReadW647CloneWrites(observer, writer.Tenant);
            await AssertW647CloneCompleted(space, modelId, source, reservation, lease, committed, counts, planning: false);
            await Assert.ThrowsAsync<SpaceJobLeaseLostException>(() => worker.ProcessAsync(lease));
            Assert.Equal(committed, await ReadW647CloneWrites(observer, writer.Tenant));
            await DeviceEvidence("w647-clone-acknowledgement.json", new { Original = original, Committed = committed, Counts = counts, Lease = lease });
            return;
        }

        if (outcome == "cancel")
        {
            await scope.ServiceProvider.GetRequiredService<SpaceJobCoordinator>().RequestCancellationAsync(reservation.Job);
            Assert.Equal(0, (await worker.ProcessAsync(lease)).Total);
        }
        else
        {
            // The late checkpoint INSERT fails after the actual raw clone has run in this transaction.
            await using var add = new NpgsqlCommand("""
                ALTER TABLE public."Space_JobStep" ADD CONSTRAINT w647_clone_step_fixture_failure CHECK ("StepCode" <> 'CloneSnapshot') NOT VALID
                """, observer);
            await add.ExecuteNonQueryAsync();
            try
            {
                for (var attemptNo = 1; attemptNo <= 3; attemptNo++)
                {
                    var error = await Assert.ThrowsAsync<SpaceVersionStateException>(() => worker.ProcessAsync(lease));
                    Assert.Equal("The clone snapshot could not be completed.", error.Message);
                    var failed = await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == reservation.Job);
                    Assert.Equal(SpaceErrorCodes.VersionStateInvalid, failed.LastErrorCode);
                    Assert.Equal(SpaceJobFailureKind.Bug, failed.LastFailureKind);
                    Assert.Equal(attemptNo < 3 ? SpaceJobStatus.Queued : SpaceJobStatus.DeadLetter, failed.Status);
                    var rolledBack = await ReadW647CloneWrites(observer, writer.Tenant);
                    Assert.Empty(rolledBack.Where(row => row.Version == reservation.Version || row.Kind == "step"));
                    await DeviceEvidence($"w647-clone-late-failure-attempt-{attemptNo}.json", new { Original = original, RolledBack = rolledBack, Lease = lease });
                    if (attemptNo == 3) break;
                    await WaitPublishJobDue(observer, failed.NextAttemptAtUtc);
                    lease = await ClaimW647Clone(scope.ServiceProvider, reservation.Job);
                }
            }
            finally
            {
                await using var drop = new NpgsqlCommand("""
                    ALTER TABLE public."Space_JobStep" DROP CONSTRAINT w647_clone_step_fixture_failure
                    """, observer);
                await drop.ExecuteNonQueryAsync();
            }
        }

        var ended = await ReadW647CloneWrites(observer, writer.Tenant);
        var version = await space.Versions.AsNoTracking().SingleAsync(row => row.Id == reservation.Version);
        var model = await space.Models.AsNoTracking().SingleAsync(row => row.Id == modelId);
        var job = await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == reservation.Job);
        var attempt = await space.JobAttempts.AsNoTracking().SingleAsync(row => row.Id == lease.AttemptId);
        Assert.Equal(outcome == "cancel" ? SpaceVersionStatus.Abandoned : SpaceVersionStatus.Failed, version.Status);
        Assert.Equal(outcome == "cancel" ? SpaceJobStatus.Cancelled : SpaceJobStatus.DeadLetter, job.Status);
        Assert.Equal(outcome == "cancel" ? SpaceJobAttemptOutcome.Cancelled : SpaceJobAttemptOutcome.Failed, attempt.Outcome);
        Assert.Null(model.ActiveDraftVersionId);
        Assert.Equal(source, model.CurrentPublishedVersionId);
        var updated = ended.Where(row => row.Id == modelId || row.Id == reservation.Version
            || row.Id == reservation.Job || row.Id == lease.AttemptId).ToArray();
        Assert.Equal(4, updated.Length);
        Assert.Single(updated.Select(row => row.Xmin).Distinct());
        Assert.Empty(ended.Where(row => row.Version == reservation.Version || row.Kind == "step"));
        await DeviceEvidence($"w647-clone-{outcome}-terminal.json", new { Original = original, Terminal = ended, Lease = lease });
    }

    private static async Task<(Guid Model, Guid Source)> SeedW647Published(Harness writer, SpaceContext space, Guid site)
    {
        await space.Database.MigrateAsync();
        // Use the published-history fixture's simulated floor, rack, bin, zone and shape data.
        var seed = await SeedBackendVersions(writer, space, site);
        var model = await space.Models.SingleAsync(row => row.Id == seed.Releases.Model);
        var bootstrap = await space.Versions.SingleAsync(row => row.Id == seed.Releases.Previous);
        var source = await space.Versions.SingleAsync(row => row.Id == seed.Releases.Candidate);
        source.BeginValidation();
        source.MarkReady(new string('b', 64), SpaceValidationRuleSet.Version, SpaceDesignSampleHash);
        source.BeginPublishing();
        source.MarkPublished(writer.User, DateTime.UtcNow);
        bootstrap.MarkSuperseded();
        model.SetPublishedVersion(source, source.ContentHash!);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return (model.Id, source.Id);
    }

    private static Task<AuthenticationApiProcess> StartW647CloneApi(string connection, Guid tenant, Guid site)
    {
        var settings = SpaceDesignSettings(tenant, site);
        if (Environment.GetEnvironmentVariable("CP6_SPACE_CLONE_FIXED_PORTS") == "true")
            settings["ASPNETCORE_URLS"] = "http://127.0.0.1:57196";
        return AuthenticationApiProcess.Start(connection, settings);
    }

    private sealed record W647CloneReservation(Guid Version, Guid Job, Guid? Branch);

    private static async Task<W647CloneReservation> ReserveW647Clone(AuthenticationApiProcess host,
        Dictionary<string, string> actor, Guid site, Guid source, bool planning)
    {
        if (planning)
        {
            var branch = Guid.NewGuid();
            using var created = await host.Send(OperationsRequest(actor, HttpMethod.Put,
                $"/api/space/planning/v1/sites/{site:D}/scenario-branches/{branch:D}",
                new CreateSpacePlanningScenarioBranchRequest(source, "Simulated planning clone")));
            Assert.True(created.StatusCode == HttpStatusCode.OK, await OperationsFailure(created));
            var receipt = (await created.Content.ReadFromJsonAsync<CreateSpacePlanningScenarioBranchResponse>())!.Branch;
            Assert.Equal(branch, receipt.BranchId);
            Assert.Equal(source, receipt.BasePublishedVersionId);
            Assert.True(receipt.ProductionIsolated);
            return new(receipt.ScenarioVersionId, receipt.CloneJobId, branch);
        }

        using var request = OperationsRequest(actor, HttpMethod.Post, $"{SpaceDesignPath}/sites/{site:D}/versions",
            new CreateSpaceVersionRequest("Simulated ordinary clone", source, "PublishedVersion"));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await host.Send(request);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await OperationsFailure(response));
        var reservation = (await response.Content.ReadFromJsonAsync<CreateSpaceVersionResponse>())!;
        Assert.Equal("Initializing", reservation.Status);
        return new(reservation.Id, reservation.JobId, null);
    }

    private static ServiceProvider CreateW647CloneServices(string connection, Guid tenant, Guid actor) =>
        new ServiceCollection()
            .AddScoped<ITenantContext, TenantContext>()
            .AddDbContext<CP6Context>(options => options.UseNpgsql(connection))
            .AddSingleton(TimeProvider.System)
            .AddScoped<ISpaceExecutionContext>(_ => new SpaceDesignExecution(tenant, actor))
            .AddScoped<ISpaceArtifactWriteAuthority, SpaceArtifactWriteAuthority>()
            .AddSpaceDesignV1Persistence(connection, new DatabaseOptions(DatabaseProvider.PostgreSql))
            .BuildServiceProvider();

    private static async Task<SpaceJobLease> ClaimW647Clone(IServiceProvider services, Guid jobId)
    {
        var lease = await services.GetRequiredService<ISpaceJobLeaseStore>().TryClaimNextAsync(
            "w647-clone-worker", SpaceVersionCloneContract.ProcessorVersion, TimeSpan.FromMinutes(2), [SpaceJobType.CloneVersion]);
        Assert.NotNull(lease);
        Assert.Equal(jobId, lease.JobId);
        return lease;
    }

    private static async Task<FenceWait> WaitW647CloneRootOrFloor(NpgsqlConnection observer, int pause, int floor)
    {
        // The second choice records the original processor reaching live business SQL before the pause commits.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var command = new NpgsqlCommand("""
            SELECT pid,usename,wait_event,clock_timestamp(),
                CASE WHEN @pause=ANY(pg_blocking_pids(pid)) THEN @pause ELSE @floor END
            FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND ((@pause=ANY(pg_blocking_pids(pid)) AND query LIKE '%cp6_authentication_v1.acquire%')
                  OR (@floor=ANY(pg_blocking_pids(pid)) AND query LIKE '%Space_FloorRevision%'))
            """, observer);
        command.Parameters.AddWithValue("pause", pause);
        command.Parameters.AddWithValue("floor", floor);
        while (true)
        {
            await using (var rows = await command.ExecuteReaderAsync(deadline.Token))
                if (await rows.ReadAsync(deadline.Token))
                    return new(rows.GetInt32(0), rows.GetInt32(4), rows.GetString(1), rows.GetString(2), rows.GetFieldValue<DateTimeOffset>(3));
            await Task.Delay(35, deadline.Token);
        }
    }

    private static async Task AssertW647CloneCompleted(SpaceContext space, Guid modelId, Guid sourceId,
        W647CloneReservation reservation, SpaceJobLease lease, W647CloneRow[] native, SpaceVersionCloneCounts counts, bool planning)
    {
        var version = await space.Versions.AsNoTracking().SingleAsync(row => row.Id == reservation.Version);
        var model = await space.Models.AsNoTracking().SingleAsync(row => row.Id == modelId);
        var source = await space.Versions.AsNoTracking().SingleAsync(row => row.Id == sourceId);
        var job = await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == reservation.Job);
        var attempt = await space.JobAttempts.AsNoTracking().SingleAsync(row => row.JobId == reservation.Job);
        var step = await space.JobSteps.AsNoTracking().SingleAsync(row => row.AttemptId == lease.AttemptId);
        Assert.Equal(SpaceVersionStatus.Draft, version.Status);
        Assert.Equal(sourceId, version.BasedOnVersionId);
        Assert.Equal(source.ContentRevision, version.ContentRevision);
        Assert.Equal(planning ? SpaceModelVersionPurpose.PlanningScenario : SpaceModelVersionPurpose.Production, version.Purpose);
        Assert.Equal(planning ? null : reservation.Version, model.ActiveDraftVersionId);
        Assert.Equal(sourceId, model.CurrentPublishedVersionId);
        if (planning)
        {
            var branch = await space.PlanningScenarioBranches.AsNoTracking().SingleAsync(row => row.Id == reservation.Branch);
            Assert.Equal(reservation.Job, branch.CloneJobId);
            Assert.Equal(reservation.Version, branch.ScenarioVersionId);
        }
        Assert.Equal(SpaceJobStatus.Succeeded, job.Status);
        Assert.Equal(lease.JobId, job.Id);
        Assert.Equal(lease.AttemptId, attempt.Id);
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Equal("CloneSnapshot", step.StepCode);
        Assert.Equal(source.ContentHash, step.OutputHash);
        Assert.Equal(job.ResultSummaryJson, step.CheckpointJson);
        Assert.Equal(6, counts.Total);
        Assert.Equal(counts.Total, job.ProgressDone);
        var sourceRows = native.Where(row => row.Version == sourceId).ToDictionary(row => row.Kind);
        var targetRows = native.Where(row => row.Version == reservation.Version).ToArray();
        Assert.Equal(6, targetRows.Length);
        foreach (var copy in targetRows)
        {
            var original = sourceRows[copy.Kind];
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal(SnapshotData(original), SnapshotData(copy));
        }

        // PostgreSQL stamps the raw copy and EF's later savepoint with their own xids.
        // Their visibility is checked together at the controlled committed boundary.
        Assert.Single(targetRows.Select(row => row.Xmin).Distinct());
        var metadata = native.Where(row => row.Id == reservation.Version || row.Id == lease.JobId
            || row.Id == lease.AttemptId || row.Id == step.Id).ToArray();
        Assert.Equal(4, metadata.Length);
        Assert.Single(metadata.Select(row => row.Xmin).Distinct());

        static (string Name, string Value)[] SnapshotData(W647CloneRow row)
        {
            using var json = JsonDocument.Parse(row.Data);
            return json.RootElement.EnumerateObject()
                .Where(property => property.Name is not ("Id" or "ModelVersionId" or "RowVersion"
                    or "CreatedAtUtc" or "CreatedBy" or "ModifiedAtUtc" or "ModifiedBy"))
                .Select(property => (property.Name, property.Value.GetRawText())).ToArray();
        }
    }

    private sealed record W647CloneRow(string Kind, Guid Id, Guid? Version, string Xmin, string Data);

    private static async Task<W647CloneRow[]> ReadW647CloneWrites(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 'model', m."Id", null::uuid, m.xmin::text, to_jsonb(m)::text FROM public."Space_Model" m WHERE m."TenantId"=@tenant
            UNION ALL SELECT 'version', v."Id", null, v.xmin::text, to_jsonb(v)::text FROM public."Space_ModelVersion" v WHERE v."TenantId"=@tenant
            UNION ALL SELECT 'branch', b."Id", null, b.xmin::text, to_jsonb(b)::text FROM public."Space_PlanningScenarioBranch" b WHERE b."TenantId"=@tenant
            UNION ALL SELECT 'job', j."Id", null, j.xmin::text, to_jsonb(j)::text FROM public."Space_Job" j WHERE j."TenantId"=@tenant
            UNION ALL SELECT 'attempt', a."Id", null, a.xmin::text, to_jsonb(a)::text FROM public."Space_JobAttempt" a WHERE a."TenantId"=@tenant
            UNION ALL SELECT 'step', s."Id", null, s.xmin::text, to_jsonb(s)::text FROM public."Space_JobStep" s WHERE s."TenantId"=@tenant
            UNION ALL SELECT 'floor', f."Id", f."ModelVersionId", f.xmin::text, to_jsonb(f)::text FROM public."Space_FloorRevision" f WHERE f."TenantId"=@tenant
            UNION ALL SELECT 'zone', z."Id", z."ModelVersionId", z.xmin::text, to_jsonb(z)::text FROM public."Space_ZoneRevision" z WHERE z."TenantId"=@tenant
            UNION ALL SELECT 'rack', r."Id", r."ModelVersionId", r.xmin::text, to_jsonb(r)::text FROM public."Space_RackRevision" r WHERE r."TenantId"=@tenant
            UNION ALL SELECT 'level', l."Id", l."ModelVersionId", l.xmin::text, to_jsonb(l)::text FROM public."Space_RackLevelRevision" l WHERE l."TenantId"=@tenant
            UNION ALL SELECT 'location', l."Id", l."ModelVersionId", l.xmin::text, to_jsonb(l)::text FROM public."Space_LocationRevision" l WHERE l."TenantId"=@tenant
            UNION ALL SELECT 'element', e."Id", e."ModelVersionId", e.xmin::text, to_jsonb(e)::text FROM public."Space_ElementRevision" e WHERE e."TenantId"=@tenant
            ORDER BY 1,2
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<W647CloneRow>();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetString(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetString(4)));
        return rows.ToArray();
    }
}
