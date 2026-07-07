# WMS 迁移批次6 报告

分支 `feat/ui-migrate-wms`。页面：QcInspection(283) / PlateMold(294) / Rma(300) / OutboundOrder(303) / PickingWork(303)。

## 形态分类

| 页 | 形态 | 迁移方案 |
|---|---|---|
| QcInspectionView | list+detail 単一ファイル（mode トグル） | list→CpPageShell+CpListPage（paginated=false）；detail→CpDetailPanel(基本情報)+編集テーブル保持+token 化 |
| PlateMoldView | 查询列表页 + 4 ダイアログ | CpPageShell+CpListPage（paginated=false）；create/use/maint/warnings は el-dialog 保持 |
| RmaView | list+detail 単一ファイル（mode トグル） | list→CpPageShell+CpListPage（paginated=false）；detail→編集フォーム保持+token 化 |
| OutboundOrderView | 特殊页（フルページ編集フォーム） | token 化 + 基础件替换（ヘッダ el-tag→CpTag、var(--el-*)→--cp-*）；模板不套用 |
| PickingWorkView | 特殊页（ピッキング作業台：タスクキュー+スキャン確認） | token 化のみ、模板不套用 |

## 逐页盘点（一项不许丢）

### QcInspectionView
- API：qcInspectionApi.search/get/createFromInbound/saveItems/judge/cancel —— 全保留。
- 列 8：inspectionNo(mono)/status(tag+map)/finalJudgement(col slot 条件付タグ、null 時非表示保つ)/inboundNo/supplierName(tooltip)/arrivalDateTime(col slot: replace('T',' ').slice(0,16))/generatedReceiptNo/操作(col slot)。
- 搜索 4：inspectionNo/inboundNo/status(select)/finalJudgement(select)。fromInbound は CpPageShell #actions。
- detail：基本情報 el-descriptions(6項)→CpDetailPanel(cols=3、全 text)；ヘッダ状態/判定 el-tag→CpTag(tone)；受入数量編集テーブル(el-input-number×5)保持；judge/bridge el-dialog 保持。判定ヒント色 #909399→--cp-muted、action-bar var(--el-*)→--cp-*。
- i18n：wms.qc.*/wms.inbound.*/wms.outbound.btn.cancel 全保留；filter-labels 接 search→wms.common.search、reset→wms.common.clear。无 v-permission。

### PlateMoldView
- API：plateMoldApi.search/create/recordUsage/startMaintenance/completeMaintenance/discard/warnings —— 全保留。
- 列 12：plateNo(mono)/status(tag+map)/plateType(map タグなし文案置換)/customerCd/productCd/productName(tooltip)/colorCount(num)/sizeNote/lifeRatio(col slot: el-progress+使用/最大)/lastUsedAt/nextMaintenanceDate/操作(col slot 状態別 4 ボタン、補償 #16)。
- 搜索 5：plateNo/plateType(select)/customerCd/productCd/status(select)（>4 → CpFilterBar 展开更多 自動折叠）。头部：新規/寿命警報。
- create(14 項グリッド)/use/maintStart/warnings は el-dialog 保持。warnings 内表の状態 el-tag→CpTag(tone)。変更後 listRef.reload()（:key 再マウント不使用）。原単表無分页 → paginated=false。
- i18n：wms.plate.*/wms.vmi.fld.customerName/wms.common.* 全保留。无 v-permission。

### RmaView
- API：rmaApi.search/get/create/receive/startInspection/judge/close/cancel —— 全保留。
- 列 9：rmaNo(mono)/status(tag+map)/customerCd/customerName(tooltip)/originalShippingNo/appliedDate(kind:'date')/warehouseCd/returnReason(tooltip)/操作(col slot)。
- 搜索 4：rmaNo/customerCd/originalShippingNo/status(select)。头部：新規(openCreate→detail 新規)。
- detail：新規/閲覧兼用 el-form(条件 disabled) 保持；ヘッダ状態 el-tag→CpTag(tone)；明細編集テーブル(condition/judgement select、TXN タグ)保持；addLine/removeLine/各遷移ボタン保持。action-bar var(--el-*)→--cp-*。
- i18n：wms.rma.*/wms.outbound.fld.*/wms.inbound.msg.*/wms.common.* 全保留。无 v-permission。

### OutboundOrderView（特殊页）
- API：outboundOrderApi.get/create/update/confirm/allocate/ship/cancel/delete —— 全保留。
- 結構（ヘッダカード+条件明細フォーム+編集明細テーブル+action-bar+ship ダイアログ）原様保持。フルページ編集フォームのため模板套用せず。
- token 化+基础件替换：ヘッダ状態/種別 el-tag→CpTag(tone)；statusTagOf(Element type)→statusTone(Tone)；action-bar/.ok の var(--el-bg-color/border-color-lighter/color-success)→--cp-card/--cp-line-soft/--cp-ok。硬编码色值なし（既に token 化済み）。

### PickingWorkView（特殊页）
- API：outboundOrderApi.search(status 2/3)/get/startPicking —— 全保留。client-side lineState(done/short/actualQty)/scan 検証/progress 全ロジック不動。
- 結構（左タスクキュー+右作業エリア+ヘッダ進捗+明細行+pick/short ダイアログ）原様保持。ピッキング作業台のため模板套用せず。
- token 化：硬编码色值(#666/#888/#67c23a/#ebeef5/#f5f7fa/#c0c4cc/#ecf5ff/#409eff/#606266/#303133/#f0f9eb/#95d475/#fafafa + rgba(64,158,255,.3))→ --cp-* token；インライン color style→ クラス化(hd-sub/hd-cap/hd-count/prod-sub/picked-qty)；6px/8px 圆角→--cp-r-sm；0.15s→--cp-t-base；font-size リテラル→--cp-fs-* token；active box-shadow→--cp-shadow-1。el-tag/el-empty は作業台の機能部品として保持。

## 验证证据

- `npm run type-check`：0 error。
- `npm run test`：46 files / 304 passed（基线 304 holds）。
- 真栈走查（dev 5173 + API 9991，admin/123456，login 200）：5 页全部路由直达打开，console 无新 error（仅既有 intlify flatten / Vue Router "No match" / next() deprecation warning，均迁移前既有）。
  - QC：数据 1 行(QC-DEMO-001 判定済)，CpTag ok tone 渲染；検索/クリア 触发 fetch；開く→detail：CpDetailPanel(6項)+編集テーブル+戻る 正常。截图 wms-qc.png / wms-qc-detail.png。
  - RMA：空态渲染(count 0，QA 库无 RMA 数据)；新規→detail 編集フォーム 打开正常。截图 wms-rma.png / wms-rma-new.png。
  - PlateMold：空态渲染(暂无数据，QA 库无版型数据)；5 搜索字段折叠正常；新規 create ダイアログ(14 項)+寿命警報 ダイアログ(CpTag 状態列) 打开正常。截图 wms-plate.png / wms-plate-create.png。
  - OutboundOrder：既存単(OB-P4-CROSS)開く→編集フォーム、ヘッダ CpTag(状態+種別 材料出庫) 渲染正常。截图 wms-outbound-order.png。
  - PickingWork：3 タスク(OB-PICK-DEMO 他 ピッキング中)渲染；タスク選択→作業エリア 明細行(#1〜#4)+進捗+本行確定/欠品報告、token 化観感正常(active カード brand-teal 高亮、明細 --cp-bg-th)。截图 wms-picking.png / wms-picking-work.png。

## 新增模板缺口

0 个。本批仅复用既有补偿：#16（无 @row-click → action-column 按钮，用于 QC/RMA/PlateMold 各行/条件行操作）。
新形态观察（非缺口，未占号）：list+detail 単一ファイル(mode トグル)の場合、CpListPage は `v-if="mode==='list'"` により detail→list 復帰時に再マウントされ onMounted で自動 fetch（QC/RMA は listRef 不要）。編集ダイアログでない複合フォーム(PlateMold create 14 項グリッド、RMA/QC 明細編集テーブル、OutboundOrder フルページ編集)は CpFormDialog 契約に載らず、既有約定どおり el-dialog/el-form を保持。

Status：DONE。
