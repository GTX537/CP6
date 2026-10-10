# PLM-CHG-01 · 工程变更、受控通知与跨 Owner 义务闭环

整理状态：`core_semantics_consolidated`。CHG 原 Target 由当前 ECM 文档承接。当前主册 5,589 行正文及三组追踪表已实读；本册整理业务设计，不把 75 个文档入口当作 75 个独立功能，不证明真实 Owner 已采用或业务测试已通过。

## 1. 目的、操作者和业务 Owner

申请人提出为什么要变、更改哪些源对象和目标约束；协调人组织分域影响评估及工作义务；有权领域评审人、独立最终批准人决定准确版本及范围；各执行 Owner 提交实际事实；关闭批准人核对本变更自己的全部必要条件。[原文E:5126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5126) [原文E:5140](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5140) [原文R:22](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:22)

ECM 拥有 ECR/ECO/ECN、总体变更范围与裁决、关闭和自身恢复协调记录。ITER 拥有计划/任务，ENG 拥有 Candidate/Manifest/Baseline，VER 拥有要求评价/复用/确认，REL 拥有用途许可，CFG 拥有正式适用规则，MES/WMS/采购等持有真实作业、库存与经营事实；OA 运行审批与投递。ECM 只能请求、引用、核自己的 Gate，不建立第二套任务、质量源账、库存账或 PASS/许可决定。[原文E:5140](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5140) [原文E:5160](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5160) [原文R:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:61)

ECR 回答是否组织变更；ECO 批准工程变更工作；ECN 通知已批准内容和需采取的行动。`AUTHORIZED` 应显示“变更作业已批准”，不能显示“已放行生产”。ECO 取消、ECN 撤回和 ECM ON_HOLD 都不能推导 MES 已停工或 WMS 已处置。[原文E:5128](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5128) [原文E:5234](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5234) [原文E:5359](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5359)

## 2. 精确规范、批准与限制

采用 RC1＋RV01 整包 SHA `4b1934e7747e8ac3f0cb6787469775d28cf891b1b35ff0a351323466e0bdbc91`；当前 06_ECM 汇编保留 R04 原文 SHA `bc0e1154226b584763ff3ddb459bfa74e9161f26e6c69facdfd78c9267685e1a`，其中包含 R03/R02/R01。七页、75 文档动作/处理组，保留历史草案标记；按明确批准记录及精确补遗读取当前效力。[原文A:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:21) [原文A:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:25) [原文E:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:13) [原文E:30](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:30)

R05 准确批准原八项 B 政策及 Owner/能力承接，不批量批准全部候选 API、状态、数据库和空配置；CHG→ECM 是文档承接而非原件等价恢复或 Target 改名。RV01 是固定文本装配与 12 主题定点 Review，不是所有业务组合穷尽验证；正式 Excel 一致性和历史对应按登记保留。[原文R:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:15) [原文R:74](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:74) [原文R:129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:129) [原文A:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:31) [原文V:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:9) [原文V:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:63)

R04 总表是 12 REQ、46 BR、176 FIELD、75 ACT、19 PROC、36 CHK、8 逻辑 IF、62 AC/TC。原文 ACT 与 PROC 检查取并集；R04 明列补充：ACT-02-006/PROC-001 加 CHK-004；ACT-02-009/PROC-003 加 CHK-012；ACT-04-004/PROC-004 加 CHK-001；ACT-04-007/PROC-006 加 CHK-015。编号存在与映射成立不证明业务通过。[原文E:1674](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1674) [原文E:1708](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1708)

## 3. 实体、身份、字段与计量

以下是逻辑记录和源字段含义，不是新增数据库表定义。[原文E:5160](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5160) [原文E:3247](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3247)

| 记录/字段组 | 必须保存的业务身份与边界 |
|---|---|
| ECR：ECM-FLD-02-001～018 | ECR ID、提交版、生命周期与独立裁决、来源 Owner/对象/受控原件、准确源版本、已知/未知范围、目标约束、候选方案（含不变更）、希望日期、责任/必要性、不可覆盖裁决、延期触发、逐行 ECR→ECO 范围版覆盖、独立关闭原因和后继关系。未来 Baseline 未产生不必伪填。[原文E:3251](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3251) |
| ImpactAssessment：03-001～024 | 评估 ID/Revision、确切 ECR/ECO 范围版、领域 Owner、原事实/版本/ObservedAt、查询边界/截断/未授权覆盖、必要性、结论、状态、拟处置及原任务、阻塞阶段、差分重评、原渠道外部确认、并行 ECO。评估成本不成为财务账。[原文E:3359](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3359) |
| ECO：04-001～020 | 当前批准范围版与待修订版并列、源对象集、目标/交付条件、真实 ENG 映射、被采用评估版、工作义务/Owner 原任务、阶段和关闭条件、角色规则/签署对象、修订差分/继续暂停决定、当前 Gate、VER/REL/CFG 引用、ECN 投影、恢复与后继链。[原文E:3503](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3503) [原文E:3515](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3515) |
| ENG/VER 追踪：05-001～026 | 对象行/义务、批准允许/禁止变化、ITER Candidate/version/reviewPackageDigest 与 ENG Candidate/version/technicalManifestDigest 分列；实际 Baseline、变化与覆盖关系、接收决定；VER Plan/Version、要求适用性、TrialRequest、真实受测对象/制造快照、原始证据、Evaluation/DesignConfirmation 和当前适用性。[原文E:1901](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1901) |
| ECN/准备：06-001～028 | ECO 批准范围版、ECN ID/version/后继、准确活动/真实 Baseline、Scope 包含排除及未知、CFG 规则/解析、REL 许可版/用途/数量单位/时段、专业条件、原 Owner 准备、外部确认、计划/正式/实际时间、预批准分割、义务/收件范围、Delivery/Acknowledgement/Application、剩余对象、撤回后果、签前依据。[原文E:2034](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2034) |
| 关闭：07-001～020 | 待关闭范围版、原 Owner 对象版/回执身份、对象行/义务关系、实际数量与单位/时点、剩余与未知、三类通知回执、全范围清点、义务去向、当时合法性/当前剩余条件、签前差分、硬阻塞、允许移交/观察要求、不可覆盖关闭、来源案件独立状态、取消核对/迟到事实。[原文E:3623](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3623) |
| 恢复：RCV-001～016 | Recovery ID、发现页/人/时间、ECR/ECO/ECN/对象行/义务/范围、异常类型、原 Owner/对象/版/原渠道、OccurredAt/ReceivedAt/处理时间、覆盖、协调人/Owner 实际接收、原请求查询、冲突事实、逐 Gate 原因、限制/继续决定、同源/独立关系、证据/决定和 RESOLVED 快照。[原文E:289](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:289) |

领域必要性 `REQUIRED / NOT_REQUIRED_WITH_BASIS / UNDETERMINED`，影响结论 `UNASSESSED / IMPACT_IDENTIFIED / NO_IMPACT / UNKNOWN`，评估生命周期 `DRAFT / SUBMITTED / REVIEWED / STALE` 分开。REVIEWED＋UNKNOWN 可表示评审完成但事实仍未知，不能放行依赖阶段；仅有完整覆盖证明的 0 条才支持无对象/无影响。[原文E:3182](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3182) [原文E:3389](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3389) [原文E:4171](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4171) [原文E:4282](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4282)

统计明确对象类型、批准范围版、过滤条件与观察点；ECO 按 ID 去重，义务仅在必需证据完整时计完成；已批准不再适用、接受移交、实际完成分栏。筛选 Site 不改总体分母，不同单位不相加；多种阻塞标签可重叠但不能重复累计主数量；无充分分母/覆盖不能显示 100%。本册未定义统一加权 KPI。[原文E:70](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:70)

## 4. 状态、动作与本地写集

ECR：`DRAFT → SUBMITTED → TRIAGED → ASSESSING → DECIDED → CLOSED`。裁决另存 `ACCEPT_FOR_ECO / RETURN_FOR_INFORMATION / DEFER / REJECT / CLOSE_NO_CHANGE`。退回建立后继草案；DEFER 留原因/责任/复查日期或事件，恢复前重查事实，延期不关闭。撤回是请求及关闭原因，不凭空新增 ECR WITHDRAWN 枚举。[原文E:3121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3121) [原文E:3749](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3749) [原文E:3799](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3799) [原文E:3859](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3859)

ECO：`DRAFT → UNDER_REVIEW → AUTHORIZED → IN_EXECUTION → READINESS_REVIEW → EFFECTIVITY_TRACKING → CLOSURE_REVIEW → CLOSED`。另有 ON_HOLD/CANCEL_PENDING 与有据 CANCELLED。生命周期、批准范围版、当前 Gate、Owner 实际状态四层分开；历史已批准且当前 BLOCKED 是合法组合。[原文E:3107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3107) [原文E:3137](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3137)

| 动作及入口 | 前置、写入与禁止推导 |
|---|---|
| ECR 提交/分流/评估/裁决：ACT-02-001～005 | 提交固化来源/源对象/已知范围/目标；分流标责任与未知；领域评估保原版本与覆盖；裁决只改变自身申请决定，不产生工程/制造结果。[原文E:3749](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3749) [原文E:4167](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4167) |
| 覆盖/关闭/撤回：02-009～012 | 接受后逐源对象行绑定 ECO 范围版；拒绝/无需变更可按有据原因关闭申请但不称源问题解决；ACCEPT_FOR_ECO 的全部行须完成处置或再裁决。ECO-B 取消不会被 ECO-A 完成冲掉。[原文E:3829](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3829) [原文E:4139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4139) |
| ECO 编制/批准：04-001～004 | 固定送审范围、评估版、角色、义务/阶段/关闭要求；批准前重核依赖事实；G2 可没有最终目标 Baseline 或最终 VER 确认。只写准确批准版、签署和工作义务；退回保旧评审，新草案不改旧签字。[原文E:3939](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3939) [原文E:4181](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4181) |
| 启动/就绪/应用/关闭申请：04-005～008 | Owner 实际接收且开始才能 IN_EXECUTION；真实工程结果及必要专业依据进入准备评审；同一活动上下文的许可/规则/外部确认/准备满足才进入分范围跟踪；全部必要回执和范围清点 READY 才提关闭。日期到达和 ACK 不够。[原文E:3979](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3979) [原文E:4001](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4001) |
| 批准后修订：04-009～010 | 旧批准 v1 保留，v2 草案并列；逐目标/范围/义务/条件/责任/时序差分，旧义务逐项保留、修改、增加、拟取消或移交。新批准版与旧→新映射受控保存；Owner 未接收/应用不能显示已采用。不可证明独立的受影响后续放行暂停。[原文E:3156](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3156) [原文E:4019](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4019) [原文E:4195](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4195) |
| 暂停/恢复/取消：04-011～014 | 写 ECM 控制决定、准确范围、原阶段、各 Owner 处置请求/引用。未知保留 ON_HOLD/CANCEL_PENDING；恢复重核当前依据且需 Owner 接收；全部已发义务最终处置已知才能 CANCELLED，不删除现实事实或自动恢复旧许可。[原文E:4039](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4039) [原文E:4256](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4256) |
| ENG 接收/验证：05-001～009 | 真实结果先 UNDER_REVIEW，逐对象行/义务核覆盖及批准边界后 ACCEPTED_FOR_ECO；超范围退回/重评，既成事实保留。一个产物覆盖多行要逐行证明，多产物缺一不能靠一个链接完成；ECM 不重新算双摘要或写 VER PASS。[原文E:1829](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1829) [原文E:2175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2175) |
| ECN：06-001～011 | 草案可保缺项；正式评审/批准/发出须真实 Baseline 和必需正式规则/许可、准确 ECO 版/义务/收件范围。提交评审保 DRAFT 并记录审查，不发明 UNDER_REVIEW 枚举。批准内容不可改，发出前重核，未来生效可正式通知但不能宣称已应用。[原文E:1865](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1865) [原文E:2227](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2227) |
| 关闭：07-001～007 | 关联原 Owner 回执，核当前范围及新增对象、必需后果/外部确认/观察、原义务和恢复；签前变化退回重核。CLOSED 记录不可覆盖，退回保原申请和已完成事实；ECR/质量源记录各自独立关闭。[原文E:4079](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4079) [原文E:4240](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4240) |

工作义务请求、Owner 接受、真实开始、完成证据是四个阶段。ECO 的本地写集限自己的版本/决定/义务与引用、Gate 和恢复投影；禁止由 ECM 直接修改原 Owner 主账。准确数据库事务、存储字段和物理 API 不在当前 ECM 原文冻结范围。[原文E:3551](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3551) [原文E:5140](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5140) [原文E:5121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5121)

## 5. 接口、事件与依赖

原逻辑接口保留 `ECM-IF-001` 来源引用；002 ITER/ENG 批准范围与真实产物；003 VER 要求差异/计划/复用复测/确认；004 REL 用途范围时序及许可限制；005 CFG 切换意图/正式规则；006 ERP/MES/WMS/采购分域评估、义务、实际对象版/结果/未决；007 OA 审批/投递；008 DEV/QCL/LCM 依赖。它们不占旧 H42/H43/H44/H53，也不是已冻结 HTTP route、消息 Topic 或错误码。[原文E:5260](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5260)

同一目标活动必须核齐 Baseline、用途、Site、配置及订单/批次/序列/时间范围。不能把 Site-A 的 CFG、Site-B 的 REL 和另一 Baseline 的 PASS 拼成 READY。CFG MATCH 只是适用性；REL 仍按当前用途/范围/数量/时点/条件裁决，真实采用由执行 Owner 证明。[原文E:1853](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1853) [原文E:2473](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2473) [原文R:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:61)

试制链保持 `VER TrialRequest → REL 本用途授权 → MES 真实试制 → 实际受测对象/原始证据 → VER 评价`，避免把最终确认设为全部早期试制的循环前置。条件确认只按已批准 FB02 类型及各活动最晚前置采用，不能由 ECM 勾选完成或豁免硬证据。[原文E:1841](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1841) [原文E:5190](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5190) [原文E:2473](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2473)

审批整合应联读 [OA-APP-01](D:/CP6/docs/CP6_开发设计文档_20261010/modules/OA-APP-01.md) 的当前持久协议。OA 决定投递与 ECM 业务应用必须分别证明，`DecisionOutbox → OwnerInbox → ApplicationReceipt` 同进程也不能绕过；这是跨册整合要求，不宣称 ECM 历史逻辑 IF 已冻结其 wire/schema。[原文O:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/0c/0cc2a482fd1dc879__OA-APP-01_业务审批绑定与回调_前后端开发Spec_v1.1.2_R2_REVIEW.md:14) [原文O:218](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/0c/0cc2a482fd1dc879__OA-APP-01_业务审批绑定与回调_前后端开发Spec_v1.1.2_R2_REVIEW.md:218) [原文E:5270](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5270)

## 6. 事务、幂等、并发与恢复

批准、修订采用、应用放行、关闭必须绑定确切送审版和被核事实；签前发现相关事实变化须差分重评并阻断依赖 Gate，保存旧批准。当前原文明确未指定数据库锁，因此实现时须补写 Owner 原子提交/版本比较合同，不能用“曾 READY”代替最终一致性保证。[原文E:4382](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4382) [原文E:4211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4211) [原文R:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:87)

ECN 同版补发不产生新业务义务；旧版已读不覆盖新范围；已经合法实际执行的事实不可因通知重发伪造再次执行。原 Owner 相同事实多处引用去重，冲突更正按可证明的原版本/因果关系，不按 ReceivedAt 选最新；发生时间、接收时间、处理时间分别留存。[原文E:1871](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1871) [原文E:1879](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1879) [原文E:2990](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2990) [原文R:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:100)

统一恢复候选流：`OPEN → TRIAGED → OWNER_CONFIRMATION_PENDING → RECONCILING → REVIEW_READY → RESOLVED`，并保留 UNRESOLVED/MANUAL_DECISION_REQUIRED。协调人被指定不等于 Owner 已接收；分类不代根因；相同证据可以共享原查询，不同原因/范围不能凭标题合并。[原文E:89](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:89) [原文E:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:100) [原文E:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:114)

原服务恢复可达只说明能够查询；NOT_FOUND、超时、连接成功和再次 ACK 不证明未执行或已停止。按原义务查原 Owner，不盲发新的执行请求。解决 A 只恢复被重新证实的 A 范围/Gate，B/C 独立原因继续阻塞；RESOLVED 也不自动批准原业务动作，应回原页再次核权限和当前依据。[原文E:117](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:117) [原文E:468](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:468) [原文E:573](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:573)

CLOSED/CANCELLED/RESOLVED 后的新事实建立后继异常/变更，保原决定、当时证据与时间；错误采用旧 Baseline 也必须如实保留，不能改成期望 Baseline 使之合格。[原文E:123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:123) [原文E:1879](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1879) [原文E:4129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4129)

PG05 将“份额结清”与“业务记录关闭”分开：REL/DEV 只按自身计量、原键及相关子范围封闭核可释放剩余额；ECM 按自己全部必要后果决定关闭。涉及剩余额是否仍能发生的未知先查清，其他无关质量/库存后果可独立未结；REL 结算不能替 DEV 签字。[原文E:5581](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5581)

## 7. 权限、租户、审计与业务政策

每份 ECO 至少明确工程责任、协调、独立最终批准职责；申请人不得成为自身变更唯一最终批准人。受影响验证、安全质量、制造、库存、采购或客户领域各 Owner 给意见；无依据不能归低风险，必要角色未知/未配置不能以零审批通过。真实人员、代理期限、组织岗位、分类判据仍是实例输入。[原文R:22](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:22)

纯文字勘误只有能证明技术含义、范围、职责和约束均不变时可追加受控更正，不能覆盖原批准正文。加急只压缩等待、并行评估和优先协调，不减少许可、强制验证和必要外部同意；立即隔离/止损由有权原 Owner 独立处理并留事实。[原文R:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:27) [原文R:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:35)

默认整包；分范围应用/关闭须预先证明对象集合、依赖、共同条件、专业许可和各自关闭标准，不能默认不同 Site 天然独立。未完成强制验证、许可、未知执行和必需不符合处置不可改称非阻塞移交；仅非关闭前置工作才可在有权确认和真实接受证据/检查点后移交。[原文R:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:48) [原文R:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:113)

跨页进入动作重新核当前对象/范围权限、批准版和事实；授权内部分数据不可访问时保 UNKNOWN，未授权不得泄漏对象或数量。租户/对象/范围约束继承各原 Owner 与公共 SYS/IAM 合同，本 ECM 原文未给可直接复制的 tenant DTO 或最终权限 code。[原文E:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:65) [原文E:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:67) [原文E:655](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:655) [原文V:44](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:44)

审计保每次提交、裁决、版本差分、角色规则/签署对象、外部原件版本/范围、Owner 实际事实、发生与接收时点、义务结转、继续/暂停依据、关闭快照与后继。客户/供应商同意走原业务渠道原件，内部会签/沉默/到期均不能替代；是否强制读、谁确认、期限语言与权限按适用规则实例化。[原文E:3563](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3563) [原文E:3695](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3695) [原文R:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:100)

## 8. 页面和服务端行为

| 页组 | 画布信息与服务端约束 |
|---|---|
| ECM01 工作台 | 六维并列：申请/审批、ENG 接收、VER、REL/CFG、投递/知悉、真实应用/关闭。身份置顶、责任事项与阶段/范围/事实、阻塞和恢复、原件链接；9/10 且一项未知不能靠进度图放行。无批量强制 READY/完成生产/重发所有执行入口。[原文E:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:55) |
| ECM02 申请/分流 | 来源原件、源对象/未知范围、目标/候选方案（包括不变更）、裁决/延期、逐行覆盖；草案阶段不强求未来 Baseline，拒绝理由与来源问题状态并列。[原文E:3251](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3251) [原文E:3749](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3749) |
| ECM03 分域评估 | 必要性、结论、状态三列，原 Owner 事实/版本/查询覆盖、各域方案成本/风险、并行 ECO、明确阻塞哪个 Gate；0 条不等无影响。[原文E:3359](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3359) [原文E:3869](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3869) |
| ECM04 受控变更包 | 当前批准版/新草案对照、目标/义务/原任务、技术结果、当前各 Gate、范围修订与结转、暂停/取消原后果；服务端只核准相应状态动作。[原文E:3503](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3503) [原文E:3939](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3939) |
| ECM05 工程和验证 | 预期与实际逐行对照、双 Candidate/双摘要、接收与专业结论独立、真实受测对象与证据、差分适用性；不得手填 PASS 或把接收工程产物等同制造许可。[原文E:1816](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1816) [原文E:1901](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1901) |
| ECM06 准备/ECN | 同活动上下文矩阵、三类时间、正式规则与许可、预批准分割、准确通知版/收件范围、Delivery/Ack/Application 分列；计划生效未到不显示已切换。[原文E:1853](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1853) [原文E:2034](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2034) |
| ECM07 处置/关闭 | 全范围含新增对象和未知、各原 Owner 真实结果、剩余量/单位、义务去向、必要观察/外部确认、恢复原因、不可覆盖关闭与原案件独立状态；退回不清空既成事实。[原文E:3201](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3201) [原文E:3623](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:3623) |

恢复可从七页进入，但仍引用同一有据事实。导航与刷新是观察行为，不能隐含批准或原 Owner 重执行；历史记录可读不等当前操作可用。[原文E:128](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:128) [原文E:386](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:386) [原文E:448](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:448)

## 9. 实现顺序与已知代码基线差异

建议的实现拆分（整理建议，不新增已批准业务规则）：先固化逻辑身份/版本/范围与可审计义务，再接 ECR/分域评估和 ECO 准确送审批复；接 ENG 双身份结果及 VER/REL/CFG 当前条件；随后 ECN 三类回执、实际采用/关闭与统一恢复，最后做工作台投影。每层以原 Owner 可证明事实和明确输入配置为前提。[原文E:5160](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5160) [原文E:5178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5178) [原文E:5260](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5260)

本组未在此新审代码，也不把历史 WP0 当作当前 ECM 实现。父报告已固定的主线代码差异和移植范围需接入具体实现任务后逐项核验；当前设计本身明确没有数据库、路由、消息 Topic 或锁实现冻结，因此不能据手册臆造已实现接口。[原文E:5121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5121) [原文A:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:35)

## 10. 验收场景与证据状态

全部 62 个当前 AC/TC 保持 `DESIGNED / NOT_RUN`；文档映射可解析不升格为业务 PASS。[原文E:16](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:16) [原文E:4554](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4554) [原文V:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:88)

| 场景组 | 必须观察到的结果 |
|---|---|
| TC001～010 | 无未来目标也可有据提交；ECO 不代 REL；未知不当无影响；事实变化依赖重评；超范围真实结果不冒接收；并行冲突不最后保存者胜出；通知不当应用；部分 Site 不整体关闭；取消迟到保事实；不可改专业结论。[原文E:4556](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4556) |
| TC011～024 | v1/v2 分离、延期不关、拒绝不判源问题解决、0 条需完整覆盖、新增 WO 重评、取消/移交不删义务、新版获批未应用、零审批不通过、关闭签前新事实重核、未知请求不超时取消、迟到不改旧史、自然到期与合法历史分离、多 ECO 逐行覆盖。[原文E:4636](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4636) |
| TC025～042 | 双身份/双摘要、产物覆盖、阶段验证、真实受测配置、同上下文许可规则、计划/实际生效、正式通知缺基准阻断、外部确认原件、旧 ACK 不盖新范围、实际错版保留、撤回/乱序/重复和工作开始回执边界。[原文E:2687](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:2687) |
| TC043～062 | 9/10 不放关；权限/覆盖未知不泄漏；筛选不改分母；多引用去重；原页再校验；协调人非 Owner 接收；可达不消 UNKNOWN；A 解决不消 B/C；同源与独立原因分开；超期不通过；终态后继；正常全链、退回/修订、勘误、暂停恢复、撤回/关闭退回、外部原渠道。[原文E:1055](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:1055) |

追加整合验收仍须取得实际输入：准确租户/对象权限、当前角色规则、原 Owner 版本和未知结果查询、签前变化竞争、同版重送去重、各域原生事务/幂等回执。本文未执行这些场景。[原文E:4382](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4382) [原文E:573](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:573) [原文O:218](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/0c/0cc2a482fd1dc879__OA-APP-01_业务审批绑定与回调_前后端开发Spec_v1.1.2_R2_REVIEW.md:218)

## 11. 缺口、差异及退出条件

八项 R05 政策已获准确批准，不应把主册保留的旧 OPEN 字节全报为当前未决；但真实岗位/代理、分类判据、加急权限/复查、每个 ECO 独立分区/依赖、Site/时区/序列定义、外部收件/确认/期限、观察指标/窗口与保留政策必须按最晚前置实配。退出条件是各活动具备真实有据实例，而非填统一默认值或设 0 天观察。[原文R:20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:20) [原文R:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:32) [原文R:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:45) [原文R:58](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:58) [原文R:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:71) [原文R:110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:110) [原文R:124](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/2d/2dc3a9621b1a50a1__APR-CP6-PLM-R05-001.md:124)

物理接口/错误码/库表、跨 Owner 提交点与回执幂等、防签前变化的实际原子机制、平台审批持久链适配仍需实现合同；Owner 软件采用及 runtime 验收为 UNPROVEN/NOT_RUN。原文未冻结的机制不能由整理代理代替 Owner 决策。[原文E:5273](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:5273) [原文E:4382](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ba/ba3fc706e3b19ec7__06_ECM_当前阅读主册.md:4382) [原文A:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:21)

PG03/04/05直接动作及跨页断言、R05原推荐包、RV01定点详情已按语义与精确复用归并；本册完成当前必要材料整理，但不是全部场景的业务审签。历史资料缺口与当前有效设计分开记录，后继解释不能伪恢复原编号。[原文A:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:31) [原文V:78](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:78)

## 12. 实际阅读与追溯

当前 ECM 主册正文分段实读完整覆盖 1～5589；其中 1308～1612、2817～3054、4751～5091 三组追踪表按全部非空行结构化读，工具截断的 2840～2886 已补读，无遗漏语义行；其余 1～1307、1613～2816、3055～4750、5092～5589 全文逐段读。保守阅读标签为 structured（正文全读＋表格全行结构化），不把标题扫描计全文。R05 批准全 141 行、基线批准全 37 行、RV01 汇总结论全 90 行已实读并在本轮复核相关段；其他合读件的实际范围见阅读台账。

精确源 SHA、分段记录和未读附件见 [本组阅读台账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)；共享合同见 [本组合同](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)。本地文档验证只检查结构/链接/行号和证据字节；业务测试、构建、迁移、Actions 均未执行。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
