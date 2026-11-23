using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Task<string> ReadLegacyOrigin(CP6Context db) => db.Database.SqlQueryRaw<string>(
        """SELECT public.legacy_form_history_origin_v2() AS "Value" """).SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task LegacyFormHistory_logical_restore_preserves_history_and_restarts_paging_by_database_origin()
    {
        const string history = "/api/oa/query/history/search";
        var sourceDatabase = await server.CreateIsolatedDatabaseAsync();
        await using var h = await SeedForDatabase(sourceDatabase);
        await RegisterScope(h);

        // 让新集群恢复后真实出现事务编号差距，证明上一候选的判定确实会漏行。
        var cluster = server.LocalCluster;
        await using (var owner = await cluster.OpenOwnerAsync(sourceDatabase))
            for (var i = 0; i < 64; i++)
                await CurrentPermissionPostgresFixture.Scalar(owner, "SELECT pg_current_xact_id()::text");

        var tiedAt = LegacyFallEnd.AddMilliseconds(-1);
        var restored = new[] { CalendarLegacyRow(h.User, tiedAt), CalendarLegacyRow(h.User, tiedAt),
            CalendarLegacyRow(h.User, LegacyFallStart) };
        var ordered = restored.Take(2).Select(row => row.Id).OrderByDescending(id => id).Append(restored[2].Id).ToArray();
        h.Db.Wf_FlowInstances.AddRange(restored);
        await h.Db.SaveChangesAsync();
        var fromOrigin = await ReadLegacyOrigin(h.Db);
        var filter = new LegacyFormHistoryFilter(StarterId: h.User, FlowKey: LegacyCalendarFlow, PageSize: 2);
        var environment = new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") };

        async Task<LegacyFormHistoryPage> Page(AuthenticationApiProcess host, Dictionary<string, string> cookies, LegacyFormHistoryFilter query)
        {
            using var response = await host.Send(BoundRequest(cookies, history, query));
            return await PersonalHttpResult<LegacyFormHistoryPage>(response);
        }

        LegacyFormHistoryPage fromPage;
        await using (var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment))
        {
            var cookies = await Login(host, h);
            fromPage = await Page(host, cookies, filter);
            Assert.Equal(3, fromPage.Total);
        }
        // 一并归档实际 Data Protection 密钥环，证明令牌可解密后仍会因来源变化被拒绝。
        var archive = await cluster.DumpForRestoreAsync(sourceDatabase);

        async Task AssertRestoredAsync(CurrentPermissionPostgresFixture target, string database, bool differentCluster)
        {
            await target.RestoreIntoNewDatabaseAsync(database, archive);
            await using var db = target.Context(h.Tenant, database);
            var origin = await ReadLegacyOrigin(db);
            Assert.NotEqual(fromOrigin, origin);
            if (differentCluster)
            {
                Assert.NotEqual(fromOrigin.Split('/')[0], origin.Split('/')[0]);
                Assert.Equal(sourceDatabase, db.Database.GetDbConnection().Database);
                var missedByPreviousCandidate = await db.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM public."Wf_FlowInstance"
                    WHERE "TenantId" = {h.Tenant} AND legacy_query_insert_origin = {fromOrigin}
                      AND NOT pg_visible_in_snapshot(legacy_query_insert_xid, pg_current_snapshot())
                    """).SingleAsync();
                Assert.Equal(restored.Length, missedByPreviousCandidate);
                output.WriteLine($"跨集群同名数据库：旧事务截点会漏查 {missedByPreviousCandidate} 条恢复记录。");
            }
            else
            {
                Assert.Equal(fromOrigin.Split('/')[0], origin.Split('/')[0]);
                Assert.NotEqual(fromOrigin.Split('/')[1], origin.Split('/')[1]);
            }

            await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!, environment);
            var cookies = await Login(host, h);
            using (var stale = await host.Send(BoundRequest(cookies, history, filter with { Page = 2, SnapshotToken = fromPage.SnapshotToken })))
            {
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                using var json = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
                Assert.Equal("QUERY_SNAPSHOT_STALE", json.RootElement.GetProperty("code").GetString());
            }

            await using var writer = target.Context(h.Tenant, database);
            await using (var pending = await writer.Database.BeginTransactionAsync())
            {
                writer.Wf_FlowInstances.Add(CalendarLegacyRow(h.User, tiedAt, "恢复后晚提交"));
                await writer.SaveChangesAsync();
                var first = await Page(host, cookies, filter);
                Assert.Equal(3, first.Total);
                Assert.Equal(ordered.Take(2), first.Items.Select(row => row.InstanceId));
                await pending.CommitAsync();

                db.Wf_FlowInstances.Add(CalendarLegacyRow(h.User, tiedAt, "恢复后翻页期间新增"));
                await db.SaveChangesAsync();
                var second = await Page(host, cookies, filter with { Page = 2, SnapshotToken = first.SnapshotToken });
                Assert.Equal(first.SnapshotAsOf, second.SnapshotAsOf);
                Assert.Equal(first.Total, second.Total);
                Assert.Equal(ordered.Skip(2), second.Items.Select(row => row.InstanceId));
            }
            var refreshed = await Page(host, cookies, filter with { PageSize = 20 });
            Assert.Equal(5, refreshed.Total);
            Assert.All(restored, row => Assert.Contains(refreshed.Items, item => item.InstanceId == row.Id));

            var oldFilter = new FormQueryFilter(h.User, null, LegacyCalendarFlow, null, null, restored[0].CreateDate, restored[0].CreateDate);
            using var legacy = await host.Send(BoundRequest(cookies, "/api/oa/query/search", oldFilter));
            var oldPage = await PersonalHttpResult<FormQueryPage>(legacy);
            Assert.Equal(4, oldPage.Total);
            Assert.All(oldPage.Items, row => Assert.Equal(restored[0].CreateDate, row.CreateDate));
        }

        var peer = await cluster.StartRestorePeerAsync();
        try { await AssertRestoredAsync(peer, sourceDatabase, differentCluster: true); }
        finally { await peer.DisposeAsync(); }
        await AssertRestoredAsync(cluster, "wp1205_same_cluster_" + Guid.NewGuid().ToString("N"), differentCluster: false);
    }
}
