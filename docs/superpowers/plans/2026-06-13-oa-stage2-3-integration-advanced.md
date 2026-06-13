# OA 阶段2+3 · 接业务 + 复杂审批（章05集成 + 06规则 + 07高级流程）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**OA 第二份计划**（共三份）。依赖 **OA 阶段1 计划已落地**（FlowEngine/FormEngine/ApproverResolver/Wf 实体）。

**Goal:** 把可用 OA 接通业务 + 撑起真实复杂审批——阶段2 ★MVP（章05）：`IApprovalService`/`IApprovalCallback` 同步回调，兑现采购总纲里的 `IApprovalService` 桩，PR/PO/付款发起→通过→回调落地；阶段3（章06+07）：规则引擎（显隐/计算/联动 + 前后端共享求值器）+ 高级流程（退回/加签/超时/委派 + 状态清理）。

**Architecture:** 集成走**同步回调、依赖单向、OA 不碰业务表**——业务调 `IApprovalService.SubmitAsync` 起流程并喂 `formSnapshot`（OA 不回查业务表），OA 终态时 `ApprovalDispatcher` 按 `bizType` 找运行时注册的 `IApprovalCallback`（业务实现）同步直调，业务在回调里走自己的状态机/BridgeHook 落地。规则引擎与流程 `condition` **共用一个安全表达式求值器**（C# `ExpressionEvaluator` + TS `ruleEngine`，同语义，禁 eval）。高级动作的难点全在**动作后的状态清理**（退回作废待办/加签实例级临时待办/超时幂等扫描/委派建时替换），`FlowHistory` 永远只追加。

**Tech Stack:** .NET 8 + EF Core 8 + HostedService（超时扫描）+ SignalR（催办）/ xUnit + EF Core InMemory / Vue 3.5 + element-plus。源文档：`docs/approval/05·06·07`。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状/对账 | **本稿建议值** |
|---|---|---|---|
| **OA2-D1** | **采购/财务 callback 归属** | 章05 给 `PoApprovalCallback` 等示范（落各业务模块） | 本计划**只做 OA 侧契约 + Dispatcher + 一个示范 callback**（建议采购 PO）；采购/财务**全量** callback + 起流程调用属各业务模块改造（B3 阶段）。本计划不替换采购真实 `IApprovalService` 桩的所有调用点，只立实现 + 示范接通 |
| **OA2-D2** | **表达式求值器共享** | 章03 condition（阶段1 已建 `ConditionEvaluator`）+ 章06 rules 同源 | **统一为 `ExpressionEvaluator`**：扩展阶段1 的 `ConditionEvaluator`（加内置函数 dateDiff/sum 等 + 字段引用白名单），流程 condition 与表单 rules 后端复算共用；前端 `ruleEngine.ts` 同语义实现（语言克制到 C#/TS 两端可一致实现） |
| **OA2-D3** | **FlowTask 扩字段** | 阶段1 FlowTask 无 AddSignSource/DueAt/TimeoutHandled，Status 仅 0/1/2 | **给 FlowTask 加** `AddSignSource`(string?)/`DueAt`(DateTime?)/`TimeoutHandled`(bool)；Status 扩 **3=作废**（退回）/**4=挂起**（前加签）。新增迁移改 Plan1 的 FlowTask 表 |
| **OA2-D4** | **超时扫描宿主** | 章07 HostedService；CP6 有 Phase6 重试 Worker 范式 | **HostedService `TimeoutScanService`**（仿现有 `IntegrationEventRetryWorker`），周期扫 DueAt 过期待办；多实例加分布式锁/抢占（v1 单实例，留注） |
| **OA2-D5** | **回调失败处理** | 章05 同步回调失败 → 终态事务回滚 + 告警，不静默吞 | 回调抛异常 → 终态分发回滚 + `IDeadLetterNotifier`（复用现有）告警；可重放终态分发。**绝不出现"OA 已通过、业务没落地"** |
| **OA2-D6** | **PUB 字段权限叠加** | 章04（阶段1）留的 OA-D5；本计划规则显隐 ∩ PUB 字段权限 | 同阶段1：节点/规则字段控制与 PUB B1 角色字段权限取交集（更严赢），PUB B1 落地后接，本计划不阻塞 |

> **测试基建**：xUnit + InMemory。Dispatcher 回调路由/防重、求值器、会签计票（含加签）、退回清理、超时幂等可纯单测；HostedService 用时间注入测扫描逻辑。

---

## File Structure

### 阶段2 集成（章05）
- `CP6.Core/Services/Wf/IApprovalService.cs`/`ApprovalService.cs`（SubmitAsync/GetStatusAsync）
- `CP6.Core/Services/Wf/IApprovalCallback.cs`（业务实现的契约）+ `ApprovalDispatcher.cs`（终态分发，按 bizType 找 callback）
- `CP6.Entity/DomainModels/Wf/Wf_ApprovalBinding.cs`（bizType→flowKey，可条件选流程）
- 修改 `FlowEngine`（终态钩 ApprovalDispatcher.OnInstanceFinished）
- 示范：`CP6.Core/Services/Pur/PoApprovalCallback.cs`（OA2-D1）

### 阶段3 规则引擎（章06）
- `CP6.Core/Services/Wf/ExpressionEvaluator.cs`（扩展阶段1 ConditionEvaluator，共享）
- `cp6.web/src/views/wf/ruleEngine.ts`（applyRules：when→then 显隐/计算/联动）+ 接入 DynamicForm（watch）
- 后端规则复算（FormService 提交时按 rules 重算 compute/复核 require）

### 阶段3 高级流程（章07）
- `CP6.Entity/DomainModels/Wf/Wf_FlowDelegate.cs`；修改 `Wf_FlowTask`（+AddSignSource/DueAt/TimeoutHandled）
- `CP6.Core/Services/Wf/AdvancedFlow.cs`（SendBack/AddSign/委派 ResolveActualAssignee）
- `CP6.WebApi/BackgroundServices/TimeoutScanService.cs`（HostedService）
- 修改 `FlowEngine.EvaluateNode`（计入加签待办）、`EnterNode`（委派替换 assignee + 建待办设 DueAt）

### 控制器 + 迁移 + 测试
- `CP6.WebApi/Controllers/Wf/AdvancedFlowController.cs`（退回/加签/委派维护）；迁移 `*_OaStage2_3`
- 测试：`ApprovalDispatcherTests`、`ExpressionEvaluatorTests`、`AdvancedFlowTests`（退回清理/加签计票/委派）、`TimeoutScanTests`

---

## 实施分三阶段

- **Phase A**（A-1..A-3）：阶段2 集成 ★MVP（章05）—— IApprovalService 兑现
- **Phase B**（B-1..B-2）：阶段3 规则引擎（章06）—— 表单"活"起来
- **Phase C**（C-1..C-4）：阶段3 高级流程（章07）—— 退回/加签/超时/委派

---

# Phase A — 阶段2 集成（章05 ★MVP）

## Task A-1: IApprovalService + ApprovalBinding + ApprovalDispatcher（章05 §2/§3/§4）★

**Files:** Create `IApprovalService.cs`/`ApprovalService.cs`, `IApprovalCallback.cs`, `ApprovalDispatcher.cs`, `Wf_ApprovalBinding.cs`; Modify `CP6Context.cs`, `FlowEngine.cs`; migration; Test `ApprovalDispatcherTests.cs`

- [ ] **Step 1: 失败测试**（Submit 按 binding 选 flowKey 起实例 + 防重[同 bizType+bizId 进行中拒绝]；终态 approved→找 bizType 的 callback 调 OnApproved；未注册 callback→抛；OA 原生[BizType 空]→不回调）

```csharp
public class ApprovalDispatcherTests
{
    class FakeCb : IApprovalCallback {
        public string BizType => "PO"; public List<Guid> Approved = new();
        public Task OnApprovedAsync(Guid id){ Approved.Add(id); return Task.CompletedTask; }
        public Task OnRejectedAsync(Guid id, string? r) => Task.CompletedTask;
    }
    [Fact]
    public async Task Finished_Approved_CallsBizCallback()
    {
        var cb = new FakeCb();
        var disp = new ApprovalDispatcher(new IApprovalCallback[]{ cb });
        var inst = new FlowInstance { Id = Guid.NewGuid(), BizType = "PO", BizId = Guid.NewGuid() };
        await disp.OnInstanceFinishedAsync(inst, approved:true, reason:null);
        Assert.Contains(inst.BizId, cb.Approved);
    }
    [Fact]
    public async Task Finished_NativeForm_NoCallback()
    {
        var disp = new ApprovalDispatcher(Array.Empty<IApprovalCallback>());
        var inst = new FlowInstance { BizType = "" };
        await disp.OnInstanceFinishedAsync(inst, true, null);   // 不抛
    }
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
// IApprovalService.cs / IApprovalCallback.cs（照章05 §2）
public interface IApprovalService {
    Task<Guid> SubmitAsync(string bizType, Guid bizId, Guid starterId, object formSnapshot);
    Task<ApprovalStatus> GetStatusAsync(string bizType, Guid bizId);
}
public interface IApprovalCallback {
    string BizType { get; }
    Task OnApprovedAsync(Guid bizId);
    Task OnRejectedAsync(Guid bizId, string? reason);
}
```
```csharp
// ApprovalDispatcher.cs（终态分发，运行时多态回调）
public class ApprovalDispatcher
{
    private readonly IEnumerable<IApprovalCallback> _callbacks;
    public ApprovalDispatcher(IEnumerable<IApprovalCallback> callbacks) => _callbacks = callbacks;
    public async Task OnInstanceFinishedAsync(FlowInstance inst, bool approved, string? reason)
    {
        if (string.IsNullOrEmpty(inst.BizType)) return;                 // OA 原生，无回调
        var cb = _callbacks.FirstOrDefault(c => c.BizType == inst.BizType)
                 ?? throw new InvalidOperationException($"未注册 BizType={inst.BizType} 的回调");
        if (approved) await cb.OnApprovedAsync(inst.BizId);             // 同步直调
        else          await cb.OnRejectedAsync(inst.BizId, reason);
    }
}
```
```csharp
// ApprovalService.SubmitAsync：防重 + 按 binding 选 flowKey + 起实例（喂 snapshot 作 Vars）
public async Task<Guid> SubmitAsync(string bizType, Guid bizId, Guid starter, object snapshot)
{
    if (await _db.FlowInstances.AnyAsync(i => i.BizType == bizType && i.BizId == bizId && i.Status == 0))
        throw new InvalidOperationException("该单据已有进行中的审批");
    var binding = await _db.Wf_ApprovalBindings.FirstOrDefaultAsync(b => b.BizType == bizType)
                  ?? throw new InvalidOperationException($"未配置 {bizType} 的审批流程");
    var flowKey = SelectFlowByCondition(binding, snapshot);            // ConditionJson 选流程
    return await _flowEngine.SubmitAsync(flowKey, bizType, bizId, starter, snapshot);
}
```
`FlowEngine` 终态处（EnterNode 到 end / 节点被否）调 `_dispatcher.OnInstanceFinishedAsync(inst, approved, reason)`，回调失败 → 事务回滚 + 告警（OA2-D5）。

- [ ] **Step 4: 跑绿 → Step 5: 实体 Wf_ApprovalBinding + 迁移 + DI（注册所有 IApprovalCallback）+ 提交** → `git commit -m "feat(wf): IApprovalService + ApprovalDispatcher sync callback (ch05 §2-4)"`

## Task A-2: 防重 + 回调幂等 + 失败回滚告警（章05 §6，OA2-D5）

**Files:** Modify `ApprovalService.cs`/`FlowEngine.cs`; Test

- [ ] **Step 1-4: 失败测试 + 实现**——SubmitAsync 防重复提交（A-1 已含）；终态分发失败 → 回滚 + `IDeadLetterNotifier` 告警；回调侧建议幂等（示范 callback 按状态判断）。提交。

## Task A-3: 示范业务 callback（采购 PO，章05 §4/§7，OA2-D1）

**Files:** Create `CP6.Core/Services/Pur/PoApprovalCallback.cs`; DI; Test

- [ ] **Step 1-5:** 实现 `PoApprovalCallback : IApprovalCallback`（BizType="PO"，OnApproved 置 PO 状态机为已确认[幂等：已确认跳过]、走原有 Hook；OnRejected 置回草稿）+ 注册 + 配 ApprovalBinding["PO"] + 集成测：起 PO 审批→通过→PO 已确认。**这条即 MVP 兑现**（采购 IApprovalService 桩换实现）。

> **注**：采购/财务**全量**接入（所有单据类型 + 起流程调用点替换）属各业务模块 B3 改造，本计划只做 PO 示范打样 + 立 OA 侧实现。

```bash
git commit -m "feat(wf): sample PO approval callback — MVP IApprovalService fulfilled (ch05 §7)"
```

---

# Phase B — 阶段3 规则引擎（章06）

## Task B-1: ExpressionEvaluator 共享求值器（章06 §5，OA2-D2）

**Files:** Modify/扩展 `CP6.Core/Services/Wf/ExpressionEvaluator.cs`（含阶段1 ConditionEvaluator）; Test `ExpressionEvaluatorTests.cs`

- [ ] **Step 1: 失败测试**（比较/逻辑 `days>3 && type=='annual'`；内置函数 `dateDiff(a,b)+1`/`sum(...)`；字段白名单；非法表达式安全失败不 eval）
- [ ] **Step 2: 跑红 → Step 3: 实现**（把阶段1 ConditionEvaluator 升级为 ExpressionEvaluator：白名单字段引用 + 比较/逻辑 + 内置函数表 dateDiff/sum/cityOf；递归下降或受限库；流程 condition 与表单规则后端复算共用此一份）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(wf): shared ExpressionEvaluator (condition + form rules, no eval) (ch06 §5)"`

## Task B-2: 前端 ruleEngine + DynamicForm 接入 + 后端复算（章06 §3/§4/§6）

**Files:** Create `cp6.web/src/views/wf/ruleEngine.ts`; Modify `DynamicForm.vue`, `FormService.cs`; Test（前端 vitest applyRules）

- [ ] **Step 1: 失败测试（vitest）**（applyRules：when 成立→show/hide/require/compute/setOptions 生效；compute 写回 model；循环依赖一轮不级联）
- [ ] **Step 2: 跑红 → Step 3: 实现**——`applyRules(schema, model)→effect{visible/required/disabled/options}`（照章06 §4）；DynamicForm `watch(model)` 重算 effect 驱动显隐/禁用/选项；后端 FormService 提交时按 rules **重算 compute / 复核 require**（OA2 铁律：前端体验、后端边界，与 B-1 同一求值器）。
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(wf): rule engine (show/compute/link) + backend recompute (ch06 §3/§4/§6)"`

---

# Phase C — 阶段3 高级流程（章07）

## Task C-1: FlowTask 扩字段 + Wf_FlowDelegate + 迁移（章07 §3/§5，OA2-D3）

**Files:** Modify `Wf_FlowTask.cs`(+AddSignSource/DueAt/TimeoutHandled); Create `Wf_FlowDelegate.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 改实体 + 新实体 + 注册**（FlowTask 加 `string? AddSignSource`/`DateTime? DueAt`/`bool TimeoutHandled`；Status 语义扩 3=作废/4=挂起；FlowDelegate[GrantorId/DelegateId/ValidFrom/ValidTo/Scope]）
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(wf): FlowTask advanced fields + Wf_FlowDelegate (ch07 §3/§5)"`

## Task C-2: 退回 SendBack（作废清理 + 重建待办，章07 §2）★

**Files:** Create `AdvancedFlow.cs`(SendBackAsync); Test `AdvancedFlowTests.cs`

- [ ] **Step 1: 失败测试**（退回到目标节点→作废"目标及之后"在途待办[Status=3]、清会签计票、CurrentNode 回退、重建目标待办；FlowHistory 追加 sendback 不删）

```csharp
[Fact]
public async Task SendBack_VoidsDownstreamTasks_AndRebuilds()
{
    // 实例走到 n2，退回 n1 → n2 在途 task 置 3 作废，CurrentNode=n1，n1 重建待办，history 多一条 sendback
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照章07 §2：AddHistory→作废目标之后在途待办[需"已走节点序列"判 IsAfterOrAt，物化在 VarsJson 或单独 path]→CurrentNode=target→EnterNode 重建。三落点[上一步/指定节点/发起人]只是 targetNodeId 不同）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(wf): send-back with downstream task voiding (ch07 §2)"`

## Task C-3: 加签 AddSign（实例级临时待办 + EvaluateNode 计入，章07 §3）★

**Files:** Modify `AdvancedFlow.cs`(AddSignAsync), `FlowEngine.cs`(EvaluateNode); Test

- [ ] **Step 1: 失败测试**（后加签→加临时 FlowTask[AddSignSource=after]，节点未流转直到加签人审完；前加签→原 task 挂起[Status=4]，加签人[before]审完再激活原审批人；EvaluateNode 的 total 含加签待办；加签上限）
- [ ] **Step 2: 跑红 → Step 3: 实现**（照章07 §3：AddSignAsync 加实例级 task 不改 FlowDef；before→原 task Status=4 挂起；EvaluateNode 计票把 AddSignSource 待办算进 total；前加签人审完激活挂起的原 task；加签层数上限闸门）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(wf): add-sign (before/after) instance-level + countersign integration (ch07 §3)"`

## Task C-4: 超时扫描 + 委派 + 控制器（章07 §4/§5）

**Files:** Create `TimeoutScanService.cs`(HostedService); Modify `AdvancedFlow.cs`(委派)/`FlowEngine.cs`(EnterNode 委派替换 + 设 DueAt)/`AdvancedFlowController.cs`; Test `TimeoutScanTests.cs`

- [ ] **Step 1: 失败测试**（超时扫描：DueAt 过期未处理→按策略 remind/approve/reject/escalate；`TimeoutHandled` 幂等[处理过不再处理]；委派：建待办时在委派期→assignee 替换为代理人，痕迹双记"代 X 审批"）
- [ ] **Step 2: 跑红 → Step 3: 实现**——TimeoutScanService（HostedService，仿 IntegrationEventRetryWorker，周期扫 DueAt<now ∧ !TimeoutHandled，按节点 timeout.action 处理：remind 软动作[可重复+重设 DueAt]/approve/reject/escalate 硬动作[一次性，调 FlowEngine.ActAsync 复用幂等闸门]+置 TimeoutHandled）；EnterNode 建 task 时 ResolveActualAssignee（委派期替换）+ 按节点 timeout 设 DueAt；委派双痕。多实例分布式锁留注（v1 单实例）。
- [ ] **Step 4: 跑绿 → Step 5: AdvancedFlowController（退回/加签/委派维护）+ HostedService 注册 + 提交** → `git commit -m "feat(wf): timeout scan (idempotent) + delegation (assignee swap, dual trace) (ch07 §4/§5)"`

---

## Self-Review（对照章05/06/07 覆盖）

- **章05**：IApprovalService/Callback(A-1) ✅ / ApprovalBinding 选流程(A-1) ✅ / ApprovalDispatcher 终态同步回调(A-1) ✅ / 编译期单向+运行时多态回调(A-1/A-3) ✅ / 防重+回调幂等+失败回滚告警(A-2) ✅ / 采购 PO 示范兑现桩(A-3) ✅ / formSnapshot 业务喂不回查(A-1) ✅
- **章06**：四类规则显隐/计算/联动/必填(B-2) ✅ / rule schema(B-2) ✅ / 监听变化→匹配→应用循环(B-2) ✅ / 安全求值器共享(B-1) ✅ / 前端体验+后端复算(B-2) ✅ / 循环依赖一轮不级联(B-2) ✅
- **章07**：退回作废清理(C-2) ✅ / 加签前后实例级+计票(C-3) ✅ / 超时四策略+双幂等(C-4) ✅ / 委派建时替换+双痕(C-4) ✅ / FlowHistory 只追加(C-2/全) ✅ / FlowTask 扩字段+Status 3/4(C-1) ✅

**已知缺口/推迟（已标注）：**
1. **采购/财务全量接入**（OA2-D1）—— 各业务模块 B3 改造，本计划只做 PO 示范 + OA 侧实现。
2. **PUB 字段权限 ∩ 规则/节点显隐**（OA2-D6）—— PUB B1 落地后接。
3. **超时多实例分布式锁**（OA2-D4）—— v1 单实例，留注。
4. **已存量待办转派**（章07 §5）—— v1 仅"建待办时替换"，存量转派后续增强。
5. **加签无限层**（章07 §7）—— 设上限闸门（C-3 含）。

**Type 一致性：** `IApprovalService.SubmitAsync`(A-1) 调阶段1 `FlowEngine.SubmitAsync`；`ApprovalDispatcher.OnInstanceFinished`(A-1) 由 FlowEngine 终态调用；`ExpressionEvaluator`(B-1) 流程 condition[阶段1]+表单 rules[B-2]共用；`FlowTask` 扩字段(C-1) 被 AddSign(C-3)/超时(C-4)/EvaluateNode 计票一致用；委派 ResolveActualAssignee(C-4) 在 EnterNode[阶段1]接入。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-oa-stage2-3-integration-advanced.md`。**OA 第二份（阶段2 MVP + 阶段3 复杂审批）**。后续：
- OA Plan 3 = `2026-06-13-oa-stage4-designers.md`（章09 自研表单/流程设计器 + 章10 多租户商业化）

**下一步按工作流是你修订**（拍板 OA2-D1~D6）。定稿后执行：OA 阶段1 → **阶段2(接采购/财务 MVP)** → 阶段3(规则+高级流程)；阶段2 独立有价值，可先于阶段3 上线。

---

*初稿生成于 2026-06-13。源：docs/approval/05·06·07。已勘察：阶段1 FlowEngine/FormEngine 为前置、IntegrationEventRetryWorker(超时扫描范式)/IDeadLetterNotifier/SignalR 现成、采购 IApprovalService 桩待接、表达式求值器前后端共享、Wf 实体扩字段。*
