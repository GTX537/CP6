# W406-01 · RMA 原两表导入隔离 Core 候选

DEV-2 于 2026-11-09 14:17:33 UTC 从 #843 `bccadb753e34c87991640260ffa05547eab0a701` 开工。`candidate_core.py import` 接收外部 manifest SHA-256、物理目标、候选 UUID 和原 Core commissioning 回执。源归档复用已有私有副本及成员校验。原生 `pg_restore --data-only` 产生的文件仅取 `heads` 和 `versions` 两段 COPY。目标保留自身的安装和原迁移回执。[命令](../../../../eng/wp02-02/rma-tenant-archive/candidate_core.py) · [W353 commissioning](../W353-01/DELIVERY.md) · [W372 原归档](../W372-01/DELIVERY.md)

同一事务核物理库、控制 owner、已设置的 `CP6-PRIVACY-RESTORE-CANDIDATE/v1/<UUID>`、原安装及 Ready、角色目录、关系所有权和权限。持现有认证栅栏后检查未开放的原生候选，锁定目标业务两表。新导入必须在空表执行，强制核原生外键及行数后保存 `IMPORTED` 回执。`recover` 以同一目标、候选、租户、manifest 摘要和 commissioning 原字节摘要只读取回持久化结果。该回执保持 `recoveryReady=false`。候选开放仍由独立现行源、环境、epoch 和原 `APPLIED` 协议判定。[候选原生标记](../../../../CP6.Core/Services/Platform/PrivacyRestoreNativeV1.cs) · [开放门](../../../../CP6.Core/Services/Platform/PrivacyRestoreAdmissionV1.cs)

## 已执行的离线验证

- W372 原 v1 pin `02d8ef33ed4749e8384fb05bfffec608bd611635d791fd980302a8d3a300eda0` 与原 v2 pin `3aa7b1c19332be13d0c0d5e04ba92f20717bca84d0abeca832e1317f93a885cc` 都从原归档提取一条 head、两条 versions。两段原生 COPY 合计 2328 字节，SHA-256 `60d30751268f42547b946b0bbd042a504400b032f3e23bb645087acf78474922`，含 Windows 行结束符。另有一份实际空 v1 归档保留两段零行 COPY。[原输入验证](/D:/CP6-local-test/CP6-72H-20261109/W406-01/test-offline.stdout) · [原空归档](/D:/CP6-local-test/CP6-72H-20261109/W406-01/test-offline-empty.log)
- W402 v9 固定 manifest `e1350c68ebc5618df6124384442630d3bba88186d8ae018b7a9fc2af4801e3bd` 和原私有资产根通过现有边界，提取实际一条 head 和一条 version。两段原生 COPY 585 字节，SHA-256 `f4f43a092eba7c9b54db7950dcb39c06d0e154d038b8530f9041efeb2452ad2c`。归档格式支持范围沿用现有 stage，实库测试另列。[W402](../W402-01/DELIVERY.md)

## 原生 COMMIT 传输验证

专属 PostgreSQL 18.6 小库 `w406_wire_driver_probe` 于 15:00:10 UTC 通过两次门槛，物理 821/855 MiB，虚拟 20585/20571 MiB。两行样本均无相关监听或重 .NET 进程。一次测试专用事务持久化单条原结果。代理在原生后端 `16828` 的 `CommandComplete COMMIT` 后断开，实际 psql exit 2，复用驱动返回 `rma-stage-outcome-unknown (use recover)`。另一次直连只读取回原结果，原持久化计数为 1。回读 stdout SHA-256 `50e7f7e3bb45075ba067111c7c71ff2f6888d1489a10b804ee624a145c27d56d`。[原生传输原件](/D:/CP6-local-test/CP6-72H-20261109/W406-01/wire-driver-20261109T150011-c181d205/actual.json)

该实测仅覆盖复用驱动的原生传输与持久化回读。测试进程 10.712 秒退出 0。专属 postmaster `53128` 已停止，`pg_ctl status` 返回 3，55564 和代理 56908 无监听，PID 文件已消失。[停机原件](/D:/CP6-local-test/CP6-72H-20261109/W406-01/wire-driver-20261109T150011-c181d205/stopped.json)

## Core 候选实库验证

2026-11-09 16:12:58.293263 UTC 在 DEV-2 从已保存的 `fe52f94eaa8d93e7c560b518a229d2ccbd75abdb` 恢复。启动连续两份物理样本为 1,849/1,826 MiB，可用 commit 20,991/20,937 MiB，相关端口空闲且无竞争进程。DatabaseInit 内门的连续物理样本为 1,667/1,693 MiB。实际沿用 W351 发布 DLL `3123dd9fa784d6c31bc674cc5bb015fd44e2431a8d825fcceb86c2e164af2be7`、原 deps/runtimeconfig 和 W353 CLI `53807c439aba8c48960a1ce1bc926d2ff02de302b66469884c9032dbedd3791c`。四份封存摘要均匹配，实际使用原发布 GC 配置。DatabaseInit PID 29704 退出 0，实际 commissioning 和重读均退出 0，回执原字节相同。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_core_candidate_import.py) · [续验命令](/D:/CP6-local-test/CP6-72H-20261109/W406-01/launch-full-native.py) · [资源与制品原件](/D:/CP6-local-test/CP6-72H-20261109/W406-01/rma-core-candidate-20261109T161415-21f0f9e6/init-artifacts.json)

- PostgreSQL 18.6 实际目标为 `127.0.0.1:55564/w406_candidate`，OID 16386，系统标识 `7706205797235709868`，候选 `6a89d755-10d4-4b72-8504-cf7f839c5754`。错物理库、错候选标记和改写后的 commissioning 原件各自拒绝。错库查询实际查无导入回执。
- 晚故障先在事务中确认 1 条 head 和 2 条原 versions 已 COPY，再改 head 版本触发 `23503 rma_application_head_version`。原生事务全回滚，随后的同键恢复查无回执。A 行数、B 原行、标记、角色目录、原迁移及控制状态前后相等。
- 对同一 v2 原包，代理 56961 在原生 backend 55820 收到 `CommandComplete COMMIT` 后断开，应用返回 `rma-stage-outcome-unknown (use recover)`。实际直连 `recover`、同键再次 import 和数据库持久回执原字节相同。`IMPORTED` 回执为 913 字节，SHA-256 `25991b0c3927c2bfb365cbc8aed3bbc28664497f9a1ea960fb83dfe34b9d3fa5`，`recoveryReady=false`。原 v1 pin 用作不同归档键的拒绝，已有行探针也实际拒绝。
- 目标实际保存 1 条 head 和 2 条 versions，原行 COPY 字节逐条核对。B 的原行和原租户 ACL、commissioning 与控制原件通过前后相等检查。A 普通 LOGIN 读导入回执及 B 普通 LOGIN 读 A 业务表均返回 `42501`。W372 两份输入包的前后摘要相等。[实际回执](/D:/CP6-local-test/CP6-72H-20261109/W406-01/rma-core-candidate-20261109T161415-21f0f9e6/actual.json) · [COMMIT 代理原件](/D:/CP6-local-test/CP6-72H-20261109/W406-01/rma-core-candidate-20261109T161415-21f0f9e6/commit-reply-cut.json)

最终测试于 16:15:10.479044 UTC 退出 0，实库运行 55.344 秒，距本次恢复 132.186 秒。专属 postmaster 6952 正常停止，独立 `pg_ctl status` 为 3，55564 和 56961 均无监听。原件保存在 `D:/CP6-local-test/CP6-72H-20261109/W406-01/rma-core-candidate-20261109T161415-21f0f9e6`。[实际退出与时钟](/D:/CP6-local-test/CP6-72H-20261109/W406-01/test-native-01.time.json) · [停机原件](/D:/CP6-local-test/CP6-72H-20261109/W406-01/rma-core-candidate-20261109T161415-21f0f9e6/stopped.json)

本窗实测覆盖隔离 Core 候选原两表的导入和恢复。源根采用、候选开放与正式 PGT-021/022 按原验收窗口执行。`codex/w406-01` 本地保存，DEV-2 交接时工作树干净，远端采用及席位释放由 COORD 办理。[正式验收卡](../../acceptance/postgresql-tenancy.json)
