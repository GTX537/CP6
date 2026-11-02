# CP6 三天执行计划：提供者核销、CRM验收补项与原命令编号

> 状态：用户已授权执行三天目标，D1/D2已完成，D3限定实现和验证完成，等待本轮最终采用/释放核销。COORD由本对话单人执行，不启用并行代理。D1–D3是开始执行后的三个工作日，不是后台定时任务。

**目标：** 在原375细项内，先核销已有交付的完整提供者范围，再补CRM两条明确验收缺口，最后交付编号按原命令分配与恢复的限定实现。

**实施结构：** 复用现有Owner、PostgreSQL事务、当前授权与原结果协议；每个实现窗口采用开工时确认的最新主线。输入在权威边界校验，内部直接信任既定合同；未知提交查询原效果，不用默认值或静默重试掩盖失败。

**技术基线：** Main/.NET、CRM/.NET、PostgreSQL18本地隔离环境，现有Python台账工具与相关页面测试。业务资料和外部服务可明确模拟，实际产品行为必须执行；生产部署另立授权与门禁。

## 范围、容量与预期

依据[W250逐卡核对](../evidence/W250-01/DELIVERY.md)，本计划选择59张局部卡中的六张原卡为主线：WP02-10、WP09-11、WP12-07、WP02-06、WP03-03、WP02-16。窗口编号只用于执行组织，不增加第376张原细项。

三天按每天约6小时主任务、最多2小时机动安排，总容量约18小时主任务加6小时故障/收尾缓冲。它是排程假设，不是以历史轮次数或登记时长外推的工期承诺。每个窗口内已经包含相关验证、一次集中审查、正常合并及席位释放；不把这些工作留到三天后。

|日程|执行窗口及原卡|主任务|当日可审查产出|容量|
|---|---|---|---|---|
|D1|W252，WP02-10 / WP09-11 / WP12-07|对齐准确交付依赖规则；逐卡核销共同事务、独立质量规格、OA决定/回执底座|台账工具定向修正与回归；三张卡各自“完整通过或具体缺项”结论，满足原范围才记DONE|约6小时，另留2小时|
|D2|W253，WP02-06 / WP03-03|核并补切组织旧会话失效、审核到期实际Owner/HTTP行为；只修实际失败|实际PG/HTTP验收结果、必要修复、原卡更新；复用身份同步、放行/拒绝等已有成功证据|约6小时，另留2小时|
|D3|W254，WP02-16限定分配范围|可信编号scope/规则版本、原operation分配回执、并发唯一和UNKNOWN恢复|可供真实Owner调用的编号分配/原结果查询与PG验证；保留规则管理/CodeGen及其余消费者范围|约6小时，另留2小时|

目标是争取核销D1三张完整提供者卡，并取得D2两条运行验收和D3一个实际实现交付；不是承诺三天关闭六张整卡。WP02-16还含规则治理、全部调用者切换和CodeGen等余项，本期限定分配实现不能将其整卡关闭。125完整目标、247正式验收只在自身完整条件满足时改变；不因本计划自动提升。

七天一期及W249已完成，本期不重复。全域隐私导出、Transfer TRANSIT/RSV、内部AR/Funds账、全库schema切换、正式制品和环境推广不装入三天必做范围。

## D1 / W252：先核销已有完整提供者

**直接前置与依据：** [WP02](../task-plans/WP02.md)、[WP09](../task-plans/WP09.md)、[WP12](../task-plans/WP12.md)原卡，以及各卡已有准确远端采用。采用相关交付子范围，不能用同一前置卡其他功能尚未完成拒绝本卡；也不能把任意一份局部报告当作所有依赖满足。

**文件职责与修改范围：**

- `docs/development/tools/manage_roadmap.py`：依赖满足判断；保留DONE必须有实际验证与远端采用证据的检查。
- `docs/development/tools/test_manage_roadmap.py`：真实/无关交付引用、缺失前置、虚报DONE等行为回归。
- `docs/development/roadmap.json`：三张卡的原验收与采用登记；复用现有交付记录，不另建资格数据库或回执层。
- Main源码只用于准确核对：`CP6.Core/Services/Integration/MainOwnerOperationJournalV1.cs`、`CP6.Core/Services/Mes/QualitySpecificationAuthorityService.cs`及`.Sources.cs`、`CP6.Core/Services/Oa/ApprovalApplicationReceiptV2.cs`。
- 本窗口单份交付报告与四份项目状态；生成页由台账工具产生，不能直接手改状态。

**执行顺序：**

- [x] 用当前源码和原报告核对三张卡的scope/正反验收，标出已运行、需复用与确有变化影响的部分。复用来源：WP02-10的共同事务/Stock原操作与[W249](../evidence/W249-01/DELIVERY.md)；质量规格[W192](../evidence/W192-01/DELIVERY.md)/[W193](../evidence/W193-01/DELIVERY.md)；OA [W181](../evidence/W181-01/DELIVERY.md)、[W183](../evidence/W183-01/DELIVERY.md)–[W185](../evidence/W185-01/DELIVERY.md)及[W191](../evidence/W191-01/DELIVERY.md)/[W200](../evidence/W200-01/DELIVERY.md)真实消费者。
- [x] 修正旧“全部dependencies整卡DONE”硬判断：完整前置DONE仍直接接受；本卡按既有`prerequisite_bindings`消费的准确已交付合同可独立满足。引用只能指向唯一台账内现有采用记录，并在本卡交付报告说明被消费范围；不接受无关交付、未采用候选或缺失引用。
- [x] 增加针对该决策的回归：准确子范围可采用；无关报告、缺少实际交付与伪造DONE拒绝；不能由提供者局部采用自动关闭消费者、125目标或247验收。依赖存在性、环路和真实未满足条件继续检查。
- [x] 按输入是否改变选择已有行为证据。只有共享代码、依赖或环境变化确实影响原结论时，运行对应代表Owner/质量规格/OA回执测试；不因SHA改变重跑全仓。
- [x] 三张卡逐一判断整卡出口，填写原scope覆盖、正反运行证据、当前采用与剩余。不完整者保留原状态并准确写缺项，不删除scope换完成数。
- [x] 一次完整diff审查，正常PR合并、远端核对、准确清理及finish。

**本窗口不能额外要求：** WP02-10不等待全部消费者迁入；WP09-11不等待生产source解析/任务族/最终Use；WP12-07不等待版模实际搬位/报废等所有域效果。上述范围由原卡明确交后续消费者。

**相关命令（从任务仓库运行，按实际影响选用）：**

```powershell
python -B -m unittest discover -s docs/development/tools -p test_manage_roadmap.py
python -B docs/development/tools/manage_roadmap.py --write
python -B docs/development/tools/manage_roadmap.py
```

预期：针对依赖/虚报完成的回归通过，台账结构与生成页一致；这些结果只证明台账工具。若现有来源校验出现失败，先辨别本次变化与既有配置，不关闭门禁或将结果写PASS。

如产品输入确有影响，相关测试入口为`CP6.Tests/Erp/StockOriginalOperationPostgreSqlTests.cs`、`QualitySpecificationPostgreSqlTests.cs`、`QualitySpecificationBoundariesPostgreSqlTests.cs`、`ApprovalDecisionPostgreSqlTests.cs`、`ApprovalReceiptRecoveryPostgreSqlTests.cs`及对应HTTP测试。按具体未覆盖风险筛选，不把这些文件全部机械重跑。

**日终出口：** 台账可按准确已交付合同处理依赖；三张候选各有明确核销结论。目标是完成三卡，不以“计划了三卡”或“已有报告数量”代替实际核销。


D1本轮结果：D1准确交付依赖规则已实现；WP09-11与WP12-07按原范围核销，WP02-10保留租户连接采用关系待核对。原375卡变为26 DONE，125目标/247正式验收不变；继续D2 CRM两条验收。 采用收尾见[W252报告](../evidence/W252-01/DELIVERY.md)。

## D2 / W253：CRM两条验收补项

**直接前置：** CRM当前已采用身份/会话、本地撤权门、公开提交和审核Owner。复用[W130](../evidence/W130-01/DELIVERY.md)/[W131](../evidence/W131-01/DELIVERY.md)/[W246](../evidence/W246-01/DELIVERY.md)身份，以及[W120](../evidence/W120-01/DELIVERY.md)–[W124](../evidence/W124-01/DELIVERY.md)审核和保留结果。D1台账工具修正不是CRM产品测试的额外资格前置；D1若仅剩文档收尾，仍可按真实合同安排CRM执行。

**CRM源码与测试定位（以CRM仓库为根）：**

- `src/CP6.CRM.Api/CrmEndpoints.cs`：`/crm/switch-organization`及登录callback，已有旧会话注销/轮换，不重写登录链。
- `src/CP6.CRM.Api/Authentication/CrmAuthenticationService.cs`、`PostgreSqlCrmAuthStore.cs`：持久session消费和当前读取。
- `src/CP6.CRM.Api/Inquiry/CrmInquiryIntakeDecisionOwnerV1.cs`、`.Graph.cs`、`CrmInquiryNativeHostV2.Intake.cs`：审核期限、原决策及HTTP边界。
- `src/CP6.CRM.Api/Inquiry/CrmInquiryRetentionOwnerV1.cs`、`CrmInquiryRetentionWorkerV1.cs`：仅核与真实到期/保留职责的关系，不能混淆两种期限。
- 扩展`tests/CP6.CRM.IdentityPostgreSqlTests/AuthenticationServiceTests.cs`及`InquiryIntakeDecisionTests.cs`；必要时新增`OrganizationSwitchHttpTests.cs`、`InquiryReviewExpiryTests.cs`以隔离实际HTTP场景。已有覆盖直接复用，不为新文件名重复测试。

**执行顺序：**

- [x] 先查既有测试和实际成功日志，分别定位“切组织清理旧会话”与“审核期限到期”。找到同输入有效执行证据则直接引用，仅补未覆盖行为。
- [x] 切组织场景：模拟A/B两个组织与合法用户，实际PG会话+HTTP登录A，调用切组织并核Cookie清除/持久旧会话失效；旧Cookie不能继续读写，登录B后只见B范围；无CSRF请求不能改变会话，原会话不能被重建重新激活。
- [x] 审核到期场景：用原合同合法的历史完整模拟提交/配置/Attempt/Consent/期限事实，或既有明确受控时钟，驱动实际Owner/HTTP边界。不要直接UPDATE不可变原件，不缩短七天合同，也不把60分钟PII匿名化当审核到期。
- [x] 核准确期限前、等于期限与期限后的行为：期限前合法操作按原规则；到期后不可再转Lead，读取呈现原合同规定的到期投影/终态，原回执与历史事实保留；重复处理不能产生第二份业务效果。放行与到期竞争按同一当前决定点裁定，不能出现到期后额外Lead。
- [x] 依原冻结合同区分派生到期投影与持久终态/worker责任，不能为测试任意新增状态或新的到期worker。若出现实际BUG，先按GitHub Issue登记，再在本任务分支修复并只回归受影响行为。
- [x] 若修改原页面状态，执行已有`InquiryIntakeBrowserTests.cs`与`fixtures/intake-ui.cjs`相关场景；没改页面且输入未变则复用原证据。完成CRM实际采用后再更新Main对应原卡结论。

**相关命令：**

```powershell
dotnet test tests/CP6.CRM.IdentityPostgreSqlTests/CP6.CRM.IdentityPostgreSqlTests.csproj --filter "FullyQualifiedName~AuthenticationServiceTests|FullyQualifiedName~OrganizationSwitchHttpTests|FullyQualifiedName~InquiryIntakeDecisionTests|FullyQualifiedName~InquiryReviewExpiryTests" --logger "trx;LogFileName=crm-session-review.trx"
```

执行前使用已有隔离PG测试引导，`CP6_CRM_PG_SETTINGS`只指向该窗口自有配置；不得将密钥/连接串写进计划、Git或输出。预期实际HTTP/PG场景成功，所选必需场景没有skip、没有零用例假通过。单元predicate检查不替代期限后的实际效果。

**日终出口：** 两条行为各有真实执行或准确复用结论；必要修复已合入CRM，Main只登记其实际采用范围。WP02-06/03-03是否整卡DONE另按全部原scope核销，不仅凭这两条补项。


D2本轮结果：D2实际CRM登录/切组织/旧会话失效通过；七天审核期限前、整点、之后及写入越界回滚通过，修复CRM Issue164。读取到期禁用动作；Expired持久终态与对应保留策略仍未实现，不关闭整卡。继续D3编号原命令提供者。 [准确范围与证据](../evidence/W253-01/DELIVERY.md)。

## D3 / W254：编号原命令分配与恢复

**原卡范围选择：** 实现WP02-16中“可信编号scope/规则版本、原子分配唯一、command固定号码/结果、不回收”的服务提供者部分。完整规则编辑CAS/历史影响、所有业务调用者切换和CodeGen模板/路径白名单继续保留。保持旧接口已承诺的合法域，不用GUID化、固定短字符串或新的精度限制缩小原输入。

**直接前置：** D1确认的Main PG事务/原结果/当前授权能力；[SYS-NUM-01](../../design-handbook/modules/SYS-NUM-01.md)及其原文；[W65](../evidence/W65-01/DELIVERY.md)对应现有Seq交付（准确报告路径由本卡采用记录定位）。当前`SeqService`只有BizKey/日期，已有PG规则行锁、调用者事务savepoint，但没有持久operation分配回执；不能将它包装后直接声称幂等。

**文件职责：**

- 核对并必要修改`CP6.Core/Services/Pub/SeqService.cs`、`ISeqService.cs`：旧分配器兼容与同域唯一写者，不重写无关SQL Server历史路径。
- 新增`CP6.Core/Services/Pub/NumberAllocationOwnerV1.cs`：受信Owner输入、原operation查询/分配、当前披露和明确错误；不是任意公众领号接口。
- 新增`CP6.Core/Services/Pub/NumberAllocationContractV1.cs`、`NumberAllocationStoreV1.cs`：准确scope/规则版本/period/号码/原回执以及PG唯一约束和事务存储。命名是本计划拟建路径，执行时不得与已存在职责重复造Owner。
- 按仓库现有PG持久化方式添加必要表/约束/迁移，只占一个迁移窗口；不为方便把全EF模型切成一租户一schema。涉及正式存储表示时记录版本及无损依据。
- 扩展`CP6.Tests/SeqServicePostgreSqlTests.cs`，新增`CP6.Tests/NumberAllocationPostgreSqlTests.cs`覆盖新服务。业务Owner未采用的范围如实保留，不能用模拟内部Owner宣称消费者完成。
- `CP6.WebApi/Controllers/Pub/SeqController.cs`的管理/删除/preview是同域并发风险入口：确实影响新分配的写入必须同门治理，不能在服务旁留下可删除历史或重置计数的旁路；不顺带重做管理页面。

**执行顺序：**

- [x] 在首个约1小时内固定实际编号域与单writer策略：来源Owner、tenant/global、法人/仓库/业务scope、ruleVersion、时区/period、数据库唯一域及原allocatorEntry。先选择可独立交付的服务边界；未确认的历史域保持旧Owner，不自动迁移计数器。
- [x] 先添加原命令持久恢复、并发和故障边界回归，再实现PG事务：原operation查回→当前可用规则与scope→同一数据库锁/唯一约束→计数推进+allocation receipt共同提交→返回已分配结果。同key异义明确冲突，不能静默换key或多领一号。
- [x] 分配事务与业务保存明确分轴：本服务独立提交后业务失败，原号保留不可回收；提交响应丢失只查原allocation。旧接口在调用者事务中仍是暂定值，不混称独立持久回执。接口若不能准确表达该差异，先调整版本化边界再采用，不伪造跨库原子性。
- [x] 旧/新分配器同域不能各自维护一份计数。为新服务治理的域采用同锁/权威写入规则，规则删除/修改与分配共同判断；未切换域继续原Owner。计数缺失快速失败，不用业务表max(number)猜续号。
- [x] 实际PG验证：同key同输入返回同一原号；同key异输入拒绝；两个独立连接并发不重号；提交响应丢失恢复；业务失败不回收；tenant/scope隔离；旧规则版本/非法格式/耗尽明确失败；preview不写计数；故障不能留下计数和回执半组。
- [x] 核当前权限和原结果披露，边界负责不变量后内部直接复用；不逐层重复序列化、hash或身份检查。需要检查当前业务状态的规则在最终决定点核对。
- [ ] 集中审查PG事务/并发/合法域和旁路写者，执行相应回归，记录限定提供者交付及仍未采用的业务调用者；合并后按正常流程释放席位。

**相关命令：**

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SeqServicePostgreSqlTests|FullyQualifiedName~NumberAllocationPostgreSqlTests" --logger "trx;LogFileName=number-allocation.trx"
```

执行前沿用`TenantManagementPostgresFixture`的隔离启动，正确配置`CP6_PERMISSION_PG_ROOT`和`CP6_PERMISSION_PG_BIN`。预期实际PostgreSQL场景通过、所选必需测试无skip；不改用InMemory验证锁、唯一约束或未知提交。

**日终出口：** 本期限定编号提供者具备真实PG原分配/原结果恢复与故障语义，旧新写者没有同域双发号。原WP02-16其余范围继续列明，不能将服务测试算成全部消费者采用。


D3实际结果：D3编号原命令提供者已实现并通过真实PG验证：新登记Main租户域独立持久分配/原结果恢复、不回收、当前权限和同域唯一写者。旧域保留原Owner；完整规则CAS、业务调用者切换、全局域和CodeGen仍为WP02-16余项。三天必做限定交付进入最终采用/释放核销。 [交付及真实余项](../evidence/W254-01/DELIVERY.md)。最后一项的审查与验证已经完成，正常采用/finish以最终证明核销。

## 候补、风险和顺延

|候补|何时取用|任务边界|不能替代的必做出口|
|---|---|---|---|
|B1：WP03-01/02整卡适用核销|D2必做提前完成，且已有输入无需新增实现|依据W135/W247核闭合合同、日历effectivity与公开状态；不另造NUM/FILE调用，不扩H01到任意时区|切组织/审核到期缺失的实际运行|
|B2：WP02-04租户原命令/CAS细化|D3必做已闭合且仍有容量；否则作为下一期首项|先锁创建模板/原命令、配置CAS、暂停恢复版本与共同fence的精确实施范围；完整实现另开任务，不承诺当天完成|编号原操作/并发/UNKNOWN与单writer保证|

时间不足时先移出B1/B2；再顺延尚未开始的主任务，不削弱原合同、不压缩必需验证、不将未运行项写PASS。每张D1候选在约45分钟核对后仍发现实质缺实现，记录准确缺项并继续下一张，不把首日变成该领域全部重建。这个时间盒限制调查扩张，不自动授予DONE。

D3主要不确定性是实际scope和旧新writer切换。若在首个核对阶段发现必须改变已接受Owner身份、合法域或存储合同，记录具体差异并重排此窗口；可以继续不依赖该决策的准备，但不能以缩小合同来满足日期。若实际问题使主任务超出当天容量，日终保存精确分支、恢复位置与下一步并尽可能释放席位；恢复继续原任务分支。

## 单人、跨仓与生命周期执行

1. 用户已授权执行三天目标；W252/D1已完成，W253/D2已实际START并完成限定验证，W254/D3已START并完成限定实现/验证。按前置与当前主线依次推进，不按日历定时启动。W251计划阶段“未开工”记录保留其历史含义。
2. 每个窗口从所属仓库当时确认的最新`origin/main`创建任务分支。Main候选`codex/w252-01`、`codex/w253-01`（CRM采用台账）、`codex/w254-01`；CRM候选`codex/w253-02`。创建前仍查重复任务/PR，不能把这些拟名当已领取。
3. 复用DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018`与DEV-2 `D:/CP6-worktrees/dev-2`，INTEGRATION-1默认空闲。DEV-1保持Main，保留可复用前端缓存；DEV-2在Handoff与CRM之间串行复用。所有写入均官方`status/start`，中断`resume`，收尾`finish`。
4. D2先将DEV-2上的Handoff START固定提交推送保存，再以`finish --outcome deferred`记录“暂让CRM使用席位”、准确HEAD与恢复位置；保留该任务分支，释放后官方`bind`为CRM。CRM提交/合并/finish后bind回Handoff，以原任务、原Owner和已保存HEAD执行`resume`，在同一分支补DELIVERY，再正常PR合并/清理/finish。不能将已完成并删除的任务分支重新开工。DEV-1承载Main协调台账，登记准确CRM采用；含Handoff在内始终不超过2开发+1集成配额，不新增整仓副本。
5. Handoff START/DELIVERY使用任务资料分支和固定提交，普通额度内直接执行；不等重复运输ACK。Main唯一台账、生成页、四份项目状态由本对话同任务窗口同步，一份交付报告引用既有有效证据，不复制成套回执。
6. 每个任务只做相关行为测试和一次完整diff审查；实际BUG按Issues。每次push/PR前核工作流及相关依赖内容/事件过滤；普通Actions保持手动，本期不主动触发或重跑。缺本地环境不得改用付费远程运行器冒充完成。
7. 正常PR合并且远端准确采用后，检查无新增提交/依赖/占用，定向删除本任务本地/远端分支，停止本任务进程并finish。每次交付同时报告分支处置、工作区状态、席位释放；主目录用户资料保持。

## 每日交付检查

- [ ] 原任务ID与本次限定范围准确，scope和正反合同没有缩小。
- [ ] 实际结果和复用结果分开；模拟了哪些输入、真实运行环境及未验证范围清楚。
- [ ] 实现、相关测试、必要页面/文档和原结果恢复按本窗口闭环。
- [ ] 无权限/租户/精度/事务破坏；UNKNOWN不被当成失败或无效果。
- [ ] 一次集中审查通过；必需失败已修复，不靠跳过或默认值过门。
- [ ] 正常合并、远端采用、准确分支清理与官方finish均完成。

## W251计划交付记录

本轮只编制计划，未修改产品源码、测试、数据库或运行配置，未执行上述命令。计划基线Main `e5e7a9dc18d49c93e7d93341ac9a179a07a0f76b`、CRM `c118d4f0f6aeb5e71f174d9a0ef96ac74fefbf2f`，均已重新fetch确认；Platform保持`a89e90eb4f078f37a6f8610882079073185fbcf8`。Handoff正式入口是`codex/t02c-entry-handoff-20260921`，不假定存在main。

W250终态已核对：Main PR677、Handoff PR226完整采用，准确分支已清理、席位已释放。本轮归档其提交前快照一次；原七天持续目标仍COMPLETE。375原卡仍24 DONE、2 IN_PROGRESS、349 PLANNED，59卡审计结论和125/247状态不变。

本计划由作者检查容量、依赖、原scope、准确源码/测试入口、引用、模拟/部署边界和生命周期顺序；只做文档与台账一致性验证，不把计划步骤记为业务PASS。Main/Handoff正常采用及finish的最终状态由本轮PR和`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave251-final-verification.json`核销，提交前不提前声称席位释放。

提交前快照：Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/227)已准确采用并清理分支、释放DEV-2；Main待最终正常PR/finish。
