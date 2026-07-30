# E07-S01～S03 WMS 适配器合同、CP6 实现与标准模拟器交付报告

- 状态：已完成并合入 Space 集成分支
- 工作分支：`codex/space-e07-wms-contract-adapters`
- 开发基线：`integration/space-v1-20260730@9e270629`
- 功能提交：`d06a8bd1`
- no-ff 集成提交：`6e67a9d1`
- 合同版本：`space-wms-adapter-v1`
- Migration：`20260730161925_SpaceE07S02WmsAdapterLedger`

## 1. 本次交付范围

本次只完成冻结 Backlog 的 E07-S01～S03：

- E07-S01：定义版本化 WMS 适配器能力合同；
- E07-S02：实现 CP6 WMS 适配器和持久化幂等操作账本；
- E07-S03：实现可编程故障注入的标准内存模拟器。

以下内容没有提前并入本卡：

- E07-S04 标准 WMS 数据集；
- E07-S05 适配器采用与切换；
- E08 运行时服务；
- Workload、Warehouse Activity 等运行分析模型；
- E13 推荐执行；
- 发布 Saga、公开 HTTP API 或 UI。

## 2. 版本化能力合同

`ISpaceWmsAdapter` 同时提供能力、健康、预检、批次写入、操作状态、回读和阻塞引用查询。
`ISpaceWmsRuntimeSource` 是只读运行时子合同，只暴露库位、库存和任务查询，避免调用方通过
运行时数据源越权获得发布写入能力。

合同冻结以下语义：

- 租户、站点、仓库和关联 ID 必须显式传递；
- 操作键格式为
  `space:{tenantId}:{siteId}:{publishAttemptId}:{batchNo}`；
- 操作键、能力、变更项和批次载荷均使用确定性哈希；
- 同一操作键和同一载荷必须重放原始结果；
- 同一操作键配不同载荷必须零副作用失败；
- 缺失、重复或不匹配的回执一律归类为 `Uncertain`；
- 成功回执必须包含外部库位 ID、外部版本和响应哈希；
- 同租户不同站点的操作状态和回读请求以
  `SPACE_WMS_OPERATION_SCOPE_DENIED` 失败关闭；
- 数据源元数据明确区分 `Real` 与 `Simulated`，模拟数据不得冒充生产观测。

## 3. CP6 WMS 适配器

`Cp6SpaceWmsAdapter` 直接映射现有 `WmsBin`、`Stock`、`OutboundOrder`、
`OutboundOrderDetail` 和 `Pallet`：

- 能力等级为 `CertifiedIdempotent`；
- 单批上限 500；
- 库位码沿用 CP6 WMS 的 30 字符约束；
- 支持创建、更新、禁用和恢复，不声明不具备的重命名能力；
- 禁用前检查库存、活动出库任务和未发运容器；
- 查询结果携带 `CP6_WMS` 真实来源标记；
- 关系型数据库写入使用 `Serializable` 事务；
- 若共享 `CP6Context` 已存在未提交写入，以
  `SPACE_WMS_CONTEXT_DIRTY` 拒绝执行，防止适配器的 `SaveChanges` 顺带提交调用方状态。

新表 `T_SpaceWmsOperation` 持久化操作键、载荷哈希、终态、外部操作 ID、完整结果和观测时间。
唯一索引为 `(TenantId, OperationKey)`。重试先查账本，再决定重放、冲突或执行新批次。

## 4. 标准 WMS 模拟器

`StandardSpaceWmsSimulator` 是内存实现，按租户、站点和仓库隔离状态：

- 能力等级为 `CertifiedAtomic`；
- 单批上限 1,000；
- 与 CP6 适配器共享完全相同的合同、哈希和幂等语义；
- 可预置库位、库存和任务；
- 支持 `Unavailable`、`Timeout`、`RejectAll`、`Partial` 和
  `UnknownAfterApply` 故障；
- 可重置单一隔离域，不影响其他租户或站点；
- 运行时来源固定标记为 `STANDARD_WMS_SIMULATOR/Simulated`。

生产依赖注入仍默认解析 `Cp6SpaceWmsAdapter`。模拟器只以显式具体类型和控制接口注册，不会替换
生产适配器。

## 5. 数据库变更与回滚

Migration 只创建 `T_SpaceWmsOperation` 及其租户级唯一索引，没有创建 Workload、活动分析、
推荐或发布编排表。EF 检查结果：

`No changes have been made to the model since the last migration.`

代码回滚时先停止新的 Space WMS 写入，再回滚适配器注册和应用代码。数据库向下迁移会删除
`T_SpaceWmsOperation`；执行前必须保留操作账本审计副本，因为删除后历史幂等回执不可恢复。

## 6. 验证证据

| 验证层 | 结果 | 说明 |
|---|---:|---|
| Space UnitTests | 73 passed | 包含合同、哈希、回执分类与跨站点操作键隔离 |
| Space IntegrationTests | 35 passed / 30 SQL-gated skipped | CP6 适配器、模拟器、脏上下文和隔离边界均通过 |
| CP6.Tests | 2674 passed / 17 environment-gated skipped | 主回归无失败 |
| CP6.Client.Tests | 71 passed | 客户端回归无失败 |
| 全解决方案 Release build | succeeded / 0 errors | 7 个既有警告位于本卡未改动的测试代码 |
| Space IntegrationTests Release build | succeeded / 0 warnings / 0 errors | E07 项目链路构建通过 |
| EF model check | no pending model changes | Migration、Designer 与 Snapshot 一致 |

新增 SQL Server 门禁测试
`Cp6SpaceWmsAdapterSqlServerTests.Migration_adapter_and_transaction_contracts_close`
验证真实迁移、唯一操作键和事务合同。当前机器仍无法通过既有 SQL Server 认证门禁，因此该测试与
其他 29 个 SQL 门禁测试记为 `skipped`，不计作通过。

## 7. 后续边界

下一步应先完成 E07-S04 标准数据集，给 CP6 与标准模拟器提供同一组可复验的位置、库存、任务和
故障场景。E07-S05 还依赖 E04-S04，不能因为本次适配器已经可用而提前宣告采用完成。
E08、E13 和发布编排只能依赖本合同，不得把模拟来源或不确定回执提升为生产成功。
