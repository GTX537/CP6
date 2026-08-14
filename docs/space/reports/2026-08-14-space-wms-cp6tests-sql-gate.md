# Space/WMS CP6.Tests SQL Server 门禁

日期：2026-08-14

范围：Space Studio V1 核心 GA / WP5-WP6 仓库真库自动化补充

结论：CP6.Tests 中属于 Space/WMS 的三个 SQL Server 集合已 15/15 通过、0 failed、0 skipped；生产等价 SQL 与真实 CP6 WMS 接受仍为 Pending，核心 GA 保持 72% / No-Go。

## 环境与命令

- SQL Server：17.0.4025.3 RTM，Express Edition (64-bit)，`MSSQLLocalDB`
- 远端 `main`：`9323c9be`
- 临时数据库：测试按唯一随机名称创建并在结束时删除

```powershell
$env:CP6_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30'
dotnet test CP6.Tests\CP6.Tests.csproj `
  --filter "FullyQualifiedName~CP6.Tests.SpaceSqlIntegrationTests|FullyQualifiedName~CP6.Tests.WmsProductionSqlServerTests|FullyQualifiedName~CP6.Tests.Space.SpaceIntegrationEventOccurredAtUtcSqlServerTests" `
  --nologo
```

结果：15 passed / 0 failed / 0 skipped，总时长 34 秒。

## 覆盖

Space SQL：

- 可空 Floor 条件的 Control Tower SQL 翻译。
- Tenant + LocationCode 过滤唯一索引；多个 NULL Draft Code 可共存。
- 经 NULL 中转的两阶段 LocationCode 交换。
- SQL Server 原生 rowversion 并发冲突。

WMS SQL：

- Move 任务并发认领、部分完成与幂等重放。
- Replenish 来源与 Move Task 的事务一致性。
- Source Document 范围翻译与失败关闭。
- Serial 唯一账本、聚合对账与启用门禁。
- Feature Flag 单一 Pending、幂等重放与原子 Apply。
- LPN 复合 Serial 身份以及整树移动/拆分/合并。

Space Integration Event SQL：

- 两连接使用 Session-owned application lock 串行完成全部回填批次。
- 无效时区失败后释放锁，后续 Context 可正常回填。

## 全套运行边界

同一环境执行完整 CP6.Tests 得到 2932 passed / 2 failed / 1 skipped。两个失败是 OA/PUR 测试主动要求 `CP6_OA_P0_SHARED_STAGE=1`，且数据库名匹配受控 `CP6OaP0Stage_<timestamp>_<suffix>` 的共享隔离 Stage；普通随机 LocalDB 不具备该授权语义，因此按设计拒绝。唯一 skip 是已声明 SQLite 结构不兼容的测试。它们不属于本 Space/WMS 任务，本报告没有删测、改过滤器或把失败计算为通过。

## 接受边界

本报告证明 Space/WMS SQL 约束和事务测试在真实 SQL Server 引擎执行；测试 WMS 仍是仓库内 CP6 Adapter/数据库，不是生产窗口。生产等价 SQL 拓扑、真实 CP6 WMS、故障注入、观测告警、备份恢复与人工对账仍须在 WP6 外部门禁完成。
