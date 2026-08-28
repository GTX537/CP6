using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceCadRemoteWorkerProviderTests
{
    private const string ProviderKey = "approved.worker";
    private const string ProviderVersion = "25.0.58.0.0";
    private static readonly string WorkerReleaseSha256 = new('c', 64);

    [Fact]
    public async Task Preparation_sends_only_minimized_CAD_identity()
    {
        var fixture = Fixture.Create();
        var worker = new RecordingWorker(fixture.Package);
        var provider = new SpaceCadRemoteWorkerProvider(
            Options(),
            worker,
            new ProfileCatalog(fixture.Profile));
        await using var source = new MemoryStream(fixture.SourceBytes, writable: false);

        var result = await provider.InspectAsync(
            new SpaceCadPreparationProviderRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                fixture.SourceSha256,
                SpaceCadSourceFormat.Dxf,
                SpaceWorkerSandboxPolicy.FileSafetyDefault),
            source);

        Assert.Same(fixture.Package, result);
        var request = Assert.Single(worker.Requests);
        Assert.Equal(SpaceCadWorkerProtocolVersions.SchemaVersion, request.SchemaVersion);
        Assert.NotEqual(Guid.Empty, request.AttemptId);
        Assert.Equal(fixture.SourceSha256, request.SourceSha256);
        Assert.Equal(SpaceCadSourceFormat.Dxf, request.SourceFormat);
        Assert.Equal(ProviderKey, request.ProviderKey);
        Assert.Equal(ProviderVersion, request.ProviderVersion);
        Assert.Equal(WorkerReleaseSha256, request.WorkerReleaseSha256);
        Assert.DoesNotContain(
            "tenant",
            JsonSerializer.Serialize(request),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "site",
            JsonSerializer.Serialize(request),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Parse_replays_sealed_mapping_inside_CP6_and_generates_required_artifacts()
    {
        var fixture = Fixture.Create();
        var worker = new RecordingWorker(fixture.Package);
        var provider = new SpaceCadRemoteWorkerProvider(
            Options(),
            worker,
            new ProfileCatalog(fixture.Profile));
        await using var source = new MemoryStream(fixture.SourceBytes, writable: false);

        var artifacts = await provider.GenerateAsync(
            new SpaceCadParseProviderRequest(
                fixture.TenantId,
                fixture.JobId,
                fixture.Payload),
            source);

        Assert.Equal(
            new[]
            {
                SpaceArtifactType.CadIr,
                SpaceArtifactType.LayerInventory,
                SpaceArtifactType.PreviewSet,
            },
            artifacts.Select(item => item.ArtifactType).ToArray());
        foreach (var artifact in artifacts)
        {
            await using var stream = await artifact.OpenReadAsync(CancellationToken.None);
            using var output = new MemoryStream();
            await stream.CopyToAsync(output);
            Assert.Equal(artifact.SizeBytes, output.Length);
            Assert.Equal(artifact.Sha256, Sha256(output.ToArray()));
        }
        var previewArtifact = artifacts.Single(
            item => item.ArtifactType == SpaceArtifactType.PreviewSet);
        await using var previewStream = await previewArtifact.OpenReadAsync(
            CancellationToken.None);
        using var reader = new StreamReader(previewStream, Encoding.UTF8);
        var preview = SpaceCadPreviewSet.Deserialize(await reader.ReadToEndAsync());
        Assert.Equal(fixture.TenantId, preview.TenantId);
        Assert.Equal(fixture.JobId, preview.CadParseJobId);
        Assert.Equal(fixture.SemanticSha256, preview.SemanticPreview.SemanticPreviewSha256);
        Assert.Single(worker.Requests);
    }

    [Fact]
    public async Task Http_client_validates_source_and_response_identity()
    {
        var fixture = Fixture.Create();
        var attemptId = Guid.NewGuid();
        var request = new SpaceCadWorkerConversionRequestV2(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            attemptId,
            fixture.SourceSha256,
            SpaceCadSourceFormat.Dxf,
            ProviderKey,
            ProviderVersion,
            WorkerReleaseSha256);
        var response = new SpaceCadWorkerConversionResponseV2(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            attemptId,
            fixture.SourceSha256,
            SpaceCadSourceFormat.Dxf,
            ProviderKey,
            ProviderVersion,
            WorkerReleaseSha256,
            SpaceCadWorkerProtocol.ComputePackageSha256(fixture.Package),
            fixture.Package);
        var handler = new WorkerHandler(response, fixture.SourceBytes);
        using var http = new HttpClient(handler);
        using var client = new HttpSpaceCadRemoteWorkerClient(http, Options());
        await using var source = new MemoryStream(fixture.SourceBytes, writable: false);

        var package = await client.ConvertAsync(request, source);

        Assert.Equal(fixture.Package.Document, package.Document);
        Assert.Equal(
            SpaceCadWorkerProtocolVersions.SchemaVersion.ToString(),
            handler.Headers["X-CP6-Cad-Schema"]);
        Assert.Equal(attemptId.ToString("D"), handler.Headers["X-CP6-Cad-Attempt"]);
        Assert.Equal(ProviderKey, handler.Headers["X-CP6-Cad-Provider-Key"]);
        Assert.Equal(ProviderVersion, handler.Headers["X-CP6-Cad-Provider-Version"]);
        Assert.Equal(
            WorkerReleaseSha256,
            handler.Headers["X-CP6-Cad-Worker-Release-Sha256"]);
        Assert.Equal(fixture.SourceBytes, handler.Body);
        Assert.Equal(
            new Uri("https://worker.internal/v1/conversions"),
            handler.RequestUri);
    }

    [Fact]
    public async Task Http_client_rejects_a_response_from_a_different_Worker_release()
    {
        var fixture = Fixture.Create();
        var attemptId = Guid.NewGuid();
        var request = new SpaceCadWorkerConversionRequestV2(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            attemptId,
            fixture.SourceSha256,
            SpaceCadSourceFormat.Dxf,
            ProviderKey,
            ProviderVersion,
            WorkerReleaseSha256);
        var response = new SpaceCadWorkerConversionResponseV2(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            attemptId,
            fixture.SourceSha256,
            SpaceCadSourceFormat.Dxf,
            ProviderKey,
            ProviderVersion,
            new string('f', 64),
            SpaceCadWorkerProtocol.ComputePackageSha256(fixture.Package),
            fixture.Package);
        var handler = new WorkerHandler(response, fixture.SourceBytes);
        using var http = new HttpClient(handler);
        using var client = new HttpSpaceCadRemoteWorkerClient(http, Options());
        await using var source = new MemoryStream(fixture.SourceBytes, writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ConvertAsync(request, source));
    }

    [Fact]
    public async Task Http_client_rejects_a_request_not_bound_to_the_approved_release()
    {
        var fixture = Fixture.Create();
        var request = new SpaceCadWorkerConversionRequestV2(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            Guid.NewGuid(),
            fixture.SourceSha256,
            SpaceCadSourceFormat.Dxf,
            ProviderKey,
            ProviderVersion,
            new string('f', 64));
        var handler = new WorkerHandler(
            new SpaceCadWorkerConversionResponseV2(
                SpaceCadWorkerProtocolVersions.SchemaVersion,
                request.AttemptId,
                fixture.SourceSha256,
                SpaceCadSourceFormat.Dxf,
                ProviderKey,
                ProviderVersion,
                request.WorkerReleaseSha256,
                SpaceCadWorkerProtocol.ComputePackageSha256(fixture.Package),
                fixture.Package),
            fixture.SourceBytes);
        using var http = new HttpClient(handler);
        using var client = new HttpSpaceCadRemoteWorkerClient(http, Options());
        await using var source = new MemoryStream(fixture.SourceBytes, writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ConvertAsync(request, source));
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public void Approval_manifest_must_bind_runtime_and_frozen_qualification()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"cp6-cad-worker-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "approval.json");
            var now = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
            var manifest = Manifest(now, businessCredentialsUnavailable: true);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllBytes(path, bytes);
            var options = Options();
            options.ApprovalManifestPath = path;
            options.ApprovalManifestSha256 = Sha256(bytes);
            options.ServerCertificateSha256 = new string('a', 64);
            options.ClientCertificateThumbprint = new string('B', 40);

            var loaded = options.LoadApprovalManifest(now);

            Assert.Equal(ProviderKey, loaded.ProviderKey);
            Assert.Equal(80, loaded.QualificationScore);
            Assert.Equal(WorkerReleaseSha256, options.WorkerReleaseSha256);

            var unsafeBytes = JsonSerializer.SerializeToUtf8Bytes(
                Manifest(now, businessCredentialsUnavailable: false),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllBytes(path, unsafeBytes);
            options.ApprovalManifestSha256 = Sha256(unsafeBytes);
            Assert.Throws<InvalidOperationException>(() =>
                options.LoadApprovalManifest(now));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SpaceCadRemoteWorkerOptions Options() => new()
    {
        Enabled = true,
        ProviderKey = ProviderKey,
        ProviderVersion = ProviderVersion,
        DisplayName = "Approved Worker",
        DeploymentMode = SpaceCadProviderDeploymentMode.OnPremisesIsolatedWorker,
        DataBoundary = SpaceCadProviderDataBoundary.SiteLocal,
        SupportsDwg = true,
        SupportsDxf = true,
        Endpoint = "https://worker.internal/",
        ServerCertificateSha256 = new string('a', 64),
        ClientCertificateThumbprint = new string('B', 40),
        MaximumSourceBytes = 1024 * 1024,
        MaximumResponseBytes = 1024 * 1024,
        TimeoutSeconds = 30,
        WorkerReleaseSha256 = WorkerReleaseSha256,
    };

    private static SpaceCadRemoteWorkerApprovalManifestV1 Manifest(
        DateTime now,
        bool businessCredentialsUnavailable) =>
        new(
            1,
            ProviderKey,
            ProviderVersion,
            "Approved Worker",
            SpaceCadProviderDeploymentMode.OnPremisesIsolatedWorker.ToString(),
            SpaceCadProviderDataBoundary.SiteLocal.ToString(),
            SupportsDwg: true,
            SupportsDxf: true,
            "https://worker.internal/",
            new string('a', 64),
            new string('B', 40),
            "vault://cad-worker/client-certificate",
            WorkerReleaseSha256,
            "identity://cad-worker/service-account",
            MutuallyAuthenticatedTls: true,
            OutboundNetworkDisabled: true,
            businessCredentialsUnavailable,
            RawCadDeletedOnCompletion: true,
            ArtifactOnlyResponse: true,
            SourceHashVerifiedBeforeConversion: true,
            ConverterContractRunnerEnforced: true,
            "evidence://licensing/approved",
            "evidence://security/approved",
            "evidence://data-region/approved",
            "evidence://retention/approved",
            80,
            "cad-provider-adr-0001-v2",
            new string('d', 64),
            new string('e', 64),
            "evidence://qualification/report",
            now.AddHours(-1),
            now.AddDays(30));

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class RecordingWorker(SpaceCadIrPackageV1 package) :
        ISpaceCadRemoteWorkerClient
    {
        public List<SpaceCadWorkerConversionRequestV2> Requests { get; } = [];

        public Task<SpaceCadIrPackageV1> ConvertAsync(
            SpaceCadWorkerConversionRequestV2 request,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(package);
        }
    }

    private sealed class ProfileCatalog(SpaceCadMappingProfileV1 profile) :
        ISpaceCadMappingProfileCatalog
    {
        public Task<IReadOnlyList<SpaceCadMappingProfileV1>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpaceCadMappingProfileV1>>([profile]);

        public Task<SpaceCadMappingProfileV1?> FindAsync(
            Guid profileId,
            int version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SpaceCadMappingProfileV1?>(
                profile.ProfileId == profileId && profile.Version == version
                    ? profile
                    : null);
    }

    private sealed class WorkerHandler(
        SpaceCadWorkerConversionResponseV2 response,
        byte[] expectedBody) : HttpMessageHandler
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.Ordinal);
        public byte[] Body { get; private set; } = [];
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            foreach (var header in request.Headers)
                Headers[header.Key] = Assert.Single(header.Value);
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal(expectedBody, Body);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                response,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers =
                    {
                        ContentType = new(
                            "application/vnd.cp6.space.cad-worker-response+json"),
                    },
                },
            };
        }
    }

    private sealed record Fixture(
        Guid TenantId,
        Guid JobId,
        byte[] SourceBytes,
        string SourceSha256,
        SpaceCadIrPackageV1 Package,
        SpaceCadMappingProfileV1 Profile,
        SpaceCadParseJobPayload Payload,
        string SemanticSha256)
    {
        public static Fixture Create()
        {
            var tenantId = Guid.NewGuid();
            var modelVersionId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var floorId = Guid.NewGuid();
            var sourceBytes = Encoding.UTF8.GetBytes("controlled-dxf-source");
            var sourceSha256 = Sha256(sourceBytes);
            var bounds = new SpaceCadBoundsV1(0, 0, 10_000, 5_000);
            var package = new SpaceCadIrPackageV1(
                new SpaceCadIrDocumentV1(
                    1,
                    sourceSha256,
                    SpaceCadSourceFormat.Dxf,
                    "AC1032",
                    SpaceCadUnit.Millimeter,
                    1,
                    SpaceCadIrVersions.CoordinateSystem,
                    bounds,
                    ProviderKey,
                    ProviderVersion),
                [new SpaceCadIrLayerV1("ZONE", "ZONE", 1)],
                [],
                [new SpaceCadIrEntityV1(
                    "H:1",
                    SpaceCadIrEntityType.ClosedPolyline,
                    "LWPOLYLINE",
                    "ZONE",
                    null,
                    [
                        new(0, 0),
                        new(10_000, 0),
                        new(10_000, 5_000),
                        new(0, 5_000),
                    ],
                    null,
                    null,
                    null,
                    SpaceCadAffineTransformV1.Identity,
                    bounds,
                    IsClosed: true,
                    IsSupported: true,
                    new Dictionary<string, string>())],
                [],
                new SpaceCadIrSummaryV1(1, 0, 1, 1, 0, 0, bounds));
            var conversion = new SpaceCadConversionRequest(
                tenantId,
                fileId,
                sourceId,
                sourceSha256,
                SpaceCadSourceFormat.Dxf,
                ProviderKey,
                ProviderVersion);
            SpaceCadConversionContract.ValidatePackage(conversion, package);
            var confirmation = new SpaceCadCoordinateConfirmationV1(
                sourceSha256,
                UnitConfirmed: true,
                SpaceCadUnit.Millimeter,
                new SpaceCadPointV1(0, 0),
                new SpaceCadMillimeterPointV1(0, 0),
                RotationZDegrees: 0,
                new SpaceCadFloorAssignmentV1(
                    floorId,
                    "F1",
                    1,
                    0,
                    SpaceCadCoordinateVersions.TargetCoordinateSystem,
                    new SpaceCadBoundsV1(-1_000, -1_000, 20_000, 20_000)));
            var preparation = SpaceCadCoordinatePreparation.Prepare(
                conversion,
                package,
                confirmation);
            var inventory = SpaceCadInventory.Build(conversion, preparation);
            var profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
                SpaceCadMappingVersions.SchemaVersion,
                Guid.NewGuid(),
                1,
                "Zone mapping",
                SpaceCadMappingScope.Tenant,
                tenantId,
                IsEnabled: true,
                BasedOnProfileId: null,
                BasedOnVersion: null,
                [new SpaceCadMappingRuleV1(
                    "zone",
                    100,
                    SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Exact,
                    "ZONE",
                    AttributeName: null,
                    AttributeMatchKind: null,
                    AttributePattern: null,
                    SpaceCadSemanticTarget.Zone,
                    TargetSubtype: null,
                    SpaceCadGeometryRule.ClosedBoundary,
                    DefaultHeightMillimeters: null,
                    DefaultThicknessMillimeters: null,
                    ConfidenceWeight: 0.95m,
                    IsRequired: true)]));
            var mapping = SpaceCadMapping.Preview(tenantId, inventory, profile);
            var snapshot = SpaceCadMappingReplaySnapshot.Create(mapping);
            var semantic = SpaceCadSemanticParser.Parse(
                conversion,
                preparation,
                inventory,
                profile,
                mapping);
            var payload = new SpaceCadParseJobPayload(
                SpaceCadParsePayloadVersions.Current,
                modelVersionId,
                sourceId,
                fileId,
                sourceSha256,
                SpaceCadSourceFormat.Dxf,
                floorId,
                SpaceCadUnit.Millimeter,
                1,
                SpaceCadCoordinatePreparation.SerializeMetadata(preparation.Metadata),
                preparation.Metadata.TransformSha256,
                profile.ProfileId,
                profile.Version,
                profile.DefinitionSha256,
                mapping.PreviewSha256,
                BaseContentRevision: 0,
                BaseContentHash: null,
                ProviderKey,
                semantic.SemanticPreviewSha256,
                SpaceCadMappingReplaySnapshot.Serialize(snapshot),
                ProviderVersion);
            return new Fixture(
                tenantId,
                jobId,
                sourceBytes,
                sourceSha256,
                package,
                profile,
                payload,
                semantic.SemanticPreviewSha256);
        }
    }
}
