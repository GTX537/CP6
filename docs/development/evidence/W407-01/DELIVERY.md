# W407-01 · RMA v10 的 Main Stock 中立暂存

DEV-2 于 2026-11-09 15:08:43.653165 UTC 从 #845 `33e60ed3ef7f12d7ba238963ee780f8b9244575f` 开工。正常 `stage.py stage` 接收已固定清单摘要的 W405 同源 v10 归档及原私有资产根。14 张 Stock 中立关系在现有 RMA、ERP 和隐私暂存之后创建和载入，随后核对原件并在同一 PostgreSQL 事务写入回执。回执沿用 `STAGED` 和 `recoveryReady=false`，v10 附带完整 `stockOutbox` 原清单段。`recover` 使用原物理目标、租户和摘要读取持久回执。[暂存入口](../../../../eng/wp02-02/rma-tenant-archive/stage.py) · [W405 同源归档](../W405-01/DELIVERY.md)

中立表沿用原字段类型、可空性、主键、唯一键和单个开放 DispatchRun 的部分唯一键。文字按 PostgreSQL C 字节比较，`varchar` 保留原包可表达的范围，源库排序元数据随原清单段保留。`source_xmin` 作为历史 `xid` 存储。原 Source→journal、Native、route、claim ticket 和 seal 关系由原生约束与共享图共同校验。共享图沿用原 Stock Owner、原输入和结果摘要、root/epoch、已记录的派送原件及可空 claim 字段。中立原 journal 另外限制为 Source 引用的记录。[Stock 中立 SQL](../../../../eng/wp02-02/rma-tenant-archive/stock_stage_v10.py) · [共享原件图](../../../../eng/wp02-02/stock-outbox-recovery/stock_outbox.py)

## 本地验证

- 基线实际用 W405 v10 固定包调用正常 CLI，退出 1，返回 `rma-stage-bundle-member-rejected`，没有生成数据库驱动。原件为 `test-red.log`、`test-red.exit`。
- COORD 对同一个实际 v9 包生成 #845 和本次的原生还原驱动，46,531 字节逐字节一致，SHA-256 `e5b7116d4104f1744a0ebcd2ba54aae2a58e1aa0a6e6eee29da70d59049548e9`。正常 v10 路径生成 14 张 Stock 表和 COPY，默认 `checked_bundle` 调用仍按 v1–v9 门拒绝完整 v10。较早版本复用 W404 的 16 个 stage/recover 驱动对照。原 live Stock 图对 W405、W403 A 与 B 的 SQL 均与 #845 逐字节相同，每份 6,301 字节。[W404 旧版验证](../W404-01/DELIVERY.md)
- 主输入为 W405 A `938f243e-9c7a-4e22-bf1a-d7e8ea1e6eca`，清单 SHA-256 `7ac25b3c3eae626277c03802e29203e7cf1f7912664e05f92aeaba817453b6e9`，原资产来自 W402 私有目录。W403 A `ec5e5a7f-d305-4397-8d77-517ac44a386d`，清单 SHA-256 `376a0f9fb2a82d7d3ee670516b0f817c14bdabff68e23ea8d9bee8b458b1f56d`，作为另一原物理库的 Published 组件输入。[定向实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_stock_stage.py) · [W403 原包](../W403-01/DELIVERY.md)

- PostgreSQL 18.6 实际运行最终夹具，退出 0。缺原资产根在连接前拒绝。14 份 Stock CSV 全部加载后注入错误的 binding 摘要，原生返回 `P0001 rma-stage-stock-originals-rejected`。目标原租户及中立 schema 均未落地，同键恢复查不到回执。正常 CLI 随后以原包、原 pin 和同一物理目标成功暂存。夹具丢弃 9,379 字节 CLI stdout，`recover` 返回与数据库中原回执相同的字节。更换 pin 恢复拒绝。目标为 `127.0.0.1:55564/w407_v10`，OID `16386`，系统标识 `7706195826605272412`。恢复输出文件 SHA-256 为 `491936a7537b7f48d75420b281b6738d1733e4b2ab989a35930f880e39bf0a92`。
- W405 的 14 个 Pending 成员逐字节读回，原生目录核对类型和主键/唯一键。实际 A 一条未领取 Pending，`ClaimEpoch` 为 NULL，原 route epoch 为 1，64 位 lock key 为 `4850436515310083011`。原 RMA journal 两条（已提交与关闭各一）及 3 条隐私审计、3 条 legal hold 同样逐字节读回。原生拒绝异租户及异 root 值（`23514`）和未知 NativeId 的 seal（`23503`）。A/B 普通 LOGIN 实际因 CONNECT 权限被拒，有效角色的中立 Stock SELECT 各返回 `42501`。
- 同一任务 PostgreSQL 的另一隔离库 `w407_published_component` 使用同一中立 SQL 验证 W403 的 14 个原始成员，全部逐字节读回且原生状态为 Published。实际 Run、Claim、Ticket、Mutation、Send 各 1，Audit 和 Seal 各 2，DLQ 为表头。组件保留其独立源物理和租户，无暂存回执。两个 Stock 样本各含一条 `Main.Wms.StockOperation.v1` 的 COMMITTED 原件。

原始记录在 `D:/CP6-local-test/CP6-72H-20261109/W407-01`。最终 `test-native-05.exit`、`test-native-05.time.json`、`test-native-05-memory.jsonl` 和 `rma-stage-v10-20261109T153533-f4a8e9fa` 含执行、回执、目录、SQLSTATE 和原字节。实际连续预检 808/806 MiB，运行最低 622 MiB 物理和 20,081 MiB 可用 commit，32 MiB shared buffers。19.36 秒的最终实库运行于 15:35:53.068838 UTC 完成，距 START 27 分 09 秒。postmaster 47796 正常停止，`pg_ctl status` 为 3，55564 无连接状态或监听。

首轮实库保留测试客户端写文件和导出 stdout 的 CRLF/LF 差异。下一轮保留旧 Python 3.8 测试助手拒绝 PostgreSQL 五位小数时间戳的错误。两轮均停机。修复仅在本任务夹具改用原生 COPY stdout，不转换 CSV。旧实例的随机测试密码已丢弃，续跑使用新合成库。启动前的保守端口门、两次内存门超时和一次私有启动脚本路径更正保留在任务目录，未启动数据库。

证据覆盖合成中立暂存及回执恢复。现行 Core 候选默认版本门、当前业务对账与正式 PGT-021/022 保留各自验收。`codex/w407-01` 本地保存，DEV-2 交接时工作树干净，席位由 COORD 在远端采用核实后处置。
