using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePlanningExchangeService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access)
    : ISpacePlanningExchangeService
{
    public const string SchemaVersion = "cp6.space.planning.gltf.v1";
    public const string ContentType = "model/gltf-binary";
    public const int MaximumNodeCount = 50_000;

    private const uint GlbMagic = 0x46546C67;
    private const uint GlbVersion = 2;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinaryChunkType = 0x004E4942;
    private const int ArrayBufferTarget = 34962;
    private const int ElementArrayBufferTarget = 34963;
    private const int FloatComponentType = 5126;
    private const int UnsignedShortComponentType = 5123;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public async Task<SpacePlanningExchangeFile> ExportGlbAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        EnsureInternal();
        EnsureIdentity(siteId, "siteId");
        EnsureIdentity(branchId, "branchId");
        access.EnsureSiteAccess(siteId, write: false);

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

        var aggregate = await (
                from branch in context.PlanningScenarioBranches.AsNoTracking()
                where branch.Id == branchId && branch.SiteId == siteId
                join model in context.Models.AsNoTracking()
                    on branch.ModelId equals model.Id
                join scenario in context.Versions.AsNoTracking()
                    on branch.ScenarioVersionId equals scenario.Id
                join baseVersion in context.Versions.AsNoTracking()
                    on branch.BasePublishedVersionId equals baseVersion.Id
                join job in context.Jobs.AsNoTracking()
                    on branch.CloneJobId equals job.Id
                select new BranchAggregate(
                    branch,
                    model,
                    scenario,
                    baseVersion,
                    job))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        EnsureReadyAndIsolated(aggregate);

        var versionId = aggregate.Scenario.Id;
        var nodeCount = 0;
        var floors = await context.FloorRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == versionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.Level)
            .ThenBy(value => value.LogicalId)
            .Take(MaximumNodeCount - nodeCount + 1)
            .Select(value => new FloorRow(
                value.LogicalId,
                value.Level,
                value.FloorCode,
                value.Name,
                value.Elevation,
                value.Height,
                value.CoordinateSystem,
                value.BoundaryJson))
            .ToArrayAsync(cancellationToken);
        nodeCount = ConsumeNodeBudget(nodeCount, floors.Length);
        var zones = await context.ZoneRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == versionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.FloorLogicalId)
            .ThenBy(value => value.ZoneCode)
            .ThenBy(value => value.LogicalId)
            .Take(MaximumNodeCount - nodeCount + 1)
            .Select(value => new ZoneRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.ZoneCode,
                value.ZoneType,
                value.Color,
                value.CapabilityFlags,
                value.PolygonJson))
            .ToArrayAsync(cancellationToken);
        nodeCount = ConsumeNodeBudget(nodeCount, zones.Length);
        var aisles = await context.AisleRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == versionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.ZoneLogicalId)
            .ThenBy(value => value.AisleCode)
            .ThenBy(value => value.LogicalId)
            .Take(MaximumNodeCount - nodeCount + 1)
            .Select(value => new AisleRow(
                value.LogicalId,
                value.ZoneLogicalId,
                value.AisleCode,
                value.Direction,
                value.PolygonJson,
                value.CenterlineJson))
            .ToArrayAsync(cancellationToken);
        nodeCount = ConsumeNodeBudget(nodeCount, aisles.Length);
        var racks = await context.RackRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == versionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.FloorLogicalId)
            .ThenBy(value => value.RackCode)
            .ThenBy(value => value.LogicalId)
            .Take(MaximumNodeCount - nodeCount + 1)
            .Select(value => new RackRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.ZoneLogicalId,
                value.AisleLogicalId,
                value.RackCode,
                value.X,
                value.Y,
                value.Z,
                value.RotationZ,
                value.Width,
                value.Depth,
                value.Height))
            .ToArrayAsync(cancellationToken);
        nodeCount = ConsumeNodeBudget(nodeCount, racks.Length);
        var levels = await context.RackLevelRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == versionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.RackLogicalId)
            .ThenBy(value => value.LevelNo)
            .Take(MaximumNodeCount - nodeCount + 1)
            .Select(value => new RackLevelRow(
                value.LogicalId,
                value.RackLogicalId,
                value.LevelNo,
                value.BottomZ,
                value.ClearHeight,
                value.BinCount,
                value.DepthCount,
                value.CellWidth,
                value.CellDepth,
                value.BeamHeight,
                value.MaxLoad))
            .ToArrayAsync(cancellationToken);
        nodeCount = ConsumeNodeBudget(nodeCount, levels.Length);
        var locations = await context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == versionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.FloorLogicalId)
            .ThenBy(value => value.RackLogicalId)
            .ThenBy(value => value.LevelNo)
            .ThenBy(value => value.ColumnNo)
            .ThenBy(value => value.DepthNo)
            .ThenBy(value => value.LogicalId)
            .Take(MaximumNodeCount - nodeCount + 1)
            .Select(value => new LocationRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.RackLogicalId,
                value.LocationCode,
                value.ColumnNo,
                value.LevelNo,
                value.DepthNo,
                value.Width,
                value.Height,
                value.Depth,
                value.MaxLoad,
                value.CodeOrigin,
                value.ExternalBindingState))
            .ToArrayAsync(cancellationToken);
        nodeCount = ConsumeNodeBudget(nodeCount, locations.Length);
        var elements = await context.ElementRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == versionId &&
                value.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(value => value.FloorLogicalId)
            .ThenBy(value => value.ElementType)
            .ThenBy(value => value.BusinessCode)
            .ThenBy(value => value.LogicalId)
            .Take(MaximumNodeCount - nodeCount + 1)
            .Select(value => new ElementRow(
                value.LogicalId,
                value.FloorLogicalId,
                value.ParentLogicalId,
                value.ElementType,
                value.X,
                value.Y,
                value.Z,
                value.RotationZ,
                value.Width,
                value.Height,
                value.Depth,
                value.BusinessCode,
                value.LinkedEntityType,
                value.LinkedLogicalId))
            .ToArrayAsync(cancellationToken);
        _ = ConsumeNodeBudget(nodeCount, elements.Length);

        var binary = UnitCubeBinary();
        var document = BuildDocument(
            aggregate,
            floors,
            zones,
            aisles,
            racks,
            levels,
            locations,
            elements,
            binary.Length);
        var content = WriteGlb(document, binary);
        var sha256 = Convert.ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return new SpacePlanningExchangeFile(
            content,
            $"space-planning-{branchId:N}-r{aggregate.Scenario.ContentRevision}.glb",
            ContentType,
            SchemaVersion,
            sha256);
    }

    private static JsonObject BuildDocument(
        BranchAggregate aggregate,
        IReadOnlyList<FloorRow> floors,
        IReadOnlyList<ZoneRow> zones,
        IReadOnlyList<AisleRow> aisles,
        IReadOnlyList<RackRow> racks,
        IReadOnlyList<RackLevelRow> levels,
        IReadOnlyList<LocationRow> locations,
        IReadOnlyList<ElementRow> elements,
        int binaryLength)
    {
        var nodes = new JsonArray();
        var sceneRoots = new JsonArray();
        var floorChildren = new Dictionary<Guid, JsonArray>();
        var floorIds = floors.Select(value => value.LogicalId).ToHashSet();
        var zoneById = zones.ToDictionary(value => value.LogicalId);
        var rackById = racks.ToDictionary(value => value.LogicalId);
        var levelByPosition = levels.ToDictionary(
            value => (value.RackLogicalId, value.LevelNo));

        foreach (var floor in floors)
        {
            var children = new JsonArray();
            floorChildren.Add(floor.LogicalId, children);
            sceneRoots.Add(nodes.Count);
            nodes.Add(new JsonObject
            {
                ["name"] = $"Floor:{floor.FloorCode}",
                ["translation"] = Vector(0, Millimeters(floor.Elevation), 0),
                ["children"] = children,
                ["extras"] = Cp6Extras(
                    ("logicalId", floor.LogicalId, "floor"),
                    ("floorCode", floor.FloorCode),
                    ("floorName", floor.Name),
                    ("level", floor.Level),
                    ("heightMm", floor.Height),
                    ("coordinateSystem", floor.CoordinateSystem),
                    ("boundaryJson", floor.BoundaryJson)),
            });
        }

        foreach (var zone in zones)
        {
            AddSemanticNode(
                nodes,
                ChildrenFor(floorChildren, zone.FloorLogicalId),
                $"Zone:{zone.ZoneCode}",
                Cp6Extras(
                    ("logicalId", zone.LogicalId, "zone"),
                    ("zoneCode", zone.ZoneCode),
                    ("zoneType", zone.ZoneType),
                    ("color", zone.Color),
                    ("capabilityFlags", zone.CapabilityFlags),
                    ("polygonJson", zone.PolygonJson)));
        }

        foreach (var aisle in aisles)
        {
            if (!zoneById.TryGetValue(aisle.ZoneLogicalId, out var zone))
                throw GeometryInvalid("An active aisle has no active zone.");
            AddSemanticNode(
                nodes,
                ChildrenFor(floorChildren, zone.FloorLogicalId),
                $"Aisle:{aisle.AisleCode}",
                Cp6Extras(
                    ("logicalId", aisle.LogicalId, "aisle"),
                    ("aisleCode", aisle.AisleCode),
                    ("zoneLogicalId", aisle.ZoneLogicalId),
                    ("direction", aisle.Direction),
                    ("polygonJson", aisle.PolygonJson),
                    ("centerlineJson", aisle.CenterlineJson)));
        }

        foreach (var rack in racks)
        {
            EnsureFloor(floorIds, rack.FloorLogicalId, "rack");
            AddBoxNode(
                nodes,
                ChildrenFor(floorChildren, rack.FloorLogicalId),
                $"Rack:{rack.RackCode}",
                0,
                rack.X,
                rack.Y,
                rack.Z,
                rack.RotationZ,
                rack.Width,
                rack.Depth,
                rack.Height,
                Cp6Extras(
                    ("logicalId", rack.LogicalId, "rack"),
                    ("rackCode", rack.RackCode),
                    ("zoneLogicalId", rack.ZoneLogicalId),
                    ("aisleLogicalId", rack.AisleLogicalId),
                    ("sourceAnchorMm", SourceVector(rack.X, rack.Y, rack.Z)),
                    ("sourceRotationZDegrees", rack.RotationZ)));
        }

        foreach (var location in locations)
        {
            EnsureFloor(floorIds, location.FloorLogicalId, "location");
            var extras = Cp6Extras(
                ("logicalId", location.LogicalId, "location"),
                ("locationCode", location.LocationCode),
                ("rackLogicalId", location.RackLogicalId),
                ("columnNo", location.ColumnNo),
                ("levelNo", location.LevelNo),
                ("depthNo", location.DepthNo),
                ("maxLoad", location.MaxLoad),
                ("codeOrigin", location.CodeOrigin.ToString()),
                ("externalBindingState", location.ExternalBindingState.ToString()));
            if (!location.RackLogicalId.HasValue)
            {
                AddSemanticNode(
                    nodes,
                    ChildrenFor(floorChildren, location.FloorLogicalId),
                    $"Location:{location.LocationCode ?? location.LogicalId.ToString("N")}",
                    extras,
                    placed: false);
                continue;
            }
            if (!rackById.TryGetValue(location.RackLogicalId.Value, out var rack) ||
                rack.FloorLogicalId != location.FloorLogicalId ||
                !levelByPosition.TryGetValue(
                    (rack.LogicalId, location.LevelNo),
                    out var level))
            {
                throw GeometryInvalid(
                    "An active location has inconsistent rack or level geometry.");
            }
            var radians = (double)rack.RotationZ * Math.PI / 180d;
            var localX = (location.ColumnNo - 0.5d) * level.CellWidth;
            var localY = (location.DepthNo - 0.5d) * level.CellDepth;
            var centerX = rack.X + localX * Math.Cos(radians) -
                localY * Math.Sin(radians);
            var centerY = rack.Y + localX * Math.Sin(radians) +
                localY * Math.Cos(radians);
            var centerZ = rack.Z + level.BottomZ + level.BeamHeight +
                location.Height / 2d;
            AddCenteredBoxNode(
                nodes,
                ChildrenFor(floorChildren, location.FloorLogicalId),
                $"Location:{location.LocationCode ?? location.LogicalId.ToString("N")}",
                1,
                centerX,
                centerY,
                centerZ,
                rack.RotationZ,
                location.Width,
                location.Depth,
                location.Height,
                extras);
        }

        foreach (var level in levels)
        {
            if (!rackById.TryGetValue(level.RackLogicalId, out var rack))
                throw GeometryInvalid("An active rack level has no active rack.");
            AddSemanticNode(
                nodes,
                ChildrenFor(floorChildren, rack.FloorLogicalId),
                $"RackLevel:{rack.RackCode}:{level.LevelNo}",
                Cp6Extras(
                    ("logicalId", level.LogicalId, "rackLevel"),
                    ("rackLogicalId", level.RackLogicalId),
                    ("levelNo", level.LevelNo),
                    ("bottomZMm", level.BottomZ),
                    ("clearHeightMm", level.ClearHeight),
                    ("binCount", level.BinCount),
                    ("depthCount", level.DepthCount),
                    ("cellWidthMm", level.CellWidth),
                    ("cellDepthMm", level.CellDepth),
                    ("beamHeightMm", level.BeamHeight),
                    ("maxLoad", level.MaxLoad)));
        }

        foreach (var element in elements)
        {
            EnsureFloor(floorIds, element.FloorLogicalId, "element");
            AddBoxNode(
                nodes,
                ChildrenFor(floorChildren, element.FloorLogicalId),
                $"Element:{element.BusinessCode ?? element.LogicalId.ToString("N")}",
                2,
                element.X,
                element.Y,
                element.Z,
                element.RotationZ,
                element.Width,
                element.Depth,
                element.Height,
                Cp6Extras(
                    ("logicalId", element.LogicalId, "element"),
                    ("elementType", element.ElementType),
                    ("businessCode", element.BusinessCode),
                    ("parentLogicalId", element.ParentLogicalId),
                    ("linkedEntityType", element.LinkedEntityType),
                    ("linkedLogicalId", element.LinkedLogicalId),
                    ("sourceAnchorMm", SourceVector(
                        element.X,
                        element.Y,
                        element.Z)),
                    ("sourceRotationZDegrees", element.RotationZ)));
        }

        var rootExtras = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["siteId"] = Id(aggregate.Branch.SiteId),
            ["modelId"] = Id(aggregate.Branch.ModelId),
            ["branchId"] = Id(aggregate.Branch.Id),
            ["basePublishedVersionId"] = Id(
                aggregate.Branch.BasePublishedVersionId),
            ["scenarioVersionId"] = Id(
                aggregate.Branch.ScenarioVersionId),
            ["scenarioContentRevision"] = aggregate.Scenario.ContentRevision,
            ["sourceCoordinateSystem"] = "LOCAL_MM_Z_UP",
            ["coordinateTransform"] = "(x,y,z)_mm -> (x,z,-y)_m",
            ["productionIsolated"] = true,
            ["productionWriteAllowed"] = false,
            ["runtimeOverlayIncluded"] = false,
            ["counts"] = new JsonObject
            {
                ["floors"] = floors.Count,
                ["zones"] = zones.Count,
                ["aisles"] = aisles.Count,
                ["racks"] = racks.Count,
                ["rackLevels"] = levels.Count,
                ["locations"] = locations.Count,
                ["elements"] = elements.Count,
            },
        };

        return new JsonObject
        {
            ["asset"] = new JsonObject
            {
                ["version"] = "2.0",
                ["generator"] = "CP6 Space Planning Exchange E12-S05",
                ["extras"] = new JsonObject { ["cp6"] = rootExtras },
            },
            ["scene"] = 0,
            ["scenes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "CP6 Planning Scenario",
                    ["nodes"] = sceneRoots,
                },
            },
            ["nodes"] = nodes,
            ["meshes"] = Meshes(),
            ["materials"] = Materials(),
            ["accessors"] = new JsonArray
            {
                new JsonObject
                {
                    ["bufferView"] = 0,
                    ["componentType"] = FloatComponentType,
                    ["count"] = 24,
                    ["type"] = "VEC3",
                    ["min"] = Vector(-0.5, -0.5, -0.5),
                    ["max"] = Vector(0.5, 0.5, 0.5),
                },
                new JsonObject
                {
                    ["bufferView"] = 1,
                    ["componentType"] = FloatComponentType,
                    ["count"] = 24,
                    ["type"] = "VEC3",
                },
                new JsonObject
                {
                    ["bufferView"] = 2,
                    ["componentType"] = UnsignedShortComponentType,
                    ["count"] = 36,
                    ["type"] = "SCALAR",
                    ["min"] = new JsonArray(JsonValue.Create(0)),
                    ["max"] = new JsonArray(JsonValue.Create(23)),
                },
            },
            ["bufferViews"] = new JsonArray
            {
                new JsonObject
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = 0,
                    ["byteLength"] = 288,
                    ["target"] = ArrayBufferTarget,
                },
                new JsonObject
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = 288,
                    ["byteLength"] = 288,
                    ["target"] = ArrayBufferTarget,
                },
                new JsonObject
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = 576,
                    ["byteLength"] = 72,
                    ["target"] = ElementArrayBufferTarget,
                },
            },
            ["buffers"] = new JsonArray
            {
                new JsonObject { ["byteLength"] = binaryLength },
            },
        };
    }

    private static void AddBoxNode(
        JsonArray nodes,
        JsonArray children,
        string name,
        int mesh,
        double anchorX,
        double anchorY,
        double anchorZ,
        decimal rotationDegrees,
        int width,
        int depth,
        int height,
        JsonObject extras)
    {
        var radians = (double)rotationDegrees * Math.PI / 180d;
        var centerX = anchorX + width / 2d * Math.Cos(radians) -
            depth / 2d * Math.Sin(radians);
        var centerY = anchorY + width / 2d * Math.Sin(radians) +
            depth / 2d * Math.Cos(radians);
        var centerZ = anchorZ + height / 2d;
        AddCenteredBoxNode(
            nodes,
            children,
            name,
            mesh,
            centerX,
            centerY,
            centerZ,
            rotationDegrees,
            width,
            depth,
            height,
            extras);
    }

    private static void AddCenteredBoxNode(
        JsonArray nodes,
        JsonArray children,
        string name,
        int mesh,
        double centerX,
        double centerY,
        double centerZ,
        decimal rotationDegrees,
        int width,
        int depth,
        int height,
        JsonObject extras)
    {
        children.Add(nodes.Count);
        var node = new JsonObject
        {
            ["name"] = name,
            ["translation"] = Vector(
                Millimeters(centerX),
                Millimeters(centerZ),
                -Millimeters(centerY)),
            ["rotation"] = YRotation(rotationDegrees),
            ["extras"] = extras,
        };
        if (width > 0 && depth > 0 && height > 0)
        {
            node["mesh"] = mesh;
            node["scale"] = Vector(
                Millimeters(width),
                Millimeters(height),
                Millimeters(depth));
        }
        else
        {
            ((JsonObject)extras["cp6"]!)["placed"] = false;
        }
        nodes.Add(node);
    }

    private static void AddSemanticNode(
        JsonArray nodes,
        JsonArray children,
        string name,
        JsonObject extras,
        bool placed = true)
    {
        children.Add(nodes.Count);
        ((JsonObject)extras["cp6"]!)["placed"] = placed;
        nodes.Add(new JsonObject
        {
            ["name"] = name,
            ["extras"] = extras,
        });
    }

    private static JsonObject Cp6Extras(
        (string Key, Guid LogicalId, string Kind) identity,
        params (string Key, object? Value)[] values)
    {
        var cp6 = new JsonObject
        {
            [identity.Key] = Id(identity.LogicalId),
            ["kind"] = identity.Kind,
        };
        foreach (var (key, value) in values)
            AddValue(cp6, key, value);
        return new JsonObject { ["cp6"] = cp6 };
    }

    private static void AddValue(JsonObject target, string key, object? value)
    {
        if (value is JsonNode node)
            target[key] = node;
        else if (value is Guid id)
            target[key] = Id(id);
        else if (value is not null)
            target[key] = JsonValue.Create(value);
    }

    private static JsonObject SourceVector(int x, int y, int z) => new()
    {
        ["x"] = x,
        ["y"] = y,
        ["z"] = z,
        ["unit"] = "mm",
    };

    private static JsonArray Meshes() => new(
        Mesh("Rack", 0),
        Mesh("Location", 1),
        Mesh("Element", 2));

    private static JsonObject Mesh(string name, int material) => new()
    {
        ["name"] = name,
        ["primitives"] = new JsonArray
        {
            new JsonObject
            {
                ["attributes"] = new JsonObject
                {
                    ["POSITION"] = 0,
                    ["NORMAL"] = 1,
                },
                ["indices"] = 2,
                ["material"] = material,
                ["mode"] = 4,
            },
        },
    };

    private static JsonArray Materials() => new(
        Material("Rack", 0.35, 0.55, 0.62, 1),
        Material("Location", 0.18, 0.55, 0.95, 0.35),
        Material("Element", 0.95, 0.55, 0.18, 1));

    private static JsonObject Material(
        string name,
        double red,
        double green,
        double blue,
        double alpha) => new()
        {
            ["name"] = name,
            ["pbrMetallicRoughness"] = new JsonObject
            {
                ["baseColorFactor"] = Vector(red, green, blue, alpha),
                ["metallicFactor"] = 0,
                ["roughnessFactor"] = 0.8,
            },
            ["alphaMode"] = alpha < 1 ? "BLEND" : "OPAQUE",
            ["doubleSided"] = true,
        };

    private static JsonArray YRotation(decimal degrees)
    {
        var half = (double)degrees * Math.PI / 360d;
        return Vector(0, Clean(Math.Sin(half)), 0, Clean(Math.Cos(half)));
    }

    private static JsonArray Vector(params double[] values)
    {
        var result = new JsonArray();
        foreach (var value in values)
            result.Add(Clean(value));
        return result;
    }

    private static double Millimeters(double value) => Clean(value / 1000d);

    private static double Clean(double value) =>
        Math.Abs(value) < 1e-12 ? 0d : value;

    private static JsonArray ChildrenFor(
        IReadOnlyDictionary<Guid, JsonArray> children,
        Guid floorLogicalId) =>
        children.TryGetValue(floorLogicalId, out var result)
            ? result
            : throw GeometryInvalid("An active object has no active floor.");

    private static void EnsureFloor(
        IReadOnlySet<Guid> floorIds,
        Guid floorLogicalId,
        string kind)
    {
        if (!floorIds.Contains(floorLogicalId))
            throw GeometryInvalid($"An active {kind} has no active floor.");
    }

    private static string Id(Guid value) => value.ToString("D");

    private static byte[] UnitCubeBinary()
    {
        float[] positions =
        [
            -0.5f, -0.5f,  0.5f,  0.5f, -0.5f,  0.5f,
             0.5f,  0.5f,  0.5f, -0.5f,  0.5f,  0.5f,
             0.5f, -0.5f, -0.5f, -0.5f, -0.5f, -0.5f,
            -0.5f,  0.5f, -0.5f,  0.5f,  0.5f, -0.5f,
            -0.5f, -0.5f, -0.5f, -0.5f, -0.5f,  0.5f,
            -0.5f,  0.5f,  0.5f, -0.5f,  0.5f, -0.5f,
             0.5f, -0.5f,  0.5f,  0.5f, -0.5f, -0.5f,
             0.5f,  0.5f, -0.5f,  0.5f,  0.5f,  0.5f,
            -0.5f,  0.5f,  0.5f,  0.5f,  0.5f,  0.5f,
             0.5f,  0.5f, -0.5f, -0.5f,  0.5f, -0.5f,
            -0.5f, -0.5f, -0.5f,  0.5f, -0.5f, -0.5f,
             0.5f, -0.5f,  0.5f, -0.5f, -0.5f,  0.5f,
        ];
        float[] normals =
        [
             0,  0,  1,  0,  0,  1,  0,  0,  1,  0,  0,  1,
             0,  0, -1,  0,  0, -1,  0,  0, -1,  0,  0, -1,
            -1,  0,  0, -1,  0,  0, -1,  0,  0, -1,  0,  0,
             1,  0,  0,  1,  0,  0,  1,  0,  0,  1,  0,  0,
             0,  1,  0,  0,  1,  0,  0,  1,  0,  0,  1,  0,
             0, -1,  0,  0, -1,  0,  0, -1,  0,  0, -1,  0,
        ];
        ushort[] indices =
        [
             0,  1,  2,  0,  2,  3,
             4,  5,  6,  4,  6,  7,
             8,  9, 10,  8, 10, 11,
            12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19,
            20, 21, 22, 20, 22, 23,
        ];
        using var stream = new MemoryStream(648);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var value in positions)
            writer.Write(value);
        foreach (var value in normals)
            writer.Write(value);
        foreach (var value in indices)
            writer.Write(value);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] WriteGlb(JsonObject document, byte[] binary)
    {
        var json = Encoding.UTF8.GetBytes(document.ToJsonString(Json));
        var paddedJsonLength = Align4(json.Length);
        var paddedBinaryLength = Align4(binary.Length);
        var totalLength = checked(
            12 + 8 + paddedJsonLength + 8 + paddedBinaryLength);
        using var stream = new MemoryStream(totalLength);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(GlbMagic);
        writer.Write(GlbVersion);
        writer.Write((uint)totalLength);
        writer.Write((uint)paddedJsonLength);
        writer.Write(JsonChunkType);
        writer.Write(json);
        for (var index = json.Length; index < paddedJsonLength; index++)
            writer.Write((byte)0x20);
        writer.Write((uint)paddedBinaryLength);
        writer.Write(BinaryChunkType);
        writer.Write(binary);
        for (var index = binary.Length; index < paddedBinaryLength; index++)
            writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static int Align4(int value) => checked((value + 3) & ~3);

    private static int ConsumeNodeBudget(int current, int added)
    {
        if (added > MaximumNodeCount - current)
        {
            throw Invalid(
                $"The planning exchange exceeds the {MaximumNodeCount}-node limit.",
                "reduce-scenario-complexity");
        }
        return checked(current + added);
    }

    private void EnsureInternal()
    {
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningScenarioInternalOnly,
                403,
                "Planning exchange export is restricted to internal users.",
                recoveryAction: "use-internal-planning-account");
        }
    }

    private static void EnsureIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw Invalid($"{parameterName} is required.", "correct-request");
    }

    private static void EnsureReadyAndIsolated(BranchAggregate value)
    {
        var isolated =
            value.BaseVersion.Purpose == SpaceModelVersionPurpose.Production &&
            value.Scenario.Purpose == SpaceModelVersionPurpose.PlanningScenario &&
            value.Scenario.BasedOnVersionId == value.BaseVersion.Id &&
            value.Scenario.CloneOperationId == value.Branch.Id &&
            value.Model.ActiveDraftVersionId != value.Scenario.Id &&
            value.Model.CurrentPublishedVersionId != value.Scenario.Id;
        var ready =
            value.Job.Status == SpaceJobStatus.Succeeded &&
            value.Scenario.Status is SpaceVersionStatus.Draft or
                SpaceVersionStatus.Validating or SpaceVersionStatus.Ready;
        if (!isolated || !ready)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PlanningExchangeUnavailable,
                409,
                "The planning branch is not ready for an isolated exchange export.",
                recoveryAction: "wait-for-scenario-clone");
        }
    }

    private static SpaceProblemException NotFound() => new(
        SpaceErrorCodes.PlanningScenarioNotFound,
        404,
        "The planning scenario branch was not found.",
        recoveryAction: "refresh");

    private static SpaceProblemException Invalid(
        string detail,
        string recoveryAction) => new(
            SpaceErrorCodes.PlanningExchangeUnavailable,
            422,
            detail,
            recoveryAction: recoveryAction);

    private static SpaceProblemException GeometryInvalid(string detail) => new(
        SpaceErrorCodes.PlanningExchangeGeometryInvalid,
        409,
        detail,
        recoveryAction: "repair-scenario-geometry");

    private sealed record BranchAggregate(
        SpacePlanningScenarioBranch Branch,
        SpaceModel Model,
        SpaceModelVersion Scenario,
        SpaceModelVersion BaseVersion,
        SpaceJob Job);

    private sealed record FloorRow(
        Guid LogicalId,
        int Level,
        string FloorCode,
        string Name,
        int Elevation,
        int Height,
        string CoordinateSystem,
        string BoundaryJson);

    private sealed record ZoneRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        string ZoneCode,
        short ZoneType,
        string? Color,
        string? CapabilityFlags,
        string PolygonJson);

    private sealed record AisleRow(
        Guid LogicalId,
        Guid ZoneLogicalId,
        string AisleCode,
        short Direction,
        string PolygonJson,
        string CenterlineJson);

    private sealed record RackRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid ZoneLogicalId,
        Guid? AisleLogicalId,
        string RackCode,
        int X,
        int Y,
        int Z,
        decimal RotationZ,
        int Width,
        int Depth,
        int Height);

    private sealed record RackLevelRow(
        Guid LogicalId,
        Guid RackLogicalId,
        int LevelNo,
        int BottomZ,
        int ClearHeight,
        int BinCount,
        int DepthCount,
        int CellWidth,
        int CellDepth,
        int BeamHeight,
        decimal? MaxLoad);

    private sealed record LocationRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid? RackLogicalId,
        string? LocationCode,
        int ColumnNo,
        int LevelNo,
        int DepthNo,
        int Width,
        int Height,
        int Depth,
        decimal? MaxLoad,
        SpaceLocationCodeOrigin CodeOrigin,
        SpaceExternalBindingState ExternalBindingState);

    private sealed record ElementRow(
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid? ParentLogicalId,
        string ElementType,
        int X,
        int Y,
        int Z,
        decimal RotationZ,
        int Width,
        int Height,
        int Depth,
        string? BusinessCode,
        string? LinkedEntityType,
        Guid? LinkedLogicalId);
}
