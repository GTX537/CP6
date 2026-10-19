# 第二十四轮：平台授撤当前调用人最终权限门

> 候选完成：[单份DELIVERY](../../evidence/WP02-03/platform-admin-current-actor/DELIVERY.md)，66项有效通过/38原生PG，作者SPEC/QUALITY无阻断；#315待正常合并与远端核对。

> 已确认P0 [#315](https://github.com/GTX537/CP6/issues/315)：5项PG/HTTP RED，入口filter后撤权/停用/删除、错tenant与无认证主体均未拒绝；沿原START优先修正。

用户要求继续公共基础，当前对话单独执行并复用原目录。预计**代理实际执行1.5–3小时**，不是用户工时；目标起点2026-10-19T08:17:12Z，24小时检查点2026-10-20T08:17:12Z。提前完成即交付，不空等时限。主线6c5df760e0d56d3cfb75fd4e919267e5931c8401，W23已完成，当前活动Issue0。

| 顺序任务 | 退出证据 | 预计代理耗时 |
|---|---|---|
| 固定行为 | 实际PG/HTTP复现过滤器之后撤权及主体绑定缺口，登记Issue | 10–20分钟 |
| 当前权限门 | 完整主体绑定，提交前当前平台权限在原PG根保护内重读，与撤权/停用/删除顺序互斥 | 40–80分钟 |
| 专项验证与审查 | 两种竞争顺序、回滚/陈旧快照、实际HTTP合法/拒绝和一次作者SPEC/QUALITY | 25–45分钟 |
| 正常集成收尾 | PR合并/远端main、Issue、共享状态/Handoff/资源/检查点 | 15–30分钟 |

## 限定设计与准入

依据[SYS-IAM-01](../../../design-handbook/modules/SYS-IAM-01.md)平台三门：模拟期间拒绝优先E-SEC-034，平台claim与当前持久平台资格E-SEC-031；主体/tenant必须来自同一已认证身份，拒绝半登录和矛盾绑定。平台跨租户操作指目标可跨租户，不允许调用人借用别的tenant主体。角色1不替代平台标志。合法无Name access保留。

复用W23已安装的PostgreSQL管理员根，不另造权威epoch。实际Grant/Revoke需在root事务内检查当前actor（AsNoTracking），再读目标/保存效果和必要审计；撤权/Enable/删除DML已参与该根。先撤权则后操作拒；操作先取得根则允许其先提交，撤权等到之后生效。不能把入口filter读过一次当作最终提交许可。错误和未知提交不得自动重放。测试直接服务调用使用明确的已认证fixture主体，不能默认绕过新安全门。

本窗口不安装既有环境、不新增迁移；完整认证版本/会话谱系/2FA/SSO政策与全IAM写者最终门不在本次完成声明中。其他平台入口（租户管理、模拟）只能复用主体过滤器，不能宣称其已取得授撤事务最终门。完整CRM投影、Owner业务最终门和247运行NOT_RUN不提升。

## 资源与交付

唯一复用D:/CP6-worktrees/wave17-dispatch-20261018，每完整任务从最新main新建codex分支。原7项用户资料不动。PG18自有55435串行，证据D:/CP6-local-test/wave24/platform-current-actor；复用W18构建和既有NuGet缓存。零新worktree/代理/Actions/既有DB操作/部署。新发现P0优先登记修正，不另等ACK。

一次作者集中SPEC/QUALITY（非独立审查），仅相关验证；W23有效未变证据引用。单份DELIVERY预定docs/development/evidence/WP02-03/platform-admin-current-actor/DELIVERY.md。push/PR前复核工作流与过滤，正常合并后远端核对才关闭Issue/目标。
