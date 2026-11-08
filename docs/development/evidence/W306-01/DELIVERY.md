# W306 原待办办理整卡核销与多人新轮验证

W306在原WP12-04/07/10整卡前置满足后，按WP12-08三项范围及正反例核销当前动作上下文、真实双身份/原grant、双/三CAS、SFS复算/APP冻结TxD与轮次；补真实两人all/any重入新轮和旧原命令回放的业务验证，原WP12-08正式DONE。

Main起点 `b5e8b9401ffb634059ec8dcda53e9cb0484ea7b4`，COORD本对话单人，DEV1/Main及DEV2/Handoff固定复用。[原WP12-08](../../task-plans/WP12.md#wp12-08)直接三项实施及四项正反验收、原OA-TASK R2 §3/当前W291核销范围和当前源码共同核对；[W319](../W319-01/DELIVERY.md)已整卡核销FORM前置，[W305](../W305-01/DELIVERY.md)已核销历史职责卡。不删除原前置，不为已交付内部合同增加资格包装。

## 原范围与真实证据

| 原要求 | 当前实际行为及准确来源 |
|---|---|
| action-context真实版本、服务端SFS/APP封闭分型 | [W290](../W290-01/DELIVERY.md)实际Program认证HTTP/Chrome：准确task/instance、SFS formData版本、token/node/stage/round/准确pin/职责；APP沿accepted五字段和原Guid身份，不接patch或第三版本。上下文和APP WFC适配源码相对该交付未变。 |
| actual/principal/effective、原grant/SOD与当前撤权 | [W277](../W277-01/DELIVERY.md)当前双身份、范围/半开UTC/撤权共同门；W289 SFS最终资格到期回滚；W290 APP委派/原件恢复。新轮夹具为真实当前不同模拟用户独立行权，非修改引擎actor或假票。 |
| Token/Node/Stage/Round，不用旧Approved凑新轮 | [W282](../W282-01/DELIVERY.md)实际退回/转交和新轮、W289再验证；本窗新增下述两组多职责阈值实际行为。当前FlowEngine按准确四维筛票，串签推进保持StageRound隔离。 |
| SFS可写字段复算、APP冻结及终态TxD | [W289](../W289-01/DELIVERY.md)当前节点/IAM/委派字段交集、精确patch、FILE/字典/Vars、三版本及决定/历史/原回执同事务；W290 APP原AppCommand/alias/Recorder/终态Outbox同TxD。后续SFS唯一相关入口差异是BUG756合法数据限额参数，原实际大数据/Unicode行为已验。 |
| 并发只一合法决定、同命令原receipt | W289两连接竞争编辑/投票；W290实际APP版本竞争一胜一412、完整故障回滚与未知原结果；本窗再证旧原命令回放不推进新轮或增加待办。 |
| 稳定TaskId、CC不能审批 | [W276](../W276-01/DELIVERY.md)授权后的合并/展开准确同TaskIds和总数；[W291](../W291-01/DELIVERY.md)两条真实SFS/APP仅CC用户即使有菜单approve，读可见内容但context/决定404，状态/票/命令不变。本窗不重复运行这两条。 |
| 缺双token/伪contextKind不能走旧单票 | W290缺版本428/APP旧版本412、封闭形状及服务器选kind；W289旧二版本SFS新写410且保原结果；旧TaskDecisionService在native Owner边界明确拒APP/FORM旁路。 |
| 实际页面/合法意见域 | W278/W289/W290真实Chrome及字段/身份/冲突/未知恢复；BUG730完整1000 Unicode码点和三张PG投影修复、APP500保原协议；无新页面改动，复用其真实时间/环境结果。 |

## 本窗新运行

`TaskRoundCard_actual_two_actor_all_and_any_new_rounds_ignore_old_votes_and_original_replays`在真实隔离PG18中分别运行all/any两套流程：两名当前角色职责人、串签第二档和后续节点全部真实发布、激活、创建任务/票决。旧轮批准后从第二档PREV_STAGE返回，准确Token/Node/Stage保持、新Round增加，两名新职责均有新TaskId。旧批准原命令返回同VoteId，当前新轮待办集合不变。

all新轮由第二人先批准，旧两票不能补足当前第一人，仍Running并等待该真实新任务；any新轮由第二人先驳回，旧第一人的批准不能把它变成通过，仍等待当前第一人。其后由第一人明确新批准才推进下一档，第二档及后续节点仍须各自当前实际批准，最终才Approved。旧Approved/Cancelled票状态原样保留。此验证覆盖实际决策结果/等待行为，无引擎/Owner替身，不只是断言轮次字段。两个场景一次通过，未发现需要产品修复的缺陷。

实际来源 `D:\CP6-local-test\wave306\foundation\multi-actor-rounds-20261108T132423Z.json`；PG集群已停止。只新增此定向行为测试，没有新HTTP/Chrome/Vue运行；不把复用结果写成本窗新执行，也不按提交SHA重跑。完整新执行/复用索引为 `D:/CP6-local-test/wave306/foundation/final-validation.json`，准确原卡/来源核销为 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave306-reconciliation.json`。

## 前置、审查与继续

S01-02/S02-00、WP12-04/07/10均DONE且准确来源在本Main；WP02-10直接用既有CLOSED W171可信上下文/当前授权共同门，WP02-13直接用CLOSED W290任务原结果/Actor证据/审计/TxD交付，不等全域审计保留。W291当时被整卡前置阻止的状态条件现在已真正满足。

TaskActionContextV2及APP WFC适配未变；SFS后来仅采用已修复的数据请求限额，核心当前授权/字段/票决/事务路径仍按原证据与当前新轮真实运行适用。一次集中SPEC/QUALITY审查原三项范围、当前真实调用链及新增测试两组状态/原键不重办、准确Actor/阶段/终态和源码/文档完整diff；无未处置阻断项。新增1项真实PostgreSQL18行为通过，包含all/any两组两人流程的完整旧轮→退回→新轮→终态及原命令重放；未改产品/页面，原HTTP/Chrome/Vue及并发/身份/CC/回滚证据按准确来源复用。当前源码、祖先、原卡/前置、文档diff/链接/台账/生成页核对通过。

原375现36DONE/2IN_PROGRESS/337PLANNED，原59局部12闭环/47剩余；125完整目标0关闭、247正式验收NOT_RUN不变。全OA-TASK批量办理/管理转单、时限通知和完整目标验收不由本卡连带完成。下一核WP02-15既有导出载荷的真实保留/处置Owner，当前撤销下载只撤访问而不删除载荷；W310预算末尾。

批量管理转单有独立NativeTaskBatchOwnerV2，不把它当作普通批量审批已经完成；完整OA-TASK的其他能力按其各自原卡和目标继续核实。本窗只核销原WP12-08明确范围。历史职责转换、生产部署及既有环境迁移仍独立。

有效累计56892秒，初估0.6–1.25h低置信实际执行，最终耗时以官方finish后采样为准；24h目标继续，下一57600秒校准。无Actions触发/重跑或取消、工作流/依赖/分支保护变更、生产部署或既有数据库迁移。测试仅新隔离PG、模拟用户/流程与实际内部Owner。

本提交时正常远端采用/准确分支清理/官方finish仍待执行；最终分支处置、工作区状态及席位释放以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave306-final-verification.json` 实际PASS核销。
