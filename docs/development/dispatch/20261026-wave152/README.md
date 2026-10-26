# W152 CFG工程选项规则及工作快照采用

COORD本聊天单人连续执行，原CP6-7D-20261022保持ACTIVE；W151两仓合并、分支删除和席位释放已核实，不重复。复用既读brainstorming/writing-plans/executing-plans与项目精简验证要求。

## 依据、边界与设计

核对当前Main/CRM/Platform/Handoff及席位、完整WP05-03与WP05-39、ENG-REV-01、原03_ENG相关段落；原08_CFG L485–600、700–930和获准PLM_LR02_03合同L200–283。CFG-REQ-005要求强类型选项、必选、互斥及依赖；CFG-REQ-007有权批准/版本不可覆盖；CFG-REQ-008与LR02-010明确完整范围、缺输入UNKNOWN，不默认第一个或新版，也不冒充REL或制造许可。

本轮限定ENGINEERING_OPTIONS_ONLY：独立Main.Plm.ConfigurationRule.v1、原生UUID/创建者/工程产品准确Ref/内容版本/ETag、不可变原文；单工程产品一个明确选项规则档案，当前获准规则指针与编辑草稿分开，不由最高版本推断。强类型STRING/DECIMAL/BOOLEAN，必选及明确允许域，MUTEX/REQUIRES条件；数字精确字符串按数值相等比较而原文不丢精度。不提供任意代码/SQL。拒绝不存在的条件名/类型/域及无明确范围；没有真实业务规则种子。

DRAFT→UNDER_REVIEW→APPROVED，RETURN回草稿，REVISE新草稿不撤旧获准规则；批准必须显式绑定被替代获准Ref和当前工程来源；WITHDRAW可在新草稿存在时撤回旧批准，并保留当前草稿。query/add/edit/submit/approve/withdraw当前授权、创建者/真实部门/租户范围，同PG事务内业务/映射/审计/原操作；不可混同版本和批准。这里的有权批准仅覆盖明确选项约束，不补签工程替代、专业试验或范围政策。

ENG工作快照显式选择准确获准RuleRef，同一真实数据库事务锁/当前来源/权限复核，持久化选项输入摘要、准确规则、检查项及OPTIONS_VALIDATED/UNKNOWN/CONFLICT/NO_MATCH。状态仅描述该组选项；完整适用范围、BOM选择、替代、冻结条件仍未完成，不能输出全局CFG_RESOLVED/MATCH。缺必选不默认，规则撤回/替换/源变更不改历史而显示当前不可继续。旧无规则快照原文字节保持不变。

## 任务与预计实际时间

75–120分钟：存储/当前权限/批准与采用40–65；独立规则页面、ENG准确规则选择、定向验证25–40；一次完整SPEC/QUALITY与两仓收尾10–15。Main官方START至finish计实际，不叠加Handoff。

- [x] 独立原生规则Owner、PostgreSQL前向迁移及实际HTTP入口；当前授权、部门创建、原文及故障回滚。
- [x] 规则类型与约束、受控批准/退回/替换/撤回、精确活动指针、历史/原键恢复及同事务ENG检查采用。
- [x] 独立页面、当前权限/身份清理、最小恢复引用、ENG选择准确批准规则、IAM真实对象。
- [x] 相关PG/HTTP、组件/恢复/IAM、类型/Vite，一次集中审查；保留失败，复用输入未变证据。
- [ ] 一份交付报告、唯一台账/四状态/09；仅归档W151一次并关闭WP05-03.wave151_revision_drafts。正常两仓PR、远端采用、精确分支清理、官方finish释放固定席位。

WP05-03和WP05-39整卡保留，D2/D3/D5/D6/D7及原七天所有出口保留，125目标/247运行验收不提升。无Actions/既有环境迁移或部署/新工作树，不虚构CFG完整闭合。

## 实际交付

[单份报告](../../evidence/W152-01/DELIVERY.md)，登记28.02分钟，前四步与集中审查完成。最后生命周期步骤待Main正常PR/finish后由proof确认，Handoff已采用释放，原七天ACTIVE。
