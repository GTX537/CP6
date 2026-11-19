# WP02-21 后台作业版本、领取终态与恢复交付

**拆卡交付。** WP02-21 原号核销已验证范围。SYS-JOB-01 的 25 项原 AC 均逐项核对，10 项 PASS，剩余 15 项归 WP02-21b。该目标仍为 PLANNED，原运行修订目录没有为其指定必需运行修订。 [原卡](../../task-plans/WP02.md#wp02-21) · [余卡](../../task-plans/WP02.md#wp02-21b) · [原验收目录](../../assessments/20261011/catalogs/foundation.json)

执行人 `EXEC-WP02-14B` 从远端 `05517ee4968a8bfd1c39d4dda0a92269b5af8783` 在 Main `DEV-1` 领取 `codex/wp02-21`，工作区为 `D:/CP6-worktrees/wave17-dispatch-20261018`。主目录文件保留，Windows 使用 `git -c core.longpaths=true status`。派工原件 `4fbee6f2c16a6989d157e1ab1303da02705f53e3` 与登记采用 `2de6f6561055d5488e2501650027813f14f9ef34` 的消息字节一致。直接前置 WP02-08 和 WP02-10 均完整 DONE 且无余卡。Platform 实际远端是 `f3fbe4be67d40641f7a8a969d74be65a95541819`，包含 WP02-08 的 `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`；CP6 实际基线包含 WP02-10 的 `ab3c7da128d1f33fabb34b92f5c1ea9ffd532116`。 [派工 #323](https://github.com/GTX537/CP6.AI-Handoff/pull/323) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/148952e426c0f41e48702b58a1e2671548a95f0d/handoffs/CP6-DEV-20261012/tasks/WP02-21/messages/20261119T040146Z-exec-wp02-14b-start/MESSAGE.md) · [Platform #71](https://github.com/GTX537/CP6.Platform/pull/71) · [CP6 #1238](https://github.com/GTX537/CP6/pull/1238)

## 实际作业、竞争与恢复

| 入口 | 本轮核对到的结果 |
| --- | --- |
| 原生 OA APP／SFS 的建单、具体截止及责任 | 流程发布版本、节点动作、截止计算的 UTC 时长和原时区在接受任务时确定。实测同一流程后续发布及租户换时区均不重写原截止。工作日日历参与的版本化调度另列在 AC-JB03-03。实际服务身份可执行获准的系统动作，APP 人工决定仍需人工证据。 |
| 原生 Owner 领取和最终事务 | 使用两条数据库连接验证 worker 并发只有一个原 task／revision／occurrence 槽。人工与 worker 持同一最终门，只产生一个合法终态和对应一份业务结果。深入到最后写入时的租约过期、过代、当前服务授权与升级目标撤销均拒绝新效果。必要回执、变化和通知 Outbox 同事务。 |
| 暂停、未知与原结果 | 暂停后的领取保留原 execution 和截止，恢复时重新核准并使用现有槽。真实数据库已提交后模拟确认丢失及取消，独立新连接读回同一结果；结果和通知各一份。取消后的旧尝试不产生第二份业务效果。 |
| 旧超时和服务任务 | 重新登记与实际宿主同摘要的模拟 C02 来源后，旧超时升级／驳回和暂停的四个提交顺序、最后时钟到期回滚、日志服务作业的两种暂停顺序均通过。 实际原 error-edge 任务延迟本地模拟连接器，在同一 job 身份下保原动作。旧错误边业务、现代 Owner 与注册态分别评价。 |
| 保留与分片 | 真库核对 WFS 的 180 天、零禁用、边界截止、占坑和其他租户。第二个分片受隔离故障影响，独立连接仍读到首批 500 行已提交及第二批 1 行保留；下轮只删除剩余行。 审计 Owner 实测生效版本、引用／hold 阻断、过期计划拒绝和真实提交确认丢失回查。WMS 现场范围使用模拟本地库，真实宿主 Worker 按 RetainUntil 清理一条已到期扫描，保留一条未到期扫描和原业务事件。 |
| 只读监控 | 真实 Generic Host 启动已配置的 NativeTimeoutWorkerV2，经真 PostgreSQL 持久结果、Main HTTP 与 Chrome 访问 OA 实际页面。详情显示原策略、最近扫描和原执行结果，无执行按钮，刷新返回同一回执。未获当前范围不得读取，记录 HTTP、真实元数据与浏览器结果。 |

原生产品入口的固定来源 `b04676667446c42c943eb2e76e88f554deb0743c` 经 CP6 #719 的 `32fcf2767435658b2f1aa362b9a84e640291f79b` 采用。旧超时源 `427c38c46119d5957598270a15fcea889c98ca13` 经 CP6 #1165 的 `4df0a3a4253eb05a22af259e14ca9817ef24857d` 采用。审计源 `1979923c4e9d4de6ffe10fe2c590a29df06f42e3` 经 #1248 采用。上述源均已核对在本轮远端主线；CRM 法定保留的原版本证据沿 WP03-04 对应 CRM 基线采用。本卡的 CP6 产品差异为空。 [原生超时 W283](../W283-01/DELIVERY.md) · [旧超时 W640](../W640-01/DELIVERY.md) · [审计保留 WP02-13b](../WP02-13b/DELIVERY.md) · [CRM 保留 WP03-04](../WP03-04/DELIVERY.md)

## SYS-JOB-01 逐项核对

| 原 AC | 结果 | 通过证据或实际剩余出口 |
| --- | --- | --- |
| AC-JB01-01 | PASS | 实际原生超时使用建单时的发布版本、节点参数、时区与 UTC 截止。后续发布、租户换时区均不改原结果。SFS 系统决定保有具名服务身份，APP 必须实际人工证据。 |
| AC-JB01-02 | PASS | 两连接的 worker 只取得一个原槽，人工与 worker 同时进入决定点仅一个合法终态；声明提醒不产生人工票。 |
| AC-JB01-03 | PASS | 暂停、当前服务撤权及升级目标撤权均经最终门。按已批准来源复验两种旧超时与人工暂停提交顺序。 |
| AC-JB01-04 | 部分 | 原生详情可读待处理、最近扫描和最近原结果，旧入口返回已处理数量。缺完整 candidate／已终态／阻断／未知的核对视图。 |
| AC-JB01-05 | PASS | 原生按 execution、截止版本与 occurrence 回读原回执。挂起恢复保留原槽，丢确认后独立作用域回读同一结果，截止变更不盲执行。 |
| AC-JB02-01 | PASS | 核对 WFS 终态的 CompletedAtUtc、触发流水 FiredUtc、默认 180 天及零关闭；实际 WMS raw 使用 RetainUntil，审计按本域现行策略。CRM 原日历月沿已采用的 WP03-04。 |
| AC-JB02-02 | 部分 | 真库核验旧 WFS 占坑／在途、审计 UNKNOWN 和引用排除。WFS／WMS 对引用全集和所有适用 hold 的共同门仍待完整采用。 |
| AC-JB02-03 | 部分 | 实际审计保留计划对新 hold、引用和政策换版有效。WFS／WMS 的当前 Owner hold 与过期预览联合门待补。 |
| AC-JB02-04 | 部分 | 旧 WFS 首片 500 行实际提交，第二片失败留下 1 行。审计 Worker 真库确认丢失可读回原回执，旧 WFS 清理缺持久原计划、分片回执和原游标。 |
| AC-JB02-05 | 部分 | 现行 WMS Worker 在隔离库实际只删过期 raw scan，留存业务事件和未到期行。未批准来源库及 demo 配置的实际阻断待核对。 |
| AC-JB03-01 | 部分 | 源码、实际宿主注册、配置、真实扫描、Owner 结果分别取证。现有实际页面仅覆盖 OA 原生运行及最后结果，仍缺完整五层诊断输出。 |
| AC-JB03-02 | PASS | 宿主配置、租户、实际登记来源、当前 Owner writer gate 或必要原回执不可用时无新效果。 |
| AC-JB03-03 | 部分 | 现行固定周期和历史重启入口分别追踪。缺受保护的持久 schedule 版本、修改审核及各 Owner 的错过运行策略联验。 |
| AC-JB03-04 | 部分 | 关闭配置、暂停租户和当前取消的部分边界已核对。统一为各 Owner 保留在途已提交、未决或阻断的持久状态仍待采用。 |
| AC-JB03-05 | PASS | 原生同一 task／revision／occurrence 的稳定 execution 及实际双 worker 唯一领取。旧 WFS 同一 token／节点原作业沿持久唯一槽，重扫不造第二个 Owner。 |
| AC-JB04-01 | 部分 | 原生区分 execution、claim／epoch、业务 command 与通知。现行入口缺完整可追溯 run／attempt／message／operation 的诊断关联。 |
| AC-JB04-02 | 部分 | 浏览器按真实 Owner 回执展示完成，旧分片后续失败未报整体成功。缺所有适用 Owner 的部分、确认零变更和 UNKNOWN 分类汇总。 |
| AC-JB04-03 | PASS | 真实 PostgreSQL 在提交完成后模拟确认丢失和取消。独立新连接用原 execution 回读原结果和同一通知，旧 attempt 无重复效果。 |
| AC-JB04-04 | PASS | 真实 Generic Host、Main HTTP 和 Chrome 验证当前范围、只读面板和刷新原结果。普通扫描日志独立，当前授权的详情仅见允许的诊断字段。 |
| AC-JB04-05 | 部分 | 读回原结果保持原业务事实且页面刷新一致。缺通用的原未决运行加上迟到回执的持久追加观察与时间线。 |
| AC-JB05-01 | 部分 | 原生已经区分失效、拒绝和提交 UNKNOWN，也验证缺 writer gate 无效果。旧作业完整失败分类、有限重试与隔离策略待核查。 |
| AC-JB05-02 | 部分 | 现行原生和审计恢复使用原 operation 读取，缺全范围已声明 typed 恢复动作、不变载荷和适配证据。 |
| AC-JB05-03 | PASS | 过期或旧代次领取均拒绝，最后校验处到期使任务、回执和通知一起回滚。真实提交后取消保原结果，再查询没有第二份效果。 |
| AC-JB05-04 | 部分 | 本次覆盖同一 Main PostgreSQL 的共同门与当前历史查看。跨库共同权威不可证明时关闭新效果及继续历史读取仍需实际联验。 |
| AC-JB05-05 | NOT_RUN | 本轮作业夹具未涉及独立用途 grant。真实无可信终态时保留授权占用、并独立撤销读取的作业链由余卡覆盖。 |

未完整验收的项目继续保有原台账状态。WP02-21b 沿各应用原 Owner 补足诊断、保留、调度和跨库安全恢复，继续使用原定义中规定的用途和原业务键。 [设计](../../../design-handbook/modules/SYS-JOB-01.md) · [唯一台账](../../roadmap.json)

## 运行证据

Windows、.NET SDK 8.0.424、PostgreSQL 18.6、隔离模拟业务和受信任宿主登记。本地节点或服务时钟由指定夹具注入时单独标注。浏览器从受测提交导出 Web 源，依赖锁摘要 `abc239f2460634d4e9417193c500db55959c0022337275be12daa61f8d92dbf7` 与已有缓存相同。Chrome、Vite、Main HTTP 和工作进程均实际运行，内部 API 使用真服务。报告只描述隔离运行，生产运行、外部归档和未批准源库暂无证据。

| TRX 与过滤 | 结果 |
| --- | --- |
| `native-timeout.trx`、`native-filter.txt` | 17 PASS。截止事实、人工与 worker、双 worker、持久原结果、现行授权和租约。 |
| `browser-readonly.trx`、`browser-filter.txt` | 3 PASS，含实际 Chrome 只读路径和当前访问范围。 |
| `audit-job.trx`、`audit-filter.txt` | 3 PASS。原保留读取、引用／hold 和真实回执。 |
| `legacy-job.trx`、`legacy-filter.txt` | 2 项早期通过。7 项首次失败是已批准的模拟 C02 来源登记摘要与现行宿主选择的保留合同摘要不一致，旧用例准备阶段被拒。 |
| `job-supplement.trx`、`supplement-filter.txt` | 13 PASS。补充真库和现行摘要后重跑旧超时及作业，明细以 TRX 为准。夹具通过 `acceptance.targets` 从私有目录临时包含，产品保留现行正式鉴权和合同。 |

共 37 项不同的当前 PostgreSQL、真实宿主和浏览器测试通过。重新执行旧作业八项与补充的五项共 13 项，均通过且零跳过。 广选的历史组件另有 13 项内存通过、9 项旧 SQLite 夹具缺 `Latin1_General_100_BIN2`、尚未进入产品逻辑，均保留原记录且不计入本轮真库结果。原程序集来自与当前仅有文档差异的 WP02-19；补证重新在同一受测主线构建。范围内只验证 PostgreSQL，保留基线编译警告。原始命令、日志、测试夹具及浏览器证据在 `D:/CP6-test-artifacts/WP02-21/`。连接凭证及模拟数据不进 Git。

## #322 解阻与工时结转

同一 PR 依已采用的 Handoff #322 将 WP02-19b 从 BLOCKED 改为 PLANNED，指派留空，原七项继续 NOT_RUN，WP02-20 的完整前置继续展开余卡。WP02-19 的候选 1,133.352 秒加官方收尾 378.458 秒，父卡合计 1,511.810 秒。此次在累计工时只加收尾一次。余卡预算校准为 12,888.190 秒，150% 升级线为 19,332.285 秒。阶段 B 用完整前置重新计算最长链。本轮业务执行范围是 WP02-21。 [#322 决定](https://github.com/GTX537/CP6.AI-Handoff/blob/427833ae8b761336fb5fb9f3dce2ba502b0aec71/handoffs/CP6-DEV-20261012/tasks/WP02-19/replies/20261119T035004Z-coord-decision-wp02-19b/MESSAGE.md) · [#322 签收](https://github.com/GTX537/CP6.AI-Handoff/blob/427833ae8b761336fb5fb9f3dce2ba502b0aec71/handoffs/CP6-DEV-20261012/tasks/WP02-19/replies/20261119T035004Z-coord-split-ack/MESSAGE.md)

## 工时与收尾

本卡从 `2026-11-18T23:01:11.723707-05:00` 至候选 `2026-11-18T23:18:51.911322-05:00`，有效 1060.188 秒。余卡预算 13339.812 秒。4 小时原预算、6 小时强制拆卡点均未到，按照实际剩余范围拆分。同一 PR 采用台账、原卡余项、单卡计划及四份状态页。合并后冒烟、分支处置、工作区和席位释放见官方 `finish` 与原通信分支回读。 [单卡计划](../../planning/20261117-card-execution.md)
