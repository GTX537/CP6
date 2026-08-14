# Space Studio 2D 画布拖动精调

日期：2026-08-14

范围：Space Studio V1 核心 GA / WP1 手工建模交互

结论：2D 画布中的 Rack 和通用 Element 已可直接拖动；落点以整数毫米写入现有 `MoveObject` 命令链，并支持多选整体移动、撤销、重做和失败回到权威场景。Zone/Aisle 继续通过 Design V1 Layout 属性合同修改，不被伪装成通用 Element。

## 行为与围栏

- 屏幕位移按当前 Zoom 转换为世界毫米，Y 轴遵循画布翻转约定。
- 已选对象再次按下不会破坏多选集合；拖动其中一个对象时，同组 Rack/Element 生成一个可逆命令批。
- Ctrl/Shift/Meta 仍只负责选择切换，不会意外提交拖动。
- 拖动开始后暂时关闭新画布编辑，提交完成或失败后再按当前租约/只读状态恢复。
- 请求必带 `leaseId`、`clientInstanceId`、Floor Revision、Content Revision、Content Hash 和稳定 Command Batch；撤销提交反向 `MoveObject`，不做客户端假回滚。
- 保存失败、租约丢失或 Revision 冲突时，画布重新渲染当前权威场景，并保留既有恢复命令包语义。

## 自动化

- 拖动坐标和可逆批次聚焦单测：14/14。
- 前端全量 Vitest：780/780。
- Space Studio Playwright：14/14；其中拖动→租约命令→场景刷新→撤销连续重复 5/5。
- Vue TypeScript 检查通过。
- Node 24.19.0 / pnpm 11.19.0 生产构建通过。

## 接受边界

该报告证明仓库内交互、合同和自动化，不替代 1440×900/1280×720 的独立人工 UX、辅助技术签字或双仓 Pilot。真实 Provider、黄金 CAD、生产 WMS 和正式签字仍为 Pending，因此核心 GA 保持 72% / No-Go。
