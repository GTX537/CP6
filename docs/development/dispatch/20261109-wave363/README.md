# W363 单快照 RMA 归档与原生权限并行

2026-11-09 09:11:24 UTC，RMA-SNAPSHOT-ARCHIVE 领取 DEV-1，codex/w363-01，基线 93fde21bff2981f7b4b34116802995e6eba38a21。W359 随 [#816](https://github.com/GTX537/CP6/pull/816) 正常采用，准确分支清理并释放，实测 1,239 秒。隔离 PostgreSQL 55566，W363 起始估 35–50 分钟。

从明确物理目标和原 Ready 取得只读可重复读快照。实际原生导出 RMA 租户 schema 和引用的共享 Outbound 投影，核成员、原字节与 ACL。A/B 在快照后提交真实变化，隔离 staging 核所选原内容。原 IAM、journal、消息、外部资产和完整恢复另窗。

W361 在 DEV-2 独占原 RMA journal 的新最小权限及控制文件。W362 提交确认丢失与原 CLI 恢复实际完成，INTEGRATION-1 干净释放，完整 720 秒，保留外围脚本失败和独立复核。

2026-11-09 09:18:15 UTC，W365-VERIFY 领取 INTEGRATION-1，RMA-NAMESPACE-ACL-VERIFY，固定 detached 同基线。独立 PostgreSQL 55568，原估 10–18 分钟。验证实际普通租户 LOGIN 的 public、临时对象和未登记表边界。COORD 串行维护台账和远端集成。

W363 实测同快照原生归档、隔离 staging、正常备份 CLI 和拒绝边界通过。 [交付报告](../../evidence/W363-01/DELIVERY.md)
