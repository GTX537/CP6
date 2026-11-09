# W418-01 · FileContent 关闭候选原件导入

DEV-2 于 2026-11-09 18:18:28.023904 UTC 从 Main #854 `03f713c0fb94b1f607420d8e213937ac0bcbe595` 开工。`file_content_candidate.py import` 消费原 W416 FileContent history v1 清单、五份成员和已备妥的私有候选文件根。控制键固定物理目标、标记、租户、原 manifest SHA-256 和根路径。同键 `recover` 只读返回原持久回执。[候选入口](../../../../eng/wp02-02/rma-tenant-archive/file_content_candidate.py) · [原包合同](../W416-01/DELIVERY.md)

导入先检查候选关闭态、原权限和 live Engineering 父行的完整内容。租户原范围须为空。原认证根下写入 contents，逐文件推进 head 并按版本写入原事件，最后追加原 FileContent Owner 回执。原触发器和外键全程生效，导入回执与原件在同一 PostgreSQL 事务提交。数据库权限限 owner 或受保护目录中已映射的无转授权 `CONNECT`，原生 FileContent schema、表和列由 owner 独占。候选回执为 `IMPORTED`、`recoveryReady=false`。[原生约束](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261029030000_FileContentV1.cs)

```powershell
python -B eng/wp02-02/rma-tenant-archive/file_content_candidate.py import `
  --pg-bin $PgBin --target-file $ExactClosedTarget --username $PrivateDatabaseOwner `
  --tenant $TenantId --candidate-id $CandidateId --bundle $VerifiedBundle `
  --manifest-sha256 $ExactManifestSha256 --file-content-root $PrivateCandidateFiles --output $NewOutput
python -B eng/wp02-02/rma-tenant-archive/file_content_candidate.py recover `
  --pg-bin $PgBin --target-file $ExactClosedTarget --username $PrivateDatabaseOwner `
  --tenant $TenantId --candidate-id $CandidateId --manifest-sha256 $ExactManifestSha256 `
  --file-content-root $PrivateCandidateFiles --output $NewRecoveryOutput
```

## 实测

- 基线实际缺候选入口。Python 3.8、入口及五段 COPY 驱动生成通过。原 RMA Core 两种回执 SQL 与 #854 字节相同，W416 原源导出查询除空白外相同。[基线失败](/D:/CP6-local-test/CP6-72H-20261109/W418-01/test-red.stderr) · [旧回执对照](/D:/CP6-local-test/CP6-72H-20261109/W418-01/test-driver.stdout) · [旧导出对照](/D:/CP6-local-test/CP6-72H-20261109/W418-01/test-existing-history.stdout)
- 转用已释放的 W416 专属停机集群。W383 封存 `before-disposition.dump` SHA-256 `a2721056979c3c0ca981752a66d7a1436d0a0973e2f4281c697bca54803faea7`。仅省略 TOC 的三张 FileContent 表数据和共享 operations 数据，共四项。原三张表各仅有 A 的三行，四个 ControlledDocument 数据成员均为零。两条其他 Owner 原流水的原 COPY 在 post-data 前保留，独立 native 回读逐字节相同。四阶段 SQL 在一条 psql 事务中完成。唯一新增目标是 `127.0.0.1:55568/w418_file_candidate`，OID 70258，系统标识 `7706131781016682348`，标记 UUID `9e9cd617-fd7c-4916-bbd3-b7eaae192f2a`。[精确 TOC 及原件](/D:/CP6-local-test/CP6-72H-20261109/W418-01/offline-prep/filtered-restore.json) · [实库恢复输出](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-01/filtered-sealed-native-restore.stdout) · [恢复后关闭态](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/empty-target-layout.stdout)
- 错标记、错物理目标、同长度错误文件、父版本原数据库时间差一秒、意外数据库 `CREATE` 和 FileContent 列 `SELECT` 均实际拒绝。ACL 两例在测试事务内授予原内置 `pg_monitor`，拒绝后回滚；文件摘要错误发生在生成驱动前。[父版本拒绝](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/wrong-parent/stage.stderr) · [数据库 ACL 拒绝](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/unexpected-database-grant/stage.stderr) · [列 ACL 拒绝](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/unexpected-column-grant/stage.stderr)
- 晚段探针在私有临时表使用 W383 关闭候选的原 v2 组件字节。两条 head 实际推进至 2，五条事件及五条 FileContent 原流水通过原生约束。随后原不可变触发器返回 `23514 SCOPE_AUTHORITY_IMMUTABLE`，目标 A 文件三表及原 Owner 流水均为零，同键查无回执。该探针未发布归档或将标记库当作合格源。[实际 v2 和回滚](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/late-v2-rollback/stage.stderr)
- 同一目标正式导入 W416 真 v1 pin `bbadad774a07905acae64a425e99f5d122510e1d9d5dc31ca0f5ffcac3b9304f`。目标五个 native COPY 与原成员逐字节一致，包含三条原事件、三条原回执和一条原父版本。原流水 CSV SHA-256 `a0b14bff8b97be1240db8352bf30b99c2c37d0177add0dc0eb6894bf6000cc8b`。同键重复和私有根暂不可用时的恢复均返回原 675 字节，SHA-256 `215c70c954b473d8af8c9c7e356edbe0365fdd3457ee2ed52cc766c2c876118a`，错根键拒绝。B 合成同键 `CLOSED`、两个其他 Owner、Engineering 原行、触发器和 admission 前后全等。两个原有数据库的记录及数据库目录一致。[实际结果](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/actual.json) · [目标原流水](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/actual-operations.stdout) · [原恢复回执](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/recover.stdout) · [受保护状态](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/protected-after.stdout)

## 资源与范围

`native-01` 完成封存备份恢复后测得物理可用 880 MiB，低于 900 MiB 行为门，退出 75 并停止。`native-02` 核实同一已恢复目标后接续，未重新创建或装载。采用一个 PG32、maintenance 32 MiB 和 max_locks 256，零 CLR。接续启动前实际 1,929 MiB，验证前 1,782 MiB，运行低点 1,768 MiB、提交可用低点 20,211 MiB。最终 18:40:03.619 UTC 退出 0，距 START 21 分 36 秒。postmaster 54796、5056 已停止，`pg_ctl status` 返回 3，55568 无监听。原 W383 控制文件及 HBA 摘要一致。[首轮资源结果](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-01/terminal.json) · [接续停机原件](/D:/CP6-local-test/CP6-72H-20261109/W418-01/native-02/stopped.json)

本窗实测独立关闭候选的 v1 原件导入和 v2 暂态回滚。共享候选普通 LOGIN 的允许分支、宿主存储接线、审批开放、完整多域切换及正式 PGT-021/022 分别验收。任务分支在本地保存，DEV-2 交付时干净，采用和席位释放交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
