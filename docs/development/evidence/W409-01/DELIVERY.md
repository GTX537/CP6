# W409-01 · RMA v9/v10 只读对账兼容

DEV-1 于 2026-11-09 16:02:57.480891 UTC 从 Main #846 `be7e044c1ac08652369acdf7c75557868a22a3bd` 开工。三个现有对账命令接受由完整归档验证器校验的 v9 和 v10。IAM 支持 v6–v10，Identity Priority 支持 v5–v10，ERP 支持 v8–v10。命令沿用原报告格式、独立 manifest SHA-256、原资产根、实际物理库和现行读权限。IAM 查询原参与者图，Identity Priority 查询 Inbox 和 Outbox 原键，ERP 查询 CommandInbox、Outbox 和 OrderBridgeDispatch 原键。[IAM](../../../../eng/wp02-02/rma-tenant-archive/reconcile_iam.py) · [Identity Priority](../../../../eng/wp02-02/rma-tenant-archive/reconcile_identity_priority.py) · [ERP](../../../../eng/wp02-02/rma-tenant-archive/reconcile_erp_deliveries.py)

## 固定输入和实测

| 原包 | Manifest SHA-256 | 归档快照水位 |
| --- | --- | --- |
| [W402 v9](../W402-01/DELIVERY.md) | `e1350c68ebc5618df6124384442630d3bba88186d8ae018b7a9fc2af4801e3bd` | `1304:1304:` |
| [W405 v10](../W405-01/DELIVERY.md) | `7ac25b3c3eae626277c03802e29203e7cf1f7912664e05f92aeaba817453b6e9` | `1332:1332:` |

两个原包使用同一 [W402 原资产根](/D:/CP6-local-test/CP6-72H-20261109/W402-01/run-20261109T134906Z/checks/private-assets)，固定同一合成 A 租户和 installation。原 PostgreSQL 目标为 `127.0.0.1:55567/w374_live`，数据库 OID `16386`，系统标识 `7706139311168619192`。从正式移交的 W408 现有副本运行 PostgreSQL 18.6，另用一次性 HBA 放行已登记的两个本机合成角色。入口使用真实 Python 3.8.6 CLI 与 PostgreSQL 会话。所有报告记录 `REPEATABLE READ READ ONLY`、当前水位 `1340:1340:`、`OBSERVED` 和 `recoveryReady=false`。[物理核对](/D:/CP6-local-test/CP6-72H-20261109/W409-01/native-01/native-guard.stdout)

- 修改前，两个固定原包分别被三个真实入口在版本门以 exit 1 拒绝，均未建立 PostgreSQL holder 或发布报告。修改后，两个原包的完整校验和实际输入成员提取共六次通过。三个入口的旧白名单与 `--help` 一并核对。[原拒绝](/D:/CP6-local-test/CP6-72H-20261109/W409-01/red/results.json) · [离线验证](/D:/CP6-local-test/CP6-72H-20261109/W409-01/verified-inputs.jsonl)
- 两个原包的六次实库调用均 exit 0。每个版本的 IAM 都得到 17 条 `UNCHANGED`。Identity Priority 都按原键保留同一条 `Published → Published` Outbox，Inbox 为空并执行实际权限查询。ERP 三类交付键均为空并执行实际权限查询，资产引用同样为空。[六份报告索引](/D:/CP6-local-test/CP6-72H-20261109/W409-01/native-01/results.json) · [验证退出](/D:/CP6-local-test/CP6-72H-20261109/W409-01/test-native-01.exit)
- 已存在的普通 `w408_stock_limited` 角色通过同一个 v10 原包进入实际 ERP 会话，PostgreSQL 在 `erp_integration` schema 权限处返回 `42501`。入口 exit 1，且无最终或暂存报告。前后在独立只读快照核对 32 个关系的行数和 SHA-256，覆盖 A/B 的 RMA 行、租户迁移和登录映射、IAM、共享原操作、Identity Priority、ERP 和文件引用，结果一致。[原生拒绝](/D:/CP6-local-test/CP6-72H-20261109/W409-01/native-01/v10-erp-restricted/holder.stderr) · [前读](/D:/CP6-local-test/CP6-72H-20261109/W409-01/native-01/before.json) · [后读](/D:/CP6-local-test/CP6-72H-20261109/W409-01/native-01/after.json)

本窗的非空覆盖是 IAM 和 Identity Priority Outbox。非空 ERP 三原键、状态变更及租约对账有 W399 的 v8 实库证据，A/B 的 v8 IAM 和 Identity Priority 对账沿用 W400 的实际结果。本窗范围为原源库的只读对账，正式 PGT 和恢复采用仍按各自入口单独验收。[W399 ERP](../W399-01/DELIVERY.md) · [W400 IAM 和 Identity Priority](../W400-01/DELIVERY.md)

## 实物和收尾

私有验证脚本以 red 或 native 模式调用三个正式 CLI，记录完整原始退出和报告。启动前两份物理内存样本为 2010、2031 MiB，可用提交量为 21042、21066 MiB，Windows 环回独占绑定通过。专属副本 postmaster `36420` 的启动至停止核对窗口为 16:12:31.919316 至 16:12:38.859646 UTC，约 6.94 秒。`pg_ctl status` 返回 3，独立复核 PID 和 55567 本地连接状态为空。W405 原库持续停机，原 `pg_control` 和 HBA 摘要一致，移交副本原 HBA 摘要一致。[私有验证脚本](/D:/CP6-local-test/CP6-72H-20261109/W409-01/verify-reconcile.py) · [资源门](/D:/CP6-local-test/CP6-72H-20261109/W409-01/native-01/preflight-launch.json) · [停止和摘要](/D:/CP6-local-test/CP6-72H-20261109/W409-01/native-01/stopped.json)
