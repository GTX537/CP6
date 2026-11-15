using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot, false)]
    [InlineData(SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot, true)]
    [InlineData(SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot, false)]
    [InlineData(SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot, true)]
    [InlineData("validation-finish", false)]
    [InlineData("validation-finish", true)]
    [InlineData(SpaceHistoricalRepublishJobSteps.QueuePublish, false)]
    [InlineData(SpaceHistoricalRepublishJobSteps.QueuePublish, true)]
    public async Task PGT017_space_historical_republish_parent_commit_order(string phase, bool effectFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:rollback", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedHistoricalParentVersions(writer, space, site);
        var queue = phase == SpaceHistoricalRepublishJobSteps.QueuePublish;
        var injectAcknowledgement = queue && effectFirst;
        var signals = NewPublishFixtureRoot();
        await using var host = await StartHistoricalParentHost(connection, writer.Tenant, site, signals, phase, injectAcknowledgement);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var (input, receipt) = await ReserveHistoricalParent(writer, space, execution, site, host, actor, versions);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        await using var control = await OpenHistoricalDatabase(connection);
        await using var effect = await OpenHistoricalDatabase(connection);
        await using var observer = await OpenHistoricalDatabase(connection);
        var work = RunPublishWorkerAsync(signals, 1);
        Assert.Equal(phase, await SpaceArtifactWorkerFixture.ReadAsync<string>(signals, 1, "before-effect"));
        if (phase == "validation-finish")
        {
            space.Issues.Add(SpaceModelIssue.Create(writer.Tenant, receipt.TargetVersionId, null, receipt.JobId,
                SpaceIssueSeverity.Info, "HISTORICAL_FIXTURE_INFO"));
            await space.SaveChangesAsync();
            space.ChangeTracker.Clear();
        }
        var before = await HistoricalParentWriteSet(writer, space, versions, receipt);
        Assert.Equal(SpaceJobStatus.Running, HistoricalParentJob(before, receipt).Status);
        Assert.Empty(before.ChildAttempts);
        Assert.Null(before.Operation.PublishAttemptId);
        Assert.Equal(phase switch
        {
            SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot => SpaceVersionStatus.Initializing,
            SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot => SpaceVersionStatus.Draft,
            "validation-finish" => SpaceVersionStatus.Validating,
            _ => SpaceVersionStatus.Ready,
        }, before.Target.Status);

        FenceWait workerWait;
        FenceWait pauseWait;
        HttpResponseMessage paused;
        HistoricalParentState? beforeAcknowledgement = null;
        HistoricalNativeState? nativeBeforeAcknowledgement = null;
        if (effectFirst)
        {
            var (table, query) = phase switch
            {
                SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot => ("Space_FloorRevision", "%INSERT%Space_FloorRevision%"),
                SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot => ("Space_ModelVersion", "%UPDATE%Space_ModelVersion%"),
                "validation-finish" => ("Space_ModelIssue", "%INSERT%Space_ModelIssue%"),
                _ => ("Space_PublishAttempt", "%INSERT%Space_PublishAttempt%"),
            };
            await using var held = await HoldUnderlayTable(effect, table);
            SpaceArtifactWorkerFixture.Write(signals, 1, "release-effect", true);
            workerWait = await WaitForFence(observer, effect.ProcessID, query, null, work);
            var pause = Pause();
            pauseWait = await WaitForFence(observer, workerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            await held.RollbackAsync();
            if (injectAcknowledgement)
            {
                await SpaceArtifactWorkerFixture.ReadAsync<bool>(signals, 1, "child-committed");
                beforeAcknowledgement = await HistoricalParentWriteSet(writer, space, versions, receipt);
                nativeBeforeAcknowledgement = await ReadHistoricalNativeWrites(observer, writer.Tenant, receipt);
                Assert.Equal(SpaceJobStatus.Running, HistoricalParentJob(beforeAcknowledgement, receipt).Status);
                AssertHistoricalQueue(beforeAcknowledgement, receipt, writer.User);
                AssertSameHistoricalQueueTransaction(nativeBeforeAcknowledgement);
            }
            paused = await pause;
            if (injectAcknowledgement)
                SpaceArtifactWorkerFixture.Write(signals, 1, "release-commit", true);
        }
        else
        {
            await using var held = await HoldUnderlayTenant(control, writer.Tenant);
            var pause = Pause();
            pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            SpaceArtifactWorkerFixture.Write(signals, 1, "release-effect", true);
            workerWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_authentication_v1.acquire%", null, work);
            await held.RollbackAsync();
            paused = await pause;
        }

        using var signedPause = paused;
        Assert.Equal(HttpStatusCode.OK, signedPause.StatusCode);
        Assert.False((await NumberHttpData<TenantManagementReceiptV1>(signedPause)).Snapshot.Enable);
        var ran = await work;
        Assert.Null(ran.Error);
        Assert.Equal(1, ran.Processed);
        var after = await HistoricalParentWriteSet(writer, space, versions, receipt);
        var order = effectFirst ? "effect-first" : "pause-first";
        await DeviceEvidence($"historical-republish-{phase}-{order}.json", new
        {
            Phase = phase, Order = order, Worker = workerWait, Pause = pauseWait, Before = before,
            BeforeAcknowledgement = beforeAcknowledgement, Native = nativeBeforeAcknowledgement, After = after
        });

        if (!effectFirst)
        {
            Assert.Equal(HistoricalBusinessWrites(before), HistoricalBusinessWrites(after));
        }
        else
        {
            switch (phase)
            {
                case SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot:
                    Assert.Equal(SpaceVersionStatus.Draft, after.Target.Status);
                    Assert.Equal(SpaceHistoricalRepublishStatus.SnapshotCloned, after.Operation.Status);
                    AssertHistoricalClonedRows(after);
                    Assert.Empty(after.ValidationRuns);
                    break;
                case SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot:
                    Assert.Equal(SpaceVersionStatus.Validating, after.Target.Status);
                    Assert.Equal(SpaceHistoricalRepublishStatus.SnapshotCloned, after.Operation.Status);
                    Assert.Empty(after.ValidationRuns);
                    break;
                case "validation-finish":
                    Assert.Equal(SpaceVersionStatus.Ready, after.Target.Status);
                    var run = Assert.Single(after.ValidationRuns);
                    Assert.Equal(SpaceValidationStatus.Passed, run.Status);
                    var issue = Assert.Single(after.Issues, row => row.ValidationRunId == run.Id);
                    Assert.Equal("HISTORICAL_FIXTURE_INFO", issue.Code);
                    Assert.Equal(1, run.InfoCount);
                    break;
                default:
                    Assert.Equal(HistoricalBusinessWrites(beforeAcknowledgement!), HistoricalBusinessWrites(after));
                    AssertHistoricalQueue(after, receipt, writer.User);
                    Assert.Equal(SpaceJobStatus.Succeeded, HistoricalParentJob(after, receipt).Status);
                    break;
            }
        }

        var completed = queue && effectFirst;
        if (!completed)
        {
            Assert.Equal(SpaceJobStatus.Queued, HistoricalParentJob(after, receipt).Status);
            Assert.Equal("SPACE_ARTIFACT_TENANT_INACTIVE", HistoricalParentJob(after, receipt).LastErrorCode);
        }
        using var stale = await SendPublishSubmission(host, actor, input);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        var pausedWorker = await RunPublishWorkerAsync(signals, 2);
        Assert.Null(pausedWorker.Error);
        Assert.Equal(0, pausedWorker.Processed);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, signedPause);
        if (!completed)
            await WaitPublishJobDue(observer, HistoricalParentJob(after, receipt).NextAttemptAtUtc);
        var resumed = await RunPublishWorkerAsync(signals, 3);
        Assert.Null(resumed.Error);
        Assert.Equal(completed ? 0 : 1, resumed.Processed);
        var final = await HistoricalParentWriteSet(writer, space, versions, receipt);
        AssertHistoricalCompleted(final, receipt, writer.User);
        Assert.Equal(completed ? 1 : 2, final.JobAttempts.Length);
        if (after.TargetRows.Length > 0)
            Assert.Equal(after.TargetRows, final.TargetRows);
        if (after.Operation.ValidationRunId.HasValue)
            Assert.Equal(after.Operation.ValidationRunId, final.Operation.ValidationRunId);
        if (completed)
            Assert.Equal(HistoricalBusinessWrites(after), HistoricalBusinessWrites(final));

        actor = await Login(host, writer);
        var replayed = await ReadHistoricalRequest(host, actor, input, receipt, final);
        var afterReplay = await HistoricalParentWriteSet(writer, space, versions, receipt);
        Assert.Equal(HistoricalBusinessWrites(final), HistoricalBusinessWrites(afterReplay));
        await DeviceEvidence($"historical-republish-{phase}-{order}-resumed.json", new { Final = final, Replay = replayed,
            Native = await ReadHistoricalNativeWrites(observer, writer.Tenant, receipt) });

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_space_historical_republish_parent_repairs_only_missing_legacy_child_link()
    {
        await using var writer = await Seed();
        await GrantSpaceDesignActions(writer, "model:rollback", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedHistoricalParentVersions(writer, space, site);
        var signals = NewPublishFixtureRoot();
        await using var host = await StartHistoricalParentHost(connection, writer.Tenant, site, signals, null, lostAcknowledgement: true);
        var actor = await Login(host, writer);
        var (input, receipt) = await ReserveHistoricalParent(writer, space, execution, site, host, actor, versions);
        await using var observer = await OpenHistoricalDatabase(connection);

        var work = RunPublishWorkerAsync(signals, 1);
        await SpaceArtifactWorkerFixture.ReadAsync<bool>(signals, 1, "child-committed");
        var committed = await HistoricalParentWriteSet(writer, space, versions, receipt);
        AssertHistoricalQueue(committed, receipt, writer.User);
        AssertSameHistoricalQueueTransaction(await ReadHistoricalNativeWrites(observer, writer.Tenant, receipt));

        // This fixture imports the persisted shape observed in the pinned pre-upgrade database.
        await using (var shape = new NpgsqlCommand("""
            UPDATE public."Space_HistoricalRepublish"
            SET "Status"=@validation, "PublishAttemptId"=NULL
            WHERE "TenantId"=@tenant AND "Id"=@id AND "PublishAttemptId"=@child
            """, observer))
        {
            shape.Parameters.AddWithValue("validation", (short)SpaceHistoricalRepublishStatus.ValidationPassed);
            shape.Parameters.AddWithValue("tenant", writer.Tenant);
            shape.Parameters.AddWithValue("id", receipt.Id);
            shape.Parameters.AddWithValue("child", Assert.Single(committed.ChildAttempts).Id);
            Assert.Equal(1, await shape.ExecuteNonQueryAsync());
        }
        var legacy = await HistoricalParentWriteSet(writer, space, versions, receipt);
        Assert.Null(legacy.Operation.PublishAttemptId);
        Assert.Equal(SpaceVersionStatus.Publishing, legacy.Target.Status);
        var originalWrites = await ReadHistoricalNativeWrites(observer, writer.Tenant, receipt);
        SpaceArtifactWorkerFixture.Write(signals, 1, "release-commit", true);
        Assert.Null((await work).Error);

        var pending = await HistoricalParentWriteSet(writer, space, versions, receipt);
        Assert.Equal(SpaceJobStatus.Queued, HistoricalParentJob(pending, receipt).Status);
        Assert.Equal("SPACE_ARTIFACT_COMMIT_UNKNOWN", HistoricalParentJob(pending, receipt).LastErrorCode);
        await WaitPublishJobDue(observer, HistoricalParentJob(pending, receipt).NextAttemptAtUtc);
        var resumed = await RunPublishWorkerAsync(signals, 2);
        Assert.Null(resumed.Error);
        Assert.Equal(1, resumed.Processed);
        var repaired = await HistoricalParentWriteSet(writer, space, versions, receipt);
        AssertHistoricalCompleted(repaired, receipt, writer.User);
        Assert.Equal(2, repaired.JobAttempts.Length);
        var repairedWrites = await ReadHistoricalNativeWrites(observer, writer.Tenant, receipt);
        Assert.Equal(originalWrites.Rows.Where(row => row.Kind != "parent-operation"),
            repairedWrites.Rows.Where(row => row.Kind != "parent-operation"));
        Assert.NotEqual(Assert.Single(originalWrites.Rows, row => row.Kind == "parent-operation").WriterXmin,
            Assert.Single(repairedWrites.Rows, row => row.Kind == "parent-operation").WriterXmin);

        var replay = await ReadHistoricalRequest(host, actor, input, receipt, repaired);
        await DeviceEvidence("historical-republish-legacy-link-recovery.json",
            new { Committed = committed, Legacy = legacy, Pending = pending, Repaired = repaired,
                OriginalWrites = originalWrites, RepairedWrites = repairedWrites, Replay = replay });
    }

    private static Task<AuthenticationApiProcess> StartHistoricalParentHost(string connection, Guid tenant, Guid site, string root,
        string? phase, bool lostAcknowledgement)
    {
        var settings = SpaceDesignSettings(tenant, site);
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "CP6.Tests";
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpaceHistoricalRepublishWorkerFixture).Assembly.Location;
        settings[SpaceHistoricalRepublishWorkerFixture.RootVariable] = root;
        if (phase is not null)
            settings[SpaceHistoricalRepublishWorkerFixture.PhaseVariable] = phase;
        if (lostAcknowledgement)
            settings[SpaceHistoricalRepublishWorkerFixture.LostAcknowledgementVariable] = "1";
        if (Environment.GetEnvironmentVariable("CP6_SPACE_PUBLISH_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(connection, settings);
    }

    private static async Task<NpgsqlConnection> OpenHistoricalDatabase(string connection)
    {
        var database = new NpgsqlConnection(connection);
        await database.OpenAsync();
        return database;
    }

    private static async Task<(PublishSubmission Input, SpaceHistoricalRepublishDto Receipt)> ReserveHistoricalParent(Harness writer,
        SpaceContext space, SpaceDesignExecution execution, Guid site, AuthenticationApiProcess host, Dictionary<string, string> actor,
        PublishFixtureVersions versions)
    {
        var input = await PreparePublishSubmission(writer, space, execution, site, host, actor, versions, "republish");
        using var accepted = await SendPublishSubmission(host, actor, input);
        Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, accepted.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(accepted));
        Assert.Equal("false", Assert.Single(accepted.Headers.GetValues("Idempotent-Replay")));
        return (input, Assert.IsType<StartSpaceHistoricalRepublishResponse>(
            await accepted.Content.ReadFromJsonAsync<StartSpaceHistoricalRepublishResponse>()).Republish);
    }

    private static async Task<SpaceHistoricalRepublishDto> ReadHistoricalRequest(AuthenticationApiProcess host, Dictionary<string, string> actor,
        PublishSubmission input, SpaceHistoricalRepublishDto receipt, HistoricalParentState final)
    {
        using var response = await SendPublishSubmission(host, actor, input);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, response.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(response));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotent-Replay")));
        var replay = Assert.IsType<StartSpaceHistoricalRepublishResponse>(
            await response.Content.ReadFromJsonAsync<StartSpaceHistoricalRepublishResponse>()).Republish;
        Assert.Equal(receipt.Id, replay.Id);
        Assert.Equal(receipt.JobId, replay.JobId);
        Assert.Equal(receipt.TargetVersionId, replay.TargetVersionId);
        Assert.Equal(final.Operation.ValidationRunId, replay.ValidationRunId);
        Assert.Equal(final.Operation.PublishAttemptId, replay.PublishAttemptId);
        using var read = await host.Send(OperationsRequest(actor, HttpMethod.Get, $"{SpaceDesignPath}/republishes/{receipt.Id:D}"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(replay, await read.Content.ReadFromJsonAsync<SpaceHistoricalRepublishDto>());
        return replay;
    }

    private static SpaceJob HistoricalParentJob(HistoricalParentState state, SpaceHistoricalRepublishDto receipt) =>
        Assert.Single(state.Jobs, row => row.Id == receipt.JobId);

    private static void AssertHistoricalClonedRows(HistoricalParentState state)
    {
        Assert.Equal(6, state.HistoricalRows.Length);
        Assert.Equal(state.HistoricalRows.Select(row => (row.Kind, row.LogicalId)), state.TargetRows.Select(row => (row.Kind, row.LogicalId)));
        Assert.Empty(state.HistoricalRows.Select(row => row.RowId).Intersect(state.TargetRows.Select(row => row.RowId)));
        Assert.Equal(state.Historical.ContentRevision, state.Target.ContentRevision);
    }

    private static void AssertHistoricalQueue(HistoricalParentState state, SpaceHistoricalRepublishDto receipt, Guid actor)
    {
        Assert.Equal(SpaceHistoricalRepublishStatus.PublishQueued, state.Operation.Status);
        Assert.Equal(SpaceVersionStatus.Publishing, state.Target.Status);
        Assert.Equal(receipt.ExpectedPublishedVersionId, state.Model.CurrentPublishedVersionId);
        Assert.Equal(SpaceVersionStatus.Superseded, state.Historical.Status);
        Assert.Equal(SpaceVersionStatus.Published, state.Current.Status);
        AssertHistoricalClonedRows(state);

        var run = Assert.Single(state.ValidationRuns);
        Assert.Equal(state.Operation.ValidationRunId, run.Id);
        Assert.Equal(SpaceValidationStatus.Passed, run.Status);
        Assert.Equal(run.ContentHash, state.Target.ContentHash);
        Assert.Equal(receipt.JobId, run.JobId);
        Assert.Equal(actor, run.RequestedBy);
        var plan = Assert.Single(state.Plans);
        var attempt = Assert.Single(state.ChildAttempts);
        var child = Assert.Single(state.Jobs, row => row.JobType == SpaceJobType.Publish);
        Assert.Equal(state.Operation.PublishAttemptId, attempt.Id);
        Assert.Equal(attempt.JobId, child.Id);
        Assert.Equal(attempt.Id, child.SubjectId);
        Assert.Equal(actor, child.RequestedBy);
        Assert.Equal(SpaceJobStatus.Queued, child.Status);
        Assert.Equal($"historical-republish:{receipt.Id:N}", attempt.BusinessIdempotencyKey);
        Assert.Equal(plan.Id, attempt.PublishPlanId);
        Assert.Equal(plan.PlanHash, child.InputHash);
        Assert.Equal(run.Id, plan.ValidationRunId);
        Assert.Equal(state.Target.Id, plan.TargetVersionId);
        Assert.Equal(receipt.ExpectedPublishedVersionId, plan.BaseVersionId);
        Assert.Equal(new[] { SpacePublishAuditEventType.Queued, SpacePublishAuditEventType.HistoricalRepublishQueued },
            state.Audit.Select(row => row.EventType));
        Assert.Equal(state.Audit[0].EventHash, state.Audit[1].PreviousEventHash);
        Assert.Equal(0, state.RuntimeElements + state.RuntimeLocations + state.WmsBins + state.WmsOperations + state.PublishBatches + state.WmsReceipts);
    }

    private static void AssertHistoricalCompleted(HistoricalParentState state, SpaceHistoricalRepublishDto receipt, Guid actor)
    {
        AssertHistoricalQueue(state, receipt, actor);
        Assert.Equal(SpaceJobStatus.Succeeded, HistoricalParentJob(state, receipt).Status);
        var latest = state.JobAttempts[^1];
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, latest.Outcome);
        var checkpoints = state.Steps.Where(step => step.AttemptId == latest.Id).OrderBy(step => step.StepNo).ToArray();
        Assert.Equal(SpaceHistoricalRepublishJobSteps.All, checkpoints.Select(step => step.StepCode));
        foreach (var checkpoint in checkpoints)
        {
            Assert.NotNull(checkpoint.CheckpointJson);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(checkpoint.CheckpointJson))).ToLowerInvariant(),
                checkpoint.OutputHash);
        }
        using var result = JsonDocument.Parse(checkpoints[^1].CheckpointJson!);
        Assert.Equal(state.Operation.PublishAttemptId, result.RootElement.GetProperty("publishAttemptId").GetGuid());
        Assert.Equal(Assert.Single(state.Jobs, row => row.JobType == SpaceJobType.Publish).Id,
            result.RootElement.GetProperty("publishJobId").GetGuid());
        Assert.Equal(Assert.Single(state.Plans).PlanHash, result.RootElement.GetProperty("planHash").GetString());
    }

    private static void AssertSameHistoricalQueueTransaction(HistoricalNativeState native)
    {
        var queueRows = native.Rows.Where(row => row.Kind is "parent-operation" or "target-version" or "publish-plan" or
            "child-attempt" or "child-job" or "child-audit").ToArray();
        Assert.Equal(7, queueRows.Length);
        Assert.Single(queueRows.Select(row => row.WriterXmin).Distinct());
    }

    private static string HistoricalBusinessWrites(HistoricalParentState state) => JsonSerializer.Serialize(new
    {
        state.Model, state.Historical, state.Current, state.Target, state.Operation, state.HistoricalRows, state.TargetRows,
        state.ValidationRuns, state.Issues, state.Plans, state.ChildAttempts, state.Audit, state.RuntimeFloors,
        state.RuntimeElements, state.RuntimeLocations, state.WmsBins, state.WmsOperations, state.PublishBatches, state.WmsReceipts,
        ChildJobs = state.Jobs.Where(row => row.JobType == SpaceJobType.Publish),
    });

    private static async Task<PublishFixtureVersions> SeedHistoricalParentVersions(Harness writer, SpaceContext space, Guid site)
    {
        var seed = await SeedBackendVersions(writer, space, site);
        var model = await space.Models.SingleAsync(row => row.Id == seed.Releases.Model);
        var bootstrap = await space.Versions.SingleAsync(row => row.Id == seed.Releases.Previous);
        var historical = await space.Versions.SingleAsync(row => row.Id == seed.Releases.Candidate);
        var current = SpaceModelVersion.CreateDraft(writer.Tenant, model.Id, 3, "Current simulated published version", historical.Id);
        var floor = SpaceFloorRevision.Create(writer.Tenant, current.Id, seed.Floor, site, 1, "F1", "Current simulated runtime floor",
            height: 5000);
        floor.ConfigureBoundary("""{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""", "LOCAL_MM_Z_UP");
        space.AddRange(current, floor);
        var now = DateTime.UtcNow;
        PublishFixture(historical, new string('b', 64), now);
        bootstrap.MarkSuperseded();
        model.SetPublishedVersion(historical, historical.ContentHash!);
        await space.SaveChangesAsync();
        model.ReserveDraft(current);
        PublishFixture(current, new string('c', 64), now.AddSeconds(1));
        historical.MarkSuperseded();
        model.SetPublishedVersion(current, current.ContentHash!);
        await space.SaveChangesAsync();
        var runtimeFloor = await writer.Db.Space_Floors.SingleAsync(row => row.Id == seed.Floor);
        runtimeFloor.FloorName = floor.Name;
        await writer.Db.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return new(model.Id, historical.Id, current.Id, current.Id);

        void PublishFixture(SpaceModelVersion version, string hash, DateTime publishedAt)
        {
            version.BeginValidation();
            version.MarkReady(hash, SpaceValidationRuleSet.Version, SpaceDesignSampleHash);
            version.BeginPublishing();
            version.MarkPublished(writer.User, publishedAt);
        }
    }

    private static async Task<HistoricalParentState> HistoricalParentWriteSet(Harness writer, SpaceContext space,
        PublishFixtureVersions versions, SpaceHistoricalRepublishDto receipt)
    {
        var attemptRows = await space.JobAttempts.AsNoTracking().Where(row => row.JobId == receipt.JobId)
            .OrderBy(row => row.AttemptNo).ToArrayAsync();
        var ids = attemptRows.Select(attempt => attempt.Id).ToArray();
        return new(
            await space.Models.AsNoTracking().SingleAsync(row => row.Id == versions.Model),
            await space.Versions.AsNoTracking().SingleAsync(row => row.Id == versions.Previous),
            await space.Versions.AsNoTracking().SingleAsync(row => row.Id == versions.Published),
            await space.Versions.AsNoTracking().SingleAsync(row => row.Id == receipt.TargetVersionId),
            await space.HistoricalRepublishes.AsNoTracking().SingleAsync(row => row.Id == receipt.Id),
            await ReadHistoricalSnapshotRows(space, versions.Previous),
            await ReadHistoricalSnapshotRows(space, receipt.TargetVersionId),
            await space.ValidationRuns.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            await space.Issues.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            await space.PublishPlans.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            await space.PublishAttempts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            await space.PublishAuditEvents.AsNoTracking().OrderBy(row => row.EventNo).ToArrayAsync(),
            await space.Jobs.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            attemptRows,
            await space.JobSteps.AsNoTracking().Where(row => ids.Contains(row.AttemptId)).OrderBy(row => row.StepNo).ToArrayAsync(),
            await writer.Db.Space_Floors.AsNoTracking().OrderBy(row => row.Id).Select(row => $"{row.Id}:{row.FloorCode}:{row.FloorName}").ToArrayAsync(),
            await space.RuntimeElements.CountAsync(),
            await writer.Db.Space_Locations.CountAsync(),
            await writer.Db.WmsBins.CountAsync(),
            await writer.Db.SpaceWmsOperations.CountAsync(),
            await space.PublishBatches.CountAsync(),
            await space.WmsReceipts.CountAsync());
    }

    private static async Task<HistoricalCloneRow[]> ReadHistoricalSnapshotRows(SpaceContext space, Guid versionId)
    {
        var rows = await space.FloorRevisions.AsNoTracking().Where(row => row.ModelVersionId == versionId)
            .Select(row => new HistoricalCloneRow("floor", row.Id, row.LogicalId)).ToListAsync();
        rows.AddRange(await space.ZoneRevisions.AsNoTracking().Where(row => row.ModelVersionId == versionId)
            .Select(row => new HistoricalCloneRow("zone", row.Id, row.LogicalId)).ToArrayAsync());
        rows.AddRange(await space.RackRevisions.AsNoTracking().Where(row => row.ModelVersionId == versionId)
            .Select(row => new HistoricalCloneRow("rack", row.Id, row.LogicalId)).ToArrayAsync());
        rows.AddRange(await space.RackLevelRevisions.AsNoTracking().Where(row => row.ModelVersionId == versionId)
            .Select(row => new HistoricalCloneRow("level", row.Id, row.LogicalId)).ToArrayAsync());
        rows.AddRange(await space.LocationRevisions.AsNoTracking().Where(row => row.ModelVersionId == versionId)
            .Select(row => new HistoricalCloneRow("location", row.Id, row.LogicalId)).ToArrayAsync());
        rows.AddRange(await space.ElementRevisions.AsNoTracking().Where(row => row.ModelVersionId == versionId)
            .Select(row => new HistoricalCloneRow("element", row.Id, row.LogicalId)).ToArrayAsync());
        return rows.OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.LogicalId).ToArray();
    }

    private static async Task<HistoricalNativeState> ReadHistoricalNativeWrites(NpgsqlConnection native, Guid tenant,
        SpaceHistoricalRepublishDto receipt)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 'parent-operation', "Id", xmin::text FROM public."Space_HistoricalRepublish" WHERE "Id"=@operation AND "TenantId"=@tenant
            UNION ALL SELECT 'target-version', "Id", xmin::text FROM public."Space_ModelVersion" WHERE "Id"=@target AND "TenantId"=@tenant
            UNION ALL SELECT 'target-floor', "Id", xmin::text FROM public."Space_FloorRevision" WHERE "ModelVersionId"=@target AND "TenantId"=@tenant
            UNION ALL SELECT 'validation-run', "Id", xmin::text FROM public."Space_ValidationRun" WHERE "ModelVersionId"=@target AND "TenantId"=@tenant
            UNION ALL SELECT 'publish-plan', "Id", xmin::text FROM public."Space_PublishPlan" WHERE "TargetVersionId"=@target AND "TenantId"=@tenant
            UNION ALL SELECT 'child-attempt', "Id", xmin::text FROM public."Space_PublishAttempt" WHERE "TargetVersionId"=@target AND "TenantId"=@tenant
            UNION ALL SELECT 'child-job', j."Id", j.xmin::text FROM public."Space_Job" j
                JOIN public."Space_PublishAttempt" a ON a."JobId"=j."Id" AND a."TenantId"=j."TenantId"
                WHERE a."TargetVersionId"=@target AND j."TenantId"=@tenant
            UNION ALL SELECT 'child-audit', e."Id", e.xmin::text FROM public."Space_PublishAuditEvent" e
                JOIN public."Space_PublishAttempt" a ON a."Id"=e."AttemptId" AND a."TenantId"=e."TenantId"
                WHERE a."TargetVersionId"=@target AND e."TenantId"=@tenant
            ORDER BY 1,2
            """, native);
        command.Parameters.AddWithValue("operation", receipt.Id);
        command.Parameters.AddWithValue("target", receipt.TargetVersionId);
        command.Parameters.AddWithValue("tenant", tenant);
        var observedAt = await BackendObservationTime(native);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<HistoricalNativeRow>();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));
        return new(observedAt, rows.ToArray());
    }

    private sealed record HistoricalCloneRow(string Kind, Guid RowId, Guid LogicalId);
    private sealed record HistoricalNativeRow(string Kind, Guid RowId, string WriterXmin);
    private sealed record HistoricalNativeState(DateTimeOffset ObservedAt, HistoricalNativeRow[] Rows);
    private sealed record HistoricalParentState(SpaceModel Model, SpaceModelVersion Historical, SpaceModelVersion Current,
        SpaceModelVersion Target, SpaceHistoricalRepublish Operation, HistoricalCloneRow[] HistoricalRows, HistoricalCloneRow[] TargetRows,
        SpaceValidationRun[] ValidationRuns, SpaceModelIssue[] Issues, SpacePublishPlan[] Plans,
        SpacePublishAttempt[] ChildAttempts, SpacePublishAuditEvent[] Audit, SpaceJob[] Jobs,
        SpaceJobAttempt[] JobAttempts, SpaceJobStep[] Steps, string[] RuntimeFloors, int RuntimeElements, int RuntimeLocations,
        int WmsBins, int WmsOperations, int PublishBatches, int WmsReceipts);
}
