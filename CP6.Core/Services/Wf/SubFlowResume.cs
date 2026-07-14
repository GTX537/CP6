using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>subFlowResume 内部 job 载荷（spec §3.2 第一段）。ParentTokenId 是复核定位键；
/// ChildInstanceId/SubIndex 供排查与哨兵防重。</summary>
internal sealed record SubFlowResumePayload(Guid ParentTokenId, Guid ParentInstanceId, Guid ChildInstanceId, int SubIndex)
{
    private static readonly JsonSerializerOptions Opts = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static SubFlowResumePayload? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<SubFlowResumePayload>(json, Opts);
            return p is { ParentTokenId: var t } && t != Guid.Empty ? p : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>子终态第一段原子入队（spec D5）：只做纯内存 Add——与子终态同一 SaveChanges 持久化即 crash-safe，
/// 窗口内零计票/零推进/零外呼（DispatchIfFinished 原子接缝铁律相容）。
/// <para>防撞定案（计划侦察结论 #5）：<c>TokenId=子实例 Id</c> + <c>NodeId="$subFlowResume"</c> 哨兵——
/// 若用 ParentTokenId 占 TokenId 槽，同组两个并发子终态会撞 <c>UX_Wf_ServiceJob_LiveToken</c> filtered unique
/// 令子终态事务整体失败；子实例 Id 全局唯一且一次终态一条凭据，天然防重不撞组。ParentTokenId 走载荷。</para></summary>
internal static class SubFlowResume
{
    public const string JobNodeId = "$subFlowResume";

    public static void EnqueueIfChild(CP6Context db, Wf_FlowInstance inst)
    {
        if (inst.ParentInstanceId is not Guid pi || inst.ParentTokenId is not Guid pt) return;   // 顶层实例：纯谓词短路,零开销

        // 防重（Local ∪ DB 惯用法,镜像 ServiceTaskNodeHandler.EnqueueServiceJob）：每子实例至多一条活跃凭据
        if (db.Wf_ServiceJobs.Local.Any(j => j.TokenId == inst.Id && j.NodeId == JobNodeId
                && (j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Running)))
            return;
        var localIds = db.Wf_ServiceJobs.Local
            .Where(j => j.TokenId == inst.Id && j.NodeId == JobNodeId).Select(j => j.Id).ToHashSet();
        if (db.Wf_ServiceJobs.Any(j => j.TokenId == inst.Id && j.NodeId == JobNodeId
                && (j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Running)
                && !localIds.Contains(j.Id)))
            return;

        var now = DateTime.UtcNow;
        db.Wf_ServiceJobs.Add(new Wf_ServiceJob
        {
            Id = Guid.NewGuid(),
            InstanceId = pi,               // 归父实例：父终止清 Pending 是良性动作（复核状态闸兜底）
            TokenId = inst.Id,             // ★ 子实例 Id 占防撞键
            NodeId = JobNodeId,
            Kind = WfJobKind.SubFlowResume,
            ActionRefJson = new SubFlowResumePayload(pt, pi, inst.Id, inst.SubIndex ?? 0).ToJson(),
            DueAtUtc = now,
            Status = ServiceJobStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = 4,               // 复核幂等,重投无害；对齐 job 缺省口径
            NextAttemptAtUtc = now,
            CreateDate = now,
        });   // TenantId 由 StampTenant 自动盖
    }
}
