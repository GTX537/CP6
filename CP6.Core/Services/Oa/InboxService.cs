using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class InboxService : IInboxService
{
    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;       // T7 批量办理
    private readonly IForecastService _forecast; // T8 详情预计段
    public InboxService(CP6Context db, IFlowEngine engine, IForecastService forecast)
    {
        _db = db; _engine = engine; _forecast = forecast;
    }

    public async Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId)
    {
        var rows = await (from t in _db.Wf_FlowTasks
                          where t.AssigneeId == userId && t.Status == FlowTaskStatus.Pending
                          join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                          where i.Status == FlowInstanceStatus.Running
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          orderby t.CreateDate descending
                          select new { t, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
        return rows.Select(x => new InboxPendingItem(
            x.t.Id, x.i.Id, x.t.TokenId, x.i.FlowKey, x.FlowName,
            x.t.NodeId, null, x.i.StarterId,
            x.Starter == null ? "" : (string.IsNullOrWhiteSpace(x.Starter.NickName) ? x.Starter.UserName : x.Starter.NickName!),
            x.i.BizType, x.i.BizId, x.t.IsRead, x.t.CreateDate)).ToList();
    }

    public async Task<IReadOnlyList<InboxCcItem>> PendingCcAsync(Guid userId)
    {
        var rows = await (from c in _db.Wf_FlowCcs
                          where c.RecipientId == userId
                          join i in _db.Wf_FlowInstances on c.InstanceId equals i.Id
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          orderby c.CreateDate descending
                          select new { c, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
        return rows.Select(x => new InboxCcItem(
            x.c.Id, x.i.Id, x.i.FlowKey, x.FlowName, x.c.AtNodeId, x.i.StarterId,
            x.Starter == null ? "" : (string.IsNullOrWhiteSpace(x.Starter.NickName) ? x.Starter.UserName : x.Starter.NickName!),
            x.c.IsRead, x.c.CreateDate)).ToList();
    }

    public async Task MarkTaskReadAsync(Guid userId, Guid taskId)
    {
        var t = await _db.Wf_FlowTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.AssigneeId == userId);
        if (t is null || t.IsRead) return;   // 幂等：不存在/非本人/已读 → no-op
        t.IsRead = true; t.ReadAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task MarkCcReadAsync(Guid userId, Guid ccId)
    {
        var c = await _db.Wf_FlowCcs.FirstOrDefaultAsync(x => x.Id == ccId && x.RecipientId == userId);
        if (c is null || c.IsRead) return;
        c.IsRead = true; c.ReadAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private static string Name(Sys_User? u) =>
        u == null ? "" : (string.IsNullOrWhiteSpace(u.NickName) ? u.UserName : u.NickName!);

    public async Task<IReadOnlyList<InboxRunningItem>> RunningAsync(Guid userId)
    {
        var rows = await (from i in _db.Wf_FlowInstances
                          where i.StarterId == userId && i.Status == FlowInstanceStatus.Running
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          orderby i.CreateDate descending
                          select new { i, FlowName = d == null ? null : d.FlowName }).ToListAsync();
        var instIds = rows.Select(x => x.i.Id).ToList();
        var pendings = await _db.Wf_FlowFormTos
            .Where(f => instIds.Contains(f.InstanceId) && f.Status == FlowFormToStatus.Pending)
            .Select(f => new { f.InstanceId, f.ExpectedHandlerId }).ToListAsync();
        var names = await OaUserNames.ResolveAsync(_db, pendings.Select(p => p.ExpectedHandlerId));
        return rows.Select(x => new InboxRunningItem(
            x.i.Id, x.i.FlowKey, x.FlowName, x.i.CurrentNode, x.i.Status,
            pendings.Where(p => p.InstanceId == x.i.Id)
                    .Select(p => names.GetValueOrDefault(p.ExpectedHandlerId, p.ExpectedHandlerId.ToString()))
                    .Distinct().ToList(),
            x.i.CreateDate)).ToList();
    }

    public async Task<IReadOnlyList<InboxDoneItem>> DoneAsync(Guid userId, int? year, int? month, string tab = "mine")
    {
        bool InMonth(DateTime dt) => (year is null || dt.Year == year) && (month is null || dt.Month == month);

        var mine = new List<InboxDoneItem>();
        if (tab is "mine" or "all")
        {
            var handled = await (from f in _db.Wf_FlowFormTos
                                 where f.ActualHandlerId == userId && f.HandledAt != null
                                       && (f.Status == FlowFormToStatus.Approved
                                           || f.Status == FlowFormToStatus.Rejected
                                           || f.Status == FlowFormToStatus.Transferred)
                                 join i in _db.Wf_FlowInstances on f.InstanceId equals i.Id
                                 join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                                 from d in dd.DefaultIfEmpty()
                                 join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                                 from s in ss.DefaultIfEmpty()
                                 select new { f, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
            mine = handled.Where(x => InMonth(x.f.HandledAt!.Value))
                .GroupBy(x => x.i.Id)
                .Select(g => g.OrderByDescending(x => x.f.HandledAt).First())
                .Select(x => new InboxDoneItem(x.i.Id, x.i.FlowKey, x.FlowName, x.i.StarterId, Name(x.Starter),
                    x.f.Status, x.f.HandledAt!.Value, x.i.Status))
                .OrderByDescending(x => x.DoneAt).ToList();
        }

        var cc = new List<InboxDoneItem>();
        if (tab is "cc" or "all")
        {
            var ccRows = await (from c in _db.Wf_FlowCcs
                                where c.RecipientId == userId
                                join i in _db.Wf_FlowInstances on c.InstanceId equals i.Id
                                join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                                from d in dd.DefaultIfEmpty()
                                join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                                from s in ss.DefaultIfEmpty()
                                select new { c, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
            cc = ccRows.Where(x => InMonth(x.c.CreateDate))
                .Select(x => new InboxDoneItem(x.i.Id, x.i.FlowKey, x.FlowName, x.i.StarterId, Name(x.Starter),
                    x.i.Status, x.c.CreateDate, x.i.Status))
                .OrderByDescending(x => x.DoneAt).ToList();
        }

        if (tab == "mine") return mine;
        if (tab == "cc") return cc;
        return mine.Concat(cc).GroupBy(x => x.InstanceId)
            .Select(g => g.OrderByDescending(x => x.DoneAt).First())
            .OrderByDescending(x => x.DoneAt).ToList();
    }

    public async Task<IReadOnlyList<BatchActResultItem>> ActBatchAsync(
        Guid userId, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null)
    {
        var results = new List<BatchActResultItem>();
        foreach (var taskId in taskIds.Distinct())
        {
            var t = await _db.Wf_FlowTasks.FirstOrDefaultAsync(x => x.Id == taskId);
            if (t is null || t.AssigneeId != userId || t.Status != FlowTaskStatus.Pending)
            {
                results.Add(new BatchActResultItem(taskId, false, "E-WF-004"));   // 无效/已办/非本人
                continue;
            }
            try
            {
                await _engine.ActAsync(taskId, userId, approve, comment);
                results.Add(new BatchActResultItem(taskId, true, null));
            }
            catch (InvalidOperationException e)
            {
                results.Add(new BatchActResultItem(taskId, false, e.Message));
            }
        }
        return results;
    }

    public async Task<InboxDetail?> DetailAsync(Guid instanceId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId);
        if (inst is null) return null;
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(d => d.FlowKey == inst.FlowKey);
        var formSchema = def == null ? null
            : (await _db.Wf_FormDefs.FirstOrDefaultAsync(fd => fd.FormKey == def.FormKey))?.SchemaJson;

        var formTos = await _db.Wf_FlowFormTos.Where(f => f.InstanceId == instanceId)
            .OrderBy(f => f.StepSeq).ThenBy(f => f.SentAt).ToListAsync();
        var snaps = await _db.Wf_FlowDatas.Where(s => s.InstanceId == instanceId)
            .OrderBy(s => s.StepSeq).ToListAsync();
        var ccs = await _db.Wf_FlowCcs.Where(c => c.InstanceId == instanceId).ToListAsync();

        var ids = formTos.SelectMany(f => new[] { f.ExpectedHandlerId, f.ActualHandlerId ?? Guid.Empty, f.OnBehalfOfId ?? Guid.Empty })
            .Concat(ccs.Select(c => c.RecipientId));
        var names = await OaUserNames.ResolveAsync(_db, ids);
        string? N(Guid? id) => id is null || id == Guid.Empty ? null : names.GetValueOrDefault(id.Value, id.Value.ToString());

        var timeline = formTos.Select(f => new TimelineRow(
            f.StepSeq, f.TokenId, f.NodeId, f.NodeName,
            f.ExpectedHandlerId, names.GetValueOrDefault(f.ExpectedHandlerId, f.ExpectedHandlerId.ToString()),
            f.ActualHandlerId, N(f.ActualHandlerId), f.OnBehalfOfId, N(f.OnBehalfOfId),
            f.Status, f.Comment, f.SentAt, f.HandledAt)).ToList();
        var snapshots = snaps.Select(s => new SnapshotRow(s.StepSeq, s.NodeId, s.DataJson)).ToList();
        var ccRows = ccs.Select(c => new CcRow(c.RecipientId, N(c.RecipientId) ?? "", c.AtNodeId, c.IsRead)).ToList();

        IReadOnlyList<ForecastStep> forecast = inst.Status == FlowInstanceStatus.Running
            ? (await _forecast.ForecastAsync(inst.FlowKey, inst.VarsJson, inst.StarterId, fromNodeId: inst.CurrentNode)).Steps
            : Array.Empty<ForecastStep>();

        return new InboxDetail(inst, def?.FlowName, def?.FormKey, formSchema,
            inst.VarsJson, timeline, snapshots, forecast, ccRows);
    }

    public async Task<InboxStats> StatsAsync(Guid userId)
    {
        var pending = await PendingAsync(userId);
        var running = await RunningAsync(userId);
        var doneMine = await DoneAsync(userId, DateTime.Now.Year, DateTime.Now.Month, "mine");
        var rejectedBack = await _db.Wf_FlowInstances
            .CountAsync(i => i.StarterId == userId && i.Status == FlowInstanceStatus.Rejected);

        var since = DateTime.Now.Date.AddDays(-6);
        var handledRows = await _db.Wf_FlowFormTos
            .Where(f => f.ActualHandlerId == userId && f.HandledAt != null && f.HandledAt >= since)
            .Select(f => f.HandledAt!.Value).ToListAsync();
        var trend = Enumerable.Range(0, 7).Select(d =>
        {
            var day = since.AddDays(d);
            return new TrendPoint(day.ToString("MM-dd"), handledRows.Count(h => h.Date == day));
        }).ToList();

        return new InboxStats(pending.Count, running.Count, doneMine.Count, rejectedBack,
            trend, pending.Take(5).ToList());
    }
}
