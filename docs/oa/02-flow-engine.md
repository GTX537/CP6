# 02 · 流程引擎：审批流就是一台状态机（阶段 1）

> 这是整个 OA 最硬核、最值钱的部分。你在 SmartOA 里"拖一根审批线、配一个审批节点"，背后就是本章要造的引擎。学完这章，你能说清"一张单子从提交到结束，引擎每一步在算什么"——这是 OA 工程师和 OA 使用者的分水岭。

## 📍 学习目标

1. 审批流的本质是什么？为什么说它是一台"状态机"？
2. 流程定义（FlowDef）、流程实例（FlowInstance）、待办（FlowTask）三者什么关系？
3. 用户点"同意/驳回"后，引擎是怎么算出下一个节点的？
4. 条件分支（天数>3 才走 HR）怎么实现？
5. 为什么阶段1**只做串行、不碰会签**？

---

## 🔎 核心：审批流 = 节点 + 连线 + 当前位置

把审批流想象成地铁线路图：**节点是站台，连线是轨道，一张单子就是一列车，"当前停在哪站"就是它的状态**。引擎要做的，只有一件事：

> **给定"当前停在哪个节点" + "用户做了什么动作（同意/驳回）"，算出"下一个该停哪个节点"。**

就这么简单。所有审批流引擎，内核都是这台状态机。

### 流程定义（设计器的产出，存 `FlowDef.SchemaJson`）

```json
{
  "flowKey": "leave_flow",
  "formKey": "leave_apply",
  "nodes": [
    { "id": "start", "type": "start" },
    { "id": "n1", "type": "approval", "name": "直属上级审批",
      "assignee": { "type": "leader" } },
    { "id": "n2", "type": "approval", "name": "HR审批",
      "assignee": { "type": "role", "value": "HR" } },
    { "id": "end", "type": "end" }
  ],
  "edges": [
    { "from": "start", "to": "n1" },
    { "from": "n1", "to": "n2",  "when": "approve", "condition": "days > 3" },
    { "from": "n1", "to": "end", "when": "approve", "condition": "days <= 3" },
    { "from": "n1", "to": "start", "when": "reject" }
  ]
}
```

读法：从 `start` 到 `n1`；在 `n1` 点同意，若 `days>3` 去 `n2`，否则直接 `end`；点驳回退回 `start`（发起人）。**这段 JSON 就是你拖拽连线的全部成果。**

---

## 🔎 三张表的关系（务必分清"定义"和"实例"）

这是新手最容易混的地方。用"菜谱 vs 做菜"来类比：

| 概念 | 类比 | 是什么 | 例子 |
|---|---|---|---|
| **FlowDef** | 菜谱 | 流程**模板**，配一次用很多次 | "请假审批流程" |
| **FlowInstance** | 正在做的一道菜 | 某人发起的**一张具体单子** | "张三 6/9 的请假单，现停在 n1" |
| **FlowTask** | 待办便签 | 实例在某节点产生的**待某人处理的任务** | "李经理要审张三这张单" |
| **FlowHistory** | 已完成记录 | 谁在哪个节点做了什么 | "李经理 6/9 10:00 同意，意见：准" |

```csharp
// CP6.Entity/DomainModels/Oa/FlowInstance.cs（阶段1要建）
[Table("T_Oa_FlowInstance")]
public class FlowInstance : BaseEntity
{
    public string FlowKey      { get; set; } = "";  // 用哪个流程定义
    public string BizId        { get; set; } = "";  // 关联的表单数据
    public string CurrentNode  { get; set; } = "";  // ★当前停在哪个节点——状态机的"状态"
    public string Status       { get; set; } = "running"; // running/approved/rejected/canceled
    public string StarterId    { get; set; } = "";  // 发起人
    public string VariablesJson{ get; set; } = "{}"; // 流程变量（含表单关键值，给条件判断用）
}

// CP6.Entity/DomainModels/Oa/FlowTask.cs
[Table("T_Oa_FlowTask")]
public class FlowTask : BaseEntity
{
    public Guid   InstanceId { get; set; }
    public string NodeId     { get; set; } = "";
    public string AssigneeId { get; set; } = "";  // 该谁办
    public string State      { get; set; } = "pending"; // pending/done
    public string? Action    { get; set; }        // approve/reject（办完后填）
    public string? Comment   { get; set; }        // 审批意见
}
```

**`CurrentNode` 就是状态机的"状态"**。引擎每次流转，就是改这个字段 + 关旧待办 + 开新待办 + 记历史。

---

## 🔎 引擎的心脏：`AdvanceAsync`（流转一步）

整个流程引擎，核心就这一个方法。把它读懂，你就懂了审批流：

```csharp
// CP6.Core/Services/Oa/FlowEngine.cs（阶段1的核心）
public async Task AdvanceAsync(Guid instanceId, string action, string comment, string operatorId)
{
    var inst = await _db.FlowInstances.FindAsync(instanceId);
    var def  = await LoadFlowDef(inst.FlowKey);          // 取流程定义 JSON
    var task = await GetPendingTask(instanceId, operatorId); // 当前人的待办

    // 1) 办结当前待办，记历史
    task.State = "done"; task.Action = action; task.Comment = comment;
    await WriteHistory(inst, inst.CurrentNode, action, comment, operatorId);

    // 2) 找从当前节点出发、动作匹配、条件成立的那条边
    var vars = JsonToDict(inst.VariablesJson);
    var edge = def.Edges.FirstOrDefault(e =>
        e.From == inst.CurrentNode &&
        e.When == action &&
        EvalCondition(e.Condition, vars));   // 条件表达式求值，见下

    if (edge == null) throw new InvalidOperationException("没有匹配的流转路径");

    // 3) 移动到下一节点
    inst.CurrentNode = edge.To;
    var nextNode = def.Nodes.First(n => n.Id == edge.To);

    if (nextNode.Type == "end")
    {
        inst.Status = "approved";            // 到终点 → 审批完成
        await OnApproved(inst);              // ★这里触发回写业务（见第09章 BridgeHook）
    }
    else if (nextNode.Type == "start")
    {
        inst.Status = "rejected";            // 退回发起人（简化处理）
    }
    else
    {
        // 4) 在新节点给"该办的人"开待办
        var assignees = await ResolveAssignees(nextNode.Assignee, inst); // ★审批人解析，见第04章
        foreach (var uid in assignees)
            _db.FlowTasks.Add(new FlowTask { InstanceId = inst.Id, NodeId = nextNode.Id, AssigneeId = uid });
    }
    await _db.SaveChangesAsync();
}
```

把这段读三遍。**审批流的全部神秘感，到这里就消失了**——它就是"关旧待办 → 按边找下家 → 改当前节点 → 开新待办"。

两个被它调用、但属于别的引擎的能力，本章先打桩、后续章节展开：
- `ResolveAssignees(...)`：把"直属上级"/"HR角色"算成具体的人 → [第04章 组织引擎](./04-org-engine.md)。阶段1可以先硬编码一个固定审批人把流程跑通。
- `EvalCondition(...)`：求值 `days > 3` 这种表达式 → [第05章 规则引擎](./05-rule-engine.md)。阶段1可以先只支持最简单的 `字段 比较符 数字`。

---

## 🔎 条件分支怎么做：表达式求值

`"condition": "days > 3"` 怎么从字符串变成 true/false？这是新手第二个卡点。三种实现，从易到难：

1. **极简自研**（阶段1够用）：约定只支持 `字段 op 值`，split 字符串自己比。够跑通。
2. **表达式引擎**（推荐进阶）：用 `DynamicExpresso` / `NCalc`（.NET 库），`new Interpreter().Eval("days > 3", vars)` 直接出结果，支持复杂表达式。
3. **规则 DSL**：自定义一套 JSON 规则（`{field:"days", op:">", value:3}`），可视化设计器友好。生产级常走这条。

```csharp
bool EvalCondition(string? expr, Dictionary<string, object> vars)
{
    if (string.IsNullOrEmpty(expr)) return true;       // 无条件 = 永远通过
    // 阶段1极简版：仅支持 "field op number"
    // 进阶：return new Interpreter().SetVariables(vars).Eval<bool>(expr);
    ...
}
```

`vars`（流程变量）从哪来？发起时把表单里参与判断的字段（如 `days`）灌进 `VariablesJson`。**条件判断的是流程变量，不是直接读表单表**——这样引擎和表单存储解耦。

---

## 💡 资深视角

**为什么阶段1坚决不碰会签？**
会签（多人都要同意才过）会把状态机从"一个 CurrentNode"变成"一个节点上多个并行任务 + 计票逻辑"，复杂度陡增（[第06章](./06-advanced-flow.md)专门讲）。阶段1的目标是**先理解状态机骨架**：一个节点一个待办、一进一出。骨架稳了，会签只是在节点上加"多实例 + 计数"。先跑通串行，能从头审到尾，比一上来做全功能重要得多。

**为什么流转要"事件化 / 可补偿"？**
`OnApproved` 里要回写业务（扣库存、改订单状态…），这一步可能失败。如果审批已完成、但回写失败，单子状态就和业务数据不一致。CP6 已有 `IntegrationEvent` + 重试 + 死信（Phase 6），**把"审批通过后的业务动作"做成集成事件**，失败能自动重试、进死信人工补偿。这是 CP6 给 OA 的最大便宜，[第09章](./09-cp6-integration.md)详述。

**状态机 vs BPMN 引擎（Flowable/Camunda/Elsa）**
成熟引擎（Flowable、Camunda、.NET 的 Elsa）实现的是 BPMN 标准，支持并行网关、子流程、定时边界事件等。**自研一个够用的状态机** vs **接一个 BPMN 引擎**，是真实的取舍：自研可控、贴合业务、好商用化；接引擎功能全但重、学习曲线陡、定制受限。学习阶段强烈建议自研（你才能真懂），商用到复杂场景再评估是否引入 Elsa。

---

## ⚠️ 踩坑记录

1. **把定义和实例混在一张表**：改了流程模板，结果跑到一半的旧单子全乱。**FlowDef 要版本化，实例记住自己用的哪一版**，改模板不影响在途单子。
2. **驳回不清理待办**：退回时忘了把当前及后续节点的 `pending` 待办关掉，导致一张单子出现多个活动待办。流转时**先关旧待办再开新的**。
3. **条件互斥没保证**：`n1` 同意后两条出边 `days>3` 和 `days<=3` 必须互斥且全覆盖，否则要么找不到下家、要么找到俩。配置时要校验。
4. **审批人算空了**：`ResolveAssignees` 返回空（如该员工没配上级），单子卡死无人能办。引擎要有兜底（转管理员/报错），别静默卡住。
5. **流程变量不更新**：单子在审批中被改了关键字段，但 `VariablesJson` 没同步，后面条件判断用的还是旧值。明确"哪些字段进流程变量、何时刷新"。

---

## 🧪 自检题

1. 为什么说审批流是状态机？它的"状态"具体存在哪个字段？
2. FlowDef / FlowInstance / FlowTask 三者用菜谱类比分别是什么？
3. 用户点"同意"后，`AdvanceAsync` 依次做了哪几步？
4. `days > 3` 这个条件字符串，引擎有哪几种方式把它变成 true/false？
5. 为什么"审批通过后的业务动作"要做成集成事件、而不是同步直接写库？

---

## 🔗 延伸阅读 / 动手清单

**读源码：**
- `Workflow Core`（.NET）—— 看它的 `StepBody` 和实例持久化，对照本章的状态机。
- `Elsa Workflows` —— 看一个完整 BPMN 风格引擎长什么样（看完更确信自研够用）。

**阶段 1 动手清单（做完即过关）：**
- [ ] 建表 `T_Oa_FlowDef` / `T_Oa_FlowInstance` / `T_Oa_FlowTask` / `T_Oa_FlowHistory`
- [ ] 写 `FlowEngine.StartAsync`（发起：建实例、灌流程变量、在首节点开待办）
- [ ] 写 `FlowEngine.AdvanceAsync`（流转：本章核心方法）
- [ ] 审批人先**硬编码**一个固定 userId，把流程从头跑到尾（组织引擎留到第04章）
- [ ] 条件求值先做**极简版**（仅 `field op number`）
- [ ] 用第01章的请假表单 + 本章的请假流程，手工 STtart → approve → approve → end 跑通一张单

**下一章** → [03. 表单 × 流程：字段级权限 + 待办中心](./03-form-flow-binding.md)，进入阶段2，把表单和流程合体成一个**能用的 OA**。
