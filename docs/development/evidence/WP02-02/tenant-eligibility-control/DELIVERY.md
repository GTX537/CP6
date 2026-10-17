# WP02-02 / 第十三轮 B：已登记服务租户资格目录控制

2026-10-17。本窗口执行者为用户启动的新 Codex 对话 B（会话标识 `01a1480c-fed2-7e42-bce0-9b3db2477bfe`）。分支 `codex/wp02-02-tenant-eligibility-20261017`，工作树 `D:/CP6-worktrees/wave13-b-tenant-control-20261017`。开工 fetch 后 HEAD/main/origin/main 均为 `4704592f52172c941e1cb95bdeb3c73f0dd179e8`，包含准入规划基线 `86b07dec1050f948cfb07d8916d618166bdefed4`。

产品与测试提交：`c098e29a0869e1235112b91f5f3eb2afb2594ddc`。本报告随后作为同一任务的文档提交保存；包含本报告的最终候选 SHA 由 Handoff DELIVERY 固定引用。候选交给 COORD 做一次集中 SPEC/QUALITY 审查、共享状态同步和串行合并；本报告不声称远端 main 已包含候选，也不关闭完整 WP02-02。

START 已发布并核对远端：Handoff `4c4f567b720de2fccb342a8ab11fe1e1e237db4c`，`handoffs/CP6-DEV-20261012/tasks/WP02-02/messages/20261017T041140Z-wave13-start/MESSAGE.md`。既有窗口已 CONFIRMED，发布一次真实 START 后直接实施，没有重复等待运输 ACK。

## 实际改动

- 新增内部 `ServiceTokenTenantDirectoryControlV1` 及私有 stdin 命令 `service-token-tenant-directory-control-v1 --stdin`，仅 `inspect`、`initialize`、`set-eligibility`。输入绑定原安装请求、准确 TenantId 与显式资格；未知/重复 JSON 字段拒绝，资格对象必须同时给出 Enable 和 ExpireDate（null 必须显式出现）。不接受请求自报 seal/fingerprint。
- 使用独立无池 control connection 和一个物理事务。前后核对实际 IP/port/database OID/cluster、独立 superuser/database owner 且 session_user=current_user、原 control-owned installation、原 canonical、编译冻结 baseline hash、原 profile/seal、完整准确 role OID/name/TenantId 映射。复用原 seal 计算，未更改 `ServiceRevocationInstallation`、`ServiceTokenProfileV1`、baseline、模型、helper、ACL 或历史安装恢复路径。
- 共享安装/映射 advisory lock 加保护表 SHARE 锁；不使用旧恢复验证器的 directory ACCESS EXCLUSIVE 锁。准确租户 `FOR UPDATE` 与原签发 helper `FOR SHARE` 串行，其他租户读取仍可进行。每租户事务 advisory lock 覆盖不存在行，原 PK/Code unique constraint 仲裁插入冲突。相同租户重复初始化返回 ALREADY_EXISTS，原行不变；另租户编码冲突返回 CODE_CONFLICT，不泄露冲突租户目录。
- initialize 用登记 TenantId 作为原 Id，显式 code/name/Enable/ExpireDate，TwoFactorMode=0、其余可空字段沿原默认，CreateDate/Creator 记录数据库本地时间和认证 control login。不创建 admin/角色/凭据/映射，不代替业务审计。字段保持冻结 bpchar、`cp6_ci_as_provider_v1` 及 UTF-16 容量；不 trim/改大小写，大小写和尾空格编码冲突按原数据库唯一索引处理。
- set-eligibility 使用两字段 `IS NOT DISTINCT FROM` CAS，只更改 Enable/ExpireDate 及原 Modifier/ModifyDate。其他字段不变；本地 timestamp without time zone、null 不限期、相等即过期语义保持。v1 输入显式限定为无 offset 的有限本地 wall time、PG 微秒精度，拒绝 Z/offset、.NET infinity sentinel 和超出微秒精度的值，避免隐式转换/截断。
- 当前状态、MISSING、ALREADY_EXISTS、CODE_CONFLICT、CAS_CONFLICT、NOT_APPLIED、UNKNOWN 分开输出。确认 precommit rollback 才报 NOT_APPLIED；commit 开始后异常、rollback 未确认或执行后的输出失败保守报 UNKNOWN。没有请求键/持久原结果槽，无自动重试；inspect 不证明某请求执行过，CAS 不能防 ABA。进程被杀或完全丢输出时，调用方仍需按 UNKNOWN 处理。旧安装器的同输入恢复承诺不扩展到新命令。

具体输入、退出码、时限和运行约束见 [CLI README](../../../../../tools/CP6.TenantStorage/README.md)。所有文件在本轮登记白名单内；未改共享台账、生成页和四份项目状态文件。

## 实际本地验证

SDK `C:/Users/tt/.dotnet/dotnet.exe`（.NET 8）、PostgreSQL 18.6、私有 NuGet cache `D:/CP6-local-test/WP02-02/adopt-a1/n`。未修改项目、依赖或锁文件。日志在 `D:/CP6-local-test/WP02-02/`，直接引用原件，不复制到仓库：

| 原始证据 | 结果及范围 |
| --- | --- |
| `tenant-control-restore-cli.log`、`tenant-control-restore-tests.log` | 两个相关根 locked restore 成功；原依赖漏洞警告保留。 |
| `tenant-control-build-cli-1.log`、`tenant-control-build-tests-1.log` | CLI/Tests 及必要依赖构建成功，0 error；既有 NU1902/NU1903、async/analyzer 警告未顺手修改。 |
| `tenant-control-test-1.log`、`tenant-control-results/tenant-control-1.trx` | 7/7 PASS、0 FAIL、0 SKIP；2026-10-17 00:22:56–00:23:40 America/New_York。真实 PG 和真实 CLI，没有用 mock 证明权限或原子性。 |
| `tenant-control-run-1-root.txt` | 自有新根 `tenant-connections-173dfb7d90de48f38fc26a68696c72a2`；端口 55436。该根 `server-version.txt`=18.6，`stopped.txt` 确认按实际 data_directory、ownership/postmaster PID 核对后停止；数据、日志保留。 |

实际场景包括：

1. 单租户 inspect、未登记拒绝、缺行查询/更新、合法初始化、重复不覆盖、Code 唯一冲突、大小写/尾空格等价、Unicode 容量边界、引号参数值，以及业务 DML 后原安装恢复仍成功。
2. true/false/null/有限期限的 CAS、旧状态或期限差一微秒冲突、未涉及列完整保留；通过原 production eligibility helper 验证 null、将来一微秒、相等、过去一微秒的实际资格判断。
3. 同一缺行同时初始化、不同租户同 Code 竞争；由 pg_stat_activity 确认真实锁等待，分别得到 INITIALIZED/ALREADY_EXISTS/CODE_CONFLICT，没有重复行。
4. 原 helper 先持 FOR SHARE 时 control 等待、control 先持行锁时 helper 等待，提交后读取新资格；A 被锁时 B helper 可读。没有全目录排他锁假装成准确行竞争。
5. 错误 database/OID/system/installation/mapping、runtime login、非数据库 owner 的 superuser、profile/fingerprint/baseline/canonical/issuer 元数据漂移、额外目录 SELECT 权限和未知安装拒绝；恢复原值后正常读取，拒绝期间资格未改变。
6. 写后取消及写后异常确认 rollback；等待行锁时取消；commit 后注入输出丢失报 UNKNOWN、真实行已改变；重试旧 CAS 得冲突，状态 ABA 后旧 CAS 可再成功，明确不冒充请求幂等。
7. 实际 apphost 仅经 redirected stdin 运行 inspect/initialize/CAS、重复和冲突；缺失 Enable/ExpireDate、重复字段、自报 fingerprint、超长输入均拒绝，无旧“重试恢复”提示或凭据回显。

此轮没有失败构建或失败测试，日志均保留。没有为了提交 SHA 或文档新增而重复产品测试。未改变旧安装/恢复验证内核，因此没有重跑旧 profile 全套；新 fixture 本次实际运行了原 service-token 安装恢复，初始化测试还在新 DML 后实际恢复。签发/自撤销端到端、旧 profile 兼容的未变证据继续仅按[上一轮交付](../service-issuance/DELIVERY.md)真实范围引用，不改称本轮新执行。

复现（先按原仓 NuGet 规则 locked restore，构建 CLI；新根必须不存在）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:DOTNET_ROOT='C:/Users/tt/.dotnet'
$env:CP6_TENANCY_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_TENANCY_PG_ROOT='D:/CP6-local-test/WP02-02/tenant-connections-'+[guid]::NewGuid().ToString('N')
$env:CP6_TENANT_CONTROL_CLI='D:/CP6-worktrees/wave13-b-tenant-control-20261017/tools/CP6.TenantStorage/bin/Debug/net8.0/CP6.TenantStorage.exe'
& C:/Users/tt/.dotnet/dotnet.exe build tools/CP6.TenantStorage/CP6.TenantStorage.csproj --no-restore -v:q
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --filter FullyQualifiedName~ServiceTokenTenantDirectoryControlPostgresTests --logger 'trx;LogFileName=tenant-control.trx' --results-directory D:/CP6-local-test/WP02-02/tenant-control-results -v:q
```

## 差异自检、Actions 与剩余范围

执行者检查了相对当前已确认 main 的完整任务 diff、输入与参数绑定、准确行写集、原 seal/唯一约束/metadata、事务回滚与 UNKNOWN 分支及证据范围；`git diff --check` 通过。没有新增生产凭据、机器专属运行配置、调试入口或范围外改动；本地环境路径只在测试复现文档中出现。Windows 普通 git status 的历史长路径假删除在 `git -c core.longpaths=true status` 下消失，没有修改/暂存这些历史文件。COORD 的一次集中审查及必要修正仍为后续合并门禁。

开工时该任务分支 Actions run list 为空，无需取消。候选与 `origin/main` 的 13 份 workflow 内容一致：12 份仅 workflow_dispatch，r2-candidate 仅 `v*.*.*` tag push；本次只推任务分支、不推 tag，无 workflow/依赖触发修改、无主动远程运行。Handoff 仓仍私有、Actions disabled、候选/默认分支无 workflows；只追加本人任务消息，远端回读后才称送达。

未验证/未交付：完整租户创建生命周期、在线配置或角色/凭据更新、任意并发 superuser DDL fencing、持久请求原结果恢复、业务审计、全 Main 启动/宿主采用、原 17 PGT、真实业务/生产验收、正式制品或部署。现有数据库未迁移、未替换；新实例只是合成租户的自有真实数据库组件验证。完整 WP02-02 仍 IN_PROGRESS/CONFIRMED，不提升业务验收。共享状态、最终集中审查、串行 PR 合并及远端 main 核对由 COORD 接续；保留本任务分支/worktree。
