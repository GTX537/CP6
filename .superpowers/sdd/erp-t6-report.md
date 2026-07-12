# M-ERP T6 报告：测试补网(Quotation 报价计算 / Order 建单算价)

## 既有覆盖盘点结论

对 CP6.Tests 全仓 grep（Quotation / Order / 算价关键字），既有 ERP 相关测试仅两处，**均不触及计算式**：

| 文件 | 覆盖内容 | 是否触及金额计算式 |
|---|---|---|
| `CP6.Tests/Erp/ErpAuditTests.cs` (T5) | 字段级审计接线冒烟——**直接给实体 `Amount`/`FxRate` 赋值**再断言产生 `Sys_FieldAuditLogs` 行 | 否。把 Amount 当输入直接写，从不验证 `数量×単価` 是否算对 |
| `CP6.Tests/OrderServiceCancelTests.cs` (Phase6) | `OrderService.CancelAsync` 取消状态机（Rejected/NeedsDecision/Cancelled/PartiallyCancelled 分支 + Bridge Hook mock） | 否。纯生命周期状态，无算价 |

结论：`QuotationService` 报价计算（行金额=数量×単価、合計=Σ行金額、訂正再计算）与 `OrderService` 建单算价（价格来源 SalesPriceDiv 分岐、`CalcAmountAsync`、`BatchUpdatePriceAsync` 金额再计算）核心路径**测试为零**。本任务 7 用例与既有零重复。

## 计算主路径（生产代码，零改动）

- QuotationService.CreateAsync/UpdateAsync：`amount = (Quantity ?? 0) * (UnitPrice ?? 0)`；`TotalAmount = Σ amount`（UpdateAsync 只累加 incoming 明细，软删除行被排除）。
- OrderService.CalcAmountAsync：`qty * (SalesPriceDiv=="1" ? newIndPrice : newSetPrice)`——价格来源由 SalesPriceDiv 决定（"1"=個別売取個別単価，其余=セット売取セット単価）。
- OrderService.BatchUpdatePriceAsync：单价訂正保存时 `entity.Amount = (Quantity ?? 0) * (SalesPriceDiv=="1" ? IndividualUnitPrice : SetUnitPrice)`；单价变更则 ApprovalStatus 差戻し=1 + WF 起票计数。

## 逐用例「输入 → 手算演算 → 期望值」

### QuotationCalcTests.cs（QuotationService）

| 用例 | 输入 | 手算演算 | 期望值 |
|---|---|---|---|
| Q1 正常 `Create_TwoDetails_ComputesLineAmountAndTotal` | 行1 Qty=12 UP=150；行2 Qty=3 UP=2000 | 行1=12×150=1800；行2=3×2000=6000；合計=1800+6000 | d1.Amount=1800, d2.Amount=6000, Total=7800 |
| Q2 边界 `Create_NullQtyOrZeroPrice_...` | 行1 Qty=null UP=500；行2 Qty=10 UP=0；行3 Qty=5 UP=100 | 行1=(0)×500=0；行2=10×0=0；行3=5×100=500；合計=0+0+500 | d1=0, d2=0, d3=500, Total=500 |
| Q3 訂正再计算 `Update_RemoveDetailAndChangeQty_RecomputesTotal` | 建单 行1 Qty=10 UP=100(=1000)+行2 Qty=2 UP=250(=500)，Total=1500；訂正=删行2、行1 Qty 10→8 | 行1=8×100=800；行2 论理削除→排除合計；合計=800 | 明细仅剩1行, d1.Amount=800, Total=800 |

### OrderCalcTests.cs（OrderService）

| 用例 | 输入 | 手算演算 | 期望值 |
|---|---|---|---|
| O1 正常個別売 `CalcAmount_IndividualSalesDiv_...` | 明细 Qty=100 Div="1"；调用 newInd=25.5 newSet=999 | Div="1"→取個別単価 25.5；100×25.5（セット 999 无视） | 2550 |
| O2 正常セット売 `CalcAmount_SetSalesDiv_...` | 明细 Qty=100 Div="2"；调用 newInd=25.5 newSet=30 | Div≠"1"→取セット単価 30；100×30（個別 25.5 无视） | 3000 |
| O3 边界 `CalcAmount_MissingDetailOrNullPrice_ReturnsZero` | (a)误 NO 999；(b)明细 Div="2" newSet=null | (a)明细不在→0；(b)セット単価 null→100×0 | 两断言均=0 |
| O4 バッチ算价 `BatchUpdatePrice_MultiLine_...` | 2 明细同受注 Div="1"；item1 IndAfter=25、item2 IndAfter=10（原単価 null） | l1=100×25=2500；l2=40×10=400；两行単価变更→各 ApprovalStatus=1、WF 计 2 | UpdatedCount=2, WfRequestedCount=2, pe.Calls=2, l1.Amount=2500(ApprovalStatus=1), l2.Amount=400 |

## 测试命令与输出

聚焦：
```
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~CP6.Tests.Erp.QuotationCalcTests|FullyQualifiedName~CP6.Tests.Erp.OrderCalcTests"
Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7
```
全量（基线 1709）：
```
dotnet test CP6.Tests/CP6.Tests.csproj
Passed! - Failed: 0, Passed: 1716, Skipped: 5, Total: 1721
```
1709 + 7 = 1716，无回归下跌。

## Files changed

- `CP6.Tests/Erp/QuotationCalcTests.cs`（新增，3 用例）
- `CP6.Tests/Erp/OrderCalcTests.cs`（新增，4 用例）
- 零生产代码改动。

## Self-review

- 断言全为手算期望值（1800/6000/7800/500/800/2550/3000/0/2500/400），无「以服务输出为期望」的套套逻辑。✔
- 与既有 ErpAuditTests / OrderServiceCancelTests 零重复（盘点见上）。✔
- 每服务正常+边界各覆盖：Quotation 正常(Q1)+边界(Q2 null/0)+訂正(Q3)；Order 正常(O1/O2)+边界(O3)+バッチ(O4)。✔
- 零生产改动。✔

## Concerns

1. **【测试局限，非缺陷】set 单价一括伝播路径 InMemory 不可测**：`BatchUpdatePriceAsync` 在 `item.SetUnitPriceAfter.HasValue` 时用 `ExecuteUpdateAsync` 把同一 WebOrderNo 全明细的 SetUnitPrice 一括更新。`ExecuteUpdateAsync` 是 relational 专属扩展，EF InMemory 8.0.12 抛「could not be translated」。故本单元测试仅覆盖個別売(Div="1")保存路径（不触发 ExecuteUpdate），セット単価一括伝播（明细横断汇总）留待 relational 集成测试。O4 已注释说明。**未发现服务缺陷**——手算与服务输出全部一致。
2. 计算式本身健壮（null 用 `?? 0` 兜底），本轮真值锚定未暴露任何算价 bug。
