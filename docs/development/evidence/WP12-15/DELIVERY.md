# WP12-15 · 流程预演与 Cron 时间解释

本草稿交总控独立验收。以 Main a52330b4ef243146e51560ae40d664c9b99d376e 领取。直接前置 5 项，展开后完整前置 17 项。 [正式派工 #423](https://github.com/GTX537/CP6.AI-Handoff/blob/f438891212b38396b3b90d58faea8a954bc09078/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T165911Z-coord3-wp12-15-dispatch/MESSAGE.md)

## 交付范围

- OA 流程工作区可选准确发布版本或已保存的草稿修订。草稿显式固定依赖。按当前登录者作为模拟 Starter，保留原模拟变量的精度，记录 PostgreSQL knowledge cut。结果包含 EXPLANATION_ONLY、isExecutionPermit=false、条件 TRUE/FALSE/UNKNOWN、覆盖和未遍历位置。遇服务节点只给诊断，子流程沿准确依赖检查深度与环。 [预演 Owner](../../../../CP6.Core/Services/Wf/FlowAuthoringOwnerV2.Forecast.cs) · [图遍历](../../../../CP6.Core/Services/Wf/WorkflowForecastPlanner.cs) · [OA 页面](../../../../cp6.web/src/views/oa/designer/FlowSimulationPanelV2.vue)
- 人员分为 RESOLVED_AT_CUT、ARRIVAL_TIME、REDACTED。按现有定义、字段、租户和人员读权裁剪。导出重新请求 Main，在共同授权门内核对当前读权。OA 仅从本次成功响应生成下载，失权清私有内容，旧选择的晚到回包丢弃。 [Main HTTP 入口](../../../../CP6.WebApi/Controllers/Oa/FlowAuthoringController.cs)
- Cron 由触发器配置页进入工作日历，沿现有 Calendar.View。固定日历修订、租户时区、规则版本和起算 UTC，返回最多 5 个 UTC、当地时刻、偏移和 fold。缺失当地时刻跳过，回拨重复时刻按 UTC 返回两次。选择“仅工作日”时按固定修订筛选。缺租户时区或规则阻断，规则变化返回 412，搜索达到上限显式呈现。只读时间来源沿 WP12-11 已采用的租户日历和时区规则。 [Cron Owner](../../../../CP6.Core/Services/Oa/VersionedWorkCalendarV1.Cron.cs) · [WP12-11 证据](../WP12-11/DELIVERY.md)

## 四项原验收的候选证据

| 原项 | 本机实测 |
| --- | --- |
| CONN-15 | 实际 Main HTTP 回读旧发布 pin、原 FORM 与子流程版本，当前草稿单独识别。服务诊断停止在服务节点。注册合成连接器观测 0 次调用。预演前后的定义/发布预览计数及 Submission、Instance、Token、Task、FormData、通知、Job、连接器命令/准入和所核对的 Outbox 计数相同。 |
| CONN-16 | 实际 HTTP 返回并行分支、UNKNOWN 且不猜兜底，100 步退出并返回未遍历位置。准确子流程链跨深度 8 返回 PARTIAL 和 DEPENDENCY_DEPTH_EXCEEDED，缺 pin 保持缺失。图单元另核 TRUE/FALSE、循环和未知节点。 |
| CONN-17 | 实际 HTTP 核对已授权人选、缺变量时到达解析、现有 IAM 人员读权撤回时对人选的 REDACTED 投影。空列表保留人员状态。 |
| CONN-30 | 另一个合成租户收到 404。一次真实导出请求在数据库当前授权锁等待时提交撤权，返回 403，无 payload、摘要、人员或凭据信息。Chrome 用实际 Main 回包验证晚到预演失效，并检查重新核权后的下载。Vitest 覆盖旧身份迟到导出及拒绝清理。 |

Cron 在 America/New_York 的隔离租户重现场景。2027-11-07 的两个 01:30 分别为 05:30Z、06:30Z 和偏移 −240、−300。2027-03-14 的 02:30 缺失，首个可解释时点为 2027-03-15 06:30Z。比较原/新补班修订、未知日历、错误 cron、规则变化和缺时区。原禁用触发器的配置、NextDueUtc 和 LastFiredUtc 回读相同。 [Cron HTTP 用例](../../../../CP6.Tests/Erp/WorkflowCronExplanationPostgreSqlTests.cs) · [预演 HTTP 用例](../../../../CP6.Tests/Erp/WorkflowForecastPostgreSqlTests.cs) · [Chrome 场景](../../../../cp6.web/scripts/oa-simulation-browser.mjs)

## 受验提交和工件

受验源码 `2e9ec9ddd65e59f1163ab1cc3e9543fc00896e8a`，Git 树 `6bad32625f402b1570f073cf23b90e5c5c2eeb79`。LAPTOP-3QQ44FJS 使用隔离 PostgreSQL 18.6、合成租户、实际 Main HTTP 和 OA Chrome。.NET 构建和定向测试 **23/23**，其中 Chrome 1 项，零失败或跳过。Vue 类型检查通过，定向 Vitest **19/19**。按 #423 和仓库后续开发数据库裁定执行 PostgreSQL，新范围的 SQL Server 验收为 NOT_RUN。首轮 10 通过、1 项 Chrome 因合成日历菜单未配置路由停于导航，最终轮修正该夹具。

本机工件目录 C:/Users/tt/CP6-test-artifacts/WP12-15/20261123-wp1215-exec3。以下摘要可供独立对照。

| 工件 | SHA-256 |
| --- | --- |
| `wp1215-candidate.trx` | `93f2fe450105561b8aa3eba241ba5acaf8a922734a77b47e7d172194e6829785` |
| `wp1215-candidate.log` | `1b1a300593b4d0a8e5cd2126cd6b8ae06ad919eb4b4947b41a73be7d751f7448` |
| `permission-consumption-wp1215-candidate/simulation-browser/result.json` | `8e2bceb0a12a02c5f073d5f174c94cf0157e2bf71d4b6aac6a90617eff8652af` |
| `permission-consumption-wp1215-candidate/simulation-browser/workflow-forecast.png` | `c466a0a531b813a9d83c07f7b78717fcf70925f1991becfb1f442a0e97bdf559` |
| `permission-consumption-wp1215-candidate/simulation-browser/cron.png` | `7c330718cbcb34bb50fde3f30a76b232584b395d47460561c0274fab9fe03b3e` |
| `type-candidate.exit.txt` | `5feceb66ffc86f38d952786c6d696c79c2dbc239dd4e91b46729d73a27fb57e9` |
| `vitest-candidate.log` | `6858ab791e9ad081fecf9ac71a0acf6a69da13552c9b92e06d72454cba509885` |
| `source-bind.json` | `60e72dcc1a399184d61e2d5ba98d6c0639254769d4b7454254bb339c7583ab26` |
| `dotnet/bin/CP6.Tests/debug/CP6.Tests.dll` | `f384f7651f8964702673cccd448a7b690fdf28a864f3f32b4703495532092df6` |
| `dotnet/bin/CP6.WebApi/debug/CP6.WebApi.dll` | `b49027628ce9b63a12c0d7c9f036d9efa5ad1917d32e502aac561a9206accd9f` |
| `ledger-adopted-check.log` | `0f159c53e2f6586a09c81d072380061067efa7153ea389500022111c7997d86b` |
| `ledger-readback.json` | `c5aaadfeae23b0af5dca4c72a3d958a221806cc539bf890a9cf3b0dfa7d95806` |
| `previous-tail-dedup.json` | `c76f60006457c598fa01544e9b1f6ae48e3bde3812c658989cb79b21155d297a` |

## 台账及工时

Main WP12-15 为 PLANNED。CONN-15、16、17、30 保持 NOT_RUN 待独立验收，OA-CONN-01 原 AC 为 16/32。真实 TIMER 开通的运行采用、RP-CONN-01/02 和 WP12-14b 仍在原阻断链。草稿走原 DEV-1 和 codex/wp12-15，留待总控验收。 [采用台账](../../roadmap.json) · [原工作包](../../task-plans/WP12.md#wp12-15)

卡内起点 2026-11-23T17:06:25.896415Z。截至 2026-11-23T17:40:46.4069162+00:00 计 2060.511 有效秒。领取前核对 107.428 秒另列。额度 14400 秒，21600 秒强制拆卡。快照后提交和交接另计。

WP12-14 的 754.861 秒已按正式 DELIVERY 完整提交及路径核对。采用台账和执行计划均为零匹配，当前草稿没有入账。下一份合资格 Main 台账 PR 单次结转后，WP12-14 为 3274.331 秒，WP12-14b 余 25525.669 秒，计划累计在计入本卡前为 202648.852 秒。WP12-05 的既有结转已核实。 [WP12-14 原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/951c930a7e05ecd544a3e5ab03770798b7a52793/handoffs/CP6-DEV-20261012/tasks/WP12-14/messages/20261123T163520Z-cp6-exec-3-delivery-closeout/MESSAGE.md)
