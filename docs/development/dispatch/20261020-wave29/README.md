# 第二十九轮：角色生命周期与封闭授权编辑

执行：COORD当前聊天独立，writing-plans / executing-plans；测试先复现真实缺口，精简相关验证，每完整任务一次作者SPEC/QUALITY。基线563850b2db83bff6587109e5f636be7df2642847；持续目标已建立，24小时检查窗从2026-10-20T05:40Z起，预计本代理实际3–5小时，故障余量8小时，提前完成即交付。

W27跨Tab认证/SSO首次批准与映射、W28用户引用/组织CAS已合入，直接复用。原接受设计S5 SYS-IAM-01 v0.2 §3/5/9是本窗合同；不修改动作并集、Scope数值MAX或字段显式行MIN后完整域默认1语义，不启动PRIV物理删除或CRM完整Owner采用。

| 窗口 | 完整交付 | 代理预计时间 |
|---|---|---|
| W29-01 | 角色白名单创建/编辑/启停、版本和引用检查、准确操作恢复、角色页面 | 45–75分钟 |
| W29-02 | 菜单动作/数据范围/字段权限封闭编辑与版本/资源域/引用验证，三个配置页面恢复 | 90–150分钟 |
| W29-03 | 组合边界、每任务集中审查/正常集成、台账/Handoff及席位收尾；测试随相关任务执行 | 30–60分钟 |

## W29-01：角色生命周期（DEV-1 / codex/w29-01）

文件：新增Core/Services/Sys/IamRoleAdministrationV1.cs与WebApi/Controllers/Sys/IamRoleController.cs；RoleController旧路由适配、IamAdministrationV1共用回执、Entity/DTOs/Sys角色白名单；RoleView.vue及api/sys/iam.ts、iamOperationRecovery.ts；Platform/IamRoleAdministrationPostgreSqlTests.cs、实际HTTP和相关组件/浏览器测试。

- [x] 真实PG旧入口复现：缺版本覆盖、删除仍被主附角色/SSO/授权或其他业务引用的角色；确认红测来源后登记Issue。
- [x] 角色仍以(可信TenantId,用户定义int RoleId)定位；CreateDate服务端，名称/说明/Enable/OrderNo白名单，RoleId不可随编辑改变。沿用既有role revision，不凭RoleId=1授平台权。
- [x] 实际认证共同门后再核actor/当前权限、expectedVersion/ReferencesVersion及ABA；审计/身份Outbox/原操作回执同PG事务。缺IdentityRuntime拒绝效果；查询/封闭原操作不盲重放。
- [x] 删除显示当前主附成员、SSO及已知授权引用；缺完整业务/审批/PRIV保留权威时明确409，只提供受控停用。停用保历史角色关系，不恢复旧会话。
- [x] 角色页面采用新合同；412保非敏感草稿、明确重读确认，跨Tab复用公开operation定位/锁，权限撤销/身份变化清页面。旧无版本入口受控拒绝。
- [x] 相关PG真实writer并发/回滚/丢ack、HTTP Cookie/CSRF及实际页面；一次集中审查、单份DELIVERY、正常PR与manage finish。

## W29-02：封闭授权编辑（复用释放后的DEV-1）

文件：新增IamRolePolicyAdministrationV1与对应Controller；RolePermService/RolePermController及RoleController菜单入口adapter；DTO显式envelope；RolePermView.vue/DataScopeView.vue/FieldPermView.vue、共用恢复；相关Platform PG/HTTP与组件/浏览器测试。必要目录writer迁移仅在证据确认缺口后加入，并覆盖普通身份安装/旧填充数据。

- [x] 先验证未知资源/非法Access/重复字段/外tenant部门、无版本覆盖的实际行为，保留失败。
- [x] 功能矩阵只能引用实际菜单和已注册动作；动作必须属于已授菜单。数据范围必须来自完整注册资源与支持类型，自定义部门来自同tenant健康祖先链。
- [x] 字段仅1/2/3，资源/字段封闭且不重复；每次显式指定资源并携带完整字段矩阵，全默认1明确清该资源存行，其他资源保留；缺/未知域拒绝，不以空items推定全清。
- [x] 当前角色/政策、注册域版本与目录引用纳入CAS；实际writer与公共门一致；IdentityChange/审计/回执同提交。旧入口明确adapter或受控拒绝，不留绕过版本写入口。
- [x] 三页面保护切换角色/资源的迟到响应、412草稿、权限撤销、原操作UNKNOWN、保存成功刷新失败；说明配置默认不等于多角色有效全权。
- [x] 相关PG/实际HTTP、静态注册域/原聚合兼容、实际Chrome与类型检查；单份报告/集中审查/正常PR/finish。跨仓Owner正式采用不提升。

## W29-03 与总收尾

- [x] 将相关认证/权限消费者组合验证纳入各原任务，不为纯验证新建业务分支或工作树。
- [x] roadmap/生成页/四状态、单份DELIVERY/任务和Handoff按实际更新，原失败保留；不变成功证据复用。
- [x] 逐项正常合并，远端采用核对；停止自有进程，定向处置本地/远端任务分支，finish释放固定席位；总协调文档最后闭环。

环境：四仓合计2开发+1集成；DEV-1现有产品目录，DEV-2现有Handoff目录，集成空闲。已通过status/start；零新树/代理/完整clone/依赖复制。已有NuGet/.NET输出/前端缓存复用，PG18 55435串行自有隔离根；默认不启动Actions，不安装既有环境迁移，不审批真实租户、不部署生产。完整IAM/CRM/Owner/247真实运行状态不提升。

## W29-01 交付窗口

角色安全写入及实际页面候选已就绪；61相关.NET/PG/HTTP、12Vitest、6Chrome、typecheck通过。#351/#352待正常PR集成后关闭；#353固定跨仓v1整数域缺口保持Open。单份[DELIVERY](../../evidence/WP02-03/role-administration/DELIVERY.md)记录准确范围。作者集中审查无本窗阻断，未宣称完整角色/IAM合同、跨仓Owner或真实业务完成。组合验证W29-03已部分随本窗执行。自有PG/HTTP/Vite已停止，等待manage finish释放后复用原目录，不创建新树。

## W29-02 实际开工

W29-01已通过PR354合入远端b4c60e5c，#351/#352关闭；manage finish核实本地/远端任务分支清除、固定席位释放。原DEV-1已从此基线领取W29-02。Sys_MenuAction是tenant定义，实际writer尚无共同门/目录revision；本窗按原计划补必要前向迁移及普通安装兼容，不安装到既有环境。#353仍Open。

## W29-02 交付窗口

封闭授权编辑、原操作恢复和实际页面已就绪；78不同.NET/PG/HTTP、19Vitest、4Chrome、typecheck通过。单份[DELIVERY](../../evidence/WP02-03/role-policy-administration/DELIVERY.md)记录升级保留与普通数据库身份验证。#355待正常集成关闭，#353仍Open。W29-03组合验证已随两项产品任务执行，不另造纯测试分支；后续仅正常PR、远端采用和分支/席位处置及总台账收尾。自有PG/HTTP/Vite停止，无新树/代理/Actions/既有DB迁移。

## 本轮最终交付与实际处置

| 项目 | 合入与交付 | 分支／工作区／席位结果 |
|---|---|---|
| W29-01 | [PR354](https://github.com/GTX537/CP6/pull/354)，源`a99779d0cabe5a224b0f1489bdb3a9a8abc230bc`，main`b4c60e5c54ec9db2913bdbe670bcd27485971587`；[DELIVERY](../../evidence/WP02-03/role-administration/DELIVERY.md) | #351/#352 Closed；本地/远端codex/w29-01删除；DEV-1干净detached释放后复用 |
| W29-02 | [PR356](https://github.com/GTX537/CP6/pull/356)，源`0a2bc718de2a89e36712632e691889fa32037b92`，main`c762505cc8c4047bb1faf2166364bb22a2c344b8`；[DELIVERY](../../evidence/WP02-03/role-policy-administration/DELIVERY.md) | #355 Closed；本地/远端codex/w29-02删除；DEV-1干净detached并释放 |
| W29-03 | 相关组合验证与集中审查随上述两原任务完成 | 未创建额外业务验证分支或目录 |
| W29-HANDOFF | [PR4](https://github.com/GTX537/CP6.AI-Handoff/pull/4)，源`3d97c6faa74e9957346da15638893c49c49d82ca`，默认目标合入`d3c58b92ee02356143951994222e7ccc4131d653` | codex/w29-handoff本地/远端删除；DEV-2干净detached，席位释放 |

最后协调文档W29-CLOSE已在释放后的原DEV-1从产品main经start领取；只更新本计划、台账、生成页和四状态，本提交不能预先证明自身合并。其最终PR/source/finish以统一管理登记和最终聊天交付为准。INTEGRATION-1始终空闲，零新树/代理/整仓副本，保留已有缓存与用户资料。所有自有PG55435/HTTP/Vite58427已停止，原失败证据保留。

实际有效证据：角色窗61相关.NET/PG/HTTP、12Vitest、6Chrome；授权/组合窗78不同.NET/PG/HTTP、19Vitest、4Chrome及类型检查通过。跨窗有重叠，不合计虚增总数。既有成功输入未变则直接引用；最后文档仅校验diff/链接和roadmap生成一致性。无Actions、真实租户审批、既有数据库迁移或生产部署。125目标/247真实运行验收保持原状态。

**剩余事项不是完成声明**：#353固定身份v1正整数subject与自定义int角色域差异仍Open；本轮0/int.MinValue角色创建是明确503和全事务回滚，不能称支持完整整数域。下一独立公共基础窗口需先核权威版本规则，形成不改变Owner或既有正数身份的版本化合同，再依赖顺序串行采用Main writer与CRM reader并验证边界/旧消费者拒绝；仅保留Git/Issue即可，无须占用工作树。本轮没有重新启动冻结PRIV物理删除，完整IAM/CRM/Owner及真实业务验收未完成。

原估计本代理实际3–5小时、故障余量8小时；产品和Handoff于本次记录时（06:43Z）已完成，自05:40Z起约63分钟。提前完成限定窗口不需要等满24小时；最终协调PR和finish后再报告总耗时。
