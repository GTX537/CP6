# W356-01 · 原 RMA HTTP 当前与历史读取

官方 DEV-1 基线 8c72ed72784d973d3fb66818c8e01a245e77d1cf，2026-11-09 08:24:33 UTC 开工。两项定向验收通过，包含真实登录签发 Bearer 的原 RMA GET 和实际读取所用的 EF 模型缓存。全 Core 库、租户、授权和业务数据均为本任务的隔离合成夹具。

## 实现范围

- 启用 RmaReadTenancy 后，原 GET /api/wms/rma/native-applications-v1/{id} 及可选 version 使用租户 heads/versions。原 Owner 执行当前会话、action、出库可见性、字段投影和最终授权，原 MVC 路由、DTO、强 ETag、no-store 与状态码保留。托管模式下 list、create、submit、原 journal lookup/close 明确返回 RMA_TENANT_ENTRY_UNAVAILABLE。共享 source-only 入口继续执行原权限。未启用的宿主保留原共享模式，正式夹具用独立未 commissioning 的全 Core 副本验证默认 GET。[原 Owner](../../../../CP6.Core/Services/Wms/RmaApplicationOwnerV1.cs) · [正常宿主配置](../../../../CP6.WebApi/Configuration/RmaReadTenancyConfiguration.cs)
- 绑定限定为 W353 的 rma-application-core-tenant-v1 和实际 Ready 迁移。原 Owner 的 authority 事务在真实当前认证/共享出库图权限下读取并锁定受保护安装及 Ready 行，核物理库、安装键及绑定 LOGIN。正式本地夹具的默认 authority 使用实际 Core 数据库拥有者 w350_owner，具备上述控制读取及原认证/业务图权限。普通租户登录沿已采用的可信上下文、lease 和命令守卫，复核同主库和实际 session tenant，只在 READ ONLY 事务读两张租户表。普通角色未获控制表、共享出库或 journal 权限。生产 authority 角色与正式权限授予仍需单独验收。[连接与原生绑定](../../../../CP6.Core/Persistence/Tenancy/TenantRmaReadStore.cs)
- 最小 Npgsql EF Context 只映射 heads 和 versions、原复合键及实际 schema。缓存键包含 Context 类型、provider、可信 layout/model/schema 与 designTime，同一 tenant 复用 IModel，不同 tenant/schema 和版本区分。真实路由即使用此 Context，原共享迁移 blob 037ea53344edf378d630c032198908f6b9e61f5b 保留。[两表 EF 模型](../../../../CP6.Core/Persistence/Tenancy/RmaTenantReadContext.cs)

## 实测

正式证据根为 D:/CP6-local-test/CP6-72H-20261109/W356-01/rma-http-20261109T083908-a8d6b094/fixture。PostgreSQL 18.6、SDK 8.0.424 与 .NET 8.0.30，VSTest exit 0，2 通过、0 失败、0 跳过。复用 W351 c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 的发布 DatabaseInit，实际 PID 2108 exit 0，物理 Core/Space/Identity/ERP 历史 224/1/1/1 对应源。复用 W353 已验证的 CLI 真实 prepare/commission，随后构造不同物理 OID 的已 commissioning 模板副本与独立未启用副本。

| 场景 | 实际结果 |
| --- | --- |
| Bearer 与原 DTO | 原 POST /api/auth/login 实际签发 cp6_at，从真实 cookie 取 token，用显式 Authorization Bearer 调原 GET。同一 Kestrel 内 A/B 交替用相同 ApplicationId，各自读出当前 v2 和历史 v1、不同出库与备注、原 ETag 和精度。非法版本与缺失 ID 分别返回原 404 |
| 当前授权 | 已签发 token 上调整 A 的字段权限后真实遮蔽原因、备注和申请人。移除仓库范围及 query action 后分别得到 OBJECT_FORBIDDEN 和 ACTION_FORBIDDEN，B 仍正常读取。共享 source-only GET 实际 200 |
| 原生绑定与未接通路由 | 互换两个合法运行登录、连接已复制原安装记录的另一物理库、把真实 native Ready 置回 Provisioning，实际均返回 RMA_TENANT_BINDING_UNAVAILABLE。启用宿主的 list、create、submit、原 journal GET 和 close 均为 RMA_TENANT_ENTRY_UNAVAILABLE。未启用的独立宿主实际从原共享 RMA 返回原 DTO 和 ETag |
| EF 与数据库见证 | 真实 Context 测试复用 A 的 IModel，区分 B、layout、model、designTime，两表映射和复合键与实际一致。PostgreSQL 原日志见 A/B 真实普通登录的 READ ONLY 和各自限定 schema SELECT。普通连接未查询另一租户、原共享 RMA 或 public 表 |

原件包括 rma-read.trx、03-vstest.stdout、04-ordinary-rma-sql.log、postgres.log、source.json。真实 RED 在 rma-http-20261109T083219-2d15c9a3/fixture，原宿主在同一真实登录及已 commissioning 条件下返回 404 RMA_APPLICATION_NOT_FOUND。

| 正式动态 loopback HTTP | PID | 端口 |
| --- | --- | --- |
| A/B 正常托管 | 53184 | 63266 |
| 交换绑定 | 46532 | 63060 |
| 错物理副本 | 51912 | 57206 |
| 未启用原共享 | 30228 | 53433 |

RED 的 HTTP PID 55844、端口 63655。所有上述 HTTP 子进程、两个专属 postmaster 60428/38280 均停止，55566 与实际动态 HTTP 端口无监听。夹具使用隔离 loopback HTTP 和 SCRAM PostgreSQL，VerifyFull 仍是新连接配置的默认 TLS 模式。

### 复现

锁定还原使用 W346 的本地源配置和全局 NuGet 缓存。私有 ArtifactsPath 只构建 CP6.Tests Release 一次供最终验收，直接使用其产出的实际 WebApi 子进程，无需另做 publish。最终 build-green.log 为 0 错误、150 条既有其他源文件告警。候选 WebApi DLL SHA-256 e6a1e89ab8f7ec26bf792d8e0a8912db213da1a51ee440ce3c090f369c0b4fcc，Tests DLL SHA-256 e5f23a2cdfaf2d1d48b27132116f96ba745240fdefb7f1dc68f93934d1e5873d。复用 W351 DatabaseInit DLL SHA-256 3123dd9fa784d6c31bc674cc5bb015fd44e2431a8d825fcceb86c2e164af2be7，W353 CLI DLL SHA-256 53807c439aba8c48960a1ce1bc926d2ff02de302b66469884c9032dbedd3791c。[验收入口](../../../../eng/wp02-02/rma-tenant-migration/verify_read.py)

    & 'C:/Program Files/python/python.exe' 'eng/wp02-02/rma-tenant-migration/verify_read.py' --root 'D:/CP6-local-test/CP6-72H-20261109/W356-01' --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli 'D:/CP6-local-test/CP6-72H-20261109/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll' --webapi 'D:/CP6-local-test/CP6-72H-20261109/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll' --webapi-source 'c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40' --test-dll 'D:/CP6-local-test/CP6-72H-20261109/W356-01/build/bin/CP6.Tests/release/CP6.Tests.dll'

托管配置需要 Enabled、原 W353 InstallationId、绑定 Revision 以及 Tenants 中每个 TenantId、Login、Password 和 CredentialVersion，凭据由已有私有配置来源传入。租户业务写入和原结果原子性、共享 source/journal 普通身份权限、全面池容量及生产环境接入另窗验收。

分支为 codex/w356-01，DEV-1，当前 origin/main ca97fd003 的 W355 变更与本任务无文件重叠。COORD 负责最终合并与官方 finish。
