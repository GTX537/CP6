# W358-01 · RMA 租户并发索引恢复

2026-11-09 08:35:12 UTC 在 DEV-2 开工，分支 `codex/w358-01`，登记基线 `ca97fd003044b15c8caa676eda732e4ad816e83d`。本窗覆盖 PGT-019 中逐租户非事务建索引和恢复。 [PGT-019](../../acceptance/postgresql-tenancy.json#L525) · [W353 初版](../W353-01/DELIVERY.md)

## 交付

`migrate-rma-core-outbound-index-v1 --stdin` 为已 commissioning 的 Core 租户 `heads.outbound_id` 增加具名 B-tree。入口复用准确物理目标、原安装、普通登录目录和初版 Ready/history 核验。步骤保存初版操作、表 OID、当前操作与固定脚本摘要。无池控制连接用同租户会话锁串行执行，先提交 `Building` 原步骤，再单独运行 PostgreSQL 并发 DDL。 [CLI](../../../../tools/CP6.TenantStorage/README.md) · [实现](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.OutboundIndex.cs)

中断后用同键检查操作派生暂存名和原生定义。无效索引的确切 OID 先持久为 `Dropping`，再并发删除并重建。有效暂存索引在短事务内改成 `rma_heads_outbound_id_v1`，改名后同事务读取实际 canonical OID 与有效状态，核对后才保存 `Applied` 和原回执。重放核对最终对象和持久 OID。操作窗口将目标 schema 的特权 DDL 交给本控制入口。 [PostgreSQL 18 并发建索引](https://www.postgresql.org/docs/18/sql-createindex.html) · [进度阶段](https://www.postgresql.org/docs/18/progress-reporting.html) · [并发删除](https://www.postgresql.org/docs/18/sql-dropindex.html)

## 实际验证

Windows、.NET SDK 8.0.424 和 PostgreSQL 18.6，隔离端口 55567。复用 W351 发布宿主 `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 的 DatabaseInit，实际 Core、Space、IdentityPriority、ERP 迁移历史分别为 224、1、1、1，逐项匹配原发布源码。A/B 业务行和普通登录均为模拟。正向库 OID 16386，system identifier 7706091216123823976。锁定还原使用现有全局包。最终 CLI Release 为 0 错误、23 条既有告警，Core DLL SHA-256 为 `d868684e4798e9a99d6101c4449c01a41df744a9711913656617592523b6f31c`，CLI DLL 为 `7c99e33c35ee555d115a5fe242a9d96c4ea2745d39fef7b1b6e69021dc59f70d`。 [原生夹具](../../../../eng/wp02-02/rma-tenant-migration/verify_outbound_index.py)

| 场景 | 实测结果 |
| --- | --- |
| 并发创建中断 | A 的 heads OID 43511。实际 PostgreSQL 后端 37028 在 `waiting for writers before build` 等待 A 写锁 61140。目录中暂存 OID 43599 为 invalid、未 ready、仍 live。精确终止后 CLI exit 4，`Building` 原键保留，A/B 普通登录在等待期间读回各自旧结果 |
| 并发清理中断 | 同键先持久绑定无效 OID 43599。DROP 后端 50880 在 `virtualxid` 等待普通 A 读事务 51708，B 普通读继续。精确终止后 exit 4，`Dropping` 及同一 OID 留存 |
| 同键恢复和旧版本 | 继续原键安装 canonical OID 43601，valid、ready、live，仍索引表 OID 43511。恢复和再次重放的回执逐字节一致。A/B 旧行、初版 Ready/history 和原 commissioning 回执均一致，B 无新增步骤或索引 |
| 拒绝边界 | 真实物理克隆、错误数据库 OID、错误安装、普通登录执行、同租户竞争和异键、预存同名索引、错误暂存定义、应用后缺索引和同形不同 OID 均 exit 1。冲突对象保持原样 |
| 改名竞态 | 集中审查使用私有事件触发器，把实际产品改名暂停在按名取锁前。修正前原暂存 OID 43584 被同形 OID 43585 换入，旧候选错误 exit 0，最终名指向 43585，回执仍写 43584。修正后同一竞态 exit 1，改名回滚，`Building` 无成功回执。恢复原有效暂存后，同原键应用 43584 并逐字节重放 |

## 原件与收尾

原件根为 `D:/CP6-local-test/CP6-72H-20261109/W358-01`。并发创建和清理实绿为 `rma-index-20261109T084937-8d214c24/outbound-index`，含 `create-waiting.json`、`drop-waiting.json`、`A-invalid.json`、`A-drop-interrupted.json` 和 `final.json`。后续集中审查只改最终改名后的同事务核验，沿用前述创建和清理输入。改名 RED 为 `rma-index-20261109T085330-14129c21/outbound-index`，最终定向通过为 `rma-index-20261109T085602-e1aa514e/outbound-index`。最终 `rename-race-final.json` 于 08:56:39 UTC 写入，距开工约 21 分 26 秒。

首次种子辅助函数用控制身份误入普通读验证，调整夹具后 W353 基线真实初始化并对新命令返回 exit 2。首轮候选在真实创建中断后因目录 JSON 的 OID 类型比较失败，改为明确数值比较后通过。保留这些失败与诊断。六个私有实例均有停止原件，55567 无监听，本任务进程为零。

按 `verify_outbound_index.py --help` 配置现有 W351 发布 DLL、其源码 SHA、候选 CLI、任务私有根和专用端口。直接运行覆盖创建与清理中断，追加 `--rename-race` 覆盖最终改名与有效暂存恢复。

索引列对应实际 RMA List 的来源过滤。现行 W356 租户读取仍采用首版 Ready 和两张原表，本窗没有启用租户 HTTP list。PGT-019 的共享升级失败和版本路由矩阵留待原验收。Actions 触发、取消、工作流变更及部署均为零。

本地候选和干净 DEV-2 交 COORD 执行远端交付与席位释放。
