# W49-01 真实 START / 七天一期 D3

COORD本聊天单人；2026-10-22T10:15:11.224128+00:00；Main基线`2f135d6b91d520cbe0c431df677654f5611aabb4`。W48 PR411/412、Handoff24已采用，Issue410关闭，准确分支删除、固定工作区干净detached、席位finish，实际56.28分钟。W37–W48不重复；七天目标ACTIVE。

本窗计划看板/达成率独立Owner、同范围读取及导出边界。DEV-1 `codex/w49-01`；DEV-2 `codex/w49-handoff`；预计实际90–150分钟：服务/Owner40–65、页面/导出20–30、定向验证/一次集中审查/合并收尾30–55。按完成条件推进，不等待日历。

## 最新前置与限定实施

- 最新Main与远端一致；AGENTS/POLICY自W48基线无变、根tracked干净且用户未跟踪资产保留，无相关嵌套规则。CRM main仍`c420cf9e0711bc0c5529d2f709708a57533b341a`、Platform main仍`a89e90eb4f078f37a6f8610882079073185fbcf8`；当前Main开放PR/Issue均空，W48 Actions0，容量无告警。固定席位重新start，不新增物理树。
- 已读当前PlanningBoardService/PlanAchievementService完整业务代码、两个Controller及页面实际交互；独立入口尚无当前Owner。Board KPI含SQLServer DateDiffDay，PG不可依其方言验收；KPI当前范围也未覆盖相同过程/状态/时间过滤。达成率来自现有工单累计字段，不把它改称已采用PERF真实分窗事实。CSV当前四位格式不应截断合法八位数量，导出须有独立动作及当前读取范围。
- 复用当前OperationsOwner、W42工单真实UUID部门绑定和独立工单/报工来源。查询/统计/导出按当前实际会话/Actor、完整父图和范围，最终再核现权；过滤不能在汇总之后。Board提供同一事务内的bars/KPI捕获供页面使用，旧两个读取入口同样设Owner。保留原合法查询与非PG历史路径，不创建新SQLServer实现。
- 已完整读取MES-SCHED-01语义整理正文（仅去除重复来源URL以免输出截断），并直接核对准确原R2文本L124–153、L246–251。原合同规定无Snapshot/Occurrence的历史数据LEGACY_READ_ONLY；改期要完整Root组、五域、源窗口和最终资源/执行门，换机另需六阶段及真实WO/RES共同UoW。当前MES模型/服务无这些实际采用，旧ProcessStatus=0、MachineCd或直接SaveChanges不能替代。该决定来自已接受设计，不是因为PG选择而任意缩小输入。
- 本窗明确落实历史计划的独立读取与正式写入口前置拒绝：有旧reschedule/arrange动作也不绕过缺失Owner协议写计划或换机；稳定错误/页面说明，无伪造Plan/Claim/默认工厂身份。原非PG历史实现保留。正式新排程、完整Root/六阶段/原键恢复仍下一准确采用任务；不把静态36AC或缺件写成已通过。
- 达成率summary/export分别核query/export，新增catalog不自动授角色。页面按身份/请求次序处理迟到结果、401/403清保护值、卸载停轮询；旧确认/导出响应不触发新身份下载，不重复导出。兼容统计清楚标识现有工单累计来源，不声称正式执行事实。

本窗独占Main OperationsOwner、计划两个服务/DTO/Controller/Program/种子/必要PG迁移、对应页面/测试和台账。相关本地PG/真实HTTP/页面与一次作者SPEC/QUALITY，精简验证，旧成功证据按输入复用。无代理、Actions、既有环境迁移、自动角色授予或生产部署。整卡24/375、125目标0交付、247运行NOT_RUN不因本窗提升；正常合并、远端核对、准确分支删除、manage finish后直接继续七天计划的下一实际前置。

## candidate

计划看板/达成率独立Owner、同范围capture及安全导出已验证；34项相关.NET用例、8项Vitest及完整vue-tsc -b通过；同一testId复用最新有效结果，未把旧执行改称新执行。正式排程缺Root/WO/RES采用保持LEGACY_READ_ONLY，产品PR/Handoff与准确分支/席位finish待执行。

[DELIVERY](../../evidence/WP02-03/planning-owner/DELIVERY.md)。

## close

产品[PR](https://github.com/GTX537/CP6/pull/414)及[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/25)采用，source`d5e8fb07231cdbd5016cb028dd9ea6bab34fec51`、远端main`6cc3c5e3d205b43eded0e398ef673a3a29f5b5b6`，32任务文件blob一致；Handoff source`2ec59c822c266115bc26a50d0463374170133fc4`、目标`942eac113b9c456e37c0fea7ca4137b07eb8f80d`，准确分支删除/DEV-2释放。

[DELIVERY](../../evidence/WP02-03/planning-owner/DELIVERY.md)。
