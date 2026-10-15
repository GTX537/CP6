# WP02-02 Main 正式包采用交付

2026-10-15。本报告覆盖 [PLAN](PLAN.md) 中的实际依赖采用、锁文件恢复和本地兼容性检查；完整 WP02-02 仍有后续实现与真实验收。
Main 基线 `5816d04eaff210db7a3b788390e4f560a67603f7`，任务分支 `codex/wp02-02-main-adoption-20261015`。
Platform 正式版本 `0.10.3`，源码 `1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`。

## 实际改动

- `CP6.Core.csproj` 的 Messaging、EntityFramework，`CP6.WebApi.csproj` 的 AspNetCore，以及 `CP6.DatabaseCompatibility.Probe.csproj` 的 EntityFramework，由精确 `[0.10.2]` 改为 `[0.10.3]`。
- [依赖提案](../consumption-preparation/DEPENDENCY-PROPOSAL.md) 列出的 16 份锁全部由真实 `dotnet restore` 生成。没有手改 `contentHash`，没有把 Platform 改为 ProjectReference。
- `NuGet.config` 的必需签名、固定 signer 和 source mapping 保持原样；独立历史 `tools/p10` Release `[0.10.1]` 及其三份锁保持原样。
- 本实现未修改业务代码或测试源码、EF 映射、宿主配置、迁移、工作流、数据库与部署配置。总台账、状态与正式 consumer 身份资料由 COORD 在同一任务交付窗口管理。
- 集中审查发现数据库探针成功提示硬编码旧版本 `0.10.2`，已改为实际消费包的中性表述；只修正输出文字，不改变实验行为。旧 README 中的 0.10.2 运行范围保留，不冒充本版本运行通过。

实际 Main 闭包为 Abstractions、AspNetCore、Contracts、EntityFramework、Messaging 五包；Deployment、Release 不加入产品依赖闭包。
从独占空缓存恢复的五份 `.nupkg` SHA-256 均与[正式 publication](../../authorized-adoption-release-20261015/publication/formal-package-publication.v1.json) 一致，且每份 NuGet metadata 的 source 均为真实 GitHub feed。
[本地结果](local-results.json)记录这些实际摘要、恢复目标、测试筛选和计数。

## 非 Platform 锁差异

14 份锁只更新 Platform 包节点与项目节点中对应的 Platform 精确依赖。
另两份旧锁在本基线中滞后于现有源码，真实恢复同时补齐下列既有闭包；没有升级、删除其他第三方包或改变其内容哈希：

| 旧锁 | 本次恢复补齐的既有依赖 |
|---|---|
| `eng/crm/erp-live-fixture/packages.lock.json` | Npgsql `8.0.8`、Npgsql.EntityFrameworkCore.PostgreSQL `8.0.11`；`cp6.core` 的这两个既有精确依赖；`cp6.webapi` → `CP6.Persistence.PostgreSql`；该 Persistence 项目到 Core / Space.Infrastructure 的引用 |
| `eng/crm/legacy-exit-tests/packages.lock.json` | 相同两个 Npgsql 包，以及 `cp6.core` 中对应的既有精确依赖 |

这些声明在 Main 基线的 Core/WebApi/Persistence 项目中已存在；例如 Core 的 PostgreSQL 引用来自历史提交 `d30da70e6698b73ceac0d23c16f16dc02d9b8429`。
保留 NuGet 实际生成的补齐结果，避免为缩小文本差异而保留错误闭包。逐项差异见 `local-results.json.nonPlatformDrift`。

## 本次实际本地验证

使用 `C:/Users/tt/.dotnet/dotnet.exe`：SDK `8.0.424`、运行时 `8.0.30`，配置为 Release。
独占缓存/原始输出目录为 `D:/CP6-local-test/WP02-02/adopt-a1`；没有复用其他任务的 cache 或测试资源。
凭据仅捕获到执行进程内并传给 dotnet 子进程，未写入文件或日志。

| 验证 | 实际结果 |
|---|---|
| 16 个入口真实 feed restore | 16/16 exit 0，保留原签名与审计 |
| 五个实际消费包的 archive SHA-256 / source | 全部匹配正式 publication / GitHub feed |
| WebApi 构建（隐含 Core 与相关依赖） | exit 0；5 warnings，0 errors |
| DatabaseCompatibility.Probe 构建 | exit 0；4 warnings，0 errors |
| CrmIdentityContractTests | 38 passed |
| ErpEventContractTests | 77 passed |
| PostgreSQL 定向模型测试 | 36 passed |

三组共实际执行 **151 passed / 0 failed / 0 skipped**。TRX 已检查非零匹配，且包含
`Actual_platform_private_setters_keep_generated_concurrency_metadata` 的 Core（4 实体）、IdentityPriority（4 实体）、ErpIntegration（2 实体）三个通过用例。
模型测试构造实际 provider model、生成 SQL/类型映射并检查并发元数据，不打开数据库连接。
两项 build 和 test 均使用 `-warnaserror:MSB3277`；实际输出无 MSB3277。

[完整脱敏命令与输出](local-validation.log)合并保留 16 次恢复及两项构建、一次定向测试的实际输出。
日志中的 `$WORKTREE` 为本任务 worktree，`$LOCAL_RUN` 为上述独占目录，`$DOTNET` 为上述明确的 SDK 8 可执行文件。
原始分项日志、执行器及 `test-results/contracts-model.trx` 继续保留在该本地目录，没有删除失败/重试记录。
恢复与构建的命令形式为：

```powershell
$env:NUGET_PACKAGES = 'D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:NUGET_HTTP_CACHE_PATH = 'D:/CP6-local-test/WP02-02/adopt-a1/http'
$dotnet = 'C:/Users/tt/.dotnet/dotnet.exe'
# 对 local-results.json.restore.projects 的每个项目执行；原 NuGet.config 不变。
& $dotnet restore <project> --configfile <worktree>/NuGet.config --force-evaluate --verbosity minimal
& $dotnet build CP6.WebApi/CP6.WebApi.csproj --no-restore -c Release -warnaserror:MSB3277 --verbosity minimal
& $dotnet build tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj --no-restore -c Release -warnaserror:MSB3277 --verbosity minimal
# 完整 --filter 参数和实际结果目录见日志及 local-results.json.tests.filter。
& $dotnet test CP6.Tests/CP6.Tests.csproj --no-restore -c Release -warnaserror:MSB3277 --filter <recorded-filter> --logger 'trx;LogFileName=contracts-model.trx' --results-directory <local-run>/test-results --verbosity minimal
```

首次 Core 恢复记录了 JsonPointer.Net、EF Abstractions、Logging Abstractions 下载超时；Oidc 入口记录了旧依赖 OpenIdConnect 下载超时。NuGet 在同一次命令内重试后成功，没有人工重跑整个恢复或放宽校验。
审计报告既有 `System.Security.Cryptography.Xml 8.0.4` 的四项 NU1903：`GHSA-7hqp-7c5m-4vq6`、`GHSA-c399-9853-6jmj`、`GHSA-r955-8q5j-3h83`、`GHSA-vrv5-2jg9-4mmr`。
其版本未变，本窗口未顺手升级或关闭审计；不能由本次构建成功推定正式发布漏洞门禁通过。
编译还报告未修改源码中的 CS1998 与 xUnit2017，完整位置保留在日志。

## 复用来源与边界

正式 producer 身份、七包发布及 Windows/Linux 签名/RFC3161 证据复用[一次正式发布记录](../../authorized-adoption-release-20261015/README.md)。本次新增证据是 Main 真实 feed 恢复、实际五包字节核对、构建与上述测试；没有另行触发 producer 发布或远程重验证。
旧 [consumption-compatibility](../consumption-compatibility/README.md) 的 0.10.2 宿主/图检查和 [consumption-preparation](../consumption-preparation/README.md) 的合成探针保留其历史范围，不作为 0.10.3 新包运行通过的依据。

以下保持 **NOT_RUN**：真实 PostgreSQL 存储/权限/事务、可信 binding/Owner 采用、原始 [17 PGT](../consumption-preparation/PGT-BOUNDARY.md)、LegacyOutboxFenceV2 的 PostgreSQL 运行路径、生产候选与部署。没有运行完整测试套件或新增 SQL Server 验收。
## 正式包身份记录

COORD 对照已批准且已发布的固定源码、原始 publication 和七包摘要，独立核对身份输入；
[identity-approval.json](identity-approval.json) 是本窗口技术核对所固定的调用参数，不是新的用户签名或运行兼容性批准。
任务私有 [IdentityInspector](../../../../../eng/wp02-02/main-package-adoption/Program.cs) 使用从真实 feed 恢复的 Release `[0.10.3]`；该工具依赖与产品五包闭包分开，且不改历史 P10 工具。
Release archive SHA-256 与原 publication 相同；工具实际 restore/build 通过，构建零警告、零错误。

实际调用正式 `Cp6CapabilityValidator` 生成 [matrix](matrix.json)、[consumer lock](consumer-lock.json) 和 [identity state](identity-state.json)。
准确来源、合同形状及七包原字节均为 Verified；capabilities / compatibility 均为空，兼容性、ConsumerQualified、HostRegistered、ConfigEnabled、RuntimeObserved、BusinessApplied 和 IndependentlyAccepted 均为 Unknown。
没有提供 signature callback，故本工具的 TrustCurrentlyAccepted 仍为 Unknown；原正式签名结果只复用此前发布记录，不伪装为本次重新验签或永久信任。
签名并未被禁用：实际 NuGet restore 仍使用未修改的 require / trustedSigners；不能把身份 lock 当作能力启用令牌。
错误 matrix pin 和错误 source pin 两个定向反例均返回 exit 2，且未产生输出目录。
准确命令和输出保留在 [identity-validation.log](identity-validation.log)；`$FORMAL_ARTIFACT` 指一次已核对的正式运行 38709310767 中 Windows artifact 11959124489 的解压目录。
其 trust policy / certificates 和七包 feed-readback 原字节保持，未另复制到产品目录。

## 集成与后续

一次任务级集中 SPEC/QUALITY 审查通过，无阻断项；审查者只读核对完整依赖、身份工具、证据与状态差异，
并对照实际包摘要、deps.json 和 TRX，没有重复运行测试。
探针提示修正由 COORD 复查该行及影响，增量构建 exit 0、4 项原有审计警告、0 errors；151 项行为结果按未变行为复用。
台账/生成视图检查、报告链接和 diff 检查通过；没有为文档启动额外产品验证。
台账已登记 COORD 实际领取和开始本步骤，完整 WP02-02 保持 IN_PROGRESS / CONFIRMED，不登记 DONE。
后续按已接受的 `cp6-pg-tenancy-v1` 推进最终 EF 模型/原始 SQL 写入清单、可信租户 binding、租户 LOGIN/DataSource、连接池上下文及隔离存储；原 17 PGT 留到相关实现完成时执行。
没有把新版本可构建等同于真实 Owner 接线或多租户数据库完成。

本次无 Actions 触发/取消/触发器变更、签名发包、数据库启动、迁移或部署；Main queued / in_progress 检查为空。
正常分支推送、PR、合并前核对工作流事件；提交和远端 Main 包含性在交付时核对，并由 Handoff 记录准确 SHA / PR。
当前报告不声称完整 WP02-02 或业务验收完成；历史失败、缺件和 125 目标的运行状态保持。
