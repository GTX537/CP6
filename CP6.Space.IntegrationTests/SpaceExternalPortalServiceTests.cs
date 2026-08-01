using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExternalPortalServiceTests
{
    [Fact]
    public async Task Organizations_and_sites_expose_only_active_membership_capabilities()
    {
        await using var fixture = await Fixture.CreateAsync();
        var organizations = await fixture.CreateService(
            organizationContextId: null).GetOrganizationsAsync();
        var sites = await fixture.Service.GetSitesAsync();

        var organization = Assert.Single(organizations);
        Assert.Equal(fixture.OrganizationId, organization.OrganizationId);
        Assert.Equal("Customer", organization.Type);
        var site = Assert.Single(sites);
        Assert.Equal(fixture.SiteId, site.SiteId);
        Assert.Equal(fixture.PublishedVersionId, site.PublishedVersionId);
        Assert.True(site.CanViewScene);
        Assert.True(site.CanViewStock);
        Assert.True(site.CanViewTasks);
        Assert.True(site.CanExport);
        Assert.Equal(64, site.AuthorizationVersion.Length);
    }

    [Fact]
    public async Task Published_scene_projects_only_allowed_fields_and_spatial_scope()
    {
        await using var fixture = await Fixture.CreateAsync();

        var response = await fixture.Service.GetPublishedSceneAsync(fixture.SiteId);

        Assert.Equal(fixture.PublishedVersionId, response.PublishedVersionId);
        var floor = Assert.Single(response.Floors);
        Assert.Null(floor.Code);
        Assert.Null(floor.Name);
        var zone = Assert.Single(floor.Zones);
        Assert.Equal("ZO***-A", zone.Code);
        Assert.Null(zone.Color);
        Assert.Single(floor.Racks);
        Assert.Single(floor.Locations);
        Assert.Empty(floor.Elements);
    }

    [Fact]
    public async Task Stock_is_owner_filtered_and_unknown_fields_are_omitted()
    {
        await using var fixture = await Fixture.CreateAsync();

        var response = await fixture.Service.GetStockAsync(fixture.SiteId);

        var item = Assert.Single(response.Items);
        Assert.Equal("OWNER-A", item.OwnerId);
        Assert.Equal("MA***01", item.MaterialNumber);
        Assert.Equal(8m, item.PhysicalQuantity);
        Assert.Null(item.LotNumber);
        Assert.Null(item.FloorName);
        Assert.Equal([fixture.LocationId], fixture.Runtime.InventoryLocationIds);
    }

    [Fact]
    public async Task Tasks_are_object_filtered_and_text_hashing_is_deterministic()
    {
        await using var fixture = await Fixture.CreateAsync();

        var response = await fixture.Service.GetTasksAsync(fixture.SiteId);

        var item = Assert.Single(response.Items);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("TASK-1"))),
            item.TaskId);
        Assert.Equal("Released", item.Status);
        Assert.Equal(2m, item.Quantity);
        Assert.Null(item.MaterialNumber);
        Assert.Equal([fixture.LocationId], fixture.Runtime.TaskLocationIds);
    }

    [Fact]
    public async Task Retired_policy_removes_portal_capability_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ScenePolicy.Update(
            fixture.ScenePolicy.Name,
            fixture.ScenePolicy.CanExport,
            SpaceFieldPolicyStatus.Retired);
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetPublishedSceneAsync(fixture.SiteId));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.ExternalScopeDenied, error.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTime Now =
            new(2026, 8, 1, 18, 0, 0, DateTimeKind.Utc);

        private Fixture(
            SpaceContext context,
            ExternalExecution execution,
            FixedClock clock,
            Guid organizationId,
            Guid siteId,
            Guid publishedVersionId,
            Guid locationId,
            SpaceFieldPolicy scenePolicy,
            SceneReader scenes,
            RuntimeService runtime)
        {
            Context = context;
            Execution = execution;
            Clock = clock;
            OrganizationId = organizationId;
            SiteId = siteId;
            PublishedVersionId = publishedVersionId;
            LocationId = locationId;
            ScenePolicy = scenePolicy;
            Scenes = scenes;
            Runtime = runtime;
            Service = CreateService(organizationId);
        }

        public SpaceContext Context { get; }
        public ExternalExecution Execution { get; }
        public FixedClock Clock { get; }
        public Guid OrganizationId { get; }
        public Guid SiteId { get; }
        public Guid PublishedVersionId { get; }
        public Guid LocationId { get; }
        public SpaceFieldPolicy ScenePolicy { get; }
        public SceneReader Scenes { get; }
        public RuntimeService Runtime { get; }
        public SpaceExternalPortalService Service { get; }

        public SpaceExternalPortalService CreateService(Guid? organizationContextId)
        {
            var execution = new ExternalExecution(
                Execution.TenantId,
                Execution.ActorId,
                organizationContextId);
            return new SpaceExternalPortalService(
                Context,
                execution,
                Clock,
                new SpaceAccessEvaluator(Context, execution, Clock),
                Scenes,
                Runtime);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var floorId = Guid.NewGuid();
            var zoneId = Guid.NewGuid();
            var rackId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            var execution = new ExternalExecution(tenantId, userId, null);
            var clock = new FixedClock(Now);
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid().ToString("N"),
                        SpaceTestDatabaseRoots.InMemory)
                    .Options,
                execution,
                clock);
            var organization = SpaceExternalOrganization.Create(
                tenantId,
                SpaceExternalOrganizationType.Customer,
                "CUST-A",
                "Customer A");
            var membership = SpaceExternalMembership.Create(
                tenantId,
                organization.Id,
                userId,
                SpaceExternalMembershipRole.Viewer,
                Now.AddDays(-1),
                null,
                SpaceExternalMembershipStatus.Active,
                null,
                Now);
            var model = SpaceModel.Create(tenantId, siteId);
            var version = SpaceModelVersion.CreateDraft(
                tenantId,
                model.Id,
                1,
                "External portal");
            var floor = SpaceFloorRevision.Create(
                tenantId,
                version.Id,
                floorId,
                siteId,
                1,
                "F1",
                "Floor 1");
            var zone = SpaceZoneRevision.Create(
                tenantId,
                version.Id,
                zoneId,
                floorId,
                "ZONE-A",
                1);
            var rack = SpaceRackRevision.Create(
                tenantId,
                version.Id,
                rackId,
                floorId,
                zoneId,
                "RACK-1");
            rack.ConfigureGeometry(0, 0, 0, 0, 1_000, 1_000, 2_000);
            var location = SpaceLocationRevision.Create(
                tenantId,
                version.Id,
                locationId,
                floorId,
                rackId,
                "L-001",
                1,
                1,
                1,
                1_000,
                1_000,
                1_000);
            var (scenePolicy, sceneFields) = Policy(
                tenantId,
                "Scene portal",
                SpaceResourceType.PublishedScene,
                [
                    ("floor.code", SpaceFieldMaskingRule.None),
                    ("zone.code", SpaceFieldMaskingRule.Partial),
                    ("rack.code", SpaceFieldMaskingRule.None),
                    ("location.code", SpaceFieldMaskingRule.None),
                ]);
            var (stockPolicy, stockFields) = Policy(
                tenantId,
                "Stock portal",
                SpaceResourceType.Stock,
                [
                    ("ownerId", SpaceFieldMaskingRule.None),
                    ("materialNumber", SpaceFieldMaskingRule.Partial),
                    ("physicalQuantity", SpaceFieldMaskingRule.None),
                ]);
            var (taskPolicy, taskFields) = Policy(
                tenantId,
                "Task portal",
                SpaceResourceType.Task,
                [
                    ("taskId", SpaceFieldMaskingRule.Hash),
                    ("status", SpaceFieldMaskingRule.None),
                    ("quantity", SpaceFieldMaskingRule.None),
                ]);
            var sceneGrant = Grant(
                tenantId,
                organization.Id,
                siteId,
                scenePolicy.Id,
                canExport: true);
            var stockGrant = Grant(
                tenantId,
                organization.Id,
                siteId,
                stockPolicy.Id);
            var taskGrant = Grant(
                tenantId,
                organization.Id,
                siteId,
                taskPolicy.Id);

            context.AddRange(
                organization,
                membership,
                model,
                version,
                floor,
                zone,
                rack,
                location,
                scenePolicy,
                stockPolicy,
                taskPolicy,
                sceneGrant,
                stockGrant,
                taskGrant,
                SpaceExternalGrantZone.Create(tenantId, sceneGrant.Id, zoneId),
                SpaceExternalGrantZone.Create(tenantId, stockGrant.Id, zoneId),
                SpaceExternalGrantOwner.Create(
                    tenantId,
                    stockGrant.Id,
                    "OWNER-A"),
                SpaceExternalGrantZone.Create(tenantId, taskGrant.Id, zoneId),
                SpaceExternalGrantObject.Create(
                    tenantId,
                    taskGrant.Id,
                    "task",
                    "TASK-1"));
            context.AddRange(sceneFields);
            context.AddRange(stockFields);
            context.AddRange(taskFields);
            await context.SaveChangesAsync();

            var hash = new string('b', 64);
            version.BeginValidation();
            version.MarkReady(hash, "space-v1", hash);
            version.BeginPublishing();
            version.MarkPublished(userId, Now);
            model.SetPublishedVersion(version, hash);
            await context.SaveChangesAsync();

            execution.OrganizationContextId = organization.Id;
            var scenes = new SceneReader(Scene(
                siteId,
                version.Id,
                floorId,
                zoneId,
                rackId,
                locationId));
            var runtime = new RuntimeService(
                siteId,
                version.Id,
                floorId,
                zoneId,
                rackId,
                locationId);
            return new Fixture(
                context,
                execution,
                clock,
                organization.Id,
                siteId,
                version.Id,
                locationId,
                scenePolicy,
                scenes,
                runtime);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        private static (
            SpaceFieldPolicy Policy,
            SpaceFieldPolicyField[] Fields) Policy(
            Guid tenantId,
            string name,
            SpaceResourceType resource,
            IReadOnlyList<(string Name, SpaceFieldMaskingRule Rule)> fields)
        {
            var policy = SpaceFieldPolicy.Create(
                tenantId,
                name,
                SpaceExternalOrganizationType.Customer,
                canExport: true);
            var policyResource = resource switch
            {
                SpaceResourceType.PublishedScene =>
                    SpaceFieldPolicyResourceType.PublishedScene,
                SpaceResourceType.Stock => SpaceFieldPolicyResourceType.Stock,
                SpaceResourceType.Task => SpaceFieldPolicyResourceType.Task,
                _ => throw new InvalidOperationException(),
            };
            return (
                policy,
                fields.Select(item => SpaceFieldPolicyField.Create(
                    tenantId,
                    policy.Id,
                    policyResource,
                    item.Name,
                    item.Rule)).ToArray());
        }

        private static SpaceExternalGrant Grant(
            Guid tenantId,
            Guid organizationId,
            Guid siteId,
            Guid policyId,
            bool canExport = false) =>
            SpaceExternalGrant.Create(
                tenantId,
                organizationId,
                siteId,
                policyId,
                canExport,
                Now.AddDays(-1),
                null,
                SpaceExternalGrantStatus.Active);

        private static SpaceDesignSceneDto Scene(
            Guid siteId,
            Guid versionId,
            Guid floorId,
            Guid zoneId,
            Guid rackId,
            Guid locationId)
        {
            static SpaceSceneRevisionDto Revision(Guid id) =>
                new(Guid.NewGuid(), id, Guid.NewGuid(), "internal", "Active", "rv");
            return new SpaceDesignSceneDto(
                1,
                "DesignRevision",
                false,
                versionId,
                siteId,
                "Published",
                10,
                new string('c', 64),
                new SpaceSceneFloorDto(
                    Revision(floorId), siteId, 1, "F1", "Floor 1", 0, 5_000,
                    "[]", "LOCAL", Guid.NewGuid(), Guid.NewGuid(), 1, 0, 0, 0, 8),
                [new SpaceSceneZoneDto(
                    Revision(zoneId), floorId, "ZONE-A", 1, "[]", "#fff", "all")],
                [],
                [new SpaceSceneRackDto(
                    Revision(rackId), floorId, zoneId, null, "RACK-1", null,
                    0, 0, 0, 0, 1_000, 1_000, 2_000)],
                [],
                [new SpaceSceneLocationDto(
                    Revision(locationId), floorId, rackId, "L-001", 1, 1, 1,
                    1_000, 1_000, 1_000, null, "Design", "Bound")],
                [],
                []);
        }
    }

    private sealed class ExternalExecution(
        Guid tenantId,
        Guid actorId,
        Guid? organizationContextId) : ISpaceExecutionContext
    {
        public Guid TenantId { get; } = tenantId;
        public Guid ActorId { get; } = actorId;
        public bool IsExternal => true;
        public Guid? OrganizationContextId { get; set; } = organizationContextId;
    }

    private sealed class FixedClock(DateTime now) : ISpaceClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private sealed class SceneReader(SpaceDesignSceneDto scene) :
        ISpacePublishedSceneReader
    {
        public Task<SpaceDesignSceneDto> GetSceneAsync(
            Guid versionId,
            Guid floorLogicalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(scene);
    }

    private sealed class RuntimeService(
        Guid siteId,
        Guid versionId,
        Guid floorId,
        Guid zoneId,
        Guid rackId,
        Guid locationId) : ISpaceWmsRuntimeService
    {
        private static readonly SpaceWmsRuntimeSourceDto Source = new(
            "Real", "test", "wms", new DateTimeOffset(Fixture.Now),
            new DateTimeOffset(Fixture.Now), 0, 0, false, true);

        public IReadOnlyCollection<Guid>? InventoryLocationIds { get; private set; }
        public IReadOnlyCollection<Guid>? TaskLocationIds { get; private set; }

        public Task<SpaceWmsRuntimeInventoryResponse> QueryInventoryAsync(
            Guid requestedSiteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
            CancellationToken cancellationToken = default)
        {
            InventoryLocationIds = locationLogicalIds;
            return Task.FromResult(new SpaceWmsRuntimeInventoryResponse(
                siteId,
                versionId,
                "WH1",
                Source,
                [
                    new(locationId, Guid.NewGuid(), "L-001", "WMS-001", true,
                        floorId, "F1", "Floor 1", 1, 8, 1, "MAT-01", "LOT-1",
                        "CONT-1", "OWNER-A"),
                    new(locationId, Guid.NewGuid(), "L-001", "WMS-001", true,
                        floorId, "F1", "Floor 1", 1, 3, 0, "MAT-02", null,
                        null, "OWNER-B"),
                ]));
        }

        public Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
            Guid requestedSiteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
            CancellationToken cancellationToken = default)
        {
            TaskLocationIds = locationLogicalIds;
            return Task.FromResult(new SpaceWmsRuntimeTaskResponse(
                siteId,
                versionId,
                "WH1",
                Source,
                [
                    new("TASK-1", "Pick", "Released", 1, locationId,
                        Guid.NewGuid(), "L-001", "WMS-001", true, floorId,
                        "F1", "Floor 1", 1, zoneId, "ZONE-A", rackId, "RACK-1",
                        0, 0, 0, 2, "MAT-01"),
                    new("TASK-2", "Pick", "Released", 2, locationId,
                        Guid.NewGuid(), "L-001", "WMS-001", true, floorId,
                        "F1", "Floor 1", 1, zoneId, "ZONE-A", rackId, "RACK-1",
                        0, 0, 0, 1, "MAT-02"),
                ]));
        }

        public Task<SpaceWmsRuntimeInventoryLocateResponse> LocateInventoryAsync(
            Guid requestedSiteId,
            SpaceWmsInventoryLocateCriteria criteria,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceWmsRuntimeTaskPathResponse> GetTaskPathAsync(
            Guid requestedSiteId,
            string taskId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
