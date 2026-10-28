# W200：迭代决定应用、问题关闭与撤回竞争

COORD 单人继续 CP6-7D-20261022 的 D3 实际前置。W199 Main PR622 / Handoff PR175 已核对远端采用、准确分支清理及席位释放；仅归档一次。复用 DEV-1 / DEV-2，不新增物理工作树。

## 本轮任务及实际执行预估

1. 准确轮次/候选位置的问题登记、责任与期限、原答复和证据、独立验收关闭：45–75 分钟。
2. 原生 OA 专业决定、原 bytes Inbox/去重/GAP、Owner 原子应用与回执/Outbox、撤回竞争和后继轮次：90–150 分钟。
3. 独立 API/受控页面与未知恢复、相关 PG18/实际 HTTP/UI、一轮 SPEC/QUALITY 审查、台账四状态及正常 PR/finish：45–75 分钟。

预计180–300分钟，沿用原七天持续目标；只按真实 START/finish 记录实际耗时，不以用例数、轮次数或日历时间代表完成比例。

## 依据与前置核对

固定 WP05-20、PLM-ITER-01 原 PROC010/011/012、OA R2 封闭合同、ITER-OA-APPLICATION/1、共享消费者准确 mapping/schema/验收资产。W199 提供原生完整集合、准确评审包与 RoundBinding、原子 IN_REVIEW/双 journal/Outbox、持久 OA intake/FLOW start；既有 OA 决定、服务身份、原bytes接包/回执链与本地最终权限门按真实源码采用。

Main 当前确认基线 df309fb2bc9a91bba907057f93034c95e4e1eee8；CRM/Platform 不修改。WP02 原可信上下文、PostgreSQL、同事务原命令/审计、当前权限与最终 fence 采用原生基础；不以整个前置任务已完成或跨仓运输 ACK 代替当前消费证明。工程来源缺失/过期仍阻当前相关动作，不造替身服务作为生产证据。

## 本轮输出与必须保持的行为

- 问题只定位冻结包实际候选/技术位置或明确流程问题，不改包。阻塞问题默认保持阻塞，降级须当前授权和理由；答复/证据追加，问题作者及答复人不得自行验收关闭。APPROVED 应用要求全部阻塞问题已授权 VerifiedClosed。
- 原决定 bytes 持久 Inbox，准确环境/租户/服务/Owner/版本/事件身份、异体冲突及序列 GAP 隔离；当前控制头、Round、完整 Binding/Package/政策/来源逐项核验。接收和 OA Approved 均不等于 Owner Applied。
- 应用的业务阶段/问题处置、原命令结果、应用回执、Outbox 和审计同 PostgreSQL commit。冻结 rejectDisposition 决定 RETURN→IN_DESIGN 或 REJECT→待评价，不自动关闭。只接受原 OA 决定枚举。
- 撤回与应用争用相同当前头/轮次 CAS；应用先赢撤回冲突并保原回执，撤回先赢旧批准只产生 STALE，不改后继轮次。保留原包和 OA 实例，持久通知/原结果查询；后继轮次重新固定实际候选/输入/规则和新摘要。
- 回执网络投递在业务锁外，丢回执只重投同一原文；恢复追加合法 revision/previousReceiptId，不改写终态；OA 仅投影最高连续回执，不反驱动业务。
- 当前项目/对象/字段/来源/专业角色与 SOD、服务注册和最终授权仍检查；API 拒客户端伪造身份/控制头。历史和未知原操作查询受当前读权限，UI 按身份/租户隔离恢复状态并丢弃过期响应。

## 保留的完整目标范围

本轮实现原生评审决定消费链；完整 WP05-19/20 的外域兼容及全部正式验收不因本地实例通过自动关闭。工程冻结/REL/VER 与实际 WO/SCHED 继续按原 Owner 前置推进。原 D2/D3/D5/D6/D7 出口、125 目标和247正式验收均保留；不触发 Actions、迁移既有环境、生产部署或修改保护。

## 源码核对补充

WP12-19 可信业务撤回目前尚无原生实现，旧 TaskCenter 撤回明确拒绝原生 oa.app 实例。将该直接前置纳入同一 W200 串行窗口：封闭 OwnerWithdrawalRecord/OwnerWithdrawCommand、当前服务和原人授权、真实控制行版本、原生工作流撤回命令/决定/Outbox、原文查询恢复。不得伪造审批任务或投票替代撤回事实。完成底座后由 WP05-20 同事务采用。

## 实际交付

[报告](../../evidence/W200-01/DELIVERY.md)，登记105.01分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
