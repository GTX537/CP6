using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 流程引擎高级动作（OA 章07：退回 / 加签 / 委派）。作为 <see cref="FlowEngine"/> 的 partial，
/// 直接复用其 EnterNodeAsync/LoadSchemaAsync/AddHistory 等私有成员与同一 scoped DbContext。
/// 难点全在<b>动作后的状态清理</b>：退回作废在途待办、前加签挂起原任务，FlowHistory 永远只追加。
/// </summary>
public partial class FlowEngine
{
    public async Task SendBackAsync(Guid taskId, Guid actorId, string targetNodeId, string? comment = null)
    {
        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("任务不存在");
        if (task.Status != FlowTaskStatus.Pending) return;   // 幂等闸门：已办无效

        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) return;

        var schema = await LoadSchemaAsync(inst.FlowKey);
        var target = FindNode(schema, targetNodeId)
                     ?? throw new InvalidOperationException($"退回目标节点不存在：{targetNodeId}");
        if (IsType(target, "end")) throw new InvalidOperationException("不能退回到结束节点");

        // 作废本实例所有在途待办（含挂起的前加签原任务）——线性引擎下在途任务即当前节点 + 加签。
        // 一并清掉本节点会签计票（被作废的任务不再计入 EvaluateNodeCounts）。
        var live = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id
                        && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended))
            .ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;

        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {targetNodeId}");

        await EnterNodeAsync(inst, schema, target);   // CurrentNode=target + 重建待办（缺审批人→挂起）
        await _db.SaveChangesAsync();
    }
}
