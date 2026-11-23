# WP12-14 手动触发动作词条交付草稿

`WorkCalendarConnectorPermissionSeed.OwnerActions` 为 `oa-flow-admin`（734）逐租户登记 `conn.trigger.fire`（手动触发流程）。词条通过现有启动播种写入 `Sys_MenuAction`。当前权限服务和角色授权规则沿用原实现。 [登记入口](../../../../CP6.WebApi/Seed/WorkCalendarConnectorPermissionSeed.cs)

## 候选验证

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

- 采用基线 `55667a87aea03405467dc47ec76ea938c320c69f`。受验源码提交 `9ca28b0af3f567c52b83b7404490343d2d4bc699`，构建输入和提交中的三个修改文件逐一匹配。草稿提交只增加这一份报告。
- 原始 TRX `wp12-14-fire-candidate2.trx` 的 SHA-256 为 `2dc658d51a14920e52c21ae7a7476aba65a10eb46ef1b73f2965ded176b7deb7`。TRX 内记录首次及重复播种的数据库观测。候选测试程序集 `CP6.Tests.dll` 的 SHA-256 为 `16045f56d8b533c2d840b72ff3a8f81ae05ed8a3d21e434e9bc9fdb62cedba37`。
- 本回合到源码候选快照的有效工时为 801.144 秒，后续交付登记另计。候选提交和证据随草稿 PR 交总控独立验收。

## 待验范围

WP12-14 的台账为 `PLANNED`。TIMER、EVENT、MESSAGE 与手动真实启动为 `BLOCKED`，RP-CONN-01/02 为 `UNPROVEN`。`CONN-02、04–09、13、14` 待验。`CONN-21` 随 WP12-18。生产 provider/Owner 为 `UNPROVEN`。
