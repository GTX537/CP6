# 当前待办与优先级

## P0：Space V1 下一批受控实现

- 当前功能集成检查点为 `d3c2da75`。E07 S01–S05、E13 S01–S12/S16 及其报告均已进入 `integration/space-v1-20260730`，不要按旧计划重复实现；`main` 仍不承接这批开发切片。
- E13-S11 已完成用户可见取消安全点、同输入重试分类、权威 CommandBatch 对账、Failed/Stale replacement Run、RuleOnly 降级和真库运维演练；继续保持 S10 单事务零部分 Draft 写入边界。生产 BuildScene executor 仍失败关闭，不能把 replacement 已排队描述成真实 Provider 端到端完成。
- 下一张建议实施卡为 E13-S13 外部用户拒绝与数据外发门禁：先盘点所有 AI/恢复/策略/用量端点与 Provider request 建模的既有拒绝，再补齐稳定 403、脱敏字段白名单、审计和测试矩阵。E13-S17 迁移、前向修复与保留清理的依赖也已满足，可作为不依赖外部 CAD/Provider 的后续独立卡。
- E13-S10 已消费 E13-S09 Decision 并原子写入 Draft，但真实 Worker `LoadLockedFacts` 自动接线、不同 SourceHash 的确定性几何建议继承和人工确认仍未完成，不能用猜测匹配绕过失败关闭。
- 继续保持批量 High Accept 默认关闭、原始 CAD 不外发、外部 Provider 默认关闭、配额失败关闭、规则路径不依赖 Provider，以及 Draft/Published/WMS/设备边界隔离。
- E02 S01 中立实验工具已集成，但最终签收仍需数据/QA 提供正式 20 文件黄金集（Calibration 10 / Validation 5 / Holdout 5、L1–L5 各至少 4）及 DWG/DXF 版本/实体矩阵。
- 法务/采购需确认 ODA 正式 Web/SaaS 授权；工程需获得校验过的 ODA Windows/Linux SDK 包。APS 备试需批准区域、DPA、删除/保留证据和非生产凭据。平台/安全需提供 8 vCPU / 32GiB 的冻结隔离 Worker。
- 外部输入齐全后，在同一冻结环境对 ODA 与 APS 各黄金样本 5 次、50MiB/100 万实体/200MiB 上限、超时/取消/并发进行评分；低于 ADR-0001 的 80 分硬门槛不得主选，若都失败则继续阻断 DWG Beta。
- 本机 `KOUSQLSERVER` 已用于 E13-S11 与 S10 原子 Apply/Recovery 整组复验，结果 14/14、0 skipped；本切片未重跑默认 Space Integration 全量，不能把未启动的环境门禁写成通过。
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
