# ERP 写端点 × 権限键清单（M-ERP Task 1 真相源）

> 生成于 2026-07-12。本表是 **M-ERP 横切接线波的唯一真相源**：T2（`Sys_MenuAction`/`Sys_RoleAction` 逐租户种子 + 菜单 MenuKey 回填）与 T3（逐端点贴 `[RequirePermission("menu-key","action")]`）、T4（反射 fail-closed 测试 + 收编五条孤儿路由菜单）均以本表为准。
> 依据：`docs/00-横切接线规范.md` 第一章（功能级四粒度）+ 样板 `docs/seeds/wms-permission-keys.md`（格式基准）+ 现有 ERP 菜单种子 `CP6.WebApi/Program.cs` MenuId 200–215（**16 行均缺 MenuKey，且 RoutePath 为裸路径无 `erp/` 前缀**——见 §六 头号命门）+ 逐 Service 实现读证的只读 POST 豁免判定。
> 扫描范围：`CP6.WebApi/Controllers/Erp/` 下 **全部 15 个控制器**。
> **本任务只产出本文档，不改任何控制器/种子/测试/前端代码。**

## 约定

- **资源键 = `{menu-key}:{action}`**，**menu-key 一律连字符小写、绝对禁止下划线**（全仓 100% RequirePermission 用连字符）。本波统一冠 `erp-` 业务域前缀（`erp-order`、`erp-quotation`…）。
- **资源键必须能锚定到一个 `Sys_Menu` 行**（`PermissionAggregator.FillActionKeysAsync = Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → {MenuKey}:{ActionCode}`）。逐键给出锚定菜单 MenuId/RoutePath；无菜单行的键标注「**T4 需补菜单**」。
- **`高危?` 列三值**（沿用 WMS 定义）：
  - `是` = 触及**金额/不可逆/级联取消**（T3 贴点与 T2 审计的**最高优先级**，绝不可与 view/edit 混授）。
  - `状态` = 独立工作流状态流转/文档发行（不直接动钱，但仍**单独成键**，不塞进 edit/view）。
  - `否` = 四基粒度 `view/add/edit/del` 之一。
- **只读 POST 豁免**：纯查询/计算/导出的 POST 归 `view`，表内标 `只读POST→view`，并在 §四逐条附**读 Service 实现证得的**无写副作用依据。GET 端点一律不列。

---

## 一、写端点映射表（POST/PUT/DELETE，共 46 行）

| # | 控制器 | HTTP方法 + 路由 | 方法名 | 建议 menu-key | action | 高危? | 备注 |
|---|---|---|---|---|---|---|---|
| 1 | OrderController | POST `/api/orders/lead-time` | CalcLeadTime | `erp-order` | view | 只读POST→view | 纯计算，营业日逆算，无 DB（详§四） |
| 2 | OrderController | POST `/api/orders/calc-product-category` | CalcProductCategory | `erp-order` | view | 只读POST→view | AsNoTracking 读，无写（§四） |
| 3 | OrderController | POST `/api/orders/calc-materials` | CalcMaterials | `erp-order` | view | 只读POST→view | BOM 展开纯读（§四） |
| 4 | OrderController | POST `/api/orders/report` | ExportReport | `erp-order` | view | 只读POST→view | 受注伝票 PDF/txt 导出，纯读（§四） |
| 5 | OrderController | POST `/api/orders` | Create | `erp-order` | add | 否 | 受注登録。菜单208 受注入力。**注：触发 ERP→MES 前向 Hook 自动展开製造指図**（Program.cs 4.14），仍属正常创建，不提级 |
| 6 | OrderController | PUT `/api/orders/{webOrderNo}` | Update | `erp-order` | edit | 否 | 受注訂正 |
| 7 | OrderController | DELETE `/api/orders/{webOrderNo}` | Delete | `erp-order` | del | 否 | 软删除 |
| 8 | OrderController | PUT `/api/orders/price-correction/batch` | BatchUpdatePrice | `erp-order-price-correction` | correct | **是** | **单价订正**（金额批量改写，已確定受注）。**跨菜单**：归菜单209 単価訂正，非 erp-order。简报点名高危 |
| 9 | OrderController | POST `/api/orders/{webOrderNo}/cancel` | Cancel | `erp-order` | cancel | **是** | **受注取消**（force=true 经 Bridge Hook 级联取消 WO/Outbound，不可逆）。简报点名高危。force=false 为探査模式无写，但端点整体须护 |
| 10 | BackorderController | POST `/api/backorder/{webOrderNo}/{detailNo}/close-remaining` | CloseRemaining | `erp-backorder` | close | 状态 | 关闭残数（欠品履行状态流转）。**T4 需补菜单** |
| 11 | BackorderController | POST `/api/backorder/{webOrderNo}/{detailNo}/split-to-new-order` | SplitToNewOrder | `erp-backorder` | split | 状态 | 残数拆分生成新受注（履行状态流转，写新单）。**T4 需补菜单** |
| 12 | CreditNoteController | POST `/api/credit-note/search` | Search | `erp-credit-note` | view | 只读POST→view | 纯分页查询，CreditNoteService 全类无写（§四）。**T4 需补菜单** |
| 13 | FxRateController | POST `/api/erp/fx-rate` | Create | `erp-fx-rate` | add | 否 | 為替レート master。**T4 需补菜单**。金额-adjacent 见§六注 |
| 14 | FxRateController | PUT `/api/erp/fx-rate/{id}` | Update | `erp-fx-rate` | edit | 否 | |
| 15 | FxRateController | DELETE `/api/erp/fx-rate/{id}` | Delete | `erp-fx-rate` | del | 否 | |
| 16 | EstimateCalcController | POST `/api/estimate-calcs` | Create | `erp-estimate-calc` | add | 否 | 見積計算書登録。菜单201/202 |
| 17 | EstimateCalcController | PUT `/api/estimate-calcs/{no}` | Update | `erp-estimate-calc` | edit | 否 | |
| 18 | EstimateCalcController | DELETE `/api/estimate-calcs/{no}` | Delete | `erp-estimate-calc` | del | 否 | |
| 19 | EstimateCalcController | POST `/api/estimate-calcs/{no}/copy` | Copy | `erp-estimate-calc` | add | 否 | 复制新建，复用 add（§五归并3） |
| 20 | EstimateCalcController | POST `/api/estimate-calcs/calculate` | Calculate | `erp-estimate-calc` | view | 只读POST→view | 纯计算引擎，仅写内存 DTO（§四）。**⚠端点现挂 `[AllowAnonymous]`**（§六注） |
| 21 | QuotationController | POST `/api/quotations` | Create | `erp-quotation` | add | 否 | 御見積書登録。菜单203/204 |
| 22 | QuotationController | PUT `/api/quotations/{no}` | Update | `erp-quotation` | edit | 否 | |
| 23 | QuotationController | DELETE `/api/quotations/{no}` | Delete | `erp-quotation` | del | 否 | |
| 24 | QuotationController | POST `/api/quotations/{no}/copy` | Copy | `erp-quotation` | add | 否 | 复用 add（§五归并3） |
| 25 | QuotationController | POST `/api/quotations/{no}/confirm` | Confirm | `erp-quotation` | confirm | 状态 | 確定登録（状态流转，可逆） |
| 26 | QuotationController | POST `/api/quotations/{no}/cancel-confirm` | CancelConfirm | `erp-quotation` | confirm | 状态 | 確定取消，归并入 confirm（§五归并2） |
| 27 | QuotationController | POST `/api/quotations/{no}/issue` | Issue | `erp-quotation` | issue | 状态 | 発行帳票（更新発行日、返文件名） |
| 28 | ProductController | POST `/api/products` | Create | `erp-product` | add | 否 | 製品マスタ登録。菜单205/206 |
| 29 | ProductController | PUT `/api/products/{cd}` | Update | `erp-product` | edit | 否 | |
| 30 | ProductController | DELETE `/api/products/{cd}` | Delete | `erp-product` | del | 否 | |
| 31 | ProductController | POST `/api/products/{cd}/copy` | Copy | `erp-product` | add | 否 | 复用 add（§五归并3） |
| 32 | BusinessPartnerController | POST `/api/business-partners` | Create | `erp-business-partner` | add | 否 | 取引先登録。菜单211/212 |
| 33 | BusinessPartnerController | PUT `/api/business-partners/{bpCd}` | Update | `erp-business-partner` | edit | 否 | |
| 34 | BusinessPartnerController | DELETE `/api/business-partners/{bpCd}` | Delete | `erp-business-partner` | del | 否 | **控制器已挂 `[Authorize(Roles="1,Admin")]`**（管理者限定）；T3 贴 del 键后为双闸，勿去掉既有 Roles 闸 |
| 35 | FscChecklistController | POST `/api/fsc-checklists/issue` | Issue | `erp-fsc-checklist` | issue | 状态 | チェックシート発行→写 FscChecklist 发行履历（§四证：确为写端点，非豁免）。菜单210 |
| 36 | SheetUnitPriceController | POST `/api/sheet-unit-prices/import` | Import | `erp-sheet-unit-price` | import | 状态 | Excel 批量取込→写单价 master。菜单213。价格-adjacent 见§六注 |
| 37 | SheetUnitPriceController | PUT `/api/sheet-unit-prices/batch-update` | BatchUpdate | `erp-sheet-unit-price` | edit | 否 | 选择行 UPSERT（单价 master 编辑） |
| 38 | PlateMoldController | POST `/api/plate-molds` | Create | `erp-plate-mold` | add | 否 | 版型/木型登録。菜单214/215 |
| 39 | PlateMoldController | PUT `/api/plate-molds/{wdPtnNo}/revise` | Revise | `erp-plate-mold` | edit | 否 | 版本升级（Rev+1），归并 edit（§五归并4、简报#5 裁决） |
| 40 | PlateMoldController | PUT `/api/plate-molds/{wdPtnNo}/{wdRev}` | Update | `erp-plate-mold` | edit | 否 | 就地订正指定 Rev |
| 41 | PlateMoldController | DELETE `/api/plate-molds/{wdPtnNo}/{wdRev}` | Delete | `erp-plate-mold` | del | 否 | |
| 42 | PlateMoldController | POST `/api/plate-molds/label` | Label | `erp-plate-mold` | view | 只读POST→view | ラベル CSV 生成，AsNoTracking 读（§四） |
| 43 | OtdReportController | POST `/api/otd-report/summary` | Summary | `erp-otd-report` | view | 只读POST→view | OTD 报表汇总，OtdReportService 全类无写（§四）。**T4 需补菜单** |
| 44 | OtdReportController | POST `/api/otd-report/export-csv` | ExportCsv | `erp-otd-report` | view | 只读POST→view | OTD CSV 导出，纯读（§四）。**T4 需补菜单** |
| 45 | UnshippedOrderController | POST `/api/orders/unshipped/search` | Search | `erp-order` | view | 只读POST→view | 未出荷残一覧查询，UnshippedOrderService 全类无写（§四）。归 erp-order（受注域子视图） |
| 46 | UnshippedOrderController | POST `/api/orders/unshipped/export-csv` | ExportCsv | `erp-order` | view | 只读POST→view | 未出荷 CSV 导出，纯读（§四）。归 erp-order |

> **GET-only 控制器（无 POST/PUT/DELETE，不在上表）**：
> - `MasterDataController`（`/api/master`，全 GET 下拉/lookup）——纯查询，无写端点。
> - `OrderTraceController`（`/api/order-trace`，仅 GET `{webOrderNo}`）——纯查询，无写端点；但对应前端页 `/erp/order-trace` 为孤儿路由，**T4 需补菜单**（键 `erp-order-trace:view`，见 §二）。

---

## 二、menu-key 汇总清单（去重，共 14 个）

| # | menu-key | 锚定菜单（Program.cs MenuId / RoutePath） | 说明 |
|---|---|---|---|
| 1 | `erp-estimate-calc` | 201 見積計算書 照会 `/estimate-calc-list` + 202 登録 `/estimate-calc` | ✅有菜单行（一域两行：照会+登録），缺 MenuKey→T2 补 |
| 2 | `erp-quotation` | 203 御見積書 一覧 `/quotation-list` + 204 登録 `/quotation` | ✅一域两行 |
| 3 | `erp-product` | 205 製品マスタ 一覧 `/product-list` + 206 登録 `/product` | ✅一域两行 |
| 4 | `erp-order` | 207 受注一覧照会 `/order-list` + 208 受注入力 `/order` | ✅一域两行。含 UnshippedOrder 只读子视图 |
| 5 | `erp-order-price-correction` | 209 単価訂正 `/order-price-correction` | ✅独立菜单行。**同 OrderController 跨菜单**（correct 高危键归此，非 erp-order） |
| 6 | `erp-fsc-checklist` | 210 FSC チェックシート `/fsc-checklist` | ✅ |
| 7 | `erp-business-partner` | 211 取引先 一覧 `/business-partner-list` + 212 登録 `/business-partner` | ✅一域两行 |
| 8 | `erp-sheet-unit-price` | 213 シート単価マスタ `/sheet-unit-price` | ✅ |
| 9 | `erp-plate-mold` | 214 版型/木型 一覧 `/plate-mold-list` + 215 登録 `/plate-mold` | ✅一域两行 |
| 10 | `erp-order-trace` | —— | **T4 需补菜单**（孤儿路由 `/erp/order-trace`）。仅 view（GET-only） |
| 11 | `erp-credit-note` | —— | **T4 需补菜单**（孤儿路由 `/erp/credit-note`）。仅 view（唯一端点为只读 POST 豁免） |
| 12 | `erp-backorder` | —— | **T4 需补菜单**（孤儿路由 `/erp/backorder`）。含 close/split 两状态键 |
| 13 | `erp-otd-report` | —— | **T4 需补菜单**（孤儿路由 `/erp/otd-report`）。仅 view（两端点均只读 POST 豁免） |
| 14 | `erp-fx-rate` | —— | **T4 需补菜单**（孤儿路由 `/erp/fx-rate`）。含 add/edit/del |

> **9 个 menu-key 有对应菜单行**（MenuId 201–215，缺 MenuKey，T2 统一补显式 `erp-*`）；**5 个 menu-key 菜单缺**（order-trace / credit-note / backorder / otd-report / fx-rate）——即简报点名的 T4 五条孤儿路由。

---

## 三、高危动作清单（`是`：金额/不可逆/级联，共 2 个资源键）

> T3 贴 `[RequirePermission]` 与 T2 审计（钱与不可逆优先）的**第一优先级**，**绝不可**与 view/edit 混授。

| 资源键 | 为何高危独立 |
|---|---|
| `erp-order:cancel` | 受注取消：force=true 经 Bridge Hook **级联取消下游 WO / Outbound**，不可逆，简报点名 |
| `erp-order-price-correction:correct` | 単価訂正：对已確定受注**批量改写金额**，简报点名「价格修正」 |

### 3b. 独立状态流转/发行动作键（`状态`，共 6 个，仍单独成键、不塞 edit）

`erp-quotation:confirm`（含 cancel-confirm 归并）· `erp-quotation:issue` · `erp-fsc-checklist:issue` · `erp-sheet-unit-price:import` · `erp-backorder:close` · `erp-backorder:split`

> 非 CRUD 独立动作键合计 = 2（高危）+ 6（状态）= **8 个**。其余端点走 `add/edit/del/view` 四基粒度。

---

## 四、只读 POST 豁免清单（归 view，共 11 个 —— 均逐条读 Service 实现证得无写）

| # | 端点（方法） | 豁免依据（读 Service 实现） |
|---|---|---|
| 1 | POST `/api/orders/lead-time`（OrderService.CalcLeadTimeAsync） | 纯营业日逆算，`Task.FromResult`，**无 `_db` 触碰**（OrderService.cs:702-721） |
| 2 | POST `/api/orders/calc-product-category`（CalcProductCategoryAsync） | 仅 `ProductMasters.AsNoTracking()` 读，返回区分，无 SaveChanges（:1027-1040） |
| 3 | POST `/api/orders/calc-materials`（CalcMaterialsAsync） | 仅 `ProductMaterials.AsNoTracking()` 读并投影 DTO，无写（:1042-1073） |
| 4 | POST `/api/orders/report`（ExportOrderReportPdfAsync） | 仅 `OrderDetails.AsNoTracking()` 读→拼文本 bytes，无写（:1095-1131） |
| 5 | POST `/api/estimate-calcs/calculate`（EstimateCalcService.CalculateAsync） | 计算引擎，仅对 `result.AmountRows/Notes`（内存 DTO List）Add，**无 `_db.*.Add`/无 SaveChanges**（:239-346） |
| 6 | POST `/api/plate-molds/label`（PlateMoldService.IssueLabelCsvAsync） | 仅 `PlateMolds.AsNoTracking()` 读→拼 CSV bytes，无写（PlateMoldService.cs:514-541） |
| 7 | POST `/api/credit-note/search`（CreditNoteService.SearchAsync） | 全类 grep 无 `SaveChanges/Add/Update/Remove/ExecuteUpdate/ExecuteDelete`，纯查询 |
| 8 | POST `/api/otd-report/summary`（OtdReportService.GetSummaryAsync） | OtdReportService 全类无任何写操作，纯汇总读 |
| 9 | POST `/api/otd-report/export-csv`（OtdReportService.ExportCsvAsync） | 同上，纯读导出 |
| 10 | POST `/api/orders/unshipped/search`（UnshippedOrderService.SearchAsync） | UnshippedOrderService 全类无任何写操作，纯分页查询 |
| 11 | POST `/api/orders/unshipped/export-csv`（UnshippedOrderService.ExportCsvAsync） | 同上，纯读导出 |

> **复核结论（防望文生义）**：以下"看似导出/发行"的 POST **确为写端点，不豁免**——
> - `POST /api/fsc-checklists/issue`：`FscChecklistService.IssueAsync` 在 FscChecklists **Add + SaveChangesAsync**（FscChecklistService.cs:156,189），写发行履历 → 键 `erp-fsc-checklist:issue`。
> - `PUT /api/sheet-unit-prices/batch-update` 与 `POST /import`：均写单价 master → 已按上表贴权限。
> - `POST /api/orders/{no}/cancel`（force=true）：级联写 → 高危键。

---

## 五、命名归并判断与疑点（供 T2/T3 复核）

1. **一控制器跨两 menu-key**：`OrderController` 同时承载受注（`erp-order`：Create/Update/Delete/Cancel + 4 只读计算 + 未出荷子视图）与単価訂正（`erp-order-price-correction`：BatchUpdatePrice=correct）两个菜单域。T3 贴点须按端点分别贴不同 menu-key，勿一刀切归 erp-order。（对标 WMS WarehouseController 跨 wms-warehouse/wms-location。）
2. **`erp-quotation:confirm` 归并 Confirm+CancelConfirm**：確定登録与確定取消为同一"確定管理"权限的正/反操作，归一键（对标 WMS `wms-remnant:reserve` 归并 Reserve/Unreserve）。若审计要求"可确定但不可撤销確定"更细授权，T3 可拆 `cancel-confirm`——**当前判定为不拆**。
3. **`add` 复用于同域创建+复制**：`erp-estimate-calc:add`（Create+Copy）、`erp-quotation:add`（Create+Copy）、`erp-product:add`（Create+Copy）——Copy 本质为创建新单，复用 add，不为复制造新键。若需"可新建不可复制"，T3 再拆——**当前不拆**。
4. **PlateMold Revise 归并 edit（简报#5 裁决）**：Revise（Rev+1 版本升级）与 Update（就地订正）在授权层同为"修改此版型 master"权限，且 Revise **不触及库存/金额、无独立业务风险**（仅版本记录），故归 `erp-plate-mold:edit`。**建议**：若后续版本治理需"可订正不可升版"独立管控，T3 可增 `revise` 键——当前判定统一 edit。
5. **`del` 一致语义**：全 ERP 删除均为软删除（IsDeleted），统一 `del`。BusinessPartner.Delete 额外挂 `[Authorize(Roles="1,Admin")]`，T3 贴 `erp-business-partner:del` 后与 Roles 闸并存（双闸更严，保留）。
6. **`status/confirm/issue` 档不提级高危的理由**：Quotation confirm/issue、Fsc issue、SheetPrice import 均**不直接动金额交易/不可逆**（confirm 可 cancel-confirm 回退），归 `状态`。仅受注 cancel（级联不可逆）与 単価訂正 correct（改已確定金额）提级 `是`。
7. **Backorder split-to-new-order 未提级高危**：虽写新受注，但属履行状态流转（对标 WMS `from-order` 生成出库单归 add/否），归 `状态:split`。若审计视"自动生成商业单据"为高危，T2 可提级——**待 T2 审计拍板**。

---

## 六、命门与遗留（T2/T4 硬前置，头号风险）

1. **【头号命门】ERP 现有菜单 RoutePath 裸路径、无 `erp-` 前缀 → 回填键与本表键不匹配**：
   Program.cs 对无 MenuKey 菜单执行 `MenuKey = RoutePath.Trim('/').Replace('/','-')`（:882-886）。ERP 菜单 201–215 的 RoutePath 是 `/order`、`/product`、`/estimate-calc`、`/order-price-correction`… **无 `erp/` 段**，回填后得 `order`、`product`… **无 `erp-` 前缀**，与本表 `erp-order`… **对不上**，PermissionAggregator join 不到键 → **全 ERP fail-closed 403**。
   → **T2 必须在两处菜单种子块（初始 :684-699 与幂等启动 :935-1031）对 201–215 显式赋 `MenuKey="erp-*"`，且置于回填块（:882）之前**（对标 WMS T2「30 权限键锚定行显式 MenuKey，须置于回填块之前」）。**这是 T2 不做则整波失配的硬前置。**
2. **一域两菜单行的 MenuKey 分配**：estimate-calc/quotation/product/order/business-partner/plate-mold 各有「一覧+登録」两菜单行。T2 需决定 MenuKey 落在哪行——**建议两行同赋同一 `erp-*` MenuKey**（一覧页承 view、登録页承 add/edit/del），并确认 `Sys_Menu.MenuKey` 无唯一约束阻挡同键多行（WMS inbound-order 404/405 两行共 `wms-inbound-order` 已有先例，应可行；T2 落库前复验）。
3. **`EstimateCalcController.Calculate` 现挂 `[AllowAnonymous]`**（:136）：试算引擎当前对匿名开放。本表给 `erp-estimate-calc:view`，但 T3 贴 RequirePermission 前须先决定是否撤 `[AllowAnonymous]`——**若保留匿名则该端点不受权限键管辖**（反射测试须把它列入豁免白名单，否则 fail-closed 断言会误判）。**待拍板。**
4. **credit-note 高危写端点在 ERP 不存在**：简报点名「信用単(credit note)操作」为高危，但 `CreditNoteController` 当前**只有 POST `/search`（只读）**，无任何签发/冲销写端点。信用単实际签发大概率在 FIN 模块。→ ERP 侧 credit-note 仅 `view`；高危 credit-note 操作键应在 FIN 波盘点，本波无对应端点可贴。**记为盘点差异，供上层裁处。**
5. **price/rate master 的金额-adjacent 边界**：`erp-fx-rate:add/edit/del`（为替レート）与 `erp-sheet-unit-price:import/edit`（单价 master）会影响金额换算/估价，但属**主数据 CRUD**（对标 WMS master 编辑归否），本表未提级高危。若 T2 审计要求"影响定价的主数据变更"入高危审计范围，可单独提级——**待 T2 审计拍板。**

---

## 七、计数收口

- **扫描控制器**：15（Backorder / BusinessPartner / CreditNote / EstimateCalc / FscChecklist / FxRate / MasterData / Order / OrderTrace / OtdReport / PlateMold / Product / Quotation / SheetUnitPrice / UnshippedOrder）。
- **含写端点控制器**：13（除 MasterData、OrderTrace 两个 GET-only）。
- **POST/PUT/DELETE 端点行总数**：**46**（= §一表行数，精确吻合）。
  - 其中**只读 POST 豁免（→view）**：**11**。
  - **真·写端点**：**35**。
- **menu-key（去重）**：**14**（9 有菜单 / 5 孤儿待 T4）。
- **高危键（是）**：**2**（`erp-order:cancel`、`erp-order-price-correction:correct`）。
- **状态键**：**6**。

### 逐控制器双向核对（控制器→表 / 表→控制器，零缺漏零 GET 误列）

| 控制器 | POST/PUT/DELETE 端点数 | 表内 # |
|---|---|---|
| OrderController | 9（Create/Update/Delete/BatchPrice/Cancel + 4 只读POST；其余 16 个均 GET 不列） | 1–9 |
| BackorderController | 2（GET queue 不列） | 10–11 |
| CreditNoteController | 1（唯一端点，只读POST） | 12 |
| FxRateController | 3（GET List/Resolve 不列） | 13–15 |
| EstimateCalcController | 5（Create/Update/Delete/Copy/Calculate；GET List/Get 不列） | 16–20 |
| QuotationController | 7（Create/Update/Delete/Copy/Confirm/CancelConfirm/Issue；GET List/Get/calcs 不列） | 21–27 |
| ProductController | 4（Create/Update/Delete/Copy；GET 6 个不列） | 28–31 |
| BusinessPartnerController | 3（Create/Update/Delete；GET List/Get/check/export 不列） | 32–34 |
| FscChecklistController | 1（Issue；GET list/formats/download 不列） | 35 |
| SheetUnitPriceController | 2（Import/BatchUpdate；GET list 不列） | 36–37 |
| PlateMoldController | 5（Create/Revise/Update/Delete/Label；GET 7 个不列） | 38–42 |
| OtdReportController | 2（Summary/ExportCsv，均只读POST） | 43–44 |
| UnshippedOrderController | 2（Search/ExportCsv，均只读POST） | 45–46 |
| MasterDataController | 0（全 GET） | —— |
| OrderTraceController | 0（全 GET） | —— |
| **合计** | **46** | **46 ✅** |
