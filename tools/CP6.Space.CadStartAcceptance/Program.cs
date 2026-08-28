using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Space.Application;
using CP6.Space.CadExperiment;
using CP6.Space.CadWorker.AutoCadCandidate;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.CadStartAcceptance;

internal static class Program
{
    private const string SqlServerEnvironment = "CP6_TEST_SQLSERVER";
    private const string ProviderKey = "cp6-autocad-worker";
    private const string ExpectedProviderVersion =
        "1.0.0+worker.c794e9c0ebbb.autocad.25.0.58.0.0.dxf.1.1.0";
    private const string GoldenDatasetSha256 =
        "2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15";
    private const string SourceSetSha256 =
        "7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955";
    private static readonly DateTime AcceptanceClock =
        new(2026, 8, 28, 15, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var report = await ExecuteAsync(options);
            var outputPath = Path.GetFullPath(options.OutputPath);
            if (File.Exists(outputPath))
                throw new IOException("The acceptance output already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
            await File.WriteAllBytesAsync(outputPath, bytes);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                report.Conclusion,
                outputPath,
                outputSha256 = Sha256(bytes),
                sampleCount = report.Samples.Length,
                report.Provider.ProviderKey,
                report.Provider.ProviderVersion,
            }, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<AcceptanceReport> ExecuteAsync(Options options)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerEnvironment);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            throw new InvalidOperationException(
                $"Set {SqlServerEnvironment} to a disposable SQL Server integration instance.");
        }

        var controlledManifestPath = Path.Combine(
            options.DatasetRoot,
            "controlled-manifest.json");
        var controlledManifestBytes = await File.ReadAllBytesAsync(controlledManifestPath);
        using var controlledManifest = JsonDocument.Parse(controlledManifestBytes);
        var dataset = controlledManifest.RootElement.GetProperty("dataset");
        RequireEqual(
            GoldenDatasetSha256,
            dataset.GetProperty("goldenDatasetSha256").GetString(),
            "golden dataset SHA-256");
        RequireEqual(
            SourceSetSha256,
            dataset.GetProperty("sourceSetSha256").GetString(),
            "source set SHA-256");
        if (!dataset.GetProperty("isImmutable").GetBoolean() ||
            !dataset.GetProperty("integrityAuditPassed").GetBoolean() ||
            !dataset.GetProperty("conversionValidationPassed").GetBoolean())
        {
            throw new InvalidDataException(
                "The controlled CAD dataset is not frozen and validated.");
        }

        var selected = new[]
        {
            LoadSample(dataset, options.DatasetRoot, "L1-C01", "DWG"),
            LoadSample(dataset, options.DatasetRoot, "L1-C02", "DXF"),
        };
        foreach (var sample in selected)
            await sample.VerifyAsync();

        var releaseManifestPath = Path.Combine(
            options.ReleaseRoot,
            AutoCadCandidateReleaseIdentity.ManifestFileName);
        var releaseSha256 = await FileSha256Async(releaseManifestPath);
        var coreVersion = AutoCadCandidateReleaseIdentity
            .ReadValidatedAutoCadProviderVersion(options.CoreConsolePath);
        var release = await AutoCadCandidateReleaseIdentity.LoadVerifiedAsync(
            releaseManifestPath,
            releaseSha256,
            options.ReleaseRoot,
            options.CoreConsolePath,
            coreVersion,
            AutoCadCandidateReleaseIdentity.CurrentRuntimeIdentifier());
        RequireEqual(ProviderKey, release.Manifest.ProviderKey, "Provider key");
        RequireEqual(ExpectedProviderVersion, release.ProviderVersion, "Provider version");

        var exporter = new ReleaseBoundAutoCadDwgExporter(
            new AutoCadCoreConsoleDwgExporter(
                options.CoreConsolePath,
                Path.Combine(options.WorkRoot, "autodesk-runtime-cache"),
                TimeSpan.FromMinutes(3)),
            options.CoreConsolePath,
            release.Manifest.AutoCadCoreConsoleVersion,
            release.Manifest.AutoCadCoreConsoleSha256);
        var worker = new AutoCadCandidateConversionService(
            exporter,
            options.WorkRoot,
            TimeSpan.FromMinutes(3),
            maximumConcurrency: 1,
            release);
        var provider = new FrozenWorkerPreparationProvider(worker);

        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceWp2_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var tenantId = Guid.NewGuid();
        var execution = new AcceptanceExecution(tenantId, Guid.NewGuid());
        var clock = new FixedClock();
        await using var context = CreateContext(connectionString, execution, clock);
        try
        {
            await context.Database.MigrateAsync();
            var database = await ReadDatabaseIdentityAsync(context.Database.GetDbConnection());
            var seeded = await SeedAsync(context, execution, selected);
            var files = new ControlledFileStore(seeded.Sources);
            var access = new AllowAccess();
            var profiles = new StandardSpaceCadMappingProfileCatalog();
            var preparation = new SpaceCadPreparationService(
                context,
                execution,
                access,
                provider,
                profiles,
                files,
                SpaceWorkerSandboxPolicy.FileSafetyDefault,
                clock);
            var design = new SpaceDesignV1Service(
                context,
                execution,
                clock,
                new UnusedCursorCodec(),
                access,
                new SpaceVersionCloneCoordinator(
                    execution,
                    new EfSpaceVersionCloneStore(context, execution, clock)),
                new SpaceSourceCoordinator(execution));
            var parse = new SpaceCadParseService(
                context,
                execution,
                access,
                null!,
                null!,
                clock,
                files,
                design);

            var profile = AssertSingle(
                await preparation.ListProfilesAsync(seeded.Version.Id),
                "CAD mapping profile");
            var results = new List<SampleResult>();
            foreach (var source in seeded.Sources)
            {
                var before = await context.Versions.AsNoTracking()
                    .SingleAsync(item => item.Id == seeded.Version.Id);
                var status = await preparation.GetStatusAsync(
                    seeded.Version.Id,
                    source.Source.Id);
                if (!status.ReadyForPreparation)
                    throw new InvalidDataException("The clean CAD source was not ready.");

                var request = new PreviewSpaceCadPreparationRequest(
                    seeded.Floor.LogicalId,
                    SpaceCadUnit.Millimeter,
                    new SpaceCadPointV1(0, 0),
                    new SpaceCadMillimeterPointV1(0, 0),
                    0,
                    profile.ProfileId,
                    profile.Version,
                    []);
                var preview = await preparation.PreviewAsync(
                    seeded.Version.Id,
                    source.Source.Id,
                    request);
                if (!preview.ReadyForParsing ||
                    preview.PreparationId is null ||
                    preview.StartRequest is null ||
                    preview.MappingPreview is null ||
                    preview.SemanticPreview is null)
                {
                    throw new InvalidDataException(
                        $"{source.Sample.SampleId} did not produce a sealed parse request.");
                }

                var afterPreview = await context.Versions.AsNoTracking()
                    .SingleAsync(item => item.Id == seeded.Version.Id);
                var draftUnchanged =
                    before.ContentRevision == afterPreview.ContentRevision &&
                    before.ContentHash == afterPreview.ContentHash;
                if (!draftUnchanged)
                    throw new InvalidDataException("CAD preview changed the Draft.");

                var sealedRow = await context.CadParsePreparations.AsNoTracking()
                    .SingleAsync(item => item.Id == preview.PreparationId.Value);
                RequireEqual(
                    source.Sample.SourceSha256,
                    sealedRow.SourceSha256,
                    "sealed source SHA-256");
                RequireEqual(ProviderKey, sealedRow.ProviderKey, "sealed Provider key");
                RequireEqual(
                    ExpectedProviderVersion,
                    sealedRow.ProviderVersion,
                    "sealed Provider version");

                var started = await parse.StartAsync(
                    seeded.Version.Id,
                    source.Source.Id,
                    preview.StartRequest,
                    $"wp2-{source.Sample.SampleId.ToLowerInvariant()}-start");
                var replay = await parse.StartAsync(
                    seeded.Version.Id,
                    source.Source.Id,
                    preview.StartRequest,
                    $"wp2-{source.Sample.SampleId.ToLowerInvariant()}-start");
                if (!replay.IdempotentReplay || replay.JobId != started.JobId)
                    throw new InvalidDataException("Parse start did not replay idempotently.");

                var job = await context.Jobs.AsNoTracking()
                    .SingleAsync(item => item.Id == started.JobId);
                var payload = JsonSerializer.Deserialize<SpaceCadParseJobPayload>(
                                  job.PayloadJson,
                                  new JsonSerializerOptions(JsonSerializerDefaults.Web))
                              ?? throw new InvalidDataException(
                                  "The parse job payload was empty.");
                RequireEqual(ProviderKey, payload.PreferredProviderKey, "job Provider key");
                RequireEqual(
                    ExpectedProviderVersion,
                    payload.PreferredProviderVersion,
                    "job Provider version");
                RequireEqual(
                    sealedRow.MappingReplaySnapshotJson,
                    payload.MappingReplaySnapshotJson,
                    "mapping replay snapshot");

                var conversion = provider.Get(source.Source.Id);
                results.Add(new SampleResult(
                    source.Sample.SampleRef,
                    source.Sample.SampleId,
                    source.Sample.SourceFormat,
                    source.Sample.SourceSha256,
                    source.Sample.SourceSizeBytes,
                    source.Sample.AuthorizationSha256,
                    source.Sample.DeidentificationSha256,
                    conversion.PackageSha256,
                    new WizardSelection(
                        seeded.Floor.LogicalId,
                        seeded.Floor.FloorCode,
                        "Millimeter",
                        new CoordinateSelection(0, 0, 0, 0, 0),
                        profile.ProfileId,
                        profile.Version,
                        profile.DefinitionSha256),
                    new SealedAudit(
                        preview.PreparationId.Value,
                        preview.BaseContentRevision,
                        preview.BaseContentHash,
                        preview.CoordinateMetadata.TransformSha256,
                        preview.MappingPreview.PreviewSha256,
                        preview.SemanticPreview.SemanticPreviewSha256,
                        started.JobId,
                        replay.IdempotentReplay,
                        draftUnchanged,
                        sealedRow.ReadyForParsing,
                        sealedRow.ExpiresAtUtc)));
            }

            var first = results[0];
            var firstSource = seeded.Sources[0];
            var jobsBeforeTamper = await context.Jobs.CountAsync();
            var firstPreparation = await context.CadParsePreparations.AsNoTracking()
                .SingleAsync(item => item.Id == first.Audit.PreparationId);
            var tampered = new StartSpaceCadParseRequest(
                firstPreparation.Id,
                firstPreparation.FloorLogicalId,
                Enum.Parse<SpaceCadUnit>(firstPreparation.ConfirmedUnit),
                firstPreparation.ConfirmedScaleToMillimeters,
                firstPreparation.CoordinateMetadataJson,
                firstPreparation.CoordinateTransformSha256,
                firstPreparation.MappingProfileId,
                firstPreparation.MappingProfileVersion,
                firstPreparation.MappingDefinitionSha256,
                new string('f', 64));
            string rejectionCode;
            try
            {
                _ = await parse.StartAsync(
                    seeded.Version.Id,
                    firstSource.Source.Id,
                    tampered,
                    "wp2-tampered-start");
                throw new InvalidDataException("A tampered parse request was accepted.");
            }
            catch (SpaceProblemException problem)
            {
                rejectionCode = problem.Code;
                RequireEqual(
                    SpaceErrorCodes.CadPreparationInvalid,
                    problem.Code,
                    "tamper rejection code");
            }
            var jobsAfterTamper = await context.Jobs.CountAsync();
            if (jobsBeforeTamper != jobsAfterTamper)
                throw new InvalidDataException("Tamper rejection created a parse job.");

            return new AcceptanceReport(
                1,
                "CP6_SPACE_STUDIO_V1_CORE_GA",
                "SoloDeveloper",
                "WP2_CAD_START_CONTROLLED_EXECUTION",
                "Pass",
                options.ApplicationCommitSha,
                DateTime.UtcNow,
                SourceSetSha256,
                GoldenDatasetSha256,
                new AcceptanceEnvironment(
                    "ControlledAcceptance",
                    "SQLServer",
                    database.ProductVersion,
                    database.Edition,
                    false,
                    false),
                new ProviderIdentity(
                    release.Manifest.ProviderKey,
                    release.ProviderVersion,
                    release.WorkerReleaseSha256,
                    release.Manifest.SourceCommit,
                    release.Manifest.AutoCadCoreConsoleVersion,
                    release.Manifest.AutoCadCoreConsoleSha256,
                    release.Manifest.ManagedDxfConverterVersion),
                results.ToArray(),
                new TamperResult(
                    true,
                    rejectionCode,
                    jobsBeforeTamper,
                    jobsAfterTamper),
                new AcceptanceBoundaries(
                    false,
                    false,
                    false,
                    false));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static ControlledSample LoadSample(
        JsonElement dataset,
        string datasetRoot,
        string sampleId,
        string expectedFormat)
    {
        var sample = dataset.GetProperty("samples")
            .EnumerateArray()
            .Single(item => item.GetProperty("sampleId").GetString() == sampleId);
        var sourceFormat = sample.GetProperty("sourceFormat").GetString()
                           ?? throw new InvalidDataException("Source format is missing.");
        RequireEqual(expectedFormat, sourceFormat, "source format");
        RequireEqual(
            "ApprovedOriginalWork",
            sample.GetProperty("license").GetString(),
            "CAD authorization class");
        RequireEqual(
            "Millimeter",
            sample.GetProperty("unit").GetString(),
            "CAD unit");
        RequireEqual(
            "FloorLocal-ZUp",
            sample.GetProperty("coordinateSystem").GetString(),
            "CAD coordinate system");
        var sourcePath = Path.Combine(
            datasetRoot,
            "samples",
            sampleId,
            sourceFormat == "DWG" ? "source.dwg" : "source.dxf");
        return new ControlledSample(
            sample.GetProperty("sampleRef").GetString()!,
            sampleId,
            sourceFormat,
            sample.GetProperty("sourceSha256").GetString()!,
            sample.GetProperty("sourceSizeBytes").GetInt64(),
            sample.GetProperty("authorizationEvidence").GetProperty("sha256")
                .GetString()!,
            sample.GetProperty("deidentificationEvidence").GetProperty("sha256")
                .GetString()!,
            sourcePath);
    }

    private static async Task<SeedResult> SeedAsync(
        SpaceContext context,
        AcceptanceExecution execution,
        IReadOnlyList<ControlledSample> samples)
    {
        var model = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            1,
            "Published baseline");
        context.AddRange(model, published);
        await context.SaveChangesAsync();
        published.BeginValidation();
        published.MarkReady(new string('a', 64), "space-v1", new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, AcceptanceClock);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        await context.SaveChangesAsync();

        var version = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            2,
            "WP2 controlled CAD Start acceptance",
            published.Id);
        context.Add(version);
        await context.SaveChangesAsync();
        model.ReserveDraft(version);
        var floor = SpaceFloorRevision.Create(
            execution.TenantId,
            version.Id,
            Guid.NewGuid(),
            model.SiteId,
            1,
            "F01",
            "Controlled Floor 1",
            0,
            8_000);
        floor.ConfigureBoundary(
            "[[-500000,-500000],[500000,-500000],[500000,500000],[-500000,500000]]",
            SpaceCadCoordinateVersions.TargetCoordinateSystem);
        context.Add(floor);

        var sources = new List<SeededSource>();
        foreach (var sample in samples)
        {
            var fileId = Guid.NewGuid();
            var storageKey = $"{execution.TenantId:N}/{fileId:N}/source.content";
            var isDwg = sample.SourceFormat == "DWG";
            var file = SpaceFile.CreateUploading(
                fileId,
                execution.TenantId,
                storageKey,
                isDwg ? "source.dwg" : "source.dxf",
                isDwg ? "application/acad" : "application/vnd.autocad.dxf",
                SpaceFileRetentionClass.Source);
            file.CompleteQuarantine(
                isDwg ? "application/acad" : "application/vnd.autocad.dxf",
                isDwg ? ".dwg" : ".dxf",
                sample.SourceSizeBytes,
                sample.SourceSha256);
            file.BeginScanning();
            var source = SpaceModelSource.CreatePendingFileSource(
                execution.TenantId,
                version.Id,
                isDwg ? SpaceSourceType.Dwg : SpaceSourceType.Dxf,
                file,
                isDwg ? "source.dwg" : "source.dxf");
            context.AddRange(file, source);
            sources.Add(new SeededSource(sample, file, source, storageKey));
        }
        await context.SaveChangesAsync();
        foreach (var source in sources)
            source.File.MarkClean("controlled-acceptance", "v1");
        await context.SaveChangesAsync();
        return new SeedResult(version, floor, sources.ToArray());
    }

    private static SpaceContext CreateContext(
        string connectionString,
        ISpaceExecutionContext execution,
        ISpaceClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            clock);

    private static async Task<DatabaseIdentity> ReadDatabaseIdentityAsync(
        DbConnection connection)
    {
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')), " +
                "CONVERT(nvarchar(128), SERVERPROPERTY('Edition'))";
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidDataException("SQL Server identity was unavailable.");
            return new DatabaseIdentity(reader.GetString(0), reader.GetString(1));
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values, string label) =>
        values.Count == 1
            ? values[0]
            : throw new InvalidDataException($"Expected one {label}.");

    private static void RequireEqual(string expected, string? actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidDataException($"The {label} did not match its frozen value.");
    }

    private static async Task<string> FileSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Options(
        string DatasetRoot,
        string ReleaseRoot,
        string CoreConsolePath,
        string WorkRoot,
        string ApplicationCommitSha,
        string OutputPath)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                    throw Usage();
                if (!values.TryAdd(args[index], args[index + 1]))
                    throw Usage();
            }
            var result = new Options(
                Require(values, "--dataset-root"),
                Require(values, "--release-root"),
                Require(values, "--accoreconsole"),
                Require(values, "--work-root"),
                Require(values, "--application-commit"),
                Require(values, "--output"));
            if (!Regex.IsMatch(result.ApplicationCommitSha, "^[a-f0-9]{40}$"))
                throw new ArgumentException("Application commit must be a full lowercase Git SHA.");
            return result with
            {
                DatasetRoot = RequireDirectory(result.DatasetRoot),
                ReleaseRoot = RequireDirectory(result.ReleaseRoot),
                CoreConsolePath = RequireFile(result.CoreConsolePath),
                WorkRoot = Path.GetFullPath(result.WorkRoot),
            };
        }

        private static string Require(
            IReadOnlyDictionary<string, string> values,
            string name) =>
            values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw Usage();

        private static string RequireDirectory(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException(fullPath);
            return fullPath;
        }

        private static string RequireFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Required file was not found.", fullPath);
            return fullPath;
        }

        private static ArgumentException Usage() => new(
            "Usage: --dataset-root <path> --release-root <path> " +
            "--accoreconsole <path> --work-root <path> " +
            "--application-commit <sha> --output <path>");
    }

    private sealed class FrozenWorkerPreparationProvider(
        AutoCadCandidateConversionService worker) : ISpaceCadPreparationProvider
    {
        private readonly Dictionary<Guid, ConversionResult> _results = [];

        public async Task<SpaceCadIrPackageV1> InspectAsync(
            SpaceCadPreparationProviderRequest request,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            var workerRequest = new SpaceCadWorkerConversionRequestV2(
                SpaceCadWorkerProtocolVersions.SchemaVersion,
                Guid.NewGuid(),
                request.SourceSha256,
                request.SourceFormat,
                worker.ProviderKey,
                worker.ProviderVersion,
                worker.WorkerReleaseSha256);
            var response = await worker.ConvertAsync(
                workerRequest,
                source,
                cancellationToken);
            SpaceCadWorkerProtocol.ValidateResponse(workerRequest, response);
            _results.Add(
                request.SourceId,
                new ConversionResult(response.PackageSha256));
            return response.Package;
        }

        public ConversionResult Get(Guid sourceId) =>
            _results.TryGetValue(sourceId, out var result)
                ? result
                : throw new InvalidDataException("Provider conversion evidence was missing.");
    }

    private sealed class ControlledFileStore(IReadOnlyList<SeededSource> sources) :
        ISpaceFileStore
    {
        private readonly IReadOnlyDictionary<string, SeededSource> _sources =
            sources.ToDictionary(item => item.StorageKey, StringComparer.Ordinal);

        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            if (!_sources.TryGetValue(storageKey, out var source) ||
                source.File.Id != fileId ||
                source.File.TenantId != tenantId)
            {
                throw new InvalidDataException("The controlled CAD storage identity is invalid.");
            }
            Stream stream = new FileStream(
                source.Sample.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record AcceptanceExecution(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext
    {
        public bool IsExternal => false;
        public string? ExternalSubjectType => null;
        public Guid? ExternalOrganizationId => null;
        public string ActorDisplayName => "BUBAO.GAO";
    }

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => AcceptanceClock;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class UnusedCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) => throw new NotSupportedException();

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) => throw new NotSupportedException();
    }

    private sealed record ControlledSample(
        string SampleRef,
        string SampleId,
        string SourceFormat,
        string SourceSha256,
        long SourceSizeBytes,
        string AuthorizationSha256,
        string DeidentificationSha256,
        string SourcePath)
    {
        public async Task VerifyAsync()
        {
            var info = new FileInfo(SourcePath);
            if (!info.Exists || info.Length != SourceSizeBytes)
                throw new InvalidDataException($"{SampleId} source size is invalid.");
            RequireEqual(SourceSha256, await FileSha256Async(SourcePath), "source SHA-256");
            var sampleRoot = Path.GetDirectoryName(SourcePath)!;
            RequireEqual(
                AuthorizationSha256,
                await FileSha256Async(Path.Combine(sampleRoot, "authorization.json")),
                "authorization evidence SHA-256");
            RequireEqual(
                DeidentificationSha256,
                await FileSha256Async(Path.Combine(sampleRoot, "deidentification.json")),
                "deidentification evidence SHA-256");
        }
    }

    private sealed record SeededSource(
        ControlledSample Sample,
        SpaceFile File,
        SpaceModelSource Source,
        string StorageKey);

    private sealed record SeedResult(
        SpaceModelVersion Version,
        SpaceFloorRevision Floor,
        SeededSource[] Sources);

    private sealed record ConversionResult(string PackageSha256);
    private sealed record DatabaseIdentity(string ProductVersion, string Edition);

    private sealed record AcceptanceReport(
        int SchemaVersion,
        string ProgramId,
        string DeliveryMode,
        string EvidenceClass,
        string Conclusion,
        string ApplicationCommitSha,
        DateTime ExecutedAtUtc,
        string SourceSetSha256,
        string GoldenDatasetSha256,
        AcceptanceEnvironment Environment,
        ProviderIdentity Provider,
        SampleResult[] Samples,
        TamperResult TamperTest,
        AcceptanceBoundaries Boundaries);

    private sealed record AcceptanceEnvironment(
        string Mode,
        string DatabaseEngine,
        string ProductVersion,
        string Edition,
        bool ProductionDeploymentPerformed,
        bool ProductionDataClaimed);

    private sealed record ProviderIdentity(
        string ProviderKey,
        string ProviderVersion,
        string WorkerReleaseSha256,
        string SourceCommit,
        string AutoCadCoreConsoleVersion,
        string AutoCadCoreConsoleSha256,
        string ManagedDxfConverterVersion);

    private sealed record SampleResult(
        string SampleRef,
        string SampleId,
        string SourceFormat,
        string SourceSha256,
        long SourceSizeBytes,
        string AuthorizationSha256,
        string DeidentificationSha256,
        string ProviderPackageSha256,
        WizardSelection Selection,
        SealedAudit Audit);

    private sealed record WizardSelection(
        Guid FloorLogicalId,
        string FloorCode,
        string ConfirmedUnit,
        CoordinateSelection Transform,
        Guid MappingProfileId,
        int MappingProfileVersion,
        string MappingDefinitionSha256);

    private sealed record CoordinateSelection(
        decimal SourceOriginX,
        decimal SourceOriginY,
        decimal FloorOriginMillimetersX,
        decimal FloorOriginMillimetersY,
        decimal RotationZDegrees);

    private sealed record SealedAudit(
        Guid PreparationId,
        long BaseContentRevision,
        string? BaseContentHash,
        string CoordinateTransformSha256,
        string MappingPreviewSha256,
        string SemanticPreviewSha256,
        Guid JobId,
        bool IdempotentReplay,
        bool DraftUnchangedDuringPreview,
        bool ReadyForParsing,
        DateTime ExpiresAtUtc);

    private sealed record TamperResult(
        bool Rejected,
        string ErrorCode,
        int JobsBefore,
        int JobsAfter);

    private sealed record AcceptanceBoundaries(
        bool RawCadStoredInRepository,
        bool ProductionDataClaimed,
        bool ProductionDeploymentPerformed,
        bool ProductionWmsClaimed);
}
