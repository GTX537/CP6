# W121-01 原生询盘决策Owner协调交付

CRM显式开启原生询盘人工放行/拒绝Owner，原会话和CSRF、锁外新鲜人员上下文、最终身份七锁→持久信任→稳定槽→原资源行锁，复核完整当前来源、精确site/form、主管/管理部门、query/view-pii/add或edit与六字段。Serializable同事务写完整H01 Lead/History或拒绝、原Submission状态/历史/加密原因、Consent处置保留期、受保护原200结果/H01命令/回执、审计与正式Outbox。保持原收到时间、日历、SLA及合法Unicode；拒绝无Lead。当前权限/撤回/PII期限先于重放；公开原201重试仅接受账本证明的版本转移。

CRM [PR147](https://github.com/GTX537/CP6.CRM/pull/147)采用 `0c9e9a74b6b15ec2572f8f594e14f018703fe867`，源 `5c8c7b2d96bbd0c642f5777aab9c40d539b92c48`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/0c9e9a74b6b15ec2572f8f594e14f018703fe867/docs/development/evidence/W121-02/DELIVERY.md)。68项唯一相关本地成功检查：24项PostgreSQL/HTTP、44项Web；实际H01/拒绝图、幂等/CAS/并发、真实HTTP响应丢失、晚期回滚/期限/撤回、实际Node BFF、公开原操作重试与存储保护覆盖，必需0跳过。 本地证据 D:/CP6-local-test/wave121/foundation/final-validation.json。W119实际Main人员HTTP签发按未变输入复用，不计本轮新执行；本轮TLS身份夹具与生产验收分开。真实失败保留，最终使用新的自有PG实例安装最终DDL验证，初版库保留且停止，没有迁移任何既有环境。

新增版本化crm_inquiry_decision_v1显式扩展与唯一SQL EmbeddedResource，无包/工作流改变；原v4图/旧合同门禁不变。抹除保护和延迟完整性检查保留。原生Outbox route保持Blocked/unassigned，无实际投递声明。默认IntakeWritesEnabled=false；allowedActions保守为空，页面/动作展示后继。

自有PG均停止、无新junction，缓存/失败记录/用户文件保留，无Actions、保护变更、既有环境安装/迁移或部署。Main本轮仅协调文档。页面、隐私/保留worker及实际Outbox发布者后继；正式24/375、0/125、247NOT_RUN不变，D6PARTIALLY_ADOPTED，七天ACTIVE。
Handoff [PR97](https://github.com/GTX537/CP6.AI-Handoff/pull/97)采用 `e30af151185e316255233a275ff5d12bf49a857a`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟42.12分钟，原估80–130分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。

Main仅同步台账及生成视图/四状态/七天计划；不重复编译和业务验证，准确引用CRM68项本地证据。完整差异集中审查与结构检查通过后按正常PR合并。
