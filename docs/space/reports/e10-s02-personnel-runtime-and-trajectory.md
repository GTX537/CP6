# E10-S02 人员实时位置、授权轨迹与 3D 叠加完成报告

- 状态：**Feature complete / pending controlled integration**
- 日期：2026-08-02
- 功能分支：`codex/space-e10-s02-personnel-runtime`
- 实现提交：`e70c2715`
- 集成目标：`integration/space-v1-20260730`

## 1. 交付结论

E10-S02 已在 E10-S01 的追加式人员事件账本和当前状态投影之上，完成内部人员
当前位置读取、受审计的授权轨迹查询，以及 Design V1 3D Viewer 人员图层。所有
位置只来自 `PositionObserved` 事件明确上报的 Floor/Location/XYZ；系统不从
WMS、任务、摄像头或几何邻近关系推测人员位置或工作状态。

返回合同只公开稳定的外部人员/来源身份、空间运行字段、来源事件身份和时间
证据，不返回姓名、邮箱或内部 `UserId`。外部主体在服务读库前被显式拒绝。

## 2. API、权限与查询边界

Design V1 新增两个 GET 操作：

- `GET /api/space/design/v1/sites/{siteId}/personnel`
  - 权限：`space:model:read`
  - 支持 `sourceKind`、`workState`、`floorLogicalId`、`limit` 和受保护游标；
- `GET /api/space/design/v1/sites/{siteId}/personnel/trajectory`
  - 人员和来源使用查询参数 `personExternalId`、`sourceId`，避免把任意业务身份
    放入路由段；
  - 权限：`space-audit:read`；
  - 审计操作：`space.personnel.trajectory.read`，资源为
    `PersonnelTrajectory`，读取审计失败关闭；
  - 只返回 `PositionObserved`，单次窗口最长 24 小时。

来源和人员外部 ID 继续执行 trim + uppercase 规范化。枚举过滤器只接受稳定名称，
拒绝数值别名；分页顺序和游标过滤哈希保持确定性。站点访问判断先于站点存在性
查询，继续避免跨租户或越权存在性泄露。

## 3. 当前状态、新鲜度与来源证据

当前位置响应同时提供：

- Floor/Location/XYZ/精度，以及位置和工作状态各自的发生、接收时间；
- 位置和工作状态各自的账本事件 ID、来源事件 ID；
- 位置/工作状态年龄与独立过期标记；
- `Real`/`Simulated` 来源及显式 `IsSimulated`。

当前新鲜度阈值固定为 5 分钟。过期数据仍返回并明确标记，不把“过期”伪装成
“无数据”，也不使用其他运行事实补位。SQL Server `datetime2` 物化后显式恢复
UTC Kind，真实 SQL 自动化已锁定该时区语义。

## 4. 轨迹保留语义

本卡把可查询轨迹限制在最近 30 天，并在响应中返回 `RetentionCutoffUtc`；早于
截止时间的请求固定拒绝。这里的 30 天是**可见查询保留期**，不是物理删除：
E10-S01 的事件账本继续保持追加式和离线补传语义。

物理归档、清除、法律保留例外和后台生命周期作业必须在后续独立卡中设计并
迁移，不能在本卡中静默删除事件事实。

## 5. 3D Viewer

高级面板新增人员图层开关、刷新、来源/人员稳定外部 ID、时间窗口及授权轨迹
加载。Viewer 最多分页读取 5,000 条并在达到显示上限时提示；切层、关闭图层、
并发旧响应和组件卸载都会清理状态及 Three.js GPU 对象。

当前人员只在活动楼层且来源事件包含完整 XYZ 时绘制；缺少 XYZ 的人员计入
“未定位”，界面明确提示未推断位置。颜色区分工作状态、过期和模拟来源。
轨迹线保存首尾来源事件 ID，便于把视觉结果追溯回追加式账本。

## 6. OpenAPI、SDK 与数据模型

Design V1 从 60 增至 62 个稳定 operation。OpenAPI、C# SDK 和 TypeScript SDK
已重新生成并通过漂移检查；生成版 TypeScript 通过 strict no-emit。

本卡只读取 E10-S01 已存在的 `Space_PersonnelEvent` 和
`Space_PersonnelState`，没有新增数据库实体或 Migration；EF 模型漂移检查确认
无待生成变更。

## 7. 验证证据

| 检查 | 结果 |
|---|---|
| E10-S01 + E10-S02 服务聚焦测试 | 12/12 passed |
| 权限、轨迹审计与 OpenAPI 聚焦测试 | 68/68 passed |
| E10-S02 前端 API/图层聚焦测试 | 2 files / 8 tests passed |
| 前端全量 | 113 files / 626 tests passed |
| 前端 strict type-check / production build | passed |
| Space UnitTests | 234/234 passed |
| Space IntegrationTests（默认） | 180 passed / 59 SQL-environment skipped / 0 failed |
| 本卡真实 SQL 查询与 UTC 语义 | 1/1 passed |
| 完整 Space 真实 SQL 矩阵 | 238 passed / 1 既有基线失败 / 0 skipped |
| CP6.Tests 全量 | 2736 passed / 17 environment-gated skipped / 0 failed |
| 完整 `CP6.slnx` Release 非增量构建 | 0 errors / 10 existing warnings |
| EF model drift | 无待生成模型变更 |
| OpenAPI / C# / TypeScript SDK drift | passed；Design V1 为 62 operations |
| TypeScript SDK strict no-emit | passed |
| `git diff --check` | passed |

完整真实 SQL 矩阵中的唯一失败仍为
`SpaceExcelPreflightSqlServerTests.Sql_start_atomically_pins_source_job_and_idempotency`：
既有测试种子同时新增 `SpaceModel` 与 `SpaceModelVersion` 时触发循环外键图。该失败
已在不包含 E10-S01/S02 的旧集成基线独立复现；本卡新增真实 SQL 查询和 UTC
用例通过。

## 8. 明确未提前实现

- 超过可见期事件的物理归档/删除、法律保留例外和生命周期 Worker；
- PDA 或定位供应商适配器、设备注册、凭据或连接管理；
- 速度、停留、拥堵、越界或人员行为分析；
- 人员调度、任务分配、消息下发或任何设备控制；
- 外部 Portal 人员数据开放；
- E10-S03 的 WCS/IoT 设备实时状态、AGV、输送设备和告警。
