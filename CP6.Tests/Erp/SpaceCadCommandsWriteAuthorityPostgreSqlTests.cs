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
    [InlineData("prepare", true)]
    [InlineData("prepare", false)]
    [InlineData("scan-sync", true)]
    [InlineData("scan-sync", false)]
    [InlineData("scan-rejected", true)]
    [InlineData("scan-rejected", false)]
    [InlineData("start", true)]
    [InlineData("start", false)]
    [InlineData("cancel", true)]
    [InlineData("cancel", false)]
    [InlineData("retry", true)]
    [InlineData("retry", false)]
    [InlineData("excel", true)]
    [InlineData("excel", false)]
    public async Task PGT017_space_source_processing_and_pause_share_current_authority(string operation, bool businessFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        var menu = await GrantSpaceDesignActions(writer, "model:edit", SpaceSourceUploadAction);
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, _) = await SeedSpaceDesignDraft(space, execution, site);
        var editingFloor = await space.FloorRevisions.SingleAsync(row => row.Id == floor.Id);
        editingFloor.ConfigureBoundary("[[0,0],[100000,0],[100000,100000],[0,100000]]", SpaceCadCoordinateVersions.TargetCoordinateSystem);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();

        var fileRoot = NewUnderlayFileRoot();
        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = typeof(SpaceCadFixtureHostingStartup).Assembly.GetName().Name!;
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpaceCadFixtureHostingStartup).Assembly.Location;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_PROCESSING_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var input = await PrepareSourceProcessingInput(host, actor, space, writer.User, version.Id, floor.LogicalId, operation);
        if (operation is "cancel" or "retry")
        {
            var uploadGrant = await writer.Db.Sys_RoleActions.SingleAsync(row => row.RoleId == 72 && row.MenuId == menu &&
                row.ActionCode == SpaceSourceUploadAction);
            writer.Db.Sys_RoleActions.Remove(uploadGrant);
            await writer.Db.SaveChangesAsync();
        }
        var before = await SourceProcessingWriteSet(space, fileRoot);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await using var output = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();
        var interruptedSync = operation == "scan-sync" && businessFirst;
        if (interruptedSync)
            await output.OpenAsync();

        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage> pause;
        FenceWait businessWait, pauseWait;
        FenceWait? secondWait = null;
        string? afterSync = null;
        var direction = businessFirst ? "business-first" : "pause-first";
        await using (var outputBarrier = interruptedSync ? await HoldUnderlayTable(output, "Space_CadParsePreparation") : null)
        await using (var blocked = await HoldUnderlayTable(business, input.Table))
        await using (var held = await HoldUnderlayTenant(control, writer.Tenant))
        {
            if (businessFirst)
            {
                pending = SendSourceProcessing(host, actor, input);
                businessWait = await WaitForFence(observer, business.ProcessID, $"%{input.Table}%", null, pending);
                pause = Pause();
                pauseWait = await SerialNativeWait(observer, businessWait.Backend, control.ProcessID, $"space-processing-{operation}-business-first", pause);
            }
            else
            {
                pause = Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                pending = SendSourceProcessing(host, actor, input);
                businessWait = await SerialNativeWait(observer, pauseWait.Backend, business.ProcessID, $"space-processing-{operation}-pause-first", pending);
            }

            if (interruptedSync)
            {
                await blocked.RollbackAsync();
                var heldPause = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                secondWait = await SerialNativeWait(observer, heldPause.Backend, output.ProcessID, "space-processing-scan-after-commit", pending);
                afterSync = await SourceProcessingWriteSet(space, fileRoot);
                Assert.Equal(SpaceSourceState.Ready, (await space.Sources.AsNoTracking().SingleAsync()).State);
                Assert.Empty(await space.CadParsePreparations.AsNoTracking().ToArrayAsync());
                await held.RollbackAsync();
                await pause;
                await outputBarrier!.RollbackAsync();
            }
            else
            {
                await held.RollbackAsync();
                if (pauseWait.Blocker == control.ProcessID)
                    await pause;
                await blocked.RollbackAsync();
            }
        }

        using var response = await pending;
        using var paused = await pause;
        var after = await SourceProcessingWriteSet(space, fileRoot);
        var committed = businessFirst && !interruptedSync;
        var expected = committed ? input.Status : HttpStatusCode.Unauthorized;
        await DeviceEvidence($"space-processing-{operation}-{direction}.json", new
        {
            BusinessWait = businessWait, PauseWait = pauseWait, SecondWait = secondWait,
            Expected = (int)expected, Actual = (int)response.StatusCode, Before = before, AfterSync = afterSync, After = after,
        });
        Assert.True(response.StatusCode == expected, response.StatusCode == expected ? null : await OperationsFailure(response));
        if (businessFirst)
            Assert.Equal(businessWait.Backend, pauseWait.Blocker);
        else
        {
            Assert.Equal(pauseWait.Backend, businessWait.Blocker);
            Assert.Equal(before, after);
        }
        if (interruptedSync)
        {
            Assert.Equal(pauseWait.Backend, secondWait!.Blocker);
            Assert.Equal(afterSync, after);
        }
        Guid? original = committed ? await CheckSourceProcessing(space, input, operation, writer.User, response) : null;

        using var obsolete = await SendSourceProcessing(host, actor, input);
        Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused);
        actor = await Login(host, writer);
        if (committed && operation == "prepare")
        {
            Assert.Equal(after, await SourceProcessingWriteSet(space, fileRoot));
            return;
        }
        using var recovery = await SendSourceProcessing(host, actor, input);
        var recovered = await CheckSourceProcessing(space, input, operation, writer.User, recovery);
        if (original.HasValue)
        {
            Assert.Equal(original.Value, recovered);
            Assert.Equal(after, await SourceProcessingWriteSet(space, fileRoot));
        }
        if (input.Key is not null)
        {
            var saved = await SourceProcessingWriteSet(space, fileRoot);
            using var repeat = await SendSourceProcessing(host, actor, input);
            Assert.Equal("true", Assert.Single(repeat.Headers.GetValues("Idempotent-Replay")));
            Assert.Equal(recovered, await CheckSourceProcessing(space, input, operation, writer.User, repeat));
            Assert.Equal(saved, await SourceProcessingWriteSet(space, fileRoot));
        }

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private static async Task<SourceProcessingInput> PrepareSourceProcessingInput(AuthenticationApiProcess host,
        Dictionary<string, string> actor, SpaceContext space, Guid actorId, Guid versionId, Guid floorId, string operation)
    {
        var excel = operation == "excel";
        using var upload = await SendSourceUpload(host, actor, SourceUploadSample.For(excel ? "excel" : "cad", versionId));
        Assert.True(upload.StatusCode == HttpStatusCode.Accepted,
            upload.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(upload));
        var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var sourceId = uploaded.GetProperty("source").GetProperty("id").GetGuid();
        var fileId = uploaded.GetProperty("file").GetProperty("id").GetGuid();
        var scanId = uploaded.GetProperty("scanJobId").GetGuid();
        var file = await space.Files.SingleAsync(row => row.Id == fileId);
        var source = await space.Sources.SingleAsync(row => row.Id == sourceId);
        var scan = await space.Jobs.SingleAsync(row => row.Id == scanId);
        var now = DateTime.UtcNow;
        var attempt = scan.Claim("w605-simulated-scan", SpaceFileScanProcessor.ProcessorVersion, now, TimeSpan.FromMinutes(1));
        file.BeginScanning();
        if (operation == "scan-rejected")
        {
            const string rejection = "W605-SIMULATED-REJECTION";
            const string summary = "Simulated rejected scan outcome.";
            file.Reject(rejection, "w605-simulated-scan", "1");
            var decision = SpaceJobRetryPolicy.DecideAutomatic(SpaceJobFailureKind.Security, scan.AttemptCount, scan.MaxAttempts, now);
            scan.Fail(attempt.Id, attempt.WorkerId, SpaceJobFailureKind.Security, rejection, summary, decision, now);
            attempt.Fail(SpaceJobFailureKind.Security, rejection, summary, now);
        }
        else
        {
            file.MarkClean("w605-simulated-scan", "1");
            scan.Complete(attempt.Id, attempt.WorkerId, now);
            attempt.Succeed(now);
        }
        space.JobAttempts.Add(attempt);
        if (operation is not ("scan-sync" or "scan-rejected"))
            source.CompleteFileScan(file);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();

        var basePath = $"{SpaceDesignPath}/versions/{versionId:D}/sources/{sourceId:D}";
        var key = Guid.NewGuid().ToString("N");
        if (excel)
            return new($"{basePath}/excel-preflights", new StartSpaceExcelPreflightRequest(SpaceExcelMappingService.SystemStandardProfileId, 1),
                key, "Space_Job", HttpStatusCode.Accepted, sourceId, versionId, file.Sha256!);

        var profile = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        var previewInput = new SourceProcessingInput($"{basePath}/cad-preparations:preview",
            new PreviewSpaceCadPreparationRequest(floorId, SpaceCadUnit.Millimeter, new(0, 0), new(0, 0), 0,
                profile.ProfileId, profile.Version, []), null,
            operation is "scan-sync" or "scan-rejected" ? "Space_ModelSource" : "Space_CadParsePreparation",
            operation == "scan-rejected" ? HttpStatusCode.Conflict : HttpStatusCode.OK, sourceId, versionId, file.Sha256!);
        if (operation is "prepare" or "scan-sync" or "scan-rejected")
            return previewInput;

        using var prepared = await SendSourceProcessing(host, actor, previewInput);
        var preparationId = await CheckSourceProcessing(space, previewInput, "prepare", actorId, prepared);
        var preparation = await space.CadParsePreparations.AsNoTracking().SingleAsync(row => row.Id == preparationId);
        var start = new StartSpaceCadParseRequest(preparationId, floorId, SpaceCadUnit.Millimeter, preparation.ConfirmedScaleToMillimeters,
            preparation.CoordinateMetadataJson, preparation.CoordinateTransformSha256, preparation.MappingProfileId,
            preparation.MappingProfileVersion, preparation.MappingDefinitionSha256, preparation.MappingPreviewSha256);
        var startInput = new SourceProcessingInput($"{basePath}/cad-parses", start, key, "Space_Job", HttpStatusCode.Accepted,
            sourceId, versionId, file.Sha256!);
        if (operation == "start")
            return startInput;

        using var started = await SendSourceProcessing(host, actor, startInput);
        var predecessor = await CheckSourceProcessing(space, startInput, "start", actorId, started);
        if (operation == "retry")
        {
            using var cancelled = await SendSourceProcessing(host, actor, startInput with
            {
                Path = $"{basePath}/cad-parses/{predecessor:D}:cancel", Body = null, Key = null, OriginalJob = predecessor,
            });
            Assert.Equal(HttpStatusCode.Accepted, cancelled.StatusCode);
        }
        return new($"{basePath}/cad-parses/{predecessor:D}:{operation}", null,
            operation == "retry" ? Guid.NewGuid().ToString("N") : null, "Space_Job", HttpStatusCode.Accepted,
            sourceId, versionId, file.Sha256!, predecessor);
    }

    private static async Task<HttpResponseMessage> SendSourceProcessing(AuthenticationApiProcess host, Dictionary<string, string> actor,
        SourceProcessingInput input)
    {
        using var request = OperationsRequest(actor, HttpMethod.Post, input.Path, input.Body);
        if (input.Key is { } key)
            request.Headers.Add("Idempotency-Key", key);
        return await host.Send(request);
    }

    private static async Task<Guid> CheckSourceProcessing(SpaceContext space, SourceProcessingInput input, string operation,
        Guid actor, HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == input.Status,
            response.StatusCode == input.Status ? null : await OperationsFailure(response));
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var source = await space.Sources.AsNoTracking().SingleAsync(row => row.Id == input.SourceId);
        var version = await space.Versions.AsNoTracking().SingleAsync(row => row.Id == input.VersionId);
        Assert.Equal(input.SourceHash, source.Sha256);
        Assert.Equal(1, version.ContentRevision);
        Assert.Equal(SpaceVersionStatus.Draft, version.Status);
        Assert.Empty(await space.Artifacts.AsNoTracking().ToArrayAsync());
        if (operation == "scan-rejected")
        {
            Assert.Equal(SpaceErrorCodes.SourceUnsafe, content.GetProperty("code").GetString());
            Assert.Equal(SpaceSourceState.Rejected, source.State);
            Assert.Empty(await space.CadParsePreparations.AsNoTracking().ToArrayAsync());
            Assert.Empty(await space.IdempotencyRecords.AsNoTracking().ToArrayAsync());
            return source.Id;
        }
        if (operation is "prepare" or "scan-sync")
        {
            Assert.True(content.GetProperty("readyForParsing").GetBoolean());
            Assert.Equal(SpaceSourceState.Ready, source.State);
            var preparation = await space.CadParsePreparations.AsNoTracking().SingleAsync();
            Assert.Equal(preparation.Id, content.GetProperty("preparationId").GetGuid());
            Assert.Equal(input.SourceId, preparation.SourceId);
            Assert.Equal(input.SourceHash, preparation.SourceSha256);
            Assert.Equal(version.ContentRevision, preparation.BaseContentRevision);
            Assert.Equal("w605-local-cad", preparation.ProviderKey);
            Assert.Empty(await space.IdempotencyRecords.AsNoTracking().ToArrayAsync());
            return preparation.Id;
        }

        var jobs = await space.Jobs.AsNoTracking().Where(row => row.SubjectId == input.SourceId).ToArrayAsync();
        var id = content.GetProperty("jobId").GetGuid();
        var job = Assert.Single(jobs, row => row.Id == id);
        Assert.Equal(actor, job.RequestedBy);
        var receipts = await space.IdempotencyRecords.AsNoTracking().ToArrayAsync();
        if (operation == "cancel")
        {
            Assert.Equal(input.OriginalJob, job.Id);
            Assert.Equal(SpaceJobStatus.Cancelled, job.Status);
            Assert.Equal(actor, job.CancellationRequestedBy);
            Assert.Single(jobs);
            Assert.Single(receipts);
        }
        else
        {
            Assert.Equal(SpaceJobStatus.Queued, job.Status);
            Assert.Equal(operation == "excel" ? SpaceJobType.ExcelPreview : SpaceJobType.CadParse, job.JobType);
            Assert.Equal(operation == "excel" ? SpaceSourceState.Parsing : SpaceSourceState.Ready, source.State);
            if (operation == "retry")
            {
                var original = Assert.Single(jobs, item => item.Id == input.OriginalJob);
                Assert.Equal(original.Id, job.RetryOfJobId);
                Assert.Equal(original.InputHash, job.InputHash);
                Assert.Equal(original.PayloadJson, job.PayloadJson);
                Assert.Equal(2, jobs.Length);
                Assert.Equal(2, receipts.Length);
            }
            else
            {
                Assert.Single(jobs);
                Assert.Single(receipts);
                Assert.NotNull(source.MappingProfileId);
            }
        }
        return job.Id;
    }

    private static async Task<string> SourceProcessingWriteSet(SpaceContext space, string fileRoot) => JsonSerializer.Serialize(new
    {
        Space = await UnderlayWriteSet(space, fileRoot),
        Preparations = await space.CadParsePreparations.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Artifacts = await space.Artifacts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
    });

    private sealed record SourceProcessingInput(string Path, object? Body, string? Key, string Table, HttpStatusCode Status,
        Guid SourceId, Guid VersionId, string SourceHash, Guid? OriginalJob = null);
}
