using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceElementRevisionTests
{
    private static readonly Guid TenantId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid VersionId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid FloorLogicalId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Theory]
    [InlineData(SpaceElementTypes.Wall)]
    [InlineData(SpaceElementTypes.Column)]
    [InlineData(SpaceElementTypes.Door)]
    [InlineData(SpaceElementTypes.Dock)]
    [InlineData(SpaceElementTypes.Pallet)]
    [InlineData(SpaceElementTypes.Device)]
    public void Common_element_types_create_with_integer_millimeter_placement(
        string elementType)
    {
        var element = NewElement(elementType);

        element.ConfigurePlacement(
            1200,
            -300,
            0,
            -90,
            800,
            2200,
            400);

        Assert.Equal(elementType, element.ElementType);
        Assert.Equal(1200, element.X);
        Assert.Equal(-300, element.Y);
        Assert.Equal(270, element.RotationZ);
        Assert.Equal(800, element.Width);
        Assert.Equal(2200, element.Height);
        Assert.Equal(400, element.Depth);
    }

    [Fact]
    public void Element_type_is_canonical_and_unknown_types_fail_closed()
    {
        var element = NewElement(" column ");

        Assert.Equal(SpaceElementTypes.Column, element.ElementType);
        Assert.Throws<ArgumentException>(() => NewElement("ForkliftLiveTelemetry"));
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"kind":"point","x":10,"y":-20,"z":0}""")]
    [InlineData("""{"schemaVersion":1,"kind":"path","points":[{"x":0,"y":0},{"x":100,"y":100,"z":5}],"width":20}""")]
    [InlineData("""{"schemaVersion":1,"kind":"polygon","outer":[{"x":0,"y":0},{"x":100,"y":0},{"x":100,"y":100}],"holes":[[{"x":10,"y":10},{"x":20,"y":10},{"x":20,"y":20}]],"height":3000}""")]
    [InlineData(BoxGeometry)]
    [InlineData("""{"schemaVersion":1,"kind":"asset","assetVersionId":"11111111-1111-1111-1111-111111111111","transform":{}}""")]
    public void Frozen_geometry_kinds_accept_versioned_integer_shapes(
        string geometryJson)
    {
        var element = NewElement(SpaceElementTypes.StaticEquipment, geometryJson);

        Assert.Equal(geometryJson, element.GeometryJson);
    }

    [Theory]
    [InlineData("""{"kind":"box","width":1,"height":1,"depth":1}""")]
    [InlineData("""{"schemaVersion":2,"kind":"box","width":1,"height":1,"depth":1}""")]
    [InlineData("""{"schemaVersion":1,"kind":"box","width":0,"height":1,"depth":1}""")]
    [InlineData("""{"schemaVersion":1,"kind":"path","points":[{"x":0,"y":0}],"width":1}""")]
    [InlineData("""{"schemaVersion":1,"kind":"polygon","outer":[{"x":0,"y":0},{"x":1,"y":0},{"x":1,"y":1}],"holes":[[]],"height":1}""")]
    [InlineData("""{"schemaVersion":1,"kind":"asset","assetVersionId":"00000000-0000-0000-0000-000000000000","transform":{}}""")]
    [InlineData("""{"schemaVersion":1,"kind":"unknown"}""")]
    public void Invalid_or_unknown_geometry_fails_closed(string geometryJson)
    {
        Assert.Throws<ArgumentException>(
            () => NewElement(SpaceElementTypes.Wall, geometryJson));
    }

    [Fact]
    public void Geometry_update_uses_the_same_versioned_schema_validation()
    {
        var element = NewElement(SpaceElementTypes.Column);
        var updated =
            """{"schemaVersion":1,"kind":"polygon","outer":[{"x":0,"y":0},{"x":100,"y":0},{"x":100,"y":100}],"holes":[],"height":3000}""";

        element.UpdateGeometry(updated);

        Assert.Equal(updated, element.GeometryJson);
        Assert.Throws<ArgumentException>(
            () => element.UpdateGeometry("""{"schemaVersion":99,"kind":"point"}"""));
    }

    [Theory]
    [InlineData("string", "  label  ", "String", "label")]
    [InlineData("integer", "0042", "Integer", "42")]
    [InlineData("decimal", "10.500", "Decimal", "10.5")]
    [InlineData("boolean", "TRUE", "Boolean", "true")]
    [InlineData("guid", "11111111-1111-1111-1111-111111111111", "Guid",
        "11111111-1111-1111-1111-111111111111")]
    [InlineData("json", """ { "enabled": true } """, "Json", """{"enabled":true}""")]
    public void Attribute_values_are_type_checked_and_canonicalized(
        string requestedType,
        string requestedValue,
        string expectedType,
        string expectedValue)
    {
        var attribute = SpaceElementAttribute.Create(
            TenantId,
            NewElement(SpaceElementTypes.Device),
            SpaceElementAttributeNamespaces.Manufacturer,
            "value",
            requestedType,
            requestedValue);

        Assert.Equal(expectedType, attribute.ValueType);
        Assert.Equal(expectedValue, attribute.Value);
    }

    [Fact]
    public void Date_time_attribute_is_normalized_to_utc()
    {
        var attribute = SpaceElementAttribute.Create(
            TenantId,
            NewElement(SpaceElementTypes.Device),
            SpaceElementAttributeNamespaces.Design,
            "inspectedAt",
            SpaceElementAttributeValueTypes.DateTime,
            "2026-07-30T10:20:30-04:00");

        var actual = DateTimeOffset.Parse(attribute.Value!);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-30T14:20:30Z"),
            actual);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    [Fact]
    public void Attribute_update_uses_the_same_value_validation()
    {
        var attribute = SpaceElementAttribute.Create(
            TenantId,
            NewElement(SpaceElementTypes.Device),
            SpaceElementAttributeNamespaces.Design,
            "count",
            SpaceElementAttributeValueTypes.Integer,
            "1");

        attribute.UpdateValue("decimal", "2.500", "kg");

        Assert.Equal(SpaceElementAttributeValueTypes.Decimal, attribute.ValueType);
        Assert.Equal("2.5", attribute.Value);
        Assert.Equal("kg", attribute.Unit);
        Assert.Throws<ArgumentException>(
            () => attribute.UpdateValue("Boolean", "not-a-boolean"));
    }

    [Fact]
    public void Attribute_units_are_limited_to_numeric_values()
    {
        var element = NewElement(SpaceElementTypes.Pallet);
        var numeric = SpaceElementAttribute.Create(
            TenantId,
            element,
            SpaceElementAttributeNamespaces.Design,
            "maxLoad",
            SpaceElementAttributeValueTypes.Decimal,
            "1250.0",
            "kg");

        Assert.Equal("1250", numeric.Value);
        Assert.Equal("kg", numeric.Unit);
        Assert.Throws<ArgumentException>(
            () => SpaceElementAttribute.Create(
                TenantId,
                element,
                SpaceElementAttributeNamespaces.Design,
                "label",
                SpaceElementAttributeValueTypes.String,
                "A",
                "kg"));
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("inventory.quantity")]
    [InlineData("stock")]
    [InlineData("stock.available")]
    [InlineData("task")]
    [InlineData("task.state")]
    [InlineData("runtime")]
    [InlineData("runtime.inventory")]
    public void Runtime_inventory_and_task_namespaces_are_rejected(
        string attributeNamespace)
    {
        var element = NewElement(SpaceElementTypes.Device);

        Assert.Throws<ArgumentException>(
            () => SpaceElementAttribute.Create(
                TenantId,
                element,
                attributeNamespace,
                "state",
                SpaceElementAttributeValueTypes.String,
                "live-value"));
    }

    [Theory]
    [InlineData(SpaceElementAttributeNamespaces.Owner)]
    [InlineData(SpaceElementAttributeNamespaces.Lot)]
    [InlineData(SpaceElementAttributeNamespaces.Container)]
    [InlineData(SpaceElementAttributeNamespaces.Manufacturer)]
    [InlineData(SpaceElementAttributeNamespaces.ExternalReference)]
    public void Frozen_design_and_external_reference_namespaces_are_available(
        string attributeNamespace)
    {
        var attribute = SpaceElementAttribute.Create(
            TenantId,
            NewElement(SpaceElementTypes.Pallet),
            attributeNamespace,
            "reference",
            SpaceElementAttributeValueTypes.String,
            "A");

        Assert.Equal(attributeNamespace, attribute.Namespace);
    }

    [Fact]
    public void Attribute_creation_rejects_cross_tenant_elements()
    {
        var element = NewElement(SpaceElementTypes.Device);

        Assert.Throws<SpaceTenantScopeException>(
            () => SpaceElementAttribute.Create(
                Guid.NewGuid(),
                element,
                SpaceElementAttributeNamespaces.Design,
                "label",
                SpaceElementAttributeValueTypes.String,
                "A"));
    }

    [Fact]
    public void Element_cannot_parent_itself()
    {
        var logicalId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(
            () => SpaceElementRevision.Create(
                TenantId,
                VersionId,
                logicalId,
                FloorLogicalId,
                SpaceElementTypes.Device,
                BoxGeometry,
                logicalId));
    }

    private static SpaceElementRevision NewElement(
        string elementType,
        string geometryJson = BoxGeometry) =>
        SpaceElementRevision.Create(
            TenantId,
            VersionId,
            Guid.NewGuid(),
            FloorLogicalId,
            elementType,
            geometryJson);

    private const string BoxGeometry =
        """{"schemaVersion":1,"kind":"box","width":800,"height":2200,"depth":400}""";
}
