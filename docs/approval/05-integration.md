# 05 · 与 CP6 集成：同步回调接通采购 / 财务

> **阶段 2 · ★MVP 价值点。** 前四章做的是 OA 自己。本章把它和业务模块接通：采购 PR/PO、财务付款单需要审批时，调 `IApprovalService` 起流程；OA 审批终态时，**同步回调** `IApprovalCallback` 让业务去落地。本章结束时，采购总纲里那个一直是桩的 `IApprovalService` 被真正实现——"谁来批"对全 CP6 生效。
>
> 上游：[03 流程引擎](./03-flow-runtime.md)（终态）。对接：[采购总纲](../procurement/README.md)（`IApprovalService` 桩）、[财务 AP/付款](../finance/README.md)。原则：[总纲题眼](./README.md)（审批不侵入业务、依赖单向、不双写）。

---

## 一、题眼：OA 不碰业务表，只回调

能被审批的东西有两类，处理方式不同：

| 类型 | 例子 | 数据在哪 | OA 怎么管 |
|---|---|---|---|
| **OA 原生表单** | 请假/报销/用章 | OA 的 `FormData`（02章） | 全托管，终态即结束，无需回调 |
| **ERP 业务单据** | 采购 PR/PO、财务付款 | **各模块自己的表**，结构复杂 | OA 只"挂"一层审批，终态**回调业务去落地** |

本章只讲第二类。核心铁律：

> **业务单据的状态唯一真相在它自己的模块。OA 只负责"审批中 → 通过/驳回"，终态时同步调一个业务实现的回调，由业务自己走状态机落地。OA 绝不写业务表。**

这与采购"同步可调试、依赖单向、不双写"完全一致。OA → 业务是**一条同步直线**，不是异步事件——调试时能一步步跟下去，不在死信里捞。

---

## 二、两个接口：起流程 + 回调

```csharp
// CP6.Core/Services/Wf/IApprovalService.cs —— 业务调 OA（OA 实现）
public interface IApprovalService
{
    /// 业务发起审批，返回流程实例 Id
    Task<Guid> SubmitAsync(string bizType, Guid bizId, Guid starterId, object formSnapshot);
    /// 查审批状态
    Task<ApprovalStatus> GetStatusAsync(string bizType, Guid bizId);
}

// CP6.Core/Services/Wf/IApprovalCallback.cs —— OA 调业务（业务实现）
public interface IApprovalCallback
{
    string BizType { get; }                                   // 我处理哪类单据，如 "PO"
    Task OnApprovedAsync(Guid bizId);                         // 审批通过 → 业务落地
    Task OnRejectedAsync(Guid bizId, string? reason);        // 审批驳回 → 业务回退
}
```

- `bizType`：单据类型字符串（`PR`/`PO`/`Payment`…），是 OA 与业务之间的契约键。
- `formSnapshot`：业务把单据关键字段（金额、部门等）作为快照传给 OA，供流程 `condition`/审批人解析用——**OA 不回查业务表，业务主动喂快照**，依赖单向。
- `IApprovalCallback` 由**各业务模块实现并注册**，每个声明自己的 `BizType`。

---

## 三、绑定：哪类单据走哪条流程

```csharp
// CP6.Entity/DomainModels/Wf/ApprovalBinding.cs
[Table("Wf_ApprovalBinding")]
public class ApprovalBinding : BaseEntity
{
    public string BizType { get; set; } = "";   // PO
    public string FlowKey { get; set; } = "";    // 走哪条流程定义
    public string? ConditionJson { get; set; }   // 可选：按金额等选不同流程
}
```

`SubmitAsync` 按 `bizType` 查 `ApprovalBinding` 选出 `flowKey`，再交给 [03 章流程引擎](./03-flow-runtime.md)起实例。"金额>10万走更长的流程"这种就用 `ConditionJson` 选不同 `FlowKey`。

---

## 四、完整时序：一张采购 PO 的审批

```
采购：建 PO（草稿）→ 调 IApprovalService.SubmitAsync("PO", poId, buyerId, {amount, deptId})
  OA：查 ApprovalBinding["PO"] → flowKey="po_approval"
      → 起 FlowInstance(BizType="PO", BizId=poId, Vars=快照)
      → 流转（01 算审批人 / 03 状态机 / 会签…）
  OA：走到 end（通过）
      → 找 BizType="PO" 的 IApprovalCallback
      → 同步调 OnApprovedAsync(poId)
  采购：在回调里走自己的状态机 —— PO 置"已确认"，触发 IWms/IFinApService 等原有 Hook 落地
       （或驳回 → OnRejectedAsync → PO 置回"草稿/驳回"）
```

OA 侧的终态分发（在 [03 章](./03-flow-runtime.md) `EnterNode` 到达 end / 节点被否时调用）：

```csharp
// CP6.Core/Services/Wf/ApprovalDispatcher.cs
public async Task OnInstanceFinishedAsync(FlowInstance inst, bool approved, string? reason)
{
    if (string.IsNullOrEmpty(inst.BizType)) return;          // OA 原生表单，无业务回调
    var cb = _callbacks.FirstOrDefault(c => c.BizType == inst.BizType)
        ?? throw new InvalidOperationException($"未注册 BizType={inst.BizType} 的回调");
    if (approved) await cb.OnApprovedAsync(inst.BizId);       // ★同步直调，一条直线
    else          await cb.OnRejectedAsync(inst.BizId, reason);
}
```

业务侧实现（采购）：

```csharp
// CP6.Core/Services/Pur/PoApprovalCallback.cs —— 采购模块实现并注册
public class PoApprovalCallback : IApprovalCallback
{
    public string BizType => "PO";
    public async Task OnApprovedAsync(Guid poId)
    {
        var po = await _db.PurchaseOrders.FindAsync(poId);
        po.Status = PoStatus.Confirmed;        // ★业务自己改自己的状态机
        await _db.SaveChangesAsync();
        // 走采购原有落地（同步委托 WMS 入库准备 / 触发后续）
    }
    public async Task OnRejectedAsync(Guid poId, string? reason) { /* 置回草稿，记原因 */ }
}
```

> **依赖方向**：采购 `→` 引用 `IApprovalService`/`IApprovalCallback`（在 Wf 命名空间），Wf **不引用**采购。回调靠运行时注册的 `IApprovalCallback` 列表反向找回业务——**编译期单向依赖，运行时多态回调**，这是低耦合的关键手法。

---

## 五、为什么同步回调，而不是 Phase 6 事件

CP6 财务模块走 Phase 6 **异步事件**（IntegrationEvent + 重试/死信，最终一致）。采购和审批刻意走**同步接口**。区别与理由：

| | Phase 6 事件（财务内部） | 同步回调（OA↔业务） |
|---|---|---|
| 触发 | 发事件，订阅者异步处理 | 直接调接口，当场返回 |
| 一致性 | 最终一致 | 立即一致 |
| 调试 | 要在事件/死信里追 | 一条调用栈跟到底 |
| 适用 | 模块内可容忍延迟的扩散 | 跨模块"批了就得落地"的动作 |

> **边界划清**：审批引擎**内部**流转（建待办、超时扫描）可以用 IntegrationEvent 异步驱动、幂等补偿；但**跨模块**（OA→采购/财务）只走同步回调。"收货→入库""批了→确认 PO"这类动作，调试时要能一步步跟，不该丢进异步黑盒。这是总纲反复强调的"同步可调试"。

---

## 六、防重与幂等

- **防重复提交**：`SubmitAsync` 先查该 `(bizType,bizId)` 是否已有进行中的实例，有则拒绝/返回原实例——一张单不能同时跑两条流程。
- **回调幂等**：`OnApprovedAsync` 业务侧要幂等（按 PO 当前状态判断，已确认就跳过）——万一回调因网络重试调了两次，不会把单据落地两遍。
- **回调失败**：同步回调抛异常 → 整个终态事务回滚 + 告警，**不静默吞**。下次可重放终态分发。绝不出现"OA 显示已通过、业务没落地"的割裂。

---

## 七、接入清单（采购 / 财务）

| 业务 | bizType | 起流程时机 | 回调落地 |
|---|---|---|---|
| 采购申请 PR | `PR` | PR 提交 | 通过→可转 PO；驳回→退回申请人 |
| 采购订单 PO | `PO` | PO 提交确认前 | 通过→PO 确认、走 WMS/AP；驳回→草稿 |
| 财务付款 | `Payment` | 付款单提交 | 通过→允许出纳付款；驳回→退回 |

> 采购总纲里 `IApprovalService` 原是"单人/跳过"的桩。本章把它换成 OA 实现：注册各 `IApprovalCallback`、配 `ApprovalBinding`，采购/财务**代码不大改**——它们本来就是面向 `IApprovalService` 接口编程的，桩换实现即可。这就是当初留接口的回报。

---

## 八、资深视角

**为什么让业务实现回调，而不是 OA 直接改业务表？** 因为业务落地有自己的规矩（PO 确认要连带触发 WMS、AP、库存预留）。OA 不可能懂每个模块的落地逻辑，也不该懂。**OA 只负责"批没批"，落地交还给最懂的人**——这就是关注点分离，也是"OA 不侵入业务"的工程含义。

**formSnapshot 为什么由业务喂、OA 不回查？** 若 OA 回查业务表，就形成 OA→业务的反向依赖、且 OA 要懂业务表结构——耦合爆炸。业务主动把审批需要的字段拍平成快照传过来，OA 只认快照，依赖永远单向。

**这一章才是 MVP 的钱**：前四章是"OA 能自己跑"，这一章是"OA 让采购/财务受益"。没有它，审批引擎只是个孤岛；有了它，采购总纲承诺的"PR/PO 审批"立刻兑现，财务付款也有了管控。

---

## 九、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 审批中台对接业务 | **钉钉审批 回调/事件订阅** | bizType 路由、审批结果回写业务 |
| 接口桩→实现替换 | **依赖倒置（DIP）** | 业务面向 `IApprovalService` 编程，桩换真身零改动 |
| 同步 vs 异步边界 | **SAP 工作流 + 业务对象** | 审批结果同步回写业务对象状态 |

---

## 十、阶段2 自检

- [ ] OA 为什么不直接改业务表？谁负责把 PO 置为已确认？
- [ ] `formSnapshot` 为什么由业务喂、OA 不回查业务表？这保证了什么？
- [ ] OA→业务为什么用同步回调而非 Phase 6 事件？引擎内部又能不能用事件？
- [ ] 编译期依赖是单向的（采购→Wf），运行时回调怎么反向找回业务？
- [ ] 回调失败/重复回调分别怎么处理？为什么不能静默吞？
- [ ] 采购总纲的 `IApprovalService` 桩，怎么在这一章零大改地换成实现？

全部能答 → **MVP 闭合**：审批引擎从"自己能跑"升级为"让采购/财务受益"，采购的 `IApprovalService` 立住。后续 [06 规则](./06-rule-engine.md)、[07 高级流程](./07-advanced-flow.md) 接真实复杂审批，[09 集成](./09-integration.md) 收口四接口与 Phase 6 边界。

---

*实现落 `CP6.Core/Services/Wf/{ApprovalService,ApprovalDispatcher}.cs`，业务回调落各模块 `CP6.Core/Services/{Pur,Fin}/*ApprovalCallback.cs`。DI 注册所有 `IApprovalCallback` 实现。*
