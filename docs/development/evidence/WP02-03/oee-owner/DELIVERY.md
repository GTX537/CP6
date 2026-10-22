# W48-01：OEE独立Owner、完整来源与可信后台

COORD本聊天单人；Main基线`e5702415665a4beaa41623d4a0bbd8ce41ca42a5`，DEV-1 `codex/w48-01`、DEV-2 `codex/w48-handoff`；2026-10-22 09:15:11Z START，预计实际90–150分钟，实际按本任务finish记录。W37–W47已实际收尾；七天目标ACTIVE。

## 入口、来源与计算性质

[Issue410](https://github.com/GTX537/CP6/issues/410)记录原PG服务缺Owner仍能查询/计算/重算、实际已登录但无query的HTTP返回200。OeeService四个人类公开入口现要求同Context OperationsOwnerV1、原会话及当前Actor、独立query/recalculate。Controller三个GET补query与no-store，Owner错误保留401/403/503。新增query仅目录，不自动授予角色；PG迁移只向前，不回填旧日汇总，不执行既有环境迁移。

OEE对象范围复用W47的实际machine UUID和当前可信部门映射，分别交叉设备、报工、工单独立query和完整范围。先检验整机/整日全部非删除来源是否可见，再计算：缺任一来源时列表排除整台设备，指定设备或重算拒绝，不能先筛掉隐蔽来源再披露部分合计。工单/订单父图当前有效性独立验证，跨租户只靠原Context实际租户过滤与原生证据约束；设备Owner不替代OEE Owner。W47设备列表TodayOee继续不披露，未在本窗暗中扩大附加值授权。

旧公式明确标记`LEGACY_CALC_V1`并在页面解释为参考OEE/估算稼动：保留480分钟默认计划、停止记录重叠累计并封顶、缺产能时100%回退、百分比四位与数量八位小数。它不构成`OWNER_EFFECTIVE_V1`、MES-PERF-01 source cut、实际RunSlot或正式发布。现有合法输入/数量精度不因数据库选择缩小；旧非PG路径保留历史，不新增SQLServer实现。

## 同事务证据与后台

新`cp6_oee_legacy_v1.calculations`只追加记录实际machine/daily UUID、租户、输入/结果字节及SHA-256、daily RowVersion、观察时刻、实际人类Actor或固定后台来源。版本`Main.Oee.InputEvidence.v1`；十进制按InvariantCulture G29无损最简表示，消除PG numeric固定scale填零造成的同值证据差异，未改变API或存储精度。原生触发器拒绝跨租户/错对象/错daily版本，禁止UPDATE/DELETE/TRUNCATE；证据与日汇总及最终现权都在同一PG事务。当前完整来源字节/版本、结果字节/版本与记录同时匹配才披露历史/趋势；旧无证据、源已改变（包括改回原值的ABA）或结果被改的行暂不返回，重新计算才形成新证据。实时计算不保存，SourceEvidenceVersion只标明本次输入版本。

人类入口忽略调用方userName作为权限/实际Actor的可能性。后台使用仅程序集内部IOeeWorkerV1固定入口，经宿主TenantScopeRunner新scope先绑定租户再解析Context；同Context、非空租户、无环境/嵌套事务或预跟踪业务效果，实际Sys_Tenants启用/期限与政策版本开始及提交前均核对。内部入口不以无HttpContext或字符串OeeWorker放行，不借用人类权限。共同原生权限根/业务图锁覆盖输入与OeeDaily；最终来源再核对。取消/最终失败回滚日汇总和证据，提交回执丢失明确UNKNOWN。

后台每轮仍是现有参考值重算；本窗没有持久原操作键、终态查询恢复或正式发布幂等协议。前端冻结并防重、身份切换/卸载和旧响应隔离，401/403清保护值并停轮询，UNKNOWN不自动重发；已知重算成功后的读取失败不改称提交未知。重开页/重新登录不能凭UI状态宣称确认原结果。

## 本地验证与集中审查

31项相关.NET用例通过（按TRX testId保留最新定向结果，含PG/真实HTTP/后台事务/种子/权限），6项Vitest与完整vue-tsc -b通过。来源`D:/CP6-local-test/wave48/foundation/final-validation.json`。真实Program HTTP验证无query拒绝、no-store、当前独立来源撤权和真实Actor；PG验证完整来源、部门范围、证据回读/精度/ABA/不可变/跨租户、多人类结果最终失败回滚、人工和后台UNKNOWN、后台最终到期和取消整体回滚；真实TenantEnumerator/TenantScopeRunner/DI/两个新Context按租户隔离，后台不借浏览器Actor。旧后台循环的取消/继续行为复用本次相关测试，未运行部署后的后台或浏览器E2E。

4项真实服务/HTTP红测、6项旧组件行为红测保留。开发中时间参数类型、全局唯一工单夹具冲突、十进制scale证据失败以及旧权限读取断言失败均保留真实记录；最后仅复查对应影响。权限断言更新为已采用Machine/OEE必须独立query，未降低权限。一次作者完整SPEC/QUALITY覆盖入口、范围/来源、迁移/证据、精度、后台可信边界、事务/UNKNOWN、HTTP/UI和完整diff；无剩余合并阻断，无代理/全仓检查。

8处自有PG/API已停止，经ownership、绝对路径、进程和重解析点检查后回收生成data 916928279字节，日志/TRX/runtime/ownership和缓存保留。未触发Actions、未改变触发、未取消其他运行；未跑全仓、正式RES/PERF验收、真实环境迁移或生产门禁。相同产品输入复用成功证据，提交/合并变更SHA本身不重跑。

## 剩余范围与收尾

下一PlanningBoard/PlanAchievement及其实际引用Owner；正式RES/PERF/SCHED、原操作恢复和部署仍各有前置。全任务24/375、125目标0交付、247运行NOT_RUN不因部分入口提升。

正常产品PR/Handoff采用后，原任务最后状态PR、远端blob核对、准确分支定向删除和manage finish；Issue410在生命周期完成后关闭。DEV-1/2当前仅本窗占用，集成席空闲；根用户未跟踪资料保留。七天目标保持ACTIVE。

## 远端采用与原任务收尾

产品[PR](https://github.com/GTX537/CP6/pull/411)和[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/24)已采用，source`f4958debb891cbed296ed81df9afa3bf010c8edc`、远端main`bee338101c5bb99161c91fba8fb55f069a2b0435`，29任务文件blob一致；Handoff source`01959b4c349698ddd609e81c72dc2740a5673de8`、目标`cb2769fb5f816b7c1e37d0e760e1028aa67b0aa9`，DEV-2准确分支删除/席位释放。

31项相关.NET用例通过（按TRX testId保留最新定向结果，含PG/真实HTTP/后台事务/种子/权限），6项Vitest与完整vue-tsc -b通过；最后仅文档diff/链接/台账检查，原DEV-1最后文档PR后finish并关闭Issue410。分支/工作区/席位及实际耗时以最终PR、实时登记与`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave48-final-verification.json`为准，不提前宣称释放。七天目标ACTIVE，下一计划Owner；正式范围与总计数不提升。
