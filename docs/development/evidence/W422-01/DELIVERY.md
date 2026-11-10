# W422-01 · 关闭候选的 C03 ERP 原件导入

DEV-1 于 2026-11-09 19:21:50 UTC 开工，基线 #858 `eec44dbe4f526f861f2d59837373a9fd503cfa6a`。新增 `erp_terminal_candidate.py import` 和 `recover`，使用现有隔离候选目标、原 v10 归档校验、单事务 PostgreSQL 驱动及受保护原回执。候选与租户、物理目标、manifest 摘要固定，`recoveryReady=false`。[导入入口](../../../../eng/wp02-02/rma-tenant-archive/erp_terminal_candidate.py) · [原候选驱动](../../../../eng/wp02-02/rma-tenant-archive/candidate_core.py)

## 原输入与范围

使用 W419 的真实 C03 合作伙伴缺失终态。A 的原 Aggregate、Request、Processed CommandInbox 和 Pending Outbox 为同一图。验证原租户、业务键、水位、原命令及结果摘要、终态内容和无租约事实，保留原类型、原时间、原 payload、结果 BPCHAR 和 RowVersion。其余 ERP 四表必须为空成员。现有全局 CommandInbox C 键和 Outbox ICU 键仍由真实唯一索引执行。[W419 原 Owner 结果](../W419-01/DELIVERY.md) · [ERP 原生迁移](../../../../CP6.Persistence.PostgreSql/Migrations/ErpIntegration/20261002151345_PostgreSqlErpIntegrationBaselineV1.cs)

旧入口缺失的实际退出 2 见 `test-red.stderr`。离线使用准确 W419 pin `5a2711b967d03cd9258509ea04d24f0a5a004ff9243f154df23d2e0a7359a446` 校验全包、生成八个临时 COPY 和四个准确命名的触发器窗口。未运行数据库。原包在 `D:/CP6-local-test/CP6-72H-20261109/W419-01/native-02/checks/v10-cut/bundle`，资产根沿 W402 原空目录。

本窗接收已正常释放的 W419 停机夹具 `D:/CP6-local-test/CP6-72H-20261109/W419-01/source-clone/data`。转用回执 `D:/CP6-local-test/CP6-72H-20261109/W422-01/sealed/transfer.json` SHA-256 `28a7aebebd841819ece19404ac420648d80355eb151f287fca4b98ec5d80f74c`。转用和原 W405 均实测 `pg_ctl status=3`，无额外 PGDATA 副本。只新建关闭候选数据库，使用实际 Core schema、原 Core/ERP 历史、真实 A/B 租户及 B 原 ERP 四行。源活跃环境、安装和 admission 记录保留在原库。

## 事务边界

持现有认证及候选互斥后锁八表。验证原 allocator 配置、六个行版本触发器、权限和真实键。以原 `START WITH 1` 取得序列锁，读取当前下一值，比较所有六个原分配表和待导入四条记录的最高正数八字节 token。拒绝重叠或超出范围的 token。历史 audit 中的 `InputRowVersion` 是引用。仅需前移时事务性 `RESTART WITH`，不重新生成原 token。四张写入表各自只暂停原 `cp6_rowversion_v1`，约束和其他触发器保持生效。原回执与所有状态同时提交。[原 token 安装](../../../../CP6.Persistence.PostgreSql/PostgreSqlTokenV1.cs) · [PostgreSQL 18 序列文档](https://www.postgresql.org/docs/18/sql-altersequence.html)

## 实际验证

`native-01` 于 19:39:23–19:41:23 UTC 采样 81 次。物理空闲峰值 868 MiB，未达本次获准的连续两次 1,050 MiB 启动门限。120.647 秒后退出 75。55567 无监听，转用 PGDATA 无 postmaster PID，`pg_control` 仍为 `7730f0f693e38549f3649fa331a91a5d65a31da420f021c9b22c48e81b473f24`。候选库尚未创建。原生行为和故障回归待执行。证据位于本窗私有根的 `native-01/before-pg.json`（SHA-256 `8128e6f8661719ee40b40f7f0f7182465898cd1798f8e39f0f05d504fe6d8d88`）和 `native-01/terminal.json`（SHA-256 `5334d80aaac6d0af5377c409c6629e0b24400ba49479910caed491fcc523df79`）。

从 W419 已保存的原 CSV 离线重算 A/B 原命令、结果、C03 输入三组摘要。按 Aggregate、Request、Inbox、Outbox 顺序，A 行版本为 `2, 5, 3, 4`，B 为 `7, 10, 8, 9`。运行中的序列、唯一键回滚、原回执恢复和 B 保持仍需实测。[定向原生验证入口](../../../../eng/wp02-02/rma-tenant-archive/verify_erp_terminal_candidate.py)

## 已执行的接续

2026-11-09 22:53:04 UTC 在 DEV-1 恢复同一分支。Main #863 与本任务基线的归档、Core、PostgreSQL 和依赖输入一致。`native-02` 在新鲜物理内存 1,142 MiB 实际打开原 W419 转用实例。源控件查询返回原 OID `16386` 的 JSON 字符串，验证器按整数比较而拒绝。实际数据目录、安装 ID、原 Owner 与只读状态正确。该轮 5.610 秒后停止，候选库创建尚未开始。驱动将 SQL `oid` 统一转换为 `bigint` 并按该轮准确停机指纹接续。[真实终止](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-02/terminal.json) · [原生身份输出](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-02/source/controls-before.stdout)

`native-03` 在同一实例建立隔离的 `w422_erp_candidate`，实际库 OID `43614`，system identifier `7706139311168619192`，关闭标记 `704f4215-7502-4b0a-bf3f-48fca14b2101`。真实恢复 schema、两份原迁移历史、A/B 租户和 B 的原 ERP 行。候选序列为 `[1,false]`，原六个触发器在位，准入、环境与导入回执为空。原 source-target、错误标记、错误 pin 和不合适的旧 v10 包四项拒绝已执行。[候选身份和边界](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-03/ready.json) · [候选初始写集](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-03/checks/boundary-before.stdout)

第一条原生故障注入在调用真实 `cp6_authentication_v1.acquire()` 时返回 `P0001 PLATFORM_ADMIN_GUARD_UNAVAILABLE`。schema-only 夹具保留两条已执行迁移和原授权，但缺 `PlatformAdminGuardV1` 的初始根行，ERP 导入 SQL 尚未执行。驱动沿用原迁移的 `INSERT INTO cp6_platform_admin_guard_v1.root VALUES (true, 0)`。已建候选的续跑先核对原物理标记、两条迁移记录和空回执，再锁定空根表按原语句初始化。修正后的停机输入和原包 pin 预检通过，实际初始化、故障注入和原回执回收待运行。[原生拒绝](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-03/checks/allocated-token-conflict/stage.stderr) · [原初始化](/D:/CP6/CP6.Persistence.PostgreSql/PlatformAdminGuardPostgreSqlV1.cs:14) · [修正输入预检](/D:/CP6-local-test/CP6-72H-20261109/W422-01/guard-repair-preflight.txt)

第二轮总耗时 31.352 秒。postmaster 43196 已退出，转用实例及受保护 W405 原件均为正常停机。原 W419 源控制快照前后 SHA-256 `0776b1b295fd3d0f12ef44690b7ee06c6f27eed581fce5c06fa6bf401cd24a4b`，其他 Owner 与 A/B 原行保持。转用 PGDATA 保留现有候选及原只读源，待 W422 完成后再安排该共享夹具。[实际停止](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-03/stopped.json) · [本轮终止](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-03/terminal.json)

## 续跑

`native-04` 在修正入口和报告提交后的精确源预检通过后执行一次资源检查。27 个实际样本为 1,001–1,036 MiB，39.198 秒后退出 75，实例与候选保持已记录的停机状态。[资源原件](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-04/before-pg.json) · [报告后预检](/D:/CP6-local-test/CP6-72H-20261109/W422-01/guard-repair-postreport.txt)

`native-05` 在 23:12:57 UTC 以 1,088 MiB 物理内存和 19,495 MiB 可用提交通过启动，实际 postmaster 16356。原源控制与 A/B 业务字节重新核对。隔离候选保持同一 OID、关闭标记及空回执，并在原数据库身份和两条迁移记录校验后提交缺失的初始根行。PostgreSQL 回读 `{"revision": 0, "singleton": true}`，原输出 SHA-256 `fc476ebbafcfc93be48e407c4550b134eac9e56b6ced22998375e886edfb6ddc`。后置门 21 个样本均有至少 1,010 MiB 物理内存，可用提交最高 19,398 MiB，门槛为 19,456 MiB。实例正常停止，启动检查、开库、补齐、后置检查与停机共 57.701 秒，退出 75。ERP 故障注入与导入待执行。停机转用 `pg_control` SHA-256 `b2c6bd871269870c224ce498f9b64db562ecc356447ff70f1b52338ec6ba2c6a`。[初始根行原生输出](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-05/restore-original-platform-root.stdout) · [后置资源原件](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-05/after-bootstrap.json) · [停止原件](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-05/stopped.json)

实际接续从 `native-05` 的准确停机记录复用同一已补齐候选。`native-06` 运行 `python -B D:/CP6-local-test/CP6-72H-20261109/W422-01/run-w422.py native --attempt native-06 --pg-minimum 1200 --seconds 120 --resume-attempt native-05`。独占 55567 并满足两段资源门槛。

## PostgreSQL 实库结果

`native-06` 在 PostgreSQL 18.6 原候选 OID 43614 实际执行。原 source、标记、manifest 和不匹配旧 v10 被拒绝。注入 A 原 token 与 B 已分配 token 重叠时触发 `P0001 rma-stage-erp-candidate-token-rejected`。随后原生故障在三条 A 原行及事务性序列前移已发生后触发，改为大写并带尾空格的 B 原 Outbox 键得到 `23505 IX_Cp6_OutboxMessage_MessageId`。两次失败合并后无 A 原行或回执，序列、六个触发器、ACL 及 B 原件与原状态一致。恢复入口实测原回执不存在。[原 token 拒绝](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-06/checks/allocated-token-conflict/stage.stderr) · [晚段原生冲突](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-06/checks/late-global-collision/stage.stderr) · [回滚边界](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-06/checks/boundary-after-rollback.stdout)

随后真实 `import` 提交，首回包输出丢弃。独立 `recover` 和同键 `import` 返回同一份 714 字节，直接 `psql` 文本回读附加了输出层 `CRLF`。原验证器将行终止符参与字节比较，22.362 秒后退出 1。原失败和正常停机记录保留。修正仅改验证器，以 PostgreSQL 十六进制读取原 `result_json`，解码后比较每个原字节。 [初轮退出](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-06/terminal.json) · [原回执输出](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-06/checks/recover-original.stdout) · [带输出行尾的原件](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-06/checks/persisted-receipt.stdout)

`native-07` 从同一已提交候选接续。独立进程恢复、同键重放及数据库持久字节完全相同，SHA-256 `2d3d25d0974a2b5827439303146466fcde5a167208d9b0f8558eb3851dbc47a2`。错误 pin 恢复拒绝，A 与 B 的八个原生 COPY 成员分别逐字节一致。实际 `nextval` 返回 11，六个原触发器、ACL、环境和准入边界保持。原 W419 只读源的 A/B 八表、控制和其他 Owner 摘要前后一致。该轮总耗时 17.358 秒，正常停止，`pg_ctl status=3`，端口释放。 [实际结果](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-07/actual.json) · [持久字节回读](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-07/checks/persisted-receipt-hex.stdout) · [停机](/D:/CP6-local-test/CP6-72H-20261109/W422-01/native-07/stopped.json)

集中审查核对真实源与候选物理身份、实际 C03 图、行版本分配、原唯一索引、事务回滚、原字节和受保护回执。运行器最终 SHA-256 `59987517faacef9eda8b4b67e850a7c1c7d5cba5f87ab0457186afb1f1ba2e5e`。本窗交付范围为关闭候选的单一原 C03 图和原件恢复，`recoveryReady=false`。正式 PGT-021/022 的完整切换、当前撤权与旧消息对账仍待各原场景验收。[私有运行器](/D:/CP6-local-test/CP6-72H-20261109/W422-01/run-w422.py)
