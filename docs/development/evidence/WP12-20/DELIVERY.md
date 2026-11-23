# WP12-20 通知偏好、投递和已读水位候选

Main 基线 `f0739befada77bcd98b56d448a8f89ed6564be7e`，受验源码 `ede9289d250303b6b665ffb6ef79a30b71eb90f9`，源码树 `cc00bd66f34470b2f7163a7b74037f8afcd5965e`。WP12-20 在 `LAPTOP-3QQ44FJS` 的原 DEV-1 实施。七项直接前置及 30 项展开前置均完整 DONE。由总控独立复核本候选，现行 Main 仍为 `PLANNED`，OA-TASK-01 保持 **25/46**。 [派工 #426](https://github.com/GTX537/CP6.AI-Handoff/blob/823dae77832fba814c2846819f74d89ae8b6e800/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T200227Z-coord3-wp12-20-dispatch/MESSAGE.md) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/5a93d04890eec1cd361a813156c0ffbdf24b1929/handoffs/CP6-DEV-20261012/tasks/WP12-20/messages/20261123T201443Z-cp6-exec-3-start/MESSAGE.md)

## 候选范围与证据

原 `OA-TASK-01-DEVELOPER-SPEC-R2` SHA-256 `9f9920ffe400ba982834f2d02276535e5a5654ae6b64236fd366247418084b8d`，第 174–197 行。以下为候选实测边界，原 AC 状态仍为 `NOT_RUN`。 [设计手册](../../../design-handbook/modules/OA-TASK-01.md)

| 原 AC | 候选验证边界 |
|---|---|
| AC-T06-01 | 实际 HTTP 回读旧扁平和矩阵偏好，保留 `rowMode` 和 `pageSize`。`notify:null` 恢复默认。旧坏 JSON 只读标为 `LegacyInvalidDefaulted`，零修改。 |
| AC-T06-02 | 矩阵由后端类型和 producer 支持生成。`timeout` 两格不可用，真实原生合成 remind 仍写 `TodoCreated` 事实。 |
| AC-T06-03 | 当前本人和 `oa-settings:edit` 下使用原 WFC 命令及版本 CAS。同一版本两个请求只出一条回执，原重放可读，改同键正文 409，旧版本 412。非法结构、布尔、通道、JSON 和超限零新增回执。 |
| AC-T06-04 | 实际 FORM 新建及审批、APP 转交以原 Owner 事务写通知、Outbox 和当时偏好版本。事务内故障一起回滚，同命令并发与重放保留原事实。投递失败独立于已完成业务决定。 |
| AC-T06-05 | 站内持久与模拟邮件按 `notificationId/channel/recipientVersion` 独立。无地址 `BLOCKED_ADDRESS`，已知未接收 `RETRYABLE_FAILED`，含不确定尝试 `UNKNOWN`。可查询的模拟 provider 同原键恢复，未知且缺查询时保持待处置。已接收通道无重复发送。真实 provider、正式 Required 策略和人工运维重送待采用。 |
| AC-T06-06 | 派送前复核当前源、可信租户与环境、本人收件资格、有效账号、通道选择及联系方式。偏好关闭、当前读权撤销后可选投递抑制。已发生的审批和已确认通道记录保持。正式 Required 待采用。 |
| AC-T06-07 | 稳定分页和水位绑定当前本人、租户与环境，晚提交的回填通知保留未读。单条和水位读写幂等，原 `ReadAt` 保持。旧 bodyless `read-all` 使用既有 GET 列表返回的观察 Cookie。 |
| AC-T06-08 | 两个合成租户、明确代理资格及撤权以实际 Main HTTP 与 Chrome 核对。收件按实际用户，历史详情只给最小摘要。链接复核当前 FORM/APP 任务。在 Chrome 实测 FORM 从收件箱草稿页正确打开当前任务，失权链接清空缓存并退出该任务。APP 同事务迁移由原 Owner 定向用例覆盖，APP 深链未单独跑浏览器。代理不读取 grantor 私人收件列表。 |

新增 PostgreSQL 前向迁移沿已交付的 FORM 与 APP Outbox 读取已完成来源，业务事件和原审批仍使用原 Owner。站内 SignalR 仅按可信租户、环境和本人发送刷新提示。缺来源或环境证明的旧通知只保留本人历史，不重新投递。现有邮件 sender 合同缺原键和查询能力，候选没有注册真实邮件适配器。 [同卡 QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/330504be45ba928527adc0dc503dfc698d3c18b2/handoffs/CP6-DEV-20261012/tasks/WP12-20/messages/20261123T202654Z-cp6-exec-3-question/MESSAGE.md)

### 数据库与原命令证据

- 偏好并发复用同一预期版本，HTTP 200 和 412 各 1 次；重放读回同一回执，异载荷为 409。非法或超限请求后回执仍为 1 条，明确恢复默认后共 2 条，旧坏 JSON 保持原字节。Chrome 实际保存的 commandId 与响应一致，原件留有合成 commandId。
- FORM 首次成功有 2 条通知，每条测试中被核对的通知对应 1 条 Owner 来源。原事务失败没有遗留目标通知或 Outbox。受验通道共有 2 次既定站内刷新，此后同一邮件键 2 次尝试和 1 次原键查询，站内刷新仍为 2。邮件结果从确认未接收转为未知，再由合成原键查询确认接受。任务在尝试期间仍待办。
- FORM 完成原命令并发返回同一结果，终态通知对应 1 条来源；模拟邮件未知时审批仍为 Approved、决定表决记录为 1，缺查询的模拟 provider 只收 1 次新发送，0 次查询。APP 原转办重放回执逐字节相同，对应接收人的来源只有 1 条。
- 地址缺失或随后消失保持 BLOCKED_ADDRESS；第三组明确撤销读权后两选用通道均 SUPPRESSED。该组共 3 个审批均仍 Running。站内列表保留已持久的发生时通知摘要，抑制发生在尚未发出的当前通道派送。
- 观察水位首次更新 2，重放更新 0，已有 ReadAt 原值不变。观察后新增以及在观察时尚未提交的通知仍未读，另一合成租户的水位被拒绝。

## 本机验证

- 隔离 PostgreSQL 18.6，实际 Kestrel、Chrome 和合成用户。定向 .NET **63/63**。实验的站内与邮件结果、业务审批、首次已读分开记。SQL Server `NOT_RUN`，本机服务存在，采用的新 Scope 和迁移依赖 PostgreSQL。未启用实际邮件、短信或 Slack 投递。
- Vue 类型检查 通过，遗留矩阵 Vitest 7/7。旧 SQLite 通知 Outbox 两项在当前扩展试跑及 Main 同源旧程序集均于建表阶段同样失败，缺 `Latin1_General_100_BIN2`，另存对照原件。这两项与实际 PostgreSQL 事务证据分列。
- 范围、差异和生成台账结构检查 通过。Main 无记账变更。WP12-15 尾差 **714.019 秒**已按原 DELIVERY 完整提交和路径查重，待下一资格 Main 台账单次结转。WP12-14 尾差已入 Main。 [WP12-15 原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/37c1eb25397baa8f8fe51cf75e870f23f3c045be/handoffs/CP6-DEV-20261012/tasks/WP12-15/messages/20261123T194905Z-cp6-exec-3-delivery-closeout/MESSAGE.md)

原件目录 `C:/Users/tt/CP6-test-artifacts/WP12-20/20261123-wp1220-exec3`。采用 `PostgreSQL 18.6` 本机环回和独立目录。按 `restore.log` 绑定依赖，复跑命令及输入与 `source-bind.json` 同目录。关键摘要如下。

| 原件 | SHA-256 |
|---|---|
| `wp1220-final.trx` | `8a6df660b0e0f4e677003df059bd3213a73b707d678665b031ca612a6ce943a3` |
| `permission-consumption-wp1220-final/workflow-notification-browser/result.json` | `e2de8c6ff60eabac64ebfa9b10269c80af9d8d7758b433bcf1bfcfe2edaca826` |
| `wp1220-baseline-legacy.trx` | `bddbec8dd121689375cf3e864716686857906ab2341367a15c695a8e7db27458` |
| `roadmap-structure.json` | `d887ac74bc59034d4c9c72b9e036ead7bfafc655b022293ba890f683a2324f5c` |

## 工时与留存

领取 `2026-11-23T20:12:56.412384+00:00` 至候选快照 `2026-11-23T21:00:27.7466206+00:00`，有效 **2851.334 秒**，领取前核对 **316.775 秒**另列。M 卡额度 14400 秒，21600 秒停在安全点拆分。原 DEV-1 留给 WP12-20 继续独立复核，DEV-2 通信后释放。指定技能未在本机提供，本次使用 CP6 规程。
