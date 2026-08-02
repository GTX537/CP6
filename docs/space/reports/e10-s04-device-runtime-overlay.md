# E10-S04 设备当前态与 3D 告警叠加完成报告

- 状态：**Integrated**
- 日期：2026-08-02
- 功能分支：`codex/space-e10-s04-device-runtime`
- 实现提交：`9a9802a8`
- 文档提交：`f961d7e5`
- no-ff 集成提交：`b4d5b81e`
- 集成目标：`integration/space-v1-20260730`

## 1. 交付结论

E10-S04 已把 E10-S03 的设备事件事实闭环为可读、可追踪且不会被乱序事件回退的
当前设备投影，并在 3D Viewer 中叠加 AGV、输送设备及其他已映射设备的位置、
运行状态和活动告警。

位置仍只信任来源事件的显式 XYZ。来源 XYZ 缺失时，Viewer 只使用当前 Published
版本中仍兼容的映射元素锚点，并明确记录 `MappedElement` 来源；它不会从任务、
轨迹、WMS 或几何邻近关系推断设备位置。

## 2. 当前投影与乱序规则

Migration `20260802144027_SpaceE10S04DeviceRuntime` 新增：

- `Space_DeviceState`：每个来源设备一行，位置和运行状态使用两个独立游标；
- `Space_DeviceAlarmState`：每个来源设备、每个外部告警身份一行，维护显式
  Raise/Clear 生命周期。

位置、运行状态和每个告警身份分别按 `OccurredAtUtc`、可选 `SourceSequence`、
`SourceEventId` 确定稳定顺序。迟到事件继续追加到 `Space_DeviceEvent`，回执返回
`AcceptedStale`、`ProjectionApplied=false`，但不会回退位置、状态或重新激活已被
较新 Clear 关闭的告警。台账和投影在同一个 Serializable 事务中提交；唯一索引、
复合租户外键、rowversion、检查约束和 `SpaceContext` 身份写保护共同失败关闭。

## 3. 当前设备读取 API

新增：

`GET /api/space/design/v1/sites/{siteId}/devices`

- 权限：`space:model:read`；外部主体在读库前拒绝；
- 支持 `sourceKind`、`deviceKind`、`operatingState`、`floorLogicalId`、
  `hasActiveAlarm`、`limit` 与受保护游标；
- 返回所有匹配映射，包括尚无事件的 `Unknown` 设备；
- 位置和状态新鲜度独立计算，默认阈值 5 分钟，过期值仍返回并显式标记；
- 返回当前 Published 映射是否仍有效、映射锚点、来源位置/状态事件 ID 与时间、
  活动告警严重度和 Raise/Clear 当前证据；
- `Real`/`Simulated` 始终逐设备显式返回，不把模拟数据伪装成生产事实。

Design V1 从 66 增至 67 个稳定 operation，OpenAPI、C# SDK 和 TypeScript SDK
已重新生成并通过 drift 与严格编译检查。

## 4. 3D Viewer

旧 `/api/space/floor/{floorId}/devices` 演示占位已从 Viewer 调用链移除。当前设备
图层改为分页读取 Design V1 API，最多 10 页、每页 500 条，并使用请求版本防止
切层或关闭图层后旧响应重新落图。

图层行为：

- 只绘制活动楼层；来源 XYZ 优先，当前 Published 元素锚点仅作显式回退；
- Running/Idle/Paused/Faulted/Maintenance/Offline/Unknown 使用稳定状态色；
- 模拟设备使用线框，过期位置或状态降低不透明度；
- 活动告警按 Info/Warning/Critical 着色并增加告警环；
- Three.js `userData` 保存 Mapping、来源、设备、元素、位置/状态事件和告警事件 ID；
- 切层、关闭和卸载会清理对象、Geometry 与 Material，不遗留 GPU 资源。

面板显示总数、已放置、来源 XYZ/Published 锚点数量、活动告警、过期、模拟与
新鲜度阈值；无法定位的项明确说明既无来源 XYZ，也无当前 Published 映射锚点。

## 5. 验证证据

| 检查 | 结果 |
|---|---|
| E10-S04 领域投影聚焦 | 2/2 passed |
| E10-S03/S04 服务与运行态聚焦 | 9/9 passed |
| E10-S04 真实 SQL 迁移、rowversion 与查询翻译 | 2/2 passed |
| 权限、审计与 OpenAPI 聚焦 | 70/70 passed |
| 前端 E10-S04 聚焦 | 14/14 passed |
| 前端全量 | 113 files / 629 tests passed |
| Space UnitTests | 236/236 passed |
| Space IntegrationTests（默认） | 189 passed / 60 SQL-environment skipped |
| CP6.Tests | 2738 passed / 17 environment-gated skipped |
| 完整 Space 真实 SQL 矩阵 | 248 passed / 1 已知基线失败 / 0 skipped |
| 完整 `CP6.slnx` Release 非增量构建 | 0 errors / 10 existing warnings |
| EF model drift | 无待生成模型变更 |
| OpenAPI / C# / TypeScript SDK drift | passed；Design V1 为 67 operations |
| 前端与 SDK TypeScript strict no-emit | passed |
| `git diff --check` | passed |

完整真实 SQL 矩阵的唯一失败仍为
`SpaceExcelPreflightSqlServerTests.Sql_start_atomically_pins_source_job_and_idempotency`：
既有测试种子同时新增 `SpaceModel` 与 `SpaceModelVersion` 时形成循环外键图。该失败
已在 E10-S03 之前的旧集成基线独立复现；本卡新增真实 SQL 用例均通过。

## 6. 明确未包含

- MQTT、OPC UA 或厂商连接器、Broker/Endpoint/凭据管理；
- 告警确认、派工、远程控制、控制写回或设备命令；
- 设备轨迹、历史回放、预测维护或告警分析；
- 从 WMS、任务或几何推断设备事实。

正式 backlog 的下一张独立卡为 E10-S05“货主、SKU、批次和容器空间筛选”。CAD/
E06 主链仍等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入，优先级与
失败关闭边界不因本卡改变。
