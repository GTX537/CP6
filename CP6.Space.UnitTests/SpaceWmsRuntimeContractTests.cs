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
            "DataSourceId",
            "ObservedAtUtc",
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
    }

    [Fact]
    public void Runtime_service_is_read_only_and_has_separate_inventory_and_task_queries()
    {
        var methods = typeof(ISpaceWmsRuntimeService).GetMethods();

        Assert.Equal(2, methods.Length);
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeInventoryResponse>),
            Assert.Single(
                methods,
                method => method.Name == "QueryInventoryAsync").ReturnType);
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeTaskResponse>),
            Assert.Single(
                methods,
                method => method.Name == "QueryTasksAsync").ReturnType);
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
}
