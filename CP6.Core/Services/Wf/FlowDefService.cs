using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>流程定义服务（OA 章03/04）。FlowDef upsert（schema 变更升版）+ 实例详情聚合查询。</summary>
public class FlowDefService : IFlowDefService
{
    private readonly CP6Context _db;
    public FlowDefService(CP6Context db) => _db = db;

    public async Task<Guid> SaveDefAsync(string flowKey, string flowName, string formKey, string schemaJson, string? user = null)
    {
        if (string.IsNullOrWhiteSpace(flowKey)) throw new InvalidOperationException("FlowKey 不能为空");

        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey);
        if (def == null)
        {
            def = new Wf_FlowDef
            {
                Id = Guid.NewGuid(),
                FlowKey = flowKey,
                FlowName = flowName,
                FormKey = formKey,
                SchemaJson = schemaJson,
                Version = 1,
                Creator = user,
            };
            _db.Wf_FlowDefs.Add(def);
        }
        else
        {
            if (def.SchemaJson != schemaJson) def.Version++;   // 仅 schema 变更才升版
            def.FlowName = flowName;
            def.FormKey = formKey;
            def.SchemaJson = schemaJson;
            def.Modifier = user;
            def.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return def.Id;
    }

    public Task<Wf_FlowDef?> GetDefAsync(string flowKey) =>
        _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey);

    public async Task<FlowInstanceDetail?> GetInstanceDetailAsync(Guid instanceId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId);
        if (inst == null) return null;

        return new FlowInstanceDetail
        {
            Instance = inst,
            History = await _db.Wf_FlowHistories
                .Where(h => h.InstanceId == instanceId)
                .OrderBy(h => h.CreateDate)
                .ToListAsync(),
            Tasks = await _db.Wf_FlowTasks
                .Where(t => t.InstanceId == instanceId)
                .ToListAsync(),
        };
    }
}
