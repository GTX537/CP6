# E10-S03 WCS/IoT 设备事件契约与设备主数据映射完成报告

- 状态：**Feature complete / pending controlled integration**
- 日期：2026-08-02
- 功能分支：`codex/space-e10-s03-device-events`
- 实现提交：`10b16c51`
- 集成目标：`integration/space-v1-20260730`

## 1. 交付结论

E10-S03 已建立 WCS/IoT 设备接入的受控边界：设备来源身份先映射到当前
Published 场景中的稳定设备元素 LogicalId，之后才允许写入版本化、追加式设备
事件账本。Space 不再把旧 `WmsDeviceQuery` 的空占位当作生产设备事实，也不从
几何或 WMS 任务推测设备状态、位置或告警。

本卡只完成事件合同和设备主数据映射，不提前生成“当前设备状态”投影，也不修改
3D Viewer；AGV/输送设备实时位置、状态和告警叠加属于 E10-S04。

## 2. API 与权限

Design V1 新增四个 operation：

- `GET /api/space/design/v1/sites/{siteId}/device-mappings`
  - 权限：`space:model:read`；
  - 支持 `sourceId`、`limit` 和受保护游标；
- `POST /api/space/design/v1/sites/{siteId}/device-mappings`
  - 权限：`space:integration:manage`；
  - 审计：`space.device-mapping.create`；
- `PUT /api/space/design/v1/sites/{siteId}/device-mappings/{mappingId}`
  - 权限：`space:integration:manage`；
  - 审计：`space.device-mapping.update`；
  - 使用 base64 rowversion 防止并发覆盖；
- `POST /api/space/design/v1/sites/{siteId}/device-events`
  - 权限：`space:integration:manage`；
  - 审计：`space.device-events.ingest`；
  - 成功返回 `202 Accepted`。

外部主体在服务读库前显式拒绝。所有操作继续先执行站点访问判断；任意来源或
设备业务身份都位于查询/请求体中，不作为路由段。

## 3. 设备主数据映射

映射身份为 `TenantId + SiteId + SourceId + DeviceExternalId`，其中来源和外部
设备 ID 执行 trim + uppercase 规范化。同一来源下，一个外部设备只能有一个
映射，一个 Space 元素也不能同时绑定给两个外部设备。

绑定目标必须是当前 Published 版本中的 Active 通用元素，并按设备类型失败关闭：

- `Conveyor` 只映射 `Conveyor` 元素；
- `Workstation`、`Lift`、`Sorter` 使用各自允许的 `Device`、
  `StaticEquipment`、`Workstation`、`Elevator` 或 `Conveyor` 子集；
- `Agv`、`StackerCrane`、`Sensor` 和 `Other` 映射到 `Device` 或
  `StaticEquipment`。

映射保存稳定 `ElementLogicalId`，同时记录最后验证的 Published Version、楼层和
元素类型。新事件写入前会再次解析当前 Published 元素；元素被移除或类型不再
兼容时，映射固定报 stale 并要求显式重新验证，不静默转向其他几何。

## 4. 事件合同、时间与幂等

合同版本固定为 `space-device-event-v1`，单批 1～500 条；来源类型只允许
`Real` 或 `Simulated`，已经映射的来源不能切换真实性。事件类型和严格形状为：

- `PositionObserved`：只含 Location，或 Floor + 完整 XYZ；可带非负精度；
- `OperatingStateChanged`：只含 `Unknown`、`Offline`、`Idle`、`Running`、
  `Paused`、`Faulted` 或 `Maintenance`；
- `AlarmRaised`：只含告警外部 ID、代码、`Info`/`Warning`/`Critical` 和可选
  受限消息；
- `AlarmCleared`：只含告警外部 ID。

位置 Floor/Location 必须存在于当前 Published 版本，二者同时出现时必须属于同一
楼层。坐标固定使用毫米，必须成组三值且在受支持范围内。

事件时间必须显式使用 UTC `+00:00`；允许离线补传历史事件，拒绝超过服务器
时间五分钟的未来事件。可选 `SourceSequence` 必须非负。枚举只接受名称，不接受
`0` 等数值别名。

幂等键为 `TenantId + SiteId + SourceId + SourceEventId`：相同规范化载荷重放
返回 `Duplicate`，相同事件 ID 复用为不同载荷固定 `409`。并发唯一索引冲突清理
跟踪状态后重试一次，使并发重放收敛；事件响应不回显告警消息或其他载荷正文。

## 5. 数据与安全边界

Migration `20260802141148_SpaceE10S03DeviceEvents` 新增：

- `Space_DeviceMapping`：来源设备到稳定 Space 元素的租户权威映射，含 Published
  验证快照和 rowversion；
- `Space_DeviceEvent`：位置、状态和告警事实的追加式账本，保存映射/设备/元素
  快照、来源/接收时间及 SHA-256 规范化载荷哈希。

数据库冻结来源、设备、事件、状态和告警枚举，事件互斥形状、XYZ 三值、非负
精度/序列、映射唯一性、事件幂等唯一索引，以及映射到版本/元素、事件到映射的
复合租户外键。`SpaceContext` 禁止修改或删除事件历史，也禁止重分配映射的租户、
站点、来源真实性和外部设备身份。

## 6. OpenAPI 与 SDK

Design V1 从 62 增至 66 个稳定 operation。OpenAPI、C# SDK 和 TypeScript SDK
已重新生成；请求必填字段、可空事件形状、decimal 坐标、rowversion 和 202 响应
均被冻结。SDK 漂移检查与生成 TypeScript strict no-emit 通过。

## 7. 验证证据

| 检查 | 结果 |
|---|---|
| E10-S03 服务/EF 聚焦测试 | 6/6 passed |
| E10-S03 真实 SQL 映射与事件账本 | 1/1 passed |
| 权限、审计与 OpenAPI 聚焦测试 | 70/70 passed |
| Space UnitTests | 234/234 passed |
| Space IntegrationTests（默认） | 186 passed / 60 SQL-environment skipped / 0 failed |
| 完整 Space 真实 SQL 矩阵 | 245 passed / 1 既有基线失败 / 0 skipped |
| CP6.Tests 全量 | 2738 passed / 17 environment-gated skipped / 0 failed |
| 完整 `CP6.slnx` Release 非增量构建 | 0 errors / 10 existing warnings |
| EF model drift | 无待生成模型变更 |
| OpenAPI / C# / TypeScript SDK drift | passed；Design V1 为 66 operations |
| TypeScript SDK strict no-emit | passed |
| `git diff --check` | passed |

完整真实 SQL 矩阵中的唯一失败仍为
`SpaceExcelPreflightSqlServerTests.Sql_start_atomically_pins_source_job_and_idempotency`：
既有测试种子同时新增 `SpaceModel` 与 `SpaceModelVersion` 时触发循环外键图。该失败
已在不包含 E10 的旧集成基线独立复现；E10-S03 新增真实 SQL 用例通过。

## 8. 明确未提前实现

- 当前设备状态、当前位置、活动告警投影和读取 API；
- AGV、堆垛机、输送线或 IoT 告警的 3D 实时叠加（E10-S04）；
- MQTT、OPC UA、厂商 WCS/IoT 连接器、主题、凭据和重连管理；
- 告警确认、派单、控制命令或向 WCS/设备写回；
- 速度、停留、拥堵、可用率、OEE 或维护分析；
- 把旧 `WmsDeviceQuery` 空占位升级为真实生产源。
