using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>待办中心（OA 章04 §4/§5/§6）。我的待办/我的申请/撤回。</summary>
public class TaskCenterService : ITaskCenterService
{
    private readonly CP6Context _db;
    public TaskCenterService(CP6Context db) => _db = db;

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

        _db.Wf_FlowHistories.Add(new Wf_FlowHistory
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId,
            NodeId = inst.CurrentNode,
            ActorId = userId,
            Action = "withdraw",
        });
        await _db.SaveChangesAsync();
    }
}
