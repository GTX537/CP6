# WP02-28：实际 publish 本地验证与第十七轮定向修正

## 2026-10-18 总控集成收尾（当前结论）

**本窗口已正常集成，[#251](https://github.com/GTX537/CP6/issues/251#issuecomment-6251365810)、[#252](https://github.com/GTX537/CP6/issues/252#issuecomment-6251366820)已Closed；完整WP02任务、真实业务及生产验收未完成。** [原审查最终结论](https://github.com/GTX537/CP6.AI-Handoff/blob/a7d24cd8c42fb6d07fecb563446ad2778ba1e694/handoffs/CP6-DEV-20261012/tasks/REVIEW-W13-C/REVIEW.md)为Ready to merge Yes，固定候选 `aec0e965a5419031a4d0089c8fb8da42f563356f` 无阻断，Handoff `82df565f156b32751038e5076537d68a55bfc0f8` 已远端回读。原报告接续完成，没有重复START或完整审查。

[正常代码PR](https://github.com/GTX537/CP6/pull/271)已合入；合并时main `c2061699d917042e85149b50bd58f8ddfbb3aadd`，总控再核最终产品main `8146d5e4063a1ae702e7639dc00f2df0ba29a37d` 同时包含C/D候选，完整11个范围文件与固定候选一致。复用本报告原11项工具回归及一次真实locked restore/publish；原缺陷失败、两次导出预检失败均保留。实际新publish source仍是68990978579163cb8bb8f9e3fd524e7242c40be4，pin仍为fecb9403d6a7bc80b596a5cf302a9688cf51fec090f6bcfb8c7ccb46b9f8e99c；原宿主/HTTP保持4704592f历史范围，不把新main称为实际宿主已验。

本次集成只核对源码/证据适用范围、无冲突的合并结果和Issue原完成条件；没有因SHA变化重跑产品、publish、PG/HTTP或页面。原报告/原始字节继续保留，发布物未重新签发。第十七轮本窗口写集/预约释放，目录按存储计划保留并待显式重新分配，没有删除或新建工作树。无Actions运行/取消/触发变更、既有数据库或部署操作。以下待审查/待合并/Open是历史时点，不能覆盖本节当前结论。

## 第十七轮 C / FIX-W13-C：#251、#252 修正交付

2026-10-18（America/New_York）。总控已解除旧 P0 普通集成暂停并通过 PR267 准入；本次只修正原 B 审查的两处边界，独立交付给 B/COORD 做局部复查，不能据此关闭完整 WP02-28 或宣称业务/发布验收。

- 最新已核 main/origin/main：`2f20834af3e0192cdb0d0fda404f0fc398be592d`。新工作树 `D:/CP6-worktrees/wave17-c-publish-boundary-20261018`，分支 `codex/bugs251-252-publish-boundary-20261018`；总控从该 main 承接原候选后准备 HEAD `68990978579163cb8bb8f9e3fd524e7242c40be4`。原分支/工作树保留。
- 本次工具/回归提交：`4f2b61f0109662c86558ae29d3defd96e710c16a`。含本报告的最终候选由 Handoff DELIVERY 固定引用；报告变更不触发重复 publish。
- 续接同一任务的一次 PROGRESS：Handoff `4b3fbca13df55ee139e9e1552014d87816526619`，`handoffs/CP6-DEV-20261012/tasks/WP02-28/messages/20261018T134113Z-wave17-progress/MESSAGE.md`，已安全发布并回读。准入固定 Handoff 为 `6344b2dec8c68c5603e8d7894db5ca70299fc6fe`；没有重复 START/等待运输 ACK。

### 修正行为

**#251**：构建不再读工作树。工具从指定完整 Git 提交枚举 WebApi 的项目引用闭包及显式 contracts 内容 glob，将固定树的原始文件导出至新私有目录。逐文件核对 Git blob，拒绝链接/submodule、已提交 bin/obj、导出遗漏/转换；命令级关闭 archive 的 autocrlf，避免改变原始字节。忽略的 `.cs`、私有 JSON 和旧 bin/obj 完全不会从工作树拷入；不会通过重扫污染输出生成“干净源码”收据。

受控导出只支持当前可审查的 SDK/声明式项目形态；显式 Import/Target/UsingTask、动态或越界引用、自定义额外 props/targets/user/rsp、项目属性注入等失败关闭，不用单文件名黑名单。固定 root Directory.Build.props、显式已提交 NuGet.config、隔离 MSBuild 继承环境/用户扩展路径，禁父目录 targets/central package props/auto response，生成明确选择本机 .NET8 SDK 的 global.json。此工具不是任意 MSBuild 工程的通用沙箱；未来新增构建形态需要评估输入边界。SDK 与 locked NuGet 包仍是独立工具/依赖来源，不称为 Main 提交文件。

restore/publish 的工作目录均为导出目录，项目输出从空 bin/obj 生成；构建后再次确认原始导出文件未改。收据保留完整输出清单/正式包来源/实际命令和日志摘要，增加导出目录、文件数/集合摘要及 SDK 记录。仍兼容验证原固定收据，原收据 pin 和全部 JSON/log 字节未改。

**#252**：finally 使用 Popen 创建时持有的 Windows 进程 handle 终止并等待自有子进程，不依赖再次 CIM 查询或按任意 PID 打开进程。首次身份失败仍抛出原异常，completed 保持 false；清理异常记录 cleanupFailureType，不能覆盖已有 failureType/原异常，processStopped 在确认退出前保持 false。正常路径仍须确认退出与 listener 关闭才能 completed。

### 实际定向验证与失败保留

原件统一位于 `D:/CP6-local-test/WP02-28/wave17-c-corrections/`，本报告直接引用，不复制新一套日志/清单入仓。

- `red-behavior-tests.log`：原实现出现预期的 1 failure / 2 errors，分别是 build cwd 仍包含忽略 `.cs`、首次身份失败被第二次查询异常覆盖。测试 teardown 用各自创建 handle 回收短命子进程，没有遗留服务。更早 `red-tests.log` 包含首次测试夹具拦截 git subprocess 的问题；改正夹具后才以 red-behavior 为缺陷证据，两份都保留。
- `final-tests.log`：**11 passed**。保留原 6 项工具覆盖，新增固定提交导出排除忽略输入/旧输出、真实 SDK Compile/Content 求值、父目录配置及环境注入隔离、越界 Import/ProjectReference 拒绝，以及两项真实短命 Python 子进程故障路径。SDK 求值先证明旧工作树纳入 `tmp/Uncommitted.cs` 和 `appsettings.Development.json`，再证明新导出仅含提交输入；不借 mock 宣称实际 SDK 行为。模拟 publish 的测试收据仅是夹具，不是实际制品证据。
- `publish-run.log`、`publish-verified-run.log`：两次真实操作在源码预检阶段分别因 NuGet.config 大小写假设、archive CRLF 字节转换被拒绝，**均未调用 restore/publish、未写收据**。修正后使用新的输出目录；失败目录和日志原样保留。没有覆盖旧证据或将失败改称成功。
- `publish-fixed-run.log`：之后仅一次真实 locked restore + Release `dotnet publish`，退出码均为 0。保留原 NU1902/NU1903、CS1998 警告，不称作生产漏洞检查通过。

实际发布私有根为 `D:/CP6-local-test/WP02-28/ab0f05ce88ac408cb28a6c0c8b51e589`；仅创建 ACL 受限 owner marker/settings/源码/构建输出，**没有初始化 PostgreSQL，没有创建数据库或启动宿主/HTTP**。原 marker 中的端口/数据库命名是既有路径校验合同，本轮未使用这些资源。

| 本次真实 publish 绑定 | 实际值 |
| --- | --- |
| 产品源码 SHA | `68990978579163cb8bb8f9e3fd524e7242c40be4`（总控准备提交；不是修正工具提交/报告提交重新构建） |
| SDK / 导出文件数 | .NET SDK 8.0.424 / 2,105 个固定提交文件 |
| 实际输出 | 私有根 `publish-fixed/`；260 文件、180 个闭包必需文件，五包独立正式期望匹配 |
| 入口 DLL SHA-256 | `2ef0c9048b729814c8a945ed880661e2837aac43ac5ac2834f5bcf2011ffdd46` |
| 收据原件 | 私有根 `publish-fixed.receipt.json`；完整输出清单、原始命令、工作目录、日志摘要与正式包来源均在此 |
| 独立收据 pin | `fecb9403d6a7bc80b596a5cf302a9688cf51fec090f6bcfb8c7ccb46b9f8e99c` |
| 原始构建日志 | 同根 `publish-fixed.restore.log`、`publish-fixed.publish.log`、`publish-fixed.build.json` |

复现工具回归：`python -B -X utf8 -m unittest discover -s eng/wp02-28 -p test_published_host.py -v`，使用本机 Python 3.13；包含实际 .NET8 SDK 求值和无数据库/HTTP的自有短命子进程。再次做 publish 必须使用新 owner/私有输出并保留新 pin；本轮已经完成一次真实 publish，无需为后续提交 SHA 重跑。

### 复用与交接

下文第十三轮的真实 publish、五包 Matched、旧授权矩阵及资源停止仍仅证明原 source `4704592f...` 的当时结果；本轮未再执行宿主端点或数据库验证，不能称当前 main 宿主已验证。原 JSON/log 字节及全部证据 Git blob 均与原候选一致；`.gitattributes` 的工作树 CRLF 为既有 checkout 规则，不改变 blob 或证据文件。本轮新增 source export 并不重签旧 receipt。

自检覆盖最终工具、回归、原始证据保留及报告差异；相对准备 HEAD 只改登记的四个文件，未动 pg_resource/verify_host/Program/模型/项目/锁/原 JSON-log/共享状态。未增加 SQL Server 验证。本分支与 origin/main 的 13 份 workflow 相同：12 份仅 workflow_dispatch，r2-candidate 只匹配版本 tag；只推普通分支。任务运行列表为空，无需取消，没有触发/修改 Actions。Handoff 私有、Actions disabled、默认/资料分支无 workflow。

原审查者 B 接收 #251/#252 定向复查，COORD 负责共享状态、正常合并、远端包含性与 Issue 关闭。原 NOT_RUN（两版本正向回退、schema/driver、真实 serviceHTTP、完整业务采用、正式制品与部署）保留；本地 source-bound publish 不提升 production trust。B 的 #253 固定交付到达后，C 在原 REVIEW-W13-B 中另做局部复查，不阻塞本修正独立交付。

---

# 第十三轮原交付记录（以下均为历史执行范围）


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
$source = (git -c core.longpaths=true rev-parse HEAD).Trim()
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
