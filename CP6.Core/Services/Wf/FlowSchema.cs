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

    // ── 高级审批人策略(②③①)。缺省 null=不启用,走原扁平 4 字段路径(向后兼容) ──
    public string? ApproverFieldName { get; set; }   // FormField/DataMap:字段名
    public string? ApproverMapKey { get; set; }      // DataMap:映射键
    public string? ApproverWhen { get; set; }        // ②a 门控
    public string? ApproverFilter { get; set; }      // ②a 候选过滤
    public List<ApproverSpec>? ApproverMembers { get; set; }   // Group 成员

    // ── 服务任务节点(§2.1,全可空向后兼容,null=非服务任务节点) ──
    public string? ServiceKind { get; set; }            // "dataWriteback" | "webApi" | "timer"
    public string? ServiceMode { get; set; }            // "sync" | "async"

    // 动作绑定
    public string? ServiceActionName { get; set; }      // dataWriteback / timer 动作:注册执行器键
    public string? ServiceConnectorName { get; set; }   // webApi / timer 动作:注册连接器键
    public string? ServicePath { get; set; }            // webApi:相对 baseURL 的路径模板
    public string? ServiceParamsJson { get; set; }      // 参数模板:JSON,键→表达式(§3.6 语法)

    // webApi 节点级 HTTP 覆盖(E-T1,POCO 零迁移;null=用连接器默认)。经 ActionRefJson 固化快照承载→ DbWfConnector 读取。
    public string? ServiceHttpMethod { get; set; }      // "GET"|"POST"|"PUT"|"DELETE":覆盖连接器默认 method(静态 E-WF-028 校值域)
    public int? ServiceTimeoutSec { get; set; }         // 单次调用超时秒:覆盖连接器 TimeoutSec(静态值域>0&&<=3600;保存侧<租约)

    // timer 专属
    public string? ServiceDelayMode { get; set; }       // "duration" | "untilDate" | "untilExpr"
    public string? ServiceDelayValue { get; set; }      // "3d"/"PT2H" | "2026-07-01" | 表达式

    // 重试策略(设计器口径:重试次数;映射到 job.MaxAttempts = retries + 1)
    public int? ServiceMaxRetries { get; set; }         // 默认 3(= 首次后再重试 3 次)
    public int? ServiceRetryBackoffSec { get; set; }    // 默认 30,指数退避基数

    // ── 内核 hardening（spec §2.1，可空向后兼容） ──
    /// <summary>分支驳回策略（仅 parallelSplit/inclusiveSplit 有意义）：null/"cascade"=连坐（现状）；"prune"=剪枝。</summary>
    public string? OnBranchReject { get; set; }

    // ── 子流程节点(子流程 spec §2.1,全可空向后兼容,null=非 subFlow 节点) ──
    /// <summary>子流程引用（Type="subFlow" 专用）：目标已发布流程的 FlowKey。</summary>
    public string? SubFlowKey { get; set; }
    /// <summary>父→子变量映射 JSON：{"子var":"$.父var路径"}（ServiceVarsHelper 点路径口径，含其已记档限制）。null=不传。</summary>
    public string? SubVarsInJson { get; set; }
    /// <summary>子终态→父回注映射 JSON：{"父var":"$.子var路径"}。null=不回注。多实例时回注值为数组（按子实例序号）。</summary>
    public string? SubVarsOutJson { get; set; }
    /// <summary>多实例集合变量名（父 vars 中的 JSON 数组）。null=单实例。每个元素注入对应子实例 vars 的 "item" 键（含 "itemIndex"）。</summary>
    public string? SubCollectionVar { get; set; }
    /// <summary>完成策略："all"(默认,全部 Approved 才过) | "any"(首个 Approved 即过,级联撤回其余)。</summary>
    public string? SubCompletionPolicy { get; set; }
}

public class FlowEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;

    /// <summary>流转条件表达式（空 = 无条件直达）。C-3 用 ConditionEvaluator 求值，多条件边按声明序取首个为真</summary>
    public string? Condition { get; set; }

    /// <summary>路径抄送人（token 经此转移时抄送，对齐 Delta 知会人员）。</summary>
    public List<Guid>? CcUsers { get; set; }

    /// <summary>失败出边标记(§2.2)。true=仅服务任务重试耗尽时走;普通 AdvanceToken 跳过它(null!=true 等价)。</summary>
    public bool? IsError { get; set; }
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

    // ── 高级审批人策略(②③①)。缺省 null=不启用,走原扁平 4 字段路径(向后兼容) ──
    public string? ApproverFieldName { get; set; }   // FormField/DataMap:字段名
    public string? ApproverMapKey { get; set; }      // DataMap:映射键
    public string? ApproverWhen { get; set; }        // ②a 门控
    public string? ApproverFilter { get; set; }      // ②a 候选过滤
    public List<ApproverSpec>? ApproverMembers { get; set; }   // Group 成员
}

/// <summary>审批人设计期叶规则(Group 成员用)。对齐 ApproverRule 叶子部分,无 Members(扁平合并)。</summary>
public class ApproverSpec
{
    public string? Strategy { get; set; }
    public int? ApproverLevels { get; set; }
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string? FieldName { get; set; }
    public string? MapKey { get; set; }
    public string? When { get; set; }
    public string? Filter { get; set; }
}
