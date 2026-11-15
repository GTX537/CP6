using System.Net;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.Platform;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SpaceDesignPath = "api/space/design/v1";
    private static readonly string SpaceDesignSampleHash = new('a', 64);

    [TenantManagementPostgresTheory]
    [InlineData("floor", false, false)]
    [InlineData("element", false, false)]
    [InlineData("layout", false, false)]
    [InlineData("asset", false, false)]
    [InlineData("floor", true, false)]
    [InlineData("element", true, false)]
    [InlineData("layout", true, false)]
    [InlineData("asset", true, false)]
    [InlineData("asset", false, true)]
    public async Task PGT017_space_design_write_and_pause_share_the_native_transaction(string operation, bool ownerFirst, bool revokeRole)
    {
        await using var writer = await Seed();
        await using var manager = await TenantManagementSeed();
        var db = writer.Db;
        var connection = db.Database.GetConnectionString()!;
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "space");
        if (menu is null)
        {
            menu = new() { MenuId = 32740, MenuKey = "space", MenuName = "Simulated Space Design" };
            db.Sys_Menus.Add(menu);
            await db.SaveChangesAsync();
        }
        if (!await db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "model:edit"))
            db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "model:edit", ActionName = "Edit model" });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "model:edit" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var modelSite = Guid.NewGuid();
        var options = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(options, DatabaseContextKind.Space);
        var spaceOptions = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<SpaceContext>(), options,
            connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        await using var space = new SpaceContext(spaceOptions.Options, execution, new SystemSpaceClock());
        await space.Database.MigrateAsync();
        var (version, floor, lease) = await SeedSpaceDesignDraft(space, execution, modelSite);
        var input = SpaceDesignBusinessInput(operation, version, floor, lease);
        var match = operation is "element" or "layout" ? "Space_ElementCommandBatch" : "Space_IdempotencyRecord";
        var fixtureSettings = new Dictionary<string, string>
        {
            ["Space__Compatibility__DesignApiEnabled"] = "true",
            ["Space__Compatibility__Sites__0__TenantId"] = writer.Tenant.ToString("D"),
            ["Space__Compatibility__Sites__0__SiteId"] = modelSite.ToString("D"),
            ["Space__Compatibility__Sites__0__Mode"] = "DesignV1",
            ["Space__Compatibility__Sites__0__CutoverState"] = "DesignV1",
            ["Space__Compatibility__Sites__0__Evidence__BootstrapVerified"] = "true",
            ["Space__Compatibility__Sites__0__Evidence__RuntimeHashVerified"] = "true",
            ["Space__Compatibility__Sites__0__Evidence__WmsIdentityVerified"] = "true"
        };
        await using var host = await AuthenticationApiProcess.Start(connection, fixtureSettings);
        var actor = await Login(host, writer);
        var administrator = await Login(host, manager);
        using var current = await host.Send(OperationsRequest(administrator, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var before = await SpaceDesignWriteSet(space);
        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        if (revokeRole)
        {
            Task<HttpResponseMessage> pending;
            FenceWait waiting;
            await using (var revoking = await control.BeginTransactionAsync())
            {
                await using var removeAction = new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire();
                    DELETE FROM public."Sys_RoleAction"
                    WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='model:edit';
                    """, control, revoking);
                removeAction.Parameters.AddWithValue("tenant", writer.Tenant);
                removeAction.Parameters.AddWithValue("menu", menu.MenuId);
                Assert.Equal(1, await removeAction.ExecuteNonQueryAsync());
                pending = Send();
                waiting = await WaitForFence(observer, control.ProcessID, "%cp6_authentication_v1.acquire%", null, pending);
                Assert.Equal(before, await SpaceDesignWriteSet(space));
                await revoking.CommitAsync();
            }
            using var denied = await pending;
            var afterRevocation = await SpaceDesignWriteSet(space);
            await DeviceEvidence("space-design-model-edit-revoked.json", new
                { Wait = waiting, Status = (int)denied.StatusCode, Before = before, After = afterRevocation });
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(denied));
            Assert.Equal(before, afterRevocation);

            db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "model:edit" });
            await db.SaveChangesAsync();
            using var regranted = await Send();
            Assert.True(regranted.StatusCode == HttpStatusCode.Created, await OperationsFailure(regranted));
            var grantedState = await SpaceDesignWriteSet(space);
            Assert.NotEqual(before, grantedState);
            using var repeated = await Send();
            Assert.True(repeated.StatusCode == HttpStatusCode.Created, await OperationsFailure(repeated));
            Assert.Equal(grantedState, await SpaceDesignWriteSet(space));
            return;
        }

        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> pause;
        FenceWait ownerWait, pauseWait;
        await using (var block = await business.BeginTransactionAsync())
        {
            using var quote = new NpgsqlCommandBuilder();
            await using var ledger = new NpgsqlCommand($"LOCK TABLE public.{quote.QuoteIdentifier(match)} IN SHARE MODE", business, block);
            await ledger.ExecuteNonQueryAsync();
            if (ownerFirst)
            {
                change = Send();
                ownerWait = await WaitForFence(observer, business.ProcessID, $"%{match}%", null, change);
                pause = Pause();
                pauseWait = await WaitForFence(observer, ownerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
                Assert.Equal(before, await SpaceDesignWriteSet(space));
            }
            else
            {
                await using var held = await control.BeginTransactionAsync();
                await using var tenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", control, held);
                tenant.Parameters.AddWithValue("tenant", writer.Tenant);
                Assert.Equal(1, await tenant.ExecuteScalarAsync());
                pause = Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                change = Send();
                ownerWait = await SerialNativeWait(observer, pauseWait.Backend, business.ProcessID, $"space-design-{operation}", change);
                await held.RollbackAsync();
                await pause;
            }
            await block.RollbackAsync();
        }

        using var result = await change;
        using var paused = await pause;
        var snapshot = await NumberHttpData<TenantManagementReceiptV1>(paused);
        Assert.False(snapshot.Snapshot.Enable);
        var after = await SpaceDesignWriteSet(space);
        await DeviceEvidence($"space-design-{operation}-{ownerFirst}.json", new
        {
            OperatingOrder = ownerFirst ? "business-first" : "pause-first", ownerWait, pauseWait,
            Before = before, After = after, PausedRevision = snapshot.Snapshot.Revision, Status = (int)result.StatusCode
        });
        if (ownerFirst)
        {
            Assert.Equal(ownerWait.Backend, pauseWait.Blocker);
            Assert.True(result.StatusCode == input.Status, await OperationsFailure(result));
            Assert.NotEqual(before, after);
        }
        else
        {
            Assert.Equal(pauseWait.Backend, ownerWait.Blocker);
            Assert.True(result.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(result));
            Assert.Equal(before, after);
        }
        using (var obsolete = await Send())
            Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        Assert.Equal(after, await SpaceDesignWriteSet(space));

        using var restored = await host.Send(OperationsRequest(administrator, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), snapshot.Snapshot.Revision, "reactivate", null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);
        actor = await Login(host, writer);
        using var original = await Send();
        Assert.True(original.StatusCode == input.Status, await OperationsFailure(original));
        var committed = await SpaceDesignWriteSet(space);
        if (ownerFirst) Assert.Equal(after, committed);
        else Assert.NotEqual(before, committed);
        using var replay = await Send();
        Assert.True(replay.StatusCode == input.Status, await OperationsFailure(replay));
        Assert.Equal(committed, await SpaceDesignWriteSet(space));
        switch (operation)
        {
            case "floor":
                Assert.Equal(1, await space.FloorRevisions.CountAsync(x => x.FloorCode == "F2"));
                Assert.Equal(1, await space.IdempotencyRecords.CountAsync());
                break;
            case "asset":
                Assert.Equal(1, await space.Assets.CountAsync(x => x.OwnerTenantId == writer.Tenant && x.AssetCode == "SIM"));
                Assert.Equal(1, await space.IdempotencyRecords.CountAsync());
                break;
            case "element":
                Assert.Equal(1, await space.ElementRevisions.CountAsync());
                Assert.Equal(1, await space.ElementCommandRecords.CountAsync());
                break;
            case "layout":
                Assert.Equal(1, await space.ZoneRevisions.CountAsync(x => x.ZoneCode == "Z1"));
                Assert.Equal(1, await space.ElementCommandRecords.CountAsync());
                break;
        }

        Task<HttpResponseMessage> Send()
        {
            var request = OperationsRequest(actor, HttpMethod.Post, input.Path, input.Body);
            request.Headers.Add("Idempotency-Key", input.Key);
            return host.Send(request);
        }

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(administrator, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"), new TenantManagementCommandV1(
                Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    private sealed record SpaceDesignExecution(Guid TenantId, Guid ActorId) : ISpaceExecutionContext;

    private sealed record SpaceDesignInput(string Path, object Body, HttpStatusCode Status, string Key);

    private static async Task<(SpaceModelVersion Version, SpaceFloorRevision Floor, SpaceEditLease Lease)> SeedSpaceDesignDraft(
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
        var draft = SpaceModelVersion.CreateDraft(execution.TenantId, model.Id, 2, "Simulated draft", published.Id);
        space.Versions.AddRange(published, draft);
        await space.SaveChangesAsync();
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        model.ReserveDraft(draft);
        var floor = SpaceFloorRevision.Create(execution.TenantId, draft.Id, Guid.NewGuid(), site, 1, "F1", "Simulated floor", 0, 3000);
        var lease = SpaceEditLease.Create(execution.TenantId, draft.Id, floor.LogicalId, execution.ActorId, "Simulated editor",
            Guid.NewGuid(), DateTime.UtcNow, TimeSpan.FromMinutes(4));
        space.FloorRevisions.Add(floor);
        space.EditLeases.Add(lease);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return (draft, floor, lease);
    }

    private static SpaceDesignInput SpaceDesignBusinessInput(string operation, SpaceModelVersion version,
        SpaceFloorRevision floor, SpaceEditLease lease)
    {
        var versionPath = $"{SpaceDesignPath}/versions/{version.Id:D}";
        var floorPath = $"{versionPath}/floors/{floor.LogicalId:D}";
        var key = Guid.NewGuid().ToString("N");
        return operation switch
        {
            "floor" => new($"{versionPath}/floors", new CreateSpaceFloorRequest("F2", "Simulated added floor", 2, 3000, 3000,
                version.ContentRevision, 10000, 10000), HttpStatusCode.Created, key),
            "element" => new($"{floorPath}/commands", new ApplySpaceElementCommandBatchRequest(
                1, Guid.NewGuid(), lease.ClientInstanceId, lease.LeaseId, floor.Revision,
                [new(Guid.NewGuid(), SpaceElementCommandContract.CreateElement, Guid.NewGuid(), null, CreateElement:
                    new("Wall", """{"schemaVersion":1,"kind":"box","width":1000,"height":2000,"depth":200}""",
                        0, 0, 0, 0, 1000, 2000, 200, null, null, null, null, []))],
                version.ContentRevision), HttpStatusCode.OK, key),
            "layout" => new($"{floorPath}/layout-commands", new ApplySpaceLayoutCommandBatchRequest(
                1, Guid.NewGuid(), lease.ClientInstanceId, lease.LeaseId, floor.Revision, version.ContentRevision,
                [new(Guid.NewGuid(), SpaceLayoutCommandContract.CreateZone, Guid.NewGuid(), CreateZone:
                    new("Z1", "Simulated zone", 1, "[[0,0],[3000,0],[3000,3000],[0,3000],[0,0]]", null, null))]),
                HttpStatusCode.OK, key),
            "asset" => new($"{SpaceDesignPath}/assets", new CreateSpaceAssetRequest("SIM", "Simulated asset", "Fixture",
                "Parametric", "{}", SpaceDesignSampleHash), HttpStatusCode.Created, key),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task<string> SpaceDesignWriteSet(SpaceContext space)
    {
        return JsonSerializer.Serialize(new
        {
            Versions = await space.Versions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Floors = await space.FloorRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Elements = await space.ElementRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Zones = await space.ZoneRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Receipts = await space.IdempotencyRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Batches = await space.ElementCommandBatches.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Records = await space.ElementCommandRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Assets = await space.Assets.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            AssetVersions = await space.AssetVersions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }
}
