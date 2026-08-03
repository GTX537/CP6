using System.Text.Json.Serialization;

namespace CP6.Space.Contracts;

public static class SpaceCadIrVersions
{
    public const int SchemaVersion = 1;
    public const string CoordinateSystem = "FloorLocal-ZUp";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadSourceFormat
{
    Dxf = 0,
    Dwg = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadUnit
{
    Unknown = 0,
    Millimeter = 1,
    Centimeter = 2,
    Meter = 3,
    Inch = 4,
    Foot = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadIrEntityType
{
    Line = 0,
    Polyline = 1,
    ClosedPolyline = 2,
    Circle = 3,
    Arc = 4,
    BlockReference = 5,
    Text = 6,
    Hatch = 7,
    Spline = 8,
    Ellipse = 9,
    Dimension = 10,
    Unknown = 11,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadIssueSeverity
{
    Info = 0,
    Warning = 1,
    Blocking = 2,
}

public sealed record SpaceCadPointV1(
    decimal X,
    decimal Y,
    decimal Z = 0);

public sealed record SpaceCadBoundsV1(
    decimal MinX,
    decimal MinY,
    decimal MaxX,
    decimal MaxY);

public sealed record SpaceCadAffineTransformV1(
    decimal M11,
    decimal M12,
    decimal M21,
    decimal M22,
    decimal OffsetX,
    decimal OffsetY,
    decimal OffsetZ)
{
    public static SpaceCadAffineTransformV1 Identity { get; } =
        new(1, 0, 0, 1, 0, 0, 0);
}

public sealed record SpaceCadIrDocumentV1(
    int SchemaVersion,
    string SourceSha256,
    SpaceCadSourceFormat SourceFormat,
    string CadVersion,
    SpaceCadUnit Unit,
    decimal? ScaleToMillimeters,
    string CoordinateSystem,
    SpaceCadBoundsV1? Bounds,
    string ConverterId,
    string ConverterVersion);

public sealed record SpaceCadIrLayerV1(
    string LayerId,
    string Name,
    long EntityCount,
    string? Color = null,
    string? LineType = null,
    bool IsVisible = true);

public sealed record SpaceCadIrBlockV1(
    string BlockId,
    string Name,
    bool IsExternalReference,
    string? ExternalReferenceToken,
    long EntityCount);

public sealed record SpaceCadIrEntityV1(
    string SourceRef,
    SpaceCadIrEntityType Type,
    string RawType,
    string LayerId,
    string? BlockName,
    IReadOnlyList<SpaceCadPointV1> Points,
    decimal? Radius,
    decimal? StartAngleDegrees,
    decimal? EndAngleDegrees,
    SpaceCadAffineTransformV1 Transform,
    SpaceCadBoundsV1? Bounds,
    bool IsClosed,
    bool IsSupported,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record SpaceCadConversionIssueV1(
    string Code,
    SpaceCadIssueSeverity Severity,
    string? SourceRef = null,
    string? DetailToken = null);

public sealed record SpaceCadIrSummaryV1(
    long LayerCount,
    long BlockCount,
    long EntityCount,
    long SupportedEntityCount,
    long UnsupportedEntityCount,
    long MissingSourceRefCount,
    SpaceCadBoundsV1? Bounds);

public sealed record SpaceCadIrPackageV1(
    SpaceCadIrDocumentV1 Document,
    IReadOnlyList<SpaceCadIrLayerV1> Layers,
    IReadOnlyList<SpaceCadIrBlockV1> Blocks,
    IReadOnlyList<SpaceCadIrEntityV1> Entities,
    IReadOnlyList<SpaceCadConversionIssueV1> Issues,
    SpaceCadIrSummaryV1 Summary);
