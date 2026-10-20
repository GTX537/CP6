# 第二十八轮：Main IAM 引用与组织并发闭环

执行：COORD当前聊天，writing-plans / executing-plans，单人串行。基线 `85fd27e3bf619a541a9fece2be32d6dbf8a89f53`。持续目标已建立，24小时检查窗自2026-10-20T04:05Z起；估计本代理实际6–9小时，故障余量12小时，提前完成即交付。

W27跨标签页认证恢复(PR336)、明确SSO首次身份审批(PR338)、初绑/JIT与Main原子映射(PR340)已经合入；复用原证据，不重新实施。接续S5 SYS-IAM-01 v0.2 §3/4已接受的用户角色与组织合同。

## 顺序与交付

| 任务 | 范围 | 本代理耗时 |
|---|---|---|
| W28-01 | 用户创建/修改与主附角色、部门、经理合法引用、原子保存、CAS、原操作回执 | 2–3小时 |
| W28-02 | 组织树版本、移动/负责人/引用保护、影响与准确恢复 | 2–3小时 |
| W28-03 | 用户/组织页面冲突、UNKNOWN与草稿恢复，受影响认证组合边界 | 1–2小时 |
| 总收尾 | 集中审查、正常PR、台账/Handoff、分支和席位处置 | 45–75分钟 |

架构：沿用Main PostgreSQL `cp6_authentication_v1.acquire()`共同事务门、实际writer revision、IdentityChange/Outbox、当前actor权限与强审计；没有第二身份权威。用户/角色/组织引用均按当前可信tenant与准确版本，主附角色一次业务命令。旧路由采用明确适配，缺少新合同返回受控错误，不默默覆盖。精确操作回执与关闭屏障防迟到写；未确认提交不自动重放。

## W28-01（DEV-1，codex/w28-01，已真实START）

文件：Core/Services/Sys新增IAM行政命令与恢复服务；UserController/UserRoleController及用户组织适配；PostgreSql/Migrations/Core新增必要revision/receipt实际writer；相关Platform PG/HTTP测试。页面最低兼容随合同同窗，其余交互W28-03。

- [x] 复现旧入口不核角色/部门/经理有效引用和缺CAS；保留真实失败。
- [x] 白名单创建/编辑、主附角色同保存，合法null；角色停用历史保留但不新授，跨tenant/失效引用拒绝。引用版本含ABA，当前actor等根锁后重验。
- [x] 原密码服务与明确reset政策、refresh吊销同事务；不暴露密码/身份秘密。审计、IdentityChange/Outbox与回执同提交；UNKNOWN按原operation查询/封闭。
- [x] PG实际writer并发、失权、引用变化、事务故障/原操作恢复及HTTP相关行为验证。一次作者SPEC/QUALITY、单份DELIVERY就绪；正常PR合并、finish待实际核对。

## W28-02（组织API与页面同窗）

文件：DeptService/DeptController/UserOrg适配、必要PG组织revision与writer门；Dept DTO及相关PG/HTTP测试。父/leader/null/停用祖先语义按原设计；整树诊断不把孤儿升root用于权限。

- [ ] 原树版本、预览影响、自身/子孙成环和跨tenant引用检查；提交再核版本。
- [ ] 同事务移动整子树，拒绝子部门/用户/已知自定义scope等引用删除；缺业务删除/hold权威时明确关闭破坏性入口，不默认级联。
- [ ] 实际writer加入共同门与版本，既有Main身份传播和scope当前判定；历史审批快照不改。
- [ ] PG并发树更新/回滚/原操作恢复；单份交付、集中审查、合并与finish。

## W28-03（并入W28-02）与收尾

文件：cp6.web/src/views/pms/UserView.vue、DeptTreeView.vue，相关api/types与行为测试；复用已有跨标签页认证协调器。

- [ ] 主附角色一致选择、停用项历史展示；412保非敏感草稿，重载后明确重新确认；结果未知保原operation，不生成新ID盲重放。
- [ ] 移动预览/确认、失败/迟到响应/切换主体保护，只有确认成功才显示已保存；不声称CRM投影已同步。
- [ ] 相关Vitest/typecheck/真实页面夹具，按变化选择PG/HTTP组合回归；复用未变W27认证结果。
- [ ] 同步roadmap与生成页、四状态、Handoff；每项正常合并后停止自有进程、精确清理任务分支并释放席位。最终核无遗留写入者。

## 环境及真实边界

四仓合计2开发+1集成：DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018`仅当前产品任务，DEV-2 `D:/CP6-worktrees/dev-2`仅W28-HANDOFF；集成席位空闲。均已通过统一manage入口，不新建目录、源码副本或依赖包。复用已有NuGet/.NET输出及前端node_modules junction，相关PG实例在既有本地测试根内隔离且每次停止；原失败日志保留。

不触发Actions，不替真实租户批准issuer/角色，不安装既有环境迁移，不做生产部署。完整IAM/CRM/Owner和原业务验收仍按真实范围NOT_RUN；本轮不扩展CRM消费和隐私删除需求。

## 实际窗口接续

W28-01：PR347，源`9eb79591380f8190afdb0272c9e84b73fc111984`，main`1593d573837e46a579074ed3224f47f59d9ce5f0`；#346 Closed，77有效.NET/7前端/2Chrome+typecheck。codex/w28-01本地/远端已删除，DEV-1干净释放后经start重新领取W28-02；没有增加物理目录。Handoff 470495412bcaf65b3802f479ef1752b11a795973。

## 范围与验证接续

W28-02的组织页面与API同分支交付；原W28-03恢复/组合验证已在同一窗口完成，不创建codex/w28-03。已完成当前用户/组织页面冲突、原操作UNKNOWN、跨Tab与身份撤销保护，以及Main当前CRM输出/审批路由组合验证。50项唯一有效.NET/PG/HTTP、14Vitest、5Chrome夹具、vue-tsc通过/复用；一次作者集中审查无阻断，原失败保留。所有本窗测试进程已停止。剩余步骤是正常PR、远端核对、finish及本轮协调资料最终同步。
