using System.Net;
using System.Text.Json;
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("connector-create", true)]
    [InlineData("connector-update", true)]
    [InlineData("connector-delete", true)]
    [InlineData("stop-save", true)]
    [InlineData("stop-delete", true)]
    [InlineData("template-create", true)]
    [InlineData("template-update", true)]
    [InlineData("template-delete", true)]
    [InlineData("template-clone", true)]
    [InlineData("analytics-config", true)]
    [InlineData("analytics-rebuild", true)]
    [InlineData("connector-delete", false)]
    [InlineData("template-clone", false)]
    [InlineData("analytics-rebuild", false)]
    public async Task PGT017_space_connector_template_and_analytics_share_the_authority_commit(string operation, bool revokeFirst)
    {
        await using var h = await SiteFloorSeed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        var site = await db.Space_Sites.AsNoTracking().SingleAsync(x => x.SiteCode == "SIM");
        var floor = await db.Space_Floors.AsNoTracking().SingleAsync(x => x.SiteId == site.Id && x.FloorCode == "FREE");
        var connector = Guid.NewGuid();
        var template = Guid.NewGuid();
        db.Space_Connectors.Add(new() { Id = connector, SiteId = site.Id, ConnectorCode = "LIFT", Name = "original", ConnectorType = 1 });
        db.Space_ConnectorStops.Add(new() { Id = Guid.NewGuid(), ConnectorId = connector, FloorId = floor.Id, X = 7, Y = 8 });
        db.Space_Templates.Add(new() { Id = template, TemplateCode = "BASE", TemplateName = "original", TemplateType = 1, Params = "{}" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var analytics = operation.StartsWith("analytics-", StringComparison.Ordinal);
        var permission = analytics ? "manage" : "edit";
        var resource = analytics ? "space-control-tower" : "space-floor";
        if (analytics)
        {
            if (!await db.Sys_Menus.AnyAsync(x => x.MenuKey == resource))
            {
                db.Sys_Menus.Add(new() { MenuId = 32745, MenuKey = resource, MenuName = resource, RoutePath = "/space/control-tower" });
                await db.SaveChangesAsync();
            }
            await GrantLegacyAuthorityAction(h, resource, permission);
        }

        var input = operation switch
        {
            "connector-create" => new SpaceWrite(HttpMethod.Post, "connector", new ConnectorDto { SiteId = site.Id, ConnectorCode = "SECOND", Name = "created" }, "Space_Connector"),
            "connector-update" => new(HttpMethod.Put, $"connector/{connector}", new ConnectorUpdateDto { Name = "updated", WaitSec = 5, TravelSecPerFloor = 9 }, "Space_Connector"),
            "connector-delete" => new(HttpMethod.Delete, $"connector/{connector}", null, "Space_ConnectorStop"),
            "stop-save" => new(HttpMethod.Put, $"connector/{connector}/stop", new ConnectorStopDto { FloorId = floor.Id, X = 11, Y = 12 }, "Space_ConnectorStop"),
            "stop-delete" => new(HttpMethod.Delete, $"connector/{connector}/stop/{floor.Id}", null, "Space_ConnectorStop"),
            "template-create" => new(HttpMethod.Post, "template", new TemplateDto { TemplateCode = "SECOND", TemplateName = "created" }, "Space_Template"),
            "template-update" => new(HttpMethod.Put, $"template/{template}", new TemplateDto { TemplateCode = "BASE", TemplateName = "updated" }, "Space_Template"),
            "template-delete" => new(HttpMethod.Delete, $"template/{template}", null, "Space_Template"),
            "template-clone" => new(HttpMethod.Post, $"template/{template}/clone", null, "Space_Template"),
            "analytics-config" => new(HttpMethod.Put, $"site/{site.Id}/analytics/config", new SpaceAnalyticsConfigUpdate { WindowDays = 30 }, "Space_AnalyticsConfig"),
            "analytics-rebuild" => new(HttpMethod.Post, $"site/{site.Id}/analytics/abc/rebuild", null, "Space_AbcSnapshot"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, permission);
        var before = await Writes();
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (revokeFirst)
            {
                await using var lockActions = new NpgsqlCommand("""LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await lockActions.ExecuteNonQueryAsync();
                change = Revoke();
                var first = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Write();
                await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                // Table is selected exclusively from this fixture's operation list.
                await using var lockBusiness = new NpgsqlCommand($"""LOCK TABLE public."{input.Table}" IN SHARE MODE""", barrier, hold);
                await lockBusiness.ExecuteNonQueryAsync();
                write = Write();
                var first = await WaitForFence(observer, barrier.ProcessID, $"%{input.Table}%", null, write);
                change = Revoke();
                await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await Writes());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audits);
        Assert.Equal(1, after.Receipts);

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Equal(before, await Writes());
            await GrantLegacyAuthorityAction(h, resource, permission);
            using var allowed = await Write();
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var committed = await Writes();
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await Writes());
        }

        Assert.NotEqual(before, await Writes());
        if (operation == "connector-delete")
        {
            Assert.False(await db.Space_Connectors.AnyAsync(x => x.Id == connector));
            Assert.False(await db.Space_ConnectorStops.AnyAsync(x => x.ConnectorId == connector));
        }
        if (operation == "template-clone")
            Assert.Equal(1, await db.Space_Templates.CountAsync(x => x.TemplateCode == "BASE-COPY"));
        if (operation == "analytics-rebuild")
            Assert.Equal("manual", await db.Space_AbcSnapshots.Where(x => x.SiteId == site.Id).Select(x => x.Trigger).SingleAsync());

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(writer, input.Method, "api/space/" + input.Path, input.Body));
        async Task<string> Writes() => JsonSerializer.Serialize(new
        {
            Connectors = await db.Space_Connectors.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Stops = await db.Space_ConnectorStops.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Templates = await db.Space_Templates.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Configs = await db.Space_AnalyticsConfigs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Snapshots = await db.Space_AbcSnapshots.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }

    private sealed record SpaceWrite(HttpMethod Method, string Path, object? Body, string Table);
}
