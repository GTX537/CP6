# W234 询盘模拟数据生产验收

COORD本聊天单人。实际预计60–100分钟：模拟持久broker/消费者及场景25–40，原生提交/消息/重复与故障验证20–35，审查和三仓合并收尾15–25。延续现有七天持续目标。

D6 simulated production acceptance: native public form/configuration, actual submission and H01/receipt, durable original Outbox through HTTP to simulated durable broker/consumer, duplicate and fault recovery

Simulated policy/calendar/IAM/service registration and external transport are valid production-acceptance inputs by user authority. Product Owner/UoW and event effects execute; simulated transport is explicitly identified. No enterprise-data wait, no production deployment or actual Kafka qualification claim.

- [x] 实际公开配置激活、Attempt和提交/H01/原回执，显式模拟政策/日历/身份。
- [x] HTTP原文发布到PG持久模拟服务；消息来源/租户/路由准确绑定，消费者事务和持久去重。
- [x] 消费者回滚、重复投递、重建后恢复、提交后响应丢失及真实原文一致性。
- [x] 相关本地验证、一次集中审查、单份产品报告与Main台账同步。
- [ ] 正常PR、远端采用、准确分支清理、进程停止和席位释放。

只以实际执行结果验收，不将模拟消息服务称为真实Kafka部署。D5完整宿主来源资格和原D2/D3剩余功能继续分别处理；不扩大125目标。

## 交付快照

CRM161、Handoff已采用并finish；Main待正常PR与finish。估计有效执行16.14分钟，中断等待另列。[报告](../../evidence/W234-01/DELIVERY.md)。
