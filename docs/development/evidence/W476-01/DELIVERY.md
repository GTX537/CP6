# W476-01 — 同源 RMA、FileContent、ERP v11 暂存与文件落盘

WP02-02 从远端主线 54ecf4acff22dcd5ba6c21c1f9677cf59f1de327 复用 DEV-2。在 W474 已停机的模拟源副本上，复用 W466 封存构建运行现有 FileContent 与 ERP C03 Owner 入口。文件和 ERP 由两个模拟租户分别写入，A/B 文件共享业务键与 operation UUID，各有独立原件。B 用 A 的摘要读取时被实际拒绝，A/B 的 ERP 结果事件也独立。[W474 原源](../W474-01/DELIVERY.md) · [FileContent Owner](../W420-01/DELIVERY.md) · [ERP C03 Owner](../W419-01/DELIVERY.md)

- PostgreSQL 18.6 产生完整 41 成员 v11，manifest SHA-256 f4a4dee5dea87c497614b20acf23bb8a8aac5a8d83aacc704651348c693a034f。RMA 原 1 个 head、2 个 version、1 条历史、1 条出站和两条原 COMMITTED 保留。原 journal SHA-256 a1a516a5a3a7c2293344351d813d82d2ba756c37b57272da94293f57fc5c54ed，与 W372 原件一致。选定租户的 ERP Aggregate、Request、CommandInbox 和 Outbox 各 1 条。
- 中立库 w476_neutral 的 OID 43627，与来源 OID 16386 不同，同属本次隔离副本。1 份 RMA journal、5 份 FileContent 历史和 8 份 ERP CSV 的 PostgreSQL COPY 与归档逐字节一致。暂存及只读 recover 回执一致，SHA-256 3ca49c116e830496cdd083dd5f5c0d1c06dd9d88861e0c175f8e59e2da3ada10。暂存前后，A/B ERP 和 RMA 原流水读数一致。
- 现有文件落盘与 recover 返回同一回执，SHA-256 8bf8a21212fb86f83e05632f55318715203a02889469ab03ace338987ad18e52。落盘目录只含选定租户的 45 字节原件，SHA-256 540842f6e6966d8f33243ad24bf8a88b74a408553d229e233a856d55c959fb76。原 W372、W474 的数据库控制文件及 W474 manifest 保持原状，本窗启动的数据库已停机。[暂存](../../../../eng/wp02-02/rma-tenant-archive/stage.py) · [文件落盘](../../../../eng/wp02-02/rma-tenant-archive/materialize_files.py)

首次 ERP 驱动缺少封存构建的依赖清单，加载 DiagnosticSource 时退出。按 W419 成功的启动方式传入原 runtimeconfig 和 depsfile 后，在新副本验证通过。未修改应用和恢复工具。原始退出、SQL COPY、回执及停机证据保存于 D:/CP6-local-test/CP6-72H-20261109/W476-01。

W475 经 #943 采用，清理任务分支并释放 DEV-2，实测 7.44–7.49 分钟。本窗的 Stock 源只含权威和路由记录，尚无库存操作或业务消息。暂存与文件落盘保持 recoveryReady=false，正式 PGT-021/022、候选准入和切换仍待对应验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。GitHub Actions 和部署未运行。
