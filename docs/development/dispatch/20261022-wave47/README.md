# W47-01 真实 START / 七天一期 D3

COORD本聊天单人；2026-10-22T08:15:54.267839+00:00；Main基线`6bada0cf13eeabcb8b25dab485e5c20435941258`。W46 PR405/406、Handoff22已采用，Issue404关闭，两准确分支删除、席位finish，实际15.94分钟；W37–W46不重复。

本窗实现设备/停机独立Owner与可信部门绑定。DEV-1 `codex/w47-01`；DEV-2 `codex/w47-handoff`。预计实际90–150分钟：Owner/可信绑定35–55min、HTTP/页面20–35min、定向验证/一次集中审查/合并与收尾35–60min。日次是检查点，完成直接继续OEE入口，不等待日历。

## 已核前置和限定出口

- 当前MachineService查询/写入仍没有独立OperationsOwner，Search含未授权工单和旧OeeDaily附加披露；Controller仅写动作属性。复用现有实际会话/权限根/原Actor和完整事务，新增mes-machine当前query，保持既有add/edit/del/status/downtime动作。
- W42可信绑定只含stock/stock-transaction/stocktake/work-order。本窗以向前PG迁移加入machine精确UUID、同租户对象与健康部门，IAM原操作/引用版本和现权接入，禁止从WgCd/BaseCd/ProcessCd/当前用户猜归属。部门范围新对象仍须合法明确来源，不自动回填。
- 设备/停机查询在分页、计数与附加披露前收紧；有关联WO必须消费其当前Owner范围，不能借设备权限看他人指示。旧OEE附加值没有真实独立Owner/完整来源授权时不披露；OeeService独立查询/计算和可信后台采用紧接下一窗口，不在本窗冒充完成。
- 设备变更、停机采番/记录/设备状态和审计在同一实际PG事务；当前权限最终再核，通知仅已知提交后，失败全回滚/提交确认丢失保UNKNOWN。检查现有MachineStatusMonitor真实租户与并发路径，不把启停绿灯当正式运行事实。
- 页面按当前权限处理动作/迟到响应/身份变化/未知结果；相关PG、真实HTTP和实际组件验证后一次作者SPEC/QUALITY。仅受影响验证，复用有效证据。

已语义读取MES-RES-01/MES-PERF-01当前整理正文（长来源链接另保原文）；其正式Resource/Fact双头/Claim/Checkpoint及PERF源cut/RunSlot/真实分窗/closure未实际采用。当前旧Machine接口权限补齐不能声称RES205项/PERF48项验收完成，也不能把旧480分钟/100%回退改名为新OEE。

一个Main写入窗口独占OperationsOwner、Machine/MachineDowntime、IAM绑定、PG迁移、seed/Program和对应页面；Handoff仅资料。PostgreSQL唯一新增数据库，不部署/迁移既有环境、不授真实角色、不触发Actions、不用代理/新树。完整卡/125目标/247运行不提升；roadmap唯一状态，七天目标ACTIVE。按原准确分支/固定席位finish规则收尾。

## candidate

设备/停机独立Owner、精确UUID部门绑定、同事务写入及页面保护已验证；38项相关.NET用例通过（按TRX testId合并定向复查，含PG/实际HTTP/并发/迁移/种子），21项Vitest通过、其中5项修改后定向复查通过，完整vue-tsc -b通过。正常PR/Handoff、Issue407关闭和准确分支/席位finish待执行。

[DELIVERY](../../evidence/WP02-03/machine-owner/DELIVERY.md)。

## close

产品[PR](https://github.com/GTX537/CP6/pull/408)和[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/23)已采用，source`9295ebac050bb43c3330638a59e27e71dc2ef8b2`、远端main`276e05aeeed41c560777e1d73ced49d0108387b1`，26任务文件blob一致；Handoff source`0536f372b2507fe7dceb85aa6a12e98fe881f8a5`、目标`722cc9d6a9034d181cd26d0ec07e186d24327b6c`，DEV-2准确分支删除/席位释放。

[DELIVERY](../../evidence/WP02-03/machine-owner/DELIVERY.md)。
