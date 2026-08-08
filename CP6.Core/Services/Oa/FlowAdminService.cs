using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class FlowAdminService : IFlowAdminService
{
    private readonly CP6Context _db;
    public FlowAdminService(CP6Context db) { _db = db; }

    public async Task<IReadOnlyList<FlowAdminItem>> ListFlowsAsync() =>
        await _db.Wf_FlowDefs.OrderBy(d => d.FormKey).ThenBy(d => d.FlowKey)
            .Select(d => new FlowAdminItem(
                d.FlowKey,
                d.FlowName,
                d.FormKey ?? string.Empty,
                d.Version,
                d.Enable))
            .ToListAsync();

    public async Task<FlowAdminItem?> GetFlowAsync(string flowKey) =>
        await _db.Wf_FlowDefs.Where(d => d.FlowKey == flowKey)
            .Select(d => new FlowAdminItem(
                d.FlowKey,
                d.FlowName,
                d.FormKey ?? string.Empty,
                d.Version,
                d.Enable))
            .FirstOrDefaultAsync();

    public async Task SetEnabledAsync(string flowKey, bool enabled)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(d => d.FlowKey == flowKey)
                  ?? throw new InvalidOperationException("E-WF-006");
        if (enabled && !def.Enable)
        {
            var conflict = await _db.Wf_FlowDefs.AnyAsync(d =>
                d.FormKey == def.FormKey && d.FlowKey != def.FlowKey && d.Enable);
            if (conflict) throw new InvalidOperationException("E-WF-008");   // 1 表单 ↔ 1 启用流程
        }
        def.Enable = enabled;
        await _db.SaveChangesAsync();
    }
}
