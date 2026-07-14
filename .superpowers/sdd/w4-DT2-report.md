# Task D-T2 报告：列表工具栏 rowMode 切换开关（写回偏好）

**STATUS: DONE**
**Commit: 9868b16**（branch feat/wfs-inbox-ux，已 push）

## 交付
波④信箱体验第 11/14 任务，收口 X-D 波。TDD 完成 `parseRowMode` 纯函数 + InboxPending 工具栏 rowMode 切换开关 + 偏好写回 + FormDetail expanded 保真。

## 改动（严格=简报 5 文件）
1. `cp6.web/src/views/oa/inbox/inboxModel.ts` — 末尾新增 `parseRowMode(prefsJson): 'merged'|'expanded'`（顶层 rowMode 键；缺省/非法/畸形/缺键 → merged；try/catch 兜 JSON.parse）。
2. `cp6.web/src/views/oa/inbox/inboxModel.test.ts` — 追加 `describe('parseRowMode')` 2 用例（缺省/缺键/非法/畸形→merged；expanded 显式识别），import 追加 parseRowMode。
3. `cp6.web/src/api/oa/inbox.ts` — `pending` 加可选 `rowMode?: 'merged'|'expanded'` 参数走 `params: { rowMode }`（axios 自动省略 undefined → 既有无参调用点走后端偏好回落，零变化）。
4. `cp6.web/src/views/oa/inbox/InboxPending.vue` — review 面板 `.table-toolbar` 追加 el-radio-group 开关（刷新按钮后）；脚本加 `rowMode` ref + `initRowMode()`（读 prefApi.get 经 parseRowMode）+ `onRowModeChange()`（saveMerge 顶层键合并 + loadReview）；`loadReview` 取数改 `inboxApi.pending(rowMode.value)`；`onMounted` 改为 initRowMode→loadReview；`<style>` 加 `.rowmode-toggle{margin-left:auto}`。
5. `cp6.web/src/views/oa/inbox/FormDetail.vue` — `loadDetail` 内 `inboxApi.pending()` → `inboxApi.pending('expanded')`（详情页找「我的可办任务」需逐任务粒度，不随显示偏好合并，行为保真）。

## TDD 证据
- RED：新用例 `parseRowMode is not a function`（2 failed | 4 passed）。
- GREEN：实现后全量 `bun run test -- --run` → 65 文件 / **434 passed**（基线 432 + 2）。

## Gates
- vitest：434 passed（0 fail）。
- `bun run type-check`：0 错误（vue-tsc 无输出通过）。
- `bun run build`：✓ built in 7.38s（仅既有 chunk>500kB 警告，非本任务引入）。
- `git show --stat HEAD`：仅 5 个简报文件，63 insertions / 5 deletions。
- C-T1 移动端卡片 + 跨断点多选 watch 块未受扰动；开关作为工具栏 NEW 元素桌面/移动双断点均显示（符合简报豁免）。

## Concerns
- i18n 键 `oa.inbox.rowMode.merged` / `oa.inbox.rowMode.expanded` 由 E-T1 落地；当前运行时缺键回落为键字符串（t() 非强类型键，不阻塞 type-check/build）。E-T1 前 UI 显示原始键名，预期内。
- 无其他风险。后端未触（2028/5skip 不变）。
