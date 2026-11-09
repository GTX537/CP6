# W416-01 · FileContent 原生历史同切面导出

DEV-2 于 2026-11-09 17:47:00.772306 UTC 从 Main #852 `9dca16ad396b1fc371038d5c3659024729f15232` 开工。独立 `cp6-file-content-history-v1` 包从同一个 PostgreSQL 只读切面保存五份原生 CSV。文件元数据、当前指针、完整事件、FileContent Owner 原流水及被引用的 Engineering 历史版本都保留原键与原类型。事件和原流水的正文、actor、数据库时间和审计按原字节或原生类型写出。工程父版本为引用切片。[入口与快照查询](../../../../eng/wp02-02/rma-tenant-archive/file_content_history.py) · [原 FileContent 迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261029030000_FileContentV1.cs)

导出使用现有 PostgreSQL 物理身份、只读快照、原生 COPY 以及 LocalFileStore 键、长度和流式 SHA-256。原生图检查完整事件链、原 `COMMITTED` 与事件一对一关联、`CLOSED` 原键及准确 Engineering 父版本。原结果的 `Version` 为文本，`Digest` 为对应事件体摘要。独立 `verify` 按操作者固定的 manifest 核五份成员、原文件与租户键。原生图在导出快照检查，离线校验复用该固定内容。输出为 `recoveryReady=false`。[原 Owner 结果合同](../../../../CP6.Core/Services/Pub/FileContentOwnerV1.cs#L195)

```powershell
python -B eng/wp02-02/rma-tenant-archive/file_content_history.py export `
  --pg-bin $PgBin --target-file $ExactSourceTarget --username $BackupRole `
  --tenant $TenantId --file-content-root $PrivateOriginalLocalStore --output $NewPrivateOutput
python -B eng/wp02-02/rma-tenant-archive/file_content_history.py verify `
  --bundle $ExportedBundle --manifest-sha256 $ExactManifestSha256 `
  --tenant $TenantId --file-content-root $PrivateOriginalLocalStore
```

## 实测

- 实现前旧版本实际缺入口。实现后 export/verify 参数检查退出 0。实际 Windows 私有路径测试拒绝输出与资产重叠、junction 别名、保留名、尾随点和备用数据流。原 W412 非空 v10 包继续经原 CLI 校验通过。[基线失败](/D:/CP6-local-test/CP6-72H-20261109/W416-01/test-red.stderr) · [Windows 路径实测](/D:/CP6-local-test/CP6-72H-20261109/W416-01/offline-paths/junction-export.stderr) · [原 v10 校验](/D:/CP6-local-test/CP6-72H-20261109/W416-01/test-old-v10.stdout)
- 唯一 PostgreSQL 18.6 副本从 W383 停机原件复制，环回 `55568/tenant_management_component`，OID 16385，system `7706131781016682348`。A `2c1bf42a-3cdb-4b93-8ca7-c5493eb61bb5` 有实际 Owner 原上传三份和真实 LocalFileStore 字节。正常导出和固定 pin 校验通过。五成员计数为 `3/3/3/3/1`，前三张业务表和原流水与 W383 原列逐项相同，父版本正文摘要与原引用相同。manifest SHA-256 `bbadad774a07905acae64a425e99f5d122510e1d9d5dc31ca0f5ffcac3b9304f`。[原复制](/D:/CP6-local-test/CP6-72H-20261109/W416-01/source-clone/copy.json) · [原包](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/true-owner-export/bundle/manifest.json) · [实际结果](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/actual.json)
- 同一副本的 W383 原关闭候选 `OID 43411` 只做组件查询。五条真实事件和原流水与 W383 候选原件逐项相同，其中含两条已记录的 v2 完整性隔离。带候选标记的库从正常源导出入口被拒。原始源资产根及三份文件经过前后摘要核对。[候选组件原事件](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/closed-candidate-component/file-content-events.csv) · [候选源拒绝](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/closed-candidate-rejected.stderr)
- 快照 `0000000E-00000002-1` 开启后，独立后端 50620 在副本提交 A/B 合成 `CLOSED` 原流水。五个真实 COPY 都导入该快照，成员与此前真实源包逐字节一致。新切面读取 A 的合成关闭键及 B 的独立同键，彼此隔离。之后仅在副本追加一条原生已提交但事件摘要不匹配的合成回执，正常导出拒绝且没有生成 manifest。错物理库、错 pin、改写事件成员及同长度错误文件也分别拒绝。[切点原件](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/late-after-holder.stdout) · [坏图拒绝](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/bad-native-graph.stderr) · [错误文件](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/same-length-wrong-asset.stderr)

## 收尾

首个 1,200 MiB 启动门用时 301.214 秒，150 个样本最高 1,198 MiB，退出 75，未启动 PostgreSQL。COORD 依据既有 PG32 实测批准仅本任务一次分段门。最终实际启动前 1,330 MiB，验证前 1,281 MiB，运行低点 1,241 MiB，提交可用低点 20,750 MiB。`native-02` 运行 12.788 秒，18:06:29.246 UTC 退出 0，距 START 19 分 28 秒。postmaster 47996 正常停止，原与副本 `pg_ctl status` 均为 3，55568 无监听。原 W383 控制文件、HBA 和资产保持原摘要。专属副本保留测试用合成反例。[首次资源结果](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-01/terminal.json) · [最终运行时钟](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/terminal.json) · [停机原件](/D:/CP6-local-test/CP6-72H-20261109/W416-01/native-02/stopped.json)

本窗覆盖独立 FileContent 原生历史的源导出与校验。统一多域包、Engineering 完整 lineage、W414 资产落盘的后续接线、候选原生活动表采用及正式 PGT-021/022 分别验收。任务分支在本地保存，DEV-2 交付时干净，采用和席位释放交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
