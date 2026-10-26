# W151-01 工程修订、工作快照与配置/组成草稿

Main基线dee70996e1fc2149ce60eb20065e4eb96c9e9e5e；COORD本聊天单人复用DEV-1/DEV-2，无新工作树。预计90–150分钟，Main START至登记35.85分钟，Handoff不叠加；最终耗时见官方finish和wave151-final-verification.json。WP05-03完整卡及原七天目标仍未完成。

## 范围与实现

核对WP05-03、ENG-REV-01及原03_ENG修订/工作快照/组成/编号段落。原生Main.Plm.EngineeringRevision.v1通过/api/engineering-revisions/owner-v1和/plm/engineering-revisions提供草稿目录、创建、显式patch、历史及差异。Revision UUID、Product UUID、WorkingVersion、候选UUID/版本、配置快照UUID/版本、ETag分别保存，工程产品身份版本不冒充技术修订。

新品无父必须填写原因；允许产品尚未解析的草稿，后续显式绑定真实产品UUID，一经绑定不改身份，同UUID来源升级需明确选择当前准确版本。显示号只取现有SeqService实际租户ENG_REV规则；原文记录规则身份/属性/分配值/时刻，无默认号或seed。实际规则行锁参与调用方事务，编号/头/快照/部门绑定/审计/原操作共同提交回滚；原键重放不重新分配。产品+修订号冲突包括后绑定冲突明确拒绝，无新快照。

独立query/add/edit，scope1创建者UUID、2/3/4可信部门、5租户。创建必须选当前授权部门/授权版本，真实事务一次性票据绑定。原产品当前query及对象权限独立核对，撤权后历史/恢复/重放均拒绝；原产品变动保历史并显示缺项，新采用需准确当前启用来源。未解析历史不回填后来的产品内容。

仅DRAFT工作面，SET_GOAL/SET_PRODUCT/SET_CONFIGURATION/CLEAR_CONFIGURATION/UPSERT_COMPONENT/REMOVE_COMPONENT显式patch，省略不清空。expectedVersion与ETag共同CAS，追加不可变UTF8原文/digest/原因/父工作版。候选身份稳定；配置每次明确保存为新独立不可变快照，旧工作版保原配置。STRING/BOOLEAN/DECIMAL强类型，十进制字符串保持原值，支持最多1024位整数及1024位小数，无指数/正号/负零；v1显式长度和8MiB原文限制，不改变其他Owner存储合同。

组成逐Occurrence UUID保存kind/路径/语义顺序/适用性/说明/准确SourceRef，同产品两发生位置不聚合。工程产品源由实际原Owner同事务读；EBOM/MBOM/ROUTING/DOCUMENT/QUALITY_SPEC无真实Provider时，只可保存明确缺项草稿，伪完整Ref拒绝。NOT_APPLICABLE须有理由且仍待政策核准。差异读取两份准确历史，逐字段和Occurrence比对。

无配置规则始终DRAFT_ONLY并列缺项，不称CFG_RESOLVED。CHANGE/LEGACY_REVIEW缺真实父基线/获准变更/legacy Owner明确拒绝；三方rebase、完整Provider、配置规则、评审/冻结、CD-ENG规范化技术摘要及制造采用未实现。当前digest只作原文完整性/草稿预览，只有DRAFT状态，不宣称验证了IN_REVIEW/FROZEN状态机。

前向迁移保既有工程原文、编号规则/计数与所有原部门映射类型；菜单极大ID安全分配，只定义能力不授角色。数据库保护原文/身份、一次产品绑定、单调版本、父工作版、摘要及延迟原文外键；Down拒绝逆向清历史。独立技术schema保tenant约束，并非全库租户schema迁移。

独立页面提供新建/目标/产品引用/强类型配置/组成位置编辑移除/历史差异，显式准确patch；身份变化清私有输入并拒迟到，403清已载内容。浏览器仅存公开恢复引用，Web Locks排他，UNKNOWN只查询/close、不自动重发；COMMITTED清旧编辑并刷新目录。IAM选择准确修订原UUID。

## 实际验证与集中审查

- red-20261026T183538Z真实HTTP404红灯保留；first-20261026T184355Z仅creation-context1通过，不称创建成功。
- behavior-20261026T185322Z测试代码params/new类型推断编译失败，无执行项；behavior-fixed-20261026T185645Z实际17项中16通过，配置项受.NET非回溯正则节点上限失败。改为简单线性模式加显式位数上限。
- decimal-collision-20261026T185959Z实际2/2：配置/精确数值及上下界/Occurrence/历史差异修复验证、后绑定编号冲突。forward-20261026T190227Z实际1/1：既有原文/规则计数保留、菜单极值、无自动授权。
- 共19项去重成功，复用未受小数改动影响的16项加2和1，不冒称重跑整套。含实际HTTP创建/patch/重放/撤权、原号/异体/CAS/不可变历史、一次产品绑定与源变化、部门2–4与撤权、编号/绑定共同回滚、UNKNOWN关闭、竞争写、租户/创建者、缺真实父/change/legacy拒绝、原工程/币种部门及前向迁移兼容。
- ui-first.json35/35：生产Vue组件8、恢复4、IAM23；API/Web Locks为受控边界，无真实浏览器/E2E验收宣称。vue-tsc/Vite exit0；新增UI测试后typecheck-tests再次exit0，Vite运行时输入未变复用。体积警告保留，dev/unknown资产不是签名发布。

证据D:/CP6-local-test/wave151/foundation；final-validation.json绑定真实来源和最终运行时/测试blob。一次作者SPEC/QUALITY覆盖同事务编号、权限/租户/准确来源、各身份分离、CAS/原结果、迁移及页面隔离；完整diff、台账范围、链接核对。修复后绑定冲突和小数正则节点问题，定向行为通过，无未解决实质阻塞。无Actions、Docker、既有环境迁移、真实业务配置或部署。

## 收尾与后继

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/127，source4c89878580c25b31acbd46e49768323dfa3f4209，远端c41a19fd92f8ce7d033be26c7669af0d9fc64852；准确分支已删除且DEV-2官方finish释放。Main本提交保留真实收尾前快照，待正常PR/远端准确采用/分支清理/finish；最终proof由下轮准入归档一次，不在main追加自证提交。

只新增WP05-03.wave151_revision_drafts限定记录。W150已按proof归档一次并关闭WP04-33/WP05-02两个原限定记录。375卡仍24DONE/2IN_PROGRESS/349PLANNED，125目标0完整完成、247运行验收NOT_RUN，41历史静态不冒充运行验收。原七天ACTIVE，仅D1/D4整阶段闭合，2/7=28.6%只是阶段比例。Continue actual engineering source/configuration policies, parent baseline/change/legacy and rebase/freeze prerequisites for original formal WO Root/Snapshot/Occurrence and SCHED; preserve all original D2/D5/D6/D7 exits.
