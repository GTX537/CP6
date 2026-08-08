using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadPreviewSet
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static SpaceCadPreviewSetV1 Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid sourceId,
        Guid cadParseJobId,
        SpaceCadSemanticPreviewV1 semanticPreview,
        SpaceCadSemanticDiagnosticIndexV1 diagnosticIndex)
    {
        ArgumentNullException.ThrowIfNull(semanticPreview);
        ArgumentNullException.ThrowIfNull(diagnosticIndex);
        var withoutHash = new SpaceCadPreviewSetV1(
            SpaceCadPreviewSetVersions.SchemaVersion,
            IsReadOnlyArtifact: true,
            tenantId,
            modelVersionId,
            sourceId,
            cadParseJobId,
            semanticPreview.FloorLogicalId,
            semanticPreview.SourceSha256,
            semanticPreview.CoordinateTransformSha256,
            semanticPreview.MappingPreviewSha256,
            semanticPreview,
            diagnosticIndex,
            PreviewSetSha256: string.Empty);
        var result = withoutHash with
        {
            PreviewSetSha256 = Hash(SerializeUnchecked(withoutHash)),
        };
        Validate(result);
        return result;
    }

    public static string Serialize(SpaceCadPreviewSetV1 previewSet)
    {
        Validate(previewSet);
        return SerializeUnchecked(previewSet);
    }

    public static SpaceCadPreviewSetV1 Deserialize(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<SpaceCadPreviewSetV1>(
                json,
                JsonOptions) ?? throw new JsonException();
            Validate(value);
            return value;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The CAD PreviewSet artifact is not valid JSON.",
                exception);
        }
    }

    public static void Validate(SpaceCadPreviewSetV1 previewSet)
    {
        ArgumentNullException.ThrowIfNull(previewSet);
        ArgumentNullException.ThrowIfNull(previewSet.SemanticPreview);
        ArgumentNullException.ThrowIfNull(previewSet.DiagnosticIndex);
        SpaceCadSemanticParser.Validate(previewSet.SemanticPreview);
        SpaceCadSemanticDiagnostics.Validate(previewSet.DiagnosticIndex);
        if (previewSet.SchemaVersion != SpaceCadPreviewSetVersions.SchemaVersion ||
            !previewSet.IsReadOnlyArtifact ||
            previewSet.TenantId == Guid.Empty ||
            previewSet.ModelVersionId == Guid.Empty ||
            previewSet.SourceId == Guid.Empty ||
            previewSet.CadParseJobId == Guid.Empty ||
            previewSet.FloorLogicalId == Guid.Empty ||
            !IsSha256(previewSet.SourceSha256) ||
            !IsSha256(previewSet.CoordinateTransformSha256) ||
            !IsSha256(previewSet.MappingPreviewSha256) ||
            !IsSha256(previewSet.PreviewSetSha256) ||
            previewSet.SemanticPreview.TenantId != previewSet.TenantId ||
            previewSet.SemanticPreview.FloorLogicalId != previewSet.FloorLogicalId ||
            !previewSet.SemanticPreview.SourceSha256.Equals(
                previewSet.SourceSha256,
                StringComparison.Ordinal) ||
            !previewSet.SemanticPreview.CoordinateTransformSha256.Equals(
                previewSet.CoordinateTransformSha256,
                StringComparison.Ordinal) ||
            !previewSet.SemanticPreview.MappingPreviewSha256.Equals(
                previewSet.MappingPreviewSha256,
                StringComparison.Ordinal) ||
            previewSet.DiagnosticIndex.TenantId != previewSet.TenantId ||
            previewSet.DiagnosticIndex.FloorLogicalId != previewSet.FloorLogicalId ||
            !previewSet.DiagnosticIndex.SemanticPreviewSha256.Equals(
                previewSet.SemanticPreview.SemanticPreviewSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD PreviewSet artifact identity is invalid.");
        }

        var expected = Hash(SerializeUnchecked(
            previewSet with { PreviewSetSha256 = string.Empty }));
        if (!previewSet.PreviewSetSha256.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD PreviewSet artifact hash is invalid.");
        }
    }

    private static string SerializeUnchecked(SpaceCadPreviewSetV1 value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => Uri.IsHexDigit(character) && !char.IsUpper(character));
}
