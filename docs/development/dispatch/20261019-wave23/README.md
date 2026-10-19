# 第二十三轮：平台管理员共同事务保护

## 第二十三轮有限范围已完成

两项公共基础及其P0闭环完成：#308平台管理员防自锁死/必需审计经[PR309](https://github.com/GTX537/CP6/pull/309)合入；#311普通创建白名单经[PR312](https://github.com/GTX537/CP6/pull/312)合入，最新产品main `db3526b6b1c4f6854c65d0a9fface8030ff63e15`，均远端核对且IssueClosed。各自单份DELIVERY与一次作者集中SPEC/QUALITY保留。

全轮77去重有效通过：63原生PG/HTTP、14单测/schema；W23-02的5项原密码测试不重复累加。原产品RED、测试预期/夹具失败及修正记录全部保留。新迁移只在自有PG新库/已填数据副本验证，既有环境尚未安装；新PG授撤采用前必须完成前向安装。

两个窗口CLOSED、无资源占用，10自有PG根stopped、55435/HTTP释放。新worktree/代理/Actions0，原7项用户资料保留，D余约44.6GiB；构建/NuGet与唯一原目录复用。文档收尾远端核对后完成外部持续目标，24小时是检查点，不要求空等。

剩余优先：当前调用人/会话认证和撤权最终门、全IAM权威轴/写者/引用CAS、CRM基线增量投影发布、实际Owner新效果提交、普通写账号/既有安装升级恢复及真实业务。完整WP02与247运行NOT_RUN不提升；未开下一实施窗口。


> W23-02候选完成：[BUG311交付](../../evidence/WP02-03/user-create-whitelist/DELIVERY.md)，专用创建白名单和服务端身份，17项相关验证通过；待正常集成/远端核对。

> W23-02优先修正P0 [#311](https://github.com/GTX537/CP6/issues/311)：3项实际PG/HTTP RED确认普通创建可写平台/租户/认证字段；沿原START，不重复ACK。

## W23-01已集成；接续W23-02创建白名单

W23-01 `d45fef242dd8e070e99890b98d4752bf7906d881` 已经[PR309](https://github.com/GTX537/CP6/pull/309)合入 `75fef4ac76e7908abba5592ff8f46e7dacfaf12a`，#308Closed，Handoff `e4c9bfda2b0e0b73250d006615ab1eae594cf650`远端回读。65项有效验证和全部失败来源见原单份报告。

继续同一24小时目标，新增顺序窗口W23-02：按已接受S5原文第47行，核实Add直接接收Sys_User造成的服务端身份/租户/平台权/认证字段边界；真实HTTP复现后登记Issue并优先修正。采用专用创建白名单，保留合法用户名/密码/昵称/主角色/Enable/部门/经理/邮箱输入及密码处理；Id/TenantId、平台权、认证状态和审计元数据由服务端确定。不会将完整关系CAS/权限最终门宣称完成。

允许路径：UserController、UserCreationBoundaryPostgreSqlTests、原UserControllerPasswordTests、单份WP02-03/user-create-whitelist/DELIVERY。复用当前目录与W23原生HTTP fixture/缓存，55435串行。数据库模式不再扩展，普通SQL Server无需新增验证。实际创建HTTP、危险额外字段、合法正例/跨租户/无权拒绝、相关OpenAPI和旧密码行为是相称检查；作者一次集中审查后正常合并。当前仅登记，测试NOT_RUN；单次真实START在开工时发。


> W23-01候选完成：[BUG308单份DELIVERY](../../evidence/WP02-03/platform-admin-transaction/DELIVERY.md)，65项有效通过，作者集中审查无阻断；待正常集成/远端核对。原审查失败与夹具修正均保留。

> 当前优先修正P0 [#308](https://github.com/GTX537/CP6/issues/308)：原生PG复现并发撤销和直接停用/删除可移除最后管理员。分支 `codex/bug-308-platform-admin-transaction-20261019`，沿同一真实START/领取范围，不另起审查或ACK。正常主线合并与远端核对前保持Open。

本对话单独执行，复用现有目录。持续目标从2026-10-19T07:24:41Z开始，24小时检查点2026-10-20T07:24:41Z（纽约03:24:41）；提前完成即交付，时限不替代完成证据。基线main/origin/main `ae73ad97b5f6327b05d59ccc0031ad4c52662d50`，W22已收尾；当前Issue0、无本任务Actions。

## 顺序任务与退出条件

| 阶段 | 输出 |
|---|---|
| 1 固定行为 | 真实PG复现并发撤销、停用/删除最后一名启用平台管理员；登记已证明BUG，保留失败 |
| 2 共同事务 | PostgreSQL原生稳定锁根/防自锁死约束，实际授撤与必要安全审计原子保存 |
| 3 定向实证 | 服务及直接SQL/批量、并发双向、rollback/savepoint、陈旧事务、故障取消、跨租户全局集合和合法重复操作 |
| 4 审查交付 | 作者一次集中SPEC/QUALITY、单份报告、正常PR合并及远端核对、共享状态/Handoff/资源/检查点 |

## 设计和范围

依据[WP02-03](../../task-plans/WP02.md#wp02-03)与[SYS-IAM-01平台权共同门](../../../design-handbook/modules/SYS-IAM-01.md)：保留原Enable且IsPlatformAdmin集合、E-SEC-032/037和带外审计默认平台租户。平台权限不由Role1推导。

现有PlatformAdminService先计数再保存，普通User更新/删除不共门，安全审计另存且可吞失败。先以原生行为固定缺口，再实现最小修正。计划新增PostgreSQL专属前向迁移：稳定单例事务锁根，仅作为平台管理员不变量的串行边界；不宣称其为完整IAM authority epoch。数据库约束覆盖安装后的正常DML（含直接SQL/批量）；超级用户禁用trigger/DDL等管理绕过不作为应用权限。迁移只在自有隔离fixture执行，不操作既有库。

授撤服务在当前同一事务/调用方savepoint中读当前事实、保存目标与必要审计；失败无半保存，未知提交不自动重放。不以旧可重复读快照当当前授权。只交付此防自锁死和审计边界；完整调用人认证最终复核、SSO/2FA策略、全IAM持久权威向量、跨库Main/CRM撤权、Owner业务门仍需后续。

测试集中在新PlatformAdminGuardPostgreSqlTests，复用TenantManagementPostgres迁移fixture/当前端口55435；新增迁移会影响受影响安装及身份写者测试，按真实输入选择必要回归，不全仓运行。保留旧SQL Server实现历史，不新增双库要求。普通文档仅diff/链接/台账检查。

## 资源和交付

唯一实施目录 `D:\CP6-worktrees\wave17-dispatch-20261018`；串行构建沿用D:/CP6-local-test/wave18/w18-01/artifacts，NuGet沿用现缓存，新增运行证据D:/CP6-local-test/wave23。零新树/代理/Actions；保留原7项用户资料、历史失败日志及PG根。只停止本轮自有实例。

单份[DELIVERY](../../evidence/WP02-03/platform-admin-transaction/DELIVERY.md)在候选交付时生成。没有独立审查代理，真实DEV/业务/生产与完整WP02不提升。每次push/PR复核工作流内容和路径，普通Actions不触发，不修改保护。
