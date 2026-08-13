using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceLocationRevisionTests
{
    [Fact]
    public void Imported_specification_marks_unbound_code_as_imported()
    {
        var location = Create(
            "L-001",
            SpaceLocationCodeOrigin.Generated,
            SpaceExternalBindingState.Unbound);

        location.UpdateImportedSpecification(
            location.FloorLogicalId,
            location.RackLogicalId!.Value,
            "L-002",
            2,
            3,
            1,
            800,
            900,
            1000,
            250m);

        Assert.Equal("L-002", location.LocationCode);
        Assert.Equal(SpaceLocationCodeOrigin.Imported, location.CodeOrigin);
        Assert.Equal(2, location.ColumnNo);
        Assert.Equal(3, location.LevelNo);
        Assert.Equal(250m, location.MaxLoad);
    }

    [Fact]
    public void Imported_specification_preserves_bound_code_and_origin()
    {
        var location = Create(
            "WMS-001",
            SpaceLocationCodeOrigin.Adopted,
            SpaceExternalBindingState.Bound);

        location.UpdateImportedSpecification(
            location.FloorLogicalId,
            location.RackLogicalId!.Value,
            "WMS-001",
            2,
            1,
            1,
            800,
            900,
            1000);

        Assert.Equal("WMS-001", location.LocationCode);
        Assert.Equal(SpaceLocationCodeOrigin.Adopted, location.CodeOrigin);
        Assert.Equal(SpaceExternalBindingState.Bound,
            location.ExternalBindingState);
    }

    [Fact]
    public void Imported_specification_rejects_replacing_a_bound_code()
    {
        var location = Create(
            "WMS-001",
            SpaceLocationCodeOrigin.Adopted,
            SpaceExternalBindingState.Bound);

        Assert.Throws<InvalidOperationException>(() =>
            location.UpdateImportedSpecification(
                location.FloorLogicalId,
                location.RackLogicalId!.Value,
                "WMS-002",
                2,
                1,
                1,
                800,
                900,
                1000));

        Assert.Equal("WMS-001", location.LocationCode);
        Assert.Equal(1, location.ColumnNo);
    }

    [Fact]
    public void Generated_code_can_be_cleared_and_reapplied()
    {
        var location = Create(
            "L-001",
            SpaceLocationCodeOrigin.Generated,
            SpaceExternalBindingState.Unbound);

        location.ClearGeneratedLocationCode();
        location.ApplyGeneratedLocationCode("L-002");

        Assert.Equal("L-002", location.LocationCode);
        Assert.Equal(SpaceLocationCodeOrigin.Generated, location.CodeOrigin);
        Assert.Equal(
            SpaceExternalBindingState.Unbound,
            location.ExternalBindingState);
    }

    [Theory]
    [InlineData(SpaceLocationCodeOrigin.Imported, SpaceExternalBindingState.Unbound)]
    [InlineData(SpaceLocationCodeOrigin.Manual, SpaceExternalBindingState.Unbound)]
    [InlineData(SpaceLocationCodeOrigin.Adopted, SpaceExternalBindingState.Bound)]
    public void Protected_code_cannot_be_changed_by_generation(
        SpaceLocationCodeOrigin origin,
        SpaceExternalBindingState bindingState)
    {
        var location = Create("PROTECTED", origin, bindingState);

        Assert.Throws<InvalidOperationException>(
            location.ClearGeneratedLocationCode);
        Assert.Throws<InvalidOperationException>(() =>
            location.ApplyGeneratedLocationCode("REPLACEMENT"));
        Assert.Equal("PROTECTED", location.LocationCode);
    }

    private static SpaceLocationRevision Create(
        string code,
        SpaceLocationCodeOrigin origin,
        SpaceExternalBindingState bindingState) =>
        SpaceLocationRevision.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            code,
            1,
            1,
            1,
            1000,
            1000,
            1000,
            codeOrigin: origin,
            externalBindingState: bindingState);
}
