# 当前待办与优先级

## P0：继续当前 GR-VP 波

权威计划：`docs/superpowers/plans/2026-07-17-general-role-vperm.md`。

1. T7：将 T6 合入 `main`，完成 GR-VP 全波终审。
2. 重建 API/Web 双镜像并启动 Compose；前端有新权限代码，Web 镜像不可跳过。
3. 验证四租户标准角色种子、admin 零扰动与 `qa_general` 的 8 键/4 菜单/归属闸/无权端点 403。

T6 已由 `4bb7512` + `cf20d42` 完成；不要重复铺设。Docker Desktop 当前未启动，部署前先启动并确认 `docker compose ps`。

## P1：GR-VP 收口票

- PMS/Sys 平台管理页仍未统一铺 `v-permission`。
- 决定标准角色是否使用日文显示名。
- 为 B1/C1 等租户建立/挂接一般用户。
- 评估 insert-only 标准角色种子是否应在管理员删除基线键后自动补回。

## 已知跨波跟踪票

- PLAN/PUB Attachment 写端点补强业务权限与前端权限 UX。
- WF `SignalRWfNotifier` 从广播过滤改为定向用户推送。
- Space `CodeEngineService` 大规则集 rackSeq 应改为 Zone 级完整排序。
- FIN BudgetLine 需要版本级并发控制。
- WFS/Space 各 plan 文末保留若干 live QA、移动端视觉和清理票；动手前读对应最新计划的“完成后跟踪票”。

## 文档维护任务

- 每完成一项任务，更新 `PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md` 和 `CHANGELOG-AI.md`。
- README/CODEMAP 的规模数字已经过时，未来可单独刷新，但不要在权限任务中夹带修改。
