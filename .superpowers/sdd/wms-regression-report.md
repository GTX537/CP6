# WMS Module Regression Gate — feat/ui-migrate-wms

Plan Task 12 Step 5 — final module-level verification before PR.
Date: 2026-07-04 · Repo: C:\CP6 · Frontend: cp6.web/ · Read-only (no code changes, no commits).

## Verdict

**PASS.** Build gate green (type-check 0 / test 304 / build OK). All 40 WMS menu pages
render with content; 0 NEW console errors across the module. All 6 spot interaction
checks pass. No findings.

---

## 1. Build Gate (cp6.web/)

| Check | Command | Result |
|-------|---------|--------|
| Type-check | `npm run type-check` (vue-tsc --build) | **0 errors** (exit 0) |
| Unit tests | `npm run test` (vitest run) | **304 passed / 304** across 46 files (66.6s) |
| Production build | `npm run build` | **Success** — built in 7.60s (exit 0), first prod build on this branch |

**Bundle warnings (build):** only the standard rolldown chunk-size advisory (>500 kB).
Large chunks are pre-existing library bundles, NOT WMS code:
- `es-*.js` 1,126 kB (gzip 355) — locale/i18n data
- `advanced-*.js` 590 kB (gzip 150) — editor libs
- `FloorEditor` 230 kB, `DesignerView` 195 kB, `router` 164 kB.
No new WMS-attributable oversized chunk. No new warning introduced by this branch.

---

## 2. Full-Menu Walkthrough

Dev server 5173 already running; `POST /api/auth/login` → **200** verified. Session
already authenticated (cp6_authed + localStorage.menus present); admin/123456 valid.
WMS menu = 40 leaf routes read from `localStorage.menus` (parent group 400 "倉庫管理(WMS)"
+ sub-groups 420/440/460/480). The 41st WMS-menu entry, "3D倉庫ビュー(Space)"
`/space/stacked/...`, is a Space-module page (not part of this WMS migration) — excluded.

Every page: navigated (HTTP 200 SPA shell), waited networkidle, checked rendered main
content length, captured console (errors+warnings) filtered against the known-noise
allowlist, screenshotted to `.superpowers/sdd/shots/regression-<name>.png`.

Known pre-existing noise filtered out (not counted as findings): intlify flatten
warnings, Vue Router `next()` deprecation, Vue Router "No match found for location"
transient (dynamic-route registration from menus — fires once per nav then resolves;
page renders correctly), SignalR/notification hub 401/403 negotiate, Element Plus
`el-radio`/small/label deprecations.

| Page (JP) | Route | Rendered? | TextLen | New console err | Screenshot |
|-----------|-------|-----------|---------|-----------------|------------|
| 倉庫マスタ | /wms/warehouse | yes | 1148 | none | regression-warehouse.png |
| ロケーション管理 | /wms/location | yes | 1033 | none | regression-location.png |
| 在庫照会 | /wms/stock | yes | 2701 | none | regression-stock.png |
| 入庫予定 一覧 | /wms/inbound-order-list | yes | 1070 | none | regression-inbound-order-list.png |
| 入庫予定 登録 | /wms/inbound-order | yes | 997 | none | regression-inbound-order.png |
| 入庫実績 入力 | /wms/inbound-receipt | yes | 1071 | none | regression-inbound-receipt.png |
| 出庫指示 一覧 | /wms/outbound-order-list | yes | 2554 | none | regression-outbound-order-list.png |
| 出庫指示 登録 | /wms/outbound-order | yes | 1018 | none | regression-outbound-order.png |
| 製品入庫 | /wms/product-inbound | yes | 1349 | none | regression-product-inbound.png |
| 出荷指示 一覧 | /wms/shipping-order-list | yes | 2554 | none | regression-shipping-order-list.png |
| 出荷指示 登録 | /wms/shipping-order | yes | 1018 | none | regression-shipping-order.png |
| ピッキング作業 | /wms/picking | yes | 933 | none | regression-picking.png |
| 梱包・出荷確定 | /wms/packaging | yes | 1970 | none | regression-packaging.png |
| 棚卸 一覧 | /wms/stock-take-list | yes | 990 | none | regression-stock-take-list.png |
| 棚卸 作業 | /wms/stock-take | yes | 1086 | none | regression-stock-take.png |
| WMSダッシュボード | /wms/dashboard | yes | 1428 | none | regression-dashboard.png |
| 材料欠品管理 | /wms/material-shortage | yes | 986 | none | regression-material-shortage.png |
| 出庫ルーティング | /wms/outbound-routing | yes | 1109 | none | regression-outbound-routing.png |
| 入荷検品(QC) | /wms/inspection | yes | 1181 | none | regression-inspection.png |
| スロッティング最適化 | /wms/slotting | yes | 1203 | none | regression-slotting.png |
| 補充指示 | /wms/replenish | yes | 1196 | none | regression-replenish.png |
| クロスドッキング | /wms/cross-dock | yes | 1221 | none | regression-cross-dock.png |
| キッティング・組立 | /wms/kit | yes | 1212 | none | regression-kit.png |
| 返品管理(RMA) | /wms/rma | yes | 1121 | none | regression-rma.png |
| ロット追溯・回収 | /wms/lot-trace | yes | 1036 | none | regression-lot-trace.png |
| 賞味期限管理(FEFO) | /wms/expiry | yes | 1119 | none | regression-expiry.png |
| 原紙ロール管理 | /wms/paper-roll | yes | 1263 | none | regression-paper-roll.png |
| 残材・端材管理 | /wms/remnant | yes | 1237 | none | regression-remnant.png |
| 印版・木型倉庫 | /wms/plate-mold-stock | yes | 1146 | none | regression-plate-mold-stock.png |
| インキ・接着剤管理 | /wms/ink-lot | yes | 1204 | none | regression-ink-lot.png |
| パレット管理 | /wms/pallet | yes | 1192 | none | regression-pallet.png |
| 客先預り在庫(VMI) | /wms/vmi | yes | 1069 | none | regression-vmi.png |
| 試作・サンプル在庫 | /wms/sample-stock | yes | 1210 | none | regression-sample-stock.png |
| モバイル作業指示 | /wms/mobile-task | yes | 1020 | none | regression-mobile-task.png |
| WCS/自動倉庫連携 | /wms/wcs-task | yes | 1751 | none | regression-wcs-task.png |
| 配送業者連携 | /wms/carrier | yes | 1090 | none | regression-carrier.png |
| IoT温湿度モニタ | /wms/iot-monitor | yes | 1519 | none | regression-iot-monitor.png |
| 帳票センター | /wms/report-center | yes | 902 | none | regression-report-center.png |
| 連携ヘルス監視 | /wms/bridge-health | yes | 1160 | none | regression-bridge-health.png |
| 在庫滞留レポート | /wms/stock-dwell | yes | 1550 | none* | regression-stock-dwell.png |

`none*` = stock-dwell surfaced only the known `[el-radio] label→value` Element Plus
deprecation warning (in the pre-existing noise allowlist); the stack-trace lines
initially tripped the keyword filter but the underlying message is benign. No real error.

**40 / 40 pages rendered, 40 / 40 clean.** No blank/white screens; no JS render crashes.
Dashboard (KPI cards + realtime-event panel + trend chart + warehouse-value table) and
IoT monitor (alert banner + 3-sensor table) visually verified via screenshot.

---

## 3. Spot Interaction Checks (QA data, no mutations)

| Check | Page | Result |
|-------|------|--------|
| Query + pager | 出庫指示一覧 /wms/outbound-order-list | Total **37**, 20/page; 検索 executes, next-page changes rows; no errors |
| One-sided date query | 入庫予定一覧 /wms/inbound-order-list | Set only arrivalFrom=2020-01-01 (展开更多), 検索 → Total **1**, 1 row, no errors |
| Edit dialog open/cancel | 倉庫マスタ /wms/warehouse | 編集 opens "倉庫編集" dialog (7 fields); キャンセル closes cleanly; no errors |
| Server pager | 在庫照会 /wms/stock | pager present Total **21**; btn-next changes row set (server-side page); no errors |
| List renders | WCS/自動倉庫連携 /wms/wcs-task | **5 rows** in table |
| CrossDock 単号 | クロスドッキング /wms/cross-dock | first XD number = **XD2026070001** (matches expected) |

All 6 pass. No create/execute/submit flows were triggered (read-only pass).

---

## 4. Console Audit Summary

Zero NEW error types on any WMS page. Every console entry seen falls inside the
pre-existing noise allowlist (intlify, router next()/no-match transient,
SignalR/notification hub 401/403, Element Plus deprecations). No page-specific
TypeError, undefined-access, failed component chunk-load, or render exception.

## 5. Findings

**None.** Module is regression-clean and ready for PR.

---

Artifacts:
- Screenshots: `C:\CP6\.superpowers\sdd\shots\regression-*.png` (40 pages + regression-warehouse-editdialog.png)
- Raw walk log: `C:\CP6\.superpowers\sdd\walk-results.txt`
