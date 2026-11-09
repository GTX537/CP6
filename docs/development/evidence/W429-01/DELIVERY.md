# W429-01 · RMA 同一快照的 FileContent 原历史

DEV-2 于 2026-11-09 21:16:13 UTC 从 Main #861 `4eb0e71863eeba4608490b6fd9cb83d7e1124832` 开工。工作树改动限 `archive.py`、`file_content_assets.py`、`file_content_history.py`、`verify_file_content_archive_v11.py`，由 COORD 集中审查并保存分支。当前状态为实现及离线门禁通过，待真实同源 PostgreSQL 验证。

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

## 下一真实来源

W420 有两租户真实 RMA 和 FileContent Owner 历史。其 FileContent 夹具使用进程内授权目录，未证明 v10 所需持久 privacy 环境及 Stock 原 route/authority。W423 已因资源延期，其计划中的同源 ERP/v8 尚无实际输出。W419 的停机来源已有完整真实 v10 切面和 Stock/隐私范围，FileContent 部分为空。W419 manifest pin `5a2711b967d03cd9258509ea04d24f0a5a004ff9243f154df23d2e0a7359a446`，物理库 `127.0.0.1:55567/w374_live`，OID `16386`，system `7706139311168619192`，安装 `7679733c-6e8e-4896-b8ae-b7f27a400c65`。

下一资源窗口可在协调方核准转用的单一隔离副本上，沿 W419 已有 v10 前置，向 W420 已实际通过的 FileContent Owner 驱动传入该物理目标、安装、真实 A/B 及新私有资产根。驱动按现有 RMA 原 COMMITTED 记录定位 actor，走真实 Engineering/FileContent Owner 写入。再用全套 v10 参数附加 `--include-file-content-history`，观察实际 holder 和五个原生 COPY，共同验证原 v10 成员与真实原文件。该路线直接复用 W419 的现行范围数据。本窗的 W419/W420 原数据库和资产保持停机原件，未启动 PostgreSQL 或 CLR。[W419 原件与验证](../W419-01/DELIVERY.md) · [W420 原 Owner 夹具](../W420-01/DELIVERY.md)

COORD 已集中审查完整候选差异、显式启用与版本兼容、同 holder 导出链路、原 Owner 图、成员及外部资产完整性和 stage 入口边界。本检查点保留待原生验证的分支。真实同源 v11 的导出、校验和后续采用继续单列。
