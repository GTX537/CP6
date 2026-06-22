# Fin 财务会计 · 代码级实现手册

> 同模板；公共机制见 [`codemap-erp/README.md` §0](../codemap-erp/README.md)。是 [`CODEMAP.md`](../CODEMAP.md) 的放大镜续篇。财务 23 个控制器，本册分 4 章。

## 📖 目录
| # | 功能 | 文件 | 看点 |
|---|---|---|---|
| 1 | 总账内核 | [`01-总账内核.md`](01-总账内核.md) | 借贷恒等/maker-checker/红冲/月结锁期/自动凭证引擎 |
| 2 | 往来 AP/AR | [`02-往来AP-AR.md`](02-往来AP-AR.md) | 发票过账调引擎/核销尾差汇差/出货→AR开票接缝/信用控制 |
| 3 | 三表/成本/对账 | [`03-三表-成本-对账.md`](03-三表-成本-对账.md) | 三表复用试算表/成本料工费真实/银行对账撮合/汇兑重估 |
| 4 | 资产/预算 | [`04-资产-预算.md`](04-资产-预算.md) | 四法折旧/月末Worker/处置清理结转/预算OA回调/预控守卫 |

## §0 Fin 特有约定

- **一台自动凭证引擎**：AP/AR/成本/折旧/处置等过账都不手拼凭证，而是构造 `FinBizEvent` → `IAutoVoucherEngine.GenerateAsync` 按 `PostingRule`(**规则即数据**:只配 Role 锚点+金额字段名,不写死科目Id) → `IJournalEntryService.AutoPostAsync` 直过(含借贷恒等+锁期双保险)。换准则/科目只改 seed。
- **总账三铁律**：①**借贷恒等**(`ValidateBalance` 静态纯函数,`decimal` 精确比较,手工链+自动链共用一处);②**凭证不可改不可删**(无 PUT/DELETE,只能红冲=反向分录另立新凭证);③**maker-checker**(手工过账 `MakerId==checkerId` 拒 `E-FIN-111`,自动凭证 SYSTEM 可信直过)。
- **采番** `FinSequenceService.NextAsync("GL", date)` → `GL-yyyy-MM-NNNNN`(月度作用域归零,D5 补零)。
- **错误码** `E-FIN-xxx`(文案在 `I18nFinScreenSeed.cs`,五语);资产另用裸码 `FA001~FA012`(`I18nA3ScreenSeed.cs`);预算用 `E-A5-*`(`I18nA5BudgetScreenSeed.cs`);银行对账用 `E-A4-*`;成本回退 `W-A2-COST-*`。
- **凭证状态机** `JournalStatus`：Draft0→PendingReview1→Posted2,旁支 Rejected3/Reversed4。

## §1 自动凭证引擎数据流
```
业务服务(AP/AR/成本/折旧/处置) → 构造 FinBizEvent(EventType, HeaderAmounts/DocLines, Source, SourceDocNo)
  → AutoVoucherEngine.GenerateAsync:
       Step1 幂等(同Source+SourceDocNo已过账→跳过)
       Step2 按EventType找启用PostingRule(无→E-FIN-150)
       Step3 拼行: FixedRole(按GlAccount.Role锚点取科目,缺→E-FIN-141) / DocumentLines(按科目+成本中心分组炸开) / HeaderAccount(科目来自事件头Guid)；原币×rate→本位币
       Step4 AutoPostAsync直过(同ValidateBalance借贷恒等+锁期+银行守卫;制单=过账=SYSTEM绕maker-checker但仍校恒等)
PostingRuleSeed 幂等播种12条标准规则(AP.InvoicePosted/AP.Payment/AR.Revenue/AR.Cogs/...)
```

## §2 跨模块接缝
- **出货→AR自动开票** `IFinBridgeHook.OnShipmentConfirmedAsync`(WMS→FIN)：钩子+dispatcher路由+DI齐备,但⚠️ **OutboundService 出货链未发现 live 生产调用点把真实出货组装成 `FinShipmentInvoiceRequest`**(由 dispatcher 重放+测试驱动)。
- **OA审批→财务/预算** `IApprovalCallback`：`JournalApprovalCallback`(凭证过账)、`BudgetApprovalCallback`(预算版本激活)，OA 终态同步回调(同事务原子)。详见 [codemap-wf](../codemap-wf/01-审批引擎.md)。
- **成本归集吃 MES** `CostCollectService` 消费 MES `WorkOrderMaterial.ActualQty`(真实消耗)+`ProcessCostRate`(工时×费率)。
- **采购→建应付** `IFinAp.CreateInvoiceFromPurchaseAsync`(被采购三单匹配委托)。

*生成于 2026-06-22，基于真实源码逐行核对。*
