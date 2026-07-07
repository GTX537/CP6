# ERP 迁移批次2 报告（feat/ui-migrate-erp）

样板：`cp6.web/src/views/wms/OutboundOrderListView.vue`。本批 5 页，路由（router）：`/plate-mold-list`、`/product-list`、`/estimate-calc-list`、`/erp/credit-note`、`/order-list`。
本批首次 dogfood 批次1 落地的两条契约：**#18 lazy（search-first）**（OrderList）与 **#19 sortable:'custom' 服务端排序透传**（Order/Product/Estimate）。

## 分类判定（honest classification）

| 页 | 行数 | 判定 | 依据 |
|---|---|---|---|
| CreditNoteListView | 370 | **CpPageShell + CpListPage** | 照会一覧（onMounted 自動取得・単一 fetch・ソート/CSV/一括操作なし）。唯一標題キーあり（erp.creditNote.title）→ CpPageShell + count pill。 |
| EstimateCalcListView | 356 | **CpListPage** | 照会一覧（自動取得）+ サーバソート（#19 dogfood）。CSV/一括なし。標題キー無し→スタンドアロン CpListPage。 |
| ProductMasterListView | 349 | **CpListPage** | 照会一覧（自動取得）+ サーバソート（#19）+ CSV 出力（filter-stash 代償）+ ステータス複数チェック（toolbar #15 拡張）。 |
| OrderListView | 394 | **CpListPage（lazy）** | 検索先行（自動取得なし）→ **#18 lazy dogfood** + サーバソート（#19）+ CSV（stash）+ 預り/mc チェック（#15）+ 受注取消ダイアログ（兄弟要素）+ 追跡/詳細/取消（col slot）。 |
| PlateMoldListView | 253 | **token 化** | 行内「発行」チェックの一括ラベル発行（選択行が CpListPage に内包され親から読めない）+ picker-mode の現在行選択（@current-change 未透過）+ 5 ペア FROM≤TO クロス検証。多機構オペレーション一覧＝FscChecklist（発行+FROM≤TO）/OrderPriceCorrection（行内編集グリッド）と同処置。 |

判定基準：批次1 で token 化の決定打だった「search-first / server-sort」は #18/#19 で解消済 → これらは積極的に CpListPage 化。残る真の阻却因は**行内選択に依存する一括操作**（PlateMold 発行/picker）。CSV 出力は「CpListPage 内包 filters を親が読めない」阻却があるが、**fetch closure で最後の filters/sort を stash** する代償で保全可能（Product/Order で実証）→ token 化せず変換。モバイル専用カード分岐（Estimate/CreditNote/Order）は設計システム標準（main.css:222-225「非 mobile-card-mode の el-table は横スクロール」）に統一して撤去＝基础件（bespoke workaround）を DS 標準へ置換であり機能ドロップではない。

## 迁移前盘点（一項不許丢）

### 1. CreditNoteListView → CpPageShell + CpListPage
- **API**：creditNoteApi.search（normalizePaged で items/Items・total/Total 大小写両対応）。保全。
- **列**（10）：issueDate(kind:date)/creditNoteNo(overflowTooltip)/webOrderNo(col slot＝/order へ link)/rmaNo/得意先(map name||cd)/type(kind:tag+map REFUND=warn/EXCHANGE=info/SCRAP=danger)/productCd/qty(kind:num+map formatQty)/amount(kind:num+map formatQty2)/reason(col slot＝50字省略+tooltip)。全保全。
- **検索**：customerCd/webOrderNo(text)、type(select ALL/REFUND/EXCHANGE/SCRAP)、dateFrom/dateTo(date valueFormat)。filterLabels＝{search:erp.creditNote.btn.search, reset:erp.creditNote.btn.reset}。
- **頭部**：h2 標題→CpPageShell title(erp.creditNote.title)、total el-tag→CpPageShell :count pill。
- **撤去**：isMobile カードリスト（DS 横スクロールへ）。**v-permission**：なし。

### 2. EstimateCalcListView → CpListPage
- **API**：estimateCalcApi.getList/remove、masterApi.getBases（拠点 select）。保全。**列**（11）：qtnCalcNo/qtnDate(date)/qtnBaseCd/staffCd/customerCd/顧客品名(overflowTooltip)/orderQty(num+map fmtNum)/estimateUnitPrice(num+map fmtMoney)/qtnDiv/modifyDate(map fmtDateTime)/操作。全 sortable（#19）。
- **検索**：qtnCalcNo/customerCd(text)、baseCd(select 非同期)、dateRange(daterange)。操作＝view/edit/copy/delete(col slot)、新規＝toolbar(#16)。postMessage(cp6-estimate saved/deleted)→listRef.reload()。row-dblclick→照会は操作列「照会」に集約（#16 row-click 未透過）。**撤去**：isMobile カード。**v-permission**：なし。

### 3. ProductMasterListView → CpListPage
- **API**：productApi.getList/exportCsv/remove。保全。**列**（16）：productCd/setProductCd/setProductName/customerCd/customerName/顧客品名1,2/親子案件/御見積NO/見積計算NO(全 sortable)、status(col slot＝CpTag ok/warn/info)、WF/MC(col slot＝アイコン、色→--cp-ok/--cp-info)、modifyDate(map)、操作(view/edit/copy/delete)。
- **検索**：製品CD FROM/TO+9 text+modifyDateRange(daterange)。**ステータス複数チェック**（0/1/9）＝toolbar(#15 checkbox-group 拡張、statusSel ref を fetch closure が読む、@change→reload)。**CSV 出力**＝toolbar(#16)、fetch closure で最後の filters/sort を stash→buildQuery 再利用。新規＝toolbar。mcTransferFlg 削除ガード保全。postMessage(cp6-product)→reload。**v-permission**：なし。

### 4. OrderListView → CpListPage（lazy）
- **API**：orderApi.searchList/exportListCsv。保全。**lazy=true**（検索先行、onMounted 自動取得なし＝原様）。**列**（24）：rowNo/customerCd(sort)/customerName/担当者/注文書NO/手配NO1(sort)/不適合(sort)/mc注文(sort)/受注日/客先納期(sort)/製品CD(sort)/CP品名(minWidth)/段(sort)/表中裏構成/数量(num,sort)/単位/個別単価(num,sort)/セット単価(num,sort)/受注金額(num,sort)/通貨(col slot currencyCd+fxRate)/預り売上(col slot flag)/伝票備考(sort)/操作。
- **検索**：拠点/得意先 FROM-TO/受注区分+日付 2 ペア+詳細 11 text（CpFilterBar 展开更多で折込）。**預り売上のみ/mc未転送のみ**＝toolbar checkbox(#15)。**CSV 出力**＝toolbar、filter/sort stash 再利用。**受注取消**＝OrderCancelDialog を CpListPage 外の兄弟要素で保持、col slot 取消→dialog→@cancelled→reload。追跡(goTrace)/詳細(goDetail)＝col slot。件数＝toolbar CpTag(info)。空態文言＝emptyText(sales.err.E10008)。**v-permission**：なし。
- **差異（記録）**：原 reset はローカルクリア（fetch なし）だったが lazy 契約で reset も fetch（全件再取得）。原「0 件時 E10008 toast」は CpEmpty 表示で代替。

### 5. PlateMoldListView → token 化
- **API**：plateMoldApi.search/exportCsv/issueLabel。全保全（el-form/el-table/el-pagination/検証/CSV/ラベル発行/picker 原様）。
- **el-tag→CpTag**：合計件数(info)・発行選択中(ok)。**色トークン化**：なし（scoped style は padding/margin のみ、既にクリーン）。**検証**：5 ペア FROM≤TO 保全。**picker-mode**：selectedRow(@current-change)+onPick 保全。**行内発行チェック**+一括ラベル発行 保全。**v-permission**：なし。

## 验证证据

- **type-check**：`npm run type-check` → 0 error（NODE_OPTIONS=--max-old-space-size=8192）。
- **test**：`npm run test` → 46 files / **315 passed**（baseline 315 保持）；#22 修复后 **316 passed**（+reset 时序专项测试）。
- **真栈走查**（dev 5173 proxy→9991、POST /api/auth/login 200、admin/123456、browse。5 页ともメニュー入口あり；今回はブラウザキャッシュの localStorage.menus に未載だったため menus 注入＋route 直達で検証、実メニュー（販売管理 ERP サブメニュー）にも表示確認）：
  - **CreditNote**：CpPageShell「クレジットノート +count 0 pill」/ CpFilterBar(得意先/受注No/種別/起票日+展开更多/クリア/検索) / 10 列 / CpEmpty「暂无数据」/ Total 0。検索実行→Total 0（当テナント種子なし、空態確認）。
  - **Order**：**lazy 実証**＝マウント時「合計 0 件」空態・fetch なし → 検索クリックで初回 `GET /api/orders/list?page=1&pageSize=20 → 200`（Total 24 行）。**サーバソート実証**＝数量ヘッダクリックで `orders/list?sortField=quantity&sortOrder=asc`。toolbar CpTag「合計 24 件」+預り売上/mc未転送 checkbox+CSV 出力。行操作 詳細/Trace/取消 描画。
  - **Product**：自動取得 Total 10 / toolbar ステータス checkbox 群(未承認/承認待/承認済)+新規+CSV 出力 / sortable 列（製品CD/セット製品CD/セット品名/得意先CD ↕）/ 参照・訂正・流用・削除。
  - **Estimate**：自動取得 Total 28 / 拠点 select+見積日 daterange / **サーバソート実証**＝見積日ヘッダクリックで `sortField=qtnDate&sortOrder=asc` / 数量・単価フォーマット済 / 参照・訂正・流用・削除。
  - **PlateMold**（token 化）：検索フォーム全保全（詳細コラプス・ステータス・最新Revのみ表示・表示/クリア/CSV 出力/ラベル発行(0)）+ **el-tag→CpTag「合計 0 件」(info)** + サーバソート列 + No Data（search-first 自動取得なし＝原様）。
  - **console**：5 页とも新規 error なし。残存＝intlify object-flatten warning / Vue Router next() deprecation / el-pagination small deprecation / `/api/pub/role-perm/my-actions 401`・`/api/auth/refresh 403`（トークンリフレッシュ基础设施、アプリ全体既有、本批未引入）。
- **截图**：`.superpowers/sdd/shots/erp2-{credit-note,order-list,product-list,estimate-calc-list,plate-mold-list}.png`。

## 新增模板缺口（编号接续 #18/#19，从 #20 起）

20. **CpListPage 内包 filters を親が読めない → CSV 出力等の親側操作が現在の検索条件を取得できない**（Minor、本批 3 页 Product/Order/PlateMold に該当）
    - 現象：exportCsv(query) は CpFilterBar の各フィールド値を含む全 query を後端へ送るが、filters は CpListPage 内部 `filters.value` に閉じ、親は searchFields を渡すのみで値を読めない。
    - 代償：fetch closure が毎回受け取る `filters`（+ sortField/sortOrder）を page-level ref に stash（`lastFilters`/`lastSort`）し、export で buildQuery 再利用。**唯一の齟齬**：初回 fetch 前の export はフィルタ空（lazy 页で検索前 export は無意味なので許容、auto-load 页はマウント fetch で stash 済）。
    - 建议契約：CpListPage が現在の filters を expose（例 `defineExpose({ reload, getFilters })`）、または `@fetch` に代わる filters 監視／snapshot API。
21. **CpListPage 無 @current-change（行ハイライト選択）透過 → picker-mode の「選択中行を返す」形態が表現できない**（Minor、PlateMold の token 化決定打の一部）
    - 現象：PlateMold の picker モードは highlight-current-row+@current-change で選択行を掴み、フッター「選択して戻る」で呼出元へ返す。CpListPage は selectable(checkbox) の selection-change のみ透過、単一行ハイライト選択イベントを出さない。
    - 代償（token 化側）：原機構を保留。CpListPage 化する場合は操作列の行内「選択」ボタン（#16）で代替可能だが、行内「発行」チェックの一括操作（下記）と併せて token 化を選択。
    - 建议契約：CpListPage 増 `@current-change(row)` 透過（#16 の @row-click と対で行選択系イベントを補完）。
22. **【评审指摘・未披露回归→修复済】CpListPage 無 reset 事件透传 → クリアが toolbar checkbox 筛选を清理できない**（Order/Product 2 页に実在した機能回帰）—— ✅ 已实现（最小扩展、修复 commit）
    - **回归内容（初回提出で未申告）**：原 Order resetQuery() は onlyConsignedSales/onlyMcUntransferred を、原 Product onReset() は statusSel を同時クリアしていた。迁移でこれらを toolbar slot の page-level ref に代償（#15）した結果、CpFilterBar の重置が CpListPage 内部で消化され外発されず——クリア後も checkbox が勾選のまま fetch に参加し続ける回帰が発生。
    - **修复**：CpListPage emits に `reset()` 追加。onReset の順序＝内部 state 清理（page=1、filters は CpFilterBar が先に清空回写）→ **同期 emit('reset')** → load()。監听器が自 ref を清理してから fetch closure が値を読む時序を専項テストで固定（監听器で清理した外部 ref が reset 起因 fetch の query に現れないこと）。Order `@reset`→両 checkbox=false、Product `@reset`→statusSel=[]。
    - **真栈再検証**：Order で両 checkbox 勾選→クリア→checkbox 視覚的にクリア（is-checked false,false）+ 直後 fetch URL `orders/list?page=1&pageSize=20`（両フラグ不在、network grep 0 件）。

（注：モバイル専用カード分岐の撤去は模板缺口に非ず——設計システムは main.css:222-225 で「非 mobile-card-mode の el-table は手机端横スクロール」を DS 標準とし、bespoke カードは restyle 前の workaround。StockDwell 等の分析ダッシュボードは特殊页として token 化でカード保持、標準リスト页はここで DS 標準に統一。CpFilterBar の expand/collapse は組件内中文既定「展开更多/收起」、CpEmpty は「暂无数据」——follow-up #6 既知、本批未変更。ページ標題キーの無い 3 页（Product/Estimate/Order）は不臆造のため CpPageShell 非適用、件数は Order のみ toolbar CpTag で原様保持、Product/Estimate はページャ total で表示。）

## Concerns

- **CSV filter-stash（#20 代償）**：lazy 页（Order/PlateMold）で「検索前に CSV 出力」した場合フィルタ空で全件出力になる（原は入力中フィールド値を送出）。検索先行 UI では検索前 export は非現実的な操作のため許容、記录在案。
- **OrderList reset 挙動差**：原 reset はローカルクリア（fetch なし）→ lazy 契約で reset も全件 fetch。受注一覧は大表になりうるが模板契約通り、記录在案。
- **標題キー不臆造**：Product/Estimate/Order は既存の list 標題 i18n キーが無く、不臆造方針で CpPageShell を被せず CpListPage スタンドアロン（CreditNote のみ erp.creditNote.title あり）。件数 pill は Order の toolbar CpTag で原様維持。
