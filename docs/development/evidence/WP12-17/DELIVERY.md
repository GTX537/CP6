# WP12-17 外部结果持久接收与本地流程/Owner 应用

本报告对应 CP6-EXEC-2 的正式交付。COORD 已独立复核技术候选 `b00115ccc71106b60fe9240a21c0e73d7ce201fc`。Main 基线为 `42efc232798beff73a508883226241abfadd4e17`，本轮受验源码提交为 `c56c9e07d62e7f9af8bca544324aae9bf674e832`，树为 `697005a7ad32b82ecd1b362893457561f139e0f1`。从固定的 Main DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018` 与 `codex/wp12-17` 继续原草稿，18 项严格前置均完整 DONE。 [正式派工 #414](https://github.com/GTX537/CP6.AI-Handoff/blob/f2fffc16d1b870bfb6c7feb577b66c02d4070841/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T081303Z-cp6-exec-2-wp12-17/MESSAGE.md) · [唯一 START](https://github.com/GTX537/CP6.AI-Handoff/blob/31219e240aec5d6e95ce1d0d092ff8e1c9582332/handoffs/CP6-DEV-20261012/tasks/WP12-17/messages/20261123T082552Z-cp6-exec-2-start/MESSAGE.md)

## 实现

- 精确提供方返回与原 Scope、Job/invocation、operation、effectKey、运输键、输入摘要和服务登记一致的成功原件。ResultInbox 保留原字节与摘要，同结果沿原件定位。异体、非法 schema 和受保护字段独立隔离。结果接收、隔离正文与 PostgreSQL 同为 1 MiB 上限。超限保留隔离原因与原摘要。白名单绑定原响应映射，十进制超精度直接拒绝。 [接收与应用](../../../../CP6.Core/Services/Oa/ConnectorResultApplicationService.cs) · [输出映射](../../../../CP6.Core/Services/Oa/ConnectorResultOutputV1.cs)
- 原事实先提交。冲突异体隔离后，正常 worker 继续沿已保存的首份有效原件处理。当前 worker 在 `WF_AUTH`、原 Job 租约、实例、token 和 pin 下推进，唯一 ResumeReceipt、Job、token 与 Outbox 同事务。旧租约能保留事实，已离开或撤回的 token 只记 `STALE_TOKEN`。已知原件的后续恢复从本地读取。正常调度凭已入箱原件领取正在等待的作业。 [worker 接入](../../../../CP6.Core/Services/Wf/WfServiceJobService.cs) · [PostgreSQL 前滚迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261123083000_OaConnectorResultsV1.cs)
- 原生 APP 继续采用 `oa.app/2.0` 与已冻结审批票据。精确 pin 的技术租约独立受限，进入 APP 终态时由同一 Tx D 写入决策证据、决定与 Outbox。OA 管理页分别显示外部结果、流程应用和独立 Owner 应用。待本地应用读值为 `HELD`，`RESULT_RECEIVED` 显示为“已接收结果”。Owner 回执从既有固定来源和连续链投影。 [APP Recorder](../../../../CP6.Core/Services/Oa/ApprovalTaskDecisionOwnerV2.ConnectorResume.cs) · [派发读入口](../../../../CP6.Core/Services/Oa/ConnectorVersionControlService.Dispatches.cs) · [OA 管理页](../../../../cp6.web/src/views/oa/admin/ConnectorControlPanel.vue)

## 本地验证

本机为 `LAPTOP-3QQ44FJS`。运行实际 Main HTTP、独立 hosted worker、Chrome 原 OA 页面和隔离 PostgreSQL 18.6。外部提供方为回环 HTTP 合同夹具，证据为 `SIMULATED`。Owner 场景经过 Main Tool Owner 的真实入口和原回执链，登记的证据模式为 `DEMO`，页面标 `SIMULATED`。

| 原 AC | 本地证据及边界 |
|---|---|
| CONN-21 | 已确认成功原件在独立 worker 中提交后结束进程，接管 worker 读回原件并推进，POST=1、GET=0。APP 应用回包丢失后读回同一 ResumeReceipt。同事务能力缺失时保留原件并待本地重试。TerminalFailure 和 FailureApplicationReceipt 留待 WP12-18 与本卡合验，整条待核销。 |
| CONN-22 | 用扫描入口的时钟推进触发到期回收。旧 worker 留下原件且推进为零，当前 worker 在正常扫描同一轮隔离异体并提交原件的唯一 ResumeReceipt 和对应 Outbox，实际应用为第 2 次尝试。POST 与 GET 使用同一组原键和摘要，分别共 1 次，其后正常扫描增量为零。另复验作业先等待、有效原件后到的调度恢复。 |
| CONN-23 | 从既有撤回入口撤回，迟到成功仍保存原件并显示 `STALE_TOKEN`，实例保持撤回，流程应用 Outbox 和补偿增量为零。 |
| CONN-24 | 额外 StarterId、精度超界、坏 schema、错服务登记与大于 1 MiB 的结果隔离。原 token、实例和业务字段保持原事实。 |
| CONN-26 | 省略 APP 决定 Outbox 后数据库拒绝提交，决定、token、变量、ResumeReceipt 与应用 Outbox 整体回滚。恢复写入唯一决定和应用回执，APP 证据载明 `SYSTEM_EXECUTION/RESUME`。 |
| CONN-27 | Main HTTP 与实际 OA 对照 `RESULT_RECEIVED`、`SUCCEEDED`、`HELD`、`APPLIED` 与 `UNPROVEN`。业务 Owner 独立证据缺失时仍显示未证实。 |
| CONN-28 | 第 2 条原回执先到返回 `PENDING_GAP`。接收固定链第 1 条后连续版本至 2，投影指向原第 2 条的实际 `BLOCKED`。填补缺口时外呼、Owner 应用和库存事务增量为零。 |

当前资格被撤销时，已验证结果留在 ResultInbox，恢复资格后只做本地应用。OA 浏览器检查“已接收结果”和“流程 待本地应用”文案，并核对持久接收、流程应用及业务 Owner 三轴。 [结果用例](../../../../CP6.Tests/Oa/ConnectorResultApplicationTests.cs) · [APP 和 Owner 用例](../../../../CP6.Tests/Oa/ConnectorResultAppTests.cs) · [浏览器脚本](../../../../cp6.web/scripts/oa-connector-control-browser.mjs)

### 晚到有效原件的正常队列恢复

正常扫描同时考虑原定到期时间和已验证原件的接收时间。后者仅取本租户、本部署环境已有原件且未有应用记录的作业。APP 的 PostgreSQL 领取守卫核对同一 Scope。领取后仍由当前租约和授权校验决定本地应用。

用旧草稿候选 `0eece22abd31032118e61b2567587244ca357b6b` 和新增用例重现，原件已持久化且 Main API 显示 `HELD`，下一次正常扫描处理 0 条。新候选中，第 2 次尝试隔离精度非法的原键 GET 回包，Job 进入无期限等待，原件入箱前重扫仍为 0。第 1 次尝试的有效回包随后到达，只留下原字节，Job 排期、状态、尝试计数和行版本保持原值。

当前 worker 第 3 次尝试从正常扫描领取同一 Job `004afe96-28a6-4566-ba9c-1985722377df`，提交唯一 ResumeReceipt `180b9fa3-17ab-46c0-b7d9-2fe220dbab1a` 和对应 Outbox。重扫处理 0 条，入箱之后 POST 与 GET 增量均为 0。回环模拟场景通过扫描时钟推进完成旧租约回收，整个过程沿业务入口读写。APP 守卫另用真实作业快照做只读断言，原件入箱前拒绝未到期领取，入箱后允许同一 Scope 领取。 [队列回归](../../../../CP6.Tests/Oa/ConnectorResultApplicationTests.cs) · [APP 回归](../../../../CP6.Tests/Oa/ConnectorResultAppTests.cs)

### 结果字节边界

夹具用包含中文、前置空白和 `17.20` 的 UTF-8 原件，逐字节比对 PostgreSQL 读回与原件，并独立计算 SHA-256。4 个独立 Job 各 1 次 POST，共 0 次 GET。

| 原结果字节 | PostgreSQL 及 API 结果 | 原样本 SHA-256 |
|---:|---|---|
| 65537 | 合法，ResultInbox 原字节与摘要相同，流程应用 | `a63ab229cc5a99741a899274f2221f91f313e676680ed827d1e652df5038517d` |
| 1048576 | 恰好 1 MiB，ResultInbox 原字节与摘要相同，流程应用 | `a80a1aa78ee8a1890e9f3e321098c632cc0921f27f84045721e613eafb6add40` |
| 1048576 | 错误 schema，隔离行保留完整原字节及摘要 | `a80a1aa78ee8a1890e9f3e321098c632cc0921f27f84045721e613eafb6add40` |
| 1048577 | 超限拒绝，隔离正文为空，保留原摘要，无流程回执 | `531f4b9f2662275ae818690df0624ff558dc4a5a90b8124f7d370c7492ee0167` |

### 存量前滚

隔离库先迁移到 Main 基线末次迁移 `20261123065100_WorkflowDeadlineEvidenceV1`，确认尚无 ResultInbox。由原 APP 入口完成转交和一票审批，由与已采用 Main 源文件一致的 WP12-11 构建运行原 hosted worker，生成原派发准入及 `HTTP_ACK`。然后仅增加候选迁移 `20261123083000_OaConnectorResultsV1`。迁移后经 APP Owner 读回原审批和转交，从 PostgreSQL 按原 Job 读回 effectKey、运输键、输入字节与摘要，Main HTTP 核对原运输确认和 SIMULATED。原派发样本共 1 次 POST、0 次 GET，结果和流程回执为空。 [前滚回归](../../../../CP6.Tests/Oa/ConnectorResultForwardMigrationTests.cs)

| 存量事实 | 迁移前后相同字节数 | SHA-256 |
|---|---:|---|
| APP 审批命令 | 9108 | `68c941e611a6e58d8814dc34bb5aefc8f3d0b22153ce32eedce5f8be9f05a1bc` |
| APP 投票 | 7851 | `d27355770a30e6e048e9ed9c7f6133d36c02d2fc174c39ee973a3d02f2e2de57` |
| APP 转交命令 | 7795 | `7eb46be1d4121f6e6c4e359fac337d341a06abf8fd184d893648cbe25b48f3a5` |
| 派发准入 | 1707 | `943e5be9180f5293948f7a786788bf20adfefd953fbb2e54efe15841ce0a7e86` |
| 派发观察 | 397 | `a1fa0c648e97eea1e9040a01533198fc1f75f7cbc1c6b231549669a5fcf5543b` |

基线 worker 复用 `D:/CP6-test-artifacts/WP12-11/20261123-wp1211/dotnet/bin/CP6.Tests/debug/CP6.Tests.dll`，SHA-256 为 `a9911268d03f2576e23262a370e1445f825498d6dab5a4285dc5a8b13b5962b2`。其已验证源码为 `0d8f006314883bc1e168b68416bb7ba771ecca10`，与 `42efc232798beff73a508883226241abfadd4e17` 的 .NET 源码无差异。程序集保留当时工作区基线的版本戳 `6e3d9d07ac92fb906027df1ecc6817755705a9d8`。

### 复测工件

在受验提交重新构建，.NET 构建退出 **0**，编译错误 **0**。隔离 PostgreSQL 定向 **9/9**、实际 OA Chrome **1/1**、既有派发和共享队列回归 **11/11**，均无跳过。`vue-tsc --build` 退出 **0**，定向 Vitest **3/3**。工件根目录为 `D:/CP6-test-artifacts/WP12-17/20261123-wp1217`。

| 工件 | 内容 | SHA-256 |
|---|---|---|
| `late-red.trx` | 上一候选的失败对照，原件入箱后扫描结果 0 | `a971dafa0460d33678b8c465fca2b2ac7048f48f6450facf763c7d20b608f55a` |
| `late-final-build.json` | 受验源码提交、树、程序集 SHA-256 与构建退出码 | `48cb39facf6ce489a65d6bc1da012c55f3c07b8ca8ef7c7daac5786a2c98bb9a` |
| `late-final-build.log` | .NET 构建原日志 | `86dbfad258acbd5a055a079682e39c7e05cc6cdaac665dde4c863b474fd54759` |
| `late-final-results.trx` | 9 项定向 PostgreSQL（含晚到原件、冲突、边界、前滚）及 1 项实际 OA Chrome | `0e0465b64f27d2796a0c03d09b5d688981ef14ea127a5f7463e04f04a734d7d8` |
| `late-final-queue-regression.trx` | 8 项既有派发、2 项计时作业及 1 项旧 WebAPI 延迟执行，共 11 项 | `d6d31bbd491e603095ed52313401b37dc213b6b80975756b26b8b6d1ff28078d` |
| `permission-consumption-wp1217-late-final-results/control-browser/result.json` | 实际 OA 浏览器断言、Job 标识及请求证据 | `0ff3628931ef05647d85a20091b998ec43d94aaa68362fb3539b5003684d0de5` |
| `late-final-web-type.exit.txt` | vue-tsc 原退出码 | `3d79001cc6dcaa0eb3e14b71b76867a4e6a6188b4b22f7d2393384f9f0950774` |
| `late-final-web-unit.log` | 连接器 Vitest 3 项 | `0116664ed265211d005bbbb8f655ca86bb0586526e9f829a66c63047bde0f127` |

复测使用工件目录的 `run-pg.ps1`，指定 `-Build dotnet-late-final` 和新的 `-Run`。定向筛选 `FullyQualifiedName~OaConnResult|FullyQualifiedName~OaConn_browser` 并加 `-Browser`。前滚用例取 `CP6_WP1217_BASELINE_TEST_DLL` 指向上述历史构建。共享队列回归筛选 `FullyQualifiedName~OaConnDispatch_|FullyQualifiedName~PGT017_journal_service_timer_and_pause_share_native_commit_order|FullyQualifiedName~PGT017_legacy_timeout_error_edge_defers_webapi_under_the_original_job_identity`。

上轮 29 项既有回归保留在 `review-final-regression.trx`，受验源码为 `ec5e28699fda001868b68fa45a1507f81da67ddd`。本轮统计按上表重新执行的范围。

本轮冲突恢复样本 Job `7c886338-f989-4ad2-9c37-4bddad51043d`，唯一 ResumeReceipt `5b525605-d381-4e05-8b11-6d8a2ed2e219`。跨进程回读原 Job `47c48df6-20af-4122-830e-7d5c1c466c5e` 和回执 `23c0dc68-ea89-49a8-8724-f298d0037e76`。APP 决定 `869dbc94-c1ce-475c-aca9-3755b0176f7c` 与回执 `29794cd3-ad36-4107-afc6-da73f0c6b6f4` 同事务。OA 页面“已应用”和“待本地应用”的 Job 分别为 `3a555cfa-6d1a-4d83-8eaf-1e4dad0217fd`、`27e00b16-9b5d-4ef8-9de4-6ffed98b844c`。

生产提供方采用与生产业务 Owner 应用为 `UNPROVEN`。CONN-19、CONN-25 和 CONN-21 的失败恢复沿 WP12-18 核验。本次 PR 核销 CONN-22、23、24、26、27、28 六项，OA-CONN-01 原 AC 为 16/32，目标开放。

## 工时和交接

正式起点为 `2026-11-23T08:24:02.247475Z`。START 保存至 `2026-11-23T08:27:57.397434Z`，用时 **235.150 秒**。初版续做自 `2026-11-23T08:30:11.709003Z`，技术快照为 `2026-11-23T09:19:27.4856762Z`，两段技术工作共 **3190.927 秒**。初版草稿登记至 `2026-11-23T09:22:32.259050Z`，交接用时 **184.773 秒**，单列。

上轮修复于 `2026-11-23T09:40:51.3260840Z` 登记接续，技术证据截止 `2026-11-23T10:08:26.6845849Z`，用时 **1655.359 秒**。草稿回读到 `2026-11-23T10:11:12.2862997Z`，交接 **165.602 秒**，单列。

本轮晚到结果修复于 `2026-11-23T10:20:29.4963730Z` 登记接续，技术证据截止 `2026-11-23T10:40:34.4595149Z`，用时 **1204.963 秒**。该轮草稿回读至 `2026-11-23T10:42:50.6296432Z`，草稿交接 **136.170 秒**单列。WP12-17 三段交付的技术工作合计 **6051.249 秒**，各轮独立审阅等待已扣除，草稿交接按实另记。额度为 28800 有效秒，43200 秒强制拆卡，本次保持一张卡。

在采用台账与本卡候选差异中以原 DELIVERY 完整提交 `c31f0beb9661f0304d9ea9d8d9348a22c6fc88ec`、路径 `handoffs/CP6-DEV-20261012/tasks/WP12-11/messages/20261123T072226Z-exec-wp02-14b-delivery/MESSAGE.md` 查重。本卡在原 Main DONE PR 单次结转 WP12-11 的 475.670 秒。WP12-11 卡内由 3277.773 秒更新为 3753.443 秒，原目标复核 0.081 秒单列，计入本卡前计划累计 184156.206 秒。 [签收 #413](https://github.com/GTX537/CP6.AI-Handoff/blob/714ed5fb37448f19f90f7627af6af0ea80c4f517/handoffs/CP6-DEV-20261012/tasks/WP12-11/replies/20261123T075623Z-coord-acceptance-ack/MESSAGE.md)

正式收尾从 `2026-11-23T11:02:25.737612+00:00` 至核销快照 `2026-11-23T11:08:39.877947+00:00`。本卡入账 **6911.850 秒**，由技术 **6051.249 秒**、已测三次草稿交接 **486.545 秒**及正式收尾至快照 **374.056 秒**组成，目标复核 **0.084 秒**另列。计划累计 **191068.140 秒**。快照之后的采用、同树冒烟及席位释放按实际回执另记。

正式合并提交、远端回读、同树冒烟和 DEV-1 释放以唯一 Main PR 及后续 DELIVERY 回执为准。执行提交采用证据，Dot 核对转交，COORD 独立签收。普通 Actions 与部署记录为 `NOT_RUN`。 [Main PR #1316](https://github.com/GTX537/CP6/pull/1316)
