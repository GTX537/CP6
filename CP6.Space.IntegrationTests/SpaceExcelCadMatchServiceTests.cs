using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExcelCadMatchServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 8, 16, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Start_pins_authoritative_chain_and_replays_one_active_job()
    {
        await using var fixture = await CreateFixtureAsync();
        var request = fixture.Request;

        var first = await fixture.Service.StartAsync(
            fixture.Version.Id,
            request,
            "match-key-1");
        var replay = await fixture.Service.StartAsync(
            fixture.Version.Id,
            request,
            "match-key-1");

        Assert.Equal(first.JobId, replay.JobId);
        Assert.True(replay.IdempotentReplay);
        var job = await fixture.Context.Jobs.SingleAsync(item =>
            item.Id == first.JobId);
        Assert.Equal(SpaceJobType.ExcelCadMatch, job.JobType);
        Assert.Equal(SpaceJobSubjectType.ModelSource, job.SubjectType);
        Assert.Equal(request.ExcelSourceId, job.SubjectId);
        var payload = JsonSerializer.Deserialize<SpaceExcelCadMatchJobPayload>(
            job.PayloadJson,
            JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(request.ExcelSourceId, payload!.ExcelSourceId);
        Assert.Equal(request.CadParseJobId, payload.CadParseJobId);
        Assert.Equal(request.ExpectedContentRevision,
            payload.ExpectedContentRevision);
        Assert.Equal(3, await fixture.Context.Jobs.CountAsync());
        Assert.Single(await fixture.Context.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task Start_rejects_content_revision_drift_before_queueing()
    {
        await using var fixture = await CreateFixtureAsync();
        var request = fixture.Request with { ExpectedContentRevision = 1 };

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.StartAsync(
                fixture.Version.Id,
                request,
                "match-key-drift"));

        Assert.Equal(SpaceErrorCodes.ConcurrencyConflict, error.Code);
        Assert.Equal(2, await fixture.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task External_principal_cannot_create_or_read_match_artifacts()
    {
        await using var fixture = await CreateFixtureAsync();
        var external = new ExternalExecutionContext(
            fixture.Context.CurrentTenantId,
            Guid.NewGuid());
        var service = new SpaceExcelCadMatchService(
            fixture.Context,
            external,
            new AllowAccess(),
            null!,
            new FileServiceProvider(fixture.Files),
            new FixedClock());

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.StartAsync(
                fixture.Version.Id,
                fixture.Request,
                "external-match"));

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
        Assert.Equal(2, await fixture.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task Worker_persists_one_authoritative_artifact_and_reuses_it()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Request,
            "match-worker-1");
        var lease = await ClaimAsync(fixture, started.JobId);
        var executor = new SpaceExcelCadMatchJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile));
        var execution = new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadMatchJobProcessor.PersistMatchArtifact);

        var first = await executor.ExecuteAsync(execution);
        var reused = await executor.ExecuteAsync(execution);

        Assert.Equal(first, reused);
        var persisted = await (
                from artifact in fixture.Context.Artifacts.AsNoTracking()
                join file in fixture.Context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId == started.JobId
                select new { Artifact = artifact, File = file })
            .SingleAsync();
        Assert.Equal(
            SpaceArtifactType.ExcelCadMatchPreview,
            persisted.Artifact.ArtifactType);
        Assert.Equal(
            SpaceExcelCadMatchArtifactVersions.ArtifactSchema,
            persisted.Artifact.SchemaVersion);
        await using var stream = await fixture.Files.OpenQuarantinedReadAsync(
            persisted.File.TenantId,
            persisted.File.Id,
            persisted.File.StorageKey);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var value = SpaceExcelCadMatchArtifact.Deserialize(
            await reader.ReadToEndAsync());
        Assert.Equal(started.JobId, value.MatchJobId);
        Assert.Equal(fixture.Request.CadParseJobId, value.CadParseJobId);
        Assert.Equal(1, value.Preview.Summary.ExcelRackRowCount);

        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item =>
            item.Id == started.JobId);
        var attempt = await fixture.Context.JobAttempts.SingleAsync(item =>
            item.Id == lease.AttemptId);
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            attempt.WorkerId,
            Now,
            first.CheckpointJson);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var read = await fixture.Service.GetAsync(
            fixture.Version.Id,
            started.JobId,
            null,
            "R-001",
            null,
            false,
            50,
            null);

        Assert.Equal("Succeeded", read.JobStatus);
        Assert.Equal(persisted.Artifact.Id, read.ArtifactId);
        Assert.Single(read.Rows);
        Assert.Equal(1, read.TotalRowCount);
    }

    private static async Task<SpaceJobLease> ClaimAsync(
        Fixture fixture,
        Guid jobId)
    {
        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == jobId);
        var attempt = job.Claim(
            "match-worker",
            SpaceExcelCadMatchJobProcessor.Version,
            Now,
            TimeSpan.FromMinutes(5));
        fixture.Context.JobAttempts.Add(attempt);
        await fixture.Context.SaveChangesAsync();
        return new SpaceJobLease(
            job.TenantId,
            job.Id,
            attempt.Id,
            attempt.AttemptNo,
            attempt.WorkerId,
            job.JobType,
            job.SubjectType,
            job.SubjectId,
            job.InputHash,
            job.LockExpiresAtUtc!.Value,
            job.RowVersion);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tenantId = Guid.NewGuid();
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            new FixedClock());
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published");
        published.BeginValidation();
        published.MarkReady(new string('a', 64), "space-v1", new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            2,
            "Draft",
            published.Id);
        model.ReserveDraft(version);

        var excelBytes = Encoding.UTF8.GetBytes("excel-source");
        var excelFile = CleanFile(
            tenantId,
            "racks.xlsx",
            ".xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SpaceFileRetentionClass.Source,
            excelBytes);
        var excel = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Excel,
            excelFile,
            "racks.xlsx");
        var mappingProfileId = Guid.NewGuid();
        excel.ConfigureImport(
            SpaceExcelPreflightJobProcessor.Version,
            mappingProfileId,
            1,
            null,
            null,
            null);
        excel.BeginParsing();
        excel.MarkPreviewReady();

        var cadBytes = Encoding.UTF8.GetBytes("cad-source");
        var cadFile = CleanFile(
            tenantId,
            "warehouse.dxf",
            ".dxf",
            "application/vnd.autocad.dxf",
            SpaceFileRetentionClass.Source,
            cadBytes);
        var cad = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Dxf,
            cadFile,
            "warehouse.dxf");
        var floorId = Guid.NewGuid();
        var cadAuthority = BuildCadAuthority(
            tenantId,
            cad.Id,
            cadFile.Sha256!,
            floorId);
        var metadata = cadAuthority.Metadata;
        var transformHash = cadAuthority.Preview.CoordinateTransformSha256;
        cad.ConfigureImport(
            SpaceCadParseJobProcessor.Version,
            cadAuthority.Profile.ProfileId,
            cadAuthority.Profile.Version,
            SpaceCadUnit.Millimeter.ToString(),
            1,
            JsonSerializer.Serialize(metadata, JsonOptions));
        cad.BeginParsing();
        cad.MarkPreviewReady();

        var preflightPayload = new SpaceExcelPreflightJobPayload(
            1,
            version.Id,
            excel.Id,
            mappingProfileId,
            1,
            new string('d', 64));
        var preflight = SucceededJob(
            tenantId,
            execution.ActorId,
            SpaceJobType.ExcelPreview,
            excel.Id,
            JsonSerializer.Serialize(preflightPayload, JsonOptions));
        var cadPayload = new SpaceCadParseJobPayload(
            1,
            version.Id,
            cad.Id,
            cadFile.Id,
            cadFile.Sha256!,
            SpaceCadSourceFormat.Dxf,
            floorId,
            SpaceCadUnit.Millimeter,
            1,
            JsonSerializer.Serialize(metadata, JsonOptions),
            transformHash,
            cad.MappingProfileId!.Value,
            1,
            cadAuthority.Profile.DefinitionSha256,
            cadAuthority.Preview.MappingPreviewSha256);
        var cadParse = SucceededJob(
            tenantId,
            execution.ActorId,
            SpaceJobType.CadParse,
            cad.Id,
            JsonSerializer.Serialize(cadPayload, JsonOptions));
        var previewSet = SpaceCadPreviewSet.Create(
            tenantId,
            version.Id,
            cad.Id,
            cadParse.Id,
            cadAuthority.Preview,
            cadAuthority.Diagnostics);
        var previewBytes = Encoding.UTF8.GetBytes(
            SpaceCadPreviewSet.Serialize(previewSet));
        var previewFile = CleanFile(
            tenantId,
            "preview-set.json",
            ".json",
            "application/json",
            SpaceFileRetentionClass.Artifact,
            previewBytes);
        var previewArtifact = SpaceArtifact.Create(
            tenantId,
            version.Id,
            cad,
            previewFile,
            SpaceArtifactType.PreviewSet,
            SpaceCadPreviewSetVersions.ArtifactSchema);
        previewArtifact.AttachToJob(cadParse);

        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            floorId,
            model.SiteId,
            1,
            "F01",
            "Floor 01");
        var excelDefinition = ExcelDefinition();
        var excelProfile = new SpaceExcelMappingProfileDto(
            mappingProfileId,
            "Authoritative match test",
            "Tenant",
            1,
            false,
            preflightPayload.MappingDefinitionHash,
            excelDefinition,
            null,
            null,
            null,
            null,
            null);
        var workbook = ExcelWorkbook();
        var files = new MemoryFileStore();
        files.Seed(excelFile.StorageKey, excelBytes);
        files.Seed(cadFile.StorageKey, cadBytes);
        files.Seed(previewFile.StorageKey, previewBytes);

        context.AddRange(
            model,
            published,
            version,
            excelFile,
            excel,
            cadFile,
            cad,
            preflight,
            cadParse,
            previewFile,
            previewArtifact,
            floor);
        await context.SaveChangesAsync();
        var service = new SpaceExcelCadMatchService(
            context,
            execution,
            new AllowAccess(),
            null!,
            new FileServiceProvider(files),
            new FixedClock());
        return new Fixture(
            context,
            version,
            new StartSpaceExcelCadMatchRequest(
                excel.Id,
                preflight.Id,
                cad.Id,
                cadParse.Id,
                floorId,
                version.ContentRevision),
            service,
            files,
            excelProfile,
            workbook);
    }

    private static SpaceJob SucceededJob(
        Guid tenantId,
        Guid actorId,
        SpaceJobType type,
        Guid subjectId,
        string payloadJson)
    {
        var job = SpaceJob.CreateQueued(
            tenantId,
            type,
            SpaceJobSubjectType.ModelSource,
            subjectId,
            new string(type == SpaceJobType.ExcelPreview ? '1' : '2', 64),
            Hash(payloadJson),
            50,
            3,
            actorId,
            Now,
            Guid.NewGuid(),
            payloadJson);
        var attempt = job.Claim(
            "test-worker",
            "test-processor",
            Now,
            TimeSpan.FromMinutes(5));
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            attempt.WorkerId,
            Now,
            "{}");
        return job;
    }

    private static SpaceFile CleanFile(
        Guid tenantId,
        string name,
        string extension,
        string contentType,
        SpaceFileRetentionClass retention,
        byte[] bytes)
    {
        var id = Guid.NewGuid();
        var file = SpaceFile.CreateUploading(
            id,
            tenantId,
            $"{tenantId:N}/{id:N}/content",
            name,
            contentType,
            retention);
        file.CompleteQuarantine(contentType, extension, bytes.Length, Hash(bytes));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        return file;
    }

    private static CadAuthority BuildCadAuthority(
        Guid tenantId,
        Guid sourceId,
        string sourceSha256,
        Guid floorId)
    {
        var request = new SpaceCadConversionRequest(
            tenantId,
            Guid.NewGuid(),
            sourceId,
            sourceSha256,
            SpaceCadSourceFormat.Dxf,
            "match-test",
            "1.0.0");
        var bounds = new SpaceCadBoundsV1(0, 0, 1_000, 1_200);
        var points = new SpaceCadPointV1[]
        {
            new(0, 0),
            new(1_000, 0),
            new(1_000, 1_200),
            new(0, 1_200),
            new(0, 0),
        };
        var entity = new SpaceCadIrEntityV1(
            "H:160",
            SpaceCadIrEntityType.ClosedPolyline,
            "LWPOLYLINE",
            "RACK",
            null,
            points,
            null,
            null,
            null,
            SpaceCadAffineTransformV1.Identity,
            bounds,
            true,
            true,
            new Dictionary<string, string> { ["CODE"] = "R-001" });
        var package = new SpaceCadIrPackageV1(
            new SpaceCadIrDocumentV1(
                SpaceCadIrVersions.SchemaVersion,
                sourceSha256,
                SpaceCadSourceFormat.Dxf,
                "AC1032",
                SpaceCadUnit.Millimeter,
                1,
                SpaceCadIrVersions.CoordinateSystem,
                bounds,
                request.ConverterId,
                request.ConverterVersion),
            [new SpaceCadIrLayerV1("RACK", "RACK", 1)],
            [],
            [entity],
            [],
            new SpaceCadIrSummaryV1(1, 0, 1, 1, 0, 0, bounds));
        var preparation = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            new SpaceCadCoordinateConfirmationV1(
                sourceSha256,
                true,
                SpaceCadUnit.Millimeter,
                new SpaceCadPointV1(0, 0),
                new SpaceCadMillimeterPointV1(0, 0),
                0,
                new SpaceCadFloorAssignmentV1(
                    floorId,
                    "F01",
                    1,
                    0,
                    SpaceCadCoordinateVersions.TargetCoordinateSystem,
                    new SpaceCadBoundsV1(-10_000, -10_000, 20_000, 20_000))));
        var inventory = SpaceCadInventory.Build(request, preparation);
        var profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
            SpaceCadMappingVersions.SchemaVersion,
            Guid.NewGuid(),
            1,
            "Match test profile",
            SpaceCadMappingScope.System,
            null,
            true,
            null,
            null,
            [new SpaceCadMappingRuleV1(
                "L-RACK",
                100,
                SpaceCadMappingSourceKind.Layer,
                SpaceCadMappingMatchKind.Exact,
                "RACK",
                null,
                null,
                null,
                SpaceCadSemanticTarget.Rack,
                null,
                SpaceCadGeometryRule.ClosedBoundary,
                5_000,
                null,
                0.95m,
                true)]));
        var mapping = SpaceCadMapping.Preview(tenantId, inventory, profile);
        var preview = SpaceCadSemanticParser.Parse(
            request,
            preparation,
            inventory,
            profile,
            mapping);
        var diagnostics = SpaceCadSemanticDiagnostics.Build(
            request,
            preparation,
            inventory,
            profile,
            mapping,
            preview);
        return new CadAuthority(
            preparation.Metadata,
            profile,
            preview,
            diagnostics);
    }

    private static SpaceExcelMappingDefinitionDto ExcelDefinition() => new(
        SpaceExcelTargetCatalog.MappingSchemaVersion,
        "Ignore",
        "Reject",
        "Reject",
        [new SpaceExcelSheetMappingDto(
            "Racks",
            "Racks",
            "Exact",
            1,
            2,
            SpaceExcelTargetCatalog.ForSheet("Racks")
                .Select(field => new SpaceExcelColumnMappingDto(
                    field.Field,
                    field.Field,
                    null,
                    field.DataType,
                    null,
                    null,
                    field.IsBusinessKey,
                    field.ReferenceTarget,
                    [],
                    null))
                .ToArray())]);

    private static SpaceExcelWorkbookData ExcelWorkbook()
    {
        var fields = SpaceExcelTargetCatalog.ForSheet("Racks");
        var values = new Dictionary<string, string?>
        {
            ["FloorCode"] = "F01",
            ["ZoneCode"] = "Z1",
            ["RackCode"] = "R-001",
            ["XMm"] = "0",
            ["YMm"] = "0",
            ["ZMm"] = "0",
            ["WidthMm"] = "1000",
            ["DepthMm"] = "1200",
            ["HeightMm"] = "5000",
            ["RotationZDeg"] = "0",
            ["RackTemplateCode"] = null,
            ["LifecycleStatus"] = "Active",
        };
        var header = new SpaceExcelWorkbookRow(
            1,
            fields.Select((field, index) => new SpaceExcelWorkbookCell(
                    index + 1,
                    ColumnName(index + 1),
                    field.Field,
                    false))
                .ToDictionary(item => item.ColumnIndex));
        var row = new SpaceExcelWorkbookRow(
            2,
            fields.Select((field, index) => new SpaceExcelWorkbookCell(
                    index + 1,
                    ColumnName(index + 1),
                    values.GetValueOrDefault(field.Field),
                    false))
                .ToDictionary(item => item.ColumnIndex));
        return new SpaceExcelWorkbookData(
            [new SpaceExcelWorkbookSheet("Racks", [header, row])]);
    }

    private static string ColumnName(int index)
    {
        var value = index;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static string Hash(string value) =>
        Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed record ExternalExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext
    {
        public bool IsExternal => true;
    }

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class FixedWorkbookReader(SpaceExcelWorkbookData workbook) :
        ISpaceExcelWorkbookReader
    {
        public Task<SpaceExcelWorkbookData> ReadAsync(
            Stream content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(workbook);
    }

    private sealed class FixedMappingService(SpaceExcelMappingProfileDto profile) :
        ISpaceExcelMappingService
    {
        public Task<IReadOnlyList<SpaceExcelMappingProfileDto>> GetProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpaceExcelMappingProfileDto>>([profile]);

        public Task<SpaceExcelMappingProfileDto> GetProfileAsync(
            Guid profileId,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(profile);

        public SpaceExcelMappingPreviewDto Preview(
            PreviewSpaceExcelMappingRequest request) =>
            throw new NotSupportedException();

        public Task<SaveSpaceExcelMappingProfileResponse> SaveProfileAsync(
            SaveSpaceExcelMappingProfileRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryFileStore :
        ISpaceFileStore,
        ISpaceQuarantineStore
    {
        private readonly Dictionary<string, byte[]> _objects =
            new(StringComparer.Ordinal);

        public void Seed(string storageKey, byte[] bytes) =>
            _objects[storageKey] = bytes;

        public Task<ISpaceQuarantineWriteSession> OpenWriteAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            var key = $"{tenantId:N}/{fileId:N}/{Guid.NewGuid():N}.content";
            return Task.FromResult<ISpaceQuarantineWriteSession>(
                new WriteSession(key, _objects));
        }

        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(
                _objects[storageKey],
                writable: false));

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            _objects.Remove(storageKey);
            return Task.CompletedTask;
        }

        private sealed class WriteSession(
            string storageKey,
            IDictionary<string, byte[]> objects) :
            ISpaceQuarantineWriteSession
        {
            private readonly MemoryStream _content = new();
            private bool _committed;

            public string StorageKey { get; } = storageKey;
            public Stream Content => _content;

            public Task CommitAsync(
                CancellationToken cancellationToken = default)
            {
                objects[StorageKey] = _content.ToArray();
                _committed = true;
                return Task.CompletedTask;
            }

            public Task AbortAsync(
                CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync()
            {
                _content.Dispose();
                if (!_committed)
                    objects.Remove(StorageKey);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FileServiceProvider(MemoryFileStore files) :
        IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISpaceFileStore) ||
            serviceType == typeof(ISpaceQuarantineStore)
                ? files
                : null;
    }

    private sealed record CadAuthority(
        SpaceCadCoordinateMetadataV1 Metadata,
        SpaceCadMappingProfileV1 Profile,
        SpaceCadSemanticPreviewV1 Preview,
        SpaceCadSemanticDiagnosticIndexV1 Diagnostics);

    private sealed record Fixture(
        SpaceContext Context,
        SpaceModelVersion Version,
        StartSpaceExcelCadMatchRequest Request,
        SpaceExcelCadMatchService Service,
        MemoryFileStore Files,
        SpaceExcelMappingProfileDto Profile,
        SpaceExcelWorkbookData Workbook) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
