# E01-S04 从 Published 克隆 Draft 交付报告

- 状态：Ready for integration
- 工作分支：`codex/space-e01-s04-clone`
- 基线：`448ab7a3`
- Migration：`20260726085852_SpaceE01S04PublishedClone`
- 核验日期：2026-07-30

## 1. 受控提取结论

本卡从候选检查点 `0d25da4d` 重新构建，而不是整包复制候选工作树。重新生成的 Migration 和 Designer 在替换生成时间戳后，与候选 S04 对应文件逐字节等价；EF Snapshot 的模型主体也与 S04 Designer 等价。

提取中明确排除了后续任务叠加的以下内容：

- E05：`BeamHeight`、严格 Element Geometry、资产作用域与 Asset Library。
- E06：Historical Republish。
- E12：Planning Scenario Branch 与版本 Purpose。
- E01 S05/S06：HTTP API、Problem Details、文件安全与保留。

## 2. 交付范围

- 新增 Floor、Zone、Aisle、Rack、RackLevel、Location、Element Revision 与 ElementAttribute 八张 Design Snapshot 表。
- 新增 `Initializing`、`Failed`、`Abandoned` 版本隔离态和 `CloneOperationId` 幂等键。
- 在 Serializable 事务内校验当前 Published、分配单调版本号、预留唯一活动 Draft 并创建 `CloneVersion` Job。
- Clone Processor 在 Job Lease 围栏和单事务内：
  - 重建 Source、Revision、ElementAttribute 行 ID；
  - 保留业务 `LogicalId`；
  - 重映射 Source、Underlay 和 ElementAttribute 外键；
  - 完成 Initializing→Draft 与 Job/Attempt/Step→Succeeded。
- Queued/Running 取消、最终失败和 DeadLetter 会把目标版本转为 Failed/Abandoned，并释放活动 Draft 预留。
- Published/Superseded 的 Source、Revision 和 ElementAttribute 通过 `SpaceContext` 拒绝写入。

## 3. 数据库结构证据

Migration 仅在 E01 S03 基线上执行以下变更：

- `Space_ModelVersion` 新增 nullable `CloneOperationId` 和租户/模型范围唯一索引。
- 新增 8 张 Snapshot 表。
- 新增 23 个索引、26 个外键及 Revision/Location/Rack 等约束。
- 不修改或删除 Legacy `Space_Site/Floor/Zone/Aisle/Rack/Location`。

`dotnet ef migrations has-pending-model-changes` 返回：

```text
No changes have been made to the model since the last migration.
```

## 4. 验证结果

| 检查 | 结果 |
|---|---|
| Space UnitTests | 41 passed |
| Space IntegrationTests | 9 passed，22 SQL-gated skipped |
| 新增 S04 真库用例 | 4 个，已编译；等待可认证 SQL 测试连接 |
| CP6.Tests | 2664 passed，17 environment-gated skipped |
| `dotnet build CP6.slnx -c Release` | 通过；0 errors，10 existing warnings |
| 聚焦格式检查 | 通过 |
| EF pending model changes | 无 |
| Migration/Designer 模型对照 | 与候选 S04 等价 |

2026-07-30 的本机 SQL 尝试在业务断言前被 TLS/SSPI/自动化执行身份认证阻断，因此不把 22 个门禁项记作已通过，也不把它们记作功能失败。

## 5. 权限、错误和回滚

- 本卡不新增 HTTP 入口；`space:model:edit`、Problem Details 和 API 幂等头由 E01 S05 接入。
- 使用冻结错误语义：
  - `SPACE_VERSION_CONFLICT`
  - `SPACE_VERSION_STATE_INVALID`
  - `SPACE_TENANT_SCOPE_DENIED`
- 失败摘要不包含数据库详情、文件路径或源内容。
- 回滚时先关闭新 Clone 入口并停止 Processor；未开始 Job 可取消并释放预留。生产数据库只使用向前修复 Migration。

## 6. 后续

1. 合入唯一集成分支并复跑聚焦测试。
2. 获得可认证 SQL 测试连接后补跑 22 个 Space 真库门禁。
3. 进入 E01 S05 Design API V1；不得同时带入 S06 或 E05+ 内容。
