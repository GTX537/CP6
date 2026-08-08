using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExcelCadMatchServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 8, 16, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Start_pins_authoritative_chain_and_replays_one_active_job()
    {
        await using var fixture = await CreateFixtureAsync();
        var request = fixture.Request;

        var first = await fixture.Service.StartAsync(
            fixture.Version.Id,
            request,
            "match-key-1");
        var replay = await fixture.Service.StartAsync(
            fixture.Version.Id,
            request,
            "match-key-1");

        Assert.Equal(first.JobId, replay.JobId);
        Assert.True(replay.IdempotentReplay);
        var job = await fixture.Context.Jobs.SingleAsync(item =>
            item.Id == first.JobId);
        Assert.Equal(SpaceJobType.ExcelCadMatch, job.JobType);
        Assert.Equal(SpaceJobSubjectType.ModelSource, job.SubjectType);
        Assert.Equal(request.ExcelSourceId, job.SubjectId);
        var payload = JsonSerializer.Deserialize<SpaceExcelCadMatchJobPayload>(
            job.PayloadJson,
            JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(request.ExcelSourceId, payload!.ExcelSourceId);
        Assert.Equal(request.CadParseJobId, payload.CadParseJobId);
        Assert.Equal(request.ExpectedContentRevision,
            payload.ExpectedContentRevision);
        Assert.Equal(3, await fixture.Context.Jobs.CountAsync());
        Assert.Single(await fixture.Context.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task Start_rejects_content_revision_drift_before_queueing()
    {
        await using var fixture = await CreateFixtureAsync();
        var request = fixture.Request with { ExpectedContentRevision = 1 };

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.StartAsync(
                fixture.Version.Id,
                request,
                "match-key-drift"));

        Assert.Equal(SpaceErrorCodes.ConcurrencyConflict, error.Code);
        Assert.Equal(2, await fixture.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task External_principal_cannot_create_or_read_match_artifacts()
    {
        await using var fixture = await CreateFixtureAsync();
        var external = new ExternalExecutionContext(
            fixture.Context.CurrentTenantId,
            Guid.NewGuid());
        var service = new SpaceExcelCadMatchService(
            fixture.Context,
            external,
            new AllowAccess(),
            null!,
            new FileServiceProvider(fixture.Files),
            new FixedClock());

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.StartAsync(
                fixture.Version.Id,
                fixture.Request,
                "external-match"));

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
        Assert.Equal(2, await fixture.Context.Jobs.CountAsync());
    }

    [Fact]
    public async Task External_principal_cannot_confirm_match_artifacts()
    {
        await using var fixture = await CreateFixtureAsync();
        var external = new ExternalExecutionContext(
            fixture.Context.CurrentTenantId,
            Guid.NewGuid());
        var service = new SpaceExcelCadApplyService(
            fixture.Context,
            external,
            new AllowAccess(),
            new FileServiceProvider(fixture.Files),
            new FixedClock());

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.ConfirmAsync(
                fixture.Version.Id,
                Guid.NewGuid(),
                new ConfirmSpaceExcelCadMatchRequest(
                    true,
                    Guid.NewGuid(),
                    new string('a', 64),
                    0),
                "external-apply"));

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
    }

    [Fact]
    public async Task Worker_persists_one_authoritative_artifact_and_reuses_it()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Request,
            "match-worker-1");
        var lease = await ClaimAsync(fixture, started.JobId);
        var executor = new SpaceExcelCadMatchJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile));
        var execution = new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadMatchJobProcessor.PersistMatchArtifact);

        var first = await executor.ExecuteAsync(execution);
        var reused = await executor.ExecuteAsync(execution);

        Assert.Equal(first, reused);
        var persisted = await (
                from artifact in fixture.Context.Artifacts.AsNoTracking()
                join file in fixture.Context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId == started.JobId
                select new { Artifact = artifact, File = file })
            .SingleAsync();
        Assert.Equal(
            SpaceArtifactType.ExcelCadMatchPreview,
            persisted.Artifact.ArtifactType);
        Assert.Equal(
            SpaceExcelCadMatchArtifactVersions.ArtifactSchema,
            persisted.Artifact.SchemaVersion);
        await using var stream = await fixture.Files.OpenQuarantinedReadAsync(
            persisted.File.TenantId,
            persisted.File.Id,
            persisted.File.StorageKey);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var value = SpaceExcelCadMatchArtifact.Deserialize(
            await reader.ReadToEndAsync());
        Assert.Equal(started.JobId, value.MatchJobId);
        Assert.Equal(fixture.Request.CadParseJobId, value.CadParseJobId);
        Assert.Equal(1, value.Preview.Summary.ExcelRackRowCount);

        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item =>
            item.Id == started.JobId);
        var attempt = await fixture.Context.JobAttempts.SingleAsync(item =>
            item.Id == lease.AttemptId);
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            attempt.WorkerId,
            Now,
            first.CheckpointJson);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var read = await fixture.Service.GetAsync(
            fixture.Version.Id,
            started.JobId,
            null,
            "R-001",
            null,
            false,
            50,
            null);

        Assert.Equal("Succeeded", read.JobStatus);
        Assert.Equal(persisted.Artifact.Id, read.ArtifactId);
        Assert.Single(read.Rows);
        Assert.Equal(1, read.TotalRowCount);
    }

    [Fact]
    public async Task Confirmed_apply_is_atomic_and_reuses_the_same_command_batch()
    {
        await using var fixture = await CreateFixtureAsync();
        var match = await ProduceSucceededMatchAsync(fixture, "apply-match-1");
        var confirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "apply-confirm-1");
        var lease = await ClaimAsync(
            fixture,
            confirmed.ApplyJobId,
            SpaceExcelCadApplyJobProcessor.Version,
            "apply-worker");
        var executor = new SpaceExcelCadApplyJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile),
            new FixedClock());
        var execution = new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact);

        var first = await executor.ExecuteAsync(execution);
        var replay = await executor.ExecuteAsync(execution);
        var reconfirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "apply-confirm-2");

        Assert.Equal(first, replay);
        Assert.Equal(confirmed.ApplyJobId, reconfirmed.ApplyJobId);
        Assert.True(reconfirmed.IdempotentReplay);
        var result = JsonSerializer.Deserialize<SpaceExcelCadApplyResultV1>(
            first.CheckpointJson,
            JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(1, result!.CreatedRackCount);
        Assert.Equal(0, result.UpdatedRackCount);
        Assert.Equal(0, result.UnchangedRackCount);
        Assert.Equal(1, result.ResultFloorRevision);
        Assert.Equal(1, result.ResultContentRevision);
        var rack = await fixture.Context.RackRevisions.SingleAsync();
        Assert.Equal("R-001", rack.RackCode);
        Assert.Equal("H:160", rack.SourceRef);
        Assert.Equal(SpaceLifecycleState.Active, rack.LifecycleState);
        var source = await fixture.Context.Sources.SingleAsync(item =>
            item.Id == fixture.Request.ExcelSourceId);
        Assert.Equal(SpaceSourceState.Imported, source.State);
        Assert.Equal(confirmed.CommandBatchId, source.ImportedCommandBatchId);
        Assert.Single(await fixture.Context.ElementCommandBatches.ToListAsync());
        Assert.Single(await fixture.Context.ElementCommandRecords.ToListAsync());
        Assert.Equal(1, (await fixture.Context.FloorRevisions.SingleAsync()).Revision);
        Assert.Equal(1, (await fixture.Context.Versions.SingleAsync(item =>
            item.Id == fixture.Version.Id)).ContentRevision);
    }

    [Fact]
    public async Task Confirmed_apply_persists_hierarchy_and_pins_tenant_template_version()
    {
        var original = await CreateFixtureAsync();
        await using var fixture = original with
        {
            Profile = original.Profile with
            {
                Definition = ExcelDefinition(
                    "Racks",
                    "RackLevels",
                    "Locations"),
            },
            Workbook = HierarchyExcelWorkbook(),
        };
        var tenantId = fixture.Context.CurrentTenantId;
        var actorId = Guid.NewGuid();
        var systemAsset = SpaceAsset.CreateSystem(
            "RT-STD",
            "System rack template",
            "RackTemplate",
            null,
            actorId,
            Now);
        var systemVersion = SpaceAssetVersion.CreateReady(
            systemAsset,
            1,
            SpaceAssetFormat.Parametric,
            "{}",
            null,
            null,
            new string('a', 64),
            actorId,
            Now);
        var tenantAsset = SpaceAsset.CreateTenant(
            tenantId,
            "RT-STD",
            "Tenant rack template",
            "RackTemplate",
            null,
            actorId,
            Now);
        var tenantVersion1 = SpaceAssetVersion.CreateReady(
            tenantAsset,
            1,
            SpaceAssetFormat.Parametric,
            "{}",
            null,
            null,
            new string('b', 64),
            actorId,
            Now);
        var tenantVersion2 = SpaceAssetVersion.CreateReady(
            tenantAsset,
            2,
            SpaceAssetFormat.Parametric,
            "{}",
            null,
            null,
            new string('c', 64),
            actorId,
            Now);
        fixture.Context.AddRange(
            systemAsset,
            systemVersion,
            tenantAsset,
            tenantVersion1,
            tenantVersion2);
        await fixture.Context.SaveChangesAsync();

        var match = await ProduceSucceededMatchAsync(fixture, "hierarchy-match");
        var confirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "hierarchy-confirm");
        var lease = await ClaimAsync(
            fixture,
            confirmed.ApplyJobId,
            SpaceExcelCadApplyJobProcessor.Version,
            "hierarchy-worker");
        var executor = new SpaceExcelCadApplyJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile),
            new FixedClock());
        var execution = new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact);

        var first = await executor.ExecuteAsync(execution);
        var replay = await executor.ExecuteAsync(execution);

        Assert.Equal(first, replay);
        var rack = await fixture.Context.RackRevisions.SingleAsync();
        Assert.Equal(tenantVersion2.Id, rack.TemplateVersionId);
        var levels = await fixture.Context.RackLevelRevisions
            .OrderBy(item => item.LevelNo)
            .ToArrayAsync();
        Assert.Equal(2, levels.Length);
        Assert.Equal(500, levels[0].CellWidth);
        Assert.Equal(600, levels[0].CellDepth);
        Assert.Equal(1000m, levels[0].MaxLoad);
        var locations = await fixture.Context.LocationRevisions
            .OrderBy(item => item.LocationCode)
            .ToArrayAsync();
        Assert.Equal(2, locations.Length);
        Assert.All(locations, item =>
            Assert.Equal(SpaceLocationCodeOrigin.Imported, item.CodeOrigin));
        Assert.Equal(500, locations[0].Width);
        Assert.Equal(1000, locations[0].Height);
        Assert.Equal(600, locations[0].Depth);
        var commands = await fixture.Context.ElementCommandRecords
            .OrderBy(item => item.SequenceNo)
            .ToArrayAsync();
        Assert.Equal(5, commands.Length);
        Assert.Equal(Enumerable.Range(0, 5),
            commands.Select(item => item.SequenceNo));
    }

    [Fact]
    public async Task Confirmed_apply_updates_children_and_disables_omitted_authoritative_rows()
    {
        var original = await CreateFixtureAsync();
        await using var fixture = original with
        {
            Profile = original.Profile with
            {
                Definition = ExcelDefinition(
                    "Racks",
                    "RackLevels",
                    "Locations"),
            },
            Workbook = SingleChildHierarchyExcelWorkbook(),
        };
        var tenantId = fixture.Context.CurrentTenantId;
        var floor = await fixture.Context.FloorRevisions.SingleAsync();
        var zone = await fixture.Context.ZoneRevisions.SingleAsync();
        var rack = SpaceRackRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            zone.LogicalId,
            "R-001");
        rack.ConfigureGeometry(0, 0, 0, 0, 1000, 1200, 5000);
        var retainedLevel = SpaceRackLevelRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            rack.LogicalId,
            1,
            100,
            800,
            1,
            1,
            1000,
            1200);
        var omittedLevel = SpaceRackLevelRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            rack.LogicalId,
            3,
            2400,
            800,
            1,
            1,
            1000,
            1200);
        var retainedLocation = SpaceLocationRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            rack.LogicalId,
            "L-001",
            1,
            1,
            1,
            1000,
            800,
            1200,
            codeOrigin: SpaceLocationCodeOrigin.Imported);
        var omittedLocation = SpaceLocationRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            rack.LogicalId,
            "L-OLD",
            1,
            3,
            1,
            1000,
            800,
            1200,
            codeOrigin: SpaceLocationCodeOrigin.Imported);
        fixture.Context.AddRange(
            rack,
            retainedLevel,
            omittedLevel,
            retainedLocation,
            omittedLocation);
        await fixture.Context.SaveChangesAsync();

        var match = await ProduceSucceededMatchAsync(fixture, "update-hierarchy-match");
        var confirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "update-hierarchy-confirm");
        var lease = await ClaimAsync(
            fixture,
            confirmed.ApplyJobId,
            SpaceExcelCadApplyJobProcessor.Version,
            "update-hierarchy-worker");
        var executor = new SpaceExcelCadApplyJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile),
            new FixedClock());

        await executor.ExecuteAsync(new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact));

        Assert.Null((await fixture.Context.RackRevisions.SingleAsync())
            .TemplateVersionId);
        var appliedRetainedLevel = await fixture.Context.RackLevelRevisions
            .SingleAsync(item => item.LogicalId == retainedLevel.LogicalId);
        var appliedOmittedLevel = await fixture.Context.RackLevelRevisions
            .SingleAsync(item => item.LogicalId == omittedLevel.LogicalId);
        Assert.Equal(0, appliedRetainedLevel.BottomZ);
        Assert.Equal(2, appliedRetainedLevel.BinCount);
        Assert.Equal(SpaceLifecycleState.Disabled,
            appliedOmittedLevel.LifecycleState);
        var appliedRetainedLocation = await fixture.Context.LocationRevisions
            .SingleAsync(item => item.LogicalId == retainedLocation.LogicalId);
        var appliedOmittedLocation = await fixture.Context.LocationRevisions
            .SingleAsync(item => item.LogicalId == omittedLocation.LogicalId);
        Assert.Equal(500, appliedRetainedLocation.Width);
        Assert.Equal(1000, appliedRetainedLocation.Height);
        Assert.Equal(SpaceLifecycleState.Disabled,
            appliedOmittedLocation.LifecycleState);
        Assert.Equal(5, await fixture.Context.ElementCommandRecords.CountAsync());
    }

    [Fact]
    public async Task Confirmed_apply_persists_location_types_bindings_and_design_attributes()
    {
        var original = await CreateFixtureAsync();
        await using var fixture = original with
        {
            Profile = original.Profile with
            {
                Definition = ExcelDefinition(
                    "Racks",
                    "RackLevels",
                    "Locations",
                    "Bindings",
                    "Attributes"),
            },
            Workbook = MetadataExcelWorkbook(),
        };
        var match = await ProduceSucceededMatchAsync(
            fixture,
            "metadata-match");
        var confirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "metadata-confirm");
        var lease = await ClaimAsync(
            fixture,
            confirmed.ApplyJobId,
            SpaceExcelCadApplyJobProcessor.Version,
            "metadata-worker");
        var executor = new SpaceExcelCadApplyJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(
                fixture.Files,
                new FixedBindingAuthorityResolver(
                    "cp6-wms-v1",
                    "WH-01")),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile),
            new FixedClock());
        var execution = new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact);

        var first = await executor.ExecuteAsync(execution);
        var replay = await executor.ExecuteAsync(execution);

        Assert.Equal(first, replay);
        var locations = await fixture.Context.LocationRevisions
            .OrderBy(item => item.LocationCode)
            .ToArrayAsync();
        Assert.Equal(SpaceLocationTypes.Storage, locations[0].LocationType);
        Assert.Equal(SpaceLocationTypes.Picking, locations[1].LocationType);
        var bindings = await fixture.Context.LocationExternalBindings
            .OrderBy(item => item.BindingMode)
            .ToArrayAsync();
        Assert.Equal(2, bindings.Length);
        Assert.Equal("cp6-wms-v1", bindings[0].AdapterId);
        Assert.Equal("WH-01", bindings[0].WarehouseCode);
        Assert.Equal(SpaceLocationBindingMode.WmsPrimary,
            bindings[0].BindingMode);
        Assert.Equal(SpaceLocationBindingMode.WmsAlias,
            bindings[1].BindingMode);
        Assert.Equal(bindings[0].LocationLogicalId,
            bindings[1].LocationLogicalId);
        var attributes = await fixture.Context.DesignAttributes
            .OrderBy(item => item.ObjectType)
            .ThenBy(item => item.Key)
            .ToArrayAsync();
        Assert.Equal(3, attributes.Length);
        Assert.Contains(attributes, item =>
            item.ObjectType == SpaceDesignAttributeObjectTypes.Rack &&
            item.Namespace == SpaceDesignAttributeNamespaces.Owner &&
            item.Value == "OWNER-01");
        Assert.Contains(attributes, item =>
            item.ObjectType == SpaceDesignAttributeObjectTypes.RackLevel &&
            item.ObjectLogicalId != Guid.Empty);
        Assert.Contains(attributes, item =>
            item.ObjectType == SpaceDesignAttributeObjectTypes.Location &&
            item.Unit == "C");
        var commands = await fixture.Context.ElementCommandRecords
            .OrderBy(item => item.SequenceNo)
            .ToArrayAsync();
        Assert.Equal(10, commands.Length);
        Assert.Equal(Enumerable.Range(0, 10),
            commands.Select(item => item.SequenceNo));
    }

    [Fact]
    public async Task Confirmed_apply_updates_and_removes_omitted_metadata_rows()
    {
        var original = await CreateFixtureAsync();
        await using var fixture = original with
        {
            Profile = original.Profile with
            {
                Definition = ExcelDefinition(
                    "Racks",
                    "RackLevels",
                    "Locations",
                    "Bindings",
                    "Attributes"),
            },
            Workbook = SingleMetadataExcelWorkbook(),
        };
        var tenantId = fixture.Context.CurrentTenantId;
        var floor = await fixture.Context.FloorRevisions.SingleAsync();
        var zone = await fixture.Context.ZoneRevisions.SingleAsync();
        var source = await fixture.Context.Sources.SingleAsync(item =>
            item.Id == fixture.Request.ExcelSourceId);
        var rack = SpaceRackRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            zone.LogicalId,
            "R-001");
        rack.ConfigureGeometry(0, 0, 0, 0, 1000, 1200, 5000);
        var level = SpaceRackLevelRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            rack.LogicalId,
            1,
            0,
            900,
            1,
            1,
            1000,
            1200);
        var location = SpaceLocationRevision.Create(
            tenantId,
            fixture.Version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            rack.LogicalId,
            "L-001",
            1,
            1,
            1,
            1000,
            900,
            1200,
            locationType: SpaceLocationTypes.Buffer);
        var primary = SpaceLocationExternalBinding.Create(
            tenantId,
            Guid.NewGuid(),
            location,
            "cp6-wms-v1",
            "WH-01",
            "EXT-001",
            SpaceLocationBindingMode.WmsAlias,
            source,
            "Bindings!2");
        var omittedBinding = SpaceLocationExternalBinding.Create(
            tenantId,
            Guid.NewGuid(),
            location,
            "cp6-wms-v1",
            "WH-01",
            "EXT-OLD",
            SpaceLocationBindingMode.WmsPrimary,
            source,
            "Bindings!3");
        var retainedAttribute = SpaceDesignAttribute.Create(
            tenantId,
            Guid.NewGuid(),
            fixture.Version.Id,
            SpaceDesignAttributeObjectTypes.Location,
            location.LogicalId,
            SpaceDesignAttributeNamespaces.Custom,
            "TargetTemperature",
            "12",
            "C",
            source,
            "Attributes!2");
        var omittedAttribute = SpaceDesignAttribute.Create(
            tenantId,
            Guid.NewGuid(),
            fixture.Version.Id,
            SpaceDesignAttributeObjectTypes.Location,
            location.LogicalId,
            SpaceDesignAttributeNamespaces.Owner,
            "LegacyOwner",
            "OLD",
            null,
            source,
            "Attributes!3");
        fixture.Context.AddRange(
            rack,
            level,
            location,
            primary,
            omittedBinding,
            retainedAttribute,
            omittedAttribute);
        await fixture.Context.SaveChangesAsync();

        var match = await ProduceSucceededMatchAsync(
            fixture,
            "metadata-update-match");
        var confirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "metadata-update-confirm");
        var lease = await ClaimAsync(
            fixture,
            confirmed.ApplyJobId,
            SpaceExcelCadApplyJobProcessor.Version,
            "metadata-update-worker");
        var executor = new SpaceExcelCadApplyJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(
                fixture.Files,
                new FixedBindingAuthorityResolver(
                    "cp6-wms-v1",
                    "WH-01")),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile),
            new FixedClock());

        await executor.ExecuteAsync(new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact));

        var appliedLocation = await fixture.Context.LocationRevisions
            .SingleAsync(item => item.LogicalId == location.LogicalId);
        Assert.Equal(SpaceLocationTypes.Staging,
            appliedLocation.LocationType);
        var activeBindings = await fixture.Context.LocationExternalBindings
            .ToArrayAsync();
        Assert.Single(activeBindings);
        Assert.Equal(primary.Id, activeBindings[0].Id);
        Assert.Equal(SpaceLocationBindingMode.WmsPrimary,
            activeBindings[0].BindingMode);
        var allBindings = await fixture.Context.LocationExternalBindings
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .ToArrayAsync();
        Assert.True(allBindings.Single(item => item.Id == omittedBinding.Id)
            .IsDeleted);
        var activeAttributes = await fixture.Context.DesignAttributes
            .ToArrayAsync();
        Assert.Single(activeAttributes);
        Assert.Equal("18", activeAttributes[0].Value);
        var allAttributes = await fixture.Context.DesignAttributes
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .ToArrayAsync();
        Assert.True(allAttributes.Single(item => item.Id == omittedAttribute.Id)
            .IsDeleted);
        Assert.Equal(7,
            await fixture.Context.ElementCommandRecords.CountAsync());
    }

    [Fact]
    public async Task Confirmed_apply_rejects_non_authoritative_warehouse_binding()
    {
        var original = await CreateFixtureAsync();
        await using var fixture = original with
        {
            Profile = original.Profile with
            {
                Definition = ExcelDefinition(
                    "Racks",
                    "RackLevels",
                    "Locations",
                    "Bindings",
                    "Attributes"),
            },
            Workbook = MetadataExcelWorkbook(),
        };
        var match = await ProduceSucceededMatchAsync(
            fixture,
            "wrong-warehouse-match");
        var confirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "wrong-warehouse-confirm");
        var lease = await ClaimAsync(
            fixture,
            confirmed.ApplyJobId,
            SpaceExcelCadApplyJobProcessor.Version,
            "wrong-warehouse-worker");
        var executor = new SpaceExcelCadApplyJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(
                fixture.Files,
                new FixedBindingAuthorityResolver(
                    "cp6-wms-v1",
                    "OTHER-WAREHOUSE")),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile),
            new FixedClock());

        var error = await Assert.ThrowsAsync<SpaceJobProcessingException>(() =>
            executor.ExecuteAsync(new SpaceJobStepExecution(
                lease,
                1,
                SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact)));

        Assert.Equal(SpaceErrorCodes.ExcelCadApplyArtifactInvalid,
            error.ErrorCode);
        Assert.Empty(await fixture.Context.RackRevisions.ToArrayAsync());
        Assert.Empty(await fixture.Context.LocationExternalBindings
            .ToArrayAsync());
        Assert.Empty(await fixture.Context.ElementCommandRecords.ToArrayAsync());
    }

    [Fact]
    public async Task Revision_drift_fails_before_any_apply_write()
    {
        await using var fixture = await CreateFixtureAsync();
        var match = await ProduceSucceededMatchAsync(fixture, "apply-match-drift");
        var confirmed = await fixture.ApplyService.ConfirmAsync(
            fixture.Version.Id,
            match.JobId,
            new ConfirmSpaceExcelCadMatchRequest(
                true,
                match.ArtifactId,
                match.Artifact.ArtifactPayloadSha256,
                fixture.Version.ContentRevision),
            "apply-confirm-drift");
        var version = await fixture.Context.Versions.SingleAsync(item =>
            item.Id == fixture.Version.Id);
        version.TouchContent();
        await fixture.Context.SaveChangesAsync();
        var lease = await ClaimAsync(
            fixture,
            confirmed.ApplyJobId,
            SpaceExcelCadApplyJobProcessor.Version,
            "apply-worker-drift");
        var executor = new SpaceExcelCadApplyJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile),
            new FixedClock());

        var error = await Assert.ThrowsAsync<SpaceJobProcessingException>(() =>
            executor.ExecuteAsync(new SpaceJobStepExecution(
                lease,
                1,
                SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact)));

        Assert.Equal(SpaceErrorCodes.ConcurrencyConflict, error.ErrorCode);
        Assert.Empty(await fixture.Context.RackRevisions.ToListAsync());
        Assert.Empty(await fixture.Context.ElementCommandBatches.ToListAsync());
        Assert.Empty(await fixture.Context.ElementCommandRecords.ToListAsync());
        Assert.Equal(0, (await fixture.Context.FloorRevisions.SingleAsync()).Revision);
        Assert.Equal(
            SpaceSourceState.PreviewReady,
            (await fixture.Context.Sources.SingleAsync(item =>
                item.Id == fixture.Request.ExcelSourceId)).State);
    }

    private static async Task<ProducedMatch> ProduceSucceededMatchAsync(
        Fixture fixture,
        string idempotencyKey)
    {
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Request,
            idempotencyKey);
        var lease = await ClaimAsync(fixture, started.JobId);
        var executor = new SpaceExcelCadMatchJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            new FixedWorkbookReader(fixture.Workbook),
            new FixedMappingService(fixture.Profile));
        var output = await executor.ExecuteAsync(new SpaceJobStepExecution(
            lease,
            1,
            SpaceExcelCadMatchJobProcessor.PersistMatchArtifact));
        var persisted = await (
                from artifact in fixture.Context.Artifacts.AsNoTracking()
                join file in fixture.Context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId == started.JobId
                select new { Artifact = artifact, File = file })
            .SingleAsync();
        await using var stream = await fixture.Files.OpenQuarantinedReadAsync(
            persisted.File.TenantId,
            persisted.File.Id,
            persisted.File.StorageKey);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var artifactValue = SpaceExcelCadMatchArtifact.Deserialize(
            await reader.ReadToEndAsync());

        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item =>
            item.Id == started.JobId);
        var attempt = await fixture.Context.JobAttempts.SingleAsync(item =>
            item.Id == lease.AttemptId);
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            attempt.WorkerId,
            Now,
            output.CheckpointJson);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new ProducedMatch(
            started.JobId,
            persisted.Artifact.Id,
            artifactValue);
    }

    private static async Task<SpaceJobLease> ClaimAsync(
        Fixture fixture,
        Guid jobId,
        string processorVersion = SpaceExcelCadMatchJobProcessor.Version,
        string workerId = "match-worker")
    {
        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == jobId);
        var attempt = job.Claim(
            workerId,
            processorVersion,
            Now,
            TimeSpan.FromMinutes(5));
        fixture.Context.JobAttempts.Add(attempt);
        await fixture.Context.SaveChangesAsync();
        return new SpaceJobLease(
            job.TenantId,
            job.Id,
            attempt.Id,
            attempt.AttemptNo,
            attempt.WorkerId,
            job.JobType,
            job.SubjectType,
            job.SubjectId,
            job.InputHash,
            job.LockExpiresAtUtc!.Value,
            job.RowVersion);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tenantId = Guid.NewGuid();
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            new FixedClock());
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published");
        published.BeginValidation();
        published.MarkReady(new string('a', 64), "space-v1", new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            2,
            "Draft",
            published.Id);
        model.ReserveDraft(version);

        var excelBytes = Encoding.UTF8.GetBytes("excel-source");
        var excelFile = CleanFile(
            tenantId,
            "racks.xlsx",
            ".xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SpaceFileRetentionClass.Source,
            excelBytes);
        var excel = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Excel,
            excelFile,
            "racks.xlsx");
        var mappingProfileId = Guid.NewGuid();
        excel.ConfigureImport(
            SpaceExcelPreflightJobProcessor.Version,
            mappingProfileId,
            1,
            null,
            null,
            null);
        excel.BeginParsing();
        excel.MarkPreviewReady();

        var cadBytes = Encoding.UTF8.GetBytes("cad-source");
        var cadFile = CleanFile(
            tenantId,
            "warehouse.dxf",
            ".dxf",
            "application/vnd.autocad.dxf",
            SpaceFileRetentionClass.Source,
            cadBytes);
        var cad = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Dxf,
            cadFile,
            "warehouse.dxf");
        var floorId = Guid.NewGuid();
        var cadAuthority = BuildCadAuthority(
            tenantId,
            cad.Id,
            cadFile.Sha256!,
            floorId);
        var metadata = cadAuthority.Metadata;
        var transformHash = cadAuthority.Preview.CoordinateTransformSha256;
        cad.ConfigureImport(
            SpaceCadParseJobProcessor.Version,
            cadAuthority.Profile.ProfileId,
            cadAuthority.Profile.Version,
            SpaceCadUnit.Millimeter.ToString(),
            1,
            JsonSerializer.Serialize(metadata, JsonOptions));
        cad.BeginParsing();
        cad.MarkPreviewReady();

        var preflightPayload = new SpaceExcelPreflightJobPayload(
            1,
            version.Id,
            excel.Id,
            mappingProfileId,
            1,
            new string('d', 64));
        var preflight = SucceededJob(
            tenantId,
            execution.ActorId,
            SpaceJobType.ExcelPreview,
            excel.Id,
            JsonSerializer.Serialize(preflightPayload, JsonOptions));
        var cadPayload = new SpaceCadParseJobPayload(
            1,
            version.Id,
            cad.Id,
            cadFile.Id,
            cadFile.Sha256!,
            SpaceCadSourceFormat.Dxf,
            floorId,
            SpaceCadUnit.Millimeter,
            1,
            JsonSerializer.Serialize(metadata, JsonOptions),
            transformHash,
            cad.MappingProfileId!.Value,
            1,
            cadAuthority.Profile.DefinitionSha256,
            cadAuthority.Preview.MappingPreviewSha256);
        var cadParse = SucceededJob(
            tenantId,
            execution.ActorId,
            SpaceJobType.CadParse,
            cad.Id,
            JsonSerializer.Serialize(cadPayload, JsonOptions));
        var previewSet = SpaceCadPreviewSet.Create(
            tenantId,
            version.Id,
            cad.Id,
            cadParse.Id,
            cadAuthority.Preview,
            cadAuthority.Diagnostics);
        var previewBytes = Encoding.UTF8.GetBytes(
            SpaceCadPreviewSet.Serialize(previewSet));
        var previewFile = CleanFile(
            tenantId,
            "preview-set.json",
            ".json",
            "application/json",
            SpaceFileRetentionClass.Artifact,
            previewBytes);
        var previewArtifact = SpaceArtifact.Create(
            tenantId,
            version.Id,
            cad,
            previewFile,
            SpaceArtifactType.PreviewSet,
            SpaceCadPreviewSetVersions.ArtifactSchema);
        previewArtifact.AttachToJob(cadParse);

        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            floorId,
            model.SiteId,
            1,
            "F01",
            "Floor 01");
        var zone = SpaceZoneRevision.Create(
            tenantId,
            version.Id,
            Guid.NewGuid(),
            floorId,
            "Z1",
            0,
            "Zone 1");
        var excelDefinition = ExcelDefinition();
        var excelProfile = new SpaceExcelMappingProfileDto(
            mappingProfileId,
            "Authoritative match test",
            "Tenant",
            1,
            false,
            preflightPayload.MappingDefinitionHash,
            excelDefinition,
            null,
            null,
            null,
            null,
            null);
        var workbook = ExcelWorkbook();
        var files = new MemoryFileStore();
        files.Seed(excelFile.StorageKey, excelBytes);
        files.Seed(cadFile.StorageKey, cadBytes);
        files.Seed(previewFile.StorageKey, previewBytes);

        context.AddRange(
            model,
            published,
            version,
            excelFile,
            excel,
            cadFile,
            cad,
            preflight,
            cadParse,
            previewFile,
            previewArtifact,
            floor,
            zone);
        await context.SaveChangesAsync();
        var service = new SpaceExcelCadMatchService(
            context,
            execution,
            new AllowAccess(),
            null!,
            new FileServiceProvider(files),
            new FixedClock());
        var applyService = new SpaceExcelCadApplyService(
            context,
            execution,
            new AllowAccess(),
            new FileServiceProvider(files),
            new FixedClock());
        return new Fixture(
            context,
            version,
            new StartSpaceExcelCadMatchRequest(
                excel.Id,
                preflight.Id,
                cad.Id,
                cadParse.Id,
                floorId,
                version.ContentRevision),
            service,
            applyService,
            files,
            excelProfile,
            workbook);
    }

    private static SpaceJob SucceededJob(
        Guid tenantId,
        Guid actorId,
        SpaceJobType type,
        Guid subjectId,
        string payloadJson)
    {
        var job = SpaceJob.CreateQueued(
            tenantId,
            type,
            SpaceJobSubjectType.ModelSource,
            subjectId,
            new string(type == SpaceJobType.ExcelPreview ? '1' : '2', 64),
            Hash(payloadJson),
            50,
            3,
            actorId,
            Now,
            Guid.NewGuid(),
            payloadJson);
        var attempt = job.Claim(
            "test-worker",
            "test-processor",
            Now,
            TimeSpan.FromMinutes(5));
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            attempt.WorkerId,
            Now,
            "{}");
        return job;
    }

    private static SpaceFile CleanFile(
        Guid tenantId,
        string name,
        string extension,
        string contentType,
        SpaceFileRetentionClass retention,
        byte[] bytes)
    {
        var id = Guid.NewGuid();
        var file = SpaceFile.CreateUploading(
            id,
            tenantId,
            $"{tenantId:N}/{id:N}/content",
            name,
            contentType,
            retention);
        file.CompleteQuarantine(contentType, extension, bytes.Length, Hash(bytes));
        file.BeginScanning();
        file.MarkClean("test", "v1");
        return file;
    }

    private static CadAuthority BuildCadAuthority(
        Guid tenantId,
        Guid sourceId,
        string sourceSha256,
        Guid floorId)
    {
        var request = new SpaceCadConversionRequest(
            tenantId,
            Guid.NewGuid(),
            sourceId,
            sourceSha256,
            SpaceCadSourceFormat.Dxf,
            "match-test",
            "1.0.0");
        var bounds = new SpaceCadBoundsV1(0, 0, 1_000, 1_200);
        var points = new SpaceCadPointV1[]
        {
            new(0, 0),
            new(1_000, 0),
            new(1_000, 1_200),
            new(0, 1_200),
            new(0, 0),
        };
        var entity = new SpaceCadIrEntityV1(
            "H:160",
            SpaceCadIrEntityType.ClosedPolyline,
            "LWPOLYLINE",
            "RACK",
            null,
            points,
            null,
            null,
            null,
            SpaceCadAffineTransformV1.Identity,
            bounds,
            true,
            true,
            new Dictionary<string, string> { ["CODE"] = "R-001" });
        var package = new SpaceCadIrPackageV1(
            new SpaceCadIrDocumentV1(
                SpaceCadIrVersions.SchemaVersion,
                sourceSha256,
                SpaceCadSourceFormat.Dxf,
                "AC1032",
                SpaceCadUnit.Millimeter,
                1,
                SpaceCadIrVersions.CoordinateSystem,
                bounds,
                request.ConverterId,
                request.ConverterVersion),
            [new SpaceCadIrLayerV1("RACK", "RACK", 1)],
            [],
            [entity],
            [],
            new SpaceCadIrSummaryV1(1, 0, 1, 1, 0, 0, bounds));
        var preparation = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            new SpaceCadCoordinateConfirmationV1(
                sourceSha256,
                true,
                SpaceCadUnit.Millimeter,
                new SpaceCadPointV1(0, 0),
                new SpaceCadMillimeterPointV1(0, 0),
                0,
                new SpaceCadFloorAssignmentV1(
                    floorId,
                    "F01",
                    1,
                    0,
                    SpaceCadCoordinateVersions.TargetCoordinateSystem,
                    new SpaceCadBoundsV1(-10_000, -10_000, 20_000, 20_000))));
        var inventory = SpaceCadInventory.Build(request, preparation);
        var profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
            SpaceCadMappingVersions.SchemaVersion,
            Guid.NewGuid(),
            1,
            "Match test profile",
            SpaceCadMappingScope.System,
            null,
            true,
            null,
            null,
            [new SpaceCadMappingRuleV1(
                "L-RACK",
                100,
                SpaceCadMappingSourceKind.Layer,
                SpaceCadMappingMatchKind.Exact,
                "RACK",
                null,
                null,
                null,
                SpaceCadSemanticTarget.Rack,
                null,
                SpaceCadGeometryRule.ClosedBoundary,
                5_000,
                null,
                0.95m,
                true)]));
        var mapping = SpaceCadMapping.Preview(tenantId, inventory, profile);
        var preview = SpaceCadSemanticParser.Parse(
            request,
            preparation,
            inventory,
            profile,
            mapping);
        var diagnostics = SpaceCadSemanticDiagnostics.Build(
            request,
            preparation,
            inventory,
            profile,
            mapping,
            preview);
        return new CadAuthority(
            preparation.Metadata,
            profile,
            preview,
            diagnostics);
    }

    private static SpaceExcelMappingDefinitionDto ExcelDefinition(
        params string[] sheets) => new(
        SpaceExcelTargetCatalog.MappingSchemaVersion,
        "Ignore",
        "Reject",
        "Reject",
        sheets.Select(sheet => new SpaceExcelSheetMappingDto(
            sheet,
            sheet,
            "Exact",
            1,
            2,
            SpaceExcelTargetCatalog.ForSheet(sheet)
                .Select(field => new SpaceExcelColumnMappingDto(
                    field.Field,
                    field.Field,
                    null,
                    field.DataType,
                    null,
                    null,
                    field.IsBusinessKey,
                    field.ReferenceTarget,
                    [],
                    null))
                .ToArray()))
            .ToArray());

    private static SpaceExcelMappingDefinitionDto ExcelDefinition() =>
        ExcelDefinition("Racks");

    private static SpaceExcelWorkbookData ExcelWorkbook()
    {
        var fields = SpaceExcelTargetCatalog.ForSheet("Racks");
        var values = new Dictionary<string, string?>
        {
            ["FloorCode"] = "F01",
            ["ZoneCode"] = "Z1",
            ["RackCode"] = "R-001",
            ["XMm"] = "0",
            ["YMm"] = "0",
            ["ZMm"] = "0",
            ["WidthMm"] = "1000",
            ["DepthMm"] = "1200",
            ["HeightMm"] = "5000",
            ["RotationZDeg"] = "0",
            ["RackTemplateCode"] = null,
            ["LifecycleStatus"] = "Active",
        };
        var header = new SpaceExcelWorkbookRow(
            1,
            fields.Select((field, index) => new SpaceExcelWorkbookCell(
                    index + 1,
                    ColumnName(index + 1),
                    field.Field,
                    false))
                .ToDictionary(item => item.ColumnIndex));
        var row = new SpaceExcelWorkbookRow(
            2,
            fields.Select((field, index) => new SpaceExcelWorkbookCell(
                    index + 1,
                    ColumnName(index + 1),
                    values.GetValueOrDefault(field.Field),
                    false))
                .ToDictionary(item => item.ColumnIndex));
        return new SpaceExcelWorkbookData(
            [new SpaceExcelWorkbookSheet("Racks", [header, row])]);
    }

    private static SpaceExcelWorkbookData HierarchyExcelWorkbook(
        bool withTemplate = true) => new(
        [
            Sheet("Racks",
                new Dictionary<string, string?>
                {
                    ["FloorCode"] = "F01",
                    ["ZoneCode"] = "Z1",
                    ["RackCode"] = "R-001",
                    ["XMm"] = "0",
                    ["YMm"] = "0",
                    ["ZMm"] = "0",
                    ["WidthMm"] = "1000",
                    ["DepthMm"] = "1200",
                    ["HeightMm"] = "5000",
                    ["RotationZDeg"] = "0",
                    ["RackTemplateCode"] = withTemplate ? "RT-STD" : null,
                    ["LifecycleStatus"] = "Active",
                }),
            Sheet("RackLevels",
                new Dictionary<string, string?>
                {
                    ["RackCode"] = "R-001",
                    ["LevelNo"] = "1",
                    ["BottomZMm"] = "0",
                    ["ClearHeightMm"] = "1000",
                    ["BinCount"] = "2",
                    ["DepthCount"] = "2",
                    ["LoadCapacityKg"] = "1000",
                    ["LifecycleStatus"] = "Active",
                },
                new Dictionary<string, string?>
                {
                    ["RackCode"] = "R-001",
                    ["LevelNo"] = "2",
                    ["BottomZMm"] = "1200",
                    ["ClearHeightMm"] = "900",
                    ["BinCount"] = "2",
                    ["DepthCount"] = "1",
                    ["LoadCapacityKg"] = null,
                    ["LifecycleStatus"] = "Disabled",
                }),
            Sheet("Locations",
                new Dictionary<string, string?>
                {
                    ["LocationCode"] = "L-001",
                    ["RackCode"] = "R-001",
                    ["ColumnNo"] = "1",
                    ["LevelNo"] = "1",
                    ["DepthNo"] = "1",
                    ["LifecycleStatus"] = "Active",
                    ["LocationType"] = null,
                },
                new Dictionary<string, string?>
                {
                    ["LocationCode"] = "L-002",
                    ["RackCode"] = "R-001",
                    ["ColumnNo"] = "2",
                    ["LevelNo"] = "1",
                    ["DepthNo"] = "2",
                    ["LifecycleStatus"] = "Active",
                    ["LocationType"] = null,
                }),
        ]);

    private static SpaceExcelWorkbookData SingleChildHierarchyExcelWorkbook()
    {
        var workbook = HierarchyExcelWorkbook(withTemplate: false);
        return new SpaceExcelWorkbookData(workbook.Sheets
            .Select(sheet => sheet.Name == "Racks"
                ? sheet
                : sheet with { Rows = sheet.Rows.Take(2).ToArray() })
            .ToArray());
    }

    private static SpaceExcelWorkbookData MetadataExcelWorkbook()
    {
        var hierarchy = HierarchyExcelWorkbook(withTemplate: false);
        return new SpaceExcelWorkbookData(
        [
            hierarchy.Sheets[0],
            hierarchy.Sheets[1],
            Sheet("Locations",
                new Dictionary<string, string?>
                {
                    ["LocationCode"] = "L-001",
                    ["RackCode"] = "R-001",
                    ["ColumnNo"] = "1",
                    ["LevelNo"] = "1",
                    ["DepthNo"] = "1",
                    ["LifecycleStatus"] = "Active",
                    ["LocationType"] = "Storage",
                },
                new Dictionary<string, string?>
                {
                    ["LocationCode"] = "L-002",
                    ["RackCode"] = "R-001",
                    ["ColumnNo"] = "2",
                    ["LevelNo"] = "1",
                    ["DepthNo"] = "2",
                    ["LifecycleStatus"] = "Active",
                    ["LocationType"] = "Picking",
                }),
            Sheet("Bindings",
                new Dictionary<string, string?>
                {
                    ["WmsWarehouseCode"] = "WH-01",
                    ["ExternalLocationId"] = "EXT-001",
                    ["LocationCode"] = "L-001",
                    ["BindingMode"] = "WmsPrimary",
                },
                new Dictionary<string, string?>
                {
                    ["WmsWarehouseCode"] = "WH-01",
                    ["ExternalLocationId"] = "EXT-001-ALIAS",
                    ["LocationCode"] = "L-001",
                    ["BindingMode"] = "WmsAlias",
                }),
            Sheet("Attributes",
                new Dictionary<string, string?>
                {
                    ["ObjectType"] = "Rack",
                    ["BusinessKey"] = "R-001",
                    ["Namespace"] = "Owner",
                    ["Key"] = "OwnerCode",
                    ["Value"] = "OWNER-01",
                    ["Unit"] = null,
                },
                new Dictionary<string, string?>
                {
                    ["ObjectType"] = "RackLevel",
                    ["BusinessKey"] = "R-001/1",
                    ["Namespace"] = "Manufacturing",
                    ["Key"] = "BeamProfile",
                    ["Value"] = "B-100",
                    ["Unit"] = "mm",
                },
                new Dictionary<string, string?>
                {
                    ["ObjectType"] = "Location",
                    ["BusinessKey"] = "L-001",
                    ["Namespace"] = "Custom",
                    ["Key"] = "TargetTemperature",
                    ["Value"] = "18",
                    ["Unit"] = "C",
                }),
        ]);
    }

    private static SpaceExcelWorkbookData SingleMetadataExcelWorkbook() => new(
    [
        Sheet("Racks",
            new Dictionary<string, string?>
            {
                ["FloorCode"] = "F01",
                ["ZoneCode"] = "Z1",
                ["RackCode"] = "R-001",
                ["XMm"] = "0",
                ["YMm"] = "0",
                ["ZMm"] = "0",
                ["WidthMm"] = "1000",
                ["DepthMm"] = "1200",
                ["HeightMm"] = "5000",
                ["RotationZDeg"] = "0",
                ["RackTemplateCode"] = null,
                ["LifecycleStatus"] = "Active",
            }),
        Sheet("RackLevels",
            new Dictionary<string, string?>
            {
                ["RackCode"] = "R-001",
                ["LevelNo"] = "1",
                ["BottomZMm"] = "0",
                ["ClearHeightMm"] = "1000",
                ["BinCount"] = "2",
                ["DepthCount"] = "2",
                ["LoadCapacityKg"] = "1000",
                ["LifecycleStatus"] = "Active",
            }),
        Sheet("Locations",
            new Dictionary<string, string?>
            {
                ["LocationCode"] = "L-001",
                ["RackCode"] = "R-001",
                ["ColumnNo"] = "1",
                ["LevelNo"] = "1",
                ["DepthNo"] = "1",
                ["LifecycleStatus"] = "Active",
                ["LocationType"] = "Staging",
            }),
        Sheet("Bindings",
            new Dictionary<string, string?>
            {
                ["WmsWarehouseCode"] = "WH-01",
                ["ExternalLocationId"] = "EXT-001",
                ["LocationCode"] = "L-001",
                ["BindingMode"] = "WmsPrimary",
            }),
        Sheet("Attributes",
            new Dictionary<string, string?>
            {
                ["ObjectType"] = "Location",
                ["BusinessKey"] = "L-001",
                ["Namespace"] = "Custom",
                ["Key"] = "TargetTemperature",
                ["Value"] = "18",
                ["Unit"] = "C",
            }),
    ]);

    private static SpaceExcelWorkbookSheet Sheet(
        string name,
        params IReadOnlyDictionary<string, string?>[] values)
    {
        var fields = SpaceExcelTargetCatalog.ForSheet(name);
        var rows = new List<SpaceExcelWorkbookRow>
        {
            new(
                1,
                fields.Select((field, index) => new SpaceExcelWorkbookCell(
                        index + 1,
                        ColumnName(index + 1),
                        field.Field,
                        false))
                    .ToDictionary(item => item.ColumnIndex)),
        };
        rows.AddRange(values.Select((row, rowIndex) =>
            new SpaceExcelWorkbookRow(
                rowIndex + 2,
                fields.Select((field, columnIndex) =>
                        new SpaceExcelWorkbookCell(
                            columnIndex + 1,
                            ColumnName(columnIndex + 1),
                            row.GetValueOrDefault(field.Field),
                            false))
                    .ToDictionary(item => item.ColumnIndex))));
        return new SpaceExcelWorkbookSheet(name, rows);
    }

    private static string ColumnName(int index)
    {
        var value = index;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static string Hash(string value) =>
        Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed record ExternalExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext
    {
        public bool IsExternal => true;
    }

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class FixedWorkbookReader(SpaceExcelWorkbookData workbook) :
        ISpaceExcelWorkbookReader
    {
        public Task<SpaceExcelWorkbookData> ReadAsync(
            Stream content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(workbook);
    }

    private sealed class FixedMappingService(SpaceExcelMappingProfileDto profile) :
        ISpaceExcelMappingService
    {
        public Task<IReadOnlyList<SpaceExcelMappingProfileDto>> GetProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpaceExcelMappingProfileDto>>([profile]);

        public Task<SpaceExcelMappingProfileDto> GetProfileAsync(
            Guid profileId,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(profile);

        public SpaceExcelMappingPreviewDto Preview(
            PreviewSpaceExcelMappingRequest request) =>
            throw new NotSupportedException();

        public Task<SaveSpaceExcelMappingProfileResponse> SaveProfileAsync(
            SaveSpaceExcelMappingProfileRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryFileStore :
        ISpaceFileStore,
        ISpaceQuarantineStore
    {
        private readonly Dictionary<string, byte[]> _objects =
            new(StringComparer.Ordinal);

        public void Seed(string storageKey, byte[] bytes) =>
            _objects[storageKey] = bytes;

        public Task<ISpaceQuarantineWriteSession> OpenWriteAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            var key = $"{tenantId:N}/{fileId:N}/{Guid.NewGuid():N}.content";
            return Task.FromResult<ISpaceQuarantineWriteSession>(
                new WriteSession(key, _objects));
        }

        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(
                _objects[storageKey],
                writable: false));

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            _objects.Remove(storageKey);
            return Task.CompletedTask;
        }

        private sealed class WriteSession(
            string storageKey,
            IDictionary<string, byte[]> objects) :
            ISpaceQuarantineWriteSession
        {
            private readonly MemoryStream _content = new();
            private bool _committed;

            public string StorageKey { get; } = storageKey;
            public Stream Content => _content;

            public Task CommitAsync(
                CancellationToken cancellationToken = default)
            {
                objects[StorageKey] = _content.ToArray();
                _committed = true;
                return Task.CompletedTask;
            }

            public Task AbortAsync(
                CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync()
            {
                _content.Dispose();
                if (!_committed)
                    objects.Remove(StorageKey);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FixedBindingAuthorityResolver(
        string adapterId,
        string warehouseCode) : ISpaceExcelBindingAuthorityResolver
    {
        public Task<SpaceExcelBindingAuthority?> ResolveAsync(
            Guid siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SpaceExcelBindingAuthority?>(new(
                siteId,
                adapterId,
                warehouseCode));
    }

    private sealed class FileServiceProvider(
        MemoryFileStore files,
        ISpaceExcelBindingAuthorityResolver? bindingAuthority = null) :
        IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISpaceFileStore) ||
            serviceType == typeof(ISpaceQuarantineStore)
                ? files
                : serviceType == typeof(ISpaceExcelBindingAuthorityResolver)
                    ? bindingAuthority
                : null;
    }

    private sealed record CadAuthority(
        SpaceCadCoordinateMetadataV1 Metadata,
        SpaceCadMappingProfileV1 Profile,
        SpaceCadSemanticPreviewV1 Preview,
        SpaceCadSemanticDiagnosticIndexV1 Diagnostics);

    private sealed record ProducedMatch(
        Guid JobId,
        Guid ArtifactId,
        SpaceExcelCadMatchArtifactV1 Artifact);

    private sealed record Fixture(
        SpaceContext Context,
        SpaceModelVersion Version,
        StartSpaceExcelCadMatchRequest Request,
        SpaceExcelCadMatchService Service,
        SpaceExcelCadApplyService ApplyService,
        MemoryFileStore Files,
        SpaceExcelMappingProfileDto Profile,
        SpaceExcelWorkbookData Workbook) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
