# 当前待办与优先级

## P0：继续当前 GR-VP 波

权威计划：`docs/superpowers/plans/2026-07-17-general-role-vperm.md`。

1. T4：扫描 `cp6.web/src/views/mes`，按 MES 权限键事实源建立按钮映射，只增加 `v-permission`。
2. T5：扫描 `views/fin`，以 FIN 权限反射测试/键清单为准铺设指令。
3. T6：扫描 `views/pur`、`views/plan` 及独立 PLAN/PUB 页，铺设 PUR/PLAN/PUB 指令。
4. 每任务运行 `vue-tsc`、Vitest、前端 build，写 SDD 报告并单独提交。
5. T7：全波终审、合并 main、重建 API/Web 镜像、权限种子 SQL 验证和真实一般用户端到端冒烟。

共同规程：纯读按钮不贴；对话框确认若入口已守则不重复；一个按钮多端点使用主动作键并记录偏差；任务范围仅 template 指令，不顺手重构。

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
