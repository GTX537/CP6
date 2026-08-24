# Space Studio WP5 键盘与可达性验证记录

日期：2026-08-14

分支：`codex/space-studio-accessibility-ga`

基线：`origin/main@1a30a601`

## 范围

本切片只关闭 Space Studio 工作台可由仓库自动化验证的键盘、焦点、字号、主要热区、右侧面板主题一致性和窄屏问题定位行为。它不替代独立 WCAG 审计、真实辅助技术验收或 Iris Xe Viewer 性能测试。

## 已验证行为

- 检查器使用 `tablist`、`tab`、`tabpanel`，支持 ArrowLeft、ArrowRight、Home、End 与 roving `tabindex`。
- 2D/3D、选择、平移和测量公开选中状态；命令栏、标题状态、状态栏和 2D 画布具有对应可达语义。
- `G` 按 Blocking、Warning、Info 和稳定 ID 顺序循环定位 Open 问题，选择与 2D/3D 场景同步；窄屏只读状态不切出 3D。
- `?`/Shift+/ 打开完整快捷键帮助；输入框和弹窗内快捷键不会被工作台劫持。
- CAD 审核、Excel–CAD 匹配、属性和 WMS 核心面板使用 `--space-studio-*` token；问题说明和正文为 16px，元数据为 13–14px，主要按钮、输入和问题行不小于 44px。
- 390/420/440px 的旧面板固有宽度在 324px 检查器内被父工作台收敛。

## 自动化证据

- `npm run type-check`：通过。
- `npm run test:unit -- --run`：146 files，754 tests passed。
- `npm run build`：通过；保留既有大 chunk 提示，无新增构建错误。
- `npx playwright test e2e/space-studio.spec.ts --project=space-studio-mocked`：9/9 passed。
- `git diff --check`：通过。

## 未关闭门槛

- Iris Xe/WebGL2、500 货架/10,000 库位的首次交互、帧时间、拾取和批量着色基准。
- 独立工具生成的 4.5:1 对比度报告，以及真实键盘、屏幕阅读器/辅助技术人工验收。
- 真实 Provider、黄金 CAD、WMS 恢复、双仓 Pilot 和五方 GA 签字。
