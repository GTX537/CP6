namespace CP6.Space.Domain;

public sealed class SpaceModelSource : SpaceTenantEntity
{
    private SpaceModelSource()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public SpaceSourceType SourceType { get; private set; }
    public Guid? FileId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;
    public string? ParserVersion { get; private set; }
    public Guid? MappingProfileId { get; private set; }
    public long? MappingProfileVersion { get; private set; }
    public string? Unit { get; private set; }
    public decimal? ScaleToMillimeters { get; private set; }
    public string? TransformJson { get; private set; }
    public SpaceSourceState State { get; private set; }
    public Guid? ImportedCommandBatchId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceModelSource CreateFileSource(
        Guid tenantId,
        Guid modelVersionId,
        SpaceSourceType sourceType,
        SpaceFile file,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (modelVersionId == Guid.Empty)
            throw new ArgumentException("Model version is required.", nameof(modelVersionId));
        if (file.TenantId != tenantId)
            throw new SpaceTenantScopeException("Source and file tenants must match.");
        if (file.State != SpaceFileState.Clean || file.IsDeleted)
            throw new SpaceFileStateException("A source requires a clean file.");
        if (file.RetentionClass != SpaceFileRetentionClass.Source)
            throw new SpaceFileStateException("A source requires Source retention.");
        if (file.Sha256 is null)
            throw new SpaceFileStateException("A source file requires a SHA-256 hash.");
        if (!MatchesSourceType(sourceType, file.Extension))
            throw new SpaceFileStateException(
                "The source type does not match the clean file extension.");

        var source = new SpaceModelSource
        {
            ModelVersionId = modelVersionId,
            SourceType = sourceType,
            FileId = file.Id,
            DisplayName = RequireText(displayName, 260, nameof(displayName)),
            Sha256 = file.Sha256,
            State = SpaceSourceState.Ready,
        };
        source.SetTenant(tenantId);
        return source;
    }

    public static SpaceModelSource CreateInlineSource(
        Guid tenantId,
        Guid modelVersionId,
        SpaceSourceType sourceType,
        string displayName,
        string sha256)
    {
        if (sourceType is not (SpaceSourceType.Editor or SpaceSourceType.Template))
            throw new ArgumentException(
                "Only Editor and Template sources may omit a file.",
                nameof(sourceType));
        if (modelVersionId == Guid.Empty)
            throw new ArgumentException("Model version is required.", nameof(modelVersionId));

        var source = new SpaceModelSource
        {
            ModelVersionId = modelVersionId,
            SourceType = sourceType,
            DisplayName = RequireText(displayName, 260, nameof(displayName)),
            Sha256 = RequireHash(sha256),
            State = SpaceSourceState.Ready,
        };
        source.SetTenant(tenantId);
        return source;
    }

    public void ConfigureImport(
        string parserVersion,
        Guid? mappingProfileId,
        long? mappingProfileVersion,
        string? unit,
        decimal? scaleToMillimeters,
        string? transformJson)
    {
        if (State is not (SpaceSourceState.Ready or SpaceSourceState.PreviewReady))
            throw new SpaceFileStateException("Only a ready source can be configured.");
        if (mappingProfileId.HasValue != mappingProfileVersion.HasValue)
            throw new ArgumentException(
                "Mapping profile identity and version must be supplied together.");
        if (mappingProfileVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(mappingProfileVersion));
        if (scaleToMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(scaleToMillimeters));

        ParserVersion = RequireText(parserVersion, 100, nameof(parserVersion));
        MappingProfileId = mappingProfileId;
        MappingProfileVersion = mappingProfileVersion;
        Unit = OptionalText(unit, 50, nameof(unit));
        ScaleToMillimeters = scaleToMillimeters;
        TransformJson = OptionalText(transformJson, 8000, nameof(transformJson));
    }

    public void BeginParsing()
    {
        RequireState(SpaceSourceState.Ready);
        State = SpaceSourceState.Parsing;
    }

    public void MarkPreviewReady()
    {
        RequireState(SpaceSourceState.Parsing);
        State = SpaceSourceState.PreviewReady;
    }

    public void MarkImported(Guid commandBatchId)
    {
        if (State is not (SpaceSourceState.Parsing or SpaceSourceState.PreviewReady))
            throw new SpaceFileStateException("Only a parsed source can be imported.");
        if (commandBatchId == Guid.Empty)
            throw new ArgumentException("Command batch is required.", nameof(commandBatchId));

        ImportedCommandBatchId = commandBatchId;
        State = SpaceSourceState.Imported;
    }

    public void Reject()
    {
        if (State == SpaceSourceState.Imported)
            throw new SpaceFileStateException("An imported source cannot be rejected.");
        State = SpaceSourceState.Rejected;
    }

    private void RequireState(SpaceSourceState expected)
    {
        if (State != expected)
            throw new SpaceFileStateException(
                $"Source state must be {expected}, but was {State}.");
    }

    private static string RequireHash(string value)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hex value is required.", nameof(value));
        return value.ToLowerInvariant();
    }

    private static bool MatchesSourceType(
        SpaceSourceType sourceType,
        string? extension) =>
        (sourceType, extension?.ToLowerInvariant()) switch
        {
            (SpaceSourceType.Dwg, ".dwg") => true,
            (SpaceSourceType.Dxf, ".dxf") => true,
            (SpaceSourceType.Pdf, ".pdf") => true,
            (SpaceSourceType.Png, ".png") => true,
            (SpaceSourceType.Jpg, ".jpg" or ".jpeg") => true,
            (SpaceSourceType.Excel, ".xlsx") => true,
            _ => false,
        };

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return RequireText(value, maxLength, parameterName);
    }
}
