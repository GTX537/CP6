using System.Text.Json;

namespace CP6.Space.Domain;

public abstract class SpaceRevisionEntity : SpaceTenantEntity
{
    public Guid ModelVersionId { get; private set; }
    public Guid LogicalId { get; private set; }
    public Guid? SourceId { get; private set; }
    public string? SourceRef { get; private set; }
    public SpaceLifecycleState LifecycleState { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    protected void InitializeRevision(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId)
    {
        SpaceRevisionValue.RequireIdentity(modelVersionId, nameof(modelVersionId));
        SpaceRevisionValue.RequireIdentity(logicalId, nameof(logicalId));
        SetTenant(tenantId);
        ModelVersionId = modelVersionId;
        LogicalId = logicalId;
        LifecycleState = SpaceLifecycleState.Active;
    }

    public void AttachSource(SpaceModelSource source, string? sourceRef)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.TenantId != TenantId)
            throw new SpaceTenantScopeException("Revision and source tenants must match.");
        if (source.ModelVersionId != ModelVersionId)
        {
            throw new SpaceVersionConflictException(
                "Revision and source must belong to the same model version.");
        }

        SourceId = source.Id;
        SourceRef = SpaceRevisionValue.OptionalText(sourceRef, 500, nameof(sourceRef));
    }

    public void ChangeLifecycle(SpaceLifecycleState lifecycleState)
    {
        LifecycleState = lifecycleState;
    }
}

public sealed class SpaceFloorRevision : SpaceRevisionEntity
{
    private SpaceFloorRevision()
    {
    }

    public Guid SiteLogicalId { get; private set; }
    public int Level { get; private set; }
    public string FloorCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Elevation { get; private set; }
    public int Height { get; private set; }
    public string BoundaryJson { get; private set; } = "[]";
    public string CoordinateSystem { get; private set; } = "LOCAL_MM_Z_UP";
    public Guid? UnderlaySourceId { get; private set; }
    public Guid? UnderlayCalibrationId { get; private set; }
    public decimal? UnderlayScale { get; private set; }
    public int UnderlayOffsetX { get; private set; }
    public int UnderlayOffsetY { get; private set; }
    public decimal UnderlayRotationZ { get; private set; }
    public long Revision { get; private set; }

    public static SpaceFloorRevision Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId,
        Guid siteLogicalId,
        int level,
        string floorCode,
        string name,
        int elevation = 0,
        int height = 0)
    {
        SpaceRevisionValue.RequireIdentity(siteLogicalId, nameof(siteLogicalId));
        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        var revision = new SpaceFloorRevision
        {
            SiteLogicalId = siteLogicalId,
            Level = level,
            FloorCode = SpaceRevisionValue.RequiredText(floorCode, 100, nameof(floorCode)),
            Name = SpaceRevisionValue.RequiredText(name, 200, nameof(name)),
            Elevation = elevation,
            Height = height,
        };
        revision.InitializeRevision(tenantId, modelVersionId, logicalId);
        return revision;
    }

    public void ConfigureBoundary(string boundaryJson, string coordinateSystem)
    {
        BoundaryJson = SpaceRevisionValue.Json(boundaryJson, nameof(boundaryJson));
        CoordinateSystem = SpaceRevisionValue.RequiredText(
            coordinateSystem,
            100,
            nameof(coordinateSystem));
    }

    public void ConfigureUnderlay(
        SpaceModelSource? source,
        decimal? scale,
        int offsetX,
        int offsetY,
        decimal rotationZ)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (source is not null)
        {
            if (source.TenantId != TenantId ||
                source.ModelVersionId != ModelVersionId)
            {
                throw new SpaceTenantScopeException(
                    "Underlay source must belong to the same tenant and version.");
            }
            UnderlaySourceId = source.Id;
        }
        else
        {
            UnderlaySourceId = null;
        }

        UnderlayScale = scale;
        UnderlayOffsetX = offsetX;
        UnderlayOffsetY = offsetY;
        UnderlayRotationZ = SpaceRevisionValue.Rotation(rotationZ);
    }

    public void AttachUnderlay(SpaceModelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.TenantId != TenantId ||
            source.ModelVersionId != ModelVersionId)
        {
            throw new SpaceTenantScopeException(
                "Underlay source must belong to the same tenant and version.");
        }
        if (UnderlaySourceId == source.Id)
            return;

        UnderlaySourceId = source.Id;
        UnderlayCalibrationId = null;
        UnderlayScale = null;
        UnderlayOffsetX = 0;
        UnderlayOffsetY = 0;
        UnderlayRotationZ = 0;
    }

    public void ApplyUnderlayCalibration(
        SpaceModelSource source,
        SpaceUnderlayCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(calibration);
        if (source.TenantId != TenantId ||
            source.ModelVersionId != ModelVersionId ||
            source.Id != UnderlaySourceId ||
            calibration.TenantId != TenantId ||
            calibration.ModelVersionId != ModelVersionId ||
            calibration.FloorLogicalId != LogicalId ||
            calibration.SourceId != source.Id)
        {
            throw new SpaceTenantScopeException(
                "Underlay calibration must match the attached source and floor.");
        }

        UnderlayCalibrationId = calibration.Id;
        UnderlayScale = calibration.MillimetersPerPixel;
        UnderlayOffsetX = calibration.OffsetX;
        UnderlayOffsetY = calibration.OffsetY;
        UnderlayRotationZ = calibration.RotationZ;
    }

    public void AdvanceRevision(long expectedRevision)
    {
        if (expectedRevision != Revision)
            throw new SpaceVersionConflictException("The floor revision is stale.");
        Revision = checked(Revision + 1);
    }
}

public sealed class SpaceZoneRevision : SpaceRevisionEntity
{
    private SpaceZoneRevision()
    {
    }

    public Guid FloorLogicalId { get; private set; }
    public string ZoneCode { get; private set; } = string.Empty;
    public short ZoneType { get; private set; }
    public string PolygonJson { get; private set; } = "[]";
    public string? Color { get; private set; }
    public string? CapabilityFlags { get; private set; }

    public static SpaceZoneRevision Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId,
        Guid floorLogicalId,
        string zoneCode,
        short zoneType)
    {
        SpaceRevisionValue.RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        var revision = new SpaceZoneRevision
        {
            FloorLogicalId = floorLogicalId,
            ZoneCode = SpaceRevisionValue.RequiredText(zoneCode, 100, nameof(zoneCode)),
            ZoneType = zoneType,
        };
        revision.InitializeRevision(tenantId, modelVersionId, logicalId);
        return revision;
    }

    public void ConfigureShape(
        string polygonJson,
        string? color = null,
        string? capabilityFlags = null)
    {
        PolygonJson = SpaceRevisionValue.Json(polygonJson, nameof(polygonJson));
        Color = SpaceRevisionValue.OptionalText(color, 50, nameof(color));
        CapabilityFlags =
            SpaceRevisionValue.OptionalText(capabilityFlags, 1000, nameof(capabilityFlags));
    }
}

public sealed class SpaceAisleRevision : SpaceRevisionEntity
{
    private SpaceAisleRevision()
    {
    }

    public Guid ZoneLogicalId { get; private set; }
    public string AisleCode { get; private set; } = string.Empty;
    public string PolygonJson { get; private set; } = "[]";
    public string CenterlineJson { get; private set; } = "[]";
    public short Direction { get; private set; }

    public static SpaceAisleRevision Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId,
        Guid zoneLogicalId,
        string aisleCode,
        short direction)
    {
        SpaceRevisionValue.RequireIdentity(zoneLogicalId, nameof(zoneLogicalId));
        var revision = new SpaceAisleRevision
        {
            ZoneLogicalId = zoneLogicalId,
            AisleCode = SpaceRevisionValue.RequiredText(aisleCode, 100, nameof(aisleCode)),
            Direction = direction,
        };
        revision.InitializeRevision(tenantId, modelVersionId, logicalId);
        return revision;
    }

    public void ConfigureShape(string polygonJson, string centerlineJson)
    {
        PolygonJson = SpaceRevisionValue.Json(polygonJson, nameof(polygonJson));
        CenterlineJson = SpaceRevisionValue.Json(centerlineJson, nameof(centerlineJson));
    }
}

public sealed class SpaceRackRevision : SpaceRevisionEntity
{
    private SpaceRackRevision()
    {
    }

    public Guid FloorLogicalId { get; private set; }
    public Guid ZoneLogicalId { get; private set; }
    public Guid? AisleLogicalId { get; private set; }
    public string RackCode { get; private set; } = string.Empty;
    public Guid? TemplateVersionId { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Z { get; private set; }
    public decimal RotationZ { get; private set; }
    public int Width { get; private set; }
    public int Depth { get; private set; }
    public int Height { get; private set; }

    public static SpaceRackRevision Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId,
        Guid floorLogicalId,
        Guid zoneLogicalId,
        string rackCode,
        Guid? aisleLogicalId = null)
    {
        SpaceRevisionValue.RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        SpaceRevisionValue.RequireIdentity(zoneLogicalId, nameof(zoneLogicalId));
        if (aisleLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Aisle logical identity cannot be empty.",
                nameof(aisleLogicalId));
        }

        var revision = new SpaceRackRevision
        {
            FloorLogicalId = floorLogicalId,
            ZoneLogicalId = zoneLogicalId,
            AisleLogicalId = aisleLogicalId,
            RackCode = SpaceRevisionValue.RequiredText(rackCode, 100, nameof(rackCode)),
        };
        revision.InitializeRevision(tenantId, modelVersionId, logicalId);
        return revision;
    }

    public void ConfigureGeometry(
        int x,
        int y,
        int z,
        decimal rotationZ,
        int width,
        int depth,
        int height,
        Guid? templateVersionId = null)
    {
        SpaceRevisionValue.RequireDimensions(width, height, depth);
        if (templateVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Template version identity cannot be empty.",
                nameof(templateVersionId));
        }

        X = x;
        Y = y;
        Z = z;
        RotationZ = SpaceRevisionValue.Rotation(rotationZ);
        Width = width;
        Depth = depth;
        Height = height;
        TemplateVersionId = templateVersionId;
    }
}

public sealed class SpaceRackLevelRevision : SpaceRevisionEntity
{
    private SpaceRackLevelRevision()
    {
    }

    public Guid RackLogicalId { get; private set; }
    public int LevelNo { get; private set; }
    public int BottomZ { get; private set; }
    public int ClearHeight { get; private set; }
    public int BinCount { get; private set; }
    public int DepthCount { get; private set; }
    public int CellWidth { get; private set; }
    public int CellDepth { get; private set; }
    public int BeamHeight { get; private set; }
    public decimal? MaxLoad { get; private set; }

    public static SpaceRackLevelRevision Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId,
        Guid rackLogicalId,
        int levelNo,
        int bottomZ,
        int clearHeight,
        int binCount,
        int depthCount,
        int cellWidth,
        int cellDepth,
        decimal? maxLoad = null,
        int beamHeight = 0)
    {
        SpaceRevisionValue.RequireIdentity(rackLogicalId, nameof(rackLogicalId));

        var revision = new SpaceRackLevelRevision
        {
            RackLogicalId = rackLogicalId,
        };
        revision.UpdateSpecification(
            levelNo,
            bottomZ,
            clearHeight,
            binCount,
            depthCount,
            cellWidth,
            cellDepth,
            maxLoad,
            beamHeight);
        revision.InitializeRevision(tenantId, modelVersionId, logicalId);
        return revision;
    }

    public void UpdateSpecification(
        int levelNo,
        int bottomZ,
        int clearHeight,
        int binCount,
        int depthCount,
        int cellWidth,
        int cellDepth,
        decimal? maxLoad = null,
        int beamHeight = 0)
    {
        RequirePositive(levelNo, nameof(levelNo));
        RequireNonNegative(bottomZ, nameof(bottomZ));
        RequirePositive(clearHeight, nameof(clearHeight));
        RequirePositive(binCount, nameof(binCount));
        RequirePositive(depthCount, nameof(depthCount));
        RequirePositive(cellWidth, nameof(cellWidth));
        RequirePositive(cellDepth, nameof(cellDepth));
        RequireNonNegative(beamHeight, nameof(beamHeight));
        if (maxLoad < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLoad),
                "Rack level maximum load cannot be negative.");
        }

        LevelNo = levelNo;
        BottomZ = bottomZ;
        ClearHeight = clearHeight;
        BinCount = binCount;
        DepthCount = depthCount;
        CellWidth = cellWidth;
        CellDepth = cellDepth;
        BeamHeight = beamHeight;
        MaxLoad = maxLoad;
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Rack level numbers, counts and cell dimensions must be positive.");
        }
    }

    private static void RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Rack level offsets and beam dimensions cannot be negative.");
        }
    }
}

public sealed class SpaceLocationRevision : SpaceRevisionEntity
{
    private SpaceLocationRevision()
    {
    }

    public Guid FloorLogicalId { get; private set; }
    public Guid? RackLogicalId { get; private set; }
    public string? LocationCode { get; private set; }
    public int ColumnNo { get; private set; }
    public int LevelNo { get; private set; }
    public int DepthNo { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Depth { get; private set; }
    public decimal? MaxLoad { get; private set; }
    public SpaceLocationCodeOrigin CodeOrigin { get; private set; }
    public SpaceExternalBindingState ExternalBindingState { get; private set; }

    public static SpaceLocationRevision Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId,
        Guid floorLogicalId,
        Guid? rackLogicalId,
        string? locationCode,
        int columnNo,
        int levelNo,
        int depthNo,
        int width,
        int height,
        int depth,
        decimal? maxLoad = null,
        SpaceLocationCodeOrigin codeOrigin = SpaceLocationCodeOrigin.Generated,
        SpaceExternalBindingState externalBindingState =
            SpaceExternalBindingState.Unbound)
    {
        SpaceRevisionValue.RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        if (rackLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Rack logical identity cannot be empty.",
                nameof(rackLogicalId));
        }
        if (columnNo <= 0 || levelNo <= 0 || depthNo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnNo),
                "Location coordinates must be positive.");
        }
        SpaceRevisionValue.RequireDimensions(width, height, depth);
        if (maxLoad < 0)
            throw new ArgumentOutOfRangeException(nameof(maxLoad));

        var revision = new SpaceLocationRevision
        {
            FloorLogicalId = floorLogicalId,
            RackLogicalId = rackLogicalId,
            LocationCode = SpaceRevisionValue.OptionalText(
                locationCode,
                200,
                nameof(locationCode)),
            ColumnNo = columnNo,
            LevelNo = levelNo,
            DepthNo = depthNo,
            Width = width,
            Height = height,
            Depth = depth,
            MaxLoad = maxLoad,
            CodeOrigin = codeOrigin,
            ExternalBindingState = externalBindingState,
        };
        revision.InitializeRevision(tenantId, modelVersionId, logicalId);
        return revision;
    }
}

public sealed class SpaceElementRevision : SpaceRevisionEntity
{
    private SpaceElementRevision()
    {
    }

    public Guid FloorLogicalId { get; private set; }
    public Guid? ParentLogicalId { get; private set; }
    public string ElementType { get; private set; } = string.Empty;
    public string GeometryJson { get; private set; } = "{}";
    public Guid? ModelAssetId { get; private set; }
    public SpaceAssetScope? ModelAssetScope { get; private set; }
    public Guid? ModelAssetOwnerTenantId { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Z { get; private set; }
    public decimal RotationZ { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Depth { get; private set; }
    public string? BusinessCode { get; private set; }
    public string? LinkedEntityType { get; private set; }
    public Guid? LinkedLogicalId { get; private set; }

    public static SpaceElementRevision Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid logicalId,
        Guid floorLogicalId,
        string elementType,
        string geometryJson,
        Guid? parentLogicalId = null)
    {
        SpaceRevisionValue.RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        if (parentLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Parent logical identity cannot be empty.",
                nameof(parentLogicalId));
        }
        if (parentLogicalId == logicalId)
        {
            throw new ArgumentException(
                "An element cannot be its own parent.",
                nameof(parentLogicalId));
        }

        var revision = new SpaceElementRevision
        {
            FloorLogicalId = floorLogicalId,
            ParentLogicalId = parentLogicalId,
            ElementType = SpaceElementTypes.Normalize(
                elementType,
                nameof(elementType)),
            GeometryJson = SpaceElementGeometry.Validate(
                geometryJson,
                nameof(geometryJson)),
        };
        revision.InitializeRevision(tenantId, modelVersionId, logicalId);
        return revision;
    }

    public void UpdateGeometry(string geometryJson)
    {
        var validated = SpaceElementGeometry.Validate(
            geometryJson,
            nameof(geometryJson));
        var geometryAssetVersionId =
            SpaceElementGeometry.ReadAssetVersionId(validated);
        if (geometryAssetVersionId.HasValue &&
            geometryAssetVersionId != ModelAssetId)
        {
            throw new InvalidOperationException(
                "Asset geometry must reference the attached asset version.");
        }

        GeometryJson = validated;
    }

    public void ConfigurePlacement(
        int x,
        int y,
        int z,
        decimal rotationZ,
        int width,
        int height,
        int depth)
    {
        SpaceRevisionValue.RequireDimensions(width, height, depth);
        X = x;
        Y = y;
        Z = z;
        RotationZ = SpaceRevisionValue.Rotation(rotationZ);
        Width = width;
        Height = height;
        Depth = depth;
    }

    public void AttachAsset(SpaceAssetVersion assetVersion)
    {
        ArgumentNullException.ThrowIfNull(assetVersion);
        if (assetVersion.Status != SpaceAssetVersionStatus.Ready)
        {
            throw new InvalidOperationException(
                "Only a ready asset version can be attached.");
        }
        if (!assetVersion.IsVisibleTo(TenantId))
        {
            throw new SpaceTenantScopeException(
                "The asset version is not visible to this element tenant.");
        }

        var geometryAssetVersionId =
            SpaceElementGeometry.ReadAssetVersionId(GeometryJson);
        if (geometryAssetVersionId.HasValue &&
            geometryAssetVersionId != assetVersion.Id)
        {
            throw new InvalidOperationException(
                "Asset geometry must reference the attached asset version.");
        }

        ModelAssetId = assetVersion.Id;
        ModelAssetScope = assetVersion.Scope;
        ModelAssetOwnerTenantId = assetVersion.OwnerTenantId;
    }

    public void DetachAsset()
    {
        if (SpaceElementGeometry.ReadAssetVersionId(GeometryJson).HasValue)
        {
            throw new InvalidOperationException(
                "Asset geometry requires an attached asset version.");
        }

        ModelAssetId = null;
        ModelAssetScope = null;
        ModelAssetOwnerTenantId = null;
    }

    public void EnsureAssetReferenceConsistency()
    {
        var geometryAssetVersionId =
            SpaceElementGeometry.ReadAssetVersionId(GeometryJson);
        if (geometryAssetVersionId.HasValue &&
            geometryAssetVersionId != ModelAssetId)
        {
            throw new InvalidOperationException(
                "Asset geometry must reference the attached asset version.");
        }
        if (ModelAssetId.HasValue != ModelAssetScope.HasValue ||
            ModelAssetId.HasValue != ModelAssetOwnerTenantId.HasValue)
        {
            throw new InvalidOperationException(
                "Asset identity, scope, and owner must be supplied together.");
        }
        if (ModelAssetScope == SpaceAssetScope.System &&
            ModelAssetOwnerTenantId != Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "System asset references use the platform owner identity.");
        }
        if (ModelAssetScope == SpaceAssetScope.Tenant &&
            ModelAssetOwnerTenantId != TenantId)
        {
            throw new SpaceTenantScopeException(
                "Tenant asset references must belong to the element tenant.");
        }
    }

    public void ConfigureBusinessLink(
        string? businessCode,
        string? linkedEntityType,
        Guid? linkedLogicalId)
    {
        if (linkedLogicalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Linked logical identity cannot be empty.",
                nameof(linkedLogicalId));
        }
        if (linkedLogicalId.HasValue != !string.IsNullOrWhiteSpace(linkedEntityType))
        {
            throw new ArgumentException(
                "Linked entity type and logical identity must be supplied together.");
        }

        BusinessCode =
            SpaceRevisionValue.OptionalText(businessCode, 200, nameof(businessCode));
        LinkedEntityType = SpaceRevisionValue.OptionalText(
            linkedEntityType,
            100,
            nameof(linkedEntityType));
        LinkedLogicalId = linkedLogicalId;
    }
}

public sealed class SpaceElementAttribute : SpaceTenantEntity
{
    private SpaceElementAttribute()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid ElementRevisionId { get; private set; }
    public string Namespace { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string ValueType { get; private set; } = string.Empty;
    public string? Value { get; private set; }
    public string? Unit { get; private set; }

    public static SpaceElementAttribute Create(
        Guid tenantId,
        SpaceElementRevision element,
        string attributeNamespace,
        string key,
        string valueType,
        string? value,
        string? unit = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.TenantId != tenantId)
            throw new SpaceTenantScopeException("Element attribute tenant does not match.");

        var normalized = SpaceElementAttributeValueTypes.Normalize(
            valueType,
            value,
            unit);
        var attribute = new SpaceElementAttribute
        {
            ModelVersionId = element.ModelVersionId,
            ElementRevisionId = element.Id,
            Namespace = SpaceElementAttributeNamespaces.Normalize(
                attributeNamespace,
                nameof(attributeNamespace)),
            Key = SpaceRevisionValue.RequiredText(key, 100, nameof(key)),
            ValueType = normalized.ValueType,
            Value = normalized.Value,
            Unit = normalized.Unit,
        };
        attribute.SetTenant(tenantId);
        return attribute;
    }

    public void UpdateValue(string valueType, string? value, string? unit = null)
    {
        var normalized = SpaceElementAttributeValueTypes.Normalize(
            valueType,
            value,
            unit);
        ValueType = normalized.ValueType;
        Value = normalized.Value;
        Unit = normalized.Unit;
    }

    public void Remove()
    {
        MarkEntityDeleted();
    }
}

internal static class SpaceRevisionValue
{
    public static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    public static string RequiredText(
        string value,
        int maxLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    public static string? OptionalText(
        string? value,
        int maxLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RequiredText(value, maxLength, parameterName);

    public static string Json(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1_048_576)
        {
            throw new ArgumentException(
                "Revision JSON is required and is too large.",
                parameterName);
        }
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Revision JSON is invalid.",
                parameterName,
                exception);
        }
        return value;
    }

    public static decimal Rotation(decimal value)
    {
        var normalized = value % 360m;
        return normalized < 0 ? normalized + 360m : normalized;
    }

    public static void RequireDimensions(int width, int height, int depth)
    {
        if (width <= 0 || height <= 0 || depth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Revision dimensions must be positive integer millimeters.");
        }
    }
}
