# WP02-02 / B：受控 service self-revocation 有界采用

日期：2026-10-16。实现分支 `codex/wp02-02-service-revocation-20261016`，基线 Main `04a0f76ec940bdf11299459a100660f9fc7faa0b`。本报告与实现、测试同一任务提交；集中 SPEC/QUALITY 审查及 Main 集成由 COORD 在交付窗口执行。本交付不将 WP02-02、WP02-03 或 WP02-10 标为整体完成。

## 已实现行为

- 实际 `POST /connect/service-revocations` 保留严格 Basic、单个 canonical GUID-D jti、同 issuer/client/tenant、未知或已撤销返回相同成功的原合同。Basic 拒绝非规范 Base64 与无效 UTF-8。停用签发的客户端仍可用当前正确凭据撤销自己的记录；没有引入 issuance Enabled/scope/active-tenant 限制。
- `CrmIdentityConfiguration` 提供显式 opt-in 的生产 DI。专用 singleton 持有一致的 Platform 0.10.3 executor/accessor/池管理器，池预算跨请求共享。实际 Basic 再验证产生 Service actor，无自然人 Owner；host/issuer/region/purpose/client registration/storage mapping 都来自启动时冻结的受控配置。writer 使用独立冻结 runtime，避免 legacy 可变 Options 改写事件 issuer/region。
- source 期限默认 60 秒（上限 300），实例 epoch 防止旧宿主 source 在新宿主复用。最终写入和 commit 重验凭据、当前 actor/version/operation permission/期限。当前支持的是停止接入、排空全部实例、控制窗口更新、重启；不支持热更新，不宣称跨实例即时撤销。
- 真实 `CrmServiceTokenRecordStore` 提取同事务内核，由 `TenantStorageTransaction` 的普通 tenant login 连接运行真实 `CP6Context` / `IdentitySnapshotWriter`。没有嵌套事务、独立业务连接或自动重试。`ServiceToken.RevokedAtUtc`、Snapshot、generation trigger 和 priority Outbox 原子提交。
- 受管 context 的 CWT 标记只由内部 adapter 建立，`DatabaseSharedContext` 显式继承相同连接、事务和 EF guard。同步/异步 EF、child、raw snapshot advisory lock 走同一检查；legacy 未标记路径保留。commit fence 在子 context 结束后继续校验物理身份、映射和 Owner 授权。
- 目录 helper 的 shared advisory transaction lock 持有到 commit；映射控制写等待它。此锁只证明 storage mapping，不能代替 Owner 最终授权。提交前确定性故障全部回滚；提交后丢响应返回不确定失败，重试用持久撤销记录返回原结果，不增加 event/version/generation。

## 存储及安装边界

新增独立 `service-revocation-v1` / `service-revocation-tenant-rls-v1` fresh-only profile，由显式 CLI `install-service-revocation-v1 --stdin` 在安静控制窗口安装。它不改变旧 `erp-identity-messaging-v1` 的含义或恢复 seal。

| 对象 | 普通 tenant login 的有限权限 |
| --- | --- |
| `crm_identity.ServiceToken` | SELECT、仅 `RevokedAtUtc` 列 UPDATE |
| `crm_identity.Snapshot` | SELECT/INSERT/UPDATE，原 `(TenantId, AggregateId)` 键 |
| `public.CrmIdentityTenantGenerations` | SELECT/INSERT/UPDATE，由原 Snapshot trigger 推进 |
| `crm_identity_priority.Cp6_OutboxMessage` | SELECT/INSERT；原 MessageId 全局唯一键 |
| Core/Identity RowVersion 与文本容量辅助对象 | 所需 schema/sequence/function usage/execute |
| 受保护 directory | 仅固定 current_tenant helper，不能直接读写 |

四表从冻结 PostgreSQL baseline operations 精确选取，复用原类型、精度/容量、索引、generation/RowVersion 函数与事件 schema/ID/digest。没有复制实体或修改 EF model/migration/snapshot。部分安装**不写 full Core 或 IdentityPriority EF migration history**。安装 control owner、普通 tenant runtime、原发行 RecordAsync 使用的独立 issuance login 分离；没有 blanket Core 权限。

只允许显式数值 IP，严格相同 server/port/database 的发行 DefaultConnection；runtime 还核对真实 database OID、`pg_control_system` system_identifier、installation ID 和结构 seal。故障不会回退默认特权 CP6Context，也不能以无关空库的 unknown success 冒充采用。未知/变更布局拒绝；已有 cluster-wide directory helper 不被接管。当前不支持旧数据迁移、已有完整 Core 库 overlay、增租户、任意 DDL、恢复到新物理库或修改旧 profile；它们需要独立任务。

## 实际验证及证据

本地 .NET 8 / PostgreSQL 18.6、私有 NuGet cache；专属 loopback PG 55436，每轮新 `tenant-connections-*` 根。夹具检查端口、归属与 postmaster 后启动/停止，原数据与失败日志保留。Kestrel 使用独立随机端口与自签 HTTPS，HTTP client 只信任本次证书。没有接触已有业务库或切换环境。

验证宿主使用实际 production controller、DI registration、Basic verifier/authority、签名、`CrmOidcServiceTokens.IssueAsync`、真实 `RecordAsync` 与完整撤销存储链。唯一 issuance 支持替身是 `ICrmOidcServiceDirectory.IsServiceTenantActiveAsync`（A/B 返回有效），因为本有限 profile 不安装 Sys_Tenants；正常用户登录、租户有效性查询、bootstrap/dispatcher 不在本轮采用。无手工 ServiceToken 行替代发行；未知 jti 验证与真实已发行记录验证分别执行。

| 证据 | 实际结果 / 范围 |
| --- | --- |
| `D:/CP6-local-test/WP02-02/service-revocation-build-1.log`、`service-revocation-build-2.log` | 相关项目构建成功；现存包漏洞/历史 analyzer 警告保留，无依赖变更 |
| `service-revocation-results/service-revocation-1.trx` | 首轮安装拒绝：PG inet::text 含 `/32`；只读复现后改为 `host(inet_server_addr())`，不放宽身份条件 |
| `service-revocation-results/service-revocation-2.trx` | 权限项通过，HTTPS issuer 合同拒绝 HTTP 夹具；后改真实本地 TLS，未修改产品 URI 合同 |
| `service-revocation-results/service-revocation-3.trx` | 7 项真实 PG/HTTPS 行为通过 |
| `service-revocation-results/service-revocation-4.trx` | 120 执行/通过，0 fail/skip：8 项新 PG/HTTPS、既有 service token/revocation/config、tenant storage unit 相关回归 |
| `service-revocation-cli-build.log`、`service-revocation-cli-replay.log` | CLI 构建；私有 stdin 对第四轮真实数据库幂等恢复成功，同一 installation/profile/基线 digest |
| `service-revocation-results/service-revocation-5.trx` | 最终安装权限收紧后的 fresh install + 8 项新 PG/HTTPS 行为全通过，0 fail/skip |
| `service-revocation-results/service-revocation-guard-6.trx` | 2 项既有 Identity 定向回归通过：同步/异步 guard 漂移、嵌套/配置拒绝，以及真实 context 同事务 commit/rollback |

上表相对证据路径均位于 `D:/CP6-local-test/WP02-02/`；对应原始 stdout 为 `service-revocation-test-N.log`（第 6 轮为 `service-revocation-guard-6.log`），每轮 cluster 根记录在 `service-revocation-run-N-root.txt`。没有复制整套日志/哈希清单到仓库。第 4 轮验证后仅收紧安装 control 必须是 DB owner 和收尾拒绝额外 DDL/TEMP 权限，因此第 5 轮定向重验 fresh install；第 4 轮其余成功证据按未改变的实际代码/依赖范围复用。最终 `git diff --check` 无错误。

行为覆盖：A/B 普通登录、交叉租户/其他 client、unknown/repeat/concurrent revoke、strict Basic 拒绝且不打开 runtime pool、disabled-client 自撤销、伪造 authority/旧实例 source 拒绝、配置/物理目标/布局/目录漂移失败闭合、目录锁跨 commit、最终 source 过期回滚、提交前全写集回滚、提交后丢响应重试同一事件/generation、池复用无租户泄漏、无 DDL/目录权限。

已复用历史证据，仅在其真实范围内：[pool/连接组件](../tenant-connections/DELIVERY.md)、[ERP8](../erp-tenant-isolation/DELIVERY.md)、[Identity4](../identity-priority-tenant-isolation/DELIVERY.md)、[旧显式 provisioning](../provisioning/DELIVERY.md)、[Platform 0.10.3 Main 采用](../main-package-adoption/DELIVERY.md)。本轮不重跑或扩大这些组件的全套场景。

复现入口（PowerShell，仓库根目录；下列是已执行命令的参数/环境，每项单独运行并使用全新 PG root，不复用旧数据目录）：

```powershell
$dotnet = 'C:\Users\tt\.dotnet\dotnet.exe'
$env:NUGET_PACKAGES = 'D:\CP6-local-test\WP02-02\adopt-a1\n'
$env:CP6_TENANCY_PG_BIN = 'C:\Program Files\PostgreSQL\18\bin'
$evidence = 'D:\CP6-local-test\WP02-02'
$runs = @(
  @{ Name='service-revocation-4'; Filter='FullyQualifiedName~ServiceRevocationPostgresTests|FullyQualifiedName~CrmIdentityRevocationTests|FullyQualifiedName~CrmIdentityConfigurationTests|FullyQualifiedName~CrmOidcServiceTokenTests|FullyQualifiedName~TenantStorageUnitTests'; Extra=@() },
  @{ Name='service-revocation-5'; Filter='FullyQualifiedName~ServiceRevocationPostgresTests'; Extra=@() },
  @{ Name='service-revocation-guard-6'; Filter='FullyQualifiedName~IdentityTenantPostgresTests.Identity_guard_rejects_nested_scope_catalog_rotation_profile_and_mapping_drift|FullyQualifiedName~IdentityTenantPostgresTests.Real_contexts_share_one_connection_transaction_and_commit_or_rollback_together_with_real_tokens'; Extra=@('--no-build') }
)
foreach ($run in $runs) {
  $env:CP6_TENANCY_PG_ROOT = Join-Path $evidence ('tenant-connections-' + [guid]::NewGuid().ToString('N'))
  $extra = $run.Extra
  & $dotnet test CP6.Tests/CP6.Tests.csproj --no-restore @extra --filter $run.Filter `
    --logger ('trx;LogFileName=' + $run.Name + '.trx') `
    --results-directory (Join-Path $evidence 'service-revocation-results') -v:q
  if ($LASTEXITCODE -ne 0) { throw ('Failed: ' + $run.Name) }
}
& $dotnet build tools/CP6.TenantStorage/CP6.TenantStorage.csproj --no-restore -v:q
# CLI process arguments; its JSON document is written only to redirected private stdin:
& $dotnet tools/CP6.TenantStorage/bin/Debug/net8.0/CP6.TenantStorage.dll install-service-revocation-v1 --stdin
```

CLI 最后一行必须由受控调用器提供重定向 stdin；不能交互粘贴密码，也不能把凭据作为命令参数。实际 replay 使用 `ProcessStartInfo`（`UseShellExecute=false`、`CreateNoWindow=true`、`RedirectStandardInput/Output/Error=true`），将内存中的 JSON 写入 `StandardInput` 后关闭输入；参数就是上述 DLL 与 `install-service-revocation-v1 --stdin`。输入合同及前置条件见 [CLI README](../../../../../tools/CP6.TenantStorage/README.md#bounded-service-revocation-profile)。本次 replay 对第 4 轮已保留集群核对 `ownership.txt`/postmaster/55436 后临时启动，使用数据库中原 canonical request 与原 InstallationId，经私有 stdin 提供独立 control 凭据，最后再次核对归属后停止；因此它验证的是已提交安装的恢复，不是新安装。fresh install 由第 5 轮夹具验证。上面的命令块仅补充可复现入口，文档补充时未重跑成功测试。

## 剩余范围

Full17PGT、其他上下文、普通用户/部门 identity snapshot、ordinary outbox、ERP handler/dispatcher、默认 Owner 全生命周期、跨仓宿主采用/真实业务验收、现有数据迁移、DEV/UAT/PROD 与生产候选均为 **NOT_RUN**。新 profile 不是完整 Main 数据库；不能据此启用整个 Main 的其他路由或后台服务。目录变更与配置更新需要上述控制窗口，未提供跨实例在线授权撤销协议。

本任务未触发/重跑 Actions、未取消他人运行、未改 workflow/分支保护/依赖、未发布制品或部署。COORD 负责最终审查、台账/四状态同步、正常 PR/main 集成和远端核对。
