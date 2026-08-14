namespace CP6.Space.Domain;

public sealed class SpaceCadParsePreparation : SpaceTenantEntity
{
    private SpaceCadParsePreparation()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid SourceId { get; private set; }
    public string SourceSha256 { get; private set; } = string.Empty;
    public Guid FloorLogicalId { get; private set; }
    public string ConfirmedUnit { get; private set; } = string.Empty;
    public decimal ConfirmedScaleToMillimeters { get; private set; }
    public string CoordinateMetadataJson { get; private set; } = string.Empty;
    public string CoordinateTransformSha256 { get; private set; } = string.Empty;
    public Guid MappingProfileId { get; private set; }
    public int MappingProfileVersion { get; private set; }
    public string MappingDefinitionSha256 { get; private set; } = string.Empty;
    public string MappingPreviewSha256 { get; private set; } = string.Empty;
    public string SemanticPreviewSha256 { get; private set; } = string.Empty;
    public bool ReadyForParsing { get; private set; }
    public long BaseContentRevision { get; private set; }
    public string? BaseContentHash { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public static SpaceCadParsePreparation Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid sourceId,
        string sourceSha256,
        Guid floorLogicalId,
        string confirmedUnit,
        decimal confirmedScaleToMillimeters,
        string coordinateMetadataJson,
        string coordinateTransformSha256,
        Guid mappingProfileId,
        int mappingProfileVersion,
        string mappingDefinitionSha256,
        string mappingPreviewSha256,
        string semanticPreviewSha256,
        bool readyForParsing,
        long baseContentRevision,
        string? baseContentHash,
        DateTime expiresAtUtc)
    {
        RequireId(modelVersionId, nameof(modelVersionId));
        RequireId(sourceId, nameof(sourceId));
        RequireId(floorLogicalId, nameof(floorLogicalId));
        RequireId(mappingProfileId, nameof(mappingProfileId));
        if (mappingProfileVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(mappingProfileVersion));
        if (confirmedScaleToMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(confirmedScaleToMillimeters));
        if (baseContentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(baseContentRevision));
        if (expiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Expiry must be UTC.", nameof(expiresAtUtc));

        var value = new SpaceCadParsePreparation
        {
            ModelVersionId = modelVersionId,
            SourceId = sourceId,
            SourceSha256 = RequireHash(sourceSha256, nameof(sourceSha256)),
            FloorLogicalId = floorLogicalId,
            ConfirmedUnit = RequireText(confirmedUnit, 50, nameof(confirmedUnit)),
            ConfirmedScaleToMillimeters = confirmedScaleToMillimeters,
            CoordinateMetadataJson = RequireText(
                coordinateMetadataJson,
                8_000,
                nameof(coordinateMetadataJson)),
            CoordinateTransformSha256 = RequireHash(
                coordinateTransformSha256,
                nameof(coordinateTransformSha256)),
            MappingProfileId = mappingProfileId,
            MappingProfileVersion = mappingProfileVersion,
            MappingDefinitionSha256 = RequireHash(
                mappingDefinitionSha256,
                nameof(mappingDefinitionSha256)),
            MappingPreviewSha256 = RequireHash(
                mappingPreviewSha256,
                nameof(mappingPreviewSha256)),
            SemanticPreviewSha256 = RequireHash(
                semanticPreviewSha256,
                nameof(semanticPreviewSha256)),
            ReadyForParsing = readyForParsing,
            BaseContentRevision = baseContentRevision,
            BaseContentHash = baseContentHash is null
                ? null
                : RequireHash(baseContentHash, nameof(baseContentHash)),
            ExpiresAtUtc = expiresAtUtc,
        };
        value.SetTenant(tenantId);
        return value;
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hex value is required.", parameterName);
        return value.ToLowerInvariant();
    }

    private static string RequireText(string value, int maximum, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximum)
            throw new ArgumentException("A bounded value is required.", parameterName);
        return normalized;
    }
}
