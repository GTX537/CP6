// CP6.Tests/Wf/SubFlowConcurrencyTests.cs
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>两段式回注竞态矩阵（spec §3.2/§7）：SQLite 共享连接 + rowversion 触发器 + 双 context 模拟两事务。
/// all 不丢唤醒（陈旧身份映射被 Reload 击穿）/ any 不双恢复（RowVersion 撞 → 状态闸零动作）/
/// fast path 崩溃 worker 兜底 / 停泊重入唯一槽。</summary>
public class SubFlowConcurrencyTests
{
    private static async Task<(Guid pid, Guid parkedTokenId, Guid pa, Guid ca, List<Guid> childIds)> SeedAndSubmitAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn, string? policy, string parentVars,
        string? varsOut = null, string? varsIn = null)
    {
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        using (var db = Ctx(conn))
        {
            SeedDef(db, "child", ChildSchema(ca));
            SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items", policy: policy,
                varsIn: varsIn, varsOut: varsOut));
            await db.SaveChangesAsync();
        }
        using (var db = Ctx(conn))
        {
            var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), parentVars);
            var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub");
            var kids = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid)
                .OrderBy(i => i.SubIndex).Select(i => i.Id).ToListAsync();
            return (pid, parked.Id, pa, ca, kids);
        }
    }

    [Fact]
    public async Task All_TwoRequests_StaleIdentityMap_ReloadDefeatsIt_NoLostWakeup()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, pa, ca, kids) = await SeedAndSubmitAsync(conn, null, "{\"items\":[1,2]}");

        // 请求2 的 context 先把 child0 拉进身份映射（陈旧态 Running）——复核若不 Reload 会误判「未齐」丢唤醒
        using var db2 = Ctx(conn);
        _ = await db2.Wf_FlowInstances.SingleAsync(i => i.Id == kids[0]);

        // 请求1：审结 child0（独立事务提交）
        using (var db1 = Ctx(conn))
        {
            var t0 = await db1.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[0] && t.Status == FlowTaskStatus.Pending);
            await Engine(db1).ActAsync(t0.Id, ca, approve: true);
        }

        // 请求2：审结 child1 → fast path 复核（其身份映射中 child0 是陈旧 Running）→ Reload 击穿 → 恢复父
        var t1 = await db2.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[1] && t.Status == FlowTaskStatus.Pending);
        await Engine(db2).ActAsync(t1.Id, ca, approve: true);

        using var check = Ctx(conn);
        Assert.True(await check.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await check.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.False(await check.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
    }

    [Fact]
    public async Task Any_LateStaleChecker_RowVersionClash_StateGate_NoDoubleResume()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, pa, ca, kids) = await SeedAndSubmitAsync(conn, "any", "{\"items\":[\"x\",\"y\"]}",
            varsOut: "{\"winner\":\"$.v\"}", varsIn: "{\"v\":\"$.item\"}");

        // 迟到复核方：先把父 token/实例/子组拉进身份映射（陈旧态：token 仍停泊）
        using var db2 = Ctx(conn);
        _ = await db2.Wf_FlowTokens.SingleAsync(t => t.Id == tok);
        _ = await db2.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        _ = await db2.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).ToListAsync();

        // 胜方：child0 审过 → any 立即恢复父 + 级联撤回 child1（独立事务已提交）
        using (var db1 = Ctx(conn))
        {
            var t0 = await db1.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[0] && t.Status == FlowTaskStatus.Pending);
            await Engine(db1).ActAsync(t0.Id, ca, approve: true);
        }

        // 败方：拿陈旧停泊 token 直闯复核 → 双恢复动作在 SaveChanges 撞父行 RowVersion → 重读 → 状态闸零动作
        await Engine(db2).CheckSubFlowGroupAsync(tok);

        using var check = Ctx(conn);
        Assert.Equal(1, await check.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.Equal(1, await check.Wf_FlowTasks.CountAsync(t => t.InstanceId == pid && t.NodeId == "pa"));   // 不双推进
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await check.Wf_FlowInstances.SingleAsync(i => i.Id == kids[1])).Status);
    }

    [Fact]
    public async Task FastPathCrash_WorkerScan_RescuesWakeup()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, pa, ca, kids) = await SeedAndSubmitAsync(conn, "any", "{\"items\":[1,2]}");

        // 「崩溃窗口」等价面：engine=null 撤回 child0 → 凭据落库但第二段没跑
        using (var db1 = Ctx(conn))
        {
            var child = await db1.Wf_FlowInstances.SingleAsync(i => i.Id == kids[0]);
            await new TaskCenterService(db1).WithdrawAsync(child.Id, child.StarterId);
            Assert.True(await db1.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
        }

        // worker 兜底（新 scope=新 context）：any 策略一死一活 → 未决,凭据消费但父照常停泊
        using (var db2 = Ctx(conn))
        {
            var svc = new WfServiceJobService(db2, Engine(db2), Array.Empty<IServiceTaskExecutor>());
            Assert.Equal(1, await svc.ScanOnceAsync(DateTime.UtcNow, "w1"));
        }
        using (var check1 = Ctx(conn))
            Assert.Equal(FlowInstanceStatus.Running, (await check1.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);

        // child1 审过 → fast path 恢复（证明前一凭据消费不吞后续唤醒）
        using (var db3 = Ctx(conn))
        {
            var t1 = await db3.Wf_FlowTasks.SingleAsync(t => t.InstanceId == kids[1] && t.Status == FlowTaskStatus.Pending);
            await Engine(db3).ActAsync(t1.Id, ca, approve: true);
        }
        using var check2 = Ctx(conn);
        Assert.True(await check2.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task ParkedReentry_UniqueSlot_NoDuplicateChildren()
    {
        using var conn = NewSqliteWithSchema();
        var (pid, tok, _, _, kids) = await SeedAndSubmitAsync(conn, null, "{\"items\":[1,2]}");

        using var db = Ctx(conn);
        var eng = Engine(db);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        var token = await db.Wf_FlowTokens.SingleAsync(t => t.Id == tok);
        var def = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "parent");
        var schema = System.Text.Json.JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var node = schema.Nodes.Single(n => n.Id == "sub");

        await eng.EnterNodeAsync(inst, schema, node, token);   // 停泊重入（InternalsVisibleTo 直调）
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync(i => i.ParentTokenId == tok));   // 槽幂等,不重复起子
    }
}
