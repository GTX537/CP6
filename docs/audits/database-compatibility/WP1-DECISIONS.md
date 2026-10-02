# DB-COMPAT-01 WP1：基础实现与方案验证

日期：2026-10-02。父任务：[Issue #134](https://github.com/GTX537/CP6/issues/134)。本阶段从已确认的 `main / origin/main` 基线 `685a5238734816f6a19b21ce60f76d4bf93cd796` 开始，使用独立分支 `codex/db-compat-wp1-20261002`。

**状态：WP1 本地门禁完成，方案已冻结，准备正常 PR 交付。** 配置测试 133/133、SQL Server 限量真库探针 28 Passed / 1 NotApplicable、PostgreSQL 18.6 探针 31 Passed / 1 Rejected，均无 Failed / Blocked；Rejected 为实际旁路写入淘汰的 SaveChanges-only 应用令牌候选。集中审查及必要局部复查没有遗留实质问题，两库临时数据库已校验所有权后清理。WP2 可按下表冻结方案实施完整模型与迁移；本记录不代表整体兼容已完成，PostgreSQL 应用 runtime guard 保留至后续适配与验收完成。

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
- 实际 Platform EntityFramework `[0.10.2]` 的固定源码、包签名与哈希见 [WP1-PLATFORM](WP1-PLATFORM.md)。静态审计没有 Provider 专有 SQL，但四个私有 setter 的 `byte[] RowVersion` 需要数据库生成能力；`ExecuteUpdateAsync` claim 不赋应用 token。实际固定包的双库运行试验均通过，保留 0.10.2，消费者实现 PostgreSQL 模型覆盖与迁移管理触发器；不复制包源码替代。该签名 0.10.2 包内的运行 `AssemblyInformationalVersion` 实际为 `0.8.0-alpha.2+fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，来源 SHA 与包元数据一致；分别记录 NuGet 包身份与既有程序集版本元数据，不把它手工改写成 0.10.2。

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
| SQL Server 收尾完整限量探针 | 28 Passed / 1 NotApplicable / 0 Failed / 0 Blocked | [原始 JSON](evidence/wp1-sql-reviewed.json) |
| PostgreSQL 收尾完整限量探针 | 31 Passed / 1 Rejected / 0 Failed / 0 Blocked | [原始 JSON](evidence/wp1-postgresql.json) |
| 两库所有权拒绝 / 中途 DDL / 陈旧 token 谓词负对照 | 预期退出 1，失败与清理记录保留 | [证据目录](evidence/README.md) |
| 两库临时数据库整体清理 | 所有权一致、剩余对象/连接为 0，DROP 后两库均不存在 | [清理回执](evidence/wp1-database-cleanup.json) |

Provider 最终命令：

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~CP6.Tests.Persistence.DatabaseProviderTests --logger 'trx;LogFileName=database-provider-pinned-green.trx' --results-directory tmp/test-results/provider --disable-build-servers -m:1 -p:BuildInParallel=false --verbosity quiet
```

Provider 固定依赖 GREEN TRX SHA-256：`1a144d354ef809876ccba00d08958a1bd75ecff15970986d9ed15d33970dd159`。RED 和初次 GREEN 均保留，没有覆盖失败记录。收尾 GREEN 覆盖后续接线变更；不把配置测试说成业务、权限或全量迁移验收。

接线 GREEN TRX SHA-256：`4f4226735d29b259640f05f7e887a69bd05038e44f0899d25d97d9ec2e80b05e`。早期八份 Markdown 检查为 **8 文件 / 658 本地链接 / 0 错误**；收尾含新证据导航的检查为 **11 文件 / 694 本地链接 / 0 错误**。仅验证文件目标，不验证锚点或外部 URL；没有为文档改动重新构建或执行成功业务用例。

SQL 探针涵盖 native rowversion 的多写入路径、同事务 tenant generation / 墓碑 / 真子进程重启、两个代表实体 EF SaveChanges，以及四个真实 Context DatabaseFacade 与 Dapper 对限量表的共同提交/回滚；实际 Platform 包覆盖 enqueue / claim / stale lease / retry / dead letter / requeue / Inbox / checkpoint 竞争及 retention。详见 [探针证据与边界](WP1-PROBE.md)。四 Context 试验没有替换模型，但没有证明完整实体或迁移兼容；numeric Position 的分页夹具只证明 generation 机制，实际 `IdentitySnapshotReader` 仍待 WP3 接入，保留 Boundary 字符串、opaque NextCursor 和 LastAggregate 语义。切换 generation 时须使用 v2 protector / 格式判别，拒绝旧 rowversion 水位 cursor。

初次 SQL positive 报告中的 SourceSha 是分支基线，运行时源码含未提交改动；原报告没有候选输入 fingerprint，不把它称作最终候选 exact-SHA 证据。旧报告原样保留。集中审查发现 Outbox 竞争缺双方同候选屏障、checkpoint 结果断言过宽与重启空页假阳性，已修复并仅定向复查对应部分。最终 SQL 正向试验因这些实质断言修改和首次关联证据缺口重新执行一次：**28 Passed / 1 NotApplicable / 0 Failed / 0 Blocked**，记录 26 个源码输入及 9 个实际运行程序集哈希。随后 PostgreSQL 使用同一已编译程序执行 **31 Passed / 1 Rejected / 0 Failed / 0 Blocked**，全部输入哈希相同，fingerprint 为 `0f0b443961674d40b8163768225ae12d1a55cd18f28fce0d5f16bb0cb91c6bb3`。源码检查点 `d30da70e6698b73ceac0d23c16f16dc02d9b8429` 于 13:26:43 UTC 保存，PG 于 13:26:54 UTC 启动；原 JSON 的 CLI SourceState 标签保留启动准备时的旧假设，动态 SHA、源码/二进制哈希及本说明准确建立关联。没有为提交/SHA 改变重跑 SQL。原始编译命名冲突失败和修正后零警告/零错误记录均保留。PG 证据只读收尾复核无实质阻断，复用此前完整审查结论，没有另起整仓审查。

## 已冻结决策与后续安装职责

| 门禁 | WP1 冻结结论 | 后续实施与验收职责 |
| --- | --- | --- |
| 并发 token | SQL Server native rowversion；PG `bytea`、8 字节、数据库 BEFORE INSERT OR UPDATE 触发器生成。私有 NO CYCLE bigint sequence 经 `pg_catalog.int8send` 编码；bytes opaque，不作为 commit-order 水位 | WP2 将函数/序列/触发器纳入迁移，为四 Context 全部实际 RowVersion 表登记并安装；EF concurrency + AddOrUpdate + save Ignore；WP3–WP6 验证具体调用 |
| 应用生成 token | 淘汰本次 SaveChanges-only 候选。实际 raw / ExecuteUpdate 漏更新导致陈旧写入覆盖 | 不引入该候选；不是宣称所有可能的完整应用写入设计均不可行 |
| Platform `[0.10.2]` | 保留签名固定包。实际四实体、bulk claim、lease、重试/重放/死信、Inbox/checkpoint/retention 双库通过 | 消费者统一 PG 映射与触发器；严格 UTC 输入，非零 offset 明确 `ToUniversalTime`；WP3 分类与全事务重试，40001 generic RetryScheduled 不能替代分类 |
| 身份分页边界 | 两库使用同事务推进的持久化 tenant generation。短页事务同步读 generation / directory，继续页 generation 变化则拒绝；墓碑保留版本，物理删除也推进边界 | WP2 前向 SQL Server / PG 迁移增加 generation 表和 directory 变更触发器；WP3 接入现有 Boundary/LastAggregate 排序与 opaque NextCursor，v2 protector/格式判别拒绝旧 rowversion cursor；墓碑 purge/replay retention 后续落实 |
| 四 Context 共事务 | 两库 actual 四 Context DatabaseFacade + Dapper 限量写入，共实际 DbConnection / DbTransaction；commit / rollback 通过 | WP3 接入具体跨 Context 工厂与调用；WP4–WP6 验证实体、完整迁移、真实业务/传输 |
| PostgreSQL 支持与映射 | WP1 实测 PostgreSQL 18.6，目标 18 系列；EF8.0.30 / Provider8.0.11 / driver8.0.8；独立程序集、四组迁移、history 显式 public | WP2 按 [映射清单](WP2-MAPPING-CHECKLIST.md)完成 types / filters / checks / names / defaults。业务本地时间不能 blanket UTC；Sys_Lang 全局 NULL 唯一性明确约束。支持版本的完整验收在 WP6 |
| 全量模型/迁移与业务 | 未开始；PG runtime guard 保留 | 后续 WP2–WP6 的适用门禁；不把限定表探针冒充整体可运行 |

## 所有写入路径覆盖与 DDL 清单

下面记录已实际验证的机制与生产安装责任；探针按路径覆盖，不声称逐一执行全部 208 个现有 snapshot 并发映射。原生 sequence 的空洞可出现，API 只比较 bytes。

| 路径 | 双库真实结果 | 安装 / 调用要求 |
| --- | --- | --- |
| INSERT、默认值、EF SaveChanges、私有 setter 回读 | INSERT / UPDATE 返回精确 8 字节，数据库回读一致；陈旧 update / delete 拒绝 | WP2 四模型逐表 RowVersion 安装；不可将 PG IsRowVersion 映射为 xmin |
| Dapper 条件 UPDATE 与 OUTPUT / RETURNING | 替换 bytes；陈旧谓词影响 0 行，当前值影响 1 行 | WP3–WP5 具体 writer 显式传旧 token、检查行数；共享事务必须传实际 transaction |
| ExecuteUpdate / Platform claim | 原始 bulk UPDATE 自动替换 bytes；双方同候选屏障到达 2 后仅一个 owner；旧 lease 拒绝 | 数据库 trigger 覆盖 token 更新；bulk writer 仍须显式业务 fence / token 条件，EF 不自动加入乐观谓词 |
| 原始维护 SQL、同值 UPDATE、其他 trigger 发起 UPDATE | 各路径均更换 bytes；SaveChanges-only 候选在 raw/bulk 两次实际失败 | WP2 迁移安装后维护/seed/升级走相同数据库 trigger；不得禁用它或把旧 token 写回 |
| replay audit、事务中止 | bytea / binary(8) 与 Base64 保留精确原值；回滚恢复数据及 committed token | PG 重放输入为普通 8 字节二进制列，不能误装成自动生成 RowVersion |
| 身份写入、乱序/重复、墓碑/DELETE、跨页/跨租户、真实子进程 | 同事务 generation commit/rollback；旧 continuation 拒绝；精确恢复位置 3、4；撤销后不复活 | WP2 generation 表/触发器覆盖实际目录 mutation；WP3 reader/writer/v2 cursor 与保留策略，WP4 真消息 / 安全验收 |

PG 迁移管理对象至少包含四 Context 各 schema 的 token sequence / function / 逐表 trigger、并发列长度约束、身份 tenant generation 表及变更 trigger；具体对象名、合法标识符与完整表清单由 WP2 模型 manifest 固定。SQL Server 原生 rowversion 不改；generation 为新增前向迁移。历史财务不可变 trigger、OIDC raw DDL、MES 报表和数据修复的最终业务要求不能遗漏，按 WP2 清单逐项验收。

## 本机隔离与执行边界

SQL Server `localhost\KOUSQLSERVER` 的 Windows 集成登录已验证，版本 `16.0.1000.6`。本任务创建独立库 `CP6Compat_WP1_20261002_0b54dc81`，以数据库 extended property 固定随机任务 owner；清理须校验该 owner，不操作已有业务库。探针还须在独立随机 schema 中安装自己的 owner 标记，并只清理显式对象。

PostgreSQL 18 的本机 5432 服务已就绪；用户已通过 pgAdmin 作为 `postgres` 登录。本地密码入口两次保存均遭服务拒绝；native psql 与 Npgsql 诊断均确认认证失败，Npgsql SQLSTATE=`28P01`，两次失败均未创建 PG 数据库。已核对 5432 listener 的 parent PID 属于 `postgresql-x64-18` 服务。用户随后在已登录 pgAdmin 执行本机专用测试账号脚本；随机凭据只保留 Git 忽略目录，任务真实登录成功，服务器版本 **18.6**，已创建 owner-marked 临时库并完成上述试验。用户无需事先建 CP6 库或配置应用；没有读取 pgAdmin 保存密码、重置管理员密码或改认证配置。

2026-10-02 13:34:15–13:34:17 UTC，清理前再次核对两库精确名称及任务 owner marker，剩余用户对象 / probe schema / 活动连接均为 0；原生 DROP 后从两个 maintenance catalog 确认数据库不存在。未使用 FORCE、终止会话或更改认证；专用 PG 本地测试角色保留给后续隔离库验证。[回执](evidence/wp1-database-cleanup.json)不含连接或密码。

本阶段截至当前未触发 Actions / Azure 桥、修改工作流或分支保护、切换既有 demo / DEV 环境、执行生产部署或已有数据搬迁。父任务保持 Open，准备正常 PR 交付；远端 main 包含性由交付回执记录，尚不提前宣称已合并。
