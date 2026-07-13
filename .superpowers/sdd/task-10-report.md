# Task 10 报告：SpaceSqlIntegrationTests 真库化（环境变量门控）

## 交付
- 新建 `CP6.Tests/Infra/SqlServerFactAttribute.cs`：`FactAttribute` 子类，ctor 检测 `CP6_TEST_SQLSERVER` 环境变量，缺失即 `Skip`（CI 恒绿）。
- 全文重写 `CP6.Tests/SpaceSqlIntegrationTests.cs`：4 个 `[SqlServerFact]`，用**完整 CP6Context** 跑真实 SQL Server；每测试实例建唯一名临时库 `CP6Test_{Guid:N}`，`EnsureCreated` 建 schema，`Dispose`→`EnsureDeleted`（try/catch 兜底）。
- 四测试真实断言（无 `Assert.True(true)` 混绿）：
  1. `UniqueIndex_SameNonNullCode_SecondInsertThrows`：同租户同非空 `LocationCode` 二插 → `DbUpdateException`（内层 `SqlException` 2601/2627）。
  2. `UniqueIndex_TwoNullCodes_BothCoexist`：两行 `LocationCode=null` 共存（过滤索引 `HasFilter([LocationCode] IS NOT NULL)` 排除 NULL），断言 count==2。
  3. `TwoPhaseReorder_SwapCodes_NullIntermediate_Succeeds`：经 NULL 中转（腾空→占用→回填）交换两码，断言 S-A/S-B 已互换。
  4. `RowVersion_ConcurrentUpdate_SecondThrows`：两上下文并发改同行，先写者提交后 rowversion 变，后写者 → `DbUpdateConcurrencyException`。

## 测试实证（两种输出）

### 模式一：无环境变量（CI 默认路径）→ 4 Skip
```
[SKIP] TwoPhaseReorder_SwapCodes_NullIntermediate_Succeeds
[SKIP] UniqueIndex_TwoNullCodes_BothCoexist
[SKIP] RowVersion_ConcurrentUpdate_SecondThrows
[SKIP] UniqueIndex_SameNonNullCode_SecondInsertThrows
Skipped! - Failed: 0, Passed: 0, Skipped: 4, Total: 4
```

### 模式二：设 CP6_TEST_SQLSERVER（本机 Docker SQL Server，sa 密码打码）→ 4 绿
```
$env:CP6_TEST_SQLSERVER = "Server=127.0.0.1,1433;Database=master;User Id=sa;Password=****;TrustServerCertificate=True"
dotnet test --filter SpaceSqlIntegrationTests
Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 44 s
```
注：连接主机须用 `127.0.0.1`（IPv4）。`localhost` 会先解析 `::1`（IPv6）被 Docker 端口映射拒绝（"target machine actively refused it"）。文档给的 `Server=localhost,1433` 在本机走 IPv6 连不上，用 127.0.0.1 即通。

### 临时库清理实证
测试后查 `sys.databases WHERE name LIKE 'CP6Test[_]%'` → `LEFTOVER_COUNT=0`（Dispose 的 EnsureDeleted 生效，真库无垃圾）。

### 全量后端（无变量路径）→ 基线不降
```
Passed! - Failed: 0, Passed: 1824, Skipped: 5, Total: 1829, Duration: 1 m 16 s
```
`passed=1824`（≥基线，未降）；`skipped=5`＝4 门控 SpaceSql + 1 既有 BudgetSqlite（4 桩 Skip 原地换成 4 门控 Skip，passed 数不变）。

## 疑虑
- 无。（4 桩改门控后 skipped 总数仍为 5——原 4 桩本就计入 skipped，非 brief 预估的 5→9；passed 无损即达标。）
