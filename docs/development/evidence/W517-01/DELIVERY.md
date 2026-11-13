# W517-01 · 同源 v11 归档期间的普通 B 写入

Main 基线 `7783e38adc59b8213e0c1257d06ae102a274cf81`，验证器源 `8cfd8ec768a20d5256704cc92e4a50ce2bf0772f`。本地 PostgreSQL 18.6，复用 W477 的停机合成数据副本、原归档及 FileContent，保留原物理身份和固定源端口 55567。目标为本轮新建独占 PostgreSQL 集群 55594。原始源始终停机，原控制文件、HBA、归档及资产复核一致。 [原 v11](../W477-01/DELIVERY.md) · [验证器](../../../../eng/wp02-02/rma-tenant-archive/verify_concurrent_v11.py)

- A 快照 `00000005-00000002-1`、水位 `1424:1424:` 由实际后端 22952 持有。同库独立 B 普通 LOGIN 后端 29128、xid 1424 调用原 RMA `create_draft` PostgreSQL 写入入口。原角色映射锁定 B，未增加成员或授权。A 持有同一快照时 B 提交，另一个普通连接读到 B 恰新增一份草稿头、版本和同键 COMMITTED 回执。原 B 行一致，`pg_visible_in_snapshot` 明确显示 B xid 在 A 水位之外，B 直接读 A schema 得 42501。
- 新 v11 清单 `3d40a047da6a1f82a4f71c6a7dbdfedb7fc2c095e4c6961359fe73fa47cf4c89` 与原 v11 的 40 份 A CSV 全部字节一致，原生 `pg_restore --data-only` 的 RMA 逻辑数据一致。A/B 普通登录在暂存之后回读各自预期状态。直接数据库写入实证覆盖正常 B 的受限 PostgreSQL 路径，B 的 HTTP 权限场景沿既有 W364/W372 独立引用。 [RMA 现有托管写入](../W372-01/DELIVERY.md)
- 独立目标系统标识 `7707440130500263524`，来源为 `7706117870886737900`。使用原 v11 暂存器写入并按相同清单读取原回执，两次字节一致且 `recoveryReady=false`。目标 RMA heads/versions 精确等于 A，14 个 Stock、8 个 ERP 和 1 个原 journal CSV 从 PostgreSQL 逐字节回读。B 普通角色不能连接候选，候选无 B schema、该次 B 操作、活动 Stock/ERP schema 或开放记录。

最终 `native-02` 退出 0。首次新集群缺少原归档 ACL 引用的 journal executor 角色，暂存遇 42704，原失败完整保留。第二次沿用原暂存测试的 NOLOGIN 角色准备，未开放 CONNECT 或角色成员。源与目标两个自有实例均已停止，原生 PID、结果、暂存回执和复现命令位于 `D:/CP6-test-artifacts/W517-01`。集中审查覆盖实际快照与独立写入、原租户边界、固定归档及隔离候选。

PGT-021 已补合法 B 并发和关闭暂存。现行 Stock 业务账导入、更多 ERP 原业务图以及批准切换依然是正式剩余。PGT-022 和 PGT-024 的未覆盖范围继续沿原验收。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘业务 60/60。Actions 触发和取消为零，部署为零。 [上轮组合恢复](../W481-01/DELIVERY.md)
