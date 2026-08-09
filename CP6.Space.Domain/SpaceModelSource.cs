using System.Text.Json;

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
        ValidateFileSource(
            tenantId,
            modelVersionId,
            sourceType,
            file,
            requireClean: true);

        var source = new SpaceModelSource
        {
            ModelVersionId = modelVersionId,
            SourceType = sourceType,
            FileId = file.Id,
            DisplayName = RequireText(displayName, 260, nameof(displayName)),
            Sha256 = file.Sha256!,
            State = SpaceSourceState.Ready,
        };
        source.SetTenant(tenantId);
        return source;
    }

    public static SpaceModelSource CreatePendingFileSource(
        Guid tenantId,
        Guid modelVersionId,
        SpaceSourceType sourceType,
        SpaceFile file,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(file);
        ValidateFileSource(
            tenantId,
            modelVersionId,
            sourceType,
            file,
            requireClean: false);
        if (file.State is not (
            SpaceFileState.Quarantined or SpaceFileState.Scanning))
        {
            throw new SpaceFileStateException(
                "A pending source requires a quarantined or scanning file.");
        }

        var source = new SpaceModelSource
        {
            ModelVersionId = modelVersionId,
            SourceType = sourceType,
            FileId = file.Id,
            DisplayName = RequireText(displayName, 260, nameof(displayName)),
            Sha256 = file.Sha256!,
            State = SpaceSourceState.Scanning,
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
        if (SourceType is SpaceSourceType.Dwg or SpaceSourceType.Dxf)
        {
            ValidateCadCoordinateMetadata(
                unit,
                scaleToMillimeters,
                transformJson);
        }

        ParserVersion = RequireText(parserVersion, 100, nameof(parserVersion));
        MappingProfileId = mappingProfileId;
        MappingProfileVersion = mappingProfileVersion;
        Unit = OptionalText(unit, 50, nameof(unit));
        ScaleToMillimeters = scaleToMillimeters;
        TransformJson = OptionalText(transformJson, 8000, nameof(transformJson));
    }

    public void BeginParsing()
    {
        if (State is not (
                SpaceSourceState.Ready or
                SpaceSourceState.PreviewReady))
        {
            throw new SpaceFileStateException(
                "Only a ready or preview-ready source can begin parsing.");
        }
        if (SourceType is SpaceSourceType.Dwg or SpaceSourceType.Dxf
            && (string.IsNullOrWhiteSpace(Unit)
                || ScaleToMillimeters is null or <= 0
                || string.IsNullOrWhiteSpace(TransformJson)))
        {
            throw new SpaceFileStateException(
                "CAD parsing requires confirmed units, scale, transform and target floor metadata.");
        }
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

    public void CompleteFileScan(SpaceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (FileId != file.Id || TenantId != file.TenantId)
        {
            throw new SpaceTenantScopeException(
                "The source and scanned file must share tenant and identity.");
        }
        if (State != SpaceSourceState.Scanning)
        {
            throw new SpaceFileStateException(
                "Only a scanning source can accept a file scan result.");
        }

        State = file.State switch
        {
            SpaceFileState.Clean => SpaceSourceState.Ready,
            SpaceFileState.Rejected => SpaceSourceState.Rejected,
            _ => throw new SpaceFileStateException(
                "The file scan has not reached a terminal result."),
        };
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

    private static void ValidateFileSource(
        Guid tenantId,
        Guid modelVersionId,
        SpaceSourceType sourceType,
        SpaceFile file,
        bool requireClean)
    {
        if (modelVersionId == Guid.Empty)
            throw new ArgumentException("Model version is required.", nameof(modelVersionId));
        if (file.TenantId != tenantId)
            throw new SpaceTenantScopeException("Source and file tenants must match.");
        if ((requireClean && file.State != SpaceFileState.Clean) || file.IsDeleted)
            throw new SpaceFileStateException("A source requires a clean file.");
        if (file.RetentionClass != SpaceFileRetentionClass.Source)
            throw new SpaceFileStateException("A source requires Source retention.");
        if (file.Sha256 is null)
            throw new SpaceFileStateException("A source file requires a SHA-256 hash.");
        if (!MatchesSourceType(sourceType, file.Extension))
        {
            throw new SpaceFileStateException(
                "The source type does not match the file extension.");
        }
    }

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

    private void ValidateCadCoordinateMetadata(
        string? unit,
        decimal? scaleToMillimeters,
        string? transformJson)
    {
        if (string.IsNullOrWhiteSpace(unit)
            || scaleToMillimeters is null or <= 0
            || string.IsNullOrWhiteSpace(transformJson))
        {
            throw new ArgumentException(
                "CAD import requires confirmed units, scale and coordinate metadata.");
        }

        try
        {
            using var document = JsonDocument.Parse(transformJson);
            var root = document.RootElement;
            var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            var sourceSha256 = root.GetProperty("sourceSha256").GetString();
            var unitConfirmed = root.GetProperty("unitConfirmed").GetBoolean();
            var confirmedUnit = root.GetProperty("confirmedUnit").GetString();
            var confirmedScale = root.GetProperty("confirmedScaleToMillimeters").GetDecimal();
            var transformSha256 = root.GetProperty("transformSha256").GetString();
            var targetFloor = root.GetProperty("targetFloor");
            var floorLogicalId = targetFloor.GetProperty("floorLogicalId").GetGuid();
            var coordinateSystem = targetFloor.GetProperty("coordinateSystem").GetString();
            if (schemaVersion != 1
                || !unitConfirmed
                || !Sha256.Equals(sourceSha256, StringComparison.Ordinal)
                || !unit.Equals(confirmedUnit, StringComparison.Ordinal)
                || scaleToMillimeters != confirmedScale
                || floorLogicalId == Guid.Empty
                || !"LOCAL_MM_Z_UP".Equals(coordinateSystem, StringComparison.Ordinal)
                || transformSha256 is null
                || transformSha256.Length != 64
                || transformSha256.Any(character =>
                    !Uri.IsHexDigit(character) || char.IsUpper(character)))
            {
                throw new ArgumentException(
                    "CAD coordinate metadata does not match the source confirmation.",
                    nameof(transformJson));
            }
        }
        catch (Exception exception) when (
            exception is JsonException
                or KeyNotFoundException
                or InvalidOperationException
                or FormatException)
        {
            throw new ArgumentException(
                "CAD coordinate metadata is invalid.",
                nameof(transformJson),
                exception);
        }
    }
}
