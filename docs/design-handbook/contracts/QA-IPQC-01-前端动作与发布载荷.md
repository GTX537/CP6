# QA-IPQC-01：前端动作与发布载荷

本附册供前后端共同实施。来源为恢复版 R4 的前端契约与五组载荷出版记录；所有组件/客户端路径是设计指定位置，`actualImplementationFiles=[]`，前端构建未运行。不能从本文推定现有应用已经实现这些动作。

## 1. 页面、身份与恢复入口

六个页面依次是工作台、任务明细、处置审批、工程交接、恢复中心和历史审计。页面角色只决定导航/展示；例如任务页采用 `QA_IPQC_INSPECTOR`，I11/I15 仍要求服务端 `QA_IPQC_REVIEWER`，I13/I14 要求 `QA_IPQC_APPROVER`。不能将按钮可见等同于写入授权。

每个动作携带原始 `commandId/requestKey/inputDigest` 与八项 expected 版本：来源、映射、家族目录、任务、观测集、结果集、决策序列和 writer epoch。输入/结果摘要与版本是不同字段，不用摘要替代版本。时间字段保留源 `date-time` 约束，数量保留十进制字符串精度。

动作具有 LOADING、READY、SUBMITTING、COMMITTED、REJECTED、UNKNOWN、STALE_CURRENT、OWNER_PENDING 八种页面状态。UNKNOWN 保留原始请求并通过 C01 查询；不能自动生成新意图或重算旧载荷。只有明确需要刷新并重新表达业务意图的拒绝，才进入新命令流程。

旧 `/mes/quality-inspection` 在 writer epoch 切换后通过精确 LegacyMapping 路由到新任务只读页面；旧列表仍为历史只读。缺策略、范围或基线的旧 PASS 不转换成新质量接受事实。

## 2. 二十个写入动作

| API / 动作 | 服务端角色 | 请求 → 结果 | 端点（统一前缀 `/api/quality/ipqc/v1`） | 源位置 |
|---|---|---|---|---|
| I03 解析策略/任务上下文 | `QA_IPQC_PLANNER` | `PolicyResolveCommand` → `QapPolicyTaskContext` | `/policy-contexts/resolve` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:61) |
| I05 创建检验任务 | `QA_IPQC_PLANNER` | `CreateTaskCommand` → `TaskDetail` | `/tasks` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:763) |
| I09 批量登记观测 | `QA_IPQC_INSPECTOR` | `ObservationBatchCommand` → `ObservationCommitResult` | `/tasks/{taskId}/observations` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:1385) |
| I10 追加观测修正 | `QA_IPQC_INSPECTOR` | `CorrectionCommand` → `CorrectionCommitResult` | `/tasks/{taskId}/observations/{observationId}/corrections` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:5332) |
| I11 独立复核观测 | `QA_IPQC_REVIEWER` | `ReviewCommand` → `ReviewCommitResult` | `/tasks/{taskId}/reviews` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:6215) |
| I12 提议复验 | `QA_IPQC_PLANNER` | `RetestProposalCommand` → `RetestProposalResult` | `/tasks/{taskId}/retest-proposals` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:8380) |
| I13 激活复验轮次 | `QA_IPQC_APPROVER` | `ActivateRetestCommand` → `RetestActivationResult` | `/tasks/{taskId}/retest-episodes` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:8918) |
| I14 记录复验裁决 | `QA_IPQC_APPROVER` | `RulingCommand` → `RulingCommitResult` | `/tasks/{taskId}/retest-episodes/{episodeId}/rulings` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:9396) |
| I15 冻结完整结果集 | `QA_IPQC_REVIEWER` | `FreezeResultSetCommand` → `ResultSet` | `/tasks/{taskId}/result-sets` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:9930) |
| I16 拟定处置分区 | `QA_IPQC_PLANNER` | `DraftCommand` → `DispositionDraftResult` | `/tasks/{taskId}/disposition-drafts` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:15327) |
| I17 冻结审批包 | `QA_IPQC_PLANNER` | `FreezePacketCommand` → `ApprovalPacketResult` | `/tasks/{taskId}/approval-packets` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:17950) |
| I18 审批质量决策 | `QA_IPQC_APPROVER` | `ApprovalCommand` → `Decision` | `/tasks/{taskId}/approvals` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:18485) |
| I19 发布后继决策 | `QA_IPQC_APPROVER` | `SuccessorCommand` → `Decision` | `/decisions/{decisionId}/successors` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:24057) |
| I20 撤回决策 | `QA_IPQC_APPROVER` | `WithdrawCommand` → `Decision` | `/decisions/{decisionId}/withdraw` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:26496) |
| I23 重评来源变化 | `QA_IPQC_REVIEWER` | `SourceReassessmentCommand` → `SourceReassessmentResult` | `/sources/{canonicalId}/reassess` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:27125) |
| I24 接收入站Owner结果 | `QA_IPQC_ADAPTER` | `OwnerResultCommand` → `OwnerIngressResult` | `/owner-results` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:27631) |
| I26 恢复冻结交付 | `QA_IPQC_RECOVERY` | `DeliveryResumeCommand` → `FrozenDelivery` | `/deliveries/{deliveryId}/resume` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:28018) |
| I29 向PLM交接原始证据 | `QA_IPQC_REVIEWER` | `HandoffCommand` → `FrozenDelivery` | `/plm/evidence-handoffs` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:28343) |
| I33 导出审计产物 | `QA_IPQC_AUDITOR` | `ExportCommand` → `ExportReceipt` | `/exports` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:29863) |
| I35 映射旧检验记录 | `QA_IPQC_MIGRATION` | `LegacyMapCommand` → `LegacyMapResult` | `/legacy/{legacyId}/map` | [动作原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:30253) |

I09 的观测值采用互斥的数值/代码分支，不能同时填写；六类资格事实分别提供有效期、撤销时间、控制头、权限与覆盖范围。I10 追加修正链，不覆盖原始观测。I11 保留逐观测的 VERIFY/RETURN、理由及 authority。I15 需要完整 slot 集、逐范围 outcome、总体覆盖和当前观测集版本；复验通过不能删掉旧 FAIL 事实。

I16/I18 的分区保持结果语义：ACCEPTED→ACCEPTED、REJECTED→REJECTED、HOLD→HELD 或 REWORK、PENDING→UNEXAMINED。每段须带准确 outcomeRef、半开范围与 segmentId，不能仅提交合计数量。I17 冻结 task/result/draft 对应的包；I18 的审批证据必须是同一条复合事实，并在同一 UoW 中绑定审批人、权限、stage、任务/结果和审批包，不能由客户端拆成分别有效但组合无效的证据。

I24 保存 Owner 原始 body 和 digest、messageId 与 sequence；I26 使用已冻结 deliveryId 和 expectedLeaseEpoch。I29 原始证据必须分别保留 source、test execution、sample、actual baseline、method/environment、raw records、coverage、quality task/result/decision 与 documents。PLM-DOC 引用只展示 ref/state，不把文档不可授权误作不存在。I33 只生成导出产物；I35 的 writer epoch 检查不能跳过。

源字段名 `successReceipt` 在 I19、I26、I35 示例中实际绑定 `Problem` 拒绝/未知终态。实现应按 outcome.state/terminalType 显示真实结果，不能因为这个字段的名称而展示“成功”。三例分别是后继测量未完成、Owner 结果未知、旧 writer epoch。

## 3. 五组载荷的冻结边界

| 载荷 | 样例业务内容 | 文档中的持久化演进 | 采用边界 |
|---|---|---|---|
| EXEC disposition（PROPOSED） | F01：100 单位中接受90、拒绝10 | F01 checkpoint 中的冻结记录 | portion 身份映射见 F01 附册；不是外部发送凭据 |
| OUTPUT task result | F02：任务v4、来源v3、范围[0,100)、状态CREATED | schema-only 示例，无该交付关系演进 | 不把它当作新增运行步骤 |
| OUTPUT disposition | F02：接受80、返工10、未检10 | 连续夹具保留冻结载荷 | C40来源版本2与D60来源版本3的 receiptSubject 分别保留 |
| PLM raw evidence | F03：10样本/10原始记录、[0,10)、基线v7/方法v4/环境v3 | F03-I29 的持久化交接示例 | 原载荷是 RawEvidenceHandoff 本体，不是命令外壳 |
| EXEC task result（PROPOSED） | OWNER_PROTOCOL_NOT_READY，taskRef为空 | schema-only 拒绝示例 | REAL标签不能替代适配器就绪证据 |

所有出版记录的注册状态为 DESIGN_REGISTERED，运行采用为 UNPROVEN，externalSend 为 NOT_RUN。PLM 的出版 destination 为 PLM，注册 destination 为 PLM-VER，开发接入应保留并显式处理这一区别，不自行改写已冻结字节。

## 4. 本次阅读与核对范围

前端 20 个动作、791 个字段出现位置和 63 种字段属性组合已按完整字段表阅读；前端 request/response 与连续案例的 40 个对应对象逐值相等，40 个摘要一致。427 个固定 Owner 读体的摘要一致；参数化阅读视图可逐项还原全部 427 个原体。五组出版载荷的 38 项 body 摘要已核对。它们属于文档字节/引用一致性检查，未执行前端、API、数据库或业务验收。

[前端原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/9a/9a3c4c7f7927e1bd__FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json:1) · [载荷出版原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/27/27edb910bb416a4f__PAYLOAD_PUBLICATIONS_RECOVERY_R4_SYSTEMIC.json:1) · [模块主册](../modules/QA-IPQC-01.md) · [类型字段索引](QA-IPQC-01-类型字段索引.md) · [关系约束](QA-IPQC-01-关系约束与检验边界.md)
