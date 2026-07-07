# ERP 迁移批次3 报告（feat/ui-migrate-erp）

样板：`cp6.web/src/views/wms/OutboundOrderListView.vue`。本批 5 页/組件。路由：`/quotation-list`、`/erp/backorder`、`/erp/otd-report`、`/erp/order-trace`、`OrderCancelDialog`（`/order-list` の取消フローで消費）。

## 分类判定（honest classification）

| 页 | 行数 | 判定 | 依据 |
|---|---|---|---|
| QuotationListView | 471 | **CpListPage（standalone）** | 照会一覧（onMounted 自動取得）+ サーバソート（#19）+ ステータス複数チェック（#15）+ 別タブ発行/参照（openInWindow）+ postMessage 自動 reload。ページ標題キー無し（不臆造）→ CpListPage スタンドアロン、件数はページャ total。 |
| BackorderListView | 418 | **CpPageShell + CpListPage** | 照会一覧（backorderApi.queue＝扁平配列・ページングなし）→ **paginated=false**（単表スクロール）。標題キー erp.backorder.title あり → CpPageShell。close/split アクション＝確認ダイアログ（兄弟要素）。件数＝toolbar CpTag（原 erp.backorder.total 文言保持）。 |
| OtdReportView | 420 | **特殊页（token化）** | KPI カード + 横棒チャート + 進捗テーブルの分析レポート。テンプレートを強套せず token化＋CpTag 置換のみ。 |
| OrderTraceView | 446 | **特殊页（token化）** | el-timeline/el-collapse ベースの追跡タイムライン。token化＋el-tag→CpTag（サマリ帯/チェーン見出し/EventContent 描画関数）。 |
| OrderCancelDialog | 235→237 | **working form dialog（token化＋Phase-6 遺留 bug 修復）** | 3 ステップの取消フォームダイアログ。scoped style なし＝色トークン化対象ゼロ、基础件置換（el-tag→CpTag ×4）のみ。@cancelled 契約保全。**Phase-6 から存在した「開かない」bug を 1 行で修復（下記 concern）。** |

## 迁移前盘点（一項不許丢）

### 1. QuotationListView → CpListPage
- **API**：quotationApi.getList/issue/remove、masterApi.getBases（拠点 select）。全保全。
- **列**（15）：qtnNo(mono,fixed left,sort)/qtnIssueDate(date,sort)/baseCd(sort)/staffCd(sort,**col slot＝staffName tooltip**)/customerCd(sort)/customerName(overflowTooltip,sort)/projectNoParent(sort)/projectNoChild(sort)/itemName1(overflowTooltip)/firstQuantity(num+map fmtNum)/firstUnitPrice(num+map fmtMoney)/firstAmount(num+map fmtMoney)/totalAmount(num+map fmtMoney,sort)/status(tag+map statusTone: 見積確定済=ok/承認済=warn/else info)/操作。
- **検索**（8）：qtnNoFrom/qtnNoTo(text)、issueDate(daterange valueFormat→issueDateFrom/To を fetch で分解)、baseCd(select 非同期)、staffCd/customerCd/projectNoParent/customerProductName1(text)。filterLabels＝{search:sales.btn.search, reset:sales.btn.clear}。
- **ステータス複数チェック**（0/9/C）＝toolbar（#15、statusSel ref を fetch closure が読む、@change→reload、**#22 @reset で statusSel クリア**）。**新規**＝toolbar（#16、openInWindow('new')）。
- **行操作**：参照/訂正/流用＝openInWindow、発行＝ElMessageBox.prompt（Q/SC/C 選択）、削除＝確定済ガード＋confirm。全保全。postMessage（cp6-quotation saved/deleted）→listRef.reload()。**v-permission**：なし。
- **撤去**：isMobile カードリスト（DS 横スクロール標準へ）／onSortChange 手書き（CpListPage @sort-change へ）／dateRange・statusSel watch（fetch closure 直読へ）。

### 2. BackorderListView → CpPageShell + CpListPage
- **API**：backorderApi.queue/closeRemaining/splitToNewOrder。全保全。**paginated=false**（扁平配列、原 max-height スクロール相当）。
- **列**（10）：webOrderNo(**col slot＝/order へ link**)/顧客(map name||cd,overflowTooltip)/detailNo(align right)/productCd(overflowTooltip)/orderedQty·shippedQty·backorderQty(num+map formatQty)/remainingQty(**col slot＝strong.remaining、--cp-warn**)/lastShipDate(map slice(0,10)||'-')/操作(col slot close/split)。
- **検索**（3）：customerCd(text)、dateFrom/dateTo(date valueFormat)。filterLabels＝erp.backorder.btn.search/reset。
- **頭部**：h2→CpPageShell title(erp.backorder.title)。**件数**＝toolbar CpTag(erp.backorder.total {n})＋**更新 circle ボタン**（listRef.reload）。**close/split**＝確認ダイアログ（reason 必須検証保全）を CpListPage 外兄弟要素で保持。**撤去**：isMobile カード。**v-permission**：なし。

### 3. OtdReportView → 特殊页 token化
- 構造保全（フィルタ／KPI ×3／横棒チャート／進捗テーブル／el-progress／el-radio-group）。**el-tag→CpTag**（チャートヘッダ件数 info）。
- **色トークン化**：#303133→--cp-ink、#909399→--cp-muted、#606266→--cp-text、#edf2f7→--cp-line-soft、KPI rate #2f8f63→--cp-ok、late #c45656→--cp-danger、warn border #f3d19e→--cp-warn、bar-fill good/warn/bad→--cp-ok/--cp-warn/--cp-danger（StockDwell と同じく意味論トークンへ、chart-color 豁免 不要）。**API/計算/export CSV 一切改変なし**。

### 4. OrderTraceView → 特殊页 token化
- 構造保全（el-timeline/el-collapse/el-switch/EventContent 描画関数）。**el-tag→CpTag**：サマリ帯 ×5（totalEvents=info/success=ok/failed·dead=danger|info/distinctChains=warn）、チェーン見出し件数（info）、EventContent（sourceModule=info/targetModule=muted/status=statusTone 新設）。ElTag import 除去。
- **色トークン化**：#303133→--cp-ink、#909399→--cp-muted、#606266→--cp-text、event-box border #ebeef5→--cp-line。timelineType/statusIcon/copyCorrelationId 等ロジック不変。

### 5. OrderCancelDialog → token化 + 回帰修復
- **el-tag→CpTag**（autoCancellable yes=ok/no=danger、WO/Outbound 各表 ×2＝計4）。scoped style 無し＝色トークン化対象ゼロ。onProbe/onForceConfirm/woStatusLabel/outboundStatusLabel、**emits @cancelled / update:modelValue 完全保全**。
- **Phase-6 遺留 bug 修復（1 行）**：`const visible = ref(false)` → `ref(props.modelValue)`。`visible=ref(false)`＋非 immediate watch＋`v-if` マウント（modelValue 既に true）の 3 要素は **Phase 6 実装時（c0cc753）から全て存在**——批次2 は配線をバイト同一で保全しただけで、批次2 回帰ではなく **Phase-6 既存 bug**。修正で契約非破壊のままダイアログが開くようになった。

## 验证证据

- **type-check**：`npm run type-check`（vue-tsc --build）→ **0 error**（ダイアログ修復後も再実行し 0）。
- **test**：`npm run test`（vitest）→ 46 files / **316 passed**（baseline 316 保持、増減なし）。
- **真栈走查**（dev 5173 proxy→9991、POST /api/auth/login 200、admin/123456、gstack browse。backorder/otd-report/order-trace は当テナント menus 未載のため localStorage.menus 注入＋route 直達で検証（quotation-list/order-list はメニュー実在））：
  - **Quotation**：Total **15** 行描画。toolbar＝ステータス checkbox 群（未確定/承認済/確定済）＋新規。**サーバソート実証**＝見積NO ヘッダで `GET /api/quotations?sortField=qtnNo&sortOrder=asc&…→200`。**ステータス filter 実証**＝未確定チェックで `?statuses=0` → Total 15→**5**。staffCd tooltip/操作 5 ボタン描画。
  - **Backorder**：CpPageShell 標題「Backorder queue」＋toolbar CpTag「Total: 21」＋更新 circle。**21 行**描画、**pager なし**（paginated=false）。**close ダイアログ**開閉確認（title「Close remaining quantity」、reason textarea、未提出）。リセット→Total 21 復帰。
  - **OtdReport**：標題「On-time delivery report」＋KPI ×3（0.0%/0/0）＋チャートヘッダ CpTag。`POST /api/otd-report/summary→200`（当テナント種子なし→**チャート/テーブル空態 el-empty 確認**）。token 描画正常。
  - **OrderTrace**：標題「Order trace」＋初期タイムライン空態。ORD2026060001 検索→`GET /api/order-trace/ORD2026060001→200`、**サマリカード＋CpTag ×5 描画**。**当テナントに bridge イベント持つ受注ゼロ**（試行 4 件とも totalEvents=0）→ **el-timeline-item（EventContent CpTag 描画関数）は実データ未走査**（型検査通過・サマリ CpTag 経路は実証）。
  - **OrderCancelDialog**：`/order-list` lazy 検索→20 行→行内「取消」→**修復後ダイアログ OPEN**（title「受注取消 —」＋警告 alert＋reason textarea＝step input、未提出でクローズ）。
  - **console**：5 页とも**新規 error なし**。残存＝intlify object-flatten warning / Vue Router next() deprecation / `/api/pub/role-perm/my-actions 401`・`/api/auth/refresh 403`（トークンリフレッシュ基础设施、アプリ全体既有、本批未引入）。
- **截图**：`.superpowers/sdd/shots/erp3-{quotation-list,backorder,otd-report,order-trace,order-cancel-dialog}.png`。

## 新增模板缺口（编号接续，从 #22 起）

（本批で **新規の CpListPage/CpFilterBar/CpTag 模板缺口は発生せず**。既存契約 #15/#16/#17/#19/#20/#22 の再消費のみ。番号 #22 以降の新規追加なし。）

- **参考記録（模板缺口に非ず、Phase-6 遺留 bug）**：OrderCancelDialog が `v-if` マウント時に開かない件は **Phase 6 実装時（c0cc753）から存在した既存 bug**（批次2 は配線バイト同一保全、回帰に非ず）。**非 immediate watch のダイアログ側 1 行修正**で解消（模板本体 CpListPage/CpFilterBar は無関係）。CpListPage の #20（内包 filters を親が読めない）は本批 Quotation の CSV 非該当（Quotation に CSV 出力なし）。
- **既知限制補記（#21 家族、番号不新設）**：QuotationList 原 `@row-dblclick→照会` 手勢は CpListPage に行アクティベート鉤子が無く喪失（#16 行内按钮代偿 / #21 @current-change 未透传と同族の行選択系イベント缺口）。照会は操作列で一键可達＝**能力未丢**、双击ショートカットのみ降級——批次2 EstimateCalc（row-dblclick→操作列集約）と同処置。plan doc「ERP批次3 复盘」に記録済。

## Concerns

- **OrderCancelDialog 可視性 bug（Phase-6 遺留）を本批で修復**：`visible=ref(false)`＋非 immediate watch＋`v-if` マウントの 3 要素は **Phase 6 実装時（c0cc753）から全て存在**し、**取消ダイアログは当初から一切開かなかった**（批次2 は配線をバイト同一で保全＝回帰に非ず）。`visible=ref(props.modelValue)` で修復・契約非破壊。レビュー承認済。
- **OrderTrace EventContent 未走査**：当テナントに bridge/trace イベントを持つ受注が存在せず、`h(CpTag,…)` を含むタイムライン項目の実描画は未確認（型検査は通過、サマリ帯 CpTag は実証済）。データ投入テナントでの再確認を推奨。
- **OtdReport 空チャート**：当テナント種子データなしで KPI=0/チャート空態のみ確認（レンダリング健全）。
- **取消ダイアログ title「— undefined」**：webOrderNo の配線は **Phase 6 から不変**（DTO 上は必須フィールド）で、QA テナントの**種子データ欠落**により当該行の webOrderNo が未投入なだけ——フロント側に修正対象は存在しない。データ由来の表示事象として記録のみ。
