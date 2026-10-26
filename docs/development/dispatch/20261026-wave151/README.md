# W151 工程修订、工作快照与组成草稿

COORD本聊天单人连续执行；沿用已读brainstorming/writing-plans/executing-plans及项目精简节奏，不重复审批、不启代理。原七天CP6-7D-20261022 ACTIVE，W150正常两仓合并、准确分支删除和席位释放已核实，不重复。

## 实际前置与设计

已读完整WP05-03卡、ENG-REV-01主册及原03_ENG L55–166、854–916、1349–1417、3098–3113；当前主线没有EngineeringRevision/WorkingSnapshot实现。W150稳定工程身份、当前准确来源/权限/实际事务已交付；WP02-10/13原结果/审计采用可复用。原SeqService已经按PostgreSQL真实行锁参加调用方事务，不另造编号库；需要实际tenant ENG_REV规则，缺规则明确拒绝，不迁移seed默认号。

修订UUID、工程产品UUID、工作快照版本、组成候选UUID/版本、配置快照身份和并发令牌分别保存；不把工程产品身份的版本当技术Revision，不把ETag当修订号。既有Owner内容不复制成BOM/图纸/质量主账。新品无父可创建并记录原因，未解析工程产品可保存工作草稿；以后显式绑定准确原产品，一经绑定不重键。CHANGE/LEGACY_REVIEW需真实父基线/变更或legacy提供者，目前源码无对应Owner，保持明确REQUIRED_FENCED，不伪造父基线/获准变更。

采用新原生Main.Plm.EngineeringRevision.v1与PostgreSQL独立heads/不可变working_versions，同一事务分配显示号、写修订来源、初始快照/候选、部门绑定、审计和原操作。编号仅使用现有SeqService实际规则和原分配结果；失败回滚一起撤回，不重试换号。query/add/edit独立工程修订资源、scope1创建者UUID、2/3/4真实部门、5租户；创建选择可信部门/当前授权版本。原工程产品读取另需其当前query及对象范围。

工作稿仅DRAFT，显式patch操作和expectedVersion/ETag，省略字段不清空；每次保存追加快照及理由/父工作版，候选身份稳定、版本随内容变更，不搬旧检查/评审资格。配置为独立不可变草稿快照，强类型选项保持原值/十进制字符串；无真实规则时标UNKNOWN，不称CFG_RESOLVED。组成逐Occurrence保存kind/顺序、准确原Ref或明确缺项与说明；相同来源两位置不聚合。当前实际可解析的工程产品源通过原Owner读；BOM/工艺/文档/质量来源提供者缺失时只允许明确缺项草稿，未知Owner/伪造准确Ref不能变成完整成员。NOT_APPLICABLE必须有明确理由，缺实际政策仍为未核准，不伪PASS。

当前工作快照摘要只作原文完整性/草稿预览，不冒充已通过黄金向量的CD-ENG技术摘要或冻结回执。准确历史、字段/组成差异、同原键恢复和UNKNOWN查询/close；当前授权仍逐次复核，身份改变清页面私有内容。无评审/冻结/制造权限；三方父基线rebase、完整来源Provider和配置规则在实际Owner交付后继续接入，完整WP05-03不因此关闭。

选择此设计是因为将Product/Revision/WorkingVersion混用会覆盖原历史，直接接受任意来源的完整manifest会虚构Owner采用；原规范明确允许带缺项工作稿，因此先建立真实可操作的修订/快照/候选Owner及准确边界，保留全部后续出口。

## 任务与预计实际时间

预计90–150分钟：原生存储/权限/事务/编号45–75，工作快照和组成页面及相关验证30–50，集中审查/两仓收尾15–25；以Main官方START至finish记实际，Handoff不叠加。

- [x] 真实Program/Kestrel缺入口红灯；新增原生EngineeringRevision合同/存储/Owner/部门创建及PostgreSQL前向迁移，保现有工程/币种/UOM映射和无自动角色授权。
- [x] 接现有SeqService同事务编号、稳定修订身份/来源、未解析产品草稿和显式一次绑定；当前授权与源版本、CAS/不可变快照、原键恢复和提交故障回滚。
- [x] 配置/组成草稿、Occurrence/顺序、准确原产品源或缺项、显式patch和历史差异；未知规则/来源不能伪完整，父基线/变更/legacy缺实际提供者保持明确拒绝。
- [x] 实际API/DI、独立Vue页面、最小公开原操作恢复、IAM准确修订UUID选择；身份/迟到/未知结果保护。
- [x] 定向实际PG18/HTTP、组件/恢复/IAM、类型/Vite；一次任务级SPEC/QUALITY含编号共同事务、权限/租户、来源和身份分离、差异/并发/恢复。保留失败证据，输入不变复用。
- [ ] 单份W151-01/DELIVERY、唯一台账和四状态/计划/09同步；只归档W150一次并关闭WP04-33/WP05-02两个限定记录；正常PR、远端准确采用、精确分支删除、官方finish与固定席位释放。

原D2/D3/D5/D6/D7全部出口保留，原目标不缩成草稿Owner；125完整目标/247运行验收不提升。无Actions、既有环境迁移/部署、源代码副本、新工作树或真实业务配置seed。

## 实际交付

[单份报告](../../evidence/W151-01/DELIVERY.md)，登记35.85分钟；前五步完成，一次集中审查通过。最后生命周期步骤待Main正常PR/finish后由proof确认，Handoff已采用释放，原七天ACTIVE。
