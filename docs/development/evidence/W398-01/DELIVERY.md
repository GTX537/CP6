# W398-01 · ERP v8 原子私有暂存

DEV-1 于 2026-11-09 12:55:11 UTC 开工，基线 #836 `bfa3c2701bb37249da3ca8e420811aae332cc65a`。[W395 原 v8 制品](../W395-01/DELIVERY.md) · [W394 v7 暂存](../W394-01/DELIVERY.md)

`stage.py stage` 通过现有外部 pin 和资产根门核定 v8。八张原 ERP 表以 `erp_` 前缀进入 `rma_archive_stage`，避开 Identity Priority 的同名表。列、原类型、原空值约束、原主键及唯一键按共享 ERP 描述和 PostgreSQL 迁移生成。命令键与原 audit/bridge 字段使用原 `C`，Outbox 使用现有暂存 ICU 定义。原结果文本保持 BPCHAR，保留原载荷、租约、时间、RowVersion 和 replay audit 输入 token。[暂存入口](../../../../eng/wp02-02/rma-tenant-archive/stage.py) · [原 ERP 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/ErpIntegration/20261002151345_PostgreSqlErpIntegrationBaselineV1.cs)

八次 `COPY`、精确租户检查、v8 计数和原 `STAGED` 回执在同一原生事务。回执增加 `erpIntegrationCounts`，仍按同一物理目标、租户和摘要只读恢复，`recoveryReady=false`。`v1-v7-driver-compatibility.log` 按真实 v1–v7 原件逐一比对 #836 与当前生成的 stage/recover 驱动，共 14 份逐字节相同。`erp-migration-shape.log` 逐列核对 v8 原类型、空值和排序规则。

## 实库

原件在 `D:/CP6-local-test/CP6-72H-20261109/W398-01/rma-stage-v8-20261109T130023-a8d9815e`。实际 PostgreSQL 18.6，专属 55564，启动门记录物理可用 801 MiB、虚拟可用 20697 MiB。使用 W395 A manifest SHA-256 `ef9876aa2ec112952c9793eb77bda0670b3b1200c85784061867ba36ba424bda`、B `984bab4a2f745eb9d0636df1cb17bf5611a74755cd12507d355f77a3b9e13c57` 和原空资产根。单个纯 PostgreSQL 夹具，两套隔离数据库。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_erp_stage.py)

- 在 A 的八表 COPY 后、写回执前，仅在测试驱动注入大写及尾空格的 Outbox 原键变体。PostgreSQL 原生 23505 命中 `erp_Cp6_OutboxMessage_MessageId_key`。目标无半成品 schema，原键 recover 返回无回执。按同一目标和 pin 正常 CLI 重试，丢弃 stdout，再恢复与数据库持久化回执完全相同的原字节。`v8-recover.stdout` SHA-256 `433e508b715479b0238d6451a18a88435c7e774a70cf922d75f69d5c0df4f848`。
- A 的八份原生 COPY 回读逐字节等于原成员，原键列逐表与实际目录一致。真实 CommandInbox 大小写变体可并存，检查事务回滚。原键加尾空格触发 23505。B 两张空表的原生回读只含原表头。B `v8-stage-b.stdout` SHA-256 `a8b9579195768aee797b4076f1aa56753a49f4f5a0c094e175a3991d54b947f7`。
- `test-native-01.log` 在以上验证完成后因测试误写 B 登录名得到原生 42704，完整脚本退出 1。更正为原夹具的实际带引号登录名后，沿原两个精确目标定向只读复测。`test-native-acl-recheck.log` 退出 0，两个目标的原生 ACL/停用范围均返回 `t`。复测使用专属临时回环 HBA，原 HBA 摘要一致，临时副本已移除。
- 原 postmaster 6824 与复测 30148 均已停止，55564 无监听，PID 文件不存在。**可丢弃 PGDATA** 是原件目录下的精确 `data` 子目录，测得 77,859,769 字节。其余同级原件、`native-acl-recheck`、驱动比对和真实失败日志作为证据保留。无本地发布或额外数据库副本。

本窗交付隔离暂存和原回执恢复。当前全局键对账、重放、业务启用及生产切换由独立流程处理。PGT-021/022 保持 `NOT_RUN`。本地分支 `codex/w398-01`，集中审查、合并及 DEV-1 官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
