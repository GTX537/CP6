# WP02-28 第十三轮 C：实际 publish 本地启动与身份验证

本窗口已实现、完成相关本地验证，候选提交和 Handoff DELIVERY 固定引用由通信提交给出；共享状态、一次任务级集中审查及串行合并交 COORD。完整 WP02-28 仍为 IN_PROGRESS/CONFIRMED。此结果只证明实际 publish 制品的本地诊断启动与身份一致，不代表生产发布或业务 readiness。

源码基线及本次实际构建输入为 `4704592f52172c941e1cb95bdeb3c73f0dd179e8`，开工时 HEAD/main/fetched origin/main 一致。执行工作树 `D:/CP6-worktrees/wave13-c-published-host-20261017`，分支 `codex/wp02-28-published-host-20261017`。真实 START 已远端回读：Handoff `bf0df79a7b366b7b1c103a0128c930d9743b64fc`，`handoffs/CP6-DEV-20261012/tasks/WP02-28/messages/20261017T041153Z-wave13-start/MESSAGE.md`。构建后产品源码、项目及锁文件未改；后续候选提交固定验证器和证据，不把候选 SHA 说成重新执行过的宿主构建。

## 改动与拒绝边界

- `eng/wp02-28/published_artifact.py` 在全新自有私有根的显式 publish 路径执行 locked restore 和真实 `dotnet publish`，生成独立收据；入口、完整发布文件 SHA-256、实际 deps runtime/native/resource/runtimeTargets 闭包、runtimeconfig、源码 SHA、构建命令及日志绑定在同一收据。仅支持本任务使用的 portable framework-dependent net8.0 输出，其他形态不猜测放行。
- 五包期望来自该源码 SHA 所登记的正式 publication 和真实 0.10.3 nupkg；先核归档哈希，再从同一字节快照提取 DLL。实际输出不能自证期望。收据仅在成功 publish 后产生，启动方必须提供单独保存的收据 SHA-256；没有扫描任意目录后重新生成放行清单的命令。启动前和端点检查后均复核准确目录、全部文件集合/哈希及独立正式包期望。
- `eng/wp02-28/verify_published_host.py` 从收据指定的发布 DLL 启动真实 CP6.WebApi，使用独立 content root、临时 JWT 和只读 runtime 连接；过滤继承配置及 .NET loader/startup hook 变量，HTTP 不使用代理或跟随重定向。沿用现有 PostgreSql、SkipDatabaseInitialization、SkipHostedServices 开关及真实 JWT/平台管理员授权路径，未改 Program、业务宿主 profile 或认证服务。
- 核对 OS PID、启动时间、准确 executable/command、唯一 loopback listener，再核真实 `/health/platform-identity` 的 PID/启动时间、五包声明/源码/文件哈希、MVID 和加载上下文，以及 `/health/release` 的原五字段与 no-store。缺件/错配拒绝启动或诊断通过；只有 `ASSEMBLY_NOT_LOADED` 可诚实保留 Unknown，并明确 `identityReadiness.ready=false`，不强制加载。
- 运行结果分开记录诊断启动和身份就绪；关闭经身份核实的自有宿主并确认 listener 消失后才设置 completed。运行配置、JWT、连接串和数据库不入仓。收据是本地观测绑定，不能代替签名制品或证明 Current trust；同账户恶意修改运行文件的防护不属于本工具的信任边界。

## 实际验证与证据

以下执行时间为 2026-10-17 UTC（客户端纽约时间为 2026-10-17 00:xx）。所有产品验证均在本地完成。

| 验证 | 实际结果和证据 |
|---|---|
| 真实 publish | SDK `C:/Users/tt/.dotnet/dotnet.exe`，locked restore 与 Release publish 均退出 0；`-p:UseAppHost=false -p:SourceRevisionId=4704592f52172c941e1cb95bdeb3c73f0dd179e8 -warnaserror:MSB3277`。260 个发布文件，入口 DLL SHA-256 `fe6c41088505a9023fbc0104343392c40f7e0a3dc726bb5d552373984e60cdb1`。完整文件清单、独立包来源、原始命令和日志哈希见 [publish.receipt.json](publish.receipt.json)。收据 SHA-256 `ddcf438624231ee31a2aceb1006e272dc62d0d975a8bd1e7a6d2bcb98786fb87`。未复制 bin 假装 publish。 |
| 实际端点及最终进程 | [host-results.json](host-results.json)、[host-stdout.log](host-stdout.log)：最终 PID 35820、HTTP 54549，五包全部实际加载且 Matched；声明版本 0.10.3、源码 `1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`、文件哈希/MVID 一致。没有向宿主额外加载程序集。与上轮三份已加载、两份 Unknown 的观测不同，本轮如实记录当前主线结果。 |
| 真实发布负向 | 首次运行 `published-host-20261017T041500086418Z` 在任何宿主启动前，分别移除 runtimeconfig、改写 AspNetCore DLL，均因闭包不一致拒绝；finally 恢复原字节并复核同一收据。两项实际结果摘录为 [negative-results.json](negative-results.json)，完整首次记录仍在私有根；该运行随后 PID 27980/HTTP 63704 的正向诊断也通过并停止。 |
| 工具行为回归 | [tool-tests.log](tool-tests.log)：6 passed，0 failed。覆盖真实拒绝路径所需的闭包缺件/额外文件/字节修改、receipt/source/path 绑定、重签收据仍不能覆盖独立包期望、预检拒绝不创建进程/写数据库、PID/启动时间及身份错配拒绝、未加载 Unknown。单元夹具仅用于工具回归，不作真实宿主证据。 |
| 本地资源关闭 | [resource-stop.json](resource-stop.json)：新 owner `7da18a5de15c451da8464af7daeeaa13`，PG system identifier `7697484797195025204`，数据库 OID 16385；核实 PG PID 25092、程序路径/data directory、owner marker、零客户端后 smart-stop。PG55438 与两次 HTTP listener 均已关闭；postmaster.pid 已移除。全部数据及日志保留。 |

工作中收尾自查将 nupkg 读取改为单一已校验字节快照，并把 completed 移到资源关闭后。修正后只重跑上述工具定向测试与实际宿主启动，复用同一 publish 字节及首次负向结果，没有重建产品或运行全仓测试。最终私有目录为 `D:/CP6-local-test/WP02-28/7da18a5de15c451da8464af7daeeaa13/published-host-20261017T041654990708Z/`；最终 stdout SHA-256 `636f8a2d15e2caebabf656dae9ef04b967e9e380fc81778fbac326bd8a569731`，stderr 为空。原始 restore/publish 日志在该 owner 根的 `publish.restore.log` / `publish.publish.log`，其哈希与命令已在收据中。

未遇到非预期构建/测试失败；负向拒绝没有伪装成正向。首次暂存字节复核发现 Git 换行归一化使收据 blob 哈希变为 `5e604abd...`，与实际执行的 `ddcf4386...` 不同；没有替换原 pin，已在本证据目录增加局部 `.gitattributes` 保留 JSON/log 原始字节，重新暂存并核对 pin。此为证据保存修正，不重跑产品。保留现有 NU1903（System.Security.Cryptography.Xml 8.0.4）、NU1902（MessagePack 2.5.302）及既有 CS1998 警告；这不是生产漏洞扫描通过。

复用 [上轮报告](../DELIVERY.md) 的 36 项身份/回退单测及真实授权矩阵（401/403/503、同令牌撤权、尾斜杠/no-store）。已核 `c92f12e33937a47093e5d9861c4269bb17b432b6..4704592f52172c941e1cb95bdeb3c73f0dd179e8` 中对应观测器、端点、配置、RequirePlatformAdmin、PlatformHostIdentity/PlatformHostRollback 测试和旧 verify_host 均无差异；本窗口也未修改它们。该复用不是声称在本轮重新执行旧授权矩阵。正式包采用/签名来源仍引用 [既有正式发布](../../authorized-adoption-release-20261015/README.md)。

## 复现

Windows、Python 3.13、现有 .NET8 SDK 和 PostgreSQL18。每次创建全新 owner/root；准确填写已核对的源码 SHA，保持产品源码、项目/锁文件无改动，不在源项目放私有 appsettings.Local。收据和私有输出不覆盖旧运行。

```powershell
$py = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe'
$owner = [guid]::NewGuid().ToString('N')
$root = "D:/CP6-local-test/WP02-28/$owner"
$source = (git rev-parse HEAD).Trim()
$cache = 'D:/CP6-local-test/WP02-02/adopt-a1/n'
& $py -B -X utf8 eng/wp02-28/pg_resource.py create $owner
if ($LASTEXITCODE -ne 0) { throw 'Resource creation failed; inspect retained private logs' }
try {
    $result = & $py -B -X utf8 eng/wp02-28/published_artifact.py --owner $owner --settings "$root/settings.json" --publish-directory "$root/publish" --source-sha $source --package-cache $cache
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; keep logs' }
    $receipt = $result | ConvertFrom-Json
    # Preserve this pin independently; never replace it from a later directory scan.
    & $py -B -X utf8 eng/wp02-28/verify_published_host.py --owner $owner --settings "$root/settings.json" --publish-directory "$root/publish" --receipt $receipt.receipt --receipt-sha256 $receipt.sha256 --source-sha $source --package-cache $cache --negative-checks
    if ($LASTEXITCODE -ne 0) { throw 'Published diagnostic failed; keep logs' }
} finally {
    & $py -B -X utf8 eng/wp02-28/pg_resource.py stop "$root/settings.json"
    if ($LASTEXITCODE -ne 0) { throw 'Owned resource stop failed; inspect identity and sessions' }
}
& $py -B -X utf8 -m unittest discover -s eng/wp02-28 -p test_published_host.py -v
if ($LASTEXITCODE -ne 0) { throw 'Tool tests failed' }
```

运行 fixture 只有自有资源 marker 与管理员授权所需的最小 Sys_Users 表，runtime 只读，无 Main 全量迁移；`latestMigration=null` 如实保留。`/health/release.gitSha` 是运行配置声明，独立的 publish 命令/收据/文件哈希及 OS 进程才提供本次本地绑定，不把该配置值当成程序集自证。

## 交付范围与未运行项

仅新增登记范围内三个 Python 工具/测试文件和本目录证据。未改 Program/Core/EF/PG/CLI/项目/锁文件、部署/工作流、旧报告或共享状态。工作树开始已有历史长路径审计文件缺失显示，保持原状且未纳入提交。自查覆盖本窗口完整候选差异、证据来源/链接和敏感信息；COORD 继续统一任务级集中审查、台账/四状态与合并。

推送前核 origin/main 为 `4704592f52172c941e1cb95bdeb3c73f0dd179e8`；候选与远端工作流及依赖未变，普通工作流均仅 workflow_dispatch，r2-candidate 的 push 只匹配 `v*.*.*` tag。仅普通任务分支 push，不推 tag、不创建 PR 或触发 Actions。任务运行列表为空，无需取消；触发配置未改。Handoff 私有、Actions disabled，默认/协调/本任务分支均无 workflow，消息仅追加本任务目录并回读固定提交。

两份实际不同且兼容的 service-token Main 制品尚不具备：两版本正向回退、准确 schema/driver 兼容、真实 serviceHTTP 全链、完整业务采用/原业务验收、Current trust、生产候选、R2/GHCR/Azure 制品与 DEV/UAT/PROD 部署均 **NOT_RUN**。本地五包 Matched 不解除这些边界；回退仍 `allowed=false / COMPATIBILITY_EVIDENCE_MISSING`，业务/runtime qualification/trust 仍 Unknown。
