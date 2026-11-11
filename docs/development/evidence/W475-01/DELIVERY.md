# W475-01 — 同源完整 v11 的实际 RMA Owner 原流水导入

WP02-02 从远端主线 `340c82c6b52c7a17f5f2f611bf600aa2248fcb7c` 复用 DEV-2。现有 `candidate_core.py import-journal` 显式接受完整 v11，使用同一真实归档、独立物理目标、闭合标记、租户、commissioning 和已持久 Core 导入回执。原 RMA 图、原生权限和 journal 事务按原合同执行。[导入入口](../../../../eng/wp02-02/rma-tenant-archive/candidate_core.py) · [实际 v11 样本](../W474-01/DELIVERY.md) · [原 journal 合同](../W413-01/DELIVERY.md)

- 基线完整 v11 默认退出 1，显式选项未实现时退出 2。最终 PostgreSQL 18.6 新建关闭候选，系统标识 `7706879808919234076`。同一 W474 manifest `5c658f31c926b6f2f6f58958a52d0a06ddbd8795131002c8c1875562ab21ef86` 实库导入原 1 个 head、2 个 version 和两条原 `COMMITTED`。W351、W353 与 W372 原安装可执行物实跑，原权限目录可回读。原流水全列 COPY 3,443 字节与 W372 原件一致，SHA-256 `a1a516a5a3a7c2293344351d813d82d2ba756c37b57272da94293f57fc5c54ed`。
- Core 晚段 22012 与 journal 原生 23514 分别实测全回滚。journal 故障在两条原件和临时原生 gate 状态已发生后触发原不可变规则，回滚后 A 原件和回执为空，gate、B 和其他 Owner 原件保持原样。错误图、错标记和错恢复键实际拒绝。
- 最终 Core 与原流水的持久回执可独立只读恢复，重复导入原样返回。原流水回执 656 字节，SHA-256 `e893ac461afb80ca90efaf6dcd6bacd5fc8d298d0721ff32cc8aa0344fc5cb61`。实际普通 A/B 只读投影各见本租户原件，B 的同 operation UUID 独立保留，A 读私有回执、直接 INSERT 及旧共享入口写入均按原生权限拒绝。
- W472 的 W429 模拟结果仍由同一个原图校验拒绝。旧真 v2 的 import-journal/recover-journal 驱动与基线逐字节一致，复用 W413 原实库验证。最终候选准入与 Stock 写集为空，`recoveryReady=false`。[相关验收](../../../../eng/wp02-02/rma-tenant-archive/verify_core_candidate_journal.py)

原始进程退出、原 SQLSTATE、前后原件和停机记录在 `D:/CP6-local-test/CP6-72H-20261109/W475-01`。只执行相关 Python、PostgreSQL 和封存本地安装夹具。GitHub Actions、应用宿主和部署均未运行。

W474 经 #942 采用并释放，实际 10.73–10.83 分钟。本窗关闭 Core 候选的实际原 RMA 流水验证完成。多域同源整体验证、正式 PGT-021/022 和切换各自验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。
