# 01 · 取引先マスタ BusinessPartner（PA110/120）

> 先读 [`README.md` §0 公共约定](README.md)：`http.ts`、实体基类链、乐观锁全链路、采番、错误码体系——本文不再重复。
> 全部 `文件:行号` 与代码片段均为 2026-06-22 实测；逐字引用。

## 0. 架构定位（重要：本功能是"反范例"）

- `BusinessPartnerService` **直接实现 `IBusinessPartnerService`，不继承 `ServiceBase<T>`/`RepositoryBase<T>`**（`BusinessPartnerService.cs:17`：`public class BusinessPartnerService : IBusinessPartnerService`），**直接注入 `CP6Context _db`**（`:19,23`），用原生 `_db.BusinessPartners.AsNoTracking()...`。
- 因此**没有任何 `override`**——它根本没有可重写的基类。泛型基类的 `GetByIdAsync(Guid id)`/`DeleteAsync(Guid[] ids)` 按 **Guid 主键**设计；而本实体业务键是 **`BpCd`（字符串）**，所以绕开了泛型基类，自定义了一套以 `BpCd` 为键的方法。
- 它展示的是更贴近真实复杂主数据的套路：**业务键 CRUD + 直注 DbContext + 手工 DTO 映射(`ToDto`/`FromDto`/`ApplyDto`) + 乐观锁 + 逻辑删 + 多租户自动盖章**。
- DI：`Program.cs:266` `AddScoped<IBusinessPartnerService, BusinessPartnerService>();`
- Controller：`[Route("api/business-partners")] [Authorize]`（`BusinessPartnerController.cs:15`），7 端点。

---

## 列表查询 — GET /api/business-partners/list

**页面入口**：`/business-partner-list`（`router/index.ts:77`）→ `views/erp/BusinessPartnerListView.vue`（列表页用页内 `reactive` query，**不用 store**）。

**前端**
- view `search()`（`BusinessPartnerListView.vue:138-158`）：先校验「属性 FLG / 状态 至少各选一」（`E10030`，前端拦），再调 `bpApi.search(query)`，0 件提示 `E10008`。排序 `onSortChange` 把 `ascending/descending` 归一化为 `asc/desc`。
- api（`api/erp/businessPartner.ts:46-51`）：
```ts
search(query: BpQueryDto) {
  return http.get<any, ApiResult<{ rows: BpListItemDto[]; total: number }>>(
    '/business-partners/list',
    { params: query, paramsSerializer: { indexes: null } },
  )
},
```
  `paramsSerializer:{indexes:null}` → 布尔数组/重复键扁平序列化。
- type：`BpQueryDto`（`types/erp/businessPartner.ts:204-250`，11 个 `include*` FLG + 文本条件 + `bpClass01~10` + 分页排序）、`BpListItemDto`（`:252-286`）。

**后端**
- Controller（`BusinessPartnerController.cs:106-118`）`Search([FromQuery] BpQueryDto query)` → `(rows,total)=SearchAsync`，`InvalidOperationException→400`。
- Service `SearchAsync`（`BusinessPartnerService.cs:262-360`）核心步骤：
  1. `AsNoTracking().Where(x => !x.IsDeleted)`（`:264`）软删过滤。
  2. 11 个属性 FLG **OR 拼接**（`:273-284`）；一个都没选直接返回空集（`:271`）。
  3. 状态过滤 0=事前登録/1=本登録（`:287-291`）；登録日 FROM/TO（TO 取 `< 次日0点`）；`BpCd/BpName/Ein/...` 模糊，`Addr` 对 Addr1~4 做 OR 模糊（`:301-305`），`AreaCd/SalesStaffCd/BpClass01~10` 精确等值。
  4. `CountAsync`（`:322`），超 `MaxRows` 抛 `E10013`（`:323-324`）。
  5. **排序走白名单**：`QuerySort.Apply(iq, q.SortField, q.SortOrder, BpSortMap, s => s.OrderBy(x => x.BpCd))`（`:326`，白名单 `BpSortMap` `:248-260`，不在白名单回退 `BpCd` 升序——防注入）。
  6. `Skip/Take` 分页 + `.Select` 投影 `BpListItemDto`，回填 `RowNo`。

**校验与错误码**：FLG/状态"至少选一"前端拦（`E10030`）；后端 `E10013`（件数上限）。
**数据流**：`search()` → `bpApi.search` → `GET /list` → `SearchAsync`(软删→FLG OR→条件→Count→白名单排序→分页投影) → `{rows,total}`。

---

## CSV 导出 — GET /api/business-partners/export-csv

**前端**：`exportCsv()`（`BusinessPartnerListView.vue:178-191`）拿 Blob → `URL.createObjectURL` → `<a download>`，文件名 `business-partners_YYYY-MM-DD.csv`。api `exportCsv`（`businessPartner.ts:54-60`，`responseType:'blob'`）。
**后端**：Controller `ExportCsv`（`:121-127`）`File(bytes,"text/csv; charset=utf-8",...)`；Service `ExportListCsvAsync`（`:362-407`）复用 `SearchAsync`（`PageSize=int.MaxValue` 取全量），CSV 转义 `E()`（含逗号/引号/换行才加引号），前置 **UTF-8 BOM**（防 Excel 乱码），FLG 用 `○`/空。

---

## 单条加载 / 参照 — GET /api/business-partners/{bpCd}

**页面入口**：`/business-partner`（`router/index.ts:76`，另有独立窗口 `/business-partner/window` `:188-192`）→ `BusinessPartnerView.vue`（9 个动态 Tab）。
**前端**：`onLoad()`（`BusinessPartnerView.vue:127-151`）→ `bpApi.getByCd(bpCd)` → `store.loadFromDto`。`onMounted`（`:154-165`）读 `route.query.bpCd/mode` 自动加载。
- api（`businessPartner.ts:9-14`）`getByCd(bpCd, includeDeleted=false)`。
- store `loadFromDto(dto)`（`stores/businessPartner.ts:82-86`）`bp.value={...emptyBp(),...dto}` 并深拷贝存 `original`（FLG 变更守卫用）。
**后端**：Controller `Get`（`:28-35`）未命中返 **HTTP 200 但 code=404**（`E10008`）；Service `GetByCdAsync`（`:29-35`）`AsNoTracking` + `ToDto`（逐字段手工映射 `:421-503`），DTO 末尾带 `byte[]? RowVersion`（`BusinessPartnerDto.cs:210`）。
> 附属：重复检查 `GET /check-exists/{bpCd}`（Controller `:38-43` → `ExistsAsync` `:37-38`）。

---

## 新建（事前登録 / 本登録）— POST /api/business-partners?preRegister=

**前端**：`onSave()`（`BusinessPartnerView.vue:167-198`）前端校验（`bpName/baseCd` 必填、`store.hasAnyFlg`）→ `bpApi.create(store.bp, store.isPreReg)`（第二参 `preRegister` 直接取"是否事前登録"）→ 成功 `store.loadFromDto` 并切 Edit。
- api（`businessPartner.ts:24-28`）`create(data, preRegister=false)` → `POST '/business-partners',{params:{preRegister}}`。
- store `hasAnyFlg`（`:70-73`）9 FLG 任一为真；`emptyBp()`（`:8-45`）含共通初期值（`purchaseTaxCd:'P010'`、`supplierCalendarCd:'CAL01'`…）。

**后端**
- Controller（`BusinessPartnerController.cs:46-59`）`Create([FromBody] BusinessPartnerDto dto, [FromQuery] bool preRegister=false)` → `CreateAsync` → 回查 `GetByCdAsync` 返回带 RowVersion 的新 DTO；`InvalidOperationException→400`。`UserName` 取 `User.FindFirstValue(ClaimTypes.Name)`。
- Service `CreateAsync`（`BusinessPartnerService.cs:40-63`）：
  1. `BpCd` 必填（`:42-43` `E10022`）。
  2. `ExistsAsync` 查重（`:44-45` `E10035`）。
  3. `Validate(dto, isEdit:false, before:null, errors)` 跑 **21 条校验**，有错合并抛 `InvalidOperationException`（`:47-49`）。
  4. `dto.Status = preRegister ? 0 : 1`（`:52`）。
  5. `FromDto(dto)`→`ApplyDto` 逐字段拷贝，手填审计字段，强制 `GifuInterfaceFlg=false`、`McTransferFlg=false`（`:58-59`）。
  6. `Add` + `SaveChangesAsync`（`:60-61`）——`TenantId` 由 `CP6Context.SaveChanges` 自动盖章。
- 实体 `BusinessPartner`（`DomainModels/Erp/BusinessPartner.cs`）：`[Table("T_WebBusinessPartner")]`（`:14`），`: BaseBizEntity`（`:15`），业务键 `BpCd`（`:18-19`，非 PK），9 属性 FLG（`:66-74`），13 种"变更不可"FLG（`:77-83`）。

**校验与错误码（grep 实测 `BusinessPartnerService.cs`）**：
| 码 | 含义 | 行 |
|---|---|---|
| `E10022` | 必填类（取引先名/拠点CD + 各 FLG=ON 的关联必填，21 条主体） | `:113,114,125-127,132,136,140,145-157,161,167,172,177,181,185` |
| `E10030` | 9 个属性 FLG 一个都没选 | `:120` |
| `E10031` | 郵便番号/TEL/FAX 正则不符（`ZipPattern`/`TelPattern` `:20-21`） | `:190,194,196` |
| `E10032` | 締日不在 1〜31 或 99 | `:203` |
| `E10035` | 取引先 CD 已存在 | `CreateAsync:45` |
| `E10013` | 检索件数超 `MaxRows` | `SearchAsync:324` |
| `E10008` | 无结果（Controller） | `:33` |
| `E10034` | 乐观锁冲突（Controller） | `:73,97` |
> `MSG-018` 只在注释里（`:15,74,211`）作为规格编号出现，**FLG 变更不可实际抛 `E10033`**（见下）。grep 时勿把注释当真实码。

**数据流**：`onSave`(create) → `create` → `POST ?preRegister=` → `CreateAsync`(必填→查重→21校验→Status→FromDto→审计→Add→SaveChanges,TenantId 自动盖) → 回查 → `store.loadFromDto` + Edit。

---

## 訂正（编辑）— PUT /api/business-partners/{bpCd}

**前端**：`onSave()` edit 分支（`BusinessPartnerView.vue:192-217`）`bpApi.update(bpCd, store.bp)`；`catch` 内对 **409** 单独处理（弹 `ElMessageBox.confirm` 问是否重取，因 `http.ts` 对 409 静默）。订正前 `store.flgChangedOnEdit()`（store `:98-121`，比对 `original` 与 13 FLG，仅 UI 警告）。
- api（`businessPartner.ts:31-35`）`update(bpCd, data)` → `PUT '/business-partners/{bpCd}'`。

**后端**
- Controller（`:62-83`）`Update`，catch：`DbUpdateConcurrencyException→409 E10034(msgId)`、`InvalidOperationException→400`、`KeyNotFoundException→404`。
- Service `UpdateAsync`（`:65-90`）：
  1. 取实体（**带跟踪**）`FirstOrDefaultAsync(BpCd==.. && !IsDeleted)`，无则 `KeyNotFoundException`（`:67-69`）。
  2. 乐观锁：DTO 的 `RowVersion` 写进 EF `OriginalValue`（`:71-72`）。
  3. **FLG 变更不可检查** `CheckFlgChange(beforeDto, dto)`，不过抛 `E10033`（`:75-78`；`CheckFlgChange` 在 `:214-241` 比对 13 FLG）。
  4. 21 条 `Validate(dto, isEdit:true, before, errors)`（`:81-83`）。
  5. `ApplyDto` 回写所有字段，更新 `Modifier/ModifyDate`，`McTransferFlg=false`（变更需重新转 mc）。
  6. `SaveChangesAsync`（并发校验）。

**校验与错误码**：`E10033`（FLG 变更不可 `:78`）、21 条 Validate、`E10034`/409（乐观锁 `:73`）、404（不存在）。
**数据流**：`onSave`(edit) → `update` → `PUT /{bpCd}` → `UpdateAsync`(取跟踪→盖 RowVersion 原值→FLG 守卫→21校验→ApplyDto→SaveChanges 并发) → 成功回查；冲突 409 → 前端弹框问是否重取。

---

## 删除（管理者限定，逻辑删除）— DELETE /api/business-partners/{bpCd}

**前端**：`onDelete()`（`BusinessPartnerView.vue:219-237`）`ElMessageBox.confirm` 二确认 → `bpApi.remove(bpCd, rowVersion)` → 成功 `store.reset()`。删除按钮 `:disabled="!hasLoaded || !store.isAdmin"`（`store.isAdmin` 由 localStorage `user.roleId===1` 推断 `:91-96`）。
- api（`businessPartner.ts:38-43`）`remove(bpCd, rowVersion?)` → `DELETE`，`rowVersion` 放 **body** `{ data:{ rowVersion } }`。

**后端**
- Controller（`:86-103`）`[Authorize(Roles = "1,Admin")]`（`:87`，**唯一带角色限制的端点**），`Delete(string bpCd, [FromBody] DeleteRequest? req)`（`DeleteRequest` `:130-133`）。catch 409/404。
- Service `DeleteAsync`（`:92-104`）：**逻辑删除**——取跟踪实体→盖 RowVersion 原值→`IsDeleted=true; Status=9`（`:99-100`）→更新审计→`SaveChangesAsync`。不物理删。

**校验与错误码**：HTTP 403（非管理者，框架拦）、`E10034`/409（乐观锁 `:97`）、404。
**数据流**：确认框 → `remove` → `DELETE /{bpCd}`(body rowVersion) → 角色校验 → `DeleteAsync`(IsDeleted=true/Status=9/SaveChanges 并发) → `store.reset()`。

---

## 涉及文件清单

| 路径 | 作用 |
|---|---|
| `cp6.web/src/views/erp/BusinessPartnerListView.vue` | PA120 列表/检索/CSV（页内 query，不用 store） |
| `cp6.web/src/views/erp/BusinessPartnerView.vue` | PA110 单条 加载/新建/订正/删除（9 动态 Tab） |
| `cp6.web/src/views/erp/bp/*Tab.vue` | 各属性 Tab 子组件，`v-model="store.bp.*"` |
| `cp6.web/src/api/erp/businessPartner.ts` | `bpApi`：getByCd/checkExists/create/update/remove/search/exportCsv |
| `cp6.web/src/types/erp/businessPartner.ts` | `BpOperationType` + `BusinessPartnerDto`/`BpQueryDto`/`BpListItemDto` |
| `cp6.web/src/stores/businessPartner.ts` | store（仅编辑页用）：操作种别状态机 + FLG 联动 + dirty/original 守卫 |
| `CP6.WebApi/Controllers/Erp/BusinessPartnerController.cs` | `[Route("api/business-partners")][Authorize]`，7 端点 + `DeleteRequest` |
| `CP6.Core/Services/Erp/IBusinessPartnerService.cs` / `BusinessPartnerService.cs` | 服务接口/实现（**不继承 ServiceBase，直注 CP6Context**）；21 校验/FLG 守卫/Search/CSV/手工映射 |
| `CP6.Core/Services/Common/QuerySort.cs` | 列表服务端动态排序（白名单防注入） |
| `CP6.Entity/DomainModels/Erp/BusinessPartner.cs` | 实体 `T_WebBusinessPartner`，`: BaseBizEntity`，170+ 字段 |
| `CP6.Entity/DTOs/Erp/BusinessPartnerDto.cs` | `BusinessPartnerDto`/`BpQueryDto`/`BpListItemDto` |

## 给初学者两条核对提醒
1. **本功能刻意不用泛型基类**（业务键 `BpCd` 字符串 ≠ Guid）。要找"真正用 `ServiceBase<T>` 的范例"，得另找别的简单 master，别拿它当泛型模板。
2. `MSG-018` 不是抛出的错误码，只是注释里的规格编号；FLG 变更不可实际抛 `E10033`。
