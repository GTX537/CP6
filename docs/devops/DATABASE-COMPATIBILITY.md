# 数据库选择、本地验收与恢复

## 2026-10-04：BUG163 已交付，WP6 接入当前版本验收

[BUG163](https://github.com/GTX537/CP6/issues/163) 已由 [PR164](https://github.com/GTX537/CP6/pull/164) 正常合入远端 main `6322ac152cab7d462719169fb0301f49e9ee39ba` 并关闭，WP6 已接入。WMS 原八项＋新增十七项两库各25/25、零失败/跳过；合并后两库各3/3。两个修复测试库及原 WP6 SQL 失败库均已普通 DROP 并确认不存在；SQL 实际持续会话超时拒绝、短暂会话自行结束后清理通过，原 Failed summary 与 core/WMS TRX 字节保持。[专项记录](../audits/2026-10-03-bug-163-wms-local-transactions/README.md)保留111份原件及36份交付、冒烟、清理原件。

当前必需清单只扩展 WMS 为25项，其他61入口不变。最终入口仍为 PG Matrix34、SQL Matrix37、两库 Application各17，共105个适用入口；使用新的 `wms-local-transaction-fix` 运行目录。编译输入已改变，需当前代码七项构建及新的完整双库验收；后续 SQL 可在源码与完整运行文件逐字节一致后复用 PG 的真实构建，两库应用使用同一新发布产物。此前 PG Matrix34/34、旧发布物应用17/17保留历史来源，不代称当前最终验收。WP6 与父任务#134仍未完成；最终恢复审计、集中审查、归档与正常远端交付待完成。没有启动远程 Actions、改变生产门禁或部署既有环境。下方记录保留历史时点。

最新阻断（2026-10-03，BUG163 Open）：SqlServer cad-cleanup-fix Matrix在原核心25/25后，WMS8项为7P/1F，原Lpn_UsesCompositeSerialIdentity_AndMovesSplitsMergesWholeTree的CreateAsync第二查询触发Implicit distributed transactions have not been enabled；后35入口未执行。原summary SHA256 8259FAEA4EEEF0786705CB4501F1072F79D0CA7DC116606B9EEB5364A7205F50与WMS TRX 5AD8388203E589148B68795BFBC56C2478BC0D95EF9646C6A3A5D06C042A2870保留Failed，一个该次SQL owned库暂留。已登记[Issue163](https://github.com/GTX537/CP6/issues/163)，修复从最新已确认main ce49012d37da4b1b01f074d5b5b7fd227780e656创建独立codex/bug-163-wms-local-transactions分支。源码确认LPN四处自建ambient事务，runner明确Pooling=False；须原生确认与修复本地事务生命周期，Label相似路径尚待实际确认。PG Matrix34/34/11库Absent仍是修复前该次证据；修复影响编译输入后最新双库最终Matrix/Application需要重新适用验证，WP6与父Issue134未完成。首次SQL连接缺失为配置预检拒绝，未创建运行目录/DB；随后仅进程内使用既有本机KOUSQLSERVER Windows登录，未改用户PG配置/环境。


最新验收进展（2026-10-03 23:17Z）：新cad-cleanup-fix PostgreSql Matrix已实际Passed，34/34必需入口、零失败/跳过，11个owned库AbsentVerified；summary SHA256 5AD4D28C008E705F9DF355572ED9B532B1D80723BE1FB4D18439FDA1321BB107。该轮明确SkipBuild，复用原实际7项成功构建：编译输入与2059运行文件逐字节一致，PowerShell限定清理变化另有RED/GREEN及native控制证据；不把上一轮清理Failed改成Passed。进入SqlServer Matrix37，SQL136旧基线仍单独构建/安装；两库最新发布物Application17各仍待执行，最终105入口/恢复核对、归档、正常远端WP6交付与父任务关闭尚未完成。


最新本地进展（2026-10-03）：cad-provider-fix PG Matrix 的34/34入口全部通过，CAD25/25且零skip；收尾owned-database-cleanup遇到短暂会话，整轮仍为Failed，原summary SHA256 EEF84ED7808D7D83D7389708102C3989BA03DEEDD4EA17C367123D476AC4BCD1及34入口原件不变。七库原已Absent、余四库由实际限定等待控制后普通DROP，11库均Absent；原会话类型未记录，不推断其来源。WP6新清理模块默认逐库最多等待15秒，只对会话未结束重试，每次重核owner/principal/physical，超时或身份变化仍拒绝，保留全目标预检及逐库复检；新离线控制RED后41项GREEN，原42项契约通过，真PG持续会话1秒超时拒绝且receipt不变，短暂会话自行结束后清理通过。仅此PowerShell与控制变化，不改业务编译输入，7个既有真实成功构建/2059运行文件逐字节核对后复用；新完整Matrix和双库Application使用cad-cleanup-fix目录，最终验收/远端交付仍待完成，父Issue134保持Open。下文旧目录及状态保留历史时点。


最新交付（2026-10-03，BUG161 Closed）：PR162 正常合并到远端 main ce49012d37da4b1b01f074d5b5b7fd227780e656，并已接入 WP6。两库 CAD/资产/协作原15＋新十项各25/25、零skip，补充 InMemory17/17；合并后两库各3/3，PG两项协调测试再次实际40001后恢复。两个 BUG 自有库已普通DROP并核对不存在，Issue161于22:11:25Z关闭。[源码/原始失败及双库回归](../audits/2026-10-03-bug-161-cad-provider-recovery/README.md)与交付/冒烟/清理原件保留；原 claim-fix Matrix 28P后14P/1F及后五入口未执行仍为失败，七个该次PG库清理且原summary不变。最新清单仅把CAD组15扩为25，其他61入口不变；最终两库Matrix与最新产物Application采用新cad-provider-fix目录，仍待实际执行。WP6未完成，父Issue134仍Open。下文BUG161 Open及旧claim-fix计划保留历史时点。


最新状态（2026-10-03，BUG161 Open）：claim-fix PostgreSQL 最终 Matrix 已终止为 Failed；7 个相关项目构建通过，28 个入口 Passed，第 29 个 CAD/资产/协作入口 15 项为 14P/1F，后五入口未执行。原 Concurrent_replace_preserves_one_current_revision_and_immutable_evidence 出现实际 PostgreSQL 40001，失败 summary SHA256 9A788F5E6450F4B4FE4258FEDA9FFC99B959EB7A7E997F2A8EB2BEAD49F6284C，TRX SHA256 32E0C9CD7990B0EE5ACD8B7F20C19EA351C3F9465D13FAEAABE711B7D275E364；原件保留。修复从已确认远端 main 4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0 建立独立 codex/bug-161-cad-provider-recovery 分支，活动缺陷以 [Issue #161](https://github.com/GTX537/CP6/issues/161) 为准。最新 SQL Matrix / 双库 Application 仍未开始；WP6 与父 Issue134 未完成。下文 claim-fix 计划及状态保留历史时点。


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

修复前两库正式 Application 分项各完成 17 个必需入口，覆盖首次/重复初始化、隔离 API 健康/登录/授权、两个真实用户的 WebSocket 通知、原生新库恢复、全表数据对账、恢复后待发通知及幂等重放和旧 Data Protection 游标续页。各运行的两个临时库已清理并确认不存在。两库使用同一组 261 个发布文件，API SHA-256 为 `445E9B73EB39277C8D395677DCDDE5E14B48AB834890ACB79C985A2AC4F6B9A5`；正式两库运行的 2009 个相关源码/配置输入也相同。这是该历史本地验证发布物的身份，不是当前源码新构建结果或 R2 候选身份。随后 BUG155/157/159 分别由 PR156/158/160 修复生产 Space 校验、楼层和任务领取事务恢复并交付；WP6 接入后以新的 claim-fix 执行目录和当前源码发布物完成最终矩阵及应用验收，保留原件及历史适用范围。

正式两库 Matrix 和最终归档、远端交付仍待完成，因此此时不声明 WP6 或父任务完成。Application 分项均记录 `FullAcceptance=false`，不能改称单次 Full。后续结论以 [WP6 实施记录](../audits/database-compatibility/WP6-IMPLEMENTATION.md)、对应实际原件和远端 main 包含性核对为准。部分阶段、旧证据复用或本地 `Passed` 都不能替代完整验收与生产发布门禁。
