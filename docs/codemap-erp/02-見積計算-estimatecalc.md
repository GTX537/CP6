# 02 · 見積計算書 EstimateCalc（PA010）

> 先读 [`README.md` §0 公共约定](README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位

- **带计算逻辑的 3 步向导**：Step1 基本信息 → Step2 工程明细 → Step3 计算结果。**计算只发生在 Step3 → 后端 `CalculateAsync`**，前端只触发、回填、展示，公式不在前端。
- 双页架构：列表页用 `window.open` 新页签打开编辑页，编辑页保存后 `postMessage` 通知列表刷新。
- 计算与保存**解耦**：算出的单价先回填进 `store.basicInfo`，再随保存动作落库（计算本身不落库）。
- 采番 `EMC`；实体 `EstimateCalc : BaseBizEntity`，主键 `QtnCalcNo`。

---

## ⭐ 计算（試算/再計算）— POST /api/estimate-calcs/calculate

**这是本功能的核心，也是"计算在哪做、怎么做"的答案。**

**页面入口**：`/estimate-calc`（`router/index.ts:62`，独立窗口 `:164`）→ 向导壳 `views/erp/EstimateCalcView.vue`；计算页是 Step3 子组件 `views/erp/estimate/Step3Result.vue`。

**前端**
- 触发：Step3「再計算」按钮（`Step3Result.vue:16`）+ **进入第 3 页 `onMounted` 自动算一次**（`:142-144`）。
- `runCalc()`（`Step3Result.vue:119-139`）：
```js
const dto = { ...form.value, processes: store.processRows }
const res = await estimateCalcApi.calculate(dto)
if (res.code === 0) {
  result.value = res.data
  form.value.estimateSqm = res.data.estimateSqm
  form.value.standardUnitPrice = res.data.standardUnitPrice
  form.value.estimateUnitPrice = res.data.estimateUnitPrice
  if (form.value.confirmedUnitPrice == null || form.value.confirmedUnitPrice === 0) {
    form.value.confirmedUnitPrice = res.data.confirmedUnitPrice  // 仅空/0 时回填，保留用户手改
  }
  store.calcResult = res.data as any
}
```
- api（`api/erp/estimateCalc.ts:58-60`）`calculate(data) → http.post('/estimate-calcs/calculate', data)`。
- type：响应 `EstimateCalcResult`（`types/erp/estimateCalc.ts:189-196`）：`estimateSqm/standardUnitPrice/estimateUnitPrice/confirmedUnitPrice/amountRows[]/notes[]`。

**后端**
- Controller（`EstimateCalcController.cs:135-141`）：
```csharp
[HttpPost("calculate")]
[AllowAnonymous]                              // ← 注意：类级 [Authorize] 但 calculate 单独豁免（纯计算无副作用）
public async Task<IActionResult> Calculate([FromBody] EstimateCalcDto dto)
{
    var result = await _service.CalculateAsync(dto);
    return Ok(new { code = 0, message = "OK", data = result });
}
```
- Service `CalculateAsync`（`EstimateCalcService.cs:239`）。**唯一计算引擎**。

### 计算逻辑专讲（核心）

顶部 4 常量（`EstimateCalcService.cs:241-244`）：
```csharp
const decimal paperPriceFallback = 30m;
const decimal yieldRateFallback = 1.00m;
const decimal processCostPerSheet = 5m;
const decimal profitRate = 1.20m;
```

计算链路（步骤 + 真实代码点）：

**① 取原紙単価 paperPrice**（fallback 30）：先查通用码 `M_GenericCode(GroupCode='Paper', Code=PaperCdF).Num1`（`:251-260`）。
**② PA130 见積用シート単価マスタ覆盖**（`:266-285`）——若得意先×段一致且有客户协商价，覆盖通用码价：
```csharp
var sheetPrice = await _db.SheetUnitPriceEstimates.AsNoTracking()
    .Where(x => !x.IsDeleted && x.BaseCd == baseCd && x.CustomerCd == dto.CustomerCd
        && x.SheetFlute == dto.SheetFlute
        && (dto.PaperCdF == null || x.PaperCdF == dto.PaperCdF)
        && x.RevisionDate <= today)
    .OrderByDescending(x => x.RevisionDate)
    .Select(x => (decimal?)x.UnitPrice).FirstOrDefaultAsync();
if (sheetPrice is decimal sp && sp > 0) { paperPrice = sp; }
```
**③ 取段成率 yieldRate**（fallback 1.00）：`M_GenericCode(GroupCode='M067', Code=SheetFlute).Num1`（`:288-299`）。
**④ 面積**（`:302-309`）：`面積/枚 = W×F/1,000,000 (m²)`；`見積面積 = 面積/枚 × 受注数量`（round 4）。
**⑤ 工程原価**（`:311-312`）：`工程明细行数 × 5円`。
**⑥ 原紙原価 + 标准原价 + 见积单价**（`:314-321`）：
```csharp
decimal paperUsagePerSheet = _usageCalc.CalcDimensional(dto.SheetDimW ?? 0, dto.SheetDimF ?? 0, yieldRate, 1);
decimal paperCostPerSheet = paperPrice * paperUsagePerSheet;
decimal stdPrice = paperCostPerSheet + processCost;
result.StandardUnitPrice = Math.Round(stdPrice, 3);
result.EstimateUnitPrice = Math.Round(stdPrice * profitRate, 3);   // 利益率 ×1.20
result.ConfirmedUnitPrice = result.EstimateUnitPrice;
```
**共享内核** `MaterialUsageCalculator.CalcDimensional`（`Services/Common/IMaterialUsageCalculator.cs:33-34`）：
```csharp
public decimal CalcDimensional(decimal sheetDimW, decimal sheetDimF, decimal yieldRate, decimal outputQty)
    => (sheetDimW * sheetDimF / 1_000_000m) * yieldRate * outputQty;
```
> **見積 与 MRP（MrpEngine）共用同一公式**（grep 确认 `MaterialUsageCalculator` 出现在 `MrpEngine.cs` 等 8 文件），保证单耗算法一致。

**⑦ 各数量段金额**（`:323-337`）：对 `EstimateQtys[8]` 中 >0 项，`Amount = round(q × EstimateUnitPrice, 0)`。
**⑧ Notes 透明化**（`:339-346`）：把每步公式拼成日文说明回前端展示（Step3 的「計算ロジック」卡片）。

公式一图流：
```
paperPrice ← M_GenericCode(Paper) → PA130 客户协商价覆盖 → fallback 30
yieldRate  ← M_GenericCode(M067)  → fallback 1.00
用量/枚    = CalcDimensional(W,F,yieldRate,1) = (W×F/1e6)×yieldRate     ← 共享内核
原紙原価/枚 = paperPrice × 用量/枚
標準原価   = 原紙原価/枚 + 工程行数×5            → round(3)
見積単価   = 標準原価 × 1.20（利益率20%）        → round(3)
確定単価   = 見積単価（可手改）
見積面積   = 面積/枚 × OrderQty                  → round(4)
AmountRows[i] = round(EstimateQtys[i] × 見積単価, 0)
```

**校验与错误码**：calculate 接口**无业务校验**（缺字段 `?? 0` 兜底，主数据缺失走 fallback），无错误码。
**数据流**：`runCalc()` → `calculate` → `CalculateAsync` 查 `M_GenericCode`+`T_SheetUnitPriceEstimate` → 算单价 → `EstimateCalcResult` → 前端回填 `store.basicInfo` 展示（**不落库**）。

---

## 保存（登録/訂正）— POST /api/estimate-calcs（新建）/ PUT /api/estimate-calcs/{no}（修改）

**前端**：向导壳「保存」（`EstimateCalcView.vue:68-75`）→ `onSave()`（`:243-273`）：Step1 时先 `step1Ref.validate()`，再按操作种别分支 `create`/`update`，成功 `store.loadBasicInfo` + `notifyOpener('saved')`；`catch` 走 `handleConflict(e)`（409）。
- Step1 校验 `validate()`（`Step1BasicInfo.vue:567-581`）= `formRef.validate()`（rules）+ `validateBusiness(form)`，`defineExpose({ validate })`。
- api：`create`（`estimateCalc.ts:30-32`）、`update`（`:35-40`）。type 请求体 `EstimateCalcDto`（含 `rowVersion`）。

**后端**
- Controller `Create`（`:53-59`）`CreateAsync` → 回查 `GetByNoAsync`；`Update`（`:65-87`）catch `KeyNotFound→404`、`DbUpdateConcurrency→409 MSG-W10002`。
- Service `CreateAsync`（`EstimateCalcService.cs:94-121`）：采番 `DocNumber.NextAsync(_db,"EMC")`，`QtnCalcNo=$"{mainNo}-01"`；`ApplyDto`（`:353-430`）把扁平数组 `StrategicDivs[10]→StrategicDiv01..10`、`EstimateQtys[8]→EstimateQty01..08`、`PalletCnts[8]` 写回实体；工程明细全作新增。
- Service `UpdateAsync`（`:123-166`）：乐观锁 `OriginalValue`（`:131-134`）；明细 diff 按 `SeqNo`（缺失软删/已有更新/新增）。
- 实体 `EstimateCalc`（`DomainModels/Erp/EstimateCalc.cs`，`T_EstimateCalc`，主键 `QtnCalcNo` `:21`，计算结果落库字段 `EstimateSqm/StandardUnitPrice/EstimateUnitPrice/ConfirmedUnitPrice` `:223-226`）；子表 `EstimateCalcProcess`（`T_EstimateCalcProcess`，导航 `Processes` `:240`）。

**校验与错误码（grep `useValidation.ts` 实测）**：
- Step1 rules（`useValidation.ts:13-102`）：`MSG-111 商品コード`/`MSG-112 見積日`/`MSG-113 見積拠点`/`MSG-114 受注拠点`/`MSG-115 担当者`/`MSG-116 顧客コード`/`MSG-117 受注形態`/`MSG-118 商品大分類`/`MSG-119 商品中分類`/`MSG-120 顧客品名1`/`MSG-121 受注数量(>0)`/`MSG-122 親子区分`/`MSG-123 シート・フルート`/`MSG-124 原紙(F)`/`MSG-125 印刷(F)`/`MSG-126 最終工程`/`MSG-127 形状1`/`MSG-128 流通区分`/`MSG-129 単位`。
- 业务校验 `validateBusiness`（`:108-128`）：`MSG-W10010 小分類有但中分類空`/`MSG-W10011 見積数量≥1件`/`MSG-W10012 刃渡り填则流れ必須`。
- 后端 409：`MSG-W10002`（`EstimateCalcController.cs:84`），前端 `useConflictHandler.ts:42` 弹「最新版を取得」。

**数据流**：`onSave` →（validate）→ `create/update` → `CreateAsync/UpdateAsync`(ApplyDto+采番/乐观锁+明细 diff) → `SaveChangesAsync` 落 `T_EstimateCalc`+`T_EstimateCalcProcess` → 回查 → `loadBasicInfo` + postMessage 刷新列表。

---

## 加载（按 No 取明细）— GET /api/estimate-calcs/{no}

**前端**：`loadByNo()`（`EstimateCalcView.vue:206-223`）→ `getByNo` → `loadBasicInfo`；独立窗口 `onMounted` 按 `?op=&no=` 自动加载，View 模式传 `includeDeleted=true`。
**后端**：Controller `Get`（`:41-47`）为空返 404 `MSG-102`「見積計算書NOが未登録です。」；Service `GetByNoAsync`（`:85-92`）`Include(Processes)` + `ToDto`（数组还原、软删明细排除）。

---

## 列表查询 — GET /api/estimate-calcs

**前端**：`/estimate-calc-list`（`router/index.ts:63`）→ `EstimateCalcListView.vue`，`loadData()`（`:171-189`）→ `getList`；行操作 `onView/onEdit/onCopy` 经 `openInWindow` 以 `window.open('/estimate-calc/window?op=&no=')` 新页签打开，`handleMessage` 监听子窗口 saved/deleted 自动刷新。
**后端**：Controller `GetList`（`:30-35`）；Service `GetPageListAsync`（`:46-83`）`Where(!IsDeleted)` + 条件 + `QuerySort.Apply`（白名单 `EstimateCalcSortMap`，默认 `QtnDate desc, QtnCalcNo desc`）+ 分页投影 `EstimateCalcListItem`。无错误码。

---

## 删除（軟削除）— DELETE /api/estimate-calcs/{no}

**前端**：`onDelete()`（`EstimateCalcView.vue:275-297`）确认 → `remove(no, rowVersion)`（body 带 rowVersion）→ `store.reset()` + `notifyOpener('deleted')`。
**后端**：Controller `Delete`（`:94-110`）catch 404/409；Service `DeleteAsync`（`:168-183`）**软删** `IsDeleted=true` + 乐观锁。错误码 404 / 409 `MSG-W10002`。

---

## 复制（コピー/流用）— POST /api/estimate-calcs/{no}/copy

**前端**：`onOpChange` 切 Copy → `copy(no)` → `loadBasicInfo`。
**后端**：Controller `Copy`（`:116-129`）；Service `CopyAsync`（`:185-221`）重新采番 `EMC`、`RefQtnCalcNo=源号`、`RowVersion=null`、明细整体复制为新行（不重算，搬运已算好的单价）。

---

## 涉及文件清单

| 层 | 文件 | 关键 |
|---|---|---|
| FE 壳 | `cp6.web/src/views/erp/EstimateCalcView.vue` | 3 步壳、onSave/onDelete/loadByNo、URL 驱动 `:327` |
| FE 列表 | `cp6.web/src/views/erp/EstimateCalcListView.vue` | 查询/分页/排序、新页签+postMessage |
| FE Step1 | `cp6.web/src/views/erp/estimate/Step1BasicInfo.vue` | 基本信息、`validate()` `:567`、defineExpose |
| FE Step3（计算页） | `cp6.web/src/views/erp/estimate/Step3Result.vue` | **runCalc() `:119`、onMounted 自动算 `:142`、Notes 展示** |
| FE store | `cp6.web/src/stores/estimate.ts` | basicInfo/processRows/calcResult、loadBasicInfo |
| FE api | `cp6.web/src/api/erp/estimateCalc.ts` | getList/getByNo/create/update/remove/copy/**calculate `:58`** |
| FE type | `cp6.web/src/types/erp/estimateCalc.ts` | DTO/Result `:189`/ListItem/Query |
| FE 校验 | `cp6.web/src/composables/useValidation.ts` | MSG-111~129 + MSG-W10010~12 |
| FE 字段控制 | `cp6.web/src/composables/useFieldControl.ts` | 操作种别×字段矩阵 |
| FE 冲突 | `cp6.web/src/composables/useConflictHandler.ts` | 409 MSG-W10002 处理 |
| BE Controller | `CP6.WebApi/Controllers/Erp/EstimateCalcController.cs` | 7 端点；calculate `:135` `[AllowAnonymous]` |
| BE Service | `CP6.Core/Services/Erp/EstimateCalcService.cs` | **CalculateAsync `:239`**；CRUD+采番+乐观锁+映射 |
| BE 共享内核 | `CP6.Core/Services/Common/IMaterialUsageCalculator.cs` | `CalcDimensional `:33`（見積/MRP 共用） |
| BE 实体 | `CP6.Entity/DomainModels/Erp/EstimateCalc.cs` / `EstimateCalcProcess.cs` | `T_EstimateCalc`(+单价字段 `:223`) / `T_EstimateCalcProcess` |
| BE PA130 联动 | `CP6.Entity/DomainModels/Erp/SheetUnitPrice.cs` | `SheetUnitPriceEstimate` `:52` 客户协商シート単価 |
| BE DTO | `CP6.Entity/DTOs/Erp/EstimateCalcDto.cs` | DTO/Query/Result/ListItem |
| 测试 | `CP6.Tests/EstimateCalcServiceTests.cs` 等 | 服务/回归/内核测试 |

## 关键发现
1. **计算落点单一**：唯一引擎 `CalculateAsync`（`:239`），公式不在前端。
2. **calculate 接口 `[AllowAnonymous]`**（纯计算无副作用，与类级 `[Authorize]` 共存）。
3. **计算与保存解耦**：算出值经 Step3 回填 `store.basicInfo` 再走 create/update 才落库。
4. **价格覆盖优先级**：PA130 客户协商价 > `M_GenericCode(Paper)` > fallback 30；段成率 `M067` > fallback 1.00。
5. **共享内核一致性**：`MaterialUsageCalculator` 同时被見積与 MRP 消费。
6. 前端组件实际在 `views/erp/estimate/`（`components/estimate/` 只有 `RecycleLawDialog.vue` 容利法弹窗，非计算主线）。
