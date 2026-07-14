# Task C-T1 报告：信箱壳 + 三列表页卡片化 + 筛选抽屉

**STATUS: DONE**
**Commit:** 89a060e8c4ef337b27965f6d130084158bc29d15（已 push 至 feat/wfs-inbox-ux）
**分支:** feat/wfs-inbox-ux

## 测试小结
- `bun run test -- --run`：65 文件 / 432 通过（基线 432，零回归，纯模板分支无逻辑改动故无新测试）
- `bun run type-check`（NODE_OPTIONS heap 8192）：EXIT 0
- `bun run build`：EXIT 0（仅既有 chunk-size 告警，无关本改动）
- `git show --stat HEAD`：仅 4 个 brief 指定文件（+302 / -4）

## 实现摘要（对齐 brief 五步）
- Step 1 InboxView.vue：脚本加 `useBreakpoint` + `folderList` computed；`el-aside` 加 `v-if="!isMobile"`；`</el-header>` 与 `inbox-body` 之间插移动端 `mobile-folder-bar` 横滑条（`v-if="isMobile"`）；详情 `el-drawer` size 改 `:size="isMobile ? '100%' : '60%'"`；CSS 尾部加 `.mobile-folder-bar` 两条 + `@media (max-width:767px)` 块。
- Step 2 InboxPending.vue：脚本加 `useBreakpoint` + `isSelected`/`toggleMobileSelect`（复用同一 `selected` 数组）；review `el-table` 加 `v-if="!isMobile"`，其后（CpEmpty 前）插 `mobile-list` 卡片流（含 checkbox 多选）；cc `el-table` 同法 + 卡片流；CSS 尾部加 `.mobile-*` 七条 + `@media` batch-bar 换行块。
- Step 3 InboxRunning.vue：脚本加 `useBreakpoint`；`el-table` 加 `v-if="!isMobile"`，其后插 `mobile-list`；CSS 加 `.mobile-list/.mobile-row/.mobile-main/.mobile-flow/.mobile-meta` 五条。
- Step 4 InboxDone.vue：脚本 `Refresh` import 换 `Filter, Refresh` + `useBreakpoint` + `filterDrawer` ref；`.done-controls` 加 `v-if="!isMobile"`；`table-toolbar` 内刷新按钮后加移动端筛选入口按钮（`v-if="isMobile"`）；新增底部方向 `el-drawer` 承载月份选择 + `el-radio-group` tab；`el-table` 加 `v-if="!isMobile"` + `mobile-list`；CSS 加 `.mobile-*` 五条。

## 桌面端像素零回归自检（逐文件）

### InboxView.vue — PURE
- 模板改动：①`el-aside` 追加 `v-if="!isMobile"`（≥768px 恒真，桌面渲染路径字节不变，无类改名/无包裹层）；②`mobile-folder-bar` 整块以 `v-if="isMobile"` 门控（桌面永不渲染）；③`el-drawer` 的 `size="60%"` → `:size="isMobile ? '100%' : '60%'"`（桌面分支求值仍为 `'60%'`，等价）。
- CSS 改动：新增 `.mobile-folder-bar`（桌面无此元素）+ `@media (max-width:767px)` 尾块（≥768px 不命中）。桌面既有规则零改。
- 结论：≥768px 渲染路径字节等价。

### InboxPending.vue — PURE
- 模板改动：review/cc 两 `el-table` 各追加 `v-if="!isMobile"`（桌面恒真，属性顺序及余下属性字节不变）；两处 `mobile-list` 卡片流以 `v-if="isMobile"` 门控（桌面永不渲染）。CpEmpty/batch-bar/table-toolbar 桌面元素零改。
- 脚本新增仅移动端使用的 `isSelected`/`toggleMobileSelect`，`selected` 语义不变（D-T2/批量条复用零改动）。
- CSS 改动：新增 `.mobile-*` scoped 类（桌面无此元素）+ `@media (max-width:767px)` batch-bar 尾块（≥768px 不命中）。桌面既有 `:deep(.row-unread td)` 等零改。
- 结论：≥768px 渲染路径字节等价。

### InboxRunning.vue — PURE
- 模板改动：`el-table` 追加 `v-if="!isMobile"`（桌面恒真）；`mobile-list` 以 `v-if="isMobile"` 门控。CpEmpty/table-toolbar 桌面元素零改。
- CSS 改动：仅新增 `.mobile-*` scoped 类（桌面无此元素）。桌面既有规则零改，无 `@media` 改动亦无桌面选择器改动。
- 结论：≥768px 渲染路径字节等价。

### InboxDone.vue — PURE
- 模板改动：①`.done-controls` 追加 `v-if="!isMobile"`（桌面恒真，桌面控制区字节不变）；②`table-toolbar` 内新增移动端筛选按钮，以 `v-if="isMobile"` 门控（桌面永不渲染，插在既有刷新按钮之后，桌面既有两子元素顺序与字节不变）；③新增底部 `el-drawer`（`v-model="filterDrawer"`，默认 false，桌面 `isMobile` 恒假 → 内容不渲染，且 drawer 为 teleport 浮层不占布局）；④`el-table` 追加 `v-if="!isMobile"` + `mobile-list` 门控。
- 脚本改动：`Refresh` import 扩为 `Filter, Refresh`（Filter 已被移动端按钮引用，无未用告警）。
- CSS 改动：仅新增 `.mobile-*` scoped 类（桌面无此元素）。桌面既有 `.done-controls/.done-tabs/.table-toolbar` 规则零改。
- 结论：≥768px 渲染路径字节等价。

## 说明与关切
- **列表页 anchor 漂移已核对**：wave-② OA 批次2 重构曾精简三列表页桌面表格列（如 InboxPending 桌面表仅 flowName/starterName/sentAt，非 brief 原描述的关卡/状态列）。brief 的移动端卡片 markup 引用字段（`stageName`/`nodeId`/`flowKey` on PendingItem；`atNodeId`/`ccId` on CcItem；running/done 各字段）经核对 `types/oa/inbox.ts` 全部存在，故按 brief markup 逐字落地，无字段缺失，type-check 通过佐证。
- **noUncheckedIndexedAccess**：本任务未新增下标访问（`instanceStatusTone`/`formToStatusTone` 的数组下标为既有代码，未触碰）。
- **i18n 新键**：`oa.inbox.mobileFilter` 为新键，按全局约束由 E-T1 一次性 seed；运行时缺失仅回退为原样字符串，不影响 build/type-check。folderList 复用既有 `oa.inbox.*` 键。
- **零硬编码色**：新增 CSS 全部使用 `--cp-*` token（`--cp-card`/`--cp-line`/`--cp-line-soft`/`--cp-ink`/`--cp-muted`）。
- **375px 走查**：留 E-T2 harness（brief Step 5 口径）。

---

## 审查修复：C-T1 跨断点多选回填（Important）

**提交** 274dd42 `fix(wfs-inbox): C-T1审查修复 跨断点多选回填(toggleRowSelection同步)`

### 缺陷
`InboxPending.vue` 审核 el-table 受 `v-if="!isMobile"` 门控（完整卸载/重挂载）。移动端卡片复选框通过 `toggleMobileSelect` 维护同一 `selected` 数组。断点跨越 mobile→desktop 时，表格以空的内部选中态重挂载；el-table→数组同步是单向的（`@selection-change` 整体覆盖 `selected`），故：用户在移动端勾选 → 旋转到桌面 → 触碰任一原生复选框 → 之前的移动端选择被静默丢弃（批量条计数与提交 ids 背离）。次要问题同根源：重挂载的表格不可视化呈现已选行。

### 修复
在 script 中新增 `watch(isMobile, ...)`：当 `isMobile` 由 `true` 变 `false`（→桌面），`await nextTick()` 后遍历 `reviewRows`，对 taskId 命中 `selected` 的行调用 `reviewTableRef.value?.toggleRowSelection(row, true)`，将选中态回填进重挂载的表格。同时修复可视化呈现（toggleRowSelection 会勾选并高亮行）。

反方向 desktop→mobile **无需处理并已核实**：移动端卡片通过 `isSelected(row)` 直接读同一 `selected` 数组，天然一致。

### 硬约束遵守：桌面像素纯净
- 模板改动仅限已存在 `<el-table>` 标签上的 `ref="reviewTableRef"` 属性（本次实际发现该 ref 与声明在 89a060e 中已就位，故本次修复**零模板改动**，全部逻辑在 script）。
- 全部逻辑落在 `<script setup>`：新增 `nextTick/watch` import + 一个 watch 块，不改桌面渲染。

### 门禁
- `bun run test -- --run`：432 passed（65 files）✅
- `bun run type-check`（NODE_OPTIONS heap 8192）：0 error ✅
- `bun run build`：built OK（仅既有 chunk-size 告警，与本修复无关）✅
- `git show --stat HEAD`：仅 InboxPending.vue（1 file, +18 -1）✅

### 关注点
无。修复内敛于单文件 script，不触碰桌面模板与移动端逻辑。
