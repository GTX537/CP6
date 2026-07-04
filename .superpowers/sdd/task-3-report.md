# Task 3 报告：Dashboard 重做

分支：`feat/ui-restyle`　Commit：`202bf3f`

## 1. 保留的业务逻辑清单（逐项核对）

从旧 `DashboardView.vue`（~28KB）通读并核对，以下全部原样保留：

**API 调用**
- `dashboardApi.getSummary()` → `summary` / `recentOrders` / `workOrderStatus`
- `orderApi.searchUnshipped(...)` → Phase 8 受注済未出荷 widget（`unshippedRows/unshippedTotal`）
- `stockDwellApi.summary(...)` → T13 90天以上滞留库存 widget（`stockDwellSummary`）
- `startConnection()/getConnection()`（SignalR）→ `BusinessNotification` / `NewOperLog` 订阅，`onUnmounted` 里的 `conn.off(...)` 清理

**数据 ref / computed**
- `summary`, `recentOrders`, `workOrderStatus`, `latestNotice`, `feed`, `dashboardReady`
- `statCards`（8 项 KPI 全部保留，未删减）, `quickLinks`/`allQuickLinks`
- `unshippedRows/unshippedTotal/unshippedLoading/onlyOverdue/unshippedQuery`
- `stockDwellSummary/stockDwellLoading/stockDwellRows/maxStockDwellOver90`

**路由跳转**：`canGo/onCardClick/go/goOrderDetail`，`/order`、各 KPI 的 `to`、`quickLinks` 的 `path` 全部不变。

**i18n key**：未新增任何 key。新视觉层复用的 key：`dashboard.title`、`dashboard.stockWarnings`、`dashboard.pendingApprovals`、`dashboard.qOrder`、`dashboard.shipStatus`、`dashboard.ship0/9`、`dashboard.workOrderStatus`、`dashboard.wo0~9`、`dashboard.noData`、`dashboard.waitingFeed` 等，均已在 `deploy/seed-data/sys_lang.json` 中确认存在中/日文案。

**格式化辅助函数**：`fmtQty/fmtTime/fmtDate/todayString/shipLabel/shipColor/woLabel/orderLifecycleColor/stockDwellPercent/revealDashboard/loadData/loadUnshipped/loadStockDwell` 全部原样保留（仅 `alertType` 改名重写为 `alertBarStyle`，见下）。

## 2. 重写前后结构对照

| 区块 | 旧实现 | 新实现 |
|---|---|---|
| 顶部通知 | 独立 `el-alert` 横幅（页面最上方） | 移入右列，改为渐变 `alertbar`（按 level 变色），逻辑不变（`latestNotice`/`@close`） |
| KPI 8 卡 | `el-row/el-col` + `el-card` `stat-card` | `KpiCard.vue` 新组件，`.kpis{grid-template-columns:repeat(4,1fr)}`，8 卡两行铺满，未删减任何指标 |
| 制造ステータス | `el-progress` 逐行横条 | `DonutChart.vue` 环图 + 图例（segments 来自 `workOrderStatus`） |
| 快捷入口 | `.quick-grid/.quick-item` | 沿用同一份 `allQuickLinks` 数据，改用 mockup 的 `.quick/.q` 类名 |
| 最近受注/未出荷/滞留库存 | `el-card` + `el-table` | 改为纯 div `.card/.card-head`，`el-table` 本体不变（表头样式来自 Task 1 全局 override，未新增表格 CSS） |
| 新增：page-head | 无 | 标题 `dashboard.title` + 日期（`Intl`/`toLocaleDateString`，非 i18n key）+ 预警摘要（复用 `stockWarnings`/`pendingApprovals` 真实计数与既有 key 拼接）+ 一个真实按钮（`qOrder`→`/order`） |
| 新增：出货状态条 | 无对应 UI | 用**已加载的 `recentOrders` 样本**统计 `shipStatus===9` 占比画进度条（非新 API、非编造数据，明确是该表格样本的出货占比，不是"今日全量"口径） |

## 3. 关于 mockup 与"不造假数据"的取舍

- mockup 的 KPI `trend` sparkline 和"较上周+2""目标120"等文案：现有 API 无 7 日趋势/目标值数据源，**未传 `trend`，未编造任何 sub 文案**，仅用 `clickable` 生成一个纯装饰的 `→` 提示（复用原有 `canGo` 布尔值，无新文案）。
- mockup 的"导出日报"按钮：无对应功能/API，**未实现**，只保留了有真实路由的"受注入力"按钮。
- 出货状态条数值来自 `recentOrders`（已加载、非新请求），不是编造数字。

## 4. 新组件

- `cp6.web/src/views/dashboard/components/KpiCard.vue`：props `label/value/suffix/tone/trend/sub/clickable`，tone→chip 配色 4 种（brand/info/warn/danger），trend 可选 sparkline，clickable 追加 `→`。
- `cp6.web/src/views/dashboard/components/DonutChart.vue`：props `segments/centerLabel`，`r=46, C=2πr≈289.03`，每段 `dasharray = rawLen-3 / C`、`dashoffset = -累计rawLen`（清空隙 3px），空数据渲染灰色整环 + `#empty` 插槽。

## 5. 验证输出摘要

```
> npm run type-check
> vue-tsc --build
（无输出，0 错误）

> npm run build
✓ built in 7.39s
（仅有既存的 chunk-size 警告，与本次改动无关）
```

真实登录 + gstack browse 截图对照（`admin/123456`，dashboard 路由）：
- 桌面 1600px：page-head/8×KPI/双列(出货状态条+最近受注+快捷入口 | 制造ステータス环图+实时通知)/受注済未出荷(21条+分页)/90天滞留库存(空态) 全部渲染正常，视觉上与 `picture/mockup-final-a-dashboard.html` 的配色、圆角、卡片阴影、环图结构高度一致。
- 手机 390px：KPI 网格回落 2 列，最近受注表格自动切换为 `simple-list`，快捷入口回落 3 列，无横向溢出。
- `console --errors`：仅有 1 个**既存**（非本次改动引入）的警告 —— `NotificationBell.vue` 里 `ElBadge` prop 类型不匹配导致头部出现 `[object Object]` 红点（`git log` 确认来自更早的 `447f3b0` 提交，与 dashboard 无关，未在本任务范围内修复）；以及既存的 SignalR CSRF negotiate 403（本地 dev 环境已知问题，非本次引入）。无任何来自 `KpiCard`/`DonutChart`/`DashboardView` 新代码的报错或 Vue 警告。

## 6. 自审发现

- `chipStyle`/`toneVar` 最初用 `Record<string,...>` 导致 `noUncheckedIndexedAccess` 下 vue-tsc 报 `Object is possibly 'undefined'`；改为 `Record<Tone,...>`（Tone 为 4 值联合类型）后消除。
- 图标标签（`Van/List/Grid/Warning/SetUp/Bell/DocumentAdd`）改为显式从 `@element-plus/icons-vue` 导入并作为字面量标签使用，与仓库既有约定一致（其余视图均是"作为标签用就显式 import，仅动态 `:is="字符串"` 才依赖全局注册」），避免潜在的 volar 类型检查空洞。
- 未改动 `.el-table` 相关 CSS（复用 Task 1 `element-overrides.css` 的全局表头样式），符合"表格只调整容器 class"的要求。

## Concerns

- `picture/demo4-soft-saas.html`、`picture/files/`、`picture/preview.webp` 是会话开始前就存在的未跟踪文件（非本任务产出），未纳入本次 commit。
- 头部 `[object Object]` 红点是既存 bug（`NotificationBell.vue`），与本任务无关，未处理，建议单独立票。

---

# Task 3 修复执行报告（2026-07-04）

分支：`feat/ui-restyle`　Commit：`4867be8`

## 修复 1：KpiCard.vue chip 背景 token 化（Important）

**改动内容**：第 18 行 `chipStyle` Record 中 brand 档的背景值从硬编码 `'rgba(20,184,196,.10)'` 改为 `'var(--cp-brand-bg)'`，其他三档（info/warn/danger）保持不变。

**文件**：`cp6.web/src/views/dashboard/components/KpiCard.vue`

**验证**：✓ type-check 0 错误，✓ build 0 错误

## 修复 2：DashboardView.vue 出货状态条改为三段（Important）

**改动内容**：
- 修改 `shipSummary` computed（第431行）从 2 值（shipped/pending）改为 4 值（shipped/partial/unshipped 数量 + shippedPct/partialPct 百分比）
- 修改 template `.ship` 区块（第45行）从 2 个 block 改为 3 个 block：
  - 已出货(9)：数值显示，色值 `var(--cp-brand)`，label `t('dashboard.ship9')`
  - 部分出货(5)：数值显示，色值 `#7CE3DD`（图表系列色，字面量豁免），label `t('dashboard.ship5')`（已在 sys_lang.json 确认存在）
  - 未出货(0)：数值显示，色值 `var(--cp-line-soft)`，label `t('dashboard.ship0')`
- 进度条由 2 段改为 3 段，颜色对应上述三档

**文件**：`cp6.web/src/views/dashboard/DashboardView.vue`

**前置验证**：✓ grep 确认 `dashboard.ship5` 在 `deploy/seed-data/sys_lang.json` 中存在，翻译为"部分出货"

**验证**：✓ type-check 0 错误，✓ build 0 错误

## 修复 3：KpiCard.vue sparkline 添加面积填充（Minor）

**改动内容**：
- 新增 `areaPath()` 函数（第36行）：将 polyline points 转换为闭合的 SVG path，格式 `M x y L x y ... V30 H0 Z`，用于生成面积填充路径
- 在 template 的 SVG 中（第54行）polyline 前添加一个面积填充 `<path>`，fill 值根据 tone 类型从字典取值：
  - brand: `rgba(20,184,196,.10)`
  - info: `rgba(78,128,238,.10)`
  - warn: `rgba(240,148,10,.10)`
  - danger: `rgba(229,72,77,.08)`

**文件**：`cp6.web/src/views/dashboard/components/KpiCard.vue`

**参考**：参照 `picture/mockup-final-a-dashboard.html` 第260-272行的 spark path 写法

**验证**：✓ type-check 0 错误，✓ build 0 错误

## 修复 4：检查并清理 color 字段（Minor）

**改动内容**：
- grep 检查 `statCards` 和 `quickLinks` 数据的 `color` 字段是否被使用
- 发现 `kpiTone()` 函数（第396-397行）仍在使用 `card.color` 来确定 KPI 卡的视觉基调（tone 映射）
- **结论**：保留 color 字段，未删除

**文件**：`cp6.web/src/views/dashboard/DashboardView.vue`

**依据**：```
396:  if (card.color === '#e6a23c') return 'warn'
397:  if (card.color === '#409eff') return 'info'
```
color 字段在 `kpiTone()` 函数中仍被读取用于 tone 映射，删除会导致 KPI 卡无法正确显示色调。

## 验证摘要

```
$ npm run type-check
> vue-tsc --build
（0 错误）

$ npm run build
✓ 2561 modules transformed
✓ built in ~7.39s
（0 errors）
```

两项验证全部通过，无 build 错误或 type-check 错误。
