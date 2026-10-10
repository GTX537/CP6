# PLAN-EXC-01：版本选择、评审与归档资格

本附册回答开发时应该读哪一版、哪些旧阻塞已关闭、哪些接入条件仍需实现和验证。它只整理归档证据，不执行归档中的命令，也不授权部署。

## 1. 当前有效设计

当前接受的是原始五 Spec 的 CP12 静态功能设计，主文 v1.11，Stage100。原功能接受时间为 2026-10-06 03:26:38Z；恢复归档附加资格 E09 在 10:01:57Z 获得后继批准。两次记录职责不同，不能重复计分或把恢复记录当成第二次业务验收。[当前正式入口](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ef/ef2087f07e47b1a8__CURRENT.json:9>)

|维度|正确读法|
|---|---|
|需求与业务验收|40 个新 REQ，103 个新 AC；五 Spec 分别 25、18、15、19、26 个 AC|
|设计专业评审|A 103、B 21、C 12 个精确作用域，合计 136；不是 136 个业务测试|
|作用域构成|97 叶契约、17 聚合契约、10 角色提案、12 补充作用域|
|业务测试执行|103 AC 均 NOT_RUN；本次文档整理也未执行它们|
|实际接入|Owner/Core 实际采用、注册、授予为 0；Provider、DB、all-writer、保留、迁移、部署及签验签运行仍 UNPROVEN|
|原文中的授权字段|记录当时 ImplementationAuthorized=false 等状态；它们是被整理的历史材料，不替代用户在当前会话中的请求|

专业角色的“采用”是被指定评审者对精确静态设计的意见。A 不能代替 B/C 采用，B/C 也不能替代上游 Owner 或实际组织批准。[136作用域及边界](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ef/ef2087f07e47b1a8__CURRENT.json:538>)；[运行门](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ef/ef2087f07e47b1a8__CURRENT.json:709>)

## 2. 五个载体共同构成当前版本

不能只读名称最新的 CP12 ZIP：351 个当前逻辑成员由完整 CP08 基础包和 CP09–CP12 后继成员按组合索引选择，展开字节数 487,356,950。

|来源载体|被当前组合选中的成员|
|---|---:|
|CP12|12|
|CP11|2|
|CP10|4|
|CP09|7|
|CP08|326|

组合索引选择的是精确完整成员，不是文件名最大版本或差分补丁。旧成员仍存在不等于当前生效；后继新正文也不能把原来已撤回的整册 PASS 恢复。[组合与载体](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ef/ef2087f07e47b1a8__CURRENT.json:163>)

## 3. 两处正式阅读更正

### B 的 PC09 对 A 的依赖状态

原 B 机器记录 `/decisions/9/crossGroupReadDependencies/2/currentNormalDependency` 及原报告第 163 行仍含历史 OPEN 字符串。正式更正仅在 contractId、正文哈希、decision/dependency 索引均相符时，将读法更正为 `SATISFIED_CURRENT_CP12_STATIC_DESIGN`；原文件字节保留。

材料父版本链、八阶段同 slot/body 及 A 当前 103 个静态作用域已在该限定范围关闭。不能据此改掉所有 OPEN、覆写历史 RETURN，或认为实际运行采用已完成。[精确更正](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f78f1cfe23d32f0f__CP12_B_PRECISE_PC09_A_DEPENDENCY_READING_OVERRIDE_20261006.json:26>)

四个关键 A 作用域分别是 `MrpOutputNativeFact`、`CP03/EXC/PLAN-MRP/NATIVE`、`EXC-PC02` 和 `EXC-PC04`。原合同阅读可按完全相等对象复用；当前正常链中新 native body、ref、header 与消费者的关系仍由新评审判断，不能自动沿用旧实例采用。

其中真实父版本为 CP09-A，派生为 CP11-A；`versionKey` 标量、ref 与被引用 header 必须一致。CP12 必要的 Source PAGE/CAPTURE 后继不会创建新的 Run、Attempt 或 Publication。原协调请求 34,755 字节、slot/P1 及八阶段链保留，Preparation 10.500 先于 Accepted 11。[A精确证据](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f78f1cfe23d32f0f__CP12_B_PRECISE_PC09_A_DEPENDENCY_READING_OVERRIDE_20261006.json:54>)

### AC 导航计数

旧 first80 导航的 14/14/14/18/20 不是当前最终 AC 范围。后继正式澄清为 103，按五 Spec 分配 25/18/15/19/26；它不修改原报告，也不宣称业务测试已经执行。[AC范围更正](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/43/43a55b3defd8a1c8__CP12_GOVERNANCE_COMPLETE_EVIDENCE_INDEX.json:639>)

## 4. E09 缺件限定如何影响开发

原计划为 192 个归档文件。实际恢复 182 份原件，加上 4 份明确标识为新增的恢复记录，当前 REV2 为 186 份。缺失的 10 份是历史上传后的本地辅助身份回执，不是功能设计、正式评审或专业采用正文。

11 份历史正式报告/撤回报告已另行恢复；CP03 的辅助回执由当时完整日志精确恢复，不能推广为其他十份也已恢复。每份缺件的原路径、字节、SHA 和预期 Git blob 都保留在资格文件中，不能造替代文件冒充原件。[十份缺件与恢复依据](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c1/c1e43ed46ec61cb9__ARCHIVE-RECOVERY-REV2-QUALIFICATIONS.json:8>)

资格文件内 `RootAddedArchiveQualificationReview=PENDING_NOT_FABRICATED` 是成文时点；后继 CURRENT 完整记录了实际批准。开发阅读应同时保留两者时间关系，而不是把 PENDING 抄成今天的持续阻塞。此次整理仍将这十份列为缺失，不将“对开发非阻塞”等同于“归档齐全”。[E09后继批准](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ef/ef2087f07e47b1a8__CURRENT.json:646>)

## 5. 上游依赖与作用范围

|输入|作用范围|开发约束|
|---|---|---|
|PLAN-SUP、PLAN-MRP|原始直接 RequiredFenced|按 EXC 精确角色、body/root、消费者、动作和授权范围接入|
|PLAN-POL|继承的 RequiredFenced|策略、日历与资格栅栏；不是额外原始直接接口|
|WMS-STOCK、PLAN-ATP、PUR-PR、PUR-PO|限定范围支持输入|其 Stage100 或 PO CP22 评审不能替代 EXC 独立接入|

支持材料按被具体消费的精确正文与附件读取；不能把支持模块整包接受解释为 EXC 全部接口、现场 Provider 或生产写权已成立。Stock H31 的只读 discovery 接受也不自动授予 EXC 写库存权限。[直接与继承依赖](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ef/ef2087f07e47b1a8__CURRENT.json:299>)；[Stock支持入口](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/90/90b6c19cd6ab391f__CURRENT.json:1>)

原始准备阶段的 Registry81、Stage10/assigned-not-started 与随后 Registry82 的 PO CP22 支持资格各有时点；它们保留来源链，不能覆盖当前 CP12 Stage100。原始 H11/H24 仍要求一次完整输入/输出快照、同版本移动替换，防止半结果和双计。DP02/03 区分预留、覆盖与履行，DP05 安全库存只计一次，DP09 保留真实 Owner 转换及部分恢复责任。[原始范围](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d3/d329857cafbe35d9__PLAN-EXC-ORIGINAL-SCOPE-AND-QUALIFICATIONS.json:1>)；[来源与正式指派资格](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7c/7ca5b6a21c6fafcd__SOURCE-QUALIFICATIONS.json:1>)

## 6. 本次整理证据与尚未继承的阅读

八份治理/来源 JSON 已对全部唯一内容作语义阅读，并对 48 个重复对象核对精确等同；REV2 索引全文已读。验证仅证明整理读视图保真，不能证明其中引用的报告或大包成员已全文阅读。

完整阅读记录见 [治理阅读证据](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-governance-reading.json)；136 作用域身份核对见 [矩阵验证](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/PLAN-EXC-01-governance136-validation.json)。整个 EXC 目标仍按根阅读台账逐成员核定，未因本附册自动关闭必要材料范围。
