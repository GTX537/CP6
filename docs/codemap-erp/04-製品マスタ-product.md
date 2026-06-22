# 04 · 製品マスタ Product（PA050/060）

> 先读 [`README.md` §0 公共约定](README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位

- **5 ページ ウィザード**：部材一覧 → 基本情報 → 工程情報 → 材料設定 → ロット別単価。5 张子表一次提交。
- 子表更新 = **全削除→全再挿入**（更新走物理删 `RemoveRange`，删除走软删 `SoftDeleteChildren`，两者不同，见下）。
- 乐观锁 = SQL Server `ROWVERSION` + 前端专属 `useProductConflictHandler`（见 README §0.5，本文展开完整链路）。
- 编辑页是 **standalone 独立窗口** `/product/window`（`window.open` 打开），保存后 `postMessage` 通知列表。采番 `PRD`。

---

## 列表查询 — GET /api/products

**前端**：`/product-list`（`router/index.ts:67`）→ `ProductMasterListView.vue`，`loadData()`（`:188-211`）剔空串后 `productApi.getList`。状态多选 `statusSel`（0/1/9）watch 映射 `query.statuses`（`:169`）。api `getList`（`product.ts:16-21`，`paramsSerializer:{indexes:null}`）。type `ProductQuery`（`types/erp/productMaster.ts:262-281`）、`ProductListItemDto`（`:243-259`）。
**后端**：Controller `GetList`（`ProductController.cs:29-34`）。Service `GetPageListAsync`（`ProductService.cs:46-117`）：`AsNoTracking().Where(!IsDeleted)`；`ProductCdFrom/To` 用 `string.Compare` 范围、`CustomerItemName1/2` `.Contains`、`ModifyDateTo` 加一天、`Statuses.Contains`；`CountAsync`；`QuerySort.Apply`（白名单 `ProductSortMap`，默认 `OrderBy ProductCd`）；`Skip/Take` 投影。纯查询无错误码。

---

## 新建（登録）— POST /api/products

**前端**：编辑页 `/product/window?op=new`（standalone，`router/index.ts:175-180`），列表 `openInWindow('new')` 打开。保存 `onSave()`（`ProductMasterView.vue:331-364`）新建分支：
```ts
const dto = store.buildDto()
if (store.isNew || store.isCopy) {
  const res = await productApi.create(dto)
  if (res.code === 0) {
    store.loadFromDto(res.data)
    store.setOperationType(ProductOperationType.Edit)
    await runWipCheck()               // 仕掛チェック（mc 無 → level=0）
    notifyOpener('saved')             // postMessage 通知列表刷新
  }
}
```
保存前 `runAllValidations()`（`:255-329`）。store 5 页 state（`productMaster.ts:56-66`），`buildDto()`（`:135-153`）把 5 页+PK+`rowVersion` 组装成单一 `ProductDto`。api `create`（`product.ts:37-39`）。

**后端**
- Controller `Create`（`ProductController.cs:92-98`）`CreateAsync` → `GetByCdAsync` 回查。
- Service `CreateAsync`（`ProductService.cs:167-246`）：
  1. 部材一覧空则默认补 1 行亲部材。
  2. **採番** `DocNumber.NextAsync(_db,"PRD")`，`itemCd=seq`，`setProductCd=$"{itemCd}0001"`。
  3. 行 1（亲）作主表，`productCd=$"{itemCd}{firstRow.RowNo:D4}"`，`ParentChildDiv="0"`，枝番 `Branch2/3=BranchNull("MCNULLVAL")`。
  4. **状态判定**（`:200-202`）：
```csharp
var approvedAtCreate = string.IsNullOrWhiteSpace(firstRow.EstimateCalcNo);
entity.Status = approvedAtCreate ? 1 : 0;          // 有見積計算書NO→0待WF；无→1直接承認済
entity.WfApprovalFlg = approvedAtCreate;
```
  5. 子部材行（行 2+）建多条 `ProductMaster`，`ParentChildDiv="1"`。
  6. **子表插入**（`:239-242`，全挂亲 `productCd`）：
```csharp
AddProcesses(productCd, dto.Processes, userName);
AddMaterials(productCd, dto.Materials, userName);
AddLotPrices(productCd, dto.LotPrices, userName);
AddCoProducts(productCd, dto.CoProducts, userName);
```
  7. `SaveChangesAsync(); return productCd;`

**实体（主 + 4 子表）**：
- 主 `ProductMaster`（`ProductMaster.cs`）：PK 注释「製品コード=品目CD(連番)+枝番1(行番)+枝番2/3(MCNULLVAL)」（`:11`）；导航 `Processes/Materials/LotPrices/CoProducts`（`:209-212`）。
- 工程子表 `ProductProcess`（`T_ProductProcess`）：业务复合 PK=`ProductCd+TaskCd`；工程仕様 `Spec01~10`、製造順 `ManufOrderPrio1~8`、A2 标准工时 `SetupHour/CycleTime/StandardCrewSize`、`SortOrder`。
- 材料 BOM `ProductMaterial`（`T_ProductMaterial`）：PK=`ProductCd+ProcessCd+MaterialCd`；`MaterialTypeDiv`「1仕掛/2連産/3原料/4印刷原紙」；MRP 用量 `UsageType/UnitUsage/UsageUnit`（`:51-58`）。
- `ProductLotPrice`（PK=`ProductCd+DetailNo`）、`ProductCoProduct`（PK=`ProductCd+ProcessCd+RowNo`，工程 0600/0601/0602 トムソン連産品）。
- DTO `ProductDto`（`ProductDto.cs:22-61`），5 子表 `Members/Processes/CoProducts/Materials/LotPrices` + `byte[]? RowVersion`。

**校验与错误码**（`runAllValidations` 前端拦，`ProductMasterView.vue:255-329`）：
- 部材一覧空 → `E10007`（仅前端注释/内联消息 `:258`）→ error，return false。
- 工程为空 → `W20011`（`:264`）→ confirm 后可续。
- ロット別単価为空 → `W20016`（`:275`）→ confirm 后续。
- 基本情報必填：`customerCd`/`customerItemName1`/`setRatio>0`；連産品比率合计=1.0（误差 0.0001）；材料行 `processCd/materialCd/materialTypeDiv` 必填；ロット数量须升序。

**数据流**：`onSave` → `buildDto()` → `create` → `CreateAsync`（採番+主表+子部材+4子表 Add）→ `SaveChangesAsync` → `GetByCdAsync` 回读 → `loadFromDto`。

---

## 加载详情 — GET /api/products/{cd}

**前端**：`onMounted`（`ProductMasterView.vue:415-468`）按 URL `op/cd/quotationNo` 自动加载，或顶部 `loadByCd()`（`:196-211`）。`includeDeleted` 仅 View/Delete 模式传 true。api `getByCd`（`product.ts:29-34`）。store `loadFromDto`（`productMaster.ts:111-132`）展开 5 页+PK+status+rowVersion，`isDirty=false`。
**后端**：Controller `Get`（`:82-89`）未找到→404 `Localizer["製品マスタが未登録です。"]`（注意：静态路由 `next-seq`/`by-quotation` 等须置于 `{cd}` 之前避免冲突）。Service `GetByCdAsync`（`:123-161`）：主表（`includeDeleted=false` 加 `!IsDeleted`）+ 4 子表各 `AsNoTracking()`+`!IsDeleted`+排序键；`BasicInfo=MapToBasicInfo`（70+ 字段）+ 子表 `MapTo*Dto` + `RowVersion=entity.RowVersion`。

---

## ⭐ 编辑保存（訂正）— PUT /api/products/{cd}

**前端**：`onSave()` Edit 分支（`:348-357`）`productApi.update(cd, dto)`；外层 `catch (e) { const handled = await handleConflict(e); if (!handled) throw e }`（`:358-363`）——409 交给冲突处理器。`buildDto` 把当前 `rowVersion` 一并送回。api `update`（`product.ts:42-47`）。
**后端**
- Controller `Update`（`ProductController.cs:101-123`）：catch `KeyNotFound→404`、`DbUpdateConcurrency→409{ msgId:"MSG-W10002" }`。
- Service `UpdateAsync`（`ProductService.cs:252-274`）：
  1. 查主表 `!IsDeleted`，无则 `KeyNotFoundException`。
  2. **乐观锁注入**（`:259-260`）：`_db.Entry(entity).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;`
  3. `MapFromBasicInfo`（70+ 字段回写）。
  4. 状态规则（`:263-266`）：`Status/WfApprovalFlg` **不变更**，`McTransferFlg=false`（再连携对象）。
  5. **子表全删全插**（`:271`）`await ReplaceChildrenAsync(productCd, dto, userName)`。
  6. `SaveChangesAsync()`（并发检测）。

**⭐子表全删全插（`ReplaceChildrenAsync` `:483-498`）**：
```csharp
var oldProcesses = await _db.ProductProcesses.Where(x => x.ProductCd == productCd).ToListAsync();
// ... oldMaterials/oldLotPrices/oldCoProducts 同理
_db.ProductProcesses.RemoveRange(oldProcesses);     // ← 物理删除！
_db.ProductMaterials.RemoveRange(oldMaterials);
_db.ProductLotPrices.RemoveRange(oldLotPrices);
_db.ProductCoProducts.RemoveRange(oldCoProducts);
AddProcesses(productCd, dto.Processes, userName);   // 全量重插
AddMaterials(productCd, dto.Materials, userName);
AddLotPrices(productCd, dto.LotPrices, userName);
AddCoProducts(productCd, dto.CoProducts, userName);
```
> 注释（`:270`）：「子表=全削除→再挿入（複雑な diff は Phase 3 以降で最適化）」。注意是**物理删**（与删除主流程的软删不同）。重建细节：`AddProcesses`（`:524-588`）`sortIdx+=10` 重排、`Specs[0..9]→Spec01~10`、`ManufOrderPrios[0..7]→ManufOrderPrio1~8`。

**⭐乐观锁冲突处理专讲**
- 后端 `RowVersion` 由 `[Timestamp]` 标注（`BaseBizEntity.cs:24-25`），EF 自动并发列；Update 把 `dto.RowVersion` 设为 `OriginalValue`；`SaveChangesAsync` 的 `UPDATE ... WHERE RowVersion=@original` 行数 0 即抛 `DbUpdateConcurrencyException` → Controller 409 `{ code:409, message, msgId:"MSG-W10002" }`。
- 前端 RowVersion 流转：`GetByCdAsync` 把 `entity.RowVersion(byte[])` 放进 `ProductDto.RowVersion`（Base64 下传）→ `store.loadFromDto` 存 `rowVersion`（`productMaster.ts:130`）→ `buildDto` 回送（`:151`）。
- 冲突弹窗 `useProductConflictHandler.handle(err)`（`useProductConflictHandler.ts:26-66`）：
```ts
const status = axErr?.response?.status
if (status !== 409) return false
const body = axErr.response?.data
const msg = body?.message ?? t('更新が競合しました。')
const msgId = body?.msgId ?? 'MSG-W10002'
try {
  await ElMessageBox({
    title: t('排他制御エラー'),
    message: h('div', null, [
      h('p', null, `[${msgId}] ${msg}`),
      h('p', { style: 'color:#909399;font-size:12px;margin-top:8px' },
        t('他のユーザーが先に更新しています。最新版を読み込んで再編集してください。')),
    ]),
    type: 'warning',
    confirmButtonText: t('最新版を取得'),
    cancelButtonText: t('キャンセル'),
    showCancelButton: true,
  })
} catch { return true }
// 确认「最新版を取得」后（:52-65）：
const cd = store.productCd
const res = await productApi.getByCd(cd)
if (res.code === 0) {
  store.loadFromDto(res.data)                       // 拉回最新 rowVersion
  store.setOperationType(ProductOperationType.Edit)
  ElMessage.success(t('最新データを取得しました。もう一度保存してください'))
}
return true                                          // 已处理 → view 不再 throw
```
view 解构注入：`const { handle: handleConflict } = useProductConflictHandler()`（`ProductMasterView.vue:107`）。

校验码：404 `KeyNotFoundException`（`ProductService.cs:256`）；409 `MSG-W10002`（`ProductController.cs:114-122`）。前端校验同新建。

---

## 删除（軟削除）— DELETE /api/products/{cd}

**前端**：两入口——列表行 `onDelete(row)`（`ProductMasterListView.vue:294-313`，`mcTransferFlg=true` 前置拦截，**不传 rowVersion**）；编辑器 Delete（`ProductMasterView.vue:366-389`，`productApi.remove(cd, store.rowVersion)` **带 rowVersion**，catch 走 `handleConflict`）。api `remove`（`product.ts:50-55`，body `{rowVersion}`）。
**后端**：Controller `Delete`（`ProductController.cs:126-142`，body `DeleteRequest`）。Service `DeleteAsync`（`:280-297`）：查主表 `!IsDeleted`；`rowVersion` 设 `OriginalValue`；主表 `IsDeleted=true`；**子表同步软删** `SoftDeleteChildrenAsync`（`:500-522`）对 4 子表 `ExecuteUpdateAsync` 批量置 `IsDeleted=true`：
```csharp
await _db.ProductProcesses.Where(x => x.ProductCd == productCd && !x.IsDeleted)
    .ExecuteUpdateAsync(s => s
        .SetProperty(x => x.IsDeleted, true)
        .SetProperty(x => x.Modifier, userName)
        .SetProperty(x => x.ModifyDate, DateTime.Now));
```
> 删除走 **soft-delete**，与 Update 的 `RemoveRange` 物理删不同。

校验：列表前置 `mcTransferFlg=true` 禁删；404；409 `MSG-W10002`。

---

## 复制新建（コピー）— POST /api/products/{cd}/copy

**前端**：列表「コピー」→ `openInWindow('copy')`，或编辑器 `onOpChange` Copy 分支（`ProductMasterView.vue:173-184`）`productApi.copy(cd)` → `loadFromDto`。api `copy`（`product.ts:58-62`）。
**后端**：Controller `Copy`（`:145-158`）；Service `CopyAsync`（`:306-328`）：`GetByCdAsync` 取源（无则 404）；**E10107 上限**：部材 >100 抛 `InvalidOperationException("E10107: 一度にコピーできる部材は 100 件まで…")`（`CopyMemberLimit=100`）；清 PK/RowVersion/状态（`Status=0; WfApprovalFlg=false; McTransferFlg=false`）；部材裁为 1 行亲；`return await CreateAsync(src, userName)`（复用采番）。

---

## 辅助动作

均在 `ProductController.cs`+`ProductService.cs`：
- **採番** `GET /next-seq`（`:38-43` → `NextSequenceAsync` `:334-339`）。
- **御見積書引入** `GET /by-quotation/{no}`（`:46-51` → `GetMembersByQuotationAsync` `:345-384`，从 `QuotationDetails` 转部材一覧，按 `Branch1` 照合既存）。← **主链 ②→③ 的接力点**。
- **見積計算書引入** `GET /by-estimate-calc/{no}`（`:54-61`）。
- **仕掛チェック** `GET /check-wip/{cd}`（`:74-79`，`IWipCheckService`；mc 無 → 恒 Level=0 `NoOpWipCheckService`）。
- **CSV 出力** `GET /export.csv`（`:64-70` → `ExportCsvAsync` `:405-462`，BOM+UTF-8 全量）。

---

## 乐观锁端到端小结
1. 读：`GetByCdAsync` → `entity.RowVersion(byte[])` → `ProductDto.RowVersion`（Base64）→ `store.loadFromDto`。
2. 写：`buildDto` 回送 → PUT/DELETE body。
3. 服务端：`OriginalValue = dto.RowVersion`（Update `:259`、Delete `:286`）。
4. `SaveChangesAsync`：`UPDATE … WHERE RowVersion=@original`，并发覆盖 → `DbUpdateConcurrencyException`。
5. Controller → 409 `{ msgId:"MSG-W10002" }`。
6. 前端 `useProductConflictHandler` → 弹「最新版を取得」→ 重读拉回最新 `rowVersion`。

---

## 涉及文件清单

| # | 层 | 文件 | 关键行 |
|---|---|---|---|
| 1 | FE 路由 | `cp6.web/src/router/index.ts` | `/product`:66、`/product-list`:67、standalone `:175-180` |
| 2 | FE 列表 | `cp6.web/src/views/erp/ProductMasterListView.vue` | loadData:188、onDelete:294、handleMessage:316 |
| 3 | FE 编辑器(5页) | `cp6.web/src/views/erp/ProductMasterView.vue` | onSave:331、runAllValidations:255、onDelete:366、onMounted:415 |
| 4 | FE store | `cp6.web/src/stores/productMaster.ts` | loadFromDto:111、buildDto:135、rowVersion:80 |
| 5 | FE api | `cp6.web/src/api/erp/product.ts` | getList:16、getByCd:29、create:37、update:42、remove:50、copy:58 |
| 6 | FE type | `cp6.web/src/types/erp/productMaster.ts` | ProductDto:224、Material:179、Process:130、Query:262 |
| 7 | FE 冲突处理器 | `cp6.web/src/composables/useProductConflictHandler.ts` | handle:26、409 判定:29、弹窗:36、重读:55 |
| 8 | BE Controller | `CP6.WebApi/Controllers/Erp/ProductController.cs` | GetList:29、Get:82、Create:92、Update:101(409:114)、Delete:126、Copy:145 |
| 9 | BE Service | `CP6.Core/Services/Erp/ProductService.cs` | GetPageList:46、GetByCd:123、Create:167、Update:252、Delete:280、Copy:306、ReplaceChildren:483、SoftDeleteChildren:500 |
| 10 | BE 接口 | `CP6.Core/Services/Erp/IProductService.cs` | 9 契约 |
| 11-15 | BE 实体 | `ProductMaster.cs`/`ProductProcess.cs`/`ProductMaterial.cs`/`ProductLotPrice.cs`/`ProductCoProduct.cs` | 主+4子表 |
| 16 | BE 基类 | `CP6.Entity/BaseBizEntity.cs` | IsDeleted:17、[Timestamp] RowVersion:24 |
| 17 | BE DTO | `CP6.Entity/DTOs/Erp/ProductDto.cs` | ProductDto:22、5 子表 DTO:64-277 |

### 错误码实证表（grep）
| 码 | 含义 | 出处 | 状态 |
|---|---|---|---|
| `MSG-W10002`/`W10002` | 楽観排他冲突(409) | `ProductController.cs:120,140`；i18n 种子 | 真实词条 |
| `E10007` | 登録する部材がありません | `ProductMasterView.vue:258` | **仅前端注释/内联**，无独立 i18n 键 |
| `W20011` | 工程・作業がありません | `ProductMasterView.vue:264` | 仅前端注释 |
| `W20016` | ロット別単価がありません | `ProductMasterView.vue:275` | 仅前端注释 |
| `E10107` | 一度にコピーできる部材は 100 件まで | `ProductService.cs:303,311,314` | 真实 |
| 404 文案 | 製品マスタが未登録です/CD 見つかりません | `ProductController.cs:87`；`ProductService.cs:256` | 真实 |

## 关键发现
1. **更新物理删子表、删除软删子表**——同一功能两种删除策略，别混。
2. **`ProductMaterial` 的 MRP 用量字段（`UsageType/UnitUsage/UsageUnit`）未走 PA050 通道**：`MapToMaterialDto`(`:891`)/`AddMaterials`(`:590`)/DTO 都没映射这三字段（应由 MRP/P1 侧维护）。这是真实缺口。
