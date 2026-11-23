# WP12-20 通知偏好、投递和已读水位修订候选

Main 为 f0739befada77bcd98b56d448a8f89ed6564be7e，修订前 Draft 为 6144f3352bf1a57d61627848fd4391005291a4a3，本轮受验源码为 b69f2e9a8a27cfb098aa3d19bb2449defa70279b，源码树为 4c042fae3ed983450799d28874c99445a9d774b0。原 DEV-1 的 WP12-20 交同一 Draft #1320 独立复验。 [正式 REVISE #427](https://github.com/GTX537/CP6.AI-Handoff/blob/a3a565ebd51c29da13f32fe6fa0b2419bf5deb7b/handoffs/CP6-DEV-20261012/tasks/WP12-20/replies/20261123T211202Z-coord3-candidate-review-r1/MESSAGE.md) · [首版原证据](https://github.com/GTX537/CP6/blob/6144f3352bf1a57d61627848fd4391005291a4a3/docs/development/evidence/WP12-20/DELIVERY.md)

## 本轮修订

- **现有站内投递。** 期限重算沿原日历 Owner、截止版本、原命令和已提交回执绑定可信租户、环境、任务和通知。站内保留“待办期限已重算”，点击复核原任务权限。旧通用引擎通知的新写入只在原事件键、写入时已登记环境及提交后的真实资源匹配时入队。消费仍走现有后台生命周期及逐租户 Scope，SignalR 只发送本人、租户、环境绑定的刷新提示。
- **历史及重试。** 可证明来源、从未尝试且已到期的站内记录走当前接收资格及偏好，并以同一通知通道键持久记录一次。保留原已知站内完成，旧未确定结果记录 UNKNOWN，历史写集和 ReadAt 保持原值。无可信归属或环境的旧通知仅保留本人收件中的最小历史摘要。通用旧引擎的邮件保留未采用状态。实际邮件、Required 和生产运维原键重送继续等 Owner 采用。 [同卡 QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/330504be45ba928527adc0dc503dfc698d3c18b2/handoffs/CP6-DEV-20261012/tasks/WP12-20/messages/20261123T202654Z-cp6-exec-3-question/MESSAGE.md)
- **旧一键已读。** 旧列表保留 data 数组，外层返回该次列表的受保护 watermark。旧 read-all 请求明确提交页面持有的 watermark，缺水位得到 428，提示重新读取。浏览器共享观察 Cookie 已移除。v2 使用原 Scope 绑定和按 PostgreSQL 提交可见性判断的快照。同一登录两个观察分别提交，晚于各自观察的消息保持未读，重放不写第二次 ReadAt。
- **深链。** Chrome 合成用户验证真实 FORM 与 APP 当前任务动作上下文。旧 APP 接收人仍能读取本人最小历史摘要，点击失权通知时看到安全状态并退出失权任务。实际 APP 投票为零。

## 定向验证

本机隔离 PostgreSQL 18.6、Kestrel 和 Chrome 合成资料 **65/65**；Vue 应用类型检查通过，通知矩阵和任务面板 Vitest **23/23**，台账结构与生成视图检查通过。SQL Server 未运行。SQLite 两项旧用例的排序规则错误沿用精确 Main 对照证据。实际邮件、短信、Slack 及部署为零。

| 原验收维度 | 候选原件覆盖 |
|---|---|
| AC-T06-01/02/03 | 旧偏好及顶层合并、通知矩阵、原 remind、实际本人 WFC CAS 与同原回执。 |
| AC-T06-04/05/06 | FORM/APP 原事务样本，期限重算原回执和重复扫描，两个租户的实时分组，历史站内已确认/未知/缺证，合成邮件去重、未知恢复与抑制。真实 provider、Required 和正式运维仍未验。 |
| AC-T06-07 | v2 与旧入口，本人两次观察、到达时序、幂等重放、无水位安全响应。 |
| AC-T06-08 | FORM/APP 实际页面深链重查、当前及历史通知、失权清理、合法代理和租户内隔离。 |

原件位于 C:/Users/tt/CP6-test-artifacts/WP12-20/20261123-wp1220-exec3-r2。source-bind.json 包含相对 Main 的逐文件 Git blob、测试命令和原件摘要；独立 PostgreSQL 实例已停止。两个 SignalR 分组各收到 3 和 2 次刷新，包含各自的原始待办与本轮合格通知；并发或重扫不增加重复提示。该历史处置回归的替身发送、查询、无来源派送和审批投票均为 0。原记录在 TRX 和 raw-focus-test-output.json。混合版本的真实切换未演练，仍待正式部署流程。

| 原件 | SHA-256 |
|---|---|
| wp1220-r2-first.trx | a483dd32b1111cce021d913e014dca4b4c200bb8a5b356030df0ab6dc3073f7e |
| permission-consumption-wp1220-r2-first/workflow-notification-browser/result.json | 86d8b37b5dbe7cf9d1c46ca717a728a92c1ea8818676f91e728df0aa04260bcb |
| roadmap-structure.json | d887ac74bc59034d4c9c72b9e036ead7bfafc655b022293ba890f683a2324f5c |

## 台账和工位

Main 台账为 PLANNED，原 AC-T06-01 至 AC-T06-08 全为 NOT_RUN，OA-TASK-01 25/46。本候选没有台账或权限词条/授予变更。WP12-15 的 **714.019 秒**按原 DELIVERY 提交与路径查重，下一合资格 Main 台账单次结转。WP12-14 已入 Main。 [WP12-15 原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/37c1eb25397baa8f8fe51cf75e870f23f3c045be/handoffs/CP6-DEV-20261012/tasks/WP12-15/messages/20261123T194905Z-cp6-exec-3-delivery-closeout/MESSAGE.md)

快照 2026-11-23T21:36:34.0137578+00:00。本轮有效 1125.114 秒，首轮最终快照有效 2975.939 秒，合计 4101.053 秒。两轮间等待 916.548 秒、首次领取前核对 316.775 秒单列。DEV-1 保留本卡，DEV-2 正式通信后释放。两项指定技能在本机未找到，已依授权继续使用 CP6 规程。
