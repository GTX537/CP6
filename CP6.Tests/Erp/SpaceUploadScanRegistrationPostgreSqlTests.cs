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
    [InlineData("cad", "effect-first")]
    [InlineData("cad", "pause-first")]
    [InlineData("cad", "rejected-effect-first")]
    [InlineData("cad", "legacy-file-reference")]
    [InlineData("excel", "effect-first")]
    [InlineData("excel", "pause-first")]
    [InlineData("excel", "rejected-effect-first")]
    [InlineData("underlay", "effect-first")]
    [InlineData("underlay", "pause-first")]
    [InlineData("underlay", "rejected-effect-first")]
    public async Task PGT017_space_upload_registers_completed_scan_in_the_original_source_transaction(string kind, string order)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, _, _) = await SeedSpaceDesignDraft(space, execution, site, storeLease: false);
        var upload = UploadScanSample(kind, version.Id);
        var root = NewUnderlayFileRoot();
        var forwarded = new NpgsqlConnectionStringBuilder(connection) { SslMode = SslMode.Disable };
        var relayPort = int.TryParse(Environment.GetEnvironmentVariable("CP6_W648_RELAY_PORT"), out var reserved) ? reserved : 0;
        await using var relay = new StockPostgresCommitRelay(forwarded.Port, forwarded.Username!, relayPort);
        forwarded.Port = relay.Port;
        await using var host = await StartSourceUploadHost(forwarded.ConnectionString, writer.Tenant, site, root);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);

        await using var observer = await OpenHistoricalDatabase(connection);
        await using var control = await OpenHistoricalDatabase(connection);
        await using var table = await OpenHistoricalDatabase(connection);
        var legacy = order == "legacy-file-reference";
        var rejected = legacy || order == "rejected-effect-first";
        var effectFirst = !legacy && order != "pause-first";
        relay.ArmHold();
        var pending = SendUploadScanInput(host, actor, kind, upload);
        try
        {
            await relay.CommitHeld.WaitAsync(TimeSpan.FromSeconds(12));
            var (fileId, jobId) = await CheckSourceCatalog(space, root, upload);
            var pendingFile = legacy ? await space.Files.AsNoTracking().SingleAsync() : null;
            Assert.Empty(await space.Sources.AsNoTracking().ToArrayAsync());
            var fixture = new FileScanFixture(connection, execution, root, fileId, jobId, upload.Sha256, host, manager, active.Revision);
            await using var scanner = new IsolatedFileScanWorker(fixture, outcome: rejected ? "reject" : null);
            var lease = await scanner.Claim();
            var terminal = await scanner.Processor.ProcessAsync(lease);
            Assert.Equal(1, scanner.ScannerCalls);
            var prior = await ReadUploadScanRegistration(space, root, version.Id);
            Assert.Equal(version.ContentRevision, prior.Version.ContentRevision);
            Assert.Equal(rejected ? SpaceFileState.Rejected : SpaceFileState.Clean, Assert.Single(prior.Files).State);
            Assert.Equal(fileId, Assert.Single(prior.Files).Id);
            Assert.Equal(jobId, Assert.Single(prior.Jobs).Id);
            Assert.Equal(rejected ? SpaceJobStatus.Failed : SpaceJobStatus.Succeeded, Assert.Single(prior.Jobs).Status);
            Assert.Empty(prior.Sources);
            Assert.Equal(upload.Bytes, await File.ReadAllBytesAsync(Assert.Single(prior.Content)));
            Assert.Empty(prior.Temporary);

            FenceWait sourceWait, pauseWait;
            Task<HttpResponseMessage> pause;
            if (effectFirst)
            {
                await using var holdSource = await HoldUnderlayTable(table, "Space_ModelSource");
                relay.ReleaseCommit();
                sourceWait = await WaitForFence(observer, table.ProcessID, "%INSERT%Space_ModelSource%", null, pending);
                pause = fixture.Pause();
                pauseWait = await WaitForFence(observer, sourceWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
                Assert.Empty(await space.Sources.AsNoTracking().ToArrayAsync());
                await holdSource.RollbackAsync();
            }
            else
            {
                await using var holdTenant = await HoldUnderlayTenant(control, writer.Tenant);
                pause = fixture.Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                relay.ReleaseCommit();
                sourceWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_authentication_v1.acquire%", null, pending);
                Assert.Empty(await space.Sources.AsNoTracking().ToArrayAsync());
                await holdTenant.RollbackAsync();
            }

            using var paused = await pause;
            Assert.False((await NumberHttpData<TenantManagementReceiptV1>(paused)).Snapshot.Enable);
            using var result = await pending;
            var after = await ReadUploadScanRegistration(space, root, version.Id);
            var originalSource = after.Sources.SingleOrDefault();
            var originalCommit = originalSource is null ? null : await ReadUploadScanTransaction(observer, version.Id, originalSource.Id);
            await DeviceEvidence($"space-upload-scan-{kind}-{order}.json", new
            {
                NativeFileCommitHeld = true, Scan = terminal, SourceWait = sourceWait, PauseWait = pauseWait,
                BeforeRegistration = prior, After = after, Http = result.StatusCode, Transaction = originalCommit,
            });
            Assert.Equal(JsonSerializer.Serialize(new { prior.Files, prior.Jobs, prior.Content, prior.Temporary }),
                JsonSerializer.Serialize(new { after.Files, after.Jobs, after.Content, after.Temporary }));
            if (effectFirst)
            {
                var receipt = await CheckUploadScanReceipt(result, upload, after, fileId, jobId, rejected, reused: false);
                Assert.Equal(originalSource!.Id, receipt.SourceId);
                Assert.Equal(version.ContentRevision + 1, after.Version.ContentRevision);
                Assert.Equal(originalCommit!.Version, originalCommit.Source);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
                Assert.Empty(after.Sources);
                Assert.Equal(version.ContentRevision, after.Version.ContentRevision);
            }

            await fixture.Resume(paused);
            actor = await Login(host, writer);
            if (pendingFile is not null)
            {
                await CheckLegacyUploadFileReference(space, execution, host, actor, root, kind, upload, prior, pendingFile);
                return;
            }
            using var replay = await SendUploadScanInput(host, actor, kind, upload);
            if (rejected)
            {
                Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
                var denied = await ReadUploadScanRegistration(space, root, version.Id);
                Assert.Equal(originalSource!.Id, Assert.Single(denied.Sources).Id);
                Assert.Equal(fileId, Assert.Single(denied.Sources).FileId);
                Assert.Equal(SpaceSourceState.Rejected, Assert.Single(denied.Sources).State);
                Assert.Equal(after.Version.ContentRevision, denied.Version.ContentRevision);
                Assert.Equal(originalCommit!, await ReadUploadScanTransaction(observer, version.Id, originalSource.Id));
                Assert.Contains(denied.Jobs, job => job.Id == jobId && job.SubjectId == fileId);
                return;
            }

            var restored = await ReadUploadScanRegistration(space, root, version.Id);
            var recovered = await CheckUploadScanReceipt(replay, upload, restored, fileId, jobId, rejected: false, reused: true);
            Assert.Equal(version.ContentRevision + 1, restored.Version.ContentRevision);
            var complete = await ReadUploadScanTransaction(observer, version.Id, recovered.SourceId);
            Assert.Equal(complete.Version, complete.Source);
            if (originalSource is not null)
            {
                Assert.Equal(originalSource.Id, recovered.SourceId);
                Assert.Equal(originalCommit, complete);
            }
            Assert.Equal(upload.Bytes, await File.ReadAllBytesAsync(Assert.Single(restored.Content)));
            Assert.Empty(restored.Temporary);
            await DeviceEvidence($"space-upload-scan-{kind}-{order}-recovered.json", new { Native = complete, Receipt = recovered, State = restored });
        }
        finally
        {
            relay.ReleaseCommit();
        }
    }

    private static SourceUploadSample UploadScanSample(string kind, Guid version) => kind == "underlay"
        ? new(version, UploadUnderlayInput(version).Path, "survey.pdf", "application/pdf", SpaceSourceType.Pdf, UnderlayPdf)
        : SourceUploadSample.For(kind, version);

    private static Task<HttpResponseMessage> SendUploadScanInput(AuthenticationApiProcess host,
        Dictionary<string, string> actor, string kind, SourceUploadSample sample) => kind == "underlay"
        ? SendUnderlay(host, actor, UploadUnderlayInput(sample.VersionId))
        : SendSourceUpload(host, actor, sample);

    [CurrentPermissionPostgresFact]
    public async Task PGT017_space_upload_returns_the_committed_source_after_concurrent_removal()
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, _, _) = await SeedSpaceDesignDraft(space, execution, site, storeLease: false);
        var sample = SourceUploadSample.For("cad", version.Id);
        var root = NewUnderlayFileRoot();
        var forwarded = new NpgsqlConnectionStringBuilder(connection) { SslMode = SslMode.Disable };
        var relayPort = int.TryParse(Environment.GetEnvironmentVariable("CP6_W648_RELAY_PORT"), out var reserved) ? reserved : 0;
        await using var relay = new StockPostgresCommitRelay(forwarded.Port, forwarded.Username!, relayPort);
        forwarded.Port = relay.Port;
        await using var host = await StartSourceUploadHost(forwarded.ConnectionString, writer.Tenant, site, root);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
        await using var observer = await OpenHistoricalDatabase(connection);
        await using var table = await OpenHistoricalDatabase(connection);

        Task<HttpResponseMessage> pending;
        Guid fileId, jobId;
        await using (var holdSource = await HoldUnderlayTable(table, "Space_ModelSource"))
        {
            pending = SendSourceUpload(host, actor, sample);
            await WaitForFence(observer, table.ProcessID, "%INSERT%Space_ModelSource%", null, pending);
            (fileId, jobId) = await CheckSourceCatalog(space, root, sample);
            Assert.Empty(await space.Sources.AsNoTracking().ToArrayAsync());
            relay.ArmHold();
            await holdSource.RollbackAsync();
        }

        try
        {
            await relay.CommitHeld.WaitAsync(TimeSpan.FromSeconds(12));
            var original = Assert.Single(await space.Sources.AsNoTracking().ToArrayAsync());
            Assert.Equal(SpaceSourceState.Scanning, original.State);
            var fixture = new FileScanFixture(connection, execution, root, fileId, jobId, sample.Sha256, host, manager, active.Revision);
            await using var scanner = new IsolatedFileScanWorker(fixture);
            await scanner.Processor.ProcessAsync(await scanner.Claim());
            var prior = await ReadUploadScanRegistration(space, root, version.Id);
            var current = Assert.Single(prior.Sources);
            Assert.Equal(original.Id, current.Id);
            Assert.Equal(SpaceSourceState.Ready, current.State);

            using var removal = OperationsRequest(actor, HttpMethod.Post,
                $"{SpaceDesignPath}/versions/{version.Id:D}/sources/{original.Id:D}:remove",
                new RemoveSpaceSourceRequest(prior.Version.ContentRevision, Convert.ToBase64String(current.RowVersion)));
            removal.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var removed = await host.Send(removal);
            Assert.True(removed.StatusCode == HttpStatusCode.OK,
                removed.StatusCode == HttpStatusCode.OK ? null : await OperationsFailure(removed));
            Assert.Empty(await space.Sources.AsNoTracking().ToArrayAsync());
            var deleted = await ReadUploadScanTransaction(observer, version.Id, original.Id);

            relay.ReleaseCommit();
            using var response = await pending;
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                response.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(response));
            var receipt = Assert.IsType<UploadSpaceCadSourceResponse>(await response.Content.ReadFromJsonAsync<UploadSpaceCadSourceResponse>());
            Assert.Equal(original.Id, receipt.Source.Id);
            Assert.Equal(original.State.ToString(), receipt.Source.State);
            Assert.Equal(fileId, receipt.File.Id);
            Assert.Equal(jobId, receipt.ScanJobId);
            var after = await ReadUploadScanRegistration(space, root, version.Id);
            Assert.Equal(deleted, await ReadUploadScanTransaction(observer, version.Id, original.Id));
            Assert.Empty(after.Sources);
            Assert.Equal(prior.Version.ContentRevision + 1, after.Version.ContentRevision);
            Assert.Equal(sample.Bytes, await File.ReadAllBytesAsync(Assert.Single(after.Content)));
            await DeviceEvidence("space-upload-scan-concurrent-removal.json", new { Prior = prior, After = after, Receipt = receipt, Deleted = deleted });
        }
        finally
        {
            relay.ReleaseCommit();
        }
    }

    private static async Task CheckLegacyUploadFileReference(SpaceContext space, SpaceDesignExecution execution,
        AuthenticationApiProcess host, Dictionary<string, string> actor, string root, string kind,
        SourceUploadSample sample, UploadScanRegistration scanned, SpaceFile previouslyPendingFile)
    {
        // Recreate an already persisted Source from the old gap between registration and its follow-up write.
        var draft = await space.Versions.SingleAsync(row => row.Id == sample.VersionId);
        var original = new SpaceSourceCoordinator(execution).AddPendingFileSource(draft, previouslyPendingFile,
            sample.SourceType, sample.Name);
        space.Sources.Add(original);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();

        using var upload = await SendUploadScanInput(host, actor, kind, sample);
        Assert.True(upload.StatusCode == HttpStatusCode.Accepted,
            upload.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(upload));
        var receipt = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var originalJobId = Assert.Single(scanned.Jobs).Id;
        Assert.Equal(previouslyPendingFile.Id, receipt.GetProperty("file").GetProperty("id").GetGuid());
        Assert.Equal(originalJobId, receipt.GetProperty("scanJobId").GetGuid());
        Assert.Equal($"/{SpaceDesignPath}/jobs/{originalJobId:D}", upload.Headers.Location?.OriginalString);
        Assert.Equal(original.Id, receipt.GetProperty("source").GetProperty("id").GetGuid());
        Assert.Equal(SpaceSourceState.Rejected.ToString(), receipt.GetProperty("source").GetProperty("state").GetString());
        Assert.True(receipt.GetProperty("reused").GetBoolean());

        var repaired = await ReadUploadScanRegistration(space, root, sample.VersionId);
        var repairedSource = Assert.Single(repaired.Sources);
        Assert.Equal(original.Id, repairedSource.Id);
        Assert.Equal(previouslyPendingFile.Id, repairedSource.FileId);
        Assert.Equal(SpaceSourceState.Rejected, repairedSource.State);
        Assert.Equal(scanned.Version.ContentRevision + 1, repaired.Version.ContentRevision);
        Assert.Equal(2, repaired.Files.Length);
        Assert.Equal(SpaceFileState.Rejected, Assert.Single(repaired.Files, row => row.Id == previouslyPendingFile.Id).State);
        var newFile = Assert.Single(repaired.Files, row => row.Id != previouslyPendingFile.Id);
        Assert.Equal(SpaceFileState.Quarantined, newFile.State);
        Assert.All(repaired.Files, file => Assert.Equal(sample.Sha256, file.Sha256));
        Assert.Equal(2, repaired.Jobs.Length);
        Assert.Equal(originalJobId, Assert.Single(repaired.Jobs, row => row.SubjectId == previouslyPendingFile.Id).Id);
        Assert.Equal(SpaceJobStatus.Queued, Assert.Single(repaired.Jobs, row => row.SubjectId == newFile.Id).Status);
        Assert.Equal(2, repaired.Content.Length);
        foreach (var blob in repaired.Content)
            Assert.Equal(sample.Bytes, await File.ReadAllBytesAsync(blob));

        using var retry = await SendUploadScanInput(host, actor, kind, sample);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(repaired),
            JsonSerializer.Serialize(await ReadUploadScanRegistration(space, root, sample.VersionId)));
        await DeviceEvidence("space-upload-scan-legacy-file-reference-recovered.json", new { Repaired = repaired, Receipt = receipt });
    }

    private static async Task<UploadScanReceipt> CheckUploadScanReceipt(HttpResponseMessage response, SourceUploadSample sample,
        UploadScanRegistration state, Guid fileId, Guid jobId, bool rejected, bool reused)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            response.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(response));
        var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        var source = Assert.Single(state.Sources);
        var file = Assert.Single(state.Files);
        Assert.Equal(jobId, Assert.Single(state.Jobs).Id);
        Assert.Equal(fileId, file.Id);
        Assert.Equal(fileId, source.FileId);
        Assert.Equal(sample.VersionId, source.ModelVersionId);
        Assert.Equal(sample.Sha256, source.Sha256);
        Assert.Equal(sample.SourceType, source.SourceType);
        Assert.Equal(rejected ? SpaceSourceState.Rejected : SpaceSourceState.Ready, source.State);
        Assert.Equal(source.Id, value.GetProperty("source").GetProperty("id").GetGuid());
        Assert.Equal(source.State.ToString(), value.GetProperty("source").GetProperty("state").GetString());
        Assert.Equal(fileId, value.GetProperty("file").GetProperty("id").GetGuid());
        Assert.Equal(file.State.ToString(), value.GetProperty("file").GetProperty("state").GetString());
        Assert.Equal(jobId, value.GetProperty("scanJobId").GetGuid());
        Assert.Equal(reused, value.GetProperty("reused").GetBoolean());
        Assert.Equal($"/{SpaceDesignPath}/jobs/{jobId:D}", response.Headers.Location?.OriginalString);
        return new(fileId, source.Id, jobId);
    }

    private static async Task<UploadScanRegistration> ReadUploadScanRegistration(SpaceContext space, string root, Guid version) => new(
        await space.Versions.AsNoTracking().SingleAsync(row => row.Id == version),
        await space.Files.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        await space.Jobs.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        await space.Sources.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        UnderlayFiles(root, "*.content").Order().ToArray(), UnderlayFiles(root, "*.upload").Order().ToArray());

    private static async Task<UploadScanTransaction> ReadUploadScanTransaction(NpgsqlConnection observer, Guid version, Guid source)
    {
        await using var command = new NpgsqlCommand("""
            SELECT v.xmin::text, s.xmin::text FROM public."Space_ModelVersion" v
            INNER JOIN public."Space_ModelSource" s ON v."Id"=s."ModelVersionId" AND v."TenantId"=s."TenantId"
            WHERE v."Id"=@version AND s."Id"=@source
            """, observer);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("source", source);
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        return new(row.GetString(0), row.GetString(1));
    }

    private sealed record UploadScanRegistration(SpaceModelVersion Version, SpaceFile[] Files, SpaceJob[] Jobs,
        SpaceModelSource[] Sources, string[] Content, string[] Temporary);
    private sealed record UploadScanReceipt(Guid FileId, Guid SourceId, Guid ScanJobId);
    private sealed record UploadScanTransaction(string Version, string Source);
}
