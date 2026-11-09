# W351-01 · WP02-28 全 Core 前向迁移与应用回退

2026-11-09 07:29:19 UTC，FORWARD-COMPAT 在固定 DEV-2 从 Main `bc10c9f6f85eb30490886f43ab68b0a711906063` 领取。两份真实 publish 在同一隔离 PostgreSQL 18.6 上完成旧版初始化、携带原服务令牌的前向迁移，以及停当前版后回退旧宿主。 [WP02-28 原卡](../../task-plans/WP02.md#wp02-28)

## 实际执行

- **制品。** 复用 W345 旧版原发布物和独立收据。对已采用 #801 的 `c5642cd4…` 从 Git 对象导出 3,289 个锁定构建输入，执行一次 locked restore 和 Release publish，两个命令均退出 0。使用 MSBuild 单节点，关闭节点复用和共享编译。当前产出 308 个发布文件，196 个必需闭包文件。发布工具按两份独立 pin 校验目录、入口、依赖和 Platform 0.10.3 正式包归档字节。SDK 为 8.0.424，ASP.NET Core 与 .NET 运行时为 8.0.30。 [W345 原件](../W345-01/DELIVERY.md) · [#801 修复与回归](../BUG-801-PG-FIRST-ADMIN/DELIVERY.md) · [发布工具](../../../../eng/wp02-28/published_artifact.py)
- **同一物理库。** 专属 PostgreSQL 集群端口 55438，system identifier `7706070792611900284`，业务库 `w351_core_6a5c78c9`，OID `16403`。管理标记在独立数据库。两次真正的 `Startup:Mode=DatabaseInit` 分别执行旧、新发布入口 DLL，均记录完成标记并退出 0。旧应用先在真实 Core baseline 上签发一个 JTI 后停止。新初始化在同一库保留四套原历史记录和原 C02 状态，精确追加 223 项 Core 迁移。直接读取本地 `cp6_operations_owner_v1.mapping_validate()` 确认最新迁移的 Stock 分支存在，旧宿主回退后函数定义 SHA-256 与升级后相同。
- **原记录与重放。** 在全 Core legacy C02 布局，以同一合成租户、Basic 服务客户端和签名材料执行真实 Kestrel HTTPS。旧版签发的原 JTI 在升级前后逐字段相同。当前版撤销原 JTI 后签发第二个 JTI，随后关闭当前进程和监听端口。旧版以 `SkipDatabaseInitialization=true` 重启，撤销第二个 JTI，再各重放一次两笔原撤销。三个 `/health/release`、两次签发和四次撤销 HTTP 均为 200、`no-store`。撤销返回空体。回退的旧版报告最新 Core migration，独立 SQL 同样读到新历史。
- **物理结果。** 每次撤销只推进一次对应原 token 的撤销状态和 row version，写入一条 snapshot 及一个优先 `com.gtx537.platform.token.revoked.v1` 事件，generation 从 0 到 1 再到 2。逐条核对原 JTI、issuer、subject、有效期、版本和存储哈希。两次重放后的两条 token、两条 snapshot、两条 Outbox 原字节和 generation 与重放前相同。最终四套 EF history 保持升级后的物理记录。

验证命令退出 0，从输入检查到停机实测 81.7 秒。两次初始化进程和三个宿主均已退出，三个应用监听端口关闭，自有 PG 经所有权与零会话核对后 smart-stop。独立复查没有 `postmaster.pid` 和 55438 监听。现有 WP02-28 工具测试 18 项通过。运行器最后加强了一次性进程在等待被中断时的清理，并单独验证该分支。 [验证器](../../../../eng/wp02-28/verify_forward_schema_rollback.py)

## R2 源审查采用清单

| 固定输入 | 旧版 | 当前版 |
| --- | --- | --- |
| 源码 SHA | `585ab63045930fcae131305c927aadb458fa544e` | `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` |
| 独立收据 SHA-256 | `2d46471a3620ad50aec4c467503a38fc84724f759f209658e3d87cb0fdb460bc` | `9da1b3282a206539a2ea7dfcfcdd87ff2396f27445e30e103fa36915e3b27231` |
| `CP6.WebApi.dll` SHA-256 | `037fa117bee20671c23421e90df28f0958c6d22c993e476029c58507026c1f12` | `3123dd9fa784d6c31bc674cc5bb015fd44e2431a8d825fcceb86c2e164af2be7` |
| 实际 deps 与驱动 | Npgsql 8.0.8、EF PostgreSQL provider 8.0.11 | 同版本且 DLL 字节一致 |

- 两版 `Npgsql.dll` SHA-256 为 `2083de510463c4f708fa433d68bf3da149f10e25cd99a86f4519360e3f59b70d`，`Npgsql.EntityFrameworkCore.PostgreSQL.dll` 为 `e0a329c94ca198007947d38e89b38fa7f332e65b8a63a2035d06143e759b7d3c`。
- 物理 Core lineage 为 1 → 224 → 224，从 `20261002161715_PostgreSqlCoreBaselineV1` 前进到 `20261109040000_StockCreationMappingDispatchV2`。Space、IdentityPriority、ErpIntegration 始终各保留原一条 `20261002161945_PostgreSqlSpaceBaselineV1`、`20261002151342_PostgreSqlIdentityPriorityBaselineV1`、`20261002151345_PostgreSqlErpIntegrationBaselineV1`。回退方向仅为应用 `c5642cd4…` → `585ab630…`，数据库保留前向版本。回退后实查最新迁移安装的函数定义 SHA-256 为 `cabddf262c2f7de77014904f4bcfd9c1e2787cf107014ac37e9fccb29752adaf`。
- 原件为 `D:/CP6-local-test/CP6-72H-20261109/W351-01/6a5c78c9642844379b98beea99dbbb71/w351-results.json`，SHA-256 `f4306d0bd59e917c72062c3c81e54bec7fe23e510631c4b1d766c57b55d2078d`。同目录保留两次初始化日志、三次 HTTPS 宿主日志、当前 publish 与收据、构建日志和停机记录。业务范围为默认合成租户、临时服务客户端、两个 JTI 和临时密钥。数据库连接使用本次隔离库的所有者身份，权限隔离实证见 W347。源 SHA 和版本响应是启动声明，实际入口身份以固定发布文件和进程命令为据，迁移与业务判断以物理库为据。 [W347 五表布局与权限实证](../W347-01/DELIVERY.md)
- W351 的 `LOCAL_EVIDENCE_ONLY` 记录适用于该对发布、全 Core PG18.6 和已测 C02 路径。W349 的离线判断使用 W347 的 `service-token-v1` 五表布局，绑定其另一份当前制品与相同布局上的回退。正式 R2 候选由受保护 Tag、SQL/E2E、签名、扫描、不可变证据、镜像 digest 和环境门禁判定。 [W349 本地判断](../W349-01/DELIVERY.md) · [正式 R2 门禁](../../../client/r2/README.md)

## 复跑

用上表 pin 复用两份发布物。省略 `--settings` 时，验证器在私有根下创建新自有集群和独立业务库，完毕后保留数据和日志。复跑需要空闲的本地 55438。

~~~powershell
$w351Python = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13.13-windows-x86_64-none/python.exe'
$w351Private = 'D:/CP6-local-test/CP6-72H-20261109/W351-01'
$w351Current = Join-Path $w351Private '6a5c78c9642844379b98beea99dbbb71/current.receipt.json'
& $w351Python -B -X utf8 eng/wp02-28/verify_forward_schema_rollback.py `
  --private-root $w351Private --current-receipt $w351Current `
  --current-receipt-sha256 9da1b3282a206539a2ea7dfcfcdd87ff2396f27445e30e103fa36915e3b27231 `
  --prior-root 'D:/CP6-local-test/WP02-28/146aa60c2fe84c6aaad680d2faaa4ec5' `
  --package-cache 'C:/Users/tt/.nuget/packages'
~~~

全部验证在本地执行，Actions 触发、取消及工作流修改均为零。交付为本地 `codex/w351-01` 候选，DEV-2 工作区和席位交 COORD 集中审查、PR 和官方 finish。
