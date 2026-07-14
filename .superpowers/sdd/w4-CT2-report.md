# Task C-T2 报告 — FormDetail 堆叠 + 审批操作钉底栏 + 对话框全屏化

**STATUS: DONE**
**Commit:** `99fd054be8337a34a32362b33b420d3d5ea1ac89`（已 push → feat/wfs-inbox-ux）
**分支:** feat/wfs-inbox-ux

## 改动（严格按 brief，代码逐字）
1. **FormDetail.vue Step1** — 左右 `el-col` 由 `:span="14"`/`:span="10"` 换为响应式 `:xs="24" :sm="14"` / `:xs="24" :sm="10"`。≥768px 走 sm 值，与原 span 等价。
2. **FormDetail.vue Step2** — `.action-bar` 模板不动（`v-if="myTaskId"` 保留）；`<style scoped>` 尾部追加 `@media (max-width:767px)` 块：detail-left 去右边框/右内距、detail-right 解除 max-height+改纵向堆叠、`.action-bar` sticky 钉底栏（安全区 `env(safe-area-inset-bottom)` + `margin: 16px -16px 0` 贴满抽屉）+ `.action-bar .el-input width:100%!important` 覆盖行内 280px。
3. **TransferDialog.vue Step3** — 脚本引入 `useBreakpoint` + `const { isMobile }`；`el-dialog` `width="440px"` → `:width="isMobile ? '100vw' : '440px'"` + `:fullscreen="isMobile"`。
4. **SendBackDialog.vue Step3** — 同 TransferDialog（实读现值确认 440px）。

## Gates
- `bun run test -- --run`: **432 passed / 65 files**（基线 432，零回归）。
- `bun run type-check`: 0 error（vue-tsc --build 干净）。
- `bun run build`: EXIT=0（build 经 run-p 内含 type-check，一并通过）。
- `git show --stat HEAD`: 仅 brief 三文件（FormDetail 34、SendBackDialog 5、TransferDialog 5），40 insertions / 4 deletions。

## 逐文件桌面像素纯度声明（HARD gate）
- **FormDetail.vue** — 纯净。模板改动为 el-col 原生响应式属性，桌面（≥768px）走 sm 值与原 span 14/10 逐像素等价；CSS 仅新增 `@media (max-width:767px)` 尾块，桌面分支字节保留。
- **TransferDialog.vue** — 纯净。width 由字面量改属性三元 `isMobile ? '100vw' : '440px'`，桌面（isMobile=false）恒求值 '440px'；`:fullscreen="isMobile"` 桌面恒 false（等价原无 fullscreen）。无 CSS 改动。
- **SendBackDialog.vue** — 纯净。同 TransferDialog：桌面三元恒 '440px'、fullscreen 恒 false。无 CSS 改动。

## Concerns
- 无。后端未触（2016/5skip 不受影响）。i18n 键在 E-T1，本任务无硬编码文案/颜色（全走 `var(--cp-*)`，`--cp-card`/`--cp-shadow-up`/`--cp-line` 均已在 tokens.css 定义并实证存在）。
