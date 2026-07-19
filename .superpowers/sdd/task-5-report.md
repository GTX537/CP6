# GR-VP Task 5 报告：FIN v-permission

**Status: DONE** — code commit `5732057` on `main`，已推送 `origin/main`。

## 交付规模

- 16 个 FIN 视图
- 66 个 `v-permission` 指令目标
- 51 个唯一权限键
- 所有前端键均逐字命中 `CP6.WebApi/Controllers/Fin` 的 `[RequirePermission]`

| 视图 | 指令数 |
|---|---:|
| ApInvoiceView | 3 |
| ArInvoiceView | 4 |
| AssetCardView | 2 |
| AssetCategoryView | 3 |
| AssetDepreciationView | 3 |
| AssetDisposalView | 3 |
| BankImportProfileView | 3 |
| BankReconciliationView | 7 |
| BankStatementView | 5 |
| BudgetEditView | 12 |
| CostSheetView | 2 |
| GlAccountView | 4 |
| JournalEntryView | 5 |
| PaymentView | 4 |
| PeriodCloseView | 2 |
| ReceiptView | 4 |

## 关键映射决定

- 银行对账自动匹配、手工匹配、取消匹配统一使用 `fin-bank-reconciliation:match`；导入、配置管理、锁定/解锁、生成凭证各用后端现有键。
- 银行流水“新建”使用 `fin-bank-reconciliation:view`：这是 `BankStatementController` 当前 POST 贴点的真实语义，前端未擅自改键。
- 预算“复制版本”调用版本 create API，因此使用 `fin-budget:add`；seed 中的 `fin-budget:copy` 当前没有对应前端触发器。
- 预算 M1–M12、控制模式、控制口径会直接调用 upsert，使用 `fin-budget:edit`。独立复审发现直接移除控件会让 view-only 用户丢失数据，已改成“有权可写、无权只读显示”，同时保留指令和 store 未加载时 fail-open 语义。
- 纯读的刷新、查询、预览、排程、期末预检等操作保持不贴点。

## 未使用的后端键

以下 9 个 FIN 后端键当前没有对应前端写触发器，或仅对应读操作，因此未强行铺设：

`fin-ap-payment:tax`、`fin-asset-card:view`、`fin-asset-card:edit`、`fin-asset-category:view`、`fin-asset-deprec:view`、`fin-asset-disposal:view`、`fin-budget:view`、`fin-period:year-close`、`fin-period:reopen-year`。

## 验证

- `npm run type-check`：通过，0 error。
- `npm run test:unit`：71/71 files，481/481 tests passed。
- `npm run build`：通过，2649 modules；仅有既存的 >500 kB chunk warning。
- `git diff --check`：通过；`package.json` 与 lockfile 无变化。
- 权限真值反向扫描：66 个指令、16 个视图、51 个唯一键；所有写 API 入口均已覆盖。

### 真实 Chrome

GStack Windows 包缺少可运行的本机 browse daemon，因此按其 fallback 规程改用项目 Playwright 驱动系统 Chrome。

- 全面抽样：银行流水 denied / view-only / import-only、预算 denied / edit-only、凭证 submit-only 均通过，console error 0。
- 修复后聚焦复测：
  - `BUDGET_VIEW_ONLY`：0 个金额输入、0 个下拉框；M1–M12 金额和控制标签完整可读。
  - `BUDGET_EDIT_ONLY`：12 个金额输入、2 个下拉框；编辑入口正常可用。
- 临时 QA harness 已清理，未进入提交。

## 审查

独立复审先发现 1 个 high：预算草稿 view-only 会因 DOM 移除而显示空白。修复并复测后再次复核，结论为 **0 blockers**；其余权限键、写入口、纯读豁免和 BankStatement 特殊映射均确认正确。
