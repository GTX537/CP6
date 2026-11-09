# W377-01 · RMA v3 来源原子 staging

DEV-1 于 2026-11-09 10:49:13 UTC 开工，基线 1eb00c4914d1893b879c2e43f860a7a6396f5492。复用 W370 受控 staging 操作入口和 W374 v3 来源归档。[W370](../W370-01/DELIVERY.md) · [W374](../W374-01/DELIVERY.md)

## 交付

stage.py 消费可信 pin 的 v3 原包，在同一既有 PostgreSQL 事务加载原 tenant schema、原 journal、九列出库头、明细与原 JSONB lineage。三个来源成员沿用 native COPY HEADER MATCH。所有共享行只落 rma_archive_stage，沿用实际物理空库、原角色、ACL 和可信 manifest 门禁。头及明细保留 BPCHAR 原值和 numeric(21,8) 发货数量，文档关联采用 staging 内与 CP6 一致的 ICU und-u-ks-level2 非确定性 collation。原 TenantId、出库 ID、单号组成明细复合外键，lineage 通过同租户明细键关联，且按原头单号文本逐字节比较。有效明细允许缺 lineage。[操作实现](../../../../eng/wp02-02/rma-tenant-archive/stage.py) · [CP6 collation 定义](../../../../CP6.Core/Persistence/PostgreSqlMigrationPrerequisitesV1.cs) · [PostgreSQL collation](https://www.postgresql.org/docs/18/collation.html)

提交前核对来源计数和对应关系。受保护 STAGED 回执增加 v3 sourceCounts，与原 target、tenant、manifest SHA-256 同事务保存。recover 仍只读返回原结果字节，recoveryReady 为 false。v1/v2 的既有中性 DDL、数据门及原回执读写 SQL 与已采用基线相符，使用 test-static.log 对照并复用 W370 实库证据。

## 实库

最终证据 D:/CP6-local-test/CP6-72H-20261109/W377-01/rma-stage-v3-20261109T105525-c2a62e9d。test-native-01.log 退出 0。唯一专属 PostgreSQL 18.6 环回实例，纯原生夹具。真实 W374 v3-cut manifest SHA-256 为 211af6ee04323c488a3070ca26ddee61eddb1b869e1c48d4f2e5bf6f6dfbd6ad。

- 夹具保持真实原包与 pin，在同一个已生成的 PostgreSQL driver 中，仅于各 COPY 后临时加入原生 UPDATE 去除 staged lineage 尾空格。真实 backend 4208、xid 757 已加载原 schema、头、明细、lineage 和 journal，随后原精确文本核验以 P0001 rma-stage-source-lineage-mismatch 失败并 ROLLBACK。隔离目标回到空态，同键只读查询无回执。
- 同一物理库、租户和原摘要通过正常 CLI 再执行，backend 32224、xid 758 提交。丢弃 607 字节 stdout，read-only backend 32540 返回与持久回执相同的原字节。sourceCounts 为 1 头、3 明细、1 lineage，另保留 1 条 COMMITTED。三个来源文件经 native COPY 回出，逐字节等于输入。原 journal、tenant 原 ACL 与目标非活动边界通过。
- 实际 catalog 发货数量为 numeric(21,8)。尾空格差异在 BPCHAR 比较中相等，原文本与值保留。临时将明细改单号为原单号小写，复合 FK 接受后由夹具回滚。错误单号触发 23503，其他租户触发 23514。原 lineage 精确文本边界已用同包后段故障证明。
- 旧 staging 针对同一个真实 v3 包在连接数据库前返回 rma-stage-bundle-member-rejected，test-red.log 保留。实库 postmaster 60900 已停止，无 PID 文件，55566 无监听。

## 复现与边界

使用已生成的 W374 原包、空闲环回端口和专属证据目录。[实际夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_stage.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_stage.py' --root "$b/W377-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --v3-only --v3-bundle "$b/W374-01/rma-source-20261109T103956-600b02ab/v3-cut/bundle" --v3-sha256 211af6ee04323c488a3070ca26ddee61eddb1b869e1c48d4f2e5bf6f6dfbd6ad

范围为受控 v3 staging。当前 IAM、授权、Orders/WorkOrders、队列、路由、外部资产、全局键和生产切换仍属后续。正式 PGT-021/022 保持 NOT_RUN。分支 codex/w377-01，合并及 DEV-1 官方 finish 交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
