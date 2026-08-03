namespace CP6.Space.Contracts;

public static class SpaceCadCoordinateVersions
{
    public const int SchemaVersion = 1;
    public const string TargetCoordinateSystem = "LOCAL_MM_Z_UP";
}

public sealed record SpaceCadCoordinateLimitsV1(
    decimal MinimumFloorSpanMillimeters = 1_000m,
    decimal MaximumFloorSpanMillimeters = 5_000_000m,
    decimal BoundaryToleranceMillimeters = 50m);

public sealed record SpaceCadCoordinateAnalysisV1(
    int SchemaVersion,
    string SourceSha256,
    SpaceCadUnit SuggestedUnit,
    decimal? SuggestedScaleToMillimeters,
    SpaceCadBoundsV1? SourceBounds,
    SpaceCadBoundsV1? SuggestedBoundsMillimeters,
    bool IsSuggestedExtentPlausible,
    bool RequiresUnitConfirmation,
    IReadOnlyList<SpaceCadConversionIssueV1> Issues);

public sealed record SpaceCadMillimeterPointV1(
    int X,
    int Y,
    int Z = 0);

public sealed record SpaceCadFloorAssignmentV1(
    Guid FloorLogicalId,
    string FloorCode,
    int Level,
    int ElevationMillimeters,
    string CoordinateSystem,
    SpaceCadBoundsV1 BoundaryBounds);

public sealed record SpaceCadCoordinateConfirmationV1(
    string SourceSha256,
    bool UnitConfirmed,
    SpaceCadUnit ConfirmedUnit,
    SpaceCadPointV1 SourceOriginInSourceUnits,
    SpaceCadMillimeterPointV1 FloorOriginMillimeters,
    decimal RotationZDegrees,
    SpaceCadFloorAssignmentV1 TargetFloor);

public sealed record SpaceCadCoordinateMetadataV1(
    int SchemaVersion,
    string SourceSha256,
    bool UnitConfirmed,
    SpaceCadUnit DetectedUnit,
    decimal? DetectedScaleToMillimeters,
    SpaceCadUnit ConfirmedUnit,
    decimal ConfirmedScaleToMillimeters,
    SpaceCadPointV1 SourceOriginInSourceUnits,
    SpaceCadMillimeterPointV1 FloorOriginMillimeters,
    decimal RotationZDegrees,
    SpaceCadFloorAssignmentV1 TargetFloor,
    SpaceCadAffineTransformV1 SourceToFloorTransform,
    SpaceCadBoundsV1? SourceBounds,
    SpaceCadBoundsV1? PreparedBounds,
    string TransformSha256);

public sealed record SpaceCadCoordinatePreparationV1(
    SpaceCadCoordinateMetadataV1 Metadata,
    SpaceCadIrPackageV1 Package,
    IReadOnlyList<SpaceCadConversionIssueV1> Issues,
    bool ReadyForParsing);
