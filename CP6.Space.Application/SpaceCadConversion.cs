using System.Text.RegularExpressions;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed record SpaceCadConversionRequest(
    Guid TenantId,
    Guid FileId,
    Guid SourceId,
    string SourceSha256,
    SpaceCadSourceFormat SourceFormat,
    string ConverterId,
    string ConverterVersion);

public sealed record SpaceCadConversionResult(
    string SourceSha256,
    string CadIrSha256,
    string ConverterId,
    string ConverterVersion,
    SpaceCadIrSummaryV1 Summary,
    IReadOnlyList<SpaceCadConversionIssueV1> Issues);

public interface ISpaceCadIrSink
{
    ValueTask WriteDocumentAsync(
        SpaceCadIrDocumentV1 document,
        CancellationToken cancellationToken = default);

    ValueTask WriteLayerAsync(
        SpaceCadIrLayerV1 layer,
        CancellationToken cancellationToken = default);

    ValueTask WriteBlockAsync(
        SpaceCadIrBlockV1 block,
        CancellationToken cancellationToken = default);

    ValueTask WriteEntityAsync(
        SpaceCadIrEntityV1 entity,
        CancellationToken cancellationToken = default);

    ValueTask<string> CompleteAsync(
        IReadOnlyList<SpaceCadConversionIssueV1> issues,
        SpaceCadIrSummaryV1 summary,
        CancellationToken cancellationToken = default);
}

public interface ICadConverter
{
    Task<SpaceCadConversionResult> ConvertAsync(
        SpaceCadConversionRequest request,
        Stream source,
        ISpaceCadIrSink sink,
        CancellationToken cancellationToken = default);
}

public static partial class SpaceCadConversionContract
{
    public const int MaximumIdentifierLength = 128;
    public const int MaximumSourceReferenceLength = 200;
    public const int MaximumAttributeCount = 64;
    public const int MaximumAttributeKeyLength = 128;
    public const int MaximumAttributeValueLength = 512;

    public static void ValidateRequest(SpaceCadConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireId(request.TenantId, nameof(request.TenantId));
        RequireId(request.FileId, nameof(request.FileId));
        RequireId(request.SourceId, nameof(request.SourceId));
        RequireSha256(request.SourceSha256, nameof(request.SourceSha256));
        RequireIdentifier(request.ConverterId, nameof(request.ConverterId));
        RequireIdentifier(request.ConverterVersion, nameof(request.ConverterVersion));
    }

    public static void ValidateDocument(
        SpaceCadConversionRequest request,
        SpaceCadIrDocumentV1 document)
    {
        ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != SpaceCadIrVersions.SchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(document),
                $"CAD IR schema must be {SpaceCadIrVersions.SchemaVersion}.");
        }

        RequireSha256(document.SourceSha256, nameof(document.SourceSha256));
        if (!document.SourceSha256.Equals(
                request.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "CAD IR source hash does not match the conversion request.");
        }

        if (document.SourceFormat != request.SourceFormat)
        {
            throw new InvalidDataException(
                "CAD IR source format does not match the conversion request.");
        }

        RequireIdentifier(document.CadVersion, nameof(document.CadVersion));
        RequireIdentifier(document.ConverterId, nameof(document.ConverterId));
        RequireIdentifier(document.ConverterVersion, nameof(document.ConverterVersion));
        if (!document.ConverterId.Equals(request.ConverterId, StringComparison.Ordinal)
            || !document.ConverterVersion.Equals(
                request.ConverterVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "CAD IR converter identity does not match the conversion request.");
        }

        if (!document.CoordinateSystem.Equals(
                SpaceCadIrVersions.CoordinateSystem,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"CAD IR coordinate system must be {SpaceCadIrVersions.CoordinateSystem}.");
        }

        if (document.Unit == SpaceCadUnit.Unknown)
        {
            if (document.ScaleToMillimeters is not null)
            {
                throw new InvalidDataException(
                    "Unknown CAD units cannot declare a millimeter scale.");
            }
        }
        else if (document.ScaleToMillimeters is null or <= 0)
        {
            throw new InvalidDataException(
                "Known CAD units require a positive millimeter scale.");
        }

        ValidateBounds(document.Bounds);
    }

    public static void ValidateEntity(SpaceCadIrEntityV1 entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        RequireBoundedText(
            entity.SourceRef,
            MaximumSourceReferenceLength,
            nameof(entity.SourceRef));
        RequireIdentifier(entity.RawType, nameof(entity.RawType));
        RequireBoundedText(entity.LayerId, MaximumIdentifierLength, nameof(entity.LayerId));
        if (entity.BlockName is not null)
        {
            RequireBoundedText(
                entity.BlockName,
                MaximumIdentifierLength,
                nameof(entity.BlockName));
        }

        ArgumentNullException.ThrowIfNull(entity.Points);
        ArgumentNullException.ThrowIfNull(entity.Transform);
        ArgumentNullException.ThrowIfNull(entity.Attributes);
        ValidateBounds(entity.Bounds);
        if (entity.Type == SpaceCadIrEntityType.Unknown && entity.IsSupported)
        {
            throw new InvalidDataException(
                "Unknown CAD entities cannot be marked supported.");
        }

        if (entity.Attributes.Count > MaximumAttributeCount)
        {
            throw new InvalidDataException(
                $"CAD entity attributes exceed {MaximumAttributeCount} entries.");
        }

        foreach (var (key, value) in entity.Attributes)
        {
            RequireBoundedText(key, MaximumAttributeKeyLength, "attribute key");
            RequireBoundedText(value, MaximumAttributeValueLength, "attribute value");
        }
    }

    public static void ValidatePackage(
        SpaceCadConversionRequest request,
        SpaceCadIrPackageV1 package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateDocument(request, package.Document);
        ArgumentNullException.ThrowIfNull(package.Layers);
        ArgumentNullException.ThrowIfNull(package.Blocks);
        ArgumentNullException.ThrowIfNull(package.Entities);
        ArgumentNullException.ThrowIfNull(package.Issues);
        ArgumentNullException.ThrowIfNull(package.Summary);

        var layerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in package.Layers)
        {
            RequireBoundedText(layer.LayerId, MaximumIdentifierLength, nameof(layer.LayerId));
            RequireBoundedText(layer.Name, MaximumIdentifierLength, nameof(layer.Name));
            if (layer.Color is not null)
            {
                RequireBoundedText(layer.Color, MaximumIdentifierLength, nameof(layer.Color));
            }
            if (layer.LineType is not null)
            {
                RequireBoundedText(
                    layer.LineType,
                    MaximumIdentifierLength,
                    nameof(layer.LineType));
            }
            if (layer.EntityCount < 0 || !layerIds.Add(layer.LayerId))
            {
                throw new InvalidDataException(
                    "CAD IR layers must have unique IDs and non-negative counts.");
            }
        }

        var blockIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in package.Blocks)
        {
            RequireBoundedText(block.BlockId, MaximumIdentifierLength, nameof(block.BlockId));
            RequireBoundedText(block.Name, MaximumIdentifierLength, nameof(block.Name));
            if (block.ExternalReferenceToken is not null)
            {
                RequireBoundedText(
                    block.ExternalReferenceToken,
                    MaximumIdentifierLength,
                    nameof(block.ExternalReferenceToken));
            }
            if (block.EntityCount < 0 || !blockIds.Add(block.BlockId))
            {
                throw new InvalidDataException(
                    "CAD IR blocks must have unique IDs and non-negative counts.");
            }
        }

        var sourceRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in package.Entities)
        {
            ValidateEntity(entity);
            if (!sourceRefs.Add(entity.SourceRef))
            {
                throw new InvalidDataException(
                    $"Duplicate CAD source reference '{entity.SourceRef}'.");
            }

            if (!layerIds.Contains(entity.LayerId))
            {
                throw new InvalidDataException(
                    $"CAD entity references unknown layer '{entity.LayerId}'.");
            }
        }

        foreach (var layer in package.Layers)
        {
            if (layer.EntityCount != package.Entities.LongCount(
                    entity => entity.LayerId.Equals(layer.LayerId, StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"CAD layer '{layer.LayerId}' entity count does not match its records.");
            }
        }

        foreach (var issue in package.Issues)
        {
            RequireIdentifier(issue.Code, nameof(issue.Code));
            if (issue.SourceRef is not null)
            {
                RequireBoundedText(
                    issue.SourceRef,
                    MaximumSourceReferenceLength,
                    nameof(issue.SourceRef));
            }
            if (issue.DetailToken is not null)
            {
                RequireBoundedText(
                    issue.DetailToken,
                    MaximumIdentifierLength,
                    nameof(issue.DetailToken));
            }
        }

        var supported = package.Entities.LongCount(entity => entity.IsSupported);
        var unsupported = package.Entities.LongCount(entity => !entity.IsSupported);
        var missingSourceRefs = package.Issues.LongCount(
            issue => issue.Code.Equals(
                "SPACE_CAD_SOURCE_REF_SYNTHESIZED",
                StringComparison.Ordinal));
        if (package.Summary.LayerCount != package.Layers.Count
            || package.Summary.BlockCount != package.Blocks.Count
            || package.Summary.EntityCount != package.Entities.Count
            || package.Summary.SupportedEntityCount != supported
            || package.Summary.UnsupportedEntityCount != unsupported
            || package.Summary.MissingSourceRefCount != missingSourceRefs)
        {
            throw new InvalidDataException(
                "CAD IR summary counts do not match the package records.");
        }

        ValidateBounds(package.Summary.Bounds);
        if (package.Summary.Bounds != package.Document.Bounds)
        {
            throw new InvalidDataException(
                "CAD IR document and summary bounds must match.");
        }
    }

    private static void ValidateBounds(SpaceCadBoundsV1? bounds)
    {
        if (bounds is not null
            && (bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY))
        {
            throw new InvalidDataException("CAD IR bounds are inverted.");
        }
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A non-empty ID is required.", parameterName);
    }

    private static void RequireSha256(string value, string parameterName)
    {
        if (!Sha256Pattern().IsMatch(value ?? string.Empty))
        {
            throw new ArgumentException(
                "A lowercase 64-character SHA-256 is required.",
                parameterName);
        }
    }

    private static void RequireIdentifier(string value, string parameterName) =>
        RequireBoundedText(value, MaximumIdentifierLength, parameterName);

    private static void RequireBoundedText(
        string value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A non-empty value up to {maximumLength} characters is required.",
                parameterName);
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
