# WP02-28 Main 实际运行身份与回退判断

本次交付范围为实际 Main 身份观测、诊断授权和缺少兼容证据的回退拒绝。没有两个实际制品及 PostgreSQL schema/驱动组合的兼容证明，正向回退保持 NOT_RUN；不将本局部交付记为完整 WP02-28 DONE。

Main 基线 `37d84b4b6eaeefe7e8b35c21df26a8b1f8c119d5`；分支 `codex/wp02-28-runtime-identity-20261015`。Handoff 第九轮分配 `b2c72583d8bb1147e2e853abf0e7e63a111c9830`，START `d84f7096fc6e0879de338bddf080719fc71a06ee`。候选完整 SHA 在 Handoff DELIVERY 固定引用；主线合并、台账与四份状态由 COORD 串行处理。

## 实现和使用边界

新增受保护的只读 `GET /health/platform-identity`；注册与路由仅在 Program 增加三行。原 `/health/release` 的五个字段、匿名访问及 no-store 语义保持不变。诊断使用现有 JWT 认证和 `RequirePlatformAdminAttribute` 的 impersonation 拒绝、claim 快判、数据库中启用/平台管理员状态实时回查；不另设仅凭 header 或角色 claim 的捷径。使用 minimal endpoint 避免 MVC 全局操作日志过滤器对诊断请求写入；成功、认证拒绝和授权拒绝均 no-store。授权数据库不可用返回固定 503，不能回显 provider exception。

`PlatformHostIdentity:ExpectedModules` 是受控启动配置，按五个准确包 ID 输入 `PackageVersion`、`SourceSha` 和 `AssemblySha256`；登记时快照，未提供完整期望保持 Unknown。观测来自当前 AppDomain 已加载程序集，不从传入路径加载或复制到另一个 ALC。固定诊断类别区分缺件/未知与已观察冲突；文件路径、连接串、凭据和未验证的任意元数据不进入响应。

输出分开保留 CLR `AssemblyVersion`、`InformationalVersion` 和实际入口 `.deps.json` 的 `DeclaredPackageVersion`。后者仅是包/runtime asset 关联声明，缺失、歧义或程序集不在应用输出目录时保持 Unknown；独立期望文件哈希才用于固定所观测的 backing DLL 字节。当前加载 MVID 与同一字节快照的 PE MVID 比对，不将文件哈希称为运行内存哈希或签名信任证明。

Match 表示本次受限观测与明确期望一致，不证明 Current signing trust、正式 OBS 资格、业务应用或发布许可。当前正式 0.10.3 资格仅为已登记的 ctx-binding-v1/ctx-v1 Http/Message/Background；类型存在不增加正式能力。Rollback 始终拒绝当前缺证范围；没有启用 bool、请求 DTO 或“同版本即兼容”的放行入口。

## 验证与复用

2026-10-16 本地相关验证已通过。Release 构建随定向测试执行，命令包含 `--no-restore -c Release -warnaserror:MSB3277 --filter 'FullyQualifiedName~PlatformHostIdentity|FullyQualifiedName~PlatformHostRollback'`；最终结果 36 passed、0 failed、0 skipped，原始记录为下述私有根目录中的 `unit-metadata-correction/results.trx` 与 `test.log`。覆盖缺失/错配期望、重复及非默认 ALC、MVID/文件读取失败、元数据与声明区别、依赖关联歧义和回退拒绝。

真正的 CP6.WebApi 进程结果见 [实际宿主原始结果](actual-host-results.json)，最终运行目录 `host-20261016T115757836255Z`。三个场景分别运行 PID 4080 / 20824 / 26528，HTTP 53268 / 53300 / 50428；逐一核 OS 进程、启动时间、命令及实际 listener，结束后全部关闭。结果适用本分支当时工作字节，构建源码基线为上述 main；实际宿主 DLL SHA-256 为 `453d985981d64269e610e844cc6426276bd4ee518444f74bfd281e4ffab71ee2`。候选提交只固定这些已验证输入，不将提交后未重跑写为新执行。

| 实际验证 | 结果与适用范围 |
|---|---|
| 正确期望 | AspNetCore、EntityFramework、Messaging 已加载且 Matched；Abstractions、Contracts 未加载，保留 `ASSEMBLY_NOT_LOADED` / Unknown，整体 Unknown。没有为了绿色结果强制加载。 |
| 错配/未知 | 已加载 AspNetCore 的错误期望哈希得到 Mismatch；未提供期望清单得到 Unknown。 |
| 身份与授权 | 匿名/错误签名 401；租户身份/伪造管理员 header/冒充会话 403；同一 JWT 在数据库撤权或禁用后 403；撤销授权表读取权限得到固定 503。所有上述响应 no-store。 |
| 输出及旧健康合同 | 诊断不含私有路径、连接串或凭据；旧 `/health/release` 仍匿名 200、原五字段及 no-store，fixture 未造迁移历史，latestMigration 为 null。 |
| 回退与资格 | 所有场景 `allowed=false`；错配报 `IDENTITY_MISMATCH`，其余报 `COMPATIBILITY_EVIDENCE_MISSING`。业务验收、runtime qualification、trust 均 Unknown。 |

任务级 SPEC/QUALITY 合并审查发现尾随 `/` 等价 URL 未加 no-store，已修正路径规范化，并在实际宿主补齐尾斜杠授权成功及五类拒绝回归；定向复核通过，未扩大为第二轮全量审查。修正后仅重新构建受影响 WebApi（`--no-restore -c Release -warnaserror:MSB3277`，退出 0，`endpoint-correction-build.log`）并执行真实宿主脚本；观测器/依赖解析/回退逻辑及单测输入未变，复用上述 36 项单测。修正前成功宿主 `host-20261016T115251523170Z` 的结果仍保留为历史，不替代最终运行。

首次真实宿主 `host-20261016T114926559878Z` 在匹配检查失败，原始结果/日志保留。原因是最初将信息版本当成包版本：正式 0.10.3 archive 的实际 DLL 为 AssemblyVersion `0.8.0.0`、InformationalVersion `0.8.0-alpha.2+1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`，但其哈希及源码 SHA 与正式发布记录一致。本任务修正观测语义并新增对应定向验证，没有修改正式包或伪造 DLL 元数据。更早的 `unit-green` 16 项结果仅为修正前证据，最终以 `unit-metadata-correction` 为准。

命令从本任务 worktree 运行，SDK 为 `C:/Users/tt/.dotnet/dotnet.exe`（8.0.424）。本次 `restore CP6.Tests/CP6.Tests.csproj --locked-mode` 成功，原 NuGet 签名信任、source mapping、锁文件及依赖版本未改。原有 `System.Security.Cryptography.Xml 8.0.4` NU1903 与 `MessagePack 2.5.302` NU1902 保留，不代表发布漏洞门禁通过。没有运行全仓/架构测试。

复用 [Main 正式包采用](../WP02-02/main-package-adoption/DELIVERY.md) 与[一次正式发布](../authorized-adoption-release-20261015/README.md)未变的来源、版本和信任记录。运行器独立从正式 publication 所固定的实际 `.nupkg` 字节提取 DLL 期望并核对本次输出，未用观测结果反填期望。此操作不声称重新发布或重新完成签名验收。

本地原始日志及数据保留于 `D:/CP6-local-test/WP02-28/75fe75731e3e4568af4cf2781fc07621/`。首次 RED 因缺少观测 API 编译失败，原输出为 `unit-red/test.log`；没有把该失败写为测试通过。跨会话恢复时确认原 cluster 已停止，核 control system identifier、owner/path 和空闲端口后仅恢复本任务 PG55438。

完成验证后已核自有 PG system identifier `7696923210433097924`、数据库 OID `16385`、owner 和零客户端，再执行 smart-stop。PG55438 与上述 HTTP listener 均已关闭；数据及失败日志保留，未删除文件。运行身份仅是本地任务 fixture，不作为生产候选或业务验收。

复现入口：

```powershell
# 新运行必须新 owner；不要覆盖本报告已有私有 settings 或数据。
$owner = [guid]::NewGuid().ToString('N')
$py = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe'
& $py -X utf8 eng/wp02-28/pg_resource.py create $owner
$settings = "D:/CP6-local-test/WP02-28/$owner/settings.json"
& C:/Users/tt/.dotnet/dotnet.exe restore CP6.Tests/CP6.Tests.csproj --locked-mode
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore -c Release -warnaserror:MSB3277 --filter 'FullyQualifiedName~PlatformHostIdentity|FullyQualifiedName~PlatformHostRollback'
& $py -X utf8 eng/wp02-28/verify_host.py --owner $owner --settings $settings
& $py -X utf8 eng/wp02-28/pg_resource.py stop $settings
```

逐条检查退出码。运行器启动真正的 CP6.WebApi 输出，HTTP 为随机 loopback 端口，使用既有 SkipDatabaseInitialization / SkipHostedServices 开关。数据库只有本任务自有管理员授权 fixture 表和资源 marker；runtime 仅 SELECT 且默认事务只读。此 schema 不冒充完整迁移后的 Main，也不证明 schema 回退兼容。测试 JWT 由临时私有密钥签发，仅用于本 fixture 的真实 JWT 管道，不声明真实生产管理员身份。失败时保留输出并停止核实的自有宿主，PG 由显式 stop 核身份、零客户端后 smart-stop。

## 未验证范围与交付

真实生产镜像/制品运行身份、两个实际版本在准确 PostgreSQL schema/driver 组合上的正向兼容、C02/ERP/其他业务 Owner 采用、原业务验收、Current trust、生产候选/环境审批与部署仍未验收。没有修改迁移、EF/租户存储、认证配置、项目依赖、Platform 合同、工作流或部署文件。

推送前 fresh fetch 确认 main/origin/main 仍为上述基线，候选与远端 main 的工作流及依赖内容未变：普通工作流仅手动入口，唯一 push 入口 r2-candidate 限 `v*.*.*` tag，本次只有任务分支 push，不命中。任务分支运行列表为空，无需取消；未触发 Actions、未改触发配置。Handoff 仓私有且 Actions disabled，当前默认分支与任务分支均无 workflow。候选经普通 push 及远端 SHA 核对后由固定 Handoff DELIVERY 交给 COORD；执行者不越权合并或更新共享状态。
