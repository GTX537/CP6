# Space CAD Provider SQL Server 门禁

日期：2026-08-14

范围：Space Studio V1 核心 GA / WP3 仓库真库自动化

结论：`SpaceCadProviderSqlServerTests` 已在 SQL Server LocalDB 上 3/3 通过、0 failed、0 skipped；该结果关闭 Provider 认证数据模型的仓库真库门禁，不代表真实 ODA/APS、Site 审批或双 Provider 链已经完成。

## 环境与命令

- SQL Server：17.0.4025.3 RTM，Express Edition (64-bit)，`MSSQLLocalDB`
- 基线：远端 `main@d157599d`
- 临时数据库：每个场景使用唯一 `CP6SpaceCadProviders_<guid>` 数据库，并在结束时删除；运行后复核无残留

```powershell
$env:CP6_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30'
dotnet test CP6.Space.IntegrationTests\CP6.Space.IntegrationTests.csproj `
  -c Release `
  --filter "FullyQualifiedName~SpaceCadProviderSqlServerTests" `
  --nologo `
  --logger "console;verbosity=normal"
```

结果：3 passed / 0 failed / 0 skipped；测试执行时间 18.4323 秒。

## 覆盖

- 并发替换只允许一个写者成功，另一个稳定返回 Provider Revision Conflict。
- 每个 Tenant/Site 只保留一个 Current Revision；旧 Revision 追加保留且变为 Superseded。
- 两份 Provider 认证证据保持不可变，尝试修改审批证据时保存失败关闭。
- 幂等记录只产生一次；Provider 路由、资格和版本三份迁移脚本均在每个场景中重复执行两次。
- 旧认证缺失四项审批/资格证据时，能力查询和执行路由失败关闭。
- 旧认证缺失 Provider Version 时，主备链均失去资格并产生阻断码。

## 接受边界

该报告证明认证唯一性、并发、历史追加、不可变证据、迁移幂等和旧数据失败关闭在真实 SQL Server 引擎执行。测试 Provider 为无网络的合同替身，不是 ODA、APS 或候选替代者；20 份授权黄金 CAD、冻结隔离 Worker、法务/安全/客户批准、主备评分、真实 DWG/DXF 和 Site 故障切换仍须完成，因此 WP3 继续为 Partial/Pending，核心 GA 保持 72% / No-Go。
