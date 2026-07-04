# CP6 UI 风格翻新 · 设计决策 Spec

日期：2026-07-04 · 状态：待用户批准后进入实施计划

## 背景与目标

现有 UI 为通用后台模板风（深色侧栏 + 硬编码 `#409eff`），无任何主题体系（无 variables、无暗色模式），204 个视图样式散落硬编码。目标：在其他模块前端（波次拣货、Space 3D 发布 UI、OA 后续）尚未编码的窗口期，建立统一设计系统并全站翻新为「专业化科技感的柔和 SaaS」风格，使未来所有模块保持一致体验。

## 已确认的决策

| 决策点 | 结论 |
|---|---|
| 翻新深度 | 全站（C 方案），但以「token 层 → 模板层 → 分模块迁移」三步实现，非逐页手绘 |
| 风格方向 | 以用户 demo（柔和 SaaS）为参照做专业化改造：保留青绿主色/悬浮侧栏/柔和阴影；emoji→线性图标、糖果色降级为图表与状态色、数据页高密度克制 |
| 模板共享 | 新建 CpListPage 等 8+ 模板组件收敛 130+ 个手写 el-table 查询页；现存 VolTable/VolForm 废弃 |
| 视觉基准 | `picture/mockup-final-a-dashboard.html`、`picture/mockup-final-b-wms-list.html`（已用户验收） |
| 规范载体 | **《CP6 Design System v1.0》= `docs/CP6_Design_System_v1.0.md`**，为 UI 唯一事实来源，本 spec 不重复其内容 |
| 暗色模式 | v1.0 只建两级 token 结构与文件占位，不实现切换 |
| 字体 | Nunito（本地打包，不依赖 CDN）只管拉丁与数字 + 系统中文栈；数字 tabular-nums |

## 架构（三层）

1. **tokens**：`src/styles/tokens.css` / `tokens-dark.css` / `element-overrides.css` / `transitions.css`，`--cp-*` 变量 + Element Plus 变量映射
2. **组件**：`components/base/`（CpButton/CpTable/CpTag 等薄封装）+ `components/templates/`（CpPageShell/CpListPage/CpFormDialog/CpStatCard 等）
3. **页面**：views 只写布局，禁止硬编码视觉值；特殊页面（3D/画布/流程设计器）只强制 token 与基础件

## 实施阶段（每阶段可独立合并上线）

1. tokens + overrides + LayoutView 重做 + Dashboard 重做（全站观感变 70~80%）
2. base/templates 组件落地 + 1~2 个 WMS 页试点验证 CpListPage API
3. 分模块批量迁移（WMS→ERP→OA→FIN→PMS→MES→其余），每模块一个 PR，同步清除硬编码色值
4. 新功能自阶段 2 后直接按规范开发

## 错误处理与测试要点

- CpListPage 的 `fetch` 数据源函数统一处理 loading/错误提示/空态（CpEmpty）
- 每个模板组件配 Vitest 单测（props 渲染 + 事件）；迁移页面以 gstack 截图对比验收关键页
- 回归风险最大点：LayoutView 重做需保留现有菜单数据结构（localStorage.menus）、i18n 语言切换、通知铃、模拟登录横幅（ImpersonationBanner）功能不变

## 非目标（v1.0 不做）

暗色模式实现、页面级入场动画、图标按需加载优化、移动端专项重设计（沿用现有 main.css 响应式策略并适配新组件）
