# Space Studio 底图图层控制实现报告

日期：2026-08-15
任务分支：`codex/space-studio-underlay-layer-controls`

## 结论

详细 Spec LM-FR-020 的仓库实现已闭环。PDF/PNG/JPG 底图挂接后，用户可在工作台“图层”模式中显示或隐藏底图、以 0～100% 调整透明度，并锁定或解锁底图标定。控制会立即作用于真实 Konva 底图画布，并按版本与楼层保存到当前浏览器标签页的视图状态。

这些设置是个人视图偏好，不修改 Draft、不推进 Floor/Content Revision，也不建立第二套设计权威。底图来源、标定和撤销/重做仍完全沿用 Design V1 权威链。

## 实现范围

- 把原先写死的“底图”图层复选框替换为可访问的显示、透明度和锁定控件；无底图时控件失败关闭。
- 工作台将控件绑定到既有 `UnderlayStage.setLayerState`，显示和透明度变化会立即重绘实际栅格图层。
- 锁定状态阻止标定入口；新挂接底图自动解锁以完成首次标定，成功保存标定后自动锁回。显式移除仍保留二次确认与可撤销历史。
- 扩展现有 floor view schema v1 的可选 `underlay` 字段，按 `versionId + floorLogicalId` 保存显示、透明度和锁定状态；旧 schema v1 数据继续兼容，损坏或越界状态拒绝加载。
- 切换楼层、重新加载页面及恢复 2D/3D 视角时，同步恢复对应楼层的底图视图状态。

## 验证

- 图层面板与 floor view 聚焦单测：8/8 通过；Web Vitest 全量 824/824 通过。
- Vue TypeScript 检查与 production build 通过。
- Space Studio Playwright 全量 22/22 通过；新场景覆盖显示、实际画布像素变化、透明度、锁定、标定门禁和页面重载恢复，既有底图上传/标定链继续通过。
- 完整 `dotnet build CP6.slnx -c Release --no-restore` 通过，0 warning / 0 error。
- GA 证据校验通过并继续派生 `NoGo`：5 类外部输入、9 个门禁和 5 个签字人仍 Pending。

## 边界与后续

- 本项关闭 LM-FR-020 的仓库实现，不把模拟附件或浏览器自动化当作真实 PDF/PNG/JPG 现场验收。
- WP4 继续保持 `Partial/Pending`，核心 GA 继续保持 72% / `NoGo`。
- 下一独立任务继续审计 LM-FR-021～029，优先检查 LM-FR-022 的托盘与六类静态设备是否具有完整组件库、业务编码和 2D/3D 同源证据。
