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

    public async Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null)
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

        // ── rowMode（wfs-inbox-ux §5）：merged=同实例合并取最新（照 DoneAsync 既有口径）；分组先于分页 ──
        if (rowMode != "expanded")
            rows = rows.GroupBy(x => x.i.Id)
                       .Select(g => g.OrderByDescending(x => x.t.CreateDate).First())
                       .OrderByDescending(x => x.t.CreateDate)
                       .ToList();
        if (page is { } p && pageSize is { } ps && p >= 1 && ps >= 1)
            rows = rows.Skip((p - 1) * ps).Take(ps).ToList();

        // Batch-load frozen stage plans for tokens that carry multi-stage plans
        var tokenIds = rows.Where(x => x.t.TokenId.HasValue).Select(x => x.t.TokenId!.Value).Distinct().ToList();
        var tokenPlans = new Dictionary<(Guid tokenId, int stageIndex), (string? name, string? code)>();
        if (tokenIds.Count > 0)
        {
            var tokens = await _db.Wf_FlowTokens
                .Where(tok => tokenIds.Contains(tok.Id) && tok.StagePlanJson != null)
                .Select(tok => new { tok.Id, tok.StagePlanJson })
                .ToListAsync();
            foreach (var tok in tokens)
            {
                if (string.IsNullOrEmpty(tok.StagePlanJson)) continue;
                var plan = System.Text.Json.JsonSerializer.Deserialize<List<Wf.RuntimeApprovalStage>>(tok.StagePlanJson);
                if (plan is null) continue;
                foreach (var stage in plan)
                    tokenPlans[(tok.Id, stage.StageIndex)] = (stage.StageName, stage.StageCode);
            }
        }

        return rows.Select(x =>
        {
            (string? stageName, string? stageCode) = (x.t.TokenId.HasValue)
                ? tokenPlans.GetValueOrDefault((x.t.TokenId.Value, x.t.StageIndex), (null, null))
                : (null, null);
            return new InboxPendingItem(
                x.t.Id, x.i.Id, x.t.TokenId, x.i.FlowKey, x.FlowName,
                x.t.NodeId, null, x.i.StarterId,
                x.Starter == null ? "" : (string.IsNullOrWhiteSpace(x.Starter.NickName) ? x.Starter.UserName : x.Starter.NickName!),
                x.i.BizType, x.i.BizId, x.t.IsRead, x.t.CreateDate,
                StageIndex: x.t.StageIndex, StageRound: x.t.StageRound,
                StageName: stageName, StageCode: stageCode,
                CanSendBackPrevStage: x.t.StageIndex > 0);
        }).ToList();
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

    public async Task<IReadOnlyList<BatchActResultItem>> ActBatchAsAsync(
        Guid actorId, Guid? onBehalfOf, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null)
    {
        var owner = onBehalfOf ?? actorId;
        var results = new List<BatchActResultItem>();
        foreach (var taskId in taskIds.Distinct())
        {
            var t = await _db.Wf_FlowTasks.FirstOrDefaultAsync(x => x.Id == taskId);
            if (t is null || t.AssigneeId != owner || t.Status != FlowTaskStatus.Pending)
            { results.Add(new BatchActResultItem(taskId, false, "E-WF-004")); continue; }
            try
            {
                await _engine.ActAsAsync(taskId, actorId, onBehalfOf, approve, comment);
                results.Add(new BatchActResultItem(taskId, true, null));
            }
            catch (InvalidOperationException e) { results.Add(new BatchActResultItem(taskId, false, e.Message)); }
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
            f.Status, f.Comment, f.SentAt, f.HandledAt,
            StageIndex: f.StageIndex, StageRound: f.StageRound)).ToList();
        var snapshots = snaps.Select(s => new SnapshotRow(s.StepSeq, s.NodeId, s.DataJson)).ToList();
        var ccRows = ccs.Select(c => new CcRow(c.RecipientId, N(c.RecipientId) ?? "", c.AtNodeId, c.IsRead)).ToList();

        IReadOnlyList<ForecastStep> forecast = inst.Status == FlowInstanceStatus.Running
            ? (await _forecast.ForecastAsync(inst.FlowKey, inst.VarsJson, inst.StarterId, fromNodeId: inst.CurrentNode)).Steps
            : Array.Empty<ForecastStep>();

        // ── 子流程互链（spec §4.5）：向上=父实例链接;向下=本实例名下子实例组（按停泊 token 的 NodeId 归组）──
        SubFlowParentRow? subFlowParent = null;
        if (inst.ParentInstanceId is Guid parentId)
        {
            var p = await _db.Wf_FlowInstances.FirstOrDefaultAsync(x => x.Id == parentId);
            var pDef = p is null ? null : await _db.Wf_FlowDefs.FirstOrDefaultAsync(d => d.FlowKey == p.FlowKey);
            if (p is not null) subFlowParent = new SubFlowParentRow(p.Id, p.FlowKey, pDef?.FlowName);
        }
        var childRows = await (
            from c in _db.Wf_FlowInstances
            where c.ParentInstanceId == instanceId
            join tk in _db.Wf_FlowTokens on c.ParentTokenId equals tk.Id
            join cd in _db.Wf_FlowDefs on c.FlowKey equals cd.FlowKey into cds
            from cd in cds.DefaultIfEmpty()
            orderby tk.NodeId, c.SubIndex
            select new SubFlowChildRow(c.Id, c.SubIndex ?? 0, c.FlowKey, cd != null ? cd.FlowName : null, c.Status, tk.NodeId)
        ).ToListAsync();

        return new InboxDetail(inst, def?.FlowName, def?.FormKey, formSchema,
            inst.VarsJson, timeline, snapshots, forecast, ccRows,
            subFlowParent, childRows);
    }

    public async Task<IReadOnlyList<FormQueryItem>> QueryAsync(FormQueryFilter f)
    {
        var q = _db.Wf_FlowInstances.AsQueryable();
        if (f.StarterId is { } s) q = q.Where(i => i.StarterId == s);
        if (!string.IsNullOrWhiteSpace(f.FlowKey)) q = q.Where(i => i.FlowKey == f.FlowKey);
        if (f.Status is { } st) q = q.Where(i => i.Status == st);
        if (f.From is { } fr) q = q.Where(i => i.CreateDate >= fr);
        if (f.To is { } to) q = q.Where(i => i.CreateDate <= to);
        if (f.HandlerId is { } h)   // 处理人：我办过/正办该实例
            q = q.Where(i => _db.Wf_FlowFormTos.Any(ft => ft.InstanceId == i.Id
                && (ft.ExpectedHandlerId == h || ft.ActualHandlerId == h)));
        if (!string.IsNullOrWhiteSpace(f.Keyword))
            q = q.Where(i => i.FlowKey.Contains(f.Keyword!) || (i.BizId != null && i.BizId.Contains(f.Keyword!)));

        var rows = await (from i in q
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          join u in _db.Sys_Users on i.StarterId equals u.Id into uu
                          from u in uu.DefaultIfEmpty()
                          orderby i.CreateDate descending
                          select new { i, FlowName = d == null ? null : d.FlowName, Starter = u }).Take(500).ToListAsync();
        return rows.Select(x => new FormQueryItem(x.i.Id, x.i.FlowKey, x.FlowName, x.i.StarterId,
            Name(x.Starter), x.i.Status, x.i.CurrentNode, x.i.CreateDate)).ToList();
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

    // ── 在途批量转单（wfs-inbox-ux §3，D3：逐条独立事务 + 汇总报告）──────────

    private const int MaxBatchTransfer = 500;

    /// <summary>
    /// 候选查询。常规路径：from 的全部 Pending 待办（Running 实例）按 filter 收窄；
    /// BeforeUtc 直接比对 CreateDate（库内为服务器本地时，C7）。
    /// <b>TaskIds 显式点名（=单条重试口径，spec §3.2）</b>：不预筛任务/实例状态，让引擎
    /// TransferAsync 裁决——已办结等脏数据以失败明细行（E-WF-002）呈现，不特殊处理；
    /// 仍保留 AssigneeId==from 归属过滤（已被转走的任务不再属 from，绝不能改派他人任务）。
    /// </summary>
    private async Task<List<(Guid TaskId, string FlowKey)>> QueryTransferCandidatesAsync(Guid fromUserId, BatchTransferFilter? f)
    {
        if (f?.TaskIds is { Count: > 0 } ids)
        {
            var named = await (from t in _db.Wf_FlowTasks
                               where t.AssigneeId == fromUserId && ids.Contains(t.Id)
                               join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                               orderby t.CreateDate
                               select new { t.Id, i.FlowKey }).ToListAsync();
            return named.Select(x => (x.Id, x.FlowKey)).ToList();
        }

        var q = from t in _db.Wf_FlowTasks
                where t.AssigneeId == fromUserId && t.Status == FlowTaskStatus.Pending
                join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                where i.Status == FlowInstanceStatus.Running
                select new { t.Id, i.FlowKey, t.CreateDate };
        if (!string.IsNullOrWhiteSpace(f?.FlowKey)) q = q.Where(x => x.FlowKey == f.FlowKey);
        if (f?.BeforeUtc is { } before) q = q.Where(x => x.CreateDate < before);
        var rows = await q.OrderBy(x => x.CreateDate).ToListAsync();
        return rows.Select(x => (x.Id, x.FlowKey)).ToList();
    }

    /// <inheritdoc/>
    public async Task<BatchTransferReport> BatchTransferAsync(
        Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null)
    {
        // 前置校验（入参级，400 口径，不占 E-WF 码）
        if (fromUserId == toUserId)
            throw new InvalidOperationException("oa.bt.errSameUser");
        var to = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == toUserId);   // 全局租户过滤器：跨租户查不到（R3）
        if (to is null || !to.Enable)
            throw new InvalidOperationException("oa.bt.errTargetInvalid");

        var candidates = await QueryTransferCandidatesAsync(fromUserId, filter);
        if (candidates.Count > MaxBatchTransfer)
            throw new InvalidOperationException("oa.bt.errTooMany");               // 超上限 → 提示分批（防长事务假象与超时）

        var failed = new List<BatchTransferItemResult>();
        var succeeded = 0;
        foreach (var (taskId, flowKey) in candidates)
        {
            try
            {
                // 引擎动作只调用不改动：内部校验 + FormTo 双行 + history + 通知 + 单次 SaveChanges（=单条独立事务）
                await _engine.TransferAsync(taskId, actorId, toUserId, comment, bypassOwnership: true);
                succeeded++;
            }
            catch (InvalidOperationException e)                                    // 单条失败不中断后续（D3）
            {
                failed.Add(new BatchTransferItemResult(taskId, flowKey, false, e.Message));
            }
        }
        return new BatchTransferReport(candidates.Count, succeeded, failed);
    }

    /// <inheritdoc/>
    public async Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null)
    {
        var candidates = await QueryTransferCandidatesAsync(fromUserId, filter);
        var candidateIds = candidates.Select(c => c.TaskId).Take(10).ToHashSet();
        var all = await PendingAsync(fromUserId, rowMode: "expanded");             // 逐任务行拿展示字段（C5：merged 现为默认，preview 须显式 expanded 保 sample 逐任务口径，R5）
        var sample = all.Where(p => candidateIds.Contains(p.TaskId)).ToList();
        return new BatchTransferPreview(candidates.Count, sample);
    }
}
