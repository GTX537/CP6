using System.Net;
using System.Net.Http.Json;
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
    [InlineData("cad-finalize", "effect-first")]
    [InlineData("cad-finalize", "pause-first")]
    [InlineData("excel-issues", "effect-first")]
    [InlineData("excel-issues", "pause-first")]
    [InlineData("excel-preview", "effect-first")]
    [InlineData("excel-preview", "pause-first")]
    [InlineData("excel-issues", "commit-ack")]
    public async Task PGT017_space_source_metadata_worker_commit_uses_current_tenant(string phase, string order)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        var root = NewUnderlayFileRoot();
        var fileRoot = Path.Combine(root, "files");
        var signals = Path.Combine(root, "signals");
        Directory.CreateDirectory(signals);
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, _) = await SeedSpaceDesignDraft(space, execution, site);
        var editingFloor = await space.FloorRevisions.SingleAsync(row => row.Id == floor.Id);
        editingFloor.ConfigureBoundary("[[0,0],[100000,0],[100000,100000],[0,100000]]",
            SpaceCadCoordinateVersions.TargetCoordinateSystem);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();

        var cad = phase == "cad-finalize";
        var issues = phase == "excel-issues";
        var effectFirst = order == "effect-first";
        var lostAck = order == "commit-ack";
        var step = cad ? SpaceCadParseJobProcessor.FinalizePreview : issues
            ? SpaceExcelPreflightJobProcessor.ValidateWorkbook : SpaceExcelPreflightJobProcessor.PersistPreview;
        var prerequisite = cad ? SpaceCadParseJobProcessor.GenerateArtifacts : SpaceExcelPreflightJobProcessor.ValidateWorkbook;
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "CP6.Tests";
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpaceSourceMetadataWorkerFixture).Assembly.Location;
        settings[SpaceSourceMetadataWorkerFixture.RootVariable] = signals;
        if (!effectFirst && !lostAck)
            settings[SpaceSourceMetadataWorkerFixture.PhaseVariable] = step;
        if (lostAck)
            settings[SpaceSourceMetadataWorkerFixture.FaultVariable] = "1";
        if (Environment.GetEnvironmentVariable("CP6_SPACE_SOURCE_METADATA_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";

        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var request = await PrepareSourceProcessingInput(host, actor, space, writer.User, version.Id, floor.LogicalId, cad ? "start" : "excel");
        using var queued = await SendSourceProcessing(host, actor, request);
        var jobId = await CheckSourceProcessing(space, request, cad ? "start" : "excel", writer.User, queued);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        MetadataSnapshot before;
        MetadataSnapshot? committedBeforeAck = null;
        FenceWait? workerWait = null;
        FenceWait? pauseWait = null;
        Task<SpaceSourceMetadataWorkerFixture.WorkerResult> running;
        HttpResponseMessage paused;
        if (lostAck)
        {
            running = RunSourceMetadataWorker(signals, 1);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(signals, 1, "committed");
            before = await ReadSourceMetadataSnapshot(space, fileRoot, jobId, request.SourceId);
            committedBeforeAck = before;
            paused = await Pause();
            SpaceArtifactWorkerFixture.Write(signals, 1, "release", true);
        }
        else if (effectFirst)
        {
            var table = issues ? "Space_ModelIssue" : "Space_ModelSource";
            var sql = issues ? "%INSERT%Space_ModelIssue%" : "%UPDATE%Space_ModelSource%";
            await using var blocker = await HoldUnderlayTable(business, table);
            running = RunSourceMetadataWorker(signals, 1);
            workerWait = await WaitForFence(observer, business.ProcessID, sql, null, running);
            before = await ReadSourceMetadataSnapshot(space, fileRoot, jobId, request.SourceId);
            var pause = Pause();
            pauseWait = await WaitForFence(observer, workerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            await blocker.RollbackAsync();
            paused = await pause;
        }
        else
        {
            running = RunSourceMetadataWorker(signals, 1);
            var atStep = await SpaceArtifactWorkerFixture.ReadAsync<Guid>(signals, 1, "at-step");
            await using var tenantHold = await HoldUnderlayTenant(control, writer.Tenant);
            var pause = Pause();
            pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            SpaceArtifactWorkerFixture.Write(signals, 1, "release", true);
            workerWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_authentication_v1.acquire%", null, running);
            before = await ReadSourceMetadataSnapshot(space, fileRoot, jobId, request.SourceId);
            Assert.Equal(atStep, before.Job.ActiveAttemptId);
            await tenantHold.RollbackAsync();
            paused = await pause;
        }

        using var signedPause = paused;
        Assert.Equal(HttpStatusCode.OK, signedPause.StatusCode);
        Assert.False((await NumberHttpData<TenantManagementReceiptV1>(signedPause)).Snapshot.Enable);
        var first = await running;
        Assert.Null(first.Error);
        Assert.Equal(1, first.Processed);
        var after = await ReadSourceMetadataSnapshot(space, fileRoot, jobId, request.SourceId);
        await DeviceEvidence($"space-source-metadata-{phase}-{order}.json",
            new { Phase = phase, Order = order, Worker = workerWait, Pause = pauseWait, Before = before, After = after });

        var stepBefore = Assert.Single(before.Steps, row => row.StepCode == step);
        Assert.Equal(SpaceJobStepStatus.Running, stepBefore.Status);
        var priorState = cad ? SpaceSourceState.Ready : SpaceSourceState.Parsing;
        Assert.Equal(priorState, before.Source.State);
        AssertStableSourceArtifacts(before, after);
        if (cad)
            Assert.Equal(3, before.Catalog.Artifacts.Count(row => row.JobId == jobId));
        if (!issues)
        {
            var ready = Assert.Single(before.Steps, row => row.StepCode == prerequisite);
            Assert.Equal(SpaceJobStepStatus.Succeeded, ready.Status);
            Assert.NotNull(ready.OutputHash);
            Assert.Equal(JsonSerializer.Serialize(before.Issues), JsonSerializer.Serialize(after.Issues));
        }
        else if (!lostAck)
        {
            Assert.Empty(before.Issues);
        }

        var finished = effectFirst && !issues;
        Assert.Equal(finished ? SpaceSourceState.PreviewReady : priorState, after.Source.State);
        if (finished)
        {
            Assert.Equal(SpaceJobStatus.Succeeded, after.Job.Status);
        }
        else
        {
            Assert.Equal(SpaceJobStatus.Queued, after.Job.Status);
            Assert.Equal(lostAck ? "SPACE_ARTIFACT_COMMIT_UNKNOWN" : "SPACE_ARTIFACT_TENANT_INACTIVE", after.Job.LastErrorCode);
        }

        if (issues && (effectFirst || lostAck))
            Assert.NotEmpty(after.Issues);
        if (issues && !effectFirst && !lostAck)
            Assert.Empty(after.Issues);
        if (committedBeforeAck is not null)
            Assert.Equal(JsonSerializer.Serialize(committedBeforeAck.Issues), JsonSerializer.Serialize(after.Issues));

        using var stale = await SendSourceProcessing(host, actor, request);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        var suspended = await RunSourceMetadataWorker(signals, 2);
        Assert.Null(suspended.Error);
        Assert.Equal(0, suspended.Processed);

        await ResumeSourceUploadTenant(host, manager, writer.Tenant, signedPause);
        if (!finished)
            await WaitPublishJobDue(observer, after.Job.NextAttemptAtUtc);
        var resumed = await RunSourceMetadataWorker(signals, 3);
        Assert.Null(resumed.Error);
        Assert.Equal(finished ? 0 : 1, resumed.Processed);
        var final = await ReadSourceMetadataSnapshot(space, fileRoot, jobId, request.SourceId);
        Assert.Equal(jobId, final.Job.Id);
        Assert.Equal(SpaceJobStatus.Succeeded, final.Job.Status);
        Assert.Equal(SpaceSourceState.PreviewReady, final.Source.State);
        Assert.Equal(finished ? 1 : 2, final.Attempts.Length);
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, final.Attempts[^1].Outcome);
        AssertStableSourceArtifacts(before, final);

        var steps = final.Steps.Where(row => row.AttemptId == final.Attempts[^1].Id).OrderBy(row => row.StepNo).ToArray();
        Assert.Equal(2, steps.Length);
        Assert.Equal(steps[0].CheckpointJson, steps[1].CheckpointJson);
        Assert.Equal(steps[0].OutputHash, steps[1].OutputHash);
        Assert.NotNull(steps[1].OutputHash);
        if (!finished && (!issues || effectFirst))
            Assert.Equal(SpaceJobStepStatus.Reused, steps[0].Status);

        if (!cad)
        {
            Assert.Contains(final.Issues, issue => issue.Severity == SpaceIssueSeverity.Blocking);
            Assert.All(final.Issues, issue =>
            {
                Assert.Equal(jobId, issue.JobId);
                Assert.Equal(request.SourceId, issue.SourceId);
            });
            if (after.Issues.Length > 0)
                Assert.Equal(JsonSerializer.Serialize(after.Issues), JsonSerializer.Serialize(final.Issues));
        }

        actor = await Login(host, writer);
        using var replay = await SendSourceProcessing(host, actor, request);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replay")));
        Assert.Equal(jobId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid());
        await DeviceEvidence($"space-source-metadata-{phase}-{order}-resumed.json",
            new { Resumed = resumed, Final = final, ClosedSessionStatus = (int)stale.StatusCode, ReplayStatus = (int)replay.StatusCode });

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private static Task<SpaceSourceMetadataWorkerFixture.WorkerResult> RunSourceMetadataWorker(string root, int sequence)
    {
        SpaceArtifactWorkerFixture.Write(root, sequence, "request", true);
        return SpaceArtifactWorkerFixture.ReadAsync<SpaceSourceMetadataWorkerFixture.WorkerResult>(root, sequence, "result");
    }

    private static async Task<MetadataSnapshot> ReadSourceMetadataSnapshot(
        SpaceContext space, string fileRoot, Guid jobId, Guid sourceId)
    {
        var attempts = await space.JobAttempts.AsNoTracking().Where(row => row.JobId == jobId).OrderBy(row => row.AttemptNo).ToArrayAsync();
        var attemptIds = attempts.Select(row => row.Id).ToArray();
        return new(
            await space.Sources.AsNoTracking().SingleAsync(row => row.Id == sourceId),
            await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == jobId),
            attempts,
            await space.JobSteps.AsNoTracking().Where(row => attemptIds.Contains(row.AttemptId)).OrderBy(row => row.StepNo).ThenBy(row => row.Id).ToArrayAsync(),
            await space.Issues.AsNoTracking().Where(row => row.JobId == jobId).OrderBy(row => row.Id).ToArrayAsync(),
            await ReadArtifactWriteSet(space, fileRoot));
    }

    private static void AssertStableSourceArtifacts(MetadataSnapshot left, MetadataSnapshot right)
    {
        Assert.Equal(left.Catalog.Files, right.Catalog.Files);
        Assert.Equal(left.Catalog.Artifacts, right.Catalog.Artifacts);
        Assert.Equal(left.Catalog.Blobs, right.Catalog.Blobs);
        Assert.Equal(left.Catalog.Versions, right.Catalog.Versions);
        Assert.Equal(left.Source.Id, right.Source.Id);
        Assert.Equal(left.Source.FileId, right.Source.FileId);
        Assert.Equal(left.Source.Sha256, right.Source.Sha256);
    }

    private sealed record MetadataSnapshot(SpaceModelSource Source, SpaceJob Job, SpaceJobAttempt[] Attempts,
        SpaceJobStep[] Steps, SpaceModelIssue[] Issues, WorkerArtifactSet Catalog);
}
