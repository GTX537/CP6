# E08-S01 Unified Runtime Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one read-only Published runtime boundary that returns identical inventory and task response contracts for the CP6 WMS adapter and the standard simulator.

**Architecture:** Keep `ISpaceWmsRuntimeSource` as the low-level WMS-shaped read contract. Add `ISpaceWmsRuntimeService` above it to enforce tenant/site/Published scope, map E07-S05 adopted identities, query in bounded chunks, validate source results fail-closed, and expose two Design V1 HTTP endpoints through a focused controller.

**Tech Stack:** .NET 8, C# records, ASP.NET Core controllers and authorization attributes, EF Core 8, xUnit, NSwag-generated C# and TypeScript clients.

---

## Scope and file map

The approved design is `docs/superpowers/specs/2026-07-31-space-e08-s01-unified-runtime-source-design.md`.

Create these focused files:

- `CP6.Space.Contracts/SpaceWmsRuntimeContracts.cs` — public source, inventory, and task response DTOs.
- `CP6.Space.Application/SpaceWmsRuntime.cs` — read-only application service interface.
- `CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs` — Published scope resolution, identity mapping, chunking, contract validation, and DTO projection.
- `CP6.WebApi/Controllers/Space/SpaceWmsRuntimeController.cs` — two read-only Design V1 endpoints.
- `CP6.Space.UnitTests/SpaceWmsRuntimeContractTests.cs` — public shape and error-code freeze.
- `CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs` — service behavior against a recording source and the standard simulator.
- `docs/space/reports/e08-s01-unified-runtime-source.md` — delivery evidence.

Modify these existing files:

- `CP6.Space.Contracts/SpaceErrorCodes.cs:65` — add the runtime contract-violation code beside WMS errors.
- `CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs:122-125` — register the scoped runtime service without replacing the production adapter.
- `CP6.Space.IntegrationTests/StandardSpaceWmsSimulatorTests.cs:348-378` — freeze DI selection.
- `CP6.Tests/Space/SpacePermissionAttributeTests.cs:45-64,105-109` — recognize the new controller and read permissions.
- `CP6.Tests/Space/SpaceDesignV1OpenApiTests.cs:19-70` — freeze paths, query parameters, and DTO schemas.
- `docs/space/contracts/design-v1.openapi.json` — regenerate the OpenAPI artifact.
- `CP6.Space.Client/SpaceDesignV1Client.g.cs` — regenerate the C# client.
- `sdk/typescript/space-design-v1/spaceDesignV1Client.ts` — regenerate the TypeScript client.
- `docs/project-memory/PROJECT_STATE.md:5-10` — record completion and advance the next card to E08-S02.

Do not change EF entities, `SpaceContext` mappings, migrations, Design Revision data, Published materialization, or the existing legacy Viewer endpoints.

### Task 1: Freeze public runtime contracts and application interface

**Files:**

- Create: `CP6.Space.UnitTests/SpaceWmsRuntimeContractTests.cs`
- Create: `CP6.Space.Contracts/SpaceWmsRuntimeContracts.cs`
- Create: `CP6.Space.Application/SpaceWmsRuntime.cs`
- Modify: `CP6.Space.Contracts/SpaceErrorCodes.cs:65`

- [ ] **Step 1: Write the failing contract tests**

Create `CP6.Space.UnitTests/SpaceWmsRuntimeContractTests.cs` with tests that freeze the public property names and the two service methods:

```csharp
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceWmsRuntimeContractTests
{
    [Fact]
    public void Public_runtime_contracts_expose_source_inventory_and_task_shapes()
    {
        Assert.Equal(
            ["Kind", "DataSourceId", "ObservedAtUtc", "IsSimulated", "IsAvailable"],
            Properties<SpaceWmsRuntimeSourceDto>());
        Assert.Equal(
            [
                "LocationLogicalId", "WmsLogicalId", "SpaceLocationCode",
                "WmsLocationCode", "CodeMatches", "FloorLogicalId", "FloorCode",
                "FloorName", "FloorLevel", "PhysicalQuantity", "AllocatedQuantity",
                "MaterialNumber", "LotNumber", "ContainerNumber", "OwnerId",
            ],
            Properties<SpaceWmsRuntimeInventoryItemDto>());
        Assert.Equal(
            [
                "TaskId", "TaskType", "Status", "SequenceNo", "LocationLogicalId",
                "WmsLogicalId", "SpaceLocationCode", "WmsLocationCode", "CodeMatches",
                "FloorLogicalId", "FloorCode", "FloorName", "FloorLevel",
                "ZoneLogicalId", "ZoneCode", "RackLogicalId", "RackCode",
                "AnchorXMillimeters", "AnchorYMillimeters", "AnchorZMillimeters",
                "Quantity", "MaterialNumber",
            ],
            Properties<SpaceWmsRuntimeTaskItemDto>());
    }

    [Fact]
    public void Runtime_service_is_read_only_and_has_separate_inventory_and_task_queries()
    {
        var methods = typeof(ISpaceWmsRuntimeService).GetMethods();

        Assert.Equal(2, methods.Length);
        Assert.Contains(methods, value =>
            value.Name == nameof(ISpaceWmsRuntimeService.QueryInventoryAsync) &&
            value.ReturnType == typeof(Task<SpaceWmsRuntimeInventoryResponse>));
        Assert.Contains(methods, value =>
            value.Name == nameof(ISpaceWmsRuntimeService.QueryTasksAsync) &&
            value.ReturnType == typeof(Task<SpaceWmsRuntimeTaskResponse>));
        Assert.Equal(
            "SPACE_WMS_RUNTIME_CONTRACT_VIOLATION",
            SpaceErrorCodes.WmsRuntimeContractViolation);
    }

    private static string[] Properties<T>() =>
        typeof(T).GetProperties().Select(value => value.Name).ToArray();
}
```

- [ ] **Step 2: Run the test to verify RED**

Run:

```powershell
dotnet test CP6.Space.UnitTests/CP6.Space.UnitTests.csproj -c Release --filter FullyQualifiedName~SpaceWmsRuntimeContractTests
```

Expected: compilation fails with `CS0246`/`CS0117` because the runtime DTOs, service interface, and error constant do not exist.

- [ ] **Step 3: Add the exact public DTOs**

Create `CP6.Space.Contracts/SpaceWmsRuntimeContracts.cs`:

```csharp
namespace CP6.Space.Contracts;

public sealed record SpaceWmsRuntimeSourceDto(
    string Kind,
    string DataSourceId,
    DateTimeOffset ObservedAtUtc,
    bool IsSimulated,
    bool IsAvailable);

public sealed record SpaceWmsRuntimeInventoryItemDto(
    Guid LocationLogicalId,
    Guid WmsLogicalId,
    string SpaceLocationCode,
    string WmsLocationCode,
    bool CodeMatches,
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    decimal PhysicalQuantity,
    decimal AllocatedQuantity,
    string? MaterialNumber,
    string? LotNumber,
    string? ContainerNumber,
    string? OwnerId = null);

public sealed record SpaceWmsRuntimeInventoryResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsRuntimeSourceDto Source,
    IReadOnlyList<SpaceWmsRuntimeInventoryItemDto> Items);

public sealed record SpaceWmsRuntimeTaskItemDto(
    string TaskId,
    string TaskType,
    string Status,
    int SequenceNo,
    Guid LocationLogicalId,
    Guid WmsLogicalId,
    string SpaceLocationCode,
    string WmsLocationCode,
    bool CodeMatches,
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    Guid? RackLogicalId,
    string? RackCode,
    double? AnchorXMillimeters,
    double? AnchorYMillimeters,
    double? AnchorZMillimeters,
    decimal? Quantity,
    string? MaterialNumber);

public sealed record SpaceWmsRuntimeTaskResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsRuntimeSourceDto Source,
    IReadOnlyList<SpaceWmsRuntimeTaskItemDto> Items);
```

Add this constant directly after `SpaceErrorCodes.WmsUnavailable`:

```csharp
public const string WmsRuntimeContractViolation =
    "SPACE_WMS_RUNTIME_CONTRACT_VIOLATION";
```

- [ ] **Step 4: Add the read-only application interface**

Create `CP6.Space.Application/SpaceWmsRuntime.cs`:

```csharp
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceWmsRuntimeService
{
    Task<SpaceWmsRuntimeInventoryResponse> QueryInventoryAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds = null,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Run the contract tests to verify GREEN**

Run the Step 2 command again.

Expected: `2 passed, 0 failed`.

- [ ] **Step 6: Commit the contract slice**

```powershell
git add CP6.Space.Contracts/SpaceWmsRuntimeContracts.cs CP6.Space.Contracts/SpaceErrorCodes.cs CP6.Space.Application/SpaceWmsRuntime.cs CP6.Space.UnitTests/SpaceWmsRuntimeContractTests.cs
git commit -m "feat(space): define E08 runtime response contracts"
```

### Task 2: Implement Published scope, identity mapping, and happy-path queries

**Files:**

- Create: `CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs`
- Create: `CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs`

- [ ] **Step 1: Write failing happy-path, scope, and chunking tests**

Create `CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs`. Start with these named cases:

```csharp
[Fact]
public async Task Simulator_inventory_and_tasks_map_adopted_identity_and_spatial_context()
{
    var execution = Execution();
    var clock = new TestClock();
    await using var context = NewContext(execution, clock);
    var seeded = await SeedPublishedAsync(context, "NATIVE-01", "ADOPTED-01");
    var simulator = new StandardSpaceWmsSimulator();
    var adoptedWmsId = Guid.NewGuid();
    var adoption = SpaceWmsAdoption.Discover(
        execution.TenantId,
        seeded.SiteId,
        simulator.RuntimeAdapterId,
        simulator.RuntimeDataSourceId,
        adoptedWmsId,
        "external-adopted-01",
        "ADOPTED-01",
        true,
        "1",
        Hash,
        Now);
    adoption.Bind(seeded.PublishedVersionId, seeded.LocationIds[1], Now);
    context.WmsAdoptions.Add(adoption);
    await context.SaveChangesAsync();
    var wms = WmsContext(execution, seeded.SiteId);
    simulator.SeedInventory(wms,
    [
        new(seeded.LocationIds[0], "NATIVE-01", 10, 2, "SKU-A", "LOT-A", null),
        new(adoptedWmsId, "ADOPTED-01", 7, 3, "SKU-B", "LOT-B", "PALLET-B"),
    ]);
    simulator.SeedTasks(wms,
    [
        new("PICK-001", "Pick", "Released", 1, adoptedWmsId,
            "ADOPTED-01", 3, "SKU-B"),
    ]);
    var service = CreateService(context, execution, clock, seeded.SiteId, simulator);

    var inventory = await service.QueryInventoryAsync(seeded.SiteId);
    var tasks = await service.QueryTasksAsync(seeded.SiteId);

    Assert.Equal("Simulated", inventory.Source.Kind);
    Assert.True(inventory.Source.IsSimulated);
    Assert.Equal(2, inventory.Items.Count);
    var adopted = Assert.Single(inventory.Items,
        value => value.WmsLogicalId == adoptedWmsId);
    Assert.Equal(seeded.LocationIds[1], adopted.LocationLogicalId);
    Assert.True(adopted.CodeMatches);
    var task = Assert.Single(tasks.Items);
    Assert.Equal(seeded.LocationIds[1], task.LocationLogicalId);
    Assert.Equal(adoptedWmsId, task.WmsLogicalId);
    Assert.NotNull(task.AnchorXMillimeters);
    Assert.NotNull(task.AnchorYMillimeters);
    Assert.NotNull(task.AnchorZMillimeters);
}

[Fact]
public async Task Inventory_and_task_queries_use_500_item_chunks()
{
    var execution = Execution();
    var clock = new TestClock();
    await using var context = NewContext(execution, clock);
    var seeded = await SeedPublishedAsync(
        context,
        Enumerable.Range(1, 1_001).Select(value => $"L-{value:0000}").ToArray());
    var source = new RecordingRuntimeSource();
    var service = CreateService(context, execution, clock, seeded.SiteId, source);

    await service.QueryInventoryAsync(seeded.SiteId);
    await service.QueryTasksAsync(seeded.SiteId);

    Assert.Equal([500, 500, 1], source.InventoryBatchSizes);
    Assert.Equal([500, 500, 1], source.TaskBatchSizes);
}

[Fact]
public async Task Requested_location_must_be_active_in_current_published_version()
{
    var execution = Execution();
    var clock = new TestClock();
    await using var context = NewContext(execution, clock);
    var seeded = await SeedPublishedAsync(context, "L-001");
    var source = new RecordingRuntimeSource();
    var service = CreateService(context, execution, clock, seeded.SiteId, source);

    var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
        service.QueryTasksAsync(seeded.SiteId, [Guid.NewGuid()]));

    Assert.Equal(404, error.StatusCode);
    Assert.Equal(SpaceErrorCodes.LogicalIdNotFound, error.Code);
    Assert.Empty(source.TaskBatchSizes);
}
```

The same test file must define deterministic helpers with these exact roles:

```csharp
private static readonly DateTime Now =
    new(2026, 7, 31, 16, 0, 0, DateTimeKind.Utc);
private static readonly string Hash = new('a', 64);

private static TestExecutionContext Execution() =>
    new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

private static SpaceWmsRuntimeService CreateService(
    SpaceContext context,
    TestExecutionContext execution,
    TestClock clock,
    Guid siteId,
    ISpaceWmsRuntimeSource source,
    TestAccessEvaluator? access = null) =>
    new(
        context,
        execution,
        clock,
        access ?? new TestAccessEvaluator(siteId),
        new TestWarehouseResolver(siteId),
        source);

private sealed record SeededPublished(
    Guid SiteId,
    Guid PublishedVersionId,
    IReadOnlyList<Guid> LocationIds);

private static async Task<SeededPublished> SeedPublishedAsync(
    SpaceContext context,
    params string[] locationCodes)
{
    var tenantId = context.CurrentTenantId;
    var siteId = Guid.NewGuid();
    var model = SpaceModel.Create(tenantId, siteId);
    var version = SpaceModelVersion.CreateDraft(
        tenantId, model.Id, 1, "Published runtime");
    var floorLogicalId = Guid.NewGuid();
    var zoneLogicalId = Guid.NewGuid();
    var rackLogicalId = Guid.NewGuid();
    var floor = SpaceFloorRevision.Create(
        tenantId, version.Id, floorLogicalId, siteId, 1,
        "F1", "Floor 1", height: 5_000);
    var zone = SpaceZoneRevision.Create(
        tenantId, version.Id, zoneLogicalId, floorLogicalId,
        "STORAGE", zoneType: 1);
    var rack = SpaceRackRevision.Create(
        tenantId, version.Id, rackLogicalId, floorLogicalId,
        zoneLogicalId, "RACK-01");
    rack.ConfigureGeometry(
        0, 0, 0, 0,
        width: Math.Max(1, locationCodes.Length) * 1_000,
        depth: 1_100,
        height: 4_000);
    var level = SpaceRackLevelRevision.Create(
        tenantId, version.Id, Guid.NewGuid(), rackLogicalId,
        levelNo: 1,
        bottomZ: 0,
        clearHeight: 1_200,
        binCount: Math.Max(1, locationCodes.Length),
        depthCount: 1,
        cellWidth: 1_000,
        cellDepth: 1_100);
    var locationIds = locationCodes.Select(_ => Guid.NewGuid()).ToArray();
    var locations = locationIds.Select((logicalId, index) =>
        SpaceLocationRevision.Create(
            tenantId, version.Id, logicalId, floorLogicalId,
            rackLogicalId, locationCodes[index],
            columnNo: index + 1,
            levelNo: 1,
            depthNo: 1,
            width: 1_000,
            height: 1_200,
            depth: 1_100)).ToArray();

    context.AddRange(model, version, floor, zone, rack, level);
    context.AddRange(locations);
    await context.SaveChangesAsync();
    version.BeginValidation();
    version.MarkReady(Hash, "space-v1", Hash);
    version.BeginPublishing();
    version.MarkPublished(Guid.NewGuid(), Now);
    model.BeginCutover(Guid.NewGuid());
    model.MarkFrozen();
    model.MarkBootstrapping();
    model.MarkVerified(version);
    model.ActivateDesignV1();
    await context.SaveChangesAsync();
    return new SeededPublished(siteId, version.Id, locationIds);
}

private static SpaceContext NewContext(
    ISpaceExecutionContext execution,
    ISpaceClock clock) =>
    new(
        new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options,
        execution,
        clock);

private static SpaceWmsContext WmsContext(
    TestExecutionContext execution,
    Guid siteId) =>
    new(execution.TenantId, siteId, "WH1", execution.CorrelationId);

private sealed record TestExecutionContext(
    Guid TenantId,
    Guid ActorId,
    Guid CorrelationId) :
    ISpaceExecutionContext,
    ISpaceCorrelationContext;

private sealed class TestClock : ISpaceClock
{
    public DateTime UtcNow => Now;
}

private sealed class TestAccessEvaluator(Guid allowedSiteId) :
    ISpaceDesignAccessEvaluator
{
    public List<bool> Writes { get; } = [];

    public void EnsureSiteAccess(Guid siteId, bool write)
    {
        if (siteId != allowedSiteId)
            throw new InvalidOperationException("Site access denied.");
        Writes.Add(write);
    }
}

private sealed class TestWarehouseResolver(Guid siteId) :
    ISpaceWarehouseResolver
{
    public Task<SpaceWarehouseIdentity?> ResolveAsync(
        Guid requestedSiteId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SpaceWarehouseIdentity?>(
            requestedSiteId == siteId
                ? new SpaceWarehouseIdentity(siteId, "SITE", "WH1")
                : null);
}
```

Add this recording source; Task 3 will extend it with injected failures and per-call provenance:

```csharp
private sealed class RecordingRuntimeSource : ISpaceWmsRuntimeSource
{
    public string RuntimeAdapterId => "recording-wms-v1";
    public string RuntimeDataSourceId => "RECORDING_WMS";
    public SpaceWmsDataSourceKind RuntimeDataSourceKind =>
        SpaceWmsDataSourceKind.Real;
    public List<int> InventoryBatchSizes { get; } = [];
    public List<int> TaskBatchSizes { get; } = [];
    public IReadOnlyList<SpaceWmsInventoryItem> InventoryItems { get; init; } = [];
    public IReadOnlyList<SpaceWmsTaskItem> TaskItems { get; init; } = [];

    public Task<SpaceWmsInventoryResult> QueryInventoryAsync(
        SpaceWmsInventoryQuery request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        InventoryBatchSizes.Add(request.LogicalIds.Count);
        var requested = request.LogicalIds.ToHashSet();
        return Task.FromResult(new SpaceWmsInventoryResult(
            Source(),
            InventoryItems.Where(value => requested.Contains(value.LogicalId))
                .ToArray()));
    }

    public Task<SpaceWmsTaskResult> QueryTasksAsync(
        SpaceWmsTaskQuery request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        TaskBatchSizes.Add(request.LogicalIds.Count);
        var requested = request.LogicalIds.ToHashSet();
        return Task.FromResult(new SpaceWmsTaskResult(
            Source(),
            TaskItems.Where(value => requested.Contains(value.LogicalId))
                .ToArray()));
    }

    private static SpaceWmsSourceMetadata Source() =>
        new(
            SpaceWmsDataSourceKind.Real,
            "RECORDING_WMS",
            new DateTimeOffset(Now));
}
```

- [ ] **Step 2: Run the focused tests to verify RED**

Run:

```powershell
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SpaceWmsRuntimeServiceTests
```

Expected: compilation fails because `SpaceWmsRuntimeService` does not exist.

- [ ] **Step 3: Implement the service happy path**

Create `CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs`. Use this exact class header, constants, dependency fields, and constructor:

```csharp
public sealed class SpaceWmsRuntimeService : ISpaceWmsRuntimeService
{
    private const int QueryChunkSize = 500;
    private const int MaxLocationCount = 10_000;

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceWarehouseResolver _warehouses;
    private readonly ISpaceWmsRuntimeSource _source;

    public SpaceWmsRuntimeService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceWarehouseResolver warehouses,
        ISpaceWmsRuntimeSource source)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _warehouses = warehouses;
        _source = source;
    }
}
```

Implement the two interface methods with their exact signatures from Task 1. Each method first calls `LoadScopeAsync`, iterates `scope.WmsLogicalIds.Chunk(QueryChunkSize)`, and projects through `scope.LocationByWmsLogicalId`. Use these exact item projections:

```csharp
items.Add(new SpaceWmsRuntimeInventoryItemDto(
    location.SpaceLogicalId,
    item.LogicalId,
    location.SpaceLocationCode,
    item.LocationCode,
    string.Equals(
        location.SpaceLocationCode,
        item.LocationCode,
        StringComparison.Ordinal),
    location.FloorLogicalId,
    location.FloorCode,
    location.FloorName,
    location.FloorLevel,
    item.PhysicalQuantity,
    item.AllocatedQuantity,
    item.MaterialNumber,
    item.LotNumber,
    item.ContainerNumber,
    item.OwnerId));

items.Add(new SpaceWmsRuntimeTaskItemDto(
    item.TaskId,
    item.TaskType,
    item.Status,
    item.SequenceNo,
    location.SpaceLogicalId,
    item.LogicalId,
    location.SpaceLocationCode,
    item.LocationCode,
    string.Equals(
        location.SpaceLocationCode,
        item.LocationCode,
        StringComparison.Ordinal),
    location.FloorLogicalId,
    location.FloorCode,
    location.FloorName,
    location.FloorLevel,
    location.ZoneLogicalId,
    location.ZoneCode,
    location.RackLogicalId,
    location.RackCode,
    location.AnchorXMillimeters,
    location.AnchorYMillimeters,
    location.AnchorZMillimeters,
    item.Quantity,
    item.MaterialNumber));
```

For the happy-path commit, retain the first observed source with
`observedSource ??= result.Source`; Task 3 replaces that assignment with strict
per-chunk merging. Convert source metadata with:

```csharp
private static SpaceWmsRuntimeSourceDto ToDto(
    SpaceWmsSourceMetadata source) =>
    new(
        source.Kind.ToString(),
        source.DataSourceId,
        source.ObservedAtUtc.ToUniversalTime(),
        source.IsSimulated,
        source.IsAvailable);
```

End the file with these exact private records:

```csharp
private sealed record RuntimeLocation(
    Guid SpaceLogicalId,
    Guid WmsLogicalId,
    string SpaceLocationCode,
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    Guid? RackLogicalId,
    string? RackCode,
    double? AnchorXMillimeters,
    double? AnchorYMillimeters,
    double? AnchorZMillimeters);

private sealed record RuntimeScope(
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsContext WmsContext,
    IReadOnlyList<RuntimeLocation> Locations,
    IReadOnlyList<Guid> WmsLogicalIds,
    IReadOnlyDictionary<Guid, RuntimeLocation> LocationByWmsLogicalId);
```

`LoadScopeAsync` must execute in this order:

1. Validate non-empty execution Tenant ID and Actor ID, non-empty Site ID, non-empty requested IDs, and the 10,000 limit.
2. Call `_access.EnsureSiteAccess(siteId, write: false)` before querying site data.
3. Load `SpaceModel` by Site ID; throw `SPACE_MODEL_NOT_FOUND`/404 if absent and `SPACE_VERSION_STATE_INVALID`/409 if `CurrentPublishedVersionId` is null.
4. Load only Active `LocationRevisions` from that Published Version. Reject any requested ID not returned with `SPACE_LOGICAL_ID_NOT_FOUND`/404.
5. Load Active floor, rack, zone, and rack-level data from that same version. A rack-backed location must have a consistent active chain.
6. Calculate anchors using radians from rack Rotation Z:

```csharp
var localX = (columnNo - 0.5d) * rackLevel.CellWidth;
var localY = (depthNo - 0.5d) * rackLevel.CellDepth;
var anchorX = rack.X + localX * Math.Cos(radians) - localY * Math.Sin(radians);
var anchorY = rack.Y + localX * Math.Sin(radians) + localY * Math.Cos(radians);
var anchorZ = rack.Z + rackLevel.BottomZ +
    rackLevel.BeamHeight + rackLevel.ClearHeight / 2d;
```

7. Load WMS adoptions for the Site ID and `_source.RuntimeAdapterId` where `LocationLogicalId` is non-null. Use the adoption WMS Logical ID for selected bound locations and the Space Logical ID otherwise.
8. Reject multiple bindings for one Space ID or one WMS ID mapped by multiple Space locations with `SPACE_WMS_ADOPTION_DUPLICATE`/409.
9. Resolve the warehouse and create `SpaceWmsContext` from execution Tenant ID, Site ID, Warehouse Code, and the existing correlation ID when present.

Use these exact Problem Details helpers for scope validation; Task 3 adds the
502 and 503 helpers:

```csharp
private static SpaceProblemException Invalid(
    string field,
    string detail) =>
    new(
        SpaceErrorCodes.RequestInvalid,
        400,
        "The runtime query is invalid.",
        $"{field}: {detail}",
        "correct-request");

private static SpaceProblemException NotFound(
    string code,
    string title) =>
    new(code, 404, title, recoveryAction: "verify-resource");

private static SpaceProblemException Conflict(
    string code,
    string title,
    string recoveryAction) =>
    new(code, 409, title, recoveryAction: recoveryAction);
```

- [ ] **Step 4: Run the focused tests to verify GREEN**

Run the Step 2 command again.

Expected: all tests in `SpaceWmsRuntimeServiceTests` pass; the chunk assertion is exactly `[500, 500, 1]` for inventory and tasks.

- [ ] **Step 5: Run existing adapter and simulator regressions**

Run:

```powershell
dotnet test CP6.Space.UnitTests/CP6.Space.UnitTests.csproj -c Release --filter FullyQualifiedName~SpaceWmsAdapterContractTests
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Cp6SpaceWmsAdapterTests|FullyQualifiedName~StandardSpaceWmsSimulatorTests"
```

Expected: both commands pass with no new skips.

- [ ] **Step 6: Commit the service happy path**

```powershell
git add CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs
git commit -m "feat(space): query Published inventory and tasks"
```

### Task 3: Add fail-closed provenance and error semantics

**Files:**

- Modify: `CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs`
- Modify: `CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs`

- [ ] **Step 1: Write failing provenance and failure tests**

Add these cases to `SpaceWmsRuntimeServiceTests`:

```csharp
[Fact]
public async Task Unavailable_source_returns_empty_with_explicit_flags()
{
    await using var fixture = await RuntimeFixture.CreateAsync("L-001");
    fixture.Source.DeclaredKind = SpaceWmsDataSourceKind.Unavailable;

    var result = await fixture.Service.QueryInventoryAsync(fixture.SiteId);

    Assert.Empty(result.Items);
    Assert.Equal("Unavailable", result.Source.Kind);
    Assert.False(result.Source.IsAvailable);
    Assert.False(result.Source.IsSimulated);
}

[Fact]
public async Task Returned_identity_outside_requested_scope_is_a_502_contract_violation()
{
    await using var fixture = await RuntimeFixture.CreateAsync("L-001");
    fixture.Source.UnexpectedInventoryIdentity = Guid.NewGuid();

    var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
        fixture.Service.QueryInventoryAsync(fixture.SiteId));

    Assert.Equal(502, error.StatusCode);
    Assert.Equal(SpaceErrorCodes.WmsRuntimeContractViolation, error.Code);
    Assert.False(error.Retryable);
}

[Fact]
public async Task Transport_failure_is_retryable_but_cancellation_is_preserved()
{
    await using var fixture = await RuntimeFixture.CreateAsync("L-001");
    fixture.Source.QueryException = new TimeoutException("simulated timeout");

    var unavailable = await Assert.ThrowsAsync<SpaceProblemException>(() =>
        fixture.Service.QueryTasksAsync(fixture.SiteId));

    Assert.Equal(503, unavailable.StatusCode);
    Assert.Equal(SpaceErrorCodes.WmsUnavailable, unavailable.Code);
    Assert.True(unavailable.Retryable);

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        fixture.Service.QueryTasksAsync(fixture.SiteId, cancellationToken: cancellation.Token));
}

[Fact]
public async Task Multi_chunk_snapshot_uses_earliest_observation_and_rejects_source_change()
{
    await using var fixture = await RuntimeFixture.CreateAsync(
        Enumerable.Range(1, 501).Select(value => $"L-{value:0000}").ToArray());
    fixture.Source.Observations =
    [
        DateTimeOffset.Parse("2026-07-31T16:00:00Z"),
        DateTimeOffset.Parse("2026-07-31T15:59:55Z"),
    ];

    var result = await fixture.Service.QueryInventoryAsync(fixture.SiteId);
    Assert.Equal(DateTimeOffset.Parse("2026-07-31T15:59:55Z"), result.Source.ObservedAtUtc);

    fixture.Source.ResetCalls();
    fixture.Source.ReturnedDataSourceIds = ["RECORDING_WMS", "OTHER_WMS"];
    var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
        fixture.Service.QueryInventoryAsync(fixture.SiteId));
    Assert.Equal(SpaceErrorCodes.WmsRuntimeContractViolation, error.Code);
}

[Fact]
public async Task Empty_published_selection_keeps_declared_source_without_calling_wms()
{
    await using var fixture = await RuntimeFixture.CreateAsync();

    var result = await fixture.Service.QueryInventoryAsync(fixture.SiteId);

    Assert.Empty(result.Items);
    Assert.Equal("Real", result.Source.Kind);
    Assert.True(result.Source.IsAvailable);
    Assert.Empty(fixture.Source.InventoryBatchSizes);
}

[Fact]
public async Task More_than_10000_requested_locations_fail_before_wms()
{
    await using var fixture = await RuntimeFixture.CreateAsync("L-001");
    var requested = Enumerable.Range(0, 10_001)
        .Select(_ => Guid.NewGuid())
        .ToArray();

    var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
        fixture.Service.QueryInventoryAsync(fixture.SiteId, requested));

    Assert.Equal(400, error.StatusCode);
    Assert.Equal(SpaceErrorCodes.RequestInvalid, error.Code);
    Assert.Empty(fixture.Source.InventoryBatchSizes);
}
```

Add this disposable fixture using the Task 2 helpers:

```csharp
private sealed class RuntimeFixture : IAsyncDisposable
{
    private RuntimeFixture(
        SpaceContext context,
        RecordingRuntimeSource source,
        SpaceWmsRuntimeService service,
        Guid siteId)
    {
        Context = context;
        Source = source;
        Service = service;
        SiteId = siteId;
    }

    public SpaceContext Context { get; }
    public RecordingRuntimeSource Source { get; }
    public SpaceWmsRuntimeService Service { get; }
    public Guid SiteId { get; }

    public static async Task<RuntimeFixture> CreateAsync(
        params string[] locationCodes)
    {
        var execution = Execution();
        var clock = new TestClock();
        var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(context, locationCodes);
        var source = new RecordingRuntimeSource();
        var service = CreateService(
            context, execution, clock, seeded.SiteId, source);
        return new RuntimeFixture(context, source, service, seeded.SiteId);
    }

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
```

Replace the Task 2 `RuntimeDataSourceKind` property and static `Source()` method,
then add the following members. Use `NextSource()` in both result records:

```csharp
private int _callIndex;

public SpaceWmsDataSourceKind DeclaredKind { get; set; } =
    SpaceWmsDataSourceKind.Real;
public SpaceWmsDataSourceKind RuntimeDataSourceKind => DeclaredKind;
public Exception? QueryException { get; set; }
public Guid? UnexpectedInventoryIdentity { get; set; }
public IReadOnlyList<DateTimeOffset> Observations { get; set; } =
    [new DateTimeOffset(Now)];
public IReadOnlyList<string> ReturnedDataSourceIds { get; set; } =
    ["RECORDING_WMS"];

public void ResetCalls()
{
    _callIndex = 0;
    InventoryBatchSizes.Clear();
    TaskBatchSizes.Clear();
}

private SpaceWmsSourceMetadata NextSource()
{
    var index = _callIndex++;
    return new SpaceWmsSourceMetadata(
        DeclaredKind,
        ReturnedDataSourceIds[Math.Min(index, ReturnedDataSourceIds.Count - 1)],
        Observations[Math.Min(index, Observations.Count - 1)]);
}
```

At the beginning of both query methods call `ct.ThrowIfCancellationRequested()`, then throw `QueryException` when non-null. When `UnexpectedInventoryIdentity` has a value, return one inventory item with that ID and code `UNEXPECTED` instead of filtering the seeded collection.

- [ ] **Step 2: Run the tests to verify RED**

Run the Task 2 focused integration-test command.

Expected: the new tests fail because source validation, retryable error mapping, and conservative observation merge are not complete.

- [ ] **Step 3: Implement source validation and error mapping**

Add these helpers to `SpaceWmsRuntimeService` and call them for every chunk:

```csharp
private SpaceWmsSourceMetadata MergeSource(
    SpaceWmsSourceMetadata? current,
    SpaceWmsSourceMetadata next)
{
    ValidateSource(next);
    if (current is null)
        return next with { ObservedAtUtc = next.ObservedAtUtc.ToUniversalTime() };
    if (current.Kind != next.Kind ||
        !string.Equals(current.DataSourceId, next.DataSourceId, StringComparison.Ordinal))
    {
        throw ContractViolation(
            "The runtime source changed within one logical snapshot.");
    }
    return current with
    {
        ObservedAtUtc = current.ObservedAtUtc <= next.ObservedAtUtc
            ? current.ObservedAtUtc
            : next.ObservedAtUtc.ToUniversalTime(),
    };
}

private void ValidateSource(SpaceWmsSourceMetadata source)
{
    if (!Enum.IsDefined(source.Kind) ||
        string.IsNullOrWhiteSpace(source.DataSourceId) ||
        source.DataSourceId.Length > 100 ||
        source.ObservedAtUtc == default ||
        source.Kind != _source.RuntimeDataSourceKind ||
        !string.Equals(
            source.DataSourceId,
            _source.RuntimeDataSourceId,
            StringComparison.Ordinal))
    {
        throw ContractViolation("The runtime result has invalid provenance.");
    }
}

private static SpaceProblemException ContractViolation(string detail) =>
    new(
        SpaceErrorCodes.WmsRuntimeContractViolation,
        502,
        "The WMS runtime response violated its contract.",
        detail,
        "verify-wms-adapter");

private static SpaceProblemException Unavailable(Exception error) =>
    new(
        SpaceErrorCodes.WmsUnavailable,
        503,
        "The WMS runtime source is unavailable.",
        error.Message,
        "retry-runtime-query",
        retryable: true);
```

Wrap only the source call in `try/catch`. Rethrow `OperationCanceledException` when the supplied token is canceled; map other adapter/transport exceptions to `Unavailable`. Validate these item invariants before projection:

- returned Logical ID is non-empty and belongs to `LocationByWmsLogicalId`;
- WMS Location Code is non-empty;
- inventory item collection and task item collection are non-null;
- task ID, type, and status are non-empty and Sequence Number is at least 1.

When any chunk returns `Kind == Unavailable`, discard items accumulated from earlier chunks and return one empty response with that source. For an empty Published selection, do not call WMS; construct source metadata from the declared Kind/ID and `_clock.UtcNow`.

- [ ] **Step 4: Verify all service failure tests pass**

Run the focused integration-test command again.

Expected: every `SpaceWmsRuntimeServiceTests` case passes; 502 errors are not retryable, 503 errors are retryable, and cancellation remains `OperationCanceledException`.

- [ ] **Step 5: Commit the defensive boundary**

```powershell
git add CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs
git commit -m "feat(space): fail closed on invalid WMS runtime data"
```

### Task 4: Expose HTTP endpoints, DI, OpenAPI, and generated clients

**Files:**

- Create: `CP6.WebApi/Controllers/Space/SpaceWmsRuntimeController.cs`
- Modify: `CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs:122-125`
- Modify: `CP6.Space.IntegrationTests/StandardSpaceWmsSimulatorTests.cs:348-378`
- Modify: `CP6.Tests/Space/SpacePermissionAttributeTests.cs:45-64,105-109`
- Modify: `CP6.Tests/Space/SpaceDesignV1OpenApiTests.cs:19-70`
- Modify generated: `docs/space/contracts/design-v1.openapi.json`
- Modify generated: `CP6.Space.Client/SpaceDesignV1Client.g.cs`
- Modify generated: `sdk/typescript/space-design-v1/spaceDesignV1Client.ts`

- [ ] **Step 1: Write failing DI, permission, and OpenAPI tests**

Extend `Registration_exposes_control_without_replacing_cp6_adapter`:

```csharp
var runtimeService = Assert.Single(
    services,
    value => value.ServiceType == typeof(ISpaceWmsRuntimeService));
Assert.Equal(typeof(SpaceWmsRuntimeService), runtimeService.ImplementationType);
Assert.Equal(ServiceLifetime.Scoped, runtimeService.Lifetime);
```

In `SpacePermissionAttributeTests`, change the controller discovery count from 11 to 12 and add:

```csharp
["SpaceWmsRuntimeController.GetInventory"] = "space:model:read",
["SpaceWmsRuntimeController.GetTasks"] = "space:model:read",
```

In `SpaceDesignV1OpenApiTests`, add both paths to the frozen path set and add one schema test:

```csharp
[Fact]
public void Runtime_inventory_and_task_contracts_expose_source_and_dual_identity()
{
    using var document = ReadContract();
    var root = document.RootElement;
    var paths = root.GetProperty("paths");
    Assert.True(paths.TryGetProperty(
        "/api/space/design/v1/sites/{siteId}/runtime/inventory", out _));
    Assert.True(paths.TryGetProperty(
        "/api/space/design/v1/sites/{siteId}/runtime/tasks", out _));
    var schemas = root.GetProperty("components").GetProperty("schemas");
    var source = schemas
        .GetProperty("CP6.Space.Contracts.SpaceWmsRuntimeSourceDto")
        .GetProperty("properties");
    Assert.True(source.TryGetProperty("kind", out _));
    Assert.True(source.TryGetProperty("observedAtUtc", out _));
    Assert.True(source.TryGetProperty("isAvailable", out _));
    var inventory = schemas
        .GetProperty("CP6.Space.Contracts.SpaceWmsRuntimeInventoryItemDto")
        .GetProperty("properties");
    Assert.True(inventory.TryGetProperty("locationLogicalId", out _));
    Assert.True(inventory.TryGetProperty("wmsLogicalId", out _));
    Assert.True(inventory.TryGetProperty("codeMatches", out _));
}
```

- [ ] **Step 2: Run the focused tests to verify RED**

Run:

```powershell
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release --filter FullyQualifiedName~Registration_exposes_control_without_replacing_cp6_adapter
dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter "FullyQualifiedName~SpacePermissionAttributeTests|FullyQualifiedName~SpaceDesignV1OpenApiTests"
```

Expected: DI assertion fails, controller count/whitelist fails, and the OpenAPI paths/schemas are absent.

- [ ] **Step 3: Register the scoped service**

Add directly after the current `ISpaceWmsRuntimeSource` registration:

```csharp
services.AddScoped<ISpaceWmsRuntimeService, SpaceWmsRuntimeService>();
```

Do not register the simulator as `ISpaceWmsRuntimeSource`; tests that need it must pass or override it explicitly.

- [ ] **Step 4: Add the focused runtime controller**

Create `CP6.WebApi/Controllers/Space/SpaceWmsRuntimeController.cs`:

```csharp
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Authorize]
[Route("api/space/design/v1/sites/{siteId:guid}/runtime")]
[ProducesResponseType(typeof(SpaceDesignProblemDetails),
    StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(SpaceDesignProblemDetails),
    StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(SpaceDesignProblemDetails),
    StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(SpaceDesignProblemDetails),
    StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(SpaceDesignProblemDetails),
    StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(SpaceDesignProblemDetails),
    StatusCodes.Status502BadGateway, "application/problem+json")]
[ProducesResponseType(typeof(SpaceDesignProblemDetails),
    StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
public sealed class SpaceWmsRuntimeController(
    ISpaceWmsRuntimeService runtime) : ControllerBase
{
    [HttpGet("inventory")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceWmsRuntimeInventoryResponse>(
        StatusCodes.Status200OK)]
    public Task<SpaceWmsRuntimeInventoryResponse> GetInventory(
        Guid siteId,
        [FromQuery(Name = "locationLogicalId")] Guid[]? locationLogicalIds = null,
        CancellationToken cancellationToken = default) =>
        runtime.QueryInventoryAsync(siteId, locationLogicalIds, cancellationToken);

    [HttpGet("tasks")]
    [RequirePermission("space", "model:read")]
    [ProducesResponseType<SpaceWmsRuntimeTaskResponse>(StatusCodes.Status200OK)]
    public Task<SpaceWmsRuntimeTaskResponse> GetTasks(
        Guid siteId,
        [FromQuery(Name = "locationLogicalId")] Guid[]? locationLogicalIds = null,
        CancellationToken cancellationToken = default) =>
        runtime.QueryTasksAsync(siteId, locationLogicalIds, cancellationToken);
}
```

- [ ] **Step 5: Regenerate OpenAPI and both clients**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/generate-space-design-sdk.ps1
```

Expected: only the OpenAPI JSON, generated C# client, and generated TypeScript client change; generated operations include inventory and task runtime methods with repeated `locationLogicalId` query parameters.

- [ ] **Step 6: Run API, permission, and SDK drift tests**

Run:

```powershell
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release --filter FullyQualifiedName~Registration_exposes_control_without_replacing_cp6_adapter
dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter "FullyQualifiedName~SpacePermissionAttributeTests|FullyQualifiedName~SpaceDesignV1OpenApiTests"
powershell -ExecutionPolicy Bypass -File tools/generate-space-design-sdk.ps1 -Check
```

Expected: all tests pass and the SDK check reports no stale generated artifact.

- [ ] **Step 7: Commit the HTTP contract slice**

```powershell
git add CP6.WebApi/Controllers/Space/SpaceWmsRuntimeController.cs CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs CP6.Space.IntegrationTests/StandardSpaceWmsSimulatorTests.cs CP6.Tests/Space/SpacePermissionAttributeTests.cs CP6.Tests/Space/SpaceDesignV1OpenApiTests.cs docs/space/contracts/design-v1.openapi.json CP6.Space.Client/SpaceDesignV1Client.g.cs sdk/typescript/space-design-v1/spaceDesignV1Client.ts
git commit -m "feat(space): expose unified WMS runtime APIs"
```

### Task 5: Verify the feature and record delivery evidence

**Files:**

- Create: `docs/space/reports/e08-s01-unified-runtime-source.md`
- Modify: `docs/project-memory/PROJECT_STATE.md:5-10`

- [ ] **Step 1: Run focused and full Space test gates**

Run serially to control disk and memory pressure:

```powershell
dotnet test CP6.Space.UnitTests/CP6.Space.UnitTests.csproj -c Release
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release
dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter "FullyQualifiedName~SpaceDesignV1OpenApiTests|FullyQualifiedName~SpacePermissionAttributeTests|FullyQualifiedName~SpaceDataSourceContractTests"
```

Expected: all new tests pass, default SQL-gated skips remain environment-gated, and there are no new failures.

- [ ] **Step 2: Run build, SDK, EF, and whitespace gates**

```powershell
dotnet build CP6.slnx -c Release --no-incremental
powershell -ExecutionPolicy Bypass -File tools/generate-space-design-sdk.ps1 -Check
dotnet ef migrations has-pending-model-changes --project CP6.Space.Infrastructure/CP6.Space.Infrastructure.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context SpaceContext
git diff --check
```

Expected: solution build has zero errors, generated clients have no drift, EF reports no pending model changes, and `git diff --check` is silent.

- [ ] **Step 3: Review the feature diff against the design commit**

Run:

```powershell
git diff --stat 636eb6d5..HEAD
git diff --name-only 636eb6d5..HEAD
```

Expected: changes are restricted to the file map above. No migrations, frontend Viewer components, legacy stock/task controllers, or Design Revision persistence files appear.

- [ ] **Step 4: Write the delivery report**

Create `docs/space/reports/e08-s01-unified-runtime-source.md` with these completed sections and the actual command results from Steps 1–3:

```markdown
# E08-S01 统一运行态数据源交付报告

- 状态：已完成，待合入 Space 受控集成分支
- 工作分支：`codex/space-e08-s01-runtime-source`
- 设计提交：`636eb6d5`

## 1. 交付范围

记录统一 Published 运行态服务、双身份映射、500 条分块、10,000 上限、
来源失败关闭、两条只读 API，以及明确未包含的 E08-S02～S05 范围。

## 2. 数据与信任边界

记录 Design Revision 不承载库存/任务、生产 DI 默认选择 CP6 WMS、模拟器只能显式选择，
以及 Unavailable 不等同于真实空库存。

## 3. API 与合同

列出 inventory/tasks 路径、`space:model:read`、公开来源字段和双 Logical ID 字段。

## 4. 验证证据

以表格写入每条实际命令、passed/skipped 数量、构建结果、SDK drift、EF drift 和 diff check。

## 5. 后续边界

下一张卡为 E08-S02 库存来源、时间和延迟展示；接收时间、延迟、健康历史和 UI 不在本卡。
```

Do not claim the integration merge hash before the merge exists.

- [ ] **Step 5: Update project memory**

At the top of `docs/project-memory/PROJECT_STATE.md`, add an E08-S01 section containing the actual feature commit, verification counts, runtime authority rule, and E08-S02 as the next recommended card. Preserve older historical entries unchanged.

- [ ] **Step 6: Commit evidence and memory**

```powershell
git add docs/space/reports/e08-s01-unified-runtime-source.md docs/project-memory/PROJECT_STATE.md
git commit -m "docs(space): record E08 S01 completion"
```

### Task 6: Review, integrate, and verify the controlled baseline

**Files:**

- Modify after merge: `docs/space/reports/e08-s01-unified-runtime-source.md`
- Modify after merge: `docs/project-memory/PROJECT_STATE.md`

- [ ] **Step 1: Run pre-landing review on the feature range**

Use the required code-review skill for the complete range `636eb6d5..HEAD`. Resolve every Critical or Important finding with a failing regression test first, then rerun the affected focused gate.

- [ ] **Step 2: Confirm the feature worktree is clean**

```powershell
git status --short --branch
```

Expected: branch `codex/space-e08-s01-runtime-source` with no staged or unstaged files.

- [ ] **Step 3: Finish the development branch**

Use `superpowers:finishing-a-development-branch`. For this project, select the established controlled-baseline path: no-ff merge the feature branch into `integration/space-v1-20260730` from `D:\CP6\tmp\worktrees\space-integration-20260730`. Do not touch the user workspace `D:\CP6` or merge the historical candidate checkpoint.

- [ ] **Step 4: Re-run merge-state gates**

From the integration worktree run:

```powershell
dotnet test CP6.Space.UnitTests/CP6.Space.UnitTests.csproj -c Release --filter "FullyQualifiedName~SpaceWmsRuntimeContractTests|FullyQualifiedName~SpaceWmsAdapterContractTests"
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~SpaceWmsRuntimeServiceTests|FullyQualifiedName~Cp6SpaceWmsAdapterTests|FullyQualifiedName~StandardSpaceWmsSimulatorTests"
dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter "FullyQualifiedName~SpaceDesignV1OpenApiTests|FullyQualifiedName~SpacePermissionAttributeTests"
powershell -ExecutionPolicy Bypass -File tools/generate-space-design-sdk.ps1 -Check
```

Expected: every focused merge-state gate passes and generated clients have no drift.

- [ ] **Step 5: Record the actual feature and no-ff merge hashes**

Update the delivery report and top project-memory entry with the actual feature and integration merge hashes plus the merge-state test counts. Commit only those documentation changes on the integration branch:

```powershell
git add docs/space/reports/e08-s01-unified-runtime-source.md docs/project-memory/PROJECT_STATE.md
git commit -m "docs(space): finalize E08 S01 integration evidence"
```

- [ ] **Step 6: Confirm both controlled worktrees are clean**

Run `git status --short --branch` in the feature and integration worktrees.

Expected: both are clean; the main user workspace remains on its original branch with no task-created changes.
