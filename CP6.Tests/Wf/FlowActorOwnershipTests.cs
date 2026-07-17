using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>
/// P0 越权代批封堵（M-OA/WF 票#1）：引擎四变更方法归属闸。
/// 放行三路径=本人 / act-as 有效委派（引擎复验） / SystemActor(Guid.Empty)；
/// 违规=E-WF-029；act-as 无效委派=E-WF-001；批量转单唯一可信旁路 bypassOwnership。
/// </summary>
public class FlowActorOwnershipTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private const string FlowKey = "own-two-step";

    private static async Task SeedFlowAsync(CP6Context db, Guid a, Guid b)
    {
        var schema = new FlowSchema
        {
            Start = "n1",
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = a },
                new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = b },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "n2" }, new FlowEdge { From = "n2", To = "end" } },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = FlowKey, FlowName = "归属闸两段审批", FormKey = "test",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>起流程并取 n1 待办（assignee=a）。</summary>
    private static async Task<Wf_FlowTask> SubmitAndGetTaskAsync(CP6Context db, Guid a, Guid b)
    {
        await SeedFlowAsync(db, a, b);
        await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        return await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1" && t.Status == FlowTaskStatus.Pending);
    }

    private static void SeedGrant(CP6Context db, Guid grantor, Guid delegateId, bool enable = true,
        DateTime? from = null, DateTime? to = null)
    {
        db.Wf_FlowDelegates.Add(new Wf_FlowDelegate
        {
            Id = Guid.NewGuid(), GrantorId = grantor, DelegateId = delegateId, Enable = enable,
            ValidFrom = from ?? DateTime.Now.AddDays(-1), ValidTo = to ?? DateTime.Now.AddDays(1),
        });
        db.SaveChanges();
    }

    private static void SeedUser(CP6Context db, Guid id)
    {
        db.Sys_Users.Add(new Sys_User { Id = id, UserName = "to", NickName = "to", Enable = true });
        db.SaveChanges();
    }

    // ── ActAsync/ActOnceAsync ──

    [Fact]
    public async Task Act_ByNonAssignee_ThrowsE029_AndTaskStaysPending()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsync(t.Id, intruder, approve: true));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
        Assert.False(await db.Wf_FlowHistories.AnyAsync(h => h.Action == "approve"));   // 零履历污染
    }

    [Fact]
    public async Task Act_ByAssignee_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        await Engine(db).ActAsync(t.Id, a, approve: true);
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
    }

    [Fact]
    public async Task Act_BySystemActor_Bypasses()   // 超时 worker 硬动作路径回归
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        await Engine(db).ActAsync(t.Id, Guid.Empty, approve: true, "超时自动同意");
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
    }

    [Fact]
    public async Task ActAs_DelegateWithActiveGrant_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, grantor: a, delegateId: me);
        await Engine(db).ActAsAsync(t.Id, me, onBehalfOf: a, approve: true);
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
    }

    [Fact]
    public async Task ActAs_WithoutGrant_ThrowsE001()   // 防御纵深：引擎不再仅信控制器闸
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsAsync(t.Id, me, onBehalfOf: a, approve: true));
        Assert.Equal("E-WF-001", ex.Message);
    }

    [Fact]
    public async Task ActAs_ExpiredOrDisabledGrant_ThrowsE001()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, a, me, to: DateTime.Now.AddDays(-1));            // 已过期
        SeedGrant(db, a, me, enable: false);                            // 已停用
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsAsync(t.Id, me, onBehalfOf: a, approve: true));
        Assert.Equal("E-WF-001", ex.Message);
    }

    [Fact]
    public async Task ActAs_OnBehalfOfNotAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid(); var other = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, other, me);   // me 是 other 的有效代理，但任务属 a
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsAsync(t.Id, me, onBehalfOf: other, approve: true));
        Assert.Equal("E-WF-029", ex.Message);
    }

    [Fact]
    public async Task Act_DelegateDirect_WithoutActAs_ThrowsE029()   // 设计决策：代理人必须走 act-as，旧栈直办不放行
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, a, me);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsync(t.Id, me, approve: true));
        Assert.Equal("E-WF-029", ex.Message);
    }

    // ── TransferAsync ──

    [Fact]
    public async Task Transfer_ByNonAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid(); var to = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedUser(db, to);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).TransferAsync(t.Id, intruder, to));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(a, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).AssigneeId);   // 未被抢走
    }

    [Fact]
    public async Task Transfer_ByAssignee_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var to = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedUser(db, to);
        await Engine(db).TransferAsync(t.Id, a, to);
        Assert.Equal(to, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).AssigneeId);
    }

    [Fact]
    public async Task Transfer_BypassOwnership_AllowsForeignActor()   // admin 批量转单可信旁路回归
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var admin = Guid.NewGuid(); var to = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedUser(db, to);
        await Engine(db).TransferAsync(t.Id, admin, to, comment: null, bypassOwnership: true);
        Assert.Equal(to, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).AssigneeId);
    }

    // ── SendBackAsync / AddSignAsync ──

    [Fact]
    public async Task SendBack_ByNonAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid();
        await SeedFlowAsync(db, a, b);
        await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).ActAsync(t1.Id, a, approve: true);   // 流转到 n2（assignee=b）
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SendBackAsync(t2.Id, intruder, "n1"));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t2.Id)).Status);
    }

    [Fact]
    public async Task AddSign_ByNonAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid(); var signee = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).AddSignAsync(t.Id, intruder, signee, "after"));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);   // before 挂起未发生
    }

    [Fact]
    public async Task AddSign_ByAssignee_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var signee = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        var addId = await Engine(db).AddSignAsync(t.Id, a, signee, "after");
        Assert.Equal(signee, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == addId)).AssigneeId);
    }
}
