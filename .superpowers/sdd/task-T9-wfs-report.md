# Task T9 报告：错误边（IsError）画布视觉区分（danger 虚线，Design System token）

**Status: DONE** — commit `4b90463`（已 push，分支 feat/wfs-cleanup-tickets）

## 核实证据（实现前）
- `designerModel.ts:84-90` `schemaToGraph` 建边：`isError` 仅塞进 `edge.data`，无 `style/class`——渲染成默认灰边，与普通边零视觉区分。行号与 brief 完全一致（无漂移）。
- `graphToSchema:113-118` 只读 `data.isError`，不读 `style`——样式纯呈现层，round-trip 无损。
- token 存在：`tokens.css:14` `--cp-danger:#E5484D`。属性面板复选切换经 `graphToSchema→父→schemaToGraph` 重建，样式随之刷新。

## 红→绿
- **红**：新增 `error edge visual` 用例后跑 spec，`err.style?.stroke` = undefined ≠ `var(--cp-danger)`，1 failed（预期）。
- **实现**：`schemaToGraph` edges map 加条件展开——`e.isError === true` 时注入 `style: { stroke: 'var(--cp-danger)', strokeWidth: 2, strokeDasharray: '6 4' }, class: 'edge-error', animated: false`。零硬编码色（走 token）。
- **绿**：目标 spec 5 passed。

## 验证
- 前端全量：`npx vitest run` → **401 passed**（60 文件；基线 400，+1）。
- type-check：`vue-tsc --build` 0 错。
- build：`npm run build` ✓ built in 6.44s。
- diff scope：仅 2 文件（`designerModel.ts` +4 行 / `designerModel.serviceTask.spec.ts` +26 行），全在 `views/oa/designer/**` 白名单内，零跨模块污染。

## 疑虑
无。纯前端呈现层改动，后端/迁移不动，round-trip 由既有 `serviceTask round-trip` 用例保障。
