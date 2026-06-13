namespace CP6.Core.Services.Wf;

/// <summary>
/// 审批人解析策略（OA 章01）。消费 PUB 组织模型（Sys_Dept/Sys_User），不自建组织表。
/// </summary>
public enum ApproverStrategy
{
    /// <summary>直属上级（沿 Sys_User.ManagerId 上溯 Levels 级；链短于 N 取链顶）</summary>
    DirectManager,
    /// <summary>部门负责人（沿 Sys_Dept.ParentId 上溯，取首个有效 LeaderId）</summary>
    DeptLeader,
    /// <summary>指定角色（Sys_User.RoleId == RoleId 的全部启用用户）</summary>
    Role,
    /// <summary>指定用户（SpecifiedUserId）</summary>
    Specified,
    /// <summary>发起人本人</summary>
    Starter,
}

/// <summary>审批人规则。RoleId 为 int（OA-D2，对齐 Sys_User.RoleId 实际类型）。</summary>
public record ApproverRule(ApproverStrategy Strategy, int? Levels, int? RoleId, Guid? SpecifiedUserId);

/// <summary>解析上下文（当前仅需发起人；阶段2 接业务后可扩业务变量）。</summary>
public class ApproverResolveContext
{
    public Guid StarterUserId { get; set; }
}

/// <summary>
/// 解析结果。Resolved=false 时携 UnresolvedReason —— 流程引擎据此把实例挂起待人工指派，
/// 解析器本身纯查询、不抛异常（OA-D1）。
/// </summary>
public class ApproverResolveResult
{
    public List<Guid> ApproverIds { get; set; } = new();
    public bool Resolved => ApproverIds.Count > 0;
    public string? UnresolvedReason { get; set; }

    public static ApproverResolveResult Ok(params Guid[] ids) => new() { ApproverIds = ids.ToList() };
    public static ApproverResolveResult Unres(string why) => new() { UnresolvedReason = why };
}

public interface IApproverResolver
{
    Task<ApproverResolveResult> ResolveAsync(ApproverRule rule, ApproverResolveContext ctx);
}
