# 09 · 与 CP6 集成：实体落点 / BridgeHook 接法 / 数据一致性

> **把整个财务模块物理落进 CP6 工程。** 前面各章讲"做什么"，本章讲"放哪、怎么接、怎么不破坏现有架构"——目录结构、DI 注册、迁移、BridgeHook 接线、以及最关键的"财务怎么和业务保持最终一致"。
>
> 上游：全部章节。横向参考：[`docs/oa/09`](../oa/09-cp6-integration.md)（OA 集成同一套原则）。

---

## 一、落点：folder = namespace（沿用 CP6 约定）

CP6 最近一次重构是"按功能/类别分文件夹（folder = namespace）"。财务模块照此落 `Fin` 文件夹：

```
CP6.Entity/DomainModels/Fin/      GlAccount, JournalEntry, JournalLine, FiscalPeriod,
                                  CostCenter, ApInvoice, ApInvoiceLine, Payment, ApSettlement,
                                  BankAccount, ArInvoice, Receipt, ArSettlement,
                                  CostSheet, PostingRule, ReportLineMapping
CP6.Core/Services/Fin/            JournalEntryService, GlAccountService, TrialBalanceService,
                                  FiscalPeriodService, ApInvoiceService, PaymentService,
                                  ApSettlementService, AutoVoucherEngine, CostCollectService,
                                  BalanceSheetService, IncomeStatementService, FinBridgeHook
CP6.WebApi/Controllers/Fin/       JournalEntryController, GlAccountController, PeriodController,
                                  ApInvoiceController, PaymentController, ArInvoiceController,
                                  ReportController
CP6.Core/Migrations/              FinAddGlKernel, FinAddApAr, FinAddCost, FinAddPostingRule …
cp6.web/src/views/fin/            凭证录入/科目表/期末结账/试算表/AP/AR/成本/报表 视图
cp6.web/src/api/fin/              对应 TS API 客户端
```

> 与现有 `Erp`/`Mes`/`Wms` 平级，互不污染。财务对其他模块**只读引用**（读 Order/Shipment/WorkOrder/PaperRoll），不反向修改业务表——边界干净。

---

## 二、DI 注册：沿用条件 NoOp 开关

CP6 用 `appsettings.json` 开关 + NoOp 实现切换功能（`OutboundRouting:Enabled`、`MesBridge:Enabled`…）。财务照此，**默认关闭**，不影响现有部署：

```csharp
// Program.cs
builder.Services.AddScoped<IJournalEntryService, JournalEntryService>();
builder.Services.AddScoped<IAutoVoucherEngine, AutoVoucherEngine>();
builder.Services.AddScoped<IApInvoiceService, ApInvoiceService>();
// …

var finEnabled = builder.Configuration.GetValue<bool>("FinBridge:Enabled");
if (finEnabled)
    builder.Services.AddScoped<IFinBridgeHook, FinBridgeHook>();
else
    builder.Services.AddScoped<IFinBridgeHook, NoOpFinBridgeHook>();   // 不生成凭证
```

> 这样财务模块可以**灰度上线**：先开 GL/AP 手工录入跑通，再开 `FinBridge:Enabled` 让出货自动开票。和你现有的 `WmsBridge`/`MesBridge` 开关同一个心智，运维零学习成本。

---

## 三、BridgeHook 接线：挂上 Phase 6，不内联业务事务

财务自动凭证**绝不内联进业务的数据库事务**（[05 章](./05-auto-voucher.md#五它怎么挂上-phase-6-的-bridgehook)），而是挂在你现成的 `IntegrationEvent` 异步链上：

```
业务动作（出货确认/付款/工单完工）
   → 写业务表 + 落 IntegrationEvent（同一事务，Phase 6 已有）
   → IntegrationEventDispatcher 异步分发
   → FinBridgeHook 消费 → AutoVoucherEngine 生成凭证 → 直过
   → 失败 → IntegrationEventRetryWorker 重试 → 死信告警（全是 Phase 6 现成）
```

**复用清单（一个轮子都不用重造）：**

| 能力 | Phase 6 现成组件 | 财务怎么用 |
|---|---|---|
| 事件持久化 | `IntegrationEvent` + `BridgeHookBase` | `FinBridgeHook` 继承 `BridgeHookBase` |
| 路由分发 | `IntegrationEventDispatcher` | 注册财务事件类型 → FinBridgeHook |
| 幂等 | `CorrelationId` + 事件去重 | 自动凭证幂等键 `SourceDocNo` 叠加 |
| 重试 | `IntegrationEventRetryWorker` | 凭证生成失败自动重试 |
| 死信告警 | `DeadLetterNotifier`（SignalR） | 凭证持续失败 → 推财务告警 |
| 健康度 | `BridgeHealthService` + `/metrics` | 财务凭证成功率进 Prometheus |

> **这是财务模块能快速落地的最大原因**：跨模块联动、幂等、重试、补偿、可观测——CP6 在 Phase 6 全做好了。财务只是再注册一个 Hook + 一套 PostingRule。

---

## 四、数据一致性：最终一致，不是强一致

财务和业务是**最终一致**：出货先成功，凭证稍后异步生成。会不会出现"货出了、账没记"的窗口？会——但**有兜底**：

1. 凭证生成失败 → 进重试 → 仍失败 → 死信 + 告警，**不丢**（持久化在 IntegrationEvent）
2. 定时**对账 job**（见 [10 章](./10-integrity-audit.md)）：每日核对"已确认出货数 vs 已生成 AR 凭证数""AP 子账 vs GL 控制科目"，差异报警
3. 凭证幂等：重试不会重复记账

> **为什么不强一致（财务内联进出货事务）？** 因为那样财务一出错，出货就回滚——业务被财务绑架。制造业的铁律是"货必须发得出去"，账可以稍后补。所以选最终一致 + 兜底对账，和 [docs/oa/09](../oa/09-cp6-integration.md) 的"审批通过异步回写业务"是同一个工程判断。

---

## 五、迁移与种子

| 迁移/种子 | 内容 | 参考现有 |
|---|---|---|
| `FinAddGlKernel` | GlAccount/JournalEntry/JournalLine/FiscalPeriod/CostCenter | EF Migration（已有 73 个的风格） |
| `FinAddApAr` | AP/AR 全套表 | |
| `FinAddCost` | CostSheet | |
| `fin-coa-cn-gaap-seed.sql` | 中国准则科目表（70 科目，[01 章 3.2](./01-gl-kernel.md#32-中国企业会计准则模板cn-gaap70-科目全量)） | `docs/*-i18n-seed.sql` 风格 |
| `fin-coa-intl-seed.sql` | 国际区间码科目表 | |
| `fin-posting-rules-seed.sql` | AP/AR/成本的默认入账规则 | |
| `fin-menu-seed.sql` | 财务菜单 + RBAC | `wms-menu-seed.sql` 风格 |
| `fin-i18n-seed.sql` | 财务界面多语言（5 语言） | `wms-*-i18n-seed.sql` 风格 |

> 部署时按客户 `StandardScheme` 选一套 CoA seed 导入（[01 章模板包机制](./01-gl-kernel.md#31-模板包机制)）。

---

## 六、本章自检

- [ ] 财务实体落在 `Fin` 文件夹、与 Erp/Mes/Wms 平级、只读引用业务表——做到了吗？
- [ ] `FinBridge:Enabled` 开关能让财务灰度上线吗？默认关闭不影响现有部署吗？
- [ ] 财务自动凭证为什么不能内联进出货事务？最终一致靠什么兜底不丢账？
- [ ] Phase 6 的哪些组件被财务直接复用了？（幂等/重试/死信/健康度）
- [ ] CoA 按 `StandardScheme` 选种子导入，这和多国别模板包怎么对应？

全部能答 → 财务模块在 CP6 里有了干净的物理位置和接线。最后一章 [10 完整性与审计](./10-integrity-audit.md)：让这本账经得起审计。

---

*生成于 2026-06-10。需求基线：folder=namespace / 条件 NoOp 开关 / 复用 Phase 6 / 最终一致。配套实现落于 `CP6.*/.../Fin`。*
