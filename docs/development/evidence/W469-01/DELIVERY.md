# W469-01 — v11 FileContent 原历史导入关闭候选

WP02-02 从远端主线 `36e1e89f172cf26e85b3d3a6fe504d9c9fd351ab` 复用 DEV-2。导入命令显式使用 `--include-file-content-history` 接受完整 v11 归档，复用原 manifest SHA-256、完整成员和外部文件校验。候选沿用独立物理库、关闭标记、Owner 与 ACL、Engineering 父版本及现有原生事务。回执绑定目标、租户、候选标记、原清单和文件根。[候选入口](../../../../eng/wp02-02/rma-tenant-archive/file_content_candidate.py) · [W429 原件](../W429-01/DELIVERY.md) · [W468 文件](../W468-01/DELIVERY.md)

- 基线实跑完整 v11 参数退出 2，未生成驱动。最终导入使用 W429 原清单 `c4d87a7a6d41376fea0e440c63d3661edc7f7b06340cdc5b0a991cbee22a7050` 及 W468 原 45 字节文件根。候选在 PostgreSQL 18.6 私有复制集群中新建，数据库 OID 71191。按原 PostgreSQL dump 只过滤 A 文件三表和该 Owner 原流水各一行，恢复 B 各一行和共享原流水 12 条。四张 ControlledDocument 表当时均为空。保留的 COPY 实际回读一致，原 W429 数据目录一直停机且控制摘要保持一致。
- 候选五份 PostgreSQL COPY 与原 CSV 逐字节相同，每份一行，保留内容、head、事件、原 Owner 操作及被引用的 Engineering working version。B 的同文件键和同操作键、其他 Owner 原件、父历史、约束和权限保持原样。原 705 字节回执在同键重送和只读恢复时逐字节相同，SHA-256 `5eeb6964480f4678ccb9ee4f12749c98914fe29d55a013b1a4cfa82bacffac11`。
- 未显式启用 v11、使用 v10 冒充 v11、错误 manifest pin 均在生成驱动前拒绝。实际父版本时间不符触发原拒绝。实库在已生成导入回执后触发 `22012`，回滚 A 的文件三表、Owner 原流水和回执。错误恢复 pin 拒绝。用 W418 原 v1 输入复核 import 与 recover 两个旧驱动，均与基线逐字节相同，复用 W418 原 PostgreSQL 结果。[相关验证](../../../../eng/wp02-02/rma-tenant-archive/verify_file_content_candidate.py) · [W418 原验收](../W418-01/DELIVERY.md)

原始命令、前后回读、拒绝、停机和资源记录保存在 `D:/CP6-local-test/CP6-72H-20261109/W469-01`。本窗仅使用模拟业务数据和一套隔离 PostgreSQL 18 测试库，两个验证阶段均已停机。集中审查覆盖范围、原始合同和验证。Actions、CLR 和部署均未运行。

候选回执为 `IMPORTED` 且 `recoveryReady=false`。宿主实际 Owner 读取、ERP 与 Stock 当前源对账、权限闭环、正式 PGT-021/022 和切换仍待各自验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。W468 已由 #936 采用并释放席位，实际 12.88–12.97 分钟。当前窗原估计 30–50 分钟，完成后按原生时钟校准。
