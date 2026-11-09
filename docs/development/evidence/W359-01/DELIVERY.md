# W359-01 · 原 RMA 申请分页列表

官方 DEV-1 基线 421fb54497913f0998506455715d90e05bec7605，2026-11-09 08:50:03 UTC 开工。原 RMA HTTP 列表在 W356 托管读取 profile 上通过真实 Bearer 和 PostgreSQL 验收。合成租户、业务行与授权均在专属本地全 Core 夹具内。

## 实现

托管 GET /api/wms/rma/native-applications-v1 复用原 Owner 的当前认证、action、共享出库可见性与最终授权，以及 W356 普通租户只读 lease 和两表 EF 模型。候选先在租户库下推 tenant、自有范围及 PostgreSQL UUID 顺序，每批最多 128 个 head。既有 authority 的 Outbounds 只查这批候选的出库 ID。只在候选可见时扣减分页偏移，收齐 50 条或耗尽才结束。跳过的记录不读 payload。

列表在当前页按精确 id/version 分组，由一条 EF 查询返回所选版本，最多 50 行。原 PostgreSQL ID 顺序、版本 DTO、字段投影与 page 参数错误保留。未启用模式仍走原共享列表。托管模式的 create、submit 和原 journal lookup/close 继续明确拒绝。原配置、模型、CLI、控制和迁移都无需调整。[列表 Owner](../../../../CP6.Core/Services/Wms/RmaApplicationOwnerV1.cs) · [复用的租户读取](../../../../CP6.Core/Persistence/Tenancy/TenantRmaReadStore.cs)

## 实测

正式证据 D:/CP6-local-test/CP6-72H-20261109/W359-01/rma-http-20261109T085838-281e9309/fixture。PostgreSQL 18.6，SDK 8.0.424，.NET 8.0.30。复用 W351 c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 的实际发布 DatabaseInit，PID 59544 exit 0，实际 Core/Space/Identity/ERP 历史 224/1/1/1 对应源码。复用 W353 CLI 实际 prepare/commission。VSTest exit 0，2 通过、0 失败、0 跳过。[原 Bearer 夹具](../../../../CP6.Tests/Persistence/Tenancy/RmaTenantReadHttpPostgresTests.cs)

- A/B 普通租户共享 200 个确定 ID，各附一条原单条 GET 的高位 ID。A 的出库不可见和异申请人图样分别每 3、7 条，B 分别每 4、11 条。首批 128 个满足自有范围的候选只有 86/96 个可见。真实登录签发的 Bearer 在同一宿主验证 A/B 的第 1 和第 2 页各 50 条，逐项匹配 ID、版本、出库和投影。A 第 3 页返回余下 16 条。A/B 的第 2 页分别达到固定 ID 序号 173/146，跨越首批边界。
- 普通 A/B 日志实际出现 h.tenant_id、h.actor_id、h.id > 绑定 UUID、ORDER BY h.id 和 LIMIT。版本查询以 tenant、version 和该组 id 数组组成 UNION ALL，再由 PostgreSQL 按 id 返回。A/B 分别观察到 4/2 次实际 UUID 续扫及 4/3 条混合版本查询。普通连接未查另一租户、共享 RMA 或 public 表。原证据在 04-ordinary-rma-sql.log 与 postgres.log。
- 使用同一已签发 token，A 在字段权限更新后第 2 页实际遮蔽原因、备注和申请人。撤 WMS 范围后返回原语义的空页，撤 query action 后为 ACTION_FORBIDDEN 403，B 的第 2 页仍逐项一致。无效 page 返回 RMA_PAGE_INVALID。实际旧默认宿主返回原共享列表。W356 的单条/历史、原权限与错绑定、错物理、未 Ready、原 journal/写拒绝继续通过。

原 RED 在 rma-http-20261109T085420-2ee4a9a6/fixture。相同实际初始化和授权下，单条和历史先通过，原宿主第 1 页返回 503 RMA_TENANT_ENTRY_UNAVAILABLE，模型通过，HTTP 用例按预期失败。正式原件为 rma-read.trx、03-vstest.stdout、04-ordinary-rma-sql.log、source.json。

| 专属 loopback HTTP | PID | 端口 |
| --- | --- | --- |
| 正式 A/B | 32104 | 61096 |
| 交换绑定 | 18000 | 57745 |
| 错物理副本 | 55228 | 60900 |
| 原共享默认 | 57596 | 63469 |
| RED | 45632 | 51067 |

以上进程及专属 PostgreSQL postmaster 29072/20640 均已停止，55566 与动态端口无监听。

## 复现与交付

全局 NuGet 缓存和 W346 本地源完成 locked restore。私有 ArtifactsPath 只构建受影响的 CP6.Tests Release 与依赖。最终 build-green.log 为 0 错误、150 条既有其他源文件告警。候选 WebApi DLL SHA-256 为 81191bcd1283b97657b8426ab657f75a4ba96605b1e3f830a227eb89461401bd，Tests DLL 为 2f1f9970c2e66a6515557676128631e3ea43988c4c21eb0eedb353e8f2527b4d。复用 W351 DatabaseInit DLL 为 3123dd9fa784d6c31bc674cc5bb015fd44e2431a8d825fcceb86c2e164af2be7，W353 CLI DLL 为 53807c439aba8c48960a1ce1bc926d2ff02de302b66469884c9032dbedd3791c。[复现入口](../../../../eng/wp02-02/rma-tenant-migration/verify_read.py)

    & 'C:/Program Files/python/python.exe' 'eng/wp02-02/rma-tenant-migration/verify_read.py' --root 'D:/CP6-local-test/CP6-72H-20261109/W359-01' --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli 'D:/CP6-local-test/CP6-72H-20261109/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll' --webapi 'D:/CP6-local-test/CP6-72H-20261109/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll' --webapi-source 'c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40' --test-dll 'D:/CP6-local-test/CP6-72H-20261109/W359-01/build/bin/CP6.Tests/release/CP6.Tests.dll'

分支为 codex/w359-01，DEV-1。本窗复用原权限角色，租户写入、原结果授权及生产角色属于后续范围。COORD 负责最终合并与官方 finish。
