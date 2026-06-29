using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Core.Services.Oa;

public class DesignerService : IDesignerService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    private readonly IFlowDefService _flowDef;
    public DesignerService(CP6Context db, IFlowDefService flowDef) { _db = db; _flowDef = flowDef; }

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

        // ② 身份码租户内唯一（排除自身 FlowKey）
        if (!string.IsNullOrWhiteSpace(req.FunctionId) &&
            await _db.Wf_FlowDefs.AnyAsync(d => d.FunctionId == req.FunctionId && d.FlowKey != req.FlowKey))
            throw new InvalidOperationException("E-WF-009");
        if (!string.IsNullOrWhiteSpace(req.FlowCode) &&
            await _db.Wf_FlowDefs.AnyAsync(d => d.FlowCode == req.FlowCode && d.FlowKey != req.FlowKey))
            throw new InvalidOperationException("E-WF-009");

        // ③ upsert（SaveDef 升版） + 身份码落库
        await _flowDef.SaveDefAsync(req.FlowKey, req.FlowName, req.FormKey, req.SchemaJson, user);
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
        await _flowDef.SaveDefAsync(req.NewFlowKey, req.NewFlowName, src.FormKey, src.SchemaJson, user);
        var copy = await _db.Wf_FlowDefs.FirstAsync(d => d.FlowKey == req.NewFlowKey);
        copy.FunctionId = null; copy.FlowCode = null; copy.Enable = false;
        await _db.SaveChangesAsync();
    }
}
