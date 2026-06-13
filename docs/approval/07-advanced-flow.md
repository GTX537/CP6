# 07 · 高级流程：退回 / 加签 / 超时 / 委派

> **阶段 3 · 全书最脏的一章。** 03 章的流程是"直着往前走"。真实审批从不直走：被退回去重填、临时拉个人来会签、卡太久要催/自动处理、审批人休假要委托他人。这四个动作总纲点名全要。难点都不在"动作本身"，而在**动作之后的状态清理**——退回要清掉哪些待办、加签怎么不破坏原流程、超时扫描怎么幂等。本章把这些脏活做干净。
>
> 上游：[03 流程引擎](./03-flow-runtime.md)（FlowInstance/Task/History、会签计票）、[01 组织模型](./01-org-model.md)（委派要算人）。

---

## 一、题眼：难的不是动作，是动作后的状态清理

正常流转只在节点间**前进**。高级动作会**回退、插入、跳过、替换**——一旦打破线性，就会留下一堆"不一致的中间状态"：退回后，原先建好的待办还在；加签后，节点序列变了；超时自动通过后，待办还显示"待办"。

> **每个高级动作 = 一次状态变更 + 一次状态清理。** 写对清理（删哪些 `FlowTask`、清哪些会签计票、保留哪些 `FlowHistory`），动作才不会留下"幽灵待办"。本章每一节都先讲动作、再讲清理。

一条铁律先立在这：**`FlowHistory` 永远只追加，绝不因为退回/撤销而删。** 状态可以回退，历史不能。

---

## 二、退回：回到上一步 / 指定节点 / 发起人

退回不是"驳回"。驳回是终态（整单结束），退回是**把实例拉回某个更早的节点重走**。

```csharp
// CP6.Core/Services/Wf/AdvancedFlow.cs
public async Task SendBackAsync(Guid taskId, Guid actor, string targetNodeId, string comment)
{
    var task = await _db.FlowTasks.FindAsync(taskId);
    var inst = await _db.FlowInstances.FindAsync(task.InstanceId);
    await AddHistory(inst.Id, task.NodeId, actor, "sendback", comment);   // 痕迹保留

    // ★清理：作废"目标节点之后（含当前节点）"所有未完成的待办
    var stale = _db.FlowTasks.Where(t => t.InstanceId == inst.Id && t.Status == 0
                                      && IsAfterOrAt(inst, targetNodeId, t.NodeId));
    foreach (var t in stale) t.Status = 3;        // 3=作废（不是同意也不是驳回）

    inst.CurrentNode = targetNodeId;
    await EnterNode(inst, await LoadDef(inst.FlowKey), NodeById(targetNodeId)); // 重新建待办
}
```

三种落点只是 `targetNodeId` 不同：
- **退回上一步**：`targetNodeId` = 上一个审批节点（沿痕迹找前一节点）。
- **退回指定节点**：`targetNodeId` = 流程图里任选一个已过节点。
- **退回发起人**：`targetNodeId` = start 后第一节点，且把单据置回"可重新提交"（发起人改完再提，重走流程）。

**清理要点**：必须作废"目标之后"的全部在途待办（用 `Status=3` 作废，而非删除——可追溯），并**清零这些节点的会签计票**（计票就是数 `FlowTask` 状态，作废后自然不计）。退回再前进时，这些节点会重新建待办、重新计票。

> **退回是最难的动作**，因为"目标之后有哪些节点/待办"在有分支、会签时不直观。物化一个"已走过的节点序列"（存 `FlowInstance.VarsJson` 或单独的 path）能让"谁在目标之后"一目了然。

---

## 三、加签：前加签 / 后加签

加签 = 运行时**临时拉一个人进来审**，流程图（`FlowDef`）里本来没有他。

- **后加签**：当前审批人审完，**在他之后**再加一个人审，通过了才继续往下。
- **前加签**：当前审批人先不审，**先让被加签人审**，被加签人审完再回到当前审批人。

关键设计：**加签不改 `FlowDef`（静态定义不动），只在 `FlowInstance` 上加动态待办**。

```csharp
public async Task AddSignAsync(Guid taskId, Guid actor, Guid extraUserId, bool before, string comment)
{
    var task = await _db.FlowTasks.FindAsync(taskId);
    await AddHistory(task.InstanceId, task.NodeId, actor, before ? "addsign_before" : "addsign_after", comment);

    var extra = new FlowTask {
        InstanceId = task.InstanceId, NodeId = task.NodeId, AssigneeId = extraUserId,
        Status = 0, Countersign = task.Countersign,
        AddSignSource = before ? "before" : "after"     // ★标记来源，区别于原生待办
    };
    _db.FlowTasks.Add(extra);

    if (before) task.Status = 4;   // 4=挂起：原审批人等被加签人审完再激活
}
```

引擎判定节点"是否有结论"时，要**把加签待办算进去**：前加签的人没审完，原审批人不激活；后加签的人没审完，节点不流转。即 [03 章](./03-flow-runtime.md) `EvaluateNode` 的 `total` 要包含 `AddSignSource` 的临时待办。

> 加签为什么不改 `FlowDef`？因为定义是**所有实例共享**的模板，改它会污染别的单子。加签是**这一张单**的临时行为，只能加在**这个实例**的待办上。这是"定义 vs 实例"分离的又一处体现。

---

## 四、超时：催办 / 自动通过 / 自动驳回 / 升级

给待办设一个"到期时间"，一个定时任务周期性扫描，按节点配置处理卡太久的待办。

```csharp
// 节点 schema 上配超时策略
// "timeout": { "hours": 24, "action": "remind" | "approve" | "reject" | "escalate" }

// 后台定时扫描（HostedService / 复用 Phase6 worker 风格）
public async Task ScanTimeoutsAsync()
{
    var now = DateTime.Now;
    var overdue = await _db.FlowTasks
        .Where(t => t.Status == 0 && t.DueAt != null && t.DueAt < now && !t.TimeoutHandled)
        .ToListAsync();

    foreach (var t in overdue)
    {
        switch (TimeoutActionOf(t)) {
            case "remind":   await _notifier.RemindAsync(t); break;            // 催办，不改状态
            case "approve":  await _engine.ActAsync(t.Id, SystemUser, true, "超时自动通过"); break;
            case "reject":   await _engine.ActAsync(t.Id, SystemUser, false, "超时自动驳回"); break;
            case "escalate": await ReassignToManagerAsync(t); break;           // 升级给上级
        }
        t.TimeoutHandled = true;     // ★幂等：处理过的不再处理（catch/重启都安全）
    }
}
```

**幂等是超时扫描的生命线**：扫描任务可能重叠运行、进程可能重启。靠 `TimeoutHandled` 标记 + `ActAsync` 自身的幂等闸门（[03 章](./03-flow-runtime.md) `task.Status != 0`），同一个超时绝不处理两遍（不会自动通过两次）。

> `remind` 是软动作（只发通知，待办仍在，可重复催，按间隔再设下次 `DueAt`）；`approve`/`reject`/`escalate` 是硬动作（改状态，一次性）。两类的幂等处理不同：软的允许重复触发、硬的必须只一次。

---

## 五、委派 / 代理：休假期间转交待办

```csharp
// CP6.Entity/DomainModels/Wf/FlowDelegate.cs（总纲数据模型）
[Table("Wf_FlowDelegate")]
public class FlowDelegate : BaseEntity
{
    public Guid     GrantorId  { get; set; }   // 授权人（休假者）
    public Guid     DelegateId { get; set; }   // 代理人
    public DateTime ValidFrom  { get; set; }
    public DateTime ValidTo    { get; set; }
    public string?  Scope      { get; set; }   // 可选：限定某些流程类型
}
```

两种落地时机，选**建待办时替换**（最简单、最干净）：

```csharp
// EnterNode 建 FlowTask 时，对每个 assignee 查是否在委派期
private Guid ResolveActualAssignee(Guid assignee, string flowKey, DateTime now)
{
    var d = _db.FlowDelegates.FirstOrDefault(x => x.GrantorId == assignee
              && x.ValidFrom <= now && now <= x.ValidTo
              && (x.Scope == null || x.Scope.Contains(flowKey)));
    return d?.DelegateId ?? assignee;     // 在委派期 → 待办直接建给代理人
}
```

> **建待办时替换 vs 已有待办转派**：前者只影响"委派期内新产生的待办"，干净、无歧义；后者要把已经在某人手里的待办批量改办理人，边界复杂（已经开始审的怎么办）。MVP 用"建待办时替换"，痕迹里记清"代 X 审批"，谁批的可追溯。已存量待办的转派作为后续增强。

---

## 六、四个动作的状态清理对照表

| 动作 | 状态变更 | 清理 | 痕迹 | 幂等点 |
|---|---|---|---|---|
| 退回 | CurrentNode 回退 | 作废目标之后的在途待办、清会签计票 | 追加 sendback | 重复退回看 CurrentNode |
| 加签 | 加临时待办 | 原审批人前加签时挂起(4) | 追加 addsign | 重复加签产生重复 task→去重 |
| 超时 | 按策略改/不改 | 硬动作改 Status | 追加 timeout_xxx | `TimeoutHandled` + Status闸门 |
| 委派 | 待办建给代理人 | 无（建时即替换） | 记"代 X 审批" | 委派期判定纯函数，天然幂等 |

---

## 七、资深视角

**为什么退回用"作废(3)"而不是删除待办？** 删了就查不到"这张单曾经走到过 N 节点又被退回"。作废保留记录，配合只追加的 `FlowHistory`，整张单的来回曲折全程可复盘——审计、甩锅、优化流程都靠它。

**加签会不会无限加？** 会，要设上限（如最多 3 层加签）或只允许当前审批人加一次。无限加签是流程失控的信号，引擎该有闸门。

**超时定时任务多实例会不会重复处理？** 会，所以两道幂等：扫描标记 `TimeoutHandled` + 动作本身幂等。分布式部署时再加个分布式锁/抢占，保证同一时刻只一个实例在扫。

**委派的权责**：代理人批的，法律上算谁批的？痕迹必须双记——"代理人 Y（代授权人 X）同意"。否则出了事追责对不上人。这是委派**必须留双痕**的原因。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 退回/驳回到指定节点 | **Flowable / Activiti 自由跳转** | `moveActivityIdTo`、清理运行时 token |
| 加签（前/后） | **钉钉 加签、SAP 临时审批人** | 实例级动态节点、不改模板 |
| 超时升级 | **BPMN Timer Boundary Event** | 边界定时器、升级路径 |
| 委派/代理 | **Flowable DelegateTask / SAP 代理人** | 授权期、双重留痕 |

> Flowable 的"自由跳转 + 运行时 token 清理"就是本章退回的难点本质——核心都是**改了状态机的当前位置，就得把不一致的运行态收拾干净**。

---

## 九、阶段3（高级流程）自检

- [ ] 退回和驳回有什么本质区别？退回要清理哪些状态？为什么用作废而非删除？
- [ ] 加签为什么只改 `FlowInstance` 待办、不改 `FlowDef`？前加签怎么挂起原审批人？
- [ ] 超时扫描的两道幂等分别是什么？软动作(remind)和硬动作(approve)幂等处理为何不同？
- [ ] 委派为什么选"建待办时替换"？痕迹为什么要双记？
- [ ] 哪一张表永远只追加、不因任何回退而删？

全部能答 → 流程接得住真实审批的"不走寻常路"。阶段 3 连同 [06 规则](./06-rule-engine.md) 闭合，复杂审批齐活。下一步进入 [08 数据存储](./08-data-storage.md) 与 [09 自研设计器](./09-designers.md)——把"手配 JSON"升级为"拖拽生成"。

---

*配套教学见 [docs/oa/06](../oa/06-advanced-flow.md)。实现落 `CP6.Core/Services/Wf/AdvancedFlow.cs` + 超时 `TimeoutScanService`（HostedService）。*
