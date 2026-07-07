# WMS 迁移批次8 报告（模块收尾）

分支：`feat/ui-migrate-wms` ｜ 前端 `cp6.web/`

## Part A — 末两页迁移（模块最大）

### 1. KitView.vue（キット，原 420 行）

**迁移前盘点（一项不许丢）**
- API（kittingApi）：searchMasters / getMaster / createMaster / updateMaster / deleteMaster / searchOrders / getOrder / createOrder / execute / cancel（10 项全保留）
- マスタ一覧列：kitSku / kitName / defaultWarehouseCd / active(ON/OFF) / 操作(開く·削除)
- 組立一覧列：kitOrderNo / direction(tag) / status(tag) / kitSku / kitName / qty / warehouseCd / kitLocationCd / kitLotNo / executedAt / 操作(開く)
- マスタ検索：kitSku；組立検索：kitOrderNo / kitSku / direction / status
- マスタ detail：フォーム(kitSku/kitName/defaultWh/activeFlg/remarks) + BOM 編集テーブル(componentProductCd/componentName/requiredQty/unitCd + 行追加/削除) + action-bar(戻る/保存)
- 組立 detail：フォーム(kitSku select/direction/qty/warehouse/kitLoc/kitLot/remarks) + executedTxnNos alert + ヘッダ status/direction tag + action-bar(戻る/保存/実行/取消)
- 権限：无 v-permission；i18n：全 wms.kit.* + wms.common.* + wms.outbound.btn.cancel + wms.inbound.msg.* 词条保留（未臆造）
- 码值：directionMap(ASSEMBLE/DISASSEMBLE) / orderStatusMap(0 下書き/1 実行済/9 取消)

**分类与处置**：el-tabs 双模块 × list+detail 双态。list 态→CpListPage（tag map / col slot qty·executedAt / toolbar 新規 #15），`v-if` 随 mode 卸载→戻る重挂 auto-fetch。detail 态=新規/閲覧兼用の編集フォーム+BOM 編集テーブル（特殊エディタ領域，保留 el-card/el-form/el-table/el-affix）→ CpTag ヘッダ + token 化 action-bar/txn-list。組立 kitSku ドロップダウン=別ソース `activeMasters`（onMounted＋マスタ変更後ロード）で疎結合。el-tabs=特殊ナビ（模板契約外）→ CpPageShell 被せず原页无页头を踏襲。status tone：0=muted(EP info=グレー保色)/1=ok/9=danger。direction tone：ASSEMBLE=ok/DISASSEMBLE=warn。active：ON=ok/OFF=muted。

### 2. StockDwellView.vue（在庫滞留レポート，原 456 行）

**迁移前盘点**：API stockDwellApi.summary(payload)；query(groupBy/warehouse/product/owner/asOfDate)；KPI×4(総在庫/90日超数量/90日超比率/在庫金額)；滞留バケット横棒グラフ(0-30/31-60/61-90/90超)；明細テーブル(集計単位/総数量/金額/4 バケット/最古入庫日/最長滞留日数) + モバイル表示；i18n 全 wms.stockDwell.* 保留。

**分类与处置**：仪表盘/分析特殊页 →「非表格特殊页只做 token 化 + 基础件替换」。el-tag→CpTag×3、el-empty→CpEmpty×2；内联全色値 token 化（#303133→--cp-ink / #606266→--cp-muted / KPI 枠色 #409eff·#f56c6c·#e6a23c·#67c23a→--cp-info·danger·warn·ok / バケット 4 色→--cp-ok·info·warn·danger / #eef2f7→--cp-line-soft / #d93026→--cp-danger / #ebeef5→--cp-line）。バケット色は意味づけ色（新鮮→期限超過）で設計トークン 1:1 対応 → §2.5 図表色免除は不使用、完全トークン化。

## Part B — 模块硬编码清扫

最终 grep（`#[0-9a-fA-F]{3,8}|rgba?(`，排除 `template #default` 正则误报后）：

```
（空 —— 非 #default 误报行 = 0，无 /* cp-chart-color */ 免除行）
```

`var(--el-*)` 残留清扫（wms 视图内，有 --cp 等价者）：InboundReceipt/InboundOrder/Kit action-bar（→--cp-card/--cp-line-soft）、LotTrace .qty-in/.qty-out（→--cp-ok/--cp-danger）。清扫后 `grep var(--el- cp6.web/src/views/wms` = 0。

## Part C — 累积清理

1. SlottingView：删死 CSS `.wms-slotting{padding:16px}`；`listRef` 接线（listDirty 脏标记 + backToList() 承認/取消后返回一覧时 reload()，因 CpListPage 为 v-show 常挂需手动刷新）。
2. SlottingView：删除 detail action-bar 重复「戻る」（保留 header #actions 版）。
3. CrossDock xDockNo 修正：列 prop + onExecute/onCancel 行读取 + CrossDockOrder 类型三处 `xdockNo`→`xDockNo`（后端 XDockNo camelCase 序列化含大写 D）；create 响应体 `{ xdockNo }` 字面匿名对象 + search 过滤键（大小写不敏感绑定）保持不变。

## 验证证据

- **type-check**：0 error（vue-tsc --build）
- **test**：304 passed / 46 files（基线 304 ✅）
- **hardcode grep**：非 #default 误报行 = 0；var(--el-*) in wms views = 0
- **真栈走查**（截图 `.superpowers/sdd/shots/`）：
  - Kit：マスタ一覧 Total 2 + ON pill + 開く/削除；マスタ detail フォーム+BOM+行追加；組立一覧 Total 3 + 方向/状態 pill；組立 detail 下書き(muted)/組立(ok) CpTag + 実行/取消 action-bar
  - StockDwell：KPI×4 トークン枠色 + バケット横棒(--cp-ok/info 実測 rgb(34,181,115)/rgb(78,128,238)) + 基準日 CpTag(info)
  - CrossDock：単号列 = XD2026070001（原空白）；`POST /api/wms/cross-dock/XD2026070001/execute → 200`（原 `/undefined/execute → 400`）
  - Slotting：分析実行→detail 承認→戻る→一覧 `GET /wms/slotting` リロード発火、SLP2026070001/承認済/admin 反映（フレッシュネス確認）
  - console 无本批新 error（SignalR CSRF 403 / EP small·label deprecation = 環境·既有，非本批引入）

## 新增模板缺口

**无**（两页均落在既有契约 #15/#16/#17 与「特殊页 token 化」处置内）。批次8 复盘已写入 `docs/superpowers/plans/2026-07-04-ui-restyle.md`。

## Commits

- ①（refactor）：Kit/StockDwell + 模块硬编码清扫 + gap doc
- ②（fix）：CrossDock xDockNo + Slotting 清理
