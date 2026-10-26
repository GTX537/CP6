# W152-01 CFG工程选项规则及工作快照采用

COORD本聊天单人复用DEV-1/DEV-2，Main基线52bf73c40f1fbdd22c0a89b567b4f88f7940ed91。预计75–120分钟；Main START至登记28.02分钟，Handoff不叠加，最终耗时由官方finish/proof记录。WP05-03/WP05-39完整卡及原七天目标仍未完成。

## 实际实现与边界

依据原08_CFG选项/规则评审及解析要求、ENG-REV-01及获准LR02-010完整覆盖合同；独立Main.Plm.ConfigurationRule.v1，/api/configuration-rules/owner-v1与/plm/configuration-rules。单工程产品一份显式ENGINEERING_OPTIONS_ONLY规则档案，不建立第二产品主账，不约束未来全域Effectivity/替代规则集合。准确ProductRef、RuleUUID、内容版本、ETag分开；STRING/BOOLEAN/DECIMAL、必选及明确允许域，MUTEX/REQUIRES强类型条件，拒绝未知定义/域/重复条件。十进制最多原支持1024整数+1024小数位，比较只归一末尾零，不转float/decimal，原文精度保留。

独立query/add/edit/submit/approve/withdraw；scope1创建者UUID，2/3/4实际部门，5租户，实际创建票据/当前授权版本。历史/原键恢复/重放也核对当前query及工程来源权限；相同源码的原有IAM/授权机制按局部采用复用。DRAFT→UNDER_REVIEW→APPROVED；RETURN回草稿，REVISE开新草稿而旧活动批准保留。APPROVE明确绑定当前ETag/工作版和被替代批准Ref，核对真实当前工程来源，不由最高版或首项推断；WITHDRAW在新草稿/审核存在时仍可撤旧批准，保留当前稿。审批是本范围内CFG有权动作，不补签专业试验、替代兼容或制造许可。

PostgreSQLheads/不可变UTF8versions及活动批准指针；单调前沿、身份/父版/摘要/状态转换、原文禁止update/delete/truncate、延迟原文FK。业务、部门绑定、审计、Main原操作同真实事务；CAS竞争只成功一次，提交故障共同回滚，UNKNOWN只查/close。新迁移只在隔离测试数据库执行，保既有工程配置原文字节、CandidateRef及配置引用；新增可选检查字段为空时不序列化，避免改变旧候选摘要。所有旧部门映射类型保留，只定义菜单能力、不自动授角色，无真实业务规则seed。

ENG显式采用准确活动获准RuleRef，在同DbContext事务内取得原规则及工程来源、对象范围和最终当前授权；固定选项输入摘要/规则版本/逐项检查。OPTIONS_VALIDATED、UNKNOWN、CONFLICT、NO_MATCH仅描述已声明选项域，缺必选/依赖不默认，已知不匹配保留。完整适用范围始终另列待核准，不输出全域MATCH/CFG_RESOLVED。旧批准撤回/替换/来源改变不会改写原工作快照，当前读取显示缺项；新采用拒绝旧批准。没有规则的旧草稿保持原语义。

独立页面提供准确来源/部门、强类型定义与条件、提交/批准/退回/撤回、历史；批准/撤回明确展示准确版本与范围确认。工程页读取活动批准原版，不以更高草稿版替代，不自动选首个。最小公开恢复引用/Web Locks、UNKNOWN不重发、COMMITTED清旧编辑、身份/403清私有内容；IAM选择真实规则UUID。集中审查修复工程页新增布尔默认false，必须明确选择是/否；布尔占位使用真正空值，避免浏览器把文案当默认选择。

## 验证与集中审查

- behavior-20261026T192909Z：实际PG18/Program HTTP24/24；涵盖新规则HTTP、生命周期/活动版替换撤回、原文、真实权限/当前来源、精确约束、同事务ENG采用、部门2–4、故障回滚/UNKNOWN、竞争/租户/创建者及原修订兼容。
- adoption-migration-20261026T193250Z：实际4/4；补同事务ENG提交故障/目录准确产品查询及旧配置原文/CandidateRef/无自动授权迁移，重查加入嵌套源提交前复核后的2个采用用例。22未受影响的成功项复用，加4共26个不同用例，不累计重跑数。
- ui-first49/49；审查修复后ui-boolean-fixed18/18覆盖两个受影响页面，含新增布尔明确选择。复用32个未变恢复/IAM用例，共50个不同用例。送审前发现未保存修改时明确阻止提交旧内容，ui-submit-fixed7/7定向复查；其余通过结果复用，50个不同用例不变。最终vue-tsc/Vite exit0，构建体积提示保留，资产为本地dev/unknown而非签名发布。
- 失败保留：red初始测试缺命名空间编译失败；修正后red-entry真实404；ui-explicit-boolean10通过/1失败，发现null占位产生非空option值，修正两页面组件后18项通过。无跳过、无伪造成功。

证据D:/CP6-local-test/wave152/foundation/final-validation.json绑定最终源码/测试blob及真实运行来源。一次作者SPEC/QUALITY覆盖完整差异，身份/来源/当前权限、实际事务与回滚、批准替换/撤回、完整覆盖边界、历史兼容、页面恢复、迁移、唯一台账及文档链接。当前无实质阻塞。未运行真实浏览器E2E、生产或原业务验收；无Actions/Docker/既有环境迁移/部署。

## 收尾与剩余

Handoff交付时已记录PG26/UI50/type/Vite结果；其后Main补充未保存草稿送审保护并定向验证，不修改原消息历史。Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/128；source 25850b29cf37f01fc955bf7f763e35f4347b1eeb，远端 e9094691955ee09304c991399f64c0ac909ecbed，准确分支已删除、DEV-2官方finish释放。Main保留本提交的真实收尾前快照，待正常PR、远端准确采用、精确分支清理与finish；最终proof下轮准入归档一次，不在main追加自证提交。

W151已按最终proof归档一次，只关闭WP05-03.wave151_revision_drafts。本轮仅新增WP05-03/WP05-39.wave152_configuration_rules两个限定记录。完整375卡仍24DONE/2IN_PROGRESS/349PLANNED；125目标0完整完成、247运行验收NOT_RUN，41历史静态不冒充运行验收。原七天ACTIVE，仅D1/D4整阶段闭合，2/7=28.6%只是阶段比例。完整适用范围/生效规则、替代、BOM/工艺/图文/质量来源、父基线/change/legacy、rebase/工程评审冻结及制造采用仍待实现。Continue actual engineering BOM/routing/document/quality sources and complete configuration applicability, parent baseline/change/legacy/rebase/review/freeze for original D3 formal WO and SCHED; retain all original D2/D5/D6/D7 exits.
