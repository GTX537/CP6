# QA-NCR-01 返工消费与两 Owner 合同附册

本册解释当前组合保留的 CP4_R1 consumer5 与 Owner 补件 CP2。文件中的候选标签、当时 REQUIRED_FENCED、后续准确设计采用和真实运行资格分别保存；本册不把示例里的 READY/authority 字符串解释为实际登记。全部正常图及AC字段已补读；26项准确设计采用证据已合并，当前必要材料整理闭合；真实运行资格仍未证明。

## 1. 一项返工工作的身份链

MES Binding 准入只说明允许执行；完成需要已被 MES-WO 采纳的 OPERATION_ENDED/EXECUTION_CLOSED、FINISHED 全范围及当前 Registry/Validation。NCR 独立绑定完成后才可使用针对本轮实际 occurrence 的重检。返回库存/报废、让步等义务不由这个纯返工 consumer profile 处理。[G1 范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7f826b9ac90ce2f3__MES-WO-EXECUTION-COMPLETION-ADDENDUM.md:7) [G2 范围](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/84/843d545df4c46088__QA-IPQC-EXEC-OCCURRENCE-MAPPING-ADDENDUM.md:7)

旧 range 的 portionId 可以是 PORTION-1，旧单位只有 id/version；新 target 的 UUID portion、三字段 OwnerRef、cycle 和本地到 MES 映射只能来自受控 IdentityApproval/UnitAssignment/CycleAssignment/ScopeAssignment。相同字符串、EA 或数量不能自动建立业务身份。scope assignment 含双方批准及完整 requiredMeterProfileRefs，不从现有 occurrence 反向猜所需计量。[身份权威](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ca/cae430c1bf292ad1__LEGACY-IDENTITY-PRODUCER.md:21)

PublishLegacyNcrIdentity 锁 rank20 身份/单位/authority、30 MES 源、60 NCR，重核同一个原 cut，原子追加原件锚、投影、cycle/scope、身份 authority、target 与 current CAS；不改原 NCR 业务 head/Decision/work/command、不写 MES Actual/QA Task/Use。GET 只读已发布关系。撤回追加后继，最终消费同步核 current。[身份发布](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ca/cae430c1bf292ad1__LEGACY-IDENTITY-PRODUCER.md:33)

原 head6 由 LegacyHeadAnchor 保存完整旧行及原 bytes，窄 normalized head 不取代全行相等；后续 head7 用新 HeadEvidence，previousHeadRef 指原 normalized head6。CoreProfile 独立于身份 ProducerProfile，明确 EXACT_RECOVERED_CORE 或 HYPOTHETICAL_STATIC_DESIGN_CORE；当前静态世界的后者不恢复丢失旧 core/codec。[head 与 core](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/ca/cae430c1bf292ad1__LEGACY-IDENTITY-PRODUCER.md:59)

## 2. G1 完成证据

|检查|实施规则|
|---|---|
|准入与当前版本|admissionBindingRevision、event 采纳版、ClosureProof 输入版、Validation 输出版、当前 Binding/Registry 分别保存；通过具体后继链证明，不强求版本数字相同。|
|流与终态|从登记 anchor 到 throughSequence 无洞，eventRevision 与 ownerSequence 各核前驱；同事实 transport 别名不二次计量，同版异文为冲突。|
|计量与范围|逐 required profile 核稳定 identity domain/key/meter/point/真实 baseline 与 occurrence；不同 profile 不加总。finishedScope 等于完整 requestedScope，多 portion 不互抵。|
|后继与反证|原 ClosureProof、producer fence、全部 descendant 完成与 current validation 可达；无 MAY_EXECUTE、未知、待采纳反证。根内无关 Binding 更新也需新 validation。|
|时间|事件 commit ≤ adoption ≤ capture ≤ publication ≤ 实际 read < validUntil；exact 历史 proof 不因 TTL 被删。|
|生产者|当前/历史 read 零领域写；MES 已采纳 hook 或 Owner 持久 publisher 发布投影。投影等待为 PENDING，不把缓存旧 proof 当 current。|

[G1 八项算法](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7f826b9ac90ce2f3__MES-WO-EXECUTION-COMPLETION-ADDENDUM.md:52)

只有 FOUND_CURRENT_FINISHED 携完整 proof；PENDING/UNKNOWN/CONFLICT/SUPERSEDED/UNAVAILABLE 各保真实原因，不能给部分 proof 凑成功。read 的 NO_NEW_EFFECT 只说明本次查询无新效果，不能推出历史未执行。完成消费唯一业务键按 target/cycle/operation，不能用 global UNIQUE(eventRef) 禁止合法不同消费，也不能由此自动放开跨 target 复用。[状态及消费边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7f/7f826b9ac90ce2f3__MES-WO-EXECUTION-COMPLETION-ADDENDUM.md:76)

## 3. G2 重检与四套坐标

阶段 A 在 Task 测量/批准前冻结真实 ExecSourceLineage、QAP SourceMapping、NCR target 及 bridge；阶段 B 只把已存在 Task/Decision/ResultSet/Coverage 连接起来。Decision.mappingRef 指原 QAP mapping，不是包含 Decision 的复合 proof，避免摘要环。[两阶段映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/84/843d545df4c46088__QA-IPQC-EXEC-OCCURRENCE-MAPPING-ADDENDUM.md:21)

每段同时保 source S、MES M、canonical C、NCR L 的身份域、portion、单位与范围。Tsm(S)=M、Tsc(S)=C、Tcl(C)=L，均为完整无洞、无重叠、单值双射。AFFINE_EXACT 使用正 Int64 分子/分母、精确带符号 offset，端点不能舍入；DISCRETE 另需完整成员双射。QAP 原 localRanges 永远是 S，不能覆盖成 L。[转换](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/84/843d545df4c46088__QA-IPQC-EXEC-OCCURRENCE-MAPPING-ADDENDUM.md:48) [集合等式](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/84/843d545df4c46088__QA-IPQC-EXEC-OCCURRENCE-MAPPING-ADDENDUM.md:60)

本版重检要求 FULL_COUNT：population=ResultSet/Coverage=C，measured=C，inferred/uncovered=[]，每个必需 requirement 的完整 slot 覆盖交集成立；原量测 value/code 恰一非空，原 value='1.0' 保真。完整 accepted SourceQA schema 允许的抽样/推断形状不自动成为当前 consumer 已启用的 profile。Decision 分区必须全部 ACCEPTED/ACCEPTED；让步、报废、免检不在本路径内。[覆盖与质量](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/84/843d545df4c46088__QA-IPQC-EXEC-OCCURRENCE-MAPPING-ADDENDUM.md:74)

G2 current guard 包括同 effect 的 G1、原 IPQC FinalGuard 和 QAP 当前 source/family/policy/control。原 F05 wire 不增加字段，新调用包装参加已登记 ambient UoW；NCR 不进入原 QAP EffectLocator 的旧三枚举。A10 绑定后到 A14 close 仍重新核全套，不以历史重检绑定当永续许可。[同事务使用](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/84/843d545df4c46088__QA-IPQC-EXEC-OCCURRENCE-MAPPING-ADDENDUM.md:92)

## 4. 一次提交、恢复与物理行

新结果/head 与 guard/check/Use/children、原 aggregate current CAS、slot/audit/outbox/checkpoint/FactCommitMap 在同一登记数据库事务内形成。UseKey 的 SINGLE/COMPOSITE 共用业务唯一性；多返工成员整单 one commit，任一 guard 失败全回滚，不逐 partition 成功关闭。ConsumerResult 的 resultingHeadVersion 为数值，不引用未来 head body；result/guard/Use 的实际写入按 FK 父存在顺序完成。[复合 Use 与写序](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b1/b1a6c30ccac8f4dd__DATA-MODEL.md:77) [精确键](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d3/d3b76c7f876a8b66__TRANSACTION-AND-STORAGE.md:124)

同 intent 已提交先返回原 slot 保存的 AppliedBytes/ReplayBytes，仍核当前恢复权限和原 principal。proof 过期或后续 current 变化不修改历史成功；未提交且业务语义不变才能同 intent 刷新运输 request/proof/head。NOT_OBSERVED、超时和非 200 均不证明无历史效果；缺原件为 INTEGRITY_UNAVAILABLE，不用 latest 补链。[恢复合同](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/af/af6790ef2da68f4c__API-AND-RECOVERY.md:1)

ER4 是 owner/id/version/digest；OR3 没有 digest；BR5 保 owner/streamId/businessId/revision/digest。四个 typed DTO 表的 56 映射保完整 E 与原关系：CompletionOccurrenceLink 的 occurrence/profile/point/baseline 为 OR3，event 为 BR5；ReinspectionSlot 保 observationRevision 与必填 ruleRef；ReinspectionSegment 保四坐标和两项 transform；ConsumerTargetCurrentCheck 保 identity/assignment/source/core/UoW 关系。JSON segment 保全字段，typed query projection 不得丢嵌套信息。[全部 typed 映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/00/00b71b675424067f__STORAGE-REFERENCE-MAP.json:1)

## 5. 浏览器发现、详情与固定读截止

浏览器从 workId 调 evidence discovery，不预知 proofRef，也不传 producer registration、原 bytes、principal、guard 或 currentVector。BFF 从已存 work/command/admission/identity authority 建 G1 请求；G2 index 以 E/NCR/work/partition/cycle/boundCompletion/semanticTarget 找已发布 snapshot 与 bridge，不按 root/operation/同数量猜 source。PENDING 无 candidate，冲突不任取最新。[发现](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c7/c752dbae7ff87e4d__BFF-DISCOVERY-AND-DETAIL.md:5) [G2 索引](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c7/c752dbae7ff87e4d__BFF-DISCOVERY-AND-DETAIL.md:33)

completion/reinspection 详情每次核该 proof 确属此 work/cycle 以及专业字段权限。一次完整集合上限沿 schema（10,000 members/slots、500 ranges），响应超过 8 MiB 或缺必要原件整体阻断，不能剪掉 slot 再标完整。S/M/C/L 的域、单位、转换均来自公开 DTO；原 Observation QapUom 两字段不借 MES 单位补 owner。[详情和披露](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c7/c752dbae7ff87e4d__BFF-DISCOVERY-AND-DETAIL.md:50)

A17 REWORK 历史页用同一 SNAPSHOT 的 GlobalCommit cut，从不可变事实先取每 work 最新再过滤；排序为 GlobalRv 数值、UUID 网络字节序。游标固定 15 分钟上限且每页重核权限，HMAC 绑定 E/route/query/principal/全部成员摘要；不续期、不换 latest 接旧页。历史页不能当关闭 basis。[固定 cut 与游标](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c40da0dc609c4eaf__PAGINATION-AND-FRONTEND.md:3)

A20 在单一当前 cut 完整枚举全部 partition/work/义务，只读诊断，无 guard/Use/刷新任务写。500 close members、10,000 blockers、8 MiB 任一超限即 typed failure；不能截断后 eligible=true。真实 runtime 尚未 ready 要显示准确的运行阻断，不重新说 Owner 缺设计。页面“制造完成”“重检已绑定”“关闭后反证待复核”分别展示；未知提交保持原 intent，不乐观 closed。[诊断与页面](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c4/c40da0dc609c4eaf__PAGINATION-AND-FRONTEND.md:25)

## 6. 类型及本次阅读界限

Consumer84、Owner93、Legacy12、Support68 定义已逐字段/分支/约束结构化阅读；SourceQA36 与当前 IPQC 精确相同并明确复用。516 个 consumer 字段和 423 个 Owner 消费字段的全部来源/持久/guard 元数据已按 44/40 个精确组阅读，每一行回建与原件相等；56 存储 mapping 的 shape 与原 consumer schema 全等。这些检查不等业务运行。

Main6 候选 C# 有 NCR-TYPE-01 四处 null-only 被声明为非空 long，CP4 另有 int GuardRef => null；FIELD 有 NCR-FIELD-01 五处 nullable 注释与同条 shape 冲突。实现前按原 Schema 校准声明/元数据和严格 converter。TS/C# record/readonly 不等输入校验，Case.Value 是进程内包装，wire 不能增加 Value 层。[语言及转换器义务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/23c597a0e6230a92__LANGUAGE-CONTRACTS.md:49)

已闭合阅读：Main171 普通对象、CP4 255 catalog/37 messages/3 row sets、Owner215 catalog/22 messages/31 timeline，以及50/102/68 AC全部字段；26 scope采用正文已精确复用主代理完整补读并合并。完整旧 Core 原件资格与本次真实执行仍分开保留；没有运行作者脚本、SQL、编译、业务测试或 Actions。


## Owner 普通世界：从发生事实到 NCR 关闭

本节已读 215 个 catalog 全封套/正文、22 项消息、三个 intent、四个坐标见证和 31 项时序。原文标 authorityClaim=NONE、businessExecution=NOT_RUN、actualOwnerAdoption=UNPROVEN；内部假设的 DESIGN_ADOPTED／READY 不能成为实际启用凭据。[原件 L1–20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:1)

四个物理成员分属两个 occurrence，每 occurrence 为 2 EA。S/M/C/L 分别从 100/40/0/20 起连续递增，两个区段各长 2；S→C 偏移 −100，C→L 偏移 +20。QA 单位只有 id/version，MES/NCR 单位保留 MD-UOM Owner，不能因同名 EA 省略转换授权。返工计量为每个冻结成员在获授权操作中计一次，firstEntryMeter=false。[物理映射 L3717–3994](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:3717)、[转换 L7434–7611](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:7434)

两 OPERATION_ENDED 事件在 06:05:01、06:06:01 提交，MES-WO 分别下一秒采用。closure 输入 binding revision 1／registry 5，验证输出 binding revision 2／registry 6；root revision 7、control epoch 4、evidence version 11 各自独立。G1 保存事件原文/原提交/采用记录、stream slots、物理 occurrence、closure、current validation 和完整 registry cut。contentDigest、rawSha256、coverage coreDigest 不同域，不能互代。[事件/closure L4215–4815](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:4215)、[G1 L5169–6030](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:5169)

06:06:23 完成绑定条件性同事务提交 result、MES guard、Use，NCR head 6→7。06:06:30 bridge 绑定 G1、EXEC lineage、NCR target、source mapping 和成员映射；06:07:00 冻结 TaskBasis 后才产生四个 INITIAL observation，每项都有独立 slot/member、原始记录、review、effective result。本例 value=null、code=CONFORMING，全数复检 measuredRanges=C[0,4)，inferred/uncovered 为空。[完成绑定 L6729–7326](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:6729)、[Task/观测/结果 L8998–10245](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:8998)

Decision 在 06:11:01 提交并关闭 family obligation；G2 捕获/发布/读取分别为 06:11:02/03/04。06:12:00 两个 Owner guard 和 QAP current check 核各自 current cut，06:12:01 复检绑定产生 NCR result、2 guard、1 新 Use、2 segment、4 slot 行，head 7→8；物理执行、质量批准、库存、财务写集均为 0。[G2 与 guards L11227–13792](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:11227)、[写集 L25828–25865](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:25828)

G2 为 head 7→8 连续性重新发布，保同一个专业 Decision，列出真实差量 headVersion/phase/previousHeadRef/reinspectionResultRef。whole NCR close 使用完整 obligation/member 集、已绑定两个 result、QA+MES guards 和 composite Use，在 06:13:02 条件性提交 head 9，不能以单 work 通过代替全部关闭。[刷新与关闭 L13793–16602](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:13793)

过期 G1 在 06:16:05 返回 PENDING，reasonRef 指已有 TTL task，GET 不创建 task 或领域写入。publisher 06:17:00 捕获、06:17:01 发布 v2、06:17:02 被 current-read 读取；old exact-read 仍返回 v1 原字节并标 SUPERSEDED。TTL 刷新不改变 MES current vector/NCR head；UseLookup 的 NOT_OBSERVED 仍 canInferNoEffect=false。[TTL 与读取 L17032–20730](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/98/984c83bf53604b59__CROSS-OWNER-EXAMPLE.json:17032)

## 替代值案例的用途

另已读 29 个替代原件、34 cases。10000000000000 是合法字符串版本；0、0.5、9223372036854775808 不合法。连续长度单位另声明量子 0.00000001；0.5 已知量、0.25 未决上界、0.125 未覆盖量是 complete=false 诊断，不能成为 FINISHED。[原件 L8–384](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/23ddd8b3c7ab9151__STATIC-DESIGN-CORRECTION-EXAMPLES.json:8)、[版本分支 L3614–3956](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/23ddd8b3c7ab9151__STATIC-DESIGN-CORRECTION-EXAMPLES.json:3614)

四个 numeric observation 以 value=1.0/code=null 接新 review/effective result，是独立替代世界，仍需数值方法/判据授权，不产生新 Decision/Use。value/code both 或 neither 非法；inferred/uncovered 是坐标范围，不是 Rule EvidenceRef，形状合法不自动准用。[numeric/range L3957–4412](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/23ddd8b3c7ab9151__STATIC-DESIGN-CORRECTION-EXAMPLES.json:3957)

UoW 阶段为 DRAFT/UNPROVEN（空 adoption）、DESIGN_ADOPTED/UNPROVEN、DESIGN_ADOPTED/READY（有准确 runtime attestation）、WITHDRAWN/SUSPENDED（有 predecessor）。draft-ready、adopted 无 adoption、ready 无 attestation、withdrawn 无 predecessor 均非法。本次没有执行 34 cases。[阶段 L1119–3612](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/23ddd8b3c7ab9151__STATIC-DESIGN-CORRECTION-EXAMPLES.json:1119)、[分支 L4413–6761](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/23/23ddd8b3c7ab9151__STATIC-DESIGN-CORRECTION-EXAMPLES.json:4413)

采用可逆的全部字段基准/递归差量阅读，只复用已确认完全相等子树；摘要/UUID 以可回查别名展示，base64 解码后读 JSON。四份普通数据的 1033 次解码、原文字节摘要及表示还原核对单独记录，它们与已完成的 CP4/Main 业务字段语义阅读分别记账，不等于 Schema 校验或业务执行。


## CP4_R1 普通世界：三次 Consumer 提交的具体行

本节依据 [design/references/consumer-cp4-r1/NORMAL-DESIGN-DATA.json](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/24/24a01f470ed9009e__NORMAL-DESIGN-DATA.json:1) 的完整 255 个 catalog 对象、37 个 message 和三份 consumerRowImages。其世界明确是 ISOLATED_NEW_STATIC_DESIGN_CASE；authority/runtime 记录均为假设，runtime 与 fixtureExecution 为 NOT_RUN（L39986–40060）。原 Owner 专业输入按原身份引用；修改了依赖的对象重建字节和摘要，不能当作修改已接受 Owner 原件或实际注册。

Legacy 身份转换保留组织、站点、旧 head 行键及毫秒精度提交时间的原字节；EnvironmentBinding、PortionIdentityApproval、UnitAssignment、CycleAssignment、ScopeAssignment 分别承担转换授权。旧 PORTION-1 与两字段 EA 不可直接补成新环境 UUID、portion UUID 或 MD-UOM 三字段单位。ConsumerTargetCurrentCheck 在提交锁内复核原件、六类锚点及各项 assignment，预览中的 expected head/etag 仅作调用条件（L7622–10680、L22203–22933）。

三次操作分别产生 head 7、8、9 与 globalRv 1008、1009、1010。完成绑定在 06:06:23 写 CompletionTerminal、两条 CompletionOccurrenceLink 和目标 successor；两次 MES occurrence 各 2 EA，范围分别为 [40,42)、[42,44)。复检绑定在 06:12:02 写 ReinspectionBinding、两条 Segment、四条 Slot；source [100,104)、canonical [0,4)、NCR local [20,24) 通过已有双向映射对应。关闭在 06:13:02 写 CompleteClosureBasis、ClosureReceipt、逐目标 ClosureReceiptItem，UseKind 为 COMPOSITE（L21093–21666、L29163–29522、L32441–33040、L36791–36927）。

每个写集都包含 TerminalSlot（原请求/业务体/主体摘要及 applied/replay 原字节）、ConsumerResult、Head、Use/UseLink、结果与旧 head 行键关联、Owner guard、TargetCurrentCheck、全局提交行、旧 NCR head 版本、审计、outbox、RecoveryCheckpoint 和 FactCommitMap。复检与关闭另含 IPQC/QAP 专业 current check。旧 head 的 CAS 与 Consumer expectedHeadRef 必须同事务成功，任一失败回滚全部新增行；完成绑定还保存六条 OwnerObjectAnchor 与两条 OwnerBusinessObjectAnchor，复检/关闭不重新制造这些身份锚点（L28620–39985）。

恢复检查点分别保存 132、213、220 个原件引用，规则为当前隔离 catalog 中完整传递 exact-ref 图；不能只留下顶层结果摘要。FactCommitMap 给每种事实的行定位、head、globalRv 和提交时间，三写集分别有 23、21、17 行。历史详情返回已提交结果原字节，同时另给当前资格；REPLAY 保持结果、Use、head、时间一致，仅 kind/replayed 改变。恢复查询携带原 requestDigest 和 principalEnvelopeDigest，不重新发明业务意图（L22934–23436、L28312–28618、L29911–31346、L34195–36062、L38144–39936）。

Discovery 和详情保持只读。复检索引返回 UNIQUE、complete、requiresOwnerCurrentRead 及完整 ownerCurrentCutRefSet；得到唯一 candidate 仍须 Owner current read 和最终锁内复核。目标 head 从 7 到 8 后，publisher 以新的 G2 proof 记录连续性：committedAt、headVersion、ownerCutDigest、phase、previousHeadRef、reinspectionResultRefs 六项变化均明列，专业质量决定不因 Consumer 绑定而重新批准（L18045–19213、L23437–26891、L27750–28311）。
