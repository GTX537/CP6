# WMS 迁移批次4 报告

分支 `feat/ui-migrate-wms`。样板 = `OutboundOrderListView.vue`。规则见 `wms-batch-guide.md`。
本批 5 页：WcsTask / StockTake / IotMonitor / ReportCenter / Carrier。

## 形态分类

| 页面 | 形态 | 处置 |
|---|---|---|
| WcsTaskView | 查询列表页 | CpPageShell + CpListPage + CpFilterBar + 3×CpFormDialog（新建/派発/失败） |
| CarrierView | 查询列表页 | CpPageShell + CpListPage + CpFilterBar + 3×CpFormDialog（新建/イベント追加/失败）+ 只读 timeline el-dialog |
| StockTakeView | 棚卸明細/编辑特殊页 | token 化：el-tag→CpTag+tone、内联 #aaa/el-var→--cp-* token；保留 el-descriptions + 可编辑 el-table + el-affix |
| IotMonitorView | 监控仪表盘特殊页 | token 化 + CpTag/CpEmpty 替换；保留 30s 轮询/アラート/行クリック履歴；新建/投入迁 CpFormDialog |
| ReportCenterView | 帳票中心特殊页 | token 化 + CpTag/CpEmpty；保留動的表单/5 结果表/CSV |

## 迁移盘点（一项不许丢）

### WcsTaskView（查询列表页）
- **API**：`wcsApi.search/create/dispatch/start/complete/fail`——全保留。
- **搜索字段**：taskNo(text)/taskType(select)/deviceCd(text)/status(select) → CpFilterBar 4 项。
- **列(13)**：taskNo(mono)/状態(tag+map,statusTone)/種別(纯 map,无 tag)/優先度(col slot：急 danger·↑ warn·—)/装置/移動元(col slot 复合)/移動先(col slot 复合)/製品/数量(col slot formatQty,右)/関連/作成(datetime 原样)/完了(datetime 原样)/操作(col slot,fixed right)。
- **行操作**：status=0 派発 / =1 開始 / =2 完了 / =1|2 失敗——条件按钮全保留；start/complete 直接调用后 `listRef.reload()`。
- **头部**：新規 → openCreate。
- **弹窗**：新建(13 字段 grid,taskType required)/派発(device required)/失败(error required) 迁 CpFormDialog，`@saved=reload`。
- **i18n**：全 t() 词条保留；filter-labels 接 wms.common.search/clear；title=wms.wcs.title。

### CarrierView（查询列表页）
- **API**：`carrierApi.search/create/pickUp/inTransit/delivered/fail/addEvent/get`——全保留。
- **搜索字段**：shipmentNo/trackingNo(text)/carrierCd/status(select) → 4 项。carrierMap const→computed（i18n 反应式，行为增强）。
- **列(11)**：shipmentNo(mono)/状態(tag+map)/業者(纯 map)/追跡番号/梱包/顧客/住所(minWidth180+tooltip)/重量(col slot formatQty3,右)/集荷日/配送日/操作(fixed right)。
- **行操作**：常驻「詳細」+ status 条件 pickup/transit/delivered/fail——全保留。
- **弹窗**：新建(10 字段 grid,packageNo/carrierCd required)/イベント追加/失败(reason required) 迁 CpFormDialog；詳細(timeline 只读)保留 el-dialog（内联 #606266/#909399→--cp-ink/--cp-muted）。
- **缺口 #16**：原 `@row-click` 整行进详情 → CpListPage 无透传，代偿为操作列「詳細」按钮（详情/timeline 功能全保，仅失整行点击 affordance）。

### StockTakeView（特殊页·token 化）
- 保留全部逻辑（load/startCount/saveCounts/submit/approve/cancel、recalcDiff、filteredDetails、diffCount、canXxx 权限计算、el-descriptions 8 项、可编辑明細 el-table、el-affix action-bar 6 按钮）。
- 视觉：状態/種別/承認/ADJ el-tag → CpTag + tone（statusTone/approvalTone）；内联 `#aaa` → `.dash{color:var(--cp-faint)}`；`.plus/.minus` el-var→--cp-*；action-bar `--el-*` → `--cp-card/--cp-line-soft`。

### IotMonitorView（特殊页·token 化）
- 保留全部逻辑（30s setInterval 轮询、reload、simulate、isAlert、alerts 面板 el-alert、sensor el-table、行クリック→履歴、onTypeChange 默认值）。
- 视觉：lastValue/履歴⚠ el-tag → CpTag+tone；`el-empty`(无アラート) → CpEmpty；内联 `#909399` → `.sub{color:var(--cp-muted)}`；新建/投入 el-dialog → CpFormDialog（`editing` ref→reactive createForm/postForm）；履歴弹窗(只读)保留 el-dialog。

### ReportCenterView（特殊页·token 化）
- 保留全部逻辑（5 report type 動的字段切换、run 5 分支、downloadCsv 5 分支、fmtQty/fmtMoney、maxLimit el-alert）。
- 视觉：件数 el-tag / ABC rank / 滞留 idleDays el-tag → CpTag+tone；`el-empty` → CpEmpty。无内联硬编码色，style 仅布局。

## 验证证据

- **type-check**：`npm run type-check` 0 error（修 1 处 noUncheckedIndexedAccess：IotMonitor onTypeChange defaults 回退）。
- **test**：`npm run test` **304 passed (46 files)**，与 baseline 持平。
- **真栈走查**（dev 5173，admin 已登录，gstack browse 无头，截图 `.superpowers/sdd/shots/wms-*.png`）：
  - WcsTask：title「WCS タスク 5」+ count pill、CpFilterBar、5 行、状態/優先度 CpTag pill、新建 CpFormDialog（13 字段 grid）——✅。
  - Carrier：title「配送業者 0」、空态 CpEmpty + pager(Total 0)、新建 CpFormDialog（梱包NO*/業者* required 标记 + キャンセル/保存）——✅（无种子数据，验证空态）。
  - IotMonitor：「IoT 監視 · 1 alerts · 3 sensors」、el-alert、3 sensor 行、投入 CpFormDialog + 履歴只读弹窗——✅。
  - ReportCenter：動的表单 + 実行→在庫月報 16 行 + 「件数: 16」CpTag pill + CSV 按钮——✅。
  - StockTake：无数据故 `/wms/stock-take` 直达验证 default 渲染（計画/全棚卸 CpTag pill + el-descriptions + action-bar 戻る/カウント開始/取消）——✅。
  - **console**：5 页均无新 error；仅既有 intlify flatten warning + Vue Router next() deprecated + 首帧「No match」warning（动态路由，均属 baseline 非本批引入）。

## 新增模板缺口

**1 个**：#16 CpListPage 无 `@row-click` 透传（Carrier，已代偿为操作列「詳細」按钮）。写入计划文档 §模板缺口「批次4 复盘」。

## Concerns

- Carrier / StockTake / stock-take-list 三处后端无种子数据；已按 guide 验证空态 / default 渲染并注明，未能真栈点击真实行操作（列表页行按钮、StockTake 状态流转）。逻辑经 type-check + 单测覆盖。
- CpEmpty 空态仍显中文默认「暂无数据」（见 Carrier 截图）——属既有 follow-up #6（共享词条决策），非本批引入。
