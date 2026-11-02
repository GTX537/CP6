# CP6 第八期三天：共享菜单树版本化排序与层级

本对话单人使用writing-plans/executing-plans执行W266-01，一个任务分支、一次集中审查。目标是菜单拖拽有明确的全局影响、并发冲突和原操作恢复；不会在另一管理员或原生写者修改后静默覆盖。

基线Main `2acdf8199928d31f462f6e32f824dd6c0c28e493`，固定DEV-1/Main和DEV-2/Handoff已官方start。每阶段预计6小时核心+2小时缓冲，按真实验收收尾，不按日历等待。W265最终PASS只归档；七天/W249及前七期不重复。原375项27DONE/2IN_PROGRESS/346PLANNED；原59局部关闭3、剩56。

架构：共享Sys_Menus仍为唯一树定义，单调树版本由PG原生写触发器推进；IAM完成门与所有原角色/菜单写者串行，当前平台管理员加menu:edit动作权限守住全局修改。独立Owner事务保存准确positions、原结果及强审计。Tree不修改MenuId/MenuKey/RoutePath/Enable等其他字段；有效导航仍由现有当前身份/权限读取。技术.NET8/EF Core/PG18/Vue3。

## D1：树版本、影响与原命令

- [ ] 新建`CP6.Core/Services/Sys/MenuTreeAdministrationV1.cs`及DTO，Read/Preview/Execute/ReadOriginal，命令OperationRef/ExpectedRevision/Positions/Reason。首次新命令核对当前全树，正ID、非负顺序、无重复、父存在、不自引用/无环；partial positions只改提交节点，未提交节点保持。原件恢复优先于当前CAS；异义409、他人403、冲突412，未知提交503。
- [ ] `CP6.Persistence.PostgreSql/Migrations/Core/20261102120000_MenuTreeAdministrationV1.cs`：singleton revision及commands，前向安装原生Sys_Menus变化触发器，INSERT/UPDATE/DELETE/TRUNCATE和ABA可见。迁移锁保证安装与原写一致。commands原键/请求/结果字节不可变，不造第二份树。
- [ ] Preview在同一门内计算位置真正变化的节点、全部租户的RoleMenu与RoleAction引用及受影响角色/租户数，返回准确已读版本；预览不当授权票据，提交重新核对。审计与原定义/原件同事务，不删除映射或授权，不修改原权限含义。
- [ ] `CP6.Tests/MenuTreeAdministrationV1PostgreSqlTests.cs`用现有PG夹具覆盖合法移动/顺序/partial保留、结构拒绝、全tenant影响、并发CAS、原生ABA/旧写、审计失败/未知恢复、当前平台与动作撤权、迁移保留原记录。

## D2：实际会话HTTP

- [ ] 新建`CP6.WebApi/Controllers/Sys/MenuTreeAdministrationControllerV1.cs`，`api/menu/tree-v1`的GET当前、POST preview/commands/commands/read。独立scope和当前完整会话，复用TenantAdministrationHttpV1会话完成适配与原IAM权限读取；拒绝模拟、普通tenant菜单管理员及撤权旧会话。NoStore/CSRF/严格JSON，不接收自报actor/tenant。
- [ ] 实际Main Program和登录cookie验证多租户全局影响、准确命令/结果、无CSRF/伪造字段、锁等待中权限变化、原结果读取当前授权。旧PUT tree保持原合同且原生写推进V1版本；本期不声称全部旧API强制CAS。

## D3：拖拽页面、验证与交付

- [ ] `cp6.web/src/api/sys/menu.ts`增加V1 API/DTO；`cp6.web/src/views/pms/MenuView.vue`排序/层级保存采用已读版本，显示全局影响后确认；读取失败不拿空树覆盖。412保留草稿并提示重读差异，避免盲目覆盖。
- [ ] 发送前以当前身份隔离的sessionStorage保留准确命令；刷新、超时后按原件查询/原样重试，404或后续权限拒绝不能当未提交；身份切换忽略晚到响应。确认后刷新当前权威树和导航，刷新失败明确部分完成，不能把历史回执当当前树。
- [ ] 相关PG/实际HTTP测试、Vue类型检查和实际浏览器拖拽/保存夹具；完整diff一次集中SPEC/QUALITY审查，台账/生成页/四状态同步、一份DELIVERY。正常Main/Handoff PR采用，核对远端完整树，准确任务分支条件删除、官方finish和席位释放。

范围：WP02-18整卡仍有菜单其他字段/路由注册/个性配置及Owner KPI、授权缓存/导航完整采用等余项。此次只闭合共享树排序和层级实际用户流程，不改变菜单命名空间或权限定义，不新增生产授权。所有验收可用模拟业务数据，明确真实PG/Main与页面夹具边界；不运行Actions、不部署或迁移既有环境。
