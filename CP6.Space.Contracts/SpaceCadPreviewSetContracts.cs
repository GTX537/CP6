namespace CP6.Space.Contracts;

public static class SpaceCadPreviewSetVersions
{
    public const int SchemaVersion = 1;
    public const string ArtifactSchema = "space-cad-preview-set-v1";
}

public sealed record SpaceCadPreviewSetV1(
    int SchemaVersion,
    bool IsReadOnlyArtifact,
    Guid TenantId,
    Guid ModelVersionId,
    Guid SourceId,
    Guid CadParseJobId,
    Guid FloorLogicalId,
    string SourceSha256,
    string CoordinateTransformSha256,
    string MappingPreviewSha256,
    SpaceCadSemanticPreviewV1 SemanticPreview,
    SpaceCadSemanticDiagnosticIndexV1 DiagnosticIndex,
    string PreviewSetSha256);
