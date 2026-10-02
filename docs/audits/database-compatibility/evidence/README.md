# WP1 原始本地双库证据

2026-10-02，任务 [DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134)。边界及命令见 [WP1-PROBE](../WP1-PROBE.md)，方案冻结与后续安装清单见 [WP1-DECISIONS](../WP1-DECISIONS.md)。这些是限定表实验，不是完整模型、迁移、业务或生产验收。

本目录从 Git 忽略的本机 `tmp` 逐字节复制原始 JSON，经过无凭据检查；不重新序列化或改写原始 SourceState/失败结论。`.gitattributes` 禁止这些 JSON 的换行归一化，以下 SHA-256 可直接复核。

| 文件 | 实际结论 | SHA-256 |
| --- | --- | --- |
| [wp1-sql-reviewed.json](wp1-sql-reviewed.json) | SQL Server 28 Passed / 1 NotApplicable / 0 Failed / 0 Blocked | `2F96A6FBC4366DCA99707A1C654F084BBC063D320E01FDDD06C0123E1756CD4A` |
| [wp1-postgresql.json](wp1-postgresql.json) | PostgreSQL 18.6，31 Passed / 1 Rejected / 0 Failed / 0 Blocked | `FC650F4A27724ED9331AD8C87071AE7AD11FD29EBED960486A8EE5C7F35CDCB9` |
| [wp1-sql-owner-missing.json](wp1-sql-owner-missing.json)、[owner-mismatch](wp1-sql-owner-mismatch.json) | 必须失败；数据库所有权 gate 生效，未尝试 schema | 原始文件内保留结果 |
| [wp1-postgresql-owner-missing.json](wp1-postgresql-owner-missing.json)、[owner-mismatch](wp1-postgresql-owner-mismatch.json) | 必须失败；未尝试 schema | 原始文件内保留结果 |
| [wp1-sql-setup-negative.json](wp1-sql-setup-negative.json)、[PG setup-negative](wp1-postgresql-setup-negative.json) | 预期真实中途 DDL 失败，事务回滚后 schema 不存在 | 原始文件内保留结果 |
| [wp1-sql-negative.json](wp1-sql-negative.json)、[PG negative](wp1-postgresql-negative.json) | 故意遗漏旧 token 谓词，真实陈旧覆盖导致预期失败；随后清理 | 原始文件内保留结果 |
| [wp1-database-cleanup.json](wp1-database-cleanup.json) | 精确所有权、剩余对象/连接为 0；两库 DROP 后 catalog 均确认不存在 | 原始回执内保留结果 |

两份最终正向报告的 26 个源码输入、9 个实际运行程序集哈希及 input fingerprint 完全相同：`0F0B443961674D40B8163768225AE12D1A55CD18F28FCE0D5F16BB0CB91C6BB3`。SQL 的 SourceSha 为构建时基线 `685a5238`，工作树含实际 WP1 改动；不把该值称作最终 exact-source 提交。检查点 `d30da70e6698b73ceac0d23c16f16dc02d9b8429` 在 PG 启动前保存相同代码；PG 动态 SHA 正确，CLI SourceState 保留准备阶段旧假设。以真实哈希、检查点及此说明关联，未手工伪造报告或为 SHA 变化重跑成功结果。

Platform NuGet 0.10.2 与它既有的 `0.8.0-alpha.2+fbcd215...` 程序集 metadata 在报告中分开保留。应用候选 Rejected 与有意负对照失败属于比较证据，不作为必需正向场景失败或跳过。

Provider/接线 RED/GREEN TRX、历史初次探针、实际 compile failure 与修正 build logs 仍保留本机忽略目录；适用计数/哈希见 WP1 记录。本目录不包含测试连接、随机账号密码、机器运行配置或部署凭据。专用 PG 测试角色保留供后续独占临时库验证；原有管理员/服务/认证/业务库均未变更。
