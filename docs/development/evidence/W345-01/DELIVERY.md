# W345-01 · WP02-28 两份本地 publish 与运行身份

2026-11-09，DEV-2 从 Main `b10b819dcab7ed11ea195145ac14befba8404528` 领取。合并执行 A9a/b/c，交付两份固定源码的真实发布物和各自的本地运行身份。选定的当前产品输入为 `634ca5cc2ffda8eadbfd109c922ce59dd2d3eb15`，和领取基线的产品构建输入相同。 [WP02-28 原卡](../../task-plans/WP02.md#wp02-28) · [72 小时计划](../../planning/20261109-codex-72h-execution.md)

## 固定制品

| 项目 | 旧版 | 当前版 |
| --- | --- | --- |
| 产品源码 SHA | `585ab63045930fcae131305c927aadb458fa544e` | `634ca5cc2ffda8eadbfd109c922ce59dd2d3eb15` |
| 导出源文件数 | 2,116 | 3,288 |
| 发布文件 / 必需闭包 | 260 / 180 | 308 / 196 |
| 入口 DLL SHA-256 | `037fa117bee20671c23421e90df28f0958c6d22c993e476029c58507026c1f12` | `85b7a1f9c500585157b22350276f728ddc54e746a326dd10678097ca78c23318` |
| 独立收据 SHA-256 | `2d46471a3620ad50aec4c467503a38fc84724f759f209658e3d87cb0fdb460bc` | `036e23de34c230c484737ecf12e97b81437118d2587b8ea05e42500f714f7bf7` |
| 实际 deps 驱动 | Npgsql 8.0.8、EF PostgreSQL provider 8.0.11 | Npgsql 8.0.8、EF PostgreSQL provider 8.0.11 |
| 构建阶段实测用时 | 102.47 秒 | 109.01 秒 |

Windows 使用本机 .NET SDK 8.0.424、ASP.NET Core/.NET runtime 8.0.30。复用 WP02-28 的 Git archive/blob 校验、八项目构建闭包和发布工具，从两份固定提交导出新快照，串行 locked restore 和 Release `dotnet publish`，四个命令均退出 0。低内存时将 MSBuild 限为一个节点，关闭节点复用及共享编译。实测运行的是发布入口 DLL。当前 deps 新增 NSec.Cryptography 25.4.0 和 libsodium 1.0.20.1，旧私有 NuGet 缓存缺这两包，因此两版统一使用本机全局缓存。四份项目锁的 178/180 个 ID 与版本均有匹配的 NuGet content hash 和归档。 [发布工具](../../../../eng/wp02-28/published_artifact.py)

两个源码 SHA 的正式记录 blob 相同，SHA-256 为 `fae24b7cc239d79f12f7e2b0dc627789423f3aa6275ad38fb54a5f9ebc05bc36`。五包 0.10.3 来自 Platform 源 `1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`。以正式记录校验真实 nupkg，再从包字节提取期望程序集哈希，独立比对发布目录和运行观察。历史 `4704592f`、`68990978` 的收据与目录也按原 pin 复核，分别保留原诊断和仅发布范围。 [正式包记录](../authorized-adoption-release-20261015/publication/formal-package-publication.v1.json) · [历史发布证据](../WP02-28/published-host/DELIVERY.md)

## 实际 PostgreSQL 和 HTTP

- **旧版。** 使用新自有 PostgreSQL 18.6，端口 55438，最小管理员表、只读运行账号和本次临时 JWT。实际进程 PID 6288 在 loopback 53408 上返回 `/health/live`、`/health/ready`、`/health/release`、`/health/platform-identity` 全部 HTTP 200 和 `no-store`，5/5 Platform 包 Matched。无迁移的最小夹具如实返回 `latestMigration=null`。宿主启动前实测移除 runtimeconfig 和篡改 Platform DLL 均被闭包预检拒绝，恢复原字节后用原 pin 复核。
- **当前版，旧认证夹具。** 在同一最小 PostgreSQL 夹具上串行启动真实当前发布进程 PID 54932，loopback 63104。存活、就绪和 release 都返回 HTTP 200，旧 JWT 在当前授权入口被拒为 HTTP 401。真实响应和 `completed=false` 保留。产品就绪响应沿用检查项名称 `sqlserver`，两次实际配置和连接均为 PostgreSQL，缓存使用宿主已有的进程内配置。
- **当前版，真实初始化失败。** 第二个新自有 PostgreSQL 18.6 上实际执行当前发布物 `Startup:Mode=DatabaseInit`。Core 224 条迁移已应用至 `20261109040000_StockCreationMappingDispatchV2`。Space、IdentityPriority、ERP 历史各 1 条，分别为 `20261002161945_PostgreSqlSpaceBaselineV1`、`20261002151342_PostgreSqlIdentityPriorityBaselineV1`、`20261002151345_PostgreSqlErpIntegrationBaselineV1`。其后的 `PurApprovalFlowSeed` 抛出 `PUR approval seed requires an enabled approver`，进程退出 3762504530。保留失败现场，恢复同一自有库时实查 31 条菜单、0 用户、0 admin。PG 迁移已插入菜单，初始化程序将 admin 创建置于 `!db.Sys_Menus.Any()` 下，所以 PUR 种子找不到 admin。完整初始化结果为失败，需独立修复。 [初始 admin 守卫](../../../../CP6.WebApi/Program.cs#L1562) · [PUR 种子](../../../../CP6.WebApi/Seed/PurApprovalFlowSeed.cs#L17) · [插入菜单的迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261026153000_UomProfileOwnerV1.cs#L145)
- **当前版，受保护身份成功。** 核实上次留下的四套迁移和自有库物理身份，沿现有 `SkipDatabaseInitialization` 模式启动。显式生成一个合成租户、平台管理员和临时密码，使用实际 `POST /api/auth/login` 获得 HTTP 200 与一个持久会话凭据。随后关闭登录进程，以独立普通 PostgreSQL LOGIN 启动同一发布物。数据库返回 `transaction_read_only=on`。用刚签发的会话调用受保护身份端点，5/5 Platform 包 Matched，PID 33240/loopback 49323。四个健康端点均 HTTP 200 和 `no-store`，release 返回实际 Core 最新迁移。对产品表仅授予诊断角色所需的认证及迁移历史读取权限。 [现有实际登录测试模式](../../../../CP6.Tests/Platform/AuthenticationStatePostgreSqlTests.cs#L232)

本机原件位于 `D:/CP6-local-test/WP02-28/146aa60c2fe84c6aaad680d2faaa4ec5/`，包括 `prior/`、`current/`、对应收据、源码快照、构建日志、`w345-stage1.py`、`w345-matrix.json`、两次 HTTP 结果与停机记录。当前认证库原件位于 `D:/CP6-local-test/WP02-28/b3bfb75c8b7a426592ef39ec693ef0b4/`，包括 `initialize/`、失败及恢复结果、原停止及最终停止记录。认证运行脚本和 owner 指针位于 `D:/CP6-local-test/CP6-72H-20261109/W345-01/`。四个实际 HTTP 进程均已退出，监听端口关闭。两个自有 PG 均核 owner、物理库与零会话后 smart-stop，最终无 postmaster.pid，保留原始数据和证据。模拟范围仅包含本地诊断和登录所需数据。

## 后续兼容与发布边界

旧版 Core 源仅有 `20261002161715_PostgreSqlCoreBaselineV1`，当前版有 224 条实际 Core 历史。两个版本的诊断仍返回回退未获准，原因是 `COMPATIBILITY_EVIDENCE_MISSING`，业务采用和 Current trust 保持 Unknown。B4 使用另一个新隔离库，复用两份固定制品，先验证准确 `service-token-v1` 安装收据、schema/driver 和当前权限。两版具有共同签发/自撤销路径。用旧版签发、当前版撤销验证历史读写，再由当前版签发、停止当前版、用旧版读取并撤销原 JTI。以真实记录、快照、generation 和 priority Outbox 核对成功与重复请求零重复写。撤销接口会有意对不存在或错归属的 JTI 返回 200，需一并实查对应记录未变。服务令牌业务共存、正向回退和不兼容组合留给 B4。 [共同安装和签发合同](../WP02-02/service-issuance/DELIVERY.md) · [既有业务验证](../../../../CP6.Tests/Persistence/Tenancy/ServiceRevocationPostgresTests.cs#L312)

本窗仅提交交付报告。构建、PG 和 HTTP 均在本地完成，无任务 Actions 运行或取消项，也无触发配置改动。正式候选、签名、漏洞门、镜像 digest 和环境推广继续由 R2/GHCR 门禁记录。交付时 `codex/w345-01` 为本地候选，DEV-2 保留给 COORD 集成和工作区收尾。 [R2 发布边界](../../../client/r2/README.md)
