# WP02-02 / wave10：PostgreSQL 消息存储初始化与恢复

范围基线：`8823c31421c1346d2750faee8da16d016ff5fc56`；任务分支 `codex/wp02-02-provisioning-20261016`。对应[执行包](../../../dispatch/20261016-wave10/WP02-02.md)。本报告只交付 `erp-identity-messaging-v1` 存储初始化候选；完整 WP02-02 保持 IN_PROGRESS。候选提交、PR 与远端确认由执行对话完成后补入 Handoff，COORD 负责共享状态与合并。

## 改动

- 新 CLI `tools/CP6.TenantStorage/` 与 Core Tenancy provisioning。显式选择已有空隔离数据库，核对 cluster system identifier、database OID/name、控制身份及数据库 Owner；不从业务连接推断目标，不创建数据库或运行登录，不接 WebApi 启动。控制身份需独立 superuser；预建普通运行登录不得具有额外成员资格、Owner、角色配置、DDL/对象授权。运行密码不进入工具。
- 单一事务按 ERP PostgreSQL baseline → ERP RLS overlay → Identity PostgreSQL baseline → Identity overlay 安装，保留两个真实独立 EF migration history；历史迁移和原安装器未修改。只生成精确固定 baseline 的无事务 SQL，在调用方事务内执行，保存实际脚本 SHA-256 与准确 profile/migration 身份。
- 首装建立受保护目录、Owner-only 操作结果及租户 schema 本地 `initialization_history_v1`；本地记录只代表该共享消息 profile 初始化，不冒充业务表迁移。后续租户先验证所有已安装结构、有效权限、OID+名称映射、准确 EF 历史及每个租户本地历史，再追加映射/schema/最低运行授权与既有策略角色列表。
- 操作 UUID、规范非敏感输入、目标身份、租户/登录 OID、脚本/profile/迁移版本和结果持久保存。相同操作/输入返回原始结果，包括原完成时间；相同操作不同输入及重复租户/登录映射拒绝。数据库级 advisory transaction lock 串行化首装/追加，随后使用原目录锁；事务提交前失败全部回滚，提交后丢响应可在新连接中恢复原结果。
- 首装拒绝未知对象、event trigger、额外 extension、非默认系统对象、custom cast/operator/dependency 和不安全初始授权。后续目录验证及结构 seal 检测列/默认值/约束/索引/RLS/触发器/函数/ACL/角色/成员资格/default ACL/依赖等变化，业务行与 sequence 当前值不进入 seal。seal 仅来自本工具受控的首次原子安装，不能接管旧布局。
- CLI 只接受 `provision --stdin` 的有界 JSON，拒绝额外字段、递归重复键、非法参数；凭据仅经重定向标准输入，规范输入/输出/异常无凭据或底层 SQL。允许原控制身份轮换密码后以同一操作重试。

## 验证与证据

仅执行相关 CLI Release 构建和 `TenantProvisioningPostgresTests` 的真实 PostgreSQL 场景，未跑全仓。SDK `.NET 8.0.424`，PostgreSQL `18.6`，独占端口 `55436`，所有数据库均为自有新 `tenant-connections-<GUID>` 集群。运行结束由 fixture 核对 data_directory/postmaster/端口后停止；数据和失败日志保留。

最终 attempt7 场景通过（1 场景、0 失败、0 跳过，8 秒），实际覆盖：首次安装；第二租户追加；同输入重复/并发与冲突；提交前故障到达真实 before-commit 检查点且 schema/helper/history/results 全回滚；提交后故障到达真实 after-commit 检查点，并在**重试之前**由独立连接读取已提交结果及本地历史，随后恢复完全相同结果；未知 operator/cast/布局/授权拒绝；结构、目录授权、EF 历史和本地历史篡改拒绝；普通 A/B 实际 ERP 可见性；追加 B 的真实 Identity Outbox INSERT/READ、A 不可见、跨租户 INSERT/UPDATE 拒绝；运行身份无 schema/table DDL、目录/结果读取和 helper SET ROLE 权限；业务数据与 RowVersion sequence 前进后仍可恢复。真实子进程 CLI 通过 stdin 成功重放，并对非法参数/JSON/递归重复键/错误密码返回无凭据的错误。

最终 CLI Release build7 通过（0 error），pg_depend 守卫与最终实现的 attempt7 通过。原始证据入口：[构建日志](wave10-cli-build-7.log)、[测试日志](wave10-attempt7.log)、[TRX](wave10-attempt7.trx)。最终自有根为 `D:/CP6-local-test/WP02-02/tenant-connections-50d91ae0a97444bb9c2ee306c647a666`；全部 7 个自有集群均有停止记录且无 postmaster.pid，55436 无监听。不复制私有集群、密码文件、重复哈希清单或整套旧证据进仓库。

保留的开发失败：首次/第二次编译分别发现局部变量命名冲突及修复中的拼写错误；真实 PG attempt1–3 在身份参数绑定阶段失败，定位为 Npgsql `uint` 推断，改为显式 `NpgsqlDbType.Oid` 后 attempt4/5 通过。attempt6 的新 pg_depend 键拼接触发 PostgreSQL 42725（internal char 运算符歧义），显式 deptype::text 后 attempt7 通过。开发失败原日志保留于本目录：[首次编译](wave10-cli-build.log)、[第二次编译](wave10-cli-build-2.log)、[PG attempt1](wave10-attempt1.log)、[attempt2](wave10-attempt2.log)、[attempt3](wave10-attempt3.log)、[attempt6](wave10-attempt6.log)。全部原始日志、TRX 和集群仍保留于本地，不覆盖或改称成功。既有 `System.Security.Cryptography.Xml 8.0.4` NU1903、测试依赖 MessagePack NU1902 及既有编译警告保持可见，未升级依赖或抑制警告。

原 ERP/Identity 安装器、RLS helper、池连接、RowVersion、同事务适配、主 EF 模型和历史迁移未改变；复用[连接组件](../tenant-connections/DELIVERY.md)、[ERP](../erp-tenant-isolation/DELIVERY.md)和[Identity](../identity-priority-tenant-isolation/DELIVERY.md)的既有验证及其原范围，不声称重新执行。新增 CLI 只有自身 lock，已有依赖/lock/solution 未改。Core 项目文件仅增加 CLI 所需 InternalsVisibleTo。

复现使用 .NET 8、已按 lock restore 的当前工作树和 PostgreSQL 18；本次最终构建/测试均使用 `--no-restore`。PowerShell 入口如下，必须为每次测试生成新的自有根，不复用上述已停止集群；CLI 子进程通过 PATH 使用同一 .NET 8：

```powershell
$env:NUGET_PACKAGES = 'D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:PATH = 'C:/Users/tt/.dotnet;' + $env:PATH
& C:/Users/tt/.dotnet/dotnet.exe build tools/CP6.TenantStorage/CP6.TenantStorage.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --nologo
$env:CP6_TENANCY_PG_ROOT = 'D:/CP6-local-test/WP02-02/tenant-connections-' + [guid]::NewGuid().ToString('N')
$env:CP6_TENANCY_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
$env:CP6_TENANCY_CLI = Join-Path (Get-Location) 'tools/CP6.TenantStorage/bin/Release/net8.0/CP6.TenantStorage.dll'
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~TenantProvisioningPostgresTests --logger 'trx;LogFileName=wave10-provisioning.trx' --nologo
```

实际最终命令另将 stdout/stderr 定向到上列 build7/attempt7 日志，并使用 `--results-directory D:/CP6-local-test/WP02-02/wave10-attempt7` 保存 TRX。fixture 自行生成私有凭据，命令不含凭据。

## 边界与集中审查

一次任务级 SPEC/QUALITY 集中审查中发现并修复未知 event-trigger/extension/operator/cast/依赖接管缺口，以及提交后恢复/追加 B Identity 授权的实际验证不足。审查代理 wave10_provisioning_review 完成修改部分复查并直接核证 build7/attempt7，最终 SPEC/QUALITY PASS，无剩余阻断项。执行对话已归档原始证据、补齐复现命令，并检查最终 diff/链接；未重复整轮审查或未变测试。

控制端应选择静默初始化窗口。目录优先控制锁与已经持有消息表锁、随后调用目录 helper 的运行查询可能发生死锁；PostgreSQL 检测/15 秒 lock timeout/60 秒 statement timeout 使事务回滚，须用同操作/输入重试。CLI 总超时三分钟；异常或网络错误不能证明没有提交。该工具不承诺运行流量无中断，也不协调任意并发管理员 DDL。

未操作既有业务数据库，未创建/轮换运行凭据，未执行 Owner/WebApi 接线、生产迁移或部署，未宣称原 17 PGT/真实业务验收完成。未触发或重跑 Actions，无本任务需取消的运行，无工作流触发变更。生产身份验证、真实业务写集及后续采用由各自执行窗口完成。
