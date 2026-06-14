namespace CP6.Core.Services.Wf;

/// <summary>
/// 流程 schema（Wf_FlowDef.SchemaJson 反序列化目标，OA 章03 §3）。节点 + 边的有向图。
/// 流程引擎（C-3）按此驱动状态机：EnterNode 算审批人建任务、EvaluateNode 会签判定、NextNode 沿边条件流转。
/// </summary>
public class FlowSchema
{
    /// <summary>起始节点 Id（空则取 Nodes 首个）</summary>
    public string? Start { get; set; }

    public List<FlowNode> Nodes { get; set; } = new();
    public List<FlowEdge> Edges { get; set; } = new();
}

public class FlowNode
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>节点类型：start / approval / end</summary>
    public string Type { get; set; } = "approval";

    // ── 审批人规则（C-3 映射为 ApproverRule；start/end 节点可空）──
    /// <summary>DirectManager / DeptLeader / Role / Specified / Starter</summary>
    public string? ApproverStrategy { get; set; }
    public int? ApproverLevels { get; set; }
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }

    /// <summary>会签规则：all(全同意才过/任一驳回即否) / any(任一同意即过) / veto(任一反对即死)。默认 all</summary>
    public string Countersign { get; set; } = "all";

    /// <summary>节点字段权限（D-1）：字段名 → edit | readonly | hidden</summary>
    public Dictionary<string, string>? FieldPerms { get; set; }
}

public class FlowEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;

    /// <summary>流转条件表达式（空 = 无条件直达）。C-3 用 ConditionEvaluator 求值，多条件边按声明序取首个为真</summary>
    public string? Condition { get; set; }
}
