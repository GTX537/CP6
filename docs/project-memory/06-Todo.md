# 当前待办与优先级

## P0：Space V1 下一批受控提取

- 以 `36f534d9` 为唯一集成基线，从 `0d25da4d` 只提取 E01 S06（文件安全与保留）；单独审查 Quarantine→Scan→Safe/Rejected、引用感知删除、到期清理、迁移链和共享依赖并完整回归。
- E01 S06 保持独立功能提交、no-ff 集成提交和交付报告；不得把候选中的后续 Scene/Asset/Planning/Publish 能力夹带进入。
- 为自动化执行身份提供可认证的 SQL Server 测试连接后，补跑当前 24 个 SQL-gated Space Integration 测试；2026-07-30 的本机尝试在业务断言前被 TLS/SSPI/Guest 身份认证阻断，未补跑前不得把“跳过”写成“通过”。
- E01 完成后再进入 E02 S01 CAD 选择试验与 E07 WMS 契约/模拟器；E13 Provider 试验按冻结批次并行评估。
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
