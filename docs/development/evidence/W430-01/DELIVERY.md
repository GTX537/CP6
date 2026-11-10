# W430-01 · PGT-027 同 schema 归属更新

DEV-1 于 2026-11-09 21:31:14 UTC 从 Main #861 领取，首次暂缓后于 21:49:13 UTC 恢复同一任务分支。真实 PostgreSQL 18.6 已完成同 schema 的归属更新验证。正式 PGT-027 保持 `NOT_RUN`，待 W428 的正常 EF 迁移入口与 IAM 宿主回归。[原验收项](../../acceptance/postgresql-tenancy.json) · [W428 检查点](https://github.com/GTX537/CP6/blob/1de6970320f139e99ac68c1cb8f238c4e447e91c/docs/development/evidence/W428-01/DELIVERY.md)

沿用 W367 已以原工具安装、迁移和写入的独立 `rma-application-tenant-v1`。两租户持有相同 ApplicationId，原混合 COPY 的第二行以 `23514` 回滚，纯 A 批次已提交。W430 运行在一个隔离副本，物理库 `w367_rma_bulk`，OID `16386`，system identifier `7706104419616707100`。五份源 PG 控制文件和三个原生结果原件的摘要在运行前后相同。当前 Main 的存储 profile 文件与 W367 验证基线无差异。[W367 原件](/D:/CP6-local-test/CP6-72H-20261109/W367-VERIFY/DELIVERY.md) · [物理目标](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/actual.json)

## PostgreSQL 实测

恢复原 SCRAM 规则后，未提供密码的 A 连接被拒绝。后续 A/B 都按各自普通角色登录，并核对绑定、`session_user`、`current_user` 及实际列权限。A 的原 ACL 允许更新 `version`，`tenant_id` 受限。所有临时密码及短时列授权限于同一停机后保留的隔离副本。[原 A 权限](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/ordinary-a-original-acl.stdout) · [原 B 权限](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/ordinary-b-original-acl.stdout)

| 场景 | 原生后端与事务 | 实际结果 |
|---|---|---|
| A 暂存合法 `version=1` 后尝试把归属改为 B | PID 1832，xid `763` | 原 ACL 拒绝，`42501`，独立查询为 `aborted` |
| 只在副本授予 A 更新归属列，重新执行同一事务 | PID 31636，xid `765` | `heads_tenant_id_check` 返回 `23514`，独立查询为 `aborted`，临时权限随后撤销 |
| 恢复原 ACL 后由 A 更新合法版本 | PID 57268，xid `767` | 独立查询为 `committed`，只改本租户原 head 的 `version` |

两次拒绝前后的完整 A/B 写集、目录 OID、复合键、原历史及共享 Ready 逐字节一致，SHA-256 `7d9aeefe72c2f556d84b20d1360b2140a4c88ed4009d31e2895f79256381e053`。成功事务只把 A 的原版本 2 改为同租户现有版本 1，B 与 DDL 保持原值。[原 ACL 拒绝](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/ordinary-original-acl.stderr) · [原 CHECK 拒绝](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/ordinary-controlled-check.stderr) · [成功写集](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/after-legal-commit.json) · [原生约束](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/native-constraints.json)

## 启动和审查

`native-01` 以资源门限退出 75。`native-02` 达到连续门限并准备停机副本，复制后的新鲜样本为 935 MiB，低于 950 MiB，退出 75。`native-03` 复用该副本，连续样本 1,030/1,013 MiB，新鲜样本 1,009 MiB，启动后 975 MiB。最终 PostgreSQL 窗口 4.906 秒，postmaster 60956 及其子进程结束，55568 无监听，原件和副本 `pg_ctl status` 均为 3。驱动仅增加第三轮输出名，执行阈值保持 950/840 MiB，最终驱动 SHA-256 `3ac6bec532ff88bfa958ca8c7ac8157e1745cbc4ea03bbda64c3f54e7f48416e`。[第二轮门限](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-02/before-pg-fresh.json) · [最终门限](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/prelaunch.json) · [停机](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-03/stopped.json) · [私有驱动](/D:/CP6-local-test/CP6-72H-20261109/W430-01/run-w430.py)

COORD 集中审查原场景对应、相同物理来源、普通登录、临时 ACL 生命周期、三段原生 xid、完整前后写集及后续范围。W430 只交付 PGT-027 的同 schema 更新子场景。
