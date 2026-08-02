# E12-S03 距离、拥堵、容量、吞吐和成本仿真交付报告

- 状态：功能实现、全量门禁、远端备份、no-ff 受控集成与合并态复验完成，临时资源清理待本报告后续记录
- 起始基线：`1650e8ba240cb7263970b376c4990f4c579224b3`
- 功能提交：`ab21aed4`
- no-ff 集成提交：`f2d68897`
- 功能分支：`codex/space-e12-s03-planning-simulation`
- Migration：`20260802221548_SpaceE12S03PlanningSimulation`

## 1. 交付结果

E12-S03 在 E12-S01 生产隔离场景与 E12-S02 不可变脱敏历史数据集上新增同步、确定性的分析仿真。内部规划人员以调用方稳定 `runId`、固定场景内容修订、历史数据集请求哈希和显式参数生成不可变运行证据；同一 ID/同一载荷幂等返回，同一 ID/不同载荷稳定冲突。

仿真只读取场景快照和脱敏历史任务，不等待真实时间、不读取实时 WMS/WCS、不创建或修改库存、订单、任务、场景版本或生产指针。每次运行持久化参数、五类汇总、逐位置证据和 SHA-256 结果哈希，`ProductionWriteAllowed` 始终为 `false`。

## 2. 仿真口径与边界

- 距离：使用来源/目的库位所属货架格口锚点的同层欧氏直线距离；同一位置固定为 0。跨层、缺来源锚点或缺货架锚点记为未知，并报告已知距离任务覆盖率；不冒充巷道路由距离。
- 拥堵：按历史任务执行区间在目的位置的重叠计算，输出超过调用方并发容量后的拥堵墙钟秒和超额任务秒；不做实时交通、排队网络或设备动力学模拟。
- 容量：使用历史并发任务数量和调用方声明的默认/逐位置任务数量容量，输出峰值并发数量和利用率；不把货架 `MaxLoad` 误作任务容量。
- 吞吐：用数据集精确历史秒数计算已完成任务/数量的平均每小时值，并按调用方 `1..1440` 分钟固定桶计算峰值；展示小时数的舍入不参与速率计算。
- 人工：相同 SHA-256 worker token 的重叠区间取并集，未分配 worker 的任务区间独立计入，避免重复累计同一人员并行任务时间。
- 成本：仅由调用方明确给出的距离单价、人工小时单价和拥堵任务小时单价构成，并保留调用方币种；这是规划估算，不是财务实际成本。

最短历史窗口为 1 秒。仿真不进行高精度物理/交通求解、不排名方案、不写回生产；多方案比较和决策记录属于 E12-S04。

## 3. API、权限、数据库与 SDK

内部端点为：

- `PUT /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/simulation-runs/{runId}`
- `GET /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/simulation-runs/{runId}`
- `GET /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/simulation-runs`

创建要求 `space:planning:simulation:create`，读取要求 `space:planning:simulation:read`；外部主体、跨租户/站点、非生产隔离分支、错误场景/数据集血缘和越界容量参数均在读写证据前失败关闭。Design V1 OpenAPI 从 74 增至 77 个唯一 operation，C# 与 TypeScript 生成客户端已同步。

Migration 新增 `Space_PlanningSimulationRun` 与 `Space_PlanningSimulationLocationResult`。历史数据集增加 `(TenantId, Id, BranchId, ModelId, ScenarioVersionId)` 复合候选键；运行表以复合外键绑定同一数据集/分支/模型/场景，逐位置结果再以复合外键绑定运行/场景和场景位置。两表具备租户过滤、检查约束、唯一索引和不可变保护；容量利用率使用 `decimal(38,4)` 覆盖合法极值。EF migration、模型快照与增量幂等 SQL 均已提交。

## 4. 前端与五语

历史数据面板新增仿真入口，可选择数据集，配置默认数量容量、默认并发容量、吞吐窗口、币种、三项单价和可选逐目的位置容量覆盖 JSON。结果显示五类 KPI、热点位置、结果哈希、场景内容修订和“不得生产回写”护栏。

新增 41 个 `space.planningSimulation.*` 词条，全部提供简中、繁中、英文、日文和韩文运行时种子及完整性测试。静态 i18n 门禁仍精确报告 908 项既有快照欠账，本卡净新增欠账为 0。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 268 passed / 0 failed |
| Space Integration 默认全集 | 242 passed / 0 failed / 63 SQL 环境门禁 skipped |
| CP6.Tests 全量 | 2,771 passed / 0 failed / 17 环境门禁 skipped |
| 前端全量 | 122 files / 670 tests passed |
| 前端聚焦与严格类型检查 | 3 files / 7 tests passed；type-check passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution 非增量 Release | 0 error / 3 条既有可空性 warning |
| `SpaceContext` 与 `CP6Context` EF pending model | 均无待迁移模型变化 |
| Design V1 SDK drift | passed；77 unique operations |
| TypeScript SDK strict no-emit | passed |
| i18n 静态门禁 | 908 项既有欠账；本卡净新增 0 |
| Git 差异检查 | passed |

新增引擎测试 4/4、服务测试 3/3、合同测试 3/3；权限/合同/OpenAPI/五语聚焦 65/65 通过。默认集成测试未连接 SQL Server，因此 63 项 SQL 测试按既有约定跳过，不能记作通过；发布前仍需在发布 SQL Server 环境执行迁移/回滚演练与完整真实 SQL 矩阵。

## 6. 明确未做与下一步

本卡不做巷道路由求解、实时拥堵预测、设备动力学、财务实际成本、方案排名或自动推荐，不提供生产回写、自动合并或生产发布入口。

下一张可独立实施卡为 E12-S04“多场景比较与决策记录”。E03-S04、E04-S05、E06 与 E13 CAD 后续链继续等待正式黄金集、授权供应商证据及冻结 Worker；E09 产品/QA/WMS/安全 GA 签字继续由发布治理完成。

## 7. 远端备份与资源清理

功能分支最新 tip `2cd1faed`（含交付文档）已推送远端备份，并以 `--no-ff` 合入受控集成，集成提交为 `f2d68897`。合并树与功能 tip 文件树一致；合并态再次通过引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 65/65、前端 3 files / 7 tests、类型检查、双 EF、SDK drift、TypeScript SDK strict no-emit 与 Git 差异检查。功能工作树及本地/远端临时分支清理和实际释放空间将在完成后补记；`main` 不在本卡操作范围内。
