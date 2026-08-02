# E12-S04 多场景比较与决策记录交付报告

- 状态：功能实现、全量门禁、远端备份、no-ff 受控集成与合并态复验完成，临时资源清理待本报告后续记录
- 起始基线：`6d13d7da0b63ae0f023724ba12a3f1a458fcb2c7`
- 功能提交：`7b919b4b`
- no-ff 集成提交：`577168e3`
- 功能分支：`codex/space-e12-s04-comparison-decision`
- Migration：`20260802224514_SpaceE12S04PlanningComparisonDecision`

## 1. 交付结果

E12-S04 在 E12-S01～S03 的生产隔离场景、不可变脱敏历史数据集和确定性仿真证据之上，新增多场景证据比较与人工决策记录。每次比较固定 2～10 个不同规划分支的运行、显式人工基线、调用方风险阈值、逐项指标/基线差值、风险标记和 SHA-256 比较哈希。

比较引擎不计算加权总分，不排名、不选优、不生成推荐。人工决策仅允许 `Selected`、`Deferred` 或 `RejectedAll`，必须填写理由；后续记录必须引用并替代当前链头。历史比较与决策均不可修改或删除，且不会写入、合并或发布到生产。

## 2. 可比性、风险与决策边界

- 每个比较要求 2～10 个不同运行且分别属于 2～10 个不同生产隔离规划分支，并固定同一 Site、Model 和基础 Published 版本。
- 仿真必须共享定义版本、几何口径、币种、三类成本费率、吞吐时间桶、任务数及已完成任务/数量证据。
- 历史数据集必须共享来源数据哈希、历史窗口、任务数、数据定义和脱敏版本；不把不同样本的结果伪装成同口径比较。
- 距离、拥堵、峰值容量、超载位置、平均吞吐和总成本按显式基线给出差值；原始指标与运行结果哈希一并保存。
- 风险只由调用方给出的最低距离覆盖、最高峰值容量、最高拥堵任务小时及可选总成本阈值产生。容量假设与基线不同时追加信息标记，不隐藏输入差异。
- 基线始终由人指定，风险标记不是推荐；界面查看证据时也不会预选决策方案。
- 决策链追加写：第一条不得替代历史，后续条目必须替代唯一当前链头；选择方案时只能引用本比较内的运行。

## 3. API、权限、数据库与 SDK

内部端点为：

- `PUT /api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}`
- `GET /api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}`
- `GET /api/space/planning/v1/sites/{siteId}/comparisons`
- `PUT /api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}/decisions/{decisionId}`
- `GET /api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}/decisions/{decisionId}`
- `GET /api/space/planning/v1/sites/{siteId}/comparisons/{comparisonId}/decisions`

比较读取/创建分别要求 `space:planning:comparison:read/create`，决策读取/创建分别要求 `space:planning:decision:read/create`。所有端点仅允许内部主体，租户、站点、分支、版本、运行和数据集血缘在证据读写前失败关闭；调用方稳定 ID 以规范化请求哈希实现幂等，相同 ID/不同载荷稳定冲突。

Migration 新增 `Space_PlanningComparison`、`Space_PlanningComparisonEntry`、`Space_PlanningComparisonRisk` 和 `Space_PlanningDecisionRecord` 四张租户隔离表，并为仿真运行补充证据复合候选键。表结构包含复合租户外键、唯一基线、唯一决策替代边、检查约束、查询索引和上下文不可变保护；EF migration、模型快照与增量幂等 SQL 均已提交。

Design V1 OpenAPI 从 77 增至 83 个唯一 operation，C# 与 TypeScript 生成客户端已同步并通过漂移与严格编译门禁。

## 4. 前端与五语

规划场景页新增比较工作区：可从 Ready/Succeeded/Isolated 分支选择完成的运行，输入比较名称，显式指定基线和四类风险阈值。证据矩阵同时展示原始指标、相对基线差值、阈值风险、结果哈希、无自动排名和无生产回写护栏。

同一工作区提供人工结果、可选方案和理由录入，显示追加式决策历史及替代关系。新完成的分支状态会自动刷新候选，但系统不会默认选择任何决策方案。

新增 47 个 `space.planningComparison.*` 词条，全部提供简中、繁中、英文、日文和韩文运行时种子及完整性测试。静态 i18n 门禁仍精确报告 908 项既有快照欠账，本卡净新增欠账为 0。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 272 passed / 0 failed |
| Space Integration 默认全集 | 245 passed / 0 failed / 63 SQL 环境门禁 skipped |
| CP6.Tests 全量 | 2,775 passed / 0 failed / 17 环境门禁 skipped |
| 前端全量 | 123 files / 674 tests passed |
| 前端聚焦与严格类型检查 | 3 files / 10 tests passed；type-check passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution 非增量 Release | 0 error / 10 条既有 warning |
| `SpaceContext` 与 `CP6Context` EF pending model | 均无待迁移模型变化 |
| Design V1 SDK drift | passed；83 unique operations |
| TypeScript SDK strict no-emit | passed |
| i18n 静态门禁 | 908 项既有欠账；本卡净新增 0 |
| Git 差异检查 | passed |

新增比较引擎测试 4/4、服务测试 3/3、合同测试 3/3；权限/合同/OpenAPI/五语聚焦 66/66 通过。默认集成测试未连接 SQL Server，因此 63 项 SQL 测试按既有约定跳过，不能记作通过；发布前仍需在发布 SQL Server 环境执行迁移/回滚演练、决策并发冲突和完整真实 SQL 矩阵。

## 6. 明确未做与下一步

本卡不做自动评分、方案排名、自动推荐、自动批准、场景合并、生产回写或生产发布，也不扩大 S03 的直线距离、参数化拥堵/容量和规划成本口径。

下一张可独立实施卡为 E12-S05“标准交换格式导出”。E03-S04、E04-S05、E06 与 E13 CAD 后续链继续等待正式黄金集、授权供应商证据及冻结 Worker；E09 产品、QA、WMS 和安全 GA 签字继续由发布治理完成。

## 7. 远端备份与资源清理

功能分支最新 tip `a9298bad`（含交付文档）已推送远端备份，并以 `--no-ff` 合入受控集成，集成提交为 `577168e3`。合并树与功能 tip 文件树一致；合并态再次通过引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 66/66、前端 3 files / 10 tests、类型检查、双 EF、SDK drift、TypeScript SDK strict no-emit 与 Git 差异检查。功能工作树及本地/远端临时分支清理和实际释放空间将在完成后补记；`main` 不在本卡操作范围内。
