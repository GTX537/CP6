# 08 · 与 CP6/财务集成

> **Part 3 · 把散落的接口收口成一张图。** 前面 01-07 每章各自调了几个外部接口——03 调 WMS 收货/QC、07 调 WMS 发料、04 调财务建票、05/02 调审批桩。本章不引入新业务，只做一件事：**把这五个接口的契约、依赖方向、注册方式、桩→真实的切换统一讲清**，并回答一个贯穿全书的设计问题——**采购为什么全程走同步接口，而财务内部走 Phase 6 异步事件，边界划在哪**。本章结束时，采购模块"对外只有五根明确的线，每根都单向、可追、可替换"。
>
> 上游：全部前序章节（接口的调用点）。对接：[WMS](../../README.md)（收/发/QC 实现）、[财务 AP](../finance/03-accounts-payable.md)（建票实现）、[OA 审批 05](../approval/05-integration.md)（`IApprovalService` 实现）。

---

## 一、题眼：对外五根线，根根单向、可追、可替换

采购模块"自洽 + 依赖单向"的总纲原则，落到代码就是这五个接口：

> **采购拥有全部单据与逻辑（PR/RFQ/PO/GR/匹配），物理资源和应付通过接口单向委托出去。接口契约定义在采购侧（采购说"我要什么"），由 WMS/财务/OA 实现（它们说"我怎么做"）。采购编译期只依赖接口、不依赖任何外部模块的实现——根根单向，桩可换真，调试一条直线。**

```
┌──────────────────── 采购模块（Procurement）────────────────────┐
│  契约定义在采购侧：CP6.Core/Services/Pur/Contracts/             │
│    IWmsReceiveService   收货物理入库      → WMS 实现            │
│    IWmsQcQuery          查 QC 检收结果    → WMS 实现            │
│    IWmsIssueService     外注支給材出库    → WMS 实现            │
│    IFinApService        匹配通过建应付票  → 财务 实现           │
│    IApprovalService     PR/PO 审批        → OA 实现（先桩）     │
└───┬─────────────┬──────────────┬──────────────┬───────────────┘
    │ 同步         │ 同步          │ 同步          │ 同步（可插拔）
    ▼             ▼              ▼              ▼
  WMS 库存      WMS QC         财务 ApInvoice   OA 流程引擎
 （唯一真相）                 （应付唯一真相）  （approval 05）
```

| 接口 | 章 | 方向 | 职责 | 实现方 |
|---|---|---|---|---|
| `IWmsReceiveService` | [03](./03-goods-receipt.md) | 采购→WMS | GR 确认物理入库 | WMS |
| `IWmsQcQuery` | [03](./03-goods-receipt.md) | 采购→WMS | 查 QC 检收结果 | WMS |
| `IWmsIssueService` | [07](./07-subcontract.md) | 采购→WMS | 外注支給材出库 | WMS |
| `IFinApService` | [04](./04-three-way-match.md) | 采购→财务 | 匹配通过建 `ApInvoice`（填 `PurchaseOrderId`） | 财务 |
| `IApprovalService` | [05](./05-purchase-request.md) | 采购→OA | PR/PO 审批起流程 | OA（先桩） |

---

## 二、依赖方向：契约在采购、实现在对方

低耦合的关键手法：**接口（契约）定义在采购侧，外部模块实现它**。这样采购编译期不引用 WMS/财务/OA 的任何类型，依赖是单向的"采购 → 接口"，而不是"采购 ↔ 外部模块"互相牵扯。

```csharp
// 契约都落在采购侧：CP6.Core/Services/Pur/Contracts/
public interface IWmsReceiveService { Task<WmsReceiveResult> ReceiveAsync(WmsReceiveRequest req); }
public interface IWmsQcQuery        { Task<QcResult> QueryByReceiptAsync(string wmsInboundNo); }
public interface IWmsIssueService   { Task<WmsIssueResult> IssueAsync(WmsIssueRequest req); }
public interface IFinApService      { Task<string> CreateApInvoiceAsync(ApInvoiceCreateDto dto); }

// 实现落在各自模块，DI 注册时绑定：
// WMS 项目： class WmsReceiveAdapter : IWmsReceiveService { ... 写 WMS 库存/批次/PaperRoll }
// 财务项目： class FinApAdapter     : IFinApService      { ... 建 ApInvoice，填 PurchaseOrderId }
services.AddScoped<IWmsReceiveService, WmsReceiveAdapter>();   // Program.cs / 模块装配
services.AddScoped<IFinApService,      FinApAdapter>();
```

> **为什么契约定义在采购侧（消费者）而非提供方？** 这是"依赖倒置"的标准用法：采购知道自己需要"入库"这个能力，但不该知道 WMS 怎么实现。把接口放在采购侧，采购只对自己定义的契约编程；WMS 来适配这个契约。换 WMS 实现、换成桩、换成另一个仓储系统，采购代码一行不改。

---

## 三、四接口里的两组基准，怎么落地

接口不只是"调一下"，调用时机由 [02 PostingBasis](./02-purchase-order.md) 和 [03 双基准](./03-goods-receipt.md) 决定——这是集成时最容易接错的地方：

```
着荷基准（货到即认）：
  GR 确认 → IWmsReceiveService.ReceiveAsync → ReceivedQty=AcceptedQty 一起累加
         → 立刻可建 AP（IFinApService）

检收基准（QC 合格才认）：
  GR 确认 → IWmsReceiveService.ReceiveAsync → 只累加 ReceivedQty（货在待检区）
         → IWmsQcQuery.QueryByReceiptAsync 查 QC
              ├ 合格 → 累加 AcceptedQty → 才能建 AP
              └ 不良 → 退货/让步，AcceptedQty 不动
```

> **基准决定"何时调财务建票"**：着荷基准入库即可建 AP；检收基准必须等 `IWmsQcQuery` 返回合格才建。集成时若忽略基准、收货就建票，检收基准的不良品会被错误地建成应付——这是接口落地最常见的 bug。基准从供应商配置（`BusinessPartner` 发注先）一路带到 GR，全链统一。

---

## 四、审批接口：桩 → 真实的无缝切换

`IApprovalService` 是五接口里唯一"先桩后真"的——审批引擎（OA）做好前用桩，做好后换真实现，采购代码不改：

```csharp
// 审批引擎未建：单人/跳过桩（采购总纲里的临时实现）
public class StubApprovalService : IApprovalService
{
    public Task<Guid> SubmitAsync(string bizType, Guid bizId, Guid starterId, object snapshot)
        => Task.FromResult(bizId);            // 提交即批：直接回调 OnApprovedAsync 落地
}

// 审批引擎建好：换成 OA 的真实现（approval 05），DI 改一行绑定即可
// services.AddScoped<IApprovalService, OaApprovalService>();   // 替换 StubApprovalService
```

切换后采购自动享受 OA 的组织路由 + 会签 + 高级动作（[approval 05](../approval/05-integration.md)），PR/PO 提交走真实流程：

```
采购：建 PO(草稿) → IApprovalService.SubmitAsync("PO", poId, buyerId, {amount, deptId})
   OA：按 bizType=PO 查 ApprovalBinding 选 flowKey → 起流程实例
   审批终态 → 同步回调 IApprovalCallback.OnApprovedAsync(poId) → 采购落地 PO.Status=确认
```

> **可插拔的价值**：采购落地**不阻塞于审批引擎**。桩让采购先跑起来（提交即批），引擎好了无缝接入。`IApprovalCallback` 由采购在运行时注册、OA 反向回调——**编译期采购→OA 单向，运行时 OA 多态回调采购**，和 [approval 05](../approval/05-integration.md) 讲的是同一套手法。回调要幂等（按 PO 当前状态判断，已确认就跳过）。

---

## 五、Phase 6 边界：采购同步、财务内部异步

这是全书最该说清的设计抉择——**为什么采购全程同步，财务却有 Phase 6 异步事件**：

```
采购模块内部 + 对外五接口：全程同步
  收货→入库、匹配→建票、发料→出库——一条直线调下去，调试能一步步跟

跨进 财务之后：财务内部走它自己的事
  IFinApService 同步把 ApInvoice 建好（这一步还是同步）
       ↓ 进了财务的地盘
  财务内部：发票 → 自动凭证（借原材料/进项税、贷应付）——财务的自动凭证引擎
            （财务模块内部是否用 Phase 6 事件，是财务的事，不跨回采购）
```

| | 采购 | 财务内部 |
|---|---|---|
| 通信方式 | **同步接口调用** | Phase 6 异步事件（最终一致） |
| 为什么 | 收货/匹配要能一步步跟、在死信里捞货太痛苦——**可调试性优先** | 凭证生成可异步、解耦、削峰——**吞吐与解耦优先** |
| 边界 | 采购调 `IFinApService` 把票建好就收手 | 票建好后"发票→凭证"是财务内部的事，不跨模块 |

> **边界划在 `IFinApService` 这一刀**：采购同步调它建出 `ApInvoice`（含 `PurchaseOrderId`），到此采购的责任结束。财务拿到发票后，"发票 → 自动凭证"走财务自己的引擎，**那是模块内、不跨回采购**。所以采购全程同步、财务内部可异步，两者在"建票"这个同步接口上交接，互不渗透。这就是总纲题眼"采购走同步、财务走异步"的落地点。

---

## 六、集成自检清单：接一个新接口时

每接一个外部能力，照这张表核一遍，就不会接错：

- [ ] **契约在采购侧吗？** 接口定义放 `Pur/Contracts/`，采购不引用对方实现类型。
- [ ] **依赖单向吗？** 采购 → 接口 → 对方实现；对方不反向依赖采购（回调用运行时注册）。
- [ ] **同步还是异步？** 采购对外一律同步（可调试）；异步只在财务/事件模块内部。
- [ ] **调用时机对吗？** 受 PostingBasis/双基准约束的接口（收货、建票），按基准决定何时调。
- [ ] **幂等吗？** 回调与重试要幂等（按当前状态判断），防重复落地/重复建票。
- [ ] **桩能跑吗？** 对方未就绪时有桩（如审批），采购不阻塞。

---

## 七、资深视角

**"契约在消费者侧"是低耦合的总开关。** 很多系统把接口定义在提供方（WMS 定义 `IReceiveService`），结果采购要引用 WMS 才能编译——耦合就这么产生了。反过来，采购定义自己需要的契约、对方来适配，采购就能独立编译、独立测试、独立替换依赖。这一个决定，决定了整个模块能不能解耦。

**同步 vs 异步不是技术品味，是可调试性 vs 吞吐的权衡。** 采购选同步，因为"收货→入库→匹配→建票"这条链人要能一步步跟、出错能当场定位；财务内部选异步，因为凭证生成可以削峰、解耦、容忍最终一致。**没有绝对的好坏，只有边界划得对不对**——把异步藏在财务内部、对采购暴露同步接口，两全。

**桩不是临时凑数，是接口设计的试金石。** 能写出"提交即批"的审批桩，说明 `IApprovalService` 契约干净（采购只要"提交+回调"两个动作）。如果桩很难写、要塞一堆字段，说明接口设计漏了耦合。**桩写得轻松 = 接口设计得好。**

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 依赖倒置 / 契约在消费者侧 | **Clean Architecture（Ports & Adapters）** | 接口（Port）定义在内层、适配器（Adapter）在外层实现 |
| 模块间同步 vs 异步边界 | **DDD 限界上下文 + 集成方式** | 同步调用 vs 领域事件，按一致性要求选 |
| 业务系统接口契约 | **SAP BAPI / Odoo 模块间 API** | 模块通过明确接口交互，不直接读对方表 |

> Ports & Adapters 里的"Port 定义在应用核心、Adapter 在外围实现"就是本章"契约在采购、实现在 WMS/财务"——采购是核心、外部模块是适配器，全世界一个架构原则。

---

## 九、本章自检

- [ ] 采购对外有哪五个接口？为什么契约定义在采购侧而非提供方？
- [ ] 依赖方向是怎样的？采购编译期依赖 WMS/财务的实现吗？回调怎么做到不反向依赖？
- [ ] 着荷/检收基准怎么决定"何时调 WMS 入库、何时调财务建票"？接错会怎样？
- [ ] 审批接口为什么"先桩后真"？切换时采购代码要改吗？
- [ ] 采购全程同步、财务内部异步——边界划在哪个接口？为什么这样划两全？
- [ ] 接一个新外部接口，自检清单要核哪几条？

全部能答 → 采购对外的五根线收口成一张清晰的图：根根单向、可追、可替换，同步/异步边界划在 `IFinApService` 一刀。下一步 [09 完整性与异常](./09-integrity.md)：防虚开发票、防外协吞料、防重复收货 + 采购对账，把采购链的风控兜底做完。

---

*实现：接口契约集中在 `CP6.Core/Services/Pur/Contracts/`；实现分别落 WMS（`IWms*`）、财务（`IFinApService`，填 `ApInvoice.PurchaseOrderId`）、OA（`IApprovalService`，见 [approval 05](../approval/05-integration.md)）；DI 在装配层绑定，桩与真实现可一行切换。*
