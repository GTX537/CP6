# W394-01 · v7 FileContent 外部资产原子 staging

DEV-2 于 2026-11-09 12:20:13 UTC 从 #833 `cf8b50a3fd2c3ba4e4875c6b2ba08c8150f6de4f` 开工。采用 W389 原 v7 切面及磁盘原件，复用 W391 的私有 staging 事务、原 schema 与隔离门。[W389 原 v7 归档](../W389-01/DELIVERY.md) · [W391 v6 staging](../W391-01/DELIVERY.md)

v7 `stage` 使用 `--file-content-root` 指定供应的私有本地文件根。工具复制并核对 pinned bundle，在生成任何 PostgreSQL restore driver 前通过现有 v7 verifier 核对路径、长度和原 SHA-256。原生 `COPY HEADER MATCH` 将原元数据写入 `rma_archive_stage.file_content_asset_snapshot`，使用精确租户 UUID、原文件键、原 PK 和唯一存储键。原 `parent_version` 与事件原 ETag 保留文本表示，字节长度和状态版本使用 bigint。原事件、文件体和父 Owner 的活动关系保持独立。仅 v7 回执增加 `fileContentAssetCount`。既有只读 `recover` 直接返回同物理目标、租户和 pin 的原持久回执。[staging](../../../../eng/wp02-02/rma-tenant-archive/stage.py) · [现有 v7 资产校验](../../../../eng/wp02-02/rma-tenant-archive/file_content_assets.py)

## 实库验证

`test-native-01.log` 退出 0。唯一纯 PostgreSQL 18.6 环回实例使用专属 55564，原件在 `D:/CP6-local-test/CP6-72H-20261109/W394-01/rma-stage-v7-20261109T122401-3b9c4995`。采用 W389 `cut` 的 manifest SHA-256 `74e75ace3a96d3f053c2c0cfc16d44173c281d32447b2bcf4305221dda9d3681`。实测启动前物理可用 848 MiB、虚拟可用 19930 MiB，W393 原实例已停止，无 .NET 初始化。

- 实现前旧 CLI 在连接前以 `rma-stage-bundle-member-rejected` 拒绝原 v7。实现后实际缺根、缺文件和同长度篡改各退出 1，分别返回原校验错误，未生成 `pg-restore-list` 或 `driver.sql`。仅在任务私有根合成损坏，W389 的 37 字节源文件原 SHA-256 `660c5cc850fdbb5e19cbe7a7f85a3632fd9123600b7a0d8ba81159577511718a` 保持。
- 保持同一 pin 与正确供应字节，在全部 native COPY 后删除 staged 资产行。backend 9040、xid 757 以 `P0001 rma-stage-file-content-count-mismatch` 回滚。隔离目标无残留 schema，backend 56316 只读查询无回执。
- 同物理库、租户和 pin 经正常 CLI 重试，backend 6856、xid 758 提交，夹具丢弃 1128 字节 stdout。将任务私有供应根改名后，原路径不可用。backend 19848 的只读 recover 返回原持久字节，带换行输出 SHA-256 为 `140499b2c9d35fd42a087f45a3856533445f56408a6fd4c4607c7305258ae077`。结果为 `STAGED`、`recoveryReady=false` 和 1 个资产引用。
- 原生 COPY 回出的资产 CSV 逐字节等于 W389 原成员，保留原 UTF-8 文件名。SHA-256 为 `1ede37edff57a5bac9a0028024ba60fd22ec707a3a9b0ba24950d66f6ce1c0cf`。实际 PostgreSQL 类型、原 tenant ACL 和中性隔离通过。W389 原目录及本窗已改名的供应副本保留。
- 实物 v1–v6 的十二份 stage/recover driver SQL 各自与 #833 逐字节相等，含原回执读写。静态对照在 `test-legacy-sql.log`，旧版原行为证据复用 W391。Postmaster 16828 已停止，`pg_ctl status` 为 3，55564 无监听。

## 复现和范围

使用空闲环回端口和任务专属证据目录。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_file_content_stage.py)

    $b = 'D:/CP6-local-test/CP6-72H-20261109'
    $v7 = "$b/W389-01/file-content-assets-20261109T120815-453ce71b"
    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify_file_content_stage.py' --root "$b/W394-01" --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55564 --bundle "$v7/cut/bundle" --file-content-root "$v7/private-local-store" --manifest-sha256 74e75ace3a96d3f053c2c0cfc16d44173c281d32447b2bcf4305221dda9d3681

本窗交付指定本地根的验证、中性 staging 和原回执恢复。活跃文件供给、Owner 启用、生产切换与正式 PGT-021/022 恢复验收分窗。分支 `codex/w394-01` 本地保存，DEV-2 工作树干净，远端采用和席位释放由 COORD 执行。[正式验收卡](../../acceptance/postgresql-tenancy.json)
