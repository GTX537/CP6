# W609-01 Excel/CAD 匹配、确认和补偿的共同事务栅栏

2026-11-15 UTC。开发基线 `30956a78973f5d398a8a7a39b94cf93604da4fec`，受测源码 `643c8ad8ec72204e71ac56d26d517c764a8a12a3`。隔离 PostgreSQL 18.6、签名 Main Kestrel、模拟业务、真实 XLSX 和私有存储。演练使用实际 Match 和 Apply 步骤准备后续 HTTP 命令所需的已有业务记录。 [缺陷 #1128](https://github.com/GTX537/CP6/issues/1128) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

- 匹配启动、确认和补偿按原 `space:model:edit` 获取 Main 原生栅栏，保留各事务原 Job、补偿批次和回执。确认先验证私有制品内容，再在事务中核对 File、Artifact、存储键、长度、SHA-256 和当前 Draft、Floor 及租约。
- 原版六项真实复现，暂停先提交后仍返回 202、202、200 并有新写集。候选 **6/6** 实际 PostgreSQL 场景通过，零跳过。业务先提交保留原结果，暂停先提交都返回 401，无新增业务记录。同键恢复原 Job 或补偿批次，逐一核对实际阻塞关系和业务快照。
- 103 项控制器、权限及 OpenAPI 契约通过。15 项相关服务测试在隔离锁文件及已确认的不相关 Dispatch 测试隔离下通过。保留原 NU1004 及夹具编译结果。隔离发布 310 文件 SHA-256，七份受测程序集和发布逐字节一致。实际存活 200，三条匿名写入 401。原始 TRX、锁序和停止记录在 `D:/CP6-test-artifacts/W609-01`。Actions 和生产部署均为零。

本轮覆盖三个前台命令。后台实际制品提交应答恢复、映射保存、后台处理、刊发激活和 WMS 仍需独立证据。正式 PostgreSQL **26 PASS / 1 NOT_RUN**。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
