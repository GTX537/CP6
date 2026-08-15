using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceDesignSceneSqlServerTests
{
    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [SqlServerFact]
    public async Task Published_viewer_scene_uses_only_current_published_pointer()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedPublishedModelWithFloorAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Unpublished changes",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var draftFloor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                2,
                "DRAFT",
                "Draft-only floor",
                height: 6000);
            context.AddRange(draft, draftFloor);
            await context.SaveChangesAsync();

            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId);

            var scene = await service.GetPublishedSceneAsync(
                seeded.Model.SiteId);

            Assert.Equal(seeded.Model.SiteId, scene.SiteId);
            Assert.Equal(seeded.Published.Id, scene.PublishedVersionId);
            Assert.Equal(SpaceDesignSceneContract.Authority, scene.Authority);
            Assert.False(scene.RuntimeOverlayIncluded);
            var floor = Assert.Single(scene.Floors);
            Assert.Equal(seeded.Published.Id, floor.ModelVersionId);
            Assert.Equal(
                seeded.PublishedFloor.LogicalId,
                floor.Floor.Revision.LogicalId);
            Assert.Equal("PUB", floor.Floor.FloorCode);
            Assert.DoesNotContain(
                scene.Floors,
                item => item.Floor.Revision.LogicalId == draftFloor.LogicalId);
        });
    }

    [SqlServerFact]
    public async Task Scene_uses_revision_authority_without_runtime_overlay()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            Guid siteId;
            Guid versionId;
            Guid floorLogicalId;
            SpaceAssetVersion modelAssetVersion;

            await using (var context = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var seeded = await SeedDesignModelAsync(
                    context,
                    execution.ActorId,
                    clock.UtcNow);
                siteId = seeded.Model.SiteId;
                var draft = SpaceModelVersion.CreateDraft(
                    execution.TenantId,
                    seeded.Model.Id,
                    2,
                    "Unified semantic scene",
                    seeded.Published.Id);
                seeded.Model.ReserveDraft(draft);

                var floor = SpaceFloorRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    siteId,
                    1,
                    "F1",
                    "Floor 1",
                    elevation: 0,
                    height: 6000);
                floor.ConfigureBoundary(
                    """
                    {"schemaVersion":1,"kind":"polygon","points":[[0,0],[10000,0],[10000,8000],[0,8000]]}
                    """,
                    "RH_Z_UP_MM");
                var zone = SpaceZoneRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    "Z1",
                    1);
                zone.ConfigureShape(
                    """
                    {"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}
                    """);
                var aisle = SpaceAisleRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    zone.LogicalId,
                    "A1",
                    1);
                aisle.ConfigureShape(
                    """
                    {"schemaVersion":1,"points":[[0,0],[1000,0],[1000,8000],[0,8000]]}
                    """,
                    """
                    {"schemaVersion":1,"points":[[500,0],[500,8000]]}
                    """);
                var rack = SpaceRackRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    zone.LogicalId,
                    "R1",
                    aisle.LogicalId);
                rack.ConfigureGeometry(
                    1000,
                    2000,
                    0,
                    90,
                    4800,
                    2200,
                    5000);
                var lower = SpaceRackLevelRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    rack.LogicalId,
                    levelNo: 1,
                    bottomZ: 0,
                    clearHeight: 1200,
                    binCount: 4,
                    depthCount: 1,
                    cellWidth: 1000,
                    cellDepth: 900,
                    maxLoad: 1500m,
                    beamHeight: 100);
                var upper = SpaceRackLevelRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    rack.LogicalId,
                    levelNo: 2,
                    bottomZ: 1300,
                    clearHeight: 800,
                    binCount: 3,
                    depthCount: 2,
                    cellWidth: 1200,
                    cellDepth: 1100,
                    maxLoad: 750m,
                    beamHeight: 80);
                var location = SpaceLocationRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    rack.LogicalId,
                    "F1-R1-01-01",
                    columnNo: 1,
                    levelNo: 1,
                    depthNo: 1,
                    width: 1000,
                    height: 1200,
                    depth: 900,
                    maxLoad: 1500m,
                    locationType: SpaceLocationTypes.Picking);
                var excelSource = SpaceModelSource.CreateInlineSource(
                    execution.TenantId,
                    draft.Id,
                    SpaceSourceType.Editor,
                    "Scene metadata fixture",
                    new string('e', 64));
                var locationBinding = SpaceLocationExternalBinding.Create(
                    execution.TenantId,
                    Guid.NewGuid(),
                    location,
                    "wms-adapter",
                    "WH-001",
                    "EXT-F1-R1-01-01",
                    SpaceLocationBindingMode.WmsPrimary,
                    excelSource,
                    "Bindings!2");
                var rackAttribute = SpaceDesignAttribute.Create(
                    execution.TenantId,
                    Guid.NewGuid(),
                    draft.Id,
                    SpaceDesignAttributeObjectTypes.Rack,
                    rack.LogicalId,
                    SpaceDesignAttributeNamespaces.Custom,
                    "temperatureClass",
                    "Ambient",
                    null,
                    excelSource,
                    "Attributes!2");
                var element = SpaceElementRevision.Create(
                    execution.TenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    SpaceElementTypes.Column,
                    """
                    {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                    """);
                var modelAsset = SpaceAsset.CreateSystem(
                    "SYS-COLUMN",
                    "System Column",
                    "Structure",
                    null,
                    execution.ActorId,
                    clock.UtcNow);
                modelAssetVersion = SpaceAssetVersion.CreateReady(
                    modelAsset,
                    1,
                    SpaceAssetFormat.Glb,
                    "{}",
                    "assets/column.png",
                    "assets/column.glb",
                    new string('c', 64),
                    execution.ActorId,
                    clock.UtcNow);
                element.AttachAsset(modelAssetVersion);
                element.ConfigurePlacement(
                    500,
                    500,
                    0,
                    0,
                    400,
                    5000,
                    400);
                var attribute = SpaceElementAttribute.Create(
                    execution.TenantId,
                    element,
                    SpaceElementAttributeNamespaces.Manufacturer,
                    "material",
                    "String",
                    "steel");

                context.AddRange(
                    draft,
                    floor,
                    zone,
                    aisle,
                    rack,
                    lower,
                    upper,
                    location,
                    excelSource,
                    locationBinding,
                    rackAttribute,
                    modelAsset,
                    modelAssetVersion,
                    element,
                    attribute);
                await context.SaveChangesAsync();

                versionId = draft.Id;
                floorLogicalId = floor.LogicalId;
                var service = NewService(
                    context,
                    execution,
                    clock,
                    siteId);
                var scene = await service.GetSceneAsync(
                    versionId,
                    floorLogicalId);

                Assert.Equal(
                    SpaceDesignSceneContract.SchemaVersion,
                    scene.SchemaVersion);
                Assert.Equal(
                    SpaceDesignSceneContract.Authority,
                    scene.Authority);
                Assert.False(scene.RuntimeOverlayIncluded);
                Assert.Equal(versionId, scene.ModelVersionId);
                Assert.Equal(siteId, scene.SiteId);
                Assert.Equal("Draft", scene.VersionStatus);
                Assert.Equal(floorLogicalId, scene.Floor.Revision.LogicalId);
                Assert.Equal("RH_Z_UP_MM", scene.Floor.CoordinateSystem);
                Assert.Single(scene.Zones);
                Assert.Single(scene.Aisles);
                Assert.Single(scene.Racks);
                Assert.Collection(
                    scene.RackLevels,
                    level =>
                    {
                        Assert.Equal(1, level.LevelNo);
                        Assert.Equal(1200, level.ClearHeight);
                        Assert.Equal(100, level.BeamHeight);
                        Assert.Equal(1500m, level.MaxLoad);
                    },
                    level =>
                    {
                        Assert.Equal(2, level.LevelNo);
                        Assert.Equal(800, level.ClearHeight);
                        Assert.Equal(2, level.DepthCount);
                        Assert.Equal(80, level.BeamHeight);
                        Assert.Equal(750m, level.MaxLoad);
                    });
                Assert.Single(scene.Locations);
                Assert.Equal(
                    SpaceLocationTypes.Picking,
                    scene.Locations[0].LocationType);
                var binding = Assert.Single(scene.LocationExternalBindings!);
                Assert.Equal(location.LogicalId, binding.LocationLogicalId);
                Assert.Equal("wms-adapter", binding.AdapterId);
                Assert.Equal("EXT-F1-R1-01-01", binding.ExternalLocationId);
                Assert.Equal("WmsPrimary", binding.BindingMode);
                var designAttribute = Assert.Single(scene.DesignAttributes!);
                Assert.Equal(
                    SpaceDesignAttributeObjectTypes.Rack,
                    designAttribute.ObjectType);
                Assert.Equal(rack.LogicalId, designAttribute.ObjectLogicalId);
                Assert.Equal("temperatureClass", designAttribute.Key);
                Assert.Equal("Ambient", designAttribute.Value);
                Assert.Single(scene.Elements);
                Assert.Equal(
                    SpaceElementTypes.Column,
                    scene.Elements[0].ElementType);
                Assert.Equal(
                    modelAssetVersion.Id,
                    scene.Elements[0].ModelAssetId);
                Assert.Equal(
                    "System",
                    scene.Elements[0].ModelAssetScope);
                Assert.Single(scene.ElementAttributes);
                Assert.Equal(
                    scene.Elements[0].Revision.RevisionId,
                    scene.ElementAttributes[0].ElementRevisionId);
                Assert.Equal(
                    SpaceElementAttributeNamespaces.Manufacturer,
                    scene.ElementAttributes[0].Namespace);

                var wrongVersionFloor =
                    await Assert.ThrowsAsync<SpaceProblemException>(
                        () => service.GetSceneAsync(
                            seeded.Published.Id,
                            floorLogicalId));
                Assert.Equal(
                    SpaceErrorCodes.LogicalIdNotFound,
                    wrongVersionFloor.Code);
                Assert.Equal(404, wrongVersionFloor.StatusCode);
            }

            var otherExecution = execution with
            {
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid(),
            };
            await using var otherContext = CreateContext(
                connectionString,
                otherExecution,
                clock);
            var otherService = NewService(
                otherContext,
                otherExecution,
                clock,
                siteId);
            var crossTenant = await Assert.ThrowsAsync<SpaceProblemException>(
                () => otherService.GetSceneAsync(
                    versionId,
                    floorLogicalId));
            Assert.Equal(
                SpaceErrorCodes.VersionNotFound,
                crossTenant.Code);
            Assert.Equal(404, crossTenant.StatusCode);
        });
    }

    [SqlServerFact]
    public async Task Element_commands_update_delete_replay_and_audit_atomically()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedDesignModelAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Element editing",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1",
                height: 6000);
            var element = SpaceElementRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                SpaceElementTypes.Column,
                """
                {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                """);
            element.ConfigurePlacement(1000, 2000, 0, 0, 400, 5000, 400);
            var attribute = SpaceElementAttribute.Create(
                execution.TenantId,
                element,
                SpaceElementAttributeNamespaces.Design,
                "label",
                SpaceElementAttributeValueTypes.String,
                "Column A");
            context.AddRange(draft, floor, element, attribute);
            await context.SaveChangesAsync();

            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId);
            var clientId = Guid.NewGuid();
            var editLease = SpaceEditLease.Create(
                execution.TenantId,
                draft.Id,
                floor.LogicalId,
                execution.ActorId,
                "Editor",
                clientId,
                clock.UtcNow,
                TimeSpan.FromSeconds(90));
            context.EditLeases.Add(editLease);
            await context.SaveChangesAsync();
            var update = new ApplySpaceElementCommandBatchRequest(
                SpaceElementCommandContract.SchemaVersion,
                Guid.NewGuid(),
                clientId,
                editLease.LeaseId,
                ExpectedFloorRevision: 0,
                [
                    new SpaceElementCommandDto(
                        Guid.NewGuid(),
                        SpaceElementCommandContract.UpdateProperties,
                        element.LogicalId,
                        new SpaceUpdateElementPropertiesDto(
                            """
                            {"schemaVersion":1,"kind":"box","width":600,"height":5200,"depth":500}
                            """,
                            X: 1200,
                            Y: 2200,
                            Z: 0,
                            RotationZ: 90,
                            Width: 600,
                            Height: 5200,
                            Depth: 500,
                            BusinessCode: "C-100",
                            LinkedEntityType: null,
                            LinkedLogicalId: null,
                            [
                                new SpaceElementAttributeWriteDto(
                                    SpaceElementAttributeNamespaces.Design,
                                    "label",
                                    SpaceElementAttributeValueTypes.String,
                                    "Column B",
                                    null),
                                new SpaceElementAttributeWriteDto(
                                    SpaceElementAttributeNamespaces.Manufacturer,
                                    "material",
                                    SpaceElementAttributeValueTypes.String,
                                    "steel",
                                    null),
                            ],
                            ElementType: SpaceElementTypes.Door))
                ]);

            var updated = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                update);
            var replay = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                update);

            Assert.Equal(1, updated.FloorRevision);
            Assert.Equal(1, updated.VersionContentRevision);
            Assert.False(updated.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(updated.FloorRevision, replay.FloorRevision);
            Assert.Single(updated.AffectedObjects);
            Assert.Equal(1200, updated.AffectedObjects[0].Element.X);
            Assert.Equal(
                SpaceElementTypes.Door,
                updated.AffectedObjects[0].Element.ElementType);
            Assert.Equal("C-100", updated.AffectedObjects[0].Element.BusinessCode);
            Assert.Equal(2, updated.AffectedObjects[0].Attributes.Count);
            Assert.Single(context.ElementCommandBatches);
            Assert.Single(context.ElementCommandRecords);
            var audit = await context.ElementCommandRecords
                .AsNoTracking()
                .SingleAsync();
            Assert.Contains("\"businessCode\":null", audit.BeforeJson);
            Assert.Contains("\"businessCode\":\"C-100\"", audit.AfterJson);
            Assert.Contains("\"elementType\":\"Door\"", audit.AfterJson);
            Assert.Contains("\"label\"", audit.AfterJson);

            var unsupportedRetype = update with
            {
                CommandBatchId = Guid.NewGuid(),
                ExpectedFloorRevision = 1,
                Commands =
                [
                    update.Commands[0] with
                    {
                        CommandId = Guid.NewGuid(),
                        UpdateProperties =
                            update.Commands[0].UpdateProperties! with
                            {
                                ElementType = "LiveRobot",
                            },
                    },
                ],
            };
            var unsupportedProblem =
                await Assert.ThrowsAsync<SpaceProblemException>(
                    () => service.ApplyElementCommandsAsync(
                        draft.Id,
                        floor.LogicalId,
                        unsupportedRetype));
            Assert.Equal(SpaceErrorCodes.RequestInvalid, unsupportedProblem.Code);
            Assert.Equal(400, unsupportedProblem.StatusCode);
            Assert.Single(context.ElementCommandBatches);

            var stale = new ApplySpaceElementCommandBatchRequest(
                SpaceElementCommandContract.SchemaVersion,
                Guid.NewGuid(),
                update.ClientInstanceId,
                editLease.LeaseId,
                ExpectedFloorRevision: 0,
                [
                    new SpaceElementCommandDto(
                        Guid.NewGuid(),
                        SpaceElementCommandContract.DeleteObject,
                        element.LogicalId,
                        null)
                ]);
            var staleProblem = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyElementCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    stale));
            Assert.Equal(
                SpaceErrorCodes.FloorRevisionConflict,
                staleProblem.Code);
            Assert.Equal(409, staleProblem.StatusCode);
            Assert.Single(context.ElementCommandBatches);

            var atomicFailure = update with
            {
                CommandBatchId = Guid.NewGuid(),
                ExpectedFloorRevision = 1,
                Commands =
                [
                    update.Commands[0] with
                    {
                        CommandId = Guid.NewGuid(),
                        UpdateProperties =
                            update.Commands[0].UpdateProperties! with
                            {
                                X = 1500,
                            },
                    },
                    new SpaceElementCommandDto(
                        Guid.NewGuid(),
                        SpaceElementCommandContract.DeleteObject,
                        Guid.NewGuid(),
                        null),
                ],
            };
            var atomicProblem = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyElementCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    atomicFailure));
            Assert.Equal(
                SpaceErrorCodes.LogicalIdNotFound,
                atomicProblem.Code);
            Assert.Equal(404, atomicProblem.StatusCode);
            Assert.Single(context.ElementCommandBatches);
            Assert.Single(context.ElementCommandRecords);
            Assert.Equal(
                1200,
                await context.ElementRevisions
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == element.Id)
                    .Select(candidate => candidate.X)
                    .SingleAsync());
            Assert.Equal(
                1,
                await context.FloorRevisions
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == floor.Id)
                    .Select(candidate => candidate.Revision)
                    .SingleAsync());

            var remove = stale with
            {
                CommandBatchId = Guid.NewGuid(),
                ExpectedFloorRevision = 1,
                Commands =
                [
                    stale.Commands[0] with { CommandId = Guid.NewGuid() }
                ],
            };
            var removed = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                remove);
            var scene = await service.GetSceneAsync(
                draft.Id,
                floor.LogicalId);

            Assert.Equal(2, removed.FloorRevision);
            Assert.Equal(2, removed.VersionContentRevision);
            Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                removed.AffectedObjects[0].Element.Revision.LifecycleState);
            Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                scene.Elements[0].Revision.LifecycleState);
            Assert.Equal(2, await context.ElementCommandBatches.CountAsync());
            Assert.Equal(2, await context.ElementCommandRecords.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Element_group_merge_and_saved_compensation_are_atomic()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedDesignModelAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Element exception merge",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1",
                height: 6000);
            var survivor = SpaceElementRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                SpaceElementTypes.Column,
                """
                {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                """);
            survivor.ConfigurePlacement(1000, 2000, 0, 0, 400, 5000, 400);
            var source = SpaceElementRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                SpaceElementTypes.Column,
                """
                {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                """);
            source.ConfigurePlacement(1800, 2000, 0, 0, 400, 5000, 400);
            var survivorAttribute = SpaceElementAttribute.Create(
                execution.TenantId,
                survivor,
                SpaceElementAttributeNamespaces.Design,
                "label",
                SpaceElementAttributeValueTypes.String,
                "CAD exception");
            var sourceAttribute = SpaceElementAttribute.Create(
                execution.TenantId,
                source,
                SpaceElementAttributeNamespaces.Design,
                "label",
                SpaceElementAttributeValueTypes.String,
                "CAD exception");
            context.AddRange(
                draft,
                floor,
                survivor,
                source,
                survivorAttribute,
                sourceAttribute);
            await context.SaveChangesAsync();

            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId);
            var clientId = Guid.NewGuid();
            var lease = SpaceEditLease.Create(
                execution.TenantId,
                draft.Id,
                floor.LogicalId,
                execution.ActorId,
                "Editor",
                clientId,
                clock.UtcNow,
                TimeSpan.FromSeconds(90));
            context.EditLeases.Add(lease);
            await context.SaveChangesAsync();
            var groupGeometry = $$$"""
                {"schemaVersion":1,"kind":"group","parts":[{"sourceLogicalId":"{{{survivor.LogicalId}}}","x":0,"y":0,"z":0,"rotationZ":0,"width":400,"height":5000,"depth":400,"geometry":{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}},{"sourceLogicalId":"{{{source.LogicalId}}}","x":800,"y":0,"z":0,"rotationZ":0,"width":400,"height":5000,"depth":400,"geometry":{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}}]}
                """;
            var attributes = new[]
            {
                new SpaceElementAttributeWriteDto(
                    SpaceElementAttributeNamespaces.Design,
                    "label",
                    SpaceElementAttributeValueTypes.String,
                    "CAD exception",
                    null),
            };
            var merged = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 0,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.UpdateProperties,
                            survivor.LogicalId,
                            new SpaceUpdateElementPropertiesDto(
                                groupGeometry,
                                1000,
                                2000,
                                0,
                                0,
                                1200,
                                5000,
                                400,
                                null,
                                null,
                                null,
                                attributes,
                                SpaceElementTypes.Column)),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.DeleteObject,
                            source.LogicalId,
                            null),
                    ]));

            Assert.Equal(1, merged.FloorRevision);
            Assert.Equal(1, merged.VersionContentRevision);
            Assert.Equal(2, merged.AffectedObjects.Count);
            Assert.Equal(2, await context.ElementCommandRecords.CountAsync());
            Assert.Single(await context.ElementCommandBatches.ToListAsync());
            Assert.Contains("\"kind\":\"group\"", survivor.GeometryJson);
            Assert.Equal(SpaceLifecycleState.RemoveRequested, source.LifecycleState);

            var compensated = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 1,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.UpdateProperties,
                            survivor.LogicalId,
                            new SpaceUpdateElementPropertiesDto(
                                """
                                {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                                """,
                                1000,
                                2000,
                                0,
                                0,
                                400,
                                5000,
                                400,
                                null,
                                null,
                                null,
                                attributes,
                                SpaceElementTypes.Column)),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.RestoreLogicalObject,
                            source.LogicalId,
                            null),
                    ]));

            Assert.Equal(2, compensated.FloorRevision);
            Assert.Equal(2, compensated.VersionContentRevision);
            Assert.Equal(2, compensated.AffectedObjects.Count);
            Assert.Equal(SpaceLifecycleState.Active, source.LifecycleState);
            Assert.Equal(4, await context.ElementCommandRecords.CountAsync());
            Assert.Equal(2, await context.ElementCommandBatches.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Element_group_split_compensation_and_redo_keep_new_identities_atomic()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedDesignModelAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Element exception split",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1",
                height: 6000);
            var survivorLogicalId = Guid.NewGuid();
            var sourceLogicalId = Guid.NewGuid();
            var groupGeometry = $$$"""
                {"schemaVersion":1,"kind":"group","parts":[{"sourceLogicalId":"{{{survivorLogicalId}}}","x":0,"y":0,"z":0,"rotationZ":0,"width":400,"height":5000,"depth":400,"geometry":{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}},{"sourceLogicalId":"{{{sourceLogicalId}}}","x":800,"y":0,"z":0,"rotationZ":0,"width":400,"height":5000,"depth":400,"geometry":{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}}]}
                """;
            const string partGeometry = """
                {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                """;
            var survivor = SpaceElementRevision.Create(
                execution.TenantId,
                draft.Id,
                survivorLogicalId,
                floor.LogicalId,
                SpaceElementTypes.Column,
                groupGeometry);
            survivor.ConfigurePlacement(1000, 2000, 0, 90, 1200, 5000, 400);
            survivor.ConfigureBusinessLink(
                "COL-01",
                "Floor",
                floor.LogicalId);
            var survivorAttribute = SpaceElementAttribute.Create(
                execution.TenantId,
                survivor,
                SpaceElementAttributeNamespaces.Design,
                "label",
                SpaceElementAttributeValueTypes.String,
                "CAD exception");
            context.AddRange(draft, floor, survivor, survivorAttribute);
            await context.SaveChangesAsync();

            var clientId = Guid.NewGuid();
            var lease = SpaceEditLease.Create(
                execution.TenantId,
                draft.Id,
                floor.LogicalId,
                execution.ActorId,
                "Editor",
                clientId,
                clock.UtcNow,
                TimeSpan.FromSeconds(90));
            context.EditLeases.Add(lease);
            await context.SaveChangesAsync();
            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId);
            var splitLogicalId = Guid.NewGuid();
            var attributes = new[]
            {
                new SpaceElementAttributeWriteDto(
                    SpaceElementAttributeNamespaces.Design,
                    "label",
                    SpaceElementAttributeValueTypes.String,
                    "CAD exception",
                    null),
            };
            SpaceUpdateElementPropertiesDto survivorPart() => new(
                partGeometry,
                1000,
                2000,
                0,
                90,
                400,
                5000,
                400,
                "COL-01",
                "Floor",
                floor.LogicalId,
                attributes,
                SpaceElementTypes.Column);

            var split = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 0,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.UpdateProperties,
                            survivor.LogicalId,
                            survivorPart()),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.CreateElement,
                            splitLogicalId,
                            UpdateProperties: null,
                            CreateElement: new SpaceCreateElementDto(
                                SpaceElementTypes.Column,
                                partGeometry,
                                1000,
                                2800,
                                0,
                                90,
                                400,
                                5000,
                                400,
                                "COL-01",
                                ParentLogicalId: null,
                                SourceId: null,
                                SourceRef: null,
                                attributes,
                                LinkedEntityType: "Floor",
                                LinkedLogicalId: floor.LogicalId)),
                    ]));

            Assert.Equal(1, split.FloorRevision);
            Assert.Equal(1, split.VersionContentRevision);
            Assert.Equal(2, split.AffectedObjects.Count);
            var created = await context.ElementRevisions.SingleAsync(
                item => item.LogicalId == splitLogicalId);
            Assert.Equal(SpaceLifecycleState.Active, created.LifecycleState);
            Assert.Equal("COL-01", created.BusinessCode);
            Assert.Equal("Floor", created.LinkedEntityType);
            Assert.Equal(floor.LogicalId, created.LinkedLogicalId);
            Assert.Equal(1000, created.X);
            Assert.Equal(2800, created.Y);
            Assert.Equal(
                "CAD exception",
                Assert.Single(await context.ElementAttributes
                    .Where(item => item.ElementRevisionId == created.Id)
                    .ToListAsync()).Value);
            Assert.Equal(2, await context.ElementCommandRecords.CountAsync());

            var compensated = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 1,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.UpdateProperties,
                            survivor.LogicalId,
                            new SpaceUpdateElementPropertiesDto(
                                groupGeometry,
                                1000,
                                2000,
                                0,
                                90,
                                1200,
                                5000,
                                400,
                                "COL-01",
                                "Floor",
                                floor.LogicalId,
                                attributes,
                                SpaceElementTypes.Column)),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.DeleteObject,
                            splitLogicalId,
                            null),
                    ]));

            Assert.Equal(2, compensated.FloorRevision);
            Assert.Equal(SpaceLifecycleState.RemoveRequested, created.LifecycleState);

            var redone = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 2,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.UpdateProperties,
                            survivor.LogicalId,
                            survivorPart()),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.RestoreLogicalObject,
                            splitLogicalId,
                            null),
                    ]));

            Assert.Equal(3, redone.FloorRevision);
            Assert.Equal(3, redone.VersionContentRevision);
            Assert.Equal(SpaceLifecycleState.Active, created.LifecycleState);
            Assert.Equal(
                2,
                await context.ElementRevisions.CountAsync(item =>
                    item.ModelVersionId == draft.Id));
            Assert.Equal(6, await context.ElementCommandRecords.CountAsync());
            Assert.Equal(3, await context.ElementCommandBatches.CountAsync());

            var invalidLink = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                service.ApplyElementCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    new ApplySpaceElementCommandBatchRequest(
                        SpaceElementCommandContract.SchemaVersion,
                        Guid.NewGuid(),
                        clientId,
                        lease.LeaseId,
                        ExpectedFloorRevision: 3,
                        [new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.CreateElement,
                            Guid.NewGuid(),
                            UpdateProperties: null,
                            CreateElement: new SpaceCreateElementDto(
                                SpaceElementTypes.Column,
                                partGeometry,
                                1800,
                                2800,
                                0,
                                90,
                                400,
                                5000,
                                400,
                                "COL-01",
                                ParentLogicalId: null,
                                SourceId: null,
                                SourceRef: null,
                                attributes,
                                LinkedEntityType: "Floor",
                                LinkedLogicalId: null))])));
            Assert.Equal(SpaceErrorCodes.RequestInvalid, invalidLink.Code);
            Assert.Equal(
                2,
                await context.ElementRevisions.CountAsync(item =>
                    item.ModelVersionId == draft.Id));
            Assert.Equal(6, await context.ElementCommandRecords.CountAsync());
            Assert.Equal(3, await context.ElementCommandBatches.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Create_element_accepts_null_content_hash_and_stale_fence_is_zero_write()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedDesignModelAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Blank canvas editing",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1",
                height: 6000);
            context.AddRange(draft, floor);
            await context.SaveChangesAsync();
            var clientId = Guid.NewGuid();
            var lease = SpaceEditLease.Create(
                execution.TenantId,
                draft.Id,
                floor.LogicalId,
                execution.ActorId,
                "Editor",
                clientId,
                clock.UtcNow,
                TimeSpan.FromSeconds(90));
            context.EditLeases.Add(lease);
            await context.SaveChangesAsync();
            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId);
            var logicalId = Guid.NewGuid();
            var create = new ApplySpaceElementCommandBatchRequest(
                SpaceElementCommandContract.SchemaVersion,
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                ExpectedFloorRevision: 0,
                [new SpaceElementCommandDto(
                    Guid.NewGuid(),
                    SpaceElementCommandContract.CreateElement,
                    logicalId,
                    UpdateProperties: null,
                    CreateElement: new SpaceCreateElementDto(
                        SpaceElementTypes.Wall,
                        """
                        {"schemaVersion":1,"kind":"box","width":5000,"height":3000,"depth":200}
                        """,
                        1000,
                        2000,
                        0,
                        0,
                        5000,
                        3000,
                        200,
                        null,
                        null,
                        null,
                        null,
                        [new SpaceElementAttributeWriteDto(
                            SpaceElementAttributeNamespaces.Design,
                            "label",
                            SpaceElementAttributeValueTypes.String,
                            "Wall A",
                            null)]))],
                ExpectedContentRevision: 0,
                ExpectedContentHash: null,
                ChangesetSha256: new string('c', 64));

            var applied = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                create);

            Assert.Equal(1, applied.FloorRevision);
            Assert.Equal(1, applied.VersionContentRevision);
            var element = Assert.Single(applied.AffectedObjects);
            Assert.Equal(logicalId, element.TargetLogicalId);
            Assert.Equal(SpaceElementTypes.Wall, element.Element.ElementType);
            Assert.Equal(5000, element.Element.Width);
            Assert.Equal("Wall A", Assert.Single(element.Attributes).Value);

            var replay = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                create);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(applied.FloorRevision, replay.FloorRevision);
            Assert.Equal(
                applied.VersionContentRevision,
                replay.VersionContentRevision);

            var currentVersion = await context.Versions.SingleAsync(
                item => item.Id == draft.Id);
            currentVersion.TouchContent();
            await context.SaveChangesAsync();
            var advancedReplay = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                service.ApplyElementCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    create));
            Assert.Equal(SpaceErrorCodes.ParseChangesetStale, advancedReplay.Code);

            var stale = create with
            {
                CommandBatchId = Guid.NewGuid(),
                ExpectedFloorRevision = 1,
                Commands = [create.Commands[0] with
                {
                    CommandId = Guid.NewGuid(),
                    TargetLogicalId = Guid.NewGuid(),
                }],
            };
            var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
                service.ApplyElementCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    stale));
            Assert.Equal(SpaceErrorCodes.ParseChangesetStale, problem.Code);
            Assert.Single(await context.ElementRevisions.AsNoTracking().ToListAsync());
            Assert.Single(await context.ElementCommandBatches.AsNoTracking().ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Mixed_editor_batches_array_and_saved_compensation_are_atomic()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedDesignModelAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Unified rack and element editing",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1",
                height: 6000);
            var zone = SpaceZoneRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                "Z1",
                1);
            var rack = SpaceRackRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                zone.LogicalId,
                "R-TEMPLATE");
            rack.ConfigureGeometry(
                1000,
                2000,
                0,
                0,
                1200,
                800,
                3000);
            var level = SpaceRackLevelRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                rack.LogicalId,
                levelNo: 1,
                bottomZ: 0,
                clearHeight: 1200,
                binCount: 2,
                depthCount: 1,
                cellWidth: 600,
                cellDepth: 800,
                maxLoad: 750,
                beamHeight: 100);
            var location = SpaceLocationRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                rack.LogicalId,
                "R-TEMPLATE-01",
                columnNo: 1,
                levelNo: 1,
                depthNo: 1,
                width: 600,
                height: 1200,
                depth: 800,
                maxLoad: 750,
                codeOrigin: SpaceLocationCodeOrigin.Manual,
                externalBindingState: SpaceExternalBindingState.Bound);
            var element = SpaceElementRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                SpaceElementTypes.Column,
                """
                {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                """);
            element.ConfigurePlacement(500, 700, 0, 0, 400, 5000, 400);
            context.AddRange(
                draft,
                floor,
                zone,
                rack,
                level,
                location,
                element);
            await context.SaveChangesAsync();

            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId);
            var clientId = Guid.NewGuid();
            var editLease = SpaceEditLease.Create(
                execution.TenantId,
                draft.Id,
                floor.LogicalId,
                execution.ActorId,
                "Editor",
                clientId,
                clock.UtcNow,
                TimeSpan.FromSeconds(90));
            context.EditLeases.Add(editLease);
            await context.SaveChangesAsync();
            var mixed = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    editLease.LeaseId,
                    ExpectedFloorRevision: 0,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.MoveObject,
                            element.LogicalId,
                            null,
                            new SpaceMoveObjectDto(1500, 1700, 0)),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.MoveObject,
                            rack.LogicalId,
                            null,
                            new SpaceMoveObjectDto(3000, 4000, 0)),
                    ]));

            Assert.Equal(1, mixed.FloorRevision);
            Assert.Single(mixed.AffectedObjects);
            Assert.Single(mixed.AffectedRacks!);
            Assert.Equal(1500, mixed.AffectedObjects[0].Element.X);
            Assert.Equal(3000, mixed.AffectedRacks![0].X);

            var compensation = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    editLease.LeaseId,
                    ExpectedFloorRevision: 1,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.MoveObject,
                            element.LogicalId,
                            null,
                            new SpaceMoveObjectDto(500, 700, 0)),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.MoveObject,
                            rack.LogicalId,
                            null,
                            new SpaceMoveObjectDto(1000, 2000, 0)),
                    ]));

            Assert.Equal(2, compensation.FloorRevision);
            Assert.Equal(500, compensation.AffectedObjects[0].Element.X);
            Assert.Equal(1000, compensation.AffectedRacks![0].X);

            var generated = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    editLease.LeaseId,
                    ExpectedFloorRevision: 2,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.GenerateRackArray,
                            rack.LogicalId,
                            null,
                            GenerateRackArray:
                                new SpaceGenerateRackArrayDto(
                                    Rows: 2,
                                    Columns: 2,
                                    RowGap: 500,
                                    ColumnGap: 300,
                                    StaggerOffset: 100,
                                    CodePrefix: "ARR-",
                                    StartNumber: 1,
                                    CodeDigits: 3)),
                    ]));

            Assert.Equal(3, generated.FloorRevision);
            Assert.Equal(3, generated.AffectedRacks!.Count);
            Assert.Equal(3, generated.AffectedRackLevels!.Count);
            Assert.Equal(3, generated.AffectedLocations!.Count);
            Assert.All(
                generated.AffectedLocations,
                generatedLocation =>
                {
                    Assert.Null(generatedLocation.LocationCode);
                    Assert.Equal(
                        SpaceLocationCodeOrigin.Generated.ToString(),
                        generatedLocation.CodeOrigin);
                    Assert.Equal(
                        SpaceExternalBindingState.Unbound.ToString(),
                        generatedLocation.ExternalBindingState);
                });
            var generatedIds = generated.AffectedRacks
                .Select(candidate => candidate.Revision.LogicalId)
                .ToArray();

            var removed = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                LifecycleBatch(
                    clientId,
                    editLease.LeaseId,
                    expectedFloorRevision: 3,
                    SpaceElementCommandContract.DeleteObject,
                    generatedIds));
            Assert.Equal(4, removed.FloorRevision);
            Assert.All(
                removed.AffectedRacks!,
                candidate => Assert.Equal(
                    SpaceLifecycleState.RemoveRequested.ToString(),
                    candidate.Revision.LifecycleState));
            Assert.All(
                removed.AffectedRackLevels!,
                candidate => Assert.Equal(
                    SpaceLifecycleState.RemoveRequested.ToString(),
                    candidate.Revision.LifecycleState));
            Assert.All(
                removed.AffectedLocations!,
                candidate => Assert.Equal(
                    SpaceLifecycleState.RemoveRequested.ToString(),
                    candidate.Revision.LifecycleState));

            var restored = await service.ApplyElementCommandsAsync(
                draft.Id,
                floor.LogicalId,
                LifecycleBatch(
                    clientId,
                    editLease.LeaseId,
                    expectedFloorRevision: 4,
                    SpaceElementCommandContract.RestoreLogicalObject,
                    generatedIds));
            Assert.Equal(5, restored.FloorRevision);
            Assert.All(
                restored.AffectedRacks!,
                candidate => Assert.Equal(
                    SpaceLifecycleState.Active.ToString(),
                    candidate.Revision.LifecycleState));

            var atomicFailure =
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    editLease.LeaseId,
                    ExpectedFloorRevision: 5,
                    [
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.MoveObject,
                            rack.LogicalId,
                            null,
                            new SpaceMoveObjectDto(9999, 9999, 0)),
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.DeleteObject,
                            Guid.NewGuid(),
                            null),
                    ]);
            var problem = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyElementCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    atomicFailure));

            Assert.Equal(SpaceErrorCodes.LogicalIdNotFound, problem.Code);
            var codeConflict =
                await Assert.ThrowsAsync<SpaceProblemException>(
                    () => service.ApplyElementCommandsAsync(
                        draft.Id,
                        floor.LogicalId,
                        new ApplySpaceElementCommandBatchRequest(
                            SpaceElementCommandContract.SchemaVersion,
                            Guid.NewGuid(),
                            clientId,
                            editLease.LeaseId,
                            ExpectedFloorRevision: 5,
                            [
                                new SpaceElementCommandDto(
                                    Guid.NewGuid(),
                                    SpaceElementCommandContract
                                        .GenerateRackArray,
                                    rack.LogicalId,
                                    null,
                                    GenerateRackArray:
                                        new SpaceGenerateRackArrayDto(
                                            Rows: 1,
                                            Columns: 2,
                                            RowGap: 0,
                                            ColumnGap: 100,
                                            StaggerOffset: 0,
                                            CodePrefix: "ARR-",
                                            StartNumber: 1,
                                            CodeDigits: 3)),
                            ])));
            Assert.Equal(SpaceErrorCodes.CommandConflict, codeConflict.Code);
            Assert.Equal(
                1000,
                await context.RackRevisions
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == rack.Id)
                    .Select(candidate => candidate.X)
                    .SingleAsync());
            Assert.Equal(
                5,
                await context.FloorRevisions
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == floor.Id)
                    .Select(candidate => candidate.Revision)
                    .SingleAsync());
            Assert.Equal(5, await context.ElementCommandBatches.CountAsync());
            Assert.Equal(11, await context.ElementCommandRecords.CountAsync());
            var arrayAudit = await context.ElementCommandRecords
                .AsNoTracking()
                .SingleAsync(candidate =>
                    candidate.CommandType ==
                    SpaceElementCommandContract.GenerateRackArray);
            Assert.Contains("\"generatedRacks\"", arrayAudit.AfterJson);
            Assert.Contains("\"rackCode\":\"ARR-001\"", arrayAudit.AfterJson);
        });
    }

    [SqlServerFact]
    public async Task Layout_commands_create_coded_warehouse_atomically()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedDesignModelAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Blank layout",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1",
                height: 6000);
            context.AddRange(draft, floor);
            await context.SaveChangesAsync();

            var clientId = Guid.NewGuid();
            var lease = SpaceEditLease.Create(
                execution.TenantId,
                draft.Id,
                floor.LogicalId,
                execution.ActorId,
                "Editor",
                clientId,
                clock.UtcNow,
                TimeSpan.FromSeconds(90));
            context.EditLeases.Add(lease);
            await context.SaveChangesAsync();

            var zoneId = Guid.NewGuid();
            var aisleId = Guid.NewGuid();
            var rackId = Guid.NewGuid();
            var request = new ApplySpaceLayoutCommandBatchRequest(
                SpaceLayoutCommandContract.SchemaVersion,
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                ExpectedFloorRevision: 0,
                ExpectedContentRevision: 0,
                [
                    new SpaceLayoutCommandDto(
                        Guid.NewGuid(),
                        SpaceLayoutCommandContract.CreateZone,
                        zoneId,
                        CreateZone: new SpaceCreateLayoutZoneDto(
                            "Z-A",
                            "Ambient",
                            1,
                            """
                            {"schemaVersion":1,"points":[[0,0],[12000,0],[12000,8000],[0,8000]]}
                            """,
                            "#00A6B2",
                            null)),
                    new SpaceLayoutCommandDto(
                        Guid.NewGuid(),
                        SpaceLayoutCommandContract.CreateAisle,
                        aisleId,
                        CreateAisle: new SpaceCreateLayoutAisleDto(
                            zoneId,
                            "A-01",
                            "Main aisle",
                            1,
                            """
                            {"schemaVersion":1,"points":[[0,0],[2000,0],[2000,8000],[0,8000]]}
                            """,
                            """
                            {"schemaVersion":1,"points":[[1000,0],[1000,8000]]}
                            """)),
                    new SpaceLayoutCommandDto(
                        Guid.NewGuid(),
                        SpaceLayoutCommandContract.CreateRack,
                        rackId,
                        CreateRack: new SpaceCreateLayoutRackDto(
                            zoneId,
                            aisleId,
                            "R-001",
                            "Rack 1",
                            "Selective",
                            null,
                            X: 2500,
                            Y: 1000,
                            Z: 0,
                            RotationZ: 0,
                            Width: 2400,
                            Depth: 1000,
                            Height: 4000,
                            [
                                new SpaceCreateLayoutRackLevelDto(
                                    LevelNo: 1,
                                    BottomZ: 0,
                                    ClearHeight: 1600,
                                    BinCount: 2,
                                    DepthCount: 2,
                                    CellWidth: 1200,
                                    CellDepth: 500,
                                    BeamHeight: 100,
                                    MaxLoad: 1000,
                                    LocationCodePrefix: "R-001"),
                                new SpaceCreateLayoutRackLevelDto(
                                    LevelNo: 2,
                                    BottomZ: 1700,
                                    ClearHeight: 1600,
                                    BinCount: 2,
                                    DepthCount: 2,
                                    CellWidth: 1200,
                                    CellDepth: 500,
                                    BeamHeight: 100,
                                    MaxLoad: 1000,
                                    LocationCodePrefix: "R-001"),
                            ])),
                ]);
            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId);

            var applied = await service.ApplyLayoutCommandsAsync(
                draft.Id,
                floor.LogicalId,
                request);
            var replay = await service.ApplyLayoutCommandsAsync(
                draft.Id,
                floor.LogicalId,
                request);

            Assert.Equal(1, applied.FloorRevision);
            Assert.Equal(1, applied.VersionContentRevision);
            Assert.False(applied.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(3, applied.AppliedCommands.Count);
            Assert.Single(applied.AffectedZones);
            Assert.Single(applied.AffectedAisles);
            Assert.Single(applied.AffectedRacks);
            Assert.Equal(2, applied.AffectedRackLevels.Count);
            Assert.Equal(8, applied.AffectedLocations.Count);
            Assert.Contains(
                applied.AffectedLocations,
                location => location.LocationCode == "R-001-L01-C001-D01");
            Assert.Equal(
                WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                    rackId,
                    2),
                applied.AffectedRackLevels[1].Revision.LogicalId);
            Assert.Equal(
                WarehouseDeterministicIdentity.CreateLocationLogicalId(
                    rackId,
                    2,
                    2,
                    2),
                applied.AffectedLocations[^1].Revision.LogicalId);
            Assert.Equal(3, await context.ElementCommandRecords.CountAsync());

            var lostLeaseRequest = request with
            {
                CommandBatchId = Guid.NewGuid(),
                LeaseId = Guid.NewGuid(),
                ExpectedFloorRevision = 1,
                ExpectedContentRevision = 1,
                Commands = [request.Commands[0] with
                {
                    CommandId = Guid.NewGuid(),
                    TargetLogicalId = Guid.NewGuid(),
                }],
            };
            var leaseProblem = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyLayoutCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    lostLeaseRequest));
            Assert.Equal(SpaceErrorCodes.EditLeaseLost, leaseProblem.Code);
            Assert.Equal(3, await context.ElementCommandRecords.CountAsync());

            var scene = await service.GetSceneAsync(
                draft.Id,
                floor.LogicalId);
            Assert.Single(scene.Zones);
            Assert.Single(scene.Aisles);
            Assert.Single(scene.Racks);
            Assert.Equal(2, scene.RackLevels.Count);
            Assert.Equal(8, scene.Locations.Count);

            var atomicBatchId = Guid.NewGuid();
            var atomicZoneId = Guid.NewGuid();
            var atomicProblem = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyLayoutCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    new ApplySpaceLayoutCommandBatchRequest(
                        SpaceLayoutCommandContract.SchemaVersion,
                        atomicBatchId,
                        clientId,
                        lease.LeaseId,
                        ExpectedFloorRevision: 1,
                        ExpectedContentRevision: 1,
                        [
                            new SpaceLayoutCommandDto(
                                Guid.NewGuid(),
                                SpaceLayoutCommandContract.CreateZone,
                                atomicZoneId,
                                CreateZone: new SpaceCreateLayoutZoneDto(
                                    "Z-B",
                                    null,
                                    1,
                                    "[]",
                                    null,
                                    null)),
                            new SpaceLayoutCommandDto(
                                Guid.NewGuid(),
                                SpaceLayoutCommandContract.CreateAisle,
                                Guid.NewGuid(),
                                CreateAisle: new SpaceCreateLayoutAisleDto(
                                    Guid.NewGuid(),
                                    "A-BROKEN",
                                    null,
                                    1,
                                    "[]",
                                    "[]")),
                        ])));
            Assert.Equal(SpaceErrorCodes.LogicalIdNotFound, atomicProblem.Code);
            Assert.False(await context.ZoneRevisions.AsNoTracking().AnyAsync(
                candidate => candidate.LogicalId == atomicZoneId));
            Assert.False(await context.ElementCommandBatches.AsNoTracking()
                .AnyAsync(candidate => candidate.Id == atomicBatchId));

            var stale = request with
            {
                CommandBatchId = Guid.NewGuid(),
                Commands = request.Commands.Select(command => command with
                {
                    CommandId = Guid.NewGuid(),
                    TargetLogicalId = Guid.NewGuid(),
                }).ToArray(),
            };
            var staleProblem = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyLayoutCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    stale));
            Assert.Equal(
                SpaceErrorCodes.FloorRevisionConflict,
                staleProblem.Code);
            Assert.Equal(3, await context.ElementCommandRecords.CountAsync());

            var update = new ApplySpaceLayoutCommandBatchRequest(
                SpaceLayoutCommandContract.SchemaVersion,
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                ExpectedFloorRevision: 1,
                ExpectedContentRevision: 1,
                [
                    new SpaceLayoutCommandDto(
                        Guid.NewGuid(),
                        SpaceLayoutCommandContract.UpdateZone,
                        zoneId,
                        UpdateZone: new SpaceUpdateLayoutZoneDto(
                            "Z-AMBIENT",
                            "Ambient storage",
                            2,
                            """
                            {"schemaVersion":1,"points":[[0,0],[14000,0],[14000,8000],[0,8000]]}
                            """,
                            "#008C99",
                            "temperatureControlled")),
                    new SpaceLayoutCommandDto(
                        Guid.NewGuid(),
                        SpaceLayoutCommandContract.UpdateAisle,
                        aisleId,
                        UpdateAisle: new SpaceUpdateLayoutAisleDto(
                            zoneId,
                            "A-PRIMARY",
                            "Primary aisle",
                            2,
                            """
                            {"schemaVersion":1,"points":[[0,0],[2200,0],[2200,8000],[0,8000]]}
                            """,
                            """
                            {"schemaVersion":1,"points":[[1100,0],[1100,8000]]}
                            """)),
                    new SpaceLayoutCommandDto(
                        Guid.NewGuid(),
                        SpaceLayoutCommandContract.UpdateRack,
                        rackId,
                        UpdateRack: new SpaceUpdateLayoutRackDto(
                            zoneId,
                            aisleId,
                            "R-PRIMARY",
                            "Primary rack",
                            "Selective",
                            null,
                            X: 3000,
                            Y: 1200,
                            Z: 0,
                            RotationZ: 90,
                            Width: 2400,
                            Depth: 1000,
                            Height: 4200,
                            [
                                new SpaceUpdateLayoutRackLevelDto(
                                    LevelNo: 1,
                                    BottomZ: 0,
                                    ClearHeight: 1800,
                                    BinCount: 1,
                                    DepthCount: 1,
                                    CellWidth: 1200,
                                    CellDepth: 500,
                                    BeamHeight: 100,
                                    MaxLoad: 1200),
                                new SpaceUpdateLayoutRackLevelDto(
                                    LevelNo: 3,
                                    BottomZ: 1900,
                                    ClearHeight: 1800,
                                    BinCount: 1,
                                    DepthCount: 1,
                                    CellWidth: 1200,
                                    CellDepth: 500,
                                    BeamHeight: 100,
                                    MaxLoad: 1200),
                            ])),
                ]);
            var updated = await service.ApplyLayoutCommandsAsync(
                draft.Id,
                floor.LogicalId,
                update);

            Assert.Equal(2, updated.FloorRevision);
            Assert.Equal(2, updated.VersionContentRevision);
            Assert.Equal("Z-AMBIENT", Assert.Single(updated.AffectedZones).ZoneCode);
            Assert.Equal("A-PRIMARY", Assert.Single(updated.AffectedAisles).AisleCode);
            Assert.Equal("R-PRIMARY", Assert.Single(updated.AffectedRacks).RackCode);
            Assert.Equal(3, updated.AffectedRackLevels.Count);
            Assert.Equal(9, updated.AffectedLocations.Count);
            var preservedLocation = Assert.Single(updated.AffectedLocations, candidate =>
                candidate.Revision.LogicalId ==
                WarehouseDeterministicIdentity.CreateLocationLogicalId(
                    rackId,
                    1,
                    1,
                    1));
            Assert.Equal("R-001-L01-C001-D01", preservedLocation.LocationCode);
            Assert.Equal(
                SpaceLifecycleState.Active.ToString(),
                preservedLocation.Revision.LifecycleState);
            Assert.Contains(updated.AffectedLocations, candidate =>
                candidate.Revision.LogicalId ==
                WarehouseDeterministicIdentity.CreateLocationLogicalId(
                    rackId,
                    3,
                    1,
                    1) &&
                candidate.LocationCode is null);

            var guardedDelete = new ApplySpaceLayoutCommandBatchRequest(
                SpaceLayoutCommandContract.SchemaVersion,
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                ExpectedFloorRevision: 2,
                ExpectedContentRevision: 2,
                [
                    new SpaceLayoutCommandDto(
                        Guid.NewGuid(),
                        SpaceLayoutCommandContract.DeleteZone,
                        zoneId,
                        DeleteObject: new SpaceDeleteLayoutObjectDto(Cascade: false)),
                ]);
            var cascadeProblem = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyLayoutCommandsAsync(
                    draft.Id,
                    floor.LogicalId,
                    guardedDelete));
            Assert.Equal(SpaceErrorCodes.CommandConflict, cascadeProblem.Code);
            Assert.Equal("confirm-layout-cascade", cascadeProblem.RecoveryAction);

            var deleted = await service.ApplyLayoutCommandsAsync(
                draft.Id,
                floor.LogicalId,
                guardedDelete with
                {
                    CommandBatchId = Guid.NewGuid(),
                    Commands = [guardedDelete.Commands[0] with
                    {
                        CommandId = Guid.NewGuid(),
                        DeleteObject = new SpaceDeleteLayoutObjectDto(Cascade: true),
                    }],
                });
            Assert.Equal(3, deleted.FloorRevision);
            Assert.All(deleted.AffectedZones, candidate => Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                candidate.Revision.LifecycleState));
            Assert.All(deleted.AffectedAisles, candidate => Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                candidate.Revision.LifecycleState));
            Assert.All(deleted.AffectedRacks, candidate => Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                candidate.Revision.LifecycleState));
            Assert.All(deleted.AffectedRackLevels, candidate => Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                candidate.Revision.LifecycleState));
            Assert.All(deleted.AffectedLocations, candidate => Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                candidate.Revision.LifecycleState));
            Assert.All(
                await context.ElementCommandRecords.AsNoTracking()
                    .Where(candidate =>
                        candidate.CommandType.StartsWith("Update"))
                    .ToArrayAsync(),
                candidate => Assert.NotEqual("{}", candidate.BeforeJson));

            var noCascadeZoneId = Guid.NewGuid();
            var noCascadeCreated = await service.ApplyLayoutCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceLayoutCommandBatchRequest(
                    SpaceLayoutCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 3,
                    ExpectedContentRevision: 3,
                    [
                        new SpaceLayoutCommandDto(
                            Guid.NewGuid(),
                            SpaceLayoutCommandContract.CreateZone,
                            noCascadeZoneId,
                            CreateZone: new SpaceCreateLayoutZoneDto(
                                "Z-EMPTY",
                                null,
                                1,
                                "[]",
                                null,
                                null)),
                    ]));
            Assert.Equal(4, noCascadeCreated.FloorRevision);
            var noCascadeDeleted = await service.ApplyLayoutCommandsAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceLayoutCommandBatchRequest(
                    SpaceLayoutCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 4,
                    ExpectedContentRevision: 4,
                    [
                        new SpaceLayoutCommandDto(
                            Guid.NewGuid(),
                            SpaceLayoutCommandContract.DeleteZone,
                            noCascadeZoneId,
                            DeleteObject: new SpaceDeleteLayoutObjectDto(Cascade: false)),
                    ]));
            Assert.Equal(5, noCascadeDeleted.FloorRevision);
            Assert.Equal(
                SpaceLifecycleState.RemoveRequested.ToString(),
                Assert.Single(noCascadeDeleted.AffectedZones).Revision.LifecycleState);
        });
    }

    [SqlServerFact]
    public async Task Location_coding_previews_without_writes_and_applies_with_fences()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            await using var context = CreateContext(
                connectionString,
                execution,
                clock);
            var seeded = await SeedDesignModelAsync(
                context,
                execution.ActorId,
                clock.UtcNow);
            var draft = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                seeded.Model.Id,
                2,
                "Coding draft",
                seeded.Published.Id);
            seeded.Model.ReserveDraft(draft);
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                seeded.Model.SiteId,
                1,
                "F1",
                "Floor 1",
                height: 6000);
            var zone = SpaceZoneRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                "Z-A",
                zoneType: 1,
                "Ambient");
            var rack = SpaceRackRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                zone.LogicalId,
                "R-01");
            var emptyGenerated = SpaceLocationRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                rack.LogicalId,
                locationCode: null,
                columnNo: 1,
                levelNo: 1,
                depthNo: 1,
                width: 1000,
                height: 1000,
                depth: 1000);
            var existingGenerated = SpaceLocationRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                rack.LogicalId,
                "OLD-GENERATED",
                columnNo: 2,
                levelNo: 1,
                depthNo: 1,
                width: 1000,
                height: 1000,
                depth: 1000);
            var adopted = SpaceLocationRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                rack.LogicalId,
                "WMS-001",
                columnNo: 3,
                levelNo: 1,
                depthNo: 1,
                width: 1000,
                height: 1000,
                depth: 1000,
                codeOrigin: SpaceLocationCodeOrigin.Adopted,
                externalBindingState: SpaceExternalBindingState.Bound);
            var manual = SpaceLocationRevision.Create(
                execution.TenantId,
                draft.Id,
                Guid.NewGuid(),
                floor.LogicalId,
                rack.LogicalId,
                "MANUAL-001",
                columnNo: 4,
                levelNo: 1,
                depthNo: 1,
                width: 1000,
                height: 1000,
                depth: 1000,
                codeOrigin: SpaceLocationCodeOrigin.Manual);
            context.AddRange(
                draft,
                floor,
                zone,
                rack,
                emptyGenerated,
                existingGenerated,
                adopted,
                manual);
            await context.SaveChangesAsync();

            var clientId = Guid.NewGuid();
            var lease = SpaceEditLease.Create(
                execution.TenantId,
                draft.Id,
                floor.LogicalId,
                execution.ActorId,
                "Coding editor",
                clientId,
                clock.UtcNow,
                TimeSpan.FromSeconds(90));
            context.EditLeases.Add(lease);
            await context.SaveChangesAsync();

            var rules = new MutableCodingRuleProvider();
            var service = NewService(
                context,
                execution,
                clock,
                seeded.Model.SiteId,
                rules);
            var previewRequest = new PreviewSpaceLocationCodesRequest(
                SpaceDesignCodingContract.SchemaVersion,
                SpaceDesignCodingContract.FillEmpty,
                ScopeZoneLogicalId: null,
                ExpectedFloorRevision: 0,
                ExpectedContentRevision: 0);

            await using (var externalContext = CreateContext(
                             connectionString,
                             new TestExecutionContext(
                                 execution.TenantId,
                                 execution.ActorId,
                                 IsExternal: true),
                             clock))
            {
                var externalProblem = await Assert.ThrowsAsync<SpaceProblemException>(
                    () => NewService(
                            externalContext,
                            new TestExecutionContext(
                                execution.TenantId,
                                execution.ActorId,
                                IsExternal: true),
                            clock,
                            seeded.Model.SiteId,
                            rules)
                        .PreviewLocationCodesAsync(
                            draft.Id,
                            floor.LogicalId,
                            previewRequest));
                Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, externalProblem.Code);
            }

            var preview = await service.PreviewLocationCodesAsync(
                draft.Id,
                floor.LogicalId,
                previewRequest);

            Assert.Equal(1, preview.ChangedCount);
            Assert.Equal(1, preview.UnchangedCount);
            Assert.Equal(2, preview.ProtectedCount);
            Assert.Equal(
                "A-Z-A-R-01-01-01-01",
                preview.Items.Single(item =>
                    item.LocationLogicalId == emptyGenerated.LogicalId)
                    .ProposedCode);
            Assert.Null(
                await context.LocationRevisions
                    .AsNoTracking()
                    .Where(location => location.Id == emptyGenerated.Id)
                    .Select(location => location.LocationCode)
                    .SingleAsync());
            Assert.Empty(await context.ElementCommandBatches.ToListAsync());

            var applyRequest = new ApplySpaceLocationCodesRequest(
                SpaceDesignCodingContract.SchemaVersion,
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                preview.Mode,
                preview.ScopeZoneLogicalId,
                preview.BaseFloorRevision,
                preview.BaseContentRevision,
                preview.ProposalHash);
            var applied = await service.ApplyLocationCodesAsync(
                draft.Id,
                floor.LogicalId,
                applyRequest);
            var replay = await service.ApplyLocationCodesAsync(
                draft.Id,
                floor.LogicalId,
                applyRequest);

            Assert.Equal(1, applied.AppliedCount);
            Assert.False(applied.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(1, applied.FloorRevision);
            Assert.Equal(1, applied.VersionContentRevision);
            Assert.Equal(
                "A-Z-A-R-01-01-01-01",
                await context.LocationRevisions
                    .AsNoTracking()
                    .Where(location => location.Id == emptyGenerated.Id)
                    .Select(location => location.LocationCode)
                    .SingleAsync());
            Assert.Equal(
                "OLD-GENERATED",
                await context.LocationRevisions
                    .AsNoTracking()
                    .Where(location => location.Id == existingGenerated.Id)
                    .Select(location => location.LocationCode)
                    .SingleAsync());
            Assert.Equal(
                "WMS-001",
                await context.LocationRevisions
                    .AsNoTracking()
                    .Where(location => location.Id == adopted.Id)
                    .Select(location => location.LocationCode)
                    .SingleAsync());
            Assert.Equal(
                "MANUAL-001",
                await context.LocationRevisions
                    .AsNoTracking()
                    .Where(location => location.Id == manual.Id)
                    .Select(location => location.LocationCode)
                    .SingleAsync());
            Assert.Single(await context.ElementCommandBatches.ToListAsync());
            Assert.Single(await context.ElementCommandRecords.ToListAsync());

            var rebuildPreview = await service.PreviewLocationCodesAsync(
                draft.Id,
                floor.LogicalId,
                new PreviewSpaceLocationCodesRequest(
                    SpaceDesignCodingContract.SchemaVersion,
                    SpaceDesignCodingContract.Rebuild,
                    ScopeZoneLogicalId: null,
                    ExpectedFloorRevision: 1,
                    ExpectedContentRevision: 1));
            var rebuilt = await service.ApplyLocationCodesAsync(
                draft.Id,
                floor.LogicalId,
                new ApplySpaceLocationCodesRequest(
                    SpaceDesignCodingContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    rebuildPreview.Mode,
                    rebuildPreview.ScopeZoneLogicalId,
                    rebuildPreview.BaseFloorRevision,
                    rebuildPreview.BaseContentRevision,
                    rebuildPreview.ProposalHash));
            Assert.Equal(1, rebuilt.AppliedCount);
            Assert.Equal(2, rebuilt.FloorRevision);
            Assert.Equal(2, rebuilt.VersionContentRevision);
            var rebuildAudit = await context.ElementCommandRecords
                .AsNoTracking()
                .SingleAsync(record =>
                    record.CommandBatchId == rebuilt.CommandBatchId);
            Assert.Contains("OLD-GENERATED", rebuildAudit.BeforeJson);
            Assert.DoesNotContain("OLD-GENERATED", rebuildAudit.AfterJson);

            var stalePreview = await service.PreviewLocationCodesAsync(
                draft.Id,
                floor.LogicalId,
                new PreviewSpaceLocationCodesRequest(
                    SpaceDesignCodingContract.SchemaVersion,
                    SpaceDesignCodingContract.Rebuild,
                    ScopeZoneLogicalId: null,
                    ExpectedFloorRevision: 2,
                    ExpectedContentRevision: 2));
            rules.Prefix = "B";
            var stale = await Assert.ThrowsAsync<SpaceProblemException>(
                () => service.ApplyLocationCodesAsync(
                    draft.Id,
                    floor.LogicalId,
                    new ApplySpaceLocationCodesRequest(
                        SpaceDesignCodingContract.SchemaVersion,
                        Guid.NewGuid(),
                        clientId,
                        lease.LeaseId,
                        stalePreview.Mode,
                        stalePreview.ScopeZoneLogicalId,
                        stalePreview.BaseFloorRevision,
                        stalePreview.BaseContentRevision,
                        stalePreview.ProposalHash)));
            Assert.Equal(SpaceErrorCodes.CodingProposalStale, stale.Code);
            Assert.Equal(2, await context.ElementCommandBatches.CountAsync());
            Assert.Equal(2, await context.ElementCommandRecords.CountAsync());
            Assert.Equal(
                "A-Z-A-R-01-02-01-01",
                await context.LocationRevisions
                    .AsNoTracking()
                    .Where(location => location.Id == existingGenerated.Id)
                    .Select(location => location.LocationCode)
                    .SingleAsync());
        });
    }

    private static ApplySpaceElementCommandBatchRequest LifecycleBatch(
        Guid clientInstanceId,
        Guid leaseId,
        long expectedFloorRevision,
        string commandType,
        IReadOnlyList<Guid> logicalIds) =>
        new(
            SpaceElementCommandContract.SchemaVersion,
            Guid.NewGuid(),
            clientInstanceId,
            leaseId,
            expectedFloorRevision,
            logicalIds
                .Select(logicalId => new SpaceElementCommandDto(
                    Guid.NewGuid(),
                    commandType,
                    logicalId,
                    null))
                .ToArray());

    private static SpaceDesignV1Service NewService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock,
        Guid allowedSiteId,
        ISpaceLocationCodeRuleProvider? codingRules = null)
    {
        var cloneStore = new EfSpaceVersionCloneStore(
            context,
            execution,
            clock);
        return new SpaceDesignV1Service(
            context,
            execution,
            clock,
            new TestCursorCodec(),
            new TestAccessEvaluator(allowedSiteId),
            new SpaceVersionCloneCoordinator(execution, cloneStore),
            new SpaceSourceCoordinator(execution),
            codingRules);
    }

    private static async Task<(SpaceModel Model, SpaceModelVersion Published)>
        SeedDesignModelAsync(
            SpaceContext context,
            Guid actorId,
            DateTime nowUtc)
    {
        var model = SpaceModel.Create(
            context.CurrentTenantId,
            Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            context.CurrentTenantId,
            model.Id,
            1,
            "Published baseline");
        context.AddRange(model, published);
        await context.SaveChangesAsync();

        await PublishDesignModelAsync(context, model, published, actorId, nowUtc);
        return (model, published);
    }

    private static async Task<(
        SpaceModel Model,
        SpaceModelVersion Published,
        SpaceFloorRevision PublishedFloor)> SeedPublishedModelWithFloorAsync(
            SpaceContext context,
            Guid actorId,
            DateTime nowUtc)
    {
        var model = SpaceModel.Create(
            context.CurrentTenantId,
            Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            context.CurrentTenantId,
            model.Id,
            1,
            "Published baseline");
        var publishedFloor = SpaceFloorRevision.Create(
            context.CurrentTenantId,
            published.Id,
            Guid.NewGuid(),
            model.SiteId,
            1,
            "PUB",
            "Published floor",
            height: 6000);
        context.AddRange(model, published, publishedFloor);
        await context.SaveChangesAsync();

        await PublishDesignModelAsync(context, model, published, actorId, nowUtc);
        return (model, published, publishedFloor);
    }

    private static async Task PublishDesignModelAsync(
        SpaceContext context,
        SpaceModel model,
        SpaceModelVersion published,
        Guid actorId,
        DateTime nowUtc)
    {
        published.BeginValidation();
        published.MarkReady(ContentHash, "space-v1", WmsHash);
        published.BeginPublishing();
        published.MarkPublished(actorId, nowUtc);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        await context.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceScene_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var setup = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await setup.Database.MigrateAsync();
            await action(connectionString, execution, clock);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(options, execution, clock);
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal = false) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
    }

    private sealed class TestAccessEvaluator(Guid allowedSiteId) :
        ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            if (siteId != allowedSiteId)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.TenantScopeDenied,
                    403,
                    "Site denied.");
            }
        }
    }

    private sealed class MutableCodingRuleProvider :
        ISpaceLocationCodeRuleProvider
    {
        public string Prefix { get; set; } = "A";

        public Task<SpaceLocationCodingCatalog> GetCatalogAsync(
            Guid siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SpaceLocationCodingCatalog(
                "SITE",
                [
                    new SpaceLocationCodingRuleDefinition(
                        Guid.Parse("34b217b5-02e3-4d26-9017-2295e9b97770"),
                        "Default location code",
                        ScopeType: 0,
                        ScopeId: null,
                        [
                            Segment("prefix", "fixed", Prefix),
                            Segment("zone", "zone-code"),
                            Segment("rack", "rack-code"),
                            Segment("column", "col", width: 2),
                            Segment("level", "level", width: 2),
                            Segment("depth", "depth", width: 2, separator: ""),
                        ],
                        IsDefault: true),
                ]));

        private static SpaceLocationCodeSegmentDto Segment(
            string key,
            string source,
            string fixedValue = "",
            int width = 0,
            string separator = "-") =>
            new(
                key,
                key,
                source,
                width,
                "0",
                Start: 1,
                Step: 1,
                separator,
                Upper: true,
                fixedValue,
                Optional: false);
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            throw new NotSupportedException();

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }
}
