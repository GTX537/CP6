# W185-01 — 业务回执可靠返回与缺链恢复

COORD 本聊天单人，Main 基线 `8dc871aa21fc084237302b4d093c6954308ae364`，复用 DEV1/DEV2。W184 已完整收尾，本轮归档一次，仅核销 `WP10-48.wave184_application_receipts`。

## 实际采用

真实 Owner 原 ApplicationReceipt/Outbox 经登记的服务可靠返回审批端。发送方保存租约、epoch、尝试及确认，先查询原事件；仅明确 NOT_OBSERVED 才原键重投。确认丢失、当前写权撤销、服务不可达及旧租约都有准确恢复边界；PENDING_GAP 只查询，不重新执行业务。

Workflow 用独立服务和精确 Subject 授权接收原件，按 Submission→流投影→Inbox 顺序进入 PostgreSQL 事务。原始请求、规范回执、摘要、权限审计和连续历史持久保存。乱序回执先保存缺链记录；补到前驱后连续推进。事件/版本冲突保留原件和冲突证据，伪造 APPLIED 不会覆盖投影。当前结果查询保持原规范摘要与时间。

缺链 worker 使用单独的显式恢复权，通过 Owner 固定服务路由读取签名快照链；逐页检查 Scope、Subject、决定、版本、前驱和状态转移，网络在事务之外，已验证前缀用小事务提交。丢页或失效游标从已提交位置续跑；快照变化隔离，当前撤权停止补录，过期租约不能回写。快照最高版本进入持久目标，分页预算耗尽仍保留余项排队，不因初始缺口已经补齐而提前结束。

`ToolApplicationDelivery`、`ApprovalReceiptRecovery` 均为独立默认关闭的登记配置。没有自动种入企业权限、服务或凭据；真实业务关闭来源缺失仍 BLOCKED，不声称物理报废或其他库存效果。实现及配置边界见[回执返回说明](../../approval-receipt-return-v2.md)。

## 验证与审查

26 项相关检查通过，其中 26 项 PostgreSQL18/HTTP/TLS、0 项纯单元，0 skip。真实来源与准确源码摘要在 `D:/CP6-local-test/wave185/foundation/final-validation.json`；每项保留所属运行，不把重复执行累加为交付量。

覆盖乱序/连续前驱、原事件冲突、伪造业务结果、两个连接并发、缺失必需记录、权限 ABA 回滚、未知提交与确认丢失、当前只读恢复、租约接管、固定目的地址、闭合回执 DTO、真实 C02/TLS、固定快照并发追加、丢页/失效游标恢复、快照冲突隔离、显式恢复权和网络后的撤权。实际 Program 启动/HTTP 及受影响的旧提交/决定传输路径做了相关回归。

一次作者集中 SPEC/QUALITY 审查完整任务差异。审查修正了分页预算耗尽时的剩余快照排队边界，并补充旧租约回写、固定地址与错误回执验证；同时避免每个新回执的延迟约束扫描整条历史。修改后只复查相关实现并运行对应回归。无前端改动，不重跑页面、类型检查或构建；旧 UI 结果没有改称本轮执行。

## 剩余范围与生命周期

真实 WORK/BORROW/STOCK 关闭、业务效果、企业运行资格及原 D2/D3/D5/D6/D7 出口保持未完成。仅新增 `WP12-07.wave185_receipt_return` 限定采用记录，不提升完整任务或正式运行验收；七天目标 ACTIVE。下一步重新核对原 D3 计划/工单的直接工程前置，不把本回执前置自动扩成全部工作流或版模报废功能。

预计本对话实际 120–210 分钟；当前 Main 登记 55.45 分钟，最终以 START→finish 计一次，不叠加 Handoff。Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/161，source `c2d836165ebe962045aaa216fe6b0ab412883e87` → target `10a5f286d78a47b61d8ea68e8bce90881760ada3`，准确采用、任务本地/远端分支删除及 DEV2 释放已核对。Main 本提交待正常 PR/远端采用/finish，最终以 `wave185-final-verification.json` 核销。

固定工作目录、用户原件及缓存保留。无新源码树、Actions 触发/取消、既有环境迁移、部署或保护绕过。
