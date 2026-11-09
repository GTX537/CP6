# W430-01 · PGT-027 同 schema 归属更新

DEV-1 于 2026-11-09 21:31:14 UTC 从 Main #861 领取。当前窗口准备了原 PGT-027 的归属更新验证，并保存等待资源的检查点。PGT-027 维持 `NOT_RUN`。[原验收项](../../acceptance/postgresql-tenancy.json)

本窗沿用 W367 真实 PostgreSQL 18.6 的独立 `rma-application-tenant-v1`。W367 已完成 A/B 原安装和迁移。两租户持有相同 ApplicationId，A 的混合 COPY 在第二行返回 `23514`，整事务回滚。纯 A 写入实库提交，B、复合键和迁移历史保持。当前归属更新夹具固定同一份停机原件、五份 PG 控制文件与三份原生结果摘要。原库 OID 为 `16386`，system identifier 为 `7706104419616707100`。[W367 实测](/D:/CP6-local-test/CP6-72H-20261109/W367-VERIFY/DELIVERY.md)

续跑会在单一隔离副本通过普通 SCRAM 登录执行三段事务。先在原 ACL 下暂存 A 的合法版本更新，再尝试 A→B，预期 `42501` 且整笔回滚。随后只在副本短时授予 A 的归属列更新权，复测同一事务，预期固定 TenantId CHECK 返回 `23514`，随后恢复原 ACL。最后以原 ACL 提交合法 A 版本更新。独立连接核实每段 xid 状态、完整 A/B 写集、复合键和迁移历史。临时权限和合成密码只用于该隔离副本。计划副本逻辑大小 48.2 MiB，控制文件与原 W367 保持独立。[私有驱动](/D:/CP6-local-test/CP6-72H-20261109/W430-01/run-w430.py)

驱动 SHA-256 `f0152597685f8ea58fb7416b4bc0d8003f66d1e1b74bd57373226b79c22a76eb`，导入与 CLI 预检退出 0。实际 `pg_ctl status` 对原件退出 3。首轮启动门禁 19 个样本为 769–886 MiB，未达到连续两次 950 MiB 和可提交内存 19 GiB 的条件，退出 75。副本未创建，数据库和 CLR 未启动。源原件仍为停机状态。归属更新三段事务待执行。[停机原件检查](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-01/original-before-stopped.process.json) · [实测启动采样](/D:/CP6-local-test/CP6-72H-20261109/W430-01/native-01/prelaunch.json)

接续同一分支与固定席位。确认独占 55568、两次 950 MiB 及复制后的新鲜样本，运行 `python -B D:/CP6-local-test/CP6-72H-20261109/W430-01/run-w430.py --attempt native-02`。集中审查已覆盖隔离范围、临时权限回收、事务前后核对及精确停止路径。W428 的 EF/IAM 宿主验证仍是正式 PGT-027 的另一条必需证据。[W428 检查点](https://github.com/GTX537/CP6/blob/1de6970320f139e99ac68c1cb8f238c4e447e91c/docs/development/evidence/W428-01/DELIVERY.md)
