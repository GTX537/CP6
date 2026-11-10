# W429-01 · RMA 同一快照的 FileContent 原历史

DEV-2 于 2026-11-09 21:16:13 UTC 从 Main #861 `4eb0e71863eeba4608490b6fd9cb83d7e1124832` 开工。原离线窗口保存于 `edb597ef09c92a1908e7d925dd9835c934cc79fd`。2026-11-10 03:24:04 UTC 由 COORD 在同一分支恢复，合入 Main #869 后的实测源码为 `75af8ff249c0efee853293248d4b4ec5c593816f`，四份归档实现与原检查点一致。

## 实现和边界

`archive.py export` 新增 `--include-file-content-history`，只有显式启用并满足完整 v10 前置时生成 v11。API 默认关闭。v1–v10 的格式选择和既有独立 `file_content_history.py` 入口沿用原合同。

新分支复用原生 FileContent 来源判定、物理 Owner、布局和图验证，并从同一 holder 取得资产计数。内容、head、event、原 Owner journal 和被引用的 working parent 版本由原 COPY 查询按原主键排序，均使用原 RMA holder 的 `snapshotId`。`fileContentHistory` 记录五个成员的关系、列、PostgreSQL 类型、主键、实际行数及原迁移和 Owner 依赖。原 `members` 记录各成员字节与 SHA-256。清单发布前核文件和关联，外部文件仍位于原私有 LocalFileStore 根。

校验覆盖真实 CSV 行数、类型、租户、主键顺序、head 前沿、原流水与事件、父版本与 v7 资产投影的对应，含原 `store_path`、长度、文件摘要及当前状态。外部文件在每次校验中只读取并计算一次摘要。输出保持 `recoveryReady=false`。现有 stage 对完整 v11 在生成 SQL 驱动前拒绝。[归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [原历史 SQL 和 v11 校验](../../../../eng/wp02-02/rma-tenant-archive/file_content_history.py)

## 实际离线结果

- 旧入口 RED 退出 1，原 v1/v10 和独立历史先通过，合成 v11 以 `bundle-format-rejected` 拒绝。首次接入后，测试发现本机 Python 的 ISO 解析需要把原 PostgreSQL `+00` 补成 `+00:00` 再解析。保留该次真实失败，最终修正只用于校验，原 CSV 字节不变。[基线 RED](/D:/CP6-local-test/CP6-72H-20261109/W429-01/offline-red/synthetic-v11.stderr) · [首次解析失败](/D:/CP6-local-test/CP6-72H-20261109/W429-01/offline-green-01/synthetic-v11.stderr)
- 最终 `offline-final-02` 退出 0，约 7.29 秒。W363 原 v1、W405 原 v10、W420 原独立历史正常校验。明确合成 v11 有 1 内容、1 head、2 事件、3 原流水和 1 引用父版本，验证通过，实际文件读摘要调用 1 次。九个 v10 前置逐项缺失时，CLI 均在创建输出前拒绝。缺件、错误类型/行数/来源元数据、他租户、非法 bytea、同摘要错 store key、跨租户 key、错误父版本引用、错误当前状态和原 actor，以及同长度错误文件均被拒绝。另验证输出不能与私有资产根重叠、现有 stage 不生成驱动。[最终日志](/D:/CP6-local-test/CP6-72H-20261109/W429-01/test-offline-final-02.log) · [原始 CLI 结果目录](offline-final-02)
- W412 含真实原 41 字节投影的 v10 沿原根单独复验退出 0。它只有投影证据，不能作 v11 FileContent 原历史正例。[非空 v10 复验](/D:/CP6-local-test/CP6-72H-20261109/W429-01/test-offline-w412-v10.log)
- W420 A/B 的五个原生 CSV 按封存摘要逐个核对。离线由其原 head 与 event 推导资产投影，新校验接受两组原数据并分别读取各自 45 字节原文件。该核对与合成 v11 都未运行 PostgreSQL，同一 holder 的 v11 原件仍待验证。[原生 CSV 离线兼容](/D:/CP6-local-test/CP6-72H-20261109/W429-01/test-offline-w420-native-shape.log)

| 复用原件 | manifest SHA-256 |
| --- | --- |
| W363 `rma-archive-20261109T092353-eb98873a/cli-export/bundle` | `3a3c73afc0ff65e35990478922de46174dfd66927c46d4874c4dad0181381f0b` |
| W405 `native-03/checks/v10-cut/bundle` | `7ac25b3c3eae626277c03802e29203e7cf1f7912664e05f92aeaba817453b6e9` |
| W412 `native-01/v10-cut/bundle` | `974ba915760c13a62494563633251ec8c3fa324ffe06ceba3a0e12e8fdb48505` |
| W420 `native-01/after/A-history/bundle` | `080cf46538994a00cd317cae0a239e728113802d52c2d183e7b2b826864202bd` |
| W420 `native-01/after/B-history/bundle` | `af204315a82a2968efaa668bbebe3e3ec3d552a4645f3abb78c3d7a0b145cbf8` |

在 DEV-2 执行的最终离线命令如下。

```powershell
$b = 'D:/CP6-local-test/CP6-72H-20261109'
python -B eng/wp02-02/rma-tenant-archive/verify_file_content_archive_v11.py `
  --v1 "$b/W363-01/rma-archive-20261109T092353-eb98873a/cli-export/bundle" `
  --v10 "$b/W405-01/native-03/checks/v10-cut/bundle" `
  --v10-assets "$b/W402-01/run-20261109T134906Z/checks/private-assets" `
  --standalone "$b/W420-01/native-01/after/A-history/bundle" `
  --standalone-assets "$b/W420-01/native-01/private-assets" `
  --output "$b/W429-01/offline-final-02"
```

## 同源 PostgreSQL 实测

沿 W422 最后一次停机移交接收 W419 v10 原件，manifest SHA-256 `5a2711b967d03cd9258509ea04d24f0a5a004ff9243f154df23d2e0a7359a446`。实际目标为 `127.0.0.1:55567/w374_live`、OID `16386`、system identifier `7706139311168619192`、安装 `7679733c-6e8e-4896-b8ae-b7f27a400c65`。只启动新增的任务专属副本，复制 11,375 个文件共 226,325,948 字节，前后核对五个控制文件。真实 W420 Owner 驱动沿用已核封存程序集，核对本次的物理身份、安装、数据目录与 A/B 登录绑定。数据和文件均为隔离的合成夹具。 [W419 原件](../W419-01/DELIVERY.md) · [W422 移交](../W422-01/DELIVERY.md) · [W420 Owner 入口](../W420-01/DELIVERY.md) · [复制原件](/D:/CP6-local-test/CP6-72H-20261109/W429-01/source-clone/copy.json)

- `native-01` 在 Windows PostgreSQL 18.6 执行。Owner 从 A/B 原 COMMITTED RMA 记录定位各自 actor，经过真实 EngineeringProduct、EngineeringRevision 和 FileContent 入口。A/B 以相同 FileId 和 OperationId 各写入一个不同的 45 字节文件，B 在自己的上传前读 A 时得到 403。原 A/B RMA heads、versions、迁移、共享 Outbound 和原 RMA journal 逐字节一致。全库原 7 条流水逐字节一致，只增加该夹具的 6 条准确 Owner 流水。 [原 Owner 执行](/D:/CP6-local-test/CP6-72H-20261109/W429-01/native-01/owner.stdout)
- A 使用正常 `archive.py export` 全套 v10 参数加 `--include-file-content-history`，导出和 `verify` 各退出 0。清单 41 个成员，SHA-256 `c4d87a7a6d41376fea0e440c63d3661edc7f7b06340cdc5b0a991cbee22a7050`。PostgreSQL 后端 25880 导出快照 `00000007-00000004-1`，`pg_dump` 与各 COPY 合计 41 次导入同一快照。五个文件历史查询在各自后端先导入该快照，再按 A 租户执行 COPY，均在 holder 提交前完成。独立查询逐份核对原字节。五个历史成员各一行，真实文件长 45 字节，B 校验 A 包得到 `bundle-tenant-mismatch`。 [v11 清单](/D:/CP6-local-test/CP6-72H-20261109/W429-01/native-01/A-v11/bundle/manifest.json) · [原 PostgreSQL 导出日志](/D:/CP6-local-test/CP6-72H-20261109/W429-01/native-01/A-v11-postgres.log)
- B 的 FileContent 独立历史也正常导出并按 manifest pin 校验，独立 COPY 的五个原成员与 B 制品逐字节相同。B 的同键原件用于隔离验证，组合 v11 的来源为 A。 [B 原件](/D:/CP6-local-test/CP6-72H-20261109/W429-01/native-01/after/B-history/bundle/manifest.json)
- 首次验证器把旧 v10 的 IAM 投影也设为逐字节相同，导致末尾对账退出 1。实际夹具新增权限触发原 `cp6_sso_admission_revision_v1`，在 17 条 IAM 投影中只有 A 角色 72 的 `revision` 变化。修正私有对账后，复用已执行的导出、校验及原生 COPY。其余 33 个 v10 CSV 逐字节一致，三表 `pg_restore` COPY 数据也相同。资产投影由 0 行变为 1 行。 [IAM 原触发器](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261019155500_SsoIdentityMappingV1.cs) · [对账结果](/D:/CP6-local-test/CP6-72H-20261109/W429-01/reconcile-01/actual.json)

本轮 PostgreSQL 起止为 03:36:17.304647–03:36:37.963623 UTC，历时 20.659 秒，真实 Owner 4.337 秒。运行 PostgreSQL 和 CLR 前的可用物理内存分别为 1,668 和 1,657 MiB，可用提交均超过 19,456 MiB。副本 postmaster 正常退出，原件与副本 `pg_ctl status=3`，临时 HBA 已移除，55567 已释放。原件与证据分开保留。 [停机回执](/D:/CP6-local-test/CP6-72H-20261109/W429-01/native-01/stopped.json)

COORD 集中审查了候选差异和真实证据，覆盖显式 opt-in、旧格式兼容、单一 holder、原 Owner 图、外部资产及隔离边界。组合 v11 保持 `recoveryReady=false`。W423 与 W415 的恢复路径、当前权限和消息对账、正式 PGT-021/022 继续按原范围验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 为 16 PASS、11 NOT_RUN。
