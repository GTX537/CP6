# W477-01 — 同源四域 v11 的 Stock 原件与当前状态

从远端主线 b39e3f480c7163f4c8f769fb14884419ac22b8ef 复用 DEV-2。新建 W476 已停机源的隔离副本，复用 W466 封存构建及 W405 的库存验证入口。实际调用 StockMovementService.ApplyCommandAsync。A/B 模拟租户各用同一 operation UUID c376d9ab-5518-4646-92d4-b55220f14f97 完成原 COMMITTED，原生结果摘要与 Stock Source 一致。两租库存各从 10.12345678 加 1.12345678，结果均为 11.24691356。两个租户分别保留原 NativeId、root、message key 和独立后端。[W476 同源原件](../W476-01/DELIVERY.md) · [W405 库存 Owner](../W405-01/DELIVERY.md)

- PostgreSQL 18.6 生成完整 41 成员 v11，manifest SHA-256 f0d711a4b488e2df294364dacb8c1151f2c01036b84c24e35eed60b53cdd5e07。与 W476 比对的 16 份 RMA journal、出站、FileContent 和 ERP CSV 逐字节一致，原 RMA journal 摘要 a1a516a5a3a7c2293344351d813d82d2ba756c37b57272da94293f57fc5c54ed。同一归档的 Stock 部分包含实际库存操作、Authority、Route、Pending Outbox、Source 及 Binding。
- 中立库 w477_neutral 的 OID 44438 与来源 OID 16386 不同。14 份 Stock CSV 从原库到归档和中立库原生 COPY 逐字节一致，另有 14 份 RMA journal、FileContent 和 ERP CSV 逐字节回读。暂存及 recover 回执一致，SHA-256 83625faa2d51a920fa9385d7dc180154bc4860f88d2b080dce92cb3638d2571b。文件落盘保留原 45 字节，回执 SHA-256 e90aabf99fa0e0f296af303b8e9c9c2bd59731dce03b2611324c635baa9dc6c3。中立库没有活动 Stock schema、公共 CONNECT 授权或发送运行。
- 用同一 v11 和文件根实际运行 Stock 只读对账。原 Pending 与当前 Pending 匹配，PRESERVE_CURRENT，0 次尝试，无租约和发送。报告 SHA-256 e549a652cc964ccc7565c325a49e3699ce7a8d8b9c903f704cb27a08cdc51e4a。对账前后 A/B 各 14 份 Stock 原 CSV、库存量及 RMA 原流水保持一致，W476 原数据目录控制文件、文件根和 manifest 保持原状。[Stock 只读对账](../../../../eng/wp02-02/rma-tenant-archive/reconcile_stock_outbox.py)

原生 SQL、原件、回执和进程退出保存在 D:/CP6-local-test/CP6-72H-20261109/W477-01。本窗启动的 PostgreSQL 和验证驱动已退出。W476 经 #944 采用后任务分支清理、DEV-2 干净释放，实际 12.88–12.96 分钟。

本窗验证了源、归档及中立库的原件与 Pending 状态。后续仍需实际消息状态变化、候选导入与开放、正式 PGT-021/022 和批准切换。Stock 发布仍受 CP6.Platform #80 对应版本交付约束。候选保持 recoveryReady=false，原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。GitHub Actions 和部署未运行。
