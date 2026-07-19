# GR-VP Task 6 报告：PUR / PLAN / PUB v-permission

**Status: DONE** — code commits `4bb7512` + `cf20d42` on `feat/gr-vp-t6`。

## 交付规模

- 扫描 12 个目标视图，11 个视图产生权限改动；`PurReconcileView` 只有查询/只读动作。
- 34 个直接 `v-permission` 声明 + `SeqView` 3 个权限 props，共 37 个页面级声明。
- 覆盖 33 个唯一后端写权限键；静态 oracle 对比为 missing=0、extra=0。
- `VolTable` 增加 3 个可选权限 props，并以 DOM 指令、响应式条件渲染及事件守卫覆盖 9 个桌面/移动端 CRUD 入口；未传 props 时保持既有 fail-open 行为。

## 视图映射

| 视图 | 权限声明数 | 权限键 |
|---|---:|---|
| GoodsReceiptView | 2 | `pur-gr:add`, `pur-gr:qc` |
| PrView | 5 | `pur-pr:add`, `pur-pr:submit`, `pur-pr:convert`（列表/详情复用） |
| PurchaseOrderView | 3 | `pur-po:add`, `pur-po:submit`, `pur-po:cancel` |
| PurReconcileView | 0 | 仅查询/只读 |
| RfqView | 7 | `pur-rfq:add/invite/quote/rank/select/writeback/convert` |
| SubcontractView | 4 | `pur-subcontract:consign/issue/cost`（issue 两入口） |
| SupplierPriceView | 2 | `pur-supplier-price:add/delete` |
| ThreeWayMatchView | 3 | `pur-match:add/release/reject` |
| ItemPolicyView | 3 | `plan-item-policy:add/delete`（新增与编辑共用 add 端点权限） |
| MrpBoardView | 4 | `plan-mrp:run/confirm/convert/ignore` |
| CodeGenView | 1 | `pub-codegen:save` |
| SeqView | 3 props | `pub-seq:add/edit/delete` |

## 豁免与适配说明

- `pur-subcontract:view`：后端 POST 读取工作台详情，属于纯读，不贴指令。
- `pub-codegen:view`：后端 POST 读取表元数据，属于纯读，不贴指令。
- 刷新、查询、预览、详情展开和对话框内已被入口守住的确认按钮不重复贴点。
- `SeqView` 没有独立 CRUD 按钮，动作由 `VolTable` 生成。为避免把 `pub-seq:*` 硬编码到所有通用表格页面，采用可选 props，并额外保护手机卡片的隐式编辑入口。

## 审查中修复

- 受限用户在手机端没有 edit/delete 权限时，原通用卡片仍显示空“更多”菜单；新增 `hasDefaultMobileActions` 后，仅在存在默认动作或扩展 slot 时渲染菜单。
- 手机卡片点击原本会隐式打开编辑框；现在按 `editPermission` 进行同语义守权。
- 独立覆盖审计发现生产启动先 `app.mount()`、后异步 `loadMyActions()`；原指令只在 `mounted` 判定一次，首屏无权按钮可能持续保留。`cf20d42` 让指令在 `loaded: false → true` 时重判一次并立即注销监听。
- Element Plus 下拉菜单项不是稳定的单根 DOM，组件级指令不能保证生效；移动 edit/delete/select 项改为响应式条件渲染，命令处理器同步二次守权。
- `VolTable.permission.spec.ts` 扩展为 6 个回归用例，覆盖移动端 add-only/full、异步加载重判、真实指令 DOM 移除、桌面 edit-only、未传权限 props 的兼容行为。

## 验证

- 后端 oracle：`PurPermissionAttributeTests` + `PlanPubPermissionAttributeTests`，11/11 passed。
- `npx vue-tsc --noEmit`：通过，0 error。
- `npx vitest run`：72 files / 487 tests passed。
- `npm run build`：通过，Vite 8.0.6，2652 modules；仅既有 >500 kB chunk warning。
- 新增聚焦测试：6/6 passed。
- `git diff --check`：通过。
- 真实 Chrome 首轮权限矩阵：受限 RFQ 仅保留“比价排名”；admin 保留 6 个写按钮；受限 Seq 保留新增且不可编辑，admin 可编辑；console error 0。审查发现的空菜单随后由持久回归测试覆盖。
- 正式 pre-landing review 初审 0 blockers；ship 覆盖审计随后发现 1 个异步权限加载 blocker，已由 `cf20d42` 修复并纳入持久回归；最终独立复核为 0 blocker / 0 important。无 SQL、数据迁移、后端权限语义或 API 变更。

## 并行工作隔离

`cp6.web/src/router/index.ts` 与未跟踪的 `MenuDesignVariantsView.vue` 属于另一项菜单设计工作；`.claude/settings.local.json` 属于用户本地配置。三者均未纳入 T6 白名单。
