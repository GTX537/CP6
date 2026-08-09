using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceWmsAdoptionTests
{
    private static readonly DateTime ObservedAt =
        new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Discovery_preserves_wms_identity_and_provenance()
    {
        var logicalId = Guid.NewGuid();

        var adoption = NewAdoption(logicalId);

        Assert.Equal(logicalId, adoption.WmsLogicalId);
        Assert.Equal("EXT-001", adoption.ExternalLocationId);
        Assert.Equal("WMS-A-01", adoption.WmsLocationCode);
        Assert.Equal("simulator-v1", adoption.DataSource);
        Assert.Equal("Simulated", adoption.DataSourceKind);
        Assert.Equal(SpaceWmsAdoptionStatus.Unbound, adoption.Status);
    }

    [Fact]
    public void Bound_adoption_becomes_diverged_when_wms_code_changes()
    {
        var adoption = NewAdoption(Guid.NewGuid());
        adoption.Bind(Guid.NewGuid(), Guid.NewGuid(), ObservedAt.AddMinutes(1));

        adoption.Observe(
            "simulator-v2",
            "Simulated",
            "EXT-001",
            "WMS-A-02",
            true,
            "2",
            new string('b', 64),
            ObservedAt.AddMinutes(2));

        Assert.Equal(SpaceWmsAdoptionStatus.Diverged, adoption.Status);
        Assert.Equal("WMS-A-01", adoption.BoundLocationCode);
        Assert.Equal("WMS-A-02", adoption.WmsLocationCode);
        Assert.Equal("simulator-v2", adoption.DataSource);
    }

    [Fact]
    public void Missing_adoption_cannot_be_bound()
    {
        var adoption = NewAdoption(Guid.NewGuid());
        adoption.MarkMissing(ObservedAt.AddMinutes(1));

        var exception = Assert.Throws<InvalidOperationException>(
            () => adoption.Bind(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ObservedAt.AddMinutes(2)));

        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void Bound_space_location_keeps_adopted_wms_code_immutable()
    {
        var location = SpaceLocationRevision.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "GENERATED-01",
            1,
            1,
            1,
            1_000,
            1_200,
            1_100);

        location.BindAdoptedLocationCode("WMS-A-01");

        Assert.Equal("WMS-A-01", location.LocationCode);
        Assert.Equal(SpaceLocationCodeOrigin.Adopted, location.CodeOrigin);
        Assert.Equal(
            SpaceExternalBindingState.Bound,
            location.ExternalBindingState);
        Assert.Throws<InvalidOperationException>(
            () => location.BindAdoptedLocationCode("WMS-A-02"));
    }

    [Fact]
    public void Rebinding_same_location_and_code_is_idempotent()
    {
        var versionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var adoption = NewAdoption(Guid.NewGuid());

        adoption.Bind(versionId, locationId, ObservedAt.AddMinutes(1));
        adoption.Bind(versionId, locationId, ObservedAt.AddMinutes(2));

        Assert.Equal(SpaceWmsAdoptionStatus.Bound, adoption.Status);
        Assert.Equal(locationId, adoption.LocationLogicalId);
        Assert.Equal("WMS-A-01", adoption.BoundLocationCode);
    }

    private static SpaceWmsAdoption NewAdoption(Guid logicalId) =>
        SpaceWmsAdoption.Discover(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "simulator-v1",
            "simulator-v1",
            "Simulated",
            logicalId,
            "EXT-001",
            "WMS-A-01",
            true,
            "1",
            new string('a', 64),
            ObservedAt);
}
