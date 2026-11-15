using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Space.Observability;
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
    private static readonly JsonSerializerOptions ExcelCadJson = new(JsonSerializerDefaults.Web);

    [TenantManagementPostgresTheory]
    [InlineData("start", true)]
    [InlineData("start", false)]
    [InlineData("confirm", true)]
    [InlineData("confirm", false)]
    [InlineData("compensate", true)]
    [InlineData("compensate", false)]
    public async Task PGT017_space_excel_cad_foreground_writers_share_current_authority(string command, bool businessFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        var fileRoot = NewUnderlayFileRoot();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, lease) = await SeedSpaceDesignDraft(space, execution, site);

        var settings = SpaceDesignSettings(writer.Tenant, site);
        settings["Space__Files__RootPath"] = fileRoot;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_EXCEL_CAD_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var input = await PrepareExcelCadWrite(host, actor, space, execution, version, floor, lease, fileRoot, command);
        var before = await ExcelCadWriteSet(space, fileRoot);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage> pause;
        FenceWait businessWait;
        FenceWait pauseWait;
        var order = businessFirst ? "business-first" : "pause-first";
        await using (var blocked = await HoldUnderlayTable(business, input.Table))
        await using (var held = await HoldUnderlayTenant(control, writer.Tenant))
        {
            if (businessFirst)
            {
                pending = SendExcelCadWrite(host, actor, input);
                businessWait = await WaitForFence(observer, business.ProcessID, $"%{input.Table}%", null, pending);
                pause = Pause();
                pauseWait = await SerialNativeWait(observer, businessWait.Backend, control.ProcessID, $"space-excel-cad-{command}-{order}", pause);
            }
            else
            {
                pause = Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                pending = SendExcelCadWrite(host, actor, input);
                businessWait = await SerialNativeWait(observer, pauseWait.Backend, business.ProcessID, $"space-excel-cad-{command}-{order}", pending);
            }

            await held.RollbackAsync();
            if (pauseWait.Blocker == control.ProcessID)
                await pause;
            await blocked.RollbackAsync();
        }

        using var response = await pending;
        using var paused = await pause;
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        var after = await ExcelCadWriteSet(space, fileRoot);
        var audits = await writer.Db.SpaceAuditEvents.AsNoTracking()
            .Where(row => row.ActorId == writer.User.ToString() && row.Action == input.AuditAction)
            .OrderBy(row => row.OccurredAtUtc).Select(row => row.Outcome).ToArrayAsync();
        var expected = businessFirst ? input.Status : HttpStatusCode.Unauthorized;
        await DeviceEvidence($"space-excel-cad-{command}-{order}.json", new
        {
            BusinessWait = businessWait, PauseWait = pauseWait, Expected = (int)expected, Actual = (int)response.StatusCode,
            Before = before, After = after, AuditOutcomes = audits,
        });
        Assert.True(response.StatusCode == expected, response.StatusCode == expected ? null : await OperationsFailure(response));
        Assert.Equal(businessFirst ? 1 : 0, audits.Count(outcome => outcome == SpaceAuditOutcome.Succeeded));
        Guid? original = null;
        if (businessFirst)
        {
            Assert.Equal(businessWait.Backend, pauseWait.Blocker);
            original = await CheckExcelCadWrite(space, writer.User, input, response, before, after, replay: false);
        }
        else
        {
            Assert.Equal(pauseWait.Backend, businessWait.Blocker);
            Assert.Equal(before, after);
        }

        using var obsolete = await SendExcelCadWrite(host, actor, input);
        Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused);
        actor = await Login(host, writer);
        using var recovered = await SendExcelCadWrite(host, actor, input);
        var recoveredSet = await ExcelCadWriteSet(space, fileRoot);
        var effect = await CheckExcelCadWrite(space, writer.User, input, recovered, before, recoveredSet, replay: businessFirst);
        if (original.HasValue)
        {
            Assert.Equal(original, effect);
            Assert.Equal(after, recoveredSet);
        }
        using var repeated = await SendExcelCadWrite(host, actor, input);
        Assert.Equal(effect, await CheckExcelCadWrite(space, writer.User, input, repeated, before, recoveredSet, replay: true));
        Assert.Equal(recoveredSet, await ExcelCadWriteSet(space, fileRoot));

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private static async Task<ExcelCadWriteInput> PrepareExcelCadWrite(AuthenticationApiProcess host, Dictionary<string, string> actor,
        SpaceContext space, SpaceDesignExecution execution, SpaceModelVersion version, SpaceFloorRevision floor, SpaceEditLease lease,
        string fileRoot, string command)
    {
        var clock = new SystemSpaceClock();
        var store = new FileSystemSpaceFileStore(new SpaceFileStorageOptions { RootPath = fileRoot });
        var workbookReader = new OpenXmlSpaceExcelWorkbookReader();
        var mappings = new SpaceExcelMappingService(space, execution, clock);
        var profile = await mappings.GetProfileAsync(SpaceExcelMappingService.SystemStandardProfileId, 1);
        var request = await SeedExcelCadPreviewReady(space, execution, version, floor, store, profile);
        var key = Guid.NewGuid().ToString("N");
        var matchPath = $"{SpaceDesignPath}/versions/{version.Id:D}/excel-cad-matches";
        var start = new ExcelCadWriteInput(matchPath, request, key, "Space_Job", HttpStatusCode.Accepted,
            $"excel-cad-match:{version.Id:N}", "space.excel-cad-match.start", "jobId", request.ExcelSourceId, SpaceJobType.ExcelCadMatch);
        if (command == "start")
            return start;

        using var started = await SendExcelCadWrite(host, actor, start);
        var match = await ReadExcelCadResponse<StartSpaceExcelCadMatchResponse>(started, HttpStatusCode.Accepted);
        using var files = new ServiceCollection().AddSingleton<ISpaceFileStore>(store)
            .AddSingleton<ISpaceQuarantineStore>(store).BuildServiceProvider();
        var matcher = new SpaceExcelCadMatchJobStepExecutor(space, files, workbookReader, mappings);
        await RunExcelCadFixtureStep(space, match.JobId, SpaceExcelCadMatchJobProcessor.Version,
            SpaceExcelCadMatchJobProcessor.PersistMatchArtifact, matcher.ExecuteAsync);
        var artifact = await space.Artifacts.AsNoTracking().SingleAsync(item => item.JobId == match.JobId);
        var matchFile = await space.Files.AsNoTracking().SingleAsync(item => item.Id == artifact.FileId);
        await using var content = await store.OpenQuarantinedReadAsync(execution.TenantId, matchFile.Id, matchFile.StorageKey);
        using var streamReader = new StreamReader(content);
        var preview = SpaceExcelCadMatchArtifact.Deserialize(await streamReader.ReadToEndAsync());
        Assert.True(preview.Preview.CanConfirm, JsonSerializer.Serialize(preview.Preview));
        Assert.Equal(1, preview.Preview.Summary.NewCount);
        var confirmRequest = new ConfirmSpaceExcelCadMatchRequest(true, artifact.Id, preview.ArtifactPayloadSha256,
            version.ContentRevision, lease.ClientInstanceId, lease.LeaseId, floor.Revision);
        var confirmPath = $"{matchPath}/{match.JobId:D}/confirmations";
        var confirm = new ExcelCadWriteInput(confirmPath, confirmRequest, Guid.NewGuid().ToString("N"), "Space_Job", HttpStatusCode.Accepted,
            $"excel-cad-apply:{version.Id:N}:{match.JobId:N}", "space.excel-cad-match.confirm", "applyJobId", request.ExcelSourceId, SpaceJobType.ExcelCadApply);
        if (command == "confirm")
            return confirm;

        using var confirmed = await SendExcelCadWrite(host, actor, confirm);
        var apply = await ReadExcelCadResponse<ConfirmSpaceExcelCadMatchResponse>(confirmed, HttpStatusCode.Accepted);
        using var mainAuthority = new ServiceCollection()
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped(provider => new CP6Context(
                new DbContextOptionsBuilder<CP6Context>().UseNpgsql(space.Database.GetConnectionString()).Options,
                provider.GetRequiredService<ITenantContext>()))
            .BuildServiceProvider();
        var authority = new SpaceArtifactWriteAuthority(mainAuthority.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        var executor = new SpaceExcelCadApplyJobStepExecutor(space, files, workbookReader, mappings, clock, authority);
        await RunExcelCadFixtureStep(space, apply.ApplyJobId, SpaceExcelCadApplyJobProcessor.Version,
            SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact, executor.ExecuteAsync);
        var batch = await space.ElementCommandBatches.AsNoTracking().SingleAsync(item => item.Id == apply.CommandBatchId);
        var result = JsonSerializer.Deserialize<SpaceExcelCadApplyResultV1>(batch.ResponseJson!, ExcelCadJson)!;
        var compensate = new CompensateSpaceExcelCadApplyRequest(SpaceExcelCadApplyVersions.SchemaVersion,
            SpaceExcelCadCompensationDirections.Undo, Guid.NewGuid(), lease.ClientInstanceId, lease.LeaseId,
            result.ResultFloorRevision, result.ResultContentRevision, result.HistorySha256!);
        return new ExcelCadWriteInput($"{confirmPath}/{apply.ApplyJobId:D}:compensate", compensate, Guid.NewGuid().ToString("N"),
            "Space_ElementCommandBatch", HttpStatusCode.OK, $"excel-cad-apply-compensation:{apply.ApplyJobId:N}:{compensate.Direction}",
            "space.excel-cad-match.confirmation.compensate", "commandBatchId", request.ExcelSourceId, null);
    }

    private static async Task<StartSpaceExcelCadMatchRequest> SeedExcelCadPreviewReady(SpaceContext space, SpaceDesignExecution execution,
        SpaceModelVersion version, SpaceFloorRevision floor, FileSystemSpaceFileStore store, SpaceExcelMappingProfileDto excelProfile,
        byte[]? workbook = null)
    {
        var tenant = execution.TenantId;
        const string workbookMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var excelFile = await WriteExcelCadFixtureFile(store, tenant, "racks.xlsx", workbookMediaType,
            SpaceFileRetentionClass.Source, workbook ?? BuildExcelCadWorkbook(floor.FloorCode, excelProfile.Definition));
        var excel = SpaceModelSource.CreateFileSource(tenant, version.Id, SpaceSourceType.Excel, excelFile, "racks.xlsx");
        excel.ConfigureImport(SpaceExcelPreflightJobProcessor.Version, excelProfile.Id, excelProfile.Version, null, null, null);
        excel.BeginParsing();
        excel.MarkPreviewReady();
        var preflight = SeedExcelCadSucceededJob(space, execution, SpaceJobType.ExcelPreview, SpaceExcelPreflightJobProcessor.Version, excel.Id,
            new SpaceExcelPreflightJobPayload(1, version.Id, excel.Id, excelProfile.Id, excelProfile.Version, excelProfile.DefinitionHash));

        var cadSample = SourceUploadSample.For("cad", version.Id);
        var cad = await WriteExcelCadFixtureFile(store, tenant, cadSample.Name, cadSample.ContentType, SpaceFileRetentionClass.Source, cadSample.Bytes);
        var cadSource = SpaceModelSource.CreateFileSource(tenant, version.Id, SpaceSourceType.Dxf, cad, cadSample.Name);
        var conversion = new SpaceCadConversionRequest(tenant, Guid.NewGuid(), cadSource.Id, cad.Sha256!, SpaceCadSourceFormat.Dxf, "fixture", "1.0");
        var bounds = new SpaceCadBoundsV1(0, 0, 1000, 1200);
        var entity = new SpaceCadIrEntityV1("H:160", SpaceCadIrEntityType.ClosedPolyline, "LWPOLYLINE", "RACK", null,
            [new(0, 0), new(1000, 0), new(1000, 1200), new(0, 1200), new(0, 0)], null, null, null,
            SpaceCadAffineTransformV1.Identity, bounds, true, true, new Dictionary<string, string> { ["CODE"] = "R-001" });
        var package = new SpaceCadIrPackageV1(new SpaceCadIrDocumentV1(SpaceCadIrVersions.SchemaVersion, cad.Sha256!,
            SpaceCadSourceFormat.Dxf, "AC1032", SpaceCadUnit.Millimeter, 1, SpaceCadIrVersions.CoordinateSystem,
            bounds, conversion.ConverterId, conversion.ConverterVersion), [new("RACK", "RACK", 1)], [], [entity], [],
            new SpaceCadIrSummaryV1(1, 0, 1, 1, 0, 0, bounds));
        var preparation = SpaceCadCoordinatePreparation.Prepare(conversion, package, new SpaceCadCoordinateConfirmationV1(
            cad.Sha256!, true, SpaceCadUnit.Millimeter, new(0, 0), new(0, 0), 0,
            new SpaceCadFloorAssignmentV1(floor.LogicalId, floor.FloorCode, 1, 0, SpaceCadCoordinateVersions.TargetCoordinateSystem,
                new(-10000, -10000, 20000, 20000))));
        var inventory = SpaceCadInventory.Build(conversion, preparation);
        var profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(SpaceCadMappingVersions.SchemaVersion, Guid.NewGuid(),
            1, "Simulated rack mapping", SpaceCadMappingScope.System, null, true, null, null,
            [new SpaceCadMappingRuleV1("L-RACK", 100, SpaceCadMappingSourceKind.Layer, SpaceCadMappingMatchKind.Exact, "RACK",
                null, null, null, SpaceCadSemanticTarget.Rack, null, SpaceCadGeometryRule.ClosedBoundary, 5000, null, 0.95m, true)]));
        var mapped = SpaceCadMapping.Preview(tenant, inventory, profile);
        var semantic = SpaceCadSemanticParser.Parse(conversion, preparation, inventory, profile, mapped);
        var diagnostics = SpaceCadSemanticDiagnostics.Build(conversion, preparation, inventory, profile, mapped, semantic);
        var coordinates = JsonSerializer.Serialize(preparation.Metadata, ExcelCadJson);
        cadSource.ConfigureImport(SpaceCadParseJobProcessor.Version, profile.ProfileId, profile.Version,
            SpaceCadUnit.Millimeter.ToString(), 1, coordinates);
        cadSource.BeginParsing();
        cadSource.MarkPreviewReady();
        var cadParse = SeedExcelCadSucceededJob(space, execution, SpaceJobType.CadParse, SpaceCadParseJobProcessor.Version, cadSource.Id,
            new SpaceCadParseJobPayload(SpaceCadParsePayloadVersions.Current, version.Id, cadSource.Id, cad.Id, cad.Sha256!,
                SpaceCadSourceFormat.Dxf, floor.LogicalId, SpaceCadUnit.Millimeter, 1, coordinates, semantic.CoordinateTransformSha256,
                profile.ProfileId, profile.Version, profile.DefinitionSha256, semantic.MappingPreviewSha256,
                version.ContentRevision, version.ContentHash, null, null));
        var preview = SpaceCadPreviewSet.Create(tenant, version.Id, cadSource.Id, cadParse.Id, semantic, diagnostics,
            version.ContentRevision, version.ContentHash);
        var previewFile = await WriteExcelCadFixtureFile(store, tenant, "cad-preview.json", "application/json",
            SpaceFileRetentionClass.Artifact, Encoding.UTF8.GetBytes(SpaceCadPreviewSet.Serialize(preview)));
        var artifact = SpaceArtifact.Create(tenant, version.Id, cadSource, previewFile,
            SpaceArtifactType.PreviewSet, SpaceCadPreviewSetVersions.ArtifactSchema);
        artifact.AttachToJob(cadParse);
        var zone = SpaceZoneRevision.Create(tenant, version.Id, Guid.NewGuid(), floor.LogicalId, "Z1", 0, "Zone 1");
        space.AddRange(excelFile, excel, cad, cadSource, previewFile, artifact, zone);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return new(excel.Id, preflight.Id, cadSource.Id, cadParse.Id, floor.LogicalId, version.ContentRevision);
    }

    private static byte[] BuildExcelCadWorkbook(string floorCode, SpaceExcelMappingDefinitionDto definition)
    {
        using var workbook = new XLWorkbook();
        var rack = new Dictionary<string, string>
        {
            ["FloorCode"] = floorCode, ["ZoneCode"] = "Z1", ["RackCode"] = "R-001", ["XMm"] = "0", ["YMm"] = "0", ["ZMm"] = "0",
            ["WidthMm"] = "1000", ["DepthMm"] = "1200", ["HeightMm"] = "5000", ["RotationZDeg"] = "0", ["LifecycleStatus"] = "Active",
        };
        foreach (var sheet in definition.Sheets)
        {
            var worksheet = workbook.AddWorksheet(sheet.SourceSheet);
            for (var i = 0; i < sheet.Columns.Count; i++)
            {
                var column = sheet.Columns[i];
                worksheet.Cell(sheet.HeaderRow, i + 1).Value = column.TargetField;
                if (sheet.TargetSheet == "Racks" && rack.TryGetValue(column.TargetField, out var value))
                    worksheet.Cell(sheet.DataStartRow, i + 1).Value = value;
            }
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<SpaceFile> WriteExcelCadFixtureFile(FileSystemSpaceFileStore store, Guid tenant, string name,
        string mediaType, SpaceFileRetentionClass retention, byte[] bytes)
    {
        var fileId = Guid.NewGuid();
        await using var session = await store.OpenWriteAsync(tenant, fileId);
        await session.Content.WriteAsync(bytes);
        await session.CommitAsync();
        var file = SpaceFile.CreateUploading(fileId, tenant, session.StorageKey, name, mediaType, retention);
        file.CompleteQuarantine(mediaType, Path.GetExtension(name), bytes.LongLength, ExcelCadHash(bytes));
        file.BeginScanning();
        file.MarkClean("fixture", "1.0");
        return file;
    }

    private static SpaceJob SeedExcelCadSucceededJob(SpaceContext space, SpaceDesignExecution execution, SpaceJobType type,
        string processorVersion, Guid source, object payload)
    {
        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(payload, ExcelCadJson);
        var digest = ExcelCadHash(Encoding.UTF8.GetBytes(json));
        var job = SpaceJob.CreateQueued(execution.TenantId, type, SpaceJobSubjectType.ModelSource, source, digest, digest,
            50, 5, execution.ActorId, now, Guid.NewGuid(), json);
        var attempt = job.Claim("fixture", processorVersion, now, TimeSpan.FromMinutes(1));
        attempt.Succeed(now);
        job.Complete(attempt.Id, attempt.WorkerId, now, "{}");
        space.AddRange(job, attempt);
        return job;
    }

    private static async Task RunExcelCadFixtureStep(SpaceContext space, Guid jobId, string processorVersion, string step,
        Func<SpaceJobStepExecution, CancellationToken, Task<SpaceJobStepOutput>> execute)
    {
        var job = await space.Jobs.SingleAsync(row => row.Id == jobId);
        var now = DateTime.UtcNow;
        var attempt = job.Claim("fixture", processorVersion, now, TimeSpan.FromMinutes(1));
        space.JobAttempts.Add(attempt);
        await space.SaveChangesAsync();
        var lease = new SpaceJobLease(job.TenantId, job.Id, attempt.Id, attempt.AttemptNo, attempt.WorkerId, job.JobType,
            job.SubjectType, job.SubjectId, job.InputHash, job.LockExpiresAtUtc!.Value, job.RowVersion);
        space.ChangeTracker.Clear();

        var result = await execute(new SpaceJobStepExecution(lease, 1, step), CancellationToken.None);
        job = await space.Jobs.SingleAsync(row => row.Id == jobId);
        attempt = await space.JobAttempts.SingleAsync(row => row.Id == lease.AttemptId);
        now = DateTime.UtcNow;
        job.Complete(attempt.Id, attempt.WorkerId, now, result.CheckpointJson);
        attempt.Succeed(now);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
    }

    private static Task<HttpResponseMessage> SendExcelCadWrite(AuthenticationApiProcess host, Dictionary<string, string> actor, ExcelCadWriteInput input)
    {
        var request = OperationsRequest(actor, HttpMethod.Post, input.Path, input.Body);
        request.Headers.Add("Idempotency-Key", input.Key);
        return host.Send(request);
    }

    private static async Task<T> ReadExcelCadResponse<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected, response.StatusCode == expected ? null : await OperationsFailure(response));
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> CheckExcelCadWrite(SpaceContext space, Guid actor, ExcelCadWriteInput input,
        HttpResponseMessage response, string before, string after, bool replay)
    {
        var body = await ReadExcelCadResponse<JsonElement>(response, input.Status);
        var id = body.GetProperty(input.IdField).GetGuid();
        Assert.Equal(replay ? "true" : "false", Assert.Single(response.Headers.GetValues("Idempotent-Replay")));
        var receipt = await space.IdempotencyRecords.AsNoTracking().SingleAsync(row => row.Operation == input.ReceiptOperation);
        Assert.Equal(actor, receipt.PrincipalId);
        Assert.Equal((int)input.Status, receipt.HttpStatusCode);
        Assert.Equal(ExcelCadHash(Encoding.UTF8.GetBytes($"{input.ReceiptOperation}\n{input.Key}")), receipt.IdempotencyKeyHash);
        using var saved = JsonDocument.Parse(receipt.ResponseJson);
        Assert.Equal(id, saved.RootElement.GetProperty(input.IdField).GetGuid());

        using var prior = JsonDocument.Parse(before);
        using var current = JsonDocument.Parse(after);
        var changed = new List<string> { "Receipts" };
        var source = await space.Sources.AsNoTracking().SingleAsync(row => row.Id == input.SourceId);
        if (input.JobType is { } type)
        {
            changed.Add("Jobs");
            var job = await space.Jobs.AsNoTracking().SingleAsync(row => row.JobType == type && row.SubjectId == source.Id);
            Assert.Equal(id, job.Id);
            Assert.Equal(actor, job.RequestedBy);
            Assert.Equal(SpaceJobStatus.Queued, job.Status);
            Assert.Equal(ExcelCadHash(Encoding.UTF8.GetBytes(job.PayloadJson)), job.InputHash);
            Assert.Equal(SpaceSourceState.PreviewReady, source.State);
            if (input.Body is ConfirmSpaceExcelCadMatchRequest request)
            {
                var payload = JsonSerializer.Deserialize<SpaceExcelCadApplyJobPayload>(job.PayloadJson, ExcelCadJson)!;
                Assert.Equal(request.ArtifactId, payload.ArtifactId);
                Assert.Equal(request.ArtifactPayloadSha256, payload.ArtifactPayloadSha256);
                Assert.Equal(request.LeaseId, payload.LeaseId);
                Assert.Equal(body.GetProperty("commandBatchId").GetGuid(), payload.CommandBatchId);
                Assert.Equal(saved.RootElement.GetProperty("commandBatchId").GetGuid(), payload.CommandBatchId);
            }
        }
        else
        {
            changed.AddRange(["Sources", "Versions", "Floors", "Batches", "Records", "Racks"]);
            var request = Assert.IsType<CompensateSpaceExcelCadApplyRequest>(input.Body);
            Assert.Equal(request.CommandBatchId, id);
            Assert.Equal(request.HistorySha256, body.GetProperty("historySha256").GetString());
            Assert.Equal(SpaceSourceState.PreviewReady, source.State);
            Assert.Null(source.ImportedCommandBatchId);
            Assert.Equal(request.ExpectedContentRevision + 1, (await space.Versions.AsNoTracking().SingleAsync(row => row.Id == source.ModelVersionId)).ContentRevision);
            Assert.Equal(request.ExpectedFloorRevision + 1, (await space.FloorRevisions.AsNoTracking().SingleAsync()).Revision);
            var batch = await space.ElementCommandBatches.AsNoTracking().SingleAsync(row => row.Id == id);
            Assert.Equal(actor, batch.AppliedBy);
            Assert.Equal(request.HistorySha256, batch.ChangesetSha256);
            var record = await space.ElementCommandRecords.AsNoTracking().SingleAsync(row => row.CommandBatchId == id);
            Assert.Equal("ExcelCadUndo:ExcelCadApplyRackNew", record.CommandType);
            var rack = await space.RackRevisions.AsNoTracking().SingleAsync();
            Assert.Equal("R-001", rack.RackCode);
            Assert.Equal(SpaceLifecycleState.Disabled, rack.LifecycleState);
        }

        foreach (var property in prior.RootElement.EnumerateObject().Where(item => !changed.Contains(item.Name)))
            Assert.Equal(property.Value.GetRawText(), current.RootElement.GetProperty(property.Name).GetRawText());
        foreach (var table in new[] { "Receipts", input.JobType is null ? "Batches" : "Jobs" })
            Assert.Equal(prior.RootElement.GetProperty(table).GetArrayLength() + 1, current.RootElement.GetProperty(table).GetArrayLength());
        return id;
    }

    private static async Task<string> ExcelCadWriteSet(SpaceContext space, string fileRoot) =>
        JsonSerializer.Serialize(new
        {
            Models = await space.Models.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Versions = await space.Versions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Floors = await space.FloorRevisions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Leases = await space.EditLeases.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Sources = await space.Sources.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Files = await space.Files.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Artifacts = await space.Artifacts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Jobs = await space.Jobs.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Attempts = await space.JobAttempts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Steps = await space.JobSteps.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Receipts = await space.IdempotencyRecords.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Batches = await space.ElementCommandBatches.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Records = await space.ElementCommandRecords.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Racks = await space.RackRevisions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Levels = await space.RackLevelRevisions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Locations = await space.LocationRevisions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Bindings = await space.LocationExternalBindings.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Attributes = await space.DesignAttributes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Blobs = UnderlayFiles(fileRoot, "*").Order(StringComparer.Ordinal).Select(path => new
            {
                Path = Path.GetRelativePath(fileRoot, path), Hash = ExcelCadHash(File.ReadAllBytes(path)),
            }).ToArray(),
        });

    private static string ExcelCadHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ExcelCadWriteInput(string Path, object Body, string Key, string Table, HttpStatusCode Status, string ReceiptOperation,
        string AuditAction, string IdField, Guid SourceId, SpaceJobType? JobType);
}
