# WFS 引擎深化 · 串簽(顺签/逐级审批)设计 Spec

> 版本 v1.0 · 2026-06-28 · 分支 `feat/wfs-serial-sign`(off `main` a462764)
> 上游内核：[[2026-06-26-wfs-runtime-kernel-design]](token 内核 L0,本 spec 不推倒)与 [[2026-06-26-wfs-form-inbox-unified-design]](OA 信箱 umbrella)。
> 本 spec 在**已完成的 token 内核 + OA 电子表单信箱(A/B/C/C′/D-1 全上 main)**之上,深化审批节点的**串簽**表达力。

---

## §0 范围(做什么 / 不做什么)

**做(四件事)：**
1. **审批节点多档串簽**：`FlowNode` 增有序 `Stages`,token 停泊本节点、内部**档位游标**逐档推进(第 K 档按其会签模式判定通过 → 建第 K+1 档待办;末档过 → `AdvanceToken` 去下一节点)。
2. **三种档形态**：①**固定顺序清单**(设计期排好的有序档)②**逐级上报**(`managerChain` 档,运行时沿 `Sys_User.ManagerId` 链动态展开成 N 档,到链顶或 `MaxLevels` 封顶)③**串中带并**(某档审批人解析出多人 + `Countersign` all/any/veto,该档内并行会签)。
3. **退回动作泛化**：现 `SendBackAsync` 仅 node 级,泛化为三目标 —— `prevStage`(同节点上一档)/`starter`(退回发起人重填,回 `Draft` 复用草稿重提)/`node`(退回指定前置节点,沿用现状);**驳回 `ActAsync(approve:false)` 保持整单 terminate**。
4. **全栈贯通**：读模型传签履历按档落库 + forecast 按档展开 + 信箱 timeline 显档位 + Vue Flow 设计器档编辑面板。

**不做(YAGNI,留 roadmap)：**
- 串簽档内**再嵌套并行网关/子流程**(串中带并仅"一组人并行会签",不嵌 split/join)。
- 服务任务/WebAPI/JOB/子流程节点(umbrella §9,另起 spec)。
- inclusive 包容网关、跨并行块退回(引擎 hardening 方向,另起)。
- 串簽档的**条件跳档**(某档按表单值动态跳过)——P1 全档必经,YAGNI。
- 会签人**动态加档**(运行时插入新档)——加签 `AddSignAsync` 已覆盖"档内加人",不做"加档"。

---

## §1 现状锚点(实读真行号,落码前仍建议复核)

> 工作树 `D:/CP6-wfs-serial` @ `feat/wfs-serial-sign`(== `main` a462764)。

| 主题 | 文件:行 | 现状要点 |
|---|---|---|
| 节点/边 DSL | `CP6.Core/Services/Wf/FlowSchema.cs:16-71` | `FlowNode.Type`(L21-23,start/approval/end/parallelSplit/parallelJoin)、`ApproverStrategy/Levels/RoleId/UserId`(L27-30)、`Countersign`(L33,all/any/veto)、`CcUsers/CcRoleId`(L49-50)、`Code`(L58)。`FlowEdge.From/To/Condition/CcUsers`(L61-71)。 |
| 审批人策略 | `IApproverResolver.cs:6-46` | `ApproverStrategy` 枚举(DirectManager/DeptLeader/Role/Specified/Starter)、`ApproverRule(Strategy,Levels,RoleId,SpecifiedUserId)`(L21)、`ResolveAsync` 纯查询缺位返因不抛(L45)。 |
| 审批人解析 | `ApproverResolver.cs:17-78` | `DirectManagerAsync` 沿 `ManagerId` 上溯 `Levels` 级取**第 N 级单人**(L30-48,**串簽逐级要改为逐级走**);`RoleAsync` 取角色全员(L71-78)。 |
| 审批节点 handler | `NodeHandlers/ApprovalNodeHandler.cs:12-53` | `OnEnterAsync`：解析规则→`stale` 作废重入旧任务(L23-26)→`step=NextStepSeq`/`WriteSnapshot`(L29-30)→`foreach` 审批人建 `Wf_FlowTask`(`TokenId`=L43)+`WriteFormToOnSend`(L46)+通知。token 停泊。**串簽改造点**。 |
| 办理外壳(重试) | `FlowEngine.cs:100-128` | `ActAsync`/`ActAsAsync` 包 `ActOnceAsync`,`DbUpdateConcurrencyException` 重读重试×3(L102-110/120-127)。 |
| 办理核心 | `FlowEngine.cs:130-199` | `ActOnceAsync`：幂等闸(L135)→改 task 状态+`UpdateFormToOnHandleAsync`(L145)→办结快照(L148-156)→**会签计票按 `(Inst,Node,Token)`**(L163-169)→`EvaluateNodeCounts`(L169)→写触达 `inst.ModifyDate`(L175)→`!decided` 停泊早退(L177-181)→`passed`：`SkipPendingFormTos`+`AdvanceToken`(L184-190) / `!passed`：`Rejected`+`CancelAllActiveTokens`+`VoidPendingFormTos`(L191-196)→`DispatchIfFinishedAsync`(L197)。**串簽改造点**。 |
| 会签三规则 | `FlowEngine.cs:220-235` | `EvaluateNodeCounts(approved,rejected,total,countersign)` 纯静态,any/veto/all。**每档复用,签名不动**。 |
| 进节点分发 | `FlowEngine.cs:238-245` | `EnterNodeAsync` → `inst.CurrentNode=node.Id` + `_handlers[type].OnEnterAsync(ctx)`,未知类型抛错。 |
| token 原语 | `FlowEngine.Tokens.cs:14-83` | `SpawnToken`(L14)/`ConsumeToken`(L27,Active 守卫)/`CancelAllActiveTokens`(L36,Local+DB 安全合并)/`FinishIfDrained`(L48)/`HasActiveToken`(L57)/`AdvanceToken`(L68,排他流转无后继即消费 drained)。 |
| 读模型钩子 | `FlowEngine.ReadModel.cs:12-161` | `NextStepSeq`(L12)/`WriteFormToOnSend`(L29,建 Pending 履历)/`UpdateFormToOnHandleAsync`(L52,按 `(Inst,Node,Token,ExpectedHandler,Pending)` 关单)/`WriteSnapshot`(L72)/`WriteCcAsync`(L82)/`SkipPendingFormTos`(L101)/`TransferFormToAsync`(L115)/`VoidPendingFormTos`(L142)。 |
| 高级动作 | `AdvancedFlow.cs:16-129` | `AddSignAsync`(L16,before/after,层上限 10)/`SetDelegateAsync`(L55)/`TransferAsync`(L78)/**`SendBackAsync(taskId,actor,targetNodeId,comment)`**(L100-129,作废全在途→`CancelAllActiveTokens`→`VoidPendingFormTos`→`SpawnToken`(target)→`EnterNodeAsync`)。**退回泛化点**。 |
| 实体 | `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs:11-53` | `BaseTenantEntity`,`TokenId`(L46),`IsRead`(L49)。**增 `StageIndex`**。 |
| 实体 | `Wf_FlowFormTo.cs:11-32` | 传签履历台账,`StepSeq`(L15)/`NodeId/NodeCode/NodeName`/`ExpectedHandlerId`/`Status`。**增 `StageIndex?`**。 |
| 状态常量 | `WfStatus.cs:4-43` | `FlowInstanceStatus`(Running0/Approved1/Rejected2/Withdrawn3/Suspended4/**Draft5**)、`FlowTaskStatus`(Pending0/Approved1/Rejected2/Cancelled3/Suspended4)、`FlowTokenStatus`、`FlowFormToStatus`(Pending0…Voided6)。 |
| 引擎接口 | `IFlowEngine.cs:7-49` | 对外签名;`SendBackAsync`(L27,注释已写"三落点只是 targetNodeId 不同")、`StartDraftAsync`(L44)。 |
| forecast | `CP6.Core/Services/Oa/ForecastService.cs:15-85` | `ForecastAsync` 逐节点前推(L15);`approval` 分支一节点一步(L54-59);`ResolveApproverNamesAsync`(L72);`NextNodeId` 排他边(L65)。**按档展开点**。 |
| 校验器 | `FlowSchemaValidator.cs:9-55` | 静态校验六规则,统一 `E-WF-010`;`KnownStrategies`(L6)。**增串簽档规则**。 |
| 设计器 model | `cp6.web/src/views/oa/designer/designerModel.ts:3-70` | `SchemaNode`(L3-11)/`NODE_PALETTE`(L15)/`schemaToGraph`(L24)/`graphToSchema`(L43)/`validateClient`(L61)。**增 stages 序列化 + 镜像校验**。 |
| 设计器面板 | `cp6.web/src/views/oa/designer/NodePropertyPanel.vue` | 审批节点属性面板(基本/進階/知會三段)。**增「串簽档位」段**。 |

**关键既有错误码**：`E-WF-002`(转交非法)/`E-WF-003`(草稿提交越权/非草稿)/`E-WF-006`(流程缺失)/`E-WF-009`(身份码重复)/`E-WF-010`(schema 非法)。

---

## §2 数据模型(改动极小,DB 仅 2 小列)

### §2.1 `ApprovalStage`(新,`FlowSchema.cs`)

```csharp
/// <summary>串簽档位(WFS 引擎深化)。一个 approval 节点可挂有序 Stages;空=单档(用节点既有字段)。</summary>
public class ApprovalStage
{
    public string? Name { get; set; }                 // 档名(信箱/forecast 显示)
    public string? Code { get; set; }                 // 人面档码(可空)
    /// <summary>档型：fixed=固定一组审批人;managerChain=沿 ManagerId 链逐级展开。</summary>
    public string Kind { get; set; } = "fixed";
    // ── fixed 档：一条审批人规则(同节点既有四字段语义) ──
    public string? ApproverStrategy { get; set; }     // DirectManager/DeptLeader/Role/Specified/Starter
    public int? ApproverLevels { get; set; }
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
    /// <summary>该档会签模式(串中带并)：all/any/veto。默认 all。</summary>
    public string Countersign { get; set; } = "all";
    // ── managerChain 档：封顶级数(到链顶或本上限,二者先到为止) ──
    public int? MaxLevels { get; set; }
}
```

`FlowNode` 增一字段(`FlowSchema.cs:58` Code 之后)：

```csharp
/// <summary>串簽档位序列(有序)。空/缺省=单档,用本节点 ApproverStrategy/Countersign(向后兼容)。</summary>
public List<ApprovalStage>? Stages { get; set; }
```

> **序列化进 `Wf_FlowDef.SchemaJson`,无 DB 列。** 旧 schema 无此字段 → 反序列化为 null → 单档路径。

### §2.2 `Wf_FlowTask.StageIndex`(新 DB 列)

`Wf_FlowTask.cs:46`(`TokenId` 之后)增：

```csharp
/// <summary>串簽档位序号(WFS 引擎深化)。会签计票按 (Inst,Node,Token,StageIndex) 隔离。
/// 默认 0：单档节点 / 旧数据 / 在途任务皆 0,语义不变,无需回填。</summary>
public int StageIndex { get; set; }
```

### §2.3 `Wf_FlowFormTo.StageIndex`(新,可空)

`Wf_FlowFormTo.cs` 增 `public int? StageIndex { get; set; }`(信箱 timeline / forecast 按档标号显示用;旧行 null)。

### §2.4 迁移

EF 迁移 `WfsSerialSign`：仅 `Wf_FlowTask.StageIndex`(int,default 0)+ `Wf_FlowFormTo.StageIndex`(int null)两列,**零数据搬迁、零回填**(默认 0 即单档语义)。索引不动(会签计票仍走既有 `(InstanceId,NodeId)` 查询,加内存 `StageIndex` 过滤)。

---

## §3 运行时(档位游标,方案 1)

### §3.1 `ResolveStageK` 纯函数(新,`ApprovalNodeHandler` 或 `FlowEngine` 私有)

把设计期 `Stages` 按序**展平成运行时档**,映射"第 K 档 → 解析任务"。签名(草案)：

```csharp
// 返回第 K 档的 (审批人规则, 会签模式);K 越界(无更多档) → null
internal static (ApproverRule rule, string countersign)? StageSpecAt(FlowNode node, int k, ...);
```

展平规则：
- **无 `Stages`(单档兼容)**：K=0 → `(BuildRule(node), node.Countersign)`(即今天的单规则);K≥1 → null。**这条保证 631 测试逐字等价**。
- **有 `Stages`**：按序遍历设计档,`fixed` 档占 1 个运行时档槽;`managerChain` 档占 1..N 个槽(沿链每级一槽)。运行时索引 K 落在哪个槽 → 取该档的 `(rule, countersign)`：
  - `fixed` → `ApproverRule(strategy, levels, roleId, userId)` + 该档 `Countersign`。
  - `managerChain` 第 j 级 → `ApproverRule(DirectManager, Levels=j, null, null)`(逐级走:第 1 级=直属主管、第 2 级=主管的主管…)+ 该档 `Countersign`(通常 all,单人)。链断(某级无上级)或 `j>MaxLevels` → 该 managerChain 档槽数到此为止。

> **确定性**：`fixed` 完全由 schema 决定;`managerChain` 每次从 starter 现查组织。"流程中途换主管 → 用新主管"是**可接受且通常更可取**的语义(文档化于 §11 边界)。`ApproverResolver.DirectManagerAsync`(`:30-48`)已能取第 N 级,逐级即对 j=1,2,3… 逐次调用,链顶天然返 `Unres` 作终止信号。

### §3.2 `ApprovalNodeHandler` 档化

`OnEnterAsync`(`:12-53`)重构为"**进入第 0 档**"：
- 单档(无 Stages):与今天**逐字等价**(解析 `BuildRule(node)`→建任务,`StageIndex=0`)。
- 多档:取 `StageSpecAt(node,0)`,解析审批人,建第 0 档任务(`StageIndex=0`),其余逻辑(stale 作废 / `step`/`WriteSnapshot` / `WriteFormToOnSend` / 通知)不变。
- 抽出私有 `EnterStageAsync(inst,schema,node,token,k)`：给定档号 K,解析该档审批人→建任务(打 `StageIndex=k` + `WriteFormToOnSend(stageIndex:k)`)→快照。`OnEnter` = `EnterStageAsync(...,0)`。**前进档 / 退回档复用同一入口**。

### §3.3 `ActOnceAsync` 档化

`ActOnceAsync`(`:130-199`)三处增量,签名与重试外壳(`:100-128`)**不动**：
1. **计票加档维度**(`:163-166`)：`nodeTasks` 过滤加 `&& t.StageIndex == task.StageIndex`(本档隔离)。`EvaluateNodeCounts`(`:220`)不动,逐档判。
2. **passed 推进改为"先推档,无档才推节点"**(`:184-190`)：
   ```
   passed:
     SkipPendingFormTos(本档兄弟 Pending → Skipped)   // 复用,加 StageIndex 过滤
     k1 = task.StageIndex + 1
     若 StageSpecAt(node, k1) 存在 → EnterStageAsync(inst,schema,node,token,k1)   // 同节点同 token 建下档
     否则 → AdvanceToken(inst,schema,token)                                       // 末档过 → 去下一节点
   ```
3. **!passed(驳回)不变**(`:191-196`)：整单 terminate(`Rejected`+`CancelAllActiveTokens`+`VoidPendingFormTos`)。退回是另一条动作路径(§4),不走这里。

> **写触达 `inst.ModifyDate`(`:175`)位置不动**——停泊(推下档前的 `!decided` 早退)与推进两条 mutating 路径仍都参与 RowVersion 乐观并发(串中带并某档并行会签的丢失唤醒防护,与今天等价,只是 scope 缩到档内)。

---

## §4 动作语义(驳回 vs 退回,对齐 Delta 双按钮)

### §4.1 驳回 = 整单 terminate(沿用现状)

`ActAsync(approve:false)` → §3.3 `!passed` 分支,整单 `Rejected`。**零改**。

### §4.2 退回 `SendBackAsync` 泛化三目标

`IFlowEngine.SendBackAsync` 签名扩(保留旧重载向后兼容)：

```csharp
// 新：target 描述退回落点;旧重载 SendBackAsync(taskId,actor,targetNodeId,comment) 转发为 Kind=node
Task SendBackAsync(Guid taskId, Guid actorId, SendBackTarget target, string? comment = null);

public sealed record SendBackTarget(string Kind, string? NodeId = null);  // Kind: prevStage | starter | node
```

实现(`AdvancedFlow.cs`,复用既有清场原语)：

| Kind | 行为 | 复用 |
|---|---|---|
| **`node`** | 现状逐字保留：作废全在途待办→`CancelAllActiveTokens`→`VoidPendingFormTos`→`SpawnToken`(target)→`EnterNodeAsync`。目标非前置/非审批/end → `E-WF-012`。 | `SendBackAsync` 原体(`:100-129`) |
| **`prevStage`** | 仅 `task.StageIndex>0` 合法(否则 `E-WF-012`)。作废**本档**在途任务(同 Token/Node/StageIndex)→ `VoidPendingFormTos`(本档 Pending)→ 游标减一 `EnterStageAsync(...,task.StageIndex-1)`(同 token,不动其他档已办履历)。不 terminate token(token 仍停本节点)。 | `EnterStageAsync`(§3.2)、`VoidPendingFormTos`(加档过滤) |
| **`starter`** | terminate token(`CancelAllActiveTokens`)+ `VoidPendingFormTos`(全实例)+ **实例回 `Draft`**(`FlowInstanceStatus.Draft=5`)归属发起人。发起人改后经既有 草稿→`StartDraftAsync`(`:72-92`)**整流程从头重跑**。 | `StartDraftAsync` 既有路径 |

> 三目标均 `AddHistory("sendback")` 追加(不删历史,与现状一致)。`prevStage` 是新增的**档内退回**,与 `node` 级正交。

### §4.3 动作接缝复用

`加签 AddSignAsync`(`:16`)/`委派 SetDelegateAsync`(`:55`)/`转交 TransferAsync`(`:78`)在**当前档内**照常工作：新建任务随原任务同 `TokenId`,须同步带 `StageIndex=原任务.StageIndex`(加签/转交计票方与原任务同档)。

---

## §5 读模型 + forecast + 信箱

### §5.1 传签履历(几乎免费,加档标号)

`WriteFormToOnSend`(`:29-46`)增 `stageIndex` 入参,落 `Wf_FlowFormTo.StageIndex`;`NodeName` 可附档名(`stage.Name`)。`UpdateFormToOnHandleAsync`(`:52`)/`SkipPendingFormTos`(`:101`)/`VoidPendingFormTos`(`:142`)关单/跳过/作废**加 `StageIndex` 过滤**(档内精确,不误伤同节点其他档履历)。`StepSeq` 每档经 `NextStepSeq`(`:12`)递增 → 信箱 `FlowTimeline` 按 `StepSeq` 排序天然顺序呈现各档。

### §5.2 forecast 按档展开

`ForecastService.ForecastAsync`(`:15`)的 `approval` 分支(`:54-59`)由"一节点一步"改为"**一节点 N 步**"：
- 无 `Stages` → 一步(现状)。
- 有 `Stages` → 展平(同 §3.1 规则)逐档 emit `ForecastStep`：`fixed` 档解析该档审批人名;`managerChain` 沿链前推 emit 各级主管名(链断/封顶即止)。
- `ForecastStep` 增 `stageIndex/stageName` 字段;`ResolveApproverNamesAsync`(`:72`)按档规则解析。发起预览与详情时间线共用(现状已共用)。

### §5.3 信箱左读右签

`FormDetail` 操作条按状态浮现「同意 / 驳回 / 退回 / 加签 / 转交」。**退回目标选择器**(新)：
- `prevStage`：仅当前任务 `StageIndex>0` 时显示「退回上一档」。
- `starter`：恒显示「退回发起人」。
- `node`：列出可退的前置审批节点「退回到…」。

`FlowTimeline` 渲染 `Wf_FlowFormTo` 行时,同 `NodeId` 多档按 `StageIndex` 分组显示「第 K 档 · 档名 · 审批人 · 状态」。读模型已驱动,增量仅显示层。

---

## §6 设计器(Vue Flow,C′ 之上)

**不新增节点类型**;审批节点(`approval`)`NodePropertyPanel.vue` 增「串簽档位」段：
- 勾「启用串簽」展开**有序档列表**(增 / 删 / 上移 / 下移),每档配：
  - 档型 radio：`固定` / `逐级`(managerChain)。
  - `固定`：审批人策略 + 角色/用户(复用既有 `userApi/roleApi` 远程搜索,同单档 UI)。
  - `逐级`：`MaxLevels`(封顶级数)。
  - 会签 `Countersign`：all/any/veto(串中带并)。
  - 档名 `Name`(可选)。
- 不勾「启用串簽」= 现有单档单规则 UI(`Stages` 不写,向后兼容)。

`designerModel.ts`：
- `SchemaNode`(`:3-11`)增 `stages?: ApprovalStage[]`;`schemaToGraph`(`:24`)/`graphToSchema`(`:43`)经 `...n`/`...(n.data)` 已透传,补 `stages` 字段穿透即可(vitest 验串簽往返互逆)。
- `validateClient`(`:61`)镜像后端新规则：`stages` 非空时每档须合法(`fixed` 须策略 / `managerChain` 须正 MaxLevels / countersign 合法 / 不得空档),错误 key `oa.designer.errStageInvalid`。

---

## §7 校验 / 错误码 / i18n

### §7.1 `FlowSchemaValidator` 增串簽规则(`:26-27` 后)

approval 节点新规则(任一不满足 → 加 `E-WF-011`)：
- `Stages` 为空 → 走既有规则⑤(须合法 `ApproverStrategy`)。
- `Stages` 非空 → 每档：`fixed` 须合法策略(`KnownStrategies`,`:6`);`managerChain` 须 `MaxLevels>=1`;`Countersign` ∈ {all,any,veto};档列表不得为空。

### §7.2 错误码(新增 2)

| 码 | 含义 |
|---|---|
| `E-WF-011` | 串簽档配置非法(空档 / fixed 缺策略 / managerChain 缺 MaxLevels / countersign 非法) |
| `E-WF-012` | 退回目标非法(prevStage 于第 0 档 / node 目标非前置审批节点 / 退到 end) |

> `E-WF-010` 仍承载结构性问题(节点/边/网关);串簽配置专用 `E-WF-011` 便于 UI 定位。

### §7.3 i18n

设计器档编辑 UI 文案(`oa.designer.stage.*`)+ 2 新错误码 × 五语(ZhCN/ZhTW/En/Ja/Ko),沿用 `I18nOa*ScreenSeed` 静态 `Sys_Lang[]` 模式,**grep 全 `t('oa.*')` 新键去重避开既有 seed**(`I18nOaInboxScreenSeed`/`I18nOaAdvancedScreenSeed`/`I18nOaDesignerScreenSeed`/`I18nOaNotify*`),接 `Program.cs` concat 链(带去重)。

---

## §8 兼容 / 迁移

1. **回归硬闸**：既有 Wf 测试(`dotnet test --filter Wf`：单档 + 并行 + 会签 + 退回 + 加签 + 委派 + 转交)**零改照绿** + 全量回归(基线 1294/1skip)不降。单档兼容靠 §3.1 "无 Stages → K=0 单规则 / K≥1 null"逐字等价 + `StageIndex` 默认 0。
2. **在途实例**：现存 Running/Suspended 实例任务 `StageIndex` 默认 0 = 单档语义,**无需回填**(区别于 token 内核当年须回填 token 行)。
3. **新列默认**：`Wf_FlowTask.StageIndex`(int,0)、`Wf_FlowFormTo.StageIndex`(int null)。EF 迁移仅加列。
4. **旧 `SendBackAsync(...,targetNodeId,...)` 重载保留**(转发 `Kind=node`),既有退回调用方零感知。

---

## §9 测试

**单元(后端,全量基线 1294/1skip → +N)：**
- 固定三档顺序推进(档 0 过→建档 1→过→建档 2→过→AdvanceToken→下一节点)。
- 逐级动态 N 档展开(沿 ManagerId 链:有 3 级则 3 档,链断即止 AdvanceToken;`MaxLevels=2` 封顶 2 档)。
- 串中带并某档会签 all/any/veto(档内多人,EvaluateNodeCounts 按档隔离)。
- 档计票隔离：两档不串台(`StageIndex` 过滤生效)。
- 退回 `prevStage`：档 2 退回 → 游标减一重建档 1、档 0 已办履历不动、token 不 terminate;第 0 档 prevStage → `E-WF-012`。
- 退回 `starter`：实例回 `Draft`、token cancelled、全 Pending 履历 Voided;`StartDraftAsync` 重提整流程从头跑。
- 退回 `node`：现状等价(token terminate + 目标节点重建)。
- 驳回(approve:false)某档 → 整单 `Rejected` + 兄弟连坐(沿用)。
- forecast 展开档(固定 + managerChain 链解析名字 + 封顶)。
- 校验四规则(空档 / fixed 缺策略 / managerChain 缺 MaxLevels / countersign 非法 → `E-WF-011`)。
- 迁移：`StageIndex` 默认 0,旧实例单档语义不变。
- **加签/转交在档内**：带 `StageIndex` 与原任务同档计票。

**前端(vitest)：** `designerModel` 串簽往返互逆(stages 穿透 `schemaToGraph`/`graphToSchema`)+ `validateClient` 镜像四规则。

**gstack 真浏览器 QA(隔离库 `CP6DB_OA`,harness `docs/superpowers/qa/wfs-serial-signing/`)：** 设计器配串簽流程(固定 2 档 + 逐级 1 档)→保存(校验过)→发起→逐档审(信箱 timeline 显档位)→退回上一档→再审→末档过→Approved;另跑驳回 terminate + 退回发起人重提。

---

## §10 分期(交付波次,plan 阶段细化为 T1~Tn)

| 期 | 范围 | 闸 |
|---|---|---|
| **P-A 引擎内核** | §2 数据模型 + 迁移 + §3.1 `StageSpecAt` + §3.2 handler 档化 + §3.3 ActOnce 档化 + 单档兼容闸 + 单测 | `--filter Wf` 既有零改照绿 + 串簽推进单测绿 |
| **P-B 退回泛化** | §4 `SendBackTarget` 三目标 + 旧重载转发 + 读模型档清理(Skip/Void 加档过滤)+ 单测 | 三退回路径单测绿 + 既有退回测零改 |
| **P-C 读模型/forecast/信箱** | §5 `StageIndex` 落库 + `WriteFormToOnSend` 档号 + forecast 档展开 + `FormDetail` 退回选择器 + timeline 显档 | forecast 单测 + 前端 type-check/vitest |
| **P-D 设计器** | §6 档编辑面板 + `designerModel` stages 往返 + `validateClient` + §7 校验/错误码/i18n | vitest 往返 + build + i18n check |
| **P-E gstack QA** | §9 全链真浏览器固化 | 7 剧本 PASS |

每 Task：全新 general-purpose subagent(sonnet) TDD → 控制器/diff 级复核(`git show` + 零 Space/零越界核验)→ 本地 commit 不 push;**零改引擎执行态硬闸**(`dotnet test --filter Wf` 既有照绿)。隔离 worktree `D:/CP6-wfs-serial` @ `feat/wfs-serial-sign`,绝不碰 `D:/CP6`(脏分支)/`D:/CP6-space-backend`(Space 会话)。

---

## §11 延后(YAGNI)与边界

- **延后**：串中带并嵌套网关/子流程;服务任务/WebAPI/JOB;inclusive 网关;跨并行块退回;条件跳档;运行时加档。
- **边界(文档化)**：①`managerChain` 每档现查组织,流程中途换主管用**新主管**(通常可取);②`managerChain` 与 token 内核并行网关组合时,串簽档在单 token 内顺序推进,不与 split/join 冲突(档不 spawn 子 token);③`prevStage` 退回不跨节点,第 0 档退回须用 `starter`/`node`;④`starter` 退回复用 Draft,流程从头跑(非从当前节点续)——若业务要"退回发起人补料后从当前节点续"属另一语义,YAGNI 不做。

---

## §12 决策锚点汇总表

| # | 决策 | 取值 |
|---|---|---|
| D1 | 串簽建模 | 方案 1：审批节点内多档 + 档位游标(token 停泊,游标推进) |
| D2 | 档形态 | 固定顺序 / 逐级动态(managerChain) / 串中带并(档内会签),三者全要 |
| D3 | 数据模型 | `FlowNode.Stages`(进 SchemaJson 无 DB 列)+ `Wf_FlowTask.StageIndex` + `Wf_FlowFormTo.StageIndex?`(DB 仅 2 列) |
| D4 | 会签计票 | 扩到 `(Inst,Node,Token,StageIndex)`,`EvaluateNodeCounts` 每档复用、签名不动 |
| D5 | 驳回 | `ActAsync(approve:false)` 保持整单 terminate(零改) |
| D6 | 退回 | `SendBackAsync` 泛化三目标 `prevStage`/`starter`/`node`;旧重载转发 node |
| D7 | 退回发起人 | 复用 `Draft` + `StartDraftAsync`,整流程从头重跑 |
| D8 | 逐级解析 | 复用 `ApproverResolver.DirectManager` 逐级(j=1,2,…)调用,链顶 Unres 终止 |
| D9 | 向后兼容 | 无 Stages → 单档逐字等价,`--filter Wf` 既有零改照绿;StageIndex 默认 0 无需回填 |
| D10 | 设计器 | 不新增节点类型,approval 面板加「串簽档位」段;designerModel stages 往返 + validateClient 镜像 |
| D11 | 错误码 | 新增 `E-WF-011`(档配置非法)/`E-WF-012`(退回目标非法) |
| D12 | 范围 | 全栈含设计器,五期 P-A~P-E;服务任务/inclusive/子流程/条件跳档 YAGNI |
