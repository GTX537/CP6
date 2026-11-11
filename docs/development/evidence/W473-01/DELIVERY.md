# W473-01 — v11 ERP C03 原件导入关闭候选

WP02-02 从远端主线 `8b42c8ac271aaabe558eb88db1ca13afe21b8a38` 复用 DEV-2。ERP C03 导入显式接受完整 v11，继续核对原 manifest、外部文件、物理源分离及现有八表形状。原 C03 图、全局唯一键、分配器、原生事务和回执沿用原合同。[候选入口](../../../../eng/wp02-02/rma-tenant-archive/erp_terminal_candidate.py) · [原 v10 C03](../W422-01/DELIVERY.md)

- 原入口实际拒绝 v11，显式选项未实现时退出 2。最终使用 W429 原清单 `c4d87a7a6d41376fea0e440c63d3661edc7f7b06340cdc5b0a991cbee22a7050`。八个 A ERP 成员与 W419 原成员逐字节相同。A 的真实 Aggregate、Request、Processed Inbox 和 Pending Outbox 导入 PostgreSQL 18.6 新建关闭候选，OID 98529。B 的四条原记录已保留。A、B 各八个成员独立 native COPY 回读，全部逐字节一致。
- 分配 token 重叠按原合同拒绝。实际写入三条原件并事务性前移序列后，第四条遇到原 Outbox 全局唯一键返回 23505。A 原件、序列、原六个触发器、ACL、回执及 B 原件一起保持原状态。随后真实导入成功，实际下一 token 为 11。
- 完整 v11 同键重送、只读 recover 和持久原回执逐字节一致，714 字节 SHA-256 `6e5aace37ea8d648ac1da795fa8f7503a75587dc135d55dd38d72c1384755efc`。错源身份、错标记、错 pin、缺选项和旧包错误形状均按原入口拒绝。用 W419 真 v10 比较旧 import/recover 驱动，与基线逐字节相同。[定向验证](../../../../eng/wp02-02/rma-tenant-archive/verify_erp_terminal_candidate.py)
- 只使用 W469 的私有数据库副本，W429 原目录持续停机，源库 49 个关系及原包和原文件摘要前后一致。候选保持 `recoveryReady=false`，实际 ERP 环境及准入为空。专属 PostgreSQL 已停止。

原始退出、原件回读、原生唯一键失败与停机记录在 `D:/CP6-local-test/CP6-72H-20261109/W473-01`。本窗使用模拟业务数据，只运行相关 Python 及 PostgreSQL 验证。GitHub Actions、CLR 和部署均未运行。

W472 经 #940 采用并释放，实际 16.40–16.45 分钟。当前范围为关闭候选的原 C03 单图。原 RMA 流水仍待真实 Owner 同源 v11 样本，正式 PGT-021/022 和切换各自验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。
