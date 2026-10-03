# WP3 运行时双数据库验证

此工具调用 CP6 的真实锁、编号、Context 工厂、身份分页和工作槽服务。选择一个 Provider，使用已有的本任务独占临时数据库；不连接业务数据库。完整 API、消息、ERP/WMS、Space/CAD、预算和恢复验收属于后续阶段。

```powershell
dotnet restore tools/CP6.DatabaseCompatibility.RuntimeProbe/CP6.DatabaseCompatibility.RuntimeProbe.csproj --locked-mode
dotnet build tools/CP6.DatabaseCompatibility.RuntimeProbe/CP6.DatabaseCompatibility.RuntimeProbe.csproj --no-restore
dotnet tools/CP6.DatabaseCompatibility.RuntimeProbe/bin/Debug/net8.0/CP6.DatabaseCompatibility.RuntimeProbe.dll --provider PostgreSql --suite orders --initialize --output <新的报告路径> --source-sha <基线提交>
```

从仓库根目录运行，身份用例读取该目录中固定的 `contracts/events/platform`。SQL Server 对应 `--provider SqlServer`。连接从进程环境变量 `CP6_TEST_POSTGRES` / `CP6_TEST_SQLSERVER` 读取；`CP6_TEST_DATABASE_OWNER` 为创建该测试库时记录的32位小写十六进制随机所有者标识。不要把连接或凭据写入命令行、报告或 Git。

前置条件：数据库名称为 `CP6Compat_WP3_yyyyMMdd_8位小写十六进制`，主机为字面量 loopback。SQL Server 的数据库级扩展属性 `CP6CompatTask` 必须为 `DB-COMPAT-01-WP3`，`CP6CompatOwner` 必须匹配所有者；PostgreSQL 数据库 comment 必须为 `DB-COMPAT-01-WP3:<所有者>`。库必须由调用方新建并记录，不能给现有业务库补标记后运行。工具不负责数据库创建或删除；每个门禁只清理自身产生的夹具，数据库由持有创建记录的调用方统一清理。

`--initialize` 按受支持历史前缀执行实际 Core 迁移；省略时要求完整且精确的已安装 Core 历史。`claim` 另外按受支持前缀执行实际 Space 迁移。它们不是 `EnsureCreated`。工具不安装 IdentityPriority/ERP 历史，不代替完整 db-init 验收。

| `--suite` | 验证内容 |
| --- | --- |
| `orders` | ORD 立即保存、首次创建并发、调用方回滚、全局不重置作用域和待保存手工修改拒绝；`--case immediate`只执行第一项 |
| `orders-extra` | 既存计数并发、Modified/Deleted拒绝、非ORD Local批量延迟保存 |
| `locks` | 事务/会话锁的等待、取消和释放，数据库UTC在同一事务内推进 |
| `failures` | 真实唯一/FK/check错误的精确分类、SQL死锁或PG序列化失败后的整事务回滚和重新执行；SQL验证原会话语言及英文并恢复原语言 |
| `shared` | 七个子Context的Provider/profile、同物理连接事务、非拥有式处置与共同回滚 |
| `cursor` | 持久generation/v2游标、分页、回滚、更新/删除和实际新进程续读；PG另验证同页MVCC一致性 |
| `claim` | 真实工作槽服务的容量竞争、同run单例、token/owner fence、到期抢占、租户独立、原生skip-locked候选 |

工作槽用例使用可控UTC夹具时钟；数据库时钟由 `locks` 独立验证。身份子进程只继承测试环境，私有游标和临时 Data Protection 密钥在本机临时目录中，结束时清理，不进入报告。不要并行运行同一个测试数据库上的不同 suite。

报告拒绝覆盖，返回非零表示失败。报告记录真实加载程序集位置/hash及实际主机UTC；基线SHA不能单独标识未提交源代码，调用方需同时保留源码观察、命令与原始失败。阶段证据与范围见[WP3实施记录](../../docs/audits/database-compatibility/WP3-IMPLEMENTATION.md)。
