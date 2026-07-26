using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CP6.Core.Services.Oa;

public class DesignerService : IDesignerService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    private readonly IFlowDefService _flowDef;
    private readonly IEnumerable<IServiceTaskExecutor> _execs;
    private readonly IEnumerable<IWfConnector> _connectors;
    public DesignerService(CP6Context db, IFlowDefService flowDef,
        IEnumerable<IServiceTaskExecutor> execs, IEnumerable<IWfConnector> connectors)
    {
        _db = db; _flowDef = flowDef; _execs = execs; _connectors = connectors;
    }

    /// <summary>P1-6 服务目录：actions 只含 Kind==dataWriteback 且 VisibleInDesigner 的执行器
    /// （WebApiExecutor 被排除）；connectors 含全部注册连接器。每项 {name, label(DisplayName)}。</summary>
    public ServiceCatalog GetServiceCatalog() => new(
        _execs.Where(e => e.Kind == ServiceKind.DataWriteback && e.VisibleInDesigner)
              .Select(e => new ServiceCatalogItem(e.Key, e.DisplayName)).ToList(),
        _connectors.Select(c => new ServiceCatalogItem(c.Name, c.DisplayName)).ToList());

    public async Task<IReadOnlyList<FlowDefSummary>> ListAsync(string? functionId = null)
    {
        var q = _db.Wf_FlowDefs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(functionId)) q = q.Where(d => d.FunctionId == functionId);
        return await q.OrderBy(d => d.FunctionId).ThenBy(d => d.FlowKey)
            .Select(d => new FlowDefSummary(d.FlowKey, d.FlowName, d.FormKey, d.FunctionId, d.FlowCode, d.Version, d.Enable))
            .ToListAsync();
    }

    public async Task<FlowDefSummary?> LoadAsync(string flowKey) =>
        await _db.Wf_FlowDefs.Where(d => d.FlowKey == flowKey)
            .Select(d => new FlowDefSummary(d.FlowKey, d.FlowName, d.FormKey, d.FunctionId, d.FlowCode, d.Version, d.Enable))
            .FirstOrDefaultAsync();

    public async Task SaveAsync(SaveFlowRequest req, string? user)
    {
        // ① schema 校验
        var schema = JsonSerializer.Deserialize<FlowSchema>(req.SchemaJson, JsonOpts) ?? new FlowSchema();
        var schemaErrs = FlowSchemaValidator.Validate(schema);
        if (schemaErrs.Count > 0) throw new InvalidOperationException(schemaErrs[0]);

        // ①b 服务任务引用注册名校验(E-WF-018)：dataWriteback 的 ActionName 须命中 Kind==dataWriteback 的执行器 Key；
        //     webApi 的 ConnectorName 须命中连接器 Name。缺名属 E-WF-016(上一步已拦)，此处只查"引用了但未注册"。
        var actionKeys = _execs.Where(e => e.Kind == ServiceKind.DataWriteback)
            .Select(e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var connNames = _connectors.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var n in schema.Nodes.Where(n =>
            string.Equals((n.Type ?? string.Empty).Trim(), "serviceTask", StringComparison.OrdinalIgnoreCase)))
        {
            if (n.ServiceKind == ServiceKind.DataWriteback && !string.IsNullOrWhiteSpace(n.ServiceActionName)
                && !actionKeys.Contains(n.ServiceActionName))
                throw new InvalidOperationException("E-WF-018");
            if (n.ServiceKind == ServiceKind.WebApi && !string.IsNullOrWhiteSpace(n.ServiceConnectorName)
                && !connNames.Contains(n.ServiceConnectorName))
                throw new InvalidOperationException("E-WF-018");
        }

        // ①c E-WF-028 保存侧（节点 HTTP 超时 >= 租约 → 拒，E-T1）：与 WfConnectorService.ValidateLease 同源常量
        //     WfServiceJobService.LeaseDuration（单次调用须严格短于租约，否则 reaper 误判崩溃重投→重复外呼）。
        //     静态值域（>0 且 <=3600）已由 FlowSchemaValidator 前置；此处只加租约下界。仅挂 SaveAsync，不影响旧数据读取。
        var leaseSec = (int)WfServiceJobService.LeaseDuration.TotalSeconds;
        foreach (var n in schema.Nodes.Where(n =>
            string.Equals((n.Type ?? string.Empty).Trim(), "serviceTask", StringComparison.OrdinalIgnoreCase)))
        {
            if (n.ServiceTimeoutSec is int ts && ts >= leaseSec)
                throw new InvalidOperationException("E-WF-028");
        }

        // ①d 子流程引用校验(E-WF-025/026,子流程 spec §5)：FlowKey 存在且启用 + 引用环 DFS(深度 8,当前已发布版快照)。
        //     brief 原定接线位 ①c 已被波⑤ E-WF-028 租约校验占用,顺延为 ①d(报告说明)。仅挂 SaveAsync,不影响旧数据读取。
        SubFlowRefValidator.Validate(_db, req.FlowKey, schema);

        // ② 身份码租户内唯一（排除自身 FlowKey）
        if (!string.IsNullOrWhiteSpace(req.FunctionId) &&
            await _db.Wf_FlowDefs.AnyAsync(d => d.FunctionId == req.FunctionId && d.FlowKey != req.FlowKey))
            throw new InvalidOperationException("E-WF-009");
        if (!string.IsNullOrWhiteSpace(req.FlowCode) &&
            await _db.Wf_FlowDefs.AnyAsync(d => d.FlowCode == req.FlowCode && d.FlowKey != req.FlowKey))
            throw new InvalidOperationException("E-WF-009");

        // ③ upsert（SaveDef 升版） + 身份码落库
        await _flowDef.SaveDraftAsync(req.FlowKey, req.FlowName, req.FormKey, req.SchemaJson, req.RowVersion, user);
        var def = await _db.Wf_FlowDefs.FirstAsync(d => d.FlowKey == req.FlowKey);
        def.FunctionId = string.IsNullOrWhiteSpace(req.FunctionId) ? null : req.FunctionId;
        def.FlowCode = string.IsNullOrWhiteSpace(req.FlowCode) ? null : req.FlowCode;
        await _db.SaveChangesAsync();
    }

    public async Task CloneAsync(CloneRequest req, string? user)
    {
        var src = await _flowDef.GetDefAsync(req.SourceFlowKey)
                  ?? throw new InvalidOperationException("E-WF-006");
        if (await _db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == req.NewFlowKey))
            throw new InvalidOperationException("E-WF-009");   // 新 FlowKey 已存在
        // 独立副本：同 schema/FormKey，清身份码 + 停用（避免撞唯一、需重新设定身份与启用）
        await _flowDef.SaveDraftAsync(req.NewFlowKey, req.NewFlowName, src.FormKey, src.SchemaJson, null, user);
        var copy = await _db.Wf_FlowDefs.FirstAsync(d => d.FlowKey == req.NewFlowKey);
        copy.FunctionId = null; copy.FlowCode = null; copy.Enable = false;
        await _db.SaveChangesAsync();
    }
}
