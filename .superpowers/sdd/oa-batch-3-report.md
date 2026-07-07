# OA 迁移批次 3 报告（信箱详情/仪表盘/对话框族）

分支 `feat/ui-migrate-oa`，HEAD 起点 5495c3c（批次 1/2）。样板对照 `views/wms/BridgeHealthView.vue`（监控·仪表盘特殊页处置先例）。

本批 5 文件，全部落在既有契约：**Dashboard/FormDetail/FlowTimeline = 特殊页 token 化 + 无损基础件替换**；**SendBack/Transfer = working form dialog，审计后判定既已合规（无硬编码色 / 无 el-tag / 保留 el-dialog）**。与 OA 批次 1(FormQuery)、批次 2(信箱四面板)、ERP 批次 3(OrderCancelDialog) 同处置逻辑。

---

## 1. InboxDashboard.vue（信箱仪表盘）

**迁移前盘点**
- API：`inboxApi.stats()`（InboxStats：pendingCount/runningCount/doneThisMonth/rejectedBackToMe/trend[]/recentPending[]）。
- 结构：4 KPI 卡（el-statistic）+ 7 日趋势柱（自绘，无图表库）+ 最近待办 el-table（3 列）。
- 行操作：recent 行点击 `@row-click` → `emit('open-detail', row.instanceId)`。
- i18n：`oa.dashboard.{pending,running,doneThisMonth,rejected,trend,noTrend,recentPending,noPending}`、`oa.col.{flowName,starter,sentAt}`——全部保留。
- 无搜索/批量/权限指令。

**迁移摘要**
- 4×（el-card + el-statistic）→ `CpStatCard`（label=原 title、value=原 value；tone：pending=warn / running=info / doneThisMonth=brand / rejected=danger）。移除 `.stat-card` deep 样式。
- 趋势卡 / 最近卡：保留 el-card 壳，`#header` span → `CpSectionHeader :title`（同 BridgeHealthView 先例）。
- 趋势柱 token 化：`.bar-track` 背景 `#f5f7fa`→`var(--cp-line-soft)`；`.bar-fill` `var(--el-color-primary)`→`var(--cp-brand)`（行尾 `/* cp-chart-color */` 豁免）；`.bar-count/.bar-date` 文字色 → `--cp-muted`/`--cp-faint`。
- 空态 el-empty(×2) → `CpEmpty :text`。el-table 保留（信箱内嵌迷你表，非独立查询页；强套 CpListPage 需 fetch 包装/分页，属过度）。
- **emits 签名 `open-detail` 不变**（批次 2 面板依赖）。scoped style 仅剩布局。

## 2. FormDetail.vue（表单详情）

**迁移前盘点**
- API：`Promise.all([inboxApi.detail(instanceId), inboxApi.pending()])`；`inboxApi.batch`（承認/却下）。
- 结构：左 DynamicForm（工作流 schema 表单，只读 mask）+ 右 FlowTimeline + CC 标签 + action-bar（comment 输入 + 承認/却下/転送/差戻 4 按钮，仅当 myTaskId 存在）。
- 子组件：FlowTimeline / TransferDialog / SendBackDialog（本批同族）。
- i18n：`oa.detail.*`——全部保留。
- props `instanceId` / emits `done`——**签名不变**。

**迁移摘要**
- CC `el-tag(type=info effect=plain)` → `CpTag tone="info"`。
- 加载失败 / 无表单数据 el-empty(×2) → `CpEmpty :text`（保留 v-else-if / v-else 分支语义）。
- el-skeleton 保留（加载骨架，无 Cp 等价）。DynamicForm 保留：schema 驱动的工作流表单，CpDetailPanel（静态 label/value items[]）表达不了 → 按 §5 保留原机制（同 LocationList master-detail 先例，不计入模板缺口）。
- 样式 token 化：`.panel-title` 色 `--el-text-color-primary`→`--cp-ink`、下边框 `--el-border-color-light`→`--cp-line`；`.detail-left` 右边框、`.action-bar` 上边框 → `--cp-line`；`.cc-title` → `--cp-muted`。
- action-bar el-input/el-button 保留（表单控件，无 Cp 等价；批次 2 面板同处置）。

## 3. FlowTimeline.vue（流程时间线）

**迁移前盘点**
- 数据：props `timeline[] / forecast[]` → `mergeTimeline`/`groupByBranch`（inboxModel）分支分组。
- 结构：el-timeline / el-timeline-item（多分支）+ 状态 el-tag(×2) + 处理人/评论/时间 + 预计段（forecast）。
- i18n：`formToStatusText`（inboxModel）+ `oa.timeline.sentBack` + 若干中文 key t() 调用（`t('分支')`/`t('实办')`/…，既有 i18n 机制，不改）。

**迁移摘要**
- 主状态 `el-tag :type=statusTagType` → `CpTag :tone=formToStatusTone`；SentBack 附加 `el-tag(danger,plain)` → `CpTag tone="danger"`。
- 新增 `formToStatusTone(s): Tone = ['warn','ok','danger','info','info','info','info','danger'][s] ?? 'info'`——**0-6 与 InboxDone.formToStatusTone 对齐**（同状态同 tone，无漂移），index 7=danger，保留原 statusTagType 视觉（0→warn·1→ok·2/7→danger·其余 info）。移除旧 `statusTagType`。
- el-empty → `CpEmpty :text`。el-timeline / el-timeline-item **保留**（设计系统无时间线模板，特殊形态；`:type` 为语义 prop 非硬编码色）。`timelineType`（节点圆点色）保留不动。
- 样式 token 化：`.branch-header` 色/左边框 → `--cp-brand-deep`/`--cp-brand`；`.tl-stage-label` → `--cp-brand-deep`+`--cp-brand-bg`；`.tl-handler`→`--cp-text`；`.tl-comment/.tl-approvers`→`--cp-muted`；`.tl-time`→`--cp-faint`；forecast `:deep` 内容色 → `--cp-muted`。

## 4. SendBackDialog.vue（退回对话框）

**迁移前盘点**：`inboxApi.sendBack(taskId, kind, nodeId?, comment?)`；kind = starter/prevStage/node 单选（node 时联动 el-select 选上游节点）+ comment textarea；footer 取消 + `type=danger` 确认。props `taskId/modelValue/canSendBackPrevStage/timeline/currentNodeId`，emits `done/update:modelValue`。i18n `oa.sendback.*`/`oa.detail.sendback.*`/`oa.transfer.comment*`/`common.cancel`。

**迁移摘要**：**审计后无改动**——文件无 scoped style、无硬编码色值、无 el-tag，内联 style 均为纯布局。保留 el-dialog：`kind` 单选驱动的条件字段（node 选择器 v-if）+ `type=danger` 破坏性确认按钮 CpFormDialog 声明式 fields 表达不了（其 confirm 硬编码 primary，套用将丢 danger 语义 = 回归），按 §5 保留原机制。emits/props 签名保全。

## 5. TransferDialog.vue（转派对话框）

**迁移前盘点**：`transferApi.transfer(taskId, toUserId, comment?)` + `userApi.getList`（remote 搜索）；el-select filterable remote + comment textarea；footer 取消 + `type=warning` 确认。props `taskId/modelValue`，emits `done/update:modelValue`。i18n `oa.transfer.*`/`common.cancel`。

**迁移摘要**：**审计后无改动**——同 SendBack：无样式/无色值/无 el-tag。保留 el-dialog：remote 异步搜索 select（同缺口 #23 家族）+ `type=warning` 确认 CpFormDialog 表达不了，按 §5 保留。签名保全。

---

## 验证证据

- `npm run type-check`：**0 error**（vue-tsc --build 通过）。
- `npm run test`：**316 passed (46 files)**——基线保持，无下降。
- 真栈走查（dev 5173 代理 9991，login `POST /api/auth/login` 200，admin/123456，gstack browse）：
  - `/oa/inbox` 仪表盘默认视图：4 张 CpStatCard 渲染（处理待ち1/進行中1/今月0/却下0，却下 tone=danger 呈红），トレンド柱（teal --cp-brand），最近待办表 1 行（预算审批/管理员/2026-06-20），CpSectionHeader 区块头。截图 `shots/oa-inbox-dashboard.png`。
  - 点最近行 → 详情抽屉（FormDetail）：フォーム内容 CpEmpty（`フォームデータがありません`，因该单无 schema 数据，空态渲染正确），審査進捗 FlowTimeline（el-timeline `end` 节点），action-bar 4 按钮（承認/却下/転送/差戻，因当前用户有待办 taskId）。截图 `shots/oa-form-detail.png`。
  - 転送 → TransferDialog 打开（タスクを転送 / 転送先 select / 転送メモ / 転送を確認）。截图 `shots/oa-transfer-dialog.png`。
  - 差戻 → SendBackDialog 打开（差戻目标单选含「指定ノード」/ 差戻メモ / 确认）。截图 `shots/oa-sendback-dialog.png`。
  - console 无本批引入的新错误。既有告警/错误均迁移前既有、非本批引入：
    - `POST /hubs/notify/negotiate 403`（SignalR CSRF，已知基础设施问题，与 WmsDashboard 同源）。
    - `[el-pagination] small … deprecated`、intlify `Ignore object flatten`、Vue Router `next()` deprecated——既有。
    - **`common.cancel` 在 ja 语言下显示原始 key**（TransferDialog/SendBackDialog 取消按钮）：ja 词典缺 `common.cancel` 键，属**迁移前既有 i18n 缺口**，且本批未改动这两个文件 / 不改 i18n 机制——记录待后续统一补 `common.*` 词条（与缺口 #6 follow-up 同族）。

## 模板缺口

**本批无新增模板缺口编号**（延续至 #23）。五文件均落在既有契约：三个特殊页按「非表格特殊页只做 token 化 + 无损基础件替换」处置（同批次 2/ERP 批次先例），两个 working form dialog 审计合规且其保留 el-dialog 的动因（破坏性/warning 确认按钮 tone、条件字段、remote 搜索）分属既知 #23 家族与「CpFormDialog confirm 硬编码 primary」既知契约差异，未触发新的模板扩展需求。

## 修复记录

审查者定级 Important：批次 3 遗留 3 处硬编码 `border-radius: 3px` 未 token 化。修复如下：

- `InboxDashboard.vue:131` `.bar-track` `border-radius: 3px` → `border-radius: var(--cp-r-sm)`。
- `InboxDashboard.vue:141` `.bar-fill` `border-radius: 3px 3px 0 0` → `border-radius: var(--cp-r-sm) var(--cp-r-sm) 0 0`。
- `FlowTimeline.vue:166` `.tl-stage-label` `border-radius: 3px` → `border-radius: var(--cp-r-sm)`。

**token 选用说明**：`tokens.css` 的 Radius 块仅 4 档——`--cp-r-xl:20px / --cp-r-lg:16px / --cp-r-md:12px / --cp-r-sm:8px`，无更小档位。`--cp-r-sm`(8px) 是唯一可选、也是最小可用 token，与原 3px 存在肉眼可辨差异（趋势柱轨道/填充、时间线阶段标签的圆角会略变大），但为体系内最接近选项，且与 `InboxPending.vue`/`BackorderListView.vue` 等既有小圆角元素（8-12px 内边框卡片）用法一致，未引入新 token。

**验证**：两文件 grep 硬编码圆角/色值/阴影（`border-radius:\s*\d`、`color:\s*#`、`box-shadow:\s*\d`）均无残留。`npm run type-check`（`NODE_OPTIONS=--max-old-space-size=4096`）：`vue-tsc --build` 通过，0 error（CSS-only 改动，未跑测试套件）。
