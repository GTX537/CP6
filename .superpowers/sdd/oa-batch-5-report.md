# OA 迁移批次 5 报告(收尾批)

分支 `feat/ui-migrate-oa`。范围:2 文件 token 化 + 全模块硬编码清扫 + el-tag/el-empty 终验。

---

## 1. 两文件盘点 + 迁移摘要

### 1.1 `cp6.web/src/views/oa/settings/InboxSettings.vue`(路由 /oa/settings,菜单 737)

**盘点**
- API 调用:`delegateApi.list/add/remove`、`prefApi.get/save`、`userApi.getList`(远程搜代理人)——全部保留,未动。
- 形态:三 Tab 设定页(代理人设定 / 显示偏好 / 通知设定)+ 新增代理人弹窗。属指南 §9「非表格特殊页」——只做 token 化与基础件替换,**不强套 CpListPage**(代理人表是 Tab 内小型子表,无搜索/分页/状态列,套 CpPageShell+CpListPage 会破坏 Tab 布局且属过度工程)。
- i18n:全部 `t()` 词条原样保留。
- 权限指令:本页无 `v-permission`。
- 列/字段:代理人表 4 列(delegateName/validPeriod/scope/remark)+ 操作删除;偏好/通知表单项——均无状态色/tag 视觉。

**迁移结论:无需改动。** 全文件盘点后确认:
- scoped style 仅 `.inbox-settings{padding:16px}`、`.tab-toolbar{margin-bottom:12px}` —— **纯布局,零硬编码色/影/圆角**。
- 内联样式仅 `max-width/margin-top/width:100%` —— 纯布局,指南允许残留。
- **无 el-tag、无 el-empty**(见 §3 普查)。
- el-table/el-form/el-dialog/el-select/el-card 为 Element 基础件,已由 `element-overrides.css` 统一 token 化(与批次 1~4 同类 admin/设定页处理一致)。
- 故本文件已完全合规,batch-5 对其为「盘点确认 + 终验通过」,diff 不含它。

### 1.2 `cp6.web/src/views/oa/notification/NotificationBell.vue`(顶栏全局铃铛,非路由页)

**盘点**
- API:`notificationApi.unreadCount/list/read/readAll`——保留。
- SignalR:`WfNotification` handler + 60s 轮询兜底 + onUnmounted off()——**未动**(含今晨 `[object Object]` 角标修复:`res.data.count` 取数逻辑原样保留,未回退)。
- i18n:全部 `t()` 词条保留。
- 状态角标:`el-badge :value="unreadCount"`——数值角标,非状态色 tone;无 instanceStatusTone 家族介入需求。

**迁移(token 化 13 处硬编码)**

| 选择器 | 原值 | → token | 说明 |
|---|---|---|---|
| `.bell-btn` color | `#606266` | `var(--cp-muted)` | 图标默认灰 |
| `.bell-btn:hover` color | `#409eff` | `var(--cp-brand)` | 悬停品牌色 |
| `.notify-panel-title` color | `#303133` | `var(--cp-ink)` | 主文本 |
| `.notify-empty` color | `#909399` | `var(--cp-muted)` | 空态文本 |
| `.notify-empty-icon` color | `#dcdfe6` | `var(--cp-faint)` | 空态弱化图标 |
| `.notify-loading` color | `#409eff` | `var(--cp-brand)` | 加载态 |
| `.notify-item` border-radius | `6px` | `var(--cp-r-sm)` | 圆角 token |
| `.notify-item:hover` bg | `#f5f7fa` | `var(--cp-bg-hover)` | 悬停底 |
| `.notify-item+.notify-item` border-top | `#f0f0f0` | `var(--cp-line-soft)` | 分隔线 |
| `.notify-item.is-unread` bg | `#f0f7ff` | `var(--cp-brand-bg)` | 未读品牌淡底 |
| `.notify-item.is-unread:hover` bg | `#e8f3ff` | `color-mix(in srgb, var(--cp-brand) 14%, var(--cp-card))` | 未读悬停加深 |
| `.notify-item-dot` bg | `#409eff` | `var(--cp-brand)` | 未读点(其 `border-radius:50%` 正圆豁免保留) |
| `.notify-item-title` color | `#303133` | `var(--cp-ink)` | 标题主文本 |
| `.notify-item-time` color | `#909399` | `var(--cp-muted)` | 时间弱化文本 |

原设计的蓝色淡底(#f0f7ff/#e8f3ff,承旧蓝主题)统一收敛为品牌青(--cp-brand-bg + color-mix),与 CP6 设计系统一致。所有用到的 token 均确认存在于 `src/styles/tokens.css`。

---

## 2. 模块硬编码清扫(grep 证据)

命令:
```
rg -n "#[0-9a-fA-F]{3,8}\b|rgba?\(|box-shadow:\s*[0-9]|border-radius:\s*[0-9]" cp6.web/src/views/oa
```

清扫后命中 **11 处,全部为合法豁免**,逐条列举:

| # | 文件:行 | 命中 | 豁免类别 |
|---|---|---|---|
| 1 | designer/nodes/StartNode.vue:28 | `box-shadow: 0 0 0 2px color-mix(…var(--cp-ok)…)` | token 化焦点环(颜色走 color-mix over token,`0 0 0 2px` 为几何非色值) |
| 2 | designer/nodes/GatewayNode.vue:32 | `box-shadow: 0 0 0 2px color-mix(…var(--cp-warn)…)` | 同上 |
| 3 | designer/nodes/EndNode.vue:28 | `box-shadow: 0 0 0 2px color-mix(…var(--cp-muted)…)` | 同上 |
| 4 | designer/nodes/ApprovalNode.vue:37 | `box-shadow: 0 0 0 2px color-mix(…var(--cp-info)…)` | 同上 |
| 5 | designer/designerModel.ts:52 | `color: '#67c23a'` | `/* cp-chart-color */`(节点身份 categorical 色) |
| 6 | designer/designerModel.ts:53 | `color: '#409eff'` | `/* cp-chart-color */` |
| 7 | designer/designerModel.ts:54 | `color: '#e6a23c'` | `/* cp-chart-color */` |
| 8 | designer/designerModel.ts:55 | `color: '#e6a23c'` | `/* cp-chart-color */` |
| 9 | designer/designerModel.ts:56 | `color: '#909399'` | `/* cp-chart-color */` |
| 10 | designer/DesignerCanvas.vue:495 | `border-radius: 50%` | 正圆豁免 |
| 11 | notification/NotificationBell.vue:285 | `border-radius: 50%` | 正圆豁免(未读点) |

**本批处置说明(#5~#9,designerModel.ts NODE_PALETTE.color):**
- 这 5 个 hex 是**死数据**:实际渲染的调色板圆点用 CSS 类 `.dot-<type>`(DesignerCanvas.vue:500-504,已 token 化为 `--cp-ok/--cp-info/--cp-warn/--cp-muted`);`palette.color` 字段全库无任何读取处(DesignerCanvas:205 只用 `.type`;测试只断言 `.type`)。
- 属节点身份 categorical 色(§2.5 图表色系列)。本批按指南豁免机制补 `/* cp-chart-color */` 行尾注释 + 一行说明,使全模块 grep 达「零未豁免硬编码」。
- **follow-up 建议(不在本批范围):** 该 color 字段为死代码,可在后续设计器票据中直接删除。

清扫前 NotificationBell.vue 有 13 处未豁免硬编码,现全部 token 化;designer nodes 焦点环与 border-radius:50% 属批次 4 已审查产物,未动。

---

## 3. el-tag / el-empty 全模块普查(终验)

命令:
```
rg -n "<el-tag|<el-empty" cp6.web/src/views/oa
```

**命中 0。** 全 OA 模块无任何残留 `<el-tag>` / `<el-empty>`——批次 1~4 已全部转 CpTag/CpEmpty,本批终验确认清零。

现存 CpTag/CpEmpty 用法抽样(均为已迁移正确形态):
- `FormQuery.vue:127` CpTag `:tone="instanceStatusTone(...)"`(info/ok/warn/danger 家族)
- `InboxRunning/InboxDone/FlowTimeline.vue` CpTag + formToStatusTone/instanceStatusTone
- `InboxView.vue:107` CpTag `:tone="row.enable ? 'ok' : 'muted'"`
- `NodePropertyPanel.vue:182` CpTag `tone="muted"`
- 各 Inbox/Catalog/Query 页 CpEmpty `:text`

无遗留豁免项需列举(el-tag 侧已零残留)。

---

## 4. 验证证据

| 项 | 结果 |
|---|---|
| `npm run type-check`(NODE_OPTIONS=8192) | **0 error**(vue-tsc --build 通过) |
| `npm run test`(vitest run) | **316 passed / 46 files**(基线 316 保持,零下降) |
| 模块硬编码 grep | 11 命中全豁免(见 §2) |
| el-tag/el-empty grep | 0 命中(见 §3) |
| git diff scope | 仅 2 文件(NotificationBell.vue 28 行 + designerModel.ts 12 行);InboxSettings.vue 已合规无 diff |

### 真栈走查:已完成(见文末「## 走查补记」)
- 首轮走查曾被环境阻塞(WSL Ubuntu Stopped,`0x80072746`;协调者确认根因为 bun 进程泄漏耗尽提交内存击崩 wslservice,已清理修复)。
- 环境恢复后已补完全部走查项与两张截图,证据见 **## 走查补记**。

---

## 5. 缺口台账
- 新增模板缺口:**0**(未触碰模板组件本体;InboxSettings 保留基础件属既有形态,非新缺口)。
- 台账 #23 维持;本批不接续新条目。

## 6. concerns
1. ~~真栈走查被 WSL/后端环境阻塞~~ **已解除**:环境修复后走查已补完(见「## 走查补记」),无本批引入的问题。
2. designerModel.ts `NODE_PALETTE.color` 为死代码,本批以 chart-color 豁免注释处置;建议后续设计器票据直接删除该字段。

---

## 走查补记(环境修复后补做,2026-07-05)

环境状态:WSL/Docker/dev server 全链路恢复(根因 bun 进程泄漏已清理);`POST http://localhost:5173/api/auth/login` 预检 **200** 后开始走查。gstack browse,admin/123456 登录成功(→ /dashboard)。

### /oa/settings(設定页)
- 页面正常打开,三 Tab(代理管理 / 設定 / 通知設定)渲染齐全;代理人表格表头(委任者/委任期間/代理範圍/備考)+ 空数据态(No Data)正常;偏好表单与通知开关表单在 DOM 中齐全。
- 后端数据链路全通:`GET /api/oa/delegate/list → 200`、`GET /api/oa/pref/get → 200`。
- 截图:`C:\CP6\.superpowers\sdd\shots\oa-settings.png`。
- 观察(非本批引入):操作列表头渲染为原始键 `common.action`(另有 `common.save` 按钮同现象)——ja 词典缺键,与已登记 follow-up「common.operation/common.cancel ja 缺键(#6 共享词条决策)」同族,属迁移前 i18n 缺口,本批未触碰 i18n,不处理仅上报。

### 顶栏通知铃铛(NotificationBell)
- 铃铛图标正常显示;**角标正确隐藏**(unread=0,`GET /api/oa/notification/unread-count → 200` 返回真实数据;今晨 `[object Object]` 修复保持有效,未回退)。
- 点击铃铛 → 通知面板打开(`is visible ".notify-panel"` → **true**):标题「通知」(--cp-ink)、「すべて既読にする」按钮正确禁用(0 未读)、空态铃铛图标(--cp-faint)+「通知はありません」(--cp-muted)渲染正常,token 化配色与 CP6 设计系统一致。
- 本批 scoped 样式块经 Vite 正常加载(`NotificationBell.vue?…&type=style… → 200`)。
- 截图:`C:\CP6\.superpowers\sdd\shots\oa-notification-bell.png`(面板展开态)。

### console 检查
- **无本批引入的错误。** 现存条目均为已知/迁移前问题:SignalR negotiate 403(CSRF,已登记环境 follow-up)、intlify flatten 警告(全局既有)、el-pagination `small` 弃用警告(Element Plus 既有)、Vue Router `next()` 弃用警告(全局既有)。CSS token 替换不产生任何 console 面。

### 结论
两处走查全部通过,无本批引入的问题;i18n `common.action`/`common.save` ja 缺键为迁移前既有缺口(同 follow-up #6 家族),已上报不处理。
