# 当前待办与优先级

## P0：Space V1 下一批受控实现

- 以 `e8e84853` 为当前 Space 代码集成基线，`b721468c` 为对应文档基线。E04 S01、E05 S01–S05、E13 S01–S03/S12 均已完成，不要重复实现。
- 下一张可独立推进的 3D Space 卡为 E04 S02：两点标定。必须复用 S01 的 Ready/Clean 底图来源，冻结像素点→世界坐标变换、退化条件、精度、持久化和 revision 语义；不提前混入 S03/S04 编辑命令。
- E04 后续固定为 S02 两点标定 → S03 通用元素选择/属性面板 → S04 多选/对齐/分布/阵列；S04 完成后才解除 E07 S05 的采用前置条件。S01 的默认扫描器仍失败关闭，生产启用前必须配置真实扫描引擎和共享耐久文件卷。
- E13 S04 等待 E02 S03；E13 S05 等待 S04 和正式供应商证据。默认无外部 Provider、配额失败关闭与规则路径独立这三条回滚面不得降级。
- E02 S01 中立实验工具已集成，但最终签收仍需数据/QA 提供正式 20 文件黄金集（Calibration 10 / Validation 5 / Holdout 5、L1–L5 各至少 4）及 DWG/DXF 版本/实体矩阵。
- 法务/采购需确认 ODA 正式 Web/SaaS 授权；工程需获得校验过的 ODA Windows/Linux SDK 包。APS 备试需批准区域、DPA、删除/保留证据和非生产凭据。平台/安全需提供 8 vCPU / 32GiB 的冻结隔离 Worker。
- 外部输入齐全后，在同一冻结环境对 ODA 与 APS 各黄金样本 5 次、50MiB/100 万实体/200MiB 上限、超时/取消/并发进行评分；低于 ADR-0001 的 80 分硬门槛不得主选，若都失败则继续阻断 DWG Beta。
- E07 S01–S04 已完成；标准仓数据包已可确定性重建 500 货架、10,000 库位、库存、任务和异常场景。E07 S05 继续等待 E04 S04，不提前采用。
- E13 后续仍须保证原始 CAD 不外发；Mock、本地和外部适配器只能在 S05 使用 S01 的同一端口实现，规则路径不得依赖外部 Provider。
- 本机 `KOUSQLSERVER` 的 Windows 集成认证已可用于提权测试。E04 S01 文件安全/底图事务为 6/6 passed，E05 S01–S04 与受影响 Clone 的真实 SQL 串行链为 11/11 passed；默认门禁仍保留环境隔离 skip 语义，不能把未启动的 SQL-gated 测试写成通过。
- `0d25da4d` 中 E05–E12 是候选证据，不得整包 merge/cherry-pick；必须重新核对依赖、迁移链和产品冻结范围。
- P2.5 不混入本轮 Space 基线，待 E01 基线稳定后另行评估。

## 已完成：GR-VP 波

权威计划：`docs/superpowers/plans/2026-07-17-general-role-vperm.md`。

1. T6 已通过 `d79a39c` 合入并推送 `main`。
2. API/Web 双镜像已重建并运行，Web 使用干净 `main` 产物。
3. 当前注册租户仅 `DEFAULT/A1`，已验证 RoleId=10 的 4 菜单/8 动作、admin 零扰动，以及 `qa_general` 的本人审批、归属闸和无权端点 403。

T1–T7 已完成，不要重复铺设。T7 细节见 `.superpowers/sdd/gr-vp-t7-report.md`。

## P1：GR-VP 收口票

- PMS/Sys 平台管理页仍未统一铺 `v-permission`。
- 决定标准角色是否使用日文显示名。
- 为 B1/C1 等租户建立/挂接一般用户。
- 评估 insert-only 标准角色种子是否应在管理员删除基线键后自动补回。
- 若后续恢复的数据库重新出现 B1/C1/D1，补跑 T7 四租户 SQL 矩阵；当前库只有 A1，不要为凑验收数虚构租户。

## 已知跨波跟踪票

- PLAN/PUB Attachment 写端点补强业务权限与前端权限 UX。
- WF `SignalRWfNotifier` 从广播过滤改为定向用户推送。
- Space `CodeEngineService` 大规则集 rackSeq 应改为 Zone 级完整排序。
- FIN BudgetLine 需要版本级并发控制。
- WFS/Space 各 plan 文末保留若干 live QA、移动端视觉和清理票；动手前读对应最新计划的“完成后跟踪票”。

## 文档维护任务

- 每完成一项任务，更新 `PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md` 和 `CHANGELOG-AI.md`。
- README/CODEMAP 的规模数字已经过时，未来可单独刷新，但不要在权限任务中夹带修改。
