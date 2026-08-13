namespace CP6.Space.Contracts;

public static class SpaceCadPreviewSetVersions
{
    public const int SchemaVersion = 2;
    public const string ArtifactSchema = "space-cad-preview-set-v2";
}

public sealed record SpaceCadPreviewSetV2(
    int SchemaVersion,
    bool IsReadOnlyArtifact,
    Guid TenantId,
    Guid ModelVersionId,
    Guid SourceId,
    Guid CadParseJobId,
    Guid FloorLogicalId,
    long BaseContentRevision,
    string? BaseContentHash,
    string SourceSha256,
    string CoordinateTransformSha256,
    string MappingPreviewSha256,
    SpaceCadSemanticPreviewV1 SemanticPreview,
    SpaceCadSemanticDiagnosticIndexV1 DiagnosticIndex,
    string PreviewSetSha256);
