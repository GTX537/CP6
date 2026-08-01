using System.Reflection;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceWmsRuntimeContractTests
{
    [Fact]
    public void Public_runtime_contracts_expose_source_inventory_and_task_shapes()
    {
        AssertPropertyOrder<SpaceWmsRuntimeSourceDto>(
            "Kind",
            "AdapterId",
            "DataSourceId",
            "ObservedAtUtc",
            "ReceivedAtUtc",
            "DelayMilliseconds",
            "ClockSkewMilliseconds",
            "IsSimulated",
            "IsAvailable");
        AssertPropertyOrder<SpaceWmsRuntimeInventoryItemDto>(
            "LocationLogicalId",
            "WmsLogicalId",
            "SpaceLocationCode",
            "WmsLocationCode",
            "CodeMatches",
            "FloorLogicalId",
            "FloorCode",
            "FloorName",
            "FloorLevel",
            "PhysicalQuantity",
            "AllocatedQuantity",
            "MaterialNumber",
            "LotNumber",
            "ContainerNumber",
            "OwnerId");
        AssertPropertyOrder<SpaceWmsRuntimeInventoryResponse>(
            "SiteId",
            "PublishedVersionId",
            "WarehouseCode",
            "Source",
            "Items");
        AssertPropertyOrder<SpaceWmsRuntimeTaskItemDto>(
            "TaskId",
            "TaskType",
            "Status",
            "SequenceNo",
            "LocationLogicalId",
            "WmsLogicalId",
            "SpaceLocationCode",
            "WmsLocationCode",
            "CodeMatches",
            "FloorLogicalId",
            "FloorCode",
            "FloorName",
            "FloorLevel",
            "ZoneLogicalId",
            "ZoneCode",
            "RackLogicalId",
            "RackCode",
            "AnchorXMillimeters",
            "AnchorYMillimeters",
            "AnchorZMillimeters",
            "Quantity",
            "MaterialNumber");
        AssertPropertyOrder<SpaceWmsRuntimeTaskResponse>(
            "SiteId",
            "PublishedVersionId",
            "WarehouseCode",
            "Source",
            "Items");
    }

    [Fact]
    public void Runtime_service_is_read_only_and_has_separate_inventory_and_task_queries()
    {
        var methods = typeof(ISpaceWmsRuntimeService).GetMethods();

        Assert.Equal(2, methods.Length);
        var inventory = Assert.Single(
            methods,
            method => method.Name == "QueryInventoryAsync");
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeInventoryResponse>),
            inventory.ReturnType);
        AssertQueryParameters(inventory);

        var tasks = Assert.Single(
            methods,
            method => method.Name == "QueryTasksAsync");
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeTaskResponse>),
            tasks.ReturnType);
        AssertQueryParameters(tasks);
        Assert.Equal(
            "SPACE_WMS_RUNTIME_CONTRACT_VIOLATION",
            SpaceErrorCodes.WmsRuntimeContractViolation);
    }

    private static void AssertPropertyOrder<T>(params string[] expected)
    {
        var actual = typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static void AssertQueryParameters(MethodInfo method)
    {
        var parameters = method.GetParameters();

        Assert.Collection(
            parameters,
            parameter =>
            {
                Assert.Equal("siteId", parameter.Name);
                Assert.Equal(typeof(Guid), parameter.ParameterType);
                Assert.False(parameter.IsOptional);
            },
            parameter =>
            {
                Assert.Equal("locationLogicalIds", parameter.Name);
                Assert.Equal(typeof(IReadOnlyCollection<Guid>), parameter.ParameterType);
                Assert.True(parameter.IsOptional);
                Assert.True(parameter.HasDefaultValue);
                Assert.Null(parameter.DefaultValue);
            },
            parameter =>
            {
                Assert.Equal("cancellationToken", parameter.Name);
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.True(parameter.IsOptional);
                Assert.True(parameter.HasDefaultValue);
                Assert.Null(parameter.DefaultValue);
            });
    }
}
