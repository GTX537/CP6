using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("Blank", false)]
    [InlineData("Blank", true)]
    [InlineData("PublishedVersion", false)]
    [InlineData("PublishedVersion", true)]
    public async Task PGT017_space_version_reservation_observes_both_orders(string mode, bool ownerFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (model, source) = await SeedSpaceDesignPublished(space, execution, site);
        var clone = mode == "PublishedVersion";
        var input = new CreateSpaceVersionRequest("Simulated draft", clone ? source.Id : null, mode);
        var key = Guid.NewGuid().ToString("N");
        await using var host = await AuthenticationApiProcess.Start(connection, SpaceDesignSettings(writer.Tenant, site));
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var before = await SpaceVersionWriteSet(space);
        await using var owner = new NpgsqlConnection(connection);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await owner.OpenAsync();
        await control.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> write, pause;
        FenceWait writeWait, pauseWait;
        await using (var held = await owner.BeginTransactionAsync())
        {
            await using var holdJobs = new NpgsqlCommand("""LOCK TABLE public."Space_Job" IN SHARE MODE""", owner, held);
            await holdJobs.ExecuteNonQueryAsync();
            if (ownerFirst)
            {
                write = Send(key);
                writeWait = await WaitForFence(observer, owner.ProcessID, "%Space_Job%", null, write);
                pause = Pause();
                pauseWait = await WaitForFence(observer, writeWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
                Assert.Equal(before, await SpaceVersionWriteSet(space));
            }
            else
            {
                await using var locked = await control.BeginTransactionAsync();
                await HoldSpaceVersionTenant(control, locked, writer.Tenant);
                pause = Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                write = Send(key);
                writeWait = await SerialNativeWait(observer, pauseWait.Backend, owner.ProcessID, $"space-version-{mode}", write);
                await locked.RollbackAsync();
                await pause;
            }
            await held.RollbackAsync();
        }

        using var observed = await write;
        using var paused = await pause;
        var suspended = await NumberHttpData<TenantManagementReceiptV1>(paused);
        Assert.False(suspended.Snapshot.Enable);
        var after = await SpaceVersionWriteSet(space);
        await DeviceEvidence($"space-version-{mode}-{ownerFirst}.json",
            new { Mode = mode, BusinessFirst = ownerFirst, WriteWait = writeWait, PauseWait = pauseWait,
                PauseRevision = suspended.Snapshot.Revision, Status = (int)observed.StatusCode, Before = before, After = after });
        Guid? originalId = null;
        if (ownerFirst)
        {
            Assert.Equal(writeWait.Backend, pauseWait.Blocker);
            Assert.True(observed.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.Unauthorized, await OperationsFailure(observed));
            var accepted = await space.Models.AsNoTracking().SingleAsync(x => x.Id == model.Id);
            originalId = accepted.ActiveDraftVersionId;
            Assert.NotNull(originalId);
            Assert.Equal(source.Id, accepted.CurrentPublishedVersionId);
            Assert.Single(await space.Jobs.AsNoTracking().Where(x => x.SubjectId == originalId).ToArrayAsync());
        }
        else
        {
            Assert.Equal(pauseWait.Backend, writeWait.Blocker);
            Assert.True(observed.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(observed));
            Assert.Equal(before, after);
        }
        using (var stale = await Send(key))
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(after, await SpaceVersionWriteSet(space));
        await ReactivateSpaceVersionTenant(host, manager, writer.Tenant, suspended.Snapshot.Revision);
        actor = await Login(host, writer);
        using var recovered = await Send(key);
        Assert.True(recovered.StatusCode == HttpStatusCode.Accepted, await OperationsFailure(recovered));
        var response = (await recovered.Content.ReadFromJsonAsync<CreateSpaceVersionResponse>())!;
        if (ownerFirst) Assert.Equal(originalId, response.Id);
        var draft = await space.Versions.AsNoTracking().SingleAsync(x => x.Id == response.Id);
        Assert.Equal(clone ? SpaceVersionStatus.Initializing : SpaceVersionStatus.Draft, draft.Status);
        Assert.Equal(clone ? source.Id : null, draft.BasedOnVersionId);
        var job = await space.Jobs.AsNoTracking().SingleAsync(x => x.SubjectId == draft.Id);
        Assert.Equal(response.JobId, job.Id);
        Assert.Equal(clone ? SpaceJobType.CloneVersion : SpaceJobType.InitializeVersion, job.JobType);
        Assert.Equal(clone ? SpaceJobStatus.Queued : SpaceJobStatus.Succeeded, job.Status);
        Assert.Equal(clone ? 0 : 1, await space.JobAttempts.CountAsync());
        Assert.Equal(1, await space.IdempotencyRecords.CountAsync());
        var done = await SpaceVersionWriteSet(space);
        using var replay = await Send(key);
        var original = (await replay.Content.ReadFromJsonAsync<CreateSpaceVersionResponse>())!;
        Assert.Equal(response.Id, original.Id);
        Assert.Equal(response.JobId, original.JobId);
        Assert.True(original.IdempotentReplay);
        using var second = await Send(Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(done, await SpaceVersionWriteSet(space));

        Task<HttpResponseMessage> Send(string operationKey)
        {
            var request = OperationsRequest(actor, HttpMethod.Post, $"{SpaceDesignPath}/sites/{site:D}/versions", input);
            request.Headers.Add("Idempotency-Key", operationKey);
            return host.Send(request);
        }

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"), new TenantManagementCommandV1(
                Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    [TenantManagementPostgresTheory]
    [InlineData("SystemTemplate")]
    [InlineData("TenantTemplate")]
    public async Task PGT017_space_version_template_recovers_committed_floor_after_pause_before_lease(string mode)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:read", "model:edit");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (model, source) = await SeedSpaceDesignPublished(space, execution, site);
        await using var host = await AuthenticationApiProcess.Start(connection, SpaceDesignSettings(writer.Tenant, site));
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var template = await SpaceVersionTemplate(host, actor, mode);
        using var previewed = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"{SpaceDesignPath}/templates/{template.Id:D}/instantiate",
            new PreviewSpaceWarehouseTemplateRequest(template.LatestVersion.Id)));
        Assert.True(previewed.StatusCode == HttpStatusCode.OK, await OperationsFailure(previewed));
        var preview = (await previewed.Content.ReadFromJsonAsync<SpaceWarehouseTemplateInstantiationPreviewDto>())!;
        var input = new CreateSpaceVersionRequest("Simulated template", null, mode,
            template.Id, template.LatestVersion.Id, preview.ProposalHash);
        var key = Guid.NewGuid().ToString("N");
        await using var floors = new NpgsqlConnection(connection);
        await using var leases = new NpgsqlConnection(connection);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await floors.OpenAsync();
        await leases.OpenAsync();
        await control.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> inProgress, pause;
        FenceWait floorWait, authorityWait, leaseWait;
        await using (var floorBarrier = await floors.BeginTransactionAsync())
        await using (var leaseBarrier = await leases.BeginTransactionAsync())
        await using (var tenantBarrier = await control.BeginTransactionAsync())
        {
            await using var holdFloors = new NpgsqlCommand("""LOCK TABLE public."Space_FloorRevision" IN SHARE MODE""", floors, floorBarrier);
            await using var holdLeases = new NpgsqlCommand("""LOCK TABLE public."Space_EditLease" IN SHARE MODE""", leases, leaseBarrier);
            await holdFloors.ExecuteNonQueryAsync();
            await holdLeases.ExecuteNonQueryAsync();
            await HoldSpaceVersionTenant(control, tenantBarrier, writer.Tenant);
            inProgress = Send();
            floorWait = await WaitForFence(observer, floors.ProcessID, "%Space_FloorRevision%", null, inProgress);
            pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
                new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
            authorityWait = await WaitForFence(observer, floorWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            await floorBarrier.RollbackAsync();
            var pausing = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            leaseWait = await SerialNativeWait(observer, pausing.Backend, leases.ProcessID, "space-version-template-lease", inProgress);
            Assert.Equal(pausing.Backend, leaseWait.Blocker);
            await tenantBarrier.RollbackAsync();
            await pause;
            await leaseBarrier.RollbackAsync();
        }
        using var denied = await inProgress;
        using var suspended = await pause;
        var pauseReceipt = await NumberHttpData<TenantManagementReceiptV1>(suspended);
        Assert.False(pauseReceipt.Snapshot.Enable);
        Assert.True(denied.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(denied));
        var staged = await space.Models.AsNoTracking().SingleAsync(x => x.Id == model.Id);
        var original = Assert.IsType<Guid>(staged.ActiveDraftVersionId);
        Assert.Equal(source.Id, staged.CurrentPublishedVersionId);
        var floor = await space.FloorRevisions.AsNoTracking().SingleAsync(x => x.ModelVersionId == original);
        Assert.Equal(0, floor.Revision);
        var job = await space.Jobs.AsNoTracking().SingleAsync(x => x.SubjectId == original);
        Assert.Equal(SpaceJobStatus.Succeeded, job.Status);
        Assert.Empty(await space.EditLeases.AsNoTracking().ToArrayAsync());
        Assert.Empty(await space.ElementCommandBatches.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, await space.IdempotencyRecords.CountAsync(x => x.Operation.StartsWith("create-floor:")));
        Assert.Empty(await space.IdempotencyRecords.Where(x => x.Operation.StartsWith("create-version:")).ToArrayAsync());
        var intermediate = await SpaceVersionWriteSet(space);
        using (var obsolete = await Send()) Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        Assert.Equal(intermediate, await SpaceVersionWriteSet(space));
        await ReactivateSpaceVersionTenant(host, manager, writer.Tenant, pauseReceipt.Snapshot.Revision);
        actor = await Login(host, writer);
        using var resumed = await Send();
        Assert.True(resumed.StatusCode == HttpStatusCode.Accepted, await OperationsFailure(resumed));
        var response = (await resumed.Content.ReadFromJsonAsync<CreateSpaceVersionResponse>())!;
        Assert.Equal(original, response.Id);
        Assert.Equal(job.Id, response.JobId);
        var completedFloor = await space.FloorRevisions.AsNoTracking().SingleAsync(x => x.Id == floor.Id);
        Assert.Equal(1, completedFloor.Revision);
        Assert.Equal(preview.Floors.Count, await space.FloorRevisions.CountAsync(x => x.ModelVersionId == original));
        Assert.Equal(preview.Floors.Count, await space.ElementCommandBatches.CountAsync(x => x.ModelVersionId == original));
        Assert.Equal(preview.Counts.Locations, await space.LocationRevisions.CountAsync(x => x.ModelVersionId == original));
        var completed = await SpaceVersionWriteSet(space);
        using var replay = await Send();
        Assert.True(replay.StatusCode == HttpStatusCode.Accepted, await OperationsFailure(replay));
        Assert.True((await replay.Content.ReadFromJsonAsync<CreateSpaceVersionResponse>())!.IdempotentReplay);
        Assert.Equal(completed, await SpaceVersionWriteSet(space));
        await DeviceEvidence($"space-version-template-{mode}-interrupted.json", new
        {
            FloorWait = floorWait, AuthorityWait = authorityWait, LeaseWait = leaseWait, OriginalVersionId = original,
            OriginalJobId = job.Id, PausedStatus = (int)denied.StatusCode, AfterPause = intermediate, AfterRetry = completed
        });

        Task<HttpResponseMessage> Send()
        {
            var request = OperationsRequest(actor, HttpMethod.Post, $"{SpaceDesignPath}/sites/{site:D}/versions", input);
            request.Headers.Add("Idempotency-Key", key);
            return host.Send(request);
        }
    }

    private static async Task HoldSpaceVersionTenant(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId)
    {
        await using var lockTenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",
            connection, transaction);
        lockTenant.Parameters.AddWithValue("tenant", tenantId);
        Assert.Equal(1, await lockTenant.ExecuteScalarAsync());
    }

    private static async Task<SpaceWarehouseTemplateDto> SpaceVersionTemplate(AuthenticationApiProcess host,
        Dictionary<string, string> actor, string mode)
    {
        if (mode == "SystemTemplate")
        {
            using var catalog = await host.Send(OperationsRequest(actor, HttpMethod.Get, SpaceDesignPath + "/templates?scope=System"));
            Assert.True(catalog.StatusCode == HttpStatusCode.OK, await OperationsFailure(catalog));
            return Assert.Single((await catalog.Content.ReadFromJsonAsync<SpaceWarehouseTemplateDto[]>())!);
        }

        var floorKey = new string('层', 80);
        var input = new CreateTenantSpaceWarehouseTemplateRequest(
            "TENANT-START-01", "Tenant starter", null, SpaceWarehouseTemplateContract.SchemaVersion,
            [new(floorKey, "F1", "Ground", 1, 0, 10_000, 10_000, 6_000)],
            [new("zone-1", floorKey, "STORAGE", "Storage", 0, 0, 10_000, 10_000)],
            [new("aisle-1", floorKey, "zone-1", "A01", 0, 5_000, 10_000, 5_000)],
            [new("rack-1", floorKey, "zone-1", "aisle-1", "R01", 1_000, 1_000, 0, 0, 2_000, 1_000, 3_000, 2, 3, 1)]);
        var request = OperationsRequest(actor, HttpMethod.Post, SpaceDesignPath + "/templates", input);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var created = await host.Send(request);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await OperationsFailure(created));
        return (await created.Content.ReadFromJsonAsync<CreateTenantSpaceWarehouseTemplateResponse>())!.Template;
    }

    private static async Task ReactivateSpaceVersionTenant(AuthenticationApiProcess host, Dictionary<string, string> manager,
        Guid tenant, long revision)
    {
        using var restored = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), revision, "reactivate", null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);
    }

    private static async Task<(SpaceModel Model, SpaceModelVersion Published)> SeedSpaceDesignPublished(
        SpaceContext space, SpaceDesignExecution execution, Guid site)
    {
        var model = SpaceModel.Create(execution.TenantId, site);
        space.Models.Add(model);
        await space.SaveChangesAsync();
        var published = SpaceModelVersion.CreateDraft(execution.TenantId, model.Id, 1, "Published");
        published.BeginValidation();
        published.MarkReady(SpaceDesignSampleHash, "simulated", SpaceDesignSampleHash);
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, DateTime.UtcNow);
        space.Versions.Add(published);
        await space.SaveChangesAsync();
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return (model, published);
    }

    private static async Task<string> SpaceVersionWriteSet(SpaceContext space) => JsonSerializer.Serialize(new
    {
        Models = await space.Models.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Versions = await space.Versions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Jobs = await space.Jobs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Attempts = await space.JobAttempts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Receipts = await space.IdempotencyRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Floors = await space.FloorRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Leases = await space.EditLeases.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Batches = await space.ElementCommandBatches.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });
}
