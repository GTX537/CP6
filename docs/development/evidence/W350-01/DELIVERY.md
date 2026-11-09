# W350-01 · RMA 租户 schema 首版迁移

2026-11-09 在 PostgreSQL 18.6、.NET SDK 8.0.424 与运行时 8.0.30 上通过新隔离 profile 的真实 CLI 验收。源码基线为官方 DEV-1 的 22bf06da325c099c6d2b894c7f9e7c748f85d622，开工 07:25:50 UTC。本地交付依据为 Release 编译与下列 PG 证据，环境与数据均为模拟。

## 改动

- 新增 PostgreSQL-only rma-application-tenant-v1。一次性 bootstrap 核对 IP、端口、数据库名、OID、cluster system identifier 和实际控制身份，拒绝已有共享 RMA、combined 或其他非空布局，复用可信 LOGIN 目录。安装目标随 bootstrap 持久化，复制出来的目录或控制表不能取得另一物理库的授权。现有迁移与默认 shared Owner 的 git diff 为空，原历史迁移 blob 为 037ea53344edf378d630c032198908f6b9e61f5b。[控制与迁移](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.cs) · [原 RMA 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261030022000_RmaApplicationV1.cs)
- 后续迁移只独占本租户与 lineage 控制行。DDL、tenant-local 历史、共享 Ready 和原回执同事务提交。保留原 heads/versions 的租户复合 PK/FK、延迟头版本引用、JSONB、状态和不可变版本。增加固定租户 CHECK。普通角色得到自己 schema 的 USAGE、业务表的 SELECT/INSERT 和 heads.version 更新权限，控制、历史、DDL 与其他租户 schema 由数据库拒绝。[CLI 操作说明](../../../../tools/CP6.TenantStorage/README.md)
- 两个具名 stdin 命令使用现有有界解析和私有输入。重复安装回原结果，重复迁移按原目标、租户、lineage、操作、实际脚本 hash 与本地历史复核后回原件。不同租户可各用同一操作 UUID，同租户已经完成的 lineage 拒绝另一个操作。[真实进程验收](../../../../eng/wp02-02/rma-tenant-migration/verify.py)

## PostgreSQL 实测

正式运行在 D:/CP6-local-test/CP6-72H-20261109/W350-01/rma-migration-20261109T073847-452810fd。进程脚本 exit 0，fresh 与 legacy 两个场景均通过。使用隔离 SCRAM 实例与 127.0.0.1:55558，测试角色和业务数据由脚本创建，凭据经私有标准输入及私有进程环境传入。

| 场景 | 实际结果 | 原始文件 |
| --- | --- | --- |
| 准入与旧布局 | 在不同物理目标尝试错误 OID、模板克隆、普通登录代替控制身份及未知租户，全部拒绝。独立 legacy 实例执行旧 RMA 原 DDL 与真实原 combined CLI 后，新 profile 均拒绝，原 combined 回执重放字节一致 | fresh/01-*.stderr，legacy/01-shared-rejected.stderr，legacy/03-combined-rejected.stderr |
| A/A 争用、B 独立 | 私有测试 trigger 只让 A 的最后 Ready 更新停在 advisory holder 后台 42636。A1 CLI 32312、后台 50632、xid 760 已运行 DDL 和本地历史 INSERT。A2 CLI 58176、后台 55192 在原 FOR UPDATE 等待 A1 的 transactionid。A 尚等待时 B 真实提交，并通过普通独立 LOGIN 写入 head、DRAFT v1 和 SUBMITTED v2，再读回同一业务对象 | fresh/02-lock-chain.json，fresh/02-a-transaction.log，fresh/03-b-while-a-blocked.json |
| 中断与恢复 | 用独立观察连接取消准确 A2，收到 SQLSTATE 57014。只终止持有句柄的 A1 CLI，再释放测试 holder，等两后台退出。A 回到 Provisioning，无 schema、业务表、本地历史或共享结果。B 原回执、历史和普通 LOGIN 读回等于中断前。A 原键新进程重试成功 | fresh/04-a2-cancelled.stderr，fresh/04-a1-killed.process.json，fresh/04-after-interrupt.json，fresh/05-a-retry.stdout |
| 原件与 tenant-local 键 | A/B 用相同 OperationId 11d3659f-2209-4e84-94d2-6708fc855417 分别保存结果。普通 A/B 又分别写入相同 ApplicationId 13596c69-3689-4097-9744-16445e8b08ce，各从自己的 schema 读到 SUBMITTED。两个迁移原件和 bootstrap 原件分别逐字节重放，改键或破坏本地 hash 均被拒绝 | fresh/05-a-replay.stdout，fresh/05-b-replay.stdout，fresh/05-bootstrap-replay.stdout，fresh/05-ready-conflict.stderr，fresh/05-local-history-drift.stderr，fresh/07-final.json |
| 原生权限与约束 | 普通 LOGIN 的显式跨 schema 查询、控制/历史读取、CREATE/TEMP、改写头的其他字段与删除版本均返回 42501。跨租户插入被固定 CHECK 拒绝，缺失版本被延迟 FK 在提交处以 23503 拒绝。控制身份的原版本更新和两表共同 TRUNCATE 均被不可变触发器以 23514 拒绝 | fresh/06-*.stderr |

A 实际 DDL SHA-256 为 0ffa0de801b3e75862838521ab2c8999292540ed41dcdf1d019443f638680438，B 为 fd3b7c8de494b1c4c5367fc37a88eb326e5ce2b97f11681deaeac94bde3b05c2。A/B 原件 SHA-256 分别为 431805b6cee006e318d705e669906582614c714b3e09ca357759a9e4999d28d6 与 2fd63255e8058dd20d939a13b9a3084335087ed09e5ff36812b7589d8277eefb。原始结果保存各自完成时间。

### 构建与复现

使用 W346 的本地离线源配置做 locked restore，安装到本任务私有缓存，再对 tools/CP6.TenantStorage 做单节点 Release 编译。build-cli-2.log 为 0 错误、23 条其他既有源文件告警。以下 SHA-256 对应私有 build/bin/CP6.TenantStorage/release：

| 制品 | SHA-256 |
| --- | --- |
| CP6.TenantStorage.dll | babfd10127d9d80b56542ea763e963307fddb6dd8cda15030db9b1cc7cd5cb41 |
| CP6.Core.dll | 33040b5795131e458efd4faa049b64422cafc38652867366ab1e7795e1718992 |
| CP6.Persistence.PostgreSql.dll | b5f275ea38626cb786a0ae55214e3f1958788bebfdb5356f9af960a20d1c1051 |

    & 'C:/Program Files/python/python.exe' 'eng/wp02-02/rma-tenant-migration/verify.py' --root 'D:/CP6-local-test/CP6-72H-20261109/W350-01' --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55558 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli 'D:/CP6-local-test/CP6-72H-20261109/W350-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll'

保留三次先前记录。rma-migration-20261109T073007-44ee0cf6 用原 W346 CLI 在真实 PG 得到缺少新命令的 exit 2。rma-migration-20261109T073600-60ab5807 发现合法带引号登录名的原 regrole 参数需按 PG 标识符引用，首轮为 SQLSTATE 42602。rma-migration-20261109T073802-ff33a292 已通过主恢复场景，但单表 TRUNCATE 先被原外键返回 0A000，最终用包含全部引用关系的两表 TRUNCATE 触发原生 23514。

## 交付边界

该结果覆盖 fresh-only 的实际首版 RMA 业务表及其迁移线。共享 auth、Outbound/lineage 源读取、原业务 journal 和 RMA HTTP commissioning 需另窗验收。既有租户 schema 版本升级、真实 COMMIT 确认丢失、PGT-019 的非事务/共享迁移与正式环境部署各有独立验收范围。单一活动 RMA 权威仍由明确的 profile 准入和后续宿主接线共同守住。

分支为 codex/w350-01，DEV-1。集中 diff 检查已核对本任务范围与无冲突的当前 origin/main 变化。正式 fresh/legacy postmaster 58816/40224 均已停止，全部尝试的 owned PID 文件已消失，55558 无监听。COORD 负责最终审查、合并和官方 finish，DEV-1 仍登记给本任务。无远端执行或部署。
