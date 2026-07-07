# WMS 迁移批次3 报告

分支：`feat/ui-migrate-wms`　样板：`OutboundOrderListView.vue`
页面（5）：InboundOrder / SampleStock / Pallet / LocationList / PaperRoll

## 形态分类

| 页面 | 分类 | 目标模板 |
|---|---|---|
| SampleStockView | 查询列表页 + 新建/借出弹窗 + 行操作 | CpPageShell + CpListPage + 2×CpFormDialog（default slot） |
| PalletView | 查询列表页 + 新建/移動/出荷弹窗 + 行操作 | CpPageShell + CpListPage + 3×CpFormDialog（default slot） |
| PaperRollView | 查询列表页 + 入庫/消費/スリッター弹窗 + 行操作 | CpPageShell + CpListPage + 3×CpFormDialog（default slot） |
| InboundOrderView | 全页可编辑录入页（明细行内编辑） | 特殊页——token 化 + 基础件替换（状態 el-tag → CpTag），维持 el-card 结构 |
| LocationListView | master-detail 双表联动（倉庫选择→ロケ列表）+ 编辑弹窗 | 特殊页（CpListPage 不表达双表联动）——token 化 + CpTag 替换，保留双栏结构与 el-dialog |

## 迁移前盘点（一项不丢）

**SampleStock**：API `sampleApi.search/create/lend/return/expire`；列 13+操作（サンプルNO mono / ステータス tag / 種別 map / 顧客 / 客先名 overflow / 製品 / 製品名 overflow / 数量 map·formatQty+unitCd·右 / ロケ / 貸出先 / 貸出日時 / 予定返却日 col slot·逾期红字 / 返却日時 / 操作）；行操作（貸出 status∈{0,2} / 返却 status=1 / 廃棄 status≠3）；搜索 6（サンプルNO/種別/顧客/製品/状態 + **overdueOnly 复选**）；头部 新規；新建弹窗 10 字段（種別 select·必填/数量 number·必填/顧客/客先名/製品/製品名/Unit/倉庫/ロケ/備考）；借出弹窗（貸出先 必填/予定返却日 date）；无 v-permission。

**Pallet**：API `palletApi.search/create/completeBuilding/moveToShipping/markShipped/delete`；列 12+操作（パレットNO mono / ステータス tag / 製品 / 製品名 overflow / ロット / カートン数 右 / 重量 map·formatQty·右 / 高さ 右 / 最大段 右 / 倉庫 / ロケ / 出荷先NO / 操作）；行操作（完了 status=0 / 移動 status=1 / 出荷 status=2 / 削除 status=0）；搜索 5（パレットNO/製品/ロット/倉庫/状態）；新建弹窗 10 字段（製品·必填/製品名/ロット·必填/カートン数 number·必填/重量/高さ/最大段/倉庫·必填/ロケ·必填/備考）；移動弹窗（移動先ロケ 必填）；出荷弹窗（出荷先NO 必填）；无 v-permission。

**PaperRoll**：API `paperRollApi.search/create/consume/slit/dispose`；列 10+操作（ロールNO mono / ステータス tag / 銘柄 / 幅mm 右 / 流れ目 中 / 坪量 右 / 残/原長 col slot·progress / 芯径 map·加″·右 / ロケ / 親ロール / 操作）；行操作（消費 status≠3 / 廃棄 status≠3）；搜索 5（ロールNO/銘柄/**幅mm number**/流れ目 select T·Y/状態）；头部 新規 + **スリット**；入庫弹窗 11 字段（銘柄·必填/幅 number·必填/坪量/流れ目 select/原長 number·必填/芯径/倉庫·必填/ロケ·必填/製造日 date/製造ロット/廃棄閾値）；消費弹窗（残長 CpTag 展示 + 消費長 number·必填）；スリッター弹窗（親ロール·必填/子幅 CSV·必填/端材保持 switch）；无 v-permission。

**InboundOrder**：API `inboundOrderApi.get/create/update/confirm/cancel/delete`；头部编辑表单（区分 select/入庫倉庫/入荷予定日 date/仕入先CD/仕入先名/発注書NO/備考）+ 状態 pill；明细行内编辑表（製品/製品名/ロット/予定数量/累計受入/単位/予定ロケ/単価 + 追加/削除行）；action-bar（戻る/保存/確定 status=0/取消/削除 status=0/受領 status∈{1,2}）；onMounted 依 query.no 载入；无 v-permission；原页零硬编码色值（仅 EP 令牌）。

**LocationList**：API `warehouseApi.search/getLocationTree/createLocation/updateLocation/deleteLocation`；左栏倉庫表（CD/倉庫名/種別 map）+ current-change 选择；右栏ロケ表（ロケCD/表示名/階層レベル tag / 親ロケ / 座標 col slot / 容量 col slot / フラグ tag×2 / バーコード / 操作 編集·削除）；空态（未选倉庫 / 无ロケ）；编辑弹窗（ロケCD·必填/倉庫 disabled/親ロケ/レベル select/表示名/XYZ 座標 number/容量 number/許可品目/ピッキング可 switch/凍結 switch/バーコード）；无 v-permission。

## 每页迁移摘要

- **码值列**：ステータス（各页）→ `kind:'tag'` + `map`（label 走 t() computed，tone 用共享 Tone；EP type→Tone 保色 info→muted·primary→info·warning→warn·success→ok·danger→danger）；種別/芯径 用无 kind 的纯 `map`（换文案/加单位，无 tone）。
- **数量列**：数量/重量 用 `map:(v,row)=>({label:formatQty(...)})`（声明式，免插槽，数量列 map 读 row 拼 unitCd）；`align:'right'`。
- **col-slot 逃生舱**：SampleStock 予定返却日（逾期红字 class）、PaperRoll 残/原長（el-progress 进度条）—— slot 保原视觉；操作列一律 `col-_action` 具名插槽。
- **弹窗**：Sample/Pallet/PaperRoll 全部 8 个弹窗用 CpFormDialog **default slot**（fields 声明表达不了 select/input-number/switch/date/maxlength/placeholder）；必填改 el-form `rules`（validate() 门禁，等价原手工校验；PaperRoll 消費長补 >0 validator）；移動/出荷/借出/消費 标题拼 target 单号（title prop 计算式）。
- **in-place 刷新**：所有列表页 `listRef.value?.reload()`（契约 #12），删除/执行/新建后保留筛选/页码；`:key` 重挂载方案未用。
- **filter number 字段**：PaperRoll 幅mm 用 FilterField `type:'number'`（契约 #10），spinner 恢复。
- **特殊页 token 化**：InboundOrder 状態 `el-tag :type` → `CpTag :tone`（原页无硬编码色值，仅此一处基础件替换）；LocationList `#909399/#999`→`--cp-muted`、`#ecf5ff`→`--cp-brand-bg`（is-selected 行高亮）、階層レベル/フラグ el-tag → CpTag（tone info/muted/danger），保留 master-detail 双栏结构与 el-dialog（特殊页「不强套模板」）。
- scoped style 目标归零：Sample 仅 `.sample-overdue`（--cp-danger）；PaperRoll 仅 `.cp-hint`（--cp-muted）；Pallet 无 scoped；InboundOrder 仅布局 + EP 令牌；LocationList 仅布局（色值全 --cp-* 令牌）。

## 批次验证证据

- `npm run type-check`：**0 error**。
- `npm run test`：**46 files / 304 tests 全绿**（基线 304，未回退）。
- 真栈走查（dev 5173→9991，`POST /api/auth/login` 200，admin/123456，gstack browse，日本語）：
  - **InboundOrder**：编辑表单渲染（区分/倉庫/入荷予定日 2026-07-04/仕入先/備考）、状態 pill「● 下書き」= CpTag(muted)、明细 No Data、action-bar 戻る/保存；截图 `shots/wms-InboundOrder.png`。
  - **SampleStock**：空态；标题「サンプル品」+ 计数 pill 0、4 搜索字段 + 展开更多（第 5 状態）+ クリア/検索、**未返却(超過) 复选置 toolbar slot**、CpEmpty「暂无数据」、pager Total 0；截图 `shots/wms-SampleStock.png`。
  - **Pallet**：空态；标题「パレット管理」0、5 搜索字段（4+展开）、新規、CpEmpty、pager；截图 `shots/wms-Pallet.png`。
  - **PaperRoll**：1 条 ROLL-D-20260515-003（ステータス「● 使用中」= CpTag(info)、ロールNO mono、幅 905、残/原長 300/1,500m + 进度条、消費/廃棄 行操作）、幅mm number 过滤 spinner、新規 + スリット 头部动作；新規弹窗 11 字段（必填星号 + input-number/select/date）打开 OK；截图 `shots/wms-PaperRoll.png` / `wms-PaperRoll-createdlg.png`。
  - **LocationList**：master-detail 双栏；未选倉庫时右栏空态「左の倉庫一覧から…」（--cp-muted）；选 DW01 → is-selected 行高亮（--cp-brand-bg 令牌）+ 右栏ロケ表 DEMO-RAW-A-01、階層レベル「● ゾーン」= CpTag(info)、編集/削除；截图 `shots/wms-LocationList.png` / `wms-LocationList-detail.png`。
  - **console**：仅既有全局噪声——intlify object-flatten warning、Vue Router `next()` 弃用 warning、首帧异步路由未匹配 `No match found for /wms/…`（页面随即正常渲染，与批次2同类瞬时噪声）。CpFormDialog 打开后 `console --errors` = (no console errors)。**无本批新增错误**（无 [error]、无 Vue 组件 warning、无 el-* 库内新增 warning）。

## 新增模板缺口：1（#15）

15. **CpFilterBar 无 boolean/checkbox 字段类型**（Minor）
    - 现象：SampleStock 原「未返却(超過)」为 el-checkbox 查询条件（`overdueOnly: boolean`）。FilterField 仅 text/select/date/daterange/number，无法渲染复选。
    - 代偿：`overdueOnly` 提为页级 `ref`，放 CpListPage **toolbar slot** 复选，`fetchList` 闭包读取 + `@change` 触发 `listRef.reload()`——控件与功能完整保全（未降级为 select 下拉）。
    - 建议：FilterField 增 `type:'boolean'`（透传 el-checkbox / el-switch），或 CpFilterBar 增字段级插槽，让布尔查询条件声明式进查询区。

（注：LocationList 为 master-detail 双表联动，CpListPage 单表卡形态不表达，按「特殊页不强套模板」保留双栏 el-table + el-dialog，未计入模板缺口——与批次2 LotTrace/InboundReceipt 处置一致。CpFilterBar `展开更多/收起` 与 CpEmpty 空态仍组件内中文默认，沿用 follow-up #6，非本批引入。）

## Concerns

1. **无阻塞项**；未发现模板 BUG（未改任何模板组件本体）。
2. **列表页由「全量单表」转「客户端分页」**：Sample/Pallet/PaperRoll 原页 `pageSize:100` 单表无分页，迁移后走 CpListPage 默认 `paginated` + PAGE_CAP=500 客户端切片（20/页），与批次1 CrossDock 同一 accepted transform（数据完整、total 按全量算）。
3. **InboundOrder 近零改动**：特殊录入页原已合规（无硬编码色值），仅状態 el-tag → CpTag 一处基础件替换，维持 el-card + action-bar 结构（与批次2 InboundReceipt 保守处置一致）。
4. **LocationList 编辑弹窗保留 el-dialog**：双表联动特殊页，未将复杂座標/switch 表单强转 CpFormDialog，符合「特殊页不强套模板」；若后续统一，需接受 isNew 检测逻辑重排。
