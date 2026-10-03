# 数据库选择、本地验收与恢复

本页说明 DB-COMPAT-01 的运行配置与本机隔离恢复流程。部署时由配置选择 `SqlServer` 或 `PostgreSql`，使用同一份 CP6 代码和发布文件；一个应用实例的 Core、Space、IdentityPriority、ErpIntegration 必须使用同一 Provider 和目标数据库。

本页不授权切换现有环境、把已有 SQL Server 数据搬到 PostgreSQL、在同一实例混用两种 Provider，或执行生产部署。生产候选、环境推广与真实恢复验收仍遵循 [R2 主规范](../client/r2/README.md)、[发布流程](./RELEASE-PROCESS.md) 和 [本地验证策略](./LOCAL-VALIDATION-POLICY.md)。GitHub R2 + GHCR 保持唯一候选与部署权威。

## 配置契约

`Database:Provider` 只接受大小写完全一致的 `SqlServer`、`PostgreSql`。仅在配置键完全缺省时兼容旧配置并选用 `SqlServer`；显式空值、未知值或拼写错误均启动失败。连接字符串不能代替 Provider 选择。实现见 [DatabaseOptions](../../CP6.Core/Persistence/DatabaseOptions.cs)。

两个示例都是数据库配置片段，密码仅为占位符；实际值由受控 Secret 注入，不提交 Git。它们不包含完整生产环境所需的身份、Redis、消息、存储等其他配置。

SQL Server：

```json
{
  "Database": {
    "Provider": "SqlServer"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:sql.example.invalid,1433;Database=CP6;User Id=cp6_app;Password=<injected-secret>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=False"
  }
}
```

PostgreSQL：

```json
{
  "Database": {
    "Provider": "PostgreSql"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres.example.invalid;Port=5432;Database=cp6;Username=cp6_app;Password=<injected-secret>;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgresql-ca.crt;Include Error Detail=False"
  }
}
```

生产 SQL Server 必须加密传输并设置 `TrustServerCertificate=False`。生产 PostgreSQL 必须使用 `SSL Mode=VerifyFull`，连接主机名必须匹配服务端证书，CA 由受信任证书存储或实际挂载的 `Root Certificate` 文件提供；不能仅设置文件路径而遗漏证书交付。生产配置检查同时拒绝本机开发数据库端点，见 [ProductionConfigurationValidator](../../CP6.WebApi/Configuration/ProductionConfigurationValidator.cs)。

环境变量形式为 `Database__Provider` 与 `ConnectionStrings__DefaultConnection`。这两个值应作为一组受控配置，同时提供给一次性初始化进程和 API。不要在 Compose `environment` 或 Kubernetes `env` 中添加会覆盖 Secret 的 Provider 默认值。

显式 loopback 临时库的本地测试可以选用 PostgreSQL `SSL Mode=Disable`；这个例外不适用于生产。正式本地 runner 只接受受控 loopback 端点，并拒绝多主机路由、附加数据库、MARS、PostgreSQL multiplexing、非 `public` search path 或不识别的连接参数。测试身份需要创建、标记、备份、恢复和删除本次临时库的权限；SQL 清理还需要实际查看目标全部会话的权限。这类测试身份不能直接作为生产 API 的最小权限账号。

## 初始化、API 与部署模板

初始化与 API 使用同一发布目录或同一不可变 API 镜像，只切换启动模式和必要环境配置：

| 进程 | `Startup:Mode` | `Startup:SkipDatabaseInitialization` | `Startup:SkipHostedServices` |
| --- | --- | --- | --- |
| 一次性初始化 | `DatabaseInit` | `false` | `true` |
| API 与正常后台服务 | `Api` | `true` | `false` |

先执行一次性初始化并要求退出码为 0，再启动 API。重复初始化必须保留已有业务数据、权限和定制种子，并核对 applied/pending migration；不能用删除重建库作为重复初始化的替代。迁移只向前执行，应用回退前另行证明 Schema 兼容。

迁移身份按 [DatabaseMigrationProfile](../../CP6.Core/Persistence/DatabaseMigrationProfile.cs) 核对：PostgreSQL 有四个独立 history；SQL Server 保留既有 Core/Space 迁移所有权，原 Identity/ERP 消息表迁移仍归 Core。不能因有四个 Context 就要求 SQL Server 生成四份新 history。

| 入口 | 数据库输入与范围 |
| --- | --- |
| [根 docker-compose.yml](../../docker-compose.yml) | 开发用 SQL Server 编排，包含本地 SQL 容器和 SQL 连接配置；没有提供 PostgreSQL 开发编排。不能只改 Provider 就声称该整套编排支持 PostgreSQL。 |
| [根 k8s/](../../k8s/) | 开发模板，不作为 R2 生产输入。 |
| [生产 Compose](../../deploy/production/compose/compose.yaml) | 数据库由外部服务提供；`db-init` 与 `api` 已共享 `CP6_ENV_FILE`。受控文件同时提供 Provider 与对应连接字符串。 |
| [生产 API Deployment](../../deploy/production/kubernetes/api.yaml) 与 [db-init Job](../../deploy/production/kubernetes/db-init-job.yaml) | 两者使用同一组 `cp6-runtime` / `cp6-runtime-secrets` 输入。Provider 与连接字符串放入同一受控 Secret；保持 API 和 Job 的配置一致。 |

生产模板能传递两种 Provider 的配置，不等于 PostgreSQL 已获 R2 发布授权。现有 R2 candidate/source/migration/manifest 门禁仍以 SQL Server 为基线；本地 PostgreSQL 通过不能替换这些门禁、修改既有 `LatestMigration` 合同，或作为 GHCR/DEV/UAT/PROD 推广凭据。

## 正式本地 runner

入口为 [Test-Cp6DatabaseCompatibility.ps1](../../scripts/Test-Cp6DatabaseCompatibility.ps1)，必需入口及精确 case 名由 [required-cases.json](../../eng/database-compatibility/required-cases.json) 维护。选择的数据库不可用、必需 case 缺失/多出/跳过、真实进程非零退出或应用实际数据库身份不符，均不能计为通过。

准备 PowerShell 7、匹配仓库的 .NET SDK，以及 PATH 中的 `sqlcmd` 或 `psql` / `pg_dump` / `pg_restore`。PostgreSQL 工具也可通过 `-PostgreSqlBinDirectory` 显式指定，不依赖固定工作盘。完整应用的端口与所属 PID 核验当前使用 Windows `Get-NetTCPConnection`；其他平台不能以省略这一步冒充等价应用验收。

连接首先读取所选 Provider 对应的进程环境变量 `CP6_TEST_SQLSERVER` 或 `CP6_TEST_POSTGRES`。未提供该变量时，才读取显式 `-LocalConfigurationPath` 文件；不会读取另一种 Provider，也不会自动回退到应用的本机配置。文件仅放本机受控私有目录，结构如下，实际密码不出现在命令行：

```json
{
  "SqlServer": "Server=localhost,1433;Database=master;User Id=<test-login>;Password=<private-secret>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False",
  "PostgreSql": "Host=127.0.0.1;Port=5432;Database=postgres;Username=<test-role>;Password=<private-secret>;SSL Mode=Disable"
}
```

这里只填写本机测试服务器连接。runner 创建自己的新库并改写目标数据库名；不要填写已有业务库并把它当成验收目标。连接、初始化凭据和应用私有状态仅进入私有配置或子进程环境，不能在 shell 参数、公开报告、截图或工单中打印。可用 `CP6_COMPAT_ADMIN_PASSWORD` 或本机配置的 `DatabaseCompatibility.AdminPassword` 提供验收初始化口令；不在文档中复制程序的 bootstrap 口令。

| 参数 | 行为 |
| --- | --- |
| `-Provider SqlServer\|PostgreSql` | 一次运行选择一种引擎，两个 Provider 分别执行并分别归档。 |
| `-RunDirectory` | 必须是全新绝对路径；失败目录不覆盖，也不隐式续跑。 |
| `-Phase Full\|Matrix\|Application` | 默认 `Full`。`Matrix` 包含安装、runtime、业务/身份/Space/报表和适用的 SQL 升级历史入口；`Application` 包含初始化、种子、实际 API/认证/通知、原生恢复和恢复后应用验证。部分阶段即使 `Status=Passed`，仍为 `FullAcceptance=false`。 |
| `-LocalConfigurationPath` | 显式私有 JSON，所选连接的环境变量优先。 |
| `-PostgreSqlBinDirectory` | PostgreSQL 原生工具目录。 |
| `-PostgreSqlSslMode Disable\|Require\|VerifyCA\|VerifyFull` | 本机 PostgreSQL 的显式 SSL 选择；`Disable` 仅用于该 loopback 验收。 |
| `-Configuration Debug\|Release` | 默认 `Debug`；构建和入口执行配置保持一致。 |
| `-SkipBuild` | 本次不 restore/build/publish；记录复用并绑定现有 binary 与源码输入哈希。含 `Application` 的运行必须同时提供已有 `-ArtifactDirectory`。 |
| `-ArtifactDirectory` | 使用已发布 API 文件，逐文件哈希固定并在初始化、API、恢复目标之间复用；不将既有产物冒称本次发布构建。 |
| `-PlanOnly` | 仅做离线规划，不创建目录、不启动进程、不连接数据库；不是验收结果。 |

在已由安全方式设置连接环境变量后，分别执行两个全新运行。以下示例不设置凭据，也不触发远程 Actions：

```powershell
$sqlRun = Join-Path ([IO.Path]::GetTempPath()) ('cp6-wp6-sql-' + [guid]::NewGuid().ToString('N'))
pwsh -File scripts/Test-Cp6DatabaseCompatibility.ps1 -Provider SqlServer -RunDirectory $sqlRun -Phase Full

$pgRun = Join-Path ([IO.Path]::GetTempPath()) ('cp6-wp6-pg-' + [guid]::NewGuid().ToString('N'))
pwsh -File scripts/Test-Cp6DatabaseCompatibility.ps1 -Provider PostgreSql -RunDirectory $pgRun -Phase Full -PostgreSqlSslMode Disable
```

按需要追加私有配置文件路径或工具目录，不能把连接字符串作为参数。普通编译/测试只在本地执行；不要并行启动两个 runner 的 .NET/数据库工作，也不要因本地失败转而触发 GitHub Actions。

报告保留源码身份与输入哈希、实际进程退出码、精确 case 名、原生工具/服务端版本、API 发布文件哈希、HTTP 与数据库身份、恢复对账和失败进度。原始 stdout/stderr、TRX、备份、连接配置和捕获状态留在私有目录；共享前只选择经过核对的安全摘要。历史 WP2–5 证据保留原时点，不能改名为新执行的 WP6 结果。

## 同引擎备份与新库恢复

[DatabaseCompatibilityLifecycle.psm1](../../scripts/database-compatibility/DatabaseCompatibilityLifecycle.psm1) 只操作本次新建并登记的库。库名为 `CP6Compat_WP6_<UTC日期>_<owner前8位>_<固定role>`；receipt 记录完整 32 位 owner、固定 task `DB-COMPAT-01-WP6`、端点、实际数据库身份、创建/操作状态。创建前先记录 `Planned` 并确认同名库不存在；迁移、业务写入及后续生命周期操作前核对 receipt 与实际身份。恢复目标必须是本次新建、`restore` role、确认空库的另一份 receipt。

应用层先停止本次 API/worker，捕获业务数据、迁移、身份/权限、消息与密钥相关状态，再备份。恢复后核对实际目标库身份、history、数据摘要、消息状态、权限、密钥与原游标可用性，并以同一发布文件启动目标 API。待处理通知还需要恢复后的真实 worker 领取和投递证据，不能仅凭表行数相同计为完成。

SQL Server 流程：

1. 对 owned source 执行 `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM`，再执行 `RESTORE VERIFYONLY ... WITH CHECKSUM`。
2. 服务端备份位置取实际 `InstanceDefaultBackupPath`；为空时使用实际查询的 data 目录，并由真实备份操作证明 SQL 服务可写。文件名绑定本次 run/owner，归档复制到私有运行目录并计算 SHA-256；无读取/复制权限即失败，不省略归档。
3. 从源 `sys.master_files` 记录逻辑文件名；`MOVE` 只指向新空目标 receipt 已记录的物理文件路径。仅对这个已验证目标使用 `WITH REPLACE`，不覆盖源库或其他库。
4. 恢复会复制源库的 ownership marker。先核对目标数据库 ID/物理路径和复制来的源 marker，再只把目标 marker 改为目标 receipt 的 owner，并保留重标记事件。

PostgreSQL 流程：

1. 使用 `pg_dump -Fc` 创建私有 archive，记录工具版本、服务端版本、文件哈希与真实退出结果。
2. 使用 `pg_restore --exit-on-error --single-transaction` 连接新空目标，不使用 `--create`、`--clean`、`--no-owner`、`--no-acl` 或禁用触发器选项；保留数据库内对象 owner 与授权。
3. 非 `--create` 恢复不复现数据库本身的 comment/settings/ACL。模块显式核对数据库 owner、当前角色，受控重放并精确对账数据库级 grants/settings；不将这部分静默忽略。目标保留自己的 owned marker，不重新连接源库恢复。数据库级元数据行为见 [PostgreSQL 18 pg_restore](https://www.postgresql.org/docs/18/app-pgrestore.html)。

此流程证明同引擎、本 run 新库之间的恢复。它不提供跨引擎数据转换，也不代表完整生产灾备：PostgreSQL 单库 dump 不包含集群全局 role/tablespace，需要目标环境另行受控交付并核对，见 [pg_dump](https://www.postgresql.org/docs/18/app-pgdump.html) 和 [pg_dumpall](https://www.postgresql.org/docs/18/app-pg-dumpall.html)。SQL Server 用户库 `.bak` 不包含实例登录、端点、linked server、Agent job 等完整实例状态；这些位于系统数据库/实例配置，另有备份与恢复责任，见 [master](https://learn.microsoft.com/en-us/sql/relational-databases/databases/master-database?view=sql-server-ver17) 和 [系统数据库](https://learn.microsoft.com/en-us/sql/relational-databases/databases/system-databases?view=sql-server-ver15)。外部 Secret、证书、JWT/Data Protection 配置及文件/对象存储也必须按其原有安全流程保留，不能假定数据库归档包含它们。

## 清理与失败保留

runner 成功结束时，向 `Remove-Cp6OwnedDatabases` 传入本次明确的 receipt 列表；所有目标实际 `Absent` 才算该阶段成功。失败时保留数据库、receipt、备份、原始日志与结果，只停止本次拥有的 API 进程。没有按名字前缀扫库、隐式继续失败目录或“保留失败但汇总成功”的路径。

需要人工整理失败运行时，先核对原运行 manifest、receipt 与私有连接输入。使用相同 `New-Cp6LifecycleContext` 输入重新打开该已登记上下文，再把逐个明确读取的 receipt 对象传给 `Remove-Cp6OwnedDatabases`；不能通过目录通配符或数据库前缀扩展清理范围。该函数先只读检查全部目标的 owner、物理身份、角色与零活动会话，再逐个复查并正常 `DROP`，最后确认缺失。出现 owner/identity 不符、会话仍在或状态不完整时保留原进度并停止；不强制断开、不设 `SINGLE_USER`，不编辑 receipt 绕过验证。

恢复中途失败而留下 `RestoreStarted`，或创建失败留下 `Planned` / `Created` 时，不能把它们手改为 `Owned` 后删除。应先检查安全日志与实际状态，由任务负责人确认下一步；原失败证据保留。

## 已观察版本与验收边界

2026-10-03 的 WP6 本地应用验证实际使用 PostgreSQL `18.6` 与 SQL Server `16.0.1000.6`。这两个精确版本是观察到的验证环境，不扩展为所有历史/未来版本或生产发行组合的支持承诺；换引擎版本、客户端工具或依赖后按受影响范围重新验证。

两库正式 Application 分项各完成 17 个必需入口，覆盖首次/重复初始化、隔离 API 健康/登录/授权、两个真实用户的 WebSocket 通知、原生新库恢复、全表数据对账、恢复后待发通知及幂等重放和旧 Data Protection 游标续页。各运行的两个临时库已清理并确认不存在。两库使用同一组 261 个发布文件，API SHA-256 为 `445E9B73EB39277C8D395677DCDDE5E14B48AB834890ACB79C985A2AC4F6B9A5`；正式两库运行的 2009 个相关源码/配置输入也相同。这是本地验证发布物的身份，不是本页新构建结果或 R2 候选身份。

正式两库 Matrix 和最终归档、远端交付仍待完成，因此此时不声明 WP6 或父任务完成。Application 分项均记录 `FullAcceptance=false`，不能改称单次 Full。后续结论以 [WP6 实施记录](../audits/database-compatibility/WP6-IMPLEMENTATION.md)、对应实际原件和远端 main 包含性核对为准。部分阶段、旧证据复用或本地 `Passed` 都不能替代完整验收与生产发布门禁。
