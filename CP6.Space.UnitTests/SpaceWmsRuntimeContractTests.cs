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
        AssertPropertyOrder<SpaceWmsRuntimeInventoryLocateCriteriaDto>(
            "MaterialNumber",
            "LotNumber",
            "ContainerNumber",
            "OwnerId");
        AssertPropertyOrder<SpaceWmsRuntimeInventoryLocateHitDto>(
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
            "MaterialNumbers",
            "LotNumbers",
            "ContainerNumbers",
            "OwnerIds");
        AssertPropertyOrder<SpaceWmsRuntimeInventoryLocateResponse>(
            "SiteId",
            "PublishedVersionId",
            "WarehouseCode",
            "Source",
            "Criteria",
            "LocationCount",
            "FloorCount",
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
        AssertPropertyOrder<SpaceWmsRuntimeTaskFloorDto>(
            "FloorLogicalId",
            "FloorCode",
            "FloorName",
            "FloorLevel",
            "ElevationMillimeters",
            "HeightMillimeters",
            "StopCount",
            "TotalQuantity");
        AssertPropertyOrder<SpaceWmsRuntimeTaskWorkloadDto>(
            "FloorLogicalId",
            "FloorCode",
            "ZoneLogicalId",
            "ZoneCode",
            "StopCount",
            "TotalQuantity");
        AssertPropertyOrder<SpaceWmsRuntimeTaskAisleDto>(
            "FloorLogicalId",
            "ZoneLogicalId",
            "AisleLogicalId",
            "AisleCode",
            "CenterlineJson");
        AssertPropertyOrder<SpaceWmsRuntimeTaskPathResponse>(
            "SiteId",
            "PublishedVersionId",
            "WarehouseCode",
            "Source",
            "TaskId",
            "StopCount",
            "LocatedStopCount",
            "FloorCount",
            "ZoneCount",
            "FloorTransitionCount",
            "ZoneTransitionCount",
            "TotalQuantity",
            "CrossFloor",
            "CrossZone",
            "ActualStops",
            "Floors",
            "Workloads",
            "Aisles");
    }

    [Fact]
    public void Runtime_service_is_read_only_and_has_separate_inventory_and_task_queries()
    {
        var methods = typeof(ISpaceWmsRuntimeService).GetMethods();

        Assert.Equal(4, methods.Length);
        var inventory = Assert.Single(
            methods,
            method => method.Name == "QueryInventoryAsync");
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeInventoryResponse>),
            inventory.ReturnType);
        AssertQueryParameters(inventory);

        var locate = Assert.Single(
            methods,
            method => method.Name == "LocateInventoryAsync");
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeInventoryLocateResponse>),
            locate.ReturnType);
        Assert.Collection(
            locate.GetParameters(),
            parameter =>
            {
                Assert.Equal("siteId", parameter.Name);
                Assert.Equal(typeof(Guid), parameter.ParameterType);
                Assert.False(parameter.IsOptional);
            },
            parameter =>
            {
                Assert.Equal("criteria", parameter.Name);
                Assert.Equal(
                    typeof(SpaceWmsInventoryLocateCriteria),
                    parameter.ParameterType);
                Assert.False(parameter.IsOptional);
            },
            AssertCancellationParameter);

        var tasks = Assert.Single(
            methods,
            method => method.Name == "QueryTasksAsync");
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeTaskResponse>),
            tasks.ReturnType);
        AssertQueryParameters(tasks);

        var taskPath = Assert.Single(
            methods,
            method => method.Name == "GetTaskPathAsync");
        Assert.Equal(
            typeof(Task<SpaceWmsRuntimeTaskPathResponse>),
            taskPath.ReturnType);
        Assert.Collection(
            taskPath.GetParameters(),
            parameter =>
            {
                Assert.Equal("siteId", parameter.Name);
                Assert.Equal(typeof(Guid), parameter.ParameterType);
                Assert.False(parameter.IsOptional);
            },
            parameter =>
            {
                Assert.Equal("taskId", parameter.Name);
                Assert.Equal(typeof(string), parameter.ParameterType);
                Assert.False(parameter.IsOptional);
            },
            AssertCancellationParameter);
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
                AssertCancellationParameter(parameter));
    }

    private static void AssertCancellationParameter(ParameterInfo parameter)
    {
        Assert.Equal("cancellationToken", parameter.Name);
        Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
        Assert.True(parameter.IsOptional);
        Assert.True(parameter.HasDefaultValue);
        Assert.Null(parameter.DefaultValue);
    }
}
