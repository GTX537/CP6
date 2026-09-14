# C04A 源端保护与真实恢复副本演练

本目录验证旧 CRM **空来源**的源端控制组件。CRM02 的本地合同确认已由 [CRM PR #68](https://github.com/GTX537/CP6.CRM/pull/68) 关闭；源端工具位于 [`tools/CP6.Crm.SourceFence`](../../../tools/CP6.Crm.SourceFence/)，独立 SQL 回归位于 [`source-fence-tests`](../source-fence-tests/)。所有变更仅针对新建的本机 `CP6_C04A_Rehearsal_<guid>` 副本。

## 执行

需要 PowerShell 7、.NET 8 SDK 和本机 SQL Server。通过当前进程环境变量提供连接，不写入 Git、命令行参数或日志。备份操作者须能 COPY_ONLY 备份、读取 SQL 默认备份目录、创建恢复数据库；工具操作者与受限业务写入者的验证分开。

1. 设置 `C04A_SOURCE_CONNECTION` 指向已确认的本机原始数据库；运行只增不覆盖的备份恢复助手：

   ```powershell
   ./eng/crm/source-fence-rehearsal/New-SourceRestore.ps1 `
       -ExpectedServer '<本机实例>' -ExpectedDatabase '<已确认源库>' `
       -OutputDirectory '<新的私有证据目录>'
   ```

   助手先核对 Foundation 文件、20 表/305 列、全量计数和迁移历史；`COUNT_BIG` 包含软删除行，任何非空表即拒绝。随后 COPY_ONLY/CHECKSUM 备份、HEADERONLY 与 VERIFYONLY 核验、按唯一文件名 RESTORE（无 REPLACE），比较源端前后与恢复副本的 CRM 元数据。输出 `restore-receipt.json`；`private-resource-locator.json` 和备份文件保留在本机，**不要提交或公开**。

2. 编译独立工具，设置 `C04A_RESTORED_CONNECTION` 为 receipt 中的新数据库；执行真实副本验收：

   ```powershell
   dotnet build tools/CP6.Crm.SourceFence -c Release
   ./eng/crm/source-fence-rehearsal/Invoke-RestoredAcceptance.ps1 `
       -RestoreReceipt '<私有目录>/restore-receipt.json' `
       -ToolAssembly './tools/CP6.Crm.SourceFence/bin/Release/net8.0/CP6.Crm.SourceFence.dll' `
       -OutputDirectory '<新的验收证据目录>'
   ```

   验收创建副本内专属用户和 ERP 探针表，验证全部 20 表的普通及直接 SQL 拒绝、读取保留、混合事务回滚、围栏前恢复与封口后的恢复拒绝。恢复写入探针使用事务回滚，不保留 CRM 业务测试行。它验证现有 SQL OUTPUT 写法在冻结时失败、恢复后可用；不冒充完整 Core HTTP/GDPR 业务验收。

3. 工具可单独调用 `preflight`、`status`、`freeze`、`reopen`、`seal-forward-only`。通过 `C04A_SQL_CONNECTION`、`C04A_EXPECTED_DATABASE`、`C04A_EXPECTED_DATABASE_GUID`、`C04A_RUN_ID`、`C04A_EXPECTED_GENERATION` 提供显式输入。GUID 是服务代理标识，普通恢复可能保留该值，须与唯一副本名称联合核对。初次 freeze 使用 generation 0，每次成功状态变化递增；同一请求的紧接重放不重复审计。失败后先读取状态，不能猜测结果或重置控制表。

   `preflight/status` 不安装 Schema。只有 Frozen 可恢复；`seal-forward-only` 将恢复永久禁止，须先于未来目标库开放新业务写入。它表示“只能向前修复”，不表示已观察到目标首笔写入。

4. 运行输入保护回归与真实 SQL 组件测试：

   ```powershell
   ./eng/crm/source-fence-rehearsal/Test-RestoreInputGuards.ps1
   # C04A_TEST_SQL_CONNECTION 显式指向本机 master；未配置应失败，不跳过。
   dotnet test eng/crm/source-fence-tests -c Release
   ```

每次运行使用新证据目录。失败记录及已经创建的恢复副本保留；助手不会覆盖、自动删除或回退任何数据库。成功演练副本留在 ForwardOnly，供检查。后续清理须按私有定位记录核实该副本的确切名称与路径。

## 保证和待验收范围

冻结事务持有 20 表排他锁直至安装触发器、权限拒绝、状态和审计一起提交。已有写入须先结束；超时或失败不留下部分冻结。源表之外的 ERP 与 C01/C02 数据结构保留，数据库不整体设为只读。恢复只撤去本工具的保护，不清空原表或重写来源数据。

SQL 触发器补足直接 SQL/所有者链写入保护，public 的 DML/ALTER 拒绝约束受限写入者的批量和 DDL 路径。[TRUNCATE 不触发 DELETE 触发器并要求 ALTER 权限](https://learn.microsoft.com/en-us/sql/t-sql/statements/truncate-table-transact-sql)；[DENY 不约束 sysadmin 或对象所有者](https://learn.microsoft.com/en-us/sql/t-sql/statements/deny-transact-sql)。当前源端检查身份是 sysadmin；尚未盘点并替换所有实际写入身份，因此所有结果的 `completeWriteFenceVerified` 固定为 false。

完整 C04A 仍需实际旧写入方/作业/权限盘点、停止与排空证据、目标写入接线和路由切换/回退。CRM11 的目标物理 Schema、全量列转换/哈希、租户及版本化日历输入、正式切换演练另行完成；C04B 保留切换后的只读观察期前置。本工具不接入应用启动，不迁移生产，不修改已批准 CRM02 文件，也不关闭这些验收项。

元数据比较覆盖列、类型/长度/精度/可空性、主键、索引、外键、CHECK 和触发器等固定查询维度；不等于全部物理存储选项、排序规则、权限或写入身份审核。Foundation 自动化测试证明固定结构上的组件行为；真实恢复结果单独记录在 [`docs/evidence/c04a/2026-09-13`](../../../docs/evidence/c04a/2026-09-13/)。
