# A：BUG257仪表盘可信租户与缓存隔离

原A为唯一产品实施Owner。本窗口已登记，可从最新确认main开始；不要继续改已关闭#38的代码或工作树。

## 输入与范围

读取Issue257、DashboardController、CacheService、TenantMiddleware、ITenantContext、已合入的CurrentPermissionContext/PermissionAggregator、两个原Dashboard关系测试。原证据在私有BUG38/dev-cp6db-pg-20261017：实际新租户无订单但页面显示既有租户订单；不要向Handoff/Issue上传客户行或截图内容。

写集以dispatch.json为准：DashboardController、CacheService的dashboard键；DashboardRelationalTests、ReportParityRelationalTests仅相关构造/数据隔离适配；新CP6.Tests/Sys/DashboardTenantIsolationTests.cs；单份docs/quality/evidence/issue-257/DELIVERY.md。Program、TenantMiddleware/权限公共合同、EF模型/迁移、依赖/项目/锁、其他缓存和前端产品均只读。确需扩大时先报具体原因。

## 实施与验收

1. 根因已定向定位：所有KPI子查询、recent orders、work-order status都未绑定tenant，CacheService.DashboardKey是全局常量。先补能在当前代码失败的行为回归，覆盖有数据A、不同数据B、空C以及交替访问。
2. 从服务端可信上下文取tenant并绑定真实认证主体；不能只判CurrentTenantId非空，因为TenantContext默认值会回退默认租户。复用已合入的主体/tenant校验边界，确认JWT、持久主体和当前上下文一致；缺失/非法身份拒绝，请求参数/header不得选取其他tenant。不要自行重新定义Owner或平台绕过。
3. PostgreSQL每个统计子查询、最近订单和工单分布均参数化按tenant过滤；缓存使用带可信tenant的新键，不消费旧全局缓存。保留DTO、decimal数量、应用日期边界及当前缓存时长，不修改全系统缓存策略。历史SQL Server实现保留真实范围，不新增双库验收要求；若共享构造/查询改变，至少保持相关项目编译并说明未执行SQL Server。
4. 使用自有PG55449与两个以上真实tenant验证冷/暖缓存隔离、空租户、各聚合与最近订单；至少一条HTTP路径走真实认证/租户上下文。保留非法主体/tenant负向拒绝，不能只手工new Controller+伪造tenant冒充整条边界。相关原关系测试按影响选择，数据库成功证据明确0跳过；不跑全仓。
5. 白名单内连续实施，交固定候选+单份DELIVERY给COORD/D。D一次集中审查，有阻断由A定向修正并只复查影响。没有既有PG副本/DEV部署或数据迁移权限；仅自有实例/自有库，关闭时核对所有权，不停他人进程。

真正DEV验收和Issue关闭由COORD在代码合并后执行。本地成功不冒充实际DEV或完整业务验收。

## 执行与交付

先读根AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账roadmap.json。PostgreSQL唯一新增目标；只做受影响行为验证和一次集中SPEC/QUALITY，修正仅复查影响，不重复有效证据。资料仓GTX537/CP6.AI-Handoff、协调分支codex/dev-coordination-20261012，本轮固定消息在handoffs/CP6-DEV-20261012/waves/20261017-wave15.md。

COORD在本准入合并后从最新main创建A产品分支/工作树及A/D资料分支。执行者先核对当前main、分支与资源，再发布一次真实START到本任务messages/<UTC>-start/MESSAGE.md；安全推送、远端回读后连续推进，不等ACK。D前置由A固定DELIVERY满足后直接接续，无第二START。一个实现Owner，不另派代理重复本窗口。

只写自己产品白名单或资料任务根，单份DELIVERY/REVIEW列明固定SHA、实际验证/复用、失败和剩余范围。COORD负责共享台账/四状态、正常合并、远端核对和Issue关闭。执行者不改main、共享状态、他人候选或资源。push/PR前检查候选/main工作流事件及路径，内容未变复用分析；Handoff私有、Actions disabled且默认/自身无workflow后推送，无tag。不得运行Actions、部署/覆盖既有DEV、迁移原库、force-push或管理员绕过。
