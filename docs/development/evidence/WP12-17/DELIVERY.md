# WP12-17 外部结果持久接收与本地流程/Owner 应用

本报告对应 CP6-EXEC-2 的同卡修复候选，等待 COORD 独立验收。Main 基线为 `42efc232798beff73a508883226241abfadd4e17`，本轮受验源码提交为 `ec5e28699fda001868b68fa45a1507f81da67ddd`，树为 `2202bcb3fb89fbf69a2607968af944cd0c2c08eb`。从固定的 Main DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018` 与 `codex/wp12-17` 继续原草稿，18 项严格前置均完整 DONE。 [正式派工 #414](https://github.com/GTX537/CP6.AI-Handoff/blob/f2fffc16d1b870bfb6c7feb577b66c02d4070841/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T081303Z-cp6-exec-2-wp12-17/MESSAGE.md) · [唯一 START](https://github.com/GTX537/CP6.AI-Handoff/blob/31219e240aec5d6e95ce1d0d092ff8e1c9582332/handoffs/CP6-DEV-20261012/tasks/WP12-17/messages/20261123T082552Z-cp6-exec-2-start/MESSAGE.md)

## 实现

- 精确提供方返回与原 Scope、Job/invocation、operation、effectKey、运输键、输入摘要和服务登记一致的成功原件。ResultInbox 保留原字节与摘要，同结果沿原件定位。异体、非法 schema 和受保护字段独立隔离。结果接收、隔离正文与 PostgreSQL 同为 1 MiB 上限。超限保留隔离原因与原摘要。白名单绑定原响应映射，十进制超精度直接拒绝。 [接收与应用](../../../../CP6.Core/Services/Oa/ConnectorResultApplicationService.cs) · [输出映射](../../../../CP6.Core/Services/Oa/ConnectorResultOutputV1.cs)
- 原事实先提交。冲突异体隔离后，正常 worker 继续沿已保存的首份有效原件处理。当前 worker 在 `WF_AUTH`、原 Job 租约、实例、token 和 pin 下推进，唯一 ResumeReceipt、Job、token 与 Outbox 同事务。旧租约能保留事实，已离开或撤回的 token 只记 `STALE_TOKEN`。已知原件的后续恢复从本地读取。 [worker 接入](../../../../CP6.Core/Services/Wf/WfServiceJobService.cs) · [PostgreSQL 前滚迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261123083000_OaConnectorResultsV1.cs)
- 原生 APP 继续采用 `oa.app/2.0` 与已冻结审批票据。精确 pin 的技术租约独立受限，进入 APP 终态时由同一 Tx D 写入决策证据、决定与 Outbox。OA 管理页分别显示外部结果、流程应用和独立 Owner 应用。待本地应用读值为 `HELD`，`RESULT_RECEIVED` 显示为“已接收结果”。Owner 回执从既有固定来源和连续链投影。 [APP Recorder](../../../../CP6.Core/Services/Oa/ApprovalTaskDecisionOwnerV2.ConnectorResume.cs) · [派发读入口](../../../../CP6.Core/Services/Oa/ConnectorVersionControlService.Dispatches.cs) · [OA 管理页](../../../../cp6.web/src/views/oa/admin/ConnectorControlPanel.vue)

## 本地验证

本机为 `LAPTOP-3QQ44FJS`。运行实际 Main HTTP、独立 hosted worker、Chrome 原 OA 页面和隔离 PostgreSQL 18.6。外部提供方为回环 HTTP 合同夹具，证据为 `SIMULATED`。Owner 场景经过 Main Tool Owner 的真实入口和原回执链，登记的证据模式为 `DEMO`，页面标 `SIMULATED`。

| 原 AC | 本地证据及边界 |
|---|---|
| CONN-21 | 已确认成功原件在独立 worker 中提交后结束进程，接管 worker 读回原件并推进，POST=1、GET=0。APP 应用回包丢失后读回同一 ResumeReceipt。同事务能力缺失时保留原件并待本地重试。TerminalFailure 和 FailureApplicationReceipt 留待 WP12-18 与本卡合验，整条待核销。 |
| CONN-22 | 用扫描入口的时钟推进触发到期回收。旧 worker 留下原件且推进为零，当前 worker 在正常扫描同一轮隔离异体并提交原件的唯一 ResumeReceipt 和对应 Outbox，实际应用为第 2 次尝试。POST 与 GET 使用同一组原键和摘要，分别共 1 次，其后正常扫描增量为零。 |
| CONN-23 | 从既有撤回入口撤回，迟到成功仍保存原件并显示 `STALE_TOKEN`，实例保持撤回，流程应用 Outbox 和补偿增量为零。 |
| CONN-24 | 额外 StarterId、精度超界、坏 schema、错服务登记与大于 1 MiB 的结果隔离。原 token、实例和业务字段保持原事实。 |
| CONN-26 | 省略 APP 决定 Outbox 后数据库拒绝提交，决定、token、变量、ResumeReceipt 与应用 Outbox 整体回滚。恢复写入唯一决定和应用回执，APP 证据载明 `SYSTEM_EXECUTION/RESUME`。 |
| CONN-27 | Main HTTP 与实际 OA 对照 `RESULT_RECEIVED`、`SUCCEEDED`、`HELD`、`APPLIED` 与 `UNPROVEN`。业务 Owner 独立证据缺失时仍显示未证实。 |
| CONN-28 | 第 2 条原回执先到返回 `PENDING_GAP`。接收固定链第 1 条后连续版本至 2，投影指向原第 2 条的实际 `BLOCKED`。填补缺口时外呼、Owner 应用和库存事务增量为零。 |

当前资格被撤销时，已验证结果留在 ResultInbox，恢复资格后只做本地应用。OA 浏览器检查“已接收结果”和“流程 待本地应用”文案，并核对持久接收、流程应用及业务 Owner 三轴。 [结果用例](../../../../CP6.Tests/Oa/ConnectorResultApplicationTests.cs) · [APP 和 Owner 用例](../../../../CP6.Tests/Oa/ConnectorResultAppTests.cs) · [浏览器脚本](../../../../cp6.web/scripts/oa-connector-control-browser.mjs)

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
| APP 审批命令 | 9108 | `f36eea65c214702f5e5b348968aab0127970633193a73a12ac67ebef3ee9daf7` |
| APP 投票 | 7851 | `e9884d91abf68f1f19a8a6fc3edcbd5a3a574a2704cf2546df194a82ab507712` |
| APP 转交命令 | 7795 | `76eb59accd2b76099c1462e57f6d452d3b65917d3c0a60566145acbce1b90cd4` |
| 派发准入 | 1707 | `d25627c875290756acb771e7cea730ac5c6d6429c8752b32b0ef9bec270392ef` |
| 派发观察 | 397 | `00507a7323916b22bd27419c9b3ce402e61e567757f3f5472b3cad0cd86cabee` |

基线 worker 复用 `D:/CP6-test-artifacts/WP12-11/20261123-wp1211/dotnet/bin/CP6.Tests/debug/CP6.Tests.dll`，SHA-256 为 `a9911268d03f2576e23262a370e1445f825498d6dab5a4285dc5a8b13b5962b2`。其已验证源码为 `0d8f006314883bc1e168b68416bb7ba771ecca10`，与 `42efc232798beff73a508883226241abfadd4e17` 的 .NET 源码无差异。程序集保留当时工作区基线的版本戳 `6e3d9d07ac92fb906027df1ecc6817755705a9d8`。

### 复测工件

在受验提交重新构建，.NET 构建退出 **0**，编译错误 **0**，保留依赖和编译警告。隔离 PostgreSQL 定向 **8/8**、实际 OA Chrome **1/1**、既有派发及 APP 决定、回执和恢复回归 **29/29**，均无跳过。`vue-tsc --build` 退出 **0**，定向 Vitest **3/3**。工件根目录为 `D:/CP6-test-artifacts/WP12-17/20261123-wp1217`。

| 工件 | 内容 | SHA-256 |
|---|---|---|
| `review-final-build.json` | 受验产品提交、树、程序集 SHA-256 与构建退出码 | `d66765b1b7e8663ec58f1b5aa3eb8d78de5759c7e84e1c0bf728de4e759a376e` |
| `review-final-build.log` | .NET 构建原日志 | `eb8f94dfc6fe32f4a0e29df52fd67bdd2375c62bd17a9860497d03efbc0452bf` |
| `review-final-results.trx` | 8 项定向 PostgreSQL（含冲突、边界、前滚）及 1 项实际 OA Chrome | `76cf4610a484326bb54db86eaadde9e7bb586604a6ca0d09a98511f576a48661` |
| `review-final-regression.trx` | 派发、APP 决定、Owner 回执与恢复回归 29 项 | `ce9bedaad79e603f1ab5e6fe9349517ceb07acfb02784faa05489a304d4964ef` |
| `permission-consumption-wp1217-review-final-results/control-browser/result.json` | 实际 OA 浏览器断言、Job 标识及请求证据 | `cf73d5ad4071ad85546654adcded3b7acab69884675d70ca990a0eff244cfc20` |
| `review-final-web-type.exit.txt` | vue-tsc 原退出码 | `3d79001cc6dcaa0eb3e14b71b76867a4e6a6188b4b22f7d2393384f9f0950774` |
| `review-final-web-unit.log` | 连接器 Vitest 3 项 | `070f60070d66c0855fce8a644360a4c4a433067659f7b9a376762d33a9289235` |

复测使用工件目录的 `run-pg.ps1`，指定 `-Build dotnet-review-final` 和新的 `-Run`，定向筛选 `FullyQualifiedName~OaConnResult|FullyQualifiedName~OaConn_browser` 并加 `-Browser`。前滚用例取 `CP6_WP1217_BASELINE_TEST_DLL` 指向上述历史构建。既有回归筛选 `FullyQualifiedName~OaConnDispatch_|FullyQualifiedName~App_decision_|FullyQualifiedName~App_receipt_|FullyQualifiedName~Receipt_recovery_`。

冲突恢复样本 Job `631419a6-e86d-46f5-967f-0447216c4c5d`，唯一 ResumeReceipt `0cd72ca1-8b57-488e-9118-3c9ece34fe7a`。跨进程回读原 Job `27f403d9-2f7a-4565-98e0-c36f2fba3e36` 和回执 `256c03d1-b5f1-4e71-ab93-cf0f2c537e7e`。APP 决定 `7a634e43-f68c-463b-ae4d-f54c2ed38762` 与回执 `e886a707-2230-424a-bb22-eb0660c1b971` 同事务。OA 页面“已应用”和“待本地应用”的 Job 分别为 `dbd0258b-a0e7-424c-81c8-829a27185376`、`3fc1cd52-2099-49be-a5fb-b8a2f6e37598`。

生产提供方采用与生产业务 Owner 应用为 `UNPROVEN`。CONN-19、CONN-25 和 CONN-21 的失败恢复沿 WP12-18 核验。已采用台账上的 OA-CONN-01 为 10/32，目标开放。

## 工时和交接

正式起点为 `2026-11-23T08:24:02.247475Z`。START 保存至 `2026-11-23T08:27:57.397434Z`，用时 **235.150 秒**。初版续做自 `2026-11-23T08:30:11.709003Z`，技术快照为 `2026-11-23T09:19:27.4856762Z`，两段技术工作共 **3190.927 秒**。初版草稿登记至 `2026-11-23T09:22:32.259050Z`，交接用时 **184.773 秒**，单列。

本轮同卡修复于 `2026-11-23T09:40:51.3260840Z` 登记接续，技术证据截止 `2026-11-23T10:08:26.6845849Z`，用时 **1655.359 秒**。WP12-17 两轮技术工作合计 **4846.286 秒**，另加已记录的初版交接。轮间待独立审阅的间隔已扣除。额度为 28800 有效秒，43200 秒强制拆卡，本次保持一张卡。

在采用台账与本卡候选差异中以原 DELIVERY 完整提交 `c31f0beb9661f0304d9ea9d8d9348a22c6fc88ec`、路径 `handoffs/CP6-DEV-20261012/tasks/WP12-11/messages/20261123T072226Z-exec-wp02-14b-delivery/MESSAGE.md` 查重。WP12-11 当前卡内为 3277.773 秒，475.670 秒留待正常 Main DONE 或拆卡 PR 单次结转。届时 WP12-11 卡内为 3753.443 秒，原目标复核 0.081 秒单列，计入本卡前计划累计 184156.206 秒。 [签收 #413](https://github.com/GTX537/CP6.AI-Handoff/blob/714ed5fb37448f19f90f7627af6af0ea80c4f517/handoffs/CP6-DEV-20261012/tasks/WP12-11/replies/20261123T075623Z-coord-acceptance-ack/MESSAGE.md)

Main DEV-1 由 CP6-EXEC-2 按 WP12-17 保持占用。执行提交实现与证据，COORD 独立验收，Dot 核对后转交。原分支和草稿供复审，Main 采用与席位释放待正式交付。普通 Actions 与部署记录为 `NOT_RUN`。 [草稿 PR #1316](https://github.com/GTX537/CP6/pull/1316)
