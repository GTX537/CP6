using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class DraftService : IDraftService
{
    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;
    public DraftService(CP6Context db, IFlowEngine engine) { _db = db; _engine = engine; }

    public async Task<Guid> SaveDraftAsync(Guid starterId, string flowKey, string varsJson)
    {
        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, StarterId = starterId,
            Status = FlowInstanceStatus.Draft, CurrentNode = "", VarsJson = varsJson ?? "{}",
            Creator = starterId.ToString(),
        };
        _db.Wf_FlowInstances.Add(inst);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    public async Task UpdateDraftAsync(Guid starterId, Guid instanceId, string varsJson)
    {
        var inst = await LoadOwnedDraftAsync(starterId, instanceId);
        inst.VarsJson = varsJson ?? "{}";
        inst.Modifier = starterId.ToString();
        inst.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<InboxRunningItem>> ListDraftsAsync(Guid starterId)
    {
        var rows = await (from i in _db.Wf_FlowInstances
                          where i.StarterId == starterId && i.Status == FlowInstanceStatus.Draft
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          orderby i.CreateDate descending
                          select new { i, FlowName = d == null ? null : d.FlowName }).ToListAsync();
        return rows.Select(x => new InboxRunningItem(
            x.i.Id, x.i.FlowKey, x.FlowName, x.i.CurrentNode, x.i.Status,
            Array.Empty<string>(), x.i.CreateDate)).ToList();
    }

    public async Task DeleteDraftAsync(Guid starterId, Guid instanceId)
    {
        var inst = await LoadOwnedDraftAsync(starterId, instanceId);
        _db.Wf_FlowInstances.Remove(inst);
        await _db.SaveChangesAsync();
    }

    public Task SubmitDraftAsync(Guid starterId, Guid instanceId)
        => _engine.StartDraftAsync(instanceId, starterId);   // 引擎内已校验 owner/Draft 态 → E-WF-003

    private async Task<Wf_FlowInstance> LoadOwnedDraftAsync(Guid starterId, Guid instanceId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
                   ?? throw new InvalidOperationException("E-WF-003");
        if (inst.StarterId != starterId || inst.Status != FlowInstanceStatus.Draft)
            throw new InvalidOperationException("E-WF-003");
        return inst;
    }
}
