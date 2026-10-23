# CP6 七天一期持续交付计划

> 执行方式：本聊天 COORD 使用 writing-plans / executing-plans，单人逐任务执行；固定席位、一次任务级集中审查。用户已指定本对话继续推进，不另开聊天或代理。

**目标：** 在总任务既有范围内，从剩余质量与 WMS/MES 独立入口推进到 Main 原操作结果、CRM 身份/共同事务和公开询盘提交主线，形成逐项可审计的主线交付。

**架构：** 复用当前 PostgreSQL 身份/权限/OperationsOwner 与正式 Platform 合同。业务效果、最终权限、必要审计和对应原结果按已接受合同进入所属 Owner 事务；跨仓用正式合同，不跨库直写。前置未满足时保持未交付，不以接口或模拟替代采用。

**技术：** 既有 .NET / EF Core / PostgreSQL、Main Vue/Vitest、CRM Next/React、当前本地集成夹具。

## 时间与基线

- 七天计划日期：2026-10-22 至 2026-10-28；持续目标开始 UTC 2026-10-22T04:03:18Z，七日检查点 UTC 2026-10-29T04:03:18Z。
- 预计本聊天实际执行 35–55 小时，包含相关验证、审查、合并、Handoff 与分支/席位收尾。它是估算，不是 168 小时不间断运行承诺；按每次真实 START/finish 记录修正。
- 日次是检查点，不要求等待日历到点；一项完成并释放席位后直接推进下一项。中断从原任务/准确 SHA 恢复，不重做已完成轮次。
- CP6 main `f5d9a540513fa2fdea730bcad780cb3ae060cedd`；CRM main `c420cf9e0711bc0c5529d2f709708a57533b341a`；Platform main `a89e90eb4f078f37a6f8610882079073185fbcf8`；Handoff 默认目标 `ae7791814e723e9260340055e813cc5943a7496f`，均经远端核实。
- W37–W40 已合并并实际 finish；开工时三席位空闲，容量无告警，根用户资料保留。当前无开放 Issue/PR；新发现缺陷按 GitHub Issues 登记，不在此另建 BUG 队列。
- 总范围保持 14 工作包/125 目标/375 任务。既有 24 项整卡、S01 12 项交付复用。局部入口交付不自动改变 125 目标或 247 运行验收。

## 七个阶段

|日次|关联总任务|交付范围与出口|预计实际时间|
|---|---|---|---|
|D1|WP02-03，服务 WP08/WP09/WP10|W41-01：StockQcService、QcInspectionService、QualityInspectionService 独立 Owner；检验判定→收货/库存、品质检验→不良/库存 QC 关联事务；相应真实 HTTP/页面保护。原步骤、越权/撤权、回滚与 UNKNOWN 定向通过并合入。|4–6h|
|D2|WP02-03，服务 WP10|按当前源码拆分 ReplenishService、RmaService 及其实际库存/财务接缝；明确无订单对象的可信部门来源、维护权限和版本约束，禁止从字符串或当前调用人猜测历史归属。前项交付后登记各独立窗口。|4–6h|
|D3|WP02-03，服务 WP08/WP06|MachineService、OeeService、PlanningBoardService、PlanAchievementService 及实际消费的工作中心/费率引用入口；查询/统计先作用域过滤，写入与当前权限同事务，后台计算使用可信租户上下文。|4–6h|
|D4|WP02-10，采用 WP02-07/08|Main 首个实际业务 Owner 持久原操作结果、同键重复不重复生效、未知提交按原键查询；必要 outbox 与业务同事务、可恢复投递。明确当前披露权限与不可变原结果的区别。|6–9h|
|D5|WP02-06/09/29，采用 WP02-05/07/27|CRM 可信身份基线/增量持久投影、撤权 fence、真实 Owner 与 Inbox/原结果同事务，实际宿主采用已接受包；Main/CRM 分仓证明，不能以 Main 规划或 ACK 代替。|6–9h|
|D6|WP03-01/02；实际必要 WP02-13/14/15/16|先落实消费所需审计/编号/文件/隐私边界，再交付不可变公开表单 Bundle、生效选择、浏览器绑定 Attempt、提交与受理。法定政策/日历/密钥等真实外部来源缺失时保留具体未完成项，继续可独立实施范围；不伪造企业默认。|6–11h|
|D7|WP14 及本期各 Owner|对本期实际已采用合同与主线做相关跨仓 PostgreSQL/HTTP/页面集成；修正实际失败，逐卡核对剩余条件，完成远端采用、台账与总交付。真实 DEV/UAT/生产门禁仍独立。|5–8h|

本期不承诺完成全部 WMS 外围能力、采购/财务全域 Owner、工程、空间、工作流及全部询盘隐私/审核目标。WP03-03/04 完整闭环作为下一直接里程碑；只有本期先行合同/前置实际满足且时间估算支持才扩展明确窗口。不为等待整个 WP02 清零而阻塞已就绪业务。

## 任务与席位执行

- 唯一状态源仍为 `roadmap.json`。本表是获准连续推进的顺序，单个写入窗口必须核对最新基线、直接前置、实际文件和互斥资源后登记；范围实质变化在本聊天修订并说明。
- DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018`；DEV-2 `D:/CP6-worktrees/dev-2`；INTEGRATION-1 `D:/CP6-worktrees/s02-00-20261012`。四仓合计最多 2 开发 + 1 集成；跨仓仅按 bind 安全替换，不新建源码副本。
- 各任务一个 `codex/<任务编号小写>` 分支，修复/验证/最后采用文档继续原分支。Handoff 单独任务分支/席位，真实 START 仅记录一次；资料发布不等待重复 ACK。
- 每项：manage status/start → 相关行为复现与实现 → 定向验证 → 一次完整 diff 的作者 SPEC/QUALITY → 正常 PR → 远端采用 → 必要冒烟 → 停止自有进程 → 定向分支清理 → manage finish。中断使用 resume；不能手工清空席位登记。
- 每次 push/PR 核对源和受保护目标工作流/过滤与相关依赖。普通编译测试只在本地；同输入成功证据按真实来源复用。
- 全程 PostgreSQL；不授权既有环境迁移、真实角色授予、自动 Actions、force-push、保护绕过或生产部署。

## 持续目标完成条件

- [x] D1 质量入口及关联事务交付、原步骤验证、远端采用和收尾。
- [ ] D2 补充/退货及可信范围对应限定窗口交付和收尾。
- [ ] D3 设备/OEE/计划对应限定窗口交付和收尾。
- [ ] D4 Main 首个持久原结果/可靠投递业务采用交付和收尾。
- [ ] D5 CRM 身份和首个共同事务 Owner 交付和收尾。
- [ ] D6 实际前置已满足的表单/提交主线交付；外部缺失精确登记，不计完成。
- [ ] D7 本期集成验证、失败修正、实际耗时/剩余差距及所有分支/席位处置报告。

首轮 W41 结束不结束本持续目标。尚有必要工作时保持 active；未达目标不因时间/上下文不足标为 complete。受外部条件阻塞时继续独立工作并记录原任务恢复点，按持续目标工具的实际规则处理。


## D1实际检查点

产品PR #393与Handoff PR #17已采用，#392关闭；106相关.NET/20页面/最终类型检查通过。DEV-2释放，原W41-01最后文档后finish；准确生命周期及实际耗时以管理登记/最终PR为准。D1原估4–6h，实际明显较短，后续估时按阶段校正。持续目标仍ACTIVE，下一D2；本次不把完整任务或真实验收提升。

## D1实际收尾 / D2前置调整

W41 PR393/394和Handoff17已采用，实际60.92分钟、任务席位53.90分钟；两准确分支已删、席位已释放。D1 DONE，原4–6h估算修正为实际约1.02h。

最新WMS-RMA-01要求正式WMS-IN-RMA-ADAPTER-1 prepare/commit/query/seal及databaseBinding/MainUow采用证明；五类终端处置仍REQUIRED_FENCED、effectOwner=null，商业/财务效果另属Owner。旧RmaService直接库存/随机贷项不能冒充采用。D2先交付独立可信范围；RMA接线待真实前置，必要时提前D4。

D2首批[W42](../dispatch/20261022-wave42/README.md)绑定四类既有对象，预计实际2–3h：stock / stock-transaction / stocktake / work-order。已关联订单的对象不被新映射覆盖；新建、入库及RMA另核前置。总35–55h为初估，将按后续真实出口修正，不机械等待日历。


## D2首批采用检查点

W42四类独立对象绑定由PR395/Handoff18采用，100项相关.NET、25项页面/恢复和类型检查通过；DEV-2释放，原DEV-1最后文档后finish。D2尚未整阶段完成，正式RMA前置与新对象创建归属保留，下一窗口核对补充或D3直接前置，必要时提前D4。持续目标保持ACTIVE。

## W42实际收尾 / D4前置提前

W42实际39.91分钟，PR395/396与Handoff18已远端采用，两任务分支删除，固定席位实际释放。D2仍PARTIALLY_ADOPTED。

REP正式Transfer/SourceDemand、RMA prepare/commit/query/seal、SCHED资源重绑定均缺实际采用接线。按已授权七天计划提前D4的原命令/事务基础；D2/D3未完成。Platform Inbox是投递/投影原语，不替业务命令日志；本窗不伪造其接收采用或工作流发送成功。

[W43](../dispatch/20261022-wave43/README.md)预计2–4h，Main原命令日志与PA090真实业务/页面恢复先行；完整可靠投递仍为下一前置，不据此勾选D4完成。


## D4首批采用检查点

W43 Main journal/PA090原命令与页面恢复由PR397/Handoff19采用，43相关.NET、16Vitest与类型检查通过；DEV-2释放，原DEV-1最后文档后finish。D4整阶段尚未完成，可靠投递及实际消费者采用继续推进；D2/D3前置保留，七天目标保持ACTIVE。

## W43实际收尾 / W44库存原操作采用

W43实际34.94分钟、PR397/398/Handoff19采用、两分支删除/席位释放。D4仍PARTIALLY_ADOPTED。

REP需要的正式Root/Claim/Transfer、RMA正式适配器与SCHED资源重绑定尚未实际采用。本窗扩展D4已采用Main journal到库存单腿/双腿事务，不作为正式TRANSFER/REP采用；Finance bridge仍为提交后best-effort，不能借原结果恢复重新发送或宣称可靠投递。

[W44](../dispatch/20261022-wave44/README.md)预计1–2h；库存实际Owner原命令与查询/关闭API，精确对象当前授权，无页面采用或可靠外部投递声明。


## D4库存采用检查点

W44库存原命令/原结果/查询与close由PR399/Handoff20采用，38相关.NET、3客户端Vitest与完整类型检查通过；DEV-2释放，原DEV-1最后文档后finish。D4仍PARTIALLY_ADOPTED，可靠Outbox/消费者与正式Transfer等前置保留；现有页面没有采用库存命令，七天目标保持ACTIVE。

## W44实际收尾 / D3安全前置

W44实际20.83分钟，PR399/400/Handoff20采用、两分支准确删除/席位释放。D4仍局部采用。D3设备/OEE前置源码核对发现实际注册SignalRMesNotifier向Clients.All发送工单/数量/设备载荷，MesHub按无租户业务编码加入组且无当前权限准入；唯一页面消费者ControlTower直接展示这些载荷。按BUG规则P0先行，原Machine/OEE完整Owner留下一窗口。

[W45](../dispatch/20261022-wave45/README.md)预计45–90分钟；完成后继续设备/OEE独立Owner，不以实时通道修复关闭D3。


## D3安全前置采用检查点

W45 PR402/Handoff21采用MES实时边界修复。4项新增PG/实际HTTP/WebSocket加32项相关回归执行（含1项新测试重跑）均通过；按显示名称去重31项，8项Vitest与完整vue-tsc -b通过；DEV-2释放，原DEV-1最后文档后finish/Issue401关闭。D3仍PARTIALLY_ADOPTED，Machine/OEE/计划独立Owner与WmsHub作用域定向核对继续，七天目标ACTIVE。

## W45实际收尾 / W46前置

W45实际33.04分钟，PR402/403/Handoff21采用、Issue401关闭、两分支删除/席位释放。W46用真实WMS连接核对同类DI租户作用域，必要修复和收尾预计20–40分钟；随后继续设备/OEE独立Owner，七天目标ACTIVE。

## W46采用检查点

产品[PR405](https://github.com/GTX537/CP6/pull/405)和[Handoff22](https://github.com/GTX537/CP6.AI-Handoff/pull/22)采用，source`c09ee7f4bf32b735f98b45e8164d7a1fd84d45f9`、远端main`883709c885d6b13ca272c211f8dde06a7cbd939f`；13任务文件一致。Handoff source`d5ced62b53dc502690df1ce75dade0e0f8ef2b3b`、目标`ad787e92495ad409e96358190bf9939753fb1ddb`，3项资料一致，DEV-2准确分支删除/席位释放。

8项相关.NET通过（6项PG/实际HTTP-WebSocket与通知边界、2项旧路由兼容）；无前端变更，不重复前端验证；原任务最后文档后finish/Issue404关闭；下一窗口设备/OEE，七天目标ACTIVE。

## W46实际收尾 / W47设备Owner

W46实际15.94分钟、PR405/406/Handoff22采用、Issue404关闭、两分支删除/席位释放。W47先补设备/停机Owner和machine精确UUID部门绑定，预计90–150分钟；OEE独立计算及可信后台下一窗口，正式RES/PERF仍未采用，七天目标ACTIVE。

## W47采用检查点

产品[PR](https://github.com/GTX537/CP6/pull/408)和[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/23)已采用，source`9295ebac050bb43c3330638a59e27e71dc2ef8b2`、远端main`276e05aeeed41c560777e1d73ced49d0108387b1`，26任务文件blob一致；Handoff source`0536f372b2507fe7dceb85aa6a12e98fe881f8a5`、目标`722cc9d6a9034d181cd26d0ec07e186d24327b6c`，DEV-2准确分支删除/席位释放。

38项相关.NET用例通过（按TRX testId合并定向复查，含PG/实际HTTP/并发/迁移/种子），21项Vitest通过、其中5项修改后定向复查通过，完整vue-tsc -b通过；最后状态PR后finish/Issue407关闭，下一OEE/计划，七天目标ACTIVE。

## W47实际收尾 / W48 OEE Owner

W47实际62.52分钟，PR408/409/Handoff23采用、Issue407关闭、两分支删除/席位释放。W48补OEE独立入口、完整来源范围及可信后台，预计90–150分钟；正式PERF源cut/运行事实闭包仍未采用，七天目标ACTIVE。

## W48采用检查点

产品[PR](https://github.com/GTX537/CP6/pull/411)和[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/24)已采用，source`f4958debb891cbed296ed81df9afa3bf010c8edc`、远端main`bee338101c5bb99161c91fba8fb55f069a2b0435`，29任务文件blob一致；Handoff source`01959b4c349698ddd609e81c72dc2740a5673de8`、目标`cb2769fb5f816b7c1e37d0e760e1028aa67b0aa9`，DEV-2准确分支删除/席位释放。

31项相关.NET用例通过（按TRX testId保留最新定向结果，含PG/真实HTTP/后台事务/种子/权限），6项Vitest与完整vue-tsc -b通过；最后状态PR后finish/Issue410关闭，下一计划Owner，七天目标ACTIVE。

## W48实际收尾 / W49计划Owner

W48实际56.28分钟，PR411/412/Handoff24采用、Issue410关闭、准确分支删除/席位释放。W49补计划/达成率独立读取、统计与导出，预计90–150分钟；正式排程缺Root/RES/执行共同事务，按已接受LEGACY_READ_ONLY与Owner协议前置保持拒绝，七天目标ACTIVE。

## W49采用检查点

产品[PR](https://github.com/GTX537/CP6/pull/414)及[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/25)采用，source`d5e8fb07231cdbd5016cb028dd9ea6bab34fec51`、远端main`6cc3c5e3d205b43eded0e398ef673a3a29f5b5b6`，32任务文件blob一致；Handoff source`2ec59c822c266115bc26a50d0463374170133fc4`、目标`942eac113b9c456e37c0fea7ca4137b07eb8f80d`，准确分支删除/DEV-2释放。

34项相关.NET用例、8项Vitest及完整vue-tsc -b通过；同一testId复用最新有效结果，未把旧执行改称新执行；最后状态PR后finish，七天目标ACTIVE，继续Main可靠投递/CRM实际采用前置。

## W49实际收尾 / W50原生Outbox采用

W49实际39.38分钟，PR414/415/Handoff25采用、Issue413关闭、准确分支删除/席位释放。W50预计120–180分钟，补Main库存原操作与正式OutboxV2共同事务，可信后台发送空刷新提示；旧C02 token/存量队列不改写，Finance/broker/消费者Applied仍独立前置。七天目标ACTIVE。
