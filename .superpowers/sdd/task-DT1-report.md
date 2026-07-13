# Task D-T1 Report — designerModel palette 两入口 + onBranchReject round-trip + validateClient E-WF-020/021 镜像 + notification 类型镜像

**Status: DONE / 全绿**
**Branch:** feat/wfs-kernel-hardening
（本报告覆盖旧 serviceTask 波 D-T1 报告——同名文件复用，内容为内核 hardening 波。）

## 改动清单（4 文件，零跨模块污染）
- `cp6.web/src/views/oa/designer/designerModel.ts` — `SchemaNode.onBranchReject?: 'cascade'|'prune'` + palette 两入口(`inclusiveSplit`/`inclusiveJoin`,无 color 字段对齐 serviceTask 先例) + `validateClient` 三块镜像(E-WF-020 / 021a/b / 021c)。
- `cp6.web/src/views/oa/designer/designerModel.test.ts` — :45 palette 类型清单断言随扩展补 2 类型（本波唯一允许的既有断言改动）。
- `cp6.web/src/views/oa/designer/designerModel.hardening.test.ts` — 新 vitest，8 用例（计划 Step 1 逐字）。
- `cp6.web/src/types/oa/notification.ts` — `NotificationType.BranchPruned = 5`。

## 红→绿证据
- 红（实现前）：`npm run test -- designerModel.hardening` = **Tests 6 failed | 2 passed**（round-trip 因 graphToSchema 全量 spread data 天然通过；valid-pair 亦通过；其余 6 项断言失败）。符合预期红。
- 绿（实现后）：`npm run test` = **Test Files 61 passed, Tests 409 passed**（基线 401 + 8 新），零失败零回退（timer/serviceTask/波① 错误边 style 全保留）。
- type-check：`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`（vue-tsc --build）= exit 0。
- build：`npm run build` = ✓ built in 10.31s（仅既有 chunk-size 警告，非错误）。

## onBranchReject round-trip 证据
`{ id:'g', type:'parallelSplit', onBranchReject:'prune' }` → `graphToSchema(schemaToGraph(schema))` → `back.nodes.find(n=>n.id==='g').onBranchReject === 'prune'`。字段无需在转换函数显式接线：schemaToGraph `data:{...n}` 全量入 data、graphToSchema `...(n.data as SchemaNode)` 全量回写，随 spread 透传。

## D-T2 要用的接口（前端契约，均在 designerModel.ts）
- `SchemaNode.onBranchReject?: 'cascade' | 'prune'` — 属性面板「分支驳回策略」段绑定字段；仅 `parallelSplit`/`inclusiveSplit` 合法（validateClient 已守 `errBranchReject`）。
- `NODE_PALETTE` 新增两条 `{ type:'inclusiveSplit', label:'包容分叉' }` / `{ type:'inclusiveJoin', label:'包容汇聚' }`（无 color 字段，视觉走 `.dot-inclusiveSplit`/`.dot-inclusiveJoin` token；DesignerCanvas 需注册节点模板 + 空心 dot 样式）。
- `validateClient` 新错误 key：`oa.designer.errInclusiveDefault` / `errInclusivePair` / `errBranchReject`（i18n 键待 E 波 seed）。
- validateClient 内联私有辅助 `bfsDepths`/`nearestCommonJoin`/`isJoinType`/`nodeType`（非导出），D-T2 无需依赖。

## 疑虑
无。default 边判据用 `!e.condition?.trim()`（空串/纯空白视为无条件），与后端 E-WF-020 语义一致；isError 边在 nearestCommonJoin 与 E-WF-020 出边计数中已 `!e.isError` 排除。
