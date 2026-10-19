# 第二十三轮：平台管理员共同事务保护

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
