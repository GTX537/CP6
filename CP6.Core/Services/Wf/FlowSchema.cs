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

    /// <summary>节点类型：start / approval / end / parallelSplit / parallelJoin。
    /// parallelSplit=并行分叉(一入 N 出，无条件全激活)；parallelJoin=并行汇聚(N 入一出，等齐放行)。</summary>
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

    // ── 超时（C-4 / 章07 §4）。配齐 TimeoutHours + TimeoutAction 才生效，建待办时算 DueAt ──
    /// <summary>超时小时数（建待办后多久到期）；空=不限时</summary>
    public int? TimeoutHours { get; set; }

    /// <summary>超时动作：remind(软,催办可重复) / approve / reject / escalate(升级给 EscalateTo)</summary>
    public string? TimeoutAction { get; set; }

    /// <summary>escalate 的升级对象 → Sys_User.Id（仅 TimeoutAction=escalate 用）</summary>
    public Guid? EscalateTo { get; set; }

    /// <summary>节点抄送人（进入本节点时抄送，WFS 读模型）。</summary>
    public List<Guid>? CcUsers { get; set; }
    public int? CcRoleId { get; set; }

    /// <summary>画布 X 坐标（设计器布局，引擎忽略）。</summary>
    public double? X { get; set; }
    /// <summary>画布 Y 坐标（设计器布局，引擎忽略）。</summary>
    public double? Y { get; set; }

    /// <summary>状态编号（Delta StateCode / NodeCode，人面业务码；读模型 Wf_FlowFormTo.NodeCode 取此或 Id，引擎执行不依赖）。</summary>
    public string? Code { get; set; }

    /// <summary>串簽档位序列(有序)。空/缺省=单档,用本节点 ApproverStrategy/Countersign(向后兼容)。</summary>
    public List<ApprovalStage>? Stages { get; set; }
}

public class FlowEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;

    /// <summary>流转条件表达式（空 = 无条件直达）。C-3 用 ConditionEvaluator 求值，多条件边按声明序取首个为真</summary>
    public string? Condition { get; set; }

    /// <summary>路径抄送人（token 经此转移时抄送，对齐 Delta 知会人员）。</summary>
    public List<Guid>? CcUsers { get; set; }
}

/// <summary>串簽档型常量。</summary>
public static class ApprovalStageKinds { public const string Fixed = "fixed"; public const string ManagerChain = "managerChain"; }
/// <summary>会签模式常量。</summary>
public static class CountersignModes { public const string All = "all"; public const string Any = "any"; public const string Veto = "veto"; }

/// <summary>串簽档位(设计期)。一个 approval 节点可挂有序 Stages;空=单档(用节点既有字段)。</summary>
public class ApprovalStage
{
    public string? Name { get; set; }
    public string? Code { get; set; }
    /// <summary>fixed=固定一组审批人;managerChain=沿 ManagerId 链逐级展开。见 ApprovalStageKinds。</summary>
    public string Kind { get; set; } = ApprovalStageKinds.Fixed;
    public string? ApproverStrategy { get; set; }     // fixed:DirectManager/DeptLeader/Role/Specified/Starter
    public int? ApproverLevels { get; set; }          // fixed+DirectManager:取第 N 级主管(本档仍 1 运行档)
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string Countersign { get; set; } = CountersignModes.All;
    public int? MaxLevels { get; set; }               // managerChain:逐级展开上限(产 N 运行档)
}
