using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceExternalPortalService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceAccessEvaluator access,
    ISpacePublishedSceneReader scenes,
    ISpaceWmsRuntimeService runtime) : ISpaceExternalPortalService
{
    public async Task<IReadOnlyList<SpacePortalOrganizationDto>>
        GetOrganizationsAsync(
            CancellationToken cancellationToken = default)
    {
        var principal = RequireExternal(requireOrganization: false);
        var now = RequireUtcNow();
        return await (
                from membership in context.ExternalMemberships.AsNoTracking()
                join organization in context.ExternalOrganizations.AsNoTracking()
                    on membership.OrganizationId equals organization.Id
                where membership.UserId == principal.UserId &&
                      membership.Status == SpaceExternalMembershipStatus.Active &&
                      membership.ValidFromUtc <= now &&
                      (!membership.ValidToUtc.HasValue ||
                       membership.ValidToUtc > now) &&
                      organization.Status == SpaceExternalOrganizationStatus.Active
                orderby organization.Type, organization.NormalizedCode
                select new SpacePortalOrganizationDto(
                    organization.Id,
                    organization.Type.ToString(),
                    organization.Code,
                    organization.Name,
                    membership.Role.ToString(),
                    UtcOffset(membership.ValidFromUtc),
                    membership.ValidToUtc.HasValue
                        ? UtcOffset(membership.ValidToUtc.Value)
                        : null,
                    organization.SecurityStamp,
                    membership.SecurityStamp))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpacePortalSiteDto>> GetSitesAsync(
        CancellationToken cancellationToken = default)
    {
        var principal = RequireExternal(requireOrganization: true);
        var scene = await BuildScopeAsync(
            principal,
            SpaceResourceType.PublishedScene,
            cancellationToken,
            allowNoGrants: true);
        var stock = await BuildScopeAsync(
            principal,
            SpaceResourceType.Stock,
            cancellationToken,
            allowNoGrants: true);
        var tasks = await BuildScopeAsync(
            principal,
            SpaceResourceType.Task,
            cancellationToken,
            allowNoGrants: true);
        var siteIds = scene.Clauses.Select(item => item.SiteId)
            .Concat(stock.Clauses.Select(item => item.SiteId))
            .Concat(tasks.Clauses.Select(item => item.SiteId))
            .Distinct()
            .ToArray();
        var published = await context.Models
            .AsNoTracking()
            .Where(item =>
                siteIds.Contains(item.SiteId) &&
                item.CurrentPublishedVersionId.HasValue)
            .Select(item => new
            {
                item.SiteId,
                PublishedVersionId = item.CurrentPublishedVersionId!.Value,
            })
            .ToListAsync(cancellationToken);

        return published
            .OrderBy(item => item.SiteId)
            .Select(item => new SpacePortalSiteDto(
                item.SiteId,
                item.PublishedVersionId,
                HasPolicy(scene, item.SiteId),
                HasPolicy(stock, item.SiteId),
                HasPolicy(tasks, item.SiteId),
                CanExport(scene, stock, tasks, item.SiteId),
                CombinedAuthorizationVersion(scene, stock, tasks)))
            .Where(item =>
                item.CanViewScene || item.CanViewStock || item.CanViewTasks)
            .ToArray();
    }

    public async Task<SpacePortalPublishedSceneDto> GetPublishedSceneAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var principal = RequireExternal(requireOrganization: true);
        var scope = await BuildScopeAsync(
            principal,
            SpaceResourceType.PublishedScene,
            cancellationToken);
        EnsureSitePolicy(scope, siteId);
        scope = PortalScope(scope, siteId);
        var published = await LoadPublishedAsync(siteId, cancellationToken);
        var floors = await context.FloorRevisions
            .AsNoTracking()
            .Where(item =>
                item.ModelVersionId == published.VersionId &&
                item.LifecycleState == SpaceLifecycleState.Active)
            .OrderBy(item => item.Level)
            .ThenBy(item => item.LogicalId)
            .Select(item => item.LogicalId)
            .ToArrayAsync(cancellationToken);

        var result = new List<SpacePortalFloorDto>();
        foreach (var floorId in floors)
        {
            var scene = await scenes.GetSceneAsync(
                published.VersionId,
                floorId,
                cancellationToken);
            var projected = ProjectFloor(scope, scene);
            if (projected is not null)
                result.Add(projected);
        }
        if (result.Count == 0)
            throw ScopeNotFound();

        return new SpacePortalPublishedSceneDto(
            siteId,
            published.VersionId,
            scope.AuthorizationVersion,
            result);
    }

    public async Task<SpacePortalStockResponse> GetStockAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var principal = RequireExternal(requireOrganization: true);
        var scope = await BuildScopeAsync(
            principal,
            SpaceResourceType.Stock,
            cancellationToken);
        EnsureSitePolicy(scope, siteId);
        scope = PortalScope(scope, siteId);
        var published = await LoadPublishedAsync(siteId, cancellationToken);
        var locations = await LoadCandidateLocationsAsync(
            scope,
            siteId,
            published.VersionId,
            cancellationToken);
        if (locations.Count == 0)
            throw ScopeNotFound();

        var response = await runtime.QueryInventoryAsync(
            siteId,
            locations.Keys.ToArray(),
            cancellationToken);
        if (response.PublishedVersionId != published.VersionId)
            throw ScopeNotFound();

        var items = new List<SpacePortalStockItemDto>();
        foreach (var item in response.Items)
        {
            if (!locations.TryGetValue(item.LocationLogicalId, out var location))
                throw ScopeNotFound();
            var decision = SpaceAccessScopeMatcher.Evaluate(
                scope,
                SpaceAccessAction.Read,
                new SpaceResource(
                    principal.TenantId,
                    SpaceResourceType.Stock,
                    siteId,
                    location.FloorLogicalId,
                    location.ZoneLogicalId,
                    item.OwnerId));
            if (!decision.Allowed)
                continue;
            var rules = RuleSet.For(scope, decision.MatchedGrantIds);
            items.Add(new SpacePortalStockItemDto(
                item.LocationLogicalId,
                location.FloorLogicalId,
                rules.Text("spaceLocationCode", item.SpaceLocationCode),
                rules.Text("wmsLocationCode", item.WmsLocationCode),
                rules.Text("floorCode", item.FloorCode),
                rules.Text("floorName", item.FloorName),
                rules.Scalar("floorLevel", item.FloorLevel),
                rules.Scalar("physicalQuantity", item.PhysicalQuantity),
                rules.Scalar("allocatedQuantity", item.AllocatedQuantity),
                rules.Text("materialNumber", item.MaterialNumber),
                rules.Text("lotNumber", item.LotNumber),
                rules.Text("containerNumber", item.ContainerNumber),
                rules.Text("ownerId", item.OwnerId)));
        }

        return new SpacePortalStockResponse(
            siteId,
            response.PublishedVersionId,
            scope.AuthorizationVersion,
            Source(response.Source),
            items);
    }

    public async Task<SpacePortalTaskResponse> GetTasksAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var principal = RequireExternal(requireOrganization: true);
        var scope = await BuildScopeAsync(
            principal,
            SpaceResourceType.Task,
            cancellationToken);
        EnsureSitePolicy(scope, siteId);
        scope = PortalScope(scope, siteId);
        var published = await LoadPublishedAsync(siteId, cancellationToken);
        var locations = await LoadCandidateLocationsAsync(
            scope,
            siteId,
            published.VersionId,
            cancellationToken);
        if (locations.Count == 0)
            throw ScopeNotFound();

        var response = await runtime.QueryTasksAsync(
            siteId,
            locations.Keys.ToArray(),
            cancellationToken);
        if (response.PublishedVersionId != published.VersionId)
            throw ScopeNotFound();

        var items = new List<SpacePortalTaskItemDto>();
        foreach (var item in response.Items)
        {
            if (!locations.TryGetValue(item.LocationLogicalId, out var location))
                throw ScopeNotFound();
            var decision = SpaceAccessScopeMatcher.Evaluate(
                scope,
                SpaceAccessAction.Read,
                new SpaceResource(
                    principal.TenantId,
                    SpaceResourceType.Task,
                    siteId,
                    location.FloorLogicalId,
                    location.ZoneLogicalId,
                    BusinessObjectType: "task",
                    BusinessObjectId: item.TaskId));
            if (!decision.Allowed)
                continue;
            var rules = RuleSet.For(scope, decision.MatchedGrantIds);
            items.Add(new SpacePortalTaskItemDto(
                item.LocationLogicalId,
                location.FloorLogicalId,
                location.ZoneLogicalId,
                rules.Text("taskId", item.TaskId),
                rules.Text("taskType", item.TaskType),
                rules.Text("status", item.Status),
                rules.Scalar("sequenceNo", item.SequenceNo),
                rules.Text("spaceLocationCode", item.SpaceLocationCode),
                rules.Text("wmsLocationCode", item.WmsLocationCode),
                rules.Text("floorCode", item.FloorCode),
                rules.Text("floorName", item.FloorName),
                rules.Scalar("floorLevel", item.FloorLevel),
                rules.Text("zoneCode", item.ZoneCode),
                rules.Scalar("rackLogicalId", item.RackLogicalId),
                rules.Text("rackCode", item.RackCode),
                rules.Scalar("anchor", item.AnchorXMillimeters),
                rules.Scalar("anchor", item.AnchorYMillimeters),
                rules.Scalar("anchor", item.AnchorZMillimeters),
                rules.Scalar("quantity", item.Quantity),
                rules.Text("materialNumber", item.MaterialNumber)));
        }

        return new SpacePortalTaskResponse(
            siteId,
            response.PublishedVersionId,
            scope.AuthorizationVersion,
            Source(response.Source),
            items);
    }

    private async Task<SpaceQueryScope> BuildScopeAsync(
        SpacePrincipal principal,
        SpaceResourceType resourceType,
        CancellationToken cancellationToken,
        bool allowNoGrants = false)
    {
        var organizationId = principal.OrganizationContextId
            ?? throw OrganizationRequired();
        var scope = await access.BuildQueryScopeAsync(
            principal,
            resourceType,
            new SpaceOrganizationContext(organizationId),
            cancellationToken);
        if (!scope.Allowed &&
            !(allowNoGrants &&
              scope.ReasonCode == SpaceErrorCodes.ExternalGrantInactive))
            throw ScopeNotFound();
        return scope;
    }

    private SpacePrincipal RequireExternal(bool requireOrganization)
    {
        if (!execution.IsExternal ||
            execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalPortalSubjectRequired,
                403,
                "An external Portal subject is required.",
                recoveryAction: "authenticate-external-subject");
        }
        if (requireOrganization &&
            (!execution.OrganizationContextId.HasValue ||
             execution.OrganizationContextId == Guid.Empty))
        {
            throw OrganizationRequired();
        }
        return new SpacePrincipal(
            execution.TenantId,
            execution.ActorId,
            true,
            execution.OrganizationContextId);
    }

    private async Task<PublishedScope> LoadPublishedAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            throw ScopeNotFound();
        var result = await (
                from model in context.Models.AsNoTracking()
                join version in context.Versions.AsNoTracking()
                    on model.CurrentPublishedVersionId equals version.Id
                where model.SiteId == siteId &&
                      version.Status == SpaceVersionStatus.Published
                select new PublishedScope(siteId, version.Id))
            .SingleOrDefaultAsync(cancellationToken);
        return result ?? throw ScopeNotFound();
    }

    private async Task<IReadOnlyDictionary<Guid, CandidateLocation>>
        LoadCandidateLocationsAsync(
            SpaceQueryScope scope,
            Guid siteId,
            Guid publishedVersionId,
            CancellationToken cancellationToken)
    {
        var clauses = EligibleClauses(scope, siteId).ToArray();
        var unrestricted = clauses.Any(item =>
            item.FloorLogicalIds.Count == 0 &&
            item.ZoneLogicalIds.Count == 0);
        var allowedFloors = clauses
            .Where(item => item.ZoneLogicalIds.Count == 0)
            .SelectMany(item => item.FloorLogicalIds)
            .Distinct()
            .ToArray();
        var allowedZones = clauses
            .SelectMany(item => item.ZoneLogicalIds)
            .Distinct()
            .ToArray();
        var rows = await (
                from location in context.LocationRevisions.AsNoTracking()
                join rack in context.RackRevisions.AsNoTracking()
                    on new
                    {
                        VersionId = location.ModelVersionId,
                        LogicalId = location.RackLogicalId,
                    }
                    equals new
                    {
                        VersionId = rack.ModelVersionId,
                        LogicalId = (Guid?)rack.LogicalId,
                    }
                    into rackJoin
                from rack in rackJoin.DefaultIfEmpty()
                where location.ModelVersionId == publishedVersionId &&
                      location.LifecycleState == SpaceLifecycleState.Active &&
                      (unrestricted ||
                       allowedFloors.Contains(location.FloorLogicalId) ||
                       (rack != null && allowedZones.Contains(rack.ZoneLogicalId)))
                select new CandidateLocation(
                    location.LogicalId,
                    location.FloorLogicalId,
                    rack == null ? null : rack.ZoneLogicalId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(item => item.LocationLogicalId);
    }

    private static SpacePortalFloorDto? ProjectFloor(
        SpaceQueryScope scope,
        SpaceDesignSceneDto scene)
    {
        var activeZones = scene.Zones
            .Where(item => item.Revision.LifecycleState == "Active")
            .Select(item => new
            {
                Item = item,
                Decision = Evaluate(
                    scope,
                    scene.SiteId,
                    scene.Floor.Revision.LogicalId,
                    item.Revision.LogicalId),
            })
            .Where(item => item.Decision.Allowed)
            .ToArray();
        var floorDecision = Evaluate(
            scope,
            scene.SiteId,
            scene.Floor.Revision.LogicalId,
            null);
        if (!floorDecision.Allowed && activeZones.Length == 0)
            return null;

        var floorRules = RuleSet.For(
            scope,
            floorDecision.Allowed ? floorDecision.MatchedGrantIds : []);
        var allowedZoneIds = activeZones
            .Select(item => item.Item.Revision.LogicalId)
            .ToHashSet();
        var zoneDecisions = activeZones.ToDictionary(
            item => item.Item.Revision.LogicalId,
            item => item.Decision);
        var aisles = scene.Aisles
            .Where(item =>
                item.Revision.LifecycleState == "Active" &&
                allowedZoneIds.Contains(item.ZoneLogicalId))
            .Select(item =>
            {
                var rules = RuleSet.For(
                    scope,
                    zoneDecisions[item.ZoneLogicalId].MatchedGrantIds);
                return new SpacePortalAisleDto(
                    item.Revision.LogicalId,
                    item.ZoneLogicalId,
                    rules.Text("aisle.code", item.AisleCode),
                    rules.Text("aisle.polygonJson", item.PolygonJson),
                    rules.Text("aisle.centerlineJson", item.CenterlineJson),
                    rules.Scalar("aisle.direction", item.Direction));
            })
            .ToArray();
        var racks = scene.Racks
            .Where(item =>
                item.Revision.LifecycleState == "Active" &&
                allowedZoneIds.Contains(item.ZoneLogicalId))
            .Select(item =>
            {
                var rules = RuleSet.For(
                    scope,
                    zoneDecisions[item.ZoneLogicalId].MatchedGrantIds);
                return new SpacePortalRackDto(
                    item.Revision.LogicalId,
                    item.FloorLogicalId,
                    item.ZoneLogicalId,
                    item.AisleLogicalId,
                    rules.Text("rack.code", item.RackCode),
                    rules.Scalar("rack.templateVersionId", item.TemplateVersionId),
                    rules.Scalar("rack.position", item.X),
                    rules.Scalar("rack.position", item.Y),
                    rules.Scalar("rack.position", item.Z),
                    rules.Scalar("rack.rotationZ", item.RotationZ),
                    rules.Scalar("rack.dimensions", item.Width),
                    rules.Scalar("rack.dimensions", item.Depth),
                    rules.Scalar("rack.dimensions", item.Height));
            })
            .ToArray();
        var rackRules = racks.ToDictionary(
            item => item.LogicalId,
            item => RuleSet.For(
                scope,
                zoneDecisions[item.ZoneLogicalId].MatchedGrantIds));
        var rackLevels = scene.RackLevels
            .Where(item =>
                item.Revision.LifecycleState == "Active" &&
                rackRules.ContainsKey(item.RackLogicalId))
            .Select(item =>
            {
                var rules = rackRules[item.RackLogicalId];
                return new SpacePortalRackLevelDto(
                    item.Revision.LogicalId,
                    item.RackLogicalId,
                    rules.Scalar("rackLevel.geometry", item.LevelNo),
                    rules.Scalar("rackLevel.geometry", item.BottomZ),
                    rules.Scalar("rackLevel.geometry", item.ClearHeight),
                    rules.Scalar("rackLevel.geometry", item.BinCount),
                    rules.Scalar("rackLevel.geometry", item.DepthCount),
                    rules.Scalar("rackLevel.geometry", item.CellWidth),
                    rules.Scalar("rackLevel.geometry", item.CellDepth),
                    rules.Scalar("rackLevel.geometry", item.BeamHeight),
                    rules.Scalar("rackLevel.maxLoad", item.MaxLoad));
            })
            .ToArray();
        var locations = scene.Locations
            .Where(item => item.Revision.LifecycleState == "Active")
            .Select(item => new
            {
                Item = item,
                Rules = item.RackLogicalId.HasValue &&
                    rackRules.TryGetValue(item.RackLogicalId.Value, out var rackRule)
                        ? rackRule
                        : floorDecision.Allowed
                            ? RuleSet.For(scope, floorDecision.MatchedGrantIds)
                            : null,
            })
            .Where(item => item.Rules is not null)
            .Select(item => new SpacePortalLocationDto(
                item.Item.Revision.LogicalId,
                item.Item.FloorLogicalId,
                item.Item.RackLogicalId,
                item.Rules!.Text("location.code", item.Item.LocationCode),
                item.Rules.Scalar("location.position", item.Item.ColumnNo),
                item.Rules.Scalar("location.position", item.Item.LevelNo),
                item.Rules.Scalar("location.position", item.Item.DepthNo),
                item.Rules.Scalar("location.dimensions", item.Item.Width),
                item.Rules.Scalar("location.dimensions", item.Item.Height),
                item.Rules.Scalar("location.dimensions", item.Item.Depth),
                item.Rules.Scalar("location.maxLoad", item.Item.MaxLoad),
                item.Rules.Text(
                    "location.externalBindingState",
                    item.Item.ExternalBindingState)))
            .ToArray();
        var elements = floorDecision.Allowed
            ? scene.Elements
                .Where(item => item.Revision.LifecycleState == "Active")
                .Select(item => new SpacePortalElementDto(
                    item.Revision.LogicalId,
                    item.FloorLogicalId,
                    item.ParentLogicalId,
                    floorRules.Text("element.type", item.ElementType),
                    floorRules.Text("element.geometryJson", item.GeometryJson),
                    floorRules.Scalar("element.modelAssetId", item.ModelAssetId),
                    floorRules.Text("element.modelAssetScope", item.ModelAssetScope),
                    floorRules.Text("element.businessCode", item.BusinessCode),
                    floorRules.Text("element.linkedEntityType", item.LinkedEntityType),
                    floorRules.Scalar("element.linkedLogicalId", item.LinkedLogicalId)))
                .ToArray()
            : [];

        return new SpacePortalFloorDto(
            scene.Floor.Revision.LogicalId,
            floorRules.Scalar("floor.level", scene.Floor.Level),
            floorRules.Text("floor.code", scene.Floor.FloorCode),
            floorRules.Text("floor.name", scene.Floor.Name),
            floorRules.Scalar("floor.elevation", scene.Floor.Elevation),
            floorRules.Scalar("floor.height", scene.Floor.Height),
            floorRules.Text("floor.boundaryJson", scene.Floor.BoundaryJson),
            floorRules.Text(
                "floor.coordinateSystem",
                scene.Floor.CoordinateSystem),
            activeZones.Select(item =>
            {
                var rules = RuleSet.For(
                    scope,
                    item.Decision.MatchedGrantIds);
                return new SpacePortalZoneDto(
                    item.Item.Revision.LogicalId,
                    item.Item.FloorLogicalId,
                    rules.Text("zone.code", item.Item.ZoneCode),
                    rules.Scalar("zone.type", item.Item.ZoneType),
                    rules.Text("zone.polygonJson", item.Item.PolygonJson),
                    rules.Text("zone.color", item.Item.Color),
                    rules.Text(
                        "zone.capabilityFlags",
                        item.Item.CapabilityFlags));
            }).ToArray(),
            aisles,
            racks,
            rackLevels,
            locations,
            elements);
    }

    private static SpaceAccessDecision Evaluate(
        SpaceQueryScope scope,
        Guid siteId,
        Guid floorId,
        Guid? zoneId) =>
        SpaceAccessScopeMatcher.Evaluate(
            scope,
            SpaceAccessAction.Read,
            new SpaceResource(
                scope.TenantId,
                SpaceResourceType.PublishedScene,
                siteId,
                floorId,
                zoneId));

    private static IEnumerable<SpaceGrantClause> EligibleClauses(
        SpaceQueryScope scope,
        Guid siteId) =>
        scope.Clauses.Where(item =>
            item.SiteId == siteId &&
            item.FieldPolicyVersion > 0 &&
            item.FieldRules.Any(field =>
                field.ResourceType == scope.ResourceType));

    private static bool HasPolicy(SpaceQueryScope scope, Guid siteId) =>
        EligibleClauses(scope, siteId).Any();

    private static SpaceQueryScope PortalScope(
        SpaceQueryScope scope,
        Guid siteId) =>
        scope with { Clauses = EligibleClauses(scope, siteId).ToArray() };

    private static bool CanExport(
        SpaceQueryScope scene,
        SpaceQueryScope stock,
        SpaceQueryScope tasks,
        Guid siteId) =>
        new[] { scene, stock, tasks }
            .SelectMany(scope => EligibleClauses(scope, siteId))
            .Any(item => item.CanExport && item.FieldPolicyCanExport);

    private static string CombinedAuthorizationVersion(
        params SpaceQueryScope[] scopes)
    {
        var material = string.Join(
            ':',
            scopes.Select(item => item.AuthorizationVersion));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static void EnsureSitePolicy(SpaceQueryScope scope, Guid siteId)
    {
        if (siteId == Guid.Empty || !HasPolicy(scope, siteId))
            throw ScopeNotFound();
    }

    private static SpacePortalRuntimeSourceDto Source(
        SpaceWmsRuntimeSourceDto source) =>
        new(
            source.ObservedAtUtc,
            source.ReceivedAtUtc,
            source.DelayMilliseconds,
            source.IsAvailable);

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static DateTimeOffset UtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static SpaceProblemException OrganizationRequired() =>
        new(
            SpaceErrorCodes.ExternalOrganizationContextRequired,
            403,
            "An external organization context is required.",
            recoveryAction: "select-organization-context");

    private static SpaceProblemException ScopeNotFound() =>
        new(
            SpaceErrorCodes.ExternalScopeDenied,
            404,
            "The requested Portal resource was not found.",
            recoveryAction: "select-visible-resource");

    private sealed record PublishedScope(Guid SiteId, Guid VersionId);

    private sealed record CandidateLocation(
        Guid LocationLogicalId,
        Guid FloorLogicalId,
        Guid? ZoneLogicalId);

    private sealed class RuleSet
    {
        private readonly IReadOnlyDictionary<string, SpaceFieldMaskingRule> _rules;

        private RuleSet(IReadOnlyDictionary<string, SpaceFieldMaskingRule> rules)
        {
            _rules = rules;
        }

        public static RuleSet For(
            SpaceQueryScope scope,
            IReadOnlyList<Guid> grantIds)
        {
            var ids = grantIds.ToHashSet();
            var rules = scope.Clauses
                .Where(item => ids.Contains(item.GrantId))
                .SelectMany(item => item.FieldRules)
                .Where(item => item.ResourceType == scope.ResourceType)
                .GroupBy(item => item.FieldName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Min(item => item.MaskingRule),
                    StringComparer.OrdinalIgnoreCase);
            return new RuleSet(rules);
        }

        public string? Text(string fieldName, string? value)
        {
            if (value is null || !_rules.TryGetValue(fieldName, out var rule))
                return null;
            return rule switch
            {
                SpaceFieldMaskingRule.None => value,
                SpaceFieldMaskingRule.Partial => Partial(value),
                SpaceFieldMaskingRule.Hash => Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(value))),
                _ => null,
            };
        }

        public T? Scalar<T>(string fieldName, T value)
            where T : struct =>
            _rules.TryGetValue(fieldName, out var rule) &&
            rule == SpaceFieldMaskingRule.None
                ? value
                : null;

        public T? Scalar<T>(string fieldName, T? value)
            where T : struct =>
            value.HasValue &&
            _rules.TryGetValue(fieldName, out var rule) &&
            rule == SpaceFieldMaskingRule.None
                ? value
                : null;

        private static string Partial(string value)
        {
            if (value.Length <= 2)
                return new string('*', value.Length);
            if (value.Length <= 4)
                return $"{value[0]}{new string('*', value.Length - 1)}";
            return $"{value[..2]}***{value[^2..]}";
        }
    }
}

public sealed class SpacePublishedSceneReader(
    ISpaceDesignV1Service design) : ISpacePublishedSceneReader
{
    public Task<SpaceDesignSceneDto> GetSceneAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default) =>
        design.GetSceneAsync(versionId, floorLogicalId, cancellationToken);
}
