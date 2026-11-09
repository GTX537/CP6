# W370-01 · RMA 归档原子 staging

DEV-1 于 2026-11-09 10:01:15 UTC 开工，基线 8c4796e89b3a0e3b28381cbb30d0f080962aafe4。使用专属环回 PostgreSQL 18.6 和先前已验证的合成业务归档。[W363 v1](../W363-01/DELIVERY.md) · [W366 v2](../W366-01/DELIVERY.md) · [W364 Hosted Create](../W364-01/DELIVERY.md)

## 操作入口

stage.py stage 接收精确物理目标、租户 GUID、可信渠道取得的 manifest SHA-256、原归档目录及新的私有输出目录。先复制固定包成员到私有目录，校验 pin 和成员字节，再读取 native TOC。目标须由操作人预建，连接身份为实际数据库 owner，数据库与源物理身份不同，具备 owner-only ACL、无其他客户端连接、无已有业务对象。建议使用专属空库，从 PUBLIC 收回 CONNECT 和 TEMPORARY。[操作工具](../../../../eng/wp02-02/rma-tenant-archive/stage.py)

目标集群须预置归档引用的原 owner 和登录角色。W364 包还要求已有原 NOLOGIN executor。stage 保留归档 owner 和 ACL，角色及其创建授权由操作人管理。工具由 pg_restore --file 生成完整 tenant SQL，使用单个 psql --single-transaction、ON_ERROR_STOP 驱动实际目标校验、原生恢复、出库和 v2 journal COPY、计数与源迁移回执核验。共享切片只落 rma_archive_stage 下的非活动表。实际 PG18 的额外 --schema 过滤会遗漏 schema 定义和 schema ACL，因此使用原已 pin 的单 schema 归档。[pg_restore](https://www.postgresql.org/docs/18/app-pgrestore.html) · [psql 单事务](https://www.postgresql.org/docs/18/app-psql.html)

保护回执与数据同事务提交，记录精确目标、租户、manifest 摘要和 STAGED 原结果。工具只在 psql 成功退出后发布成功结果。stdout 丢失时，stage.py recover 用同一物理目标、租户和 digest 只读查回原字节。恢复还核对回执 owner/ACL、记录键、STAGED 状态及 recoveryReady=false。首次 stage 拒绝非空目标，重复获取结果使用 recover。

## 实库证据

基准原件位于 D:/CP6-local-test/CP6-72H-20261109/W370-01/rma-stage-20261109T100822-b2418454，test-native-02.log 退出 0。实际使用 W363 v1 manifest SHA-256 26872718e607687e959c5f505bbd6912707fe830e89ac286fd73d7b3f253b2d8 和 W366 v2 manifest SHA-256 b9bc10ddb67edf352e0f80cdf4345316fe2fef22c41847bdff6b5c3d0ecc286f。

- 错可信 digest、错库 OID、已有表的候选、未收回 PUBLIC 连接权的候选均拒绝。非空候选的原 sentinel 值仍为 42。v1 真实恢复为 1 head、2 versions、1 history、1 共享出库，原角色 ACL 保留。
- 夹具单独为损坏的 v2 journal header 提供测试 pin。实际 PostgreSQL backend 45244 同事务 xid 764 已加载原三表和出库，journal COPY 以 22P04 失败并 ROLLBACK。目标回到空态，原位置使用真实 v2 复跑成功。v2 的 COMMITTED 和 CLOSED、原 bytea、摘要、审计、对象和时间逐值匹配。
- 真实 v2 提交的 542 字节 CLI stdout 故意丢弃。read-only backend 49236 由原回执读回相同结果字节。错 digest、错租户、回滚后无回执及原目标重复 stage 均按边界拒绝。A 的 schema/table ACL 仍可在目录验证，A/B 均无目标库 CONNECT，stage 共享表无普通访问。

W364 专项在 D:/CP6-local-test/CP6-72H-20261109/W370-01/rma-stage-writer-20261109T101152-2f94b166，test-native-writer.log 退出 0。复用实际 post-draft v2 bundle，manifest SHA-256 af48c8096a3b8d047d6ccd89a97d081389e328888cdc6c7323402ff060bebd04。

- 缺原 NOLOGIN executor 时，native backend 32052 实际 42704 后 ROLLBACK。夹具预建准确 NOLOGIN 角色后，同一目标由 backend 13136 提交。原 schema USAGE 和十项列级 INSERT 精确保留，executor 无表级 grant 和 CONNECT。原 COMMITTED 记录逐值相同。
- 夹具只更改已提交 JSON 的 outcome、recoveryReady、tenantId、manifestSha256，逐项均无法 recover。恢复原记录后，原 stage 和 recover 各返回同样的 546 字节。

实现前的 test-red.log 与初次夹具相对路径错误 rma-stage-20261109T100740-ed1e9eed 保留。三个专属 postmaster 38152、19996、27604 已停止，PID 文件均移除，55566 无监听。

## 复现与边界

沿用已有归档，无产品构建。测试使用尚未占用的端口和私有证据目录。[可复现夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_stage.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    $verify = 'eng/wp02-02/rma-tenant-archive/verify_stage.py'
    & 'C:/Program Files/python/python.exe' -B $verify --root "$b/W370-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --v1-bundle "$b/W363-01/rma-archive-20261109T092353-eb98873a/export-a/bundle" --v1-sha256 26872718e607687e959c5f505bbd6912707fe830e89ac286fd73d7b3f253b2d8 --v2-bundle "$b/W366-01/rma-archive-20261109T095220-0d8f1824/v2-cut/bundle" --v2-sha256 b9bc10ddb67edf352e0f80cdf4345316fe2fef22c41847bdff6b5c3d0ecc286f
    & 'C:/Program Files/python/python.exe' -B $verify --root "$b/W370-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --writer-only --v2-bundle "$b/W364-01/rma-draft-20261109T095913-c36831eb/draft/archive-v2-after-draft/bundle" --v2-sha256 af48c8096a3b8d047d6ccd89a97d081389e328888cdc6c7323402ff060bebd04

操作者命令使用 --pg-bin、--target-file、--username、--tenant、--manifest-sha256、--output，stage 另需 --bundle。PGPASSFILE 或 PGPASSWORD 仅供连接。范围限受控 staging 和只读回执恢复。正式 PGT-021/022 保持 NOT_RUN，现行 IAM、路由、消息、全局键对账和生产切换仍在后续。分支 codex/w370-01，合并和 DEV-1 官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
