# Task 11 报告：WMS 出庫指示一覧 迁移到 CpListPage（模板首个真实消费者）

页面定位：`cp6.web/src/views/wms/OutboundOrderListView.vue`（菜单 id=407「出庫指示 一覧」/ route `/wms/outbound-order-list`）。同名的 `OutboundOrderView.vue` 是登録页，不在本任务范围。

## Step 1 盘点表 + 保全清单

| 类别 | 原页面项 | 迁移后落点 | 保全 |
|---|---|---|---|
| API | `outboundOrderApi.search(query)` | `fetchList` 包装（ListFetch） | ✅ |
| API | `outboundOrderApi.fromWorkOrder(no)` | 桥接对话框 `onBridgeWo` 原样保留 | ✅ |
| API | `outboundOrderApi.fromOrder(no)` | 桥接对话框 `onBridgeOrder` 原样保留 | ✅ |
| 列 | outboundNo (width180) | ListColumn kind:'mono' | ✅ |
| 列 | 区分 (el-tag info/warning + typeMap) | `col-outboundType` 插槽 + CpTag tone | ✅ |
| 列 | 状态 (el-tag + statusMap + statusTagOf) | `col-status` 插槽 + CpTag tone（statusTone） | ✅ |
| 列 | workOrderNo (width160) | ListColumn text | ✅ |
| 列 | webOrderNo (width160) | ListColumn text | ✅ |
| 列 | customerName (min-width160, tooltip) | ListColumn text（tooltip 缺失=缺口#2） | ⚠️保功能 |
| 列 | warehouseCd (width80) | ListColumn text | ✅ |
| 列 | plannedDate (slice 0,10) | `col-plannedDate` 插槽 | ✅ |
| 列 | 優先度 (priorityMap) | `col-priority` 插槽 | ✅ |
| 列 | 操作 開く | `col-_action` 插槽 → goEdit | ✅ |
| 搜索 | outboundNo (text) | FilterField text | ✅ |
| 搜索 | outboundType (select typeMap) | FilterField select | ✅ |
| 搜索 | status (select statusMap) | FilterField select | ✅ |
| 搜索 | workOrderNo (text) | FilterField text | ✅ |
| 搜索 | webOrderNo (text) | FilterField text | ✅ |
| 搜索 | customerCd (text) | FilterField text | ✅ |
| 头部动作 | 新建 goCreate | PageShell #actions el-button | ✅ |
| 头部动作 | 桥接展开（bridgeDialog） | PageShell #actions + 保留对话框 | ✅ |
| 头部动作 | 查询 | CpFilterBar 查询按钮 | ✅ |
| 行操作 | goEdit（開く） | `col-_action` 插槽 | ✅ |
| 批量操作 | 原页面无 | 无（未臆造；mockup 的批量出库无对应 API） | ✅ N/A |
| 权限指令 | 原页面无 v-permission | 无需保留（grep 确认 0 处） | ✅ N/A |
| i18n | 全部 t() key | 全部沿用（列/搜索标签改 computed 保持响应式） | ✅ |

状态筛选决策：原页面用 status **下拉筛选**（非状态卡），故迁移后保留为 select searchField，**未**改用 CpStatusStrip（statusTabs 需 per-status 计数，API 不提供 → 见缺口说明）。这是功能保全优先于视觉对齐 mockup 的显式取舍。

## 重写前后结构

- 前：`el-card(search-form inline)` + `el-card(el-table border stripe)` + `el-dialog`，视觉/布局散在页内 scoped style。
- 后：`CpPageShell(title,#actions)` → `CpListPage(:columns,:fetch,:search-fields + 5 具名列插槽)` → 保留 `el-dialog`（桥接展开，CpListPage 无对应契约）。**scoped style 已完全移除**（0 行），无任何硬编码色值/阴影/圆角。

数据适配：API 返回扁平数组无 total，fetchList 以 `pageSize:500` 取一批、`total=数组长度`、按 page/size 客户端切片（缺口#5）。

## 验证

- `npm run type-check`：通过（vue-tsc --build 无错）。
- `npm run test`：**46 文件 / 280 测试全绿**（未破坏既有）。
- 真栈验收（gstack browse，admin/123456，SPA 内 router 导航以保 CSRF）：
  - 页面加载：Total 37，20/页，2 页；**list 页 console 零 error / 零 warning**（clear 后重进确认）。
  - 查询：出庫指示NO=OUT2026060001 → 1 行 ✅
  - 重置：恢复 37 ✅
  - 翻页：第 2 页 17 行（37-20）、prev 由 disabled→enabled ✅
  - 行操作：開く → 跳 `/wms/outbound-order?no=...` ✅
  - 桥接：自動展開 → 对话框「自動展開」打开 ✅
  - 截图：`.superpowers/sdd/shots/outbound-list-1440.png`、`outbound-list-full.png`、`outbound-bridge-dialog.png`
  - 视觉对照 mockup-final-b：mono 单号品牌色、CpTag（ピッキング=info / 完了=ok绿 / 下書き=muted）、区分 pill、開く 品牌色行按钮、tcard 卡片 — 一致。
  - 注：早期用硬 `goto` 直达 URL 会触发应用级 CSRF 失效跳登录（与本次改动无关），改用 SPA 内 `$router.push` 后正常。

## 模板缺口（与 plan 文件同步）

1. **total 不外露** → CpPageShell `:count` 计数 pill 无法接线；建议 CpListPage `@total-change` / `v-model:total`。（本次省略计数 pill，不 hack。）
2. **ListColumn 缺 minWidth / overflowTooltip** → 客先名长文本无法截断+tooltip；建议加两字段透传 el-table-column。
3. **kind:'tag' 不认码值** → 区分/状态/優先度 数字码需码→文案+码→tone，只能自绘 `col-<prop>`+CpTag；建议 ListColumn 加 `map?:(val,row)=>{label,tone}`。
4. **CpTag 窄列换行**（「ピッキン グ」）→ 建议 `.cp-tag { white-space:nowrap }`（纯样式）。
5. **数据源无 total**（受不改后端约束，记录非模板缺口）→ 客户端分页适配；后端补 WmsPaged 后可服务端分页，模板不动。

模板够用之因：`col-<prop>` 具名插槽是逃生舱，凡 kind 表达不了的列都落插槽保功能，故缺口皆为「省样板/补计数」增强，非阻塞。**未改动任何已评审模板组件。**

## 变更文件

- `cp6.web/src/views/wms/OutboundOrderListView.vue`（重写）
- `docs/superpowers/plans/2026-07-04-ui-restyle.md`（追加「模板缺口（Task 11 试点复盘）」节）

## Self-Review

- 完整性：Step 1 盘点项全部保全（见表格勾选）；批量操作/权限指令原页面本就没有（grep 确认），未臆造。gap 节已写。
- 纪律：view 内零硬编码视觉值、scoped style 清零；仅动目标页 + plan 文件，无越界；已评审模板未改。
- 关注点：(a) 状态改用 select 而非状态卡（功能保全取舍，已说明）；(b) 客户端分页系 API 无 total 的适配，非理想（缺口#5）；(c) PageShell 计数 pill 缺失（缺口#1）；(d) CpTag 窄列换行为小视觉瑕疵（缺口#4）。均记录在案，无一为功能丢失。

## 评审补记（reviewer findings）

- **statusTone 重映射并非「原样沿用」**：受 CpTag 5 色调（ok/warn/danger/info/muted）限制，状态语义有偏移——取消 9: danger→muted、下書き 0: info→muted、確定 1: primary→warn、ピッキング 3: warning→info。此为有意识的调色板映射（acknowledged semantic shift），报告前文「沿用原 statusTagOf 意图」表述不准确，以本条为准。
- **CpFilterBar 按钮 i18n 回归**（Important）：查询/重置/展开更多为组件内硬编码中文（CpFilterBar.vue:111-113），原页面按钮走 t()；ja 默认语言下迁移后这三个按钮只显示中文。已补记为模板缺口 #6，待模板支持 i18n 后修复。
- **两处 undocumented behavior change 已补入缺口 #2**：操作列 `fixed="right"` 丢失（横向滚动时按钮不再钉住）、表格级 `highlight-current-row` 丢失（当前行不再高亮）。
- plan 文件缺口节开头原误提 CpStatusStrip 为本页所用组件，已更正（本页无 statusTabs）。

## 终审修复

Milestone B 全分支终审「批准合并（with fixes）」，以下 7 项在同一 hardening commit 落地。验证：`npm run type-check` 零错误；`npm run test` **46 文件 / 286 测试全绿**（原 280 + 新增 6），输出零 Vue warning。

1. **CpTag 空状态兜底**（`base/CpTag.vue`）：tone 计算改 `props.tone ?? ((props.status && STATUS_TONE[props.status]) || 'muted')`，`??` 拦不住空串这类 falsy-非 nullish 值致漏出裸 `t-`，改 `|| 'muted'` 后 `status=''`/未知状态均落 `t-muted`。
   - 测试：CpTag.spec 新增「empty status → t-muted 且无裸 t-」（`npm run test -- CpTag`，含既有共 5 it 绿）。
2. **CpListPage 错误提示硬化**（`templates/CpListPage.vue`）：catch 由 `(e as Error).message` → `(e as Error)?.message ?? String(e)`，与 CpFormDialog 同一错误契约。
   - 测试：CpListPage.spec 新增「reject 非 Error（字符串）→ ElMessage.error 收 '网络异常'」，镜像 CpFormDialog 断言。
3. **CpFormDialog 防双提交**（`templates/CpFormDialog.vue`）：onConfirm 加 `if (submitting.value) return` 并把 `submitting=true` 提到 validate 之前（覆盖校验在途窗口），全程 try/finally 复位。
   - 测试：CpFormDialog.spec 新增「validate 在途二次 onConfirm → submit 仅 1 次」（call count 1）。
4. **CpFilterBar daterange 占位**（`templates/CpFilterBar.vue`）：daterange 的 el-date-picker 忽略单 `placeholder`，补接 `start-placeholder`/`end-placeholder`（同一串）。
   - 测试：CpFilterBar.spec 新增「daterange 渲染 2 个 `.el-range-input` 且起止占位=field.placeholder」。
5. **CpTag pill nowrap**（`base/CpTag.vue`）：`.cp-tag` 加 `white-space:nowrap`，闭合缺口#4（窄列换行）。
6. **i18n 标签覆盖**：CpFilterBar `labels?:{search,reset,expand,collapse}`；CpFormDialog `labels?:{cancel,confirm}` + `requiredMessage?:(label)=>string`；CpListPage 透传 `filterLabels?`/`emptyText?`（CpEmpty 沿用 `text?`）；试点页 `OutboundOrderListView.vue` 接线 `search→wms.common.search`、`reset→wms.common.clear`（expand/collapse 无 key，留默认，未臆造词条）。各组件文件头注同步更新。
   - 测试：CpFilterBar.spec「自定义 labels 渲染」；CpListPage.spec「emptyText 透传 CpEmpty」。
7. **文档同步**（`docs/superpowers/plans/2026-07-04-ui-restyle.md`）：Task 9 Step 2 错误行改硬化形式+括注；缺口#4 标 ✅ 已修复、#6 更新为「label-override props 已落、试点已接线，剩余=Milestone C 共享词条」；新增缺口#7（共享 `Tone` 类型导出，Milestone C 票）、#8（`kind:'date'` 死词汇，Milestone C 前实现或删除）。

新增测试计数（+6）：CpTag +1、CpListPage +2、CpFilterBar +2、CpFormDialog +1。
