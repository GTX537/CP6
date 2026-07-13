# Task A-T4 报告：设置页「通知设定」矩阵卡片（前端）

**STATUS: DONE**
**Commit:** e36fd95（已 push 到 feat/wfs-inbox-ux）
**测试:** vitest 432 passed（425 基线 + 7 新增）；type-check 0 err；build OK。

## 交付
- `cp6.web/src/views/oa/settings/notifyMatrixModel.ts`（新）：`buildMatrixState` / `toNotifyPatch` 纯函数，解析语义逐位镜像后端 `NotifyMatrix.IsEnabled`。
- `cp6.web/src/views/oa/settings/notifyMatrixModel.test.ts`（新）：TDD RED→GREEN，7 用例（4 brief 原样 + 3 branchPruned 扩展）。
- `cp6.web/src/api/oa/pref.ts`：新增 `saveMerge`（merge=true 顶层合并写）+ `notifyMatrix`（元数据）。
- `cp6.web/src/views/oa/settings/InboxSettings.vue`：notify tab 扁平开关堆 → 类型×通道矩阵表（`el-table` 数据驱动，不支持格子 `:disabled`+`el-tooltip`）；删 `NotifyPrefs`/`notifyPrefs`/`saveNotifyPref`；显示偏好 `savePref` 与矩阵 `saveNotifyMatrix` 均改走 `saveMerge`；`resetNotifyMatrix` 发 `{"notify":null}` 删键恢复默认；`loadPref` 追加 `loadNotifyMatrix`（无 prefsJson 也传 `'{}'` 保证行渲染）；`.matrix-actions` 样式。

## TDD 轨迹
- RED：`bun run test -- --run notifyMatrixModel` → 模块不存在，编译失败。
- GREEN：实现后 7/7 通过。

## 记录的适配（documented adaptation）
1. **矩阵五行**：后端反射轴（波② merge）已长出第 5 行 `branchPruned`（Support=(true,true)，无遗留键）。UI 完全数据驱动自端点，无硬编码行清单。i18n 类型标签键 `oa.notify.type.branchPruned` 等五键落 E-T1 seed（非本任务）。
2. **branchPruned 遗留回落**：brief 给定的 verbatim 模型 else 分支对无遗留键类型用 `email: eventOn && emailOn`，会被遗留扁平 `email:false` 误伤——与后端 `IsEnabled` line 63（无遗留键 → 双通道**无条件** true）不一致。已将 else 分支拆为「有遗留键=原语义回落 / 无遗留键=双开」以**逐位镜像后端**，并加测试 pin 该差异（遗留 `email:false` 不波及 branchPruned）。4 个既有遗留键类型行为与 brief 给定模型完全一致，brief 原 4 用例零改动通过。
3. **noUncheckedIndexedAccess 严格模式**：模板 v-model 目标与 test 赋值处按 repo 既有口径（波③ E-T2 先例）加非空断言 `!`（`matrixState[row.typeKey]!.inApp` / `s.flowRejected!.email`）。

## Gates
- `git show --stat HEAD`：仅 brief 的 4 个前端文件（+201/-60）。后端未触碰。

## Concerns
- 无。i18n 键（`oa.notify.matrix.*` / `oa.notify.type.*` / `oa.notify.matrix.saveOk`/`resetOk`/`unsupported`/`reset`）依赖 E-T1 seed 落地，落地前页面显示原始键——属计划内跨任务依赖。
