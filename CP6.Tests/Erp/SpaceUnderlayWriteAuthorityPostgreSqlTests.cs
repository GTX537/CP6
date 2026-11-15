using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly byte[] UnderlayPdf = "%PDF-1.4\n%%EOF\n"u8.ToArray();

    [TenantManagementPostgresTheory]
    [InlineData("upload", "pause-first")]
    [InlineData("upload", "business-first")]
    [InlineData("attach", "pause-first")]
    [InlineData("attach", "business-first")]
    [InlineData("calibrate", "pause-first")]
    [InlineData("calibrate", "business-first")]
    [InlineData("compensate", "pause-first")]
    [InlineData("compensate", "business-first")]
    [InlineData("upload", "permission-first")]
    [InlineData("attach", "permission-first")]
    [InlineData("calibrate", "permission-first")]
    public async Task PGT017_space_underlay_write_and_revocation_share_current_authority(string operation, string order)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        var menuId = await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, lease) = await SeedSpaceDesignDraft(space, execution, site);
        var fileRoot = NewUnderlayFileRoot();
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var input = await BuildUnderlayInput(host, actor, space, execution, version, floor, lease, operation);
        if (operation == "compensate")
        {
            var uploadGrant = await writer.Db.Sys_RoleActions.SingleAsync(x => x.RoleId == 72 && x.MenuId == menuId &&
                x.ActionCode == SpaceSourceUploadAction);
            writer.Db.Sys_RoleActions.Remove(uploadGrant);
            await writer.Db.SaveChangesAsync();
        }

        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var before = await UnderlayWriteSet(space, fileRoot);
        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        var ownerFirst = order == "business-first";
        var permissionFirst = order == "permission-first";
        var table = operation != "upload" ? "Space_IdempotencyRecord"
            : ownerFirst ? "Space_ModelSource" : "Space_File";
        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage>? pause = null;
        FenceWait ownerWait;
        FenceWait? revokerWait = null;
        await using (var barrier = await HoldUnderlayTable(business, table))
        {
            if (ownerFirst)
            {
                pending = SendUnderlay(host, actor, input);
                ownerWait = await WaitForFence(observer, business.ProcessID, $"%{table}%", null, pending);
                pause = Pause();
                revokerWait = await WaitForFence(observer, ownerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            }
            else if (permissionFirst)
            {
                await using var revoking = await control.BeginTransactionAsync();
                await using var remove = new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire();
                    DELETE FROM public."Sys_RoleAction"
                    WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"=@action;
                    """, control, revoking);
                remove.Parameters.AddWithValue("tenant", writer.Tenant);
                remove.Parameters.AddWithValue("menu", menuId);
                remove.Parameters.AddWithValue("action", SpaceSourceUploadAction);
                Assert.Equal(1, await remove.ExecuteNonQueryAsync());
                pending = SendUnderlay(host, actor, input);
                ownerWait = await SerialNativeWait(observer, control.ProcessID, business.ProcessID,
                    $"space-underlay-{operation}-permission", pending);
                await revoking.CommitAsync();
            }
            else
            {
                await using var held = await HoldUnderlayTenant(control, writer.Tenant);
                pause = Pause();
                revokerWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                pending = SendUnderlay(host, actor, input);
                ownerWait = await SerialNativeWait(observer, revokerWait.Backend, business.ProcessID,
                    $"space-underlay-{operation}", pending);
                await held.RollbackAsync();
                await pause;
            }
            await barrier.RollbackAsync();
        }

        using var result = await pending;
        var after = await UnderlayWriteSet(space, fileRoot);
        using var paused = pause is null ? null : await pause;
        var expectedStatus = ownerFirst ? input.Status : permissionFirst ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        await DeviceEvidence($"space-underlay-{operation}-{order}.json", new
        {
            Operation = operation, Order = order, OwnerWait = ownerWait, RevokerWait = revokerWait,
            Expected = (int)expectedStatus, Actual = (int)result.StatusCode, Before = before, After = after
        });
        Assert.True(result.StatusCode == expectedStatus, await OperationsFailure(result));
        if (ownerFirst)
        {
            Assert.Equal(ownerWait.Backend, revokerWait!.Blocker);
            Assert.NotEqual(before, after);
            await CheckUnderlayResult(space, fileRoot, version.Id, floor.LogicalId, operation, result, reused: false);
        }
        else
        {
            Assert.Equal(permissionFirst ? control.ProcessID : revokerWait!.Backend, ownerWait.Blocker);
            Assert.Equal(before, after);
        }

        if (permissionFirst)
        {
            await GrantSpaceDesignActions(writer, SpaceSourceUploadAction);
        }
        else
        {
            var suspended = await NumberHttpData<TenantManagementReceiptV1>(paused!);
            Assert.False(suspended.Snapshot.Enable);
            using var obsolete = await SendUnderlay(host, actor, input);
            Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
            using var restored = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
                new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), suspended.Snapshot.Revision, "reactivate", null)));
            Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);
            actor = await Login(host, writer);
        }

        using var original = await SendUnderlay(host, actor, input);
        await CheckUnderlayResult(space, fileRoot, version.Id, floor.LogicalId, operation, original, reused: ownerFirst);
        var committed = await UnderlayWriteSet(space, fileRoot);
        if (ownerFirst) Assert.Equal(after, committed);
        else Assert.NotEqual(before, committed);
        using var replay = await SendUnderlay(host, actor, input);
        await CheckUnderlayResult(space, fileRoot, version.Id, floor.LogicalId, operation, replay, reused: true);
        Assert.Equal(committed, await UnderlayWriteSet(space, fileRoot));

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    [TenantManagementPostgresTheory]
    [InlineData("file-before-pause")]
    public async Task PGT017_space_underlay_upload_recovers_a_committed_file_when_source_was_denied(string order)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, _) = await SeedSpaceDesignDraft(space, execution, site);
        var input = UploadUnderlayInput(version.Id);
        var fileRoot = NewUnderlayFileRoot();
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        await using var fileOwner = new NpgsqlConnection(connection);
        await using var sourceOwner = new NpgsqlConnection(connection);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await fileOwner.OpenAsync();
        await sourceOwner.OpenAsync();
        await control.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage> pause;
        FenceWait fileWait, pauseWait, sourceWait;
        string afterFile;
        await using (var blockSource = await HoldUnderlayTable(sourceOwner, "Space_ModelSource"))
        await using (var holdTenant = await HoldUnderlayTenant(control, writer.Tenant))
        await using (var blockFile = await HoldUnderlayTable(fileOwner, "Space_File"))
        {
            pending = SendUnderlay(host, actor, input);
            fileWait = await WaitForFence(observer, fileOwner.ProcessID, "%Space_File%", null, pending);
            Assert.Empty(UnderlayFiles(fileRoot, "*.content"));
            Assert.Equal(UnderlayPdf.Length, new FileInfo(Assert.Single(UnderlayFiles(fileRoot, "*.upload"))).Length);
            pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
                new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
            pauseWait = await SerialNativeWait(observer, fileWait.Backend, control.ProcessID, "space-underlay-upload-catalog", pause);
            await blockFile.RollbackAsync();

            var heldPause = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            sourceWait = await SerialNativeWait(observer, heldPause.Backend, sourceOwner.ProcessID, "space-underlay-upload-source", pending);
            afterFile = await UnderlayWriteSet(space, fileRoot);
            Assert.Single(await space.Files.AsNoTracking().ToArrayAsync());
            Assert.Single(await space.Jobs.AsNoTracking().ToArrayAsync());
            Assert.Empty(await space.Sources.AsNoTracking().ToArrayAsync());
            Assert.Single(UnderlayFiles(fileRoot, "*.content"));
            await holdTenant.RollbackAsync();
            await pause;
            await blockSource.RollbackAsync();
        }

        using var denied = await pending;
        using var paused = await pause;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        var deniedState = await UnderlayWriteSet(space, fileRoot);
        await DeviceEvidence($"space-underlay-{order}.json", new
        {
            FileWait = fileWait, PauseWait = pauseWait, SourceWait = sourceWait,
            Status = (int)denied.StatusCode, AfterFile = afterFile, Denied = deniedState
        });
        Assert.Equal(fileWait.Backend, pauseWait.Blocker);
        Assert.Equal(pauseWait.Backend, sourceWait.Blocker);
        Assert.True(denied.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(denied));
        Assert.Equal(afterFile, deniedState);

        var committedFile = await space.Files.AsNoTracking().SingleAsync();
        var committedJob = await space.Jobs.AsNoTracking().SingleAsync();
        using var restored = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), receipt.Snapshot.Revision, "reactivate", null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);
        actor = await Login(host, writer);
        using var resumed = await SendUnderlay(host, actor, input);
        await CheckUnderlayResult(space, fileRoot, version.Id, floor.LogicalId, "upload", resumed, reused: true);
        Assert.Equal(committedFile.Id, (await space.Files.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(committedJob.Id, (await space.Jobs.AsNoTracking().SingleAsync()).Id);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_space_underlay_file_ownership_survives_commit_boundary_failure(bool commitSucceeded)
    {
        await using var writer = await Seed();
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var verify = SpaceDesignContext(connection, execution);
        await verify.Database.MigrateAsync();
        var (version, _, _) = await SeedSpaceDesignDraft(verify, execution, site);
        var fileRoot = NewUnderlayFileRoot();
        var storage = new FileSystemSpaceFileStore(new SpaceFileStorageOptions { RootPath = fileRoot });
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Space);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<SpaceContext>(), database,
            connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        options.AddInterceptors(new UnderlayCommitFault { Armed = commitSucceeded });
        var clock = new SystemSpaceClock();
        await using var owner = new SpaceContext(options.Options, execution, clock);
        var service = new SpaceUnderlayV1Service(owner, execution, new UnderlayTestSiteAccess(site),
            new SpaceFileUploadService(execution, storage, new EfSpaceFileCatalog(owner)),
            new SpaceSourceCoordinator(execution), storage, clock,
            writeAuthority: new UnderlayTestWriteAuthority { FailConfirmation = !commitSucceeded });

        var failure = await Assert.ThrowsAsync<SpaceProblemException>(Upload);
        Assert.Equal("FIXTURE_UPLOAD_INTERRUPTED", failure.Code);
        var files = await verify.Files.AsNoTracking().ToArrayAsync();
        var jobs = await verify.Jobs.AsNoTracking().ToArrayAsync();
        Assert.Equal(commitSucceeded ? 1 : 0, files.Length);
        Assert.Equal(files.Length, jobs.Length);
        Assert.Equal(files.Length, UnderlayFiles(fileRoot, "*.content").Length);
        Assert.Empty(UnderlayFiles(fileRoot, "*.upload"));
        Assert.Empty(await verify.Sources.AsNoTracking().ToArrayAsync());

        var resumed = await Upload();
        Assert.Equal(commitSucceeded, resumed.Reused);
        if (commitSucceeded)
        {
            Assert.Equal(files[0].Id, resumed.File.Id);
            Assert.Equal(jobs[0].Id, resumed.ScanJobId);
        }
        Assert.Single(await verify.Files.AsNoTracking().ToArrayAsync());
        Assert.Single(await verify.Jobs.AsNoTracking().ToArrayAsync());
        Assert.Single(await verify.Sources.AsNoTracking().ToArrayAsync());
        Assert.Equal(UnderlayPdf, await File.ReadAllBytesAsync(Assert.Single(UnderlayFiles(fileRoot, "*.content"))));
        Assert.Equal(1, (await verify.Versions.AsNoTracking().SingleAsync(x => x.Id == version.Id)).ContentRevision);

        async Task<UploadSpaceUnderlayResponse> Upload()
        {
            using var body = new MemoryStream(UnderlayPdf, writable: false);
            return await service.UploadAsync(version.Id, new(SpaceSourceType.Pdf, "survey.pdf", "application/pdf"), body);
        }
    }

    private sealed class UnderlayCommitFault : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Armed) return Task.CompletedTask;
            Armed = false;
            throw UnderlayInterrupted();
        }
    }

    private sealed class UnderlayTestSiteAccess(Guid siteId) : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid site, bool write) => Assert.Equal(siteId, site);
    }

    private sealed class UnderlayTestWriteAuthority : ISpaceDesignWriteAuthority
    {
        public bool FailConfirmation { get; set; }

        public Task AuthorizeModelEditAsync(SpaceContext context, CancellationToken cancellationToken, string? additionalPermission = null)
        {
            Assert.Equal("space:source:upload", additionalPermission);
            return context.Database.ExecuteSqlRawAsync("SELECT cp6_authentication_v1.acquire()", cancellationToken);
        }

        public Task ConfirmSessionAsync(SpaceContext context, CancellationToken cancellationToken)
        {
            if (!FailConfirmation) return Task.CompletedTask;
            FailConfirmation = false;
            throw UnderlayInterrupted();
        }
    }

    private static SpaceProblemException UnderlayInterrupted() =>
        new("FIXTURE_UPLOAD_INTERRUPTED", 503, "The fixture interrupted the upload.");

    private static async Task<UnderlayInput> BuildUnderlayInput(AuthenticationApiProcess host, Dictionary<string, string> actor,
        SpaceContext space, SpaceDesignExecution execution, SpaceModelVersion version, SpaceFloorRevision floor, SpaceEditLease lease,
        string operation)
    {
        if (operation == "upload") return UploadUnderlayInput(version.Id);
        var file = SpaceFile.CreateUploading(Guid.NewGuid(), execution.TenantId, $"fixture/{Guid.NewGuid():N}.pdf",
            "survey.pdf", "application/pdf", SpaceFileRetentionClass.Source);
        file.CompleteQuarantine("application/pdf", ".pdf", UnderlayPdf.Length, SpaceDesignSampleHash);
        file.BeginScanning();
        file.MarkClean("fixture", "test");
        var source = SpaceModelSource.CreateFileSource(execution.TenantId, version.Id, SpaceSourceType.Pdf, file, "survey");
        space.AddRange(file, source);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();

        var versionPath = $"{SpaceDesignPath}/versions/{version.Id:D}";
        var floorPath = $"{versionPath}/floors/{floor.LogicalId:D}/underlay";
        var attach = new UnderlayInput(HttpMethod.Put, floorPath,
            new AttachSpaceUnderlayRequest(source.Id, floor.Revision, version.ContentRevision, lease.ClientInstanceId,
                lease.LeaseId, Guid.NewGuid()), Guid.NewGuid().ToString("N"), HttpStatusCode.OK);
        if (operation == "attach") return attach;
        using var setup = await SendUnderlay(host, actor, attach);
        Assert.True(setup.IsSuccessStatusCode, await OperationsFailure(setup));
        var baseline = Assert.IsType<AttachSpaceUnderlayResponse>(await setup.Content.ReadFromJsonAsync<AttachSpaceUnderlayResponse>());
        var body = operation == "calibrate"
            ? (object)new SaveSpaceUnderlayCalibrationRequest(floor.LogicalId, 1, 1000, 500,
                new(0, 500, 1000, 2000), new(100, 500, 2000, 2000), new(0, 400, 1000, 3000),
                baseline.Floor.RevisionNumber, baseline.VersionContentRevision, lease.ClientInstanceId,
                lease.LeaseId, Guid.NewGuid())
            : new CompensateSpaceUnderlayRequest(SpaceUnderlayHistoryVersions.SchemaVersion,
                SpaceUnderlayCompensationDirections.Undo, baseline.History.OriginalCommandBatchId, baseline.History.HistorySha256,
                Guid.NewGuid(), lease.ClientInstanceId, lease.LeaseId, baseline.Floor.RevisionNumber, baseline.VersionContentRevision);
        var path = operation == "calibrate" ? $"{versionPath}/sources/{source.Id:D}/underlay-calibration"
            : floorPath + ":compensate";
        return new(HttpMethod.Post, path, body, Guid.NewGuid().ToString("N"), HttpStatusCode.OK);
    }

    private static UnderlayInput UploadUnderlayInput(Guid versionId) => new(HttpMethod.Post,
        $"{SpaceDesignPath}/versions/{versionId:D}/underlay-sources", null, null, HttpStatusCode.Accepted);

    private static async Task<HttpResponseMessage> SendUnderlay(AuthenticationApiProcess host,
        Dictionary<string, string> actor, UnderlayInput input)
    {
        using var message = OperationsRequest(actor, input.Method, input.Path, input.Body);
        if (input.Key is { } key)
        {
            message.Headers.Add("Idempotency-Key", key);
        }
        else
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent("Pdf"), "SourceType");
            var content = new ByteArrayContent(UnderlayPdf);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(content, "File", "survey.pdf");
            message.Content = form;
        }
        return await host.Send(message);
    }

    private static async Task<NpgsqlTransaction> HoldUnderlayTable(NpgsqlConnection connection, string table)
    {
        var transaction = await connection.BeginTransactionAsync();
        using var quoting = new NpgsqlCommandBuilder();
        await using var hold = new NpgsqlCommand($"LOCK TABLE public.{quoting.QuoteIdentifier(table)} IN SHARE MODE", connection, transaction);
        await hold.ExecuteNonQueryAsync();
        return transaction;
    }

    private static async Task<NpgsqlTransaction> HoldUnderlayTenant(NpgsqlConnection connection, Guid tenant)
    {
        var transaction = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", connection, transaction);
        hold.Parameters.AddWithValue("tenant", tenant);
        Assert.Equal(1, await hold.ExecuteScalarAsync());
        return transaction;
    }

    private static string NewUnderlayFileRoot() => Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
        "underlay-files", Guid.NewGuid().ToString("N"));

    private static string[] UnderlayFiles(string root, string filter) =>
        Directory.Exists(root) ? Directory.GetFiles(root, filter, SearchOption.AllDirectories) : [];

    private static async Task<string> UnderlayWriteSet(SpaceContext space, string fileRoot)
        => JsonSerializer.Serialize(new
        {
            Versions = await space.Versions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Files = await space.Files.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Sources = await space.Sources.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Floors = await space.FloorRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Calibrations = await space.UnderlayCalibrations.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Jobs = await space.Jobs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Receipts = await space.IdempotencyRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Batches = await space.ElementCommandBatches.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Commands = await space.ElementCommandRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Leases = await space.EditLeases.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Content = UnderlayFiles(fileRoot, "*.content").Select(file => Path.GetRelativePath(fileRoot, file)).Order().ToArray(),
            Temporary = UnderlayFiles(fileRoot, "*.upload").Select(file => Path.GetRelativePath(fileRoot, file)).Order().ToArray()
        });

    private static async Task CheckUnderlayResult(SpaceContext space, string fileRoot, Guid versionId, Guid floorId, string operation,
        HttpResponseMessage result, bool reused)
    {
        var expected = operation == "upload" ? HttpStatusCode.Accepted : HttpStatusCode.OK;
        Assert.True(result.StatusCode == expected, await OperationsFailure(result));
        var version = await space.Versions.AsNoTracking().SingleAsync(x => x.Id == versionId);
        var floor = await space.FloorRevisions.AsNoTracking().SingleAsync(x => x.ModelVersionId == versionId && x.LogicalId == floorId);
        var source = await space.Sources.AsNoTracking().SingleAsync();
        var file = await space.Files.AsNoTracking().SingleAsync();
        Assert.Equal(file.Id, source.FileId);
        Assert.Empty(UnderlayFiles(fileRoot, "*.upload"));
        if (operation == "upload")
        {
            Assert.Equal(1, version.ContentRevision);
            Assert.Null(floor.UnderlaySourceId);
            Assert.Equal(SpaceFileState.Quarantined, file.State);
            Assert.Equal(SpaceSourceState.Scanning, source.State);
            Assert.Equal(UnderlayPdf, await File.ReadAllBytesAsync(Assert.Single(UnderlayFiles(fileRoot, "*.content"))));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(UnderlayPdf)).ToLowerInvariant(), file.Sha256);
            var scan = await space.Jobs.AsNoTracking().SingleAsync();
            Assert.Equal(SpaceJobType.FileScan, scan.JobType);
            Assert.Equal(SpaceJobStatus.Queued, scan.Status);
            Assert.Equal(file.Id, scan.SubjectId);
            var response = Assert.IsType<UploadSpaceUnderlayResponse>(await result.Content.ReadFromJsonAsync<UploadSpaceUnderlayResponse>());
            Assert.Equal(file.Id, response.File.Id);
            Assert.Equal(source.Id, response.Source.Id);
            Assert.Equal(scan.Id, response.ScanJobId);
            Assert.Equal(reused, response.Reused);
            return;
        }

        var revision = operation == "attach" ? 1 : 2;
        Assert.Equal(revision, version.ContentRevision);
        Assert.Equal(revision, floor.Revision);
        Assert.Equal(operation == "compensate" ? (Guid?)null : source.Id, floor.UnderlaySourceId);
        if (operation == "calibrate") Assert.NotNull(floor.UnderlayCalibrationId);
        else Assert.Null(floor.UnderlayCalibrationId);
        Assert.Equal(operation == "calibrate" ? 1 : 0, await space.UnderlayCalibrations.CountAsync());
        Assert.Equal(revision, await space.ElementCommandBatches.CountAsync());
        Assert.Equal(revision, await space.ElementCommandRecords.CountAsync());
        Assert.Equal(revision, await space.IdempotencyRecords.CountAsync());
        Assert.Empty(await space.Jobs.AsNoTracking().ToArrayAsync());
        Assert.Equal(reused ? "true" : "false", Assert.Single(result.Headers.GetValues("Idempotent-Replay")));
    }

    private sealed record UnderlayInput(HttpMethod Method, string Path, object? Body, string? Key, HttpStatusCode Status);
}
