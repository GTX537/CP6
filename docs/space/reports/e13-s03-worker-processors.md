# E13-S03 Import/BuildScene Worker 处理器完成报告

- 状态：**Integrated**
- 日期：2026-07-30
- 功能分支：`codex/space-e13-s03-worker-processors`
- 功能提交：`cebd401a`
- no-ff 集成提交：`dca6e19c`
- 集成分支：`integration/space-v1-20260730`

## 1. 交付结论

E13-S03 已按冻结边界交付并进入唯一 Space 集成基线。Import 与
BuildScene 两类 Job 现在拥有显式处理器、固定步骤目录、类型过滤认领、
租约续期、取消检查、步骤级检查点、跨 Attempt 复用、单调进度、稳定失败
分类和依赖注入接线，不再落入默认“未知 Job 类型”分支。

本卡交付的是可恢复 Worker 控制面，不是假实现 CAD、Provider 或几何生成。
实际步骤执行通过两个端口注入；生产默认执行器失败关闭并返回
`SPACE_JOB_PROCESSOR_UNAVAILABLE`。跨租户调度、进程宿主和唤醒循环也不在
本卡范围内，调用方必须在已验证的租户作用域内显式运行处理器。

## 2. 冻结执行协议

### Import：6 步

1. `VerifySourceSafe`
2. `ConvertCad`
3. `ParseCadIr`
4. `BuildLayerAndBlockSummary`
5. `RunRuleRecognition`
6. `PersistArtifacts`

Import 只产生 Artifact，不直接写 Draft。

### BuildScene：12 步

1. `LoadPinnedInputs`
2. `LoadLockedFacts`
3. `EnforceTenantPolicyAndQuota`
4. `MinimizeStructuredFeatures`
5. `InvokeProvider`
6. `ValidateProviderOutput`
7. `FuseRulesAndAi`
8. `SynthesizeDeterministicGeometry`
9. `ValidateProposalSet`
10. `PersistProposalsAndIssues`
11. `RecordUsage`
12. `AwaitReview`

默认租约为 60 秒、心跳为 20 秒，Import 和 BuildScene 的硬超时均为
30 分钟。配置入口拒绝非正时长以及 `HeartbeatInterval >= LeaseDuration`。

## 3. 恢复、取消与失败语义

- Runner 只认领调用方指定的 `Import` 或 `BuildScene`，不会消费 FileScan、
  Clone、Apply 或其他 Job。
- 每步执行前续租并检查持久化取消请求；长步骤在每次心跳后再次续租和检查
  取消。
- 同一 InputHash 和 ProcessorVersion 的既有成功检查点可在新 Attempt
  中记录为 `Reused`；不重新调用执行器。
- 进度使用 `max(持久化进度, 当前步骤号)`，重试与复用不会让进度倒退。
- 当前运行步骤在取消或处理失败时先进入 `Failed`，Job 再进入取消或失败
  终态。
- 租约丢失和宿主停机不会伪写 Job 终态；由租约过期后的接管流程恢复。
- 硬超时稳定映射为 Resource / `SPACE_JOB_TIMEOUT`。
- 端口抛出的类型化错误只持久化安全错误码与摘要；未知异常统一映射为
  Bug / `SPACE_JOB_PROCESSOR_FAILED`，不泄漏原始异常文本。

## 4. 持久化与接线

`ISpaceJobLeaseStore` 新增：

- 按 JobType 集合过滤的认领重载；
- `ReuseStepAsync`；
- `FailStepAsync`。

`SpaceJobLease` 携带取消状态和持久化进度快照。EF 实现同时在正常认领和
最终过期租约处理查询中应用类型过滤，避免一个专用 Worker 收尾其他类型
的 Job。

依赖注入注册两个显式处理器和一个 scoped Runner。Import/BuildScene 步骤
执行器使用 `TryAddScoped` 注册失败关闭默认实现，后续卡片可显式替换端口，
不会因为注册顺序意外启用外部调用。

本卡没有修改 `SpaceContext` 模型，不产生 Migration、HTTP API、Provider
网络调用、Prompt/响应正文持久化或 Draft Apply。

## 5. 验证证据

| 检查 | 结果 |
|---|---|
| `dotnet build CP6.slnx -c Release --no-restore` | 0 errors，7 existing warnings |
| Space UnitTests | 126 passed；其中 E13-S03 新增 13 |
| Space IntegrationTests（默认门禁） | 45 passed，33 SQL-gated skipped |
| E13-S03 SQL Server 聚焦测试 | 3 passed，0 skipped |
| Space SQL 全量启用 | 78 项中 71 首轮通过；7 项在并行建库/删库压力下超时，逐项串行复跑全部通过 |
| CP6.Tests | 2680 passed，17 environment-gated skipped |
| CP6.Client.Tests | 71 passed |
| 精确 C# whitespace/style 与 staged diff | 通过 |

SQL 测试使用本机 `KOUSQLSERVER`、Windows 集成认证和每测试唯一临时数据库。
E13-S03 聚焦测试证明：

- Runner 完整持久化 Import 6 步和 BuildScene 12 步，同时保持 FileScan
  Job 为 Queued；
- transient failure 后的新 Attempt 将前两个成功步骤记录为 `Reused`，
  从失败点继续完成；
- 默认依赖注入提供两个显式处理器、Runner 和失败关闭执行器。

全量 SQL 首轮的 7 个失败均发生在测试宿主并行创建、握手或删除独立数据库
期间，覆盖既有 Clone、文件保留、RowVersion、Job 业务键、Migration 和
本卡重试场景；逐项串行复跑全部通过。因此不声称 78 项首轮一次性全绿，
也不把宿主资源压力记作功能通过。

## 6. 后续边界

- E13-S04 继续等待 E02-S03，不在本卡伪造 CAD IR 最小化。
- E13-S05 继续等待 E13-S04 和正式 Provider 证据。
- E13-S07、S08、S10、S11 及 Apply 路径均未提前实现。
- E13-S12 的依赖 E13-S01、S03 已满足，是下一张可独立启动卡；它必须用
  数据库租约/账本实现三并发、日/月预算和费用审计，不能使用单机
  `Semaphore`。
