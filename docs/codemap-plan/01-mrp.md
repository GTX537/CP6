# 01 · 物料计划策略 + MRP 引擎

> 先读 [`README.md`](README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位

- **MRP = regenerative 全量复算**：独立需求(开口受注/手动)→ 低层码分层 net → 计划订单 + 钉住溯源 Pegging + 净需求留痕。
- 三铁路径：`MrpEngine.RunAsync`(净算) → 看板确认/转单/忽略 `PlanConvertService` → 转单契约 `IPlanToPr/WorkOrderService`(当前桩)。

---

## 一、物料计划策略 CRUD — GET/POST/DELETE /api/plan/item-policy

**前端**：`/plan/item-policy`（`router/index.ts:61`）→ `ItemPolicyView.vue`，批量规则下拉 4 值(`:52-59`：1按需/2最小量/3订货倍数/4整卷取整)、自制采购(1自制/2采购)。`itemCd` 编辑禁改(`:43`)。api `plan.ts:34-44`(`list/save/remove`)。type `ItemPolicy`(`types/plan/plan.ts:10-20`)。
**后端**：Controller `ItemPlanningPolicyController.cs`(`[Route("api/plan/item-policy")]`，统一 `Ok2`/`Err`)。Service：
- `GetPolicyAsync`（`ItemPlanningPolicyService.cs:17-34`）：查 `ItemPlanningPolicies`，**缺主数据时合成默认策略**(`LotRule=LotForLot/SafetyStock=0/MakeOrBuy=Buy/IsDefault=true` 供引擎告警)。
- `UpsertAsync`（`:60-86`）：新增盖审计 / 更新逐字段。
- `DeleteAsync`（`:88-97`）软删。
- 实体 `Plan_ItemPlanningPolicy`(`:BaseBizEntity`，业务键 `ItemCd`，`LotRule`/`MoqQty`/`MultipleQty`/`MakeOrBuy`，枚举 `PlanLotRule{LotForLot1,Moq2,Multiple3,RoundRoll4}`/`PlanMakeOrBuy{Make1,Buy2}`)。
**错误码**：`E-PLAN-品目必填`(`:63`)、`E-PLAN-策略不存在`(`:92`)。

**提前期汇总** `GetLeadDaysAsync`（`:36-50`，引擎内部调用）：
```csharp
if (policy.MakeOrBuy == (int)PlanMakeOrBuy.Make)   // 自制：制造提前期=路线各工程 LeadTime 之和
    return await _db.ProductProcesses.AsNoTracking().Where(p => p.ProductCd == itemCd && !p.IsDeleted).SumAsync(p => p.LeadTime ?? 0m);
return policy.PurchaseLeadDays;                     // 采购：直接取采购提前期
```
该值在 MRP `releaseDate = bucket.AddDays(-leadDays)` 决定下达日。

---

## 二、⭐ MRP 净需求计算 — POST /api/plan/mrp/run → MrpEngine.RunAsync

> `MrpEngine.cs`，regenerative 全量复算（MP-D5）。接口 `record MrpDemand(ItemCd, Qty, RequiredDate, SourceRefNo?)`、`record MrpRunRequest(Demands, ScopeJson?)`。

**步骤 0 采番**（`:42-51`）`DocNumber.NextAsync(_db, "MRP")` → `Plan_MrpRun{Status=Running}`。
**步骤 1 复算存活**（`:54-66`）：作废所有 `Status==Suggested` 计划订单+Pegging（确认/转单态保留当供给），`SaveChanges`。
**步骤 2 低层码**（`:69-82`）：载 `ProductMaterials`(BOM)→`bomByParent`；`levels = _lowLevel.Compute(edges)`(成环抛 `E-PLAN-循环BOM`)；载规格 `specByCd` + `MasterGenericCode M067` 段成率 `yieldByFlute`。
> **低层码 Kahn 拓扑**（`LowLevelCodeService.cs`）：`edgeSet` 去重防 indegree 多计 → 拓扑分层 `if(level[u]+1>level[v]) level[v]=level[u]+1`(顶层成品层0，共用料取**最深**值) → 成环 `if(processed<nodes.Count) throw E-PLAN-循环BOM`。

**步骤 3 毛需求累加**（`:85-97`）：逐独立需求 `a.Gross += d.Qty`，记 `Pegging(Order, SourceRefNo, Qty)`。
**步骤 4 ⭐ 按低层码 0→N 逐层 net**（`:105-171`）：
```csharp
for (int level = 0; level <= maxLevel; level++) {
    var itemsAtLevel = accum.Where(kv => LevelOf(kv.Key)==level && kv.Value.Gross>0).Select(kv=>kv.Key);
    foreach (var item in itemsAtLevel) {
        var a = accum[item];
        var bucket = a.EarliestRequired == DateTime.MaxValue ? DateTime.Today : a.EarliestRequired;
        var pol = await _policy.GetPolicyAsync(item);
        var sup = await _supply.GetSupplyBreakdownAsync(item, bucket);
        var net = a.Gross - sup.Total - pol.SafetyStock;   // ★净需求公式
        if (net < 0) net = 0;
        // 落 Plan_NetRequirement 留痕(Gross/OnHand/InTransit/InWip/FirmPlanned/SafetyStock/Net)
        if (net <= 0) continue;
        var qty = ApplyLotRule(net, pol);                  // 批量定批
        var leadDays = await _policy.GetLeadDaysAsync(item);
        var releaseDate = bucket.AddDays(-(double)leadDays);
        var isMake = pol.MakeOrBuy == (int)PlanMakeOrBuy.Make;
        var po = new Plan_PlannedOrder { Type=(int)(isMake?Production:Purchase), ItemCd=item, Qty=qty, RequiredDate=bucket, ReleaseDate=releaseDate, Status=Suggested };
        // 自制件向下展开 BOM（采购件买入不展开）
        if (isMake && bomByParent.TryGetValue(item, out var rows))
            foreach (var row in rows) {
                var childUsage = ComputeUsage(item, row, qty, specByCd, yieldByFlute);
                if (childUsage <= 0) continue;
                var ca = Get(row.MaterialCd);
                ca.Gross += childUsage;                     // 子料毛需求汇入更低层(共用料只 net 一次)
                if (releaseDate < ca.EarliestRequired) ca.EarliestRequired = releaseDate;
                ca.Peggings.Add((ParentPlannedOrder, item, childUsage));
            }
    }
}
```
**净需求公式**：`net = Gross - Supply.Total - SafetyStock`(下限0)，`Supply.Total = OnHand+InTransit+InWip+FirmPlanned`。

**子料用量展开** `ComputeUsage`（`:197-213`，纸箱特性）：尺寸料(`UsageType==1 || MaterialTypeDiv=="4"`)经 `_usage.CalcDimensional(w, f, yield, parentQty) * coeff`；定额料经 `CalcFixed`。**与 ERP 見積共用 `IMaterialUsageCalculator`**（`CalcDimensional = (W*F/1e6)*yield*outputQty`）。

**批量定批** `ApplyLotRule`（`:216-229`）：
```csharp
case PlanLotRule.Moq: return Math.Max(net, pol.MoqQty ?? 0m);
case PlanLotRule.Multiple: case PlanLotRule.RoundRoll:
    var m = pol.MultipleQty ?? 0m; return m>0 ? Math.Ceiling(net/m)*m : net;
default: return net;   // LotForLot
```

**步骤 5 落 Pegging + 完成**（`:173-194`）：`SaveChanges` 取 `po.Id` → 落 `Plan_Pegging` → `run.Status=Completed`。
**错误码**：`E-PLAN-循环BOM`(`LowLevelCodeService:48`)、`E-PLAN-无需求`(`MrpController:62`)。⚠️ 成环时 `Plan_MrpRun` **不会置 Failed**(引擎无 try/catch，异常冒泡，批次停 Running)——未做的边界。

---

## 三、四源供给汇总 — SupplyService.GetSupplyBreakdownAsync

引擎净算时每 Item×bucket 调一次（`SupplyService.cs:21-57`）：
- ① 现库存：`Stocks(ProductCd && !IsDeleted && !RecallFlag).Sum(AvailableQty)`(公司级，剔召回)。
- ② 在途：`InboundOrderDetails ⨝ InboundOrders(Status 確定済/入庫中 && ExpectedQty>ReceivedQty).Sum(ExpectedQty-ReceivedQty)`。
- ③ 在制：`WorkOrders(Status∈{Confirmed,Issued,InProgress,Interrupted,Inspected} && ProductionQty>CompletedQty).Sum(ProductionQty-CompletedQty)`。
- ④ scheduled receipt：`PlannedOrders(Status∈{Confirmed,Converted} && RequiredDate<=bucket).Sum(Qty)`(**建议态不计**)。
- `Total = OnHand+InTransit+InWip+FirmPlanned`，明细落 `Plan_NetRequirement` 供看板钻取。

---

## 四、MRP 运算触发 + 看板 — POST /api/plan/mrp/run

**前端**：`/plan/mrp`（`router/index.ts:60`）→ `MrpBoardView.vue`，`mrpApi.run({fromOpenOrders:true})`(`:116`)，双表(计划订单+净需求)，按状态显示确认/转单/忽略按钮。
**后端**：`MrpController.Run`（`:52-66`）：`fromOpenOrders` 走 `BuildFromOpenOrdersAsync`(`OrderDetails⨝Orders` 剔 Cancelled/Shipped，剩余量>0，纳期/WebOrderNo 派生需求)否则用 `dto.Demands`；空需求→`E-PLAN-无需求`；调 `_engine.RunAsync` 返回 `{runId,runNo,status}`。钻取 `Runs/PlannedOrders(id)/NetRequirements(id)`。

---

## 五、⭐ 计划转单 PlanConvert → PR/工单（当前桩 MP-D4）

**端点**：`MrpController` `Confirm`/`Convert`/`Ignore`(`:89-110`)。
**后端** `PlanConvertService.cs`：
- `ConfirmAsync`(`:22-31`)：非 Suggested→`E-PLAN-状态非法`；置 `Confirmed`(即计入 scheduled receipt 供给)。
- `ConvertAsync`(`:33-54`)：已转单→`E-PLAN-已转单`/已忽略→`E-PLAN-已忽略`；按类型分派：
```csharp
var docNo = po.Type == (int)PlannedOrderType.Purchase
    ? await _prService.CreatePrFromPlannedOrderAsync(po, peggings)
    : await _woService.CreateWorkOrderFromPlannedOrderAsync(po, peggings);
po.Status = (int)PlannedOrderStatus.Converted; po.ConvertedDocNo = docNo;
```
- `IgnoreAsync`(`:56-65`)：已转单不可忽略(`E-PLAN-已转单`)；否则 `Ignored`。

> ⚠️ **转单确为桩**（DI `Program.cs:233-235`）：`PlanToPrServiceStub.CreatePrFromPlannedOrderAsync => Task.FromResult($"PR-STUB-{plannedOrder.ItemCd}")`、`PlanToWorkOrderServiceStub => "WO-STUB-{ItemCd}"`，**不实建 PR/工单**。`ConvertedDocNo` 回填桩号。注释三处明示 MP-D4 待采购/MES 真实落地后经 DI 替换实现，**无需改引擎**。

**错误码全清单**（grep `PlanConvertService.cs`）：`E-PLAN-状态非法`(`:26`)、`E-PLAN-已转单`(`:37,60`)、`E-PLAN-已忽略`(`:39`)、`E-PLAN-计划订单不存在`(`:69`)。

---

## 涉及文件清单

| 层 | 文件 | 职责 |
|---|---|---|
| BE Controller | `Controllers/Plan/MrpController.cs` / `ItemPlanningPolicyController.cs` | MRP 运算/钻取/转单 / 策略 CRUD |
| **BE 引擎** | `Services/Plan/MrpEngine.cs` | ⭐净需求引擎(低层码/净算/批量/BOM展开/Pegging) |
| BE Service | `Services/Plan/LowLevelCodeService.cs` | Kahn 拓扑低层码+成环检出 |
| BE Service | `Services/Plan/SupplyService.cs` | 四源供给汇总 |
| BE Service | `Services/Plan/ItemPlanningPolicyService.cs` | 策略取值/默认合成/提前期/CRUD |
| BE Service | `Services/Plan/PlanConvertService.cs` | 确认/转单/忽略状态机 |
| BE 桩 | `Services/Plan/Contracts/PlanToPrServiceStub.cs` / `PlanToWorkOrderServiceStub.cs` | ⭐转单桩(PR-STUB-/WO-STUB-, MP-D4) |
| BE 共享 | `Services/Common/IMaterialUsageCalculator.cs` | 用量内核(与見積共享) |
| 实体 | `DomainModels/Plan/Plan_ItemPlanningPolicy.cs`/`Plan_MrpRun.cs`/`Plan_PlannedOrder.cs`/`Plan_Pegging.cs`/`Plan_NetRequirement.cs` | — |
| FE | `views/plan/MrpBoardView.vue`/`ItemPolicyView.vue`、`api/plan/plan.ts`、`types/plan/plan.ts` | — |

## 关键发现
1. **净需求公式逐字**：`net = Gross - Supply.Total - SafetyStock`(下限0)，`Supply.Total = OnHand+InTransit+InWip+FirmPlanned`。
2. **低层码 Kahn 拓扑**，共用料取最深层级，成环抛 `E-PLAN-循环BOM`。
3. **四种批量法** Moq/Multiple/RoundRoll/LotForLot 在 `ApplyLotRule`(RoundRoll P1 与 Multiple 同口径)。
4. ⭐ **转单确为桩**(`PR-STUB-`/`WO-STUB-`)，DI 实际注册 Stub，不实建单，MP-D4 待落地。
5. **用量内核跨模块复用**(見積 EstimateCalcService 与 MRP 共用 `CalcDimensional`)。
6. **错误码全集**(grep)：`E-PLAN-无需求/已转单/状态非法/已忽略/计划订单不存在/循环BOM/品目必填/策略不存在`。⚠️ `MP-D1~D6` 是设计决策编号非代码错误码。
7. **未做边界**：成环时 `Plan_MrpRun` 不置 Failed；供给不细分仓库位；net-change 增量复算未做(P1=regenerative)。
