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

恢复原分支和 DEV-1 后，在独占 55567、连续及即时物理内存 1,050 MiB、可用提交 19 GiB 条件下运行 `python -B D:/CP6-local-test/CP6-72H-20261109/W422-01/run-w422.py native --attempt native-04 --pg-minimum 1050 --seconds 120 --resume-attempt native-03`。使用已建候选 OID 43614。修正驱动 SHA-256 `65e50f9a34081d2a521c8db901b16b38bf7aaa39bbf79491a44047d346ba014e`。正式 PGT-021/022 保持原状态。[私有运行器](/D:/CP6-local-test/CP6-72H-20261109/W422-01/run-w422.py)
