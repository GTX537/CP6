# 03 · 御見積書 Quotation（PA030/040）

> 先读 [`README.md` §0 公共约定](README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位 —— 御見積 ↔ 見積計算 的关系（核心结论）

**关键事实（grep 实证）**：`EstimateCalcService.cs` 对 `Quotation` 的引用数 = **0**（`Grep "Quotation"` → No matches）。

所以「見積 → 御見積」的束ね（绑定/转写）**完全是 御見積側（Quotation）的 pull 模型**——見積側不 push、不持反向引用。束ね真实路径：
1. Quotation 用 `customerCd + 案件NO(親/子/材質)` 查 `T_EstimateCalc` 拿候选（`GetCalcCandidatesAsync`）；
2. 前端勾「使用✓」→ 在中间表 `T_QuotationCalc` 插一行（存 `QtnCalcNo`），同时**自动复制**计算书展示字段（品名/数量/単価/金額）到打印明细 `T_QuotationDetail`；
3. `T_QuotationCalc` **只存 `QtnNo+QtnCalcNo`+状态FLG，不冗余存计算书内容**；每次读详情再 JOIN `T_EstimateCalc` 回填展示字段；
4. 確定登録时校验所勾选计算书 `QtnDiv == "20"`（決定見積）才允许确定。

三个束ね代码点：候选 JOIN `QuotationService.cs:625-635,648-660`；详情 JOIN 回填 `:178-184,793-803`；前端勾选复制到明细 `QuotationView.vue:626-653`。采番 `QTN`；实体 `Quotation : BaseBizEntity`。

---

## 列表查询 — GET /api/quotations

**前端**：`/quotation-list`（`router/index.ts:65`）→ `QuotationListView.vue`。点击行/操作用 `window.open('/quotation/window')` 独立窗口（`:289-297`）。状态多选 checkbox 值 `"0"/"9"/"C"`（`:45-49`），watch 映射 `query.statuses`（`:218`）。`loadData`（`:235-259`）发请求前剔空串字段。api `getList`（`quotation.ts:16-21`，`paramsSerializer:{indexes:null}` → `statuses=0&statuses=9`）。
**后端**：Controller `GetList`（`QuotationController.cs:30-35`）→ `{rows,total}`。Service `GetPageListAsync`（`:48-159`）：`!IsDeleted`+条件；**状态组合过滤**（`:74-80`）：
```csharp
q = q.Where(x =>
    (query.Statuses.Contains("0") && x.EstimateCheckFlg == 0 && x.MasterConfirmFlg != 9)
    || (query.Statuses.Contains("9") && x.EstimateCheckFlg == 9 && x.MasterConfirmFlg != 9)
    || (query.Statuses.Contains("C") && x.MasterConfirmFlg == 9));
```
排序白名单 `QuotationSortMap`（`:25-37`），默认 `StaffCd→CustomerCd→QtnNo`；分页后**批量补**第1行明细 `firstDetails`、担当者名 `staffMap`。综合状态文案 `BuildStatusText`（`:667-672`）。无业务错误码（纯查询）。

---

## ⭐ 取得関連見積計算書候选（束ね候选）— GET /api/quotations/calcs

**这是「見積→御見積」束ね的入口动作。**

**前端**：详情页 `/quotation`（`router/index.ts:64`）Tab3「③ 関連見積計算書」（`QuotationView.vue:217-281`，「使用」勾选列 `:240-248`）。联动：`watch([customerCd, projectNoParent/Child/Material])` 300ms 防抖（`:752-760`）→ `refreshCalcCandidates`（`:728-748`）。api `getCalcCandidates`（`quotation.ts:87-95`）。type `QuotationCalcCandidate`（`types/erp/quotation.ts:163-175`，含 `isLinked`）。
**后端**：Controller `GetCalcCandidates`（`QuotationController.cs:219-233`），`customerCd` 空→`BadRequest`。Service `GetCalcCandidatesAsync`（`:610-661`）：
```csharp
var q = _db.EstimateCalcs.AsNoTracking().Where(e => !e.IsDeleted && e.CustomerCd == customerCd);
if (!string.IsNullOrWhiteSpace(projectNoParent)) q = q.Where(e => e.ProjectNoParent == projectNoParent);
// ... child / material 同理
```
候选投影取 `QtnCalcNo, QtnDate, CustomerProductName1/2, DecidedQty, ConfirmedUnitPrice, Unit, QtnDiv`；若传 `currentQuotationNo`，查 `T_QuotationCalc` 标 `IsLinked`。

**字段映射（見積計算 → 御見積候选）**：
| 御見積候选 | 来源 EstimateCalc | 行 |
|---|---|---|
| `EstimateQty` | `DecidedQty`（決定予想数） | `:654` |
| `ConfirmedUnitPrice` | `ConfirmedUnitPrice`（確定単価） | `:655` |
| `Unit` | `Unit` | `:656` |
| `Amount` | `DecidedQty × ConfirmedUnitPrice` | `:657` |
| `CustomerProductName1/2` | 同名 | `:652-653` |
| `QtnDiv` | `QtnDiv`（10通常/20決定） | `:658` |
> `ConfirmedUnitPrice` 在計算側默认 = `EstimateUnitPrice`（`EstimateCalcService.cs:321`）。

校验：`customerCd is required`（`QuotationController.cs:228`，无 msgId）。

---

## ⭐ 勾选「使用✓」束ね（纯前端，随后跟保存落库）

**这是束ね"转过来"的核心：見積計算書 → 御見積明細 的复制。** 只在前端 `form` 内进行。

**前端** `toggleLinked`（`QuotationView.vue:623-661`），勾选时：
```ts
form.calcs.push(newCalc)
const nextNo = (form.details[form.details.length - 1]?.detailNo ?? 0) + 1
form.details.push({
  detailNo: nextNo,
  itemName1: cand.customerProductName1,
  itemName2: cand.customerProductName2,
  quantity: cand.estimateQty,
  unitPrice: cand.confirmedUnitPrice,
  unit: cand.unit,
  amount: cand.amount,
  printTotalFlg: true,
  qtnCalcNo: cand.qtnCalcNo,        // ← 回填来源計算書NO
})
```
取消勾选（`:654-659`）：按 `qtnCalcNo` 从 `form.calcs`/`form.details` 过滤掉 + `renumberDetails()`。末尾 `recalcTotalAmount()`（`:660`）。

**衔接特性**：束ね带出的明细行带 `qtnCalcNo`，删除按钮被禁用 `:disabled="isPageReadOnly || !!row.qtnCalcNo"`（`:371`）——必须通过取消「使用✓」移除，手工不可删。
**合計再計算** `recalcTotalAmount`（`:691-696`）：**只累加 `printTotalFlg=true` 的行**。

---

## 加载详情 — GET /api/quotations/{no}

**前端**：详情页「読込」（`QuotationView.vue:38`）→ `loadByNo`（`:763-782`）；独立窗口 `onMounted` 按 `?op=&no=`（`:1041-1105`）。加载后 `loadForm`（`:603-616`）保证 `qtnNotes(15)`/`calcNotes(8)` 长度并 `await refreshCalcCandidates()` 回显 Tab3 勾选。api `getByNo`（`quotation.ts:24-29`）。
**后端**：Controller `Get`（`QuotationController.cs:41-48`），null→404 `MSG-102`。Service `GetByNoAsync`（`:165-187`）：`Include(Calcs/Details where !IsDeleted)`，**回填**——取 `Calcs` 的 `QtnCalcNo` JOIN `T_EstimateCalc` 构 `calcMap`（`:177-184`）：
```csharp
var calcMap = await _db.EstimateCalcs.AsNoTracking()
    .Where(e => calcNos.Contains(e.QtnCalcNo))
    .Select(e => new CalcSnapshot(e.QtnCalcNo, e.QtnDate, e.CustomerProductName1, e.CustomerProductName2,
        e.DecidedQty, e.ConfirmedUnitPrice, e.Unit, e.QtnDiv))
    .ToDictionaryAsync(e => e.QtnCalcNo);
```
`ToDto(entity, calcMap)`（`:733-824`）的 `QuotationCalcDto` 展示字段从 `calcMap` 回填（`:793-803`）。

> **衔接专讲**：中间表 `T_QuotationCalc`（实体 `:16-39`）只存 `QtnNo+QtnCalcNo+各FLG`，**无品名/单价**。展示内容每次读详情实时 JOIN `T_EstimateCalc` 回填（计算书改了 Tab3 展示随之刷新）；但已落地的 `T_QuotationDetail` 打印明细是束ね当时的**快照**，不随计算书变。

校验：`MSG-102`「御見積書NOが未登録です。」（`:46`）。

---

## 新建保存（登録）— POST /api/quotations

**前端**：「保存」（`QuotationView.vue:423`）→ `onSave`（`:822-851`）先 `formRef.validate()`。`cleanPayload`（`:1006-1016`）剔除 calcs 只读 JOIN 字段，只传 `qtnCalcNo + 4个FLG`。api `create`（`quotation.ts:32-34`）。
**后端**：Controller `Create`（`:54-60`）保存后回查。Service `CreateAsync`（`:193-254`）：采番 `DocNumber.NextAsync(_db,"QTN")`+枝番 `-01`（`QtnNo=$"{mainNo}-01"`）；`ApplyDto`（含 15 行 QtnNotes/8 行 CalcNotes 经 `At()` 展开）；関連計算書全新增 `QuotationCalc`；打印明细全新增 + 重排 `DetailNo`，合計逐行求和（`:228-249`）：
```csharp
var amount = (d.Quantity ?? 0) * (d.UnitPrice ?? 0);
d.Amount = amount;
entity.Details.Add(new QuotationDetail { ... Amount = amount, ... });
total += amount;
// entity.TotalAmount = total;  (:249)
```
> **新建时 `T_QuotationCalc` 仅写 `QtnCalcNo` 指针**，明细内容是前端束ね时复制好的快照。

校验：新建无 try/catch；前端 rules（`:519-523`，baseCd/staffCd/customerCd 必填）。
> ⚠️ **合計口径差异（实证）**：后端 `CreateAsync:228-249`/`UpdateAsync:324-362` **全明细行求和**，不看 `PrintTotalFlg`；前端 `recalcTotalAmount:691-696` 只对 `printTotalFlg` 行求和。口径不同。

---

## 修改保存（訂正）— PUT /api/quotations/{no}

**前端**：`onSave` 的 `isEdit` 分支（`:838-845`）→ `update(qtnNo, cleanPayload())`（`quotation.ts:37-42`），`rowVersion` 随提交。
**后端**：Controller `Update`（`:66-93`）catch `KeyNotFound→404`、`InvalidOperation→400 MSG-004`、`DbUpdateConcurrency→409 MSG-W10002`。Service `UpdateAsync`（`:260-365`）：**確定済拒绝**（`:269-270`）：
```csharp
if (entity.MasterConfirmFlg != 0)
    throw new InvalidOperationException("確定登録済のデータとなります。編集する場合は、確定取消を実施ください。");
```
乐观锁 `OriginalValue`；関連計算書按 `QtnCalcNo` diff（缺软删/存更新/新增）；明细按 `DetailNo` diff + 合計重算。
校验码：`MSG-004`（`:82`）、`MSG-W10002`（`:90`）。冲突 409 时前端 `handleConflict`（`:978-1003`）。

---

## 复制新建（コピー/流用）— POST /api/quotations/{no}/copy

**前端**：列表/详情「コピー」→ api `copy`（`quotation.ts:53-57`），返回后 `loadForm` + 强制 `op=Edit`。
**后端**：Controller `Copy`（`:125-138`）；Service `CopyAsync`（`:395-485`）读源→重采番 `-01`→克隆主表，**重置** `RefQtnNo=源号`、`FscMgmtNo=null`、`EstimateCheckFlg=0`、`MasterConfirmFlg=0`、`QtnIssueDate/CalcIssueDate=null`；calcs 克隆但 FLG 全清 0；details 原样克隆含 `QtnCalcNo`。源不存在→404。
> 复制保留束ね关系（`QtnCalcNo` 一并 clone），但承認/確定状态清零 = "复用同一批計算書、作为全新未承認御見積"。

---

## ⭐ 確定登録 — POST /api/quotations/{no}/confirm

**这是束ね收口动作，含「決定見積」闸门。**

**前端**：「確定登録」（`QuotationView.vue:427-433`）→ `onConfirm`（`:879-914`）两段：先切 `Op.Confirm` 让用户在 Tab3 勾「確定」列（`:266-273`）；再收集勾选 → `confirm(qtnNo, { qtnCalcNos, rowVersion })`（`quotation.ts:60-65`）。
**后端**：Controller `Confirm`（`:145-168`）错误码分流（`:160-161`）：
```csharp
var msgId = ex.Message.Contains("決定") ? "MSG-003"
          : ex.Message.Contains("存在しません") ? "MSG-008" : "MSG-002";
```
Service `ConfirmAsync`（`:491-538`）：空选拒绝；**決定見積校验**（`:506-516`）：
```csharp
var targetCalcs = await _db.EstimateCalcs.AsNoTracking()
    .Where(e => req.QtnCalcNos.Contains(e.QtnCalcNo) && !e.IsDeleted)
    .Select(e => new { e.QtnCalcNo, e.QtnDiv }).ToListAsync();
if (targetCalcs.Count == 0) throw new InvalidOperationException("確定登録可能のデータが存在しません。");
if (targetCalcs.Any(c => c.QtnDiv != QtnDivDecided))      // QtnDivDecided = "20"
    throw new InvalidOperationException("決定見積登録されていないデータが含まれます。");
```
通过则主表 `MasterConfirmFlg=9` + 选中 `QuotationCalc.MasterConfirmFlg=9`。
> **衔接专讲**：確定回查 `T_EstimateCalc.QtnDiv`，只有計算書处于「決定見積(20)」才允许确定。这是束ね的业务闸门。

校验码：`MSG-003`（含"決定"）、`MSG-008`（"存在しません"）、`MSG-002`（兜底）、`MSG-W10002`（409）。

---

## 確定取消 / 逻辑删除 / 帳票発行

- **確定取消** `POST /{no}/cancel-confirm`：Controller `:174-195`（`InvalidOperation→400 MSG-009`）；Service `CancelConfirmAsync`（`:540-570`）主表 `MasterConfirmFlg=0` + 全未删 calc 回退 0。
- **削除** `DELETE /{no}`：Controller `:99-119`（body `DeleteRequest`）；Service `DeleteAsync`（`:371-389`）確定済拒绝（`MSG-004`）→ 软删 `IsDeleted=true`。
- **発行** `POST /{no}/issue`：Controller `:201-213`；Service `IssueAsync`（`:576-604`）按 `Q/SC/C` 更新 `QtnIssueDate/CalcIssueDate` + 返回文件名（**当前仅日期+文件名，真实 PDF 留 Phase 5**，`:602` 注释）。

---

## 涉及文件清单

| 层 | 文件 | 关键 |
|---|---|---|
| FE 路由 | `cp6.web/src/router/index.ts` | `:64/65` `/quotation`、`/quotation-list`、`:170` `/quotation/window` |
| FE 详情 | `cp6.web/src/views/erp/QuotationView.vue` | 束ね `623-661`、onSave `822`、onConfirm `879`、onMounted `1041` |
| FE 列表 | `cp6.web/src/views/erp/QuotationListView.vue` | loadData `235`、openInWindow `289` |
| FE api | `cp6.web/src/api/erp/quotation.ts` | 9 端点；getCalcCandidates `87` |
| FE type | `cp6.web/src/types/erp/quotation.ts` | DTO/Query/Candidate/Confirm/Issue |
| BE Controller | `CP6.WebApi/Controllers/Erp/QuotationController.cs` | `api/quotations` 9 动作 + 错误码分流 |
| BE Service | `CP6.Core/Services/Erp/QuotationService.cs` | 全逻辑；JOIN T_EstimateCalc `178/625/793` |
| BE DTO | `CP6.Entity/DTOs/Erp/QuotationDto.cs` | DTO+Query+Candidate+Confirm+Issue |
| BE 实体 | `CP6.Entity/DomainModels/Erp/Quotation.cs` | `T_Quotation` 主表 |
| BE 实体 | `CP6.Entity/DomainModels/Erp/QuotationCalc.cs` | `T_QuotationCalc` 中间表（**只存指针**） |
| BE 实体 | `CP6.Entity/DomainModels/Erp/QuotationDetail.cs` | `T_QuotationDetail` 打印明细（快照，带 QtnCalcNo） |
| BE 源 | `CP6.Entity/DomainModels/Erp/EstimateCalc.cs` | `DecidedQty:207`/`ConfirmedUnitPrice:226`/`QtnDiv:219` |
| BE 源服务 | `CP6.Core/Services/Erp/EstimateCalcService.cs` | **0 处引用 Quotation**（pull 模型证据） |

### 错误码汇总（grep `QuotationController.cs`）
| msgId | HTTP | 位置 | 触发 |
|---|---|---|---|
| MSG-102 | 404 | `:46` | 御見積NO 未登録 |
| MSG-004 | 400 | `:82,113` | 確定済拒绝 訂正/削除 |
| MSG-003 | 400 | `:160` | 確定：含非決定見積(QtnDiv≠20) |
| MSG-008 | 400 | `:161` | 確定：無確定可能データ |
| MSG-002 | 400 | `:161` | 確定：兜底（0 件選択） |
| MSG-009 | 400 | `:189` | 確定取消：無取消可能データ |
| MSG-W10002 | 409 | `:90,117,166,193` | 乐观锁冲突 |
> Service 抛日文平文异常，Controller catch 映射上表 msgId。

## 关键发现
1. **束ね单向 pull**：`EstimateCalcService` 零引用 `Quotation`。
2. **中间表不冗余**：`T_QuotationCalc` 只存指针，内容靠 JOIN 实时回填。
3. **打印明细是快照**：勾「使用✓」时复制生成，落库后不随計算書变。
4. **決定見積闸门**：確定强制 `QtnDiv=="20"`。
5. **合計口径前后端不一致**：后端全行 / 前端仅 `printTotalFlg` 行。
6. **発行未生成真实 PDF**（Phase 5 待实现）。
