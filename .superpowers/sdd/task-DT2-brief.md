# Task D-T2: serviceTask 自定义节点组件 + 画布接线

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md；spec 章节 §5.2）

**Files:**
- Create: `cp6.web/src/views/oa/designer/nodes/ServiceTaskNode.vue`
- Modify: `cp6.web/src/views/oa/designer/DesignerCanvas.vue`(注册节点类型 + 调色板渲染 serviceTask)

- [ ] **Step 1: 实现** — 仿既有 Start/Approval/Gateway/End 自定义节点(带 `<Handle>` 入/出),按 `data.serviceKind` 显示标签/图标/颜色(spec §5.2)。`DesignerCanvas.vue` 的 `:node-types` 注册 `serviceTask: ServiceTaskNode`;调色板拖拽 project() 落点生成 serviceTask 节点带预置 kind。
- [ ] **Step 2: 验证** — `npm run type-check` + `npm run build`(确认无 TS/编译错;Vue Flow 节点渲染 smoke 留 QA)。
- [ ] **Step 3: commit** — `git commit -m "feat(wfs-service-task): D-T2 ServiceTaskNode 自定义节点+画布接线"`

## 视觉纪律（2026-07-05 补充，必须遵守）
- **CP6 Design System v1.0 已在 OA 模块全面落地**（OA 迁移批次4 刚完成 Designer 族 8 文件 token 化）：所有颜色必须用 `--cp-*` token，**零硬编码 hex/rgba**。既有四节点的语义色裁决：start→ok(绿)/approval→info(蓝)/gateway→warn(橙)/end→muted(灰)，选中光环用 color-mix 50% 先例。serviceTask 节点的三 kind 配色需与该语义体系协调（例如 dataWriteback/webApi/timer 各自可辨识但同属一个色彩语言），不与既有四节点冲突。
- 参考既有节点文件：`nodes/StartNode.vue`/`ApprovalNode.vue`/`GatewayNode.vue`/`EndNode.vue`——结构、Handle 用法、class 命名、token 引用方式全部照族内惯例。
- 主控代理会用 frontend-design skill 对视觉方案把关，你先按上述纪律实现。

## 落码纪律
- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`。本地 commit 不 push。
- 零 Space 污染。视图文案全 `t()` 运行时键（`oa.designer.svc.*`，E-T2 才 seed，先用键名占位是预期）。
- type-check 用堆 8192：`$env:NODE_OPTIONS='--max-old-space-size=8192'`。
- npm run build 同样带堆 8192（build = type-check + build-only）。

## 视觉方案（主控代理经 frontend-design skill 定稿，2026-07-05——按此实现，不再自行发挥）

**命题**：设计器的语义轴是"谁执行这一步"。人类节点（填單/審批）实线、机器节点（serviceTask）虚线——执行主体的区分写进笔触本身。

1. **家族色**：三 kind 共用 `--cp-brand` / `--cp-brand-bg`（青，唯一未被节点占用的色相；danger 红保留给失败语义/错误边，绝不用于 kind）。**不给三 kind 发三个色相**。
2. **签名元素**：`border: 2px dashed var(--cp-brand)`（对比人类节点实线）+ label 左侧一个 16px 图标 chip（`<span class="node-kind-icon">`，背景 `--cp-brand-bg`、圆角 `--cp-r-sm` 减半或 4px 等 token 值，内容用字符：dataWriteback「⤓」webApi「⚡」timer「⏱」）。双重编码（线型+图标）不依赖色相辨识。
3. **kind 副标签**：仿 ApprovalNode 的 `.node-strategy` 位置显示 kind 的 t() 标签（`t('oa.designer.svc.kind.dataWriteback')` 等运行时键），色 `--cp-brand`。
4. **其余逐项照 ApprovalNode.vue 惯例**：`background: var(--cp-brand-bg)`、`border-radius: var(--cp-r-sm)`、`padding: 8px 16px; min-width: 130px`、选中态 `box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-brand) 50%, transparent)`、Handle 上入下出、class 命名 `vf-node-service` + `vf-node--selected`。
5. 调色板项渲染：DesignerCanvas 调色板里 serviceTask 三项的圆点色用同一 brand 青（与节点身份一致）；若既有圆点实现是内联 style/死 color 字段，跟随 D-T1 的处置（新项不带 color 字段，点色在组件层用 token）。
