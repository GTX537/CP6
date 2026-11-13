using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("oa-delegate-add", true)]
    [InlineData("oa-delegate-remove", true)]
    [InlineData("wf-delegate", true)]
    [InlineData("approver-add", true)]
    [InlineData("approver-edit", true)]
    [InlineData("approver-delete", true)]
    [InlineData("calendar-toggle", true)]
    [InlineData("calendar-toggle", false)]
    [InlineData("calendar-clear", true)]
    [InlineData("calendar-import", true)]
    public async Task PGT017_legacy_effects_share_the_current_action_revocation_fence(string route, bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        var tenant = h.Tenant;
        var action = route switch
        {
            "approver-add" => "add",
            "approver-edit" => "edit",
            "approver-delete" => "del",
            "calendar-toggle" or "calendar-clear" or "calendar-import" => "Calendar.Edit",
            _ => "delegate"
        };
        var resource = action switch
        {
            "add" or "edit" or "del" => "oa-approver-map",
            "Calendar.Edit" => "oa-work-calendar",
            _ => "oa-settings"
        };
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == resource);
        if (menu == null)
        {
            var menuId = resource switch { "oa-settings" => 32723, "oa-approver-map" => 32724, _ => 32725 };
            menu = new() { MenuId = menuId, MenuKey = resource, MenuName = resource };
            db.Sys_Menus.Add(menu);
        }
        db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        var target = new Sys_User { Id = Guid.NewGuid(), UserName = "delegate-target", Password = "unused", RoleId = 72 };
        db.Sys_Users.Add(target);
        var day = new DateTime(2026, 11, 11);
        var delegation = new CP6.Entity.DomainModels.Wf.Wf_FlowDelegate
        {
            Id = Guid.NewGuid(), GrantorId = h.User, DelegateId = target.Id, Enable = true,
            ValidFrom = day, ValidTo = day.AddDays(7)
        };
        var approver = new CP6.Entity.DomainModels.Wf.Wf_ApproverMap
        {
            Id = Guid.NewGuid(), MapKey = "fence", MatchValue = "before", ApproverUserId = h.User, Enable = true
        };
        db.Wf_FlowDelegates.Add(delegation);
        db.Wf_ApproverMaps.Add(approver);
        db.Sys_WorkCalendars.Add(new() { Date = day, IsWorkday = true, Note = "before" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var cookies = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, action);
        var grant = new { DelegateId = target.Id, ValidFrom = day, ValidTo = day.AddDays(7), Remark = "new effect" };
        (HttpMethod method, string path, object? input) = route switch
        {
            "oa-delegate-add" => (HttpMethod.Post, "api/oa/delegate/add", (object?)grant),
            "oa-delegate-remove" => (HttpMethod.Post, "api/oa/delegate/remove", new { delegation.Id }),
            "wf-delegate" => (HttpMethod.Post, "api/wf/advanced/delegate", grant),
            "approver-add" => (HttpMethod.Post, "api/oa/approver-map",
                new { MapKey = "fence", MatchValue = "new", ApproverUserId = h.User, OrderNo = 1 }),
            "approver-edit" => (HttpMethod.Put, $"api/oa/approver-map/{approver.Id}",
                new { MatchValue = "changed", ApproverUserId = h.User, OrderNo = 1, Enable = false }),
            "approver-delete" => (HttpMethod.Delete, $"api/oa/approver-map/{approver.Id}", null),
            "calendar-toggle" => (HttpMethod.Post, "api/oa/work-calendar/toggle",
                new { Date = day, IsWorkday = false, Note = "new" }),
            "calendar-clear" => (HttpMethod.Delete, "api/oa/work-calendar/2026-11-11", null),
            "calendar-import" => (HttpMethod.Post, "api/oa/work-calendar/import-jp", null),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        var before = await Effects();
        using (var allowed = await host.Send(OperationsRequest(cookies, method, path, input)))
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
        Assert.NotEqual(before, await Effects());
        // Use a second valid effect for the race, including routes whose first command consumed the row.
        if (route == "oa-delegate-remove")
            db.Wf_FlowDelegates.Add(new()
            {
                Id = delegation.Id, GrantorId = h.User, DelegateId = target.Id, Enable = true,
                ValidFrom = day, ValidTo = day.AddDays(7)
            });
        if (route == "approver-delete")
            db.Wf_ApproverMaps.Add(new()
            {
                Id = approver.Id, MapKey = "fence", MatchValue = "before", ApproverUserId = h.User, Enable = true
            });
        if (route == "calendar-clear")
            db.Sys_WorkCalendars.Add(new() { Date = day, IsWorkday = true, Note = "before" });
        if (route == "calendar-import")
            await db.Sys_WorkCalendars.Where(c => c.Date != day).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        if (route == "approver-add")
            input = new { MapKey = "fence", MatchValue = "second", ApproverUserId = h.User, OrderNo = 1 };
        if (route == "approver-edit")
            input = new { MatchValue = "second", ApproverUserId = h.User, OrderNo = 2, Enable = true };
        if (route == "calendar-toggle")
            input = new { Date = day, IsWorkday = true, Note = "second" };
        before = await Effects();
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            FenceWait first;
            FenceWait waiting;
            if (revokeFirst)
            {
                await using var lockRows = new NpgsqlCommand(
                    """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await lockRows.ExecuteNonQueryAsync();
                change = host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
                first = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = host.Send(OperationsRequest(cookies, method, path, input));
                waiting = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                await using var lockDay = new NpgsqlCommand(
                    """SELECT 1 FROM public."Sys_WorkCalendar" WHERE "TenantId"=@tenant FOR UPDATE""", barrier, hold);
                lockDay.Parameters.AddWithValue("tenant", tenant);
                Assert.Equal(1, await lockDay.ExecuteScalarAsync());
                write = host.Send(OperationsRequest(cookies, method, path, input));
                first = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_WorkCalendar%", null, write);
                change = host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
                waiting = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await Effects());
            output.WriteLine($"First backend {first.Backend}; waiting backend {waiting.Backend}; wait {waiting.WaitEvent}");
            await hold.RollbackAsync();
        }
        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var response = await write;
        var expected = revokeFirst ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
        Assert.True(response.StatusCode == expected, $"{route}: {response.StatusCode}: {await OperationsFailure(response)}");
        var after = await Effects();
        if (revokeFirst)
            Assert.Equal(before, after);
        else
        {
            Assert.NotEqual(before, after);
            Assert.Equal("second", await db.Sys_WorkCalendars.AsNoTracking().Where(c => c.Date == day).Select(c => c.Note).SingleAsync());
        }
        using var stale = await host.Send(OperationsRequest(cookies, method, path, input));
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(after, await Effects());

        Task<string> Effects() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(
                (SELECT jsonb_agg(to_jsonb(d) ORDER BY d."Id") FROM public."Wf_FlowDelegate" d WHERE d."TenantId"={tenant}),
                (SELECT jsonb_agg(to_jsonb(m) ORDER BY m."Id") FROM public."Wf_ApproverMap" m WHERE m."TenantId"={tenant}),
                (SELECT jsonb_agg(to_jsonb(c) ORDER BY c."Date") FROM public."Sys_WorkCalendar" c WHERE c."TenantId"={tenant})
            )::text AS "Value"
            """).SingleAsync();
    }
}
