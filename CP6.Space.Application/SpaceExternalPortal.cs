using CP6.Space.Contracts;

namespace CP6.Space.Application;

public enum SpacePortalFieldKind
{
    Text = 0,
    Scalar = 1,
}

public sealed record SpacePortalFieldDefinition(
    SpaceResourceType ResourceType,
    string FieldName,
    SpacePortalFieldKind Kind);

public static class SpacePortalFieldCatalog
{
    private static readonly SpacePortalFieldDefinition[] Definitions =
    [
        Text(SpaceResourceType.PublishedScene, "floor.code"),
        Text(SpaceResourceType.PublishedScene, "floor.name"),
        Scalar(SpaceResourceType.PublishedScene, "floor.level"),
        Scalar(SpaceResourceType.PublishedScene, "floor.elevation"),
        Scalar(SpaceResourceType.PublishedScene, "floor.height"),
        Text(SpaceResourceType.PublishedScene, "floor.boundaryJson"),
        Text(SpaceResourceType.PublishedScene, "floor.coordinateSystem"),
        Text(SpaceResourceType.PublishedScene, "zone.code"),
        Scalar(SpaceResourceType.PublishedScene, "zone.type"),
        Text(SpaceResourceType.PublishedScene, "zone.polygonJson"),
        Text(SpaceResourceType.PublishedScene, "zone.color"),
        Text(SpaceResourceType.PublishedScene, "zone.capabilityFlags"),
        Text(SpaceResourceType.PublishedScene, "aisle.code"),
        Text(SpaceResourceType.PublishedScene, "aisle.polygonJson"),
        Text(SpaceResourceType.PublishedScene, "aisle.centerlineJson"),
        Scalar(SpaceResourceType.PublishedScene, "aisle.direction"),
        Text(SpaceResourceType.PublishedScene, "rack.code"),
        Scalar(SpaceResourceType.PublishedScene, "rack.templateVersionId"),
        Scalar(SpaceResourceType.PublishedScene, "rack.position"),
        Scalar(SpaceResourceType.PublishedScene, "rack.rotationZ"),
        Scalar(SpaceResourceType.PublishedScene, "rack.dimensions"),
        Scalar(SpaceResourceType.PublishedScene, "rackLevel.geometry"),
        Scalar(SpaceResourceType.PublishedScene, "rackLevel.maxLoad"),
        Text(SpaceResourceType.PublishedScene, "location.code"),
        Scalar(SpaceResourceType.PublishedScene, "location.position"),
        Scalar(SpaceResourceType.PublishedScene, "location.dimensions"),
        Scalar(SpaceResourceType.PublishedScene, "location.maxLoad"),
        Text(SpaceResourceType.PublishedScene, "location.externalBindingState"),
        Text(SpaceResourceType.PublishedScene, "element.type"),
        Text(SpaceResourceType.PublishedScene, "element.geometryJson"),
        Scalar(SpaceResourceType.PublishedScene, "element.modelAssetId"),
        Text(SpaceResourceType.PublishedScene, "element.modelAssetScope"),
        Text(SpaceResourceType.PublishedScene, "element.businessCode"),
        Text(SpaceResourceType.PublishedScene, "element.linkedEntityType"),
        Scalar(SpaceResourceType.PublishedScene, "element.linkedLogicalId"),
        Text(SpaceResourceType.Stock, "spaceLocationCode"),
        Text(SpaceResourceType.Stock, "wmsLocationCode"),
        Text(SpaceResourceType.Stock, "floorCode"),
        Text(SpaceResourceType.Stock, "floorName"),
        Scalar(SpaceResourceType.Stock, "floorLevel"),
        Scalar(SpaceResourceType.Stock, "physicalQuantity"),
        Scalar(SpaceResourceType.Stock, "allocatedQuantity"),
        Text(SpaceResourceType.Stock, "materialNumber"),
        Text(SpaceResourceType.Stock, "lotNumber"),
        Text(SpaceResourceType.Stock, "containerNumber"),
        Text(SpaceResourceType.Stock, "ownerId"),
        Text(SpaceResourceType.Task, "taskId"),
        Text(SpaceResourceType.Task, "taskType"),
        Text(SpaceResourceType.Task, "status"),
        Scalar(SpaceResourceType.Task, "sequenceNo"),
        Text(SpaceResourceType.Task, "spaceLocationCode"),
        Text(SpaceResourceType.Task, "wmsLocationCode"),
        Text(SpaceResourceType.Task, "floorCode"),
        Text(SpaceResourceType.Task, "floorName"),
        Scalar(SpaceResourceType.Task, "floorLevel"),
        Text(SpaceResourceType.Task, "zoneCode"),
        Scalar(SpaceResourceType.Task, "rackLogicalId"),
        Text(SpaceResourceType.Task, "rackCode"),
        Scalar(SpaceResourceType.Task, "anchor"),
        Scalar(SpaceResourceType.Task, "quantity"),
        Text(SpaceResourceType.Task, "materialNumber"),
    ];

    private static readonly IReadOnlyDictionary<string, SpacePortalFieldDefinition>
        ByKey = Definitions.ToDictionary(
            item => Key(item.ResourceType, item.FieldName),
            StringComparer.Ordinal);

    public static IReadOnlyList<SpacePortalFieldDefinition> All => Definitions;

    public static SpacePortalFieldDefinition? Find(
        SpaceResourceType resourceType,
        string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return null;
        return ByKey.GetValueOrDefault(Key(resourceType, fieldName.Trim()));
    }

    private static string Key(
        SpaceResourceType resourceType,
        string fieldName) =>
        $"{(int)resourceType}:{fieldName.ToUpperInvariant()}";

    private static SpacePortalFieldDefinition Text(
        SpaceResourceType type,
        string name) => new(type, name, SpacePortalFieldKind.Text);

    private static SpacePortalFieldDefinition Scalar(
        SpaceResourceType type,
        string name) => new(type, name, SpacePortalFieldKind.Scalar);
}

public interface ISpaceFieldPolicyService
{
    Task<IReadOnlyList<SpaceFieldPolicyDto>> GetPoliciesAsync(
        string? audienceType,
        string? status,
        CancellationToken cancellationToken = default);

    Task<SpaceFieldPolicyDto> GetPolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default);

    Task<SpaceFieldPolicyDto> CreatePolicyAsync(
        CreateSpaceFieldPolicyRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceFieldPolicyDto> UpdatePolicyAsync(
        Guid policyId,
        UpdateSpaceFieldPolicyRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISpaceExternalPortalService
{
    Task<IReadOnlyList<SpacePortalOrganizationDto>> GetOrganizationsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpacePortalSiteDto>> GetSitesAsync(
        CancellationToken cancellationToken = default);

    Task<SpacePortalPublishedSceneDto> GetPublishedSceneAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);

    Task<SpacePortalStockResponse> GetStockAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);

    Task<SpacePortalTaskResponse> GetTasksAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);
}

public interface ISpacePublishedSceneReader
{
    Task<SpaceDesignSceneDto> GetSceneAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default);
}
