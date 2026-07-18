# Task 3 报告：ERP v-permission 铺设

**STATUS: COMPLETE** — 39 按钮 × 16 视图，键与后端贴点（`docs/seeds/erp-permission-keys.md` §一 35 真·写端点）逐字对齐。

## 真相源与规程
- 键表：`docs/seeds/erp-permission-keys.md`（46 写端点 = 35 贴键 + 11 只读 POST 豁免；14 menu-key；action 词汇 = add/edit/del/cancel/correct/confirm/issue/import/close/split）。
- oracle：`CP6.Tests/ErpPermissionAttributeTests.cs`（ActionVocabulary + ReadOnlyPostExemptions 为地面真相）。
- 样式样板：commit 15823c38（OA/WF），WMS `StocktakeView.vue:86`。
- 只加 `v-permission` template 字面量；既有 `v-if` 保留并列；零脚本/样式/i18n/后端改动。

## 映射清单（视图文件 → 元素 → 键）

| # | 视图 | 元素（@click 处理器） | 键 |
|---|---|---|---|
| 1 | EstimateCalcListView.vue | onNew（新規/toolbar） | `erp-estimate-calc:add` |
|  |  | onEdit（行编集） | `erp-estimate-calc:edit` |
|  |  | onCopy（行複製→copy=add） | `erp-estimate-calc:add` |
|  |  | onDelete（行削除） | `erp-estimate-calc:del` |
| 2 | EstimateCalcView.vue | onSave（保存，btn.save）※多端点 | `erp-estimate-calc:add` |
|  |  | onDelete（btn.del） | `erp-estimate-calc:del` |
| 3 | QuotationListView.vue | onNew | `erp-quotation:add` |
|  |  | onEdit | `erp-quotation:edit` |
|  |  | onCopy（→add） | `erp-quotation:add` |
|  |  | onIssue（発行） | `erp-quotation:issue` |
|  |  | onDelete | `erp-quotation:del` |
| 4 | QuotationView.vue | onSave（保存）※多端点 | `erp-quotation:add` |
|  |  | onConfirm（確定登録） | `erp-quotation:confirm` |
|  |  | onCancelConfirm（確定取消，§五归并2） | `erp-quotation:confirm` |
|  |  | onIssue（発行） | `erp-quotation:issue` |
|  |  | onDelete（削除） | `erp-quotation:del` |
| 5 | ProductMasterListView.vue | onNew | `erp-product:add` |
|  |  | onEdit | `erp-product:edit` |
|  |  | onCopy（→add） | `erp-product:add` |
|  |  | onDelete | `erp-product:del` |
| 6 | ProductMasterView.vue | onSave（保存，canSave）※多端点 | `erp-product:add` |
|  |  | onDelete（isDelete） | `erp-product:del` |
| 7 | OrderListView.vue | openCancelDialog（取消入口，高危） | `erp-order:cancel` |
| 8 | OrderEntryView.vue | onSave（保存，canSave）※多端点 | `erp-order:add` |
|  |  | onDelete（isDelete） | `erp-order:del` |
| 9 | OrderPriceCorrectionView.vue | onSubmit（一括更新，单价订正，高危） | `erp-order-price-correction:correct` |
| 10 | FscChecklistView.vue | onIssue（発行） | `erp-fsc-checklist:issue` |
| 11 | BusinessPartnerListView.vue | goEdit（行选中→編集入口） | `erp-business-partner:edit` |
| 12 | BusinessPartnerView.vue | onSave（登録，canEdit）※多端点 | `erp-business-partner:add` |
|  |  | onDelete（isDelete） | `erp-business-partner:del` |
| 13 | SheetUnitPriceView.vue | selectExcel（→onFileChange 触发 importExcel） | `erp-sheet-unit-price:import` |
|  |  | onUpdate（一括更新=batchUpdate） | `erp-sheet-unit-price:edit` |
| 14 | PlateMoldView.vue | onSave/isDelete 分支（削除） | `erp-plate-mold:del` |
|  |  | onSave/canEdit 分支（登録，Create/Revise/Update）※多端点 | `erp-plate-mold:add` |
| 15 | BackorderListView.vue | openAction('close')（关闭残数入口） | `erp-backorder:close` |
|  |  | openAction('split')（拆分新受注入口） | `erp-backorder:split` |
| 16 | FxRateView.vue | openCreate（新建入口） | `erp-fx-rate:add` |
|  |  | openEdit（行编集入口） | `erp-fx-rate:edit` |
|  |  | remove（行删除） | `erp-fx-rate:del` |

**合计 = 39 directives / 16 views。**

### 多端点按钮（规程规则 4，取主动作键 = add）
以下「保存/登録」按钮由操作种别（New/Copy→Create=add，Edit→Update=edit；PlateMold 另含 Revise→§五归并 edit）动态派发单一端点，统按主动作 **add**（登録为该向导页首要语义）贴键，并注明：
- EstimateCalcView.onSave、QuotationView.onSave、ProductMasterView.onSave、OrderEntryView.onSave、BusinessPartnerView.onSave、PlateMoldView.onSave(canEdit 分支)。
- v-permission 为 UX-only fail-open、admin 不受影响；仅 edit-无-add 或 add-无-edit 的非常规授权组合下会出现单模式按钮误隐（可接受，已注明）。

## 豁免小节（不贴指令的按钮/视图及理由）

**整页无 ERP 写端点（不贴任何指令）：**
- **CreditNoteListView.vue** — 唯一动作 goOrder（导航）；写端点仅 POST `/search`（只读 POST 豁免 §四#7）。
- **OrderTraceView.vue** — GET-only 控制器；search/reload 为读、el-switch=groupByCorrelation 纯客户端分组切换。
- **OtdReportView.vue** — Summary/ExportCsv 均只读 POST 豁免（§四#8/#9）；loadSummary/search/exportCsv/resetQuery 皆读。

**页内个别按钮豁免：**
- OrderCancelDialog.vue（onProbe/onForceConfirm）— **入口已守**：由 OrderListView.openCancelDialog（已贴 `erp-order:cancel`）唯一启动的对话框确认，按规则 3 不重复贴。
- BackorderListView 对话框 submitAction — 同上，由 openAction close/split 两入口守，不重复贴。
- PlateMoldListView.vue.onIssueLabel / PlateMoldView.vue.onIssueLabel — `Label` 为只读 POST 豁免（§四#6，AsNoTracking 生成 CSV）。
- PlateMoldListView.vue.onPick（selectReturn）— picker 模式向父流程返回选择，无 ERP 写端点，无对应键。
- PlateMoldView.vue.onPurchaseOrder — 発注（采购单）非 ERP 域端点（属 PUR/导航），无对应 ERP 键。
- PlateMoldView.vue.onLoadByEstimate（sales.btn.import 标签）— 实为 getByEstimateCalc **读取**载入表单，非写端点。
- 各列表 onView/goView、CSV/Export、search/reset/refresh/load/reload、向导 next/prev/clear、QuotationView addDetailRow/recalcTotalAmount/removeDetailRow（纯本地数组）、Step* 子组件（无 mutating API 调用，仅 ElMessageBox.confirm 清空确认）— 纯读/本地/导航，均不贴。

## 验证输出
- `npx vue-tsc --noEmit` → EXIT 0（零类型错）。
- `npx vitest run` → **71 files / 481 tests passed**（基线全绿，EXIT 0）。
- `npm run build` → built in 7.88s，EXIT 0（仅既有 chunk>500kB 提示，与本改无关）。

## 自审
- diff 仅 views/erp 下 16 个 .vue？ 是（`git diff --name-only` = 16 erp .vue；其余 .superpowers md 为 pre-existing CRLF 警告，未 stage）。
- 每个键逐字在真相源？ 是（27 唯一 (键) 全部落在 §一表 + ActionVocabulary）。
- 无脚本/样式 hunk？ 是（diff grep 确认仅 v-permission template 增行，零 `<script>`/`<style>`/import/function 改动）。

## 关注点 / concerns
1. **多端点保存按钮键选择**（见上）：6 处 create-or-update 保存按钮统贴 add。若审计要求「可编辑不可新建」独立 UX 隐显，需拆键——但后端 fail-closed 已按端点各自 [RequirePermission]（add/edit 分别），前端仅 UX，风险可控。
2. **PlateMoldView.onSave 三态**（Create/Revise/Update 共一按钮）：Revise 后端归并 edit（§五归并4），前端主动作按 add 贴；与 §五裁决一致。
3. 前端 `v-permission` 指令为全局注册（沿用 OA/WF 波已验证机制），本任务未新增/改动指令实现。
