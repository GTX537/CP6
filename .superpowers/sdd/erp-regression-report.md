# ERP Module Regression Gate — Report

- **Branch:** `feat/ui-migrate-erp` (HEAD `03b3c73` — 批次8 QuotationView token化 + 模块硬编码清扫)
- **Repo / frontend:** `C:\CP6` / `cp6.web/`
- **Date:** 2026-07-04
- **Scope:** Final module-level verification for 42 ERP files (8 batches + template extension round 3 lazy/sortable + reset passthrough #22)
- **Result:** PASS — build gate green, 21/21 top-level ERP views walked, 0 new-error findings.

---

## 1. Build Gate (cp6.web/)

| Step | Command | Result |
|------|---------|--------|
| Type-check | `npm run type-check` (vue-tsc --build) | **0 errors** |
| Unit tests | `npm run test` (vitest run) | **316 passed / 316** (46 files) |
| Prod build | `npm run build` | **Succeeded** — built in 7.45s |

**Build warnings:** Only the pre-existing chunk-size advisory ("Some chunks are larger than 500 kB after minification" — `es-*.js` 1.13 MB, `advanced-*.js` 590 kB). No NEW warnings introduced by the ERP migration.

---

## 2. Full-Menu Walkthrough

- Dev server on `:5173` verified healthy before walk: `POST /api/auth/login` via proxy → **HTTP 200** (not stale; no restart needed).
- Auth: session already authenticated as admin (redirect `/` → `/dashboard`, sidebar rendered). ERP menu visible in sidebar (販売管理(ERP)).
- **Routing note:** Routes are dynamically registered from the admin's `Sys_Menu` after login. 15 ERP paths are menu-registered and directly URL-navigable. 5 `/erp/*` paths (credit-note, backorder, otd-report, fx-rate, order-trace) are in the component map but NOT in the admin menu, so a cold direct-URL load yields a blank router-view (no match). These are reached in-app via row/button actions (e.g. OrderList row → Trace → order-trace). To verify their migrated templates still mount, they were registered in-page against their Vite dev module URLs and navigated via SPA push. **This is a pre-existing menu-config characteristic — the UI migration did not touch routing.**

Per-page results (console = after filtering known noise: intlify, vue-router No-match/next(), SignalR/CSRF-403, hub 401/403, EP deprecations):

| # | Page | Route | Rendered | Console | Screenshot |
|---|------|-------|----------|---------|------------|
| 1 | EstimateCalcList | /estimate-calc-list | yes (filter+table) | clean | erp-regression-estimate-calc-list.png |
| 2 | EstimateCalcView (3-step wizard) | /estimate-calc | yes (基本情報/工程情報/詳細) | clean | erp-regression-estimate-calc.png |
| 3 | QuotationList | /quotation-list | yes | clean | erp-regression-quotation-list.png |
| 4 | QuotationView (6-section wizard) | /quotation | yes (①ヘッダー…⑥メモ) | clean | erp-regression-quotation.png |
| 5 | ProductMasterList | /product-list | yes | clean | erp-regression-product-list.png |
| 6 | ProductMasterView (5-step wizard) | /product | yes (部材/基本/工程/材料/ロット単価) | clean | erp-regression-product.png |
| 7 | OrderList | /order-list | yes (lazy empty → 24 on 検索) | clean | erp-regression-order-list.png |
| 8 | OrderEntryView (3-step wizard) | /order | yes (基本情報・受注明細 …) | clean | erp-regression-order-entry.png |
| 9 | OrderPriceCorrection | /order-price-correction | yes (単価訂正 filter) | clean | erp-regression-order-price-correction.png |
| 10 | OrderTrace | /erp/order-trace (in-app) | yes (Order trace / Bridge hook timeline) | clean | erp-regression-order-trace.png |
| 11 | CreditNoteList | /erp/credit-note (in-app) | yes (クレジットノート) | clean* | erp-regression-credit-note.png |
| 12 | BackorderList | /erp/backorder (in-app) | yes (Backorder queue, Total 21) | clean | erp-regression-backorder.png |
| 13 | OtdReport | /erp/otd-report (in-app) | yes (On-time delivery report) | EP el-radio deprecation (known noise) | erp-regression-otd-report.png |
| 14 | FxRate | /erp/fx-rate (in-app) | yes (為替レート管理) | clean | erp-regression-fx-rate.png |
| 15 | BusinessPartnerList | /business-partner-list | yes | clean | erp-regression-business-partner-list.png |
| 16 | BusinessPartnerView (10 role tabs) | /business-partner | yes (form + 10 tabs) | clean | erp-regression-business-partner.png / -tabs.png |
| 17 | FscChecklist | /fsc-checklist | yes (FSC filter) | clean | erp-regression-fsc-checklist.png |
| 18 | SheetUnitPrice | /sheet-unit-price | yes (シート単価) | clean | erp-regression-sheet-unit-price.png |
| 19 | PlateMoldList | /plate-mold-list | yes (版型/木型 一覧) | clean | erp-regression-plate-mold-list.png |
| 20 | PlateMoldView (wizard) | /plate-mold | yes (基本/構成/添付/必要物) | clean | erp-regression-plate-mold.png |
| 21 | OrderCancelDialog | dialog from OrderList row 取消 | yes (受注取消 — WO…) | clean | erp-regression-order-cancel-dialog.png |

*CreditNote: type filter renders literal i18n key `erp.creditNote.type.undefined` — pre-existing (see Findings, not a console error / not migration-related).

**No white screens. No NEW console error types on any page.**

---

## 3. Spot Interaction Checks (no business-data mutations)

| Check | Result |
|-------|--------|
| OrderList — lazy (no auto-load) | PASS — initial Total 0 / empty; 検索 → 24 rows loaded |
| OrderList — 検索 | PASS |
| OrderList — sort | PASS — column gains `descending` class |
| OrderList — checkbox 預り売上 クリア联动 | PASS — check → is-checked true; クリア → false |
| OrderList — CSV button | Visible ("CSV 出力"); not downloaded |
| OrderCancelDialog opens from OrderList | PASS — opens (title 受注取消 — WO20260531000001); NOT submitted |
| QuotationList — sort | PASS — 15 rows, sort class applied |
| QuotationList — status filter | Select control present/renders |
| ProductMasterList — status checkbox クリア | PASS — 未承認/承認待/承認済 check true → クリア false |
| EstimateCalcList — sort | PASS — 20 rows, sort class applied |
| BusinessPartner — edit all 10 tabs | PASS — role FLGs toggled reveal 10 tabs (基本情報/得意先/売掛先/請求先/入金先/納品先/発注先/買掛先/支払予定管理先/支払先); clicked through all 10, no errors |
| CreditNote / Backorder lists load | PASS — both render (Backorder Total 21) |
| FxRate dialog opens | PASS — 新規レート → dialog title 為替レート新規; not submitted |
| CSV button on Order / Product | PASS — both show "CSV 出力"; not downloaded |

---

## 4. Console Audit

No NEW error types attributable to the ERP UI migration on any of the 21 views or during any interaction. Only known/pre-existing noise observed:
- intlify object-flatten warnings (app-wide)
- Vue Router "No match found" + "next() callback deprecated" (app-wide, cold direct-nav; same subsystem as documented known noise)
- Element Plus `el-radio` deprecation on OtdReport (documented EP-deprecation noise)

---

## 5. Findings

**Migration regressions: 0** (no new errors, no white screens, no broken interactions).

Two pre-existing observations (NOT migration-caused, logged for follow-up only):

1. **5 `/erp/*` views not in admin `Sys_Menu`** (credit-note, backorder, otd-report, fx-rate, order-trace) → blank router-view on cold direct-URL navigation. Reachable in-app via row/button actions; all render correctly once routed. Pre-existing routing/menu-data config, untouched by the migration.
2. **CreditNoteList type filter renders literal i18n key `erp.creditNote.type.undefined`** — from `t(\`erp.creditNote.type.${v}\`)` (CreditNoteListView.vue:70) mapping an undefined value with 0 data rows. Cosmetic, pre-existing logic, not a console error, unrelated to token/CpTag migration.

---

## Verdict

**GATE PASS.** Build gate green (type-check 0 / test 316 / build ok). 21/21 ERP views render with no new console errors; all spot interactions behave correctly. Migrated CpTag status pills, CpSectionHeader, filter bars, sortable/lazy list templates, and wizard step shells all render cleanly. Clear to merge.
