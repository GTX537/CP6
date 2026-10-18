# 第十六轮：P1 Space库存精确范围传输

## 2026-10-18：总控已交付R132-01修正，待原D局部复查

修正代码 `40a2d2b292df001cea4c338600ffb958e4381ae7` 已完成本地定向验证，见[原单份DELIVERY](https://github.com/GTX537/CP6/blob/40a2d2b292df001cea4c338600ffb958e4381ae7/docs/quality/evidence/issue-132/DELIVERY.md)。仅库存POST局部处理绑定阶段的413异常，公共中间件/Program/请求上限未改。HTTP夹具加入实际Space错误层；修前2项预期413/实际500的RED保留，修后HTTP/OpenAPI65通过、0失败/跳过（7行为+58合同）。非目标异常保留原500映射，合法边界200、超限413/ProblemDetails及无runtime调用均通过。

Kestrel chunked计数包含格式字节；本夹具单chunk在边界处含14字节格式，报告已明确载荷/传输字节区别，保留最初错误正向预期的失败记录。原前端35/InMemory9及D未变范围结论复用，无重复全仓或数据库/真实DEV验证。

原A候选325dc769和干净工作树保留，COORD仍是修正唯一Owner；原D对固定修正只复查R132-01及影响，沿用原REVIEW，无第二START/ACK。#132及阻断保持Open，产品尚未合并；本条随候选交付，不能当作主线集成或D通过。18232/15232无监听，无Actions、数据库操作或部署，375任务/125业务不提升。

以下为此前登记历史，当前候选结果以上述记录为准。

## 2026-10-18：总控独占接手R132-01，原D继续局部复查

用户要求继续推进。远端A仍为325dc769/Handoff8ff05c67，原A工作树干净，未观察到修正开工或交付；原D仍为9ff8c9f的No结论。当前工具无法唤醒原用户对话，COORD按总控权限接手本项定向修正，作为唯一产品写入者；原A保留原候选和工作树，不再并写R132-01。此调整不是原A已接受消息或已开始修正的事实。

本登记正常合并后，从最新确认main创建 `codex/bug-132-http413-correction-20261018` / `D:/CP6-worktrees/bug-132-http413-correction-20261018`，以可审计cherry-pick承接固定325dc769，再修正端点超限错误映射。原13项写集和18232/15232资源转由COORD独占；Program、公共中间件、依赖、数据库仍只读。实现前再次核对原A无变化及端口空闲，如有并行冲突先停下协调。

先在HTTP夹具加入实际Space错误层，复现超限返回500，再做端点局部修复；覆盖exact1MiB200、声明长度/chunked超1字节413、ProblemDetails body/content type、超限无runtime调用及非目标异常不被吞。复用未变前端35/InMemory9等有效证据，只补受影响HTTP/合同。原D在固定修正交付后只复查R132-01及影响，原REVIEW接续，无第二START/ACK或完整审查。

本次仅所有权登记，不宣称修正已完成；#132和R132-01保持Open，产品未合并。无数据库/真实DEV新增门禁，无Actions、数据库操作或部署。完整任务24/375 DONE、业务0/125不提升。

下方原A修正分配为历史记录，当前Owner以上述记录及roadmap为准。

## 2026-10-18：BUG132集中审查暂不可合并，原A接续定向修正

D已对固定候选 `325dc7693edf09ec0c9175c68554e4e3c72b2af0` 完成一次集中SPEC/QUALITY；[原REVIEW更新](https://github.com/GTX537/CP6.AI-Handoff/blob/88c89e639cfa1b1da6d98ed84d96f172cf6022f3/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)和Handoff `9ff8c9f243b6446272e5b15e5c7901719126b349` 已远端回读。**Ready to merge：No；P2阻断R132-01。** 源码显示实际SpaceDesignProblemDetailsMiddleware把超限绑定异常改为500/SPACE_INTERNAL_ERROR，而原HTTP fixture省略了该层，未证明声明的413/ProblemDetails合同。此为源码审查结论，D和COORD未新执行实际应用探针；不代表大小限制被绕过。

原A在同一BUG132分支及既有写集接续修正，无第二START/ACK。优先端点局部处理绑定阶段异常；Program和公共中间件保持只读。定向HTTP fixture纳入实际Space错误层及相对顺序，保留RED→GREEN：恰好1MiB为200，Content-Length/chunked超1字节均413、application/problem+json及body状态符合合同，超限不调用runtime，非目标异常行为不变。仍只更新原DELIVERY，固定交付后D在原REVIEW仅复查该修正及影响。

原前端35、HTTP/OpenAPI61、InMemory9通过记录保留；其中413结果限简化宿主，不能算实际管线通过。未变前端/内存与其余有效证据复用，只补受影响HTTP/合同及必要编译。本轮不新增数据库或真实DEV门禁，不重做全仓测试或完整审查。

[#132](https://github.com/GTX537/CP6/issues/132)保持Open，产品未集成；R132-01作为本Issue候选阻断记录，不另建重复Issue。#38/#257保持Closed，B/C修正仍待各自窗口；完整任务24/375 DONE、业务0/125不提升。无Actions运行/取消、工作流变更、数据库操作或部署。

以下为此前收件与原准入记录，当前状态以上述记录和roadmap为准。

## 2026-10-18：BUG132固定交付已收，等待D集中审查

A候选 `325dc7693edf09ec0c9175c68554e4e3c72b2af0`、Handoff `8ff05c67e42e6b78335afc07cf785e561b54a8d0` 已远端回读；[单份DELIVERY](https://github.com/GTX537/CP6/blob/325dc7693edf09ec0c9175c68554e4e3c72b2af0/docs/quality/evidence/issue-132/DELIVERY.md)限登记的10个路径，基线为main `1e29931374626a492ca92e848b8d8fcaf871eddf`，工作树干净。库存查询改为完整精确集合的有界POST，保留旧GET；产品代码尚未集成。

COORD读取原始日志/TRX，核对前端 **35**、HTTP/OpenAPI **61**、InMemory服务 **9** 项通过，各0失败、0跳过，未重跑。前端最终来源为锁定依赖Vitest4.1.11，早先4.1.9结果不计作最终证据；HTTP使用真实Kestrel/Cookie/CSRF及受控权限与runtime数据，服务测试使用EF InMemory/记录源，不冒充PostgreSQL或真实WMS验证。A自有18232/15232均无监听。

A真实START `b7b618df`、D真实START `2b45642d` 已登记。[D原REVIEW](https://github.com/GTX537/CP6.AI-Handoff/blob/843dcc019ea311bc18ac186f5ad397c865f9ff2e/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)及准备消息 `603df33948af18de597f2e733374b6d21db333fe` 仅为只读准备，尚无Ready to merge结论；“尚无A固定交付”的前置现已满足。原D直接对固定候选在同一REVIEW接续一次集中SPEC/QUALITY，不重复START/ACK或新增报告；总控收件不代替正式审查。

[#132](https://github.com/GTX537/CP6/issues/132)保持Open，待D结论后正常集成和原Issue验收核对。本轮不新增数据库或真实DEV门禁。#38/#257保持Closed，P0暂停已解除；B/C原P2修正仍待各自窗口，旧菜单候选未变。完整任务24/375 DONE、业务0/125不提升。无Actions运行/取消、工作流变更、数据库操作或部署。

以下为本轮原准入记录；当前收件以上述记录和roadmap为准。

[#257](https://github.com/GTX537/CP6/issues/257)已通过[D最终审查](https://github.com/GTX537/CP6.AI-Handoff/blob/68f574242b847daeb1c898a33b97c78c3c071c4c/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-257/REVIEW.md)、[PR260](https://github.com/GTX537/CP6/pull/260)和用户指定PG真实DEV，现Closed；[单份DELIVERY](../../../quality/evidence/issue-257/DELIVERY.md)。P0暂停解除。按BUG管理P1下一项规则，当前是[#132](https://github.com/GTX537/CP6/issues/132)，不是再次重复登录/仪表盘验收。

| 原对话 | 本轮职责 | 当前准入 |
|---|---|---|
| A | [BUG132精确范围有界传输](A-BUG-132.md) | 唯一实现，必要的向后兼容库存查询API合同亦由A独占 |
| D | [准备与集中审查](D-REVIEW-BUG-132.md) | 只读准备；A固定交付后一次集中审查 |
| B/C | 保留原候选与#253/#251/#252修正准备 | 尚无新产品窗口；旧阻断未解除，不能自动集成 |

旧d2836f33补丁会在选择超过100个ID时省略过滤、变为整站查询，已经撤回，不得照搬。站点超过10,000库位时，合法请求子集仍须成功且精确。现主线GET按每ID拼参数；服务QueryInventoryAsync已在一次请求内校验SiteAccess、Current Published和最多10,000唯一选择，优先复用该单一边界。

验收仅源码、受影响前端/HTTP合同和必要现有内存服务测试，依Issue不操作既有数据库或替换本机服务。可用A自有loopback18232/15232测试fixture；无PG端口/迁移/DEV部署窗口，不新增SQL Server或双库验证。375任务/125业务目标不因BUG收尾提升。#132不要求复制#257的真实DB验收矩阵。

## 执行规则

先读根AGENTS、[本轮入口](README.md)、[资源与写集](dispatch.json)和roadmap。COORD在本准入合并后从最新main创建A产品工作树和A/D任务资料分支。Handoff仍为GTX537/CP6.AI-Handoff，协调分支codex/dev-coordination-20261012，本轮waves/20261017-wave16.md；各自只写tasks/BUG-132或tasks/REVIEW-BUG-132。真实开工发布一次START并安全推送/回读后连续推进，无运输ACK；D收到A固定DELIVERY后接续同一REVIEW，无第二START。派工不代表开工。

精简验证：相关行为、故障和边界加一次集中SPEC/QUALITY；修正只复查影响，未变证据直接引用，不跑全仓或为文档编译。每任务一份DELIVERY/REVIEW。COORD统一共享台账/四状态、正常合并和远端核对；执行者不改main或共享状态。push/PR检查候选及main工作流事件/路径，未变内容复用；Handoff私有/Actions disabled且默认/自身无workflow后推送，不带tag。无Actions、既有数据库/服务操作、部署或force-push授权。
