# ERP 迁移批次1 报告（feat/ui-migrate-erp）

样板：`cp6.web/src/views/wms/OutboundOrderListView.vue`（CpPageShell + CpListPage + CpFilterBar + CpTag）。
本批 5 页，路由（menus/router）：`/erp/fx-rate`、`/fsc-checklist`、`/sheet-unit-price`、`/business-partner-list`、`/order-price-correction`。

## 分类判定（honest classification）

| 页 | 行数 | 判定 | 依据 |
|---|---|---|---|
| FxRateView | 194 | **CpListPage + CpFormDialog** | 真の照会一覧：onMounted 自動取得・必須検索条件なし・単一 fetch。CpListPage 契約に素直に載る唯一のページ。 |
| FscChecklistView | 206 | **token 化（特殊页）** | 検索先行：拠点必須・自動取得なし・FROM≤TO / フォーマット必須のクロス項目検証・出力フォーマットはアクション引数。CpListPage の onMounted 自動 fetch と相反。 |
| SheetUnitPriceView | 206 | **token 化（特殊页）** | Excel アップロード + 登録/参照デュアルモード + 行内選択グリッド + 一括更新。検索駆動の単一 fetch 形態でない。 |
| BusinessPartnerListView | 219 | **token 化（特殊页）** | サーバサイド列ソート（@sort-change、CpListPage 未透過＝機能喪失リスク）+ 属性 FLG 11 チェック群 + 詳細検索コラプス（分類 1〜10）。CpFilterBar の平坦フィールド列では構造化検索群を表現できず、強套するとソート機能を失う。 |
| OrderPriceCorrectionView | 237 | **token 化（特殊页）** | type=selection 選択に連動する行内編集グリッド（変更後単価/特値/理由）+ 拠点必須・自動取得なし。WMS StockTake（編集テーブル→token 化）と同処置。 |

判定基準：CpListPage は onMounted 自動 fetch + 単一 fetch 契約。auto-load が無害で必須検索条件を持たない照会一覧のみテンプレート化、それ以外（検索先行/行内編集グリッド/Excel 取込/サーバソート）は「非表格特殊页は token 化のみ、強套しない」に従い token 化。WMS 批次2/3/4 の LotTrace/LocationList/StockTake と同一の処置方針。

## 迁移前盘点（一項不許丢）

### 1. FxRateView → CpPageShell + CpListPage + CpFormDialog
- **API**：fxRateApi.list(currency?) / create / update(id) / remove(id)。全保全。
- **列**：currencyCd / rateDate(kind:'date') / rate(col-rate slot＝formatQty(v,6) 6 桁固定) / remarks(overflowTooltip) / 操作(col-_action)。全保全。
- **搜索**：filterCurrency → searchFields text（placeholder＝erp.fxRate.filter.currency）。
- **行操作**：編集(openEdit)/削除(remove, ElMessageBox.confirm)。
- **头部**：新規レート → #actions。base:JPY タグ + subtitle → toolbar slot（CpTag tone:info / .fx-sub）。
- **i18n**：erp.fxRate.* 全保全。filterLabels＝{ search:erp.fxRate.btn.refresh(＝「通貨で再読込」語義の既存キー), reset:sales.btn.clear(既存キー) }。
- **v-permission**：原页なし。
- **弹窗**：CpFormDialog default slot で currencyCd(uppercase)/rateDate/rate(input-number precision6 step0.5 + hint)/remarks(textarea) を保全。必須は el-form rules（currencyCd/rateDate/rate）。

### 2. FscChecklistView → token 化
- **API**：fscApi.getFormats / search / issue。全保全。**列**：17 列全保全（rowNo〜fscManagementNo/issued）。
- **搜索**：拠点必須 + 担当/発行日 FROM-TO + 御見積書 NO FROM-TO + 得意先 + 案件 + 未発行/発行済チェック + 出力フォーマット必須。全保全（el-form 原様）。
- **批量**：発行(row.issue チェック → issue API + Excel DL)。**検証**：拠点必須/ステータス択一/FROM≤TO。全保全。
- **el-tag→CpTag**：ステータス列(ok/info)・件数(info)・発行選択(ok)・発行済(ok)。**v-permission**：なし。

### 3. SheetUnitPriceView → token 化
- **API**：importExcel / search / batchUpdate。全保全。**列**：18 列全保全。
- **搜索/操作**：基準日必須/拠点必須/取込区分 radio/操作種別 radio(登録↔参照)/Excel アップロード/全選択/一括更新。全保全。
- **el-tag→CpTag**：件数(info)。**色トークン化**：選択ファイル名 #606266 → --cp-muted(.file-name)。**v-permission**：なし。

### 4. BusinessPartnerListView → token 化
- **API**：search / exportCsv。全保全。**列**：24 列（No/ステータス/取引先…FLG×11/登録日/登録担当）全保全。
- **搜索**：属性 FLG×11 + 登録日 FROM-TO + 取引先/取引先名/法人番号/標準企業/郵便/住所/TEL/営業担当/業務担当 + 詳細検索コラプス(分類 01〜10) + ステータス(事前登録/登録)。全保全。
- **行操作**：行選択(current-change)→照会/編集。**列ソート**：@sort-change サーバサイド保全（CpListPage 未対応のため token 化選択の決め手）。**CSV**：exportCsv 保全。
- **el-tag→CpTag**：ステータス列(info/ok/danger via statusTone)・件数(info)。**色トークン化**：FlgIcon の #67c23a → --cp-ok、#dcdfe6 → --cp-line。**v-permission**：なし。

### 5. OrderPriceCorrectionView → token 化
- **API**：searchPriceCorrection / batchUpdatePrice。全保全。**列**：21 列全保全。
- **搜索**：拠点必須 + 得意先 FROM-TO + 受注日 FROM-TO + 手配NO1/製品CD/顧客品名 + 数量 FROM-TO(number) + 金額 FROM-TO(number) + 仮単価チェック。全保全。
- **行内編集**：type=selection、選択行のみ活性化する 変更後個別/セット単価(input-number)・特値(checkbox)・単価変更理由(input)。**批量**：選択行を更新(WF 起票 + 競合検出)。全保全。
- **el-tag→CpTag**：状態列(warn/ok/info via approvalTone)・件数(info)・選択中(ok)。**色トークン化**：仮単価警告アイコン #e6a23c → --cp-warn(.prov-warn)。**v-permission**：なし。

## 验证证据

- **type-check**：`npm run type-check` → 0 error（NODE_OPTIONS=--max-old-space-size=8192；デフォルトヒープは OOM＝環境既知、非本批）。
- **test**：`npm run test` → 46 files / **304 passed**（baseline 304 保持）。
- **真栈走查**（dev 5173 proxy→9991、POST /api/auth/login 200、admin/123456、browse、5 页ともメニュー入口が無いため localStorage.menus 注入＋route 直達）：
  - FxRate：CpPageShell「為替レート管理 +count pill 0」/ CpFilterBar(通貨・クリア・更新) / toolbar subtitle + 「基軸: JPY」CpTag / CpEmpty「暂无数据」/ Total 0・20/page。新規レート → CpFormDialog「為替レート新規」（必須マーク・日付ピッカー 2026-07-04・input-number 1.000000 精度6・hint・textarea・キャンセル/確定）。
  - FscChecklist：検索先行フォーム全表示（拠点 必須* / ステータスチェック / 出力フォーマット select「製品用チェックシート」/ 発行(0)）+「合計 0 件」CpTag(info) + No Data。
  - SheetUnitPrice：デュアルモードフォーム（基準日必須 2026-07-04 / 拠点必須 / 取込区分 radio / 操作種別 登録↔参照 / Excel 選択）+「合計 0 件」CpTag + 全選択/全解除 + No Data。
  - BusinessPartnerList：FLG×11 グリーンチェック(--cp-ok) + 検索条件 + 詳細検索コラプス + ソート可能列(矢印) + CSV 出力 +「合計 0 件」CpTag(info) + No Data。検索実行 → 0 行（当テナントに種子データ無し、空態確認）。
  - OrderPriceCorrection：検索フォーム（拠点必須* / 数量・金額 FROM-TO number / 仮単価チェック）+ type=selection + 行内編集列 +「合計 0 件」CpTag + 選択行を更新(0) + No Data。
  - **console**：5 页とも新規 error なし。残存は intlify object-flatten warning と Vue Router deprecation / 「No match」（menus 注入リロードのタイミング由来）＝アプリ全体の既存基础设施 warning、本批未引入。
  - **無種子データ**：5 页とも当テナントに業務データ無しのため空態レンダリングを検証（BP は検索実行で 0 行確認）。表内ステータス CpTag の tone マッピングは純関数＋FxRate toolbar/全件数タグで tone 描画済のため低リスク、記录在案。
- **截图**：`.superpowers/sdd/shots/erp-{fx-rate,fx-rate-dialog,fsc-checklist,sheet-unit-price,business-partner-list,business-partner-list-data,order-price-correction}.png`。

## 新增模板缺口

**2 件（复盘评审补记；已录入 docs/superpowers/plans/2026-07-04-ui-restyle.md「ERP批次1 复盘」，编号接续 #17）**：

- **#18 CpListPage 无 search-first/lazy 模式**：onMounted 必自动 fetch 与 ERP 反复出现的「先选必填条件再查询」形态（本批 3/5 页）相反。建议 `lazy?: boolean`（默认 false），true 时抑制 onMounted(load)，首查仅由显式 search/reload() 触发。
- **#19 CpListPage 无服务端排序透传**：BusinessPartnerList @sort-change 服务端排序是该页 token 化的 decisive reason。建议 ListColumn 增 `sortable?: 'custom'`，CpListPage 接 el-table @sort-change，把 `sortField?/sortOrder?` 并入 ListFetch query（并 emit sort-change）。

## Concerns

- **subtitle 折込**：FxRate の erp.fxRate.subtitle は CpPageShell に subtitle スロットが無いため toolbar slot に移設（機能保全・視覚維持、cosmetic）。
- **FxRate エラーメッセージ**（勘误）：http.ts:91-96 の共通インターセプタが先に翻訳済みサーバメッセージを toast するため、真実の失敗原因は失われない。実際の効应＝CpFormDialog catch による第二の汎化 toast（かつ 409 時のみ詳細が汎化）——WMS 全線の既有契約であり、本批引入の回帰ではない。
