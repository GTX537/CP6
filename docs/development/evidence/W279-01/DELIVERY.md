# W279-01 原验收盘点与准确草稿预演

W279：原OA-DEF27项验收逐项核对，并补准确草稿/当前授权/无业务写入的EXPLANATION_ONLY预演，实际页面到PostgreSQL已验证。

[24小时持续计划](../../planning/20261102-codex-24h-oa.md)。最新确认基线`f750b0b7e0e0da2108a549ce094ba7c375953085`；COORD单人复用DEV1/Main与DEV2/Handoff，无子代理。

## 产品行为与实际采用

新增POST /api/oa/v2/definitions/FLOW/{key}/simulate，封闭输入为draftVersionId、expectedVersion、varsJson。准确当前Draft/token、资源及SchemaJson/FormKey读取权限在已有认证根事务内核对；不要求发布/编辑权限，不使用会写记录的publish-preview代替预演。模拟变量以原JSON文本传输，严格对象/重复键/深度/大小边界，使用现有精确表达式，不经JavaScript浮点往返。

结果固定EXPLANATION_ONLY，明确当前登录人为模拟发起人、假定审批同意和变量不再变化。线性、优先级及并行/包容分支使用现有语义；人员沿现有纯StagePlanner/ApproverResolver解析，要求当前user:query以及适用组织/映射读权。无权时不查询/披露候选人、人数或账号状态。返回的当前候选不是实际办理授权或业务准入。

服务任务/子流程不执行，汇聚同步、循环、不可求值条件、未注册依赖和超出预测预算均明确UNPROVEN并停止相应后续推断，不以默认空值冒充执行成功。普通预测路径限512个节点，人员展开和总输出有独立解释预算；预算不改变保存或运行时合法合同。没有调用Start、业务提交、通知或connector，没有创建发布预览、命令/审计/实例/任务/表单数据/服务工作记录；只读事务退出不commit。

现有FLOW草稿工作区增加预演入口和非敏感模拟变量编辑。未保存修改、Published版本和隐藏schema不能预演；输入/版本/身份改变清预测，迟到响应丢弃。拒绝权限清私有内容，格式/stale/临时错误保留模拟输入；无关storage事件不清内容。浏览器不缓存变量或结果。原发布/激活/提交流程保持各自权限和事务。

## 实际验证与一次集中审查

12项唯一后端行为通过（4项隔离PostgreSQL18，其中1条实际Chrome/Vue→Main→PG链；8项纯预测规则），后端0跳过；12项唯一相关Vue行为和显式应用vue-tsc通过。模拟变量/IAM/业务数据，真实内部Owner、数据库、登录会话和HTTP，无Actions、既有环境迁移或生产部署。

逐行为最新来源见`D:/CP6-local-test/wave279/foundation/final-validation.json`。真实HTTP先红（404缺入口）后绿；页面入口先红后绿。Chrome通过实际登录/设计器，输入9007199254740993.00与9007199254740992得到不同准确分支，读取当前真实模拟主体，修改草稿后清预测；PG核原token、单保存命令和零业务/预览副作用。组件测试使用模拟API，未冒充实际HTTP；连接器用会抛错的明确模拟实现验证调用数为零。

真实失败日志保留。最后审查补充了预演人员总展开/输出预算和不相关storage事件保持；分别定向验证对应PG/组件，正常浏览器/其他规则输入未变的成功结果复用原记录。后端12项按唯一行为去重；前端12项含已有工作区行为，最后定向入口测试未选择的七项已有同任务通过记录，不冒称再次执行。应用类型检查显式-p tsconfig.app.json。

作者一次任务级SPEC/QUALITY集中审查完整源码/测试/文档diff：原规范与实际采用范围、当前资源/字段/人员授权、准确草稿/精度、无副作用与明确不确定性、可控展开、UI错误/身份/版本、真实证据复用及375状态。无全仓回归、无额外审查代理，普通文档仅查差异及引用。所有本窗PG/API/Chrome已停止；不执行既有数据迁移或生产部署。

## 原 OA-DEF 27 项验收逐项核对

核对原文 `docs/originals/00/00435e88e979f79e__OA-DEF-01-DEVELOPER-SPEC-R1.md` 与 S3-COMMON-CONTRACT-R1，原文来自本机完整成果归档；未修改原始候选文本。下表的“已有证据”仅指当前已采用 v2 路径，复用各窗口报告及 `D:/CP6-local-test/waveNNN/foundation/final-validation.json` 内实际行为来源，不将文件存在、旧设计接受或本次阅读当作新执行。原 125/247 正式验收状态不改。

| 原 AC | 当前核对 | 真实证据与剩余 |
|---|---|---|
| AC-D01-01 新建默认停用、稳定 Key、单 head/draft/receipt | v2 已有证据 | [W271](../W271-01/DELIVERY.md)复用 `Authoring_creates_disabled_atomic_head_draft_exact_original_and_edits_CAS_preserving_extensions`；FORM 独立 Owner 见[W269](../W269-01/DELIVERY.md)。 |
| AC-D01-02 原命令重放及异 body 冲突 | v2 已有证据 | W271 原命令与草稿回归；当前命令槽含租户、环境、实际人、操作和 Key，不因重放产生第二稿。 |
| AC-D01-03 两作者争抢相同旧 token | 部分 | FLOW CAS、原子保存及撤权锁已证；[W269](../W269-01/DELIVERY.md)有 FORM 两连接竞争。未把它们冒充“两名 FLOW 作者同时保存”完整原场景，留准确场景补证。 |
| AC-D01-04 准确发布版本克隆、清身份码、无执行 | v2 已有证据 | [W271](../W271-01/DELIVERY.md)准确旧源、独立停用新稿、来源不可变、原件恢复、无实例/任务；不调用引擎或连接器。 |
| AC-D01-05 图/循环/深度/注册校验且定位 node/edge | 部分 | [W268](../W268-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md)有拒绝、循环及八层递归实证；既有发布诊断仍主要为代码数组，并非全部错误都有精确 node/edge 定位。W279 只补预演路径表达式定位。 |
| AC-D01-06 受资源权限限制、仅解释、无业务写入 | W279 新增证据 | 当前草稿/token、字段/资源/人员读权、HTTP 和实际浏览器→PG；无实例/任务/表单数据/通知/服务工作/发布预览写入。服务、子流程、汇聚及超预算部分明确 UNPROVEN，不宣称完整执行模拟。 |
| AC-D02-01 字段/table/布局/默认值/扩展完整往返 | v2 已有证据 | [W268](../W268-01/DELIVERY.md)、[W269](../W269-01/DELIVERY.md)原 schema 文本及精确原数字往返；[W275](../W275-01/DELIVERY.md)表单/子表数据精度。 |
| AC-D02-02 重名/嵌套复杂子表/超 200 行声明拒绝并定位 | 部分 | FormSchemaValidationV1 及原 table 边界拒绝非法结构，W268 有字段路径行为；全链结构化 fieldErrors 和每类 table 错误的精确 UI 定位尚未逐项闭环。 |
| AC-D02-03 顺序 compute 后端权威、禁用控件不越权 | v2 已有证据；交互仍余 | [W275](../W275-01/DELIVERY.md)精确服务端 compute、当前字段授权、隐/只读值保留；disable/enable/setOptions 的完整前端规则体验及独立纯计算预览仍待实现，不以控件 disabled 作为授权。 |
| AC-D02-04 新 draft 不改旧 FormData schema | v2 已有证据 | W268 原版本数据保持、[W275](../W275-01/DELIVERY.md)准确提交 pin、[W278](../W278-01/DELIVERY.md)新旧 FORM/FLOW 实际实例 pin 并存。 |
| AC-D02-05 文件源越权拒绝、隐藏字段不泄露下载 Ref | 部分 | 无 File Owner 准入时发布/提交明确拒绝；当前通用字段投影不越权。实际文件接入和最终下载边界未在本普通 FORM 路径闭环，不能把拒绝全部上传称作完整文件验收。 |
| AC-D03-01 GET draft 无写、显式创建下一版 | v2 已有证据 | [W269](../W269-01/DELIVERY.md)、[W270](../W270-01/DELIVERY.md)head/draft 分离、读取不建稿及显式创建；W279 本轮读取/预演前后计数与 token 不变。 |
| AC-D03-02 同 publish 命令只一版本/依赖/receipt | v2 已有证据 | [W270](../W270-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md)两类 Publication 原件重放与准确依赖；W278 实际页面采用。 |
| AC-D03-03 stale/缺 token/依赖变化及同门撤权原子裁决 | v2 已有证据 | W270/W272 预览与最终授权、真实撤权等待、保护缺失和回滚；依赖原 pin 仍合法时保留，不擅自改 latest。 |
| AC-D03-04 所有旧 helper/designer caller 处置 | 部分 | W271 旧身份互斥；[W274](../W274-01/DELIVERY.md)–[W276](../W276-01/DELIVERY.md)新原生运行与旧读写 fence 已证。旧独立链保留，完整所有 caller 清单和逐入口采用/精确拒绝尚未全量核销。 |
| AC-D03-05 发布不启用/不启动业务/不改旧 pin | v2 已有证据 | W270/W272/W274；W278 实际页面显式分开发布、激活和提交，并核原实例不变。 |
| AC-D03-06 提交后失 ACK 原命令恢复 | v2 已有证据 | W270/W272 两类 publication commit 故障/原件恢复，写权撤销后仍按当前读权投影；W278 普通填单页面另有真实丢响应恢复，二者不混称同一次执行。 |
| AC-D04-01 Published 不可变 | v2 已有证据 | W270/W272 数据库保护、旧发布原文和隐藏字段投影；W271 克隆不改源版本。 |
| AC-D04-02 在途根/子实例保持原依赖 | v2 已有证据 | [W274](../W274-01/DELIVERY.md)真实嵌套子任务及准确依赖，W275 普通 FORM 采用；W278 新旧实例精确 pin 实证。服务节点实际 Owner 执行仍为独立余项。 |
| AC-D04-03 并发下一草稿仅一版 | 部分 | W270/W272 串行 token/已有稿拒绝和数据库单 draft 约束已采用；两个独立连接同时创建下一稿的准确原场景尚未单独补证。 |
| AC-D04-04 legacy 当前 head 重建明确来源 | 未闭环 | 本次源码检索及原入口核对未取得 LEGACY_RECONSTRUCTED/UNVERIFIED_LEGACY 的完整采用证据。v2 不接受旧可变 head 作为准确发布源，但这不等于旧重建路径已标识；不执行迁移或回填来制造证明。 |
| AC-D04-05 旧版本作新 draft 且原源不变 | v2 已有证据 | W270/W272 选择明确旧 Published、记录 source_version_id，分配新 draft 版本，不把旧版本改回 Draft。版本比较和来源友好展示另见 W280 计划。 |
| AC-D05-01 同 FORM 并发启用仅一 FLOW | v2 已有证据 | [W273](../W273-01/DELIVERY.md)真实两连接竞争、PG 条件唯一约束和冲突原子回滚。 |
| AC-D05-02 停新不取消旧实例 | v2 已有证据 | W275 当前提交资格；W278 实际停 FORM 后办理已接受任务，旧 pin/其他实例保持。 |
| AC-D05-03 无准确发布/合法依赖不可启用 | v2 已有证据 | W272/W273 当前递归依赖与真正激活源，不用 head.Enable 代替资格。 |
| AC-D05-04 enable 原件重放及旧 token 拒绝 | v2 已有证据 | W273 原 command 不重复历史、不重新占绑定；CAS 不覆盖后发停用。 |
| AC-D05-05 FE 冲突后回读真实状态 | v2 已有证据 | W273 管理页 Vue 冲突/未知恢复；W278 实际准确版本启停采用。 |

上述是验收范围核对，不是将 27 项整组或 WP12 整卡标成完成。除表内余项，UC-D08 的稳定身份版本比较由 W280 接续；文件/选项真实 Owner、正式历史下载、完整 SFS patch/三版本、任务退回重送/转交加签/无人审批、准确日历和 Worker 仍分别保留。WP12-19 的已证业务采用限真实迭代 Owner，不推广为其他业务 Owner 已采用。


本次同时修正台账scope_reconciliation的过时余项：WP12-01不再把已交付FORM Owner/发布/克隆列为缺口，WP12-02不再把准确依赖/唯一绑定/普通FORM运行/在途影响列为未做，WP12-19不再把已验证的迭代委派及明确再授权列为未实现。后者本窗仅核对证据，不计入新的预演产品验证；其他Owner实际采用仍保留。

## 时间、下一步与生命周期

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不改。W280准确版本比较接续；完整字段/图诊断、FLOW双作者与下一稿竞争原场景、旧重建来源标识/全部旧caller、文件/选项Owner、正式下载、SFS patch/三版本、退回等任务操作、日历Worker及其他业务Owner采用仍保留。

本窗实际耗时按wave279-final-verification.json核销。原估计2–3h低置信度；如实用实际执行修正，不能把本窗当作24h目标完成。总目标继续IN_PROGRESS，下一累计12h校准；W280从最新已确认主线单独开工，不在本分支混入比较功能。

Main/Handoff正常远端采用、准确本地/远端任务分支删除、干净固定工作区及官方finish/席位释放见`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave279-final-verification.json`。本提交仍为合并前快照；用户主目录资料和可复用缓存保留。
