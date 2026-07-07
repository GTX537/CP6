# WMS 迁移批次7 报告

分支 `feat/ui-migrate-wms`。页面：BridgeHealth(306) / OutboundRouting(313) / MaterialShortage(317) / WmsDashboard(324) / InkLot(343)。

## 形态分类

| 页 | 形态 | 迁移方案 |
|---|---|---|
| BridgeHealthView | 监控仪表盘特殊页（30s ポーリング） | KPI×3→CpStatCard；パネルヘッダ→CpSectionHeader；状態 el-tag→CpTag；token 化。setInterval/clearInterval 原様保持 |
| OutboundRoutingView | 查询列表页 + プレビュー工具卡 + create/edit ダイアログ | CpPageShell(:count)+CpListPage(paginated=false、码値列 map)；プレビューカード/複合ダイアログ は el-dialog 保持（token 化） |
| MaterialShortageView | 查询列表页（サーバ分页）+ KPI + action ダイアログ | CpPageShell(:count)+CpStatCard+CpListPage(サーバ分页、码値状態列 kind:'tag'+map)；remark ダイアログ el-dialog 保持 |
| WmsDashboardView | 仪表盘特殊页（SignalR リアルタイム） | KPI×8→CpStatCard；カードヘッダ→CpSectionHeader；状態/TXN el-tag→CpTag；token 化。SignalR/棒グラフ/タイムライン/明細テーブル 原様保持 |
| InkLotView | タブ式ワークベンチ特殊页（2 リスト+4 ダイアログ） | tabs は模板契約外 → token 化 + 状態 el-tag→CpTag + expiry 色 token 化。el-tabs/el-form/el-table/dialogs 保持 |

## 逐页盘点（一项不许丢）

### BridgeHealthView（监控特殊页）
- API：bridgeHealthApi.metrics/compensate —— 全保留。overallSuccessRate/progressPercent/progressStatus/formatPercent/formatDateTime/formatRange 全ロジック不動。
- 30s ポーリング：`window.setInterval(loadMetrics, 30000)` + onUnmounted `clearInterval` 原様保持。compensate は ElMessageBox.confirm→補償→再ロード保持。
- KPI×3→CpStatCard：24h 成功率(brand+CircleCheckFilled)/再試行キュー(queueDepth>0?warn:info+WarningFilled)/デッドレター数(deadLetterCount>0?danger:info+BellFilled)。
- Hook サマリ/最新デッドレター パネル：el-card #header→CpSectionHeader（#extra に件数 CpTag）。表内 el-progress は EP status 色（semantic、保留）；sourceModule/targetModule el-tag→CpTag(info/muted)；status el-tag→CpTag(danger)。.bad #d93026→--cp-danger。
- i18n：wms.bridgeHealth.*/wms.common.confirm 全保留。无 v-permission。

### OutboundRoutingView（查询列表页）
- API：outboundRoutingApi.list/create/update/remove/preview + warehouseApi.search —— 全保留。
- 列 9：sortOrder(num)/ruleName(tooltip)/customerCd(map:any フォールバック)/productCdPrefix(map:any)/outboundType(map:any or type.N)/targetWarehouseCd(tooltip)/enabled(kind:'tag'+map on/off·ok/muted)/remarks(tooltip)/操作(col slot 編集/削除)。単表無分页 → paginated=false。
- 头部：新規ルール/更新（更新=listRef.reload()）。subtitle は muted テキストで保持。プレビュー工具カード（設定検証、outboundRoutingApi.preview、結果 el-tag→CpTag ok/info）と create/edit ダイアログ（8 項、el-switch/filterable allow-create select 含む複合フォーム）は el-dialog 保持。変更後 listRef.reload()。
- i18n：wms.outboundRouting.*/wms.common.total 全保留。无 v-permission。

### MaterialShortageView（查询列表页）
- API：materialShortageApi.search/resolve/dismiss —— 全保留。normalizePaged(items/Items·total/Total)/formatQty/formatDateTime/shortQty ロジック不動。
- KPI：未対応欠品件数→CpStatCard（openCount>0?danger:brand）。openCount は fetchList 内で OPEN 件数を並列取得しセット。
- 搜索 2：workOrderNo(text)/status(select ALL/OPEN/RESOLVED/DISMISSED)。**status 既定 OPEN** は fetch 側 seed（缺口 #17）。filter-labels：検索→search、クリア→reset。
- 列 11：detectedAt(col slot 日時)/wo(tooltip)/outbound(tooltip)/product(tooltip)/lot(tooltip)/requiredQty(num col slot)/availableQty(num col slot)/shortQty(num col slot 赤字)/status(kind:'tag'+map·OPEN=danger/RESOLVED=ok/DISMISSED=muted)/remark(tooltip)/操作(col slot resolve/dismiss、status!=OPEN で disabled)。サーバ分页（CpListPage 標準、page-sizes [20,50,100]）。
- action ダイアログ（remark textarea）は el-dialog 保持。変更後 listRef.reload()。
- i18n：wms.materialShortage.*/wms.common.total 全保留。无 v-permission。

### WmsDashboardView（仪表盘特殊页）
- API：wmsDashboardApi.kpi/trend/warehouseValue/alerts —— 全保留。SignalR：getWmsConnection/startWmsConnection + StockChanged/InboundReceived/OutboundShipped ハンドラ + scheduleKpiReload(300ms デバウンス) + onBeforeUnmount cleanup、**全ロジック原様保持（未改）**。
- KPI×8→CpStatCard：総在庫金額(brand,sub=SKU/数量)/滞留品(warn,sub=SKU)/今日入庫/今日出荷/引当中(brand)/棚卸(>0?danger)/賞味期限間近(>0?danger)/入庫予定遅延(>0?warn)。
- リアルタイムカード：#header→CpSectionHeader；rtStatus el-tag→CpTag(rtTone: Connected=ok/Connecting=warn/其他=muted)；el-timeline 保持、行内 TXN el-tag→CpTag(txnTone)、el-timeline-item :type は EP 内部色保留。qty-in/out/related-no 内联色→クラス化 token。
- トレンド棒グラフ：#header→CpSectionHeader + 7/30/90 radio 保持；bar-in/out/adj 硬编码 #67c23a/#f56c6c/#909399→--cp-ok/danger/muted；border/trend-date/legend 色→token。倉庫別在庫金額/賞味期限間近/入庫予定遅延 表：#header→CpSectionHeader（アラート表は #extra に件数 CpTag warn/danger）；.minus/.warn el-var→--cp-danger/warn。
- i18n：wms.dashboard.*/wms.stock.*/wms.stocktake.*/wms.warehouse.*/wms.inbound.*/wms.common.* 全保留。无 v-permission。

### InkLotView（タブ式特殊页）
- API：inkApi.searchLots/searchMatches/createLot/openLot/mix/recordMatch —— 全保留。expiryClass/formatQty/inkTypeMap/openMap 不動。
- タブ×2（ロット一覧/調色履歴）は模板契約外——el-tabs 保持。ロット一覧：検索フォーム(inkLotNo/colorCode/inkType/openStatus/expiringWithin30Days checkbox)+新規/混合/開封、テーブル 11 列。調色履歴：検索(customerCd/colorCode)+記録、テーブル 8 列。4 ダイアログ（create/open/mix/record）保持。
- 基础件替换：openStatus el-tag(warning/success)→CpTag(warn/ok)；開封ダイアログ expiry el-tag→CpTag(info)。scoped 色：.expiry-expired #f56c6c→--cp-danger、.expiry-soon #e6a23c→--cp-warn。
- i18n：wms.ink.*/wms.vmi.fld.*/wms.common.* 全保留。无 v-permission。

## 验证证据

- `npm run type-check`：0 error。
- `npm run test`：46 files / 304 passed（基线 304 holds）。
- 真栈走查（dev 5173 + API 9991，admin/123456，login 200，gstack browse）：5 页全部路由直达打开，截图 `.superpowers/sdd/shots/wms-{BridgeHealth,outbound-routing,material-shortage,WmsDashboard,ink-lot}.png` + `wms-outbound-routing-dialog.png`。
  - BridgeHealth：KPI(0.0%/0/0) CpStatCard + アイコンチップ、Hook サマリ/最新デッドレター CpSectionHeader + 件数 CpTag、表 No Data。console clean。
  - OutboundRouting：count 0 空态、新規ルール→ダイアログ開く、プレビューカード渲染。console clean。
  - MaterialShortage：KPI(未対応 0) CpStatCard、検索→reload 無 error、Total 0 + 20/page 分页。console clean。
  - WmsDashboard：KPI×8 CpStatCard グリッド、未接続 CpTag(muted)、トレンド 7/30/90 + IN/OUT/ADJ、倉庫別/アラート表。console：SignalR CSRF 403 negotiate 失败＝**測試環境基础设施既有问题**（SignalR コード未改、未接続 pill 正确反映），无本批引入的新 error。
  - InkLot：2 タブ切替 OK、検索フォーム No Data。console clean。

## 新增模板缺口

1 个：**#17 CpListPage/CpFilterBar 无初始 filter 值（无法 seed 默认查询条件）**（Minor）——MaterialShortage status 既定 OPEN 用 fetch 侧 seed 代偿（初回/リセット→OPEN、''→全件），功能等价，唯一 cosmetic 齟齬=初回 status セレクト空表示。详见 gap doc「批次7 复盘」。
本批复用既有补偿：#15 toolbar checkbox（本批未触发）、#16 action-column button（OutboundRouting/MaterialShortage 各行操作）。

Status：DONE。
