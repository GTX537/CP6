using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>待办中心（OA 章04 §4/§5/§6）。我的待办/我的申请/撤回。</summary>
public class TaskCenterService : ITaskCenterService
{
    private readonly CP6Context _db;
    private readonly FlowEngine? _engine;   // 子流程 fast path 用；null（既有测试构造）= 交 worker 20s 兜底
    public TaskCenterService(CP6Context db, FlowEngine? engine = null) { _db = db; _engine = engine; }

    public async Task<List<TodoItem>> MyTodosAsync(Guid userId)
    {
        var q = from t in _db.Wf_FlowTasks
                where t.AssigneeId == userId && t.Status == FlowTaskStatus.Pending
                join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                where i.Status == FlowInstanceStatus.Running
                orderby t.CreateDate descending
                select new TodoItem(t.Id, i.Id, i.FlowKey, t.NodeId, i.StarterId, t.CreateDate);
        return await q.ToListAsync();
    }

    public Task<List<MyApplicationItem>> MyApplicationsAsync(Guid userId) =>
        _db.Wf_FlowInstances
            .Where(i => i.StarterId == userId)
            .OrderByDescending(i => i.CreateDate)
            .Select(i => new MyApplicationItem(i.Id, i.FlowKey, i.CurrentNode, i.Status, i.CreateDate))
            .ToListAsync();

    public async Task WithdrawAsync(Guid instanceId, Guid userId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
                   ?? throw new InvalidOperationException("流程实例不存在");
        if (inst.StarterId != userId) throw new InvalidOperationException("仅发起人可撤回");
        if (inst.Status != FlowInstanceStatus.Running) throw new InvalidOperationException("仅进行中的流程可撤回");

        inst.Status = FlowInstanceStatus.Withdrawn;
        inst.Modifier = userId.ToString();
        inst.ModifyDate = DateTime.Now;

        var pending = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == instanceId && t.Status == FlowTaskStatus.Pending)
            .ToListAsync();
        foreach (var t in pending) t.Status = FlowTaskStatus.Cancelled;   // 清在途待办

        // ★ Fix3：撤回 = terminate，级联清理读模型，否则未来收件箱残留幻影 token / 待签
        var activeTokens = await _db.Wf_FlowTokens
            .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active)
            .ToListAsync();
        foreach (var t in activeTokens) t.Status = FlowTokenStatus.Cancelled;   // 在途 token → Cancelled

        // ── 子流程 C-T1（spec §3.3 路径①）：撤回 = terminate,就地循环不经 CancelAllActiveTokens → 此处补级联 ──
        foreach (var t in activeTokens) SubFlowCascade.CancelChildrenOfToken(_db, t.Id);

        var pendingFormTos = await _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == instanceId && f.Status == FlowFormToStatus.Pending)
            .ToListAsync();
        foreach (var f in pendingFormTos) f.Status = FlowFormToStatus.Voided;   // 在途传签履历 → Voided

        // ★ B-T3（P0-5 入队侧）：撤回 = terminate，同步清理 Pending 服务任务 job。
        // Running job 不强杀——由扫描 worker 执行前状态闸（§4.2 P0-5）在 worker 侧处理。
        // 注：TaskCenterService 无 FlowEngine 依赖，直接查 DB（WithdrawAsync 已把全部 token 加载进追踪器，
        //     job 若也在追踪器中用 Local 是权威态；首批撤回 job 走 SaveChangesAsync 前，DB 仍是 Pending，
        //     此处直接 DB 查即可——WithdrawAsync 是事务起点无前置未落盘 job 变动）。
        var pendingJobs = await _db.Wf_ServiceJobs
            .Where(j => j.InstanceId == instanceId && j.Status == ServiceJobStatus.Pending)
            .ToListAsync();
        var withdrawNow = DateTime.UtcNow;
        foreach (var j in pendingJobs) { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = withdrawNow; }

        // ★ 子流程第一段（Withdrawn 与 Approved/Rejected 对称,spec §3.3 末条「手工撤回入计票」）。
        //    置于本方法 pendingJobs 清理之后：本凭据 InstanceId=父实例,不会被上面按本实例（子）的清理误杀。
        SubFlowResume.EnqueueIfChild(_db, inst);

        _db.Wf_FlowHistories.Add(new Wf_FlowHistory
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId,
            NodeId = inst.CurrentNode,
            ActorId = userId,
            Action = "withdraw",
        });
        await _db.SaveChangesAsync();
        if (_engine is not null) await _engine.FastPathSubFlowResumeAsync();   // ★ 子流程 fast path（null=worker 兜底）
    }
}
