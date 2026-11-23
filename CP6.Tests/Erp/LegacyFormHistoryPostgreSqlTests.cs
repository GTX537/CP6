using System.Net;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string LegacyCalendarFlow = "wp1205-calendar";
    private const string LegacyCalendarZone = "America/Los_Angeles";
    private static readonly DateTimeOffset LegacyFallStart = new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LegacyFallEnd = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static Wf_FlowInstance CalendarLegacyRow(Guid starter, DateTimeOffset created, string node = "approved",
        int status = FlowInstanceStatus.Approved) => new()
    {
        Id = Guid.NewGuid(), FlowKey = LegacyCalendarFlow, StarterId = starter, CurrentNode = node, Status = status,
        CreateDate = TimeZoneInfo.ConvertTime(created, TimeZoneInfo.Local).DateTime, BizId = "calendar-example"
    };

    [CurrentPermissionPostgresFact]
    public async Task LegacyFormHistory_HTTP_tenant_calendar_and_snapshot_keep_exact_scope_order_and_legacy_contract()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, LegacyCalendarZone));
        var unrelated = await GrantDelegate(h, "legacy-calendar-unrelated");
        var reviewer = await GrantDelegate(h, "legacy-calendar-reviewer");
        var opening = CalendarLegacyRow(h.User, LegacyFallStart, "opening");
        var lastTime = LegacyFallEnd.AddMilliseconds(-1);
        var last = CalendarLegacyRow(h.User, lastTime, "last");
        var tied = CalendarLegacyRow(h.User, lastTime, "tied");
        var wrongStatus = CalendarLegacyRow(h.User, lastTime, "different-status", FlowInstanceStatus.Rejected);
        var wrongHandler = CalendarLegacyRow(h.User, lastTime, "different-handler");
        var privateRow = CalendarLegacyRow(unrelated, lastTime, "another-starter");
        h.Db.Wf_FlowInstances.AddRange(opening, last, tied, wrongStatus, wrongHandler, privateRow,
            CalendarLegacyRow(h.User, LegacyFallStart.AddMilliseconds(-1), "before-opening"),
            CalendarLegacyRow(h.User, LegacyFallEnd, "at-exclusive-end"));
        foreach (var row in new[] { opening, last, tied, wrongStatus, privateRow })
            h.Db.Wf_FlowFormTos.Add(new() { Id = Guid.NewGuid(), InstanceId = row.Id, NodeId = "review",
                StepSeq = 1, ExpectedHandlerId = reviewer, SentAt = row.CreateDate });

        await h.Db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h);
        const string history = "/api/oa/query/history/search";
        async Task<LegacyFormHistoryPage> Search(LegacyFormHistoryFilter filter)
        {
            using var response = await host.Send(BoundRequest(cookies, history, filter));
            return await PersonalHttpResult<LegacyFormHistoryPage>(response);
        }

        using (var context = await host.Send(BoundRequest(cookies, "/api/oa/query/history/context")))
            Assert.Equal(LegacyCalendarZone, (await PersonalHttpResult<NativeQueryContextV2>(context)).TimeZoneId);

        var filter = new LegacyFormHistoryFilter(h.User, reviewer, LegacyCalendarFlow, "calendar-example", FlowInstanceStatus.Approved,
            "2026-11-01", "2026-11-01", PageSize: 2, TimeZoneId: LegacyCalendarZone);
        var visible = await Search(filter with { StarterId = null, PageSize = 20 });
        Assert.Equal(3, visible.Total);
        Assert.DoesNotContain(visible.Items, row => row.InstanceId == privateRow.Id);

        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var pending = await peer.Database.BeginTransactionAsync();
        var lateCommit = CalendarLegacyRow(h.User, lastTime, "late-commit");
        peer.Wf_FlowInstances.Add(lateCommit);
        peer.Wf_FlowFormTos.Add(new() { Id = Guid.NewGuid(), InstanceId = lateCommit.Id, NodeId = "review",
            StepSeq = 1, ExpectedHandlerId = reviewer, SentAt = lateCommit.CreateDate });
        await peer.SaveChangesAsync();

        var first = await Search(filter);
        Assert.Equal(3, first.Total);
        Assert.Equal(LegacyFallStart, first.Window.FromInclusiveUtc);
        Assert.Equal(LegacyFallEnd, first.Window.ToExclusiveUtc);
        Assert.Equal(TimeSpan.FromHours(25), first.Window.ToExclusiveUtc - first.Window.FromInclusiveUtc);
        var expectedTies = new[] { last.Id, tied.Id }.OrderByDescending(id => id).ToArray();
        Assert.Equal(expectedTies, first.Items.Select(row => row.InstanceId));
        Assert.All(first.Items, row => Assert.Equal(lastTime.UtcDateTime, row.CreateDate));
        await pending.CommitAsync();

        var inserted = CalendarLegacyRow(h.User, lastTime, "after-page-one");
        h.Db.Wf_FlowInstances.Add(inserted);
        h.Db.Wf_FlowFormTos.Add(new() { Id = Guid.NewGuid(), InstanceId = inserted.Id, NodeId = "review",
            StepSeq = 1, ExpectedHandlerId = reviewer, SentAt = inserted.CreateDate });
        await h.Db.SaveChangesAsync();
        await h.Db.Wf_FlowInstances.Where(i => i.Id == opening.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.CurrentNode, "updated-opening"));
        var second = await Search(filter with { Page = 2, SnapshotToken = first.SnapshotToken });
        Assert.Equal(first.SnapshotAsOf, second.SnapshotAsOf);
        Assert.Equal(first.SnapshotToken, second.SnapshotToken);
        Assert.Equal(first.Total, second.Total);
        var original = Assert.Single(second.Items);
        Assert.Equal(opening.Id, original.InstanceId);
        Assert.Equal("updated-opening", original.CurrentNode.Trim());
        Assert.Equal(LegacyFallStart.UtcDateTime, original.CreateDate);

        var current = await Search(filter with { PageSize = 20 });
        Assert.Equal(5, current.Total);
        Assert.Contains(current.Items, row => row.InstanceId == lateCommit.Id);
        Assert.Contains(current.Items, row => row.InstanceId == inserted.Id);
        var spring = await Search(filter with { From = "2026-03-08", To = "2026-03-08" });
        Assert.Equal(TimeSpan.FromHours(23), spring.Window.ToExclusiveUtc - spring.Window.FromInclusiveUtc);
        Assert.Equal(0, spring.Total);
        using (var noSnapshot = await host.Send(BoundRequest(cookies, history, filter with { Page = 2 })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noSnapshot.StatusCode);

        var legacyRange = new FormQueryFilter(h.User, reviewer, LegacyCalendarFlow, "calendar-example", FlowInstanceStatus.Approved,
            last.CreateDate, last.CreateDate);
        using (var legacy = await host.Send(BoundRequest(cookies, "/api/oa/query/search", legacyRange)))
        {
            var page = await PersonalHttpResult<FormQueryPage>(legacy);
            Assert.Equal(4, page.Total);
            Assert.All(page.Items, row => Assert.Equal(last.CreateDate, row.CreateDate));
        }

        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, "UTC"));
        using var changed = await host.Send(BoundRequest(cookies, history, filter with { Page = 2, SnapshotToken = first.SnapshotToken }));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyFormHistory_forward_migration_preserves_existing_flow_rows_and_records_only_new_insertion_identity()
    {
        var database = await server.CreateIsolatedDatabaseAsync("20261123083000_OaConnectorResultsV1");
        var tenant = Guid.NewGuid();
        await using var db = server.ContextForDatabase(database, new TenantContext { CurrentTenantId = tenant });
        db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "旧流程前滚合成租户", TimeZoneId = LegacyCalendarZone });
        var starter = Guid.NewGuid();
        var existing = CalendarLegacyRow(starter, LegacyFallStart, "资料原件");
        db.Wf_FlowInstances.Add(existing);
        await db.SaveChangesAsync();

        var migrations = db.GetService<IMigrator>();
        await migrations.MigrateAsync("20261123124500_LegacyFormHistorySnapshotV1");
        var fromPreviousCandidate = CalendarLegacyRow(starter, LegacyFallStart, "已有事务编号");
        db.Wf_FlowInstances.Add(fromPreviousCandidate);
        await db.SaveChangesAsync();
        async Task<string> InsertionIdentity(Guid id) => await db.Database.SqlQuery<string>($"""
            SELECT legacy_query_insert_xid::text AS "Value" FROM public."Wf_FlowInstance"
            WHERE "TenantId"={tenant} AND "Id"={id}
            """).SingleAsync();
        var previousBirth = await InsertionIdentity(fromPreviousCandidate.Id);
        Assert.NotEqual("0", previousBirth);

        await migrations.MigrateAsync();
        db.ChangeTracker.Clear();
        var after = await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == existing.Id);
        Assert.Equal(existing.CreateDate, after.CreateDate);
        Assert.Equal(existing.StarterId, after.StarterId);
        Assert.Equal(existing.Status, after.Status);
        Assert.Equal(existing.FlowKey, after.FlowKey.Trim());
        Assert.Equal(existing.CurrentNode, after.CurrentNode.Trim());
        Assert.Equal(existing.BizId, after.BizId?.Trim());

        var inserted = CalendarLegacyRow(starter, LegacyFallStart, "前滚后新记录");
        db.Wf_FlowInstances.Add(inserted);
        await db.SaveChangesAsync();
        async Task<string?> InsertionOrigin(Guid id) => await db.Database.SqlQuery<string?>($"""
            SELECT legacy_query_insert_origin AS "Value" FROM public."Wf_FlowInstance"
            WHERE "TenantId"={tenant} AND "Id"={id}
            """).SingleAsync();
        Assert.Equal("0", await InsertionIdentity(existing.Id));
        Assert.Equal(previousBirth, await InsertionIdentity(fromPreviousCandidate.Id));
        Assert.Null(await InsertionOrigin(existing.Id));
        Assert.Null(await InsertionOrigin(fromPreviousCandidate.Id));
        var origin = await ReadLegacyOrigin(db);
        Assert.Equal(origin, await InsertionOrigin(inserted.Id));
        var birth = await InsertionIdentity(inserted.Id);
        Assert.NotEqual("0", birth);
        await db.Wf_FlowInstances.Where(i => i.Id == inserted.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, FlowInstanceStatus.Rejected));
        Assert.Equal(birth, await InsertionIdentity(inserted.Id));
        Assert.Equal(origin, await InsertionOrigin(inserted.Id));
    }
}
