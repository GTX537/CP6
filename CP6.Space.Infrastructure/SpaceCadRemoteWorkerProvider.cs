using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Infrastructure;

/// <summary>
/// Production adapter for a separately deployed CAD-only conversion Worker.
/// Mapping, semantic parsing, and PreviewSet generation remain inside CP6 and
/// are replayed from the server-sealed preparation snapshot.
/// </summary>
public sealed class SpaceCadRemoteWorkerProvider(
    SpaceCadRemoteWorkerOptions options,
    ISpaceCadRemoteWorkerClient worker,
    ISpaceCadMappingProfileCatalog profiles) :
    ISpaceCadPreparationProvider,
    ISpaceCadParseProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<SpaceCadIrPackageV1> InspectAsync(
        SpaceCadPreparationProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty ||
            request.SiteId == Guid.Empty ||
            request.FileId == Guid.Empty ||
            request.SourceId == Guid.Empty)
        {
            throw new InvalidDataException(
                "The CAD preparation Provider request identity is incomplete.");
        }
        request.Sandbox.Validate();
        var conversion = ConversionRequest(
            request.TenantId,
            request.FileId,
            request.SourceId,
            request.SourceSha256,
            request.SourceFormat);
        var package = await ConvertAsync(conversion, source, cancellationToken);
        SpaceCadConversionContract.ValidatePackage(conversion, package);
        return package;
    }

    public async Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
        SpaceCadParseProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);
        if (request.TenantId == Guid.Empty || request.JobId == Guid.Empty)
            throw new InvalidDataException("The CAD parse Provider identity is incomplete.");
        var payload = request.Payload;
        if (!string.Equals(
                payload.PreferredProviderKey,
                options.ProviderKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                payload.PreferredProviderVersion,
                options.ProviderVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD parse is not sealed to this Provider version.");
        }

        var conversion = ConversionRequest(
            request.TenantId,
            payload.FileId,
            payload.SourceId,
            payload.SourceSha256,
            payload.SourceFormat);
        var package = await ConvertAsync(conversion, source, cancellationToken);
        SpaceCadConversionContract.ValidatePackage(conversion, package);

        var preparation = PrepareCoordinates(conversion, package, payload);
        var inventory = SpaceCadInventory.Build(conversion, preparation);
        var snapshot = SpaceCadMappingReplaySnapshot.Deserialize(
            payload.MappingReplaySnapshotJson ?? throw new InvalidDataException(
                "The CAD parse has no sealed mapping replay snapshot."));
        if (snapshot.TenantId != request.TenantId ||
            snapshot.ProfileId != payload.MappingProfileId ||
            snapshot.ProfileVersion != payload.MappingProfileVersion ||
            !snapshot.ProfileDefinitionSha256.Equals(
                payload.MappingDefinitionSha256,
                StringComparison.Ordinal) ||
            !snapshot.SourceSha256.Equals(payload.SourceSha256, StringComparison.Ordinal) ||
            !snapshot.ExpectedMappingPreviewSha256.Equals(
                payload.MappingPreviewSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD parse mapping snapshot does not match its sealed payload.");
        }
        var profile = await profiles.FindAsync(
                          payload.MappingProfileId,
                          payload.MappingProfileVersion,
                          cancellationToken)
                      ?? throw new InvalidDataException(
                          "The sealed CAD mapping profile version is unavailable.");
        SpaceCadMapping.Validate(profile);
        if (!profile.DefinitionSha256.Equals(
                payload.MappingDefinitionSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The sealed CAD mapping profile hash has changed.");
        }
        var mapping = SpaceCadMapping.Preview(
            request.TenantId,
            inventory,
            profile,
            snapshot.LayerOverrides);
        SpaceCadMappingReplaySnapshot.ValidateReplay(snapshot, mapping);
        var semantic = SpaceCadSemanticParser.Parse(
            conversion,
            preparation,
            inventory,
            profile,
            mapping);
        if (!string.IsNullOrWhiteSpace(payload.ExpectedSemanticPreviewSha256) &&
            !semantic.SemanticPreviewSha256.Equals(
                payload.ExpectedSemanticPreviewSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD semantic replay does not match its sealed preparation.");
        }
        var diagnostics = SpaceCadSemanticDiagnostics.Build(
            conversion,
            preparation,
            inventory,
            profile,
            mapping,
            semantic);
        var previewSet = SpaceCadPreviewSet.Create(
            request.TenantId,
            payload.ModelVersionId,
            payload.SourceId,
            request.JobId,
            semantic,
            diagnostics,
            payload.BaseContentRevision,
            payload.BaseContentHash);

        return
        [
            Artifact(
                SpaceArtifactType.CadIr,
                "cad-ir.json",
                SpaceCadWorkerProtocol.SerializePackage(package)),
            Artifact(
                SpaceArtifactType.LayerInventory,
                "layers.json",
                Encoding.UTF8.GetBytes(SpaceCadInventory.Serialize(inventory))),
            Artifact(
                SpaceArtifactType.PreviewSet,
                "preview.json",
                Encoding.UTF8.GetBytes(SpaceCadPreviewSet.Serialize(previewSet))),
        ];
    }

    private async Task<SpaceCadIrPackageV1> ConvertAsync(
        SpaceCadConversionRequest conversion,
        Stream source,
        CancellationToken cancellationToken)
    {
        var request = new SpaceCadWorkerConversionRequestV1(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            Guid.NewGuid(),
            conversion.SourceSha256,
            conversion.SourceFormat,
            options.ProviderKey,
            options.ProviderVersion);
        return await worker.ConvertAsync(request, source, cancellationToken);
    }

    private SpaceCadConversionRequest ConversionRequest(
        Guid tenantId,
        Guid fileId,
        Guid sourceId,
        string sourceSha256,
        SpaceCadSourceFormat sourceFormat)
    {
        var request = new SpaceCadConversionRequest(
            tenantId,
            fileId,
            sourceId,
            sourceSha256,
            sourceFormat,
            options.ProviderKey,
            options.ProviderVersion);
        SpaceCadConversionContract.ValidateRequest(request);
        if ((sourceFormat == SpaceCadSourceFormat.Dwg && !options.SupportsDwg) ||
            (sourceFormat == SpaceCadSourceFormat.Dxf && !options.SupportsDxf))
        {
            throw new InvalidDataException(
                "This CAD Provider does not support the requested source format.");
        }
        return request;
    }

    private static SpaceCadCoordinatePreparationV1 PrepareCoordinates(
        SpaceCadConversionRequest conversion,
        SpaceCadIrPackageV1 package,
        SpaceCadParseJobPayload payload)
    {
        SpaceCadCoordinateMetadataV1 metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<SpaceCadCoordinateMetadataV1>(
                           payload.CoordinateMetadataJson,
                           JsonOptions)
                       ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The sealed CAD coordinate metadata is invalid.",
                exception);
        }
        if (!SpaceCadCoordinatePreparation.SerializeMetadata(metadata).Equals(
                payload.CoordinateMetadataJson,
                StringComparison.Ordinal) ||
            metadata.SourceSha256 != payload.SourceSha256 ||
            metadata.TargetFloor.FloorLogicalId != payload.FloorLogicalId ||
            metadata.ConfirmedUnit != payload.ConfirmedUnit ||
            metadata.ConfirmedScaleToMillimeters !=
                payload.ConfirmedScaleToMillimeters ||
            metadata.TransformSha256 != payload.CoordinateTransformSha256)
        {
            throw new InvalidDataException(
                "The sealed CAD coordinate metadata does not match its parse payload.");
        }
        var confirmation = new SpaceCadCoordinateConfirmationV1(
            metadata.SourceSha256,
            metadata.UnitConfirmed,
            metadata.ConfirmedUnit,
            metadata.SourceOriginInSourceUnits,
            metadata.FloorOriginMillimeters,
            metadata.RotationZDegrees,
            metadata.TargetFloor);
        var preparation = SpaceCadCoordinatePreparation.Prepare(
            conversion,
            package,
            confirmation);
        if (!preparation.ReadyForParsing ||
            preparation.Metadata.TransformSha256 != metadata.TransformSha256 ||
            preparation.Issues.Any(issue =>
                issue.Severity == SpaceCadIssueSeverity.Blocking))
        {
            throw new InvalidDataException(
                "The isolated CAD conversion is not ready for the sealed coordinate replay.");
        }
        return preparation;
    }

    private static SpaceCadGeneratedArtifact Artifact(
        SpaceArtifactType type,
        string fileName,
        byte[] bytes)
    {
        var immutable = bytes.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(immutable)).ToLowerInvariant();
        return new SpaceCadGeneratedArtifact(
            type,
            "1",
            fileName,
            "application/json",
            ".json",
            immutable.LongLength,
            sha256,
            _ => ValueTask.FromResult<Stream>(
                new MemoryStream(immutable, writable: false)));
    }
}
