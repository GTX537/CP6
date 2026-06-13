# 03 · 流程引擎运行时：审批流就是一台状态机

> **阶段 1 · 最硬核的一章。** 本章做"流程解释器"：读一段 flow schema（JSON），把一张单子从提交一路驱动到结束——建实例、解析审批人、建待办、处理同意/驳回、流转下一节点、记审批痕迹。本章结束时，一张请假单能从头审到尾，串行 + 条件分支 + 会签三规则都跑得通。
>
> 上游：[01 组织模型](./01-org-model.md)（算审批人）、[02 表单引擎](./02-form-runtime.md)（单据数据）。下游：[04 绑定](./04-form-flow-binding.md)（待办中心/字段权限）、[07 高级流程](./07-advanced-flow.md)（退回/加签/超时/委派）。

---

## 一、题眼：当前节点 + 动作 → 下一节点

审批流没有魔法，它就是一台**状态机**：

> **实例停在某个"当前节点"，等一个"动作"（同意/驳回）。动作来了，引擎算出"下一节点"，把实例推过去，建新待办。如此循环，直到走到 end。**

钉钉、泛微、Flowable——全是这一个循环。所以"做流程引擎" = **实现这台状态机的一次 tick**：`(当前状态, 输入动作) → 新状态 + 副作用(建待办/记历史)`。把这一个 tick 写对、写成幂等可重放，流程引擎就立住了。

---

## 二、flow schema 长什么样

一条审批流 = 节点（nodes）+ 连线（edges）。节点上挂"审批人规则"和"会签规则"，连线上可挂"条件"：

```jsonc
{
  "flowKey": "leave",
  "formKey": "leave",                 // 绑定哪张表单（02章）
  "nodes": [
    { "id": "start", "type": "start" },
    { "id": "n1", "type": "approval", "name": "直属上级",
      "approver": { "type": "directManager", "levels": 1 },
      "countersign": "all" },
    { "id": "n2", "type": "approval", "name": "部门长",
      "approver": { "type": "deptLeader" },
      "countersign": "any" },
    { "id": "end", "type": "end" }
  ],
  "edges": [
    { "from": "start", "to": "n1" },
    { "from": "n1", "to": "n2", "condition": "days > 3" },   // 仅请假>3天才走部门长
    { "from": "n1", "to": "end", "condition": "days <= 3" }, // ≤3天直属批完即结束
    { "from": "n2", "to": "end" }
  ]
}
```

- `approver`：审批人规则，**原样交给 [01 章的 `ApproverResolver`](./01-org-model.md)** 算出具体人。
- `countersign`：节点内多人时的通过规则（见第五节）。
- `condition`：连线条件，读表单数据（`days`）决定走哪条分支——这就是"条件流转/多层"。

---

## 三、数据模型：实例 / 待办 / 痕迹

```csharp
// CP6.Entity/DomainModels/Wf/FlowDef.cs —— 流程定义
[Table("Wf_FlowDef")]
public class FlowDef : BaseEntity
{
    public string FlowKey { get; set; } = "";
    public string Name    { get; set; } = "";
    public string FormKey { get; set; } = "";   // 绑定的表单
    public string SchemaJson { get; set; } = "";// ★整段 flow schema
    public int    Version { get; set; } = 1;
}

// 运行中的一张单
[Table("Wf_FlowInstance")]
public class FlowInstance : BaseEntity
{
    public string FlowKey     { get; set; } = "";
    public string BizType     { get; set; } = "";   // ERP单据类型（PR/PO/付款）；OA原生为空
    public Guid   BizId       { get; set; }         // 关联业务/表单数据
    public string CurrentNode { get; set; } = "";   // ★当前停在哪个节点
    public int    Status      { get; set; }         // 0进行中/1通过/2驳回/3撤回
    public string VarsJson    { get; set; } = "";   // 流程变量（含表单快照，供 condition 求值）
    public Guid   StarterId   { get; set; }         // 发起人
}

// 待办任务（实例 × 节点 × 审批人）
[Table("Wf_FlowTask")]
public class FlowTask : BaseEntity
{
    public Guid   InstanceId { get; set; }
    public string NodeId     { get; set; } = "";
    public Guid   AssigneeId { get; set; }          // 审批人（一节点多人=多条）
    public int    Status     { get; set; }          // 0待办/1同意/2驳回
    public string Countersign{ get; set; } = "all"; // 该节点的会签规则
    public string? Comment   { get; set; }
}

// 审批痕迹（谁、在哪节点、什么动作、何时）
[Table("Wf_FlowHistory")]
public class FlowHistory : BaseEntity
{
    public Guid   InstanceId { get; set; }
    public string NodeId     { get; set; } = "";
    public Guid   ActorId    { get; set; }
    public string Action     { get; set; } = "";    // submit/approve/reject/…
    public string? Comment   { get; set; }
}
```

> **`FlowInstance.CurrentNode` 是状态机的状态字段**；`FlowTask` 是"当前要谁干活"；`FlowHistory` 是只追加的流水（永不改、永不删，审计靠它）。三张表各司一职：状态、待办、痕迹。

---

## 四、一次 tick：提交与流转

```csharp
// CP6.Core/Services/Wf/FlowEngine.cs（核心逻辑，省略持久化细节）
public async Task SubmitAsync(string flowKey, string bizType, Guid bizId, Guid starter, object formData)
{
    var def = await LoadDef(flowKey);
    var inst = new FlowInstance { FlowKey = flowKey, BizType = bizType, BizId = bizId,
                                  StarterId = starter, VarsJson = Json(formData), Status = 0 };
    await EnterNode(inst, def, FirstNodeAfter(def, "start"));   // 进第一个审批节点
}

// 审批人动作（同意/驳回）
public async Task ActAsync(Guid taskId, Guid actor, bool approve, string? comment)
{
    var task = await _db.FlowTasks.FindAsync(taskId);
    if (task.Status != 0) return;                  // 幂等：已办的任务再点无效
    task.Status = approve ? 1 : 2; task.Comment = comment;
    await AddHistory(task.InstanceId, task.NodeId, actor, approve ? "approve" : "reject", comment);

    var inst = await _db.FlowInstances.FindAsync(task.InstanceId);
    var (decided, passed) = EvaluateNode(task.InstanceId, task.NodeId, task.Countersign);
    if (!decided) return;                          // 会签未到结论，继续等其他人

    if (!passed) { inst.Status = 2; return; }      // 节点被否 → 整单驳回
    await EnterNode(inst, await LoadDef(inst.FlowKey), NextNode(inst, task.NodeId));  // 流转下一节点
}

// 进入一个节点：算审批人 → 建待办（终点则收尾）
private async Task EnterNode(FlowInstance inst, FlowDef def, FlowNode node)
{
    if (node.Type == "end") { inst.Status = 1; return; }       // 走到 end → 通过

    inst.CurrentNode = node.Id;
    var rule = ParseApproverRule(node);
    var result = await _approverResolver.ResolveAsync(rule,    // ★调用 01 章解析器
                       new ApproverResolveContext { StarterUserId = inst.StarterId });
    if (!result.Resolved) { MarkPendingManualAssign(inst, result.UnresolvedReason); return; } // 缺位挂起

    foreach (var uid in result.ApproverIds)        // 一节点多人 → 多条待办
        _db.FlowTasks.Add(new FlowTask { InstanceId = inst.Id, NodeId = node.Id,
                                         AssigneeId = uid, Countersign = node.Countersign });
}
```

**注意三个接缝**：
1. `EnterNode` 调 **01 章的 `ApproverResolver`** 算人——解析逻辑不在流程引擎里，单一职责。
2. 解析 `Unresolved` 时**挂起等管理员指派**，不崩溃（[01 章](./01-org-model.md)的兜底设计在此发挥）。
3. `ActAsync` 开头的 `if (task.Status != 0) return` 是**幂等闸门**——重复提交、并发点击、消息重放都安全。

---

## 五、会签三规则：节点何时算"有结论"

一个节点多个审批人时，`EvaluateNode` 按 `countersign` 判定：

```csharp
private (bool decided, bool passed) EvaluateNode(Guid instId, string nodeId, string rule)
{
    var tasks = _db.FlowTasks.Where(t => t.InstanceId == instId && t.NodeId == nodeId).ToList();
    int approved = tasks.Count(t => t.Status == 1);
    int rejected = tasks.Count(t => t.Status == 2);
    int total    = tasks.Count;

    return rule switch
    {
        // 会签：全体同意才过；任一驳回立即否
        "all"  => rejected > 0 ? (true, false)
                 : approved == total ? (true, true) : (false, false),

        // 或签：任一同意即过；全部驳回才否
        "any"  => approved > 0 ? (true, true)
                 : rejected == total ? (true, false) : (false, false),

        // 一票否决：任一反对立即终止；否则需全体表态通过
        "veto" => rejected > 0 ? (true, false)
                 : approved == total ? (true, true) : (false, false),

        _ => (false, false)
    };
}
```

| 规则 | 通过条件 | 否决条件 | 典型场景 |
|---|---|---|---|
| `all` 会签 | 全体同意 | 任一驳回 | 重大支出多人会签 |
| `any` 或签 | 任一同意 | 全部驳回 | 多个值班经理任一批即可 |
| `veto` 一票否决 | 全体表态且无人反对 | 任一反对 | 风控/合规一票否决 |

> `all` 和 `veto` 在"任一驳回即否"上一致，差别在"是否要等全部人表态"——`veto` 强调"只要有人反对就立刻死"，语义上更强。三种规则在数据上只是对 `approved/rejected/total` 三个计数的不同判定，**没有额外状态**，这是把会签做简单的关键。

---

## 六、条件流转：读表单数据选分支

`NextNode` 沿 `edges` 找出边，按 `condition` 对 `VarsJson`（含表单快照）求值，选中第一条满足的：

```csharp
private FlowNode NextNode(FlowInstance inst, string fromNode)
{
    var vars = Json.Parse(inst.VarsJson);
    foreach (var edge in EdgesFrom(fromNode))
        if (edge.Condition == null || EvalCondition(edge.Condition, vars))  // 如 "days > 3"
            return NodeById(edge.To);
    throw new InvalidOperationException($"节点 {fromNode} 无满足条件的出边");
}
```

"请假 ≤3 天直属批完即结束、>3 天再走部门长"就是两条带 `condition` 的出边。**多层审批 = 多个串行节点；条件分支 = 带条件的出边**——两者用同一套 edges 表达，引擎不需要为"多层"另写逻辑。

> `EvalCondition` 用一个安全的小表达式求值器（白名单字段 + 比较/逻辑运算），**不要直接 eval 任意代码**——否则 schema 成了注入入口。

---

## 七、资深视角

**为什么状态全落库、而不是用工作流框架的内存引擎？** 审批可能停几天，进程会重启。状态机的"当前节点 + 待办 + 痕迹"必须落库，任何一次 tick 都是"读库→判定→写库"。这也让调试可观测——卡哪了，查 `FlowInstance.CurrentNode` 和 `FlowTask` 即知。与总纲"同步可调试、依赖单向"一脉相承。

**幂等为什么是生命线？** 网络重试、用户双击、消息重放都会让同一个动作来两次。靠 `task.Status != 0` 这道闸把重复动作挡掉，比事后对账便宜得多。

**要不要上 Elsa/Workflow Core 这类框架？** 学习期**自己写这台状态机**最能理解原理（就上面这点代码）。生产要 BPMN 全套（子流程、定时器、补偿）再考虑框架——但 90% 的企业审批，这套手写状态机足够，且完全可控可调试。

**schema 版本化**：和表单一样，`FlowDef.Version` 让进行中的实例按**发起时**那版流程跑完，定义改版不影响在途单据。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| .NET 流程引擎（匹配 CP6） | **Elsa Workflows / Workflow Core** | 节点定义、实例持久化、状态驱动 |
| 会签/或签/条件网关 | **Flowable / Camunda（BPMN）** | parallel/inclusive gateway、多实例任务 |
| 状态机本质 | **Stateless（.NET 状态机库）** | (state, trigger) → state 的最小模型 |

> Flowable 的"多实例任务 + 完成条件"就是本章的会签三规则；它的 exclusive gateway 就是本章带 `condition` 的出边——核心模型一致。

---

## 九、阶段1（流程部分）自检

- [ ] 用一句话说清"一次 tick"在算什么？（当前节点+动作→下一节点+建待办/记历史）
- [ ] `FlowInstance`/`FlowTask`/`FlowHistory` 三张表各管什么？
- [ ] 会签 `all`/`any`/`veto` 在 `approved/rejected/total` 上分别怎么判定？
- [ ] "多层审批"和"条件分支"为什么用同一套 edges 就能表达？
- [ ] 幂等闸门是哪一行？不要它会怎样？
- [ ] 审批人算不出来时，引擎怎么不崩？（挂起等指派，复用 01 章兜底）

全部能答 → 流程解释器立住了。表单（02）+ 流程（03）两台引擎齐活，[04 章](./04-form-flow-binding.md)把它们合体：节点字段权限 + 待办中心 + 我的申请 + 审批痕迹，一个手配 JSON 的可用 OA 就成型了。

---

*高级动作（退回到指定节点/加签/超时/委派）见 [07 章](./07-advanced-flow.md)。配套教学见 [docs/oa/02](../oa/02-flow-engine.md)。实现落 `CP6.Core/Services/Wf/FlowEngine.cs`。*
