# Space Studio 托盘与静态设备构件库实现报告

日期：2026-08-15
任务分支：`codex/space-studio-static-component-library`

## 结论

详细 Spec LM-FR-022 的仓库实现已闭环。现有 Design Layout 权威链继续负责 Zone、Aisle、Rack 及 Location；Space Studio 构件库在同一工作台中补齐墙、柱、门、月台、托盘以及固定的六类静态设备：输送线、AGV、叉车、工作台、电子秤和充电站。

这些预设只表达设计态静态几何、业务编码和自定义属性，不展示、仿真或承诺实时状态与运动。生产 Viewer 的 Published-only 边界未被改变。

## 实现范围

- 建立单一强类型构件目录，固定每个预设的界面名称、领域 `ElementType`、业务编码前缀、默认尺寸和设备子类。
- 预设复用已有领域类型：输送线为 `Conveyor`，AGV/叉车为 `Device`，工作台为 `Workstation`，电子秤/充电站为 `StaticEquipment`；不把界面预设名称渗入领域类型集。
- 每个新构件写入 `design.catalogPresetId` 和 `design.runtimeBehavior=Static`；六类设备另写入 `design.equipmentKind`，便于后续属性编辑和机器清单核对。
- 创建继续通过 Design V1 `CreateElement` 命令批，自动携带页面租约、Floor/Content Revision、Content Hash 和幂等标识。
- 创建立即加入公共撤销/重做历史；撤销使用 `DeleteObject`，重做使用原 LogicalId 的 `RestoreLogicalObject`，不重复分配身份。
- 2D 与草稿 3D 继续消费同一 Design Scene 和参数化几何计划，未增加第二份场景状态。

## 验证

- 构件目录与上下文面板聚焦单测：17/17 通过。
- Web Vitest 全量：837/837 通过。
- Vue TypeScript 检查与 production build 通过。
- Space Studio Playwright 全量：23/23 通过。新场景逐一创建托盘和六类设备，验证领域类型、编码前缀、设备子类、租约/Revision/Hash Fence、撤销/重做及 `2D 9 / 3D 9` 清单一致。
- 完整 `dotnet build CP6.slnx -c Release --no-restore` 通过，0 warning / 0 error。

## 边界与后续

- 本项只关闭 LM-FR-022 的仓库实现，不把 Mock E2E 当作真实 DWG/DXF、Excel、PDF/图片、Provider、WMS 或 Pilot 接受证据。
- WP4 继续保持 `Partial/Pending`，核心 GA 继续保持 72% / `NoGo`。
- 下一独立任务优先复核 LM-FR-021 两点实距标定、原点与旋转是否已经形成可操作、可恢复的完整证据。
