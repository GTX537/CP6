# WP12-14 拆卡候选 · 手动触发动作词条

`WorkCalendarConnectorPermissionSeed.OwnerActions` 为 `oa-flow-admin`（734）逐租户登记 `conn.trigger.fire`（手动触发流程）。词条通过现有启动播种写入 `Sys_MenuAction`。当前权限服务和角色授权规则沿用原实现。 [登记入口](../../../../CP6.WebApi/Seed/WorkCalendarConnectorPermissionSeed.cs)

WP12-14 拆卡，词条交付，完整未完成。该候选在 #1318 草稿中登记，实际 Main 采用以合并后回执为准。

## 词条验证

Windows 本机隔离 PostgreSQL 18.6，以两套合成租户和已登记的旧词条及授权为起点。每次播种后从数据库读回词条、比较全部角色动作及菜单授权，并用现行 `PermissionService` 查询每个角色。 [定向用例](../../../../CP6.Tests/Erp/OaConnectorControlPostgreSqlTests.cs)

| 核对项 | 首次播种 | 重复播种 |
| --- | --- | --- |
| 两租户在菜单 734 下的 fire 词条行数 | 各 1 条 | 各 1 条 |
| 本次 `Sys_MenuAction` 新增 | 2 条 fire | 0 条 |
| `Sys_RoleAction`、`Sys_RoleMenu` 相对播种前的变化 | 0 | 0 |
| 两租户的默认管理员、仅有菜单的角色、仅有旧编辑动作的角色 | 6 个主体均无 fire | 6 个主体均无 fire |
| 流程 Instance、Token、旧触发流水、Invocation、连接器命令回执、连接器结果 Outbox、审批启动 Outbox 的增量 | 各 0 | 各 0 |

权限用例先确认三个角色实际能访问菜单。管理员和旧编辑角色的 `FlowTrigger.Edit` 可用，菜单角色无此旧动作。PostgreSQL 定向 2/2，含播种和现有只读选项权限。种子与权限单元回归 33/33，合计 35/35，零失败或跳过。隔离 PostgreSQL 已停止。

## 证据绑定

- 采用基线 `55667a87aea03405467dc47ec76ea938c320c69f`。受验源码提交 `9ca28b0af3f567c52b83b7404490343d2d4bc699`，构建输入和提交中的三个修改文件逐一匹配。受验草稿 `74d44acbb4e04e134b1fbabac6c5c6dbba3e7c8c`。正式拆卡候选继续使用三个字节一致的源码及测试 Blob。
- 原始 TRX `wp12-14-fire-candidate2.trx` 的 SHA-256 为 `2dc658d51a14920e52c21ae7a7476aba65a10eb46ef1b73f2965ded176b7deb7`。TRX 内记录首次及重复播种的数据库观测。候选测试程序集 `CP6.Tests.dll` 的 SHA-256 为 `16045f56d8b533c2d840b72ff3a8f81ae05ed8a3d21e434e9bc9fdb62cedba37`。
- 源码验证为原草稿的 35/35 同源证据。拆卡新增台账、计划、本报告和生成视图的检查记录随草稿 PR 绑定候选，交总控独立复核。

## 余卡与原验收

[WP12-14b](../../task-plans/WP12.md#wp12-14b) 为唯一余卡，状态为 `BLOCKED`，准入为 `NOT_CHECKED`。原卡定义和七项直接前置保留。余卡再查父卡词条实际采用，原前置展开的 26 项完整交付按各自采用范围复用。WP14-07、WP14-08、WP14-09 继续等待 WP12-14b 完整交付。

- RP-CONN-01 PinnedWorkflowAdmission 为 UNPROVEN。先由已采用的新 Workflow 和实际效果 Owner 共同确认正式可触发 Flow、确切 publication/version、schema digest、依赖和 activation。有 FORM 时沿真实绑定及原提交合同。须证明 Trigger 专用 Main PostgreSQL Uow 按原键同事务持久化冻结 vars、TriggerVersion/Invocation、真实 Instance/起始 Token、调用或时隙结果、命令回执、Outbox 和审计，并按原键恢复同一结果。
- RP-CONN-02 WorkflowAuthorityRegistration 为 UNPROVEN。先由 SYS-IAM、来源和业务 Owner 确认可采用的principal、登记及代次、封闭 schema、可信租户/环境、Trigger、当前 Starter 受托和变量权、实际效果 Owner 绑定。采用共同锁和最终 WF_AUTH 复核来源状态、当前资格及写者。撤回后保留原合法接收事实并阻断新实例。TIMER 沿已登记调度主体和时间合同，手动沿当前用户、独立 fire 与实际 Owner 门。

两项运行合同均有 Main 采用证据并经独立核实后，按 TIMER、EVENT/MESSAGE、手动的顺序验收。

- CONN-02、04、05、06。发版后按原时隙恢复冻结 varsSnapshot、pins 与命令。两个实际 worker 竞争、事务回滚及提交后丢回执按原键恢复，实例唯一，nextDue 独立对账。LATEST_ONE_SKIP_MISSED 择一并保存跳过范围。核对原日历、时区、cron、DST 重复与缺失、UTC/offset/fold、旧预览失效和 RP-CONN-04 实际时间提供方。
- CONN-07、08、09。认证登记的 EVENT/MESSAGE 按原 ID 与完整指纹持久接收，同键同体回原事实，同键异体按原合同 QUARANTINED/409。验证原 64 KiB、封闭 schema 和拒绝零实例。来源或 Starter 撤回时保留 Inbox，依最终门阻断新实例和业务效果。
- CONN-13、14。context 与无副作用 preview 冻结原输入。仅旧 Edit 或 Starter 资格不符时确认拒绝，核对 403 与预览失效。实际确认、超时回查、重试和刷新按原 commandId/previewId 只产生一个真实实例，新意图重新通过效果 Owner 门。

四路真实启动处于 `BLOCKED`，九项原 AC 保持 `NOT_RUN`。OA-CONN-01 原 AC **16/32**，目标开放。CONN-21 随 WP12-18，生产 provider/Owner 为 `UNPROVEN`。

## 工时与交接

- 此前已计有效工时 1777.402 秒，含上一代 714.228 秒及本代首轮 1063.174 秒。此前领取前核对 139.032 秒另列。
- 以 `2026-11-23T15:58:11.260238+00:00` 回读拆分决定开始，到候选快照 `2026-11-23T16:10:33.327809+00:00` 新增 **742.068 秒**。原卡入账 **2519.470 秒**，额度 28800 秒，强制拆卡线 43200 秒。WP12-14b 额度 **26280.530 秒**，对应 150% 强制拆卡线 **39420.795 秒**。卡池仍为 2052 小时。
- 按原 DELIVERY 的完整提交 `5e9325efea02ff603691ef3369a4a70ed90f5555` 和路径 `handoffs/CP6-DEV-20261012/tasks/WP12-05/messages/20261123T141959Z-cp6-exec-2-delivery-closeout/MESSAGE.md` 查重，在本次合资格拆卡候选单次补记 WP12-05 **678.850 秒**，该卡至 **7568.334 秒**。WP12-17 已入账的 616.455 秒保留。计划累计由 198695.671 至 199374.521 秒，计入本卡后为 **201893.991 秒**。快照后有效尾差留待正式收尾记录。
- 原 `codex/wp12-14` 和 DEV-1 继续承载本草稿。总控独立复核通过后，另按实际采用回执核对分支处置、工作树和席位。

[原草稿 #1318](https://github.com/GTX537/CP6/pull/1318) · [单卡执行计划](../../planning/20261117-card-execution.md)
