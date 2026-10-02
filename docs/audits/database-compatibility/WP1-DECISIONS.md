# DB-COMPAT-01 WP1：基础实现与方案验证

日期：2026-10-02。父任务：[Issue #134](https://github.com/GTX537/CP6/issues/134)。本阶段从已确认的 `main / origin/main` 基线 `685a5238734816f6a19b21ce60f76d4bf93cd796` 开始，使用独立分支 `codex/db-compat-wp1-20261002`。

**状态：WP1 进行中。Provider 接线与配置测试已通过；SQL Server 收尾限量真库探针 28 项通过，PostgreSQL 必要试验尚未执行，双库方案门禁未冻结。** 不开始批量 PostgreSQL 模型改写或全量迁移生成，不把安装了 PostgreSQL 或工厂选择成功记作兼容验收。

## 已实现的配置与工厂契约

- [DatabaseOptions](../../../CP6.Core/Persistence/DatabaseOptions.cs) 固定部署的 `Database:Provider`；缺键兼容旧配置，默认 `SqlServer`。显式空值、错误拼写或大小写、未知枚举值均拒绝；合法值精确为 `SqlServer` / `PostgreSql`，不根据连接字符串推断。
- [DatabaseConnectionFactory](../../../CP6.Core/Persistence/DatabaseConnectionFactory.cs) 每次创建未打开的新连接；[DatabaseContextOptions](../../../CP6.Core/Persistence/DatabaseContextOptions.cs) 支持字符串及调用方持有的实际 `DbConnection`。共享连接保持非拥有模式，错误 Provider/连接类型组合提前拒绝；解析异常不回显凭据或保留敏感内部异常。
- [Program](../../../CP6.WebApi/Program.cs)、Space 注册、identity 普通/priority dispatch 工厂与 ERP 工厂使用固定 Provider；Dapper 的请求连接也由统一工厂创建。已有 SQL Server MARS 检查保留，PostgreSQL 不经过 SQL Server 连接字符串解析器。
- [设计时配置](../../../CP6.Core/Persistence/DatabaseDesignTimeConfiguration.cs) 仅接收 Provider 环境值与显式命令参数，不加载应用 secret 文件或启动宿主。设计时四工厂使用同一迁移选择规则。
- [生产配置校验](../../../CP6.WebApi/Configuration/ProductionConfigurationValidator.cs) 保留 SQL Server 加密检查，增加 PostgreSQL Host / 多 Host 检查及 `SSL Mode=VerifyFull` 要求。该模式验证证书与主机身份；依据 [Npgsql 官方安全说明](https://www.npgsql.org/doc/security.html)。本地测试不使用生产连接。
- [运行支持门禁](../../../CP6.WebApi/Configuration/DatabaseRuntimeSupport.cs) 在启动初期拒绝 PostgreSQL 应用运行，避免模型、迁移及业务 SQL 尚未完成时写入数据库。完成后续兼容工作和适用验收后移除此阶段门禁；设计时与独立探针可以选择 PostgreSQL。

## 四 Context 的迁移身份

[DatabaseMigrationProfile](../../../CP6.Core/Persistence/DatabaseMigrationProfile.cs) 集中维护以下身份。PostgreSQL history 显式限定 `public`，避免连接 Search Path 改变迁移身份。SQL Server 现有程序集与历史表选择保持；不移动、重写历史迁移。

| Context | SQL Server | PostgreSQL history | PostgreSQL assembly |
| --- | --- | --- | --- |
| CP6Context | 原 Context 程序集与默认 history | `public.__EFMigrationsHistory` | `CP6.Persistence.PostgreSql` |
| SpaceContext | 原 Context 程序集，`__EFMigrationsHistory_Space` | `public.__EFMigrationsHistory_Space` | 同上 |
| IdentityMessagingContext | 原 Core 程序集与默认 history | `public.__EFMigrationsHistory_IdentityPriority` | 同上 |
| ErpIntegrationContext | 原 Core 程序集与默认 history | `public.__EFMigrationsHistory_ErpIntegration` | 同上 |

PostgreSQL 迁移程序集尚未建立；表格记录选择契约，不代表迁移已经生成或应用。

## 依赖身份与已执行本地验证

- 保持 .NET 8 / EF Core `8.0.30`；精确新增 Npgsql EF Provider `[8.0.11]` 和 Npgsql driver `[8.0.8]`。Core / Tests / WebApi / Space.Infrastructure 四份 lock 经正常 restore 生成；没有改 NuGet 源或签名信任规则。该组合由真实编译与配置测试验证，不以主版本相同代替验证。
- 仓库本地工具入口 `dotnet tool run dotnet-ef --version` 本次输出 `8.0.30`；迁移阶段使用该入口，不使用全局 EF 10 工具。
- 实际 Platform EntityFramework `[0.10.2]` 的固定源码、包签名与哈希见 [WP1-PLATFORM](WP1-PLATFORM.md)。静态审计没有 Provider 专有 SQL，但四个私有 setter 的 `byte[] RowVersion` 需要数据库生成能力；`ExecuteUpdateAsync` claim 不赋应用 token。SQL Server 已经通过实际包运行试验，PostgreSQL 尚待验证；不复制包源码替代。该签名 0.10.2 包内的运行 `AssemblyInformationalVersion` 实际为 `0.8.0-alpha.2+fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，来源 SHA 与包元数据一致；分别记录 NuGet 包身份与既有程序集版本元数据，不把它手工改写成 0.10.2。

本次按先失败、后实现执行局部测试。下面计数有包含关系，不相加：

| 本地执行 | 结果 | 原始证据（工作树 Git 忽略目录） |
| --- | --- | --- |
| Provider 基础 RED | 编译成功；38 失败 / 0 通过 / 0 跳过 | `tmp/test-results/provider/database-provider-red.trx` |
| Provider 固定依赖 GREEN | 38 通过 / 0 失败 / 0 跳过 | `tmp/test-results/provider/database-provider-pinned-green.trx` |
| 接线 RED | 编译成功；32 失败 / 77 通过 / 0 跳过 | `tmp/db-compat-wp1-tests/wiring-red/wp1-wiring-red.trx` |
| 接线收尾 GREEN，含基础与相关配置回归 | 133 通过 / 0 失败 / 0 跳过 | `tmp/db-compat-wp1-tests/wiring-final-green/wp1-wiring-final-green.trx` |
| 已编译 API 选择 PostgreSQL 的启动试验 | 退出 1，命中初期 runtime guard，未注册 worker 或连接数据库 | `tmp/db-compat-wp1-tests/wiring-postgresql-startup.json` |
| SQL Server 初次完整限量探针 | 26 Passed / 0 Failed；PG 应用 token 候选 1 NotApplicable，无必测跳过 | `D:\CP6\tmp\wp1-probe-sql.json` |
| SQL Server 故意省略陈旧 token 谓词的负对照 | 退出 1，实际发生预期覆盖，owner schema 清理通过 | `D:\CP6\tmp\wp1-probe-sql-negative.json` |
| setup 失败后的限定清理回归 | 退出 1，真实中途 DDL 失败；查询确认 schema 不存在 | `D:\CP6\tmp\wp1-probe-sql-setup-negative.json` |

Provider 最终命令：

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~CP6.Tests.Persistence.DatabaseProviderTests --logger 'trx;LogFileName=database-provider-pinned-green.trx' --results-directory tmp/test-results/provider --disable-build-servers -m:1 -p:BuildInParallel=false --verbosity quiet
```

Provider 固定依赖 GREEN TRX SHA-256：`1a144d354ef809876ccba00d08958a1bd75ecff15970986d9ed15d33970dd159`。RED 和初次 GREEN 均保留，没有覆盖失败记录。收尾 GREEN 覆盖后续接线变更；不把配置测试说成业务、权限或全量迁移验收。

接线 GREEN TRX SHA-256：`4f4226735d29b259640f05f7e887a69bd05038e44f0899d25d97d9ec2e80b05e`。上述八份本轮 Markdown 的相对目标检查为 **8 文件 / 658 本地链接 / 0 错误**；仅验证文件目标，不验证锚点或外部 URL。

SQL 探针涵盖 native rowversion 的多写入路径、同事务 tenant generation / 墓碑 / 真子进程重启、两个代表实体 EF SaveChanges，以及四个真实 Context DatabaseFacade 与 Dapper 对限量表的共同提交/回滚；实际 Platform 包覆盖 enqueue / claim / stale lease / retry / dead letter / requeue / Inbox / checkpoint 竞争及 retention。详见 [探针证据与边界](WP1-PROBE.md)。四 Context 试验没有替换模型，但没有证明完整实体或迁移兼容；numeric Position 的分页夹具只证明 generation 机制，实际 `IdentitySnapshotReader` 仍待 WP3 接入，保留 Boundary 字符串、opaque NextCursor 和 LastAggregate 语义。切换 generation 时须使用 v2 protector / 格式判别，拒绝旧 rowversion 水位 cursor。

初次 SQL positive 报告中的 SourceSha 是分支基线，运行时源码含未提交改动；原报告没有候选输入 fingerprint，不把它称作最终候选 exact-SHA 证据。旧报告原样保留。集中审查发现 Outbox 竞争缺双方同候选屏障、checkpoint 结果断言过宽与重启空页假阳性，已修复并仅定向复查对应部分。最终 SQL 正向试验因这些实质断言修改和首次关联证据缺口重新执行一次：**28 Passed / 1 NotApplicable / 0 Failed**，记录 26 个源码输入及 9 个实际运行程序集哈希；只读复核全部匹配当前输入，input fingerprint 为 `0f0b443961674d40b8163768225ae12d1a55cd18f28fce0d5f16bb0cb91c6bb3`。报告为 `D:\CP6\tmp\wp1-probe-sql-reviewed.json`，原始编译命名冲突失败和修正后零警告/零错误记录均保留。任务级审查及其局部复查无遗留 P0/P1/P2 源码问题，PG 必要门禁继续阻止交付。

## 尚需真实数据库证据的决策

| 门禁 | 当前结论 | 完成所需证据 |
| --- | --- | --- |
| PostgreSQL 并发 token | 数据库生成 8 字节候选待实测；尚未选定 | 插入、EF SaveChanges、ExecuteUpdate、原始 SQL、触发器及实际消息包路径；8 字节 / Base64 与陈旧 token 拒绝 |
| 应用生成 token | 已查明仅 SaveChanges 拦截无法覆盖包 claim；需实验记录 | 实际旁路写入的漏更新与陈旧写入反例，不能以静态判断冒充运行结果 |
| Platform `[0.10.2]` | 签名与固定源码已核验；SQL 多路径已通过，PG 未验证 | 私有 setter 生成值回读、claim / lease 竞争、重试/重放/死信、Inbox/checkpoint 并发与事务 |
| 身份分页边界 | 候选为同事务推进的持久化 tenant generation | 跨页写入拒绝或重启、墓碑、物理删除、跨租户、回滚及真实进程重启；保留现有游标/API 外形 |
| 四 Context 共事务 | 配置与 SQL 实际 DbFacade 限量写入已通过，PG 未验证 | 四个真实 Context 的实际连接/事务身份与写入提交/回滚；Dapper 显式加入同一事务 |
| PostgreSQL 全量模型/迁移与业务 | 未开始 | 后续 WP2–WP6 的适用门禁 |

## 本机隔离与执行边界

SQL Server `localhost\KOUSQLSERVER` 的 Windows 集成登录已验证，版本 `16.0.1000.6`。本任务创建独立库 `CP6Compat_WP1_20261002_0b54dc81`，以数据库 extended property 固定随机任务 owner；清理须校验该 owner，不操作已有业务库。探针还须在独立随机 schema 中安装自己的 owner 标记，并只清理显式对象。

PostgreSQL 18 的本机 5432 服务已就绪；用户已通过 pgAdmin 作为 `postgres` 登录。本地密码入口两次保存均遭服务拒绝；native psql 与 Npgsql 诊断均确认认证失败，Npgsql SQLSTATE=`28P01`，两次失败均未创建 PG 数据库。已核对 5432 listener 的 parent PID 属于 `postgresql-x64-18` 服务。用户随后在已登录 pgAdmin 执行本机专用测试账号脚本；随机凭据只保留 Git 忽略目录，任务真实登录成功，服务器版本 **18.6**，已创建 `CP6Compat_WP1_20261002_0b54dc81` 并设置 `DB-COMPAT-01-WP1:<owner>` 数据库 comment。PG 限量试验正在执行，尚无通过结论。用户无需事先建 CP6 库或配置应用；没有读取 pgAdmin 保存密码、重置管理员密码或改认证配置。

本阶段截至当前未触发 Actions / Azure 桥、修改工作流或分支保护、切换既有 demo / DEV 环境、执行生产部署或已有数据搬迁。父任务保持 Open，任务分支尚未交付；阶段完成时补齐真实试验结果、清理回执、集中审查与远端 main 包含性。
