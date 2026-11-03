# W296-01：原APP提交卡与24h实际执行收口

W296核销原WP12-06可信审批绑定、冻结提交TxS/TxE；原合同合法Key修复已采用，三层去重/alias、旧回调隔离及无实例当前业务读面证据完整。

原375为30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53剩余。本24h周期新增整卡闭环WP12-01、WP12-02、WP12-06，起点为27DONE及56剩余。125完整目标0关闭、247正式验收NOT_RUN不变。

## 时间口径与实测

用户确认的是3个工作日、约24小时Codex有效实际执行。沿原锚点2026-11-03T03:44:56Z累计45141秒，之后只累计连续实际工作；已排除7558秒调度空档。阅读、编码、必要验证、故障分析、审查及正常交付计入；没有人为延时、空等或重复测试凑时。

最终提交前截至2026-11-03T15:06:36Z，累计有效执行86041秒（23小时54分1秒），进入用户所定约24小时的预算末尾；已排除2小时5分58秒调度空档。随后当前任务必要的正常合并、清理和席位释放继续按实计入最终证明，不再开下一开发窗口，也不为整数24小时空等或重复验证；最终实际时长如实报告。

W268–W295共28个串行窗口已完成，另有9个独立BUG闭环；W296是当前最后核销/盘点窗口。各窗口耗时不再加上Handoff时间，嵌在窗口中的BUG也不再重复加总；总有效时间以锚点账为准。前12个原计划窗口实测41089秒（11.41小时），原初估15.75–29.75小时、中心22.75小时，仍明显高估。随后连续扩展到W295并修复实际问题，未把前12个完成当作三天结束。28窗中位耗时39.9分钟，范围6.3–119.7分钟；范围差异很大，不能用卡的S/M/L或人工小时直接换算Codex时间。

后续估时采用可验证调用链和已有代码复用程度：纯核销/单一边界修复通常约5–20分钟；现有Owner内功能常见约30–75分钟；跨运行/数据库/真实页面链约1–2小时以上。它们是本轮样本观察，非新承诺；新隐私Owner、跨仓协议或未知数据迁移必须先勘察，不能套用这些短任务。每2小时滚动校准继续适用，不预先保证若干卡恰好占满三天。

## 本轮产品结果

| 范围 | 实际结果与来源 |
|---|---|
| 定义与设计器 | W268–273：完整schema与精确数字保留、FORM Owner、准确发布/独立启停、固定版本克隆、FORM/子FLOW准确依赖、唯一启用绑定。W279/280/284/286：只读解释预演、稳定身份版本比较、结构化诊断、真实页面与旧写入口收敛。 |
| 表单与运行 | W274–276：实际根/子运行pin、个人草稿/显式重基/提交、当前授权信箱与真实影响统计；W281/285/287：目录/历史及当前下载、实际文件Owner、共享字典来源。W293/294：重基路径/必填/行诊断、FLOW发布引用影响。 |
| 办理和恢复 | W277/278：当前代理身份、迭代撤回与BLOCKED显式新授权、真实Chrome到Main/PG链。W282/283：退回重送/转交加签/无人恢复、定时政策/worker。W289/290：SFS字段补丁三版本和统一SFS/APP动作上下文/原合同适配。W291提供当前抄送只读披露，不授办理权；W292保旧历史、明确身份缺证，无迁移伪造。 |
| 整卡与缺陷 | W288/W295/W296分别核销原WP12-01/02/06。9个GitHub BUG均核对Closed，实际修复/回归与远端采用按各报告；本轮测试按受影响行为执行，没有把重跑次数相加当产出。 |

上述各窗口报告在[24h计划](../../planning/20261102-codex-24h-oa.md)及总台账关联。业务/IAM/外部输入允许模拟，内部Owner、PG18、认证HTTP和标明的Chrome实际执行；组件API模拟单独标记。没有把本地验证当成生产部署、正式候选或247条正式验收通过。

## WP12-06原卡逐项核销

| 原实施范围及正反验收 | 当前源码和实际证据 |
|---|---|
| 可信Owner/subjectKind/服务路由；binding/policy不可变版及时间线 | [W175](../W175-01/DELIVERY.md)真实当前授权绑定Owner/不可变修订/时间线，Owner由登记派生且封闭DTO拒绝callback/Tenant替换；[W176](../W176-01/DELIVERY.md)真实WMS政策/冻结事实；[W177](../W177-01/DELIVERY.md)当前C02服务Scope/原令牌与登记；[W178](../W178-01/DELIVERY.md)固定真实投递路线和服务身份。配置与实际采用区分。 |
| Subject/Snapshot/FrozenInput、三层哈希/alias、原AppCommand唯一 | W176原Owner重复HTTP键仍一意图/一Outbox且不能改发起人/快照/政策；W177按FullRequestHash、SubmissionCore、IntentCore逐层比较，完整授权事实参与，直指canonical、不链式alias。BUG740回归实际三层alias成功，异Scope/伪冻结输入拒绝。 |
| TxS与TxE原子、engineStartId=SubmissionId、原键恢复 | W177命令/Submission/StartOutbox/审计同事务；[W179](../W179-01/DELIVERY.md)原生实例/任务/token/history/start完成同事务，两连接/未知提交只一实例。真正InstanceId按Scope/Owner/Submission独立命名，durable engineStartId仍原SubmissionId，不能把两个ID混为一谈。 |
| 无实例业务详情按当前业务读权；页面不造回执 | W176真实Main登录/approval-context与冻结201，实例表为空且读面仍可读业务；当前权限入口与撤权测试按原Owner。W178 ToolApprovalPanel实际组件显示“受理、尚待启动”与“尚未批准或报废”，未知/错对象回执不伪装成功；不是从信箱有实例详情推断无实例路径。 |
| 旧同步callback不能旁路新路由 | W179真实PG用legacy engine Act/AddSign/Withdraw/ApprovalDispatcher尝试新APP实例，全部APP_DECISION_OWNER_REQUIRED；原生v2不塞入legacy BizType/BizId，保完整Subject在不可变Submission。真实旧实例ID/业务键冲突仍隔离。W274扩展准确根/子pin，并保同门。 |
| 原身份与Key合法域 | 原OA-APP R2 §4明确Guid小写RFC4122，不能把“UUID身份”自身当作缺口；SubjectId/subjectKind/bizType是Key并保持字符串。实际51–100字符缺陷已由[BUG740](../BUG-740/DELIVERY.md)恢复且101拒绝。Environment来自可信部署映射，正文不能自定其他环境。其他Owner专有身份别名合同各在其卡核对。 |
| Owner alias处理边界 | W177/179正例返回原canonical/同实例；W178对未知本地canonical明确CONFLICT，绝不凭远端alias制造新的Owner事实。W176本Owner在冻结入口已合并同业务意图，保持一个Outbox；不能为制造第二意图的测试绕过其不可变约束。各新Owner的本地合并采用仍归其消费卡。 |

当前ApprovalIntake/Start/BusinessEvidence/FlowEngine及Tool投递/UI已逐入口核对。[W282](../W282-01/DELIVERY.md)后续启动时无人审批的准确槽位同事务写入，已有真实APP暂停→固定政策恢复→生成任务→人工决定终态验证；该后续修改采用W282实际通过来源，不拿W179旧结果冒称新路径通过。W191/DOC、W196/ReqSet、W199/ITER也有实际注册和采用，来源远端祖先及原PASS证据已检查；不是只看技术接口存在。S01-02/S01-07/S02-00/WP12-02均DONE；WP02-10/13按原限定TASK_DELIVERY采用实际WF_AUTH和W176–179同事务原件/审计，不关闭全局租户布局或审计保留卡。

本卡三项提供者范围无剩余。全业务Owner的应用/撤回、整体OA-APP和原正式验收未因此完成。W250/W276“native UUID profile待核”等旧描述已按原合同和后续真实交付纠正，不重做七天/W249已有能力。本窗不运行产品测试，复用来源及具体用例索引见`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave296-reconciliation.json`，原失败原样保存。

## 原59卡剩余53张

以下从原W250的59个固定ID逐项核对当前权威台账而来；6张DONE为WP09-11、WP12-07、WP03-01、WP12-01、WP12-02、WP12-06。表中未在本周期改动的领域引用其最后实际审计，不冒称重新运行业务。WP12-03/04/08/10等本周期新增局部采用不在原59清单内，不能混入该分母。

| 原卡 | 真实剩余或尚待核准 | 最新依据 |
|---|---|---|
| WP01-01 替代验收采纳后的WP01差异与清单收口 | 逐125目标采用判断仍需对照旧缺件覆盖/不能覆盖；不可恢复的历史原件保持缺失。模拟业务输入不伪造历史原件或签名；不重做已接受替代方案。 | [W250-01](../W250-01/DELIVERY.md#wp01-01) |
| WP02-02 Main PostgreSQL租户存储布局与隔离采用 | 连接组件报告明确未全宿主接线：落实当前Main运行模型缓存、可信映射/池上下文及逐租户初始化版本和普通写入权限；历史数据切换另立任务，不全库自动转schema。 | [W250-01](../W250-01/DELIVERY.md#wp02-02) |
| WP02-03 Main当前授权与撤权最终门 | 按全字段注册表与现存写者清单补query/write/export/download未纳管入口；逐入口复用当前权限，不重新实现公共fence。 | [W250-01](../W250-01/DELIVERY.md#wp02-03) |
| WP02-04 租户创建、暂停恢复与管理审计 | W264配置/启停与W265创建V1已交付。WP02-04仍有其他配置Owner、旧兼容入口全面版本/原命令采用、全部业务writer共同fence、跨系统映射/传播、worker恢复及整卡验收；命令历史保留/擦除归相应治理任务。旧CreateAsync不具备原命令恢复，未知不得自动重放；现有默认模板Owner管理不在本期新增审批流程。 | [W265-01](../W265-01/DELIVERY.md) |
| WP02-05 Main身份权威快照及完整目录发布 | 以当前全部权威写者核完tenant/org/user/permission/issuer向量及原字节；旧维护写者与issuer变更是否全部纳管尚需逐入口映射，不能从旧报告推断CRM未实现。 | [W250-01](../W250-01/DELIVERY.md#wp02-05) |
| WP02-06 CRM身份投影、撤权fence与会话恢复 | W253已执行真实双组织切换、旧会话失效和隔离场景并远端采用，此路径不再待执行。继续对照原卡其余正反验收与全部身份写者适用范围；只补没有有效证据的真实缺项，不重复整条身份链。 | [W264-01](../W264-01/DELIVERY.md) |
| WP02-09 CRM首个Owner真实共同事务与授权门 | Inquiry模型明确Ignore平台Inbox；身份入站的Inbox不能证明业务入站Inbox。确认原卡适用的真实入站Owner并接完整收件/业务/原结果同事务，不能为公开HTTP凭空造消息或声称所有CRM已具备。 | [W250-01](../W250-01/DELIVERY.md#wp02-09) |
| WP02-10 Main Owner共同事务、原结果与授权参与 | 实际Program对CP6Context使用宿主统一databaseConnection；TenantMiddleware和EF过滤/盖章传递TenantId，OperationsOwner持同Context/事务。WP02-02 TenantConnectionManager及ERP/Identity profile未覆盖Stock/主业务表，无Main业务EF适配。已从待核关系明确为未采用的新连接profile范围，完整卡保留；不判为已证泄露，不要求全库schema切换。 | [原核对](../W255-01/DELIVERY.md#wp02-10) |
| WP02-13 审计写入、授权查询和保留清理 | 全域actor/effectiveActor/UTC与脱敏、总数/明细/导出同授权及policy/hold/unknown清理收据仍需落到各权威出口；不能把Machine字段历史视为整个AUD。 | [W250-01](../W250-01/DELIVERY.md#wp02-13) |
| WP02-14 文件不可变版本、扫描和Owner授权绑定 | 补全Owner引用图与清理决定、通用草稿转正式原操作及ACT-FILE LOCAL_AUTHORITATIVE_GUARD实际适配；不要重复DOC绑定或以未做生产部署否定本地实际扫描。 | [W250-01](../W250-01/DELIVERY.md#wp02-14) |
| WP02-15 隐私subject目录、导出与擦除编排 | 逐Owner导出清单及遗漏/不可用、擦除预览/批准/hold、UNKNOWN责任与墓碑恢复/下载编排尚未完整实现；真实各域适配仍各自接入。 | [W250-01](../W250-01/DELIVERY.md#wp02-15) |
| WP02-16 编号原命令分配和生成器安全预览 | 新编号提供者/规则CAS/管理HTTP及CodeGen固定模板/差异预览/PG版本元数据和旧原件显式接入已实现。存量逐对象采用尚非全量迁移；实际文件应用/路径和并发保护、编号实际业务消费者/旧域切换及完整规则生命周期、整卡联合验收仍需核销。 | [W263-01](../W263-01/DELIVERY.md) |
| WP02-18 工作台菜单CAS与授权KPI查询 | W266排序层级和W267名称/图标/Enable展示配置已限定交付。WP02-18仍余RoutePath/MenuKey及路由/权限命名空间治理、个性配置、KPI Owner与scoped授权缓存、全部用户导航传播、旧接口/旧节点编辑全面CAS与原命令采用及原20AC整体；隐藏菜单不是动作撤权。命令历史保留/擦除归治理任务，原WP02-03整卡前置地位不变。 | [W267-01](../W267-01/DELIVERY.md) |
| WP02-28 Main宿主实际加载身份与兼容回退验证 | 尚无两份兼容实际制品的正向更新/回退采用证明；需隔离PG schema/驱动兼容验证及准确采用清单。正式R2/GHCR候选和推广另行办理，不能拿本地文件替代。 | [W250-01](../W250-01/DELIVERY.md#wp02-28) |
| WP02-29 CRM宿主平台合同与观测包采用 | 对原卡“实际加载版本/配置及Owner边界”逐项核销剩余身份观察项，区分已证实托管payload与未证实进程/native身份；正式发布与环境推广不是此卡额外开工门。 | [W250-01](../W250-01/DELIVERY.md#wp02-29) |
| WP03-02 浏览器绑定公开提交与H01自动受理 | 冻结公开/H01路径已按W135/247准确适用，LeadNo可未分配且没有附件输入；完整卡共同NUM/FILE未来实际消费前置未核销，不能借W254未接线组件关闭。公共服务消费须另有正式版本入口和准确原键恢复，不伪造调用。 | [原核对](../W255-01/DELIVERY.md#wp03-02) |
| WP03-03 询盘审核队列、放行拒绝与到期 | W256持久Expired与W259真实60分钟匿名化后Expired已验收；本链路不再列为未执行。新过期事件消费者/投递及原卡整体验收尚未核销；部署/PrimaryWriter切换属WP14独立范围，不阻塞本局部验收。 | [W259-01](../W259-01/DELIVERY.md) |
| WP03-04 询盘同意、保留匿名化与隐私导出 | 原生导出制品20/30/90/99、单次下载及FILE/PRIV/NUM适用编排未完成；Lead月度保留真实边界仅有投影证据的部分需按合同执行，不偷改期限换通过。 | [W250-01](../W250-01/DELIVERY.md#wp03-04) |
| WP04-33 ERP经营产品物料身份与工程映射读取提供者 | 当前映射明确IDENTITY_SITE_ONLY；法人/制造等完整scope版本适用及所有legacy资格变更frontier继续补，币种/有效状态按原主账读取，禁止另建ERP表。 | [W250-01](../W250-01/DELIVERY.md#wp04-33) |
| WP04-34 原发运商业可退资格与独立退货来源授权 | 核完整旧发运/已退份额和撤销/current来源覆盖，落实原商业Owner允许退货依据的准确适配；模拟外部服务可验收边界，不能被说成内部ERP所有来源均已实现。 | [W250-01](../W250-01/DELIVERY.md#wp04-34) |
| WP05-02 工程产品身份与经营料号双Owner映射 | 当前只证IDENTITY_SITE_ONLY；类别/来源、法人适用及技术修订/配置范围要按原scope补，停用改型保原身份与历史，不重建产品号。 | [W250-01](../W250-01/DELIVERY.md#wp05-02) |
| WP05-03 工程修订、工作快照与组成候选 | 完整三方rebase逐项KEEP/ACCEPT/MANUAL与旧CheckRun失效、父基线/变更和legacy入口覆盖仍需实施/验收，不能把已交付成员类型继续列空缺。 | [W250-01](../W250-01/DELIVERY.md#wp05-03) |
| WP05-04 旧版模写入口的版本与原事务兼容 | 按全部旧写者核expectedNativeRowVersion+expectedTechnicalControl、有效区间/删除/导入与Unknown恢复覆盖；技术规格当前DRAFT不等正式控制批准，但不得将后续制造许可扩大成本卡。 | [W250-01](../W250-01/DELIVERY.md#wp05-04) |
| WP05-05 版模商业联动改为原订单Owner意图 | CHANGE_REQUEST/CANCEL_REQUEST仍仅REQUESTED；接真实ERP/PE决定/应用/回执和恢复，替换旧直接联动/重建关联路径，不能以请求或NoOp算OWNER_APPLIED。 | [W250-01](../W250-01/DELIVERY.md#wp05-05) |
| WP05-06 工具规格与实例双Role当前关联 | 逐原协议核不可变证据Inbox、迟到/冲突/observationGeneration及各Owner准确控制适用；当前技术DRAFT路径不自动获得批准资格。需要外部Owner原件的路径按真实调用补，不重做双Role关联。 | [W250-01](../W250-01/DELIVERY.md#wp05-06) |
| WP05-08 版本化工艺操作与工具规格引用 | 正式资源/工具规格与替代工艺的适用及参数来源继续补；当前DRAFT_ONLY和声明能力不能声称完整受控路线资格。生产Claim/Start属资源/执行卡，勿扩大本卡。 | [W250-01](../W250-01/DELIVERY.md#wp05-08) |
| WP05-09 版本化BOM、材料替代与有效性 | 补批准单位转换、替代/配置条件与完整适用域；不得以隐含倍率1或缩小合法精度代替，旧行历史继续保持。 | [W250-01](../W250-01/DELIVERY.md#wp05-09) |
| WP05-10 配置与逐输出输入工序关系 | 核外部Item/Spec准确来源适用、完整合法配置/关系输入及大合法包验收证据；缺的是原卡完整覆盖映射，不能把REL/WO全部实现列为此卡前置。 | [W250-01](../W250-01/DELIVERY.md#wp05-10) |
| WP05-11 工程源固定证明与完整性检查 | 资源/工具等尚未具备资格的来源参与者必须保持UNKNOWN/BLOCK；补其实际原生资格/current参加和完整内部制造正例，签名或GET不替代共同最终检查。 | [W250-01](../W250-01/DELIVERY.md#wp05-11) |
| WP05-12 工程基线冻结、后继与准确发布委托 | INTERNAL技术冻结仍受实际资源/工具资格来源缺项限制；补全所需参与者与合法大包/后继覆盖，REL只保真实Owner关联，不以委托字符串授制造许可。 | [W250-01](../W250-01/DELIVERY.md#wp05-12) |
| WP05-13 工程需求来源、分解与可评价判据 | 接CRM/销售实际原件读取和DOC/FILE澄清来源，补准确方法需求/必要性与可见域覆盖；不预造ITER/VER正式ID，已做UOM判据不重复。 | [W250-01](../W250-01/DELIVERY.md#wp05-13) |
| WP05-14 需求集批准、冲突裁决与后继影响 | 真实适用交集冲突裁决、澄清/范围分离/ECM/DEV去向以及后继差分/已采用消费者影响意图仍需完整闭合，不把普通批准当冲突裁决。 | [W250-01](../W250-01/DELIVERY.md#wp05-14) |
| WP05-16 受控技术文档版本与原件派生 | 独立旧版hold/撤回/可用性与失权旧URL拒绝逐原验收核销；不能由FILE hold推定DOC独立撤回已有。源中未定位完整DOC撤回动作，需确认职责后只补真实缺项。 | [W250-01](../W250-01/DELIVERY.md#wp05-16) |
| WP05-18 工程项目、迭代输入与任务确认 | 按原卡核暂停恢复对未决外部请求和输入变化的当前重核、来源/变更/父基线适用及0分母；CHANGE/LEGACY路径待确认，不能用后续OA未实现的旧说法阻塞。 | [W250-01](../W250-01/DELIVERY.md#wp05-18) |
| WP05-19 迭代方案全集、评审包与OA持久提交 | 核ITER-OA-APPLICATION/1原身份与UUID别名完整合法域，当前native UUID正例不能代替全部外部身份映射；只补真实未覆盖路径，不另造第二种权威摘要。 | [W250-01](../W250-01/DELIVERY.md#wp05-19) |
| WP05-20 迭代决定应用、问题关闭与撤回竞争 | 核完整外部身份/alias及GAP/迟到/退回拒绝映射的原正反验收范围；模拟业务角色可执行，不能因没有真实企业数据扣留整卡。 | [W250-01](../W250-01/DELIVERY.md#wp05-20) |
| WP05-39 配置替代规则、方向性与有界解析 | 当前ENGINEERING_OPTIONS_ONLY；完整Required/Provided occurrence组合、方向/比例/工程依据、客户Site活动窗口/取整损耗、MATCH等解析与显式备选选择仍需实现。 | [W250-01](../W250-01/DELIVERY.md#wp05-39) |
| WP08-01 设备工作中心版本、归属与当前资源门 | 路线适用性、完整效力段、RES-WRITE-1 token最终写门、未来切段失效及SCHED/EXEC真实Claim准入仍需实现；身份读取不等资格。 | [W250-01](../W250-01/DELIVERY.md#wp08-01) |
| WP08-10 排程候选、完整Root组与资源队列 | 按完整RootWO/快照/operation源核原卡支持域和冲突/冻结计划的未覆盖情况；部分源由SIM profile登记，需区分允许的模拟外部输入与尚未接入的产品内部来源，不能因模拟本身判失败。 | [W250-01](../W250-01/DELIVERY.md#wp08-10) |
| WP08-11 排程应用、换机六阶段与恢复 | 核原协议全部WO/RES/EXEC当前参与者及已开始竞争在当前SIM profile以外的适用边界；仅原卡未覆盖路径追加验证，模拟输入本身不是缺口。 | [W250-01](../W250-01/DELIVERY.md#wp08-11) |
| WP08-16 RES资源身份与独立能力目录提供者 | 稳定Assignment原件、准确受控能力/批准来源与当前版本尚需补全；DECLARED_NOT_QUALIFIED不视为专业资格。路线相关RES token及执行许可交WP08-01，不扩大此提供者。 | [W250-01](../W250-01/DELIVERY.md#wp08-16) |
| WP10-01 仓库库位身份、typed capacity与初始化隔离 | SPACE CREATE与手工共享初始化器、REUSE_EXISTING保旧量/控制和所有旧写者初始化Fence尚需完整接入；不能因新位置审批完成声称SPACE路径完成。 | [W250-01](../W250-01/DELIVERY.md#wp10-01) |
| WP10-03 库存唯一数量过账、移动与原结果恢复 | 完整WriteKind合法来源、RSV预算/容量/质量/用途同QuantityUoW及manual/import/job/legacy所有写者继续补；不重跑已稳定Stock恢复链或缩小数量合法域。 | [W250-01](../W250-01/DELIVERY.md#wp10-03) |
| WP10-10 调拨Request/Stage、来源预算与本地移动Owner | 在途TRANSIT坐标、完整Selection/全部RSV Use与StockPrepareCall、现场指令/实际事实及所有来源类型继续接入；现MOVE正例不等全Transfer。 | [W250-01](../W250-01/DELIVERY.md#wp10-10) |
| WP10-19 出库授权、拣配暂存包装与真实发运Owner | 完整RSV Allocation→Pick→Stage→Package→Shipment、scan/封箱/称重与Stock同最终门，以及采购退运Part具名来源仍需实施。 | [W250-01](../W250-01/DELIVERY.md#wp10-19) |
| WP10-22 客退授权、原发运行预算与分批收货 | 非空例外批准路径、完整H30原source/claim共同资源计划及NCR/FININV实际消费者尚需闭合；财务全部关闭归WP10-23，不能以观察POSTED冒充内部Finance已实现。 | [W250-01](../W250-01/DELIVERY.md#wp10-22) |
| WP10-31 补货调拨执行、取消和完整Stage恢复 | 多阶段/在途、部分covered/remaining与已发生后的取消、旧任务族显式迁移及source-task一致性仍需完善；不等待真实补货数据。 | [W250-01](../W250-01/DELIVERY.md#wp10-31) |
| WP10-36 版模实物使用、维护周期与退役 | 真实搬位/报废仍需Transfer/Stock/Finance原Owner应用和迟到恢复，准确技术当前用途与外部使用原件继续补；不得把APPLIED工作流回执当库存/财务业务效果。 | [W250-01](../W250-01/DELIVERY.md#wp10-36) |
| WP10-47 既有物料、UOM与容量维度原Owner目录适配 | 原Owner批准的完整单位转换/量纲/容量metric及当前控制尚需提供；准确物料/Site/位置身份按原主账读取，歧义UNKNOWN，不建第二套可写WMS字典。 | [W250-01](../W250-01/DELIVERY.md#wp10-47) |
| WP10-48 WMS版模实例身份、当前头与HoldExact提供者 | 核完整旧ENG技术号候选读取与provenance适用域、外部来源原件及current版本覆盖；未知历史不能假造。正式关联/生产使用/退役归WP05-06/WP10-36，不能据此扣留本提供者。 | [W250-01](../W250-01/DELIVERY.md#wp10-48) |
| WP11-32 应收退货贷项与库存价值独立协调 | 内部AR CreditFact/余额/预算与source/frontier共同最终事务、原票逐行精确额度及INV唯一COGS/Inventory来源仍未由观察桥替代；partial claims与撤销全链继续实现。 | [W250-01](../W250-01/DELIVERY.md#wp11-32) |
| WP11-33 应收退款授权、资金事实与撤销恢复 | 真实Funds退款执行记录/会计/AR分轴、执行key完整别名预算/原claim与撤销和未知恢复仍需实现；外部模拟结果不是本产品资金账。 | [W250-01](../W250-01/DELIVERY.md#wp11-33) |
| WP12-19 可信业务撤回与阻塞回执恢复 | 迭代Owner可信委派撤回、BLOCKED显式新actor/新grant再授权及连续原事实已有W277/278实际证据；余其他实际业务Owner的当前授权/同control CAS/fence采用与对应正反例。通用运输不能替Owner赋权，worker不能自选人员；不重做已证迭代或非委派竞争。 | [W279-01](../W279-01/DELIVERY.md) |

## 下一开发顺序与明确未完

1. WP12-19：先核DOC/ReqSet等已经真实采用审批的Owner哪一个需要可信撤回；在该Owner控制头上实现当前业务授权/委派、冻结撤回事实、同CAS与应用竞争及BLOCKED显式再授权。迭代已有W277/278，不重做；通用worker不能选替身。
2. WP12-03：隐藏字段损失仍需真实受控处置Ref及其权威Owner流程；现有403不能伪称Ref已交付，不能通过临时授予读明文或构造URI补齐。先沿WP02-15明确当前处置边界，再采用。
3. WP12-04/10/08：分别核完整提交/历史身份映射卡，满足原整卡前置后再关闭已具备产品行为的WP12-08。历史映射无准确证据继续LEGACY_ACTOR_INCOMPLETE；未经授权不执行既有数据迁移。
4. 跨域较大余项按上表，优先可信租户连接profile/当前写者与实际Owner采用；不把所有消费者未完当公共提供者都没实现，不以模拟数据为阻塞理由。

当前源码进一步核实：`ApprovalBusinessEvidenceV2.Withdrawal`仅分派ITER；DOC、ReqSet、ToolDisposition、LocationRule均未接该撤回入口。DOC/ReqSet现有`ApplyAsync`按当前应用ETag和回执运行，不能直接复制ITER的业务阶段或把OA传输CAS当共同业务control。后续先做DOC完整纵向切片，再按差异采用ReqSet：本地Owner定义合法撤回责任/当前委派、同一control同时约束apply和withdraw、冻结原事实/Outbox、当前权限下原结果与BLOCKED显式新命令、真实页面恢复。必须覆盖两连接apply/withdraw只一方生效、迟到批准不生效、撤权不改旧终态、丢响应原键恢复；新前向迁移只在隔离PG验收。ToolDisposition和LocationRule按各自已冻结业务状态另核，不默认新增所有未来Owner。

DOC首个纵向切片的当前估计P50约2小时、P80约3.5小时，ReqSet在首个实现后的差异采用P50约1小时、P80约2小时；仅是当前调用链勘察后的低置信度预算，未开始实现，首个Owner完成即校准，不机械相加成三天。上述Owner权限/控制头/既有提交兼容是主要不确定性。

这些是后续顺序，不创建新的自动24h目标或预先声称任务已开工。下一项先用约15–30分钟核调用链，给出P50/P80范围，再按每2h实际吞吐调整；不再将一张抽象L卡固定估成一天。

## 交付边界

一次集中审查当前文档/台账完整diff，检查375固定定义、原59成员/数量、全部链接、来源主线祖先及生成页一致性；125/247原清单无改动。无Actions触发或取消、工作流变更、生产部署、已有环境迁移、额外worktree或子代理。普通相关验证仅本地执行，失败记录与有效旧证据均保留。

本报告是最终合并前快照。当前W296 Main/Handoff的远端准确采用、分支条件删除、固定工作区干净、官方finish和2个开发席位释放由`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave296-final-verification.json`最终确认；该证明PASS后才能宣布整个24h执行目标完成。根用户原始归档和有用缓存保留，集成席位未占用。
