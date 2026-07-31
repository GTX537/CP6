using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceUnderlayCalibrationTests
{
    [Fact]
    public void Two_points_define_scale_origin_and_zero_rotation()
    {
        var calibration = SpaceUnderlayCalibration.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            pageNumber: 1,
            pixelWidth: 1_000,
            pixelHeight: 500,
            new SpaceCalibrationPoint(0, 500, 1_000, 2_000),
            new SpaceCalibrationPoint(100, 500, 2_000, 2_000),
            new SpaceCalibrationPoint(0, 400, 1_000, 3_000),
            minimumErrorThresholdMillimeters: 50,
            relativeErrorTolerance: 0.002m);

        Assert.Equal(10m, calibration.MillimetersPerPixel);
        Assert.Equal(1_000, calibration.OffsetX);
        Assert.Equal(2_000, calibration.OffsetY);
        Assert.Equal(0m, calibration.RotationZ);
        Assert.Equal(0m, calibration.ValidationErrorMillimeters);
    }

    [Fact]
    public void Two_points_define_rotation_in_y_up_world_coordinates()
    {
        var calibration = SpaceUnderlayCalibration.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            pageNumber: 1,
            pixelWidth: 1_000,
            pixelHeight: 500,
            new SpaceCalibrationPoint(0, 500, 1_000, 2_000),
            new SpaceCalibrationPoint(100, 500, 1_000, 3_000),
            new SpaceCalibrationPoint(0, 400, 0, 2_000),
            minimumErrorThresholdMillimeters: 50,
            relativeErrorTolerance: 0.002m);

        Assert.Equal(10m, calibration.MillimetersPerPixel);
        Assert.Equal(1_000, calibration.OffsetX);
        Assert.Equal(2_000, calibration.OffsetY);
        Assert.Equal(90m, calibration.RotationZ);
        Assert.Equal(0m, calibration.ValidationErrorMillimeters);
    }

    [Fact]
    public void Validation_error_above_server_threshold_is_rejected()
    {
        var exception = Assert.Throws<SpaceUnderlayCalibrationException>(
            () => SpaceUnderlayCalibration.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                pageNumber: 1,
                pixelWidth: 1_000,
                pixelHeight: 500,
                new SpaceCalibrationPoint(0, 500, 1_000, 2_000),
                new SpaceCalibrationPoint(100, 500, 2_000, 2_000),
                new SpaceCalibrationPoint(0, 400, 1_100, 3_000),
                minimumErrorThresholdMillimeters: 50,
                relativeErrorTolerance: 0.002m));

        Assert.Equal(100m, exception.ValidationErrorMillimeters);
        Assert.Equal(50m, exception.ErrorThresholdMillimeters);
    }

    [Fact]
    public void Validation_point_on_control_line_is_rejected()
    {
        var exception = Assert.Throws<SpaceUnderlayCalibrationException>(
            () => SpaceUnderlayCalibration.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                pageNumber: 1,
                pixelWidth: 1_000,
                pixelHeight: 500,
                new SpaceCalibrationPoint(0, 500, 1_000, 2_000),
                new SpaceCalibrationPoint(100, 500, 2_000, 2_000),
                new SpaceCalibrationPoint(50, 500, 1_500, 2_000),
                minimumErrorThresholdMillimeters: 50,
                relativeErrorTolerance: 0.002m));

        Assert.Contains("validation point", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Long_control_distance_uses_the_relative_error_threshold()
    {
        var calibration = SpaceUnderlayCalibration.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            pageNumber: 1,
            pixelWidth: 1_000,
            pixelHeight: 500,
            new SpaceCalibrationPoint(0, 500, 0, 0),
            new SpaceCalibrationPoint(100, 500, 100_000, 0),
            new SpaceCalibrationPoint(0, 400, 150, 100_000),
            minimumErrorThresholdMillimeters: 50,
            relativeErrorTolerance: 0.002m);

        Assert.Equal(200m, calibration.ErrorThresholdMillimeters);
        Assert.Equal(150m, calibration.ValidationErrorMillimeters);
    }
}
