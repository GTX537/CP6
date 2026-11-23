# WP12-17 外部结果持久接收与本地流程/Owner 应用

本报告对应 CP6-EXEC-2 的候选，等待 COORD 独立审阅和验收。Main 基线为 `42efc232798beff73a508883226241abfadd4e17`，产品与测试提交为 `869fd0858f6c2b47f19a1ccb36a23b100ee2fd8e`，受验产品树为 `643a00a232d4eb5c024c3ac276d3afc2303ac839`。从固定的 Main DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018` 和 `codex/wp12-17` 继续，18 项严格前置均完整 DONE。 [正式派工 #414](https://github.com/GTX537/CP6.AI-Handoff/blob/f2fffc16d1b870bfb6c7feb577b66c02d4070841/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T081303Z-cp6-exec-2-wp12-17/MESSAGE.md) · [唯一 START](https://github.com/GTX537/CP6.AI-Handoff/blob/31219e240aec5d6e95ce1d0d092ff8e1c9582332/handoffs/CP6-DEV-20261012/tasks/WP12-17/messages/20261123T082552Z-cp6-exec-2-start/MESSAGE.md)

## 实现

- 已登记的精确提供方须返回与原 Scope、Job/invocation、operation、effectKey、运输键、输入摘要和服务登记一致的成功原件。ResultInbox 保留原字节和摘要。同结果沿原件定位，异体、非法 schema 与受保护字段进入独立隔离记录。白名单绑定原响应映射，十进制超精度直接拒绝。 [接收与应用](../../../../CP6.Core/Services/Oa/ConnectorResultApplicationService.cs) · [输出映射](../../../../CP6.Core/Services/Oa/ConnectorResultOutputV1.cs)
- 原事实先提交。当前 worker 在 `WF_AUTH`、原 Job 租约、实例、token 和 pin 下推进流程，唯一 ResumeReceipt、Job、token 与 Outbox 同事务。旧租约能保留事实，已离开或撤回的 token 只记 `STALE_TOKEN`。已知原结果的恢复只进入本地应用。 [worker 接入](../../../../CP6.Core/Services/Wf/WfServiceJobService.cs) · [PostgreSQL 前滚迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261123083000_OaConnectorResultsV1.cs)
- 原生 APP 继续采用 `oa.app/2.0` 和已冻结的审批票据。精确 pin 的技术租约独立受限，进入 APP 终态时由同一 Tx D 写入决策证据、决定与 Outbox。OA 管理页分别显示外部结果、流程回执和独立 Owner 应用。Owner 回执从既有固定来源和连续链投影。 [APP Recorder](../../../../CP6.Core/Services/Oa/ApprovalTaskDecisionOwnerV2.ConnectorResume.cs) · [派发读入口](../../../../CP6.Core/Services/Oa/ConnectorVersionControlService.Dispatches.cs) · [OA 管理页](../../../../cp6.web/src/views/oa/admin/ConnectorControlPanel.vue)

## 本地验收

本机为 `LAPTOP-3QQ44FJS`。运行实际 Main HTTP、独立 hosted worker、Chrome 原 OA 页面和独立 PostgreSQL 18.6。提供方为回环 HTTP 合同夹具，标 `SIMULATED`。Owner 场景经过 Main Tool Owner 的真实入口和原回执链，登记的证据模式为 `DEMO`，页面标 `SIMULATED`。

| 原 AC | 本地证据及边界 |
|---|---|
| CONN-21 | 已确认成功原件在独立 worker 中提交后结束进程，接管 worker 读原件并推进，POST=1、GET=0。APP 应用回包丢失后读回同一 ResumeReceipt。同事务能力缺失时保留原件并待本地重试。TerminalFailure 和 FailureApplicationReceipt 留待 WP12-18 与本卡合验，整条待核销。 |
| CONN-22 | 旧租约回包留下原件，当前租约接管期间旧 worker 推进为零。后到异体隔离，最终由当前租约提交唯一回执，第二次以后原业务外呼为零。 |
| CONN-23 | 从既有撤回入口撤回，迟到成功仍保存原件并显示 `STALE_TOKEN`，实例保持撤回，流程应用 Outbox 和补偿增量为零。 |
| CONN-24 | 原 payload 带额外 StarterId、精度超界、坏 schema 或错服务登记时隔离，实例、原 token 和业务字段保持原事实。 |
| CONN-26 | 省略 APP 决定 Outbox 后数据库拒绝提交，决定、token、变量、ResumeReceipt 与应用 Outbox 整体回滚。正常恢复写入唯一决定和应用回执，APP 证据载明 `SYSTEM_EXECUTION/RESUME`。 |
| CONN-27 | 实际 Main/OA 读取 `SUCCEEDED`、`APPLIED` 与 `UNPROVEN` 三轴。业务 Owner 独立证据缺失时保持未证实。 |
| CONN-28 | 第 2 条原回执先到返回 `PENDING_GAP`。接收固定链第 1 条后连续版本至 2，投影指向原第 2 条的实际 `BLOCKED`。新增外呼、Owner 应用和库存事务为零。 |

当前授权撤回的场景在提供方完成之后阻断本地应用，原件留在 ResultInbox。恢复当前资格后只做本地应用。正反用例与实际浏览器脚本可按原入口重跑。 [结果用例](../../../../CP6.Tests/Oa/ConnectorResultApplicationTests.cs) · [APP 和 Owner 用例](../../../../CP6.Tests/Oa/ConnectorResultAppTests.cs) · [浏览器脚本](../../../../cp6.web/scripts/oa-connector-control-browser.mjs)

最终构建 `build-app-v6.log` 为 0 编译错误。最终定向 PostgreSQL 场景 **6/6**，实际 OA Chrome **1/1**。相同入口的前一构建另有 **29/29** 项既有回归通过，合并统计时去重。前端类型检查通过，定向 Vitest **3/3**。工件根目录为 `D:/CP6-test-artifacts/WP12-17/20261123-wp1217`。

| 工件 | 内容 | SHA-256 |
|---|---|---|
| `app-v6.trx` | APP Tx D 回滚、原提交回读与 Owner 连续链 1 项 | `a8b9a1f00b4610a3ed44249e286e41d575a6acdd9b35f82a35824be2f74af19c` |
| `candidate-final.trx` | 最终构建其余 5 项定向 PostgreSQL 与 1 项 Chrome | `c1d46d4759e711a14382a8fd006b82d26ffa9c94fde23e609aa131e38ee5857f` |
| `candidate-regression.trx` | 前一构建 35 项，其中 29 项为既有回归，6 项由最终构建重跑 | `4d765ccb19c86e97dbab4b44c067e1ce0f351635534fe2edb2fdb99748b9efe5` |
| `permission-consumption-wp1217-candidate-final/control-browser/result.json` | 实际 OA 浏览器，结果 Job `eec87f8a-907f-4d2d-beed-172a3360fc65` | `95b0b7dceabdde1f0ab089bffc100cdf40e0f6621a0b7966ed56b0671fcb0db1` |
| `web-test-final.log` | 连接器 Vitest 3 项 | `667714e6819d0ce94d5759e885aee8e961c90411c131669408ae85b9f86b9979` |

本机可用工件目录的 `run-pg.ps1` 复测。用 `-Build dotnet-app` 指向本卡构建，以新 `-Run` 名称启动隔离库。定向筛选为 `FullyQualifiedName~OaConnResult`，实际 OA 浏览器用 `-Browser` 与 `FullyQualifiedName~OaConn_browser`。成功证据绑定同一产品树，报告提交仅补本文件。

跨进程样本保留 45 字节原结果，Job `c3f05932-ecbc-4b17-a874-a7671a8ef596`，ResumeReceipt `c86b95d8-683d-4de6-b474-69f43bec0848`。APP 样本决策 `89e1b7e4-00ff-4bd0-9f1e-98c94b42a66b` 与 ResumeReceipt `80f2baf5-c6b7-4712-9bca-d23d9b653e01` 同事务。

生产提供方采用和生产业务 Owner 应用保持 `UNPROVEN`。CONN-19、CONN-25 及 CONN-21 的失败恢复部分沿 WP12-18 核验。OA-CONN-01 在已采用台账仍为 10/32，目标开放。

## 工时和交接

正式起点为 `2026-11-23T08:24:02.247475Z`。到 START 保存释放的实际工作区间止于 `2026-11-23T08:27:57.397434Z`，**235.150 秒**。续做登记于 `2026-11-23T08:30:11.709003Z`，候选技术复核快照为 `2026-11-23T09:19:27.4856762Z`。两段技术工作合计 **3190.927 秒**，两轮之间的等待单列扣除。额度 28800 有效秒，43200 秒强制拆卡，本次候选未拆分。候选提交后的独立验收、采用和席位收尾另按实际记录。

在采用台账与本卡候选差异中以原 DELIVERY 完整提交 `c31f0beb9661f0304d9ea9d8d9348a22c6fc88ec`、路径 `handoffs/CP6-DEV-20261012/tasks/WP12-11/messages/20261123T072226Z-exec-wp02-14b-delivery/MESSAGE.md` 查重。WP12-11 当前卡内为 3277.773 秒，475.670 秒尚待在正常 Main DONE 或拆卡 PR 单次结转。届时 WP12-11 卡内为 3753.443 秒，原目标复核 0.081 秒单列，计入本卡前计划累计 184156.206 秒。此候选仅记录核对结果，采用台账随正式处置办理。 [签收 #413](https://github.com/GTX537/CP6.AI-Handoff/blob/714ed5fb37448f19f90f7627af6af0ea80c4f517/handoffs/CP6-DEV-20261012/tasks/WP12-11/replies/20261123T075623Z-coord-acceptance-ack/MESSAGE.md)

Main DEV-1 按 WP12-17 保持占用，任务分支供独立审阅，远端采用与正式释放待最终交付。Handoff DEV-2 可复用。普通 Actions 和部署在本轮为 `NOT_RUN`。
