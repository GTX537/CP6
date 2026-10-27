# W160-01 实物版模来源、当前头与独立资料入口

COORD单人，Main基线f4677c825c5b34644aa277645ebc39bd514a02f5，DEV-1/DEV-2固定目录复用。预计80–130分钟；Main官方START至本登记20.43分钟，最终耗时见finish/proof，Handoff不叠加。[原计划](../../dispatch/20261026-wave160/README.md)。原七天ACTIVE。

## 实际交付及边界

依据WP10-48、WMS-TOOL-01 R2 §§2–7、ENG-TOOL-COMPATIBILITY §10.2，增加ToolInstanceOwnerV1和/api/wms/specials/v1/tools。身份使用实际T_PlateMoldStock UUID/原PLT2编号，区别于工程版型原UUID。新cp6_tool_instance_v1保存原生内容首次真实观察及后续不可变版本、SHA256、观察时间和原native令牌；当前头/单步推进/防改删/truncate及原身份再用约束。捕获真实原值，decimal(18,2)字符串保精度；首次迁移只观察，不伪造早期历史或修改业务记录。仅在隔离PG18应用，未迁移既有环境。

独立wms-plate-mold当前query/edit，原actor/tenant和最终权限复查，默认本人范围；2/3/4通过IAM工具实例真实UUID映射，5仍需实际动作。仓库/区域范围与对象范围相交，未知仓位必须相应通配范围，历史还重新核原所在仓区，不以当前搬入范围暴露旧受限位置。成本、客户/产品与备注分别按当前字段策略遮罩；隐藏字段不允许过滤，成本/备注写入还验当前写权。新增query能力定义不授予真实角色。

有限资料编辑只允许色数、尺寸说明、制造日期、登记成本、最大次数和备注，严格未知字段/数值/日期边界，显式null与空串保区分；成本规范字符串和字段稳定排序进入版本化tool.profile.v1摘要。PUT须原If-Match与准确Owner/id/version/digest；不后补latest，原预期陈旧412。资料变化、来源头/历史和原结果/审计同Main事务；相同原键同义回原结果，异义409；两实际DbContext竞争同一前驱只一成功，最终授权失证全部回滚。恢复要求原actor及当前query/对象/字段范围，撤写后仍可查原结果；UNKNOWN可查/close，CLOSED拒晚写。

内部ToolInstanceStoreV1.HoldExactAsync要求同Main DbContext/实际事务及物理Owner查询授权，持有原表和原件表锁至调用者提交；最终检查同事务本身是否改变受保护头。真实并发连接验证旧头被保护，修改后旧expected被拒绝；同UoW改变后回滚。本轮交付内部来源参与者并测试，尚无ENG正式双Role消费或正面关联决定，不是外部GET/TTL许可。

旧PlateMoldService在PostgreSQL的search/get/warnings及全部create/update/use/maintenance/discard/delete入口先经过当前Owner后明确拒绝旧无版本协议；controller不再把Owner403/503吞成400。不会出现新资料写者和旧计数重置/软删并行绕过。旧非PG历史代码/测试保留，不增加第二数据库实现。

实际页面保留既有菜单/wms/plate-mold-stock，通过新Owner读取，并提供有当前query守卫的/wms/plate-mold直接入口。服务端50条分页与准确total超过500；显示本周期原次数、旧状态和UNKNOWN资格；成本一直是字符串。显式装载当前版本，编辑只发变化字段；准确历史不替换当前输入，412保原输入/预期版本。最小localStorage只有tenant/actor命名空间的operationId/objectId；Web Locks/当前scope/身份检查、原回执对象类型版本摘要核对、UNKNOWN查询close不自动重发，401/403/身份变化清私有数据、迟到响应丢弃。IAM页实际可选择tool-instance并查原UUID。

旧UsedShots不是LifetimeShots，实际CycleNo、站点/工程映射未证明。Source版本是观察版本，不能冒充维护轮次/使用事实或业务许可；原Status0也不构成当前工程资格。资料编辑不写Stock/StockTransaction、计数/位置/工程定义、使用/维护/报废事实。**新建实物、正式Site/legacy映射、独立维护轮次/终身计数、正式使用/维护/报废和ENG关联尚未交付；完整WP10-48未关闭。**

## 验证与审查

14项实际隔离PostgreSQL18/HTTP通过，覆盖分页503条、原精度/历史/未知资格、跨租户、准确If-Match/来源/不可变约束、规范金额/空值/禁止字段、三种部门映射与撤销、本人及当前/历史仓区、字段遮罩/写拒绝、丢ACK/撤写恢复/UNKNOWNclose、两个DbContext竞争、最终权限失败回滚、同UoW和另一连接HoldExact、全部旧写者无副作用、捕获缺失/不匹配失败关闭、实际HTTP的CSRF/If-Match/未知字段/权限。另7项原WmsPaperIndustry2 InMemory回归通过，**不称这7项为PG生命周期验收**。

62项UI/router/auth/IAM通过：10新实物页、27路由/认证、25IAM映射（含本轮工具实例实际选取）。vue-tsc/Vite通过，无跳过。失败原件保留：缺路由404、缺using编译失败、测试params目标推断失败；最终14项真实运行全过。初始typecheck发现IAM类型联合漏项，补齐并验证该映射25项；此前37项UI未变直接复用。证据D:/CP6-local-test/wave160/foundation/final-validation.json。没有浏览器E2E、真实企业验收、Actions、生产部署或既有环境迁移。

一次执行者SPEC/QUALITY集中审查完整任务diff，核对原身份/版本、最终权限和成本遮罩、事务锁与历史恢复、旧入口隔离、页面精度/私有状态及原合同边界；必要检查均通过。公共依赖/工作流未修改；push/PR前核对远端引用及既有手动触发策略。

## 台账及收尾

W159仅归档一次并关闭WP05-04/06.wave159_technical_drafts；本轮只增加WP10-48.wave160_physical_provider限定记录。375整卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态保持。D1/D4整阶段完成，2/7=28.6%仅阶段比例，不是工作量完成百分比。Continue WP10-48 actual Site/legacy binding and independent maintenance cycle/lifetime knowledge, then new registration and formal physical/technical dual-Role adoption. Professional approval/freeze, DOC, Order request application and all original D2/D3/D5/D6/D7 exits remain.

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/136，source df05def4865addfe61a70620caf23e2c0e586a4b，目标d61d506efcdbedde347da3bf6409585e007f44f2，准确分支删除和DEV-2官方finish释放。Main本报告是正常PR/远端采用/分支删除/finish前的真实快照；最终proof记录完整生命周期。固定目录、Main依赖连接和CRM缓存保留。
