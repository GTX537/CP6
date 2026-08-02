# E10-S01 PDA/定位人员事件契约、状态与时间语义完成报告

- 状态：**Ready for controlled integration**
- 日期：2026-08-02
- 功能分支：`codex/space-e10-s01-personnel-events`
- 实现提交：`1c7aa0e2`
- 集成目标：`integration/space-v1-20260730`

## 1. 交付结论

E10-S01 已建立 PDA/定位人员事件的受控写入边界：版本化事件合同、明确的
`Real`/`Simulated` 来源、追加式事件账本，以及按人员维护的当前位置和工作
状态投影。Space 只消费来源系统明确上报的位置或工作状态，不猜测人员位置，
也不把模拟数据伪装成真实数据。

本卡只完成事件契约、状态和时间语义，不提前实现 E10-S02 的实时查询、授权
轨迹、轨迹保留周期或 UI。

## 2. API 与事件合同

新增 Design V1 操作：

- `POST /api/space/design/v1/sites/{siteId}/personnel-events`
- operationId：`IngestPersonnelEvents`
- 权限：`space:integration:manage`
- 审计操作：`space.personnel-events.ingest`
- 成功响应：`202 Accepted`

合同版本固定为 `space-personnel-event-v1`，单批 1～500 条。来源类型只允许
`Real` 或 `Simulated`；同一站点和 `SourceId` 不能在两者之间切换。事件类型
只允许：

- `PositionObserved`：必须提供 `LocationLogicalId`，或
  `FloorLogicalId + XYZ`；坐标必须成组出现；
- `WorkStateChanged`：必须提供 `Unknown`、`Offline`、`Idle`、`Busy` 或
  `Break`，且不能混入位置字段。

`SourceId`、`SourceEventId` 和 `PersonExternalId` 经过 trim + uppercase
规范化。时间必须带 UTC `+00:00`，允许离线 PDA 补传历史事件，但拒绝超过
服务器时间五分钟的未来事件。

## 3. 幂等、乱序与投影语义

幂等键是业务合同中的 `TenantId + SiteId + SourceId + SourceEventId`，不额外
依赖 HTTP `Idempotency-Key`：

- 相同事件 ID 和相同规范化载荷返回 `Duplicate`；
- 相同事件 ID 复用为不同载荷返回稳定 `409`；
- 新事件写入账本后返回 `Accepted` 或 `AcceptedStale`；
- 并发唯一索引冲突会清理跟踪状态并重试一次，使并发重放收敛到同一事件。

当前位置与工作状态使用彼此独立的游标，排序键为：

1. `OccurredAtUtc`；
2. 可选 `SourceSequence`（同时间下有序序列优先）；
3. `SourceEventId` 字典序确定性兜底。

因此，迟到事件仍作为事实进入追加式账本，但不会把较新的位置或工作状态回退；
较旧的位置事件也不会阻止较新的工作状态，反之亦然。可选 `UserId` 可从空值
绑定一次，已绑定人员不能重新映射到另一用户。

## 4. 数据与安全边界

Migration `20260802125928_SpaceE10S01PersonnelEvents` 新增：

- `Space_PersonnelEvent`：追加式事件事实，含规范化来源、来源事件 ID、人员
  外部 ID、来源/接收时间、可选位置和 SHA-256 规范化载荷哈希；
- `Space_PersonnelState`：每个租户、站点、来源、人员一行的当前投影，位置和
  工作状态分别保存事件、时间和序列游标，并使用 rowversion。

数据库同时冻结来源/事件/工作状态枚举、位置/状态互斥形状、非负序列和精度、
事件幂等唯一索引及当前投影唯一索引。`SpaceContext` 禁止修改或删除事件历史，
也禁止重分配当前投影的租户、站点、来源、来源类型、人员身份和已绑定用户。

服务层要求非空租户和真实操作者，外部主体在读库前被显式拒绝，并继续使用
现有站点访问判断；EF 查询过滤器和写入保护器提供租户隔离。

## 5. OpenAPI 与 SDK

Design V1 从 59 增至 60 个稳定 operation。OpenAPI 明确冻结必填字段、可空
位置/人员映射字段及 decimal 坐标格式，响应只返回事件收据和计数，不含推测
位置、人员显示名或其他扩展个人资料。C# 与 TypeScript SDK 已重新生成，均
提供 `IngestPersonnelEvents`。

## 6. 验证证据

| 检查 | 结果 |
|---|---|
| E10-S01 领域聚焦测试 | 3/3 passed |
| E10-S01 服务/EF 聚焦测试 | 7/7 passed |
| 权限与 OpenAPI/SDK 聚焦测试 | 43/43 passed |
| Space UnitTests | 234/234 passed |
| Space IntegrationTests（默认） | 175 passed / 58 SQL-environment skipped / 0 failed |
| 本卡真实 SQL 迁移与唯一约束 | 2/2 passed |
| 完整 Space 真实 SQL 矩阵 | 231 passed / 1 既有基线失败 / 0 skipped |
| CP6.Tests 全量 | 2734 passed / 17 environment-gated skipped / 0 failed |
| 完整 `CP6.slnx` Release 非增量构建 | 0 errors / 10 existing warnings |
| EF model drift | 无待生成模型变更 |
| OpenAPI / C# / TypeScript SDK drift | passed；Design V1 为 60 operations |
| TypeScript SDK strict no-emit | passed（TypeScript 5.9.3） |
| E13-S16 → E10-S01 幂等迁移脚本 | generated and structurally validated |
| `git diff --check` | passed |

完整真实 SQL 矩阵中的唯一失败为既有
`SpaceExcelPreflightSqlServerTests.Sql_start_atomically_pins_source_job_and_idempotency`，
失败发生在测试种子一次性新增 `SpaceModel` 和 `SpaceModelVersion` 的既有循环
外键图。本卡未改动该图；同一用例已在未包含 E10-S01 的集成基线
`e8d4e1c2` 独立复现。E10-S01 新增的真实 SQL 迁移和唯一约束用例均通过。

## 7. 明确未提前实现

- E10-S02 的人员实时位置读取 API、场景叠加、轨迹查询和授权；
- 轨迹保留/删除策略、停留和拥堵计算；
- PDA 或定位供应商适配器、设备注册和凭据管理；
- 从任务、WMS、摄像头或几何邻近关系推测人员位置/忙闲状态；
- WCS/IoT 设备事件、AGV、输送设备或告警（E10-S03/S04）；
- 任何人员调度、任务下发或设备控制。
