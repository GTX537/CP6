# Task 9 报告：Space 波5 单格补码 UI

**Status: DONE**（含主控裁决后的修正轮，最终见文末「修正」节；commit=`e445565`+`12a34ad` 均已 push）

## Commit
- `e445565` feat(space-web): 波5 单格补码UI——BindCodesDialog行内gen-code接线（已 push → feat/space-wave5）
- 仅 3 文件入库：`BindCodesDialog.vue` / `BindCodesDialog.spec.ts` / `docs/seeds/space-i18n-seed-2.sql`
  （task-8/9-brief.md 的 working-copy 改动系并行 Task 8 所致，未纳入本提交）

## 实现要点
- `BindCodesDialog.vue`：unplaced 列表下新增「待绑定库位（可补码）」区，逐行渲染 locationCode +
  行内「补码」按钮 `v-permission="'space-code-rule:generate'"`。
- 点击 → `codeRuleApi.genSingle(row.id)`；成功：`genCodes[id]=新码`（el-tag 内联展示）+
  `ElMessage.success` + `await loadUnplaced()` 刷新；失败：`catch{}` 静默（http 拦截器已弹业务码，照生命周期页范式）。
- 防连点：`genInflight = reactive(new Set)`，按钮 `:loading`+`:disabled`，`handleGenSingle` 头部重入守卫 `if(has) return`。
- 会话隔离：watch 开弹时清空 `genInflight`/`genCodes`（loadUnplaced 保持纯，避免刷新抹掉刚设的新码）。
- 零硬编码色：新区 CSS 全用 Element Plus 主题变量（`--el-border-color`/`--el-fill-color-light`/`--el-text-color-*`）。
- i18n：`space.bind.unplacedTitle`/`genSingle`/`genDone` 三键五语，MERGE 追加 `space-i18n-seed-2.sql`（照 T7 先例，计数注释 131→134）。

## 测试
- 新增 `BindCodesDialog.spec.ts` 3 用例（先红后绿）：①点补码→genSingle 调 1 次(row.id)+行内新码+刷新列表 ②连点期间按钮 disabled 且只调 1 次+完成后恢复 ③缺 generate 权按钮从 DOM 移除（行仍在）。
  - el-dialog 默认 teleport 到 body，故查询走 `document`、点击用原生 `.click()`（disabled 按钮不触发，佐证防连点）。
- 全量：**388 passed（60 files）**，基线 385 → +3。
- type-check：`vue-tsc --build` 0 错。

## 疑虑（需上游关注，非本任务范畴）
- **后端契约与挂载面语义不吻合**：`GenSingleAsync`（CodeEngineService.cs:345）守卫
  `if (loc.RackId==null || !loc.Placed) throw E-SPACE-301`，而本弹窗 unplaced 列表恰为
  `Status==1 ∧ Placed==false`（GetUnplacedAsync:621-623）。故对这些行调 genSingle 运行时必抛
  E-SPACE-301，被拦截器静默——UI 接线正确但对目标行是 no-op。推测意图为「先绑定/放置后再补码」
  或后端守卫待放开（该服务标注「最小实现 TODO」）。已按 brief 精确接线且失败路径优雅降级；
  建议末波收口时对齐后端语义或调整挂载策略。

---

## 修正（主控裁决：后端守卫正当，错在挂载点）

**Commit: `12a34ad`** fix(space-web): 波5 单格补码迁至属性面板rack分支——unplaced恒失败死UI撤除（已 push）

- **撤除**：BindCodesDialog.vue 完整还原至 pre-task 状态（git show b316ba9 逐字节还原），BindCodesDialog.spec.ts 删除（unplaced 行 Placed==false 恒抛 E-SPACE-301 = 死 UI，不留）。
- **迁入** `PropertiesPanel.vue` rack 分支（Task 8 四态面板）：只读尺寸下方新增 `uncodedLocs` 区 =
  `store.scene.locations` 过滤 `rackId===选中rack.id ∧ placed ∧ locationCode 空`（字段名照 LocationVO 实际：`placed`/`locationCode`）。每行 `col - level - depth` + 补码按钮 `v-permission="'space-code-rule:generate'"`。
- 点击 `codeRuleApi.genSingle(loc.id)` → 成功：**直改 store 该库位 locationCode**（生码=后端持久化动作，不进命令栈 undo；测试断言 `canUndo===false`）→ computed 过滤随之**消行** + `ElMessage.success`；失败：catch 静默（拦截器已弹）。`genInflight` Set 防连点（loading+disabled+重入守卫）。无无码子库位时整区 `v-if` 不渲染。
- **i18n**：seed 行随语义迁移 `space.bind.*` → `space.rack.uncodedTitle`/`genSingle`/`genDone`（五语 MERGE，计数注释同步）。
- **零硬编码色**：新区 CSS 全用面板既有 `--cp-line`/`--cp-muted`/`--cp-text`/`--cp-bg-hover` 变量。
- **测试迁移**（先红后绿，PropertiesPanel.spec.ts +5）：①只列已落位无码行（有码/未落位/他架不列）②点击调 genSingle(loc.id) 一次+store code 更新+消行+canUndo=false ③连点禁用只调一次+完成消行 ④缺权按钮移除行仍在 ⑤无无码行整区不渲染。
- **全量**：**390 passed（59 files）** = 基线 385 − 撤 3 + 加 5；type-check `vue-tsc --build` 0 错。
- 挂载面与后端契约现已吻合：GenSingleAsync 守卫 `RackId!=null ∧ Placed==true`，本区对象恰为 placed 且挂在选中 rack 下的无码库位。

## 报告路径
`C:\CP6\.superpowers\sdd\task-9-report.md`
