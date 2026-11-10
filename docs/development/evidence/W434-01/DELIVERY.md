# W434-01 · PostgreSQL IAM 角色复合引用

DEV-1 于 2026-11-10 02:04:21 UTC 从 Main #867 领取。交付原 WP02-02 / PGT-027 的共享 IAM 角色引用。源提交为 `fe97268898b9804b0732ca8f719ebd885c04fd4b`。`Sys_UserRole(TenantId,RoleId)` 引用 `Sys_Roles(TenantId,RoleId)` 的原主键，约束已验证，使用可延迟的 `NO ACTION`。既有成员关系主键、唯一索引、用户外键和禁用角色历史均保留。旧无效引用使迁移事务失败，原行留待前向处置。[原验收](../../acceptance/postgresql-tenancy.json)

## PostgreSQL 18.6 实测

| 路径 | 实际结果 |
|---|---|
| 旧键结构与同一混合 COPY | 已有用户复合外键。合法 A 用户引用 B 独有角色的行可提交，复现缺口 |
| 原迁移和原生前后查询 | 原有无效行使 ALTER 返回 `23503`，DDL 和全部原行保持一致。仅从合成夹具撤回原两行后，原 Up 成功安装验证约束 |
| 新键结构与同一混合 COPY | 两行暂存后提交返回 `23503`，A/B 完整行及键恢复原值。合法 A COPY、禁用历史角色、同事务先写成员后补角色均成功 |
| 原生 UPDATE 尝试将成员改归 B | 更新同时使用合法 B 用户，但角色只在 A。提交返回 `23503`，写集恢复原值 |
| 普通 A 登录的角色与用户混合 COPY | 同一真实约束夹具以 SCRAM 登录，分别在 xid `767`、`768` 触发两条复合外键的 `23503`。独立查询为 `aborted`，每次完整写集一致。xid `769` 的同号合法 A 角色行提交，B 及原键保持一致 |

普通登录仅拥有合成夹具成员关系的指定 INSERT 列权限。`session_user` 和 `current_user` 均为 `w434_runtime_a`，无共享表读权、TRUNCATE、所有者角色成员资格或管理权限。观测连接独立。临时登录及授权已移除，物理集群 `7706359519759461916` 已停止。[旧键及确切 Up](/D:/CP6-local-test/CP6-72H-20261109/W434-01/native-green/result.json) · [普通登录、SQLSTATE 与 xid](/D:/CP6-local-test/CP6-72H-20261109/W434-01/ordinary-keys-02/actual.json) · [停机](/D:/CP6-local-test/CP6-72H-20261109/W434-01/ordinary-keys-02/stopped.json)

## 正常 Core 和 IAM

定向编译复用 W428 已锁定的项目与依赖输入，使用 .NET SDK `8.0.424`、EF `8.0.30`、Npgsql `8.0.8`。Provider 包含 229 个迁移标识，SHA-256 为 `317e32f59ce9e5d4308743daada496a11d2a89dd4f840b40537755cad12d4096`。隔离数据库 `tenant_management_component`、OID `16385`、集群 `7706131781016682348`。正常 Core EF 将迁移历史从 225 增至 226。两个实际 IAM 用例验证原回执、身份 Outbox、审计和禁用历史角色保留。原模拟登录 HTTP 用例实测用户和角色两条错属引用各自全回滚，随后继续验证会话、Cookie、菜单及审计。[正常 EF](/D:/CP6-local-test/CP6-72H-20261109/W434-01/host-01/upgrade-actual.json) · [IAM](/D:/CP6-local-test/CP6-72H-20261109/W434-01/host-01/iam-actual.json) · [HTTP](/D:/CP6-local-test/CP6-72H-20261109/W434-01/host-02/impersonation-actual.json)

首次宿主回归遇到旧夹具固定菜单编号 `71026` 冲突。改用本次夹具专属编号和键，复用已通过的迁移和 IAM 证据，只重编并重跑模拟登录。普通登录在完整 Core 副本先遇到现有认证、权限守卫的 `42501`，受控诊断所用的临时权限已撤销。普通 COPY 使用同一份原键结构和实际 Up 的独立夹具，完整 Core 保留现有 Owner 守卫。私有驱动保留首次选择器排序规则错误和首次输出名冲突的记录，重跑时复用各自的停机边界。W428 受保护来源和 Core 副本的 SCRAM 配置保持原摘要，两套本次数据库均已停止。[首次宿主诊断](/D:/CP6-local-test/CP6-72H-20261109/W434-01/host-01/impersonation.stderr) · [Core 停机与原件](/D:/CP6-local-test/CP6-72H-20261109/W434-01/ordinary-03/stopped.json)

结合 W367 的同 schema COPY 和 W430 的同 schema 原 ACL、归属 CHECK 与合法更新，W428 的用户引用和本窗口的角色引用覆盖原 PGT-027。普通角色对共享 IAM 的完整行级授权继续由 WP02-02 对应范围交付。COORD 已审查迁移事务、禁用历史、原键、真实 Owner 和普通登录的证据边界，以及工作流触发和固定席位交付条件。[W430](../W430-01/DELIVERY.md) · [W428](../W428-01/DELIVERY.md)
