# Space 全量 SQL Server LocalDB 门禁

日期：2026-08-14

范围：Space Studio V1 核心 GA / 仓库真库自动化

结论：完整 Space Integration 已在真实 SQL Server 引擎上 426/426 通过、0 failed、0 skipped；生产等价 SQL、真实 CP6 WMS/IdP/告警链和现场接受仍为 Pending，核心 GA 保持 72% / No-Go。

## 环境

- SQL Server ProductVersion：`17.0.4025.3`
- ProductLevel：`RTM`
- Edition：`Express Edition (64-bit)`，`MSSQLLocalDB`
- .NET SDK：`10.0.302`
- 基线：远端 `main` 的发布恢复 SQL 修复提交 `cdf23add`
- 数据库：每个场景使用随机数据库名，测试结束执行 `EnsureDeletedAsync`

连接字符串只在当前进程通过 `CP6_TEST_SQLSERVER` 提供；报告不保存凭据。

## 首次执行与发现

命令：

```powershell
$env:CP6_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30'
dotnet test CP6.Space.IntegrationTests\CP6.Space.IntegrationTests.csproj --nologo
```

首次结果：424 passed / 2 failed / 0 skipped。

1. 发布恢复指标的复合键 GroupJoin 无法由 SQL Server Provider 翻译。该问题在独立任务中改为显式 TenantId/AttemptId/AttemptStatus 相关子查询。
2. `Published_viewer_scene_uses_only_current_published_pointer` 夹具先把版本转换为 Published，随后才追加楼层，因此被生产的 Published/Superseded 不可变保护正确拒绝。

## Viewer 夹具修复

- 新夹具在版本仍为 Draft 时创建并保存 Published Floor。
- 内容完整后才执行 Validation → Ready → Publishing → Published 和 Model cutover。
- 没有修改或放宽 `ProtectPublishedSnapshotWrites`；测试现在同时证明 Published Scene 只消费 Current Published 指针、Draft-only 楼层不可见，以及已发布快照不能后写。

聚焦结果：`SpaceDesignSceneSqlServerTests` 7/7 passed、0 skipped。

## 最终结果

完整复跑：426 passed / 0 failed / 0 skipped，总时长 4 分 13 秒。

覆盖范围包括 CAD Provider/Parse、编辑租约、Design Revision/Command、Published Viewer、Validation、Publish/Retry/Reconcile、WMS Adapter、外部主体/跨租户、文件安全、Excel、AI、迁移和不可变审计等 Space Integration 场景。该数字只陈述自动化实际执行结果，不表示外部系统和现场验收已经发生。

## 接受边界

- LocalDB 是真实 SQL Server 引擎，可证明 SQL 翻译、约束、迁移和事务场景执行；它不是生产等价高可用拓扑。
- CP6 WMS 在测试进程中使用仓库适配器和隔离数据库，不是已部署生产 WMS 窗口。
- 未执行真实 IdP、Prometheus 通知、备份恢复、独立渗透测试、20 份黄金 CAD、双仓 Pilot 或五方签字。

因此，本报告把原先 107 个环境 skip 收敛为本机 0 skip，但不把 WP3、WP5、WP6 或核心 GA 的 acceptanceStatus 改为 Accepted。
