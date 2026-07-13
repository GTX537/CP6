# Task E-T2 报告：前端「触发器」tab（API + 分型表单 + 流水抽屉 + key 一次性显示）+ vitest

## STATUS: DONE

commit: `d195145`（已 push 到 `feat/wfs-event-trigger`）

## 交付文件（仅 brief 指定 6 个）
- 新增 `cp6.web/src/api/oa/flowTrigger.ts` — flowTriggerApi 包装（http + unwrap `res.data ?? res`）
- 新增 `cp6.web/src/views/oa/admin/flowTriggerModel.ts` — 纯逻辑（TRIGGER_TYPES / CRON_PRESETS / typeTone / validateTriggerForm / buildConfigJson）
- 新增 `cp6.web/src/views/oa/admin/__tests__/flowTriggerModel.spec.ts` — vitest 5 用例
- 修改 `cp6.web/src/views/oa/admin/FlowAdmin.vue` — el-tabs 包裹，既有流程内容原样移入 flows tab
- 新增 `cp6.web/src/views/oa/admin/FlowTriggerPanel.vue` — 列表 + 操作 + 流水抽屉 + key 一次性弹窗
- 新增 `cp6.web/src/views/oa/admin/FlowTriggerDialog.vue` — 分型表单（timer cron+预设+预览 / event eventKey+varsMap / message varsSchema）

## TDD 记录
- **RED**：先写 spec，`bun run test -- flowTriggerModel` → Failed（`Failed to resolve import "../flowTriggerModel"`，0 test）。
- **实现**：flowTriggerModel.ts + flowTrigger.ts 按 brief verbatim。
- **GREEN**：同命令 → Test Files 1 passed，Tests 5 passed。

## Gate 结果
1. 新 spec 通过；全量 `bun run test` → **Test Files 64 passed，Tests 425 passed**（420 基线 + 5 新增）。
2. `bun run type-check`（heap 8192）→ **EXIT=0，0 errors**。
3. `bun run build` → **✓ built in 7.12s，EXIT=0**（仅既有 chunk-size 警告，与本任务无关）。
4. `git show --stat HEAD` → 仅 brief 的 6 个文件，450 insertions / 22 deletions。

## 自查
- 零硬编码色：仅用 CpTag `:tone`（typeTone 返回 ok/info/warn/muted；流水结果 ok/warn/muted）。
- 零硬编码中文：模板全部 `t()`；i18n 键用 brief 指定的确切键名（`oa.flowtrigger.*` / `oa.flowadmin.tab.flows` / 复用 `common.*`），DB seed 归 F-T2。
- API 范式对齐 designer.ts/flowAdmin.ts：`import http from '../http'`、导出 `flowTriggerApi` 字面量、`unwrap = res?.data ?? res`。http 响应拦截器已返回 `response.data`，unwrap 二次剥壳与既有约定一致。
- FlowAdmin.vue：`:count` 与 refresh 按钮均用 `activeTab === 'flows'` 门控；触发器 tab `lazy`，既有 el-alert + CpListPage 逐字移入未改行为。
- 「每月末」预设 = `0 9 28 * *`（28 日近似，NCrontab 无 L，映射表③，文案在 `oa.flowtrigger.preset.monthEnd`）。注：brief spec 示例用 `0 0 28 * *`，CRON_PRESETS/测试统一为 `0 9 28 * *`（与其他预设同 9 点，测试断言即为此值），采用之。

## 偏差（deviations）
- **FlowTriggerDialog.vue L119**：brief verbatim `ElMessage.warning(t(errs[0]))` 在本仓 `noUncheckedIndexedAccess` 下报 TS2345（`errs[0]` 为 `string | undefined`）。最小修正为 `t(errs[0]!)`（已由 `errs.length` 守卫，运行期恒定义）。仅此一处，其余 brief 代码逐字采用。

## Concerns
- i18n 键（`oa.flowtrigger.*`、`oa.flowadmin.tab.flows`）尚未入库，属 F-T2 任务范围；未落 seed 前 UI 会显示 key 本身（回退行为，符合 http.ts 缺失回退约定）。
- 后端未触碰（backend baseline 1962/5skip 不受影响）。
