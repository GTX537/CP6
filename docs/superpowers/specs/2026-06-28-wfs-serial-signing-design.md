# WFS 引擎深化 · 串簽(顺签/逐级审批)设计 Spec

> 版本 **v1.1**(2026-06-28 稳定性修订,纳入用户评审 R1~R13) · 分支 `feat/wfs-serial-sign`(off `main` a462764)
> v1.0→v1.1 关键改动:**①档位计票加 `StageRound` 维度(解 prevStage 重入计票串台)②进节点冻结 `RuntimeApprovalStage` 运行计划(解 managerChain 档位漂移)③`IApprovalStagePlanner` 服务集中展开逻辑④空审批人不静默跳过(E-WF-013)⑤DTO/索引/测试补强**。详见 §R。
> 上游内核:[[2026-06-26-wfs-runtime-kernel-design]](token 内核 L0,本 spec 不推倒)与 [[2026-06-26-wfs-form-inbox-unified-design]](OA 信箱 umbrella)。
> 本 spec 在**已完成的 token 内核 + OA 电子表单信箱(A/B/C/C′/D-1 全上 main)**之上,深化审批节点的**串簽**表达力。

---

## §0 范围(做什么 / 不做什么)

**做(四件事):**
1. **审批节点多档串簽**:`FlowNode` 增有序 `Stages`,token 停泊本节点、内部**档位游标**逐档推进(第 K 档按其会签模式判定通过 → 建第 K+1 档待办;末档过 → `AdvanceToken` 去下一节点)。
2. **三种档形态**:①**固定顺序清单**(设计期排好的有序档)②**逐级上报**(`managerChain` 档,运行时沿 `Sys_User.ManagerId` 链动态展开成 N 档,到链顶或 `MaxLevels` 封顶)③**串中带并**(某档审批人解析出多人 + `Countersign` all/any/veto,该档内并行会签)。
3. **退回动作泛化**:现 `SendBackAsync` 仅 node 级,泛化为三目标 —— `prevStage`(同节点上一档)/`starter`(退回发起人重填,回 `Draft` 复用草稿重提)/`node`(退回指定前置节点,沿用现状);**驳回 `ActAsync(approve:false)` 保持整单 terminate**。
4. **全栈贯通**:读模型传签履历按档落库 + forecast 按档展开 + 信箱 timeline 显档位 + Vue Flow 设计器档编辑面板。

**不做(YAGNI,留 roadmap):**
- 串簽档内**再嵌套并行网关/子流程**(串中带并仅"一组人并行会签",不嵌 split/join)。
- 服务任务/WebAPI/JOB/子流程节点(umbrella §9,另起 spec)。
- inclusive 包容网关、跨并行块退回(引擎 hardening 方向,另起)。
- 串簽档的**条件跳档**(某档按表单值动态跳过)——P1 全档必经,YAGNI。
- 会签人**动态加档**(运行时插入新档)——加签 `AddSignAsync` 已覆盖"档内加人",不做"加档"。
- managerChain `AllowEmpty`(无主管自动跳过)——v1 统一按审批人缺失处理(§3.5),配置项延后。

---

## §1 现状锚点(实读真行号,落码前仍建议复核)

> 工作树 `D:/CP6-wfs-serial` @ `feat/wfs-serial-sign`(== `main` a462764)。

| 主题 | 文件:行 | 现状要点 |
|---|---|---|
| 节点/边 DSL | `CP6.Core/Services/Wf/FlowSchema.cs:16-71` | `FlowNode.Type`(L21-23)、`ApproverStrategy/Levels/RoleId/UserId`(L27-30)、`Countersign`(L33)、`CcUsers/CcRoleId`(L49-50)、`Code`(L58)。`FlowEdge`(L61-71)。 |
| 审批人策略 | `IApproverResolver.cs:6-46` | `ApproverStrategy` 枚举、`ApproverRule(Strategy,Levels,RoleId,SpecifiedUserId)`(L21)、`ResolveAsync` 纯查询缺位返因不抛(L45)。 |
| 审批人解析 | `ApproverResolver.cs:17-78` | `DirectManagerAsync` 沿 `ManagerId` 上溯取**第 N 级单人**(L30-48);`RoleAsync` 取角色全员(L71-78)。 |
| 审批节点 handler | `NodeHandlers/ApprovalNodeHandler.cs:12-53` | `OnEnterAsync`:`stale` 作废重入旧任务(L23-26)→`step`/`WriteSnapshot`(L29-30)→`foreach` 建 `Wf_FlowTask`(`TokenId`=L43)+`WriteFormToOnSend`(L46)。token 停泊。**串簽改造点**。 |
| 办理外壳(重试) | `FlowEngine.cs:100-128` | `ActAsync`/`ActAsAsync` 包 `ActOnceAsync`,`DbUpdateConcurrencyException` 重读重试×3。 |
| 办理核心 | `FlowEngine.cs:130-199` | `ActOnceAsync`:幂等闸(L135)→改 task+`UpdateFormToOnHandleAsync`(L145)→办结快照(L148-156)→**会签计票 `(Inst,Node,Token)`**(L163-169)→`EvaluateNodeCounts`(L169)→写触达(L175)→`!decided` 停泊(L177-181)→`passed`:`SkipPendingFormTos`+`AdvanceToken`(L184-190)/`!passed`:terminate(L191-196)。**串簽改造点**。 |
| 会签三规则 | `FlowEngine.cs:220-235` | `EvaluateNodeCounts(approved,rejected,total,countersign)` 纯静态。**每档复用,签名不动**。 |
| 进节点分发 | `FlowEngine.cs:238-245` | `EnterNodeAsync` → `inst.CurrentNode=node.Id` + `_handlers[type].OnEnterAsync(ctx)`。 |
| Suspend | `FlowEngine.cs:266-270` | `Suspend(inst,node,reason)`:实例 `Suspended` + AddHistory("suspend")。**空审批人复用**。 |
| token 原语 | `FlowEngine.Tokens.cs:14-83` | `SpawnToken`(L14)/`ConsumeToken`(L27)/`CancelAllActiveTokens`(L36)/`FinishIfDrained`(L48)/`HasActiveToken`(L57)/`AdvanceToken`(L68)。 |
| 读模型钩子 | `FlowEngine.ReadModel.cs:12-161` | `NextStepSeq`(L12)/`WriteFormToOnSend`(L29)/`UpdateFormToOnHandleAsync`(L52)/`WriteSnapshot`(L72)/`WriteCcAsync`(L82)/`SkipPendingFormTos`(L101)/`TransferFormToAsync`(L115)/`VoidPendingFormTos`(L142)。 |
| 高级动作 | `AdvancedFlow.cs:16-129` | `AddSignAsync`(L16)/`SetDelegateAsync`(L55)/`TransferAsync`(L78)/**`SendBackAsync(taskId,actor,targetNodeId,comment)`**(L100-129)。**退回泛化点**。 |
| 实体 task | `Wf_FlowTask.cs:11-53` | `BaseTenantEntity`,`TokenId`(L46),`IsRead`(L49)。**增 `StageIndex`+`StageRound`**。 |
| 实体 token | `Wf_FlowToken.cs:13-28` | `BaseTenantEntity`,`InstanceId/NodeId/Status/ParentTokenId/ForkId`。**增 `StagePlanJson?`**。 |
| 实体 formto | `Wf_FlowFormTo.cs:11-32` | 传签履历,`StepSeq`/`NodeId/NodeCode/NodeName`/`ExpectedHandlerId`/`Status`。**增 `StageIndex?`+`StageRound?`**。 |
| 状态常量 | `WfStatus.cs:4-43` | `FlowInstanceStatus`(…Draft5)、`FlowTaskStatus`(…Cancelled3/Suspended4)、`FlowTokenStatus`、`FlowFormToStatus`(Pending0…**Voided6**)。**增 `SentBack=7`**。 |
| 引擎接口 | `IFlowEngine.cs:7-49` | `SendBackAsync`(L27)、`StartDraftAsync`(L44)。 |
| forecast | `Services/Oa/ForecastService.cs:15-85` | `ForecastAsync` 逐节点前推(L15);`approval` 一节点一步(L54-59);`ResolveApproverNamesAsync`(L72)。**按档展开 + 复用 planner**。 |
| 校验器 | `FlowSchemaValidator.cs:9-55` | 静态校验六规则统一 `E-WF-010`;`KnownStrategies`(L6)。**增串簽档规则 E-WF-011**。 |
| 设计器 model | `cp6.web/.../designer/designerModel.ts:3-70` | `SchemaNode`(L3-11)/`NODE_PALETTE`(L15)/`schemaToGraph`(L24)/`graphToSchema`(L43)/`validateClient`(L61)。**增 stages 序列化 + 镜像校验**。 |
| 设计器面板 | `cp6.web/.../designer/NodePropertyPanel.vue` | 审批属性面板(基本/進階/知會)。**增「串簽档位」段**。 |

**关键既有错误码**:`E-WF-002`(转交非法)/`E-WF-003`(草稿越权/非草稿)/`E-WF-006`(流程缺失)/`E-WF-009`(身份码重复)/`E-WF-010`(schema 结构非法)。

---

## §2 数据模型(DB 5 小列 + 1 索引,零数据搬迁)

### §2.1 `ApprovalStage`(设计期,`FlowSchema.cs`,进 SchemaJson 无 DB 列)

```csharp
/// <summary>串簽档位(设计期)。一个 approval 节点可挂有序 Stages;空=单档(用节点既有字段)。</summary>
public class ApprovalStage
{
    public string? Name { get; set; }                 // 档名(信箱/forecast 显示)
    public string? Code { get; set; }                 // 人面档码(可空)
    /// <summary>档型:fixed=固定一组审批人;managerChain=沿 ManagerId 链逐级展开。见 ApprovalStageKinds。</summary>
    public string Kind { get; set; } = "fixed";
    // ── fixed 档:一条审批人规则(同节点既有四字段语义) ──
    public string? ApproverStrategy { get; set; }     // DirectManager/DeptLeader/Role/Specified/Starter
    public int? ApproverLevels { get; set; }          // 仅 fixed+DirectManager:取第 N 级主管(本档仍只产 1 运行档)
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
    /// <summary>该档会签模式(串中带并):all/any/veto。见 CountersignModes。默认 all。</summary>
    public string Countersign { get; set; } = "all";
    /// <summary>仅 managerChain:从第 1 级主管起逐级展开,最多 N 个运行档(到链顶或本上限,先到为止)。</summary>
    public int? MaxLevels { get; set; }
}
```

> **`ApproverLevels`(fixed) ≠ `MaxLevels`(managerChain)**:前者"取第 N 级主管,本档仍 1 个运行档";后者"从第 1 级逐级展开成最多 N 个运行档"。§6 设计器须明示区分(R7)。

`FlowNode` 增一字段(`FlowSchema.cs:58` Code 之后):

```csharp
/// <summary>串簽档位序列(有序)。空/缺省=单档,用本节点 ApproverStrategy/Countersign(向后兼容)。</summary>
public List<ApprovalStage>? Stages { get; set; }
```

### §2.2 `RuntimeApprovalStage`(运行期,展平后的稳定档,**冻结持久化**)

```csharp
/// <summary>运行期档(IApprovalStagePlanner 展平 ApprovalStage 后的稳定结果)。
/// 进入 approval 节点时算一次并冻结(序列化进 Wf_FlowToken.StagePlanJson),后续推进/退回/forecast 均以此为准。</summary>
public sealed class RuntimeApprovalStage
{
    public int StageIndex { get; set; }               // 运行档序号(0-based)
    public string Kind { get; set; } = "fixed";       // fixed | managerChain(展平后单级)
    public string? StageName { get; set; }
    public string? StageCode { get; set; }
    public ApproverRule Rule { get; set; } = default!;// 该档审批人规则(managerChain 展平为 DirectManager Levels=j)
    public string Countersign { get; set; } = "all";
}
```

### §2.3 实体新列

`Wf_FlowTask.cs:46`(`TokenId` 之后)增:
```csharp
/// <summary>串簽运行档序号(WFS 引擎深化)。默认 0=单档/旧数据,语义不变,无需回填。</summary>
public int StageIndex { get; set; }
/// <summary>同一运行档的重入轮次(prevStage 退回后 +1)。计票按 (Inst,Node,Token,StageIndex,StageRound) 隔离,
/// 杜绝退回上一档后旧轮 Approved 任务串入新轮计票(R1)。默认 0。</summary>
public int StageRound { get; set; }
```

`Wf_FlowToken.cs` 增:
```csharp
/// <summary>本 token 当前 approval 节点的冻结运行计划(RuntimeApprovalStage[] JSON)。进多档审批节点时算一次写入;
/// 单档节点 / 非审批节点 = null。推进/退回基于它,不再每次现查 → 杜绝 managerChain 档位漂移(R2)。</summary>
public string? StagePlanJson { get; set; }            // nvarchar(max),可空
```

`Wf_FlowFormTo.cs` 增 `public int? StageIndex { get; set; }` + `public int? StageRound { get; set; }`(timeline/forecast 按档·轮标号,旧行 null)。

### §2.4 状态常量

`FlowFormToStatus` 增 `public const int SentBack = 7;`(退回上一档专用,区别于普通 `Voided=6`;timeline 显"已退回"非"作废",R3)。

### §2.5 迁移 + 索引

EF 迁移 `WfsSerialSign`:5 列(`Wf_FlowTask.StageIndex`/`StageRound` int default 0、`Wf_FlowToken.StagePlanJson` nvarchar(max) null、`Wf_FlowFormTo.StageIndex`/`StageRound` int null)+ 1 轻量索引(R11):
```
IX_Wf_FlowTask_Tally : (InstanceId, NodeId, TokenId, StageIndex, StageRound, Status)
```
**零数据搬迁、零回填**(默认 0 即单档语义)。`Wf_FlowFormTo` 计票不参与,索引可选不加。

---

## §3 运行时(冻结计划 + 档位游标,方案 1)

### §3.1 `IApprovalStagePlanner`(新服务,集中展开逻辑,R4)

```csharp
public interface IApprovalStagePlanner
{
    /// <summary>把 node.Stages 展平成稳定运行档序列。无 Stages → 单档(用节点既有字段),保证旧行为逐字等价。
    /// managerChain:沿 starter 的 ManagerId 链逐级展开(第 j 级 = DirectManager Levels=j),到链顶或 MaxLevels 止;
    /// 链断属正常终止信号。fixed:1 档。展平只定"序列/档数/规则/会签",审批人 USER ID 在各档激活时晚解析(§3.3)。</summary>
    Task<IReadOnlyList<RuntimeApprovalStage>> BuildAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node);
}
```
默认实现 `ApprovalStagePlanner` 消费 `IApproverResolver`(仅 managerChain 探链长,不落人选)。DI 注册;handler 经 `ctx.Engine` 取(同 `Approver`/`Notifier` 暴露式)。

> **冻结语义(R2 核心)**:`BuildAsync` 在**进入 approval 节点时调一次**,结果序列化进 `token.StagePlanJson`。后续档推进、`prevStage` 退回**只读冻结计划**,绝不重算 → managerChain 与 fixed 混排时,组织链中途变化**不改变档序列/档数** → fixed 档永不被挤位(§9 必测)。**审批人 USER ID 不冻结**:每档激活建任务时由 `IApproverResolver` 按该档 `Rule` 现查(取激活时点在岗者,与现单节点行为一致)。即:**序列冻结、人选晚绑**。

### §3.2 `ApprovalNodeHandler` 档化

`OnEnterAsync`(`:12-53`)重构:
- 算 `plan = await planner.BuildAsync(inst, schema, node)` → 序列化写 `token.StagePlanJson`(单档:plan 仅 1 项且来自节点既有字段;多档:展平 Stages)。
- 进**第 0 档**:抽出私有 `EnterStageAsync(inst, schema, node, token, plan, k)` —— 取 `plan[k]`,按其 `Rule` 解析审批人(`IApproverResolver`);**解析不到 → `Suspend`(reason `E-WF-013`),不静默跳过(R5)**;否则建 `Wf_FlowTask`(`StageIndex=k`、`StageRound=` **单一推导规则** `(本 token·node·k 已存在的最大 StageRound ?? -1) + 1`)+`WriteFormToOnSend(stageIndex:k, stageRound:..)`+快照+通知。
  - 该推导让前进/退回两路自洽:**前进**到从未进过的下档 → 无既存轮 → `StageRound=0`;**`prevStage` 退回**到进过的上档 → 既存最大轮 +1(新轮,计票隔离)。无需额外作废旧轮任务(旧轮非本轮,计票天然不计;旧轮 Approved 留痕,timeline 显"第 R 轮")。
  - `OnEnter` = `EnterStageAsync(..., k:0)`。
- **单档兼容铁律**:plan 为 1 项且节点无 `Stages` 时,`EnterStageAsync` 走与今天 `OnEnterAsync` **逐字等价**路径(`StageIndex=0`/`StageRound=0`),`--filter Wf` 既有零改照绿。

### §3.3 `ActOnceAsync` 档化

`ActOnceAsync`(`:130-199`)三处增量,重试外壳(`:100-128`)与 `EvaluateNodeCounts`(`:220`)签名**不动**:
1. **计票加档·轮维度**(`:163-166`):`nodeTasks` 过滤加 `&& t.StageIndex==task.StageIndex && t.StageRound==task.StageRound`(R1,本档本轮隔离)。
2. **passed 推进"先推档,无档才推节点"**(`:184-190`):
   ```
   passed:
     SkipPendingFormTos(本档本轮兄弟 Pending → Skipped)        // 加 StageIndex/StageRound 过滤
     plan = Deserialize(token.StagePlanJson)
     k1 = task.StageIndex + 1
     若 k1 < plan.Count → EnterStageAsync(inst,schema,node,token,plan,k1)  // 同节点同 token 建下档(StageRound=0)
     否则 → AdvanceToken(inst,schema,token)                                // 末档过 → 去下一节点
   ```
3. **!passed(驳回)不变**(`:191-196`):整单 terminate(`Rejected`+`CancelAllActiveTokens`+`VoidPendingFormTos`)。退回是另一动作路径(§4)。

> 写触达 `inst.ModifyDate`(`:175`)位置不动 —— 停泊与推进两条 mutating 路径仍都参与 RowVersion 乐观并发(串中带并某档并行会签的丢失唤醒防护,scope 缩到档·轮内)。

### §3.4 进节点须先有 plan

`EnterNodeAsync`(`:238-245`)对 `approval` 节点的进入恒经 `ApprovalNodeHandler` 重建 `StagePlanJson`(进新审批节点即覆盖),故 token 跨节点流转/循环回同节点都拿到当次冻结计划,不会读到上一节点的陈旧 plan。

### §3.5 空审批人规则(R5,拍板)

- **fixed 档**解析不到审批人(Specified 用户禁用/删除、Role 无人、DirectManager 无上级)→ **`Suspend`(待人工指派,reason `E-WF-013`)**,**不**自动跳过(否则偷偷少审一档)。
- **managerChain 档**:逐级展开,**链断即正常停止**(已展开的档照跑);若**展开结果为 0 档**(starter 首级即无主管)→ 同按审批人缺失 `Suspend`/`E-WF-013`。`AllowEmpty`(允许 0 档静默跳过)= 延后配置(§0 不做)。

---

## §4 动作语义(驳回 vs 退回,对齐 Delta 双按钮)

### §4.1 驳回 = 整单 terminate(沿用现状)

`ActAsync(approve:false)` → §3.3 `!passed` 分支,整单 `Rejected`。**零改**。

### §4.2 退回 `SendBackAsync` 泛化三目标

签名扩(保留旧重载向后兼容):
```csharp
Task SendBackAsync(Guid taskId, Guid actorId, SendBackTarget target, string? comment = null);
public sealed record SendBackTarget(string Kind, string? NodeId = null);  // Kind: prevStage | starter | node
// 旧重载 SendBackAsync(taskId,actor,targetNodeId,comment) 转发为 new(Kind:"node", targetNodeId)
```

**共性**:三目标均先校验 `actor` 是当前 Pending task 的合法办理人、任务未办(幂等闸,沿用 `:104`);均 `AddHistory("sendback")` 追加。

| Kind | 行为 | 复用 |
|---|---|---|
| **`prevStage`**(R3) | 仅 `task.StageIndex>0` 合法,否则 `E-WF-012`。步骤:①当前 task → `Cancelled`;②本档本轮其余 Pending task → `Cancelled`;③本档本轮 Pending `Wf_FlowFormTo` → `SentBack(=7)`(非 Voided);④读 `token.StagePlanJson`,游标减一,`EnterStageAsync(..., k:task.StageIndex-1)` 以 **`StageRound = 上一档已有最大轮 + 1`** 重建上一档任务(新轮,计票隔离);⑤**token 不 terminate**(仍停本节点),其他档已办履历不动。 | `EnterStageAsync`(§3.2)冻结 plan |
| **`starter`** | terminate token(`CancelAllActiveTokens`)+ `VoidPendingFormTos`(全实例)+ 实例回 `Draft(=5)` 归属发起人。发起人改后经既有 草稿→`StartDraftAsync`(`:72-92`)**整流程从头重跑**(R 边界:非"补料后从当前节点续",§11)。 | `StartDraftAsync` 既有路径 |
| **`node`**(R8) | 现状逐字保留 + **收紧目标校验**:目标须 ①存在 ②`Type∈{approval,start}` ③在当前节点**上游可达路径**上 ④非 `end` ⑤非当前节点自身 ⑥v1 不支持跨 `parallelSplit/parallelJoin` 块退回(命中 → `E-WF-012`)。校验通过后:作废全在途→`CancelAllActiveTokens`→`VoidPendingFormTos`→`SpawnToken`(target)→`EnterNodeAsync`。 | `SendBackAsync` 原体(`:100-129`)+ 上游可达 BFS |

> **node 收紧回归安全(已核 `AdvancedFlowTests.cs`)**:既有 sendback 测试全退回上游 `n1`、`nope`/`end` 已 throw → 上游可达 + Type 约束对既有用例零破坏。

### §4.3 动作接缝复用

`加签 AddSignAsync`(`:16`)/`委派`/`转交 TransferAsync`(`:78`)在**当前档本轮内**照常工作:新建任务随原任务同 `TokenId`,**须同步带 `StageIndex`/`StageRound`=原任务值**(计票同档同轮)。

---

## §5 读模型 + forecast + 信箱

### §5.1 传签履历(几乎免费,加档·轮标号)

`WriteFormToOnSend`(`:29-46`)增 `stageIndex/stageRound` 入参,落 `Wf_FlowFormTo` 同名列;`NodeName` 可附 `StageName`。`UpdateFormToOnHandleAsync`(`:52`)/`SkipPendingFormTos`(`:101`)/`VoidPendingFormTos`(`:142`)关单/跳过/作废**加 `StageIndex/StageRound` 过滤**(档·轮内精确)。`StepSeq` 每档经 `NextStepSeq`(`:12`)递增 → timeline 按 `StepSeq` 排序;同 `NodeId` 多档·多轮按 `(StageIndex,StageRound)` 分组显示「第 K 档 · 第 R 轮 · 档名 · 审批人 · 状态」。

### §5.2 forecast 按档展开(**预览 ≠ 运行快照**,R9)

`ForecastService.ForecastAsync`(`:15`)的 `approval` 分支(`:54-59`)改为**复用 `IApprovalStagePlanner.BuildAsync`** 展平,逐运行档 emit `ForecastStep`(增 `stageIndex/stageName`):`fixed` 解析该档审批人名;`managerChain` 沿链 emit 各级主管名(链断/封顶止)。

> **明示**:forecast 展开仅为**发起/查看时预览**;真正执行以**进入 approval 节点时冻结的 `RuntimeApprovalStages`(随 token/任务/履历持久化)为准**。组织在预览后、执行前变化 → 实际 approver 可能与预览不同,**非 bug**(UI 文案点明"预计")。

### §5.3 信箱左读右签

`FormDetail` 操作条按状态浮现「同意 / 驳回 / 退回 / 加签 / 转交」。**退回目标选择器**:
- `prevStage`:仅当前任务 `StageIndex>0` 显示「退回上一档」(由 DTO `CanSendBackPrevStage` 驱动)。
- `starter`:恒显示「退回发起人」。
- `node`:列出**上游可达的** approval/start 节点「退回到…」。

### §5.4 API DTO 字段(R10)

待办列表 / 表单详情 / timeline 项 / forecast step / 退回目标 / 设计器预览的 DTO 增:
```csharp
public int StageIndex { get; set; }
public int StageRound { get; set; }
public string? StageName { get; set; }
public string? StageCode { get; set; }
public bool CanSendBackPrevStage { get; set; }   // = (StageIndex>0)
```
前端据此判断:是否第 0 档、是否显示「退回上一档」、当前档名、同节点多档·多轮如何分组。

---

## §6 设计器(Vue Flow,C′ 之上)

**不新增节点类型**;审批节点(`approval`)`NodePropertyPanel.vue` 增「串簽档位」段:
- 勾「启用串簽」展开**有序档列表**(增 / 删 / 上移 / 下移),每档配:
  - 档型 radio:`固定(fixed)` / `逐级(managerChain)`。
  - `固定`:审批人策略 + 角色/用户(复用既有 `userApi/roleApi` 远程搜索)。
  - `逐级`:**`MaxLevels`(逐级展开上限,产 N 个运行档)**。
  - 会签 `Countersign`:all/any/veto(串中带并)。
  - 档名 `Name`(可选)。
- 不勾「启用串簽」= 现有单档单规则 UI(`Stages` 不写,向后兼容)。
- **明示文案(R7)**:固定档 `DirectManager` 的 `ApproverLevels`="取第 N 级主管(1 档)";逐级档 `MaxLevels`="从第 1 级逐级展开(最多 N 档)"——两者并列须有 tooltip 区分,防混。

`designerModel.ts`:`SchemaNode`(`:3-11`)增 `stages?: ApprovalStage[]`;`schemaToGraph`(`:24`)/`graphToSchema`(`:43`)经 `...n`/`...(n.data)` 透传补 `stages`(vitest 验串簽往返互逆);`validateClient`(`:61`)镜像后端新规则(`stages` 非空每档合法:fixed 须策略 / managerChain 须正 MaxLevels / countersign 合法 / 不得空档),错误 key `oa.designer.errStageInvalid`。

---

## §7 校验 / 错误码 / i18n / 常量

### §7.1 `FlowSchemaValidator` 增串簽规则(`:26-27` 后)

approval 节点:`Stages` 为空 → 走既有规则⑤(须合法 `ApproverStrategy`);`Stages` 非空 → 每档(任一不满足 → `E-WF-011`):`fixed` 须合法策略(`KnownStrategies`,`:6`);`managerChain` 须 `MaxLevels>=1`;`Countersign` ∈ {all,any,veto};档列表不得为空。

### §7.2 错误码(新增 3)

| 码 | 含义 |
|---|---|
| `E-WF-011` | 串簽档配置非法(空档 / fixed 缺策略 / managerChain 缺 MaxLevels / countersign 非法) |
| `E-WF-012` | 退回目标非法(prevStage 于第 0 档 / node 目标非上游 approval·start / 退到 end / 退到自身 / 跨并行块) |
| `E-WF-013` | 档审批人缺失(fixed 解析不到 / managerChain 展开 0 档)→ 实例 `Suspend` 待指派 |

### §7.3 常量(R6,杜绝裸字符串)

```csharp
public static class ApprovalStageKinds { public const string Fixed="fixed"; public const string ManagerChain="managerChain"; }
public static class CountersignModes  { public const string All="all"; public const string Any="any"; public const string Veto="veto"; }
```
validator / planner / handler / 前端镜像规则统一引用。

### §7.4 i18n

设计器档编辑 UI 文案(`oa.designer.stage.*`)+ 3 新错误码 × 五语,沿用 `I18nOa*ScreenSeed` 静态 `Sys_Lang[]`,**grep 全 `t('oa.*')` 新键去重避开既有 seed**,接 `Program.cs` concat 链(带去重)。

---

## §8 兼容 / 迁移

1. **回归硬闸**:既有 Wf 测试(`dotnet test --filter Wf`:单档 + 并行 + 会签 + 退回 + 加签 + 委派 + 转交)**零改照绿** + 全量回归(基线 1294/1skip)不降。单档兼容靠 §3.1 "无 Stages → 单档逐字等价" + `StageIndex/StageRound` 默认 0 + `StagePlanJson` 单档 1 项。
2. **在途实例**:现存任务 `StageIndex/StageRound` 默认 0 = 单档语义,token `StagePlanJson` null(下次进审批节点才生成)→ **无需回填**。
3. **新列默认**:见 §2.5(5 列全可空/默认 0)。
4. **旧 `SendBackAsync(...,targetNodeId,...)` 重载保留**(转发 `Kind=node`),既有退回调用方零感知;node 收紧校验对既有上游退回用例零破坏(§4.2 已核)。

---

## §9 测试

**单元(后端,全量基线 1294/1skip → +N):**
- 固定三档顺序推进(档0过→建档1→过→建档2→过→AdvanceToken→下一节点)。
- 逐级动态 N 档展开(链 3 级→3 档,链断即止 AdvanceToken;`MaxLevels=2` 封顶 2 档)。
- 串中带并某档会签 all/any/veto(档内多人,EvaluateNodeCounts 按档·轮隔离)。
- 档计票隔离:相邻档不串台(`StageIndex` 过滤)。
- **重复 prevStage 多轮计票隔离(R12,核心)**:固定三档,档0过→档1过→档2退回档1→档1**再次**过→档2再生成→档2**再次**退回档1→档1第三次过;断言每轮计票只统计**当前 `StageRound`** 任务,旧轮 `Approved` 不串入(直接命中 R1 修复点)。
- 退回 `prevStage`:档2退回→当前 task `Cancelled`、本档本轮 Pending FormTo `SentBack(7)`、档0 已办履历不动、token 不 terminate、上一档 `StageRound+1`;第 0 档 prevStage → `E-WF-012`。
- 退回 `starter`:实例回 `Draft`、token cancelled、全 Pending 履历 Voided;`StartDraftAsync` 重提整流程从头跑。
- 退回 `node`:现状等价 + 收紧校验(退非上游/跨并行块/自身 → `E-WF-012`,既有上游退回照绿)。
- 驳回(approve:false)某档 → 整单 `Rejected` + 兄弟连坐(沿用)。
- **冻结计划防漂移(R2 单测)**:fixed+managerChain 混排(财务专员 fixed / managerChain MaxLevels=2 / 总经理 fixed),进节点冻结后**改组织链**,断言后续 fixed 档(总经理)`StageIndex` 不漂、不被 managerChain 挤位。
- 空审批人:fixed 解析不到 → `Suspend`/`E-WF-013` 不跳过;managerChain 0 档 → 同。
- forecast 展开档(固定 + managerChain 链解析名 + 封顶)+ 复用 planner 与运行一致性。
- 校验(空档 / fixed 缺策略 / managerChain 缺 MaxLevels / countersign 非法 → `E-WF-011`)。
- 迁移:默认 0,旧实例单档语义不变。
- 加签/转交在档内:带 `StageIndex/StageRound` 与原任务同档同轮计票。

**前端(vitest):** `designerModel` 串簽往返互逆(stages 穿透)+ `validateClient` 镜像四规则。

**gstack 真浏览器 QA(隔离库 `CP6DB_OA`,harness `docs/superpowers/qa/wfs-serial-signing/`):**
- 设计串簽流程(固定 2 档 + 逐级 1 档)→保存(校验过)→发起→逐档审(timeline 显档·轮)→退回上一档→再审→末档过→Approved;
- 驳回 terminate + 退回发起人重提;
- **主管变更场景(R13)**:`managerChain MaxLevels=2`,发起时链 A→B,第 1 级审批前把链改为 A→C→D;断言**序列冻结**(档数不因链增而变)、后续 fixed 档不错位、人选按冻结语义(§3.1:序列冻结/人选晚绑)落地。

---

## §10 分期(交付波次,plan 阶段细化为 T1~Tn)

| 期 | 范围 | 闸 |
|---|---|---|
| **P-A 引擎内核** | §2 数据模型(5 列+索引)+ 迁移 + §3.1 `IApprovalStagePlanner`(冻结)+ §3.2 handler 档化 + §3.3 ActOnce 档·轮化 + §3.5 空审批人 + 单档兼容闸 + 单测 | `--filter Wf` 既有零改照绿 + 串簽推进 + 冻结防漂移 + 重复退回单测绿 |
| **P-B 退回泛化** | §4 `SendBackTarget` 三目标 + prevStage 状态机(StageRound+1)+ node 收紧校验 + 旧重载转发 + 读模型档·轮清理 + 单测 | 三退回路径 + 重复 prevStage 多轮隔离单测绿 + 既有退回零改 |
| **P-C 读模型/forecast/信箱** | §5 列落库 + `WriteFormToOnSend` 档·轮 + forecast 复用 planner 展开 + §5.4 DTO + `FormDetail` 退回选择器 + timeline 显档·轮 | forecast 单测 + 前端 type-check/vitest |
| **P-D 设计器** | §6 档编辑面板(MaxLevels/ApproverLevels 区分文案)+ `designerModel` stages 往返 + `validateClient` + §7 校验/3 错误码/常量/i18n | vitest 往返 + build + i18n check |
| **P-E gstack QA** | §9 全链真浏览器固化(含主管变更场景) | 剧本 PASS |

每 Task:全新 general-purpose subagent(sonnet) TDD → 控制器/diff 级复核(`git show` + 零 Space/零越界核验)→ 本地 commit 不 push;**零改引擎执行态硬闸**(`dotnet test --filter Wf` 既有照绿)。隔离 worktree `D:/CP6-wfs-serial` @ `feat/wfs-serial-sign`,绝不碰 `D:/CP6`(脏分支)/`D:/CP6-space-backend`(Space 会话)。

---

## §11 延后(YAGNI)与边界

- **延后**:串中带并嵌套网关/子流程;服务任务/WebAPI/JOB;inclusive 网关;跨并行块退回;条件跳档;运行时加档;managerChain `AllowEmpty`。
- **边界(文档化)**:
  ① **冻结语义**:approval 节点进入时冻结 `RuntimeApprovalStages`(序列+档数)进 `token.StagePlanJson`,中途组织变化不改序列;审批人 USER ID 各档激活时晚解析(取在岗者)。预览(forecast)≠ 运行快照(§5.2)。
  ② managerChain 与 token 内核并行网关组合:串簽档在单 token 内顺序推进,不 spawn 子 token,不与 split/join 冲突。
  ③ `prevStage` 不跨节点,第 0 档退回须用 `starter`/`node`。
  ④ `starter` 退回复用 Draft,流程从头跑(非从当前节点续)——"补料后从当前节点续"属另一语义,YAGNI 不做。
  ⑤ `node` 退回 v1 不跨并行块(`CancelAllActiveTokens` 会连坐兄弟分支,跨块退回语义未定义)。

---

## §R 稳定性修订汇总(v1.0 → v1.1,用户评审 R1~R13)

| R | 评审点 | 决议 | 落点 |
|---|---|---|---|
| **R1** 🔴 | StageIndex 单维计票,prevStage 重入旧轮 Approved 串台 | 加 `Wf_FlowTask.StageRound`(+FormTo),计票维度 `(Inst,Node,Token,StageIndex,StageRound)`;选 StageRound 而非 StageEntryId(更轻+可显"第 N 轮") | §2.3 §3.3 §9 |
| **R2** 🔴 | managerChain 动态重算致 fixed+managerChain 混排档位漂移 | 进节点**冻结** `RuntimeApprovalStage[]` 进 `token.StagePlanJson`;序列冻结、人选晚绑 | §2.2 §2.3 §3.1 §3.4 §11① |
| **R3** 🟡 | prevStage 当前任务/履历状态未定义 | 当前 task→Cancelled、本档本轮 Pending→Cancelled、Pending FormTo→`SentBack(7)`、sendback history、上一档 StageRound+1;校验 actor 合法 | §2.4 §4.2 §9 |
| **R4** 🟡 | StageSpecAt 不应是散落静态函数 | 抽 `IApprovalStagePlanner` 服务,handler/ActOnce/prevStage/forecast 共用 | §3.1 §5.2 |
| **R5** 🟡 | 空审批人未拍板 | fixed 解析不到 → `Suspend`/`E-WF-013` 不跳过;managerChain 链断正常、0 档同按缺失;`AllowEmpty` 延后 | §3.5 §7.2 §0 |
| **R6** 🟢 | Kind/Countersign 裸字符串 | `ApprovalStageKinds`/`CountersignModes` 常量 | §7.3 |
| **R7** 🟢 | ApproverLevels(fixed) vs MaxLevels(managerChain) 易混 | 文档+设计器 tooltip 明示区分 | §2.1 §6 |
| **R8** 🟡 | SendBack(node) 合法目标未严格定义 | 目标须存在/Type∈approval·start/上游可达/非 end/非自身/v1 不跨并行块 → `E-WF-012`;经核 `AdvancedFlowTests` 回归安全 | §4.2 §11⑤ |
| **R9** 🟡 | forecast 易被当运行快照 | 明示 forecast=预览,运行以冻结 RuntimeApprovalStages 为准 | §5.2 §11① |
| **R10** 🟡 | API DTO 缺档字段 | DTO 增 StageIndex/StageRound/StageName/StageCode/CanSendBackPrevStage,覆盖待办/详情/timeline/forecast/退回目标/设计器预览 | §5.4 |
| **R11** 🟢 | 计票索引不应完全不动 | 加 `IX_Wf_FlowTask_Tally(Inst,Node,Token,StageIndex,StageRound,Status)` | §2.5 |
| **R12** 🟡 | 缺"重复退回上一档"测试 | 增多轮 prevStage 计票隔离单测(命中 R1) | §9 |
| **R13** 🟡 | 缺"主管变更"QA | 增 gstack managerChain 链中途变更场景,验序列冻结/fixed 不错位 | §9 |

---

## §12 决策锚点汇总表

| # | 决策 | 取值 |
|---|---|---|
| D1 | 串簽建模 | 方案 1:审批节点内多档 + 档位游标(token 停泊,游标推进) |
| D2 | 档形态 | 固定顺序 / 逐级动态(managerChain) / 串中带并(档内会签),三者全要 |
| D3 | 数据模型 | `FlowNode.Stages`(进 SchemaJson)+ `Wf_FlowTask.StageIndex/StageRound` + `Wf_FlowToken.StagePlanJson` + `Wf_FlowFormTo.StageIndex?/StageRound?`(DB 5 列 + 1 索引) |
| D4 | 会签计票 | 扩到 `(Inst,Node,Token,StageIndex,StageRound)`,`EvaluateNodeCounts` 每档·轮复用、签名不动 |
| D5 | 驳回 | `ActAsync(approve:false)` 保持整单 terminate(零改) |
| D6 | 退回 | `SendBackAsync` 泛化三目标 `prevStage`/`starter`/`node`;旧重载转发 node |
| D7 | 退回发起人 | 复用 `Draft` + `StartDraftAsync`,整流程从头重跑 |
| D8 | 逐级解析 | `IApprovalStagePlanner` 沿 DirectManager 逐级(j=1,2,…)探链,链顶终止;进节点冻结序列 |
| D9 | 向后兼容 | 无 Stages → 单档逐字等价,`--filter Wf` 既有零改照绿;新列默认 0/null 无需回填 |
| D10 | 设计器 | 不新增节点类型,approval 面板加「串簽档位」段;designerModel stages 往返 + validateClient 镜像 |
| D11 | 错误码 | 新增 `E-WF-011`(档配置)/`E-WF-012`(退回目标)/`E-WF-013`(审批人缺失→Suspend) |
| D12 | 范围 | 全栈含设计器,五期 P-A~P-E;服务任务/inclusive/子流程/条件跳档/AllowEmpty YAGNI |
| D13 | 计票稳定性 | `StageRound` 隔离重入轮次(R1);进节点冻结 `StagePlanJson` 防档位漂移(R2) |
| D14 | 展开服务 | `IApprovalStagePlanner` 单点展开,运行/forecast 共用;序列冻结、人选晚绑 |
| D15 | 空审批人 | 不静默跳过 → `Suspend`/`E-WF-013`(R5) |
| D16 | DTO/索引 | DTO 5 档字段(R10)+ 计票组合索引(R11) |
