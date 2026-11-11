# W470-01 — 完整 v11 的四项原源只读对账

WP02-02 从远端主线 `07d37612df81176d3241410dd7634dd9d8d1eea4` 复用 DEV-2。IAM、Identity Priority、ERP Delivery 和 Main Stock 四个入口接受完整 v11，沿用原 manifest pin、文件与成员摘要、原物理库和 installation、当前目录及原键校验。四个报告的原格式、事务与状态判定沿用既有合同。[W429 v11](../W429-01/DELIVERY.md) · [W409 v9/v10](../W409-01/DELIVERY.md) · [W408 v10 Stock](../W408-01/DELIVERY.md)

- 原四个正式入口对同一完整 v11 各退出 1，均在版本门拒绝且无 PostgreSQL holder 或报告。最终使用同一 W429 原清单 `c4d87a7a6d41376fea0e440c63d3661edc7f7b06340cdc5b0a991cbee22a7050` 和原非空 LocalFileStore，四个入口均退出 0。只启动 W469 留下的私有数据库副本，原 W429 目录持续停机。副本以原 OID 16386、系统标识和本机端口匹配归档原库。
- PostgreSQL 18.6 四份报告均为 `REPEATABLE READ READ ONLY`、`OBSERVED`、`recoveryReady=false`。IAM 的 17 项原事实一致。Identity Priority 保留 1 条 Published；ERP 保留 1 条 Processed Inbox 和 1 条 Pending Outbox；Main Stock 保留 2 条 Pending 原键。空的 ERP Bridge/Identity Inbox 查询沿用原实库读取。前后 49 个关系行数及 SHA-256 一致，原包和原文件摘要一致。
- 错 manifest pin 和同长度错文件均在连接数据库前拒绝。首轮验证脚本误将 Main Stock 的源关系写成暂存关系，保留原生 `42P01`；修正仅涉及私有验证脚本，最终以正确源表完成。两个副本进程已停止，端口无监听。[自动验证](../../../../eng/wp02-02/rma-tenant-archive/verify_reconciliation_v11.py)

原始退出、报告、前后摘要及资源、停机记录在 `D:/CP6-local-test/CP6-72H-20261109/W470-01`。本窗使用模拟业务数据，只执行相关 Python 及 PostgreSQL 验证。旧格式结果复用 W399、W409 和 W408 原执行。Actions、CLR 和部署均未运行。

W469 已由 #937 采用并释放 DEV-2，实际 18.64–18.71 分钟。本窗仅闭合完整 v11 的原源只读观察。关闭候选的宿主 Owner 读取、正式 PGT-021/022 和切换仍分别验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。48 小时容量仍以 172800 秒核对。
