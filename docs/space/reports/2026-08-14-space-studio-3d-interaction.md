# Space Studio 2D/3D 交互与逐楼层视角恢复证据

日期：2026-08-14
范围：WP5 仓库内 2D/3D 同源选择、3D 画布可达性与逐 Version/Floor 视角恢复

## 结论

本任务关闭以下仓库内功能缺口：

- 草稿 3D 对象可以直接拾取，并通过工作台既有选择权威同步到 2D 与检查器。
- 2D 选择继续驱动 3D 高亮；RackLevel 拾取归一到可编辑的父 Rack。
- 2D pan/zoom、投影模式及 3D camera/target 在当前浏览器标签页内按 Version+Floor 隔离保存与恢复。
- 相机拖动不会误触选择；3D 画布可由 Tab 聚焦并暴露操作说明。

这不是 Viewer 性能、生产 Published Viewer、独立 WCAG/辅助技术或核心 GA 证据。

## 实现边界

- 3D 拾取复用 `ParametricDesignSceneBuildResult.instanceToTarget/objectToTarget`，不引入平行 ID 或运行态模型。
- `RackLevel` 只作为渲染命中目标，交互选择回到其 `parentLogicalId` 对应 Rack。
- 普通点击执行 replace；Ctrl/Command 点击执行 toggle；指针位移超过 4px 视为 Orbit 操作，不执行选择。
- 视角状态 schema 固定为 v1；所有坐标必须是有限且有界数值，2D zoom 限制在 0.001–1。
- session key 为 `cp6-space-studio-floor-view-v1:{versionId}:{floorLogicalId}`。路由切换和卸载前同步 flush；不可用的浏览器存储不会阻断编辑。
- 场景在同一 Floor Revision 刷新时保持当前相机；切到新楼层且没有已存状态时重新 framing。

## 自动化证据

- `DesignScenePreview3DController.spec.ts`：视角 schema/数值边界、RackLevel → Rack 及不可编辑目标拒绝。
- `DesignScenePreview3D.spec.ts`：场景重建、相机恢复/变更事件、3D 点击与拖动区分。
- `floorViewState.spec.ts`：Version/Floor 隔离 key、有效状态解析、损坏/旧 schema/越界拒绝。
- `space-studio.spec.ts`：真实浏览器中切换 3D、选择俯视、保存 camera/target、刷新后恢复 3D 投影和画布。

门禁结果：

- Vue type-check：通过。
- Web 单元测试：148 files，761 passed。
- Web production build：通过；既有大 chunk 警告保持，不是本任务新增失败。
- Space Studio Playwright：10 passed。
- `git diff --check`：通过。

## 仍未关闭

- Iris Xe/WebGL2、500 货架/10,000 库位的首次交互、帧时间、拾取和批量着色正式性能门槛。
- Published-only 生产 Viewer 的真实部署/硬件验收。
- 4.5:1 独立对比度、真实键盘/辅助技术和人工 UX 签字。
- Provider、黄金 CAD、WMS 恢复、双仓 14 天 Pilot 与五方 GA 签字。
