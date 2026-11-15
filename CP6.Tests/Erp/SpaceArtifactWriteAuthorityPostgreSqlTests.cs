using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Tests.Erp.SpaceArtifactWorkerFixture;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("cad", "artifact-first")]
    [InlineData("cad", "pause-first")]
    [InlineData("match", "artifact-first")]
    [InlineData("match", "pause-first")]
    [InlineData("cad", "before")]
    [InlineData("match", "before")]
    [InlineData("cad", "ack")]
    [InlineData("match", "ack")]
    [InlineData("match", "expiry")]
    public async Task PGT017_space_artifact_worker_commit_uses_current_tenant_and_preserves_owned_bytes(string owner, string scenario)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        var menu = await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);

        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        var root = NewUnderlayFileRoot();
        var fileRoot = Path.Combine(root, "files");
        var signals = Path.Combine(root, "signals");
        Directory.CreateDirectory(signals);
        await using var verify = SpaceDesignContext(connection, execution);
        await verify.Database.MigrateAsync();
        var (version, floor, editLease) = await SeedSpaceDesignDraft(verify, execution, site);
        var editingFloor = await verify.FloorRevisions.SingleAsync(row => row.Id == floor.Id);
        editingFloor.ConfigureBoundary("[[0,0],[100000,0],[100000,100000],[0,100000]]",
            CP6.Space.Contracts.SpaceCadCoordinateVersions.TargetCoordinateSystem);
        await verify.SaveChangesAsync();
        verify.ChangeTracker.Clear();

        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = typeof(SpaceCadFixtureHostingStartup).Assembly.GetName().Name!;
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpaceCadFixtureHostingStartup).Assembly.Location;
        settings[RootVariable] = signals;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_ARTIFACT_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var command = await PrepareArtifactJob(host, actor, verify, execution, version, floor, editLease, fileRoot, owner);
        var original = await ReadArtifactWriteSet(verify, fileRoot);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        if (scenario == "artifact-first")
        {
            // The background lease belongs to the accepted Job, even if the submitter now has zero Space actions.
            var actions = await writer.Db.Sys_RoleActions.Where(row => row.RoleId == 72 && row.MenuId == menu).ToArrayAsync();
            Assert.Equal(2, actions.Length);
            writer.Db.Sys_RoleActions.RemoveRange(actions);
            await writer.Db.SaveChangesAsync();
        }

        ArtifactResult attempt;
        ArtifactRaceObservation? race = null;
        DateTime? expiresAt = null;
        if (scenario is "artifact-first" or "pause-first")
        {
            race = await RaceArtifactAndPause(connection, writer.Tenant, signals, command, Pause, scenario == "artifact-first");
            attempt = race.Attempt;
        }
        else if (scenario == "expiry")
        {
            var expiryRun = RunArtifactStep(signals, 1, command with { Hold = "transaction", Fault = "expiry" });
            await ReadAsync<bool>(signals, 1, "waiting");
            var tenant = await writer.Db.Sys_Tenants.SingleAsync(row => row.Id == writer.Tenant);
            expiresAt = DateTime.Now.AddSeconds(8);
            tenant.ExpireDate = expiresAt;
            await writer.Db.SaveChangesAsync();
            Write(signals, 1, "expiry", expiresAt.Value);
            Write(signals, 1, "release", true);
            attempt = await expiryRun;
            tenant.ExpireDate = null;
            await writer.Db.SaveChangesAsync();
        }
        else
        {
            attempt = await RunArtifactStep(signals, 1, command with { Fault = scenario });
        }

        var after = await ReadArtifactWriteSet(verify, fileRoot);
        var retained = scenario is "artifact-first" or "ack";
        await DeviceEvidence($"space-artifact-{owner}-{scenario}.json", new
        {
            Scenario = scenario, Race = race is null ? null : new { race.Worker, race.Pause },
            Attempt = attempt, Before = original, After = after, ExpiresAt = expiresAt,
        });
        Assert.NotEqual(Guid.Empty, attempt.AttemptId);
        Assert.Equal(scenario == "ack", attempt.AckInjected);
        if (expiresAt is { } expiry)
            Assert.True(attempt.ExpiryObservedAt > expiry);
        if (scenario is "before" or "ack" or "expiry")
            Assert.Equal(owner == "cad" ? 2 : 1, attempt.PublishedAtFault.Length - original.Blobs.Length);

        if (scenario == "artifact-first")
        {
            Assert.Equal(race!.Worker.Backend, race.Pause.Blocker);
            Assert.Null(attempt.Code);
            CheckWorkerArtifactCommit(original, after, command, attempt);
            return;
        }

        var job = await verify.Jobs.AsNoTracking().SingleAsync(row => row.Id == command.JobId);
        ArtifactResult recovered;
        if (scenario == "ack")
        {
            // Admit the next actual attempt while active, then close the gate before the immutable readback.
            var retry = RunArtifactStep(signals, 2, command with { Hold = "claim" });
            await ReadAsync<Guid>(signals, 2, "waiting");
            using var paused = await Pause();
            Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
            Write(signals, 2, "release", true);
            recovered = await retry;
        }
        else
        {
            if (scenario == "pause-first")
            {
                using var paused = race!.PauseResponse;
                await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused);
            }
            if (attempt.ErrorType is null)
            {
                Assert.Fail($"Expected no new effects in '{scenario}', observed success.");
            }
            recovered = await RunArtifactStep(signals, 2, command);
        }

        var final = await ReadArtifactWriteSet(verify, fileRoot);
        await DeviceEvidence($"space-artifact-{owner}-{scenario}-recovery.json", new { Recovered = recovered, Final = final });
        Assert.Equal(SpaceJobStatus.Queued, job.Status);
        Assert.Equal(SpaceJobFailureKind.Transient, job.LastFailureKind);
        Assert.NotEqual(attempt.AttemptId, recovered.AttemptId);
        Assert.Null(recovered.ErrorType);
        Assert.Equal(2, await verify.JobAttempts.AsNoTracking().CountAsync(row => row.JobId == command.JobId));
        Assert.Equal(recovered.OutputHash, (await verify.JobSteps.AsNoTracking().SingleAsync(row => row.AttemptId == recovered.AttemptId)).OutputHash);
        CheckWorkerArtifactCommit(original, final, command, recovered);
        if (retained)
        {
            Assert.Equal("SPACE_ARTIFACT_COMMIT_UNKNOWN", attempt.Code);
            Assert.Equal(JsonSerializer.Serialize(after), JsonSerializer.Serialize(final));
        }
        else
        {
            Assert.Equal(scenario == "before" ? "W613_BEFORE_COMMIT" : "SPACE_ARTIFACT_TENANT_INACTIVE", attempt.Code);
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(after));
            if (scenario == "pause-first")
                Assert.Equal(race!.Pause.Backend, race.Worker.Blocker);
        }

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private static async Task<ArtifactCommand> PrepareArtifactJob(AuthenticationApiProcess host, Dictionary<string, string> actor,
        SpaceContext space, SpaceDesignExecution execution, SpaceModelVersion version, SpaceFloorRevision floor, SpaceEditLease editLease,
        string fileRoot, string owner)
    {
        if (owner == "cad")
        {
            var start = await PrepareSourceProcessingInput(host, actor, space, execution.ActorId, version.Id, floor.LogicalId, "start");
            using var started = await SendSourceProcessing(host, actor, start);
            var id = await CheckSourceProcessing(space, start, "start", execution.ActorId, started);
            var storage = new FileSystemSpaceFileStore(new SpaceFileStorageOptions { RootPath = fileRoot });
            var reused = await WriteExcelCadFixtureFile(storage, execution.TenantId, "pre-existing-layers.json", "application/json",
                SpaceFileRetentionClass.Artifact, SimulatedCadArtifact(SpaceArtifactType.LayerInventory));
            space.Files.Add(reused);
            await space.SaveChangesAsync();
            space.ChangeTracker.Clear();
            return new(execution.TenantId, id, SpaceJobType.CadParse, fileRoot);
        }

        var matchStart = await PrepareExcelCadWrite(host, actor, space, execution, version, floor, editLease, fileRoot, "start");
        using var result = await SendExcelCadWrite(host, actor, matchStart);
        var match = await ReadExcelCadResponse<CP6.Space.Contracts.StartSpaceExcelCadMatchResponse>(result, HttpStatusCode.Accepted);
        return new(execution.TenantId, match.JobId, SpaceJobType.ExcelCadMatch, fileRoot);
    }

    private static async Task<ArtifactRaceObservation> RaceArtifactAndPause(string connection, Guid tenant, string signals,
        ArtifactCommand command, Func<Task<HttpResponseMessage>> pause, bool artifactFirst)
    {
        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();
        FenceWait workerWait;
        FenceWait pauseWait;
        Task<ArtifactResult> running;
        Task<HttpResponseMessage> pausing;

        await using (var blocked = await HoldUnderlayTable(business, "Space_Artifact"))
        await using (var held = await HoldUnderlayTenant(control, tenant))
        {
            if (artifactFirst)
            {
                running = RunArtifactStep(signals, 1, command);
                workerWait = await WaitForFence(observer, business.ProcessID, "%INSERT%Space_Artifact%", null, running);
                pausing = pause();
                pauseWait = await SerialNativeWait(observer, workerWait.Backend, control.ProcessID, "space-artifact-business-first", pausing);
            }
            else
            {
                running = RunArtifactStep(signals, 1, command with { Hold = "transaction" });
                await ReadAsync<bool>(signals, 1, "waiting");
                pausing = pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
                Write(signals, 1, "release", true);
                workerWait = await WaitForArtifactNativeWait(observer, pauseWait.Backend, business.ProcessID, running);
            }

            await held.RollbackAsync();
            if (pauseWait.Blocker == control.ProcessID)
                await pausing;
            await blocked.RollbackAsync();
        }

        var paused = await pausing;
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        var attempt = await running;
        if (artifactFirst)
            paused.Dispose();
        return new(attempt, workerWait, pauseWait, paused);
    }

    private static async Task<FenceWait> WaitForArtifactNativeWait(NpgsqlConnection observer, int pause, int table,
        Task<ArtifactResult> running)
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
            if (running.IsCompleted)
                Assert.Fail("Artifact processor returned before its native wait: " + JsonSerializer.Serialize(await running));
            await Task.Delay(35, timeout.Token);
        }
    }

    private static Task<ArtifactResult> RunArtifactStep(string signals, int sequence, ArtifactCommand command)
    {
        Write(signals, sequence, "request", command);
        return ReadAsync<ArtifactResult>(signals, sequence, "result");
    }

    private static async Task<WorkerArtifactSet> ReadArtifactWriteSet(SpaceContext space, string root) => new(
        await space.Artifacts.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new WorkerArtifactRow(row.Id, row.JobId, row.FileId, row.SourceId, row.ArtifactType)).ToArrayAsync(),
        await space.Files.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new WorkerFileRow(row.Id, row.StorageKey, row.Sha256!, row.SizeBytes, row.State)).ToArrayAsync(),
        UnderlayFiles(root, "*").Order(StringComparer.Ordinal)
            .Select(path => new WorkerBlob(Path.GetRelativePath(root, path).Replace('\\','/'), ExcelCadHash(File.ReadAllBytes(path)))).ToArray(),
        JsonSerializer.Serialize(await space.Sources.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
        JsonSerializer.Serialize(await space.Versions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()));

    private static void CheckWorkerArtifactCommit(WorkerArtifactSet before, WorkerArtifactSet after, ArtifactCommand command, ArtifactResult result)
    {
        var cad = command.JobType == SpaceJobType.CadParse;
        var produced = after.Artifacts.Where(row => row.JobId == command.JobId).ToArray();
        Assert.Equal(cad ? 3 : 1, produced.Length);
        Assert.Equal(produced.Length, after.Artifacts.Length - before.Artifacts.Length);
        Assert.Equal(cad ? 2 : 1, after.Files.Length - before.Files.Length);
        Assert.Equal(after.Files.Length, after.Blobs.Length);
        Assert.Equal(before.Sources, after.Sources);
        Assert.Equal(before.Versions, after.Versions);
        Assert.All(before.Artifacts, previous => Assert.Contains(previous, after.Artifacts));
        Assert.All(before.Files, previous => Assert.Contains(previous, after.Files));
        Assert.All(before.Blobs, previous => Assert.Contains(previous, after.Blobs));
        Assert.All(after.Blobs, blob => Assert.EndsWith(".content", blob.Key));
        if (cad)
        {
            var layers = Assert.Single(produced, row => row.Type == SpaceArtifactType.LayerInventory);
            Assert.Contains(before.Files, row => row.Id == layers.FileId);
        }

        using var checkpoint = JsonDocument.Parse(result.CheckpointJson!);
        var records = cad ? checkpoint.RootElement.GetProperty("artifacts").EnumerateArray().ToArray() : [checkpoint.RootElement];
        Assert.Equal(produced.Length, records.Length);
        foreach (var record in records)
        {
            var id = record.GetProperty("artifactId").GetGuid();
            var artifact = Assert.Single(produced, row => row.Id == id);
            var file = Assert.Single(after.Files, row => row.Id == artifact.FileId);
            Assert.Equal(file.Id, record.GetProperty("fileId").GetGuid());
            Assert.Equal(SpaceFileState.Clean, file.State);
            Assert.Equal(file.Sha256, record.GetProperty(cad ? "sha256" : "fileSha256").GetString());
            Assert.Equal(file.Sha256, Assert.Single(after.Blobs, blob => blob.Key == file.Key).Sha256);
        }
    }

    private sealed record ArtifactRaceObservation(ArtifactResult Attempt, FenceWait Worker, FenceWait Pause, HttpResponseMessage PauseResponse);
    private sealed record WorkerArtifactSet(WorkerArtifactRow[] Artifacts, WorkerFileRow[] Files, WorkerBlob[] Blobs, string Sources, string Versions);
    private sealed record WorkerArtifactRow(Guid Id, Guid? JobId, Guid FileId, Guid? SourceId, SpaceArtifactType Type);
    private sealed record WorkerFileRow(Guid Id, string Key, string Sha256, long Size, SpaceFileState State);
    private sealed record WorkerBlob(string Key, string Sha256);
}
