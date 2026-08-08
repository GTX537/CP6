# E12-S02 脱敏历史任务数据集与回放时钟交付报告

- 状态：功能分支全量门禁、远端备份、no-ff 受控集成、合并态复验与临时资源清理全部完成
- 起始基线：`6d837d08a0e71b098774f8b5bd96a89095137565`
- 数据/时钟/迁移提交：`4fb6941d`
- API/UI/权限/SDK 提交：`d89919b8`
- no-ff 集成提交：`c8ccbf56`
- 原功能分支：`codex/space-e12-s02-historical-replay`（历史进入远端受控集成后已删除）
- Migration：`20260802212845_SpaceE12S02HistoricalReplayDataset`

## 1. 交付结果

E12-S02 在 E12-S01 的生产隔离场景分支上新增不可变历史任务数据集。内部规划人员只能向克隆成功、仍与生产指针隔离的场景导入最多 10,000 条任务；任务来源与目的逻辑库位必须存在于该场景固定快照且仍为 Active。数据集使用调用方稳定 `datasetId` 与规范化请求 SHA-256 哈希实现幂等：相同 ID/载荷返回 `Duplicate`，相同 ID/不同载荷稳定冲突。

导入合同只接受 64 位小写规范化 SHA-256 `taskToken`、可选 `workerToken` 和调用方不可逆脱敏确认，不提供订单、人员、物料或 SKU 原始标识字段。UTC 历史窗口最长 366 天，任务必须完全落在窗口内；任务类型、结果、时间、数量、位置和 token 均失败关闭。

## 2. 确定性回放时钟与生产护栏

- `SpaceReplayClock` 以 tick/decimal 倍速映射历史时间，不等待真实时间，不依赖系统睡眠或调度时序。
- 倍速范围为 `(0, 1000]` 且最多四位小数；映射使用确定性 ToEven 舍入。
- 原始与回放时间、固定来源哈希、定义版本、脱敏版本、任务顺序和请求哈希均持久化为不可变规划证据。
- 数据集与任务禁止修改或物理删除；服务、响应和 UI 均明确 `ProductionWriteAllowed = false`。
- 本卡不运行距离/拥堵/容量/吞吐/成本仿真，不创建或修改库存、订单、任务、WCS/PDA 指令，也不提供生产回写或发布入口。

## 3. API、权限、数据库与 SDK

内部端点为：

- `PUT /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/historical-datasets/{datasetId}`
- `GET /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/historical-datasets/{datasetId}`
- `GET /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/historical-datasets`

写入要求 `space:planning:dataset:create`，读取要求 `space:planning:dataset:read`；外部主体在数据访问前失败关闭。Design V1 OpenAPI 从 71 增至 74 个唯一 operation，C# 与 TypeScript 生成客户端已同步且漂移检查通过。

Migration 新增 `Space_PlanningHistoricalDataset` 与 `Space_PlanningHistoricalTask`。两表使用租户查询过滤、复合外键、不可变检查约束、数据集+顺序与数据集+token 唯一索引；数据集同时绑定场景分支、模型和场景版本。生成式 EF migration、快照与从 E12-S01 到 E12-S02 的增量幂等 SQL 均已提交。

## 4. 前端与五语

规划场景列表仅对 `Ready + Succeeded + productionIsolated` 分支开放历史数据面板。面板支持列表、不可变回放证据读取、JSON 粘贴/文件选择、脱敏确认和导入；确认前按钮不可用，并始终显示“永不写入生产”。

新增 25 个 `space.planningDataset.*` 词条，并为场景面板新增 3 个历史数据入口词条；28 个词条全部提供简中、繁中、英文、日文和韩文运行时种子及完整性测试。静态 i18n 门禁仍精确报告 908 项既有欠账，本卡净新增欠账为 0。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 264 passed / 0 failed |
| Space Integration 默认全集 | 239 passed / 0 failed / 63 SQL 环境门禁 skipped |
| CP6.Tests 全量 | 2,767 passed / 0 failed / 17 环境门禁 skipped |
| 前端全量 | 121 files / 667 tests passed |
| 前端严格类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution 非增量 Release | 0 error / 10 条既有 warning |
| `SpaceContext` 与 `CP6Context` EF pending model | 均无待迁移模型变化 |
| Design V1 SDK drift | passed；74 unique operations |
| TypeScript SDK strict no-emit | passed |
| i18n 静态门禁 | 908 项既有欠账；本卡净新增 0 |
| Git 差异检查 | passed |

默认集成测试未连接 SQL Server，因此 63 项 SQL 测试按既有约定跳过，不能记作通过。本卡新增迁移由 EF 工具生成并通过双 Context pending-model 门禁，但发布前仍需在发布 SQL Server 环境执行迁移/回滚演练与完整真实 SQL 矩阵。

合并树与功能 tip `d89919b8` 文件树一致。受控集成态再次通过：领域 3/3、服务 4/4、权限/契约/OpenAPI/种子 64/64、前端 2 files / 5 tests、类型检查、双 EF、SDK drift、TypeScript SDK strict no-emit 与 Git 差异检查。

## 6. 明确未做与下一步

本卡不计算路线距离、拥堵、容量、吞吐、人工或距离成本，不提供运行参数、仿真结果或方案排名；这些属于 E12-S03。也不做多方案对比/决策记录、交换格式导出、DWG 回写、自动生产同步、自动合并或生产发布。

下一张可独立实施卡为 E12-S03“距离、拥堵、容量、吞吐和成本仿真”。E03-S04、E04-S05、E06 与 E13 CAD 后续链继续等待正式黄金集、授权供应商证据及冻结 Worker；E09 产品/QA/WMS/安全 GA 签字继续由发布治理完成。

## 7. 远端备份与资源清理

功能 tip `d89919b8` 已先推送远端备份，再以 `--no-ff` 合入并推送 `integration/space-v1-20260730`，集成提交为 `c8ccbf56`。确认功能 tip 是远端集成分支祖先且工作树干净后，已删除远端功能分支、本地功能分支和功能工作树；受控集成历史完整保留。本轮移除的功能工作树占用 2,177,363,070 字节（约 2.03 GiB），`main` 未被本轮操作修改。
