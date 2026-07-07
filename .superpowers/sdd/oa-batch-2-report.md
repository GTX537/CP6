# OA 迁移批次2 报告（feat/ui-migrate-oa）

电子表单信箱列表族 5 文件：InboxView（信箱外壳）+ InboxPending/InboxRunning/InboxDone/InboxDraft（内嵌子面板）。

## 结论摘要

- **形态判定**：这 5 个文件是「信箱外壳 + 内嵌子面板族」，**非独立路由查询页**。InboxView 是 `el-container` 导航壳（左菜单 + `<component :is>` 动态挂载子面板 + 详情抽屉 + 新建对话框）；四个列表面板挂在 `el-main` 内，经 `@open-detail` 上抛给壳打开抽屉。
- **处置**：统一按 OA 批次1 **FormQuery 先例**「特殊页保留原机制 + 无损基础件替换」——保留 el-table/el-tabs/月份选择器/勾选批量/行点击/行内按钮/编辑对话框等原交互，只做 **token 化（硬编码色清零）+ 基础件替换（`el-tag`→`CpTag`、`el-empty`→`CpEmpty`）**。四同型面板处置一致。
- **为何不套 CpListPage**：四面板为「内嵌子面板」形态（行点击→壳抽屉、内部 tabs/月份筛选、勾选批量、未読行加粗、行内多按钮），CpListPage 单表卡契约不表达（行激活钩子缺口 #16/#21 家族既知、单表 vs 多 tab、无 `:row-class-name` 透传）；且强套后仍须按批次指南 §5 保留原机制。故不强套（与 LocationList master-detail / FormQuery 同逻辑）。
- **验证**：type-check 0 error；test **316/316** 全绿（基线保持）；5 页/面板真栈走查渲染正常、交互可用、无新增组件错误。
- **新增模板缺口 0 项**。

---

## 每页盘点表 + 迁移摘要

### 1. InboxView.vue（/oa/inbox，菜单733）— 信箱导航壳

| 盘点项 | 迁移前 | 迁移后 |
|---|---|---|
| API | inboxApi.stats / delegateApi.myGrants / flowAdminApi.list | 不变 |
| 结构 | el-header(标题+act-as下拉+新建) + el-aside(el-menu 6 项) + el-main(`<component :is>`) + el-drawer(FormDetail) + el-dialog(flow 列表) | 不变（导航壳，不套模板） |
| 子面板路由 | folder ref + compMap + onSelect（flow-admin→router.push） | 不变 |
| 行操作 | 新建对话框 flow 表格状态 `el-tag :type=success/info` | **`CpTag :tone=ok/muted`** |
| 空态/基础件 | — | — |
| v-permission | 无 | — |
| i18n | oa.inbox.*（title/newBtn/dashboard/pending/running/done/draft/flowAdmin/close/detailTitle）| 全保留 |
| 硬编码色 | `#f5f7fa`(app bg) / `#fff`(header+aside) / `#303133`(title) / `--el-border-color-light`×3 / `--el-text-color-placeholder` | `--cp-bg` / `--cp-card`(×2) / `--cp-ink` / `--cp-line-soft`×3 / `--cp-faint` |
| 形态 | 导航壳特殊页 → token 化 + 基础件替换 | ✅ |

真栈证据：appBg=rgb(242,250,251)=`#F2FAFB`(--cp-bg)、headerBg=`#FFFFFF`(--cp-card)、title=rgb(16,52,60)=`#10343C`(--cp-ink)、note=rgb(194,210,215)=`#C2D2D7`(--cp-faint)；新建对话框 CpTag「启用」渲染。

### 2. InboxPending.vue — 待办（審査/CC 双 tab）

| 盘点项 | 迁移前 | 迁移后 |
|---|---|---|
| API | inboxApi.pending / pendingCc / markTaskRead / markCcRead / batch | 不变 |
| 列（review） | selection / flowName / starterName / sentAt(formatTime) | 不变 |
| 列（cc） | flowName / starterName / atNodeId / createDate(formatTime) | 不变 |
| 搜索 | 无 | 无 |
| 批量 | 勾选→批量条（批准/退回 + 批注输入） | 不变（`.batch-bar` token 化） |
| 行操作 | 行点击→markRead + open-detail（review）/ markCcRead（cc）；未読行 `:row-class-name` 加粗 | 不变 |
| 计数/空态 | `el-tag`(×2 共 N 条) / `el-empty`(×2) | **`CpTag`(×2) / `CpEmpty`(×2)** |
| i18n | oa.pending.*（toReview/cc/selected/commentHint/approve/reject/empty/ccEmpty）+ oa.col.* + `共 {n} 条` | 全保留 |
| 硬编码色 | `.batch-bar`：`--el-color-primary-light-9`(bg)/`--el-color-primary-light-7`(border)/`4px`(radius)；`.batch-info`：`--el-text-color-regular` | `--cp-brand-bg` / `color-mix(--cp-brand 24%)` / `--cp-r-sm` / `--cp-text`（`:deep(.row-unread td)` 纯 font-weight 保留） |
| 形态 | 双 tab + 批量 + 未読样式 + 行点击 → 特殊页保留原机制 | ✅ |

真栈证据：审査 tab 计数 CpTag「全 1 件」、CC「全 0 件」；勾选行→`.batch-bar` 可见、bg=rgba(20,184,196,0.08)=`--cp-brand-bg`。

### 3. InboxRunning.vue — 在途（单表）

| 盘点项 | 迁移前 | 迁移后 |
|---|---|---|
| API | inboxApi.running | 不变 |
| 列 | flowName / currentNode / handlers(join'、') / **status(el-tag)** / createDate | 不变（列结构） |
| 状态色 | `el-tag :type=instanceStatusType`（warning/success/danger/info/info） | **`CpTag :tone=instanceStatusTone`**（本地 helper `['warn','ok','danger','info','info']`，与 FormQuery 同法对齐 inboxModel） |
| 行操作 | 行点击→open-detail | 不变 |
| 计数/空态 | `el-tag`(共 N 条) / `el-empty` | **`CpTag` / `CpEmpty`** |
| import | `instanceStatusType, instanceStatusText` | 移除已无用的 `instanceStatusType`，保留 `instanceStatusText` |
| i18n | oa.col.* + oa.running.empty + oa.inst.* + `共 {n} 条` | 全保留 |
| 硬编码色 | 无（scoped 仅布局） | 无 |
| 形态 | 单表 + 状态列 + 行点击 → 特殊页保留原机制 | ✅ |

真栈证据：计数「全 1 件」、状态 CpTag class=`cp-tag t-warn`（status 0=進行中，对齐原 warning）；行点击→详情抽屉可见。

### 4. InboxDone.vue — 已办（月份 + 三 tab）

| 盘点项 | 迁移前 | 迁移后 |
|---|---|---|
| API | inboxApi.done({year,month,tab}) | 不变 |
| 控件 | el-date-picker(月) + el-tabs(自分/全件/CC) | 不变 |
| 列 | flowName / starterName / **status(el-tag)** / doneAt | 不变 |
| 状态色 | 本地 `formToTagType`→`['warning','success','danger','info'...]` | **`formToStatusTone` 返回 Tone**`['warn','ok','danger','info'...]`（文案仍走 `formToStatusText`） |
| 行操作 | 行点击→open-detail | 不变 |
| 计数/空态 | `el-tag`(共 N 条) / `el-empty` | **`CpTag` / `CpEmpty`** |
| i18n | oa.done.*（allMonths/mine/all/cc/empty）+ oa.col.* + oa.formto.* + `共 {n} 条` | 全保留 |
| 硬编码色 | 无（scoped 仅布局） | 无 |
| 形态 | 月份+三 tab+单表+行点击 → 特殊页保留原机制 | ✅ |

真栈证据：月选择器「全月」=2026-07、三 tab（自分が処理したもの/全件/CC）、切「全件」→重载 count「全 0 件」、CpEmpty 可见。

### 5. InboxDraft.vue — 草稿（单表 + 行内按钮）

| 盘点项 | 迁移前 | 迁移后 |
|---|---|---|
| API | draftApi.list / update / submit / remove | 不变 |
| 列 | flowName / createDate / actions(编集/提出/削除 行内按钮) | 不变 |
| 行操作 | openEdit(对话框) / submitDraft(row._submitting) / removeDraft(确认) | 不变 |
| 编辑弹窗 | el-dialog + textarea(varsJson) | 不变 |
| 计数/空态 | `el-tag`(共 N 条) / `el-empty` | **`CpTag` / `CpEmpty`** |
| i18n | oa.draft.*（edit/submit/delete/editTitle/varsHint/cancel/save/empty）+ oa.col.* + `共 {n} 条` | 全保留（原 ElMessage 硬编码「已保存/已提交/已删除」及确认框文案系迁移前既有，未改） |
| 硬编码色 | 无（scoped 仅布局） | 无 |
| 形态 | 单表 + 行内多按钮 + 编辑弹窗 → 特殊页保留原机制 | ✅ |

真栈证据：Draft 面板挂载，count「全 0 件」，`.inbox-draft .cp-empty` 可见。

---

## 验证证据

- **type-check**：`vue-tsc --build`（NODE_OPTIONS=--max-old-space-size=4096）→ **0 error**。
- **test**：`vitest run` → Test Files 46 passed，Tests **316 passed**（基线 316，无下降）。
- **真栈走查**（dev 5173，代理→9991，login 200，admin 已登录会话，gstack browse）：

| 面板 | 入口 | 结果 | 截图 |
|---|---|---|---|
| InboxView 壳 | /oa/inbox（菜单733） | 左菜单 6 项导航、壳 token 化验证（bg/header/title/note 全命中 cp token）、新建对话框 CpTag「启用」 | shots/oa-inbox-dashboard.png |
| Pending | 未処理菜单项 | 審査 tab 1 行、CpTag 计数「全 1 件」、勾选→token 化 batch-bar 可见、CC tab | shots/oa-inbox-pending.png |
| Running | 進行中菜单项 | 1 行、CpTag 计数 + 状态 t-warn「進行中」、行点击→详情抽屉 | shots/oa-inbox-running.png |
| Done | 処理済菜单项 | 月选择器 + 三 tab + refresh 均可用、空→CpEmpty | shots/oa-inbox-done.png |
| Draft | 下書き菜单项 | 空→CpEmpty、行内按钮/编辑弹窗结构保留 | shots/oa-inbox-draft.png |

- **console**：无本批引入的新错误。既有告警/错误均迁移前既有、与本批无关：`[intlify] Ignore object flatten` 警告、`Vue Router` next() 弃用警告、`el-pagination/el-*` `small is about to be deprecated` 弃用告警（原 `size="small"` 既有）、SignalR **CSRF 403**（测试环境基础设施既有问题，多批次报告已记录）。

## 新增模板缺口

**0 项**。四面板均落在既有处置（FormQuery「特殊页保留原机制」先例 + 行激活钩子缺口 #16/#21 家族既知 + 单表 vs 多 tab / `:row-class-name` 无透传），未触发新的模板扩展需求。已在 `docs/superpowers/plans/2026-07-04-ui-restyle.md` 追加「OA 迁移批次2 复盘（本批无新增编号）」。

## Concerns

- （非本批引入）四面板的计数徽标为语言词条 `共 {n} 条`（ja 显示「全 N 件」），转 `CpTag`（muted 灰调、带前导圆点）——非状态语义但取 DS 唯一 pill 基元；壳无 CpPageShell 故无 `:count` 计数 pill 可用，此为既定基础件替换取舍。
- （非本批引入）InboxDraft 的 `ElMessage.success('已保存/已提交/已删除')` 与 `ElMessageBox` 确认文案为硬编码中文（未走 t()），迁移前既有，属 i18n 词条缺口，本批不改 i18n 机制未动。
- （非本批引入）SignalR CSRF negotiate 403 为测试环境基础设施既有问题。
