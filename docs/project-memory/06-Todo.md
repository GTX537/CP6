# 当前待办与优先级

## 2026-10-18：第十八轮公共基础持续目标，单总控串行

用户要求未来24小时由当前总控对话独自推进，已建立持续目标及[第十八轮执行计划](../development/dispatch/20261018-wave18/README.md)。从已确认main `b4516ce3b2e4098347ee9a0d89322fcd561a5da9` 接续，依次为当前授权读取一致性、租户管理与必需审计同事务、身份权威快照/版本/Outbox一致性、受影响PostgreSQL/HTTP串联及逐项集成。预计16～24小时，包含修正与合并；时间到不等于目标完成。

本登记仅W18-01开放实施前置，正常规划合并后从最新main开独立分支。其余窗口由同一总控核对实际前置后串行接续，无需用户重复批准；不派新对话/子代理。复用已释放COORD目录，新增/删除worktree均0；用户原件、历史证据及数据库保留。当前没有本轮产品通过声明。

PostgreSQL为唯一新增目标，精简本地验证、一次任务级集中作者自审，明确不冒充独立审查。不触发Actions、不改既有数据库或环境、不部署生产。完整WP02、普通身份全库RLS采用、跨仓Owner撤权fence及业务验收未完成；本轮有限成果不自动提升375任务/125目标状态。

## 2026-10-18：第十七轮收尾，四项Issue均已关闭

原A最终审查134c283/be1e4dc批准菜单候选3bc3d3c8；原B局部复查a7d24cd8/82df565批准发布工具候选aec0e965、解除#251/#252。C经[正常代码PR](https://github.com/GTX537/CP6/pull/271)、D经[正常代码PR](https://github.com/GTX537/CP6/pull/272)逐项合入，远端产品main `8146d5e4063a1ae702e7639dc00f2df0ba29a37d` 包含两候选且范围文件一致。#251/#252/#243原完成条件已核对并Closed；此前#253经PR269关闭。本轮所有实现/审查窗口与资源预约关闭，原候选/证据/目录保留。

复用原11项工具回归和一次实际publish、锁定Vitest4.1.11的23项组件测试及原页面/type-check；失败记录保持。新publish source仍68990978，原宿主/HTTP仍4704592f；菜单仍本地API夹具，未改称最新main宿主、整仓type-check或真实业务通过。总控只做集成一致性及文档/台账检查，无重复产品运行、Actions或数据库/部署操作。

完整WP02-02/18/28、375任务/125业务状态不提升；F1身份/租户生命周期及F2真实Owner事务仍按后续有限窗口推进。总控继续复用既有目录切独立分支，本次新增/删除worktree均0；旧目录回收计划未执行。以下Open/待审查/待合并是历史时点。

## 2026-10-18：BUG253已集成并关闭，C/D继续审查收口

[#253已Closed](https://github.com/GTX537/CP6/issues/253#issuecomment-6250839015)。[原C定向复查](https://github.com/GTX537/CP6.AI-Handoff/blob/46c8ee9de5b1d85f0dd316fa389f492301ef5e52/handoffs/CP6-DEV-20261012/tasks/REVIEW-W13-B/REVIEW.md)解除R1，固定候选836de524通过[正常PR269](https://github.com/GTX537/CP6/pull/269)合入远端main `6be28cb56ffa5cca4d29cfc26d935d769daf3535`；五个范围文件与已验证候选一致。原合法实例/身份的真实stdin/PG回归5通过、0失败/跳过，RED3与原7项未变证据保留真实范围，不因合并SHA变化重复运行。B产品窗口与PG55436预约释放；原始证据、候选和目录暂保留，B继续原REVIEW-W13-C局部复查。

C固定候选aec0e965已通过Handoff5f9d5d12交B复查#251/#252；其报告记录11项工具回归和一次真实publish，尚待原B结论，不预先关闭Issue。D固定候选3bc3d3c8及锁定23项补证已交A，原集中审查最终结论待回。A/B/C/D不重复START/ACK。完整WP02-02/18/28及375任务/125业务状态不提升，当前待审查不转为业务通过。

沿[存储计划](../development/planning/20261018-workspace-storage.md)，本收尾继续复用原COORD目录创建独立分支，新增/删除工作树均0。文档/台账检查通过；无产品重跑、Actions、既有数据库/服务操作或部署。以下原候选Open/不可合并/待复查记录为历史时点。

## 2026-10-18：四对话已开工，协调目录改为复用

第十七轮A真实START及B/C/D实际PROGRESS已回读：A准备菜单集中审查、B修#253、C修#251/#252、D补#243锁定依赖证据。台账已登记实际开始。随后收到D固定候选3bc3d3c8（Handoff2af18458），锁定Vitest4.1.11相关23项通过，仅原DELIVERY改变；A准备报告3e0b584d已到，接续同次集中审查，最终可合并结论待回。B随后交付#253修正836de524（Handoffff88448c），真实stdin/PG定向5通过、保留原3项RED失败，待原C局部复查；C自身#251/#252修正仍在进行。写集/端口/Owner不变。

按用户节省空间要求，完成107个CP6和46个Handoff工作树的只读盘点，已统计约167 GiB逻辑文件，docs约86 GiB；不能当作保证可回收容量。总控复用已合并的wave17-dispatch目录切新分支，没有新增工作树。详见[存储计划](../development/planning/20261018-workspace-storage.md)：固定目录席位、任务独立分支、只读审查免完整检出、保留原证据；首批5个干净已合并协调目录约5.2 GiB仅列候选，尚未删除。当前四人工作目录保持；无Actions、产品测试或数据库操作，完整任务/业务验收未提升。

## 2026-10-18：第十七轮恢复公共基础候选收口

P0/P1队列已闭环，当前四个Open均P2。本轮B修正#253原始期限精度（自有PG55436/真实CLI），C同窗口修正#251/#252源码绑定与自有进程清理；沿原B/C交叉报告只复查变化。D补菜单候选锁定Vitest4.1.11证据，A准备并在固定交付后完成一次集中审查。详见[本轮执行包](../development/dispatch/20261018-wave17/README.md)。

准入不等于开工：尚无本轮实际PROGRESS/START或新候选。新产品分支由总控在准入后从最新main承接原提交，旧候选保留。只跑具体受影响验证；C默认无PG/HTTP，D默认仅相关组件测试，原有效证据复用。375任务状态与125业务验收未提升；共享状态/串行合并由COORD独占，无Actions、现有数据库操作或部署。

## 2026-10-18：BUG132已集成并关闭，第十六轮收尾

[#132](https://github.com/GTX537/CP6/issues/132#issuecomment-6250315486)已Closed。[D原报告局部复查](https://github.com/GTX537/CP6.AI-Handoff/blob/c84c6da6e2bf1ee570513dacf3e03248a8910f00/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)确认e83593a3可合并、R132-01解除；[正常PR265](https://github.com/GTX537/CP6/pull/265)已合入，远端main `5a29b6b106aa3b4bf267811371771d9209bb80fd` 包含候选且17个范围文件一致。[单份DELIVERY](https://github.com/GTX537/CP6/blob/e83593a3ddd5ffd1ac0614b943eaf3358483989d/docs/quality/evidence/issue-132/DELIVERY.md)保留完整精确集合、有界POST、旧GET兼容及实际错误层413修正证据。

复用前端35、修正HTTP/OpenAPI65和InMemory9通过结果，各0失败/跳过；修前失败和原61项的宿主限制保留，不重复产品测试。Issue原条件满足，无新增数据库/真实DEV门禁，也不宣称该类验收已通过。台账本轮实现/审查窗口关闭、资源预约释放；原A候选保留，B/C旧阻断修正仍需独立准入，375任务/125业务状态不提升。Actions启动/取消/触发变更均0，无数据库操作、现有服务替换或部署。以下Open/待审查内容是历史。

## 2026-10-18：总控已交付R132-01修正，待原D局部复查

修正代码 `40a2d2b292df001cea4c338600ffb958e4381ae7` 已完成本地定向验证，见[原单份DELIVERY](https://github.com/GTX537/CP6/blob/40a2d2b292df001cea4c338600ffb958e4381ae7/docs/quality/evidence/issue-132/DELIVERY.md)。仅库存POST局部处理绑定阶段的413异常，公共中间件/Program/请求上限未改。HTTP夹具加入实际Space错误层；修前2项预期413/实际500的RED保留，修后HTTP/OpenAPI65通过、0失败/跳过（7行为+58合同）。非目标异常保留原500映射，合法边界200、超限413/ProblemDetails及无runtime调用均通过。

Kestrel chunked计数包含格式字节；本夹具单chunk在边界处含14字节格式，报告已明确载荷/传输字节区别，保留最初错误正向预期的失败记录。原前端35/InMemory9及D未变范围结论复用，无重复全仓或数据库/真实DEV验证。

原A候选325dc769和干净工作树保留，COORD仍是修正唯一Owner；原D对固定修正只复查R132-01及影响，沿用原REVIEW，无第二START/ACK。#132及阻断保持Open，产品尚未合并；本条随候选交付，不能当作主线集成或D通过。18232/15232无监听，无Actions、数据库操作或部署，375任务/125业务不提升。

## 2026-10-18：总控独占接手R132-01，原D继续局部复查

用户要求继续推进。远端A仍为325dc769/Handoff8ff05c67，原A工作树干净，未观察到修正开工或交付；原D仍为9ff8c9f的No结论。当前工具无法唤醒原用户对话，COORD按总控权限接手本项定向修正，作为唯一产品写入者；原A保留原候选和工作树，不再并写R132-01。此调整不是原A已接受消息或已开始修正的事实。

本登记正常合并后，从最新确认main创建 `codex/bug-132-http413-correction-20261018` / `D:/CP6-worktrees/bug-132-http413-correction-20261018`，以可审计cherry-pick承接固定325dc769，再修正端点超限错误映射。原13项写集和18232/15232资源转由COORD独占；Program、公共中间件、依赖、数据库仍只读。实现前再次核对原A无变化及端口空闲，如有并行冲突先停下协调。

先在HTTP夹具加入实际Space错误层，复现超限返回500，再做端点局部修复；覆盖exact1MiB200、声明长度/chunked超1字节413、ProblemDetails body/content type、超限无runtime调用及非目标异常不被吞。复用未变前端35/InMemory9等有效证据，只补受影响HTTP/合同。原D在固定修正交付后只复查R132-01及影响，原REVIEW接续，无第二START/ACK或完整审查。

本次仅所有权登记，不宣称修正已完成；#132和R132-01保持Open，产品未合并。无数据库/真实DEV新增门禁，无Actions、数据库操作或部署。完整任务24/375 DONE、业务0/125不提升。

## 2026-10-18：BUG132集中审查暂不可合并，原A接续定向修正

D已对固定候选 `325dc7693edf09ec0c9175c68554e4e3c72b2af0` 完成一次集中SPEC/QUALITY；[原REVIEW更新](https://github.com/GTX537/CP6.AI-Handoff/blob/88c89e639cfa1b1da6d98ed84d96f172cf6022f3/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)和Handoff `9ff8c9f243b6446272e5b15e5c7901719126b349` 已远端回读。**Ready to merge：No；P2阻断R132-01。** 源码显示实际SpaceDesignProblemDetailsMiddleware把超限绑定异常改为500/SPACE_INTERNAL_ERROR，而原HTTP fixture省略了该层，未证明声明的413/ProblemDetails合同。此为源码审查结论，D和COORD未新执行实际应用探针；不代表大小限制被绕过。

原A在同一BUG132分支及既有写集接续修正，无第二START/ACK。优先端点局部处理绑定阶段异常；Program和公共中间件保持只读。定向HTTP fixture纳入实际Space错误层及相对顺序，保留RED→GREEN：恰好1MiB为200，Content-Length/chunked超1字节均413、application/problem+json及body状态符合合同，超限不调用runtime，非目标异常行为不变。仍只更新原DELIVERY，固定交付后D在原REVIEW仅复查该修正及影响。

原前端35、HTTP/OpenAPI61、InMemory9通过记录保留；其中413结果限简化宿主，不能算实际管线通过。未变前端/内存与其余有效证据复用，只补受影响HTTP/合同及必要编译。本轮不新增数据库或真实DEV门禁，不重做全仓测试或完整审查。

[#132](https://github.com/GTX537/CP6/issues/132)保持Open，产品未集成；R132-01作为本Issue候选阻断记录，不另建重复Issue。#38/#257保持Closed，B/C修正仍待各自窗口；完整任务24/375 DONE、业务0/125不提升。无Actions运行/取消、工作流变更、数据库操作或部署。

## 2026-10-18：BUG132固定交付已收，等待D集中审查

A候选 `325dc7693edf09ec0c9175c68554e4e3c72b2af0`、Handoff `8ff05c67e42e6b78335afc07cf785e561b54a8d0` 已远端回读；[单份DELIVERY](https://github.com/GTX537/CP6/blob/325dc7693edf09ec0c9175c68554e4e3c72b2af0/docs/quality/evidence/issue-132/DELIVERY.md)限登记的10个路径，基线为main `1e29931374626a492ca92e848b8d8fcaf871eddf`，工作树干净。库存查询改为完整精确集合的有界POST，保留旧GET；产品代码尚未集成。

COORD读取原始日志/TRX，核对前端 **35**、HTTP/OpenAPI **61**、InMemory服务 **9** 项通过，各0失败、0跳过，未重跑。前端最终来源为锁定依赖Vitest4.1.11，早先4.1.9结果不计作最终证据；HTTP使用真实Kestrel/Cookie/CSRF及受控权限与runtime数据，服务测试使用EF InMemory/记录源，不冒充PostgreSQL或真实WMS验证。A自有18232/15232均无监听。

A真实START `b7b618df`、D真实START `2b45642d` 已登记。[D原REVIEW](https://github.com/GTX537/CP6.AI-Handoff/blob/843dcc019ea311bc18ac186f5ad397c865f9ff2e/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)及准备消息 `603df33948af18de597f2e733374b6d21db333fe` 仅为只读准备，尚无Ready to merge结论；“尚无A固定交付”的前置现已满足。原D直接对固定候选在同一REVIEW接续一次集中SPEC/QUALITY，不重复START/ACK或新增报告；总控收件不代替正式审查。

[#132](https://github.com/GTX537/CP6/issues/132)保持Open，待D结论后正常集成和原Issue验收核对。本轮不新增数据库或真实DEV门禁。#38/#257保持Closed，P0暂停已解除；B/C原P2修正仍待各自窗口，旧菜单候选未变。完整任务24/375 DONE、业务0/125不提升。无Actions运行/取消、工作流变更、数据库操作或部署。

## 2026-10-17：仪表盘租户P0已完成真实DEV并关闭，转入P1库存传输

[#257](https://github.com/GTX537/CP6/issues/257)已Closed：[PR260](https://github.com/GTX537/CP6/pull/260)合入D审查通过的22928c9a，远端main47b69063核对候选文件一致；[单份DELIVERY](../quality/evidence/issue-257/DELIVERY.md)记录真实PG DEV验收。实际发布API/Web身份和文件哈希匹配，3个页面登录账号的空租户、精确订单及交替暖缓存通过；停用主体403、匿名401。原6项PG/HTTP结果复用，未重复测试。

4条自有测试订单移除、3账号/3租户停用及refresh吊销，API/Web停止，原PG服务保留。既有认证五表/业务六表指纹、688表和四历史不变。无Actions、工作流修改、迁移或生产部署；不提升其他WMS块、全业务/生产验收。

当前无开放P0，P0暂停解除；依BUG管理P1下一项规则，[第十六轮](../development/dispatch/20261017-wave16/README.md)登记原A修复#132精确库位范围有界传输、原D准备/集中审查，尚无真实START。旧整站fallback补丁已撤回不得照搬。B/C候选和三个P2保留，尚无新的产品修正窗口；#243菜单候选不变。完整任务24/375 DONE、业务0/125不提升。

## 2026-10-17：BUG257固定候选已收，D集中审查待接续

A候选 `22928c9ae9ad93172c61e3df5e0353cdaace4af6` 已推送，Handoff `9de55c7441830061fc73541c20c562575f4086a5` 已远端回读；[单份DELIVERY](https://github.com/GTX537/CP6/blob/22928c9ae9ad93172c61e3df5e0353cdaace4af6/docs/quality/evidence/issue-257/DELIVERY.md)范围严格为登记六文件。COORD读取原始green-subject.trx核对真实PG/HTTP **6通过、0失败、0跳过**，包括四条HTTP隔离/缓存边界与两个原生关系测试；只复用原结果，未重跑产品测试。原失败及夹具修正保留，不将RED中空用户GUID冒充合法主体正向证据。A独立PG/API/Web端口已释放，既有PG服务运行，未操作DEV数据。

A真实START6cdbf743、D真实STARTac370a04均登记。[D单份REVIEW](https://github.com/GTX537/CP6.AI-Handoff/blob/fd1ee29f2f584154f7013ee15adebc71e1744f6f/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-257/REVIEW.md)及Handoff `88bf3beb6a175a83a79d80ea0ff59f2f43a79a8a` 当前仅只读准备，尚未给候选Ready to merge。A固定交付已满足原审查前置，D直接在原REVIEW接续一次集中SPEC/QUALITY，不需第二START/ACK。总控收件核对不代替该正式审查。

[#257](https://github.com/GTX537/CP6/issues/257)保持Open，修复代码尚未合并，真实DEV为NOT_RUN；D通过后由COORD正常集成并在已确认PG副本验证空租户/交替缓存。#38保持Closed。B/C三个P2与旧候选普通实施/集成继续暂停；完整任务24/375 DONE、业务0/125不提升。无Actions启动/取消、工作流修改、数据库迁移或生产部署。

## 2026-10-17：登录P0真实DEV通过并关闭，转入仪表盘隔离P0

[#38](https://github.com/GTX537/CP6/issues/38)已关闭：[单份DELIVERY](../quality/evidence/issue-38/DELIVERY.md)记录用户确认的本机PostgreSQL副本DEV、main84896578实际发布身份和浏览器原步骤通过。首轮专用角色缺user:query/add被拒，修正仅本次角色的两项权限后重跑通过；产品未变，原失败保留。原10用户、4租户及角色/菜单关联指纹、688表和四历史不变；新增测试账号/租户停用、refresh吊销、API/Web停止，既有PG服务保留。认证50项等原证据复用，无全仓重复测试。

真实页面另显示新空租户读取其他租户订单，源码/数据库核对全局查询和缓存，独立登记[P0 #257](https://github.com/GTX537/CP6/issues/257)。[第十五轮](../development/dispatch/20261017-wave15/README.md)登记原A唯一修复、原D准备与一次集中审查；尚无实际START。B/C三个P2修正和原候选普通集成继续暂停，当前原因是#257。375完整任务/125业务目标不提升，仍24/375 DONE、0/125业务完成。

无Actions启动/取消、工作流变更、原库迁移/覆盖、Tunnel或生产部署。测试账号写入仅发生在用户选定PG副本中的本次专用租户；不宣称业务隔离、HTTPS/OIDC enabled或生产验收通过。

## 2026-10-17：登录P0本地修复已集成，真实DEV待确认

[BUG38代码PR](https://github.com/GTX537/CP6/pull/255)正常合并；远端main `17ce6f742bf9aa885f7a293e13c30fcba124ca59` 已核对包含A固定候选 `09017d7ad14c7647601edac258422365ff81daf9`，范围文件与已验候选一致。[D最终集中审查](https://github.com/GTX537/CP6.AI-Handoff/blob/84b6d2de0f7b30d493cf068db94fafc20fb7594b/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-38/REVIEW.md)本地Ready to merge = Yes、无阻断，交付ddc65ece已收；本任务单份[DELIVERY](../quality/evidence/issue-38/DELIVERY.md)追加集成记录。

复用已审查的相关认证50项（含HTTP13）、补断言后受影响HTTP13及完整应用PostgreSQL浏览器证据；主线只有协调文档漂移，未变代码/依赖不重复验证。COORD仅执行集成包含性/范围一致性及文档台账检查，不将复用称为新运行。原失败和HTTP/浏览器/PG证据边界保留。

下一步为[#38](https://github.com/GTX537/CP6/issues/38)真实DEV验收：确认目标/入口、包含修复的API/Web身份和测试账号/租户及创建改密范围。DEV NOT_RUN，Issue保持Open；B/C的3项P2修正及原候选普通实施/集成继续P0暂停。完整任务24/375、业务0/125不变，无Actions、工作流变更、部署、Tunnel或既有数据库操作。

## 2026-10-17：A登录候选已交付，交叉审查阻断已登记

[第十四轮最新收件](../development/dispatch/20261017-wave14/README.md)已同步四路真实START与固定消息。A候选09017d7a待D一次集中审查；D的“仍无DELIVERY”是早期快照，现可按原窗口接续，无第二START/ACK。A报告50项相关认证成功（含HTTP13），后续仅受影响13项复跑；本地PostgreSQL完整浏览器成功、资源已停止。COORD没有重跑这些结果，也没有合并A或关闭#38。

B对C审查为No（2项P2），C对B为No（1项P2）；对应GitHub Issues #251/#252/#253。原实施者B/C仅准备窄修正，产品修正未准入，原候选普通实施和集成继续P0暂停。D旧菜单/#243不变；完整任务24/375、业务0/125与原NOT_RUN保持。

文档所列本地DEV身份端点只读请求未成功，实际运行身份仍未确认；无登录/改密/部署/重启/数据库动作。真实DEV NOT_RUN，不以本地PG代替Issue关闭条件。此次验证为收件/源码定向核对、文档diff与台账一致性，没有新增产品测试、Actions或工作流改动。

## 2026-10-17：第十四轮已分配给原四对话，尚无真实 START

[第十四轮执行包](../development/dispatch/20261017-wave14/README.md)登记：A唯一实施登录P0 #38；B只读审查C的b9f3f30b实际publish候选；C只读审查B的e2bfcc0b租户资格候选；D先准备#38浏览器/DEV验收，在A固定候选后完成一次集中SPEC/QUALITY审查。交叉审查不改变原候选Owner、不授予产品写权限；只有A拥有新的认证代码/测试窗口。

本轮基于远端main4d5c115f，A实际从准入合并后的最新已确认main开始。#245已关闭，原A窗口不重开；B/C/D原候选与工作树保留，普通集成仍因#38 P0暂停。没有收到执行者真实START或新的交付，不把发布派工当作开工。完整任务24/375、业务0/125及原NOT_RUN不变。

#38本地代码交付和真实DEV验收分别记录，没有授权部署/替换环境、既有数据库操作、Actions或正式发包。台账、生成页和四状态由COORD统一同步；本次仅做文档/资源/链接及台账一致性检查，不重复产品验证。

## 2026-10-17：权限 P0 修复已集成，转入登录 P0 准备

[权限修复 PR](https://github.com/GTX537/CP6/pull/248)正常合并，远端 main `be66f70748b1d3185d95629b3a9a7e0b139eeea0` 已核对包含集成提交 `33d7c666284891cc91b6b1cb02895f1a61bc64b9`，范围文件与已验候选一致。#245 原步骤由最终相关回归及有效原生 PostgreSQL 证据覆盖，Issue已关闭；A限定窗口CLOSED。完整WP02-03仍PLANNED/NOT_CHECKED，Owner最终撤权fence、权威版本/字段V1来源、全Main与业务验收仍未完成。

[单份交付报告](../development/evidence/WP02-03/current-permission-consumption/DELIVERY.md)保留原15红测、原PG夹具失败、审查发现的2FA用途绕过及修正6失败/21通过记录；最终37项相关测试通过，原审查者局部复查通过，未受影响PG3及其他结果按来源复用。未因提交/合并重复产品验证。

[公共核心基础优先](../development/planning/20261017-foundation-priority.md)已获批准并执行首项P0交付。下一个是#38登录旧Cookie修复，独立范围已准备但尚未开工；真实DEV验收仍须按Issue条件留证。B/C/D候选保留、普通集成暂停，未收到暂停ACK不冒称已收到。24/375完整任务、0/125业务目标不变。无Actions触发/取消、工作流变更、发包、既有数据库操作或部署。

## 2026-10-17：权限 P0 候选修正已通过集中审查与局部复查

[单份交付报告](../development/evidence/WP02-03/current-permission-consumption/DELIVERY.md)记录当前主体/tenant绑定、启用角色和实时权限读取。集中审查在原候选发现真实2FA pending可通过的P0，修正提交 `add3373edd1c8d751a9ff2cd32079b4a96b274c1` 已在当前权限边界拒绝用途标记并保留合法正常/平台/impersonation/OIDC访问；原审查者仅复查修正影响，最终Ready to merge: Yes。

修正前新增回归6失败/21通过；修正后当前权限、真实过滤器及权限服务相关37项通过，0失败/跳过。原候选未受影响的原生PostgreSQL及相关行为按原报告复用，失败全部保留；没有将旧结果称为重新执行。共享状态已准备正常串行合并，#245仍Open直到远端包含性和原步骤证据核对；完整WP02-03仍PLANNED/NOT_CHECKED、业务NOT_RUN，24/375完整任务与0/125业务目标不变。

公共核心基础优先顺序已批准；#38独立窗口仍处于准备，B/C/D普通集成暂停。没有Actions、工作流变化、发包、既有数据库操作或部署。

## 2026-10-17：权限候选审查阻断与限定修正

[已批准基础计划](../development/planning/20261017-foundation-priority.md)登记 #245 候选的唯一 P0 审查问题：两种真实 2FA pending 令牌能通过当前权限读取。COORD 内唯一代理在独立分支/worktree接管三文件修正，原 A 候选与证据保留、不得并行追加；实施在登记发布后开始。只补用途拒绝与相关真实令牌回归，原审查者局部复查，完整任务/业务验收及普通B/C/D暂停状态不变。

## 2026-10-17：公共核心基础优先已获批准

按[已批准安排](../development/planning/20261017-foundation-priority.md)先处理P0：#245候选集中审查及#38独立修复窗口准备；普通B/C/D候选保留。随后按真实依赖收口核心租户/身份/共同事务，用首个CRM入口验证基础，近期滚动细排，不承诺未经估算的日期。

## 2026-10-17：全项目盘点与 P0 优先安排

[全项目盘点](../development/planning/20261017-project-inventory.md)核对 14 工作包、125 目标、375 任务；用户选择先打通一条可验收业务主线。24 DONE、2 IN_PROGRESS、349 PLANNED 不变，局部交付与完整业务验收分别记录。已读取四个 wave13 新对话的真实 START，随后收到四份固定候选 DELIVERY，均尚未总控审查或集成。

A 在原授权范围复现 P0 #245 并报送修复候选，优先审查集成；另有登录 P0 #38 仍 OPEN，源码检查未见所述路径闭环，本次未运行复现。按 BUG 规则暂停 B/C/D 普通集成及新增实施并经 Handoff 通知，保留工作/资源，可只读准备；尚无实际暂停 ACK，不宣称聊天已停止。恢复须协调窗口明确登记，完整任务与目标状态不改变。首条候选主线为公开询盘→受理/审核→线索，完整目标验收及隐私合同保留，日历日期待剩余范围估算。

本次为文档、台账与沟通同步，检查差异、链接和台账一致性；不跑产品测试，不触发/取消 Actions，不改工作流、不发包、不操作既有数据库或部署。

## 2026-10-17：第十三轮四个新对话窗口已登记

[四任务执行入口](../development/dispatch/20261017-wave13/README.md)：A当前授权读取、B服务租户资格control CLI、C真实publish宿主身份验证、D菜单刷新恢复（关联Issue #243）。用户手动粘贴至四个新对话；限定窗口CONFIRMED，尚未收到实际START，未执行新产品实现。COORD独占共享状态与串行合并，各任务不依赖本轮他人的候选。

第十二轮已交付工作不重复。完整任务24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变；A/D完整任务PLANNED/NOT_CHECKED，B/C IN_PROGRESS/CONFIRMED，各自完整前置及未验收范围保留。PostgreSQL、相关本地验证及一次任务级集中审查继续生效。无Actions、正式发包、既有库迁移或部署。

## 2026-10-16：第十二轮真实服务签发局部交付已集成

[241号PR](https://github.com/GTX537/CP6/pull/241)正常合并，远端main `1f52f3bf27cc13a4ad51df3d2b6bcfd0f6810fe1`已核对任务祖先关系及全部范围文件。实际 client_credentials HTTP 签发在普通租户 PostgreSQL 登录的同一物理事务中读取真实 Sys_Tenants 资格、写入 ServiceToken 并完成提交前核验；FOR SHARE 将停用更新与签发序列化。新鲜隔离库显式选择 service-token-v1，最小权限 helper 仅返回自身资格，签发令牌可在同库真实自撤销；注册时签名快照覆盖首次 DI 解析，旧配置与旧 profile 保持原支持范围。

实现、一次集中审查和原始验证见[单份报告](../development/evidence/WP02-02/service-issuance/DELIVERY.md)，固定任务Handoff已发布。限定窗口CLOSED，完整WP02-02仍IN_PROGRESS/CONFIRMED。完整 WP02-02 仍 IN_PROGRESS/CONFIRMED；Full17PGT、全 Main、普通用户/部门登录与 snapshot、真实租户创建/维护、bootstrap/dispatcher、ERP/ordinary outbox、其他Owner/上下文及跨仓宿主、在线授权/配置/密钥/凭据更新、真实业务验收、既有库迁移、环境切换及 DEV/UAT/PROD 均 NOT_RUN。本轮仅完成 fresh-only 五表服务端点采用，不构成全 Main 启用或生产发布。

复用候选有效验证，不因合并重跑产品测试。24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变，A/C/D无新窗口；无Actions触发/取消、工作流变化、发包、既有库迁移或部署。

## 2026-10-16：第十二轮服务签发候选已核验

实际 client_credentials HTTP 签发在普通租户 PostgreSQL 登录的同一物理事务中读取真实 Sys_Tenants 资格、写入 ServiceToken 并完成提交前核验；FOR SHARE 将停用更新与签发序列化。新鲜隔离库显式选择 service-token-v1，最小权限 helper 仅返回自身资格，签发令牌可在同库真实自撤销；注册时签名快照覆盖首次 DI 解析，旧配置与旧 profile 保持原支持范围。

本地 .NET8 / PostgreSQL18.6 相关构建通过。原始第1轮未受影响的94项旧行为、后续目录/资格边界与双向并发锁场景按报告范围复用；第6轮首次解析签名快照及旧Crypto定向14/14通过。审查修正后PUBLIC权限、合法数据库/login、真实HTTPS签发→记录→自撤销链三项定向回归各1/1通过、零跳过；最终新profile与原旧profile的CLI同请求恢复成功，旧baseline digest保持。原始失败、两项红测和information_schema主动扩范围导致的首次修正失败全部保留；最终权限补充范围收敛为pg_catalog / pg_*。COORD核读原始TRX/日志并确认自有55436已停止；未因提交或合并重跑未受影响测试。

完成一次完整SPEC/QUALITY集中审查，发现PUBLIC系统目录有效权限漏检及全串schema替换误改合法数据库/login两项P2。均已修复：新profile按PG原生初始权限拒绝额外PUBLIC授权，规范化seal支持权限还原后同请求恢复；固定schema插值保留合法外部标识符。原审查者仅复查127abecb..1722b061四文件修正及相关原始证据，最终Ready to merge: Yes，无剩余阻断项。COORD核对允许路径、完整diff检查、失败/成功证据及旧profile兼容；旧seal、实体/EF模型/历史迁移和依赖配置未变。

详见[单份报告](../development/evidence/WP02-02/service-issuance/DELIVERY.md)。当前等待正常PR合并及远端核对，尚未记作远端完成。完整 WP02-02 仍 IN_PROGRESS/CONFIRMED；Full17PGT、全 Main、普通用户/部门登录与 snapshot、真实租户创建/维护、bootstrap/dispatcher、ERP/ordinary outbox、其他Owner/上下文及跨仓宿主、在线授权/配置/密钥/凭据更新、真实业务验收、既有库迁移、环境切换及 DEV/UAT/PROD 均 NOT_RUN。本轮仅完成 fresh-only 五表服务端点采用，不构成全 Main 启用或生产发布。 完整任务24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变。未触发/取消Actions、未改工作流、未发包、迁移既有库或部署。

## 第十二轮签名器必要范围补充

已登记[同一实现窗口的单文件扩展](../development/dispatch/20261016-wave12/WP02-02.md)：修复首次DI解析仍依赖可变legacy签名配置的问题。唯一执行者不变，原成功结果按输入范围复用，最后仍为一次集中审查；完整任务及业务状态不变，无Actions或部署。

## 2026-10-16：第十二轮服务签发窗口已登记

[执行计划](../development/dispatch/20261016-wave12/README.md)：已核对最新Main和Handoff无新交付，选择WP02-02真实服务签发/租户有效性/同事务记录为下一轮；唯一COORD实现代理已开始只读准备，准入发布后实施。第十一轮不重做，A/C/D无新窗口。

完整任务24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变；WP02-02仍IN_PROGRESS/CONFIRMED。本条完成范围是派工/资源登记，功能尚未交付。沿用PostgreSQL与精简验证，无Actions、正式发包、既有库迁移或部署。

## 2026-10-16：第十一轮服务令牌自撤销局部交付已集成

[237号PR](https://github.com/GTX537/CP6/pull/237)已正常合并，远端main `222f948dbf8003d99f5b8bd51bde97236e62de2e`已核对任务提交祖先关系及全部范围内文件。实现、一次集中审查及三项安装问题的定向修复证据见[单份报告](../development/evidence/WP02-02/service-revocation/DELIVERY.md)。固定Handoff已发布；本局部窗口CLOSED，完整WP02-02仍IN_PROGRESS/CONFIRMED。

本次沿用候选有效验证，不为合并重复构建或测试。新profile仅覆盖fresh-only有限入口，真实租户活跃查询、其他Owner/contexts、跨仓消费者、在线授权更新、既有库迁移及原17PGT/业务验收仍未完成。24/375完整任务DONE、0/125业务、247 NOT_RUN、41历史引用不变。A/C/D无新窗口；未触发/取消Actions、未改工作流、未发包或部署。

## 2026-10-16：第十一轮服务令牌自撤销候选已核验

真实Basic来源、可信租户普通登录及ServiceToken/Snapshot/generation/优先Outbox同事务接线已完成候选核对，详见[单份交付报告](../development/evidence/WP02-02/service-revocation/DELIVERY.md)。复用原120项相关回归、安装收紧后的8项PG/HTTPS及2项共享guard定向结果，原始TRX均无失败或跳过；审查修复另验证未知布局拒绝后正常安装/恢复、发行身份漂移拒绝与真实HTTPS签发撤销、目录及安装状态漂移拒绝，并通过修复后CLI构建和私有stdin恢复/拒绝/同结果检查。原始开发失败、审查红测和夹具/断言纠正记录均保留，未重跑整套或把复用改称新执行。

一次集中SPEC/QUALITY审查发现3项P2：恢复缺目录行核验、发行角色未完整纳入身份校验、fresh安装缺系统schema未知对象拒绝。修复已在f315425b完成；同一审查者wave11_review仅复查8文件修改及其影响，核读定向红/绿TRX与CLI原日志，确认3项均关闭、旧空布局SQL原样提取且旧seal SQL未变；最终Ready to merge: yes，无剩余阻断项，仅适用于本轮有界交付。当前同步共享状态，随后正常PR合并并核对远端；尚未把候选记为远端完成。本次只采用服务令牌自撤销的可信来源、有限PostgreSQL写集及同事务链路；fresh-only安装不是完整Main数据库。真实租户活跃查询、其他Owner/contexts、普通快照与Outbox、ERP/dispatcher、跨仓消费者、在线授权更新、既有库迁移及原17PGT/业务验收仍未完成。 完整任务仍24/375 DONE、业务0/125、247 NOT_RUN及41历史引用。未触发/取消Actions、未改工作流、未发包或部署。

## 2026-10-16：第十一轮由协调内单一执行代理接续

用户要求把继续推进设为目标并允许范围内自主判断。COORD再次核对Main/Handoff，原B尚无START、分配工作树干净，现由当前协调对话内的唯一实现代理接续B / WP02-02窗口；实际资料读取已开始，代码实施在本次执行身份登记发布后开始。旧B对话恢复时应读取最新台账，不重复领取或并写该工作树。

沿用[第十一轮执行包](../development/dispatch/20261016-wave11/README.md)的全部合同、写入/资源边界、PostgreSQL及精简验证要求。24/375完整任务DONE、业务0/125、247 NOT_RUN保持；此登记未交付功能，不新增Actions、发包、既有迁库或部署权限。

## 2026-10-16：第十一轮自撤销接线窗口已分配

[第十一轮执行包](../development/dispatch/20261016-wave11/README.md)：已核对远端 Main、Platform 与 Handoff，第十轮之后无新交付。B / WP02-02 独占服务令牌自撤销的真实认证来源、有限 Core 写集隔离与同事务接线；限定窗口 CONFIRMED，尚未收到实际 START。A/C/D 本轮无新窗口，原有交付保留。

完整任务仍24/375 DONE、业务0/125、247 NOT_RUN、41历史引用。WP02-02仍IN_PROGRESS/CONFIRMED，其他完整状态/依赖不变；basis v1.2无需重复采纳，独立 verifier 未配置仍如实保留。仅派工文档、台账与资源检查；未运行产品测试、Actions或数据库，未发包、迁库或部署。

## 2026-10-16：第十轮三份局部实现已集成

[集成交付](../development/evidence/wave10-integration-20261016/README.md)：PR231/233/232按A→B→D正常合并并核对远端，三局部窗口关闭。字段权限确定性计算、PostgreSQL组合消息存储初始化/恢复、工作台数据状态已入主线。复用各固定候选相关验证与一次集中审查；仅补台账/链接/差异及远端核对，没有重复产品测试或构建。

WP02-03/WP02-18完整任务仍PLANNED/NOT_CHECKED，WP02-02仍IN_PROGRESS/CONFIRMED；真实授权与Owner写集、后端租户/撤权、原17PGT和业务验收未提升。24/375 DONE、0/125业务、247 NOT_RUN及41历史引用保持。未触发/取消Actions、未改工作流，未发包、迁库或部署；没有自动分配下一轮。

## 2026-10-16：第十轮 WP02-18 局部实现接收

Dashboard真实零、无权、失败与旧响应隔离已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-18/dashboard-states/DELIVERY.md)。复用固定候选16项定向前端测试、vue-tsc及Chrome桌面/390px手机API夹具检查；已读取最终日志，集中SPEC/QUALITY无阻断项。夹具早期失败保留，未称真实业务E2E。

当前同步状态并按 PR #232 串行合并，远端包含性由 COORD 合并后登记。只交付现有客户端状态与响应竞态；legacy后端KPI/全局缓存、scoped查询、租户/撤权、菜单CAS及20条原业务AC未验收。完整WP02-18保持PLANNED/NOT_CHECKED，WP02-03前置保留。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第十轮 WP02-02 局部实现接收

PostgreSQL消息存储初始化、追加与原结果恢复已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-02/provisioning/DELIVERY.md)。复用固定候选CLI Release build7及真实PG18.6 attempt7（单个组合场景、零失败/跳过），已读取原始构建/测试日志与TRX；一次集中审查及修改部分复查PASS。原失败保留，旧RLS/池/RowVersion证据按原范围复用。

当前同步状态并按 PR #233 串行合并，远端包含性由 COORD 合并后登记。工具只支持明确选择的空隔离库与erp-identity-messaging-v1，使用静默初始化窗口；真实可信上下文、业务写集、Owner/WebApi接线及原17PGT仍未验收。完整WP02-02保持IN_PROGRESS/CONFIRMED。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第十轮 WP02-03 局部实现接收

字段权限真实存行MIN与逐用途决策已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-03/field-decisions/DELIVERY.md)。复用固定候选报告记录的14项定向测试、必要编译及一次集中SPEC/QUALITY审查；组件没有接入真实授权链，未称本次重跑。

当前同步状态并按 PR #231 串行合并，远端包含性由 COORD 合并后登记。真实授权来源、registry/映射签认、当前版本、撤权fence、缓存与Owner接入未验收；完整WP02-03保持PLANNED/NOT_CHECKED，WP02-02前置保留。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第十轮限定实现已分配

[执行包](../development/dispatch/20261016-wave10/README.md)：A 接 WP02-03 字段权限决策；B 继续 WP02-02 PostgreSQL 初始化/恢复；D 接 WP02-18 工作台数据状态。三个独立分支复用已合并且干净的工作树，写入范围已分开；尚未收到实际 START。C 等真实消费制品与数据库边界稳定，不重复做身份检查或新增信任门。

完整 WP02-02 保持 IN_PROGRESS/CONFIRMED；WP02-03、WP02-18 保持 PLANNED/NOT_CHECKED，仅本轮限定窗口 CONFIRMED。WP01-01、WP02-16 旧窗口 CLOSED；WP02-28 本轮无新窗口。原依赖及历史结果保留，24/375 DONE、0/125 业务、247 NOT_RUN 与41历史引用不变。派工只查差异、链接和台账，不运行产品测试、Actions、数据库或部署。

## 2026-10-16：第九轮四份局部实现已集成

[集成交付](../development/evidence/wave9-integration-20261016/README.md)：PR225/227/226/228 均正常合并并核对远端，四个局部窗口关闭。现有能力包括分阶段信任、Identity/ERP 共用租户事务、安全 CodeGen 预览和实际宿主身份诊断。原定向测试/集中审查按准确范围复用，最终组合补一次 Release 编译通过；未运行新的数据库/业务验收。

A/D 完整任务保持 PLANNED/NOT_CHECKED，B/C IN_PROGRESS/CONFIRMED。独立信任、真实 Owner/宿主业务接线与正向回退兼容仍待完成；24/375 DONE、0/125 业务、247 NOT_RUN 与41历史引用不变。源码、台账及四状态已同步；未触发/取消 Actions、未改工作流，未发包、迁库或部署。

## 2026-10-16：第九轮 WP02-28 局部实现接收

Main 实际运行身份观测与缺证回退拒绝已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-28/DELIVERY.md)。原相关单测36/36通过，尾斜杠修正后的真实 Main/授权撤销/固定503/旧health合同验证通过；未改单测结果复用。三份程序集匹配、两份未加载，整体 Unknown。

当前同步状态并按 PR #228 串行合并，远端包含性由 COORD 合并后登记。无两实际制品与准确 PostgreSQL schema/driver 的正向回退证明；正向兼容 NOT_RUN，正式OBS资格/Current trust/业务Owner未验收。完整 WP02-28 IN_PROGRESS/CONFIRMED，不登记 DONE。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第九轮 WP02-16 局部实现接收

CodeGen 安全即时预览接口与页面已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-16/codegen-preview/DELIVERY.md)。原 CodeGen 25 项、合法名称修正后定向 20 项通过；未改 HTTP/旧接口证据复用。前端类型和实际 Vue/API 夹具页面检查通过，集中审查问题已关闭。

当前同步状态并按 PR #226 串行合并，远端包含性由 COORD 合并后登记。仅 NU04 局部预览；真实租户/Owner、保存 CAS/unknown 恢复、采番事务与文件应用未验收。完整 WP02-16 保持 PLANNED/NOT_CHECKED，WP02-10 前置不变。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第九轮 WP02-02 局部实现接收

Identity 四表租户隔离与 ERP 同事务适配已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-02/identity-priority-tenant-isolation/DELIVERY.md)。原 PostgreSQL18.6 Identity 5/5、受影响 ERP 回归 4/4 通过，真实模型与合成 A/B 数据；其余池/ERP 证据按报告复用。集中审查 PASS，失败日志保留。

当前同步状态并按 PR #227 串行合并，远端包含性由 COORD 合并后登记。真实 IdentitySnapshotWriter/dispatcher/宿主当前授权/Owner、其他上下文与完整生命周期及原17PGT未验收；完整 WP02-02 保持 IN_PROGRESS/CONFIRMED。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第九轮 WP01-01 局部实现接收

采纳与运行信任配置分离已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP01-01/trust-stages/DELIVERY.md)。原 adapter 27 项、manager 8 项通过，旧夹具修正后单项通过；真实只读 manager 结构检查 PASS。复用原有效结果与集中审查，不称此次重跑。

当前同步状态并按 PR #225 串行合并，远端包含性由 COORD 合并后登记。独立维护者的实际 authority registry/公钥/有效签名尚未配置；真实业务运行未验收。用户采纳已决定，不再重问；完整 WP01-01 保持 PLANNED/NOT_CHECKED。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-15：按第九轮窗口并行实施

按[第九轮执行包](../development/dispatch/20261015-wave9/README.md)分别继续 A/WP01-01 信任阶段分离、B/WP02-02 Identity 四表隔离与 ERP 同事务、C/WP02-28 实际宿主身份及回退证据判断、D/WP02-16 无持久副作用预览。独立信任配置、真实 Owner/宿主、完整迁移恢复、业务采番和真实回退等未覆盖范围仍需对应后续任务；本轮局部窗口不抹去原前置。COORD 收交付后串行整合状态与 main，执行者不改共享台账。

## 2026-10-15：ERP 共享存储隔离与 EF 事务接入

已实现 ERP 8 张共享表的受保护登录→租户目录、最小权限 helper 与数据库行隔离，
并让内部 EF 接入器使用租约中的同一连接/事务；现有全局唯一键与只追加审计保持。
10 项相关本地验证通过，零失败、零跳过，使用真实 ErpIntegrationContext 模型和自有 PG18 中的合成 A/B 数据。
详见[本步骤交付](../development/evidence/WP02-02/erp-tenant-isolation/DELIVERY.md)及[8 表写入清单](../development/evidence/WP02-02/erp-tenant-isolation/OBJECTS.md)。
安装限显式调用的新布局；已有对象拒绝接管，重复初始化/升级/恢复仍待完整生命周期任务。
现有 handler/worker/业务路由未切换；真实宿主授权、Owner 最终门、其他上下文映射与原 17 PGT 仍待完成。
WP02-02 保持 IN_PROGRESS / CONFIRMED；24/375 DONE、0/125 业务完成及 247 NOT_RUN 不变。
自有 PostgreSQL 已停止，数据与失败记录保留。本步骤没有 Actions 触发/取消/配置变更、发包、既有业务迁库或部署。
下文保留历史时点；原“没有共享 RLS/helper”不代表本轮 ERP 局部组件完成后的状态。

## 2026-10-15：PostgreSQL 租户连接组件

已实现宿主内部的不可变租户绑定、受控目录、专用连接池和显式事务租约；
当前可信 scope、实际数据库/LOGIN、池容量及轮换排空在组件入口检查。
28 项相关本地单元和自有 PostgreSQL 18 验证通过，零失败、零跳过；
集中审查后修正取消异常脱敏，对应 1 项回归通过；原 28 项结果只复用未受影响范围。
数据库只使用本任务合成 A/B 表，自有实例已停止，数据及失败记录保留。
详见[本步骤交付](../development/evidence/WP02-02/tenant-connections/DELIVERY.md)。
四个实际 EF 模型的 328 张表已形成可携带清单，供后续映射使用；其中 317 张有直接租户列的表中，
307 张至少有一个未含租户列的唯一键，不能直接复制到各租户 schema 而削弱原唯一性。
完整 raw SQL/writer/Owner 清单、真实宿主授权、Owner 最终门、共享 RLS/helper、EF 缓存与映射及迁移恢复仍待完成。
组件尚未接入业务路由；WP02-02 保持 IN_PROGRESS / CONFIRMED，原 17 PGT 和业务验收保持 NOT_RUN。
24/375 DONE、0/125 业务完成及 247 NOT_RUN 不变。本步骤无 Actions 触发/取消/配置变更、发包、业务迁库或部署。
下文保留历史时点；旧“尚未实现租户连接”不代表本轮之后的组件状态。

## 2026-10-15：Main 正式采用 Platform 0.10.3 依赖

Main 的四处直接引用和 16 份真实恢复锁已更新，WebApi / 数据库探针构建通过，
CRM、ERP 合同与 PostgreSQL 模型的 151 项相关测试通过、零失败、零跳过。
正式包身份已核对并形成空能力/兼容性矩阵与 Unknown consumer lock；没有虚构运行采用。
详见[本步骤交付](../development/evidence/WP02-02/main-package-adoption/DELIVERY.md)。
WP02-02 从 BLOCKED 转为 IN_PROGRESS / CONFIRMED；本步骤后继续可信租户绑定和 PostgreSQL 存储，
真实 Owner、数据库事务/隔离与原 17 PGT 仍 NOT_RUN，完整任务未记 DONE。
仍为 24/375 DONE、0/125 业务完成；本步骤无 Actions、发包、数据库启动、迁移或部署。
下文保留历史时点；旧“Main 仍为 0.10.2”不代表本次采用后的当前源码。

## 2026-10-15：用户采纳新验收依据并批准一次正式发布

用户明确回复“同意两项”：采纳WP01-01-ACCEPTANCE-BASIS-1.2.0的579条前瞻义务，
并批准准确Platform源码1277da5e0a20c6d96f0a95d0e9b4f20e0008332a的一次0.10.3七包正式发布。
[授权与执行报告](../development/evidence/authorized-adoption-release-20261015/README.md)记录准确范围。
台账已选择原manifest；用户采纳决定已生效，独立信任配置尚未安装，业务仍NOT_RUN。
正式运行38709310767/attempt1成功；七包原字节、Windows/Linux Current结果和归档摘要已核对。
正式包缺失阻塞已解除；Main仍为0.10.2，consumer matrix/lock及真实PG采用留待下一窗口。
24/375 DONE及0/125业务状态保持；没有重跑、触发器变更、Main接线、迁库或部署。
下文“尚未授权”等陈述保留为相应历史时点，不代表本次授权后的现状。

## 2026-10-15：验收配置异常诊断修复（Issue #216）

验收工具先检查配置为JSON对象；数组和标量现在返回结构化拒绝，不再抛出AttributeError。
相关23项算法测试及原步骤复测通过，正常配置与拒绝边界保留；正式采纳和业务状态不变。
见[修复证据](../development/evidence/bug-216-configuration/README.md)；Issue在远端main核对后关闭。
未运行Actions、签名发包、数据库或部署。

## 2026-10-15：第八轮限定实现已合并

WP01-01正式台账adapter经PR217交付；WP02-02的Platform0.10.3登记经[PR](https://github.com/GTX537/CP6.Platform/pull/77)交付。
协调以独立PR76修复Pkcs审计阻塞，并修正两条过时断言；标准定向177项通过，其他有效结果按原范围复用。
两个局部窗口已关闭，完整任务仍PLANNED/BLOCKED与NOT_CHECKED；正式采纳、签名/发包、真实Main/PG及17PGT尚未完成。
24/375 DONE、业务0/125、247 NOT_RUN、41 REFERENCE_ONLY及历史缺件保持。
最终producer源码为`1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`，不是已发布制品。详见[本轮协调报告](../development/evidence/wave8-integration-20261015/README.md)。
未触发/取消Actions、改变触发器、迁库、部署或新增派工。下文保留历史时点。

## 2026-10-15：优先推进实现

A/B按[精简后的并行约定](../development/09-parallel-development.md)继续已批准窗口；B的三入口/私有宿主兼容验证留待实际消费阶段。后续任务默认相关验证加一次集中审查，不新增重复准入、测试数量或报告数量要求；真实业务与发布尚未满足的条件保持待办。

## 2026-10-15：第八轮正式门与版本登记窗口

第八轮只开放A/WP01-01正式台账adapter/manager及完整采纳记录候选、B/WP02-02在Platform的0.10.3正式登记代码窗口。两个局部窗口CONFIRMED；完整任务仍PLANNED/BLOCKED与NOT_CHECKED，未收到START，不代表实际采纳或业务开工。C/D旧窗口关闭且不新增派工，WP02-28等待可信producer。24/375 DONE、S01 12/12、业务0/125、247 NOT_RUN、41REFERENCE_ONLY及历史缺件保持。无Actions、正式签名/发包、数据库/业务迁库或部署。 [执行包](../development/dispatch/20261015-wave8/README.md)；[准入证据](../development/evidence/wave8-dispatch-20261015/README.md)。下文保留历史时点。

## 2026-10-14：第七轮两项库实现完成、两项准备归档

WP02-12消息传输与WP02-26 SLO/精确断言/故障工具经集中审查、正常合并和远端核对登记DONE。累计24/375任务，S01为12/12；业务目标0/125、247运行NOT_RUN和41历史REFERENCE_ONLY保持。原始本地证据按准确输入复用，合流只做必要架构冒烟，详见[第七轮协调证据](../development/evidence/wave7-integration-20261014/README.md)。

WP01-01技术候选修复Issue211的六实体原始Id约束缺口后归档，主任务仍PLANNED/NOT_CHECKED，生产门与正式采纳关闭。WP02-02三入口完整依赖构建及私有宿主诊断通过，源码采用候选可审查；正式信任/生产采用尚未批准，主任务仍BLOCKED/NOT_CHECKED，17PGT仍NOT_RUN。前轮准备记录和无法恢复的原件缺口保留。

自有容器/PG已停止并保留数据/失败；协调未重启。无Actions运行/取消/触发变更、正式发包、业务迁库、部署或下一轮派工。下文保留历史时点。

## 2026-10-14：第七轮传输、验收工具与准入候选

第七轮沿用四对话：A/WP01-01完成DP决定候选与禁用完成门验证，B/WP02-02完整依赖构建/运行诊断；两主任务仍PLANNED/BLOCKED及NOT_CHECKED。C/WP02-12消息传输与D/WP02-26 SLO/故障注入READY/CONFIRMED。四独立窗口、旧准备交付和领取历史保留，未收到实际START。22/375 DONE、S01 12/12、业务0/125及247 NOT_RUN不变。只预约自有本地资源，本次未启动服务、数据库、Actions或正式发布/迁库/部署。 [执行包](../development/dispatch/20261014-wave7/README.md)及[准入证据](../development/evidence/wave7-dispatch-20261014/README.md)。下文保留历史时点。

## 2026-10-14：第六轮两项实现交付、两项准备归档

WP02-08 Outbox V2、WP02-25运行身份/健康/遥测经集中审查、四项P2修复和定向回归后正常合并并核对远端main，登记DONE。累计22/375，S01为12/12；业务目标仍0/125。初审缺陷、独立Issue分支、真实RED/GREEN、旧证据复用及共同主线架构冒烟详见[第六轮协调证据](../development/evidence/wave6-integration-20261014/README.md)。

WP01-01准备包与WP02-02消费准备包已合并：前者仍PLANNED/NOT_CHECKED，正式替代依据采纳及DP01–08待决；后者仍BLOCKED/NOT_CHECKED，旧正式包缺新能力、本地包未正式信任，17 PGT仍NOT_RUN。准备归档不计主任务DONE，不改变完成门、产品依赖或可信版本。247运行NOT_RUN、历史原件缺失及WP05 PROPOSED保持。

Outbox仅重启核实身份的专属PG55428用于缺陷回归，收尾停止并保留证据；无业务库操作。无Actions触发/取消/配置更改、正式发包、迁库、部署或新一轮派工。下文保留历史时点。

## 2026-10-13：第六轮两项实现与两项准入准备

第六轮沿用四对话：C/WP02-08 Outbox与D/WP02-25观测READY/CONFIRMED；A/WP01-01替代验收采纳准备仍PLANNED、B/WP02-02消费身份准备仍BLOCKED，两主任务NOT_CHECKED，只授予私有准备目录。未收到START，不把assignment当开工。20/375 DONE、S01 12/12、业务0/125及247 NOT_RUN不变。C仅预约新PG55428，本次未建库、触发/取消Actions、改触发器、正式发包、迁库或部署。 [执行包](../development/dispatch/20261013-wave6/README.md)及[准入证据](../development/evidence/wave6-dispatch-20261013/README.md)。下文保留历史时点。

## 2026-10-13：第五轮四项任务已交付，S01 基础合同收口

S01-12、WP02-27、WP02-07、WP02-11 已经集中审查、必要修正、正常 PR 合并及远端 main 核对，登记 DONE。累计20/375项，S01为12/12；真实业务目标仍0/125。

S01-12整理122向量、19修订和六组消费者责任，完整业务仍NOT_RUN。Inbox复用136个独立本地/隔离PG测试及4+2定向重复证据。WP02-27补齐两项精确资产断言，并修复Schema路径边界Issue60；HTTP修复拒绝响应正文故障与权限回调异常Issue62/63，保留真实失败和新回归。新运行、旧证据复用和共同主线架构冒烟详见[本轮协调证据](../development/evidence/wave5-integration-20261013/README.md)。

WP02-02仍被可消费的受信任包/源码身份阻塞；当前Main固定[0.10.2]不含新能力，本地测试包不解除门禁。下一步先明确采用身份及17项PG验收准入，再分配后续任务；本次未派新任务。247运行NOT_RUN、历史缺口和WP05 PROPOSED不变。无Actions运行/取消/触发变更、正式发包、迁库或部署。下文保留历史时点。

## 2026-10-13：第五轮四任务准入与租户采用制品门

第五轮沿用四对话分配S01-12采用证据、WP02-27包能力与信任、WP02-07同事务Inbox、WP02-11 HTTP安全重试，均READY/CONFIRMED，未收到START。WP02-02因Main可消费的可信ctx-binding制品缺口登记BLOCKED；WP02-27本地验证不自动解除发布/采用门。16/375 DONE、S01 11/12、业务0/125及247 NOT_RUN保持。C仅预约隔离PG测试资源，本次未建库、触发/取消Actions、更改触发器、迁库或部署。 [执行包](../development/dispatch/20261013-wave5/README.md)及[准入证据](../development/evidence/wave5-dispatch-20261013/README.md)。下文保留历史时点。

## 2026-10-13：第四轮四项基础任务已交付

S01-11 ERP授信合同、S01-10 PostgreSQL精确存储、WP02-01可信上下文、S01-07质量合同均经正常PR合并与远端main核对，登记DONE。累计16/375，S01为11/12；业务目标仍0/125。

S01-11在准入审查中修复Buffer字节别名Issue196，新TS428/428、类型检查和原步骤复测通过，原C#417/设计5复用。S01-10正式采用PG映射1.0.0及20字段无损数值尺度补充，政策尺度保持；原PG210+重叠57和生命周期1准确复用。WP02原502复用、新架构98通过；S01-07原657/130与类型检查复用。未执行整个合并组合或真实Owner/宿主/PG业务验收。

[完整协调证据](../development/evidence/wave4-integration-20261013/README.md)保留原候选、失败和历史PROPOSED身份；247运行NOT_RUN、ITER/REL缺口、WP05 PROPOSED不改。下一步可核S01-12及WP02后续任务准入，本次未派新任务。无Actions运行/取消/触发变更、迁库或部署。下文保留历史时点。

## 2026-10-13：第四轮四项任务已分配

沿用A/B/C/D分配S01-11 ERP-CREDIT、S01-10 PostgreSQL精确存储、WP02-01可信上下文、S01-07质量合同；均READY/CONFIRMED，独立工作树和写入窗口已核，尚无实际START。12/375 DONE、S01 8/12和业务0/125不变，247运行NOT_RUN。S01-10只预约隔离本地测试资源；本次未操作数据库、触发/取消Actions或部署。[执行包](../development/dispatch/20261013-wave4/README.md)与[准入证据](../development/evidence/wave4-dispatch-20261013/README.md)。

## 2026-10-12：第三轮四项合同任务已交付

S01-03授信金额、S01-04库存值/比例、S01-06 ATP标识及S01-08 CRM合同，均经审查、所属仓库正常合并及远端main核对，登记DONE并关闭本轮窗口。任务累计12/375（S01为8/12），不是工时或业务百分比。Main复用准确输入对应205/132/60+81及类型检查；CRM发现终态合同/Node环境阻塞后，以有版本的来源补充修正，实际C#186、Node22 Web142、importer6与类型检查通过，49原UI接受并往返。两仓回执与回归支持Issue189关闭，原候选/失败记录保留。

[完整协调证据](../development/evidence/wave3-integration-20261012/README.md)明确原运行复用与本轮新验证的范围。125业务目标仍0、247运行NOT_RUN及历史ITER/REL缺口保持；HTTP/Owner/PG采用另行推进。未自动分配下一轮，无Actions运行/取消/触发变更、迁库或部署。下文保留历史时点。

## 2026-10-12：CRM 终态合同漏同步修补（Issue #189）

第三轮接收发现 CRM 候选按旧 Schema 要求 99 终态公开到期时间非空，与已接受 Wire 修订冲突。新增明确版本 [CRM-INQ-TERMINAL-PROJECTION/20261013-R1](../design-handbook/remediation/addenda/CRM-INQ-TERMINAL-20261013-R1/README.md)，仅补齐两个遗漏节点；原归档、组合资产、输入包和固定向量均保留。静态检查及定向审查通过，真实消费者采用尚待完成。

[Issue #189](https://github.com/GTX537/CP6/issues/189) 保持 Open，S01-08 暂不登记 DONE；需要显式版本采用、根与页面嵌套 99 时序回归及受支持 Node 22 环境验证。来源修补不代表 HTTP、授权、数据库或业务验收完成。共享台账将在本轮协调交付统一同步；无 Actions 运行、取消、触发变更、迁库或部署。下文保留历史时点。

## 2026-10-12：第三轮四项合同任务已分配

沿用A/B/C/D分别分配S01-03授信金额、S01-04库存值与比例、S01-06 ATP稳定标识、S01-08 CRM原生合同。均READY/CONFIRMED，真实独立工作树已建，等待实际START；8/375 DONE与0/125业务验收不变。CRM发布前置独立完成普通工作流仅manual及严格既有批准附注兼容。未运行Actions、数据库迁移或部署。[任务包](../development/dispatch/20261012-wave3/README.md)及[证据](../development/evidence/wave3-dispatch-20261012/README.md)。

## 2026-10-12：第二轮三项合同代码已交付

S01-02严格JSON/NullOnly/结构相等、S01-05前端精确值、S01-09平台准确路由/原字节均经独立集中审查及各自远端main包含性核对，登记DONE并关闭本轮写入窗口。复用准确源码对应的77/202/110项成功结果及前端类型检查，没有把证据复核改称新测试运行。任务总计8/375（计数，不是工时或业务完成率），业务目标仍0/125，247运行NOT_RUN及历史ITER/REL缺口保持。

Main PR184/185、Platform PR58分别交付；平台包尚未发布，真实Owner/数据库/页面采用与生产验收仍独立进行。下一轮可核对S01-03/04/06/07/08/11，未自动领取。无Actions触发/取消/配置变更、迁库或部署。 [完整协调回执](../development/evidence/wave2-integration-20261012/README.md)。

## 2026-10-12：第二轮代码候选接收与协调审查

COORD已实际读取S01-02、S01-05、S01-09的START和DELIVERY，三项登记VERIFYING；不回填此前协调ACK。候选分别报告77、202、110项本地检查，本轮独立核对实际源码、固定向量及测试证据后再正常保护合并。共享状态串行维护，远端main包含前不登记DONE。业务目标0/125及247运行NOT_RUN保持。 [固定接收记录](../development/evidence/wave2-integration-20261012/intake.json)。

## 2026-10-12：WP01-02派工设计交付确认

WP01-02经PR182及真实远端回执登记DONE；A/B/C的S01-02、S01-05、S01-09保持READY并等待实际START，D本轮未分配。共享台账继续由COORD串行处理；业务目标/运行状态及WP05采纳未提升。 [交付与复用](../development/evidence/WP01-02/README.md)。

## 2026-10-12：第二轮三个代码任务准入

COORD接手WP01-02汇总/派工；沿用A/B/C分别分配S01-02、S01-05、S01-09，真实独立工作树已创建，当前READY并等待START，不宣称已经开工。D保留原验收准备。本轮未写业务实现，未运行Actions/迁库/部署；业务目标和运行场景状态不变。 [任务包与写入窗口](../development/dispatch/20261012-wave2/README.md)。

## 2026-10-12：首轮四任务远端交付确认

首轮四项设计/准备均有真实远端交付：S01-01保持DONE，S02-00、WP05-01、WP14-01依据独立审查、修正及PR178/179/180回执登记DONE，资源窗口关闭。S02补齐UPDATE双约束；WP05补43项来源映射仍PROPOSED。业务目标0/125、247运行NOT_RUN及两历史缺口保持；未运行Actions、迁移或部署。后续候选需另行核对与领取。 [完整协调回执](../development/evidence/first-wave-20261012/README.md)。

## 2026-10-12：WP05-01 设计准备接收与集中审查

本任务由COORD核查候选、集中审查及必要定向修正，串行同步共享台账后正常保护合并；远端main实际包含后另据回执登记DONE。ITER/REL版本化替代依据与覆盖矩阵；不可恢复旧30/48仍保留缺口，新依据的正式采纳和WP01-01完成门独立处理，运行NOT_RUN。 [接收依据](../development/task-deliveries/WP05-01/evidence/coordination/intake.json)。本轮不改变125目标交付状态、原清单缺口或247运行场景；无Actions、数据库迁移或部署。

## 2026-10-12：WP14-01 设计准备接收与集中审查

本任务由COORD核查候选、集中审查及必要定向修正，串行同步共享台账后正常保护合并；远端main实际包含后另据回执登记DONE。125目标、13404原验收关联、247修订场景及599消费者的验收准备；当前源子集未变，保留固定快照，所有运行NOT_RUN。 [接收依据](../development/deliveries/WP14-01/evidence/coordination/intake.json)。本轮不改变125目标交付状态、原清单缺口或247运行场景；无Actions、数据库迁移或部署。

## 2026-10-12：S02-00 设计准备接收与集中审查

本任务由COORD核查候选、集中审查及必要定向修正，串行同步共享台账后正常保护合并；远端main实际包含后另据回执登记DONE。共享 schema + 租户业务 schema、专用角色/连接池与事务恢复设计；27个运行场景保持NOT_RUN。协调审查补齐UPDATE旧行USING及PGT-008反例。 [接收依据](../development/evidence/S02-00/coordination/intake.json)。本轮不改变125目标交付状态、原清单缺口或247运行场景；无Actions、数据库迁移或部署。

## 2026-10-12：S01-01 交付与后续领取

S01-01由对话A兼协调完成，经PR #176及远端main核对后已登记DONE。后续S01-02严格JSON、S01-05 TypeScript精确值、S01-09 Platform版本路由可基于[准确包版本](../../eng/contracts/s01/README.md)各自重新核对准入，当前未领取。

其他首轮任务仍按唯一台账登记，不据本任务完成自动放行共享模型、Owner事务或数据库迁移。全部业务验收状态保持，原历史缺件不重启补找。

## 2026-10-12：从 Handoff 收取首轮启动与交付

按[通信入口](../development/09-parallel-development.md)在协调工作开始时读取 S01-01、S02-00、WP05-01、WP14-01 专属资料分支的新消息。实际读取 START 后核对分支/worktree、前置及资源，串行登记唯一台账并回传准确提交；收到交付后按业务仓验收和远端包含性登记状态。未收到或未读取的消息不得假设已确认，休眠对话需恢复后读取；不要求用户逐条搬运普通报告。

## 2026-10-12：按全量任务卡领取首批任务

协调对话从[首轮四任务](../development/09-parallel-development.md)登记准确领取：S01-01、S02-00、WP05-01、WP14-01。它们可以独立准备；实际开工仍核对来源、当前主线、共享写入窗口、assignment与entry_gate。WP01-02在首轮交付后汇总，不是首次派工的前置。

后续从[全部任务卡](../development/10-work-package-tasks.md)选择已满足直接前置的任务；商业、工程、计划执行、仓储、财务和OA/SPACE按真实提供者/消费者分别交付。涉及跨仓采用，核对各仓远端main及包/合同身份；S02-00先决定PostgreSQL共享/租户布局，不自动切换既有库。新场景的实例证据仍需真实Owner提供，不能因提供者接口已存在便启用未采用业务。

PLM-ITER/REL不可找回原件通过WP05-01设计明确替代，再由WP01-01落实采纳规则与完整覆盖；保持历史缺口，不补造旧表。所有目标须全部主体实现、原/修订验收和实际远端交付回执满足后关闭；工作包或任务计数不代业务完成率。未经新授权不运行Actions、不迁移真实业务数据、不部署生产。

## 2026-10-11：按细分任务开工，统一PostgreSQL

从[任务表](../development/07-detailed-tasks.md)选择S01-01，然后S01-02/08推进CRM合同；所需消息接线完成S01-09，真实Owner事务先满足S02-00多租户布局及S02事务/授权门。财务金额/数量与ERP贷项profile分开，按消费者需要接入，不等待所有财务任务才推进CRM。

S02-00优先评估schema多租户，写清共享数据、TenantId映射、数据库权限、连接池、EF模型缓存、迁移历史、逐租户升级和恢复；设计验收用例保持NOT_RUN。所有后续新增存储/验证只针对PostgreSQL，不用SQL Server规则约束新实现，不在当前任务删除旧数据/兼容代码。

## 2026-10-11：按WP01结果展开首批实施

从[首批切片S01](../development/06-first-slices.md)展开精确值/身份采用的有限任务，固定实际消费者、兼容边界和验收向量后编码；随后S02真实事务/当前授权门、S03公开询盘及H02目标Owner。工程/库存/财务/OA/SPACE按实际依赖提前配套，不等待整包串行完成。

PLM-ITER/REL原验收历史缺口不能找回，S00补订明确版本的替代验收及覆盖依据；完成审查与台账规则修订前保持NOT_INVENTORIED，WP01仍ASSESSING，不复发补找请求。其他完整来源切片可继续。已有固定源码结论105改造/20新增，不再使用下方旧“125目标待评估”快照作为现状。

## 2026-10-11：按全量大任务评估安排开发

从[开发总台账](../development/README.md)和[批次准入](../development/04-batches.md)接续。先推进WP01：逐目标固定代码和原验收清单，确认可复用、需改造、需新增及待决策范围；依据真实依赖选首批切片。后续公共基础、CRM/ERP、工程、计划/执行、财务与共享消费者按切片所需能力接入，不预设所有模块都从零重写。

247条仅是修订运行场景，必须与原模块验收一起管理；已有实现和历史通过均保留真实适用范围。每项实际任务同步roadmap.json与生成进度页，远端包含性和必要验证确认前不记DONE。扩展功能须明确批准范围，历史不可找回原件继续保留缺件记录；活动BUG不复制到本台账。

## 2026-10-11：设计修订后的编码交接

当前设计问题按[统一处置表](../design-handbook/remediation/00-disposition-register.json)解释，下方2026-10-10记录是修订前历史时点。先固定本轮补充及原合同准确SHA，再按[交接顺序](../design-handbook/remediation/09-implementation-handoff.md)实现值类型/身份、共同事务门、商业及财务接口、材料映射、共享消费者和完整回归。

- 32项已有设计待实际采用须逐一证明真实provider/consumer/native schema与所有writer最终门；其余设计已处置项也不自动获得运行准入。
- 247条业务/采用验收尚未执行，覆盖金额精度、数量守恒、并发锁序、重复提交、取消与迟到、撤权和审批重放；静态/编译检查不能替代这些验收。
- 数据迁移、主写者范围切换、环境发布另立任务，禁止把新DEMO政策批准、H30 seal、Store或工程证据冒充生产事实。
- R01071不可找回，补找行动已关闭，保留历史证据限制；设计台账不是活动BUG队列，真实可复现BUG仍在GitHub Issues管理。

## 2026-10-10：本地设计整理后的编码准备

- WMS-DEL指定独审R01071已于2026-10-11由用户确认无法找回，资料补找行动关闭，不再索取；保留历史评审缺口并在实施时执行适用审查与验收。准确身份：`DEL_INDEPENDENT_R3_REVIEW_c30b6885.md`，3,461字节，SHA `4a30103ef981273ecd9e0d920d4b798e19ca1724850a34413c09a8b25b16621f`；其他历史/受阻缺件见[缺件与集成待核](../design-handbook/evidence/资料缺口与集成待核.md)。四份已找回评审不重复查找。
- 按具体用例冻结准确主文/附件/采用组合，澄清当前静态字段、类型、单位/坐标和共同事务差异，再进入代码实现；设计NOT_RUN/UNPROVEN不能转为运行通过。
- 本地整理产物已形成125模块（124必要材料闭合，DEL已定位内容闭合）；用户已明确授权将整理成果上传PUBLIC仓库；按正常PR保护合并并核对远端，原21.5 GB档案与展开对象仍留本地，不能把本地提交称为远端完成。

## 2026-10-04：项目地图后续维护

- 本版地图固定在ebcbc499；结构、数据库或发布边界变化时，按[地图维护说明](../architecture/2026-10-04-cp6-map/README.md)核对源码并生成新版本，保留旧版本证据。
- 容器适配另行专项检查：静态发现API Dockerfile在restore前未复制PostgreSQL项目；本次未执行容器构建，不能声称容器链已通过。实际复现确认后按BUG管理规则登记Issue并处理，不在图生成任务混入构建修复。

## 2026-10-04：PostgreSQL 真实数据副本的后续测试

- 用户确认原CP6DB两条历史T_IntegrationEvent.CreateDate的时区后，校正本机副本的UTC派生排序字段；原日期和完整SQL备份已保留，当前UTC仅是测试暂定解释。
- 用户使用原业务账号在独立本地API测试订单、库存、Space等实际流程；本次完整数据对账与API冒烟不冒称所有业务回归、后台调度或外部集成验收。
- PostgreSQL测试副本及SQL一致性备份/SQL升级副本暂留；另获清理授权后只按确切身份逐个操作，不按名字通配清理。

数据搬迁与启动入口见[本地运行手册](../devops/CP6DB-POSTGRESQL-LOCAL-COPY.md)，原SQL CP6DB及既有本地连接未切换。

## 2026-10-04：双库兼容版已交付并通过合并后冒烟

[PR165](https://github.com/GTX537/CP6/pull/165)已正常保护合并到远端main 8b64bc8a510e9ad4a82c21c8c6ac13c138e332c3；候选4243c67d7ac2e3cfafb809d2a4efba54f4ec927a是其ancestor，完整树68f7d4accfc58897a6bb430f5f90dded50c4ba94相同，2,389个PR文件均完成分页与Git blob核对。原洁净实现e940825fc7511b41a1a80b4b73897dcc69e51bbb的双库105个适用入口、当前配置/接线139项、56项HTTP与真实原生独立恢复均通过，所有31个正式库已清理。2,091份原始证据与Git index字节一致，失败及复用来源保持。

合并后使用同一原261文件发布物，SQL/PG分别在新的独占库实际初始化、核对history、启动所属API及三个health端点、登录与12项权限/改密/401/403检查；HTTP新建用户均从确切owned库实际查得。两个新库普通DROP并核对不存在；这是新冒烟，不冒称重建、重跑完整矩阵或生产验收。初次PG冒烟脚本属性读取丢失SSL覆盖的失败保留，纯内存复现后改用正式runner的set/get方法；原Planned目标实际不存在、receipt与Failed summary保持，不改变CP6项目代码或TLS生产规则。155份交付/冒烟原件已逐份核对归档。

六工作包的功能、适用验收和代码交付已闭环，结案证据与运行入口见[最终验收及交付](../audits/database-compatibility/WP6-ACCEPTANCE.md)；父任务活动状态以[Issue134](https://github.com/GTX537/CP6/issues/134)为准。父任务只在本次结案文档正常合入并核对远端完整包含性后关闭，下方历史“未完成/guard保留”等记录不代表当前运行能力。普通PG API/worker现可使用所选Provider；没有现有业务数据搬迁或生产部署。

推送/PR/合并前分别核对17个工作流/依赖输入，队列均0，无远程Actions运行/取消、触发/保护变更或管理员绕过。根工作区用户既有改动保留；正式R2/GHCR候选、签名/扫描、审批与SQL生产基线不变。

## 2026-10-04：WP6 最新本地验收全部通过，远端交付待完成

洁净源码 e940825fc7511b41a1a80b4b73897dcc69e51bbb 的最新 wms-local-transaction-fix 四次实际分项全部Passed：PG Matrix34/34、SQL Matrix37/37、两库Application各17/17，共105个适用入口、零失败/跳过；不是单次Full。当前程序集补充配置/四Context接线/生产校验139/139，零跳过。两库使用同一份当前实际发布API（261文件逐字节一致），真实独立恢复专项核对56项HTTP、8次API启动、所有表/行/序列/history/持久key与恢复后消息一次投递/幂等重放通过；本轮31个owned临时库全部普通DROP且AbsentVerified。

八项完成条件的本地范围已逐项绑定实际原件及断言源位置，旧失败/成功及原审查时点保留。一次集中审查与定向修复/清理/WMS整合复核无剩余实质阻断。WP6公共证据归档/index核对、正常PR/main交付、合并后冒烟与远端包含性仍待完成，父Issue134继续Open；不能因本地全部通过就宣布任务关闭。实际版本、源码/产物哈希、复用来源及范围见[最终本地验收](../audits/database-compatibility/WP6-ACCEPTANCE.md)，下方旧状态均为历史时点。

普通PG API/worker临时guard已移除，仅在同一部署使用所选Provider及连接；旧记录中“guard保留”是当时状态。R2/GHCR正式门禁和SQL生产基线不变，未启动/取消远程Actions、修改触发/保护、切换既有环境或部署生产。已有SQL数据搬迁、同实例混库及外部CRM全面改造不在此目标内。

## 2026-10-04：BUG163 已交付，WP6 接入当前版本验收

[BUG163](https://github.com/GTX537/CP6/issues/163) 已由 [PR164](https://github.com/GTX537/CP6/pull/164) 正常合入远端 main `6322ac152cab7d462719169fb0301f49e9ee39ba` 并关闭，WP6 已接入。WMS 原八项＋新增十七项两库各25/25、零失败/跳过；合并后两库各3/3。两个修复测试库及原 WP6 SQL 失败库均已普通 DROP 并确认不存在；SQL 实际持续会话超时拒绝、短暂会话自行结束后清理通过，原 Failed summary 与 core/WMS TRX 字节保持。[专项记录](../audits/2026-10-03-bug-163-wms-local-transactions/README.md)保留111份原件及36份交付、冒烟、清理原件。

当前必需清单只扩展 WMS 为25项，其他61入口不变。最终入口仍为 PG Matrix34、SQL Matrix37、两库 Application各17，共105个适用入口；使用新的 `wms-local-transaction-fix` 运行目录。编译输入已改变，需当前代码七项构建及新的完整双库验收；后续 SQL 可在源码与完整运行文件逐字节一致后复用 PG 的真实构建，两库应用使用同一新发布产物。此前 PG Matrix34/34、旧发布物应用17/17保留历史来源，不代称当前最终验收。WP6 与父任务#134仍未完成；最终恢复审计、集中审查、归档与正常远端交付待完成。没有启动远程 Actions、改变生产门禁或部署既有环境。下方记录保留历史时点。

## 2026-10-03 历史快照（BUG163 合并前）：WMS 事务修复本地通过，交付待完成

最新进展（2026-10-03，BUG163 Open）：WMS LPN 四处和 Label 两处自建 ambient 事务已修复为独立调用时的 DbContext 本地 ReadCommitted 事务，保留调用方已有本地事务所有权及预打开连接的 ambient Required/abort 语义。有效种子 RED SQL17=3P/14F、PG17=10P/7F；修复后两库原8＋新17各25/25，零失败/跳过，2492源码输入与同一构建运行文件核对，一次事务专项审查无P0/P1/P2阻断；111份公共原件及原始/中间失败保留，见[BUG163本地证据](../audits/2026-10-03-bug-163-wms-local-transactions/README.md)。正常远端交付、合并后冒烟及三份自有库（修复两库与原WP6 SQL一库）清理尚待完成；父Issue134/WP6仍Open，最终双库Matrix/Application与恢复验收需要适用修复后的源码。未启动远程Actions，未变更生产门禁或部署环境。

## 2026-10-03：BUG161 已交付，WP6 继续最终双库验收

WP1–WP5 已交付；BUG153/155/157/159/161 分别由 PR154/156/158/160/162 正常交付并关闭。最新主线 `ce49012d37da4b1b01f074d5b5b7fd227780e656` 已接入 WP6。BUG161 修复 CAD Provider 配置替换的完整事务恢复：最多三次回滚/释放/重读，保留版本冲突、幂等重放和证据不可变，拒绝借用调用方事务或保存其待保存更改。修复前协调 PG 0P/2F，均实际40001；修复后两库原15＋新十项各25/25，补充 InMemory17/17；0 warning/error、一次专项审查无阻断。合并后两库各3/3，PG协调场景再次实际40001后恢复，两个自有库已普通DROP并核对不存在，Issue161已Closed，见[专项记录](../audits/2026-10-03-bug-161-cad-provider-recovery/README.md)。

原 WP6 claim-fix Matrix 在28个入口通过后 CAD/资产/协作14P/1F保持失败；该次七个PG临时库已清理，原summary哈希不变。当前接入修复，将新增十项纳入CAD组25，在新cad-provider-fix目录及当前发布物完成最终双库Matrix/Application、恢复、归档及正常交付。此前两库各17/17应用是旧发布物历史结果。父任务#134仍Open，WP6未完成。

所有执行仅本地，Actions启动/取消均0，无触发或保护变更、强推或生产部署。根工作区原改动保持，下方记录保留历史时点。

## 2026-10-03 历史快照（BUG161 合并前）：BUG161 CAD 配置替换本地回归通过，交付待完成

[Issue #161](https://github.com/GTX537/CP6/issues/161) 由 DB-COMPAT-01 WP6 最终 PostgreSQL 矩阵发现：28 个入口通过后，CAD/资产/协作原 15 项为 14P/1F，实际 40001。独立分支从远端 main 4a654320 建立，补充完整事务的最多三次恢复、回滚/释放/重读及调用方事务/未保存更改保护。修复前协调回归 0P/2F（均实际 40001）；修复后两库原 15＋新十项各 25/25、零 skip，补充 InMemory 路由 17/17；0 warning/error，SQL 复用同一已绑定产物。一次专项源码审查无 P0/P1/P2 阻断，71 份公共原件按字节归档；正常远端交付、合并后冒烟、两库清理和 Issue 关闭仍待完成，详见 [BUG161 证据](../audits/2026-10-03-bug-161-cad-provider-recovery/README.md)。WP1–WP5 已交付；WP6 与父 Issue134 保持未完成。原失败及所有历史交付记录保留。没有远程 Actions 或生产部署。

## 2026-10-03：继续 WP6 最终矩阵、新应用恢复与交付

WP1–WP5 已交付；BUG153/155/157/159 分别由 PR154/156/158/160 正常交付并关闭。最新主线 `4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0` 已接入 WP6。BUG159 精准恢复任务领取的 attempt-number 唯一冲突，保持五次上限、未知异常传播及取消；两库原17＋新6各23/23，零skip，构建/一次集中审查通过。合并后两库原并发和协调场景各2/2，PG实际23505后恢复，两个自有库已普通DROP/不存在核对，见[专项记录](../audits/2026-10-03-bug-159-space-job-claim-recovery/README.md)。

原WP6 Matrix在26个入口通过后出现的任务领取16P/1F保持失败；该次七个PG临时库已清理，原summary哈希不变。此前两库各17/17应用验收是旧发布物的历史结果。WP6接入修复，将新六项纳入任务组23，并在新的claim-fix目录/当前产物完成最终双库Matrix与Application、证据归档及正常交付。父任务#134仍Open，WP6未完成。

所有执行仅在本地，Actions启动/取消均0，无触发/保护变更、强推或生产部署。根工作区原改动保持；下方文字保留各历史时点。

## 2026-10-03 历史快照（BUG159 合并前）：Space worker 领取兼容修复双库本地通过，待交付

WP1–WP5 已交付；BUG157 已由 PR158 正常合入远端 main `5587a2a67ae73715596ca1a135b5863005abac8d` 并关闭，合并后两库冒烟与自有库清理完成。WP6 新 PG Matrix 已通过 26 个入口，随后任务领取并发出现原生 23505，原失败保留。

本次独立修复精准恢复 attempt-number 唯一冲突，并沿用五次完整事务重试、未知错误传播与取消。SQL Server / PostgreSQL 原任务/处理/生成/保留 17 项加新六项各 23/23、零 skip，同一源码/产物，locked build 零 warning/error。实际证据和限制见 [专项记录](../audits/2026-10-03-bug-159-space-job-claim-recovery/README.md)。一次集中审查无实质阻断，62份公共原件按字节归档；正常远端交付、合并后冒烟及两库清理仍待完成；整体 WP6 最终双库矩阵、新应用恢复验收和交付仍待完成，父任务 #134 保持 Open。

没有启动/取消 Actions、修改工作流/保护或部署既有环境；下方旧状态保留各历史时点。

## 2026-10-03：完成接入 BUG157 后的 WP6 最终验收与交付

BUG153/155/157已分别由PR154/156/158交付并关闭，不再列为活动BUG。WP6接入最新主线 `5587a2a67ae73715596ca1a135b5863005abac8d` 后，克隆必需清单增加七项，设计/发布41项保持；用新目录完成双库最终Matrix、新发布物Application、原件核对、所有历史/最终自有库清理和正常远端交付，才关闭[父任务#134](https://github.com/GTX537/CP6/issues/134)。

所有旧失败及两库各17/17应用的真实历史范围保留，不升级为当前通过。继续本地验证，不启动Actions、接管既有数据库或部署生产。下方合并前待办按历史时点保留。

## 2026-10-03 历史快照（BUG157 合并前）：交付 BUG157 后继续 WP6 最终双库验收

BUG153/155 已分别由 PR154/156 交付并关闭。先完成 [BUG157](https://github.com/GTX537/CP6/issues/157) 正常 PR、远端主线包含性、合并后原并发及协调复测和两个自有测试库清理；两库精确克隆各 23/23、设计/发布各 41/41、本地构建与源码集中审查已通过，活动状态以 GitHub Issue 为准。

WP6 接入修复后，将新增七项纳入克隆必需清单，并用新执行目录完成最终两库 Matrix 与受影响发布物 Application。保留旧应用各 17/17 的历史范围、所有真实失败及自有库清理证据；归档和远端交付完成后才关闭 [父任务 #134](https://github.com/GTX537/CP6/issues/134)。全部验证继续在本机隔离端口和自有数据库执行，不启动 Actions 或替换既有环境。下方保留历史待办。

## 2026-10-03：完成 WP6 最终双库验收与交付

BUG153/BUG155 已分别由 PR154/PR156 交付并关闭，不再列作活动修复。WP6 接入最新主线，继续把 Space 新六项纳入正式必需清单，以新的构建和执行目录完成两库最终 Matrix 和 Application。修复前两库各 17/17 应用结果、原生恢复与真实 SignalR 证据及原矩阵失败保持，不改写为当前源码通过。

完成适用双库验收、原件归档、历史/最终自有库清理、任务级完整交付 diff 核对和正常远端 main 交付后，才关闭[父任务 #134](https://github.com/GTX537/CP6/issues/134)。沿用本地验证策略，不启动 Actions、接管既有数据库或执行生产部署。

## 2026-10-03：WP5 本地验证与集中审查完成，待远端交付

[PR149](https://github.com/GTX537/CP6/pull/149)已正常合入远端main `c6c662f5b744b44a51427e26fb2faff96472fcc2`，包含候选`cfe479124fb04d770462b874bc2a70508a8bf79e`且完整树相同。合并后原业务快照保存SQL/PG各2/2、零skip（含owner/迁移setup）；六个WP4自有临时库已普通DROP并确认不存在，receipt和PG角色保留。[交付/清理原件](../audits/database-compatibility/wp4-native/post-merge/manifest.json)新增17份，[父任务更新记录](../audits/database-compatibility/wp4-native/post-merge/parent-progress/manifest.json)另三份；原211与七份manifest及各次失败/复用范围保持。

WP5当前为`LocalVerifiedRemotePending`：独立`codex/db-compat-wp5-20261003`分支已完成Space及报表/GDPR相关本地验证、一次任务级集中审查与发现问题的定向修复。克隆SQL14/14及PG分批限定通过、报表4/GDPR1/Core Space5、容量/文件13、带数据报表4和清理故障回滚1均保留原双库执行来源；AI应用原13两库各13通过，设计/发布35及任务/基础约束分批通过。CAD Retry与克隆同key并发定向两库各5/5、SQL独立及历史迁移6项、PG两种安装顺序/脚本重复/Space-only各1/1，详见[WP5实施记录](../audits/database-compatibility/WP5-IMPLEMENTATION.md)与[首轮原件](../audits/database-compatibility/wp5-native/initial/manifest.json)。

集中审查两项P2均已修复并定向复核：PG AI原13＋caller-pending回归1＋CAD1实际15/15，legacy恢复选择器2/2，均零skip；前置SQL生成断言修正后的同8项8/8另按原来源保留，不与首次执行相加。[BUG150](https://github.com/GTX537/CP6/issues/150)已由[PR151](https://github.com/GTX537/CP6/pull/151)交付远端main `522433a370c0c247a98b300c69e88e403217d53a`并关闭，合并后原并发两库各2/2及自有库清理完成，见[BUG150交付原件](../audits/2026-10-03-bug-150-mapping-idempotency/native/post-merge/manifest.json)。WP5在`97e4a9f2e0881c4f423111848632bbc821cde11d`纳入该依赖后，实际整合构建0warning/0error，映射SQL/PG各4/4通过；[审查修复与整合原件](../audits/database-compatibility/wp5-native/review-followup/manifest.json)保留真实RED、异常包装断言失败及后续成功，不改写历史结果或累加重复测试。

WP5正常提交/PR合并、必要合并后冒烟及远端main包含性核对仍待完成；[父Issue134](https://github.com/GTX537/CP6/issues/134)保持Open，WP5/6远端阶段未勾选。WP6完整应用启动、初始化及原生备份恢复验收尚未执行，普通PG API/worker guard保留；本地WP5通过不代替WP6或整体兼容交付。

Actions启动/取消均0，无工作流/保护变更或生产部署。根工作区原改动保持，早期Pending/Open文字保留各历史时点，当前以本节和真实Issue读回为准。

## 2026-10-03 历史快照：映射保存并发修复本地通过，待远端交付

[BUG150](https://github.com/GTX537/CP6/issues/150)在WP5对照测试中确认原SQL Server同键并发死锁，PG对应路径唯一约束冲突未回放。独立修复只调整CAD/Excel映射保存的自有事务恢复，保留调用方待提交状态和事务。真实SQL/PG各4/4、原映射相关12/12、零skip，最终构建零warning/error，一次集中代码审查无实质阻断；[实际证据与边界](../audits/2026-10-03-bug-150-mapping-idempotency/README.md)保留原RED。

本段为PR151合入前的历史LocalVerified/RemotePending记录：当时Issue仍Open，正常合并、远端包含性和原步骤冒烟待完成；现BUG150已交付并Closed，实际主线、双库冒烟及清理见上方和[交付原件](../audits/2026-10-03-bug-150-mapping-idempotency/native/post-merge/manifest.json)。WP4已由PR149交付，WP5草稿保留在另一独立工作树，父任务134与WP5/6继续。无Actions启动/取消、工作流/保护变更或生产部署；根工作区原改动保持。下方历史状态保留原时点。

## 2026-10-03 历史快照：WP4 本地双库验收与集中审查通过，待远端交付

[BUG147](https://github.com/GTX537/CP6/issues/147) 已经 [PR148](https://github.com/GTX537/CP6/pull/148) 正常合入远端 main `974e57c0650279565330a67c844355ba3e1b563d`，候选 `8f42a76b` 的包含性与完整树一致已核对；2026-10-03 10:48:57 UTC Closed。原118项本地结果按相同源码/程序集复用，合并后原业务保存另执行1/1、零跳过，独占库正常清理且只读核对零残留。[14份交付原件](../audits/2026-10-03-bug-147-snapshot-batch/native/post-merge/manifest.json)保留发布检查、首次HTTP408及正常重试、合并后冒烟与关闭记录，不改写原24份证据。

WP4已在独立分支完成本地实现及一次集中代码审查，无实质阻断。两库各通过 OIDC/Refresh 27、原ERP 95、WMS 8；身份原共同21与七个新增专项（每项2，含setup）按各自执行来源保留。财务/权限/采购/WF/通知及事件retry共同25项分批完成；PG采购锁的初次21/22和修复后采购5/5不改写为单次25/25。PG另两项实际40001证明整事务释放后重建并成功幂等；相关unit/SQLite另198/198。必需通过批次均零跳过，WMS未选择专项的预期8skip单列，不计原生验收。全部原失败、复用与外部传输/完整应用运行边界见[实施记录](../audits/database-compatibility/WP4-IMPLEMENTATION.md)。211份公共原件已按字节归档；当前待提交、正常PR合并及远端包含性核对；WP4尚未交付，WP5/6与父任务134继续Open。

根工作区原改动保持；全部验证本地执行，未触发/取消Actions、改工作流/保护或部署生产。下方Open/Pending均保留各历史时点，当前以本节和GitHub Issue为准。

## 2026-10-03：先交付 BUG147，再恢复 WP4 双库业务验收

WP3 已通过 [PR146](https://github.com/GTX537/CP6/pull/146) 进入远端 main。当前优先完成 [BUG147](https://github.com/GTX537/CP6/issues/147) 的正常 PR、必要合并核对与关闭；集中审查无实质阻断，活动状态以 GitHub Issue 为准，本地118/118结果见[专项记录](../audits/2026-10-03-bug-147-snapshot-batch/README.md)。

随后在保留的 WP4 分支接入修复，继续 OIDC/refresh、身份/priority、ERP/WMS、采购/工作流和财务两库真实业务验收；WP5/6及父任务134仍未完成。继续只在本地执行验证，不启动 Actions 或切换现有环境。

## 2026-10-03：WP3 已交付，WP4 身份与核心业务适配开始

[PR #146](https://github.com/GTX537/CP6/pull/146)正常合入远端main `0b0ab74a04c4d840a2c0bb05e36395aff8b89d32`，包含候选`4ddc6c9d8d75f47711fce1c5e56fbb0ac8733e21`且完整树相同。合并后同一API实际再次拒绝普通PG运行、非零退出、无HTTP；[七份交付/冒烟原件](../audits/database-compatibility/wp3-native/post-merge/manifest.json)随本阶段状态保留。WP3原151来源/147原件及各次真实双库结果按原范围复用，两库临时库已清理。父Issue134已勾选WP3，仍Open。

WP4在最新远端main的独立`codex/db-compat-wp4-20261003`工作树开始，首段为OIDC、真实refresh与身份撤销/priority同事务链。已建立新的两库owner-marked临时库，正在准备真实PG RED入口，尚未声称WP4业务通过。其余身份、ERP/WMS、采购/OA/WF和财务按已接受阶段计划继续；[WP4实施记录](../audits/database-compatibility/WP4-IMPLEMENTATION.md)说明范围与未验收项。

根main因原有未提交锁文件被Git拒绝fast-forward，文件及其他未跟踪内容原样保留；没有为同步而stash/暂存/覆盖。WP4基线使用已确认最新origin/main。普通PG API/worker限制保持，WP4–WP6未完成；没有Actions运行/取消、工作流/保护变更或生产部署。下文为此前历史状态。

## 2026-10-03：WP3 本地验收与集中审查完成，远端交付待核对

WP1/2已分别由PR136/145交付；本阶段基线为main `605246ca83cbb8e1a89d3ff7209ba9df65176e98`。WP3已实现两库资源锁、数据库UTC、错误分类、ORD原子分配、共享Context事务、generation/v2游标与工作槽领取，并登记全部指定Provider分支及后续业务责任。

实际两库ORD各7/7及补充各5/5、锁/时钟各5/5、共享事务各3/3、约束与整事务重试各4/4、工作槽各10/10通过；游标SQL3/3、PG4/4含真实进程重启与PG同页并发快照。通用单测75/75和共享18/18保持各自来源；最终调用方相关Core54/54、Space22/22零Skip，包含14个异常注入回归。原启动30P/2Skip后两项真实SQL用例独立2P/0Skip保留原件。API双库首次/重复DatabaseInit四次exit0/noHTTP，未开放普通PG API/worker。

一次任务级审查的P2测试工具超时清理已定向解决；最终ORD与共享工具分别双库复测通过，强制超时只静态复核。原始失败全部保留，151个来源/147份唯一原件逐一hash一致，[manifest](../audits/database-compatibility/wp3-native/manifest.json)与[实施记录](../audits/database-compatibility/WP3-IMPLEMENTATION.md)说明复用及未验证范围。两个WP3独占数据库已普通DROP并确认不存在，原receipt及PG测试role保留。根工作区其他改动未动。

当前LocalVerified/RemotePending：待正常提交、PR、main包含性与必要集成核对；随后执行WP4/5/6。父[Issue134](https://github.com/GTX537/CP6/issues/134)继续Open。没有Actions运行/取消、工作流触发变更、保护变更或生产部署。下文保留此前历史时点。
## 2026-10-03：WP2 剩余远端交付

[PR #144](https://github.com/GTX537/CP6/pull/144) 于07:15:02 UTC正常合入，main `94c0f8c9cac63a72008c4e6b242c94358f25b305` 确认包含功能 `2b6548b1` 与证据 `5348a2a5`；WP2保留任务改动并fast-forward。BUG143原17/17、升级/重复15/15、四案例及恢复、fixture清理29/29和报价SQL/PG各2/2保持原范围；合并后同API原步骤exit0/noHTTP及native完整比较15/15已完成，[Issue143](https://github.com/GTX537/CP6/issues/143) 于07:23:27 UTC Closed，closure原件由WP2交付记录引用。

2026-10-03 WP2最终SQL136→140四指定前向升级实际2/2、完整目录/定向写入/负对照35/35、未知history2/2通过。新API首次/重复DatabaseInit均exit0、无HTTP，seed capture及两次verify各2/2，全部352表计数/history、自定义global/tenant翻译、管理员昵称和密码摘要保持。实际probe BFB8…314C0、Core F84…0DE1、API7097…740A，源码为main94加未提交WP2输入；probe源码/build观察在首次native期间完成，API观察在首次initializer之后，原pre-native误词与root-only SqlClient hash准备比较失败均保留分类，不称native失败或重跑。2950源码hash仅为输入观察，不扩大测试/review范围。

PG完整限定36/36、historical fresh33/33、unknown-history2/2、实际APP首次/重复和334表保持、151/151及独立25/25按原输入复用；另一次PG报价2/2保持独立来源。SQL oracle仍d607，原失败、30/45集中review、两P2及后加四文件/reader两行增量review的来源见[实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)。当前LocalVerified；8个owned测试库已全部清理并确认不存在，原所有权记录字节保持，PG测试role保留（[实际清理原件](../audits/database-compatibility/wp2-native/wp2-cleanup-final-owned-eight-verified-env.json)）；首两次清理工具准备失败均0drop并保留，非迁移失败。原始140份清单加最终输入证明/脚本2份，共142份已归档且全数hash/bytes匹配（[manifest](../audits/database-compatibility/wp2-native/manifest.json)、[源码字节证明](../audits/database-compatibility/wp2-native/wp2-final-review-source-applicability.json)）；75个记录输入一致不称新75测试或review。WP2提交/PR/远端main核对Pending，WP3–WP6及[父任务134](https://github.com/GTX537/CP6/issues/134)继续。普通PG API/worker guard保留，没有Actions、保护变更或部署。下方Current/Open/Pending保留历史时点，以本节与后续实际核对为准。

## 2026-10-03 历史快照：BUG #143 本地门禁已齐，待正常远端交付

[Issue #143](https://github.com/GTX537/CP6/issues/143) 最终17/17、实际同API升级/重复15/15、四命令案例4/4且各恢复15/15、fixture清理29/29、报价SQL/PG各2/2和一次任务审查加P2定向复核均完成。LocalVerified/RemotePending；下一步检查候选/远端工作流事件与依赖，正常提交/PR/main合并、核对远端包含性和必要原步骤复测，再关闭Issue。旧失败、复用范围及超长最后空格差异见[审计](../audits/2026-10-02-bug-143-quotation-audit-capacity/README.md)，缺失旧review元数据不补造。当前不重跑同输入构建或完整审查。

WP2仍需整合此独立BUG后完成136→140四个指定前向迁移与适用完整SQL门禁、集中交付及父任务阶段核对；总目标继续，PG API和后续运行时阶段未完成。未运行Actions或部署。下方10-02的本BUG真实SQL/完整审查待办已由本条结果更新，其文字仅保留历史时点。

## 2026-10-02：BUG #143 待真实 SQL、完整审查与正常交付

[Issue #143](https://github.com/GTX537/CP6/issues/143) 本地目标 **17/17、零跳过、零warning/error**，有效RED **6失败/7通过**；六个nullable审计姓名列只向100收敛，超长/未知定义拒绝，保留collation，不truncate/delete/backfill/drop依赖。[证据](../audits/2026-10-02-bug-143-quotation-audit-capacity/README.md)已交负责人同actualAPI/Core；下一步owner隔离SQL真实首次/重复、100个UTF16码元（200字节）/尾空格/NULL边界、已有超长/未知定义/依赖/晚失败全事务回滚及原数据token/index/FK/history保留，再一次独立完整任务审查及正常PR/远端main包含性后关闭。Issue143 Open，不预称native通过或WP2完成。BUG141已PR142 merge、远端main4d4e2608确认并Closed，旧Pending段落保留历史；本地源码freeze，后续不因docs/commit重复编译。未运行Actions或部署。

初版17静态GREEN之后实际SQL102（COLLATE方括号）/缺列207失败已保留；两次全数据/目录恢复15/15不是容量迁移通过。已最小修native collation校验后safe literal拼接和metadata之后动态DATALENGTH bitOUT，新target17/17；APIhash不变但Corehash变更须重新绑定。修正版native与完整审查/远端交付仍Pending，不预写GREEN。

## 2026-10-02 历史快照：BUG #141 已交付，WP2 PG 门禁通过，SQL 四项前向升级待验

[BUG #141](https://github.com/GTX537/CP6/issues/141) 已经 [PR #142](https://github.com/GTX537/CP6/pull/142) 正常合入，远端 main 已核对为 `4d4e260819f0a1b59810403d016c69a2fd7319ae`，Issue Closed。WP2 分支保留本任务改动并 fast-forward 到该主线；SQL 原合同 oracle 仍冻结 `d6074aaa`。该 BUG 的真实 Core138→139、首次/重复初始化、四 FK/级联和 rollback 证据保持原范围，详见[原始审计](../audits/2026-10-02-bug-141-order-foreign-keys/README.md)。

WP2 最新实际 PostgreSQL 完整限定门禁 **36/36**，包括四 Context、65 个 raw 默认、四个全局 NULLS NOT DISTINCT 唯一索引、令牌/generation、时间与代表性关系/金融写入。执行原件 `wp2-migration-pg-final-exact-history-full-write-gates.json` 的源码基线为 `2d974736`，保留执行输入和二进制哈希；文档及主线 SHA 更新不改称新执行。输入 sidecar 补正实际 SDK `10.0.302` 与 1439 个源码哈希，原缺失 SDK 文件的无效 sidecar 保留，未因此重跑 build 或 native gate。实际 PG DatabaseInit 首次/重复退出 0、无 HTTP；其 **334 张表**行数/历史与全局/租户自定义翻译、管理员昵称和密码哈希保留。相关测试 **151/151** 与另一次索引回归 **25/25** 均零跳过，各自保留真实来源；双库金额/FK 和 Space 租户/Unicode 存储门禁各 **3/3**。这些证据证明限定初始化、结构和存储合同，全业务验收尚未完成，普通 PG API/worker 仍受运行门禁限制。

实际 PG 迁移历史负对照 **2/2**：未知 ID 在 `Migrate` 前被拒绝，外部事务回滚后原历史完整恢复；缺失 `--seed-mode` 值或 seed state 路径均实际 exit 1、`DatabaseVersion=null`，在数据库访问前拒绝。这些预期失败分别保留原件，不计为业务成功。WP2 一次集中审查已按分工完成初始化/Provider/工具 **30 文件**及映射/帮助器/迁移/模型测试 **45 文件**两个范围；前者两个 P2 已修复并仅定向复查 seed 参数前置校验与迁移历史精确合同，未因文档更新重启完整审查。执行来源、复用边界及历史失败见[实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)与[模型测试记录](../audits/database-compatibility/WP2-MODEL-TESTS.md)。

[BUG #143](https://github.com/GTX537/CP6/issues/143) 仍 Open，正从最新已确认 main 的独立分支恢复六个报价 Creator/Modifier 历史容量。WP2 最终 SQL **Core136→140** 的 generation、21 个索引、四 FK、六列容量 **四项前向升级尚未执行**；旧 generation-only 136→137 及 BUG #141 的 138→139 分项成功不扩大为最终升级通过。WP2 冻结、最终 SQL 验收、远端交付及 owner 库清理仍待完成，WP3–WP6 与 Issue #134 未完成。未触发/取消 Actions、改变触发或保护、迁移既有业务环境或部署。下方保留各历史快照，其中 Open/Pending 仅指相应记录时点，当前状态以本节为准。

## 2026-10-02 历史快照：WP2 PG 初始化与定向门禁通过，SQL 最终升级待验

本段保留 BUG #141 交付前的 WP2 执行快照；33/33、三项 136→139 计划和当时 Open/Pending 不改称最新执行或当前状态，最新进度见顶部。

[索引修复 PR #140](https://github.com/GTX537/CP6/pull/140)已正常合入并核对远端 main `2d9747360a38dfc519ddebea7aced06a9466b879`，包含功能 `083e9c4d` 与原始证据 `94124c62`；合并后原步骤目录冒烟通过，[Issue #139](https://github.com/GTX537/CP6/issues/139)已关闭。WP2 worktree 保留本任务改动并 fast-forward 到该主线；SQL 原合同 oracle 继续冻结 d607。

WP2 新空 PostgreSQL 结构/代表性写入门禁 **33/33**（65 默认和四个全局唯一索引已安装）；双库金融金额/外键及 Space 租户复合 FK/Unicode 存储边界各 **3/3**。相关 Provider、接线、模型及初始化测试 **151/151**，另一次索引集成回归 **25/25**，两次均零跳过，保留各自真实来源。实际 compiled PG DatabaseInit 首次与重复均退出 0、不启动 HTTP；全部 **334 张表**行数/迁移历史保持，全局及租户翻译、管理员昵称和密码保留。PG 实际 Order graph/四 FK/删除级联专项 **2/2**，旧 SQL136 同专项 **1 通过 / 1 失败**是有效缺 FK RED。成功结果及历史失败见[实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)，这些限定门禁不替代全业务验收。

SQL 完整目录发现四条 Order 外键缺失，由独立 [BUG #141](https://github.com/GTX537/CP6/issues/141) 修复；该源码相关 **37/37 GREEN**，真实 SQL 验收及远端交付仍 Pending，Issue Open。两个 alternate key 已确认由明确的全局 unique index 等价实现，目录继续验证定义。最终 SQL136→139 的 generation/索引/FK 三项正常前向升级尚未执行，旧升级证据不扩大为本轮通过。模式冲突已在访问数据库前拒绝；普通 PG API/worker guard 实际拒绝仍保留。WP2 收尾、集中审查、远端交付和 owner 库清理待完成；后续按授权继续 WP3，WP3–WP6/Issue #134 未完成。未运行/取消 Actions，未改触发、保护、现有数据库或部署。下方记录保留对应历史时点。

## 2026-10-02 历史快照：BUG #141 真实 SQL 门禁通过，待正常交付

本段保留 PR #142 合入前的 BUG #141 本地验收快照；其中 Issue Open、远端 Pending 为当时状态，当前已 Closed，交付事实见顶部。

[Issue #141](https://github.com/GTX537/CP6/issues/141) FK12+索引25 **37/37、零跳过**，一次独立任务审查无阻塞；真实同API首次/重复exit0无HTTP，原生升级/重复各 **9/9**、guard **11/11**（各恢复9/9）、独立WP2实际SQL订单关系 **2/2**及写入后状态比较9/9。[范围与原始失败](../audits/2026-10-02-bug-141-order-foreign-keys/README.md)保留NFR setup失败/五项复用和组合证明边界。本BUG本地门禁通过，下一步负责人正常PR、远端main包含性和必要集成核对后关闭；当前Issue Open，不执行NOCHECK或清理业务孤儿。其他WP2结构、六个报价字段容量差异及全阶段验收独立待处理，未运行Actions或部署。

## 2026-10-02：BUG #139 本地门禁通过，待正常 PR 与远端核对

[Issue #139](https://github.com/GTX537/CP6/issues/139) 单元 **25/25、零跳过**，真实同 binary 初始化首次/重复 exit 0、无 HTTP，升级/重复比较各 **25/25**，native guard **8/8**（每次状态恢复 **25/25**），一次独立任务审查无实质阻塞。[证据](../audits/2026-10-02-bug-139-order-indexes/README.md)保留原始 RED、空 stdout 归档失败与 1934 setup 失败；不把它们改写成 guard RED/成功。下一步负责人正常 PR、远端 main 包含性和必要集成核对后关闭；当前 Issue Open。WP2 generation 合入时需收窄本分支测试固定 137 条 registry 的总数断言；其他 WP2 历史结构缺口另立任务，此项不替代阶段门禁。未触发 Actions 或生产部署。

## 2026-10-02：DB-COMPAT-01 WP1 远端交付，开始 WP2

[PR #136](https://github.com/GTX537/CP6/pull/136) 已正常合入远端 main `d6074aaad3098b61adaeda902b92bf24b3eb0c04`，包含 `d30da70e` / `7bbe2eca`；本地/远端 main 一致，WP1 交付闭环。复用相同代码/依赖的真实 133 配置测试、SQL28 / PG31 限量试验，不重新执行或称为全量业务验收。未触发/取消 Actions，未改保护或环境。

从该基线创建独立 `codex/db-compat-wp2-20261002` worktree，进入四 Context 完整映射、PG 迁移与初始化，见 [WP2 实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)。WP2 尚未验收/交付，WP3–WP6 未完成，整体目标及 Issue #134 保持进行中；PG API/worker runtime guard 保留。下方 WP1“待交付”条目保留提交时的历史时点。

## 2026-10-02：BUG #137 已正常 PR 合并，远端核对后关闭

[采购对账菜单首次资源键修复](https://github.com/GTX537/CP6/issues/137)已在独立分支提交。旧逻辑 RED **2 失败 / 1 通过**；修复后相关测试 **20/20 通过、零跳过**。真实隔离 SQL 空库首次初始化即有菜单资源键；同一 compiled API 再次初始化后，**全部 351 张表行数和迁移历史不变**，包括字段审计。全局/租户翻译、管理员昵称与密码哈希原样保留，两次进程退出 0、不启动 HTTP。详见[执行证据与适用边界](../audits/2026-10-02-bug-137-menu-seed/README.md)。

功能执行源 `bab643750111beef610eba911bb530b422f9d99f`；后续文档及证据提交复用相同功能源码的测试。[PR #138](https://github.com/GTX537/CP6/pull/138)已合入远端 main `d507f389fdcadd8e2fc5dcb9e40f752cde613703`，祖先关系核对包含功能提交，Issue #137 已关闭。不执行 Actions、现有数据库迁移或部署；触发配置未改、取消项 0。

## 2026-10-02：DB-COMPAT-01 WP1 待正常 PR 交付，随后执行 WP2

[Issue #134](https://github.com/GTX537/CP6/issues/134) 是本对话持续目标，六阶段按[计划](../superpowers/plans/2026-10-02-database-compatibility.md)推进。WP1 本地门禁已完成：配置测试 **133/133，零跳过**（含原 38 项，不重复相加）；SQL **28 Passed / 1 NotApplicable / 0 Failed / 0 Blocked**，PG **31 Passed / 1 Rejected / 0 Failed / 0 Blocked**。两库相同源码与实际运行二进制 fingerprint 为 `0F0B443961674D40B8163768225AE12D1A55CD18F28FCE0D5F16BB0CB91C6BB3`，固定实际 Platform **0.10.2**。已选定 PG 数据库生成 **8 字节 bytea token** 与独立持久化 tenant generation / v2 cursor，并完成真实 `40001` 和严格 UTC 试验；见[WP1 决策](../audits/database-compatibility/WP1-DECISIONS.md)及[探针证据](../audits/database-compatibility/WP1-PROBE.md)。

任务负责人已凭准确库名、owner 标记及 **0 对象 / 0 连接**清理两库，无 FORCE 或会话终止，专用 PG 角色保留。当前尚未远端合并；下一步按正常 PR 流程交付 WP1，核对远端 main 包含性后，从最新已确认主线创建独立 WP2 分支。

WP2–WP6 及整体兼容仍未完成：继续四 Context 模型与独立迁移、业务锁/编号/错误、身份/ERP/WMS/OA/WF/财务、Space/CAD/AI/报表，以及原生恢复和隔离 API 验收。PostgreSQL 应用 runtime guard 保留，父任务 Open；不切换现有环境、不执行已有 SQL Server 数据搬迁或生产部署。下方“功能尚未开始”保留建档时点。

## 2026-10-02：DB-COMPAT-01 双数据库兼容

主任务：[Issue #134](https://github.com/GTX537/CP6/issues/134)，Open。用户已接受同一代码按部署选择 SQL Server 或 PostgreSQL，保留现有 SQL Server 迁移历史，新增独立 PostgreSQL 迁移链。唯一阶段/验收明细在[实施计划](../superpowers/plans/2026-10-02-database-compatibility.md)，事实与边界见[盘点](../audits/2026-10-02-database-compatibility.md)和[设计](../superpowers/specs/2026-10-02-database-compatibility-design.md)，此处只保留路线入口。

下一步执行 WP1：统一 Provider/连接/Context 工厂，核验实际 Platform 0.10.2 包，并以真实双库试验冻结并发 token、身份快照边界和跨 Context 原子事务方案。随后按 WP2 映射迁移、WP3 数据库能力、WP4 关键业务、WP5 Space/报表、WP6 验收运维推进。功能尚未开始；已有 SQL Server 数据搬迁、独立 CRM 和生产切换另立任务，不以连接或建档作为完成证据。

## 2026-09-22：demo 使用边界

`cp6.uk` 已接入独立 `cp6-demo`。演示前运行 [runbook](../../deploy/demo/README.md) 中的健康/身份检查，并保持电脑和 Docker 在线；不要用根项目旧启动脚本重新开启退休 API。当前 ERP/WMS 列表与 Space 两层模型可展示；实时库存叠加仍按 [Issue #132](https://github.com/GTX537/CP6/issues/132) 独立修复和验证。独立 CRM、生产发布及真实业务全流程不在此次验收范围。

## 2026-09-16：Space 库存有界查询后续项

六项发布恢复已完成定向后端与隔离 SQL 验证，详情见[报告](../space/reports/2026-09-16-space-publish-recovery.md)：26/26、SQL 4/4、最终篡改 1/1，零跳过，临时库零残留。旧 URL 补丁会在超过 100 个选择 ID 时丢失筛选条件，导致大站点整站请求超过 10,000 库位限制，本轮已排除。

后续采用有界传输且保持精确选择范围；验证站点超过 10,000 库位时合法的 101 个 ID 请求、去重、缺失/越权失败关闭和库存来源/Published 版本一致性。该后续项不阻塞已验证六项发布修复的独立交付；不能描述为库存 URL 问题已解决。

## 2026-09-16：恢复 3D Viewer 自由相机

自由相机三个历史功能提交已恢复并在当前代码上验证：Node `22.22.0` 下定向 **4 文件 / 20 项通过**，`npm run type-check`、`npm run build-only` 退出 0；构建的大 chunk 提示保留。本恢复分支的剩余交付工作是由总整合任务完成远端 main 核对，不因合并 SHA 变化重跑输入未变的成功验证。命令、回归失败/通过与测试替身边界见[项目状态](PROJECT_STATE.md)。

本次不替代历史计划中的真实 WebGL / 10,000 库位交互或生产验收，也不为恢复源码改动现有宿主、数据库或容器。设计/计划已明确为历史归档，其未勾选操作不自动成为本轮待执行任务。

## 2026-09-16：Space 首页恢复交付边界

历史遗漏 `22b3a9ce` 已在独立任务分支恢复并通过 9 项单测、Vue 类型检查和 WebAPI Debug 编译；44 个五语言词条完成静态核对。余下为整合任务的 main 合并、必要集成检查与远端提交确认，不需因文档或提交身份改变重跑相同输入的成功检查。

真实浏览器/API 联合验证、数据库启动种子与 Redis 缓存清理未在本次执行；没有新增环境、迁移、生产部署或 Actions 授权。本地组件测试不替代这些运行验收。

## 2026-09-16：BUG 队列与交付边界

后续BUG按照[管理规则](../quality/BUG-MANAGEMENT.md)在[GitHub Issues](https://github.com/GTX537/CP6/issues?q=is%3Aissue+is%3Aopen+label%3Abug)跟踪；历史PR #39仅补齐管理入口，不自动关闭或重新判定Issue #38。产品升级工作继续以2026-09-16源码盘点及私有升级工作包为依据。

## 2026-09-16：全项目源码盘点与升级入口

完成业务、架构和界面现状材料整理。公开[Core/Platform分报告](../audits/2026-09-16-full-project/README.md)保留公开源码证据；[含CRM的完整跨仓报告](https://github.com/GTX537/CP6.CRM/blob/main/docs/audits/2026-09-16-full-project/README.md)与升级工作包留在CRM私有仓库。已记录旧Web拣货未提交、外部NoOp与固定规则、消息语义差异、Space/原生客户端及界面统一范围。

本轮只改文档与复现工具，不改业务、数据库、运行宿主或既有验收；未运行业务测试/Actions或生产部署。[验证与交付范围](../audits/2026-09-16-full-project/VALIDATION.md)。

## 2026-09-16：C04B 模拟开发验收通过，交付后 C 系列100%

用户因无真实业务数据明确批准验收v2。C01/C02/C03/C04A既有20%权重及成功证据保持，C04B通过[本轮证据](../evidence/c04b/2026-09-16/simulated-exit/README.md)：20表旧生产模型/状态机退出、静态阻塞8→0、38项定向检查全部通过、20条模拟旧记录保留、独立发布257文件及HTTP三项200、临时库清理为0。C01/C02身份与C03 ERP关联保留，旧实体/状态机字节保留于测试夹具。

**本轮双仓库远端main核验后，C系列开发验收100%（5/5，较80%增加20个百分点）。** 合并前证据明确保留Git待交付状态，最终交付回执独立记录。当前版本详见[进度v2](../project-memory/C_SERIES_PROGRESS.md)；以下历史60%/80%及未关闭条件只表示其记录时点。

本轮开发目标没有剩余业务数据/观察期阻塞。真实用户采用、10工作日/200条Lead、Full Journey、正式演练/发布周期观察及生产上线移至以后独立生产验收，旧表物理DROP另立任务。原Written目标、日历、来源保护及已采用宿主保持；未执行生产部署或Actions，未重跑原五项SQL与六库恢复。


## 2026-09-16 UTC：原运行恢复，C04A 最终验收完成

[运行恢复与 A12 证据](../evidence/c04a/2026-09-16/runtime-recovery/README.md)记录重启后 Docker 修复、原库/容器身份保持、429 原发布文件及请求摘要匹配、原五进程宿主 ready、Kafka/Dapr 恢复、三源 Frozen/60 旧表零行、两目标 Written/原日历版本、两组织身份 Ready/零漂移，以及健康接口和两个公开表单实时成功。仅补中断后的最终验收，旧通过测试及初始化/冻结/启用/发布均未重做。

**整体清单80.0%（较78.3333%增加1.6667个百分点），C04A 12/12＝100%；本任务双仓库远端 main 交付后正式里程碑80%（4/5）。** 原验收回执的 Git 待交付字段保留为历史时点。两个目标首写实际由 `crm_v1.RetentionRun` 在日历发布前触发；当前保持前向修复边界。Docker 以后须使用保留的本机缓存启动脚本；原数据和异常 socket 保留，未重置/恢复数据库。部署 Agent 继续 Stopped/Disabled。

C04B 剩余20%：CRM11 正式演练/切换与采用、至少一个正式发布周期只读观察、旧 EF/运行时依赖退出及前向清理。未生产部署或运行 Actions；本机空来源采用不替代生产验收。下方记录为各历史时点。

## 2026-09-16 UTC：两组织原库日历已发布，A10/A11完成

[实际采用证据](../evidence/c04a/2026-09-16/actual-local-adoption/README.md)记录管理员独占窗口、旧API停止、八入口退休、六份当前备份、一次原库初始化、三源Frozen及经实时证明的整组启用。为两个原管理员补齐缺失的日历权限后，各一次保存/预览/发布成功，表单均200，两目标已Written。**整体清单78.3%（较75.0%增加3.3333个百分点），C04A 11/12＝91.7%，正式里程碑60%。**

中断后Docker及宿主退出；原生持久状态核对保留。第一处Docker残留socket目录已获明确允许并保留修复，第二处engine.sock目录连管理员改名也被Windows拒绝，当前运行未恢复。A12仍缺最终运行验收，暂不计80%；拟议受控Windows重启尚未执行。旧通过SQL/恢复项目沿用；禁止重新初始化、冻结、启用、发布或恢复旧库覆盖Written目标。当前任务仅交付证据/状态文档，未生产部署或运行Actions。下方为历史记录。

## 2026-09-16 UTC：实际本机切换批准已收到，余下 Windows 管理员控制

用户在具体审阅包后回复“没有问题，给你所有开发权限”，现已单独绑定 [批准记录](../evidence/c04a/2026-09-16/approved-adoption-window/approval.json) 与原审阅清单 SHA `531e2810d3ec3a010dd0a646a0cb4cca9a998d33a49cebd53ba4e260feaf93eb`。本机切换、0.2 变化字节、既有操作身份及两组织日历首写已获授权；不再重复索要批准。原审阅包的 false 字段保留为历史状态，未改写原件。

当前 Windows 令牌仍不能停止或禁用准确部署 Agent（错误 5），Agent 仍 Running / Auto，SQL Agent 仍 Stopped / Manual。已准备只控制该 Agent 的 [管理员操作与回执脚本](../evidence/c04a/2026-09-16/approved-adoption-window/README.md)，验证语法及非管理员失败关闭；管理员成功路径尚未执行。实际服务/库未变，旧通过测试未重跑。**整体清单 75.0%、C04A 9/12、正式 60%，本轮增量 0**；A11 尚缺独占控制，A10/A12 继续等待该技术前置。

## 2026-09-16 UTC：实际切换审阅输入就绪，管理员控制仍缺失

[本机审阅包](../evidence/c04a/2026-09-16/final-adoption-review/README.md)固定 429 个原发布文件、六库当前身份、八个未运行旧入口、两组织已有管理员/根部门和初始日历请求。日历正常保存须位于整组启用之后，首次保存即 Written；没有直接写表或把请求文件计作已存储版本。

部署 Agent 当前 Running / Auto，当前 Windows 令牌查询成功，但 Stop/Start/ChangeConfig 权限均返回 Access denied（5）。实际操作仍缺管理员独占窗口及本包最终批准；原 SQL Agent 保持停止，根 API 正在运行并列入排流影响。没有实际服务/库变更。A10/A11/A12 仍未闭环，**整体清单 75.0%、C04A 75.0%（9/12）、正式验收 60%**，本次准备增量为 0。

## 2026-09-16 UTC：A09 原密钥、完整宿主与身份恢复通过

复用真实备份及已初始化副本，原密钥解密 8 条历史会话成功；完整宿主五进程与私有 transport 启动，两组织身份对账 119 投影/105 用户、零漂移，原账号密码/OIDC 登录与 workspace/列表读取通过。公开表单因初始日历未发布返回 409，仍归 A10；两个业务目标始终 Closed / 只读。宿主与自有 transport 已停止，四个只读副本及运行前认证备份保留。见[恢复证据](../evidence/c04a/2026-09-16/full-host-recovery/README.md)。

A09 **100%**；C04A **75.0%（9/12）**，整体清单 **75.0%**（上次 73.3%，本项精确增加 1.6667 个百分点），正式验收仍 **60%（3/5）**。未重跑原五项 SQL、前五库保障或初始化；原输入/发布字节和 51 个既有容器保持。A10 日历、A11 独占控制/最终签收、A12 实际切换继续开放。见[固定清单](C_SERIES_PROGRESS.md)。

## 2026-09-16 UTC：Docker 第六库真实恢复通过，六库保障完成

Docker 旧套接字目录已原样保留；定位跨卷改名问题并恢复引擎后，精确核对原容器/数据库身份，完成第六库不覆盖备份、CHECKSUM/VERIFYONLY、隔离实际恢复、CHECKDB、329 张用户表读取及结构/原库状态保存。备份和只读恢复库保留；51 个现有容器配置、挂载条目、策略与状态保持。见[本轮证据](../evidence/c04a/2026-09-16/sixth-db-restore/README.md)。

六库保障 **6/6＝100%**，合计 950 张用户表已读取；前五库沿用原证据，没有重跑。C04A **66.7%（8/12）**，整体清单 **73.3%**（上一记录 73.1%），正式验收仍 **60%（3/5）**。不是六库共同停写快照或实际切换；完整应用/身份恢复、初始日历存储版本、独占控制/最终签收及实际切换继续待办。原根服务随已授权 Docker 启动按既有策略恢复，C02 transport 仍停止。见[进度清单](C_SERIES_PROGRESS.md)。

## 2026-09-16 UTC：C 系列细分进度与 Docker 第六库阻塞

应用户要求，补充固定 12 项 C04A 清单：整体清单完成度 **73.1%**，C04A **65.3%**，正式里程碑验收仍 **60%（3/5）**。这是既有成果的细分统计，不是本次新增完成 13.1 个百分点。见[详细进度](C_SERIES_PROGRESS.md)。

Docker 启动已获明确授权并实际尝试；保留第一个旧套接字运行目录后，第二个 engine.sock 仍受 Windows 访问错误阻塞。第六库未开始备份，数据库保障保持 **5/6（83.3%）**。未改数据库/卷/ACL，未实际切换，未重跑旧通过项目。原密钥和完整宿主、日历初始存储版本、独占写入/最终签收及实际切换继续待办。见[本次阻塞证据](../evidence/c04a/2026-09-16/docker-recovery/README.md)。

## 2026-09-15：真实恢复副本前向初始化与 Closed 启动通过

从三个实际 C02 备份另建隔离副本，原 0.2 API 仅初始化一次并成功；两组织 Ready/Closed，旧快照保护有效，13 表 492 条既有身份/目录记录保留。独立 loopback API live 200、Closed ready 503，进程已退出，副本只读保留。见[本轮证据](../evidence/c04a/2026-09-15/restored-initialization/README.md)。原发布包/私有输入未变，已通过的五项 SQL 未重跑。

此结果不含完整宿主、身份对账/登录或实际切换；日历实测仍为空，启用前初始存储顺序需解决。Docker 第六库、独占操作与变化字节批准、CRM11/C04B 正式验收及观察仍待办，总目标 **60%（3/5）**。正常 PR 与远端 main 包含性另记交付。

## 2026-09-15：五个实际原生数据库备份与可读恢复副本

五库 COPY_ONLY/CHECKSUM、VERIFYONLY、实际恢复、CHECKDB 及 621 张用户表读取通过；原库/副本结构摘要一致，11 份原私有输入已另存并核对哈希。备份与新只读恢复库保留，实际数据库和发布字节未变。见[恢复证据](../evidence/c04a/2026-09-15/six-db-recovery/README.md)。已通过的五项 SQL 门禁不重跑。

机器重启后旧六进程已退出，Docker 引擎未运行；第六库备份/恢复继续待办，旧 PID 清单失效。下一步补第六库、真实恢复副本上的 0.2/Closed 启动兼容、日历初始存储顺序、独占操作与变化字节签收。尚非六库全套或实际采用完成；CRM11/C04B 仍开放，总目标 **60%（3/5）**。正常 PR 与远端 main 包含性另记交付。

## 2026-09-14：来源证明与整组启用必要 SQL 门禁完成

剩余三项新 SQL 与两项受影响 Enable/Close 已全部通过；最后一项修正了锁分区的测试计数，业务代码、发布包和超时保持。既有通过项不重跑，原失败与准确版本均保留。453 文件边界、定向复查和清理通过，见[最终证据](../evidence/c04a/2026-09-14/verified-target-enable/README.md)；正常合并与远端 main 包含性另记交付。

继续实际本机采用与 Closed 启动、六库备份恢复、独占写入控制、初始日历版本及变化字节签收。CRM11 正式切换与 C04B 采用/观察尚未完成，总体 **60%（3/5）**。

## 2026-09-14：启用草稿整合已交付主线

恢复宿主和停用命令已合入启用草稿，组合工具编译与命令入口通过，453 文件边界通过；原发布包和既有通过结果按原范围保留，未重跑测试或 SQL。详见[组合记录](../evidence/c04a/2026-09-14/verified-target-enable/README.md)。

三个新增 SQL 与两个受影响 Enable/Close 门禁仍开放，Core #114 / CRM #75 继续保持草稿，不能合入主分支。实际执行、变化字节签收、CRM11/C04B 仍待完成，总体 **60%（3/5）**。

## 2026-09-14：独立旧入口停用与本机执行包准备

完整组织集合绑定的 `retire-deployment-local` 已实现，15 项新增检查通过，无 SQL、无跳过，不重跑旧通过项。当前主分支 API 和新操作工具已独立发布，恢复宿主原样复用；18 项新 API 宿主输入与实际八入口已只读核对。详见[本轮证据](../evidence/c04a/2026-09-14/local-execution-package/README.md)。原六进程、五容器和私有配置保持，新增候选运行清单未被选用；447 文件边界通过。正常 PR 与远端 main 包含性另记交付证据。

实际执行包仍为草案：来源/整组启用 SQL 门禁、六库备份/恢复保障、独占写入身份控制、当前日历初始版本、实际 C02 前向采用和读取冒烟、0.2 字节签收仍开放；初始化后禁止简单换回旧 CRM DLL。CRM11 正式切换与 C04B 采用/观察不由本轮替代。总体 **60%（3/5）**。

## 2026-09-14：C04A 独立恢复宿主开发验证完成

独立宿主已移除旧 CRM API、SQL 和验收初始化依赖，17 项新增生命周期/回调检查及 446 文件边界通过，见[本轮证据](../evidence/c04a/2026-09-14/preview-recovery-host/README.md)。只读核对原清单 18 个必要输入，现有 6 个进程和 8 个入口/控制文件保持原样；没有连接 SQL，自有夹具全部清理。正常 PR 与远端 main 包含性另记交付记录。

Core #114 / CRM #75 的 SQL 门禁继续开放，保留已有 62 项通过结果。继续实际生命周期/特权控制、当前 0.2 字节签收及完整执行恢复包；实际采用恢复、CRM11/C04B 正式验收和观察期仍未完成。总体 **60%（3/5）**。

## 2026-09-14：保留来源冻结失败诊断，启用门禁继续开放

独立恢复宿主已由 Core #115 / CRM #76 交付。此启用草稿仅补新增检查的固定错误编号输出，原发布包和 62 项通过结果不变；一次未通过场景仍在来源冻结阶段返回 C04A_SQL_FAILURE，且观察到 SQL 内存缓冲区等待及只读查询超时。详见[诊断续记](../evidence/c04a/2026-09-14/verified-target-enable/README.md)；不据此改写早先未分类证明拒绝，也不继续批量试跑。

四个本次临时数据库已清除，原生/Docker 最终临时库为零，原预览六进程与八个入口摘要不变；私有失败目录保留。三项新 SQL 和两项直接受影响门禁仍待完成，草稿禁止合并；实际采用、变化字节签收和 CRM11/C04B 保持开放。总体 **60%（3/5）**。

## 2026-09-14：C04A 来源冻结与整组启用草稿

继续关闭三个新 SQL 联动场景及两个直接受影响 Enable/Close 门禁；登录超时和一次未分类来源证明拒绝仍需定位。保持草稿，门禁通过前不合并。详见[本轮证据](../evidence/c04a/2026-09-14/verified-target-enable/README.md)。444 文件边界通过，SQL 失败原件和准确发布版本保留；不重开无关旧通过项。临时测试库已清理，现有预览保持，两个本轮私有失败夹具目录保留供调查。

实际启用、0.2 变化字节签收、CRM11/C04B 正式验收及观察期均未完成；恢复宿主兼容、特权写入控制和最终执行包继续推进。总体 **60%（3/5）**。

## 2026-09-14：C04A 多组织目标恢复必要开发检查完成

完整目标集合绑定、全部目标持锁恢复和 CRM 集合操作入口通过 60 项新增与 2 项直接受影响检查，见[最终证据](../evidence/c04a/2026-09-14/target-set-recovery/README.md)。最后一项超时检查在只读 SQL 连接恢复后以原样二进制通过；未放宽超时或改动共享环境，旧失败原件保留。原生/Docker 临时测试库及实际来源控制 Schema 最终均为零，一个自有失败临时目录因自动审批拒绝而保留。

已同步最新 main 的启动控制，保留各组件原通过范围。Core 40 文件原样复用，CRM 组合版本重新构建 178 文件并验证命令入口；438 文件边界及合并差异审查通过。此前草稿的 SQL 门禁已关闭，正常 PR 与远端 main 包含性另记交付记录。继续完成恢复宿主依赖兼容、特权写入控制、来源冻结与目标启用联动和最终执行包；实际切换、0.2 字节签收、CRM11/C04B 正式验收及观察期仍开放，总体 **60%（3/5）**。


## 2026-09-14：C04A Node 与未运行启动入口控制开发验证完成

本机排流清单已支持 Node 和未运行的旧启动入口；统一预检、精确活动进程身份、持续退休及旧清单字节兼容通过 8 项新增和 1 项受影响检查。435 文件边界和完整差异审查通过，178 个发布文件固定摘要，见[本轮证据](../evidence/c04a/2026-09-14/local-startup-control/README.md)。没有运行 SQL，原共享预览 6 个进程和 8 个入口/控制文件未变，自有临时进程与目录已清理。正常 PR 与远端 main 包含性另记交付记录。

该能力不替代实际清单完整性或管理员隔离。多目标恢复草稿 Core #112 / CRM #73 仍有一项必要 SQL 检查及最终清理观察待完成；本轮不改其结果。后续保留恢复宿主依赖兼容、特权写入控制、来源冻结证明与目标启用及最终执行包。实际环境未切换，0.2 字节签收、CRM11/C04B 正式验收和观察期仍开放，总体 **60%（3/5）**。

## 2026-09-14：C04A 本机 Docker 来源绑定开发验证完成

已实现本机 Docker 命名管道身份绑定、精确 loopback SQL 入口和实际来源冻结/恢复接线，保留原生请求字节兼容。16 项新增及 2 项直接受影响验证通过，包含发布 Core 工具控制 Docker 来源、原生 CRM 目标证明及恢复后业务写入；435 文件边界通过，一次集中审查没有待修复项，见[本轮证据](../evidence/c04a/2026-09-14/local-container-source/README.md)。新 Core 40 文件固定摘要，CRM 178 文件操作工具原样复用；普通 PR 与远端 main 包含性另记交付记录。

实际 Docker 只读观察确认 20 表空来源；两实例临时测试库和实际控制 Schema 均为 0。六进程共享预览及旧副本/恢复依赖清单已整理，实际数据库和服务未切换。本组件不再列为开发阻塞，不重跑未受影响旧套件。继续三份实际来源的启动路径/特权控制、前向激活和完整批准恢复包；0.2 变化字节、CRM11/C04B 正式验收和观察期仍开放，整体 **60%（3/5）**。

## 2026-09-14：C04A 首写前恢复协调开发完成

已接通 CRM 已关闭且未首写目标的永久终止，以及 Core 在目标锁持有期间恢复旧来源；原目标 Control/Audit 行保留，旧 Enable 拒绝，源精确重放允许业务恢复写入。16 项新增与 1 项直接受影响检查分阶段通过，包含两个真实发布 CLI 场景；一次集中审查的请求解析问题已定向关闭，见[本轮证据](../evidence/c04a/2026-09-14/cutover-recovery/README.md)。本节为分支合并前状态，普通 PR 及远端 main 包含性另以交付记录核对。

本组件不再列为开发阻塞，也不重复已通过且未受影响的旧测试。下一步完成实际原生/Docker 来源的启动路径与特权身份控制、路由/服务停启恢复、前向激活协调，以及绑定 0.2 字节、实际组织/目标/日历的具体执行批准包。实际冻结、终止和切换均未执行；既有预览保持。CRM11/C04B 验收与观察期仍开放，整体 **60%（3/5）**。

## 2026-09-14：C04A 实际源端冻结组件

新增 [实际来源冻结入口](../../tools/CP6.Crm.SourceFence/ACTUAL-FREEZE.md)：同一事务绑定具体数据库身份、锁内检查摘要、审核材料引用和 20 表写入保护。精确重放只返回原回执；状态漂移或缺少绑定拒绝。原 rehearsal 生命周期保持，带实际回执的恢复副本不能通过该入口重新开放。

[本次证据](../evidence/c04a/2026-09-14/actual-source-freeze/README.md)记录新增验证及共享事务抽取的直接代表回归。新增17项与受影响3项已分批通过；中间失败原件、修改与复用适用范围保留。集中审查的回执格式问题已修复并定向通过，独立发布 CLI 已完成最小夹具检查。只读核查另确认原生和 Docker 两份空 CP6DB 共用 broker GUID；Docker API 实际以 sa/sysadmin 连接，必须独立控制生命周期。

这只是受控首次冻结/回执组件交付。实际 reopen/seal 仍拒绝，须继续目标门禁与路由恢复协调、跨源排空和特权启动路径隔离；准备完整恢复包后才请求实际执行批准。0.2实际采用签收、CRM11和后续观察期继续适用。没有实际库冻结、预览替换或生产部署。整体目标 **60%（3/5）**，历史检查点保留如下。

## 2026-09-14：C04A 实际来源检查组件交付

新增独立 [inspect-actual](../../tools/CP6.Crm.SourceFence/ACTUAL-INSPECTION.md)，精确绑定本机实例/库身份，锁内计数 20 表，并采集结构、权限、模块、SQL Agent 和迁移摘要。可拒绝预期 scope 漂移；原实际库写入拒绝边界保留。新增 11 个不同自动化场景分批通过，CLR 方法绑定审查问题已修复，未重跑未受影响的成功项。

[最终独立发布与实际来源证据](../evidence/c04a/2026-09-14/actual-source-inspection/README.md)确认 CP6DB 全部旧表为空、scope 复读一致、实际 freeze 被拒绝、控制 schema 未安装、测试库和临时登录无残留。实例内观察到 5 个启用 sysadmin 主体及 1 个启用 Agent 作业，仍须分类和隔离；原始主体/会话信息保留本机，公共证据仅包含审阅摘要。

整体目标 **60%（3/5）**：C01/C02/C03 已交付；C04A/B 尚未完成。继续真实 writer/作业/权限分类与受控执行接线、目标首写和路由恢复；0.2 实际采用批准、CRM11 和后续观察周期仍适用。本次没有业务/Schema 写入、预览替换或生产操作，scope 摘要不代表批准。下方为历史检查点。

## 2026-09-13：C04A 源端控制组件与真实空来源恢复演练

CRM02 的本地合同确认已由 [CRM PR #68](https://github.com/GTX537/CP6.CRM/pull/68) 关闭，用户兼任三类负责人且暂无替补；后续技术工作不再重复索取这些确认。本次实现独立的[源端保护工具](../../tools/CP6.Crm.SourceFence/README.md)，只允许在本机独立恢复副本执行 20 表原子冻结、围栏前恢复、单调 generation/幂等重放及不可逆封口。

[本地证据](../evidence/c04a/2026-09-13/README.md)记录 30 项真实 SQL 基础回归、审查修复后 10 项定向回归、13 项输入保护，以及实际 COPY_ONLY/CHECKSUM 备份恢复后的 39 项验证。最终 Release DLL 另执行三项真实副本冒烟。未改行为复用原执行来源，不把旧结果标为新代码重跑。源库 20 表仍为空、元数据未变，未安装源端保护；副本保留供检查。

两项 P2 已修复：备份默认目录必须是无重解析点的本机固定磁盘路径；恢复写入后立即重试同一 Reopen 保持幂等。四份状态摘要移除旧 C03 技术测试金额的重复展示，以通过原有公开扫描；原验收报告、摘要与对账结论保留。

**只完成源端组件和空来源恢复演练，C04A 整体仍未关闭。** 下一步继续目标物理 Schema/CRM11 适配，并补齐实际旧写入身份与作业盘点、排空、目标首笔写入接线和路由切换/回退。当前操作身份为 sysadmin，工具明确不宣称完整写入围栏；没有修改旧映射、现有预览、业务启动、生产配置或工作流。下方条目是历史检查点。

## 2026-09-12：C03 开发交付关闭，远端 main 已核验

真实 SQL 联合回归 95/95、Core 单元/HTTP 153/153、CRM ERP 114/114 全部通过且无跳过。集中审查发现的行数/金额边界、报价与商品数量/计价单位、并发 Account 绑定、按租户重试、读取整体超时和权限覆盖均已修复；原始失败与结果哈希保留在[验证索引](../evidence/c03/2026-09-12/local-test-results.json)。

[新一轮真实 Release 消息验收](../evidence/c03/2026-09-12/transport-attempt-4/README.md)七场景通过，新建 ERP SQL 库应用 135 个正式迁移，最终三张订单已按币种完成金额对账，并核验数量与计价单位；具体金额保留在原始技术验收证据中。[本地发布证据](../evidence/c03/2026-09-12/local-release/README.md)绑定 Core `a51938f8`、CRM `b3adb17d`、864 个发布文件哈希和 3 项实际 CRM API HTTP 检查；执行后全部文件摘要一致。

C03 已通过 [Core PR #105](https://github.com/GTX537/CP6/pull/105) 合入 `a9cf3abfa8fee13bda2c8f52c115c5adbf6ed917`，通过 [CRM PR #62](https://github.com/GTX537/CP6.CRM/pull/62) 合入 `362e6b1794c36e6e4abf9cd7eb7708a76e46c467`。[远端交付记录](../evidence/c03/2026-09-12/main-delivery.json)确认任务提交包含关系、完整 Git tree 相同、发布源码未变、37 个契约文件一致及 58 个归档摘要通过。合并 head/main 均无 Actions 运行；其他保护保留，原预览未替换。

C04A 仍等待 CRM02 批准列合同/migration map、DEC-CRM-004 和真实恢复副本/围栏/回退证据；C04B 仍等待 CRM11 生产切换、采用门禁和旧表只读观察期。当前条件未满足，未启动迁移或解除旧映射。CRM06/07 产品状态接线、CRM12 与 WMS/MES 下游另行验收；本地交付不代表生产上线。下方为历史检查点。

## 2026-09-12：C03 收尾与后续前置

- 完成 C03 集中审查发现的订单行数/目标金额精度边界修复及定向回归，保留原始失败记录。
- 完成本地 Release 的源码身份、发布文件哈希和隔离 HTTP 启动验证；检查两边工作流后走正常 PR 合并，核实远端 main 包含任务提交再关闭 C03 开发交付。
- CRM06/07 接入真实 Account/Opportunity/IntegrationProcess 后完成业务状态与被阻塞订单的同事务释放；当前传输 probe 不替代产品验收。
- CRM12、WMS/MES 下游验收、C04A 生产副本迁移/围栏与回退、C04B 切换及只读期仍须各自证据和前置，不由 C03 本地传输通过自动解锁。

## 2026-09-12：C03 文档发布集合预登记

依据持续的 C 系列实现和文档授权，预登记已审查来源 `6cc6e10fae6b097735a94645d00af68b6cc5f989` 中 C03 工程说明及文档入口的精确摘要。原冻结产品、合同和 C01 文件摘要保留，完整集合匹配、缺件/漂移/混合版本拒绝继续生效；本准备任务不发布新说明或实现代码。

本地集合回归 174 项通过，受信任校验器对后续 C03 文档候选验证通过。回归的旧自动 push 假设已按现行仅手动策略修正，工作流文件未改动。后续实现仍须完成自身本地发布、正常 PR 合并与远端 main 核验；本登记不等于 C03、CRM 产品链或生产验收完成。

## 2026-09-10：后续任务采用集中审查节奏

- 接手后按 [AGENTS.md](../../AGENTS.md#审查与验证节奏强制) 执行：相关小步骤连续开发，局部验证；任务收尾集中审查；问题修复后增量复查。不要把每个文件、提交或进度更新当成正式交付。
- 纯风格和无关重构建议记录到相应后续任务，不阻塞当前交付；实质缺陷和必需门禁失败仍须解决。扩大检查时说明具体影响，未变化的成功结果保留来源并复用。
- 本规则记忆已落地；不据此恢复普通 Actions 自动运行或新增 CI 改造任务。原有业务优先级及生产发布前置条件保留。

## 2026-09-10：C01/C02 按本地验收政策完成开发交付

C02 已通过 [Core PR #101](https://github.com/GTX537/CP6/pull/101) 合入 `fed92b8f9b73638a9f8d7550418ffdd85df76010`，并通过 [CRM PR #60](https://github.com/GTX537/CP6.CRM/pull/60) 合入 `4485261addb2a4d827902a51cbc500b20a78ff08`；远端提交包含关系和完整合并 Git tree 相同均已核对。C01 的 CRM/Core 最终证据已由 PR #59 / #100 交付，Platform 最后记录由 [PR #57](https://github.com/GTX537/CP6.Platform/pull/57) 合入 `30bd23af6808d217a23878bd9437513043c52834`，未重建或覆盖七个正式 `0.10.2` 包。

C01 的 72 项实际验收、C02 的 13 个不同真实传输场景和三类各 100 个拒绝样本均有原始证据。另有三个实际 Release HTTP/消息场景、生产 Vue/Next 的真实浏览器密码/PKCE、工作区及安全 Cookie 验证；1,060 个发布文件摘要已核对。执行 SHA 与合并 SHA 分别记录，未改动的源码/依赖结果复用；报告、失败历史和覆盖索引不回写成新的执行。[交付锚点与复用边界](../devops/evidence/local-release-20260910/main-delivery.json)。

用户因 Actions 额度耗尽明确授权本地发布验收：Core 的五项及 Platform 的五项普通必需检查已移除，其他保护逐字段核对保留。Core 七个普通工作流与 Platform 主验证现在只手动触发；CRM 普通验证保持暂停。相关交付 head/main 均没有新 Actions 运行，不补造远程成功状态，正式发布和生产审批仍适用。

**C01/C02 开发交付关闭；下一阶段为独立的 C03 ERP 设计与实施。** C04A/B 仍须满足数据迁移、切换与回退前置。当前本地预览保留给 owner 检查，独立端口、四个自有数据库及专属消息项目仍在运行；没有替换既有环境或声称完成生产部署。下方待交付条目是此前历史检查点。

## 2026-09-10：C02 本地 Release 发布与实际浏览器检查通过

在用户授权的本地验收政策下，Core/CRM Release API、生产 Next Web 及同源码 Vue 产物已实际运行；三个真实 HTTP/消息场景与 Chromium 密码/PKCE、工作区和安全会话检查通过。此前 13 个真实场景及三类各 100 个拒绝样本按原执行来源复用，不重复编译或改称生产验收。发布文件逐项哈希、来源、原始失败和复现入口见 [发布检查记录](../devops/evidence/local-release-20260910/README.md)。

Core 编译的四次内存失败、Web 页面参数错误及隔离预览代理/检查脚本的失败均保留；对应问题已按范围解决。预览留给 owner 本机查看，使用独立端口、四个独立数据库与专属消息项目；原环境未替换。普通 Actions 未触发，C01 已正常交付。C02 代码和本批证据准备正常 PR 合并及远端 main 核验；完成前不关闭依赖，C03 随后推进，C04A/B 保留迁移与切换前置。

## 2026-09-10：C02 本地真实链路与 300 个拒绝样本通过，按授权继续本地发布

本地真实 SQL、Core/CRM API、密码/PKCE、服务令牌和 Dapr/Kafka 已覆盖 13 个不同场景。第六轮六项定向通过，复用第二至第四轮未受影响的通过项；五轮失败及全部样本原件保留，不改称后来完整重跑。用户禁用、令牌撤销、租户停用各 100/100 在 30 秒内拒绝访问；用户禁用 p99 599.234 ms；令牌撤销 p99 585.965 ms；租户停用 p99 590.952 ms。这些是本地受控负载结果，不代表生产性能。缺口和冲突自动源端核对、真实崩溃/丢失确认重放、普通积压下优先投递及两租户隔离均已有证据。

最新实际运行源为 Core `2210209ee50cf40467b10c9d5d6b912b9e568678`、CRM `7e3f7604af8b36b33d3dd746f023a6bd3ee272cb`，固定 Platform `0.10.2` 和契约 bundle 不变。公开证据及逐项来源在 CRM 的 `docs/delivery/c02/real-transport/2026-09-10/`；各轮资源清理通过。受影响配置/密码更新/CSRF/服务读取缓存/自动核对回归已通过，未变化的组件结果复用。没有新镜像构建、Actions 或生产部署。

用户因 Actions 额度耗尽明确授权以本地发布检查替代必需检查。Core 政策 PR #99 已集成，七个普通工作流仅手动；CRM crm-validation 已暂停，普通 PR/合并不触发 Actions。C01 原始证据与最终状态分别经 CRM PR #59 / Core PR #100 正常合入，远端包含关系及相同 Git tree 已核对。C02 已同步最新 main，继续隔离 Release 发布、实际 HTTP 检查与正常 PR 交付；完成前不关闭 C02。生产发布门禁及 C03/C04 原依赖保留；下方待交付与禁止移除检查的条目为最新授权之前的历史状态。

## 2026-09-10：C02 崩溃恢复通过，服务撤销入口与读取缓存修复

真实传输已覆盖前九项场景，包括发送端提交后崩溃、普通消息积压下优先投递、接收端 Inbox 提交后丢失确认与重复回调。第三轮在服务撤销被通用 CSRF 校验拒绝处失败，原始结果和通过的资源清理记录保留。Core 仅将使用显式客户端凭证的 `/connect/service-revocations` 精确路径纳入既有 CSRF 例外，15 项定向中间件回归通过。CRM 在读取接口返回 401/403 后移除对应旧服务令牌，让下一次有退避的对账尝试重新签发；503/409 保留缓存，4 项定向回归通过。匿名撤销拒绝、真实服务撤销和三类 100 样本拒绝时延仍待定向验收；尚未通过远端 main，也未触发 Actions。

## 2026-09-10：C02 真实业务更新修复

第二轮实际 Core/CRM/SQL/Dapr/Kafka 验收通过前七项场景，随后 PUT 用户更新因实体的必填密码约束返回 400。更新接口现使用独立请求类型，允许省略或留空密码；创建仍要求密码，名称与字段长度约束保留。5 项定向回归证明普通编辑/禁用保留密码哈希且不强制改密，显式重置仍哈希并撤销刷新令牌。实际传输中已通过的七项复用；发送端崩溃、接收端崩溃、服务撤销和三类计时仍待定向运行。失败证据保留，临时资源清理通过；C02 及受保护 main 交付尚未完成。

## 2026-09-10：C02 真实启动暴露租户配置绑定缺陷，定向修复通过

首次 Core/CRM 实际进程验收完成隔离 SQL 初始化后，Core 因读取客户端无法映射租户而退出；该次不计通过，四个临时数据库和专属 Dapr/Kafka 资源已清理。.NET 8 默认绑定器不会填充 Guid 键字典，现对启用的 C02 租户映射显式解析，拒绝无效或空映射，并继续核对每个读取客户端。实际服务注册、两租户映射、配置覆盖、无效输入和默认关闭共 7 项本地回归通过；[原始结果索引](../../eng/crm/identity-events-fixture/evidence/2026-09-10/configuration/summary.json)保留测试来源。跨服务 13 场景与三类实际撤销计时尚待继续；未触发 Actions、推送或变更分支保护。

## 2026-09-10：C02 发布字节修复与真实 Dapr/Kafka 定向验证

- CRM Inbox/投影/对账已保存于本地 `250e674`；43 项定向单元、28 项 SQL/HTTP 场景的最终覆盖、受影响认证兼容、9 项浏览器检查和 25 项来源授权检查已通过，仍非生产或完整传输验收。
- 真实 Dapr 1.18.2/Kafka 4.3.1 发现默认发布会改变 JSON 数据哈希；Core 发布端增加 `rawPayload=true`，接收端保留 `rawPayload=false`。最终三项字节/元数据探测通过，错误实际分区键被拒绝；前两次失败与错误测试预期保留在 `eng/crm/identity-events-fixture/evidence/2026-09-10/transport-byte/`。
- 仅本地编译受影响 fixture/API 并运行隔离传输探测，未触发 Actions、构建镜像或变更运行中的开发栈。此前 Docker 构建只覆盖修复前组件版本；未将其改标为当前镜像结果。
- 下一步仍为实际 Core/CRM SQL、故障恢复、优先队列及三类 30 秒传播采样。正常远端集成继续受必需检查/Actions 额度策略阻塞，完整 C02 未完成。

## 2026-09-10：C02 Core 已完成本地组件验证，CRM 与传输继续实施

- 五类固定 Schema、版本快照、普通/优先 Outbox、业务与直接 SQL 撤销、真实服务令牌记录、初始化及租户绑定读取 API 已实现。复用签名包 `[0.10.2]`，合同索引 SHA-256 为 `24df72e9446723fa4cf3ad12b7e273ff937ef9b0ed154b8c926b1bac6c7eab63`。
- 定向单元测试 123/123，最终真实 SQL/HTTPS 批次 23/23、零失败/跳过。覆盖历史迁移保真、并发版本、三种故障原子回滚、调用方外层事务、实际业务失效入口与服务 API。原始报告和前序失败保存在 [组件证据](../../eng/crm/identity-events-fixture/evidence/2026-09-10/README.md)，工作树执行事实不改标为 main 验收。
- 本地 Linux Docker 构建已成功，签名与 locked restore、发布内容及构建秘密不进入镜像已检查；后续文档修改复用结果，不重建。Actions 的仓库包读取权限尚未验证。功能默认关闭，启用要求 SQL `MultipleActiveResultSets=False` 以支持事务保存点。
- 用户已禁止普通验证触发 GitHub Actions；本地策略分支 `b483e62b` 已将七类普通流程改为手动，但受保护 main 的必需检查阻塞集成。C02 暂存于本地任务分支，不 push/建 PR 间接触发运行，不修改分支保护或伪造状态。CRM Inbox/投影/对账、实际 Dapr/Kafka 故障恢复与三类 30 秒传播样本尚未验收，完整 C02 不计完成。
- C01 真实身份验收已为 72/72；CRM 源码经 PR #58 合入 `c0205517`，合并后六项实质检查成功、汇总作业因额度限制未启动。C01 三仓证据分支保留待正常集成，不重跑已成功的真实验收。

## 2026-09-10：C02 设计与实施前置

- 按用户已确认的 [C02 设计](../superpowers/specs/2026-09-10-c02-identity-events-design.md) 与 [实施计划](../superpowers/plans/2026-09-10-c02-identity-events.md)，在 C01 最终真实身份验收完成后，依次交付 Core 原子事件/撤销/快照、CRM Inbox/投影/对账与真实 Dapr/Kafka 传播验收。
- 开发期间只运行相关局部测试，复用未变更的固定包证据；C02 完成需要实际管理请求拒绝、真实 SQL 事务与撤销时延证据。C03、C04 不因设计准备而提前标记完成。

## 2026-09-10：C01 跨仓真实验收 72/72 通过

C01 的实际密码/PKCE、服务令牌、固定 Platform/CRM 消费者、严格拒绝、缓存失效/恢复和真实密钥轮换全部通过，**72 通过、0 失败、0 跳过**。Core 实际执行源 `fb55a877de8ee8f9d27fd3bf8e73c824a21549e8`；CRM 实际执行源 `37cf0e58ff146ed58582768cf2c91e3c9fbe81cf` 经 [PR #58](https://github.com/GTX537/CP6.CRM/pull/58) 正常合入 `c02055178d96acf4c15ece8c5ba0f51b01b86f2c`，包含关系及相同代码树已核对。源 PR 七项适用 CI 通过；main 六个实施/适用性作业通过，但 GitHub 计费限制阻止汇总作业启动，整体 CI 失败。用户于 2026-09-10 因 Actions 额度耗尽明确授权本地验证替代必需检查；本批证据按正常 PR 交付，复用已完成的真实验收，不补造远程成功状态。Platform 七个不可变 `0.10.2` 包仍绑定发布源 `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，发布原件 main 为 `0944d808057c6cf138867a4d2b4b96247b79e14f`。

公开[原始 summary、JUnit 与五次尝试记录](https://github.com/GTX537/CP6.CRM/tree/main/docs/delivery/c01/real-identity-2026-09-10) 保留实际执行 SHA；成功 summary SHA-256 为 `ae779716b0edbf07b6b37dfea5774ba7d564c06739286f20efa124324d0b5014`，JUnit 为 `3003542d3d98c143df6331da9cbd047877de788bfcfbe5f53acf396ce5932ff1`。真实预发布不少于 60 秒，旧键在最后实际旧令牌到期加默认 60 秒偏差后移除；入口退出 0，自有 SQL 与进程清理完成。四轮失败不改写成成功；相同代码树和输入的实际结果复用。

用户于 2026-09-10 批准 C02 设计，现继续组织/权限/撤销事件的原子发布、CRM 投影与对账，之后推进 C03。C04A/B 保留原前置条件；本次是开发验收，不是生产部署。下方 C01 尚待完成的记录为历史检查点，以本条为当前状态。

## 2026-09-10：用户授权本地验证替代 Actions 必需检查

用户因本月 Actions 额度耗尽，进一步明确授权移除必需检查，并在本地发布检查。已仅移除 Core main 的五项 Actions required status checks，逐字段确认其他分支保护保持不变；正常 PR、会话解决、管理员约束及禁止强推/删除仍生效。七个普通工作流在合并期间暂停，配置经 [PR #99](https://github.com/GTX537/CP6/pull/99) 正常合入 `54096b3fc7f745f0199de9c20a9b7859e569d29e` 后，已恢复为仅手动入口；R2 候选、发布、部署和其他手动工作流状态未变。原始前后设置保存在 `docs/devops/evidence/local-gates-20260910/`。

配置保留普通 GitHub workflow_dispatch，Azure Artifact 桥同步改为手动，防止等待不存在的 GitHub 成功产物。本地发布只作隔离验证，保留源码/发布文件哈希与实际 HTTP 证据，不覆盖既有环境或替代生产发布门禁。远端已核对包含政策提交且 Git tree 相同，七个入口均 active 且仅 workflow_dispatch；该 head 与合并提交均没有 Actions 运行。本地 Release 发布验证继续推进。下方策略尚待集成条目为历史检查点。

## 2026-09-10：本地验证策略待受保护分支集成

- 7 条普通工作流及根 `AGENTS.md` 的调整已在独立分支准备。[现有五项必需检查与旧 PR 自动触发](../devops/LOCAL-VALIDATION-POLICY.md) 阻止无授权远程运行条件下的正常集成；不得改保护规则、伪造状态或强行合并。
- C02 继续本地实现 CRM 投影、对账和真实传输验收；复用已通过的 Core 123 项定向测试与 23 项 SQL/HTTPS 场景。远程运行一律先说明原因、范围、预计分钟数并等待新的明确授权。

## 2026-09-09：C01 服务端交付闭环，消费端修复与验收待完成

- [PR #97](https://github.com/GTX537/CP6/pull/97) 的公开文档集合登记已正常合入 `d7a8b1699cc47ebcf3f8507dd1dc1592756cedfb`，七项 PR 检查及该 main 的五条工作流均成功。下方登记和本地验证条目是历史检查点。
- [PR #96](https://github.com/GTX537/CP6/pull/96) 的最终 head `26be785313b5562a4cd9ca1aeb216c594cc8c70f` 通过七项检查，于 2026-09-09 正常合入远端 `main@22453814f7d465abbf48ff6cb03e794765e490b1`。远端祖先关系与合并前后完整 Git tree 相同已复核；服务客户端、真实单租户签发、JWKS 缓存及密钥轮换的生产者源码交付完成。
- 合并提交的 [client-contract](https://github.com/GTX537/CP6/actions/runs/34395263850)、[SQL integration](https://github.com/GTX537/CP6/actions/runs/34395263357)、[PRD](https://github.com/GTX537/CP6/actions/runs/34395264036)、[public contract](https://github.com/GTX537/CP6/actions/runs/34395264338) 和 [Space integrity](https://github.com/GTX537/CP6/actions/runs/34395263393) 均成功，共五条工作流、六个作业；合并后本地身份回归再次 116/116、零失败/跳过。先前全后端 3059/0/19 和真实 SQL/HTTP 48/48 的边界与记录继续保留。
- 真实 CP6 SQL/密码登录/PKCE、实际 CRM 客户端及固定已发布 Platform `0.8.0-alpha.2` 的跨仓诊断为 7 通过、4 失败、零跳过：ID Token 作 bearer、同一可信 RSA 签名但未知 kid、两种 audience 末尾多 `/` 均错误返回 200。另一个仅观察 CRM 正常缓存的真实元数据诊断为 6 通过、2 失败、零跳过：实际 JWKS 的 60 秒 must-revalidate 及随后的退避请求误用旧缓存；独立、明确标注的宽松缓存头用例在 899 秒接受、900 秒拒绝。两项诊断都属于修复前证据，不是 C01 验收成功。
- C01 继续实施消费端严格令牌/缓存修复，并准备新的不可变 `0.10.2` 正式包及固定真实消费验收；必须取得完整轮换、缓存/刷新/最大年龄及失败关闭的源 SHA/包摘要绑定 summary 和零跳过 JUnit 后才关闭 C01，再推进 C02/C03。C04A/B 保留迁移前置条件，本次不表示生产部署、生产密钥轮换或新包已发布。

## 2026-09-09：C01 生产者交付与跨仓验收

- [x] 服务签发、严格拒绝矩阵、真实 SQL/HTTP 和可控时钟轮换通过；116 项身份回归、48 项完整 OIDC SQL 测试零失败/跳过；生产者契约审查通过。
- [ ] 完成正常 PR/main 交付，核对远端包含全部任务提交及合并后检查。
- [ ] 使用固定真实 Platform/CRM 消费者验证两种 audience、缓存/刷新/最大陈旧期和失败关闭，取得绑定源 SHA/包版本的机器 summary 与 JUnit 后才关闭 C01；继续 C02、C03，C04A/B 保持原前置条件。下方设计待审阅为历史检查点。

## 2026-09-09：C 系列启动，C01 设计待审阅

- 下一步审阅 [C01 设计](../superpowers/specs/2026-09-09-c01-identity-design.md)，确认独立服务注册、单租户绑定和机器身份协议，再实施服务签发与轮换测试。之后完成固定真实消费者的跨仓契约验收，才可关闭 C01；C02 组织/权限/撤销事件、C03 ERP 集成依次推进，C04A/B 依原迁移前置条件实施。

## 2026-09-09：C01 公开文档完整集合登记

- C01 生产者 [PR #96](https://github.com/GTX537/CP6/pull/96) 已完成本地实现、自动化验证和独立审查；当前文档门禁要求先在受保护 main 登记完整文档集合，再公开对应内容。本登记固定已审查源 `2005c05fdf764ef21525d6374c1c9bf37f756050` 的 `c01-service-identity-20260909` 集合，仅新增服务身份运行手册摘要并更新首片文档的 C01 导航说明摘要。
- 用户于 2026-09-09 确认 C01 设计并授权继续实施。本次保持两套历史完整集合及历史审批记录，仍拒绝任意文档漂移、缺失、新文件和混合版本；受保护验证器不从候选仓库加载信任。登记分支不修改 `docs/crm` 内容；正常合入并通过 main 检查后，再由 PR #96 发布准确匹配的文档。
- 这里只处理文档登记前置，不表示生产者已经合并、完整 C01 跨仓验收完成或环境已部署。
- 本地验证：138/138 集合与触发器检查、59/59 PRD 负向检查通过；受信任验证器对旧集合和 PR #96 完整集合均通过，历史 GitHub 审批证据有效。独立规格与质量审查通过；登记 PR #97 已通过七项 PR 检查并正常合入远端 main `d7a8b1699cc47ebcf3f8507dd1dc1592756cedfb`，登记提交的 main 检查仍在运行。

## 2026-09-09：CRM 内部测试登录

- 修复匿名画像探测与全局退出后旧登录标记引起的 CSRF/过期误报，进入正常分支交付和远端验证。
- 用户继续使用现有 DEV 默认组织反馈首片行为；Docker 已由用户报告恢复，本次只修改统一登录页，不切换部署形态或扩大业务范围。
- 两组织 Pilot、跨时间保留任务观察与生产门禁仍按原计划推进。

## P10 最新接手状态：正式候选已审计，进入正常台账交付（2026-09-09 UTC）

- [x] gRPC `1.83.2` 修复 PR #93 与 exact-main 门禁成功；正式 validation 34310410625 / 1 成功，1651 项全量、19 项 gate 和原生 HIGH/CRITICAL=0。
- [x] Owner 确认 `v0.10.1-p10.1`；publisher/consumer 凭据分别修正并独立审批后，publication 34312917625 / 3 完成签名、条件提交与回读。
- [x] 独立只读 audit 34317792175 / 1 成功；三份 Artifact 原 ZIP、27 个公开内容文件和首条 hash-linked 审计决定已留存。[复核入口](../devops/p10-audit/README.md)。
- [ ] 本状态任务须经正常 PR、必需检查、合并及远端 exact-main 验证后交付；该条件完成才使 Frozen / Consumable 生效，不能用本地文档代替。
- 后续消费仍需按当前 trust/吊销、实际运行与远端身份重新验证；留存文件不是永不过期的验收凭证。WMS/CRM System release、业务 runtime、四仓兼容性、DEV/UAT/PROD 与迁移不属于本次完成范围，需要另行授权。
- 以下旧 P10 未完成勾选是不可变历史检查点，不要求重发已成功的本候选或复用旧失败源码。

## P10 当前执行链：完成 gRPC 补丁预验与正常交付，再正式验证（2026-09-09 UTC）

- [x] PR #92 已正常交付；获批 run `34303646636` 的原始扫描明确定位 cosign 内 gRPC `1.83.1` / HIGH `CVE-2026-84445`，1650 项测试通过不覆盖扫描失败。
- [x] Owner 批准仅升级 `1.83.2` 并标识新 `cp6.2`；实际第一轮两平台输出、Windows 全量 1651/1651、完整镜像原生扫描及只读/禁网运行时密码学验证通过。
- [x] 第二次独立编译的二进制/元数据与首次相同；上游 429 项测试/子测试、format、四条 actionlint 通过，原始记录已保留。
- [ ] 正常 PR/全部必需检查/合并、远端 main 核对和合并验证后，再发起一次新 exact-main validation 并请 owner 审批。保留所有报告与门禁，不直接重跑含旧依赖的源码。
- [ ] 新正式全流程成功后再取得 owner 的未用候选身份，执行 publication、只读 audit、append-only 状态决定；当前仍 Candidate / No-Go、`deployable=false`。

## P10 当前执行链：保留原生扫描失败证据，再继续正式验证（2026-09-09 UTC）

- [x] Vitest 4.1.11 修复经 PR #91 和 exact-main 五条工作流（六个作业）完成交付；实际 Web 全量和安全审计通过。
- [x] 获批 validation `34298959521` 完成真实输入及 1648/1648 测试、OCI push，确认失败位于 scan，但原报告未上传；复扫未重现阻塞发现，不推定根因。
- [x] 独立任务分支实现 scan 阶段/规则诊断与仅失败时两报告保留；两项新增回归 RED→GREEN、17 项相关测试及格式/语法验证通过，原拒绝门禁保留。
- [ ] 完成诊断补丁完整 diff 审查、PR 必需检查、正常合并、远端 main 核对和合并验证；再对新 exact main 发起正式 validation，请 owner 批准。若扫描仍失败，读取该 run 原始诊断，而非盲目升级依赖或降低阈值。
- [ ] 正式全流程成功后，取得 owner 确认的未用候选身份，完成受保护 publication、只读 audit 和 append-only 状态决定。P10 仍 Candidate / No-Go、`deployable=false`；下方待办为历史检查点。

## P10 当前执行链：修复前端审计阻塞，再正式验证/发布/审计（2026-09-09 UTC）

- [x] Owner 批准的 Linux preflight 1648/1648、零失败/跳过；PR #90 八项检查成功并合入远端 main，合并后 Windows P10 全量通过。
- [x] 定位 main run `34293825913` 的 npm 安全审计失败，独立只读审计复现 Vitest / `@vitest/mocker` 4.1.9 的两项 moderate 报告；owner 已批准升级至修复版 4.1.11。
- [x] 在最新 main 的单任务分支完成 Vitest 4.1.11 最小补丁；八个家族锁记录官方元数据对账、其他 460 个锁记录不变、Node 22 安装 dry-run 和零漏洞审计通过。
- [ ] 完成 GitHub 实际安装、类型检查、Web 单测和构建；全部必需 PR/main 检查成功后正常交付，不能把 dry-run 当作完整运行。
- [ ] 对最终 exact main 发起新的正式 validation，取得 owner 实际审批并核验全流程；随后按 owner 确认的新候选身份条件发布，再完成普通只读审计和 append-only 状态决定。
- [ ] 精简仅适用于未改动代码的重复本地验证；保留 npm 审计阈值、全部必需 CI、正式签名/漏洞/人工审批。P10 仍 Candidate / No-Go、`deployable=false`；下方旧待办是历史记录。

## P10 当前执行链：独立预检 → 正式验证 → 发布/审计（2026-09-08 UTC）

- [x] Owner 已批准独立托管 Linux 预检；分支 workflow、复用正式验证器的七包输入工具及新增测试已实现，Windows 全量 1648/1648、0 跳过。
- [x] 独立读取环境新增 owner 审批和唯一任务分支策略；原 main-only 正式候选环境不变，凭据未导出。
- [ ] 推送修复分支并由 owner 批准实际 preflight run；核对完整 Linux 测试结果和精确 source/run/attempt/TRX 身份。未通过不得合并。
- [ ] 全部 PR 必需检查通过后正常合并，再核对远端 main 包含修复、合并后冒烟和 exact-main 检查。
- [ ] 对最终 main 发起一次正式 validation，由 owner 审批并检查全流程成功；之后按 owner 选定的新候选身份发布，再只读审计和追加真实状态决定。
- [ ] P10 Frozen/Consumable 必须由完整实际证据闭环；`deployable=false`，不授权生产部署。下方“预检尚待同意/尚未实现”为历史检查点。

## P10 当前优先：在独立 Linux 环境验证发布修复（2026-09-08 UTC）

- [x] Owner 已启动 Docker；七个原业务容器运行、四依赖 healthy、Web/API 三探针 HTTP 200。下方未恢复/建议整机重启等条目为此前检查点，不再作为当前操作指令。
- [x] 重跑冻结输入：Windows 1644/1644 通过；Linux 1643 通过、1 项因 UTC 倒退失败、0 跳过。单 CPU/断网复现时间跳变；不把时区差异或单次短采样无异常当作原因排除。
- [ ] Owner 决定是否新增独立、人工审批、只读的托管 Linux 预检；其凭据和分支边界需明确，不继承发布/签名权限，不放宽现有 main-only Environment。尚未实现或配置。
- [ ] 在合适的 Linux 环境执行未经修改的全量门禁并通过，再完成正常 PR/必需检查/合并/exact-main 冒烟与检查；之后新 validation 仍需 owner 审批和完整成功。
- [ ] 候选身份仍由 owner 选择；不把本地报告、Windows 通过或代码修复提交当作候选发布完成。

## P10 当前优先：保存其他工作后处理 Windows 端点状态（2026-09-08 UTC）

- [x] Owner 批准冷备和不清空原盘的恢复；约 110 GB 数据盘、WSL 系统盘及当前配置均已复制并核验摘要，副本只读且私有保存。
- [x] 确认原数据目录联接保留；第一个临时通信目录已可恢复地改名隔离，未删除文件或修改数据路径。
- [ ] 第二个 `docker-secrets-engine` 端点目录被 Windows 拒绝重命名，虽无 Docker 进程且账户已有完全控制权限。建议 owner 保存其他程序工作后尝试完整 Windows 重启；本任务未重启整机、改 ACL、接管所有权或强删端点。
- [ ] 先恢复原七个业务容器并核对数据库/服务健康，再重验原生时钟和 Linux full suite；不可仅凭冷备 hash 声称数据完整或 P10 通过。PR/合并/新 validation 仍暂停。

下方“冷备待批准/未复制”等为此前检查点，当前状态以本节为准。

## P10 当前优先：确认 Docker 错误窗口操作并恢复业务（2026-09-08 UTC）

- [x] 获 owner 明确批准后正常停止 Docker、完整停止 WSL2；未修改时间或配置。
- [x] Owner 已确认点击错误窗口的恢复出厂选项。任务未执行该动作；原数据盘大小、修改时间未变且 VHDX 标识可读，内容仍未验证。
- [ ] 先获准为原盘制作并校验约 110 GB 冷备，再考虑保留原盘的修复；目前未复制、挂载、修复或改写数据盘。不要把文件存在当作完整性结论，不自行重置、重装或删除运行时端点。
- [ ] 处理已定位的 `dockerInference` 启动错误并恢复原七个业务容器，核对既有数据与 Web/API 健康；目前仍未恢复。
- [ ] 恢复后重新验证原生时钟和同一冻结输入的 Linux 全量 suite。此前 1643 通过 / 1 失败仍是最新全量结果；PR/合并/新 validation 继续暂停。

下方批准待办和业务恢复状态是更早检查点，当前以本节为准。

## P10 当前阻塞：Docker 重启不足以恢复稳定时钟（2026-09-08 UTC）

- [x] 获 owner 单独批准后重启 Docker Desktop；七个业务容器恢复，四项容器健康检查和 Web/API 三项 HTTP 200 通过。
- [x] 对同一冻结输入完成 Linux full suite 重验并保留失败：1643 通过 / 1 失败 / 0 跳过。独立 Linux 原生时钟采样也复现跳变；不能把首次十秒零跳变当作环境已修复。
- [ ] 如继续重置整个 WSL2 环境，先获 owner 对停止当前 Ubuntu 和 Docker 的明确批准；该范围超出此前 Docker-only 重启。未执行 `wsl --shutdown`，未改变系统时间、WSL 配置、时钟源或时间门禁。
- [ ] 在稳定环境完成 Linux 全量验证，再走正常 PR、全部 PR/exact-main 检查与合并冒烟；当前不合并、不 dispatch 已知受阻组合。发布仍需成功的受保护 validation、owner 候选 Tag 选择、publication/audit，P10 仍 Candidate / No-Go。

下方重启前待办保留为历史，当前状态以本节为准。

## P10 最新待办：稳定 Linux 时钟后交付安全构建（2026-09-08 UTC）

- [x] Owner 批准可复现 cosign 安全衍生构建及本地 Docker 诊断；两次独立构建摘要一致，完整实际镜像 HIGH/CRITICAL 均为 0，所有其他发现保留。
- [x] Windows 1644/1644、上游 429 个测试/子测试、十项 shell 输入保护及实际只读镜像七项密码学检查通过；签名信任、漏洞阈值和本地报告拒绝策略不变。
- [ ] 先处理实际观察到的 Docker/.NET UTC 前后跳变，再完成 Linux 全量重验；当前 1589 通过 / 55 失败，不能合并失败门禁。重启 Docker Desktop 将中断七个业务容器，须 owner 明确授权；不得自行改变系统时间、重启全部 WSL 或放宽证据时间校验。
- [ ] 正常 PR、完整 diff 审查、所有 PR/exact-main 检查和合并冒烟通过后，再请求一个新 exact-main validation 及 owner 的真实 Environment 审批。
- [ ] 新 validation 完整成功后，仍需 owner 选择未使用的候选 Tag，再完成受保护 publication、普通只读 audit 与跨仓最终判定。旧失败、正式包及现有生产门禁不改写，P10 仍 Candidate / No-Go。

## P10 最新阻塞：cosign 原生扫描不满足漏洞门禁（2026-09-08 UTC）

- [x] 安全诊断 PR #89 正常合入 `a41711dd55a093ab0ed127d599e0e7bcf11d548d` 且七项 PR/五个 main 工作流成功；新 run `34234554610` attempt 1 定位 CRM pull-request 403，owner 调整后 attempt 2 完成真实输入收集、1634 项全量测试和 OCI push。
- [x] 复现并在任务分支修正 Trivy `--image-src registry` 为 `remote`；8/8 wiring 回归和已有镜像的原生远端扫描建立证据。未合入 main，不算完整交付。
- [ ] 先由 owner 决定 cosign 依赖修复路径：当前官方最新 3.1.3 内存在 1 CRITICAL / 14 HIGH，尚无更新官方二进制。自建补丁版或改变分发来源需要受审供应链决定，禁止忽略、过滤或移除被扫描工具规避门禁。
- [ ] 完成获批修复的真实扫描、签名兼容与完整 suite，再按正常 PR/exact-main 门禁交付；只有已知阻塞排除后才请求新 validation 和 owner 审批，不重复运行已知失败的组合。
- [ ] 原 run `34234554610` attempt 2 的 sign/finalize/artifact 全部跳过；旧失败 run/正式包保留，不能把此次本地报告视为 hosted 成功或候选证据。其余历史 run 的未知状态不回溯改写。
- [ ] 真实 validation 成功后，仍须选择未使用的候选 Tag，完成受保护 publication 和普通只读 audit 才能评估 Frozen；当前 Candidate / No-Go、deployable=false。

## CRM 首片后续环境接入（2026-09-08 UTC）

首片代码、公开文档同步、独立复核、真实跨仓浏览器验证及两仓库实现 PR/main 门禁已完成，见[完成记录](05-Completed.md)和[当前远端证据](PROJECT_STATE.md)。

- 在目标测试环境由真实目录选择 TenantId、slug/region/CRM entitlement，注入 RSA/机密客户端配置，执行授权账本与持久化浏览器会话两项前向迁移，验证该环境的运行与撤销行为。桥默认关闭；旧会话需重新完整登录后获得浏览器链绑定，不能将旧 refresh 轮换计为重新认证。
- 真实客户开通、商业 Provisioning、C01/C02 全架构、Pilot/UAT/生产批准仍各自按原门禁完成；本地合成 fixture 和开发 CI 不替代环境或生产验收。
- `docs/crm` 的已登记文档是受审快照；后续内容变更须重新审查登记，不能放宽摘要或路径检查。历史产品批准和 M0 No-Go 保持原约束。

## 文档维护约定（2026-09-07 整理后）

- 新任务先从 [文档中心](../README.md) 找现有目录，并遵循 [维护规则](../CONTRIBUTING.md)；维护主题入口及本地链接，避免继续向 `docs/` 根目录堆放文件。
- 31 个散落文件的分类与导航已经处理，不重复迁移。旧正文、同名 Word 与运行版本的内容一致性需在对应模块改动时核验；此次目录整理未将它们标记为最新版本。
- `docs/file/` 混有已跟踪原始资产与本地忽略资料，后续如需去重或清理，应逐项确认来源与保留需求。历史手册导出脚本缺源、财务单页 SOP 尚未成目录均已在导航中说明，没有用占位文件冒充补齐。

## P2：Space Editor Konva Layer 性能基线

- 版本化几何兼容修复后的真实 Docker 场景已可用，但 Konva 仍提示 Stage 有 6/7 个 Layer，超过其 3～5 个建议值；当前未观察到功能错误，不能把该警告与已关闭的 `t is not iterable` 混为同一故障。
- 后续如优化，应先记录标准场景的加载时间、拖拽/缩放帧率、命中测试和对象规模，再评估合并 Layer 或改用 Group；必须保持选择、碰撞着色、标注、Ghost 和 Transformer 的层级语义，并补浏览器性能回归。
- Schema 1/旧版几何兼容、画布渲染、碰撞、吸附、模板放置和巷道统计已于 2026-08-30 关闭，不要重复实现。

## P1：Space Editor 反向建模文案占位符

- 当前 `BindCodesDialog.vue` 的两处既有文案直接包含字面量 `{diff}`，实际对话框会显示未插值占位符；该代码可追溯到本次工具反馈任务之前，未影响本轮按钮可操作性与对话框打开验收。
- `diff` 已由 `computeMismatch(totalSlots, unplacedList.length)` 计算并传入模板；后续独立任务应建立稳定的 i18n 键与插值合同，补五语言及组件正反向测试后修复，不得用固定文本或空值掩盖真实差异。
- Space Editor 五工具提示、光标、单选旋转手柄、多选时旋转手柄不显示且不提交、加载/卸载竞态、反向建模前置提示和导出反馈已于 2026-08-30 验收，不要重复实现。

## P0：Platform P08+ 与后续跨仓合同

- P08 已完成 S00-S06 并达到 `Frozen / Consumable`：`0.8.0-alpha.2` 不可变发布、CRM 固定版本黑盒消费、前向纠正、Platform 对账、公共 S05、Platform S06 与 CRM 最终传播均有精确 PR/head/merge/run/job/artifact/hash 记录。保留历史 Candidate 记录，不再重复执行 P08。
- P09 已完成 S01-S06 并达到 `Frozen / Consumable`：`CP6.Platform.Deployment 0.9.0-alpha.1` 的实现、一次性发布、Registry 下载匹配、CRM 9/9 黑盒与 72/72 .NET、公共候选同步、Platform 最终审计、CRM 最终传播以及各自 exact-main/真实 SQL 证据均有精确身份。保留历史 Candidate 记录，不再重复执行 P09。
- P10 外部 R2/cosign 前置与正式七包 `0.10.1`/CRM S05 的精确证据已进入当前 S06 输入；不要重做历史 `0.10.0` 或重复轮换已有 Environment/公钥/凭据。源、run 和 hash 见[当前台账](PROJECT_STATE.md)及[P10 参考](../devops/P10-PLATFORM-REFERENCE.md)。正式作者为 `PinnedSelfSigned`，内部信任不等于 public CA trust。
- S06 实现已由 PR #87 合入 `main@af0154782c8aa73eae47c083a884e2a404ef7b64`。实际 validation run `34221083970` attempt 1 为 feed 权限失败，attempt 2 为重跑记录时间校验失败；两次均获批准但没有候选。六个私有包的 CP6 Read 授权由 owner 报告已补齐，仍需新的实际下载验证。
- [重跑时间修复](../superpowers/plans/2026-09-08-p10-rerun-chronology.md)已通过本地完整 1615/1615 无跳过测试和 format。P10 当前下一步是正常 PR/exact-main 交付，然后对新的 main 发起 owner 批准的 validation，再同源条件 publication 和完成后的只读 audit。原始失败记录保留，不能重跑旧源码验收新修复，也不删除时间门禁或绕过审批。
- 最终保留 append-only 内容寻址审计条目，绑定七包、CRM、候选/Locator/OCI hash、trust 版本及真实工作流/artifact，再同步四份台账并核对远端 main。之前 P10 整体保持 `Candidate / No-Go`；不提前声明 Frozen / Consumable。P02–P09 已冻结；P10 仅为 `deployable=false` 的 Platform 引用候选，不授权 System release、公有 bucket 或任何生产部署。C01/C02/CRM03、CRM-F3-CONTRACT/Worker 与真实环境仍按各自任务完成。
- 下一项异步业务前置是 CRM-F3-CONTRACT/C02 业务事件与单独授权的 Worker slice；登录仍需 C01/C02/CRM03、获批的实际 CRM Gateway route 及 P09 后端隔离。每项必须有版本化 locator、实现、负向测试、不可变产物及生产者/消费者门禁，不能因 P07 完成而提前启用运行时订阅、Gateway/auth、真实登录或业务切片。

## P0：CRM V1 公开同步与 M0

- `CP6-SAAS-V1-PUBLIC-CONTRACT` 已由 ProgramOwner 对精确摘要批准并同步为 Complete；下一步把公开 merge commit、摘要和审批证据回写私有聚合记录。公开同步完成本身不构成 M0 Go。
- M0 唯一人类批准角色是 `ProgramOwner`。依次关闭 `DEC-001`、`DEC-003` 至 `DEC-009` 的合同和专业证据，复核 `DEC-000`/`DEC-002`，并强制 Critical/High 清零、分支保护及必需检查；SQL 容量模型和真实 Pilot cohort 未冻结时必须保持 Pending。
- 私有 `CP6.CRM` 仍因 GitHub 账户方案限制无法启用 required checks；在 GitHub Pro 生效并回读保护规则前，M0 必须保持 No-Go。只有 M0 Go 后才能解锁 CRM01。

## P0：白天临时家庭测试环境的外部边界

- 本机完整编译内存风险已通过 GitHub hosted build + Azure 轻量 Artifact 桥关闭；main #118、自动关闭门 #119、Manual #120/#121 均已成功，三次手动验收达到 3/3。继续保持本机 CI 不编译，除非另有等价隔离与容量证明。
- 本机必须保持开机、未睡眠，且 Docker Desktop 与网络正常；这是当前白天临时测试方案的可用性边界。若需要夜间、无人值守或稳定 SLA，仍须另选真实云主机/托管容器平台并完成生产部署设计，不能把本机 Tunnel 描述为高可用云部署。
- #95/#120/#121 手动 3/3、#129 低内存失败关闭、#131 Stage retry 与 #132→#133 最终自动发布均已验收；600 秒 readiness、备份/VERIFYONLY、Deploy、健康/身份、attempt-aware 证据 Artifact 和根环境零漂移已由真实 Run 证明。`CP6_DEV_AUTO_DEPLOY_ENABLED=true` 继续生效；任何旧版本手动回退前必须先关闭自动。
- 首次切换 `cp6.uk` 前，运行 `Invoke-Cp6PublicTunnel.ps1 -Action Validate`、显式停止旧 `cp6-cloudflared`、启动 `cp6-public-tunnel` 并核对完整 SHA；确认后才设置 `CP6_DEV_PUBLIC_VERIFICATION_ENABLED=true`。旧/新 connector 禁止同时运行。
- 给同事开放测试前，确认 `cp6-dev` 的 `19991`/`18080` 与公网 release identity 一致；同事只使用 `https://cp6.uk` 的应用账号，不共享 `.env`、Tunnel JSON、数据库/RabbitMQ/Kafka 管理端口或基础设施凭证。根 `cp6` 继续作为私人开发环境。
- Cloudflare Workers 的 `estimate` Git 集成仍需在 Cloudflare 控制台单独断开或改正 Build 配置。它与 `cp6-cloudflared` Tunnel 不在同一部署链；当前家庭测试服务器不依赖 `estimate`，也没有修复其外部构建失败。
- `MSSQLLaunchpad$KOUSQLSERVER`、`SQLPBENGINE$KOUSQLSERVER`、`SQLPBDMS$KOUSQLSERVER` 在故障恢复后保持 Stopped，但 StartMode 仍为 Automatic。确认 CP6 与其他本机工作负载均不使用这些功能后，另立管理员任务决定是否禁用；Pipeline 不得自行修改 Windows 服务启动类型。

## P0：整顿后的仓库治理与续开发边界

- 登录体验恢复已关闭：大幅模板重排已完成组件、全量 Web、类型、生产构建和桌面/移动浏览器验收；折叠 Tenant 焦点、虚假健康状态、语言语义与并发认证问题均有回归覆盖。后续新增实时服务状态时必须接入真实健康检查与失败/未知状态，不能恢复静态“正常”宣称。
- Kafka Dispose 恢复已关闭：刷新异常仍释放 producer、关闭异常只告警不阻断 Host、剩余队列可观测且重复调用幂等；4 个聚焦行为测试和 `CP6.Tests` 全量回归通过。
- 日期时间恢复的 P4/P5 已关闭：不恢复多余且弱类型的 Vue shim；普通业务日期时间固定到分钟精度并完成五语言回归。若后续审计日志明确要求秒/毫秒，必须新建立独立精确格式任务，不得修改全局 `long` 合同。
- CRM 旧 Draft PR #7 已关闭，替代 PR #8 已合并且公共契约主线冒烟通过。Cloudflare Workers `estimate` 外部失败继续单独归因，不影响受保护的 CRM 合同、Windows/Web、Android 和 SQL 必需检查。
- 完成整顿后把本机归档复制到第二介质，再考虑清理 `D:\CP6-archives\2026-08-24-branch-consolidation`；在此之前禁止删除 bundle、patch、原始未跟踪文件或 SHA-256 清单。

## P1：Space Studio V1 GA 后边界与运营增强

- Space Editor 自由视口缺陷已关闭；既有七层 Konva 性能告警仍是独立性能评估项，只有建立场景规模基准并证明收益后才允许重构图层结构。
- 当前正式派生状态为 `GaReady` / 100%：两类外部输入 Complete、WP0～WP8 全部 Accepted、唯一 DeliveryOwner Signed，Core GA 已无剩余阻断门禁。不要重复 WP2、WP5、WP6/WP8、Backup、Pilot、本地 OS Firewall 或冻结黄金集工作。
- Development V1 的仓库/开发环境功能已 100% 结案，不再新增开发版功能 Gate。正式 Core GA 使用 Schema 3；独立 Backup 与双仓 14 天 Pilot 已转为 GA 后增强。不得用 DevelopmentSeed、未批准 Provider、Mock 或 fixture 冒充正式发布演练。
- 唯一 DeliveryOwner `BUBAO.GAO` 已完成最终签署，Kickoff/目标 GA 为 `2026-08-27` / `2026-09-27`；同一人拥有并接受全部输入与 Gate。单人原创数据使用 `ApprovedOriginalWork`，不再追问不存在的客户或第二复核人。
- 20 份仓库外原创 AC1032 CAD 候选已经冻结并登记为 Complete：10 DWG/10 DXF、10/5/5、L1～L5 各 4，逐份授权/脱敏/答案/问题/Mapping/规则/复核证据齐全，产品 Converter 20/20 Pass。原始 CAD 不入 Git；后续必须使用同一 Source Set SHA `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`。
- 不再强制 ODA 或第二供应商。`BUBAO.GAO` 已选择并批准当前 AutoCAD 2025 Core Console 为唯一 Primary；本机 V1 的许可、精确版本/哈希、本地安全边界、保留/删除、正式 SemVer Worker、转换评测和 86/100 资格评分均已版本化并接受。如果未来改用 ODA、云服务或远程 Worker，再按其实际部署边界补许可证、身份、证书、Secret、网络与 SaaS 批准。
- 正式 `1.0.0` 已从精确 `main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` 封存并对 20 份 CAD 双跑通过，99.727873% 支持、P95 4.281 秒、SourceRef/Blocking/残留为 0；报告 SHA 为 `97a9ff7f7cbd60f2c2ea34a5b16e0d645823d94980cd43581dca7129e0373350`。WP7 已在其上补齐业务质量、人工操作和 Ready 性能证据；两份证据共同构成当前 Primary 基线。
- LM-FR-001/WP1 已 Complete/Accepted：正式 Manifest 绑定 `main@b0164a15`、8 个测试源 Git Blob/SHA-256、SQL Server 20/20 无跳过和 Web 25/25；Blank/模板/完整编码仓库及 Lease/双 Revision/Idempotency 已由 `BUBAO.GAO` 可重复自验收。确定性测试数据不宣称生产数据，生产发布仍使用独立 Release/CD 门禁。
- WP2 已 Complete/Accepted：授权真实 DWG/DXF 经冻结 AutoCAD Primary 重新转换，在 SQL Server 产品链完成显式 Floor/Unit/Transform/Mapping、sealed Preparation、Parse Start、幂等重放与篡改零写入；后端 21/21、Web 14/14、Worker 残留 0。后续只在 Source Set、Provider/Worker、CAD Start 合同或应用源变化时升版重跑。
- WP4 已 Complete/Accepted：授权真实 DWG/DXF Primary Package、产品自身生成并解析的受控 XLSX、受控 PDF/PNG 与空白画布已绑定同一 Draft/Typed Changeset 正式 Manifest；SQL Server 完整套件 465/465、0 failed、0 skipped。Published-only Viewer 与发布恢复/安全边界已分别由 WP5 和 WP6/WP8 关闭；仍不包含生产数据或生产 WMS 声明。
- AutoCAD 2025 Core Console 已以正式 `1.0.0` Worker 覆盖 DWG/DXF；WP7 已用同一 20 份 Source Set 完成业务准确率/精确率/Wilson、人工减少率、Holdout Blocking 和 50 MiB/Ready P95 并正式接受。后续三路径与发布演练必须复用精确 Provider Version，不得反向修改 WP7 冻结规则。
- 当前直接评测无网络监听、无业务凭据且临时 CAD 已清除；`BUBAO.GAO` 已接受本地 V1 不以 OS Firewall 出站 Deny 阻断。该口径不得外推为生产禁网、mTLS 或 SaaS 安全证明；若改为远程/生产部署，必须另行提供这些证据。
- 单人开发可使用 `00001`～`00005` 的 `DevelopmentSeed` 完成本地角色切换与权限测试；这些虚拟编号不能冒充真实 Owner、正式证据接受人或 `DeliveryOwner`，但正式 GA 不再要求团队人数或多角色独立签字。
- 核心 GA 当前派生结果为 `GaReady`：两类外部输入、WP0～WP8 和唯一 DeliveryOwner 签署均已闭环。下一步如需上线，另行推进 Release/CD 生产候选、环境审批、生产 WMS 窗口和部署验证；不得把 Core GA 结案写成已上线。
- WP3 已以精确 AutoCAD Primary、本机受控边界、正式 SemVer Release、DWG/DXF 双格式评测和 86/100 评分结案。评分工具未写 Site 配置；后续发布演练只能通过受控接口写入同一 Provider Version，Backup 另列 GA 后韧性任务。
- Site 认证、运行注册、Preparation 输出和当前 Parse v5 已绑定同一 Provider Version；真实适配器注册必须使用被评分和批准的精确版本，升级 Worker 前必须重新评分、认证并替换 Site 配置，不得在同一 Provider Key 下静默换版。历史空版本认证按设计失效，不能手工回填猜测值。
- Preparation → Parse 的 Mapping Replay Snapshot 与 v5 payload 已完成；真实 Provider 适配器必须加载快照绑定的不可变 Profile ID/Version、核对 Definition Hash、使用完整 Layer Overrides 重建 Mapping Preview，并在输出语义工件前执行 `SpaceCadMappingReplaySnapshot.ValidateReplay`。不得只信任期望 Preview Hash、忽略覆盖内容或让 Worker 使用当前 Profile 代替冻结版本。
- `CP6.Space.CadExperiment qualify-providers` 已在同一 20 份授权黄金集、冻结 Worker 和规则上完成：Primary 86/100，资格选择 SHA `d7b9645d915f28e165209b71f69386305711301a6a2fecf7422c15cbcc2a0faa`。后续升级版本必须重新评分，示例或人工改写 JSON 仍不算证据。
- 获批准的本地 Primary DWG/DXF Provider 已实测并接受。远程运行注册继续默认关闭；若启用远程/生产模式，没有对应部署批准 Manifest、身份和 Secret 引用时能力接口必须失败关闭。
- `SpaceCadProviderSqlServerTests` 已在 SQL Server LocalDB 3/3、0 skipped，关闭并发替换、唯一 Current Revision、历史追加、认证不可变、旧资格/版本失败关闭和迁移幂等。正式验收可在明确标识的受控 Release Rehearsal SQL Server 环境执行，不要求生产部署，但必须绑定真实 Primary 和不可变证据。
- CAD 起始向导、sealed Preparation、parse start fence、Site 能力检查和 Rack/Element 画布拖动精调已完成仓库内闭环；DeliveryOwner 的端到端、Viewer 可访问性和发布演练证据已接受，不要求独立人员签字。
- WP4 三路径正式证据已接受并冻结；后续只在 Source Set、Primary Version、应用基线或三路径合同变化时升版本重跑，不把 WP8 的生产 WMS/发布演练反向塞回 WP4。
- WP7 已 Complete/Accepted：正式 Manifest 绑定同一 Source Set/Worker、冻结规则、业务总体与 OOS 指标、Holdout Blocking、50 MiB/Ready 20 次稳定观察及应用提交。未来只有数据、答案、Provider、Worker、Parser、规则或应用 Commit 改变时才升版本重跑。
- WP5 已 Complete/Accepted：Current Published-only 边界、硬件 WebGL2 30 次冷启动、两个视口、键盘、Chromium Accessibility Tree 与 4.5:1 对比度均已由 DeliveryOwner 接受。后续只有生产入口、性能输入/预算或可达合同变化时才升版重跑；不要把 WP6/WP8 的 WMS/恢复/安全演练反向塞回 WP5。
- WP6/WP8 已在同一次受控发布演练中完成 SQL Server + CP6 WMS 发布、部分写入对账、幂等重试、旧 Published 持续服务、备份恢复、签名 JWT HTTP 负向和 15/240 分钟恢复门禁，并由 `BUBAO.GAO` 单一签署。生产部署、生产 WMS 时间窗口、Backup 和现场 Pilot 继续作为独立增强项。
## 已完成：OpenAPI 原生客户端漂移门禁

- PowerShell 版本差异与全局无关 schema 导致的假阳性已消除；门禁现在只哈希原生客户端路径及递归可达 schema，并使用 Node.js 稳定规范化。
- Node 20/22 单测、真实 Swagger check、.NET/Client/Web 和 R2 source gate 均已通过。该项不再作为 CRM PR #5 的归因问题；修复合入 `main` 后应更新 PR #5 基线并重跑 GitHub Actions。

## 已完成：Release/CD 仓库与平台工程建设

- GitHub PR/main 验证、Azure 轻量 Artifact 桥、DEV 自动链、GHCR/GitHub R2 唯一权威、生产候选/部署工作流与模板、Azure Shadow S0 均已闭环；PR #32、`main@9009abe6` 和 Azure Definition #5 / Run #145 构成最新证据。
- Phase 1 剩余治理项已关闭：GitHub 是唯一 PR 绿灯，Azure 保持 `pr: none`；CI/R2/Space 门禁责任矩阵和 self-hosted Agent 运维/隔离规则已写入结案报告。
- 该项不再作为长期 P0。完整结案口径与机器证据见 `docs/devops/RELEASE-CD-ENGINEERING-CLOSEOUT.md` 和 `release-cd-engineering-closeout.json`。

## P0：首个 R2 生产发行执行（事件触发）

- 当前没有可执行的 S1 或 PROD 任务：GitHub 无 R2 Release、受保护版本 Tag、R2 workflow Run、Environment 或仓库 Secret；`v1.0.0` 为 Draft，20 项生产/签名/设备/Pilot 输入均 Pending，Freeze gate 按预期失败关闭。
- 真实 Owner 批准并补齐 `candidate.yaml` 后，依次执行 Freeze → `vX.Y.Z` protected Tag → R2 candidate → Compose DEV/UAT → R2A/R2B；不得创建模拟 Secret、空批准或虚构候选来“结案”。
- 首个权威 candidate result/manifest 出现后，另开 S1 只读元数据任务；S1 之后的 GHCR digest、SBOM/Trivy 对比和三个候选等价报告继续逐卡验收。
- 当前 7 份 DEV `.bak` 不自动删除；保留数量、最小保留期、磁盘告警和恢复证据由独立运维任务批准后再实现清理策略。
- 全阶段继续遵守 Build once、同 digest 推广、前向迁移、环境侧审批、CI/Deploy 身份分离和不可变证据规则。

## P0：CRM V1 端到端交付

- 干净分支中的完整脱敏 `docs/crm/CRM-V1-PRD.md` v0.2 摘要 `5e646cc8e394c74c35f9716216be1d12fa5f4f7210e42d8d52ab9b86f4528a3a` 已由唯一 `ProgramOwner` 对候选 commit/blob 和五项产品结论批准；三次未合并预审批均已作废，不得复用。任何改变 V1 范围、状态语义、商业规则或数据主权的修改必须升版本并重新审批。
- `docs/crm/**` 已作为完整公开披露面自动发现并内容寻址；后续新增或修改 CRM 公开文档必须走受控清单/摘要更新和对应审批，不得在项目记忆或新文件中旁路恢复私有 Pilot/商业数字。`crm-v1-prd` 进入 `main` 后必须保持为受保护 base 产生的 required context，不能以 PR head 自带验证器替代。
- Public Contract Sync 已由 PR #8 完成并合入主线；继续以公开摘要 `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9` 作为工程边界，不得选择性恢复历史三仓范围。
- R00 已 Accepted 且公开镜像 Complete。M0 继续关闭 Azure SQL/Emergency Intake、System Manifest 整体回退、各专业证据 DRI、Pilot cohort、Observation Gate、Critical/High、私有仓保护和必需检查；任一缺失即 No-Go。
- 当前 `main` 的 20 表、状态机、迁移、6 个禁用菜单和 22 个动作只作为迁移源与兼容语义；私有 `GTX537/CP6.CRM` 已存在但仍为 docs-only，不得把仓库存在或菜单种子描述为应用开工。
- M0 Go 后先交付每组织数据库、身份/授权/Entitlement 投影、Website/Manual Intake 和 Lead Pilot：SLA 队列、分配/移交、客户面对型 Activity、412 草稿恢复、两租户负向和真实 SQL/Kafka/Dapr 性能 Smoke。Pilot UAT 通过前不铺开完整菜单。
- Pilot 后交付 Account/Contact、转换、Opportunity、CP6 ERP/ExternalEvidence、Dashboard/报表、Import/Export、Site/CMS/Offering、Portal 商业协作和五语言；所有读写共享 Organization、DataScope、PII、Entitlement、幂等和审计语义。
- CRM09/对应产品切片开始前批准公开站点、管理台和移动端关键流程高保真稿；完整 UAT 使用真实 C03 与隔离 ERP SQL，Mock 只用于单元测试。
- 单次切换后依次完成设计伙伴、Web GA、移动 GA、Lead Adoption、Full Journey 和最终采用门禁；精确推广时间表与数值窗口保留在私有采用 Manifest。技术绿灯、部署成功或菜单可见都不能关闭 V1 Epic。

## 已完成：Space `CodeEngineService` Zone 级 rackSeq

- 批量 `GenerateAsync` 与单格 `GenSingleAsync` 已复用同一套 Zone 级 `(X, Y, Id)` 确定性货架排序；单格生成不再把非首架简化为序号 `1`。
- CodeEngine/LocationPublish 聚焦回归 55/55、CP6.Tests 2843 passed / 19 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error；新增排序路径覆盖率审计 8/8，该项不再是跨波待办。

## 已完成：FIN BudgetLine 版本级并发控制

- `BudgetVersion.RowVersion` 已成为预算行聚合并发边界；新增、编辑、删除和 Excel 确认导入必须携带客户端最后读到的版本令牌，任一预算行写入都会推进共享版本令牌。
- 不同预算桶的两个写者也会在同一版本令牌上串行化；陈旧令牌统一返回 `E-A5-CONCURRENCY-001`，前端冲突或成功后同时刷新版本和行令牌。Excel 确认导入参与单一事务，不再忽略内部 upsert 失败或留下部分提交。
- 门禁为 FIN 303 passed / 1 个既存 SQLite 限制项 skipped；本机 `KOUSQLSERVER` 原生 `rowversion` 用例 1/1、前端令牌合同 3/3、Vue type-check、WebApi Release 0 warning / 0 error。该项不再是跨波待办。

## 已完成：PLAN/PUB Attachment 宿主业务权限

- Attachment 继续作为无独立页面的横切组件，不新增暗菜单；六个读写入口已按 `BizType` 回查宿主菜单，缺省失败关闭，rebind 另校验草稿上传人。
- `PubUpload` 已要求宿主 `writePermission` action key，无写权限时隐藏上传/删除，后端权限不依赖前端隐藏。
- 后端聚焦 21/21、OpenAPI 30/30、CP6.Tests 2841 passed / 18 environment-gated skipped / 0 failed、前端聚焦 3/3、全量 716/716、Vue type-check 与 production build 通过、WebApi Release 0 warning / 0 error。该项不再是待办。

## 已完成：分支优先规范与本地配置优先级

- `main@e4e33364` 已包含仓库级分支优先规则；后续任务除有记录的技术例外外，均需独立分支、验证后合并并推送。
- `e3bf2420` 已修复 `appsettings.Local.json` 的配置源插入边界，只在无前缀环境变量源前插入，环境变量和命令行继续保持最高优先级。
- 配置聚焦 4/4、OpenAPI 30/30、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。该项不再是待办。

## 已完成：WF 通知定向用户推送

- 当前运行链路由 `PersistentWfNotifier` 在事务内写 outbox，提交后由 `WfNotificationDispatchWorker` 以 `Clients.User(userId)` 定向派送；`NotifyHub` 要求认证。
- 未注册的 `SignalRWfNotifier` 及不可达的 `Clients.All` 回退已经删除，项目中不再存在工作流通知广播后由客户端过滤的路径。
- 门禁为通知聚焦 13/13、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。该项不再是待办。

## 已完成：`main` 同步与 P2.5 受控整合

- 远端 `main` 的文档同步基线 `e4e33364` 已包含 PR #2 的 `8045d872`、P2.5 受控整合 `030a97b9`、Space 权限/文档对齐 `b2a91680` 和分支优先流程；原始 5 个冲突已按权威边界解决，Docker Cookie 修复已由 `0fc6f529` 等价纳入。
- Analytics Control Tower 原始提交 `9b48ffbb` / `dd6637ea` 已从当前 `main` 选择性整合：保留 E10 Runtime/Viewer 真相源，加入独立控制塔、实时脏库位批处理、分析配置、定时 ABC 快照、容量发布和共享 ABC 分类器。
- 原 `20260720035903` 迁移未进入主线；已基于当前 ModelSnapshot 生成 `20260809092206_SpaceAnalyticsControlTowerCurrent`，只创建两张分析表和三个索引。
- 客户端心跳时序断言已稳定：测试周期与“事件立即唤醒”解耦，定向 50/50、客户端全量 71/71；生产心跳实现未修改。
- `deploy/production/sql/main-sync-20260808` 已包含 14 个 Core + 36 个 Space 幂等迁移、preflight/postflight；LocalDB 从远端 main 基线双执行 2/2，history 14/36，51083/51000/51020 失败关闭通过。仍需在生产备份恢复副本重复同一演练。
- P2.5 门禁已通过 WebApi build、前端 type-check/生产 build、全量 Vitest、Space UnitTests 和 CP6.Tests 并进入远端 `main`；标签、R2 候选和生产部署仍需另行审批。
- 完整依据：`docs/space/reports/2026-08-08-main-merge-readiness.md`。

## 已完成：E13 无锁 Zone 父关系确定性推导

- `d19a5300` 已实现 `warehouse-rule-only-v2`：无人工父关系锁的 Aisle/Rack 仅在一个确定性 Zone Polygon 完整包含子几何时推导 `relations.zoneSourceKey`；零候选和多候选继续 Blocking，不猜测。
- 人工锁、确定性规则、AI 的优先级保持不变；AI 冲突和父关系环失败关闭，BuildScene 不重复落同一问题。`warehouse-rule-only-v1` 冻结 Run 与恢复链保持旧行为。
- 门禁为融合聚焦 16/16、BuildScene 3/3、Space Unit 492/492、默认 Integration 288 passed / 95 skipped、完整 Release/AOT 0 warning / 0 error。证据见 `docs/space/reports/e13-deterministic-zone-parent-inference.md`。

## P0：Space V1 下一批受控实现

- 当前功能检查点为 RackGenerationProfile 权威链功能提交 `19d32650`；Generation Run Web 入口 `52bb3a29` / `282d4e54` / `2871df1b`、首次创建 `770bdc96` / `bbcaf6fe` / `9d0971f4` 与纯规则 BuildScene `36cc0241` / `89c6fb2a` / `9e7f7e0a` 均已进入 `main`。E07 S01–S05、E13 S01–S13/S16/S17、E13-S14 工程切片、E03 S01–S05 及其报告不要重复实现。
- E13-S11 已完成用户可见取消安全点、同输入重试分类、权威 CommandBatch 对账、Failed/Stale replacement Run、RuleOnly 降级和真库运维演练；生产默认 BuildScene executor 现可让 RuleOnly recovery 从权威 PreviewSet 到 AwaitingReview，且零 Provider、零 Usage、零 Draft 写入。Provider-backed 模式仍失败关闭，不能描述成真实外部 Provider 端到端完成。
- E13-S13 已完成外部主体在 16 个 AI 操作及 Gateway 的稳定 403 拒绝、External Provider 字段/Token 外发白名单、7 个 GET 读审计和 Customer/Supplier/3PL 矩阵；生产没有真实外部 Provider，不能把门禁实现描述成网络端到端签收。
- E13-S17 已完成加法 Migration、幂等 SQL、Tenant 清理 Job、90/365 天保留、保留锁、同租户并发租约和 forward-fix 操作说明；生产定时器仍需受控 Worker 配置专用 service principal。
- E13-S14 的离线评估器、Calibration-only 阈值校准、样本外 Wilson 门禁、规范报告哈希和命令入口已完成；原创 20 份黄金 CAD、10/5/5、L1～L5、实名单人复核、完整性审计及获批准 Primary 的正式版本转换输出也已关闭。正式 S14 仍需业务真值评测、人工操作实测和结果签署；Backup/影子双链与现场试点按 Lean Schema 3 转为 GA 后增强。
- E13-S10 已消费 E13-S09 Decision 并原子写入 Draft；真实 Worker 的同 SourceHash、已确认 `LoadLockedFacts` 已自动接入 RuleOnly 融合并重映射名称、allowlisted 属性和父关系。不同 SourceHash 的确定性几何建议继承与人工确认仍未完成，不能用猜测匹配绕过失败关闭。
- RackGenerationProfile 权威头/不可变版本、Tenant-only 创建、System/Tenant 读取、Run 冻结、Worker 消费和 Web 显式选择已由 `19d32650` 完成；不要再以 Asset 或任意 GUID 替代。无人工锁时的确定性 Zone 父关系推导已由 `d19a5300` 完成。下一张独立产品卡可处理不同 SourceHash 的几何匹配、建议展示与人工确认；在确认闭环完成前不得自动继承或 Apply。现有方案追加 v2、System 配置和完整管理 UI 也保持独立，仍不能发明默认尺寸或关系。
- 继续保持批量 High Accept 默认关闭、原始 CAD 不外发、外部 Provider 默认关闭、配额失败关闭、规则路径不依赖 Provider，以及 Draft/Published/WMS/设备边界隔离。
- E02 S01 中立实验工具、20 文件原创黄金候选、DWG/DXF 版本/实体矩阵以及获批准 Primary 的冻结 Release 运行均已完成；下一步消费同一输出做业务真值和人工效率评测。
- AutoCAD Primary 的本机许可、Release/Worker 哈希、本地受控边界和 86/100 评分已接受；只有实际选择 ODA、云 Provider 或远程/生产模式时才补对应 SDK、DPA、区域、身份、网络和 SaaS 证据，不预先强制第二供应商。
- WP7 继续在同一冻结环境补正式 50 MiB/资源、业务准确率/精确率/Wilson、人工操作减少率和受训用户 Ready P95；不得用当前转换支持率直接替代这些指标。
- 本机 `KOUSQLSERVER` 已用于 E13-S17 的迁移、重复清理、并发租约和幂等 SQL 双执行，结果 3/3、0 skipped；随后 Version Clone 缺失的 Zone/Aisle/Rack `Name` 与 Rack `RackType` 映射已修复，完整 Space Integration 现为 336/336 passed、0 skipped。该项不再是待办。
- `0d25da4d` 中 E05–E12 是候选证据，不得整包 merge/cherry-pick；必须重新核对依赖、迁移链和产品冻结范围。
- P2.5 已在当前主线按现行 E10 数据边界完成受控整合；历史 P2.5 分支仅保留追溯用途，不再整段 merge/cherry-pick。

## 已完成：GR-VP 波

权威计划：`docs/superpowers/plans/2026-07-17-general-role-vperm.md`。

1. T6 已通过 `d79a39c` 合入并推送 `main`。
2. API/Web 双镜像已重建并运行，Web 使用干净 `main` 产物。
3. 当前注册租户仅 `DEFAULT/A1`，已验证 RoleId=10 的 4 菜单/8 动作、admin 零扰动，以及 `qa_general` 的本人审批、归属闸和无权端点 403。

T1–T7 已完成，不要重复铺设。T7 细节见 `.superpowers/sdd/gr-vp-t7-report.md`。

## P1：GR-VP 收口票

- PMS/Sys 平台管理页仍未统一铺 `v-permission`。
- 决定标准角色是否使用日文显示名。
- 为 B1/C1 等租户建立/挂接一般用户。
- 评估 insert-only 标准角色种子是否应在管理员删除基线键后自动补回。
- 若后续恢复的数据库重新出现 B1/C1/D1，补跑 T7 四租户 SQL 矩阵；当前库只有 A1，不要为凑验收数虚构租户。

## 已知跨波跟踪票

- WFS/Space 各 plan 文末保留若干 live QA、移动端视觉和清理票；动手前读对应最新计划的“完成后跟踪票”。

## 文档维护任务

- 每完成一项任务，更新 `PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md` 和 `CHANGELOG-AI.md`。
- README/CODEMAP 的规模数字已经过时，未来可单独刷新，但不要在权限任务中夹带修改。
