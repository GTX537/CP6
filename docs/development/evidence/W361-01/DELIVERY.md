# W361-01 · Core RMA 原 journal 租户权限

2026-11-09 09:04:56 UTC 在 DEV-2 开工，分支 `codex/w361-01`，基线 `baff5fc20dceee932c805fbf309e6542b960cbeb`。采用 W353 已 Ready 的新隔离 Core RMA 数据库。 [W353 commissioning](../W353-01/DELIVERY.md)

## 交付

`install-rma-core-original-journal-v1 --stdin` 核对原物理目标、安装、完整普通登录目录和每个租户的 Ready 与本地 history。它先取得原认证锁，再对共享 journal 取一次排他 DDL 锁，在同一事务内安装权限和保存原回执。同键恢复核对受保护回执和 journal 原生权限目录。 [CLI](../../../../tools/CP6.TenantStorage/README.md#original-journal-for-commissioned-core-rma) · [实现](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.OriginalJournal.cs)

普通登录只读 `cp6_main_owner_v1.operations` 中本租户、`Main.Wms.RmaApplication.v1` 的原操作列。固定 `SECURITY DEFINER` 追加入口绑定实际 `session_user` 所属租户和 `RMA-NATIVE-APPLICATION/1`，在调用者事务中按原顺序取得认证锁并走原 `authority_gate`。旧拒绝触发器的例外只在共享 journal 的 INSERT 条件中按准确 NOLOGIN 角色 OID 生效。两张旧业务表的触发器、原键、字节列、不可变约束和其他 Owner 的 journal 路径均沿用。

旧 commissioning 首装仍要求原三处为空。当前 CLI 对已完成的原操作重放时，只有确切 overlay 仍采用且两张旧业务表仍空，才接受 RMA journal 非空并返回原字节。旧 W353 DLL 在相同已追加数据上实际返回 `shared-rma-not-empty`。启用后用 W361 兼容 CLI 重放。 [commissioning 兼容入口](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.CoreCommission.cs)

## 实际验证

Windows PostgreSQL 18.6，专用环回端口 55567，数据库 OID 16386，system identifier 7706100498199938088。W351 的已发布 DatabaseInit（源码 `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40`）实际执行并退出 0，Core、Space、IdentityPriority、ERP 历史逐项匹配发布源码。普通 A/B 登录、写入载荷和非 RMA Owner 记录均为隔离模拟。使用已保存 W353 CLI 实际 prepare、commission 并保存原回执。 [可复现夹具](../../../../eng/wp02-02/rma-tenant-migration/verify_original_journal.py)

| 场景 | 实测结果 |
| --- | --- |
| 准入和原回执 | 错物理 OID、错安装、颠倒 A/B 的映射和普通登录安装均拒绝。首装 CLI 退出 0 后丢弃 448 字节 stdout。同键 stdout 与受保护的持久原件一致，异键拒绝 |
| 原键和原字节 | A/B 普通 LOGIN 用同一 UUID，各自保存不同输入、摘要、结果字节和对象 UUID。单租、Owner 过滤及审计列拒绝实际生效。相同键重复追加触发原 `23505`。另一 Owner 的同键记录仍在，旧路径也能追加新键 |
| 事务和权限 | CLOSED 追加回滚后不可读，同键随后成功提交。跨租追加、直接 INSERT、`SET ROLE` 到另一租户或执行角色均拒绝。控制身份伪装执行角色仍被原租户目录按实际登录拒绝。旧业务 INSERT 和两表 TRUNCATE、旧 journal INSERT、原 journal UPDATE 仍到达各自数据库栅栏 |
| 原认证锁 | 普通 A 的实际后端 32312 在 `transactionid` 等待原认证锁后端 47620，尚未持有 journal 的 `RowExclusiveLock`。B 此时完成原读取，释放原锁后 A 继续提交 |
| 原 commissioning 与漂移 | 旧 W353 DLL 退出 1。当前 CLI 对原键返回相同 1488 字节，两份 Ready/history 保持原值。将实际 store 改为 `SECURITY INVOKER` 后，两条回放均以 `journal-overlay-mismatch` 拒绝。恢复精确目录后原回执仍逐字节相同 |

锁定 NuGet 还原使用现有包，.NET SDK 8.0.424 单节点 Release 编译为 0 错误、23 条既有其他文件告警。正式夹具退出 0，`final.json` 写于 09:26:24 UTC。原件在 `D:/CP6-local-test/CP6-72H-20261109/W361-01/rma-original-20261109T092538-922d15e7/journal`，其中 `source.json` 记录发布宿主、旧 CLI、当前 CLI 与 Core DLL 的 SHA-256，`auth-root-wait.json` 记录实际阻塞，`stopped.json` 记录实例停止。

旧 CLI 对新命令的真实 RED 为退出 2。首次候选实库拒绝持久 `regrole` 常量（SQLSTATE `0A000`），改为事务内读取确切角色的数值 OID。下一轮仅因夹具单独 TRUNCATE versions 先触发原 FK，未到旧拒绝触发器。改回 W353 同时 TRUNCATE 两张表的输入后，复用同一候选编译产物通过。两个失败根 `rma-original-20261109T091851-1c391ebf` 和 `rma-original-20261109T092406-93d85f5e` 均保留 PostgreSQL 日志与停止记录。

三个专用 postmaster 均已停止，55567 无监听。普通 RMA 业务写、原 Owner 的宿主采用和 HTTP 验收留在对应窗口。Actions 与部署均为零。候选本地分支及干净 DEV-2 交 COORD 集中审查、远端采用和官方释放。
