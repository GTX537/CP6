using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Xunit;

namespace CP6.Tests.Wf;

/// <summary>
/// OA/WF 治理配置 / 权限授予面 关键实体字段级审计接线冒烟（M-OA/WF 横切 T5）。
/// 照 <see cref="CP6.Tests.Mes.MesAuditTests"/> 范式：InMemory + 假当前用户。
/// 验证贴 IAuditable 的 5 个设计期治理/授权实体（流程定义 Wf_FlowDef·审批绑定单源 Wf_ApprovalBinding·
/// 表单定义 Wf_FormDef·审批委派 Wf_FlowDelegate·审批人映射 Wf_ApproverMap）在 create/update
/// 随业务行同周期写入 Sys_FieldAuditLogs（真实断言实体名/字段/新旧值）；
/// 并回归确认 12 个高频运行时流转 / 追加型 / 用户偏好 实体（实例·任务·令牌·痕迹·关卡快照·传签履历·
/// 抄送·表单数据·通知·收藏·信箱偏好·服务作业）均未贴 → 零审计行（负测试，全量豁免对账）。
/// </summary>
public class OawfAuditTests
{
    private sealed class FakeUser : ICurrentUserAccessor
    {
        public FakeUser(Guid? id, string? name) { UserId = id; UserName = name; }
        public Guid? UserId { get; }
        public string? UserName { get; }
    }

    private sealed record DiffRow(string Field, string? Old, string? New);

    private static List<DiffRow> ParseChanges(string json)
        => JsonSerializer.Deserialize<List<DiffRow>>(json) ?? new();

    private static readonly Guid _userId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CP6Context Ctx()
        => TestHelper.CreateInMemoryContext(new FakeUser(_userId, "alice"));

    // ════════════════════════════════════════════════════════════════════════
    //  纳入：治理配置 / 权限授予面（5 实体）
    // ════════════════════════════════════════════════════════════════════════

    // ── 流程定义（Wf_FlowDef）— SchemaJson/FlowKey 设计期治理配置，改版影响所有在途流程 ──
    [Fact]
    public void Create_FlowDef_writes_op1_audit_row()
    {
        using var db = Ctx();
        var fd = new Wf_FlowDef { FlowKey = "oa-leave", FlowName = "请假流程", FormKey = "fm-leave" };
        db.Set<Wf_FlowDef>().Add(fd);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_FlowDef), rows[0].EntityName);
        Assert.Equal(fd.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_FlowDef_enable_writes_op2_diff()
    {
        using var db = Ctx();
        var fd = new Wf_FlowDef { FlowKey = "oa-leave", FlowName = "请假流程", FormKey = "fm-leave", Enable = true };
        db.Set<Wf_FlowDef>().Add(fd);
        db.SaveChanges();

        fd.Enable = false;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_FlowDef), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "Enable" && d.Old == "True" && d.New == "False");
    }

    // ── 审批绑定单源（Wf_ApprovalBinding）— BizType→FlowKey 映射，改绑改变谁的审批走哪条流程 ──
    [Fact]
    public void Create_ApprovalBinding_writes_op1_audit_row()
    {
        using var db = Ctx();
        var b = new Wf_ApprovalBinding { BizType = "FinJournalPost", FlowKey = "oa-journal", Enable = true };
        db.Set<Wf_ApprovalBinding>().Add(b);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_ApprovalBinding), rows[0].EntityName);
        Assert.Equal(b.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_ApprovalBinding_flowKey_writes_op2_diff()
    {
        using var db = Ctx();
        var b = new Wf_ApprovalBinding { BizType = "PO", FlowKey = "oa-po", Enable = true };
        db.Set<Wf_ApprovalBinding>().Add(b);
        db.SaveChanges();

        b.FlowKey = "oa-po-v2";
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_ApprovalBinding), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "FlowKey" && d.Old == "oa-po" && d.New == "oa-po-v2");
    }

    // ── 表单定义（Wf_FormDef）— SchemaJson/Version 设计期治理配置（oa-designer 落库对象）──
    [Fact]
    public void Update_FormDef_version_writes_op2_diff()
    {
        using var db = Ctx();
        var f = new Wf_FormDef { FormKey = "fm-leave", FormName = "请假单", Version = 1 };
        db.Set<Wf_FormDef>().Add(f);
        db.SaveChanges();

        f.Version = 2;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_FormDef), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "Version" && d.Old == "1" && d.New == "2");
    }

    // ── 审批委派（Wf_FlowDelegate）— 委托人把审批权授予代理人：权限授予面 ────────────────
    [Fact]
    public void Create_FlowDelegate_writes_op1_audit_row()
    {
        using var db = Ctx();
        var d = new Wf_FlowDelegate
        {
            GrantorId = Guid.NewGuid(),
            DelegateId = Guid.NewGuid(),
            ValidFrom = new DateTime(2026, 7, 12),
            ValidTo = new DateTime(2026, 7, 20),
            Enable = true,
        };
        db.Set<Wf_FlowDelegate>().Add(d);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_FlowDelegate), rows[0].EntityName);
        Assert.Equal(d.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_FlowDelegate_enable_revoke_writes_op2_diff()
    {
        using var db = Ctx();
        var d = new Wf_FlowDelegate
        {
            GrantorId = Guid.NewGuid(),
            DelegateId = Guid.NewGuid(),
            ValidFrom = new DateTime(2026, 7, 12),
            ValidTo = new DateTime(2026, 7, 20),
            Enable = true,
        };
        db.Set<Wf_FlowDelegate>().Add(d);
        db.SaveChanges();

        d.Enable = false;   // 即时收回委派
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_FlowDelegate), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "Enable" && d.Old == "True" && d.New == "False");
    }

    // ── 审批人映射（Wf_ApproverMap）— MapKey→审批目标(用户/角色)：数据驱动的权限授予面 ────
    [Fact]
    public void Create_ApproverMap_writes_op1_audit_row()
    {
        using var db = Ctx();
        var m = new Wf_ApproverMap { MapKey = "dept-manager", MatchValue = "D01", ApproverRoleId = 5, Enable = true };
        db.Set<Wf_ApproverMap>().Add(m);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_ApproverMap), rows[0].EntityName);
        Assert.Equal(m.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_ApproverMap_approverRoleId_writes_op2_diff()
    {
        using var db = Ctx();
        var m = new Wf_ApproverMap { MapKey = "dept-manager", MatchValue = "D01", ApproverRoleId = 5, Enable = true };
        db.Set<Wf_ApproverMap>().Add(m);
        db.SaveChanges();

        m.ApproverRoleId = 9;   // 改派审批目标角色
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Wf_ApproverMap), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "ApproverRoleId" && d.Old == "5" && d.New == "9");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  豁免负测试：高频运行时流转 / 追加型 / 用户偏好（12 实体，全量对账）
    // ════════════════════════════════════════════════════════════════════════

    // 运行时状态载体：CurrentNode/Status 高频状态机流转，正确性由 FlowEngine 引擎测试锁定，豁免。
    [Fact]
    public void Create_FlowInstance_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FlowInstance>().Add(new Wf_FlowInstance { FlowKey = "oa-leave", CurrentNode = "start", StarterId = Guid.NewGuid() });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 高频待办任务：一节点多条、Status 幂等流转，引擎测试锁定，豁免。
    [Fact]
    public void Create_FlowTask_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FlowTask>().Add(new Wf_FlowTask { InstanceId = Guid.NewGuid(), NodeId = "n1", AssigneeId = Guid.NewGuid() });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 运行时执行点令牌：分叉/合流内核态，引擎测试锁定，豁免。
    [Fact]
    public void Create_FlowToken_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FlowToken>().Add(new Wf_FlowToken { InstanceId = Guid.NewGuid(), NodeId = "n1", Status = 0 });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 审批痕迹：仅追加事件日志（本身即审计时间线），豁免。
    [Fact]
    public void Create_FlowHistory_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FlowHistory>().Add(new Wf_FlowHistory { InstanceId = Guid.NewGuid(), NodeId = "n1", ActorId = Guid.NewGuid(), Action = "approve" });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 每关卡表单快照：不可变追加留痕，豁免。
    [Fact]
    public void Create_FlowData_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FlowData>().Add(new Wf_FlowData { InstanceId = Guid.NewGuid(), StepSeq = 1, NodeId = "n1" });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 传签履历台账：运行时读模型，豁免。
    [Fact]
    public void Create_FlowFormTo_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FlowFormTo>().Add(new Wf_FlowFormTo { InstanceId = Guid.NewGuid(), StepSeq = 1, NodeId = "n1", ExpectedHandlerId = Guid.NewGuid(), SentAt = DateTime.Now });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 抄送读模型：IsRead 高频翻转，豁免。
    [Fact]
    public void Create_FlowCc_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FlowCc>().Add(new Wf_FlowCc { InstanceId = Guid.NewGuid(), RecipientId = Guid.NewGuid() });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 表单提交数据：运行时一次提交一行的字段值快照，豁免。
    [Fact]
    public void Create_FormData_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FormData>().Add(new Wf_FormData { FormKey = "fm-leave", FormVersion = 1, DataJson = "{}" });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 站内通知：运行时追加 + IsRead 高频翻转，豁免（create 与 update 皆零审计行）。
    [Fact]
    public void CreateUpdate_Notification_writes_no_audit_row()
    {
        using var db = Ctx();
        var n = new Wf_Notification { UserId = Guid.NewGuid(), Type = WfNotificationType.TodoCreated, Title = "新待办", IsRead = false };
        db.Set<Wf_Notification>().Add(n);
        db.SaveChanges();

        n.IsRead = true;
        db.SaveChanges();

        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 填單收藏：用户个人偏好，豁免。
    [Fact]
    public void Create_FormFavorite_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_FormFavorite>().Add(new Wf_FormFavorite { UserId = Guid.NewGuid(), FormKey = "fm-leave" });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 信箱显示偏好：用户个人偏好，豁免。
    [Fact]
    public void Create_InboxPref_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_InboxPref>().Add(new Wf_InboxPref { UserId = Guid.NewGuid(), PrefsJson = "{}" });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // 服务任务异步作业台账：运行时队列（AttemptCount/Lock/Status 高频翻转），引擎测试锁定，豁免。
    [Fact]
    public void Create_ServiceJob_writes_no_audit_row()
    {
        using var db = Ctx();
        db.Set<Wf_ServiceJob>().Add(new Wf_ServiceJob { InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(), NodeId = "n1", Kind = "timer" });
        db.SaveChanges();
        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }
}
