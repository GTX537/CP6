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
    /// <summary>表单字段指定(③ Delta 02)：VarsJson[FieldName] → UserId(s)。</summary>
    FormField,
    /// <summary>数据驱动(②b Delta 17)：VarsJson[FieldName] 匹配值 → 查 Wf_ApproverMap(MapKey)。</summary>
    DataMap,
    /// <summary>JSON 组(① Delta 15)：Members 各自解析 → 去重扁平合并。</summary>
    Group,
}

/// <summary>审批人规则。RoleId 为 int（OA-D2，对齐 Sys_User.RoleId 实际类型）。</summary>
public record ApproverRule(ApproverStrategy Strategy, int? Levels, int? RoleId, Guid? SpecifiedUserId)
{
    /// <summary>FormField:取审批人的字段名;DataMap:取匹配值的字段名。</summary>
    public string? FieldName { get; init; }
    /// <summary>DataMap:命名映射(Wf_ApproverMap.MapKey)。</summary>
    public string? MapKey { get; init; }
    /// <summary>门控(②a):对表单 vars 求值,假则本规则不产审批人。</summary>
    public string? When { get; init; }
    /// <summary>候选过滤(②a):对每个候选人属性求值,留通过者。</summary>
    public string? Filter { get; init; }
    /// <summary>Group(①):成员规则(扁平,均为叶规则)。</summary>
    public IReadOnlyList<ApproverRule>? Members { get; init; }
}

/// <summary>解析上下文（当前仅需发起人；阶段2 接业务后可扩业务变量）。</summary>
public class ApproverResolveContext
{
    public Guid StarterUserId { get; set; }
    /// <summary>实例表单数据 JSON(②③① 求值/取字段用)。null=无表单上下文(如 Role→CC 解析)。</summary>
    public string? VarsJson { get; set; }
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
