using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
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
    [InlineData(true)]
    [InlineData(false)]
    public Task PGT017_space_excel_cad_background_apply_shares_main_authority(bool effectFirst) =>
        VerifyExcelCadBackgroundApply(effectFirst);

    [CurrentPermissionPostgresFact]
    public Task PGT017_space_excel_cad_background_apply_rechecks_edit_lease_at_commit() =>
        VerifyExcelCadBackgroundApply(effectFirst: null);

    private async Task VerifyExcelCadBackgroundApply(bool? effectFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        var root = NewUnderlayFileRoot();
        var fileRoot = Path.Combine(root, "files");
        var signals = Path.Combine(root, "signals");
        Directory.CreateDirectory(signals);
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, editLease) = await SeedSpaceDesignDraft(space, execution, site);
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "CP6.Tests";
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpaceSourceMetadataWorkerFixture).Assembly.Location;
        settings[SpaceSourceMetadataWorkerFixture.RootVariable] = signals;
        if (effectFirst == true)
            settings[SpaceSourceMetadataWorkerFixture.FaultVariable] = "apply";
        else
            settings[SpaceSourceMetadataWorkerFixture.PhaseVariable] = SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_APPLY_WORKER_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";

        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var fixture = await PrepareExcelCadWorkerApply(host, actor, space, execution, version, floor, editLease, fileRoot, signals);
        var initial = await ReadExcelCadApplySnapshot(space, fileRoot, fixture);
        Assert.Equal(SpaceJobStatus.Queued, initial.Job.Status);
        Assert.Equal(SpaceSourceState.PreviewReady, initial.Source.State);
        Assert.Null(initial.Batch);
        Assert.Empty(initial.Racks);
        Assert.Empty(initial.Levels);
        Assert.Empty(initial.Locations);

        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        if (effectFirst is null)
        {
            var work = RunSourceMetadataWorker(signals, 2);
            var attempt = await SpaceArtifactWorkerFixture.ReadAsync<Guid>(signals, 1, "at-step");
            await using var deadlineCommand = new NpgsqlCommand("""
                UPDATE public."Space_EditLease"
                SET "ExpiresAtUtc"=clock_timestamp() + interval '10 seconds'
                WHERE "TenantId"=@tenant AND "LeaseId"=@lease RETURNING "ExpiresAtUtc"
                """, control);
            deadlineCommand.Parameters.AddWithValue("tenant", writer.Tenant);
            deadlineCommand.Parameters.AddWithValue("lease", editLease.LeaseId);
            var deadline = Assert.IsType<DateTime>(await deadlineCommand.ExecuteScalarAsync());
            FenceWait wait;
            DateTimeOffset crossedAt;
            await using (var held = await HoldUnderlayTable(business, "Space_ElementCommandBatch"))
            {
                SpaceArtifactWorkerFixture.Write(signals, 1, "release", true);
                wait = await WaitForFence(observer, business.ProcessID, "%INSERT%Space_ElementCommandBatch%", null, work);
                Assert.True(wait.ObservedAt.UtcDateTime < deadline);
                do
                {
                    await Task.Delay(100);
                    crossedAt = await BackendObservationTime(observer);
                } while (crossedAt.UtcDateTime <= deadline);
                await held.RollbackAsync();
            }

            var run = await work;
            Assert.Null(run.Error);
            Assert.Equal(1, run.Processed);
            var expired = await ReadExcelCadApplySnapshot(space, fileRoot, fixture);
            await DeviceEvidence("space-excel-cad-apply-edit-expiry.json", new
            {
                Attempt = attempt, Wait = wait, Deadline = deadline, CrossedAt = crossedAt, Initial = initial, After = expired
            });
            Assert.Equal(SpaceJobStatus.Failed, expired.Job.Status);
            Assert.Equal(SpaceErrorCodes.EditLeaseLost, expired.Job.LastErrorCode);
            Assert.Equal(attempt, Assert.Single(expired.Attempts).Id);
            AssertNoExcelCadApplyEffect(initial, expired);
            return;
        }

        FenceWait ownerWait;
        FenceWait pauseWait;
        HttpResponseMessage paused;
        ApplyWorkerSnapshot? committedWithoutAcknowledgement = null;
        Task<SpaceSourceMetadataWorkerFixture.WorkerResult> running;
        if (effectFirst == true)
        {
            await using var held = await HoldUnderlayTable(business, "Space_ElementCommandBatch");
            running = RunSourceMetadataWorker(signals, 2);
            ownerWait = await WaitForFence(observer, business.ProcessID, "%INSERT%Space_ElementCommandBatch%", null, running);
            var pause = Pause();
            pauseWait = await WaitForFence(observer, ownerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            await held.RollbackAsync();
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(signals, 1, "committed");
            paused = await pause;
            committedWithoutAcknowledgement = await ReadExcelCadApplySnapshot(space, fileRoot, fixture);
            Assert.Equal(SpaceJobStatus.Running, committedWithoutAcknowledgement.Job.Status);
            AssertAppliedExcelCadHierarchy(initial, committedWithoutAcknowledgement, fixture);
            SpaceArtifactWorkerFixture.Write(signals, 1, "release", true);
        }
        else
        {
            running = RunSourceMetadataWorker(signals, 2);
            var attempt = await SpaceArtifactWorkerFixture.ReadAsync<Guid>(signals, 1, "at-step");
            await using var tenantHold = await HoldUnderlayTenant(control, writer.Tenant);
            var pause = Pause();
            pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            SpaceArtifactWorkerFixture.Write(signals, 1, "release", true);
            ownerWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_authentication_v1.acquire%", null, running);
            var admitted = await ReadExcelCadApplySnapshot(space, fileRoot, fixture);
            Assert.Equal(attempt, admitted.Job.ActiveAttemptId);
            AssertNoExcelCadApplyEffect(initial, admitted);
            await tenantHold.RollbackAsync();
            paused = await pause;
        }

        using var signedPause = paused;
        Assert.Equal(HttpStatusCode.OK, signedPause.StatusCode);
        Assert.False((await NumberHttpData<TenantManagementReceiptV1>(signedPause)).Snapshot.Enable);
        var first = await running;
        Assert.Null(first.Error);
        Assert.Equal(1, first.Processed);
        var after = await ReadExcelCadApplySnapshot(space, fileRoot, fixture);
        var order = effectFirst == true ? "effect-first" : "pause-first";
        await DeviceEvidence($"space-excel-cad-apply-{order}.json", new
        {
            Order = order, OwnerWait = ownerWait, PauseWait = pauseWait, Initial = initial,
            BeforeAcknowledgement = committedWithoutAcknowledgement, After = after
        });
        if (effectFirst == true)
        {
            Assert.Equal(SpaceJobStatus.Succeeded, after.Job.Status);
            Assert.Equal(committedWithoutAcknowledgement!.Batch!.ResponseJson, after.Batch!.ResponseJson);
            Assert.Equal(after.Batch.ResponseJson, Assert.Single(after.Steps).CheckpointJson);
            Assert.Equal(ExcelCadHash(System.Text.Encoding.UTF8.GetBytes(after.Batch.ResponseJson!)),
                Assert.Single(after.Steps).OutputHash);
        }
        else
        {
            Assert.Equal(SpaceJobStatus.Queued, after.Job.Status);
            Assert.Equal("SPACE_ARTIFACT_TENANT_INACTIVE", after.Job.LastErrorCode);
            AssertNoExcelCadApplyEffect(initial, after);
        }

        using var obsolete = await SendExcelCadWrite(host, actor, fixture.Confirmation);
        Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        var suspended = await RunSourceMetadataWorker(signals, 3);
        Assert.Null(suspended.Error);
        Assert.Equal(0, suspended.Processed);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, signedPause);
        if (effectFirst == false)
            await WaitPublishJobDue(observer, after.Job.NextAttemptAtUtc);
        var resumed = await RunSourceMetadataWorker(signals, 4);
        Assert.Null(resumed.Error);
        Assert.Equal(effectFirst == true ? 0 : 1, resumed.Processed);
        var final = await ReadExcelCadApplySnapshot(space, fileRoot, fixture);
        Assert.Equal(SpaceJobStatus.Succeeded, final.Job.Status);
        Assert.Equal(effectFirst == true ? 1 : 2, final.Attempts.Length);
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, final.Attempts[^1].Outcome);
        AssertAppliedExcelCadHierarchy(initial, final, fixture);

        actor = await Login(host, writer);
        using var replay = await SendExcelCadWrite(host, actor, fixture.Confirmation);
        var receipt = await ReadExcelCadResponse<ConfirmSpaceExcelCadMatchResponse>(replay, HttpStatusCode.Accepted);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replay")));
        Assert.Equal(fixture.JobId, receipt.ApplyJobId);
        Assert.Equal(fixture.BatchId, receipt.CommandBatchId);
        using var readback = await host.Send(OperationsRequest(actor, HttpMethod.Get, $"{fixture.Confirmation.Path}/{fixture.JobId:D}"));
        var original = await ReadExcelCadResponse<SpaceExcelCadApplyDto>(readback, HttpStatusCode.OK);
        Assert.Equal(final.Batch!.ResponseJson, JsonSerializer.Serialize(original.Result, ExcelCadJson));
        if (after.Batch is not null)
            Assert.Equal(after.Batch.ResponseJson, final.Batch.ResponseJson);
        await DeviceEvidence($"space-excel-cad-apply-{order}-resumed.json", new { Final = final, Resumed = resumed, Original = original });

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private static async Task<ApplyWorkerFixture> PrepareExcelCadWorkerApply(AuthenticationApiProcess host, Dictionary<string, string> actor,
        SpaceContext space, SpaceDesignExecution execution, SpaceModelVersion version, SpaceFloorRevision floor,
        SpaceEditLease editLease, string fileRoot, string signals)
    {
        var store = new FileSystemSpaceFileStore(new SpaceFileStorageOptions { RootPath = fileRoot });
        var mappings = new SpaceExcelMappingService(space, execution, new SystemSpaceClock());
        var profile = await mappings.GetProfileAsync(SpaceExcelMappingService.SystemStandardProfileId, 1);
        var workbook = BuildApplyHierarchyWorkbook(floor.FloorCode, profile.Definition);
        var request = await SeedExcelCadPreviewReady(space, execution, version, floor, store, profile, workbook);
        var matchPath = $"{SpaceDesignPath}/versions/{version.Id:D}/excel-cad-matches";
        var start = new ExcelCadWriteInput(matchPath, request, Guid.NewGuid().ToString("N"), "Space_Job", HttpStatusCode.Accepted,
            $"excel-cad-match:{version.Id:N}", "space.excel-cad-match.start", "jobId", request.ExcelSourceId, SpaceJobType.ExcelCadMatch);
        using var started = await SendExcelCadWrite(host, actor, start);
        var match = await ReadExcelCadResponse<StartSpaceExcelCadMatchResponse>(started, HttpStatusCode.Accepted);
        var matched = await RunSourceMetadataWorker(signals, 1);
        Assert.Null(matched.Error);
        Assert.Equal(1, matched.Processed);
        Assert.Equal(SpaceJobStatus.Succeeded,
            (await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == match.JobId)).Status);

        var artifact = await space.Artifacts.AsNoTracking().SingleAsync(row => row.JobId == match.JobId);
        var file = await space.Files.AsNoTracking().SingleAsync(row => row.Id == artifact.FileId);
        await using var stream = await store.OpenQuarantinedReadAsync(execution.TenantId, file.Id, file.StorageKey);
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        Assert.Equal(file.Sha256, ExcelCadHash(System.Text.Encoding.UTF8.GetBytes(json)));
        var preview = SpaceExcelCadMatchArtifact.Deserialize(json);
        Assert.True(preview.Preview.CanConfirm, JsonSerializer.Serialize(preview.Preview));
        Assert.Equal(1, preview.Preview.Summary.NewCount);

        var confirmPath = $"{matchPath}/{match.JobId:D}/confirmations";
        var confirmation = new ExcelCadWriteInput(confirmPath,
            new ConfirmSpaceExcelCadMatchRequest(true, artifact.Id, preview.ArtifactPayloadSha256, version.ContentRevision,
                editLease.ClientInstanceId, editLease.LeaseId, floor.Revision),
            Guid.NewGuid().ToString("N"), "Space_Job", HttpStatusCode.Accepted, $"excel-cad-apply:{version.Id:N}:{match.JobId:N}",
            "space.excel-cad-match.confirm", "applyJobId", request.ExcelSourceId, SpaceJobType.ExcelCadApply);
        using var confirmed = await SendExcelCadWrite(host, actor, confirmation);
        var apply = await ReadExcelCadResponse<ConfirmSpaceExcelCadMatchResponse>(confirmed, HttpStatusCode.Accepted);
        return new(apply.ApplyJobId, apply.CommandBatchId, version.Id, floor.LogicalId, request.ExcelSourceId, confirmation);
    }

    private static byte[] BuildApplyHierarchyWorkbook(string floorCode, SpaceExcelMappingDefinitionDto definition)
    {
        using var original = new MemoryStream(BuildExcelCadWorkbook(floorCode, definition));
        using var workbook = new XLWorkbook(original);
        Put("RackLevels", new Dictionary<string, string>
        {
            ["RackCode"] = "R-001", ["LevelNo"] = "1", ["BottomZMm"] = "0", ["ClearHeightMm"] = "1000",
            ["BinCount"] = "2", ["DepthCount"] = "2", ["LoadCapacityKg"] = "1000", ["LifecycleStatus"] = "Active"
        });
        Put("Locations", new Dictionary<string, string>
        {
            ["LocationCode"] = "L-001", ["RackCode"] = "R-001", ["ColumnNo"] = "1", ["LevelNo"] = "1",
            ["DepthNo"] = "1", ["LifecycleStatus"] = "Active", ["LocationType"] = "Storage"
        });
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();

        void Put(string target, Dictionary<string, string> values)
        {
            var sheet = definition.Sheets.Single(item => item.TargetSheet == target);
            var worksheet = workbook.Worksheet(sheet.SourceSheet);
            for (var index = 0; index < sheet.Columns.Count; index++)
            {
                if (values.TryGetValue(sheet.Columns[index].TargetField, out var value))
                    worksheet.Cell(sheet.DataStartRow, index + 1).Value = value;
            }
        }
    }

    private static async Task<ApplyWorkerSnapshot> ReadExcelCadApplySnapshot(SpaceContext space, string fileRoot, ApplyWorkerFixture fixture)
    {
        var attempts = await space.JobAttempts.AsNoTracking().Where(row => row.JobId == fixture.JobId)
            .OrderBy(row => row.AttemptNo).ToArrayAsync();
        var attemptIds = attempts.Select(row => row.Id).ToArray();
        return new(
            await ReadArtifactWriteSet(space, fileRoot),
            await space.Sources.AsNoTracking().SingleAsync(row => row.Id == fixture.SourceId),
            await space.Versions.AsNoTracking().SingleAsync(row => row.Id == fixture.VersionId),
            await space.FloorRevisions.AsNoTracking().SingleAsync(row => row.ModelVersionId == fixture.VersionId && row.LogicalId == fixture.FloorId),
            await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == fixture.JobId),
            attempts,
            await space.JobSteps.AsNoTracking().Where(row => attemptIds.Contains(row.AttemptId)).ToArrayAsync(),
            await space.ElementCommandBatches.AsNoTracking().SingleOrDefaultAsync(row => row.Id == fixture.BatchId),
            await space.ElementCommandRecords.AsNoTracking().Where(row => row.CommandBatchId == fixture.BatchId).OrderBy(row => row.SequenceNo).ToArrayAsync(),
            await space.RackRevisions.AsNoTracking().Where(row => row.ModelVersionId == fixture.VersionId).ToArrayAsync(),
            await space.RackLevelRevisions.AsNoTracking().Where(row => row.ModelVersionId == fixture.VersionId).ToArrayAsync(),
            await space.LocationRevisions.AsNoTracking().Where(row => row.ModelVersionId == fixture.VersionId).ToArrayAsync());
    }

    private static void AssertNoExcelCadApplyEffect(ApplyWorkerSnapshot initial, ApplyWorkerSnapshot after)
    {
        Assert.Equal(JsonSerializer.Serialize(initial.Catalog), JsonSerializer.Serialize(after.Catalog));
        Assert.Equal(JsonSerializer.Serialize(initial.Floor), JsonSerializer.Serialize(after.Floor));
        Assert.Null(after.Batch);
        Assert.Empty(after.Records);
        Assert.Empty(after.Racks);
        Assert.Empty(after.Levels);
        Assert.Empty(after.Locations);
    }

    private static void AssertAppliedExcelCadHierarchy(ApplyWorkerSnapshot initial, ApplyWorkerSnapshot after, ApplyWorkerFixture fixture)
    {
        Assert.Equal(initial.Catalog.Artifacts, after.Catalog.Artifacts);
        Assert.Equal(initial.Catalog.Files, after.Catalog.Files);
        Assert.Equal(initial.Catalog.Blobs, after.Catalog.Blobs);
        Assert.Equal(SpaceSourceState.Imported, after.Source.State);
        Assert.Equal(fixture.BatchId, after.Source.ImportedCommandBatchId);
        Assert.Equal(initial.Version.ContentRevision + 1, after.Version.ContentRevision);
        Assert.Equal(initial.Floor.Revision + 1, after.Floor.Revision);
        Assert.Equal(fixture.BatchId, after.Batch!.Id);
        var rack = Assert.Single(after.Racks);
        var level = Assert.Single(after.Levels);
        var location = Assert.Single(after.Locations);
        Assert.Equal("R-001", rack.RackCode);
        Assert.Equal(rack.LogicalId, level.RackLogicalId);
        Assert.Equal(rack.LogicalId, location.RackLogicalId);
        Assert.Equal("L-001", location.LocationCode);
        Assert.Equal(fixture.SourceId, rack.SourceId);
        Assert.Equal(fixture.SourceId, level.SourceId);
        Assert.Equal(fixture.SourceId, location.SourceId);
        Assert.Equal(3, after.Records.Length);
        var result = JsonSerializer.Deserialize<SpaceExcelCadApplyResultV1>(after.Batch.ResponseJson!, ExcelCadJson)!;
        Assert.Equal(fixture.JobId, result.ApplyJobId);
        Assert.Equal(fixture.BatchId, result.CommandBatchId);
        Assert.Equal(after.Records.Length, result.HistoryCommandCount);
        Assert.Equal(after.Batch.ChangesetSha256, result.ApplyPlanSha256);
    }

    private sealed record ApplyWorkerFixture(Guid JobId, Guid BatchId, Guid VersionId, Guid FloorId, Guid SourceId,
        ExcelCadWriteInput Confirmation);

    private sealed record ApplyWorkerSnapshot(WorkerArtifactSet Catalog, SpaceModelSource Source, SpaceModelVersion Version,
        SpaceFloorRevision Floor, SpaceJob Job, SpaceJobAttempt[] Attempts, SpaceJobStep[] Steps,
        SpaceElementCommandBatch? Batch, SpaceElementCommandRecord[] Records, SpaceRackRevision[] Racks,
        SpaceRackLevelRevision[] Levels, SpaceLocationRevision[] Locations);
}
