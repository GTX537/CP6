# Task D-T2 报告：ServiceTaskNode 自定义节点 + 画布接线

## 改动文件清单
- **Create** `cp6.web/src/views/oa/designer/nodes/ServiceTaskNode.vue` — serviceTask 自定义节点组件（虚线机器节点，图标 chip + kind 副标签）。
- **Modify** `cp6.web/src/views/oa/designer/DesignerCanvas.vue`：
  - import ServiceTaskNode；`#node-serviceTask` slot 注册。
  - 拖拽：`dragType:string` → `dragKey:string`（`serviceTask:<kind>` 复合键，区分同 type 三 kind）；新增 `paletteKey()` 辅助；`onPaletteDragStart` 改传整个 palette item；`onCanvasDrop` 按 key 精确匹配 palette 项，serviceTask 落点在 `data` 预置 `serviceKind`（D-T1 graphToSchema 随 data 透传，round-trip 成立）。
  - 调色板 dot：新增 `.dot-serviceTask { background: var(--cp-brand); }`。

## 验证命令与输出
- `npm run type-check`（NODE_OPTIONS=--max-old-space-size=8192）：**通过**，vue-tsc --build 无错误输出。
- `npm run build`（同堆）：**通过**，`✓ built in 6.59s`。仅有既存的 chunk >500kB 警告（非本任务引入，走 stderr 被 PowerShell 包装为 NativeCommandError 显示，实际 exit 0）。

## 视觉方案五条逐条落实
1. **家族色**：三 kind 共用 `--cp-brand`/`--cp-brand-bg`，无第二色相；danger 红未使用。✅
2. **签名元素**：`border: 2px dashed var(--cp-brand)`（对比人类节点实线）+ label 左侧 16px `.node-kind-icon` chip（bg `--cp-brand-bg`、`border-radius: 4px`、字符 dataWriteback「⤓」/webApi「⚡」/timer「⏱」）。线型+图标双重编码。✅
3. **kind 副标签**：`.node-strategy` 位置（仿 ApprovalNode），`t('oa.designer.svc.kind.<kind>')` 运行时键，色 `--cp-brand`。✅
4. **其余照 ApprovalNode**：`background: var(--cp-brand-bg)`、`border-radius: var(--cp-r-sm)`、`padding: 8px 16px; min-width: 130px`、选中 `box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-brand) 50%, transparent)`、Handle 上入下出、class `vf-node-service` + `vf-node--selected`。✅
5. **调色板项**：serviceTask 三项圆点 `.dot-serviceTask` 用同一 brand 青，无 color 字段（跟随 D-T1 处置，点色在组件层 token 决定）。✅

## 自查发现
- 新文件 ServiceTaskNode.vue grep `#hex`/`rgba(`/`hsla(`：**零硬编码色值**，全部 `--cp-*` token。
- DesignerCanvas 新增样式仅 `var(--cp-brand)`，无硬编码。
- 主标签 fallback 用 `t('oa.designer.svc.title')`、kind 副标签用 `t('oa.designer.svc.kind.*')`，键 E-T2 才 seed，当前界面显示裸键为预期（brief 明确）。
- `serviceKind` computed 对缺失/未知值兜底为 `dataWriteback`，避免渲染时图标/标签 undefined。
- Vue Flow 实际渲染 smoke（拖拽落点、虚线外观）留 QA。
