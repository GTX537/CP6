# W417-01 · PostgreSQL 原生租户权限验收

DEV-1 于 2026-11-09 17:54:42 UTC 开工，基线 #852 `9dca16ad396b1fc371038d5c3659024729f15232`。新增可复跑的验收入口，覆盖原 PGT-006、007、009。只对任务副本执行 PostgreSQL 命令，产品路径及验收卡由各自原流程维护。[原验收卡](../../acceptance/postgresql-tenancy.json) · [验证入口](../../../../eng/wp02-02/rma-tenant-archive/verify_native_acl.py)

## 输入与复跑

使用 W413 已关闭的 Core 候选 `127.0.0.1:55564/w406_candidate`，database OID `16386`、system identifier `7706205797235709868`、候选 `6a89d755-10d4-4b72-8504-cf7f839c5754`。候选保留 A/B 两套原 schema 和原共享流水。原 W413 journal 回执 SHA-256 `2a35571ac1a299b5656e766313f5a468d9464df82e11906072e454283a7cb66e`，A 原流水 CSV SHA-256 `a1a516a5a3a7c2293344351d813d82d2ba756c37b57272da94293f57fc5c54ed`。[W413 原采用](../W413-01/DELIVERY.md)

一次停机副本为 `D:/CP6-local-test/CP6-72H-20261109/W417-01/candidate-clone/data`，184,233,928 字节。原件和副本 `pg_ctl status` 均为 3，复制前后 `PG_VERSION`、HBA 和 `pg_control` 摘要相同。`candidate-clone/copy.json` SHA-256 `c86bdf727e0bd9dc67ab2acffe12b6b86c6c7e5e44f30446dc94ac866c23fdd6`。新入口在任何探针写入前核对实际目标、任务副本路径、owner、标记、无已开放记录和原流水回执。旧入口缺失的实际退出 2 保存在 `test-red.stderr` 和 `test-red.exit`。

任务复跑使用私有 `run-w417.py` 调用正常入口。独占环回 `55564`，仅在协商的本机资源窗口启动一个 PostgreSQL，`shared_buffers=32MB`。保留原 HBA，启动时使用仅限该任务数据库的私有 HBA，A/B 用副本内轮换的独立普通 LOGIN。凭据从进程环境传入，证据只存执行身份、真实 SQLSTATE、ACL 和原生数据。

## 检查范围

- PGT-006 以独立可回滚连接执行对真实 B `heads`、`versions` 的完全限定 SELECT、INSERT、UPDATE、DELETE。B 用原普通登录读两表，并先推进独立的合成 B-only 序列。A 试图执行该序列的 `nextval` 后由控制连接再次读序列状态。
- PGT-007 用同一 A 登录在各自连接中尝试原生 `SET ROLE B` 和 `SET SESSION AUTHORIZATION B`。回滚到 savepoint 后在同一连接继续读取原流水并尝试直接更新 B。另一事务把 `app.tenant_id` 设为 B，记录实际 GUC、`session_user`、受控 helper 和真实同键原流水可见性，再执行直接更新及原 store。
- PGT-009 尝试在 `public` 建表、建同名函数、创建临时表和替换真实 helper。另在 A 有 USAGE 的 schema 由 owner 用现行默认 ACL 建一张合成未授权表，A 直接查询。目录原件保留真实 helper 定义、SECURITY DEFINER、固定 `search_path`、owner、EXECUTE ACL、数据库/schema/default ACL、共享表 FORCE RLS、策略和成员关系。

## 原生结果

`D:/CP6-local-test/CP6-72H-20261109/W417-01/native-01/checks` 保留每条原生输出、错误及退出状态。`actual.json` SHA-256 `bd932770504241e0a1829e994580624e26621d2577dbbd1f3e2a7798c21dc753`。正常验收入口与外层运行器均退出 0。

- **PGT-006**。B 的普通 LOGIN 实际读到自己的一个 head 和一个 version，和控制连接的原行一致。B 的合成序列第一次 `nextval` 返回 101。A 对两表各四条完全限定命令及序列共九次实际 `42501`，每条 psql 退出 3。独立控制连接在前后都读到序列 `[101, true]`，没有被 A 推进。原业务数据和实际权限目录在前后保持一致。
- **PGT-007**。真实 A/B 登录的成员链为空。对带空格和引号的真实 B 角色，`SET ROLE`、`SET SESSION AUTHORIZATION` 各返回 `42501`。恢复到各自 savepoint 后，同一连接的 `session_user` 和 `current_user` 都是 A。共享键 `0a9f27da-69d2-4cfd-b9ab-77de0e1cf5ca` 在 A 是 `COMMITTED`，B 是独立的 `CLOSED`。A 只见自己的两行，B 普通会话看到自己的原 `CLOSED`。A 设置 `app.tenant_id=B` 后，原 helper 仍返回 A，B 同键计数为 0。直接 UPDATE 返回 `42501`。经原 store 以 B 为目标写入实际到达 `WITH CHECK`，返回 `42501 new row violates row-level security policy`。
- **PGT-009**。五条创建、替换及读取未授权表的原生命令均返回 `42501`。独立观察确认 A 仍有其 schema USAGE，未知表的 SELECT 拒绝来自关系本身。真实 helper owner 为 `cp6_tenant_lookup_v1`，该角色 NOLOGIN，函数 SECURITY DEFINER，配置为 `search_path=pg_catalog, cp6_tenancy_v1, pg_temp`，PUBLIC 无 EXECUTE。把调用者路径排为 `pg_temp, public, cp6_tenancy_v1, pg_catalog` 并伪造 B GUC 后，限定和非限定调用都仍返回 A。
- A/B 业务行、原迁移、原控制和 admission 前后输出 SHA-256 同为 `de0b644c1c8b748df2f47704106568846a064f5f3a474289c38047728279cef5`。共享原流水包括 A、B 与其他 Owner，4,058 字节全部原生 COPY 前后相等，SHA-256 `0ffed48c01ef7fc320d9cdf0143591bbb34880b2fd209232c9933096048c2e03`。ACL、角色、helper 和策略目录前后 SHA-256 同为 `12a7769b7e578c7e3d40b1f55b252e57e680d6a7180273bb6f73cb3b9b6850fe`。

## 停止与边界

专属 PostgreSQL 18.6 前两次物理可用内存 1,251/1,254 MiB。独占检查后的实际样本为 1,229 MiB，运行最低物理 1,070 MiB、可用提交 20,560 MiB。整个原生窗口 11.381 秒，18:07:43.285 UTC 退出 0，距 START 780.417 秒。postmaster 14112 已停止，`pg_ctl status` 返回 3，独立复核 55564 无监听且 pidfile 不存在。临时 HBA 已移除，原 W413 HBA 和控制文件摘要一致。精确可处置 PGDATA 为本窗的 `candidate-clone/data`，同级证据保留。

本次使用合成业务数据和真实 PostgreSQL 权限，范围是已关闭的 RMA Core 候选中的三张卡。合成序列和未知表只属于任务副本。没有运行宿主、CLR 或生产切换。其他 PGT 的范围仍由各自门禁验收。候选分支 `codex/w417-01`，正式卡更新、合并及 DEV-1 释放由 COORD 办理。[原验收卡](../../acceptance/postgresql-tenancy.json)
