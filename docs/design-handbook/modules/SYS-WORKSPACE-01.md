# SYS-WORKSPACE-01 · 菜单与业务工作台

整理状态：`core_semantics_consolidated`。本册为已接受静态设计的开发展开，候选接口/字段明确标识；不等于现有代码已采用。

## 1. 业务目的、操作者与边界

共享菜单定义管理员维护路由与树结构，tenant角色管理员分配本租户菜单/动作，普通用户只看当前范围内摘要与待办，业务指标Owner签署指标口径。菜单/工作台继续属于Main，不迁到Platform；纯导航不会赋予后端动作、行、字段或业务最终执行权。角色1、看见菜单或页面存在，都不能证明新业务主链完成。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:5) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:11) [原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:29)

## 2. 选定组合与接受范围

当前正文v0.1，SHA256 `fc9da230a15d2a778d410d750c8aa63a1aa056ef8451c07d35a9802cb9c165a9`，85行；纳入S5 v0.2混合十稿，当前仅IAM/CLIENT为v0.2，另外八册原字节继承。WORKSPACE为5原SPEC、20AC、12开发任务。正文STOPPED保留作者历史状态，后续独审+delegate接受建立当前静态地位；不改称已实现。[原文A:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/69/69f54296df1b30a8__UA-20261008-S5-SYS-WORKSPACE-01-STATIC-MD02.json:175) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文R:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:94) [原文B:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:85)

接受时点02:18:29Z依恢复指令保留，04:09:04Z有精确恢复确认；原首次逐字接受和messageId未重建，也不是用户本人亲签。全部AC NOT_RUN、运行/Owner采用UNPROVEN、实施false。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/69/69f54296df1b30a8__UA-20261008-S5-SYS-WORKSPACE-01-STATIC-MD02.json:8) [原文A:328](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/69/69f54296df1b30a8__UA-20261008-S5-SYS-WORKSPACE-01-STATIC-MD02.json:328)

## 3. 对象、字段与数据口径

|对象/DTO|字段及身份|约束|
|---|---|---|
|Menu|MenuId不可变，MenuName/RoutePath/MenuKey/Icon/ParentId/OrderNo/Enable可编辑，CreateDate不可改|共享全局定义；RoutePath限注册内部页或批准外链，MenuKey是权限命名空间，不能当纯标签改名。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:15)|
|MenuChangeCommand（候选）|白名单原字段、expectedVersion、operationId、reason；featureRef、routeRegistrationVersion、expectedMenuVersion为补充概念|这些新增字段尚非已证实体列；自然结构和版本都在服务端核。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:15) [原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:19)|
|MenuImpactPreview（候选）|affectedTenantsCount/rolesCount/routes、policyImpacts、previewVersion|必须含全部相关tenant；预览不是后续授权票据。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:19)|
|角色关联|roleRef=(tenant,RoleId)，menuIds，actions[{MenuId,ActionCode}]，sourcePolicyVersion/effectiveMenuVersion|action只能属于已授menu；提交完整受影响集合，但未提交资源不清空。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:31)|
|CardResult（候选）|value/availability/asOfUtc/sourceOwner/scopeDescription/metricDefinitionVersion/queryRef/staleAfter|无权HIDDEN、故障UNAVAILABLE；0仅成功查询得0，未知不伪零。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:47)|
|MetricDefinitionView|metricKey/version、Owner、sourceRef、纳入排除状态、计数单位、dedupeKey、时间字段/时区、零值条件、scope/field、freshness、drilldown及兼容口径|新口径新版本，不悄改旧报表。[原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:61)|
|NavigationTargetView（候选）|labelKey/routeKey/featureRef/safeQueryRef/availability/requiredContext/ownerState|targetBusinessRef保原Owner/id/version；URL只导航，不能替代业务引用或访问授权。[原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:73) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:77)|

原Dashboard保留Summary、RecentOrders、WorkOrderStatus；RecentOrder为WebOrderNo/CustomerCd/Quantity/OrderDate/ShipStatus。八个既有指标口径如下；状态码必须由其原Owner确认，不能重新解释为新流程状态。[原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:45) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:59) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:61)

|原指标|固定代码来源/口径|
|---|---|
|TodayOrders / MonthOrders|T_Order.CreateDate，应用壁钟DateTime.Today/月初。|
|ActiveWorkOrders|T_WorkOrder Status1–5。|
|MonthCompleted|Status4/6且ActualEndDate本月。|
|PendingOutbound|T_OutboundOrder Status1–3。|
|StockWarnings|T_Stock AvailableQty≤0。|
|PendingApprovals|产品Status0 + 盘点Status3；不是OA人工待办或PLM审签数。|
|TotalProducts|产品总数。|

本表全部依据[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:59)，不能声称已有tenant时区/财务期间采纳。

## 4. 动作、状态与写集

菜单变更：读完整树/版本→编辑或准备停用→服务端验父存在、正ID、非负OrderNo、不重复、不自引用/无环、已注册route、menu/key唯一规则→计算全部tenant角色影响→权限语义变化由IAM同writer/fence处理→同事务保存定义、相关映射及审计→失效所有相关菜单/profile缓存。删除先展示子项/角色引用，优先停用并保历史；引用未解决不静默级联清全租户关系。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:9) [原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:17)

角色关联：核本tenant有效role、已登记menu、合法action及完整影响集→按IAM原行语义保存或撤权→统一版本/失效。未提交资源不当空集合清除；旧 `/api/Role/{roleId}/menus` 入口也须纳入共同门。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:31) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:33)

工作台为只读聚合：当前scope→并行调用已登记Owner的只读聚合→逐卡附口径/时点/范围→字段和PII过滤→响应。单卡失败不fallback跨tenant或全局数；聚合许可不足整卡不出数，不能只隐藏明细留下敏感总量。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:47) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:49)

## 5. API与Owner依赖

现有 `/api/Menu` GET/POST/PUT/DELETE 与 PUT `/api/Menu/tree`，tree DTO为MenuTreePosition(MenuId,ParentId,OrderNo)。候选版本命令/影响预览加CAS和原operation查询；400结构错误、404不存在/不可见、412版本冲突、409引用未解、503授权协调不可用。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:19)

角色接口复用 `/api/pub/role-perm/{roleId}`、`/my-actions`、`/api/Role/{roleId}/menus`；EffectiveNavigationView按当前身份返回可见tree、action摘要、policyVersion/incompleteReason，浏览器不得回传该对象作为授权凭证。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:33)

GET `/api/Dashboard` 维持legacy全局合同。新ScopedDashboardView及明确版本路由由API Owner选择，当前没有已选定的新URL，不能在本册臆造；通用route resolver也只是CANDIDATE。各业务Owner须提供metric字典/只读范围、drilldown和精确feature→route→Ref注册。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:49) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:55) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:61) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:77) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:81)

## 6. 幂等、并发与恢复

配置写expectedVersion CAS；预览后提交重判真实版本/权限，保存未知查原operation，不重复排序造新意图。已提交而缓存传播失败应显示部分传播未证；恢复从权威树重新分发，不重写原权限审计。关联未知同样查原operation/当前版本；无当前read权不返敏感历史。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:19) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:21) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:37)

数据缓存键至少含tenant、subject/授权指纹、时间范围、metric版本及timezone；cache命中仍过当前权限/字段门。指标与drilldown同时间/范围/版本；分页变化可标asOf差异，未实现一致快照就不承诺。历史口径无法复算标NOT_REPRODUCIBLE，不补造旧值；工作台不补业务记录使数字对齐。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:47) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:63) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:65)

## 7. 权限、租户与敏感数据

共享Menu不是tenant局部修改；当前menu:add/edit/delete只是已有动作码，不能直接证明全局管理政策完备。新语义通过IAM执行全局影响和共同fence；菜单可见与后端action/行/字段独立。角色复合键含tenant，同号RoleId不跨租户关联。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:9) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:11) [原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:29) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:31)

直接URL和目标API重新认证tenant/动作/行/字段；切组织后不能沿旧target续写。跨系统走原SSO/session，不把token/secret放URL。缺route注册显示未接入，无对象权不暴露存在性；日志和安全queryRef不能成为数据访问票据。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:51) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:75)

## 8. 页面与服务端校验

菜单拖拽仅形成待保存草稿，显示全局影响；停用/删除分开。412展示差异保草稿，unknown先查原操作。权限变化刷新导航并清已失去可见的数据，不自动提交在编辑内容；403权限更新与503依赖未就绪区分，后者不是空菜单，也不引导重复赋权。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:21) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:35)

卡片显时间窗/asOf/scope、逐卡失败重试；全空不能显示经营正常。刷新取消旧请求，按请求tenant/版本丢弃晚响应；切组织清缓存。tooltip展示定义/过滤条件；跳转失败保安全trace和筛选，后退不保过期权限。[原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:51) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:63) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:77)

## 9. 开发顺序及代码差距

固定Main `90c871fe571fd6b390f53e8678376d7ce60bcb60` 的MenuController已有tree结构验证，删除显式清所有tenant RoleMenu；Dashboard的Dapper聚合、1分钟DashboardKey、global scope/app壁钟/int DTO仍是原事实，EF过滤不能证明Dapper隔离。这是静态差距，不是本次动态漏洞判定。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:5) [原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:9)

顺序：①Menu白名单/CAS/引用预览；②IAM覆盖全局menu writer与两条旧role入口；③业务Owner冻结八原指标及新增定义/授权范围；④Scoped聚合与隔离缓存、失效恢复；⑤页面差异/逐卡状态；⑥精确路由注册与跨系统导航。12任务均PLANNED。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:25) [原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:41) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:55) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:69) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:81)

## 10. 验收场景与状态

20AC全部NOT_RUN：WS01自环/缺父/重复树整件拒、全tenant影响、CAS/unknown保草稿、停用留历史；WS02菜单不能绕行字段动作门、旧入口统一失效、未知依赖不全权、撤权旧页/API重判；WS03隔离缓存、0/HIDDEN/UNAVAILABLE/过期区分、晚响应丢弃、legacy与新scope分开；WS04八指标原口径、版本可解释、drilldown一致、总量不反泄；WS05原Ref不丢、目标页授权、注册外链不传secret、审批/应用从原receipt取状态。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:23) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:39) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:53) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:67) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:79)

## 11. 待确认与材料边界

需要真实metricOwner确认状态码/范围、共享全局menu管理政策和全部授权writer；API Owner尚未选择ScopedDashboard新路由，通用resolver未有源码证据。原global数据源不能直接承诺tenant隔离；失效传播、运行采用和业务验收仍UNPROVEN。[原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:11) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:49) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:61) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:77) [原文B:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:85)

## 12. 原件与阅读覆盖

本次正文全文1–85，逐条读五SPEC/所有20AC及开发任务；S5最终复核全文1–128，组合索引按全部10模块语义字段/版本/hash/原SPEC与根治理字段结构化读取，接受件按决定/正文/组合/ReviewQualifications/OwnerAndExecutionBoundary结构读取。未把源码相对路径索引算本次源码全文阅读。详见本组reading JSON；未运行代码/SQL/测试/CI。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fc/fc9da230a15d2a77__S5_SYS-WORKSPACE-01_完整设计候选_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/69/69f54296df1b30a8__UA-20261008-S5-SYS-WORKSPACE-01-STATIC-MD02.json:8)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，精确阅读记录见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
