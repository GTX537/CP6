using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Erp;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

[assembly: HostingStartup(typeof(SpacePublishNoEffectFixture))]

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("capability", "pause-first")]
    [InlineData("capability", "effect-first")]
    [InlineData("preflight", "pause-first")]
    [InlineData("preflight", "effect-first")]
    [InlineData("rejected", "pause-first")]
    [InlineData("rejected", "effect-first")]
    [InlineData("capability", "commit-ack-lost")]
    public async Task PGT017_space_publish_no_effect_writer_retains_original_result(string rejection, string order)
    {
        await using var writer = await Seed();
        await using var operatorAccount = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:publish", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedBackendVersions(writer, space, site);

        var root = NewPublishFixtureRoot();
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "CP6.Tests";
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpacePublishNoEffectFixture).Assembly.Location;
        settings[SpacePublishNoEffectFixture.RootVariable] = root;
        settings[SpacePublishNoEffectFixture.RejectionVariable] = rejection;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_PUBLISH_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        var hostConnection = new NpgsqlConnectionStringBuilder(connection);
        var relayPort = int.TryParse(Environment.GetEnvironmentVariable("CP6_SPACE_PUBLISH_NO_EFFECT_RELAY_PORT"),
            out var reserved) ? reserved : 0;
        await using var relay = order == "commit-ack-lost"
            ? new StockPostgresCommitRelay(hostConnection.Port, hostConnection.Username!, relayPort)
            : null;
        if (relay is not null)
        {
            hostConnection.Port = relay.Port;
            hostConnection.SslMode = SslMode.Disable;
        }

        await using var host = await AuthenticationApiProcess.Start(hostConnection.ConnectionString, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, operatorAccount);
        var input = await PreparePublishSubmission(writer, space, execution, site, host, actor, versions.Releases, "publish");
        using var queued = await SendPublishSubmission(host, actor, input);
        var ids = await CheckPublishSubmission(space, writer, versions.Releases, input, "publish", queued, replay: false);
        var initial = await BackendWriteSet(writer, space, ids);
        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
        Assert.True(active.Enable);

        await using var control = await OpenHistoricalDatabase(connection);
        await using var observer = await OpenHistoricalDatabase(connection);
        var work = RunPublishWorkerAsync(root, 1);
        var observation = await SpaceArtifactWorkerFixture.ReadAsync<SpacePublishNoEffectFixture.Observation>(root, 1, "rejection");
        Assert.Equal(initial.Plan.CapabilityHash, observation.CapabilityHash);
        if (rejection == "capability")
            Assert.NotEqual(observation.CapabilityHash, observation.ObservedCapabilityHash);
        var selected = await BackendWriteSet(writer, space, ids);
        Assert.Equal(SpaceJobStatus.Running, selected.Job.Status);
        Assert.Equal(ids.Job, Assert.Single(selected.JobAttempts).JobId);
        Assert.Equal(rejection == "rejected" ? SpacePublishAttemptStatus.ApplyingWms : SpacePublishAttemptStatus.Preflighting,
            selected.Attempt.Status);

        FenceWait? ownerWait = null;
        FenceWait? pauseWait = null;
        BackendSnapshot held;
        Task<HttpResponseMessage> pause;
        if (order == "effect-first")
        {
            await using var barrier = await HoldUnderlayTable(control, "Space_ModelVersion");
            ReleaseProvider();
            ownerWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Space_ModelVersion%", null, work);
            pause = Pause();
            pauseWait = await WaitForFence(observer, ownerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            held = await BackendWriteSet(writer, space, ids);
            await barrier.RollbackAsync();
        }
        else if (order == "pause-first")
        {
            await using var barrier = await HoldUnderlayTenant(control, writer.Tenant);
            pause = Pause();
            pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            ReleaseProvider();
            ownerWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_authentication_v1.acquire%", null, work);
            held = await BackendWriteSet(writer, space, ids);
            await barrier.RollbackAsync();
        }
        else
        {
            held = selected;
            relay!.Arm();
            ReleaseProvider();
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(15));
            await work;
            pause = Pause();
        }

        Assert.Equal(SpaceVersionStatus.Publishing, held.Versions.Single(row => row.Id == versions.Releases.Candidate).Status);
        Assert.DoesNotContain(held.Audit, row => row.EventType == SpacePublishAuditEventType.FailedNoEffect);
        if (rejection == "rejected")
        {
            var batch = Assert.Single(held.Batches);
            Assert.Equal(SpacePublishBatchStatus.FailedNoEffect, batch.Status);
            Assert.Equal((observation.OperationKey, observation.PayloadHash), (batch.OperationKey, batch.PayloadHash));
            Assert.NotEmpty(held.Receipts);
            Assert.All(held.Receipts, row =>
            {
                Assert.Equal(SpaceWmsReceiptOutcome.NotApplied, row.Outcome);
                Assert.Equal(SpacePublishNoEffectFixture.RejectedCode, row.ErrorCode);
            });
            Assert.Single(held.Audit, row => row.EventType == SpacePublishAuditEventType.WmsApplyObserved);
        }
        else
        {
            Assert.Empty(held.Receipts);
        }

        using var signedPause = await pause;
        var stopped = await NumberHttpData<TenantManagementReceiptV1>(signedPause);
        Assert.False(stopped.Snapshot.Enable);
        var worker = await work;
        Assert.Null(worker.Error);
        Assert.Equal(1, worker.Processed);
        var whilePaused = await BackendWriteSet(writer, space, ids);
        if (order == "pause-first")
        {
            Assert.Equal(SpacePublishAttemptStatus.WaitingRetry, whilePaused.Attempt.Status);
            Assert.Equal("SPACE_ARTIFACT_TENANT_INACTIVE", whilePaused.Attempt.LastErrorCode);
            Assert.True(whilePaused.Attempt.OwnsPublishSlot);
            Assert.Null(whilePaused.Attempt.FinishedAtUtc);
            Assert.Equal(SpaceJobStatus.Queued, whilePaused.Job.Status);
            Assert.Equal(SpaceJobAttemptOutcome.Failed, Assert.Single(whilePaused.JobAttempts).Outcome);
            Assert.DoesNotContain(whilePaused.Audit, row => row.EventType == SpacePublishAuditEventType.FailedNoEffect);
            Assert.Equal(JsonSerializer.Serialize(held.Versions), JsonSerializer.Serialize(whilePaused.Versions));
            Assert.Equal(NoEffectBatchState(held), NoEffectBatchState(whilePaused));
        }
        Assert.Equal(NoEffectPhysicalState(initial), NoEffectPhysicalState(whilePaused));
        using var oldSession = await SendPublishSubmission(host, actor, input);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);

        await ResumeSourceUploadTenant(host, manager, writer.Tenant, signedPause);
        if (order == "pause-first")
            await WaitPublishJobDue(observer, whilePaused.Job.NextAttemptAtUtc);
        var resumed = await RunPublishWorkerAsync(root, 2);
        Assert.Null(resumed.Error);
        Assert.Equal(order == "pause-first" ? 1 : 0, resumed.Processed);
        await AssertOriginalPublishReplay(host, writer, input, ids);
        var final = await BackendWriteSet(writer, space, ids);
        var target = Assert.Single(final.Versions, row => row.Id == versions.Releases.Candidate);
        Assert.Equal(SpaceVersionStatus.Ready, target.Status);
        Assert.Equal(JsonSerializer.Serialize(initial.Model), JsonSerializer.Serialize(final.Model));
        Assert.Equal(JsonSerializer.Serialize(initial.Versions.Single(row => row.Id == versions.Releases.Published)),
            JsonSerializer.Serialize(final.Versions.Single(row => row.Id == versions.Releases.Published)));
        Assert.Equal(initial.Plan.Id, final.Plan.Id);
        Assert.Equal(initial.Plan.PlanHash, final.Plan.PlanHash);
        Assert.Equal((ids.Operation, ids.Job, ids.PlanOrTarget, target.Id),
            (final.Attempt.Id, final.Attempt.JobId!.Value, final.Attempt.PublishPlanId, final.Attempt.TargetVersionId));
        Assert.Equal(SpacePublishAttemptStatus.FailedNoEffect, final.Attempt.Status);
        Assert.Equal(rejection == "capability" ? SpaceErrorCodes.WmsCapabilityMissing : SpacePublishNoEffectFixture.RejectedCode,
            final.Attempt.LastErrorCode);
        Assert.False(final.Attempt.OwnsPublishSlot);
        Assert.Null(final.Attempt.WmsCommittedAtUtc);
        Assert.Null(final.Attempt.RuntimeActivatedAtUtc);
        Assert.Equal(ids.Job, final.Job.Id);
        Assert.Equal(ids.Operation, final.Job.SubjectId);
        Assert.Equal(SpaceJobStatus.Succeeded, final.Job.Status);
        Assert.Equal(Assert.Single(selected.JobAttempts).Id, final.JobAttempts[0].Id);
        Assert.Equal(order == "pause-first" ? 2 : 1, final.JobAttempts.Length);
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, final.JobAttempts[^1].Outcome);

        var audit = Assert.Single(final.Audit, row => row.EventType == SpacePublishAuditEventType.FailedNoEffect);
        Assert.Equal((ids.Operation, ids.Job), (audit.AttemptId, audit.JobId));
        Assert.Equal(final.Attempt.LastErrorCode, audit.ErrorCode);
        for (var index = 1; index < final.Audit.Length; index++)
            Assert.Equal(final.Audit[index - 1].EventHash, final.Audit[index].PreviousEventHash);
        using (var evidence = JsonDocument.Parse(audit.EvidenceJson))
            Assert.False(evidence.RootElement.GetProperty("externalEffect").GetBoolean());
        var transaction = await PublishNativeWrites(observer, target.Id, ids.Operation, audit.Id);
        Assert.Single(transaction.Values.Distinct());

        if (order != "pause-first")
        {
            Assert.Equal(SpacePublishAttemptStatus.FailedNoEffect, whilePaused.Attempt.Status);
            Assert.Equal(JsonSerializer.Serialize(whilePaused), JsonSerializer.Serialize(final));
        }
        Assert.Equal(NoEffectPhysicalState(initial), NoEffectPhysicalState(final));
        var applyCalls = Path.Combine(root, SpacePublishNoEffectFixture.ApplyCallsFile);
        if (rejection == "rejected")
        {
            Assert.Equal(observation.OperationKey, Assert.Single(await File.ReadAllLinesAsync(applyCalls)));
            Assert.Equal(NoEffectBatchState(held), NoEffectBatchState(final));
        }
        else
        {
            Assert.False(File.Exists(applyCalls));
            Assert.Empty(final.Receipts);
            if (rejection == "capability")
                Assert.Empty(final.Batches);
            else
                Assert.Equal(SpacePublishBatchStatus.Pending, Assert.Single(final.Batches).Status);
        }

        await DeviceEvidence($"space-publish-no-effect-{rejection}-{order}.json", new
        {
            Order = order, OwnerWait = ownerWait, PauseWait = pauseWait, Rejection = observation,
            Initial = initial, Selected = selected, BeforeFirstCommit = held, Paused = whilePaused,
            Terminal = final, TerminalTransaction = transaction, Pause = stopped.Snapshot,
            CommitAcknowledgementLost = relay is not null, OriginalKeysRecovered = true,
        });

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));

        void ReleaseProvider() => SpaceArtifactWorkerFixture.Write(root, 1, "rejection-continue", true);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_space_publish_reconcile_reopen_shares_native_authority(bool effectFirst)
    {
        await using var writer = await Seed();
        await using var operatorAccount = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:publish", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedBackendVersions(writer, space, site);
        var root = NewPublishFixtureRoot();
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "CP6.Tests";
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpacePublishNoEffectFixture).Assembly.Location;
        settings[SpacePublishNoEffectFixture.RootVariable] = root;
        settings[SpacePublishNoEffectFixture.RejectionVariable] = "reconcile";
        if (Environment.GetEnvironmentVariable("CP6_SPACE_PUBLISH_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, operatorAccount);
        var submission = await PreparePublishSubmission(writer, space, execution, site, host, actor, versions.Releases, "publish");
        using var queued = await SendPublishSubmission(host, actor, submission);
        var originalIds = await CheckPublishSubmission(space, writer, versions.Releases, submission, "publish", queued, replay: false);

        var original = await RunPublishWorkerAsync(root, 1);
        Assert.Null(original.Error);
        Assert.Equal(1, original.Processed);
        var restricted = await BackendWriteSet(writer, space, originalIds);
        var originalBatch = Assert.Single(restricted.Batches);
        Assert.Equal(SpacePublishBatchStatus.Partial, originalBatch.Status);
        Assert.Equal(SpacePublishAttemptStatus.ReconciliationRequired, restricted.Attempt.Status);
        Assert.Equal(SpaceVersionStatus.ReconciliationRequired, restricted.Versions.Single(row => row.Id == versions.Releases.Candidate).Status);
        Assert.Equal(SpaceJobStatus.Succeeded, restricted.Job.Status);
        Assert.Empty(restricted.Receipts);
        Assert.Empty(restricted.WmsOperations);
        Assert.Empty(restricted.WmsBins);
        var issue = await space.ReconciliationIssues.AsNoTracking().SingleAsync(row => row.AttemptId == originalIds.Operation);
        Assert.Equal(SpaceReconciliationClassification.WmsPartial, issue.Classification);

        var retry = new PublishSubmission($"{SpaceDesignPath}/publish-attempts/{originalIds.Operation:D}/retry", Guid.NewGuid().ToString("N"),
            new RetrySpacePublishAttemptRequest("Resume after verifying the staged operation", "The isolated WMS has no committed location yet."));
        using var accepted = await SendPublishSubmission(host, actor, retry);
        Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await OperationsFailure(accepted));
        Assert.Equal("false", Assert.Single(accepted.Headers.GetValues("Idempotent-Replay")));
        var receipt = Assert.IsType<RetrySpacePublishAttemptResponse>(await accepted.Content.ReadFromJsonAsync<RetrySpacePublishAttemptResponse>());
        var ids = new PublishSubmissionIds(receipt.Attempt.Id, receipt.Attempt.JobId!.Value, receipt.Attempt.PublishPlanId);
        var initial = await BackendWriteSet(writer, space, ids);
        Assert.Equal((originalIds.Operation, originalIds.PlanOrTarget), (ids.Operation, ids.PlanOrTarget));
        Assert.Equal(initial.Job.SubjectId, ids.Operation);
        Assert.Equal(originalIds.Job, initial.Job.RetryOfJobId);
        Assert.Equal(SpaceJobType.Reconcile, initial.Job.JobType);
        Assert.Equal(SpaceJobStatus.Queued, initial.Job.Status);
        Assert.Equal(SpacePublishStep.Reconcile, initial.Attempt.CurrentStep);
        Assert.Equal(SpacePublishAuditEventType.ManualRetryRequested, initial.Audit[^1].EventType);
        Assert.Equal(SpaceReconciliationStatus.Investigating,
            (await space.ReconciliationIssues.AsNoTracking().SingleAsync(row => row.Id == issue.Id)).Status);
        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);

        await using var control = await OpenHistoricalDatabase(connection);
        await using var observer = await OpenHistoricalDatabase(connection);
        var running = RunPublishWorkerAsync(root, 2);
        var claim = await SpaceArtifactWorkerFixture.ReadAsync<SpacePublishNoEffectFixture.Claim>(root, 2, "reconcile-admitted");
        Assert.Equal(ids.Job, claim.JobId);
        var selected = await BackendWriteSet(writer, space, ids);
        Assert.Equal(claim.AttemptId, Assert.Single(selected.JobAttempts).Id);

        FenceWait ownerWait;
        FenceWait pauseWait;
        BackendSnapshot held;
        Task<HttpResponseMessage> pause;
        if (effectFirst)
        {
            await using var barrier = await HoldUnderlayTable(control, "Space_ModelVersion");
            Continue(2, "reconcile-admitted");
            ownerWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Space_ModelVersion%", null, running);
            pause = Pause();
            pauseWait = await WaitForFence(observer, ownerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            held = await BackendWriteSet(writer, space, ids);
            await barrier.RollbackAsync();
            Assert.Equal(site, await SpaceArtifactWorkerFixture.ReadAsync<Guid>(root, 2, "warehouse"));
        }
        else
        {
            await using var barrier = await HoldUnderlayTenant(control, writer.Tenant);
            pause = Pause();
            pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            Continue(2, "reconcile-admitted");
            ownerWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_authentication_v1.acquire%", null, running);
            held = await BackendWriteSet(writer, space, ids);
            await barrier.RollbackAsync();
        }

        Assert.Equal(JsonSerializer.Serialize(selected.Audit), JsonSerializer.Serialize(held.Audit));
        Assert.Equal(JsonSerializer.Serialize(selected.Versions), JsonSerializer.Serialize(held.Versions));
        using var signedPause = await pause;
        Assert.False((await NumberHttpData<TenantManagementReceiptV1>(signedPause)).Snapshot.Enable);

        if (!effectFirst)
        {
            var deferred = await running;
            Assert.Null(deferred.Error);
            Assert.Equal(1, deferred.Processed);
        }
        var whilePaused = await BackendWriteSet(writer, space, ids);
        Assert.Equal(NoEffectBatchState(restricted), NoEffectBatchState(whilePaused));
        Assert.Equal(NoEffectPhysicalState(restricted), NoEffectPhysicalState(whilePaused));
        Assert.Equal(effectFirst ? SpaceVersionStatus.Publishing : SpaceVersionStatus.ReconciliationRequired,
            whilePaused.Versions.Single(row => row.Id == versions.Releases.Candidate).Status);
        Assert.Equal(effectFirst ? SpacePublishAttemptStatus.Preflighting : SpacePublishAttemptStatus.WaitingRetry,
            whilePaused.Attempt.Status);
        if (!effectFirst)
        {
            Assert.Equal(SpaceJobStatus.Queued, whilePaused.Job.Status);
            Assert.Equal("SPACE_ARTIFACT_TENANT_INACTIVE", whilePaused.Attempt.LastErrorCode);
            Assert.DoesNotContain(whilePaused.Audit, row => row.JobId == ids.Job && row.EventType == SpacePublishAuditEventType.ProcessingStarted);
        }

        var sequence = 2;
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, signedPause);
        if (!effectFirst)
        {
            sequence = 3;
            await WaitPublishJobDue(observer, whilePaused.Job.NextAttemptAtUtc);
            running = RunPublishWorkerAsync(root, sequence);
            var resumed = await SpaceArtifactWorkerFixture.ReadAsync<SpacePublishNoEffectFixture.Claim>(root, sequence, "reconcile-admitted");
            Assert.Equal(ids.Job, resumed.JobId);
            Assert.NotEqual(claim.AttemptId, resumed.AttemptId);
            Continue(sequence, "reconcile-admitted");
            Assert.Equal(site, await SpaceArtifactWorkerFixture.ReadAsync<Guid>(root, sequence, "warehouse"));
        }

        var reopened = await BackendWriteSet(writer, space, ids);
        Assert.Equal(SpaceVersionStatus.Publishing, reopened.Versions.Single(row => row.Id == versions.Releases.Candidate).Status);
        Assert.Equal(SpacePublishAttemptStatus.Preflighting, reopened.Attempt.Status);
        Assert.Equal(NoEffectPhysicalState(restricted), NoEffectPhysicalState(reopened));
        var started = Assert.Single(reopened.Audit, row => row.JobId == ids.Job && row.EventType == SpacePublishAuditEventType.ProcessingStarted);
        Assert.Equal(reopened.Audit[^2].EventHash, started.PreviousEventHash);
        var transaction = await PublishNativeWrites(observer, versions.Releases.Candidate, ids.Operation, started.Id);
        Assert.Single(transaction.Values.Distinct());
        Continue(sequence, "warehouse");

        var complete = await running;
        Assert.Null(complete.Error);
        Assert.Equal(1, complete.Processed);
        var final = await BackendWriteSet(writer, space, ids);
        AssertCompletedNativePublish(final, versions);
        Assert.Equal(effectFirst ? 1 : 2, final.JobAttempts.Length);
        Assert.Equal(originalBatch.Id, Assert.Single(final.Batches).Id);
        Assert.Equal((originalBatch.OperationKey, originalBatch.PayloadHash), (final.Batches[0].OperationKey, final.Batches[0].PayloadHash));
        Assert.Equal(SpaceReconciliationStatus.Resolved,
            (await space.ReconciliationIssues.AsNoTracking().SingleAsync(row => row.AttemptId == ids.Operation && row.Id == issue.Id)).Status);
        using var replay = await SendPublishSubmission(host, await Login(host, writer), retry);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replay")));
        var reread = Assert.IsType<RetrySpacePublishAttemptResponse>(await replay.Content.ReadFromJsonAsync<RetrySpacePublishAttemptResponse>());
        Assert.Equal((ids.Operation, ids.Job, ids.PlanOrTarget), (reread.Attempt.Id, reread.Attempt.JobId!.Value, reread.Attempt.PublishPlanId));
        Assert.Equal(JsonSerializer.Serialize(final), JsonSerializer.Serialize(await BackendWriteSet(writer, space, ids)));
        await DeviceEvidence($"space-publish-reconcile-reopen-{(effectFirst ? "effect-first" : "pause-first")}.json", new
        {
            Original = restricted, Issue = issue, OriginalIds = originalIds, ReconcileIds = ids, OwnerWait = ownerWait, PauseWait = pauseWait,
            Claim = claim, BeforeFirstCommit = held, Paused = whilePaused, Reopened = reopened, ReopenTransaction = transaction, Terminal = final,
        });

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));

        void Continue(int number, string gate) => SpaceArtifactWorkerFixture.Write(root, number, $"{gate}-continue", true);
    }

    private static string NoEffectBatchState(BackendSnapshot snapshot) =>
        JsonSerializer.Serialize(new { snapshot.Batches, snapshot.Receipts });

    private static string NoEffectPhysicalState(BackendSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        snapshot.WmsBins, snapshot.WmsOperations, snapshot.Floors, snapshot.Locations, snapshot.Elements,
    });

    private static async Task<Dictionary<string, string>> PublishNativeWrites(NpgsqlConnection observer, Guid version, Guid attempt, Guid audit)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 'version', xmin::text FROM public."Space_ModelVersion" WHERE "Id"=@version
            UNION ALL SELECT 'attempt', xmin::text FROM public."Space_PublishAttempt" WHERE "Id"=@attempt
            UNION ALL SELECT 'audit', xmin::text FROM public."Space_PublishAuditEvent" WHERE "Id"=@audit
            """, observer);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("attempt", attempt);
        command.Parameters.AddWithValue("audit", audit);
        await using var rows = await command.ExecuteReaderAsync();
        var result = new Dictionary<string, string>();
        while (await rows.ReadAsync())
            result.Add(rows.GetString(0), rows.GetString(1));
        Assert.Equal(3, result.Count);
        return result;
    }
}

public sealed class SpacePublishNoEffectFixture : IHostingStartup
{
    internal const string RootVariable = "CP6_SPACE_PUBLISH_NO_EFFECT_ROOT";
    internal const string RejectionVariable = "CP6_SPACE_PUBLISH_NO_EFFECT_REJECTION";
    internal const string ApplyCallsFile = "apply-calls.txt";
    internal const string RejectedCode = "WMS_TEST_REQUEST_REJECTED";
    internal sealed record Observation(string CapabilityHash, string? ObservedCapabilityHash = null,
        string? OperationKey = null, string? PayloadHash = null);
    internal sealed record Claim(Guid JobId, Guid AttemptId);

    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        if (Environment.GetEnvironmentVariable(RootVariable) is { } root)
        {
            var tenant = Guid.Parse(Environment.GetEnvironmentVariable("Space__Compatibility__Sites__0__TenantId")!);
            var rejection = Environment.GetEnvironmentVariable(RejectionVariable)!;
            services.AddSingleton<IStartupFilter>(new ControlledWorkerStartup(root, tenant, rejection));
        }
    });

    private sealed class ControlledWorkerStartup(string root, Guid tenant, string rejection) : IStartupFilter
    {
        Action<IApplicationBuilder> IStartupFilter.Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() => _ = RunAsync(app.ApplicationServices, lifetime.ApplicationStopping));
        };

        private async Task RunAsync(IServiceProvider services, CancellationToken ct)
        {
            for (var sequence = 1; !ct.IsCancellationRequested; sequence++)
            {
                try
                {
                    await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, sequence, "request", ct);
                    using var worker = new SpacePublishJobWorker(
                        new ControlledWorkerScopeFactory(services.GetRequiredService<IServiceScopeFactory>(), root, tenant, rejection, sequence),
                        services.GetRequiredService<ILogger<SpacePublishJobWorker>>());
                    var processed = await worker.ProcessOnceAsync(ct);
                    SpaceArtifactWorkerFixture.Write(root, sequence, "result", new SpacePublishWorkerFixture.WorkerResult(processed, null));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    SpaceArtifactWorkerFixture.Write(root, sequence, "result", new SpacePublishWorkerFixture.WorkerResult(-1, error.ToString()));
                }
            }
        }
    }

    // Keep the real worker, lease ledger and orchestrator. Only the selected worker's WMS response is simulated.
    private sealed class ControlledWorkerScopeFactory(IServiceScopeFactory inner, string root, Guid tenant, string rejection, int sequence)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new WorkerScope(inner.CreateScope(), root, tenant, rejection, sequence);

        private sealed class WorkerScope(IServiceScope scope, string root, Guid tenant, string rejection, int sequence)
            : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type type)
            {
                var services = scope.ServiceProvider;
                if (type == typeof(ITenantEnumerator))
                    return new SelectedTenant(services.GetRequiredService<ITenantEnumerator>(), tenant);
                if (type == typeof(ISpaceJobProcessorRunner))
                {
                    var recovering = rejection == "reconcile" && sequence > 1;
                    var executor = recovering
                        ? ActivatorUtilities.CreateInstance<SpacePublishOrchestrator>(services,
                            new ReconcileWarehouseBarrier(services.GetRequiredService<ISpaceWarehouseResolver>(), root, sequence))
                        : ActivatorUtilities.CreateInstance<SpacePublishOrchestrator>(services,
                            new RejectingAdapter(ActivatorUtilities.CreateInstance<Cp6SpaceWmsAdapter>(services), root, rejection, sequence));
                    var processors = services.GetServices<ISpaceJobProcessor>()
                        .Select(processor => processor.JobType switch
                        {
                            SpaceJobType.Publish => new SpacePublishJobProcessor(executor),
                            SpaceJobType.Reconcile when recovering => new ReconcileAdmissionProcessor(
                                new SpacePublishReconciliationJobProcessor(executor), root, sequence),
                            _ => processor,
                        }).ToArray();
                    return ActivatorUtilities.CreateInstance<SpaceJobProcessorRunner>(services, (object)processors);
                }
                return services.GetService(type);
            }

            public void Dispose() => scope.Dispose();
        }

        private sealed class SelectedTenant(ITenantEnumerator inner, Guid tenant) : ITenantEnumerator
        {
            public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default) =>
                (await inner.ListActiveAsync(ct)).Where(value => value == tenant).ToArray();
        }
    }

    private sealed class ReconcileAdmissionProcessor(SpacePublishReconciliationJobProcessor inner, string root, int sequence) : ISpaceJobProcessor
    {
        public SpaceJobType JobType => inner.JobType;
        public SpaceJobSubjectType SubjectType => inner.SubjectType;
        public string ProcessorVersion => inner.ProcessorVersion;
        public IReadOnlyList<string> StepCodes => inner.StepCodes;

        public async Task<SpaceJobStepOutput> ExecuteStepAsync(SpaceJobStepExecution execution, CancellationToken ct = default)
        {
            SpaceArtifactWorkerFixture.Write(root, sequence, "reconcile-admitted", new Claim(execution.Lease.JobId, execution.Lease.AttemptId));
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, sequence, "reconcile-admitted-continue", ct);
            return await inner.ExecuteStepAsync(execution, ct);
        }
    }

    private sealed class ReconcileWarehouseBarrier(ISpaceWarehouseResolver inner, string root, int sequence) : ISpaceWarehouseResolver
    {
        public async Task<SpaceWarehouseIdentity?> ResolveAsync(Guid siteId, CancellationToken ct = default)
        {
            SpaceArtifactWorkerFixture.Write(root, sequence, "warehouse", siteId);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, sequence, "warehouse-continue", ct);
            return await inner.ResolveAsync(siteId, ct);
        }
    }

    private sealed class RejectingAdapter(Cp6SpaceWmsAdapter inner, string root, string rejection, int sequence) : ISpaceWmsAdapter
    {
        private string? capabilityHash;
        public string RuntimeAdapterId => inner.RuntimeAdapterId;
        public string RuntimeDataSourceId => inner.RuntimeDataSourceId;
        public SpaceWmsDataSourceKind RuntimeDataSourceKind => inner.RuntimeDataSourceKind;

        public async Task<SpaceWmsCapabilitySnapshot> GetCapabilitiesAsync(SpaceWmsContext context, CancellationToken ct = default)
        {
            var active = await inner.GetCapabilitiesAsync(context, ct);
            capabilityHash = active.CapabilityHash;
            if (rejection != "capability")
                return active;
            var observed = SpaceWmsCapabilitySnapshot.Create(active.AdapterId, active.DataSourceKind, active.CertificationLevel,
                active.Capabilities with { BatchMaxSize = active.Capabilities.BatchMaxSize - 1 }, active.ObservedAtUtc);
            await ObserveAsync(new(active.CapabilityHash, observed.CapabilityHash), ct);
            return observed;
        }

        public Task<SpaceWmsHealth> CheckHealthAsync(SpaceWmsContext context, CancellationToken ct = default) =>
            inner.CheckHealthAsync(context, ct);

        public async Task<SpaceWmsPreflightResult> PreflightAsync(SpaceWmsPreflightRequest request, CancellationToken ct = default)
        {
            var result = await inner.PreflightAsync(request, ct);
            if (rejection != "preflight")
                return result;
            await ObserveAsync(new(result.CapabilityHash), ct);
            return result with { Issues = [new(null, RejectedCode, Blocking: true)] };
        }

        public async Task<SpaceWmsBatchResult> ApplyBatchAsync(SpaceWmsBatch batch, CancellationToken ct = default)
        {
            await File.AppendAllLinesAsync(Path.Combine(root, ApplyCallsFile), [batch.OperationKey], ct);
            if (rejection == "reconcile")
                throw new TimeoutException("The simulated WMS Apply response is unavailable.");
            if (rejection != "rejected")
                throw new InvalidOperationException("The rejected publication must never call WMS Apply.");
            await ObserveAsync(new(capabilityHash!, OperationKey: batch.OperationKey, PayloadHash: batch.PayloadHash), ct);
            return new SpaceWmsBatchResult(batch.OperationKey, batch.PayloadHash, null,
                batch.Items.Select(item => new SpaceWmsItemReceipt(item.LogicalId, item.LocationCode, item.Action,
                    SpaceWmsItemOutcome.Rejected, null, null, null, RejectedCode)).ToArray(), DateTimeOffset.UtcNow);
        }

        private async Task ObserveAsync(Observation observation, CancellationToken ct)
        {
            if (sequence != 1)
                return;
            SpaceArtifactWorkerFixture.Write(root, sequence, "rejection", observation);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, sequence, "rejection-continue", ct);
        }

        public Task<SpaceWmsOperationStatus> GetOperationStatusAsync(SpaceWmsOperationQuery request, CancellationToken ct = default) =>
            rejection == "reconcile"
                ? Task.FromResult(new SpaceWmsOperationStatus(request.OperationKey, request.PayloadHash,
                    SpaceWmsOperationState.Partial, true, DateTimeOffset.UtcNow, "simulated-partial"))
                : inner.GetOperationStatusAsync(request, ct);
        public Task<SpaceWmsReadBackResult> ReadBackAsync(SpaceWmsReadBackRequest request, CancellationToken ct = default) =>
            inner.ReadBackAsync(request, ct);
        public Task<SpaceWmsBlockingReferences> GetBlockingReferencesAsync(SpaceWmsBlockingReferencesRequest request, CancellationToken ct = default) =>
            inner.GetBlockingReferencesAsync(request, ct);
        public Task<SpaceWmsLocationResult> QueryLocationsAsync(SpaceWmsLocationQuery request, CancellationToken ct = default) =>
            inner.QueryLocationsAsync(request, ct);
        public Task<SpaceWmsInventoryResult> QueryInventoryAsync(SpaceWmsInventoryQuery request, CancellationToken ct = default) =>
            inner.QueryInventoryAsync(request, ct);
        public Task<SpaceWmsTaskResult> QueryTasksAsync(SpaceWmsTaskQuery request, CancellationToken ct = default) =>
            inner.QueryTasksAsync(request, ct);
        public Task<SpaceWmsDispatchTaskResult> QueryDispatchTasksAsync(SpaceWmsDispatchTaskQuery request, CancellationToken ct = default) =>
            inner.QueryDispatchTasksAsync(request, ct);
        public Task<SpaceWmsAbcResult> QueryAbcAsync(SpaceWmsAbcQuery request, CancellationToken ct = default) =>
            inner.QueryAbcAsync(request, ct);
    }
}
