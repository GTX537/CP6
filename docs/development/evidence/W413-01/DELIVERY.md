# W413-01 · 关闭候选库中的 RMA 原流水恢复

DEV-2 于 2026-11-09 17:12:58.515453 UTC 在 `codex/w413-01` 开工，基线 Main #850 `cb1ca74e5cd3d9929b5c12b1767e49d4757fcbe2`。`candidate_core.py import-journal` 按已核验归档与同一 Core 导入键，向候选库的原 `cp6_main_owner_v1.operations` 写入 `Main.Wms.RmaApplication.v1` 原件。`recover-journal` 从独立的私有回执读取原结果。现有 `import`、`recover` 及原 Core 回执继续使用原合同。[控制入口](../../../../eng/wp02-02/rma-tenant-archive/candidate_core.py) · [原流水装载](../../../../eng/wp02-02/rma-tenant-archive/candidate_journal.py)

物理目标、标记、原 Core 回执、Ready 和候选 admission 由现有控制路径核对。原认证根和 journal `ACCESS EXCLUSIVE` 持续至提交。事务核对实际 W361 目录指纹、执行角色、共享表形状和触发器，使用 PostgreSQL 原类型装入临时表，验证原键、计数、输入 SHA-256 和已恢复的版本、状态及 ETag。装载保留十二列，包含审计、原对象键及原提交时间。原认证、不可变、外键和已有 Owner 约束持续有效；仅原旧写拒绝触发器在排他事务内暂停，恢复同一 OID 和 `ALWAYS` 后才写回执。候选结果为 `recoveryReady=false`。

## 实际验证

- 实现前以封存 v2 输入运行入口回归，旧命令拒绝 `import-journal`。实现后以 Main #850 和当前源码生成原 v1/v2 的四个 `import`/`recover` SQL 驱动，逐字节相同。原 v2 导入及恢复驱动分别为 13,941 和 3,635 字节，SHA-256 `0974102ca2862111252d48c4f8e3b46b757d30189112544fba8381c42a159049` 和 `61663c8bf200448b055593d6349d9bf6a0ef476785b4b48a58805947ef029423`。[基线失败](/D:/CP6-local-test/CP6-72H-20261109/W413-01/test-red.stderr) · [离线对照](/D:/CP6-local-test/CP6-72H-20261109/W413-01/offline-01/old-driver-equivalence.json)
- PostgreSQL 18.6 使用 W406 原关闭候选的任务专属副本，环回 `55564/w406_candidate`，OID 16386，系统标识 `7706205797235709868`，候选 `6a89d755-10d4-4b72-8504-cf7f839c5754`。封存 W372 CLI 实际安装 W361 权限层，退出 0，原安装回执 SHA-256 `e4e0dc1cb257f689932ed8ba803d988568c85369a67d60252d0c4cf230ac6379`。后续在同一副本读回该回执，复用此前 B 普通 LOGIN 写入的同键 `CLOSED`。[原副本](/D:/CP6-local-test/CP6-72H-20261109/W413-01/candidate-clone/copy.json) · [W361 实际回执](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-01/journal-install.stdout)
- 同源 W372 v2 manifest `3aa7b1c19332be13d0c0d5e04ba92f20717bca84d0abeca832e1317f93a885cc` 的实际草稿和提交两条 `COMMITTED` 原件写入目标 A。原生 `COPY TO STDOUT` 的 3,443 字节与原包 `rma-original.csv` 全等，SHA-256 `a1a516a5a3a7c2293344351d813d82d2ba756c37b57272da94293f57fc5c54ed`。重复导入及同键恢复返回同一 655 字节，SHA-256 `2a35571ac1a299b5656e766313f5a468d9464df82e11906072e454283a7cb66e`。原 Core 的 913 字节回执仍与 W406 原件相同，错标记及错回读键实际拒绝。[实际结果](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/actual.json) · [原生 CSV](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/candidate/actual-original-csv.stdout)
- 故障注入将临时行结果换成只有 `OperationId`、`State`、`Marker` 的对象，PostgreSQL 在真实写入前以 `P0001` 拒绝。晚段探针观察到两条 A 原件已写入且旧写拒绝触发器处于未提交的暂停状态，随后原不可变门拒绝 UPDATE，返回 `23514`。事务回滚后 A 无原件、同键查无导入回执，触发器目录和候选原状态逐字节回到基线。该证据覆盖真实事务回滚及同键恢复。[图约束故障](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/candidate/bad-graph/stage.stderr) · [晚段故障](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/candidate/late-rollback/stage.stderr)
- 普通 A 读到自己的两个原结果，普通 B 使用相同操作 UUID 只读到自己的 `CLOSED`。普通 A 直接插入及读取导入回执均被 `42501` 拒绝，旧控制写入仍由原拒绝触发器返回 `RMA_SHARED_AUTHORITY_RETIRED`。A/B 两表、目录与迁移、其他 Owner 原件、原控制回执、候选 admission 及 98 个共享 journal 触发器的实际前后快照一致，其中 92 个为原生外键触发器。[A 普通读](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/candidate/a-ordinary-read.stdout) · [B 普通读](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/candidate/b-ordinary-read.stdout) · [受保护状态](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/candidate/protected-after.stdout)

## 收尾与范围

首轮实际安装成功后，其他 Owner 合成哨兵的非 JSON 字节被现有约束触发器拒绝，保留 `22P02`。第二轮使用 JSON 哨兵进入实际产品门，发现初版把共享表内部引用外键触发器也计入六个业务触发器。修正精确计数，明确拒绝未知内部 INSERT 触发器，保留原生引用外键和两次失败证据。[首轮失败](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-01/candidate/other-owner-before.stderr) · [第二轮失败](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-02/candidate/bad-graph/stage.stderr)

最终 `native-03` 运行 11.777 秒，17:26:26.728 UTC 退出 0，距 START 13 分 28 秒。启动门两次物理内存样本 1,245/1,271 MiB，独占环回确认通过，运行最低 1,196 MiB、可用 commit 最低 20,819 MiB。专属 postmaster 36544 正常停止，`pg_ctl status` 返回 3，55564 无监听，原 W406 的控制文件与 HBA 摘要一致。[运行时钟](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/terminal.json) · [停机原件](/D:/CP6-local-test/CP6-72H-20261109/W413-01/native-03/candidate/stopped.json)

本窗范围为关闭候选中的真实 v2 `COMMITTED` 原件恢复和普通 PostgreSQL 读回。归档原 `CLOSED` 正例、完整 v10 目标装载、原操作 HTTP 批准开放和正式 PGT-021/022 另验。任务分支由执行侧本地保存，DEV-2 交付时干净，正式采用与席位释放交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
