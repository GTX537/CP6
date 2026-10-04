# WP6：完整应用、双库回归与本地恢复

## 2026-10-04：WP6 最新本地验收全部通过，远端交付待完成

洁净源码 e940825fc7511b41a1a80b4b73897dcc69e51bbb 的最新 wms-local-transaction-fix 四次实际分项全部Passed：PG Matrix34/34、SQL Matrix37/37、两库Application各17/17，共105个适用入口、零失败/跳过；不是单次Full。当前程序集补充配置/四Context接线/生产校验139/139，零跳过。两库使用同一份当前实际发布API（261文件逐字节一致），真实独立恢复专项核对56项HTTP、8次API启动、所有表/行/序列/history/持久key与恢复后消息一次投递/幂等重放通过；本轮31个owned临时库全部普通DROP且AbsentVerified。

八项完成条件的本地范围已逐项绑定实际原件及断言源位置，旧失败/成功及原审查时点保留。一次集中审查与定向修复/清理/WMS整合复核无剩余实质阻断。WP6公共证据归档/index核对、正常PR/main交付、合并后冒烟与远端包含性仍待完成，父Issue134继续Open；不能因本地全部通过就宣布任务关闭。实际版本、源码/产物哈希、复用来源及范围见[最终本地验收](WP6-ACCEPTANCE.md)，下方旧状态均为历史时点。

普通PG API/worker临时guard已移除，仅在同一部署使用所选Provider及连接；旧记录中“guard保留”是当时状态。R2/GHCR正式门禁和SQL生产基线不变，未启动/取消远程Actions、修改触发/保护、切换既有环境或部署生产。已有SQL数据搬迁、同实例混库及外部CRM全面改造不在此目标内。

## 2026-10-04：BUG163 已交付，WP6 接入当前版本验收

[BUG163](https://github.com/GTX537/CP6/issues/163) 已由 [PR164](https://github.com/GTX537/CP6/pull/164) 正常合入远端 main `6322ac152cab7d462719169fb0301f49e9ee39ba` 并关闭，WP6 已接入。WMS 原八项＋新增十七项两库各25/25、零失败/跳过；合并后两库各3/3。两个修复测试库及原 WP6 SQL 失败库均已普通 DROP 并确认不存在；SQL 实际持续会话超时拒绝、短暂会话自行结束后清理通过，原 Failed summary 与 core/WMS TRX 字节保持。[专项记录](../2026-10-03-bug-163-wms-local-transactions/README.md)保留111份原件及36份交付、冒烟、清理原件。

当前必需清单只扩展 WMS 为25项，其他61入口不变。最终入口仍为 PG Matrix34、SQL Matrix37、两库 Application各17，共105个适用入口；使用新的 `wms-local-transaction-fix` 运行目录。编译输入已改变，需当前代码七项构建及新的完整双库验收；后续 SQL 可在源码与完整运行文件逐字节一致后复用 PG 的真实构建，两库应用使用同一新发布产物。此前 PG Matrix34/34、旧发布物应用17/17保留历史来源，不代称当前最终验收。WP6 与父任务#134仍未完成；最终恢复审计、集中审查、归档与正常远端交付待完成。没有启动远程 Actions、改变生产门禁或部署既有环境。下方记录保留历史时点。

最新阻断（2026-10-03，BUG163 Open）：SqlServer cad-cleanup-fix Matrix在原核心25/25后，WMS8项为7P/1F，原Lpn_UsesCompositeSerialIdentity_AndMovesSplitsMergesWholeTree的CreateAsync第二查询触发Implicit distributed transactions have not been enabled；后35入口未执行。原summary SHA256 8259FAEA4EEEF0786705CB4501F1072F79D0CA7DC116606B9EEB5364A7205F50与WMS TRX 5AD8388203E589148B68795BFBC56C2478BC0D95EF9646C6A3A5D06C042A2870保留Failed，一个该次SQL owned库暂留。已登记[Issue163](https://github.com/GTX537/CP6/issues/163)，修复从最新已确认main ce49012d37da4b1b01f074d5b5b7fd227780e656创建独立codex/bug-163-wms-local-transactions分支。源码确认LPN四处自建ambient事务，runner明确Pooling=False；须原生确认与修复本地事务生命周期，Label相似路径尚待实际确认。PG Matrix34/34/11库Absent仍是修复前该次证据；修复影响编译输入后最新双库最终Matrix/Application需要重新适用验证，WP6与父Issue134未完成。首次SQL连接缺失为配置预检拒绝，未创建运行目录/DB；随后仅进程内使用既有本机KOUSQLSERVER Windows登录，未改用户PG配置/环境。


最新验收进展（2026-10-03 23:17Z）：新cad-cleanup-fix PostgreSql Matrix已实际Passed，34/34必需入口、零失败/跳过，11个owned库AbsentVerified；summary SHA256 5AD4D28C008E705F9DF355572ED9B532B1D80723BE1FB4D18439FDA1321BB107。该轮明确SkipBuild，复用原实际7项成功构建：编译输入与2059运行文件逐字节一致，PowerShell限定清理变化另有RED/GREEN及native控制证据；不把上一轮清理Failed改成Passed。进入SqlServer Matrix37，SQL136旧基线仍单独构建/安装；两库最新发布物Application17各仍待执行，最终105入口/恢复核对、归档、正常远端WP6交付与父任务关闭尚未完成。


最新本地进展（2026-10-03）：cad-provider-fix PG Matrix 的34/34入口全部通过，CAD25/25且零skip；收尾owned-database-cleanup遇到短暂会话，整轮仍为Failed，原summary SHA256 EEF84ED7808D7D83D7389708102C3989BA03DEEDD4EA17C367123D476AC4BCD1及34入口原件不变。七库原已Absent、余四库由实际限定等待控制后普通DROP，11库均Absent；原会话类型未记录，不推断其来源。WP6新清理模块默认逐库最多等待15秒，只对会话未结束重试，每次重核owner/principal/physical，超时或身份变化仍拒绝，保留全目标预检及逐库复检；新离线控制RED后41项GREEN，原42项契约通过，真PG持续会话1秒超时拒绝且receipt不变，短暂会话自行结束后清理通过。仅此PowerShell与控制变化，不改业务编译输入，7个既有真实成功构建/2059运行文件逐字节核对后复用；新完整Matrix和双库Application使用cad-cleanup-fix目录，最终验收/远端交付仍待完成，父Issue134保持Open。下文旧目录及状态保留历史时点。


最新交付（2026-10-03，BUG161 Closed）：PR162 正常合并到远端 main ce49012d37da4b1b01f074d5b5b7fd227780e656，并已接入 WP6。两库 CAD/资产/协作原15＋新十项各25/25、零skip，补充 InMemory17/17；合并后两库各3/3，PG两项协调测试再次实际40001后恢复。两个 BUG 自有库已普通DROP并核对不存在，Issue161于22:11:25Z关闭。[源码/原始失败及双库回归](../2026-10-03-bug-161-cad-provider-recovery/README.md)与交付/冒烟/清理原件保留；原 claim-fix Matrix 28P后14P/1F及后五入口未执行仍为失败，七个该次PG库清理且原summary不变。最新清单仅把CAD组15扩为25，其他61入口不变；最终两库Matrix与最新产物Application采用新cad-provider-fix目录，仍待实际执行。WP6未完成，父Issue134仍Open。下文BUG161 Open及旧claim-fix计划保留历史时点。


最新状态（2026-10-03，BUG161 Open）：claim-fix PostgreSQL 最终 Matrix 已终止为 Failed；7 个相关项目构建通过，28 个入口 Passed，第 29 个 CAD/资产/协作入口 15 项为 14P/1F，后五入口未执行。原 Concurrent_replace_preserves_one_current_revision_and_immutable_evidence 出现实际 PostgreSQL 40001，失败 summary SHA256 9A788F5E6450F4B4FE4258FEDA9FFC99B959EB7A7E997F2A8EB2BEAD49F6284C，TRX SHA256 32E0C9CD7990B0EE5ACD8B7F20C19EA351C3F9465D13FAEAABE711B7D275E364；原件保留。修复从已确认远端 main 4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0 建立独立 codex/bug-161-cad-provider-recovery 分支，活动缺陷以 [Issue #161](https://github.com/GTX537/CP6/issues/161) 为准。最新 SQL Matrix / 双库 Application 仍未开始；WP6 与父 Issue134 未完成。下文 claim-fix 计划及状态保留历史时点。


父任务：[DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134)。当前为实施与验证中，未完成交付。WP1–WP5 已进入远端 main；WP5 的正常合并、双库冒烟和 13 个自有库清理见 [交付原件](wp5-native/post-merge/manifest.json)。

## 2026-10-03 最新整合：BUG159 已交付，最终验收继续

BUG153/155/157/159 已分别由 PR154/156/158/160 正常交付并关闭，最新确认远端 main 为 `4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0`，已接入本 WP6 分支。此前 `wp6-pg-formal-matrix-floor-fix` 七个项目构建通过，26 个必需入口通过；第27个任务/生成/保留组原17项中16P/1F，实际领取并发触发 `23505 / UX_Space_JobAttempt_Tenant_Job_AttemptNo`，后续七个入口未执行。原失败保持，七个 owner-marked PG 临时库已正常清理，summary SHA-256 `AFB53D4AE12AC5D43C00DAFA12945C4E4FE3C313B9650BF03A5047E3F5A281E5` 未改变。

BUG159 的精确领取冲突恢复，两库原17＋新6各23/23、零skip，构建和一次专项审查通过；合并后原步骤/协调冒烟各2/2、PG观察实际23505后恢复，两个自有库清理及远端包含性/完整树核对完成。62份本地原件保留真实RED和GREEN；四个执行脚本误引用旧任务的归档错误在推送前用追加审计提交校正，当前原件manifest及34份交付原件见[专项记录](../2026-10-03-bug-159-space-job-claim-recovery/README.md)。注入控制不称为实际数据库死锁复现。

只把新六项加入任务组23，克隆23、设计/发布41及其他61个入口保持。最新纯离线入口510检查、结果解析463断言/59份既有报告/62入口通过；这些不替代真库验收。最终执行改用新的 `wp6-pg-formal-matrix-claim-fix`、`wp6-sql-formal-matrix-claim-fix`、`wp6-pg-formal-application-claim-fix`、`wp6-sql-formal-application-claim-fix`，由当前源码构建并发布，全部终态成功、实际清理及105个适用入口的精确覆盖证明仍待完成。四次分项不会改称单次Full，原应用17/17保留旧产物来源；父任务#134继续Open。

## 实现范围

同一份 CP6.WebApi 发布产物按 `Database:Provider` 选择 SQL Server 或 PostgreSQL。移除 WP1–WP5 的 PostgreSQL 完整应用临时 guard，保留 provider 校验。两种数据库共用业务接口与初始化入口，四个 Context 的迁移所有者仍由既有 profile 决定。

本地入口为 [`Test-Cp6DatabaseCompatibility.ps1`](../../../scripts/Test-Cp6DatabaseCompatibility.ps1)，支持完整、矩阵或应用分项。固定场景清单要求精确名称、零跳过、真实进程退出与实际数据库身份；分项通过不称为完整验收。测试夹具共享 owner 校验，不自行清理正式 runner 的数据库。运维配置、调用方式与限制见 [运行手册](../../devops/DATABASE-COMPATIBILITY.md)。

## 已执行结果及待办

以下为原始分项运行，不能合称一次正式 Full 执行。最终公共证据归档和远端交付尚待完成。

| 范围 | 当前实际结果 |
| --- | --- |
| Runtime guard | 旧发布物真实 PG 启动失败被记录；修复后的同一份 API 在两库启动，相关定向测试 7/7 |
| 共享测试库契约 | 初始纯契约 RED 45/45 失败，实现后 45/45 通过；审查发现的 partial-config 选择漏口修复后 Core 58/58、Space 4/4、ERP 4/4，均零跳过（包含原 45 复测，不称为 66 个新增业务用例） |
| 首次/重复初始化 | 两库各执行两次实际 db-init，exit 0，迁移齐备；同一数据库的自定义种子保持 |
| 应用与重启 | 两库真实健康/版本、管理员登录、匿名 401、普通用户改密后权限 403、后台通知持久化与重启后分发通过 |
| 原生备份恢复 | SQL BACKUP/VERIFYONLY/RESTORE 与 PG pg_dump/pg_restore 实际执行；恢复前后所有用户表、行摘要及序列/identity 对账通过；旧数据库 Data Protection 分页游标可在恢复应用中继续使用 |
| 历史 SQL 升级 | 固定旧提交 993e844 的真实 API 安装 136 项迁移并保留种子，再以前向迁移到 140；2/2 通过 |
| PG 矩阵 | 前 11 个入口通过，ERP 95 项中 93 通过、2 并发测试失败；原失败保持 |
| ERP 并发诊断 | 实际 PostgreSQL 40001 / SerializationFailure；独立 [BUG153](https://github.com/GTX537/CP6/issues/153) 修正测试投递流程，双库原 ERP 各 95/95；[PR154](https://github.com/GTX537/CP6/pull/154) 已合入主线 59e09f6e，两库原两项合并后各 2/2；两个自有库已清理且 Issue 已关闭，见 [交付原件](../2026-10-03-bug-153-erp-concurrency-retry/native/post-merge/manifest.json) |
| 正式 PG 应用入口 | Application 分项 17 个入口全部通过，包含真实 SignalR 9 项、恢复后的待发通知/幂等重放、前后产物核验、源输入不变检查；两个正式临时库已普通清理并确认不存在。FullAcceptance=false，不代称完整矩阵 |
| 正式 SQL 应用入口 | Application 分项 17 个入口全部通过，包含真实 SignalR、恢复后的待发通知/幂等重放及实际输入、产物核验；两个正式临时库已普通清理并确认不存在。与 PG 使用相同发布文件，FullAcceptance=false |
| 最终业务矩阵的首轮失败 | PG 七个相关项目构建通过，25 个入口通过后在 Space 设计/发布组失败（35 项中 34P/1F），实际原生 40001；独立 [BUG155](https://github.com/GTX537/CP6/issues/155) 已由 PR156 交付并关闭，两库原 35＋新六项各 41/41、合并后原并发/协调并发各 2/2 及两个自有库清理完成，见[交付原件](../2026-10-03-bug-155-space-validation-retry/native/post-merge/manifest.json)。原失败保持，不合称单次 Full |
| 接入 BUG155 后的矩阵失败 | 新 PG Matrix 七个相关项目构建通过，24个入口通过后在原克隆16项中15P/1F，生产创建楼层发生实际40001；独立[BUG157](https://github.com/GTX537/CP6/issues/157) 已由 PR158 交付并关闭，两库原16＋新七项精确克隆各23/23、设计/发布各41/41，合并后原并发及两个协调场景各3/3及两个自有库清理完成，见[交付原件](../2026-10-03-bug-157-floor-initialization-retry/native/post-merge/manifest.json)。本次原失败保持，后续九个入口未执行 |
| 接入最新修复后的最终验收 | 接入最新主线5587a2a6。设计/发布清单41项保持，克隆清单由16扩展至23，其他61个入口保持。生产依赖改变，最终两库Matrix与Application将用新构建和floor-fix目录；修复前两库17/17和旧运行物仅保留历史适用范围 |

首次 PG 生命周期执行明确记录本机服务未启用 SSL；仅本次 loopback 测试显式采用 Disable，没有修改服务、全局连接或生产模板的 TLS 设置。SQL 初次原生命令参数失败也保留，修正参数后才完成实际运行。

## 审查与证据边界

以整个 WP6 为一次集中审查，分工覆盖共享夹具与结果判定、应用/进程/恢复快照、生命周期/入口及正式编排。四项边界已定向修正：partial-config 选择、SignalR 代理直连、每次应用启动前产物核对和测试运行目录依赖清单。相应契约 58+4+4、应用产物离线 RED→GREEN 16/16、Entries 510 项离线检查通过；Results 455、Lifecycle 42、runner 147 项离线验证按各自来源保留，不计作原生数据库业务用例。源码静态审查无剩余实质阻断，最终实际矩阵和交付仍待完成。

发布物在本地验证中共 261 个文件，两库使用相同字节。构建基于 cbbb7fc8 加当时工作树的已记录输入，不能称为该提交的洁净构建；合并后按源码、依赖、环境和产物实际相同范围复用。

正式 PG Application 完成后，WP6 接入 PR154 的主线 `59e09f6e68a36734c64144abfcefc81b7c9c8d9d`。SQL Application 复用先前实际构建：两个分项的 2009 个相关源码/配置输入哈希完全一致，261 个 API 发布文件也一致；Source HEAD 的变化单独记录，不冒称重建产物。

全部验证在本机隔离端口与本任务独占数据库执行。没有触发 Actions、修改分支保护、生成生产候选或部署既有环境。私有凭据、JWT、Cookie、完整备份和业务状态文件不进入公共证据。用户既有 SQL Server 数据搬迁不在本目标内；现有 R2/GHCR 生产候选与审批规则仍是发布权威。
