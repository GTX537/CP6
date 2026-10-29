# W219 用户执行原则更新

按用户最新指示，将Trust Internal Contracts、Fail-Fast和精简门禁写入AGENTS：内部契约直接采用，边界负责必要校验，缺前置立即明确失败；减少重复证明、兜底与重跑。仅文档差异/引用及台账结构检查，产品测试0次。

规则见 [AGENTS](../../../../AGENTS.md)，从本轮及后续任务生效。原拟全面出口审计已缩为一次源码与直接前置核对；不新建完整审计矩阵或逐项证据包，不因本轮文档变化跑编译、产品测试或浏览器。运行时代码未改，无权限、精度、事务或生产行为变化。

核对确认ReplenishService.Owner.cs和RmaService.Owner.cs的实际命令仍采用明确未就绪错误，PlanningBoardService.Owner.cs改期/排列仍缺协议。后续要实现这些实际业务入口；本规则本身不把缺实现变成可用。W129/W135原出口和真实外部材料边界保持，D1/D4已完成、其余阶段未关闭，65–70%不作为实际完成率。

W218按最终proof归档一次，只核销WP10-01/WP10-19.wave218_allocation_control限定生命周期。四份状态、当前入口与七天计划同步。CRM/Platform只读，未复制修改其AGENTS；本聊天跨仓仍执行用户本条持续指示。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/195)已合并并核对远端采用，准确分支已删除、DEV-2已释放。Main本报告为提交前快照，待正常PR/远端核对/定向分支清理/finish；最终见本机wave219-final-verification.json。固定目录、用户资料及缓存保留。

预计10–20分钟，截至本快照登记8.03分钟；最终START至finish计一次，不叠加Handoff，前置阅读另计。无Actions触发/取消/工作流变化或部署。

下一步：Continue the next original D2/D3 missing business write, using established internal contracts directly and fail-fast at the responsible boundary. Check only actual direct prerequisites; no new full audit or unrelated prerequisite expansion. D5/D6 external inputs and D7 final integration remain explicit.
