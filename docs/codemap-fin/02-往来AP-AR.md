# 02 · 往来账 AP（应付）/ AR（应收）

> 先读 [`README.md`](README.md) §0/§1/§2。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 一台引擎骨架

AP/AR 全部过账动作不直接拼凭证，而是构造 `FinBizEvent` → `IAutoVoucherEngine.GenerateAsync`(按 `PostingRule` 角色锚点拼凭证)→ `AutoPostAsync` 直过。**核销(settle)是例外**：本身不产凭证(只勾稽),仅尾差/汇差产一张差额冲销凭证。

---

## AP-1 应付发票·录入草稿 — POST /api/fin/ap/invoice

**前端**：`ApInvoiceView.vue`「新建发票」/「供应商红字」(`:14-15`)→`apInvoiceApi.create`(`fin.ts:74-76`)。
**后端**：Controller `[RequirePermission("fin-ap-invoice","add")]`→`ApInvoiceService.CreateAsync`(`:36-94`)：
1. 防重(`SupplierInvoiceNo` 非空非红字时同号→`E-FIN-201`)。
2. 无行→`E-FIN-204`。
3. **行级算税**(`:51-70`)：`t = Round(line.Amount * tc.Rate, 2)`；`tc.Recoverable` 真→`line.TaxAmount=t`(独立进项税);假→`line.Amount += t`(**不可抵扣税并入成本行**)。
4. 汇总 `GrossAmount = NetAmount + TaxAmount`(原币)。
5. 采番 `NextAsync("AP")`,`Status=Draft`。
**错误码**：`E-FIN-201/204`。

---

## AP-2 ⭐ 应付发票·过账（调引擎生成 GL 凭证）— POST /api/fin/ap/invoice/{id}/post

**后端**：`PostAsync`(`ApInvoiceService.cs:96-133`)：不存在`E-FIN-202`/非 Draft`E-FIN-203`→**构造事件**：
```csharp
var evt = new FinBizEvent { EventType = inv.IsCreditMemo ? "AP.CreditMemo" : "AP.InvoicePosted", Source = VoucherSource.AP, SourceDocNo = inv.No, PartnerId = inv.SupplierId, CurrencyCd = inv.CurrencyCd, FxRate = ... };
evt.HeaderAmounts["GrossAmount"] = inv.GrossAmount;
evt.HeaderAmounts["TaxAmount"] = inv.TaxAmount;
foreach (var line in inv.Lines) { var dl = new FinBizEventLine { CostCenterId = line.CostCenterId }; dl.Guids["ExpenseAccountId"] = line.ExpenseAccountId; dl.Amounts["Amount"] = line.Amount; evt.DocLines.Add(dl); }
await _engine.GenerateAsync(evt);
// 回填:按(Source=AP,SourceDocNo=No,Posted)反查 JournalEntry.Id → inv.JournalEntryId,Status=Posted
```
**AP 过账规则** `AP.InvoicePosted`(`PostingRuleSeed.cs:50-59`)：借各费用/原材料(DocumentLines,科目=行 ExpenseAccountId,带成本中心)/借进项税(`FixedRole TAX_INPUT`,=TaxAmount,不可抵扣为0则引擎跳过)/贷应付(`AP_CONTROL`,=GrossAmount,带供应商)。红字 `AP.CreditMemo`(镜像反向)。
**错误码**：`E-FIN-202/203`+引擎 `E-FIN-150/141`。

---

## AP-3 付款过账 — POST /api/fin/ap/payment

**后端**：`PaymentService.PayAsync`(`:37-72`)：银行账户不存在`E-FIN-210`→采番`"PAY"`→事件 `EventType = IsPrepayment ? "AP.Prepayment" : "AP.Payment"`,`HeaderGuids["BankGlAccountId"]=bank.GlAccountId`(银行科目从账户主数据取,引擎 HeaderAccount 模式)→引擎→回填。规则 `AP.Payment`(借`AP_CONTROL`带供应商/贷`Header BankGlAccountId`);预付 `AP.Prepayment`(借`AP_PREPAYMENT`/贷银行)。
**撤销** `ReversePaymentAsync`(`:74-109`,§6.1 顺序)：①先解核销(逐 `ApSettlement` 还原 `inv.SettledAmount`,有差额凭证则 `_journal.ReverseAsync(autoPost:true)`,删核销记录)→②红冲付款凭证→③Reversed。
**错误码**：`E-FIN-210/211/212`。

---

## AP-4 ⭐ 应付核销（尾差/汇差）— POST /api/fin/ap/payment/{id}/settle

**前端**：`PaymentView.vue` 拉该供应商应付发票(过滤 `status===1||2 && !isCreditMemo`)→每行填「本次核销」+「折扣」+「折扣科目」→`paymentApi.settle(id, applies)`。
**后端**：`ApSettlementService.SettleAsync`(`:24-127`)：
1. 付款不存在`E-FIN-211`/非 Posted`E-FIN-212`;可用余额 `Σ Applied > (Amount-Settled)+Eps`→`E-FIN-220`(Eps=0.0001)。
2. 逐发票校验：不存在`E-FIN-202`/非Posted·部分核销`E-FIN-224`/供应商不一致`E-FIN-221`/有折扣无科目`E-FIN-223`/`cleared > 欠款+Eps``E-FIN-222`。
3. **汇差**(`:51`)：`fxDiff = Round(a.AppliedAmount * (inv.FxRate - payment.FxRate), 2)`(实付原币×(发票汇率-付款汇率))。
4. **尾差(折扣)**(`:53`)：`discBase = Round(DiscountAmount * inv.FxRate, 2)`(折扣原币按发票记账汇率折本位币)。
5. **差额冲销凭证**(仅 `fxDiff!=0 || discBase!=0`,单据号 `{付款No}#{发票No}#DIFF`)：现金折扣(借`AP_CONTROL`/贷折扣科目)/汇兑收益(借应付/贷`FX_GAIN`)/汇兑损失(借`FX_LOSS`/贷应付)→`AutoPostAsync`。
6. 推进 `inv.SettledAmount += cleared`(`>= Gross-Eps`→Settled,否则 PartiallySettled);落 `ApSettlement` 勾稽(`DiffAmount/DiffType/DiffJournalEntryId`)。
> **关键口径**：核销本身不产凭证(勾稽),仅尾差/汇差产凭证;汇差用发票汇率vs付款汇率,折扣按发票汇率折本位币。
**错误码**：`E-FIN-211/212/220/141/202/224/221/223/222`。

---

## AP-5 应付主数据（银行账户/税码）— /api/fin/ap/master

`ApMasterService`：`CreateBankAccountAsync`(编码重复`E-FIN-230`)/`CreateTaxCodeAsync`(税码重复`E-FIN-231`)。实体 `BankAccount`(Code 唯一/GlAccountId 绑银行存款科目/CurrencyCd)、`TaxCode`(Rate/Direction 进项·销项/`Recoverable` 可抵扣性——决定不可抵扣并入成本)。

---

## AR-1 ⭐ 应收发票·过账（收入确认+成本结转双凭证）— POST /api/fin/ar/invoice/{id}/post

**后端**：`ArInvoiceService.PostAsync`(`:82-123`)：不存在`E-FIN-302`/非 Draft`E-FIN-303`→
1. **①收入确认凭证**：`EventType = IsCreditMemo ? "AR.CreditMemo" : "AR.Revenue"`,Source=AR;`HeaderAmounts` 装 Gross/Net/TaxAmount;回填 `inv.JournalEntryId`。规则 `AR.Revenue`(借`AR_CONTROL`带客户=Gross/贷`REVENUE`=Net/贷`TAX_OUTPUT`=Tax 0额跳过)。
2. **②成本结转凭证**(仅 `CostAmount>0`)：`EventType = IsCreditMemo ? "AR.CogsReversal" : "AR.Cogs"`,**`Source=VoucherSource.Cost`**(与收入分开幂等,同No不冲突);`HeaderAmounts["Amount"]=CostAmount`;回填 `inv.CostJournalEntryId`。规则 `AR.Cogs`(借`COGS`/贷`FG`);**成本为本位币**(我方成本不随销售外币)。
3. `Status=Posted`。算销项税恒按税率(不涉可抵扣,与 AP 不同)。
**错误码**：`E-FIN-302/303/304`。

---

## AR-2 ⭐ 出货→AR自动开票 FinBridgeHook 接缝

**契约** `IFinBridgeHook`(`:10-20`)：`OnShipmentConfirmedAsync(FinShipmentInvoiceRequest, userName)`/`OnShipmentCancelledAsync`/`OnWorkOrderCompletedAsync`。`FinBridgeResult` Ok/Skipped/Failed;`NoOpFinBridgeHook`(`FinBridge:Enabled=false`)。
**实现** `FinBridgeHook`(`:14`,继承 `BridgeHookBase`,Best-Effort 异常握住落 `IntegrationEvent` 可重试)：
- `OnShipmentConfirmedAsync`(`:26-50`)：`var (r,_,no) = await _ar.CreateFromShipmentAsync(request, userName)`→失败 PersistEvent(Failed)/成功 PersistEvent(Success)+`Ok(no)`。
- `OnShipmentCancelledAsync`(`:52-78`)：按 ShipmentId 反查 AR 发票,无则 Skipped,有则 `_ar.ReverseAsync` 红冲。
**自动开票** `ArInvoiceService.CreateFromShipmentAsync`(`:125-162`)：幂等(同 ShipmentId 非红字已开票返既有)→**成本切真实**(`WorkOrderNo` 且 `CostSheet.FgUnitCost>0`→`cost = Round(FgUnitCost * Σ行Qty, 2)`,否则回退 `EstimatedCost`)→`CreateAsync`→`PostAsync`(双凭证)。
> ⚠️ **实测关键发现**：钩子+`IntegrationEventDispatcher` 路由(`WMS|FIN`)+DI(`Program.cs:158`) 齐备,但 **`OutboundService` 出货确认链只直调 `_erpBridge.OnShipmentConfirmedAsync`(WMS→ERP 回写出货实绩),未发现把真实出货组装成 `FinShipmentInvoiceRequest` 触发 Fin 钩子的 live 生产调用点**。即出货→AR 自动开票钩子已实现且由 dispatcher 重放/测试驱动,但出货主流程 producer 端未在 OutboundService 落地。

---

## AR-3 收款 + 应收核销（方向与 AP 相反）

**收款** `POST /api/fin/ar/receipt`→`ReceiptService.ReceiveAsync`(`:37-72`)：银行不存在`E-FIN-310`→事件 `EventType = IsAdvance ? "AR.Advance" : "AR.Receipt"`。规则 `AR.Receipt`(借`Header`银行/贷`AR_CONTROL`带客户);预收 `AR.Advance`(借银行/贷`AR_ADVANCE`)。撤销 `ReverseReceiptAsync`(先解核销→红冲→Reversed,`E-FIN-310/311/312`)。
**应收核销** `POST .../{id}/settle`→`ArSettlementService.SettleAsync`(`:24-127`,镜像 AP 方向相反)：
- **汇差**(`:51`)：`fxDiff = Round(a.AppliedAmount * (receipt.FxRate - inv.FxRate), 2)`(实收原币×(**收款汇率-发票汇率**),减数顺序与 AP 相反)。
- 折扣 `discBase = Round(DiscountAmount * inv.FxRate, 2)`。
- 差额凭证：销售折扣(借折扣/贷应收)/汇兑收益(借应收/贷`FX_GAIN`)/损失(借`FX_LOSS`/贷应收),`Source=AR`。
**错误码**：`E-FIN-302/311/312/320/141/324/321/323/322`。

---

## AR-4 ⭐ 信用控制（反向约束）— GET /api/fin/ar/credit/check

**后端**：`CreditControlService.CheckCreditAsync`(`:17-41`)：取 `BusinessPartner.CreditLimit`(复用取引先主数据)→当前未结应收余额 `openAr = Σ Round((Gross-Settled)*FxRate,2) * (IsCreditMemo? -1 : 1)`→`exceeded = limit>0 && openAr+orderAmount > limit`。结果 `CreditCheckResult{CreditLimit/OpenAr/OrderAmount/Controlled/Exceeded/Available}`。
> ⚠️ **实测**：接口注释定位为"财务数据反向约束发货",但 grep 全仓 Fin 信用服务**仅前端"信用查询"对话框调用查询,未在出货前做硬拦截**。另有同名但不同的 ERP 级 `OrderService.CheckCreditAsync`(mcframe7 占位,固定1000万额度桩,与 Fin 信用控制无关)。即 Fin 信用控制目前是"查询/提示"形态,未强制阻断。

---

## 子账↔GL 勾稽 / 账龄（查询类）
- **AP 勾稽** `ApReconcileService`(`GET /reconcile`)：子账=未结应付 `Round((Gross-Settled)*FxRate,2)*(红字?-1:1)`；GL=`AP_CONTROL` 已过账 `Σ(Credit-Debit)`；返 `{SubLedger,GlBalance}` 应恒等。AR 镜像。
- **AP 账龄** `ApAgingService`(`GET /aging`)：未付按 DueDate 落桶 NotDue/1-30/31-60/60+。AR 镜像。

---

## 涉及文件清单

| 层 | 文件 |
|---|---|
| 引擎 | `Services/Fin/AutoVoucherEngine.cs`/`FinBizEvent.cs`/`PostingRuleSeed.cs`/`FinResult.cs` |
| AP | `Controllers/Fin/ApInvoiceController.cs`/`PaymentController.cs`/`ApMasterController.cs`、`Services/Fin/ApInvoiceService.cs`/`PaymentService.cs`/`ApSettlementService.cs`(核销尾差汇差)/`ApMasterService.cs`/`ApReconcileService.cs`/`ApAgingService.cs` |
| AR | `Controllers/Fin/ArInvoiceController.cs`/`ReceiptController.cs`/`CreditControlController.cs`、`Services/Fin/ArInvoiceService.cs`(双凭证+出货开票)/`ReceiptService.cs`/`ArSettlementService.cs`/`CreditControlService.cs`/`ArReconcileService.cs`/`ArAgingService.cs` |
| 接缝 | `Services/Integration/IFinBridgeHook.cs`、`Services/Fin/FinBridgeHook.cs`、`Services/Integration/IntegrationEventDispatcher.cs`(WMS\|FIN 路由)、`Program.cs:156-159` |
| 实体 | `DomainModels/Fin/ApInvoice.cs`/`ApSettlement.cs`(+SettlementDiffType)/`BankAccount.cs`/`TaxCode.cs`/`ArInvoice.cs`/`Payment.cs`/`Receipt.cs`/`ArSettlement.cs` |
| 前端 | `views/fin/ApInvoiceView.vue`/`PaymentView.vue`/`ArInvoiceView.vue`/`ReceiptView.vue`/`ApAgingView.vue`/`ArAgingView.vue`、`api/fin/fin.ts`、`types/fin/fin.ts` |

## 关键发现
1. **AP/AR 过账统一调引擎**(AP=`AP.InvoicePosted`、AR=`AR.Revenue`+`AR.Cogs` 双事件,成本 `Source=Cost` 分开幂等)。
2. **出货→AR自动开票钩子已实现但 OutboundService 无 live 生产调用点**(dispatcher 重放+测试驱动);出货链当前只接 ERP 回写。
3. **尾差/汇差**：核销不产凭证,仅 `fxDiff≠0||discBase≠0` 产 `#DIFF` 凭证;AP汇差=Applied×(发票-付款汇率),AR=Applied×(收款-发票汇率)方向相反。
4. **信用控制未硬拦截**(仅查询);另有同名 ERP 桩属不同实现。
