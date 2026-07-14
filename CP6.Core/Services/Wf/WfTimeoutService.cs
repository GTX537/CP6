using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>OA 章07 §4 超时扫描服务。</summary>
public interface IWfTimeoutService
{
    /// <summary>扫一遍到期未处理待办，按节点 TimeoutAction 处理。<paramref name="now"/> 注入便于单测。返回处理条数。</summary>
    Task<int> ScanOnceAsync(DateTime now, CancellationToken ct = default);
}

/// <summary>
/// 超时扫描（OA 章07 §4）。周期扫 <c>DueAt &lt;= now ∧ !TimeoutHandled</c> 的待办，按节点 TimeoutAction：
/// <list type="bullet">
/// <item>remind — 软动作：重发催办 + 把 DueAt 顺延（可重复，不置 TimeoutHandled）</item>
/// <item>approve / reject — 硬动作：以系统身份调 <see cref="FlowEngine.ActAsync"/>（本身幂等）推进/否决</item>
/// <item>escalate — 硬动作：把 assignee 升级给 EscalateTo + 双痕，原人不再持有</item>
/// <item>errorEdge — 硬动作：作废节点在途待办 + 注入 timeoutError 变量，沿 IsError 失败边路由（不硬批/硬驳）</item>
/// </list>
/// <b>双幂等</b>：TimeoutHandled 标记（硬动作处理后置位，扫描不再碰）+ ActAsync 自身幂等闸门。
/// </summary>
public class WfTimeoutService : IWfTimeoutService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>超时动作的发起者身份（系统）。</summary>
    public static readonly Guid SystemActor = Guid.Empty;

    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;
    private readonly IWfNotifier _notifier;

    public WfTimeoutService(CP6Context db, IFlowEngine engine, IWfNotifier? notifier = null)
    {
        _db = db;
        _engine = engine;
        _notifier = notifier ?? new NullWfNotifier();
    }

    public async Task<int> ScanOnceAsync(DateTime now, CancellationToken ct = default)
    {
        var due = await _db.Wf_FlowTasks
            .Where(t => t.Status == FlowTaskStatus.Pending && t.DueAt != null && t.DueAt <= now && !t.TimeoutHandled)
            .OrderBy(t => t.DueAt)
            .Take(100)
            .ToListAsync(ct);

        var schemaCache = new Dictionary<string, FlowSchema>();
        int handled = 0;
        foreach (var task in due)
        {
            var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId, ct);
            if (inst is null || inst.Status != FlowInstanceStatus.Running) { task.TimeoutHandled = true; continue; }

            var node = NodeOf(schemaCache, inst.FlowKey, task.NodeId);
            var action = (node?.TimeoutAction ?? string.Empty).Trim().ToLowerInvariant();

            switch (action)
            {
                case "remind":   // 软：催办 + 顺延 DueAt（可重复，不置 Handled）
                    await _notifier.TodoCreatedAsync(task.AssigneeId, inst.Id, task.Id, inst.FlowKey);
                    task.DueAt = now.AddHours(node!.TimeoutHours is int h && h > 0 ? h : 24);
                    break;

                case "approve":  // 硬：系统自动同意（ActAsync 幂等推进会签/流转）
                    await _engine.ActAsync(task.Id, SystemActor, approve: true, "超时自动同意");
                    task.TimeoutHandled = true;
                    break;

                case "reject":   // 硬：系统自动驳回
                    await _engine.ActAsync(task.Id, SystemActor, approve: false, "超时自动驳回");
                    task.TimeoutHandled = true;
                    break;

                case "escalate": // 硬：升级给 EscalateTo（换 assignee + 双痕），原人不再持有
                    if (node!.EscalateTo is Guid up)
                    {
                        _db.Wf_FlowHistories.Add(new Wf_FlowHistory
                        {
                            Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = task.NodeId,
                            ActorId = up, Action = "escalate", Comment = $"超时升级：{task.AssigneeId} → {up}",
                        });
                        task.AssigneeId = up;
                        task.DueAt = null;   // 升级后清限时，避免再触发
                        await _notifier.TodoCreatedAsync(up, inst.Id, task.Id, inst.FlowKey);
                    }
                    task.TimeoutHandled = true;
                    break;

                case "erroredge": // 硬：审批超时走失败边（infra ②），委托引擎节点级清场+路由；置 Handled 防反复扫
                                  // （action 已 ToLowerInvariant → 匹配小写 "erroredge"，对齐既有 case 约定）
                    await _engine.TimeoutAdvanceErrorEdgeAsync(task.Id, SystemActor, ct);
                    task.TimeoutHandled = true;
                    break;

                default:         // 无有效动作配置 → 标记，避免反复扫
                    task.TimeoutHandled = true;
                    break;
            }
            handled++;
        }

        await _db.SaveChangesAsync(ct);
        return handled;
    }

    private FlowSchema? NodeOf_Schema(Dictionary<string, FlowSchema> cache, string flowKey)
    {
        if (cache.TryGetValue(flowKey, out var s)) return s;
        var def = _db.Wf_FlowDefs.FirstOrDefault(x => x.FlowKey == flowKey);
        if (def is null) return null;
        s = JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson, JsonOpts) ?? new FlowSchema();
        cache[flowKey] = s;
        return s;
    }

    private FlowNode? NodeOf(Dictionary<string, FlowSchema> cache, string flowKey, string nodeId)
        => NodeOf_Schema(cache, flowKey)?.Nodes.FirstOrDefault(n => n.Id == nodeId);
}
