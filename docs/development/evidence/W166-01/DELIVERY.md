# W166-01：实物维护与独立周期恢复

COORD 本聊天单人；[计划](../../dispatch/20261027-wave166/README.md)。Main 基线 9bf76b128734631f513b1140ee7db5b3b4094d4b，复用 DEV-1/DEV-2。预计180–300分钟，Main官方START至本登记 48.84 分钟，最终耗时以finish/proof为准；Handoff重叠不叠加，前置只读核对另计。原 CP6-7D-20261022 持续目标 ACTIVE。

## 实际交付

采用 WMS-TOOL-01 R2 §4–7及R2维护分支：原生实物使用状态0/1/2/3保持；维护开始允许0/2进入1并产生不可变开始事实，记录原Actor、真实UTC记录时间、理由、原周期及准确原生前后版本。每物最多一条OPEN，合格完成等待reset也不能另开维护绕过。维护开始、检查报告、完成事实、恢复资格和周期恢复各有自己的持久原件，不把所有操作合成一次任意状态PUT。

实际 WMS Main.Wms.ToolMaintenancePolicy.v1 按准确工具/Site发布不可变政策，原权限制定必需检查项、是否允许重开周期、是否允许不能使用后的再次维修、是否要求独立检查确认、有效区间和依据。无默认允许政策；撤销/修改是新政策原件，旧引用不自动转为最新版。发布及撤销需独立 maintenance-policy 权限。原发布人的全部来源/action权限版本、组织版本及当前工具/Site范围在消费及最终提交再次核对，不能借用当前读取人的权限。政策不可用不隐去历史事实。

Main.Wms.ToolMaintenanceReport.v1 通过 maintenance-record 当前权限产生本域原始维修报告，保存原始码点的依据、实际发生时间、检查项/实测依据、PASS/REPAIR_REQUIRED/UNSERVICEABLE以及明确政策引用（可无）。无政策可保存原报告，不因此产生完成或重置效果；它不是完整R2受限观察/认定/apply流程的替代品，也不是外部证据adapter。正式完成从实际本域报告读取，核对当前政策、检查项/结果、政策发布及适用时间、准确维护与周期，政策要求时实际报告人与确认人必须不同。浏览器不能提交已批准的任意回执。

完成PASS首先形成唯一 MaintenanceCompletionFact，状态COMPLETED_PASS_PENDING_RESET，实物仍MAINTENANCE/RESTRICTED，次数不变。REPAIR_REQUIRED和UNSERVICEABLE关闭当前维护结果但保留实物维护状态；返修必须创建明确引用前次结果的新维护。UNSERVICEABLE再次维修需要不同的新政策且明确允许，不能把原outcome改成PASS；不能对已完成维护再次complete。原始报告/实际时间与正式完成的记录时间各自保留。

G3直接前置落实为实际本地 Main.Master.ToolMaintenanceRelease.v1：独立 space-site:tool-maintenance-assess 当前权限通过专属API核对准确实物版本、Site、完成事实、政策、已知周期，作出 ELIGIBLE/RESTRICTED、有截止时刻和依据的不可变决定。这个Owner只处理维护恢复资格，不等于泛化MASTER全生命周期审批、ENG技术批准或生产用途许可。WMS恢复不能自行提交一个外部approved=true；它只能引用该实际Owner已保存的准确决定。新的RESTRICTED决定、原评估人的撤权/来源漂移、过期、实物版本变化或换政策都阻止旧决定应用。

独立 maintenance-restore 命令携带完成Ref、当前政策Ref、MASTER资格Ref、预期周期Ref和维护上下文ETag。共同最终门通过时，同一Main PostgreSQL事务追加独立ResetApplicationReceipt、旧周期结算及新cycle UUID/序号，当前UsedShots清零，LifetimeKnowledge/值原样保留，状态0，NextMaintenanceDate清空、LastUsedAt不改。这个恢复是维护周期恢复，返回NotExecutionPermit=true，生产使用资格仍UNKNOWN；技术草稿未获批准。没有已知旧cycleNo时不猜编号，不将旧500次变成已知终身500，也不伪造从0开始的终身历史。

注册原件不重写。ToolRegistration当前读取改为核对所有中间原生观察：只有有准确START/RESET原件绑定前后原生版本的生命周期变化才归属于Owner；外部原生计数/状态/时间改变后即使恢复原值仍RECONCILIATION_REQUIRED。正常资料更新不丢失已证明周期；实际维护后的注册读取显示新周期及保留的终身知识。原CycleAnchor/OriginalJson历史保持。未登记或旧漂移对象的新恢复动作保持拒绝，旧PG use/maintenance/discard/delete快捷接口继续封闭。

## 事务、公开入口与页面

实物、注册、Site、维护事实/OPEN约束、当前政策、MASTER资格、原发布人权限与Main原操作journal在相同实际事务中保护。读写同时校验当前原始会话、对象/仓库/部门/关联来源范围。其他连接的原生writer等待恢复提交；同事务后段注入原生变化也由最后重读发现，周期、native、原件、回执整体回滚。准确ETag覆盖原生来源和维护事实头；428缺版本、412漂移、409状态/原键冲突，不静默换来源。

统一命令合同 Main.Wms.ToolMaintenance.commands.v1；Main原operation journal 保存实际动作、tool/maintenance路由身份、准确输入与ETag、原Actor、原件及原生前后版本。同键同义重放原结果，同键异义（包括不同maintenance路由、Unicode码点变化）冲突；CLOSED键不能晚到应用。未知提交只查原键，不第二次增加周期。恢复查询仍按当前来源和原Actor范围，撤销写权限不改历史成功，撤销读权限不能取得历史。

入口：

- GET /api/wms/specials/v1/tools/{id}/maintenance-context（准确上下文ETag、按20条维护分页）及 /maintenance-facts/{factId}（准确不可变历史）。
- POST /{id}/maintenance-policies；/{id}/maintenance；/{id}/maintenance/{mid}/reports；/{id}/maintenance/{mid}/complete；/{id}/maintenance/{mid}/reset-applications（以上在 /api/wms/specials/v1/tools 下）。
- POST /api/master/tool-maintenance/v1/{id}/{mid}/assessments，真实MASTER专属资格动作。
- GET /api/wms/tool-maintenance/v1/operations/{operationId} 与POST其 /close，使用原结果恢复协议。

实际宿主认证、CSRF、闭合DTO未知字段拒绝与no-store均沿用。四项新增动作仅定义能力，不自动给角色授权；旧maintenance不兼有政策发布、报告登记、MASTER评估或恢复权限。MASTER动作按实际space-site菜单键登记，未硬编码测试菜单号。

迁移20261028020000_ToolMaintenanceV1仅在隔离PostgreSQL18执行。cp6_tool_maintenance_v1.facts/open_maintenance 有tenant复合键、真实注册/原生版本关联、正文SHA256与冗余列一致性、不可变UPDATE/DELETE/TRUNCATE保护、单OPEN与单START/COMPLETE/RESET约束、pending reset准入保护、准确当前来源头及reset周期递增/次数归零/终身不变/真实完成与资格引用校验；PUBLIC权限撤销。迁移前向，既有环境未执行。

实际工具页加入维护面板，展示当前周期、精确Int64字符串终身次数、政策依据/适用期间、维修报告、三种完成结果、MASTER依据和独立恢复。按各自权限显示动作；无政策可记录报告、完成/恢复另列。版本冲突保留输入和准确原引用，身份/权限/工具切换清除私密内容，迟到响应不覆盖新对象。原键记录只存operationId/toolId/动作kind/maintenanceId，不缓存维修依据；跨页锁和当前身份核对后才提交。界面不把维护恢复称为生产放行。

## 验证与集中审查

82项相关本地检查全部通过，其中75项真实隔离PostgreSQL18/HTTP（含独立新租户种子），7项既有非PG服务回归；无跳过。新维护验证覆盖完整真实HTTP政策→开始→报告→完成→MASTER资格→独立reset→原件读取、CSRF/未知字段、准确原键/Unicode/路由冲突、未知提交与CLOSED、无政策报告、独立人员确认、原发布人撤权与权限ABA、政策及发生时间、Site变更、原生漂移/回滚、非合格后继、新政策修复和旧未知终身。真实第二连接锁等待、同事务后段变更回滚、提交响应丢失后原结果与当前知识分离均通过。

受影响的实物provider/注册/可信链接/工程关联及既有WmsPaperIndustry2服务回归采用实际原方法；不是全仓回归。扩大运行在48项终态通过后长时间无新结果，保留run-aborted记录、宿主及隔离数据库诊断，经精确身份核对停止该次宿主和其PG实例。48项只按各自真实成功结果复用，不把中止运行记作整体通过；原集合的全部剩余方法改用新隔离库、逐项输出与90秒停滞诊断完成。剩余集合中的旧全租户种子检查在新库通过，避免已累积大量租户时反复播种。发现清单与两个运行实际结果逐项对账，新增能力种子另跑，源码/测试输入未变。新增能力定义在独立新数据库核对四个动作且不自动授予角色。16项Vue组件、vue-tsc及Vite通过，覆盖准确Ref/大整数显示、权限、无政策报告、412保留、未知恢复/错误Owner回执拒绝、撤权和迟到响应。

证据 D:/CP6-local-test/wave166/foundation/final-validation.json 绑定最终源码、测试与具体运行。maintenance-red的真实404、maintenance-first的Json名称编译冲突及maintenance-compile两项未触发故障hook失败均保留；hook原仅覆盖ReaderExecuting，已改在真实reset写入后读取事实的阶段触发，并验证peer确实等待锁。集中SPEC/QUALITY补强最终原生重读、政策发生时刻和数据库来源/周期约束。成功输入最终一致后不因提交/合并SHA变更重复测试。没有浏览器E2E、真实企业验收、Actions、既有环境迁移或部署。

## 剩余出口及收尾快照

本轮是实际维护正常链和直接MASTER前置交付，不关闭完整WMS-TOOL R2或WP10-48。正常使用的稳定Occurrence、原始凭据适配器、受限观察/认定/apply、历史未知cycle/Lifetime补证、ENG准确用途/技术批准资格、报废OA-APP批准和各Owner义务关闭仍需落实。不能将本域报告端点当成完整通用受限观察流程，不能将MASTER维护决定复用为ENG许可。已知非零终身计数的使用累计出口尚未采用，本轮验证真实新登记0与旧500/终身未知，未虚构使用历史来关闭正常使用验收。普通Stock/StockTransaction、财务处置不改写。

W165归档CLOSED一次，仅WP10-48.wave165_instance_association生命周期关闭；W166限定记录wave166_maintenance。375正式卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247真实运行NOT_RUN、41历史静态维持。D1/D4完成，D2/D3/D5/D6/D7仍部分采用；2/7=28.6%只是阶段数，不是实际工作量完成百分比。WP05-05订单CHANGE/CANCEL仍REQUESTED、WP04-32正式Sales及原七天全部出口保持。

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/142，source b08cc445ea38244bd0f8fcefc286bf5b37888478，目标 322d397fea52557714854a265244797fd1588102；准确本轮分支已删除，DEV-2已finish释放。Main本报告时待正常PR合并、远端采用、准确分支清理和官方finish；最终proof记录实际结果。固定目录、依赖缓存及用户未跟踪文件保留。
