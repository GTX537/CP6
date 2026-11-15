using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
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

    public Task<int> ScanOnceAsync(DateTime now, CancellationToken ct = default)
        => _db.Database.IsNpgsql() ? ScanPostgreSqlAsync(now, ct) : ScanLegacyAsync(now, ct);

    private IQueryable<Wf_FlowTask> DueTasks(DateTime now) => _db.Wf_FlowTasks
        .Where(t => t.Status == FlowTaskStatus.Pending && t.DueAt != null && t.DueAt <= now && !t.TimeoutHandled)
        .Where(t => !_db.Wf_FlowInstances.Any(i => i.Id == t.InstanceId && (i.Creator == FlowEngine.ApprovalRuntimeV2 || i.Creator == FlowEngine.PersonalFormRuntimeV2)));

    private async Task<int> ScanPostgreSqlAsync(DateTime now, CancellationToken ct)
    {
        var candidates = await DueTasks(now).OrderBy(t => t.DueAt).Take(100).Select(t => t.Id).ToListAsync(ct);
        var handled = 0;
        foreach (var taskId in candidates)
            if (await ProcessPostgreSqlAsync(taskId, now, ct)) handled++;
        return handled;
    }

    private async Task<bool> ProcessPostgreSqlAsync(Guid taskId, DateTime now, CancellationToken ct)
    {
        // The scan owns this context. Re-read the candidate and tenant after acquiring the Main root.
        _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
            var tenantId = _db.CurrentTenantId;
            var tenant = await _db.Sys_Tenants.AsNoTracking().Where(t => t.Id == tenantId)
                .Select(t => new { t.Enable, t.ExpireDate }).SingleOrDefaultAsync(ct);
            if (tenant is not { Enable: true } || tenant.ExpireDate <= DateTime.Now)
                return false;

            var task = await DueTasks(now).SingleOrDefaultAsync(t => t.Id == taskId && t.TenantId == tenantId, ct);
            if (task is null) return false;
            var instance = await _db.Wf_FlowInstances.SingleOrDefaultAsync(i => i.Id == task.InstanceId && i.TenantId == tenantId, ct);
            var handled = false;
            if (instance is { Status: FlowInstanceStatus.Running })
            {
                var schema = await FlowEngine.LoadLegacySchemaAsync(_db, instance, ct);
                var node = schema.Nodes.FirstOrDefault(n => n.Id == task.NodeId);
                await ApplyTimeoutAsync(task, instance, node, now, postgres: true, ct);
                handled = true;
            }
            else
            {
                task.TimeoutHandled = true;
            }

            await _db.SaveChangesAsync(ct);
            // The scan time selects due tasks. Tenant expiry uses the current wall clock after the last write.
            if (tenant.ExpireDate <= DateTime.Now) return false;
            await transaction.CommitAsync(ct);
            return handled;
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    private async Task<int> ScanLegacyAsync(DateTime now, CancellationToken ct)
    {
        var due = await DueTasks(now).OrderBy(t => t.DueAt).Take(100).ToListAsync(ct);

        var schemaCache = new Dictionary<string, FlowSchema>();
        int handled = 0;
        foreach (var task in due)
        {
            var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId, ct);
            if (inst is null || inst.Status != FlowInstanceStatus.Running) { task.TimeoutHandled = true; continue; }

            var node = NodeOf(schemaCache, inst.FlowKey, task.NodeId);
            await ApplyTimeoutAsync(task, inst, node, now, postgres: false, ct);
            handled++;
        }

        await _db.SaveChangesAsync(ct);
        return handled;
    }

    private async Task ApplyTimeoutAsync(Wf_FlowTask task, Wf_FlowInstance inst, FlowNode? node, DateTime now,
        bool postgres, CancellationToken ct)
    {
        var action = (node?.TimeoutAction ?? string.Empty).Trim().ToLowerInvariant();
        switch (action)
        {
            case "remind":
                await _notifier.TodoCreatedAsync(task.AssigneeId, inst.Id, task.Id, inst.FlowKey);
                task.DueAt = now.AddHours(node!.TimeoutHours is int hours && hours > 0 ? hours : 24);
                break;

            case "approve":
            case "reject":
                var approve = action == "approve";
                var comment = approve ? "超时自动同意" : "超时自动驳回";
                if (postgres)
                    await _engine.ActAsAsync(task.Id, SystemActor, onBehalfOf: null, approve, comment);
                else
                    await _engine.ActAsync(task.Id, SystemActor, approve, comment);
                task.TimeoutHandled = true;
                break;

            case "escalate":
                if (node!.EscalateTo is Guid target)
                {
                    _db.Wf_FlowHistories.Add(new Wf_FlowHistory
                    {
                        Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = task.NodeId,
                        ActorId = target, Action = "escalate", Comment = $"超时升级：{task.AssigneeId} → {target}",
                    });
                    task.AssigneeId = target;
                    task.DueAt = null;
                    await _notifier.TodoCreatedAsync(target, inst.Id, task.Id, inst.FlowKey);
                }
                task.TimeoutHandled = true;
                break;

            case "erroredge":
                await _engine.TimeoutAdvanceErrorEdgeAsync(task.Id, SystemActor, ct);
                task.TimeoutHandled = true;
                break;

            default:
                task.TimeoutHandled = true;
                break;
        }
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
