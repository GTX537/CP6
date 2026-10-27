# W167-01：真实使用发生、独立认定与一次性计数应用

COORD 本聊天单人，持续目标 CP6-7D-20261022 保持 ACTIVE。[计划](../../dispatch/20261027-wave167/README.md)。Main 基线 c2a0d151b4d58df21dc2a0aa62e64e1b14efe303；DEV-1/DEV-2 固定目录复用。预计90–150分钟，Main官方START至本登记 44.61 分钟；最终实际时间以finish/proof为准，Handoff重叠不叠加。

## 实际交付

采用 WMS-TOOL-01 R2 的实际使用分支。Main.Wms.ManualToolUseEvidence.v1 保存本域有权人员记录的原单据、行、发生序号、实际次数、发生UTC时间和原文依据，服务器生成原件ID、版本和正文摘要。来源明确为 LOCAL_RECORDED_ATTESTATION，不能冒充工单Owner或外部已签凭据。补充原文须准确PreviousRef；同一原事件改变工具、次数或发生时间冲突，原件不能覆盖。原始Unicode码点保留，非法代理序列在JSON冻结前拒绝。

宿主必须明确配置 Wms:ToolActual:EnvironmentId 为有效UUID、Wms:ToolActual:Mode 为 PRODUCTION 或 SIMULATION；无默认值。第一次有权实际写入将该原生数据库不可变地绑定到一个环境/模式；不同宿主身份不能混写。HTTP不接收租户、Actor、环境或模式作为可信字段。此次只在隔离本地PG18使用明确SIMULATION配置，既有环境未配置、迁移或部署。

稳定发生按环境、租户、Site、模式、原件Owner、原单据UUID、行和发生序号登记，生成 Main.Wms.ToolUseOccurrence.v1 原件及稳定leaf UUID。HTTP operationId、凭据修订和维护周期不进入发生身份；重复登记返回同一发生，不能因换键或维修复位再次累计。凭据登记与发生登记不增加次数。

显式观察产生 Main.Wms.ToolActualObservation.v1，记录原发生Ref、实际次数/时刻、现场说明及独立记录时间。没有认定政策也能记录事实，当前工具呈 ACTUAL_RECONCILIATION_REQUIRED/RESTRICTED，计数不动。观察原件和应用状态分开；后续认定不能重写原观察。

Main.Wms.ToolActualRecognitionPolicy.v1 由独立 actual-policy 能力发布，绑定准确工具/Site、有效期间、是否允许本域现场原件及政策依据；撤销/修改产生新原件，无默认放行。消费时及最终提交重读原发布人的来源/action权限版本、组织版本、对象与Site范围。换政策、撤权或Site改变使旧认定不可应用，历史记录仍保留。

actual-recognize 产生真实 Main.Wms.ToolActualRecognition.v1。ACCEPT_FACT 必须由不同于观察人、原始凭据登记人及所采用补充凭据登记人的有权人员作出，携带准确原观察摘要、原件Refs、周期Ref和依据。核对实际发生时刻属于原周期，缺政策、周期或证据保存为 NEEDS_EVIDENCE；不同发生的凭据拒绝。REJECT_ASSERTION/NEEDS_EVIDENCE 是独立历史结论；较新的未接受结论使旧ACCEPT不可应用。发生时资格只按已有证据保存 UNPROVEN/VIOLATION，不伪造正常生产许可或ENG批准。

actual-apply 携带准确ETag、观察应用版本、应用上下文摘要和原认定Ref，当前政策、认定原Actor权限、最新决定与周期归属必须持续有效。同一Main PostgreSQL事务产生 Main.Wms.ToolUse.v1、周期/终身增量及 Main.Wms.ToolObservationApplication.v1 回执。当前周期增加原生UsedShots和精确投影，LastUsedAt取实际时刻最大值；超维护阈值转为受限状态，不截断真实次数。迟到旧周期使用仅增加那个历史周期和已知终身次数，当前原生周期/UsedShots/版本保持不变。Int32计数或Int64终身溢出整体拒绝，原未知终身仍UNKNOWN/null。

已应用发生无论新HTTP键、补充原件版本或维修复位都只返回原应用，不创建第二Use。注册原件、旧维修/复位原件和历史摘要保留；当前投影结合准确USE原件审查全部原生历史。外部原生漂移或恢复原值仍需核对，不能借计数重建猜历史。维护周期引用新增可选投影版本，未发生使用时省略该字段，兼容既有W166原件序列化和摘要。实际计数入账后当前资格仍受限，IsUsePermit=false。

## 事务、入口与页面

实际工具/注册、原生版本、Site、原件、当前政策/认定人权限和原操作journal由同一真实事务保护；最终重读原生和事实头。真实第二连接writer等待提交；同事务后段原生变化导致使用、计数、应用和journal整体回滚。原操作合同包含tool、action、观察路由、准确输入及ETag；同键异义冲突，CLOSED阻止迟到提交。丢失提交ACK后查询原结果；撤销写权限不改变原成功，当前读权限/Actor/对象范围仍限制披露。

实际认证、CSRF、未知字段拒绝和no-store入口：

- GET /api/wms/specials/v1/tools/{id}/actual-usage-context、actual-facts/{factId}、actual-evidence-options?kind=USE、use-occurrence-options。
- GET /api/wms/specials/v1/tools/use-occurrences/{id}?recordVersion=、observations/{id}、recognition-decisions/{id}。
- POST /api/wms/specials/v1/tools/{id}/actual-evidence、use-occurrences/register、observations、actual-recognition-policies。
- GET /api/wms/specials/v1/tools/{id}/observations/{obs}/application-context；POST同观察的recognition-decisions及apply。
- GET /api/wms/tool-actual-usage/v1/operations/{id}；POST其close。指定外部workRef的候选保持WORK_TOOL_USE_OWNER_UNAVAILABLE，未伪造工作Owner。

四项actual-*权限只定义能力，不自动授权角色。实际ToolInstancesView加入独立面板：原件/修订、发生登记、显式观察、政策、独立认定及准确原决定应用；显示历史/当前周期和大整数字符串。观察应用上下文提供该发生的全部相关凭据，分页不丢失旧观察的原来源。412保留输入/原引用并锁定原操作恢复；身份、对象、权限变化清除私密内容，迟到响应不能覆盖新对象。恢复缓存只含operationId/objectId/kind，浏览器锁和当前身份核对；不缓存现场依据。

迁移20261028030000_ToolActualUsageV1仅隔离PG18执行：环境绑定、原单据注册、不可变事实，tenant复合键/工具注册FK、正文摘要与冗余列、原生准确前后版本、证据修订、单发生/观察/Use/应用约束，七项启用的保护触发器，PUBLIC权限撤销，前向迁移。所有普通Stock/StockTransaction和财务处置未写入。

## 验证和集中审查

95项相关后端检查通过（88项隔离PostgreSQL18/HTTP，7项既有服务回归），22项组件测试、vue-tsc及Vite通过，无跳过。实际双账号登录后的HTTP链覆盖原件→发生→观察→政策→独立认定→应用→原件读取；拒绝无CSRF与approved注入。相关PG覆盖缺政策、准确周期/旧周期补报、换键和修订去重、独立人员、原发布人撤权、政策变化、更新的补证决定、Site停用、运行环境绑定、未知旧周期/终身、Unicode、同事务回滚、真实peer锁等待、提交ACK丢失及读写权限分离。跨周期累计与溢出回滚保留维修原件；新租户独立运行核对定义能力不自动授予。注册/关联/实物/维护及既有非PG服务回归依据实际影响选择，非全仓回归。

证据 D:/CP6-local-test/wave167/foundation/final-validation.json 绑定最终源码/测试与每项实际运行来源。保留HTTP404失败基线、测试编译错误、历史计数查询字段歧义和测试登录器/预期状态错误记录。查询已明确绑定外层原生版本，避免actual事实revision遮蔽导致错误标记原生漂移。双账号测试改为真实第二用户登录，不放松独立认定要求；原发布人权限版本变化为412。actual-final运行14项中13项通过、一项因数组/JsonElement记录按对象相等比较失败；该运行不记作整体通过。只将失败断言改为比较完整序列化原件，跨周期/溢出方法随相关回归完整重验；其余13项按原真实成功来源复用，核对测试旧blob证明只有这一行改变，运行时代码未变。一次集中SPEC/QUALITY覆盖完整diff、实际来源与权限、稳定身份/周期、原操作恢复、UI、迁移及台账范围；成功未变输入按原来源复用，不因commit/merge重新运行。没有浏览器E2E、企业真实验收、Actions、既有环境迁移或部署。

## 剩余与收尾快照

本轮交付实际计数应用闭环，仍未关闭完整WMS-TOOL R2/WP10-48/WP05-06。正常使用ENG用途/技术批准资格、外部工单Owner凭据适配、维护受限观察分支、正式纠正链、未知历史周期/终身补证、报废OA批准与各Owner义务关闭仍需推进。手工原凭据不会自动证明外部业务真实性，实际生产验收另行完成。

W166仅归档一次，只关闭WP10-48.wave166_maintenance生命周期；本轮限定记录wave167_actual_usage。375正式卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247真实运行NOT_RUN、41历史静态不变。D1/D4完成，D2/D3/D5/D6/D7仍部分采用；2/7=28.6%仅阶段数量，不能冒充实际工作量比例。订单CHANGE/CANCEL仍REQUESTED，正式Sales和原七天全部出口保留。

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/143，source 874c1770a95c9913306015f9378d3263cf8a6581，目标 d3fd75592ffa2afbdd0cafec090896c0d79b1fa9；准确任务分支已删除、DEV-2已finish释放。Main本报告时待正常PR合并、远端采用、任务分支清理和finish，最终proof记录实际完成。固定目录、依赖缓存和用户未跟踪文件保留。
