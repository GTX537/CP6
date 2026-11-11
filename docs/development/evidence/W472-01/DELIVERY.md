# W472-01 — 完整 v11 的 RMA 两表导入关闭 Core 候选

WP02-02 从远端主线 `8d5a6cdea672863259d204af0dba37f688f7afbd` 复用 DEV-2。只有 `candidate_core.py import` 新增完整 v11 的显式选项。继续复用原归档、原文件、物理目标、Owner、ACL、commissioning 和单事务校验。回执仍固定目标、候选、租户、原 manifest 和原 commissioning。[导入入口](../../../../eng/wp02-02/rma-tenant-archive/candidate_core.py) · [原 Core 合同](../W411-01/DELIVERY.md)

- 基线实跑完整 v11 退出 1，显式选项未实现时退出 2。最终 PostgreSQL 18.6 新建隔离数据库，实际运行 W351 DatabaseInit 与 W353 prepare/commission，数据库系统标识 `7706871095069911528`。用原 v11 pin `c4d87a7a6d41376fea0e440c63d3661edc7f7b06340cdc5b0a991cbee22a7050` 从同一原 pgcustom 提取 A 的 head 和 version，各一行。候选实际 COPY 与原字节一致。错格式和无选项在生成驱动前拒绝。
- 真正生成导入回执后触发 PostgreSQL `22012`，两表及回执一起回滚。最终 `IMPORTED` 的 914 字节经同键重送、只读 recover 和原持久值验证，SHA-256 `9d1a67ea59a8226d37502050b34e87b6afdc7a7135187b4cb9b9771fa611fe1a`。B 原行、迁移原件、权限与 14 个 Stock 检查项保持。B 普通登录读取 A 表实际返回 42501。准入及原 RMA 流水保持空，`recoveryReady=false`。
- 流水试验首轮普通 B 全列投影按原权限返回 42501。修正测试投影后，W429 模拟 `COMMITTED` 的结果只有 `OperationId`、`State` 和 `Marker`，现有 Owner 图校验按原合同拒绝，保留原生 `rma-stage-candidate-journal-graph-rejected`。v11 流水仍需带真实 Owner 结果的同源样本。最终 `import-journal` 保持既有版本范围。原 v10 四种 import/recover 驱动逐字节与基线相同，复用 W411 和 W413 的实库证据。[两表验收](../../../../eng/wp02-02/rma-tenant-archive/verify_core_candidate_v11.py) · [原流水验收](../W413-01/DELIVERY.md)

原退出、前后原件及三次隔离测试库停机记录位于 `D:/CP6-local-test/CP6-72H-20261109/W472-01`。仅运行相关 Python、PostgreSQL 与本地 DatabaseInit 和 commissioning 夹具。GitHub Actions 和部署均未运行。

W471 经 #939 采用且释放，实际 10.04–10.12 分钟。本窗交付关闭候选 RMA 两表。原 RMA 流水、ERP 原件、正式 PGT-021/022 和切换继续各自验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。
