using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DTOs.Space;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Space;

public class SpaceDataSourceContractTests
{
    [Theory]
    [InlineData(SpaceDataSourceKind.Real, false, true)]
    [InlineData(SpaceDataSourceKind.Simulated, true, true)]
    [InlineData(SpaceDataSourceKind.Unavailable, false, false)]
    public void SourceMetadata_SerializesStableStringAndFlags(
        SpaceDataSourceKind kind,
        bool isSimulated,
        bool isAvailable)
    {
        var observed = new DateTimeOffset(
            2026, 7, 25, 10, 30, 0, TimeSpan.FromHours(8));
        var source = SpaceDataSourceDto.Capture(kind, "TEST_SOURCE", observed);

        var json = JsonSerializer.Serialize(
            source,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains($"\"kind\":\"{kind}\"", json);
        Assert.Equal(isSimulated, source.IsSimulated);
        Assert.Equal(isAvailable, source.IsAvailable);
        Assert.Equal(TimeSpan.Zero, source.ObservedAtUtc.Offset);
    }

    [Fact]
    public void QueryAdapters_DeclareRealAndUnavailableExplicitly()
    {
        using var db = NewDb();

        Assert.Equal(
            SpaceDataSourceKind.Real,
            new WmsStockQuery(db).DataSourceKind);
        Assert.Equal(
            SpaceDataSourceKind.Real,
            new WmsPickTaskQuery(db).DataSourceKind);
        Assert.Equal(
            SpaceDataSourceKind.Real,
            new WmsWorkloadQuery(db).DataSourceKind);
        Assert.Equal(
            SpaceDataSourceKind.Unavailable,
            new StubWmsStockQuery().DataSourceKind);
        Assert.Equal(
            SpaceDataSourceKind.Unavailable,
            new WmsDeviceQuery().DataSourceKind);
    }

    [Fact]
    public void SimulatedAdapter_UsesTheSameMetadataContract()
    {
        var source = new SimulatedDescriptor().CaptureSource(
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"));

        Assert.Equal(SpaceDataSourceKind.Simulated, source.Kind);
        Assert.Equal("SPACE_SIMULATOR", source.DataSourceId);
        Assert.True(source.IsSimulated);
        Assert.True(source.IsAvailable);
    }

    [Theory]
    [InlineData(SpaceDataSourceKind.Real)]
    [InlineData(SpaceDataSourceKind.Simulated)]
    [InlineData(SpaceDataSourceKind.Unavailable)]
    public async Task InventoryAndTaskApis_ExposeEverySourceKind(
        SpaceDataSourceKind kind)
    {
        await using var db = NewDb();
        var queries = new FakeQueries(kind);
        var stock = new SpaceStockController(queries, db);
        var advanced = new SpaceAdvancedController(
            queries,
            queries,
            queries,
            db);

        var inventoryResult = Assert.IsType<OkObjectResult>(
            await stock.FloorStock(Guid.NewGuid(), default));
        var taskResult = Assert.IsType<OkObjectResult>(
            await advanced.PickPath(
                Guid.NewGuid(),
                "TASK-001",
                default));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var expected = $"\"kind\":\"{kind}\"";

        Assert.Contains(expected, JsonSerializer.Serialize(
            inventoryResult.Value,
            options));
        Assert.Contains(expected, JsonSerializer.Serialize(
            taskResult.Value,
            options));
    }

    [Fact]
    public async Task StockApi_UnavailableIsNotIndistinguishableFromEmptyRealStock()
    {
        await using var db = NewDb();
        var controller = new SpaceStockController(
            new StubWmsStockQuery(),
            db);

        var result = Assert.IsType<OkObjectResult>(
            await controller.FloorStock(Guid.NewGuid(), default));
        var json = JsonSerializer.Serialize(
            result.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"items\":[]", json);
        Assert.Contains("\"kind\":\"Unavailable\"", json);
        Assert.Contains("\"isAvailable\":false", json);
        Assert.Contains("\"observedAtUtc\":", json);
    }

    [Fact]
    public void SourceEndpoints_CannotBeHiddenByFieldPolicy()
    {
        Assert.Empty(typeof(SpaceStockController)
            .GetCustomAttributes(typeof(FieldMaskAttribute), true));
        Assert.Empty(typeof(SpaceAdvancedController)
            .GetCustomAttributes(typeof(FieldMaskAttribute), true));

        foreach (var methodName in new[]
                 {
                     nameof(SpaceStockController.FloorStock),
                     nameof(SpaceStockController.Locate),
                     nameof(SpaceAdvancedController.PickPath),
                     nameof(SpaceAdvancedController.Workload),
                     nameof(SpaceAdvancedController.SitePickPath),
                     nameof(SpaceAdvancedController.Devices),
                 })
        {
            var controller = methodName is nameof(SpaceStockController.FloorStock)
                or nameof(SpaceStockController.Locate)
                ? typeof(SpaceStockController)
                : typeof(SpaceAdvancedController);
            var method = controller.GetMethod(methodName);

            Assert.NotNull(method);
            Assert.Empty(method!.GetCustomAttributes(
                typeof(FieldMaskAttribute),
                true));
        }
    }

    [Fact]
    public void ViewerAndExportDtos_AlwaysCarryRuntimeSource()
    {
        var scene = new SceneDto();
        var export = new SceneExportDto();

        Assert.Equal(SpaceDataSourceKind.Real, scene.Source.Kind);
        Assert.Equal("CP6_SPACE_RUNTIME", scene.Source.DataSourceId);
        Assert.Equal(SpaceDataSourceKind.Real, export.Source.Kind);
        Assert.Equal("CP6_SPACE_RUNTIME", export.Source.DataSourceId);
    }

    private static CP6Context NewDb() =>
        new(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class SimulatedDescriptor : ISpaceDataSourceDescriptor
    {
        public SpaceDataSourceKind DataSourceKind =>
            SpaceDataSourceKind.Simulated;

        public string DataSourceId => "SPACE_SIMULATOR";
    }

    private sealed class FakeQueries :
        IWmsStockQuery,
        IWmsPickTaskQuery,
        IWmsWorkloadQuery,
        IWmsDeviceQuery
    {
        public FakeQueries(SpaceDataSourceKind kind)
        {
            DataSourceKind = kind;
        }

        public SpaceDataSourceKind DataSourceKind { get; }

        public string DataSourceId => $"TEST_{DataSourceKind}";

        public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
            IReadOnlyCollection<string> locationCodes,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WmsStockDto>>([]);

        public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
            StockLocateQuery query,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WmsLocationHit>>([]);

        public Task<decimal> GetStockQtyAsync(
            string locationCode,
            string? warehouseCd = null,
            CancellationToken ct = default) =>
            Task.FromResult(0m);

        public Task<PickPathDto> GetPickPathAsync(
            string taskNo,
            CancellationToken ct = default) =>
            Task.FromResult(new PickPathDto
            {
                TaskNo = taskNo,
                Items = [],
            });

        public Task<IReadOnlyList<WorkloadDto>> GetWorkloadAsync(
            Guid floorId,
            DateTime from,
            DateTime to,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkloadDto>>([]);

        public Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(
            Guid floorId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceDto>>([]);
    }
}
