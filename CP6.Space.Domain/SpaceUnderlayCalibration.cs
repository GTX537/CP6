namespace CP6.Space.Domain;

public readonly record struct SpaceCalibrationPoint(
    decimal PixelX,
    decimal PixelY,
    int WorldX,
    int WorldY);

public sealed class SpaceUnderlayCalibration : SpaceTenantEntity
{
    private const int MaximumRasterDimension = 100_000;
    private const int MaximumPageNumber = 200;
    private const double MinimumControlDistancePixels = 10d;

    private SpaceUnderlayCalibration()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid FloorLogicalId { get; private set; }
    public Guid SourceId { get; private set; }
    public int PageNumber { get; private set; }
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public decimal Point1PixelX { get; private set; }
    public decimal Point1PixelY { get; private set; }
    public int Point1WorldX { get; private set; }
    public int Point1WorldY { get; private set; }
    public decimal Point2PixelX { get; private set; }
    public decimal Point2PixelY { get; private set; }
    public int Point2WorldX { get; private set; }
    public int Point2WorldY { get; private set; }
    public decimal ValidationPixelX { get; private set; }
    public decimal ValidationPixelY { get; private set; }
    public int ValidationWorldX { get; private set; }
    public int ValidationWorldY { get; private set; }
    public decimal MillimetersPerPixel { get; private set; }
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }
    public decimal RotationZ { get; private set; }
    public decimal ValidationErrorMillimeters { get; private set; }
    public decimal ErrorThresholdMillimeters { get; private set; }

    public static SpaceUnderlayCalibration Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid floorLogicalId,
        Guid sourceId,
        int pageNumber,
        int pixelWidth,
        int pixelHeight,
        SpaceCalibrationPoint point1,
        SpaceCalibrationPoint point2,
        SpaceCalibrationPoint validationPoint,
        decimal minimumErrorThresholdMillimeters,
        decimal relativeErrorTolerance)
    {
        SpaceRevisionValue.RequireIdentity(modelVersionId, nameof(modelVersionId));
        SpaceRevisionValue.RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        SpaceRevisionValue.RequireIdentity(sourceId, nameof(sourceId));
        if (pageNumber is < 1 or > MaximumPageNumber)
            throw Invalid("The underlay page number is outside the supported range.");
        if (pixelWidth is < 1 or > MaximumRasterDimension ||
            pixelHeight is < 1 or > MaximumRasterDimension)
        {
            throw Invalid("The underlay raster dimensions are invalid.");
        }
        if (minimumErrorThresholdMillimeters <= 0 ||
            minimumErrorThresholdMillimeters > 10_000m)
        {
            throw Invalid(
                "The minimum calibration error threshold is invalid.");
        }
        if (relativeErrorTolerance <= 0 ||
            relativeErrorTolerance > 0.1m)
        {
            throw Invalid(
                "The relative calibration error tolerance is invalid.");
        }

        EnsurePointInside(point1, pixelWidth, pixelHeight, "point1");
        EnsurePointInside(point2, pixelWidth, pixelHeight, "point2");
        EnsurePointInside(
            validationPoint,
            pixelWidth,
            pixelHeight,
            "validationPoint");

        var pixelDx = (double)(point2.PixelX - point1.PixelX);
        var pixelDy = (double)(point1.PixelY - point2.PixelY);
        var pixelDistance = Math.Sqrt(
            pixelDx * pixelDx + pixelDy * pixelDy);
        if (pixelDistance < MinimumControlDistancePixels)
        {
            throw Invalid(
                $"Calibration control points must be at least " +
                $"{MinimumControlDistancePixels:0} pixels apart.");
        }

        var worldDx = (double)point2.WorldX - point1.WorldX;
        var worldDy = (double)point2.WorldY - point1.WorldY;
        var worldDistance = Math.Sqrt(
            worldDx * worldDx + worldDy * worldDy);
        if (worldDistance < 1d)
            throw Invalid("Calibration world points must be distinct.");
        var errorThresholdMillimeters = Math.Max(
            minimumErrorThresholdMillimeters,
            NonNegativeDecimal(
                worldDistance * (double)relativeErrorTolerance,
                4,
                "The calculated calibration error threshold is invalid."));

        var perpendicularDistance = PerpendicularDistance(
            point1,
            point2,
            validationPoint);
        var minimumValidationDistance = Math.Max(
            5d,
            pixelDistance * 0.01d);
        if (perpendicularDistance < minimumValidationDistance)
        {
            throw Invalid(
                "The validation point must be separated from the control line.");
        }

        var scale = PositiveDecimal(
            worldDistance / pixelDistance,
            8,
            "The calculated underlay scale is invalid.");
        var rotation = NormalizeRotation(
            Math.Atan2(worldDy, worldDx) -
            Math.Atan2(pixelDy, pixelDx));
        var rotationDecimal = FiniteDecimal(
            rotation * 180d / Math.PI,
            4,
            "The calculated underlay rotation is invalid.");
        var radians = (double)rotationDecimal * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var localX = (double)point1.PixelX;
        var localY = pixelHeight - (double)point1.PixelY;
        var scaledX = (double)scale * (cosine * localX - sine * localY);
        var scaledY = (double)scale * (sine * localX + cosine * localY);
        var offsetX = Integer(
            point1.WorldX - scaledX,
            "The calculated underlay X offset is outside the supported range.");
        var offsetY = Integer(
            point1.WorldY - scaledY,
            "The calculated underlay Y offset is outside the supported range.");
        var predictedValidation = Transform(
            validationPoint.PixelX,
            validationPoint.PixelY,
            pixelHeight,
            scale,
            rotationDecimal,
            offsetX,
            offsetY);
        var validationDx =
            predictedValidation.X - validationPoint.WorldX;
        var validationDy =
            predictedValidation.Y - validationPoint.WorldY;
        var validationError = NonNegativeDecimal(
            Math.Sqrt(
                validationDx * validationDx +
                validationDy * validationDy),
            4,
            "The calculated validation error is invalid.");
        if (validationError > errorThresholdMillimeters)
        {
            throw new SpaceUnderlayCalibrationException(
                "The validation point exceeds the calibration error threshold.",
                validationError,
                errorThresholdMillimeters);
        }

        var calibration = new SpaceUnderlayCalibration
        {
            ModelVersionId = modelVersionId,
            FloorLogicalId = floorLogicalId,
            SourceId = sourceId,
            PageNumber = pageNumber,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            Point1PixelX = point1.PixelX,
            Point1PixelY = point1.PixelY,
            Point1WorldX = point1.WorldX,
            Point1WorldY = point1.WorldY,
            Point2PixelX = point2.PixelX,
            Point2PixelY = point2.PixelY,
            Point2WorldX = point2.WorldX,
            Point2WorldY = point2.WorldY,
            ValidationPixelX = validationPoint.PixelX,
            ValidationPixelY = validationPoint.PixelY,
            ValidationWorldX = validationPoint.WorldX,
            ValidationWorldY = validationPoint.WorldY,
            MillimetersPerPixel = scale,
            OffsetX = offsetX,
            OffsetY = offsetY,
            RotationZ = rotationDecimal,
            ValidationErrorMillimeters = validationError,
            ErrorThresholdMillimeters = errorThresholdMillimeters,
        };
        calibration.SetTenant(tenantId);
        return calibration;
    }

    private static void EnsurePointInside(
        SpaceCalibrationPoint point,
        int width,
        int height,
        string name)
    {
        if (point.PixelX < 0 ||
            point.PixelY < 0 ||
            point.PixelX > width ||
            point.PixelY > height)
        {
            throw Invalid($"{name} is outside the underlay raster.");
        }
    }

    private static double PerpendicularDistance(
        SpaceCalibrationPoint point1,
        SpaceCalibrationPoint point2,
        SpaceCalibrationPoint validation)
    {
        var dx = (double)(point2.PixelX - point1.PixelX);
        var dy = (double)(point2.PixelY - point1.PixelY);
        var numerator = Math.Abs(
            dy * (double)validation.PixelX -
            dx * (double)validation.PixelY +
            (double)point2.PixelX * (double)point1.PixelY -
            (double)point2.PixelY * (double)point1.PixelX);
        return numerator / Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double X, double Y) Transform(
        decimal pixelX,
        decimal pixelY,
        int pixelHeight,
        decimal scale,
        decimal rotationDegrees,
        int offsetX,
        int offsetY)
    {
        var radians = (double)rotationDegrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var localX = (double)pixelX;
        var localY = pixelHeight - (double)pixelY;
        return (
            offsetX + (double)scale *
            (cosine * localX - sine * localY),
            offsetY + (double)scale *
            (sine * localX + cosine * localY));
    }

    private static decimal PositiveDecimal(
        double value,
        int decimals,
        string message)
    {
        if (!double.IsFinite(value) ||
            value <= 0 ||
            value > (double)decimal.MaxValue)
        {
            throw Invalid(message);
        }
        return Math.Round(
            (decimal)value,
            decimals,
            MidpointRounding.AwayFromZero);
    }

    private static decimal NonNegativeDecimal(
        double value,
        int decimals,
        string message)
    {
        if (!double.IsFinite(value) ||
            value < 0 ||
            value > (double)decimal.MaxValue)
        {
            throw Invalid(message);
        }
        return Math.Round(
            (decimal)value,
            decimals,
            MidpointRounding.AwayFromZero);
    }

    private static decimal FiniteDecimal(
        double value,
        int decimals,
        string message)
    {
        if (!double.IsFinite(value) ||
            value < (double)decimal.MinValue ||
            value > (double)decimal.MaxValue)
        {
            throw Invalid(message);
        }
        return Math.Round(
            (decimal)value,
            decimals,
            MidpointRounding.AwayFromZero);
    }

    private static int Integer(double value, string message)
    {
        if (!double.IsFinite(value) ||
            value < int.MinValue ||
            value > int.MaxValue)
        {
            throw Invalid(message);
        }
        return checked((int)Math.Round(
            value,
            MidpointRounding.AwayFromZero));
    }

    private static double NormalizeRotation(double radians)
    {
        var fullTurn = Math.PI * 2d;
        var normalized = radians % fullTurn;
        return normalized < 0 ? normalized + fullTurn : normalized;
    }

    private static SpaceUnderlayCalibrationException Invalid(string message) =>
        new(message);
}

public sealed class SpaceUnderlayCalibrationException : Exception
{
    public SpaceUnderlayCalibrationException(
        string message,
        decimal? validationErrorMillimeters = null,
        decimal? errorThresholdMillimeters = null)
        : base(message)
    {
        ValidationErrorMillimeters = validationErrorMillimeters;
        ErrorThresholdMillimeters = errorThresholdMillimeters;
    }

    public decimal? ValidationErrorMillimeters { get; }
    public decimal? ErrorThresholdMillimeters { get; }
}
