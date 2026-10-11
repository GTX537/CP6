# PUR-PO-01 采购订单与履约

## 1. 目的、操作者与业务责任

采购员承接已授权来源和冻结商业内容，形成可审批、发出、接受、确认的真实采购订单；随后按具体交期／来源份额处理变更、取消与独立关闭。采购审核人、供应商凭证核验者、接收守卫服务和各事实生产者分别操作。这里的 PO 是商业采购订单，PLAN-PO 是计划建议及真实转单，两者不能混用。 [原文 L7](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:7>)、[原文 L32](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:32>)、[原文 L33](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:33>)、[原文 L34](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:34>)、[原文 L35](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:35>)、[原文 L36](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:36>)、[原文 L37](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:37>)

| 原SPEC | 本稿交付结果 | 核心章节 | 出处 |
| --- | --- | --- | --- |
| SPEC-PUR-PO-01-01 订单建立 | RFQ组受理、获准非RFQ来源准备、原商业快照、逐行/逐来源映射、原子创建及原结果查询 | §4～5、§11～14 | [原文 L32](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:32>) |
| SPEC-PUR-PO-01-02 审批/确认 | PO版本审批、审批与应用、发出/供应商确认/商业确认分轴 | §6 | [原文 L33](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:33>) |
| SPEC-PUR-PO-01-03 供应日期 | 要求日、原相对/固定条款、分交期、确认评估、Planning供给日期 | §6～7、§10 | [原文 L34](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:34>) |
| SPEC-PUR-PO-01-04 变更取消 | 完整变更差分、未履约范围取消、截止/竞争、源安全处置、独立关闭及恢复 | §7～8、§13～14 | [原文 L35](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:35>) |
| SPEC-PUR-PO-01-05 收货/验收/开票累计 | 源事实分量、版本、更正、退货与信用冲销区分、对账/重算 | §9～10 | [原文 L36](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:36>) |
| SPEC-PUR-PO-01-06 材料替代停供反馈与ECO影响处置回执 | 供应反馈、技术发布接收/就绪/采用、动态影响评估、有权执行和逐项回执 | §10.3～10.5 | [原文 L37](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:37>) |

| 事实 | 谁可以改变 | PO消费与边界 | 出处 |
| --- | --- | --- | --- |
| PR/采购来源授权、份额、Claim、返额/再授权 | PUR-PR或明确授权来源Owner | 保存原引用和使用/处置回执；不能通过PO.Cancel直接给PR余额加数 | [原文 L43](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:43>) |
| RFQ报价、选择、当前作用范围及冻结组 | PUR-RFQ | 原样承接v1.1.1合同；不重新选最新价、不更改RFQ D01～D07 | [原文 L44](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:44>) |
| PO内容、商业版本、订单确认、未履约取消 | 本Target的Procurement Owner | 本稿唯一可写商业订单主账 | [原文 L45](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:45>) |
| 实收与库存移动 | PUR-GR商业收货；WMS实际物理收货/退货 | 一条实收采用指定PUR-GR生产者，必须带真实WMS证据；WMS旁路观察不再加一次PO实收 | [原文 L46](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:46>) |
| 检验/让步/免检、商业验收分量 | Quality判定；PUR-GR验收适配 | PO只有对应分量投影；免检也须策略凭证，无结果不PASS | [原文 L47](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:47>) |
| 发票匹配/容差 | PUR-MATCH | 只读匹配结果；不把“匹配受理/拟开票”记成已过账开票 | [原文 L48](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:48>) |
| AP/账套/实际票据过账及信用冲销 | Finance AP | 按账套/票据/行/分配身份消费POSTED事实；采购关闭不关闭会计期间 | [原文 L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:49>) |
| 需求与预计供给净算 | Planning | PO提供一份有谱系、版本、剩余和资格的供给投影输入，不执行MRP/不改Demand | [原文 L50](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:50>) |
| 技术内容/用途发布/ECO批准 | PLM/工程 | PO评估并执行自己范围，发布通知不能改旧PO/库存/工单 | [原文 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:51>) |

STANDARD 与 SUBCONTRACT 只定义订单类型；本册不能替委外发料、加工与在外账。RFQ D01–D07 限原 RFQ 来源，不扩大为所有非 RFQ 采购的全球政策。正式采购不接受只有 manual 字样而没有来源授权的裸建单。 [原文 L53](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:53>)、[原文 L57](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:57>)、[原文 L744](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:744>)、[原文 L748](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:748>)

| 轴 | 状态/含义 | 不得推论 | 出处 |
| --- | --- | --- | --- |
| 创建执行 | NOT_OBSERVED / PENDING / SUCCEEDED / REJECTED / CANCELLED_NO_EFFECT / UNKNOWN | 创建成功不是审批通过或供应商接受 | [原文 L67](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:67>) |
| PO文书 | CREATED / RELEASED；每修订DRAFT / SUBMITTED / ACTIVE / REJECTED / SUPERSEDED / ABANDONED | OA Approved不是已经发出；新草稿不替代旧ACTIVE | [原文 L68](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:68>) |
| 供应商与确认 | 回应UNANSWERED / ACCEPTED / COUNTEROFFER / REJECTED / WITHDRAWN；本域确认NOT_CONFIRMED / PARTIAL / CONFIRMED / DISPUTED | 已发/SENT不等供应商已读；供应商反报价不自动变更订单 | [原文 L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:69>) |
| 履约 | 各行实收、验收、开票、实物退回、票据信用分别含数值/单位/来源水位/完整性 | 三量不可相加；未知不当0；退货和红票不证明同一物理动作 | [原文 L70](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:70>) |
| 处置/结案 | 变更/取消独立请求与结果；收货范围CLOSED、采购执行CLOSED、Source/Quality/Finance待办分别显示 | 一个Closed不证明所有在途、费用和源授权已结清 | [原文 L71](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:71>) |

## 2. 准确版本、已接受选择与评审效力

| 原件 | 准确 SHA-256 | 有效范围 | 入口 |
| --- | --- | --- | --- |
| v1.1正文 | 15edbc1dfe25be7ff9450c0c15de39a16e3141801194824bda54a35970289216 | 536214 bytes／10839行；唯一完整当前Spec | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1>) |
| CURRENT | 08f889beb3fa044ff9cf6fd35b4e97b78bcc61f22b38c51c54993b51c8700a17 | USER_ACCEPTED_DEVELOPER_SPEC；ImplementationAuthorized=false | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/08f889beb3fa044f__CURRENT.json:1>) |
| UA正式接受 | 20338b1d82230ce05f459ae416cfef780760d3e0f2dd73742448e3096689c38b | UA-20260930-B-PUR-PO-MD01 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:1>) |
| MD02限定复审 | a1f355c91ac52c94dedab2e56f4d5438aff066a3c6314c0ff8b63c0e3f38cfe3 | F01–F04实质修订已验证，非运行报告 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:1>) |

用户“确认，继续”已接受准确 v1.1 原字节及四项选择：兼容 ATOMIC_MANIFEST 恰一 PO 头；真实 Supplier 接受事件作为 PO_CONFIRMED 锚点；单 Schedule 必须完整接受，部分接受先反报价再正式拆分；Source/PO/GR 高风险新效果采用 Main 同库 Owner 参与最终事务，无等价跨库证明就阻相应新效果。正文 DRAFT、四项待接受与阶段60均是编制时状态，不能当成当前未决产品问题。 [原文 L8](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:8>)、[原文 L11](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:11>)、[原文 L12](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:12>)、[原文 L13](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:13>)、[原文 L14](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:14>)、[原文 L16](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:16>)、[原文 L18](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:18>)

复审关闭 F01 当前供应商回复链、F02 Permit 当前守卫／在途、F03 持久 ChangeWork、F04 CancellationApplication 与动态证据四项。原113 JSON解析、92＋32验收完整性属于作者／复审静态证据；124项业务设计验收全部 NOT_RUN。真实Owner采用、政策、数据库并发与故障仍是实施门，不用“文档接受”推导已部署。 [原文 L24](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:24>)、[原文 L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:41>)、[原文 L65](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:65>)、[原文 L86](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:86>)、[原文 L114](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:114>)、[原文 L137](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:137>)、[原文 L139](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a1/a1f355c91ac52c94__SR-20260930-B-PUR-PO-MD02_限定复审与接受建议.md:139>)

## 3. 实体字段、身份与持久模型

金额与数量为十进制字符串：Qty(19,6)、Price(19,8)、Amount(19,4)、Rate(28,12)，不经 JS Number；技术 rowVersion 是8字节 base64，业务修订／序列为准确正整数字符串，各自作用域比较。公开 JSON 2MiB、内部8MiB、每次200行／交期、每行50源分量、20费用／档位；未知成员、重复成员、精度超限拒绝。原累计 Qty(21,8) 无损转不进19,6时保历史只读或单独兼容评审，不舍入存量。 [原文 L142](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:142>)、[原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:146>)、[原文 L1346](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1346>)、[原文 L1685](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1685>)

| 字段 | 页面/来源、必填/默认 | 持久规则 | 出处 |
| --- | --- | --- | --- |
| supplierKey/siteKey/buyerKey | 授权选择；Supplier/Site不默取任意第一项，Buyer仅可默认当前有权本人 | 一PO一Supplier/Site；已创建后不能普通PATCH改交易对手/主站点，需准确后继授权 | [原文 L150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:150>) |
| orderKind | STANDARD/SUBCONTRACT，准备时必填 | 不是让标准PO自动执行外注物料流程 | [原文 L151](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:151>) |
| orderDate | 用户有权选择的业务日期，RFQ自动创建由PO时间服务给当地日期并留来源 | 不用它改写QUOTE期限或PO_CONFIRMED时间；FX日期独立由金额政策 | [原文 L152](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:152>) |
| preparation.title / remarks | title 1～200，可null草稿；remarks≤2000，默认null | 正式源条款不可被备注覆盖 | [原文 L153](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:153>) |
| quantity/uomCode | Qty>0、单位1～16，手工准备可null待补 | 数量含义为对应行交易单位；源分量另用sourceUom；强制有证单位换算 | [原文 L154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:154>) |
| requiredDelivery / supplierDelivery | 上游原样/有据新商业文书；页面分栏 | 原快照与后继评估分表，PO默认日期不能覆盖 | [原文 L155](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:155>) |
| schedules | 分交期数量/要求日/承诺及对应Source portions | 初建一行一个交期；拆分或改期由变更草稿，最终合计守恒；已履约归属不搬家 | [原文 L156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:156>) |
| reason | 状态/确认/变更/取消必填1～1000，非空 | 不是审批/技术豁免；原证据Ref单列 | [原文 L157](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:157>) |
| confirmedAt | 来自真实Supplier接受/有据不需确认的业务凭证 | 用户不能填“今天”补锚点；RecordedAt/AppliedAt另存 | [原文 L158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:158>) |
| received/accepted/invoiced | 只读，按§9事实映射 | 不能PATCH累计；未知null、有效已核零为"0.000000" | [原文 L159](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:159>) |
| originalNet/tax/freight等 | 原币分项＋准确政策证据 | 多币费用不合成无币种单数；比较值不进PO货款列 | [原文 L160](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:160>) |

Order.creationKey 是跨重试真实业务身份，PreparationId 不是再采购许可。OrderRevision 保存不可变内容与前驱；Schedule/SourcePortion 保稳定子身份，RevisionSchedule 保存当版文书数量；执行量由 QuantityEntry／PortionTransfer 推导。SupplierResponseHead、FactCurrent、DependencyHead 各有独立当前头，不可按最新时间统一。 [原文 L166](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:166>)、[原文 L203](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:203>)、[原文 L1364](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1364>)、[原文 L1368](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1368>)、[原文 L1369](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1369>)、[原文 L1396](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1396>)、[原文 L1429](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1429>)、[原文 L1472](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1472>)

### PO全部规范 DTO：文书、审批、回复、工作、事实与工程

[原文 L166](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:166>)

```ts
type Id = string;
type Key = string;
type Seq = string;
type Version = string;
type Qty = string;
type UnitPrice = string;
type Amount = string;
type Rate = string;
type Fraction = string;
type Instant = string;
type DateText = string;
type Availability = 'AVAILABLE'|'NOT_CONFIGURED'|'NOT_AUTHORIZED'|'TEMPORARILY_UNAVAILABLE';
type EvidenceMode = 'REAL'|'DEMO';
interface PoFieldError { field:string; code:string; message:string; }
interface PoGate { code:string; allowed:boolean; missing:string[]; evidenceRefs:Ref[]; }
interface PoResource { kind:'ORDER'|'REVISION'|'PREPARATION'|'APPROVAL'|'ACKNOWLEDGEMENT'|'CONFIRMATION'|'CHANGE'|'CANCELLATION'|'CLOSURE'|'FEEDBACK'|'IMPACT'|'EXPORT'|'DISPATCH'|'DOWNLOAD'; id:Id; businessRevision:Seq|null; version:Version|null; }
interface PoCommandResult { commandId:Id; outcome:'SUCCEEDED'|'ACCEPTED'|'REJECTED'|'CANCELLED_NO_EFFECT'; effect:string; replayed:boolean; resource:PoResource|null; errorCode:string|null; recordedAt:Instant; resultUrl:string|null; }
interface PoProblem { type:string; title:string; status:number; code:string; detail:string; commandId:Id|null; traceId:string; errors:PoFieldError[]; retry:'NONE'|'SAME_KEY'|'QUERY_ORIGINAL'|'NEW_INTENT_AFTER_REVIEW'; currentVersion:Version|null; }
interface PoMutation { expectedVersion:Version; }
interface PoReasonMutation extends PoMutation { reason:string; }
interface PoPageInput { page:number; pageSize:20|50|100; }
interface PoPage<T> { items:T[]; page:number; pageSize:number; total:number; asOf:Instant; }
interface PoSearch extends PoPageInput { supplierKey:Key|null; siteKey:Key|null; buyerKey:Key|null; poNoPrefix:string|null; itemKey:Key|null; sourceKey:Key|null; lifecycle:'OPEN'|'CANCELLED'|'CLOSED'|'ALL'; confirmation:'ANY'|'NOT_CONFIRMED'|'PARTIAL'|'CONFIRMED'|'DISPUTED'; exceptionOnly:boolean; dateFrom:DateText|null; dateTo:DateText|null; sort:'updatedDesc'|'numberAsc'|'promisedDateAsc'; }
interface PoListRow { id:Id; poNo:string; supplierKey:Key; supplierName:string; siteKey:Key; buyerKey:Key; orderKind:'STANDARD'|'SUBCONTRACT'; lifecycle:'OPEN'|'CANCELLED'|'CLOSED'; revision:Seq; documentState:'CREATED'|'RELEASED'; confirmation:'NOT_CONFIRMED'|'PARTIAL'|'CONFIRMED'|'DISPUTED'; exceptionCodes:string[]; nearestSupplyDate:DateText|null; updatedAt:Instant; }
interface PoOptions { sites:{key:Key;label:string}[]; buyers:{key:Key;label:string}[]; capabilities:string[]; policies:{kind:string;availability:Availability;ref:Ref|null}[]; evidenceMode:EvidenceMode; }
interface PoSourcePortion { id:Id; parentPortionId:Id|null; transferredInSourceQty:Qty; transferredOutSourceQty:Qty; source:GrantShare; poLineId:Id; scheduleId:Id; originalSourceQty:Qty; increasedSourceQty:Qty; cancelledSourceQty:Qty; reducedSourceQty:Qty; sourceUom:string; executionEffectId:Key; dispositionState:'NONE'|'PENDING'|'CONFIRMED'|'CONFLICT'; lineageRef:Ref|null; }
interface PoSchedule { id:Id; poLineId:Id; scheduleNo:number; quantity:Qty; effectiveQuantity:Qty; uomCode:string; requiredDelivery:RequiredDelivery; deliverySnapshotHash:string; supplierDelivery:DeliveryTerms; originalDeliveryResolution:DeliveryResolution; confirmationId:Id|null; supplyDate:DateText|null; commitment:'UNCONFIRMED'|'CONFIRMED'|'DISPUTED'|'STOPPED'; sourcePortionIds:Id[]; }
interface PoLine { id:Id; lineNo:number; manifestRowId:Id; itemKey:Key; technicalBasis:TechnicalBasis; quantity:Qty; uomCode:string; originalCommercial:TargetLine|null; revisionCommercial:TargetLine|null; schedules:PoSchedule[]; }
interface PoRevisionView { id:Id; poId:Id; businessRevision:Seq; version:Version; predecessorRevisionId:Id|null; state:'DRAFT'|'SUBMITTED'|'ACTIVE'|'REJECTED'|'SUPERSEDED'|'ABANDONED'; contentHash:string; supplierKey:Key; siteKey:Key; buyerKey:Key; orderDate:DateText; lines:PoLine[]; commercialPayload:TargetCommercialPayload|null; remarks:string|null; redactedFields:string[]; }
interface PoOrderView { id:Id; poNo:string; version:Version; orderKind:'STANDARD'|'SUBCONTRACT'; originKind:'RFQ'|'AUTHORIZED_PURCHASE'; creationKey:Id; initialRevisionId:Id; activeRevisionId:Id|null; viewedRevision:PoRevisionView; lifecycle:'OPEN'|'CANCELLED'|'CLOSED'; confirmation:'NOT_CONFIRMED'|'PARTIAL'|'CONFIRMED'|'DISPUTED'; sourcePortions:PoSourcePortion[]; gates:PoGate[]; redactedFields:string[]; sourceDisposition:'NONE'|'PENDING'|'CONFIRMED'|'CONFLICT'; receivingClosure:'OPEN'|'CLOSED'; procurementClosure:'OPEN'|'CLOSED'; evidenceMode:EvidenceMode; }
interface PoPreparationLine { draftLineId:Id|null; itemKey:Key|null; quantity:Qty|null; uomCode:string|null; technicalBasis:TechnicalBasis|null; requiredDelivery:RequiredDelivery|null; sourceAuthorizationRef:Ref|null; commercialEvidenceRef:Ref|null; proposedTerms:CommercialTerms|null; }
interface PoPreparationInput { supplierKey:Key|null; siteKey:Key; buyerKey:Key; orderKind:'STANDARD'|'SUBCONTRACT'; orderDate:DateText; title:string|null; remarks:string|null; lines:PoPreparationLine[]; }
type PoPreparationPatch = PoMutation & Partial<PoPreparationInput>;
interface PoPreparationView { id:Id; version:Version; state:'DRAFT'|'VALIDATED'|'CREATED'|'BLOCKED'; input:PoPreparationInput; validatedSnapshotHash:string|null; creationKey:Id|null; authorizedBundleRef:Ref|null; gates:PoGate[]; poId:Id|null; redactedFields:string[]; }
interface PoPreparationCreate extends PoMutation { expectedValidatedSnapshotHash:string; }
interface PoAuthorizedBundle { ref:Ref; authorizationKey:Key; authorizationVersion:Key; supplierKey:Key; siteKey:Key; buyerKey:Key; orderKind:'STANDARD'|'SUBCONTRACT'; allowedLines:{draftLineId:Id;externalLineKey:Key;itemKey:Key;quantity:Qty;uomCode:string;technicalBasis:TechnicalBasis;sourceShares:GrantShare[]}[]; commercialPayload:TargetCommercialPayload; scopeAtomKeys:Key[]; controlUseRef:Ref; contractRef:Ref; evidenceMode:EvidenceMode; }
```

[原文 L209](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:209>)

```ts
interface PoApprovalSubmit { subjectKind:'PO_RELEASE'|'PO_CHANGE'|'PO_CANCEL'|'PO_CLOSE'; subjectId:Id; expectedVersion:Version; policyRef:Ref; reason:string; replacesSubmissionId:Id|null; }
interface PoApprovalView { id:Id; poId:Id; subjectKind:PoApprovalSubmit['subjectKind']; subjectId:Id; subjectBusinessRevision:Seq|null; subjectContentHash:string; submittedRowVersion:Version; policyRef:Ref; decision:'PENDING'|'APPROVED'|'REJECTED'|'NOT_REQUIRED'; requirement:'REQUIRED'|'NOT_REQUIRED_WITH_POLICY'; instanceKey:Key|null; resultRef:Ref|null; application:'NOT_APPLIED'|'APPLIED'|'CONFLICT'|'BLOCKED'; }
interface PoRelease { expectedOrderVersion:Version; expectedRevisionVersion:Version; approvalSubmissionId:Id; recipientKey:Key; disclosureEvidenceRef:Ref; }
interface PoDispatchView { id:Id; poId:Id; revisionId:Id; contentHash:string; recipientKey:Key; state:'QUEUED'|'SENT'|'UNKNOWN'|'REJECTED'; evidenceRef:Ref|null; version:Version; }
interface PoAckLine { scheduleId:Id; quantity:Qty; uomCode:string; response:'ACCEPTED'|'COUNTEROFFER'|'REJECTED'|'WITHDRAWN'; proposedDelivery:DeliveryTerms|null; reason:string|null; }
interface PoAckInput { revisionId:Id; releasedContentHash:string; supplierAcknowledgementRef:Ref; lines:PoAckLine[]; notes:string|null; }
interface PoAckView extends PoAckInput { id:Id; version:Version; verified:boolean; acknowledgedAt:Instant|null; recordedAt:Instant; exceptionCodes:string[]; responseHeads:PoSupplierResponseHead[]; }
interface PoConfirm { expectedOrderVersion:Version; revisionId:Id; acknowledgementId:Id; expectedAcknowledgementVersion:Version; scheduleIds:Id[]; expectedResponseHeads:PoResponseHeadExpectation[]; confirmationPolicyRef:Ref; }
interface PoDeliveryAssessment { scheduleId:Id; originalSnapshotHash:string; confirmationRef:Ref; confirmedAt:Instant; anchorLocalDate:DateText; resolvedDate:DateText|null; calendarSnapshotRef:Ref|null; calendarRuleHash:string|null; requiredDateCheck:DeliveryResolution['requiredDateCheck']; combinedCheck:DeliveryResolution['combinedCheck']; result:'MEETS'|'LATE_REQUESTED'|'BLOCKED'; reasons:string[]; }
interface PoConfirmationView { id:Id; poId:Id; revisionId:Id; acknowledgedEvidenceRef:Ref; confirmedAt:Instant; appliedAt:Instant; scheduleIds:Id[]; assessments:PoDeliveryAssessment[]; state:'APPLIED'|'BLOCKED'; responseHeadBindings:PoResponseHeadExpectation[]; }
```

[原文 L222](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:222>)

```ts
interface PoSupplierResponseStatement {
  scheduleId:Id; responseRef:Ref; predecessorResponseRef:Ref|null;
  streamEpoch:Key; responseSequence:Seq; response:PoAckLine['response'];
  quantity:Qty; uomCode:string; semanticHash:string;
  authorityRef:Ref; effectiveAt:Instant;
}
interface PoSupplierResponseHead {
  id:Id; poId:Id; supplierKey:Key; revisionId:Id; scheduleId:Id;
  streamEpoch:Key; headRevision:Seq; version:Version;
  responseRef:Ref|null; acknowledgementId:Id|null; responseSequence:Seq|null;
  response:PoAckLine['response']|null; semanticHash:string|null;
  state:'EMPTY'|'CURRENT'|'PENDING_PREDECESSOR'|'CONFLICT';
  authorityRef:Ref|null; pendingResponseRefs:Ref[];
}
interface PoResponseHeadExpectation {
  scheduleId:Id; headId:Id; expectedHeadRevision:Seq; responseRef:Ref;
}
interface PoSupplierHeadQuery {
  poId:Id; supplierKey:Key; revisionId:Id; releasedContentHash:string;
  scheduleIds:Id[];
}
interface PoSupplierCurrentDeclaration {
  poId:Id; supplierKey:Key; revisionId:Id; releasedContentHash:string;
  responses:PoSupplierResponseStatement[]; declarationRef:Ref;
  asOf:Instant; validThrough:Instant; completeForScheduleIds:Id[];
}
```

[原文 L257](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:257>)

```ts
interface PoScheduleChange { scheduleId:Id; replacementSchedules:{newScheduleKey:Key;quantity:Qty;uomCode:string;requiredDelivery:RequiredDelivery;supplierDelivery:DeliveryTerms;sourcePortionAllocations:{sourcePortionId:Id;sourceQty:Qty;sourceUom:string}[]}[]; }
interface PoCommercialChange { poLineId:Id; affectedPortions:PoCancelSlice[]; futureQuantity:Qty; commercialEvidenceRef:Ref; replacementTerms:CommercialTerms; additionalAuthorizationRefs:Ref[]; technicalBasis:TechnicalBasis; }
interface PoChangeInput { baseRevisionId:Id; expectedOrderVersion:Version; reason:string; scheduleChanges:PoScheduleChange[]; commercialChanges:PoCommercialChange[]; additionalLines:PoPreparationLine[]; engineeringExecutionRef:Ref|null; }
interface PoChangeView { id:Id; poId:Id; version:Version; proposalRevision:Seq; state:'DRAFT'|'SUBMITTED'|'APPLY_REQUESTED'|'DRAINING'|'READY'|'APPLIED'|'REJECTED'|'BLOCKED'|'ABANDON_REQUESTED'|'ABANDONED'; input:PoChangeInput; contentHash:string; newRevisionId:Id|null; workId:Id|null; applicationRef:Ref|null; gates:PoGate[]; }
interface PoApplyChange extends PoMutation { approvalSubmissionId:Id; expectedOrderVersion:Version; expectedAssessmentRef:Ref; supplierAgreementRef:Ref|null; }
interface PoCancelSlice { poLineId:Id; scheduleId:Id; quantity:Qty; uomCode:string; portions:{sourcePortionId:Id;quantity:Qty;uomCode:string}[]; }
interface PoCancelInput { expectedOrderVersion:Version; baseRevisionId:Id; slices:PoCancelSlice[]; reason:string; sourceImpactRef:Ref|null; supplierAgreementRef:Ref|null; }
interface PoCancellationView { id:Id; poId:Id; version:Version; proposalRevision:Seq; contentHash:string; state:'DRAFT'|'REQUESTED'|'DRAINING'|'READY'|'APPLIED'|'REJECTED'|'UNKNOWN'; input:PoCancelInput; assessmentRef:Ref|null; approvalSubmissionId:Id|null; cutoffRef:Ref|null; sourceDisposition:'NOT_REQUESTED'|'PENDING'|'CONFIRMED'|'CONFLICT'; appliedCancellationRef:Ref|null; workId:Id|null; evidenceSetRef:Ref|null; errorCode:string|null; gates:PoGate[]; }
interface PoCancellationApply extends PoMutation { approvalSubmissionId:Id; expectedAssessmentRef:Ref; }
interface PoScopeAssessment { ref:Ref; poId:Id; baseRevisionId:Id; scopeHash:string; contentHash:string; capturedAt:Instant; orderVersion:Version; fulfillmentRevision:Seq; receivingGuardRevision:Seq; rows:{poLineId:Id;scheduleId:Id;sourcePortionId:Id;uomCode:string;effectiveQty:Qty;protectedFulfilledQty:Qty|null;inflightQty:Qty|null;otherChangeHeldQty:Qty|null;maxCancellableQty:Qty|null;requestedQty:Qty}[]; allowed:boolean; gates:PoGate[]; }
interface PoCloseInput { expectedOrderVersion:Version; kind:'RECEIVING'|'PROCUREMENT'; reason:string; policyRef:Ref; evidenceRefs:Ref[]; }
interface PoCloseView { id:Id; poId:Id; version:Version; proposalRevision:Seq; kind:'RECEIVING'|'PROCUREMENT'; contentHash:string; status:'DRAFT'|'BLOCKED'|'CLOSED'; missing:string[]; evidenceRefs:Ref[]; receivingBarrierRef:Ref|null; }
interface PoApplyClose extends PoMutation { approvalSubmissionId:Id; expectedOrderVersion:Version; }
```

[原文 L277](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:277>)

```ts
type PoWorkPhase = 'REQUESTED'|'DRAINING'|'READY'|'UNKNOWN'|'CLEANUP_PENDING'|'SUCCEEDED'|'REJECTED'|'ABANDONED';
type PoDependencyKind = 'SUPPLIER_AGREEMENT'|'FEE_DISPOSITION'|'GR_CUTOFF'|'SOURCE_ELIGIBILITY'|'SOURCE_CLAIM'|'SOURCE_DISPOSITION'|'QUALITY_CLEARANCE'|'AP_CLEARANCE'|'NO_EFFECT';
interface PoWorkResume { expectedWorkVersion:Version; reason:string; }
interface PoWorkHold { id:Id; sourcePortionId:Id; segmentIds:Id[]; quantity:Qty; uomCode:string; state:'HELD'|'CONSUMED'|'RELEASED'; installedGuardRevision:Seq; }
interface PoChangeClaimPlan { executionKey:Id; changeId:Id; workId:Id; grants:GrantShare[]; scopeAtomKeys:Key[]; controlUseRef:Ref; sourceBundleRefs:Ref[]; planHash:string; }
interface PoDependencyRequest {
  id:Id; poId:Id; workKind:'CHANGE'|'CANCELLATION'; workId:Id; subjectId:Id;
  dependencyKind:PoDependencyKind; producerOwner:Key; operationKey:Key;
  subjectContentHash:string; scopeHash:string; proposalRef:Ref; policyRef:Ref;
  subjectSlices:PoCancelSlice[]; referencedInputs:Ref[];
  action:'READ_PROOF'|'PREPARE_CLAIM'|'APPLY_SOURCE_DISPOSITION'|'CLOSE_NO_EFFECT';
  expectedPredecessorRef:Ref|null; claimPlan:PoChangeClaimPlan|null; sourceDispositionRequest:PoSourceDispositionRequest|null; requestHash:string; createdAt:Instant;
}
interface PoOwnerNoEffectProof { operationKey:Key; requestHash:string; fenceRef:Ref; coveredPaths:Key[]; coveredThroughRef:Ref; releasedPreparationRefs:Ref[]; retainedObligationRefs:Ref[]; noFurtherEffect:boolean; }
interface PoOwnerDecisionPayload {
  evidenceRef:Ref; decision:'SATISFIED'|'DENIED'|'WITHDRAWN'|'PENDING'|'UNKNOWN';
  approvedContentHash:string; approvedScopeHash:string;
  applicability:'CURRENT_PROPOSAL_ONLY'; validFrom:Instant; validThrough:Instant|null;
  resultingObligationRefs:Ref[]; requiredNewTerms:boolean;
  noEffectProven:boolean; noEffectProof:PoOwnerNoEffectProof|null; coveredOperationKeys:Key[]; missing:string[];
}
interface PoChangeCutoffRequest {
  changeId:Id; poId:Id; scopeHash:string; slices:PoCancelSlice[];
  expectedGuardRevision:Seq; observedPermitIds:Id[];
}
interface PoChangeCutoffResult {
  changeId:Id; scopeHash:string; result:'COMPLETE_NO_PENDING_EFFECT'|'PENDING'|'CONFLICT';
  proofRef:Ref|null; coveredPermitIds:Id[]; uncoveredPermitIds:Id[];
  producerWatermarks:{producerOwner:Key;streamId:Key;epoch:Key;version:Seq}[];
  fulfillmentDigest:string; asOf:Instant;
}
interface PoDependencyResult {
  id:Id; dependencyRequestId:Id; operationKey:Key; producerOwner:Key;
  resultRef:Ref; resultVersion:Seq; predecessorResultRef:Ref|null;
  subjectContentHash:string; scopeHash:string; semanticHash:string;
  outcome:'SATISFIED'|'PENDING'|'UNKNOWN'|'DENIED'|'WITHDRAWN'|'CONFLICT';
  effectiveAt:Instant; observedAt:Instant; evidenceMode:EvidenceMode;
  decisionPayload:PoOwnerDecisionPayload|null;
  cancellationCutoff:PoCutoffResult|null; changeCutoff:PoChangeCutoffResult|null;
  claimBundle:ClaimBundle|null; sourceDisposition:PoSourceDispositionResult|null;
}
interface PoDependencyView {
  request:PoDependencyRequest; headVersion:Seq; currentResult:PoDependencyResult|null;
  state:'NOT_REQUESTED'|'PENDING'|'SATISFIED'|'UNKNOWN'|'DENIED'|'WITHDRAWN'|'CONFLICT';
  knownResultRefs:Ref[]; errorCode:string|null;
}
interface PoDependencyEvidenceSet {
  ref:Ref; workKind:'CHANGE'|'CANCELLATION'; workId:Id;
  subjectContentHash:string; scopeHash:string;
  members:{dependencyRequestId:Id;headVersion:Seq;resultRef:Ref;semanticHash:string}[];
  capturedGuardRevision:Seq; fulfillmentRevision:Seq; evidenceSetHash:string; capturedAt:Instant;
}
interface PoChangeWorkView {
  id:Id; changeId:Id; poId:Id; version:Version; phase:PoWorkPhase;
  businessOutcome:'NOT_APPLIED'|'APPLIED'|'REJECTED'|'ABANDONED';
  proposalRevision:Seq; subjectContentHash:string; applyRequestHash:string;
  baseRevisionId:Id; scopeHash:string; installedGuardRevision:Seq;
  holdRefs:Ref[]; holds:PoWorkHold[]; watchedPermitIds:Id[]; drainPermitIds:Id[];
  dependencies:PoDependencyView[]; evidenceSetRef:Ref|null;
  leaseEpoch:Seq; nextAttemptAt:Instant|null; attemptCount:number;
  abandonRequested:boolean; finalApplicationFenced:boolean;
  applicationRef:Ref|null; newRevisionId:Id|null;
  errorCode:string|null; nextAction:'QUERY_ORIGINAL'|'RESUME_SAME_WORK'|'WAIT_OWNER'|'NEW_INTENT_AFTER_REVIEW'|'NONE';
}
interface PoCancellationWorkView {
  id:Id; cancellationId:Id; poId:Id; version:Version; phase:PoWorkPhase;
  businessOutcome:'NOT_APPLIED'|'APPLIED'|'REJECTED';
  proposalRevision:Seq; subjectContentHash:string; applyRequestHash:string; scopeHash:string;
  installedGuardRevision:Seq; holds:PoWorkHold[]; watchedPermitIds:Id[]; drainPermitIds:Id[];
  dependencies:PoDependencyView[]; evidenceSetRef:Ref|null;
  leaseEpoch:Seq; nextAttemptAt:Instant|null; attemptCount:number;
  withdrawRequested:boolean; finalApplicationFenced:boolean;
  applicationRef:Ref|null; sourceDisposition:'NOT_REQUESTED'|'PENDING'|'CONFIRMED'|'CONFLICT';
  errorCode:string|null; nextAction:'QUERY_ORIGINAL'|'RESUME_SAME_WORK'|'WAIT_OWNER'|'NEW_INTENT_AFTER_REVIEW'|'NONE';
}
interface PoChangeApplication {
  id:Id; ref:Ref; changeId:Id; workId:Id; poId:Id; proposalRevision:Seq;
  contentHash:string; scopeHash:string; baseRevisionId:Id; newRevisionId:Id;
  transferIds:Id[]; addedSourceUseRefs:Ref[]; approvalApplicationRef:Ref;
  evidenceSetRef:Ref; appliedAt:Instant;
}
interface PoCancellationApplication {
  id:Id; ref:Ref; cancellationId:Id; workId:Id; poId:Id; proposalRevision:Seq;
  contentHash:string; scopeHash:string; baseRevisionId:Id; appliedSlices:PoCancelSlice[];
  cancelledSegments:{sourcePortionId:Id;segmentId:Id;quantity:Qty;uomCode:string}[];
  quantityEntryIds:Id[]; approvalApplicationRef:Ref; evidenceSetRef:Ref;
  cutoffProofRef:Ref; appliedAt:Instant;
}
```

[原文 L386](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:386>)

```ts
type PoFactKind = 'RECEIPT'|'ACCEPTANCE'|'INVOICE'|'RETURN'|'INVOICE_CREDIT';
interface PoFactIdentity { producerOwner:Key; ledgerKey:Key; documentId:Key; documentLineId:Key; allocationKey:Key; }
interface PoFactAllocation { poId:Id; poLineId:Id; scheduleId:Id; sourcePortionId:Id; quantity:Qty; uomCode:string; originalQuantity:Qty; originalUom:string; conversionRef:Ref|null; receiptSliceRef:Ref|null; }
interface PoFulfillmentFact { eventId:Id; kind:PoFactKind; identity:PoFactIdentity; factVersion:Seq; previousVersion:Seq|null; state:'POSTED'|'VOID_WITH_EVIDENCE'; supersedesFactRef:Ref|null; effectiveAt:Instant; observedAt:Instant; evidenceMode:EvidenceMode; allocations:PoFactAllocation[]; relatedReceiptRefs:Ref[]; movementRefs:Ref[]; qualityOrExemptionRefs:Ref[]; apPostingRefs:Ref[]; amountComponents:{amount:Amount;currencyCode:string;role:'NET'|'TAX'|'GROSS'|'FEE'}[]; correctionReason:string|null; correctionAuthorityRef:Ref|null; receivingPermitId:Id|null; }
interface PoFactAck { eventId:Id; factRef:Ref|null; result:'APPLIED'|'DUPLICATE'|'HISTORICAL'|'QUARANTINED'; currentFactVersion:Seq|null; fulfillmentRevision:Seq|null; errorCodes:string[]; }
interface PoMetric { value:Qty|null; uomCode:string; completeness:'COMPLETE'|'INCOMPLETE'|'UNKNOWN'|'CONFLICT'; evidenceRefs:Ref[]; asOf:Instant|null; }
interface PoFulfillmentRow { poLineId:Id; scheduleId:Id; sourcePortionId:Id; uomCode:string; initialOrderedQty:Qty; transferredInQty:Qty; transferredOutQty:Qty; addedQty:Qty; reducedQty:Qty; cancelledQty:Qty; effectiveOrderedQty:Qty; received:PoMetric; accepted:PoMetric; invoicedPosted:PoMetric; physicallyReturned:PoMetric; credited:PoMetric; protectedFulfilledQty:Qty|null; inflightQty:Qty|null; openToReceiveQty:Qty|null; overExecutionQty:Qty|null; discrepancyCodes:string[]; }
interface PoFulfillmentView { poId:Id; revision:Seq; asOf:Instant; rows:PoFulfillmentRow[]; unmatchedFactRefs:Ref[]; result:'KNOWN'|'PARTIAL'|'CONFLICT'; }
interface PoReceivingRequest { receiptOperationKey:Key; poId:Id; expectedOrderVersion:Version; purpose:'PURCHASE_RECEIPT'; rows:{scheduleId:Id;sourcePortionId:Id;quantity:Qty;uomCode:string}[]; policyRef:Ref; }
interface PoReceivingPermit { id:Id; version:Version; receiptOperationKey:Key; poId:Id; guardRevision:Seq; state:PoPermitState; scopeHash:string; rows:{scheduleId:Id;sourcePortionId:Id;quantity:Qty;uomCode:string}[]; expiresAt:Instant|null; receiptEvidenceRefs:Ref[]; evaluatedGuards:PoReceivingGuardVector[]; currentGuards:PoReceivingGuardVector[]; startRef:Ref|null; ownerStartIntentRef:Ref|null; startedAt:Instant|null; everStarted:boolean; blockingControlRefs:Ref[]; noEffectProofRef:Ref|null; settledSegments:PoPermitSegmentResult[]; }
interface PoNoReceiptProof { permitId:Id; receiptOperationKey:Key; scopeHash:string; ownerProofRef:Ref; closedPaths:Key[]; coveredThrough:Key; reason:string; }
interface PoCutoffRequest { cancellationId:Id; poId:Id; scopeHash:string; slices:PoCancelSlice[]; expectedGuardRevision:Seq; }
interface PoCutoffResult { cancellationId:Id; scopeHash:string; result:'COMPLETE_NO_PENDING_EFFECT'|'PENDING'|'CONFLICT'; proofRef:Ref|null; coveredPermitIds:Id[]; uncoveredPermitIds:Id[]; producerWatermarks:{producerOwner:Key;streamId:Key;epoch:Key;version:Seq}[]; fulfillmentDigest:string; asOf:Instant; }
interface PoSourceDispositionRequest { operationKey:Key; poId:Id; cancellationRef:Ref; cutoffProofRef:Ref; portions:{sourcePortionId:Id;grantRef:Ref;originalExecutionEffectId:Key;quantity:Qty;sourceUom:string;generation:Seq}[]; reason:string; }
interface PoSourceDispositionResult { operationKey:Key; state:'APPLIED'|'PENDING'|'REJECTED'|'CONFLICT'; rows:{sourcePortionId:Id;disposedQty:Qty;sourceUom:string;sourceDispositionRef:Ref|null;currentSourceObservationRef:Ref|null}[]; missing:string[]; }
```

[原文 L404](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:404>)

```ts
type PoPermitState = 'HELD_NOT_STARTED'|'IN_FLIGHT'|'USED'|'NO_EFFECT_CLOSED'|'BLOCKED'|'REVOKE_PENDING'|'UNKNOWN'|'SETTLED_PARTIAL';
interface PoReceivingGuardVector {
  scheduleId:Id; sourcePortionId:Id; guardRevision:Seq; scopeControlRef:Ref;
  technicalHoldRefs:Ref[]; cancellationHoldRefs:Ref[]; changeHoldRefs:Ref[];
  closureBarrierRefs:Ref[]; receivingClosed:boolean;
}
interface PoPermitSegmentResult {
  sourcePortionId:Id; segmentId:Id; quantity:Qty; uomCode:string;
  result:'USED'|'NO_EFFECT'; evidenceRef:Ref;
}
interface PoReceivingStart extends PoMutation {
  receiptOperationKey:Key; expectedGuardRevision:Seq;
  expectedGuards:PoReceivingGuardVector[]; scopeHash:string; ownerStartIntentRef:Ref;
}
interface PoPermitRevalidate extends PoMutation { reason:string; }
interface PoPermitControlDecision {
  permitId:Id; previousState:PoPermitState; state:PoPermitState;
  causeRef:Ref; action:'BLOCK_START'|'REQUIRE_RECONCILIATION'|'ALLOW_DRAIN_ONLY'|'CLOSE_NO_EFFECT';
  evaluatedGuards:PoReceivingGuardVector[]; permittedCompletionPaths:Key[];
  evidenceRef:Ref; recordedAt:Instant;
}
```

[原文 L436](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:436>)

```ts
interface PoTechnicalRelease { eventId:Id; releaseRef:Ref; baselineRef:Ref; baselineHash:string; purpose:Key; targetKey:Key; siteKeys:Key[]; effectiveFrom:Instant; effectiveTo:Instant|null; evidenceRefs:Ref[]; }
interface PoFeedbackInput { kind:'MATERIAL_SUBSTITUTION'|'SUPPLY_STOP'|'DELIVERY_RISK'; poLineIds:Id[]; sourceFactRef:Ref; currentBaselineRef:Ref|null; proposedBaselineRef:Ref|null; description:string; evidenceRefs:Ref[]; }
interface PoImpactRequest { eventId:Id; changeRef:Ref; impactSnapshotRef:Ref; oldBaselineRef:Ref; proposedBaselineRef:Ref; scope:{siteKeys:Key[];itemKeys:Key[];poIds:Id[]}; observedAt:Instant; }
interface PoImpactEvaluation { id:Id; poId:Id; version:Version; impactItemRef:Ref; changeRef:Ref; evaluatedRevisionId:Id; fulfillmentRevision:Seq; captureThrough:Key; state:'RECEIVED'|'EVALUATED'|'STALE'|'PENDING_EXECUTION'|'APPLIED'|'REJECTED'|'BLOCKED'; affectedScheduleIds:Id[]; proposedAction:'CONTINUE_OLD'|'HOLD_NEW_USE'|'CHANGE_VIA_PO_REVISION'|'CANCEL_REMAINDER'; missing:string[]; resultRef:Ref|null; }
interface PoImpactAction extends PoMutation { executionKey:Key; impactItemRef:Ref; approvedEcoRef:Ref; proposedAction:PoImpactEvaluation['proposedAction']; quantityScope:PoCancelSlice[]; poChangeId:Id|null; poCancellationId:Id|null; authorityEvidenceRefs:Ref[]; reason:string; }
interface PoEngineeringReceipt { eventId:Id; releaseRef:Ref|null; changeRef:Ref|null; impactItemRef:Ref|null; targetKey:Key; receiptVersion:Seq; stage:'RECEIVED'|'ACCEPTED'|'READY'|'APPLIED'|'REJECTED'|'BLOCKED'; appliedReferences:Ref[]; reasons:string[]; occurredAt:Instant; }
interface PoSupplyProjection { eventId:Id; poId:Id; supplyRevision:Seq; evidenceMode:EvidenceMode; schedules:{scheduleId:Id;poLineId:Id;sourcePortionId:Id;lineageRef:Ref|null;itemKey:Key;technicalBaselineRef:Ref|null;uomCode:string;qualified:boolean;unreceivedQty:Qty|null;rawUnreceivedQty:Qty|null;expectedDate:DateText|null;qualificationRefs:Ref[];coveredReceiptFactRefs:Ref[];restrictions:string[]}[]; causationRefs:Ref[]; asOf:Instant; }
interface PoHistoryRow { id:Id; poId:Id; timelineSeq:Seq; resourceKind:string; resourceId:Id; resourceSeq:Seq; operation:string; actorKey:Key; recordedAt:Instant; occurredAt:Instant; reason:string|null; changes:{field:string;before:string|null;after:string|null;redacted:boolean}[]; evidenceRefs:Ref[]; }
interface PoExportInput { search:PoSearch; includeCommercial:boolean; reason:string; }
interface PoExportView { id:Id; state:'QUEUED'|'READY'|'FAILED'|'REVOKED'|'EXPIRED'; rows:number|null; fileHash:string|null; downloadAllowed:boolean; errorCode:string|null; }
interface PoDownloadInput { poId:Id; evidenceRef:Ref; purpose:'READ'; }
interface PoOperationView { id:Id; kind:string; state:'PENDING'|'SUCCEEDED'|'REJECTED'|'UNKNOWN'|'CANCELLED_NO_EFFECT'; resultRefs:Ref[]; errorCode:string|null; nextAction:'QUERY_ORIGINAL'|'RETRY_SAME'|'NEW_INTENT_AFTER_REVIEW'|'NONE'; version:Version; }
```

### SQL Server 拟议映射与唯一约束

schema pur_po 是待实施物理设计，映射现有 Pur_PurchaseOrder/Line 时必须指定单一 writer，不能并开两套可写订单账。公共 E/M/Hash/J/Ref 的长度、类型及复合Tenant约束如下；事实和终态墓碑不可级联删掉，原键不因SoftDelete重新可用。 [原文 L1346](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1346>)、[原文 L1358](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1358>)

| 公共集合 | 包含的全部列 | 出处 |
| --- | --- | --- |
| E | TenantId uniqueidentifier NOT NULL、Id uniqueidentifier NOT NULL、CreatedAt datetimeoffset(3) UTC、CreatedBy nvarchar(64)；PK(TenantId,Id) | [原文 L1350](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1350>) |
| M | UpdatedAt datetimeoffset(3)、UpdatedBy nvarchar(64)、RowVersion rowversion | [原文 L1351](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1351>) |
| Key | 默认nvarchar(128) BIN2，Owner/Site/Buyer为64；显示名称nvarchar(200)，原因nvarchar(1000)，备注nvarchar(2000) | [原文 L1352](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1352>) |
| Hash | binary(32)；JSON wire用64位小写hex；不将Hash当UUID或业务批准 | [原文 L1353](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1353>) |
| J | nvarchar(max) + ISJSON检查，反序列化到本稿准确DTO；API字节/集合上限控制，无任意未知成员 | [原文 L1354](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1354>) |
| Q/P/A | decimal(19,6)数量／decimal(19,8)单价／decimal(19,4)金额；Fx decimal(28,12)；按相应源单位/币种另存代码 | [原文 L1355](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1355>) |
| Ref | Owner64 / EvidenceKey128 / EvidenceVersion64，或精确RefJson；同名Ref不可引用“latest” | [原文 L1356](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1356>) |

| 表 | 具体字段（在公共集合之外） | 关键约束及对应DTO | 出处 |
| --- | --- | --- | --- |
| Order | E+M；PoNo varchar(16)、OrderKind24、OriginKind32、CreationKey UUID、SupplierKey128、SiteKey64、BuyerKey64、InitialRevisionId UUID、ActiveRevisionId UUID NULL、Lifecycle16、Confirmation16、ReceivingClosed bit、ProcurementClosed bit、SourceDisposition24、EvidenceMode8 | UQ(TenantId,PoNo)、UQ(TenantId,CreationKey)；初始/当前Revision同PO；INDEX(TenantId,SiteKey,BuyerKey,Lifecycle,UpdatedAt,Id) | [原文 L1364](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1364>) |
| OrderRevision | E+M；PoId、BusinessRevision bigint>0、PredecessorRevisionId NULL、State24、OrderDate date、ContentHash、CommercialPayloadJson、Remarks NULL | UQ(TenantId,PoId,BusinessRevision)；状态不在ContentHash中；Payload完整TargetCommercialPayload；不能把新version改变历史内容 | [原文 L1365](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1365>) |
| RevisionLine | TenantId,RevisionId,LineId UUID；LineNo int、OriginManifestRowId UUID、ItemKey128、TechnicalJson、Quantity Q、Uom16、OriginalCommercialJson、RevisionCommercialJson | PK(TenantId,RevisionId,LineId)、UQ(TenantId,RevisionId,LineNo)；Original只读源，RevisionCommercial是本版未来商业；两者JSON类型均TargetLine | [原文 L1366](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1366>) |
| Schedule | E+M；PoId,PoLineId,IntroducedRevisionId UUID、ScheduleNo int、CurrentQuantity Q、Uom16、CurrentScope bit、OriginalDeliverySnapshotId UUID、CurrentConfirmationId NULL、SupplyDate date NULL、Commitment24 | 同PO行关联；Quantity由受控调整/转移，不直接PATCH；当前与历史RevisionSchedule关联分开 | [原文 L1367](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1367>) |
| RevisionSchedule | TenantId,RevisionId,ScheduleId UUID；FrozenQuantity Q、RequiredDeliveryJson、SupplierDeliveryJson、OriginalResolutionJson、SourcePortionIdsJson | PK含Tenant+RevisionId+ScheduleId；保存该文书时的清单，不读当前Schedule覆盖旧文书 | [原文 L1368](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1368>) |
| SourcePortion | E+M；PoId,PoLineId,ScheduleId UUID、ParentPortionId NULL、GrantRefJson、ResponsibilityJson、SourceBusinessVersion64、Generation bigint、OriginalSourceQty Q、AddedSourceQty Q、TransferredInQty Q、TransferredOutQty Q、ReducedQty Q、CancelledQty Q、SourceUom16、ExecutionEffectId128、LineageRefJson NULL、DispositionState24 | FK Parent同根；不同子portion可同Grant但由PortionTransfer证明；不得重复SourceUse；derived量与QuantityEntry对账 | [原文 L1369](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1369>) |
| PortionTransfer | E；ChangeId、FromPortionId、ToPortionId UUID、Quantity Q、SourceUom16、TransferKey128 | UQ(TenantId,ChangeId,TransferKey)；一入一出同事务；不能转已履约份额 | [原文 L1370](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1370>) |
| IntakeRegistration | TenantId,OriginKind32,CreationKey UUID；CanonicalGroupKey UUID NULL、BusinessIdentityHash、ManifestHash、Generation bigint、RequestHash、SourceRegistryRefJson、OriginalManifestJson NULL、AuthorizedBundleJson NULL、Outcome32、PoId NULL、ReceiptRefJson NULL、FenceJson NULL、ObservedAt instant、RowVersion | PK(TenantId,CreationKey)；RFQ UQ(TenantId,CanonicalGroupKey)过滤非null；同组不同generation需原上游合法新CanonicalGroup，不能覆原终态 | [原文 L1371](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1371>) |
| IntakeSnapshot | E；CreationKey UUID、SourceHash、PayloadHash、OriginalPayloadJson、OriginalSourceJson、AdapterContractRefJson | 每creationKey唯一原始商业；OriginalSource按OriginKind分RFQ完整manifest或PoAuthorizedBundle，不混同 | [原文 L1372](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1372>) |
| IntakeLineMap | TenantId,CreationKey UUID,ExternalLineKey128；ManifestRowId UUID NULL、PoId,PoLineId UUID、EffectId128、SourceShareQuantitiesJson、DeliverySnapshotHash、DeliveryEvidenceRefJson | PK(TenantId,CreationKey,ExternalLineKey)；UQ同creation+PoLineId；原回执依据，不以行号猜映射 | [原文 L1373](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1373>) |
| DeliverySnapshot | E；PoId,PoLineId UUID、ManifestRowId UUID、RequiredDeliveryJson、SupplierDeliveryJson、OriginalResolutionJson、CalendarSnapshotJson NULL、SnapshotHash | UQ(TenantId,PoLineId,SnapshotHash)；新修订另建，不改原；计算例证/引用可追 | [原文 L1374](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1374>) |
| MappingEvidence | E；InputHash、SourceRefsJson、AdapterContractRefJson、TargetHash NULL、MappingItemsJson、VerifiedAt | UQ(TenantId,InputHash,AdapterIdentityHash)；同输入同版本不同输出冲突，不覆盖 | [原文 L1375](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1375>) |
| ExecutionReceipt | E；CreationKey UUID、ReceiptVersion bigint、BodyJson、SemanticHash、OccurredAt | UQ(TenantId,CreationKey,ReceiptVersion)；body=ExecutionEvidenceBody；SUCCEEDED不可倒成未发生 | [原文 L1376](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1376>) |
| SourceUse | E；CreationKey UUID、GrantIdentityHash、GrantRefJson、Generation bigint、Qty Q、Uom16、ExecutionEffectId128、OwnerUseRefJson、State24、SourceVersion bigint、CurrentObservationRefJson NULL | UQ(TenantId,GrantIdentityHash,Generation)对本域首次完整根使用；Source权威额外防跨入口重用；PortionTransfer不新增SourceUse | [原文 L1377](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1377>) |
| Preparation | E+M；State16、InputJson、ValidatedSnapshotHash NULL、AuthorizedBundleRefJson NULL、CreationKey UUID NULL、PoId NULL、GatesJson | 精确PoPreparationInput；保存返回真实子ID；校验后变更清旧指针 | [原文 L1378](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1378>) |
| PreparationEvidence | E；PreparationId UUID、InputHash、SourceBundleJson、MappingRefsJson、CapturedAt | 不可变校验历史；不是Source授权主账 | [原文 L1379](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1379>) |

| 表 | 具体字段 | 唯一性与状态条件 | 出处 |
| --- | --- | --- | --- |
| ApprovalSubmission | E+M；PoId、SubjectKind32、SubjectId、SubjectBusinessRevision bigint>0、SubjectRowVersion binary8、SubjectHash、PolicyIdentityHash、PolicyRefJson、EvaluationJson、InstanceKey128 NULL、Decision24、Application24、ReplacesSubmissionId NULL | UQ(Tenant,SubjectKind,SubjectId,SubjectBusinessRevision,SubjectHash,PolicyIdentityHash)；审批内容和技术token分开 | [原文 L1389](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1389>) |
| ApprovalFact | E；SubmissionId、Producer64、InstanceKey128、EvidenceRefJson、Decision16、DecidedBy64、DecidedAt、SubjectHash、SemanticHash | UQ(Tenant,Producer,InstanceKey,DecisionEvidenceIdentityHash)；矛盾另Conflict | [原文 L1390](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1390>) |
| ApprovalApplication | E；SubmissionId、SubjectId、ContentHash、Action32、ResultRefJson、AppliedAt | UQ(Tenant,SubmissionId,Action)；由最终业务事务生成 | [原文 L1391](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1391>) |
| ReleaseRecord | E；PoId、RevisionId、ContentHash、ApprovalSubmissionId、RecipientKey128、DisclosureRefJson、ReleasedAt | 同版/准确收件范围受控，一次业务意图；不是Supplier接受 | [原文 L1392](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1392>) |
| Dispatch | E+M；ReleaseId、DispatchKey128、ContentHash、RecipientKey128、AttachmentsJson、State16、ChannelEvidenceRefJson NULL、EverSentOrUnknown bit | UQ(Tenant,DispatchKey)，未知不换内容 | [原文 L1393](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1393>) |
| SupplierAck | E+M；PoId、RevisionId、ReleasedHash、SupplierEvidenceRefJson、AckIdentityHash、AcknowledgedAt NULL、InputJson、Verified bit、ExceptionCodesJson | UQ(Tenant,PoId,AckIdentityHash)含证据Owner/id/version；Input和真实回应不可改。M只用于受信验证状态，不代表任意编辑原材料 | [原文 L1394](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1394>) |
| SupplierResponse | E；AckId、PoId、SupplierKey128、RevisionId、ScheduleId、ResponseRefJson、ResponseIdentityHash、PredecessorRefJson NULL、StreamEpoch64、ResponseSequence bigint>0、Response16、Quantity Q、Uom16、ProposedDeliveryJson NULL、AuthorityRefJson、SemanticHash、EffectiveAt、ValidationState32 | UQ(Tenant,ResponseIdentityHash,ScheduleId)；同Ref可多Schedule但每Schedule一条。查流INDEX(Tenant,SupplierKey,RevisionId,ScheduleId,StreamEpoch,ResponseSequence)；冲突输入保InboxConflict不覆盖可信事实 | [原文 L1395](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1395>) |
| SupplierResponseHead | E+M；PoId、SupplierKey128、RevisionId、ScheduleId、StreamEpoch64、HeadRevision bigint>=0、CurrentResponseId NULL、State32、PendingRefsJson、AuthorityRefJson NULL | UQ(Tenant,SupplierKey,RevisionId,ScheduleId)；CurrentResponse必须同流。序列/前驱原子核验，不用MAX(seq)或时间决定；分叉/缺口限制当前使用 | [原文 L1396](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1396>) |
| SupplierCurrentDeclaration | E；PoId、SupplierKey128、RevisionId、DeclarationRefJson、ScopeScheduleIdsJson、ResponsesJson、AsOf、ValidThrough、SemanticHash | UQ(Tenant,DeclarationIdentityHash)；PoSupplierCurrentDeclaration原文，不把短期签证当永久接受 | [原文 L1397](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1397>) |
| Confirmation | E；PoId、RevisionId、AckId、ConfirmationKey128、ConfirmedAt、AppliedAt、PolicyRefJson、ScheduleIdsJson、ResponseHeadBindingsJson、CurrentDeclarationId、State16 | UQ(Tenant,ConfirmationKey)含版/Ack/准确ResponseRef/Schedule集合；历史APPLIED不被当前头覆盖 | [原文 L1398](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1398>) |
| DeliveryAssessment | E；ConfirmationId、ScheduleId、OriginalSnapshotHash、AnchorLocalDate date、ResolvedDate NULL、CalendarRefJson NULL、CalendarRuleHash NULL、CheckJson、AssessmentHash | UQ(Tenant,ConfirmationId,ScheduleId)；冻结原日历 | [原文 L1399](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1399>) |
| Change | E+M；PoId、BaseRevisionId、ProposalRevision bigint、State32、InputJson、ContentHash、AssessmentId NULL、ApprovalSubmissionId NULL、WorkId NULL、NewRevisionId NULL、ApplicationId NULL | Work FK唯一；输入仅DRAFT可改。APPLIED必须指ChangeApplication，不能只有State=APPLIED无新Revision | [原文 L1400](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1400>) |
| ChangeApplication | E；ChangeId、WorkId、PoId、ProposalRevision、ContentHash、ScopeHash、BaseRevisionId、NewRevisionId、TransferIdsJson、AddedSourceUseRefsJson、ApprovalApplicationId、EvidenceSetId、AppliedAt | UQ(Tenant,ChangeId)、UQ(Tenant,NewRevisionId)；Ref={PO,Id,1}；与全部新业务写同事务 | [原文 L1401](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1401>) |
| ScopeAssessment | E；PoId、SubjectId、SubjectKind24、ScopeHash、ContentHash、CapturedAt、OrderRowVersion binary8、FulfillmentRevision bigint、GuardRevision bigint、RowsJson、GatesJson | 不可变；GuardRevision来源ReceivingGuardCounter；不作为永久许可 | [原文 L1402](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1402>) |
| Cancellation | E+M；PoId、BaseRevisionId、ProposalRevision bigint、State24、InputJson、ContentHash、AssessmentId NULL、ApprovalSubmissionId NULL、WorkId NULL、EvidenceSetId NULL、CutoffRefJson NULL、SourceDisposition24、AppliedApplicationId NULL、ErrorCode64 NULL | 内容创建后固定；UQ(Tenant,WorkId)过滤非null；View.appliedCancellationRef由Application生成，不从Input临时Ref填入 | [原文 L1403](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1403>) |
| CancellationApplication | E；CancellationId、WorkId、PoId、ProposalRevision bigint、ContentHash、ScopeHash、BaseRevisionId、AppliedSlicesJson、CancelledSegmentsJson、QuantityEntryIdsJson、ApprovalApplicationId、EvidenceSetId、CutoffProofRefJson、AppliedAt | UQ(Tenant,CancellationId)、UQ(Tenant,WorkId)；完整PoCancellationApplication，Ref={PO,Id,1}永久不变；不是Source已返额 | [原文 L1404](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1404>) |
| QuantityEntry | E；PoId、PoLineId、ScheduleId、SourcePortionId、Kind16、Qty Q、Uom16、BusinessActionKey128、EvidenceRefJson、EffectiveAt | UQ(Tenant,Kind,BusinessActionKey,SourcePortionId)；取消BusinessActionKey=CancellationApplication.id，EvidenceRef指同Application；transfer仍一入一出 | [原文 L1405](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1405>) |
| CancellationHold | E+M；CancellationId、WorkId、SourcePortionId、SegmentIdsJson、Qty Q、Uom16、State16、CreatedGuardRevision bigint | 本Work拥有的自由片；UQ(Tenant,WorkId,SourcePortionId)；HELD/CONSUMED/RELEASED；不能重复占Permit片 | [原文 L1406](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1406>) |
| ChangeHold | E+M；ChangeId、WorkId、SourcePortionId、SegmentIdsJson、Qty Q、Uom16、State16、CreatedGuardRevision bigint | 同上，移转最终时CONSUMED；拒绝/弃稿且无效果已证才RELEASED | [原文 L1407](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1407>) |
| SourceDisposition | E+M；CancellationId、ApplicationId、WorkId、SourceUseId、DependencyRequestId、OperationKey128、RequestJson、ResultJson NULL、CurrentResultId NULL、State24、LastError64 NULL | UQ(Tenant,OperationKey)及UQ(Tenant,ApplicationId,SourceUseId,DispositionScopeHash)；DispositionScopeHash binary32保存准确份额；Source返回结果历史在DependencyResult | [原文 L1408](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1408>) |
| Closure | E+M；PoId、Kind16、ProposalRevision bigint、InputJson、ContentHash、State16、ApprovalSubmissionId NULL、ClosedAt NULL、EvidenceJson NULL、CurrentEvidenceSnapshotId NULL、ReceivingBarrierId NULL | 版本及Input固定；EvidenceJson=PoClosureEvidence；UQ(Tenant,PoId,Kind,ProposalRevision) | [原文 L1409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1409>) |
| ClosureEvidenceSnapshot | E；ClosureId、AssessmentRevision bigint、EvidenceJson、EvidenceHash、CapturedAt | UQ(Tenant,ClosureId,AssessmentRevision)；当前指针不擦旧评估；不能用Close覆盖其他Owner状态 | [原文 L1410](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1410>) |

| 表 | 具体字段 | 不变量 | 出处 |
| --- | --- | --- | --- |
| ReceivingGuard | TenantId、PoId、ScheduleId、SourcePortionId；Revision bigint、Closed bit、TechnicalHold bit、RowVersion | PK四维；TechnicalHold/Closed为ControlBlock派生加速字段，最终核准确控制Ref，不能解除布尔抹多项限制 | [原文 L1418](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1418>) |
| ReceivingGuardCounter | TenantId、PoId；Revision bigint>=0、RowVersion | PK；本PO任何许可/片/屏障变更同事务+1，供旧标量guardRevision及cutoff；逐片vector仍分别检查 | [原文 L1419](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1419>) |
| ControlBlock | E+M；PoId、ScopeHash、SchedulePortionManifestJson、Kind32、CauseRefJson、OwnerWorkKind16 NULL、OwnerWorkId NULL、Active bit、InstalledGuardRevision bigint、DrainPermitIdsJson、RetiredByRefJson NULL、EffectiveAt | UQ(Tenant,Kind,CauseIdentityHash,ScopeHash)；TECHNICAL_HARD_STOP / CHANGE_DRAIN_BARRIER / CANCEL_DRAIN_BARRIER / RECEIVING_CLOSE_BARRIER / FINAL_CANCELLED_SCOPE。必须具体Ref/范围，解除一种不解除另一种 | [原文 L1420](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1420>) |
| BudgetSegment | E+M；SourcePortionId、StartUnits decimal(38,0)、EndUnits decimal(38,0)、OwnerKind16、OwnerId NULL、ParentSegmentId NULL | 6位scale整数区间，不交叠；OwnerKind FREE/PERMIT/RECEIPT/CHANGE_HOLD/CANCEL_HOLD/CANCELLED。禁止同片两Owner；只同Guard事务拆合 | [原文 L1421](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1421>) |
| ReceivingPermit | E+M；ReceiptOperationKey128、PoId、ScopeHash、RowsJson、State32、GuardRevision bigint、EvaluatedGuardsJson、ExpiresAt NULL、ReceiptEvidenceJson、StartId NULL、OwnerStartIntentRefJson NULL、StartedAt NULL、EverStarted bit、BlockingControlRefsJson、NoEffectProofRefJson NULL、SettledSegmentsJson | UQ(Tenant,ReceiptOperationKey)；State为PoPermitState全八值，UNKNOWN等未定片持续占I。currentGuards是查询投影，非写入快照 | [原文 L1422](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1422>) |
| PermitStart | E；PermitId、StartKey128、OwnerStartIntentRefJson、GuardVectorJson、GuardRevision bigint、StartedAt、SemanticHash | UQ(Tenant,PermitId)，Ref={PO,Id,1}；GR ReceiptIntent enlist同事务，不能由前端提交一串Ref假设已开始 | [原文 L1423](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1423>) |
| PermitDecision | E；PermitId、PreviousState32、State32、CauseRefJson、Action32、EvaluatedGuardsJson、PermittedCompletionPathsJson、EvidenceRefJson、RecordedAt | PoPermitControlDecision；每次有业务变化的复核/阻断追加，不删开始历史 | [原文 L1424](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1424>) |
| PermitConsumption | E；PermitId、ReceiptIdentityHash、ReceiptIdentityJson、ScopeHash、StartId、SegmentsJson、ReceiptEvidenceRefsJson、ConsumedGuardVectorJson、ConsumedAt、SemanticHash | UQ(Tenant,PermitId,ReceiptIdentityHash)；正常完整一次消费，集合须与原Permit相符。分片异常结果另PermitSegmentResult；不重发物理动作 | [原文 L1425](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1425>) |
| PermitSegmentResult | TenantId、PermitId、SegmentId；Result16、Qty Q、Uom16、EvidenceRefJson、DecisionId | PK三维；USED/NO_EFFECT，每片真实证据，冲突隔离；混合完整SETTLED_PARTIAL，未齐UNKNOWN | [原文 L1426](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1426>) |
| ReceiptSliceMap | E；ReceiptIdentityHash、ReceiptSliceRefJson、PermitId NULL、SourcePortionId、SegmentIdsJson、Qty Q、Uom16、EvidenceJson | UQ(Tenant,ReceiptIdentityHash,ReceiptSliceIdentityHash,SourcePortionId)；QA/AP同Slice不再占量 | [原文 L1427](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1427>) |
| FactVersion | E；Kind24、IdentityHash、IdentityJson、FactVersion bigint、PreviousVersion bigint NULL、State24、PayloadJson、SemanticHash、EffectiveAt、ObservedAt、EvidenceMode8 | UQ(Tenant,Kind,IdentityHash,FactVersion)；正文原量/版本保全 | [原文 L1428](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1428>) |
| FactCurrent | TenantId、Kind24、IdentityHash；VersionId、FactVersion bigint、State24、RowVersion | PK三维；当前版本前驱CAS，不按接收时间覆盖 | [原文 L1429](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1429>) |
| FactAllocation | TenantId、FactVersionId、AllocationOrdinal；PoId、PoLineId、ScheduleId、SourcePortionId、Qty Q、Uom16、OriginalQty Q、OriginalUom16、ConversionRefJson NULL、ReceiptSliceRefJson NULL | FK同Tenant，完整manifest核原单据行分配和；不猜份额 | [原文 L1430](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1430>) |
| DocumentAllocationSet | E+M；Producer64、LedgerKey128、DocumentId128、DocumentLineId128、ManifestRefJson、ManifestVersion64、Complete bit、AllocationIdentitiesJson、Hash | 同源行版本完整性，不能多allocationKey重复同量 | [原文 L1431](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1431>) |
| FulfillmentProjection | TenantId、PoId、ScheduleId、SourcePortionId；Revision bigint、InitialQ/AddedQ/TransferInQ/TransferOutQ/ReducedQ/CancelledQ各Q、Received/Accepted/Invoiced/Returned/Credited各Q NULL、MetricCompletenessJson、EvidenceWatermarksJson、AsOf | PK四维；从FactCurrent/QuantityEntry可重建，不拥有Stock | [原文 L1432](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1432>) |
| FulfillmentCursor | TenantId、PoId；Revision bigint、LastFactsHash、RowVersion | 同PO快照/CAS；可信空集合起点0 | [原文 L1433](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1433>) |
| Discrepancy | E+M；PoId、SourcePortionId NULL、Kind64、OwnerEvidenceRefJson、RelatedRefsJson、State24、ResolutionRefJson NULL | UQ(Tenant,DiscrepancyIdentityHash)；晚10 RelatedRefs必须含原CancellationApplication；Supplier后继不擦Confirmation | [原文 L1434](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1434>) |
| CutoffEvidence | E；WorkKind16、WorkId、SubjectId、ScopeHash、ResultJson、ProofRefJson、InstalledGuardRevision bigint、ObservedFactDigest、DependencyResultId、CapturedAt | UQ(Tenant,ProofIdentityHash)；CHANGE存PoChangeCutoffResult、CANCELLATION存PoCutoffResult，不借用CancellationId装Change；未来真反例另Discrepancy | [原文 L1435](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1435>) |
| FactSetSnapshot | E；PoId、Kind24、OwnerSnapshotRefJson、ThroughToken128、IdentityManifestJson、Complete bit、CollectedAt | 所有页同throughToken才完整 | [原文 L1436](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1436>) |

| 表 | 必备字段 | 约束/用途 | 出处 |
| --- | --- | --- | --- |
| EngineeringRelease | E+M；ReleaseRefJson、BaselineRefJson、Hash、Purpose64、Target128、ScopeJson、Stage24、ReceiptVersion bigint、AppliedRefsJson | 同Release/Target/scope唯一；收到不等采用 | [原文 L1444](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1444>) |
| EngineeringFeedback | E+M；PoId UUID、FeedbackKey128、InputJson、State24、OwnerReceiptJson NULL | UQ(TenantId,FeedbackKey)；input是PoFeedbackInput | [原文 L1445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1445>) |
| EngineeringImpactSnapshot | E；Producer64、RequestEventId UUID、RequestHash、RequestedSnapshotRefJson、RegistryThrough bigint、ItemIdsJson、CapturedAt | UQ(TenantId,Producer,RequestEventId)；后续分页只读同一ItemIds/评估版本，不混最新扫描 | [原文 L1446](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1446>) |
| EngineeringImpact | E+M；PoId UUID、ImpactItemRefJson、ChangeRefJson、InputSnapshotJson、EvaluatedRevisionId UUID、FulfillmentRevision bigint、RegistryThrough bigint、State24、ProposedAction32、EvaluationJson、ResultRefJson NULL | 评估版本固定，新的Delta留历史；不与PLM抢事实 | [原文 L1447](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1447>) |
| EngineeringExecution | E+M；ImpactId UUID、ExecutionKey128、RequestHash、InputJson、State24、PoChangeId NULL、PoCancellationId NULL、ReceiptJson NULL | UQ(TenantId,ExecutionKey)；同键异范围拒绝 | [原文 L1448](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1448>) |
| SupplySnapshot | E；PoId UUID、SupplyRevision bigint、CompletePayloadJson、PayloadHash、CausationRefsJson、AsOf | UQ(TenantId,PoId,SupplyRevision)；完整集合含退役键；取消因果Ref等于CancellationApplication.Ref；Owner提交后再投递 | [原文 L1449](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1449>) |
| OrderRegistry | TenantId,PoId UUID；RegistrySeq bigint、CurrentRevisionId UUID、State24 | UQ(TenantId,RegistrySeq)；新建/技术变更/影响范围改变时追加RegistryEvent并推进；仅用于有界影响水位 | [原文 L1450](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1450>) |
| Audit | E；PoId UUID、TimelineSeq bigint、ResourceKind32、ResourceId UUID、ResourceSeq bigint、Operation64、OccurredAt、Reason NULL、ChangesJson、EvidenceRefsJson、CommandId NULL | UQ(TenantId,PoId,TimelineSeq)及UQ资源事件身份；不跨资源比较ResourceSeq | [原文 L1451](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1451>) |
| AuditCounter | TenantId,PoId；NextSeq bigint | 同Order审计追加在业务事务末取号；实际事务回滚不留下伪完成 | [原文 L1452](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1452>) |
| Command | TenantId,CommandId UUID、ActorKey64、Method8、Path nvarchar(256)、RequestHash、Outcome32、HttpStatus smallint、SafeResultJson、ResourceKind32 NULL、ResourceId NULL、CreatedAt/CompletedAt | PK(TenantId,CommandId)；永久保留防重/终态墓碑；未知不能直接新键重做 | [原文 L1453](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1453>) |
| Inbox | TenantId,Producer64,EventId UUID、Type32、RequestHash、BusinessIdentityHash、PayloadJson、State32、ErrorCode64 NULL、ReceivedAt/ProcessedAt NULL | PK；陌生机器拒绝不写业务成功，合法冲突留独立表 | [原文 L1454](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1454>) |
| InboxConflict | E；Producer64、EventId UUID、OriginalHash、IncomingHash、IncomingPayloadJson、Reason64、State24 | 不覆盖原Inbox；审批/事实/执行同版冲突可定位 | [原文 L1455](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1455>) |
| Outbox | E+M；WorkType64、BusinessKey128、PayloadJson、PayloadHash、State16、AttemptCount int、LeaseId NULL、LeaseUntil NULL、NextAttemptAt、EverDispatched bit、ResultRefJson NULL、CausationRefsJson | UQ(TenantId,WorkType,BusinessKey)；原键投递/恢复；CHANGE_WAKE/CANCEL_WAKE只存WorkId及WakeRevision，不以此租约代替Work最终提交租约 | [原文 L1456](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1456>) |
| Operation | E+M；Kind32、BusinessKey128、State24、RequestHash、ResultRefsJson、ErrorCode64 NULL、NextAction24、WorkKind16 NULL、WorkId UUID NULL | UQ(TenantId,Kind,BusinessKey)；CHANGE/CANCEL只读投影真实ChangeWork/CancellationWork，不是第二套工作执行器；普通create/feedback/reconcile保原责任 | [原文 L1457](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1457>) |
| ExportJob | E+M；InputJson、AuthRevision128、State16、ArtifactRefJson NULL、ContentHash NULL、RowCount int NULL、ErrorCode64 NULL、ExpiresAt NULL | 生成前和下载前核权；query不生成文件 | [原文 L1458](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1458>) |
| DownloadTicket | E；PoId UUID、ActorKey64、EvidenceRefJson、AuthRevision128、ExpiresAt、RevokedAt NULL | 有效性不代下载当前授权 | [原文 L1459](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1459>) |
| Number | TenantId,Kind16；NextValue bigint、RowVersion | PK；取号同订单事务，禁MAX+1 | [原文 L1460](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1460>) |

| 表 | 明确字段与DTO | 唯一约束/写责任 | 出处 |
| --- | --- | --- | --- |
| ChangeWork | E+M；ChangeId、PoId、ProposalRevision bigint、SubjectContentHash、ApplyRequestHash、ApplyInputJson(PoApplyChange)、BaseRevisionId、ScopeHash、Phase32、BusinessOutcome24、InstalledGuardRevision bigint、WatchedPermitIdsJson、DrainPermitIdsJson、EvidenceSetId NULL、ApplicationId NULL、NewRevisionId NULL、LeaseId UUID NULL、LeaseEpoch bigint、LeaseUntil NULL、NextAttemptAt NULL、AttemptCount int、WakeRevision bigint、AbandonRequested bit、FinalApplicationFenced bit、OriginActor64、AuthorizationRefJson、ErrorCode64 NULL | UQ(Tenant,ChangeId)；PoChangeWorkView由本表/ChangeHold/Dependency头生成。INDEX(Phase,NextAttemptAt,LeaseUntil)。租约是Work推进权威 | [原文 L1468](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1468>) |
| CancellationWork | E+M；CancellationId、PoId、ProposalRevision、SubjectContentHash、ApplyRequestHash、ApplyInputJson(PoCancellationApply)、BaseRevisionId、ScopeHash、Phase32、BusinessOutcome24、InstalledGuardRevision、WatchedPermitIdsJson、DrainPermitIdsJson、EvidenceSetId NULL、ApplicationId NULL、SourceDisposition24、LeaseId NULL、LeaseEpoch、LeaseUntil NULL、NextAttemptAt NULL、AttemptCount、WakeRevision、WithdrawRequested bit、FinalApplicationFenced bit、OriginActor64、AuthorizationRefJson、ErrorCode64 NULL | UQ(Tenant,CancellationId)；PoCancellationWorkView；APPLIED商业事实不会因Source慢而退NOT_APPLIED。INDEX同ChangeWork | [原文 L1469](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1469>) |
| OwnerDependencyRequest | E；PoId、WorkKind16、WorkId、SubjectId、DependencyKind32、ProducerOwner64、OperationKey128、SubjectContentHash、ScopeHash、ProposalRefJson、PolicyRefJson、SubjectSlicesJson、ReferencedInputsJson、Action32、ExpectedPredecessorRefJson NULL、ClaimPlanJson NULL、SourceDispositionRequestJson NULL、RequestHash、RequestJson、EverSent bit、FirstSentAt NULL | UQ(Tenant,OperationKey)；PoDependencyRequest原文。EverSent只能false→true；工作attempt不创造另一个请求。首次网络前持久发送意图 | [原文 L1470](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1470>) |
| OwnerDependencyResult | E；DependencyRequestId、OperationKey128、ProducerOwner64、ResultRefJson、ResultIdentityHash、ResultVersion bigint、PredecessorResultRefJson NULL、SubjectContentHash、ScopeHash、SemanticHash、Outcome24、EffectiveAt、ObservedAt、EvidenceMode8、DecisionPayloadJson NULL、CancellationCutoffJson NULL、ChangeCutoffJson NULL、ClaimBundleJson NULL、SourceDispositionJson NULL | UQ(Tenant,DependencyRequestId,ResultIdentityHash)；符合PoDependencyResult判别联合，版本同义/冲突都不能覆盖原证据。请求必须存在且Producer/内容/范围吻合 | [原文 L1471](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1471>) |
| OwnerDependencyHead | TenantId、DependencyRequestId；HeadVersion bigint>=0、CurrentResultId NULL、OwnerResultVersion bigint NULL、State24、PendingRefsJson、RowVersion | PK；缺口/分叉为阻断态。业务seq和本地headVersion分开；最终EvidenceSet比此头，不从Result表MAX时间取值 | [原文 L1472](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1472>) |
| DependencyEvidenceSet | E；WorkKind16、WorkId、SubjectContentHash、ScopeHash、MembersJson、CapturedGuardRevision bigint、FulfillmentRevision bigint、EvidenceSetHash、CapturedAt | PoDependencyEvidenceSet；Ref={PO,Id,1}，不可变；成员准确指Request+HeadVersion+OwnerResultRef+Hash，不持久一串“通过”文本 | [原文 L1473](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1473>) |
| WorkAttempt | E；WorkKind16、WorkId、LeaseEpoch bigint、LeaseId UUID、Step32、DependencyRequestId NULL、StartedAt、CompletedAt NULL、Outcome24、ResultRefJson NULL、ErrorCode64 NULL | 唯一(Tenant,WorkKind,WorkId,LeaseEpoch,Id)，诊断历史；不能从attempt数推定外部效果未发生 | [原文 L1474](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1474>) |
| WorkFinalFence | TenantId、WorkKind16、WorkId；FenceId UUID、CauseRefJson、InstalledAt、OriginCommandId UUID | PK(Tenant,WorkKind,WorkId)；与最终Application争同锁；弃稿/确定不可执行时永久不许新应用，不释放外部保护 | [原文 L1475](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1475>) |

| 上层字段/说法 | 唯一持久对象/业务键 | 是否可改及消费者 | 出处 |
| --- | --- | --- | --- |
| PoCancellationView.appliedCancellationRef | CancellationApplication.Id＋version1；UQ CancellationId | 不可变；SourceDisposition、Planning causationRefs、晚10差异均指它 | [原文 L1481](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1481>) |
| PoChangeView.applicationRef | ChangeApplication.Id＋version1；UQ ChangeId | 不可变；Source追加使用/Transfer和Planning因果 | [原文 L1482](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1482>) |
| View.workId | 对应ChangeWork/CancellationWork.Id | 不换工作身份；resume只更新同Work | [原文 L1483](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1483>) |
| View.evidenceSetRef | 最终选定的DependencyEvidenceSet.Id | READY可生成后继Set，但Application保存其最终Set不可改 | [原文 L1484](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1484>) |
| Supplier取消同意 | DependencyKind=SUPPLIER_AGREEMENT；Request内容绑定提案，Result保存Owner真实同意/撤回链 | 当前Head可推进，原PoCancelInput.supplierAgreementRef不变 | [原文 L1485](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1485>) |
| 费用义务处置 | FEE_DISPOSITION＋具体商业Owner＋相同scope及原条件 | No-charge也须证据；不能自动造Finance抵销 | [原文 L1486](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1486>) |
| GR cutoff | GR_CUTOFF Result及CutoffEvidence.DependencyResultId | Proof对应特定Work/Scope/Guard/watermark；晚反例另差异 | [原文 L1487](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1487>) |
| Source执行前可处置资格 | SOURCE_ELIGIBILITY Result | 只准入，不标Source已返额；与SourceDisposition分开 | [原文 L1488](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1488>) |
| Source最终处置 | SourceDisposition.ApplicationId＋SourceUseId＋DispositionScopeHash；其Request/Result同一operationKey | APPLIED原回执重放，未知先query；不会另返额 | [原文 L1489](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1489>) |
| Quality/AP必需依赖 | QUALITY_CLEARANCE/AP_CLEARANCE独立Request、Result和Head | 生产者能力分开，后继改判/票据状态只更新本域证据投影 | [原文 L1490](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1490>) |
| 未应用关闭/清理证明 | NO_EFFECT依赖＋原operationKey/路径覆盖，WorkFinalFence | 单纯租约过期/没记录不能成为证明 | [原文 L1491](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1491>) |

PoNo 为 PO-＋12位租户原子流水，旧号原样保留；内容hash排除当前履约数、状态、rowVersion、发送状态与接收时点。CancellationEntry 已不再是规范对象：不可变 CancellationApplication 表示商业取消事实，QuantityEntry(kind=CANCEL) 表示数量作用。 [原文 L1381](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1381>)、[原文 L1412](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1412>)

## 4. 完整业务流程与数量约束

### 4.1 来源准备和原子创建

RFQ到达前先按完整原 types/mapping check，原 Supplier、Site、币种、原费用 occurrence、阶梯、零价、技术和三段交付均不能丢。整组检查双向行映射、所有Claim保护和当前Scope。一个兼容组只建一PO与所有行，PO端不另拆组；同CanonicalGroup的alias回原结果，同Grant代次不能因新Preparation／新Basis再消费。 [原文 L651](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:651>)、[原文 L661](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:661>)、[原文 L663](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:663>)、[原文 L690](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:690>)、[原文 L691](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:691>)

正式创建要求Source和RFQ应用参与同一实际 DbConnection/DbTransaction，否则 PO_OWNER_ATOMICITY_UNAVAILABLE、零PO。最终一次提交真实 PO/Rev1 DRAFT、行／初始Schedule、SourceUse、逐行Effect映射、原回执、SUCCESS、审计与Outbox；activeRevision仍null、没有Supplier承诺。原效果查询先于今日资格，提交未知只查原creationKey，不再取号。 [原文 L689](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:689>)、[原文 L692](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:692>)、[原文 L694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:694>)、[原文 L695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:695>)、[原文 L696](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:696>)、[原文 L697](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:697>)

非RFQ准备先保存服务端稳定draftLineId，不预造未来PO行。Source解析返回准确授权Bundle和商业证据；validate是有副作用操作，固化映射、bundle/hash及规范creationKey，任何输入改变清原校验指针。只保存Preparation不是PO；create-order登记原工作并取全Claim，真实完成才201／PO ID，未决202指向Preparation／Operation。原来源不允许就BLOCKED，不伪RFQ ID或默认供应商价格。 [原文 L744](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:744>)、[原文 L748](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:748>)、[原文 L750](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:750>)、[原文 L752](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:752>)、[原文 L754](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:754>)、[原文 L758](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:758>)、[原文 L760](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:760>)

### 4.2 审批、发出、供应商回应与确认

PO_RELEASE／CHANGE／CANCEL／CLOSE各用准确subject businessRevision、提交时rowVersion、内容hash及稳定policyIdentity；权限／矩阵需唯一真实匹配，不猜阈值或免审。重复同意图复用原审批实例。回调只记OA事实，按钮按当前内容/范围再应用，不自动发单、确认或取消。拒绝需要真实后继内容版本，已经消费的Source须正式处置。 [原文 L783](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:783>)、[原文 L795](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:795>)、[原文 L797](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:797>)、[原文 L799](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:799>)、[原文 L801](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:801>)

release锁内核Order和Revision token、当前批准、Source/Supplier/技术与附件披露权；一次提交ACTIVE指针、ApprovalApplication、Release、Dispatch和Command。网络投递在提交后；dispatchKey绑定原版／收件人／内容，SENT只是发送，未知且渠道无安全查询／幂等则禁止盲重发。 [原文 L805](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:805>)、[原文 L807](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:807>)、[原文 L1189](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1189>)

回复流固定Tenant/Supplier/Revision/Schedule，不允许换epoch逃避已存在头。首seq1无前驱，后继+1与真实supersedes；同Ref异义／同前驱分叉CONFLICT，缺链PENDING，低版晚到留历史不回退。无业务序列的渠道必须由真实Owner提供可信前驱关系，PO不能按收件时间自己补链。每Schedule至多一当前声明；后继COUNTEROFFER/REJECTED/WITHDRAWN阻止旧接受做新确认。 [原文 L821](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:821>)、[原文 L823](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:823>)、[原文 L825](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:825>)、[原文 L827](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:827>)

确认须选定Ack＝本地CURRENT Head＝Owner有效当前声明，同ACCEPTED Ref/hash/epoch/sequence，expectedHeadRevision逐Schedule精确。锁内再核全部身份、期限、硬交期与当前权限；一次提交所选Schedule的Confirmation、HeadBindings、独立DeliveryAssessment和供给Outbox。confirmedAt取真实Supplier接受时刻，AppliedAt为本地提交。100中只接受60视为反报价；已合法分60/40可仅确认60。历史确认先提交后来的反报价只增承诺变更差异，不删除历史或自动全单取消。 [原文 L831](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:831>)、[原文 L833](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:833>)、[原文 L837](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:837>)、[原文 L838](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:838>)、[原文 L839](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:839>)、[原文 L840](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:840>)、[原文 L841](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:841>)、[原文 L843](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:843>)

原DeliverySnapshot永不改：FIXED/RELATIVE/COMBINED要求保存原日历版本／时区／计日规则；0日等于锚点，大于0从下一当地日按原有效工作日计。确认后独立评估，不能用今天、OA批准日或最新日历补原锚点。COMBINED SAME_DATE 必须同日，HARD冲突或未知阻确认，REQUESTED晚到只在原政策允许时警示。PO演示Tokyo与RFQ示例UTC属于各自准确夹具，不强行统一。 [原文 L849](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:849>)、[原文 L851](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:851>)、[原文 L853](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:853>)、[原文 L855](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:855>)

### 4.3 未履约变更、追加与持久工作

每来源份额 Q＝initial＋added＋transferredIn−transferredOut−reduced−cancelled。拆分只把未履约份额一出多入、同UOM守恒，旧已收40仍留旧portion，未来60可30/30；不新Claim60。新增量必须另有准确Grant／Claim及同事务SourceUse。未来改价建明确新商业行／Schedule，旧已履约价不改。所有数量减少走正式取消，不同时cancel60又reduce60。 [原文 L882](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:882>)、[原文 L884](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:884>)、[原文 L886](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:886>)、[原文 L888](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:888>)、[原文 L890](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:890>)、[原文 L892](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:892>)

首次Change.apply固定返回202：只持久唯一Work、自由片Hold、CHANGE_DRAIN_BARRIER、原在途watch/drain列表、依赖请求与Wake；旧ACTIVE不改，新Revision=null。已发生新履约使原60不合法则拒原60，不静默缩50；原在途未定进入DRAINING。普通草稿不自带停止效力。 [原文 L896](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:896>)、[原文 L900](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:900>)、[原文 L902](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:902>)、[原文 L903](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:903>)、[原文 L904](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:904>)

| phase / businessOutcome | 到达条件 | 同Work的后继，不允许的捷径 | 出处 |
| --- | --- | --- | --- |
| REQUESTED / NOT_APPLIED | 原申请/屏障/Hold和依赖已提交 | Worker领取；查询无副作用 | [原文 L912](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:912>) |
| DRAINING / NOT_APPLIED | 原在途或Owner明确PENDING；所需当前证明未齐 | 逐原键查询，接收结果与cutoff；不换Change或再申请同一Grant | [原文 L913](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:913>) |
| UNKNOWN / NOT_APPLIED | Owner可能已产生效果但响应未知；无法确定最后commit | 查原Owner operationKey及本域Application；不“重置未开始” | [原文 L914](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:914>) |
| READY / NOT_APPLIED | 原量仍可执行、所有必需证据当前且完整、全部作用片已归Hold | 下一短事务重核，READY本身不承诺不可撤许可 | [原文 L915](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:915>) |
| SUCCEEDED / APPLIED | 唯一ChangeApplication与NewRevision同事务成立 | 回原结果，重放不增加第二Revision/Transfer | [原文 L916](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:916>) |
| CLEANUP_PENDING / REJECTED或NOT_APPLIED | 数量/前驱失效、申请被弃或有源准备需无效果封闭 | 固定finalApplicationFenced=true；仅原键查询/解除Source准备/处置，不再新增业务应用 | [原文 L917](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:917>) |
| REJECTED / REJECTED；ABANDONED / ABANDONED | 已拒绝或放弃且全路径无晚执行、Hold/Claim清理证据齐 | 终态；新意图需新提案/审查，不借resume改变原批准 | [原文 L918](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:918>) |

Work租约30秒、10秒续租、epoch单调；网络前记EverSent与Attempt，无业务锁调用。超过查询预算或重启未知先同operationKey查询，不换请求身份。最终先查唯一Application，再按完整锁序核当前lease、baseRevision、proposal/hash、原Hold/Permit、全部DependencyHead和EvidenceSet、finalFence。一次提交新Revision、Schedule/Transfer、可选新增SourceUse、ChangeApplication、Approval应用、原新版指针、Work与Outbox。commit未知查原Application；旧worker可交真实证据但不得最终提交。 [原文 L924](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:924>)、[原文 L926](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:926>)、[原文 L928](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:928>)、[原文 L931](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:931>)、[原文 L935](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:935>)、[原文 L1666](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1666>)、[原文 L1668](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1668>)、[原文 L1669](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1669>)、[原文 L1670](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1670>)、[原文 L1671](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1671>)、[原文 L1672](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1672>)

abandon与最终应用争同锁：Application先成则409并保真；fence先成先封新应用，保持CLEANUP_PENDING，直到全部潜在Owner效果清楚才解保护。租约／TTL到期不证明无效果，解除本Work屏障不解除别的技术／关闭屏障。未来拆出的新Schedule待确认，未改内容的原Schedule可保原合法承诺。 [原文 L939](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:939>)、[原文 L941](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:941>)、[原文 L943](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:943>)

### 4.4 精确取消、Source处置与两个关闭对象

可取消范围按互斥实际分片判定：P为已履约／不可撤／依赖保护并集；I为未决Permit且不与P重叠；H为其他工作持有自由片且不与P/I重叠。maxCancel＝max(0,Q−measure(P∪I∪H))。同ReceiptSlice收40、验40、开40只保护40，不是120。映射／水位不完整时max=null并阻准入；退货／红票不自动释放P。BudgetSegment是每portion六位scale整数[0,Q)区间，不是库存位置或另一PR预算。 [原文 L982](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:982>)、[原文 L986](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:986>)、[原文 L990](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:990>)、[原文 L994](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:994>)、[原文 L996](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:996>)、[原文 L998](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:998>)

Cancellation是创建后不可变提案；CancellationWork是处理；CancellationApplication才是最终商业事实。首次apply202固定提案hash，装CANCEL_DRAIN_BARRIER、自由片Hold、原在途监视并登记具体依赖。Supplier同意／费用／GR截止／Source资格／QA／AP按真实政策列出，N/A也要范围证据；不能把success布尔或任意Ref作为通过。 [原文 L1002](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1002>)、[原文 L1006](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1006>)、[原文 L1008](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1008>)、[原文 L1010](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1010>)

最终共Scope/Source/PO/Guard/Permit/Work/DependencyHead锁，核当前lease、最新事实与截止全路径、准确量、证据当前头，再一次写CancellationApplication(PO,id,1)、QuantityEntry CANCEL、当前有效量／Guard、批准应用、Work.businessOutcome=APPLIED、供给因果和SourceDisposition请求。网络随后，PO不直接返PR额度。Source未确认时商业APPLIED保持，Work仍可DRAINING/UNKNOWN；不能把源慢改成商业未取消。 [原文 L1012](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1012>)、[原文 L1015](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1015>)、[原文 L1018](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1018>)、[原文 L1020](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1020>)、[原文 L1032](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1032>)

SupplierAgreement种子不改提案：后到同意必须精确匹配原hash/scope/数量/条件；新增费用或60改50要新提案审批，不能包成SATISFIED。SourceDisposition键绑定取消Application、SourceUse、Grant身份代次与准确份额，已建PO永远走消费后处置，不能调用未执行Claim release。处置未知只查原键，晚反证带同Application和真实priorDisposition发独立冲突。 [原文 L1024](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1024>)、[原文 L1026](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1026>)、[原文 L1036](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1036>)、[原文 L1038](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1038>)

两种晚到10必须可复现：最终取消前已收10使原取消60上限50，拒原60；完整截止取消60后新Permit10被拒；若更早真实10漏报反证截止，真实Received50、有效Q40、超执行10全部保留，原Cancel60不擦、不把实收截40，Source未发送处置暂停、已发送按原键查并报冲突。 [原文 L1046](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1046>)、[原文 L1047](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1047>)、[原文 L1048](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1048>)

Receiving Close.apply才装接收屏障：未开始许可不得start，已开始仅有据软排空；存在未决I时200 BLOCKED、并未closed。证据齐后由同Closure再次明确apply，完整水位／终态／Owner条件过门才closed。Procurement Closure另需其要求的接收、Source、QA、MATCH/AP、费用等处置，不能关闭财务期间。后继真实事实保留并增closureException，不自动重新开放收货。 [原文 L1054](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1054>)、[原文 L1056](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1056>)、[原文 L1067](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1067>)、[原文 L1069](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1069>)、[原文 L1071](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1071>)

## 5. 接口、准确wire及跨域输入输出

公开根 /api/pur/po/v1，内部根 /internal/pur/po/v1；旧api/pur/po是固定来源旧接口，不冒当前新API已存在。普通写用Idempotency-Key／对应资源token；机器execute、事实与流用原业务身份。所有GET无修复副作用；已声明read POST也不创建Command。 [原文 L140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:140>)、[原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:146>)、[原文 L729](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:729>)、[原文 L10759](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10759>)

### RFQ四原适配入口

| Method / 完整路径 | 输入 → 输出 | 成功/写集合/恢复 | 出处 |
| --- | --- | --- | --- |
| POST `/internal/pur/po/v1/rfq-commercial-capabilities/check` | CommercialCheckRequest → AdapterResult<CommercialCheckResult> | 200能力判断；无PO/Claim，核每项映射及源证据。MappingRef需可重复解析到该输入/adapter版本的不可变记录，独立保存MappingEvidence，不是订单创建 | [原文 L671](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:671>) |
| POST `/internal/pur/po/v1/rfq-groups/execute` | PoExecuteRequest → AdapterResult<ExecutionEvidenceBody> | 200明确结果；若只持久受理则202＋ACCEPTED_PENDING；真实SUCCEEDED必须完整poLines、全部hash/份额映射 | [原文 L672](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:672>) |
| POST `/internal/pur/po/v1/rfq-groups/query` | PoQueryRequest → AdapterResult<ExecutionEvidenceBody> | 200读取原组；无可验证登记时availability=AVAILABLE,data=null,errorCode=PO_CREATION_NOT_OBSERVED，不返回空NO_EFFECT；不能凭一次未观察换组 | [原文 L673](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:673>) |
| POST `/internal/pur/po/v1/rfq-groups/fence` | PoFenceInput → AdapterResult<RevokeResultBody> | 200已裁定或202 PENDING；已真实创建一律TOO_LATE_EXECUTED；没有完整原manifest/全部接收路径证明时UNKNOWN，无伪造空份额fence | [原文 L674](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:674>) |

### 读、准备和选择器

| Method / Path | 能力；请求 → 响应 | 明确效果 | 出处 |
| --- | --- | --- | --- |
| GET `/options` | read或create.prepare；无body→PoOptions | 200当前授权Site/Buyer/政策可用性；不是生产参数默认值 | [原文 L712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:712>) |
| POST `/orders/search` | read；PoSearch→PoPage<PoListRow> | 200有权范围查询；页码1～1000，sort白名单；不得传SQL字段 | [原文 L713](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:713>) |
| GET `/orders/{poId}` | read；无body→PoOrderView | 200默认当前ACTIVE，否则初始创建修订；源/商业敏感过滤 | [原文 L714](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:714>) |
| GET `/orders/{poId}/revisions/{revisionId}` | read；无body→PoRevisionView | 200该订单固定文书，历史不以现字段覆盖 | [原文 L715](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:715>) |
| POST `/preparations` | create.prepare；PoPreparationInput→PoCommandResult | 201 PREPARATION_CREATED；只保存准备，不取得采购授权 | [原文 L716](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:716>) |
| GET `/preparations/{id}` | read；无body→PoPreparationView | 200，返回生成的draftLineId和version | [原文 L717](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:717>) |
| PATCH `/preparations/{id}` | create.prepare；PoPreparationPatch→PoCommandResult | 200 PREPARATION_SAVED；当前DRAFT/BLOCKED/VALIDATED且未建PO，任何内容变更回DRAFT、清校验指针 | [原文 L718](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:718>) |
| POST `/preparations/{id}/validate` | create.prepare；PoMutation→PoPreparationView | 200写一次校验结果/引用/版本，标VALIDATED或BLOCKED；这是命令不是纯预览 | [原文 L719](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:719>) |
| POST `/preparations/{id}/create-order` | create.execute；PoPreparationCreate→PoCommandResult | 201 PO_CREATED，或202 PO_CREATION_PENDING；实际失败/未知按creationKey查询 | [原文 L720](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:720>) |
| GET `/creation-results/{creationKey}` | recover＋原对象/源范围；无body→PoOperationView | 200安全原结果或NOT_OBSERVED错误；无新创建 | [原文 L721](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:721>) |

### 审批、发出、回应与确认

| Method / 外部相对Path | 能力；输入 → 输出 | 结果与下一步 | 出处 |
| --- | --- | --- | --- |
| POST `/approvals` | approval.submit；PoApprovalSubmit→PoApprovalView | REQUIRED 202待OA，明确豁免200；这不是发出订单 | [原文 L772](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:772>) |
| GET `/approvals/{id}` | read＋对象范围；→PoApprovalView | 200当前审批事实与应用结果；没有实例ID不是批准 | [原文 L773](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:773>) |
| POST `/orders/{poId}/revisions/{revisionId}/release` | release＋attachment.disclose；PoRelease→PoCommandResult | 200 PO_RELEASED；生成ACTIVE文书和投递任务，返回REVISION资源 | [原文 L774](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:774>) |
| GET `/orders/{poId}/dispatches` | read；page/pageSize→PoPage<PoDispatchView> | SENT/UNKNOWN/REJECTED逐任务；无副作用 | [原文 L775](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:775>) |
| POST `/dispatches/{id}/retry` | dispatch；PoReasonMutation→PoDispatchView | 原dispatchKey有安全幂等协议才重投；未知不安全渠道409 | [原文 L776](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:776>) |
| POST `/orders/{poId}/acknowledgements` | confirmation.record；PoAckInput→PoAckView | 201原回应留证；不是改变当前PO | [原文 L777](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:777>) |
| GET `/orders/{poId}/acknowledgements` | read；page/pageSize→PoPage<PoAckView> | 所响应修订/原hash/真实收到时点各自显示；每条附当前responseHeads，历史Ack不自动有确认资格 | [原文 L778](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:778>) |
| GET `/orders/{poId}/supplier-response-heads?revisionId={id}` | read＋commercial.read；page/pageSize→PoPage<PoSupplierResponseHead> | 当前规范流头，按ScheduleId稳定排序；纯读不推进或拉取Owner事实 | [原文 L779](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:779>) |
| POST `/orders/{poId}/confirmations` | confirmation.apply；PoConfirm→PoConfirmationView | 201 APPLIED或200已记录的BLOCKED评估；不在冲突时伪造供应确认 | [原文 L780](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:780>) |
| GET `/orders/{poId}/confirmations` | read；page/pageSize→PoPage<PoConfirmationView> | 原确认和后继评估，不随新日历重算覆盖 | [原文 L781](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:781>) |

### 变更与同一工作恢复

| Method / 相对Path | 能力；输入 → 输出 | 状态/效果 | 出处 |
| --- | --- | --- | --- |
| POST `/orders/{poId}/changes` | change.edit；PoChangeInput→PoChangeView | 201新差分，旧ACTIVE仍可用 | [原文 L864](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:864>) |
| GET `/changes/{id}` | read；→PoChangeView | 200原候选、已评估版本/缺口 | [原文 L865](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:865>) |
| PATCH `/changes/{id}` | change.edit；PoChangePatch→PoChangeView | 200只DRAFT；业务改变proposalRevision+1，清旧评估/审批绑定 | [原文 L866](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:866>) |
| POST `/changes/{id}/assess` | change.edit；PoMutation→PoScopeAssessment | 200固定评估快照；不是自动批准或修改旧单 | [原文 L867](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:867>) |
| POST `/changes/{id}/apply` | change.apply；PoApplyChange→PoCommandResult | 首次受理202 PO_CHANGE_APPLY_REQUESTED，持久ChangeWork；已应用新命令200原结果；不在202生成新Revision | [原文 L868](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:868>) |
| GET `/changes/{id}/work` | read/recover＋commercial.read；→PoChangeWorkView | 200工作、精确Hold、Owner依赖及当前结果；无工作404；不顺手续办 | [原文 L869](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:869>) |
| GET `/change-applications/{id}` | read/recover＋commercial.read；→PoChangeApplication | 200不可变最后应用事实；未生成404，不在GET执行apply | [原文 L870](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:870>) |
| POST `/changes/{id}/resume` | recover＋change.apply；PoWorkResume→PoChangeWorkView | 202排队同Work或200工作终态；不改提案/申请hash、不新建Change | [原文 L871](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:871>) |
| POST `/changes/{id}/abandon` | change.edit；PoReasonMutation→PoCommandResult | 无Work/无效果200弃稿；已有Work转202 PO_CHANGE_ABANDON_REQUESTED，须封最终应用并清理同Work的远端未知；已应用409 | [原文 L872](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:872>) |

### 取消与关闭

| Method / 外部相对Path | 能力；请求 → 输出 | 确切含义 | 出处 |
| --- | --- | --- | --- |
| POST `/orders/{poId}/cancellations` | cancel；PoCancelInput→PoCancellationView | 201 DRAFT，固定原申请量/范围；无源返额 | [原文 L965](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:965>) |
| GET `/cancellations/{id}` | read/recover；→PoCancellationView | 200申请、审批、截止、最终商业结果、Source结果分开；workId/evidenceSetRef可继续查 | [原文 L966](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:966>) |
| GET `/cancellations/{id}/work` | read/recover＋commercial.read；→PoCancellationWorkView | 200真实Work与依赖列表，无工作404；纯读 | [原文 L967](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:967>) |
| GET `/dependency-evidence-sets/{id}` | read/recover＋commercial.read；→PoDependencyEvidenceSet | 200原证据选择快照；成员仍可在关联Work依赖历史中查证 | [原文 L968](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:968>) |
| GET `/cancellation-applications/{id}` | read/recover＋commercial.read；→PoCancellationApplication | 200不可变最终商业事实；Ref指此资源，不指提案或临时串 | [原文 L969](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:969>) |
| POST `/cancellations/{id}/assess` | cancel；PoMutation→PoScopeAssessment | 200带水位的评估；不预留/不取消 | [原文 L970](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:970>) |
| POST `/cancellations/{id}/apply` | cancel；PoCancellationApply→PoCancellationView | 202 REQUESTED/DRAINING或200既有终态；不能把202翻译取消成功 | [原文 L971](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:971>) |
| POST `/cancellations/{id}/resume` | recover＋cancel；PoReasonMutation→PoCancellationView | expectedVersion针对Cancellation；排队既有Work，商业APPLIED后只续原Source处置；已结清仅回原结果 | [原文 L972](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:972>) |
| POST `/cancellations/{id}/withdraw-unapplied` | cancel；PoReasonMutation→PoCancellationView | 仅最终商业变更尚未发生且可证无远端取消效力时封闭提案并解除本地Hold；否则UNKNOWN，不取消已经成立的结果 | [原文 L973](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:973>) |
| POST `/orders/{poId}/closures` | close；PoCloseInput→PoCloseView | 201关闭提案；缺证BLOCKED，不执行财务关闭 | [原文 L974](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:974>) |
| POST `/closures/{id}/apply` | close；PoApplyClose→PoCloseView | 200满足独立关闭对象，返回CLOSED；不自动删除Pending源/质量/费用 | [原文 L975](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:975>) |
| GET `/closures/{id}` | read；→PoCloseView | 200历史提案和当前缺项 | [原文 L976](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:976>) |

### 事实接收与投影对账

| Method / 完整或相对Path | 请求→响应；能力 | 实际写责任 | 出处 |
| --- | --- | --- | --- |
| GET `/orders/{poId}/fulfillment` | PoFulfillmentView；read | 只读当前投影，no auto-repair | [原文 L1080](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1080>) |
| GET `/orders/{poId}/facts` | page/pageSize/kind?→PoPage<PoFulfillmentFact>；history.read | 读取当前有权原凭证/历史版；敏感金额过滤，不跨租户 | [原文 L1081](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1081>) |
| POST `/orders/{poId}/reconciliation` | PoReasonMutation→PoOperationView；recover | 登记从原事实重建投影任务；不请求重收货/重开票 | [原文 L1082](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1082>) |
| POST `/internal/pur/po/v1/facts/receipt` | PoFulfillmentFact(kind RECEIPT)→PoFactAck；ingest.receipt | PUR-GR已确认商业实收＋WMS引用，只更新PO自己的Receipt投影 | [原文 L1083](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1083>) |
| POST `/internal/pur/po/v1/facts/acceptance` | kind ACCEPTANCE→PoFactAck；ingest.acceptance | 指定PUR-GR验收生产者，引用Quality判定或有据免检；不直接改QA | [原文 L1084](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1084>) |
| POST `/internal/pur/po/v1/facts/invoice` | kind INVOICE/INVOICE_CREDIT→PoFactAck；ingest.invoice | Finance.AP的已POSTED分配/信用凭证；不以待匹配或AP草稿计量 | [原文 L1085](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1085>) |
| POST `/internal/pur/po/v1/facts/return` | kind RETURN→PoFactAck；ingest.return | 真实物理退回Owner证据/库存Movement；不因信用票据直接推断退货 | [原文 L1086](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1086>) |

### 必须真实采用的Owner契约

| 生产者 / 合同或接收路径 | 输入 → 输出 | 本域消费与故障出口 | 出处 |
| --- | --- | --- | --- |
| IAM `POST /purchase-order-access/resolve` | PoAccessRequest→AdapterResult<PoAccess> | 每次读写/下载；实际用户、Site/Buyer、字段权。失败禁敏感披露，不返回旧缓存价 | [原文 L1142](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1142>) |
| Source `POST /purchase-order-sources/resolve` | PoResolvePreparation→AdapterResult<PoPreparationResolution> | 非RFQ准备的准确来源/商业许可，不能仅凭manual标签跳过 | [原文 L1143](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1143>) |
| Source `POST /purchase-claims/prepare` | 原样ClaimPrepareRequest→AdapterResult<ClaimBundle> | 创建之前必须全部保护；不能给缺源对象伪造Grant | [原文 L1144](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1144>) |
| Source `POST /purchase-claims/release` | 原样ClaimReleaseRequest→AdapterResult<ClaimReleaseResult> | 仅PO创建未发生且全路径fence成立后的准备清理；不能代替已建PO取消处置 | [原文 L1145](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1145>) |
| Source 同库 `ConsumeClaimInTransaction` | PoSourceUseInput＋共享DbTransaction→PoSourceUseResult | 最终原子创建；绑定真实Effect，禁止二次消费，不通过HTTP在事务里假串行 | [原文 L1146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1146>) |
| Source `POST /purchase-order-consumption-dispositions` | PoSourceDispositionRequest→AdapterResult<PoSourceDispositionResult> | 已创建PO取消后的准确处置，不是未执行Claim释放；超时查同operationKey | [原文 L1147](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1147>) |
| Source `POST /purchase-order-disposition-conflicts` | PoDispositionConflict→AdapterResult<PoDispositionConflictResult> | 取消后真实晚到事实；Source自己的隔离/差异回执，不由PO返额/扣额 | [原文 L1148](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1148>) |
| GR `POST /purchase-receipt-cutoffs` | PoCutoffRequest→AdapterResult<PoCutoffResult> | 在途/全路径/水位证据；UNKNOWN不释放Hold或源保护 | [原文 L1149](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1149>) |
| GR/PO共库最终受理守卫 | PoReceivingCommitInput＋同DbTransaction→PoReceivingPermit | 在GR/WMS真实写入事务里消费许可并关联原ReceiptOperation；不直接由PO创建Receipt | [原文 L1150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1150>) |
| BP/工程 `POST /purchase-order-eligibility/check` | PoEligibilityRequest→AdapterResult<PoEligibility> | 具体CREATE/RELEASE/CONFIRM/CHANGE/RECEIVE活动，不索取未来QC来建PO | [原文 L1151](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1151>) |
| 商业/日历 `POST /delivery-calendars/resolve` | 既有准确CalendarRef→AdapterResult<DeliveryCalendarSnapshot> | 原版本，缺覆盖阻相对日期计算，不改原协议 | [原文 L1152](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1152>) |
| PO审批策略 `POST /purchase-order-approval-policies/evaluate` | PoApprovalEvaluationInput→AdapterResult<PoApprovalEvaluation> | PO域四类Subject，不偷换RFQ DTO枚举 | [原文 L1153](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1153>) |
| OA `POST /purchase-order-approval-instances` | PoApprovalInstanceInput→AdapterResult<PoApprovalInstanceOutput> | submissionId幂等，hash/修订精确；结果只变审批事实 | [原文 L1154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1154>) |
| Supplier证据 `POST /purchase-order-acknowledgements/verify` | PoSupplierEvidenceRequest→AdapterResult<PoSupplierEvidence> | 原相应内容/身份/真实接受时点；不是证明邮件SENT | [原文 L1155](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1155>) |
| 确认策略 `POST /purchase-order-confirmation-policies/evaluate` | PoConfirmationPolicyInput→AdapterResult<PoConfirmationPolicy> | 需求硬门/允许Schedule/锚点语义；缺值不默认自动确认 | [原文 L1156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1156>) |
| 取消/关闭策略 `POST /purchase-order-disposition-policies/evaluate` | PoDispositionPolicyInput→AdapterResult<PoDispositionPolicy> | Supplier同意、费用/Source/Finance等必需证据精确列表，空配置不豁免 | [原文 L1157](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1157>) |
| GR / QA / AP事实补拉 `POST /purchase-order-fact-sets/query` | PoFactSetQuery→AdapterResult<PoFactSet> | 完整identity清单、水位、每版原凭证；用于gap/重算而非造业务记录 | [原文 L1158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1158>) |
| 投递 `POST /purchase-order-dispatches/send`及`/query` | PoDispatchInput / PoDispatchQuery→AdapterResult<PoDispatchResult> | send必须原dispatchKey幂等或明确不安全；UNKNOWN先query，不以SENT认定供应商承诺 | [原文 L1159](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1159>) |
| Planning `POST /purchase-supply-observations` | PoSupplyProjection→PoProjectionAck | 完整PO级集合，Consumer据谱系替代；Pending不否定PO商业事实 | [原文 L1160](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1160>) |
| PLM `POST /engineering-feedback` | PoFeedbackEnvelope→PoProjectionAck | SourceFact固定版本/反馈键，回FeedbackId或明确拒绝；收到不等改善 | [原文 L1161](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1161>) |
| PLM `POST /engineering-target-receipts` | PoEngineeringReceipt→PoProjectionAck | 接受/就绪/采用独立；真实Owner执行完才APPLIED | [原文 L1162](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1162>) |

### ReceivingPermit受控入口

| Method / Path | 能力；输入→输出 | 成功/失败与写集合 | 出处 |
| --- | --- | --- | --- |
| POST `/internal/pur/po/v1/receiving-permits` | receiving.guard；PoReceivingRequest→PoReceivingPermit | 201新HELD_NOT_STARTED／200原业务；守卫、预算分片、Permit、审计；不写实收 | [原文 L1201](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1201>) |
| GET `/internal/pur/po/v1/receiving-permits/{id}` | receiving.guard＋所属Owner范围；→PoReceivingPermit | 200原许可与当前向量；无副作用 | [原文 L1202](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1202>) |
| POST `/internal/pur/po/v1/receiving-permits/{id}/start` | receiving.guard；PoReceivingStart→PoReceivingPermit | 200 IN_FLIGHT；GR原ReceiptIntent与PO开始记录同事务；仅机器，不能人类自造ownerStartIntentRef | [原文 L1203](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1203>) |
| POST `/internal/pur/po/v1/receiving-permits/{id}/revalidate` | receiving.guard；PoPermitRevalidate→PoReceivingPermit | 200持久复核／409仍阻断；不重分片、不生成新start、不执行WMS | [原文 L1204](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1204>) |
| POST `/internal/pur/po/v1/receiving-permits/{id}/close-no-effect` | receiving.guard；PoNoReceiptProof→PoReceivingPermit | 全路径fence/无效果已证才200 NO_EFFECT_CLOSED；未知202原保护；已用返回原USED，不删实物 | [原文 L1205](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1205>) |

### 工程影响和逐项执行

| Method / 内部或外部Path | 请求→响应 | 必须的判断 | 出处 |
| --- | --- | --- | --- |
| POST `/internal/pur/po/v1/engineering/impact-requests` | PoImpactRequest→PoPage<PoImpactEvaluation> | 固定该request.eventId的本次snapshot，首次返回第1页20项；不是无限动态游标混版 | [原文 L1267](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1267>) |
| GET `/internal/pur/po/v1/engineering/impact-requests/{requestEventId}/items` | page/pageSize→PoPage<PoImpactEvaluation> | 按原请求已保存同一snapshot分页，机器scope复核；不重新扫描混入后来的PO | [原文 L1268](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1268>) |
| GET `/orders/{poId}/engineering-impacts` | page/pageSize→PoPage<PoImpactEvaluation> | PO自身评估/处置，供前端显示缺项/过时 | [原文 L1269](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1269>) |
| POST `/engineering-impacts/{id}/evaluate` | PoReasonMutation→PoImpactEvaluation | 当前版本/履约/接收保护重新评估，生成新的captureThrough，旧评估留存 | [原文 L1270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1270>) |
| POST `/engineering-impacts/{id}/execute` | PoImpactAction→PoOperationView | 202启动或200原终态；必须批准ECO、准确数量/Source/当前评估及Owner动作权限 | [原文 L1271](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1271>) |
| GET `/operations/{id}` | →PoOperationView | 查原ExecutionKey，不重复执行Change/Cancel | [原文 L1272](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1272>) |

### 动态Owner依赖与结果

| Producer契约路径（在配置Owner地址下，非用户URL） | 固定请求/结果 | 业务键和意义 | 出处 |
| --- | --- | --- | --- |
| SUPPLIER `/purchase-order-agreements/evaluate`、`/query` | PoDependencyRequest / PoDependencyQuery → AdapterResult<PoDependencyResultInput> | SUPPLIER_AGREEMENT；当前提案取消/变更的准确同意与后继撤回；不是普通Schedule接受头 | [原文 L1298](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1298>) |
| 商业费用Owner `/purchase-order-fee-dispositions/evaluate`、`/query` | 同上 | FEE_DISPOSITION；当前条件下义务已确定/不适用；新增条款不能伪SATISFIED | [原文 L1299](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1299>) |
| GR `/purchase-order-cutoffs/evaluate`、`/query` | 同上；内部使用PoCutoffRequest或PoChangeCutoffRequest | GR_CUTOFF；返回对应cutoff载荷，不能把ChangeId假写到cancellationId | [原文 L1300](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1300>) |
| SOURCE `/purchase-order-source-eligibility/evaluate`、`/query` | 同上 | SOURCE_ELIGIBILITY；提交取消/变更前处置可行性，不是已经返额 | [原文 L1301](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1301>) |
| SOURCE `/purchase-order-change-claims/prepare`、`/query` | 同上 | SOURCE_CLAIM；请求claimPlan明确新增授权Grant/Work/执行键/Scope，claimBundle以同executionKey回原准备；不要求未生成PO行ID，不另取旧Grant | [原文 L1302](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1302>) |
| QUALITY `/purchase-order-clearances/evaluate`、`/query` | 同上 | QUALITY_CLEARANCE；本范围后继义务是否阻止处置；不生成检验合格 | [原文 L1303](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1303>) |
| AP `/purchase-order-clearances/evaluate`、`/query` | 同上 | AP_CLEARANCE；本范围已存在/未决票据义务是否已满足处置条件；不是开票/过账入口 | [原文 L1304](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1304>) |
| 既有SOURCE `/purchase-order-consumption-dispositions`及`/query` | PoSourceDispositionRequest / PoDependencyQuery → PoSourceDispositionResult，由采用适配器包装成PoDependencyResultInput | SOURCE_DISPOSITION；只在取消Application存在后发送；operationKey原值保留 | [原文 L1305](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1305>) |
| 对应原Owner `/purchase-order-operations/close-no-effect`及`/query` | PoDependencyRequest / PoDependencyQuery → AdapterResult<PoDependencyResultInput> | NO_EFFECT；覆盖原DependencyRequest operationKey及全部可能路径，未知不能宣称无效果 | [原文 L1306](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1306>) |
| PO内部 `/internal/pur/po/v1/dependency-results` | PoDependencyResultInput → PoDependencyAck | 认证Producer＋与dependencyKind匹配的专用能力；人类Bearer拒绝。200已记录，202待前驱，409内容/身份冲突 | [原文 L1307](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1307>) |

### 命令、历史、文件和导出

| Method / 相对Path | 输入→输出 | 能力/边界 | 出处 |
| --- | --- | --- | --- |
| GET `/commands/{commandId}` | →PoCommandObservation | recover；本人或明确授权恢复者；未观察不等未执行 | [原文 L1547](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1547>) |
| POST `/commands/{commandId}/fence-unobserved` | PoCommandFence→PoCommandObservation | recover；只未发生本地保存/申请登记，不取消已发远端效果 | [原文 L1548](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1548>) |
| GET `/orders/{poId}/history` | page/pageSize→PoPage<PoHistoryRow> | history.read，按该PO TimelineSeq DESC；total/items同短快照 | [原文 L1549](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1549>) |
| POST `/exports` | PoExportInput→PoExportView | export，202排队，includeCommercial需commercial.read及导出许可 | [原文 L1550](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1550>) |
| GET `/exports/{id}` | →PoExportView | 原任务权，查询无生成副作用 | [原文 L1551](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1551>) |
| GET `/exports/{id}/content` | CSV流 | export＋当前范围/字段资格；未Ready409、失权403、过期410 | [原文 L1552](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1552>) |
| GET `/orders/{poId}/attachments` | →PoAttachmentView[] | attachment.read＋源文件范围，只返回已关联且当前可见项 | [原文 L1553](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1553>) |
| POST `/download-tickets` | PoDownloadInput→PoCommandResult | attachment.download，201；resource.kind=DOWNLOAD | [原文 L1554](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1554>) |
| GET `/download-tickets/{id}/content` | 原文件流 | 再核对象/文件当前权，非公开redirect | [原文 L1555](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1555>) |

### 准确RFQ v1.1.1全量类型镜像

[原文 L463](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:463>)

```ts
interface ClaimPrepareRequest { canonicalGroupKey:Id; manifestHash:string; executionGeneration:Seq; grants:GrantShare[]; controlUseRef:Ref; }

interface ClaimReleaseRequest { bundleRef:Ref; fence:FenceProof; reason:string; }

interface ClaimReleaseResult { claims:ClaimRecord[]; sourceEvidenceRefs:Ref[]; complete:boolean; }

interface FieldError { field: string; code: string; message: string; }

interface Ref { owner: Key; id: Key; version: Key; }

interface SourceResponsibility {
  owner: Key; sourceType: 'PR' | 'AUTHORIZED_PURCHASE'; sourceId: Key;
  sourceLineId: Key; authorizationScopeId: Key;
}

interface TechnicalBasis {
  itemKey: Key; revisionRef: Ref | null; purpose: string; siteKey: Key;
  result: 'APPLICABLE' | 'NOT_APPLICABLE_WITH_BASIS' | 'UNKNOWN' | 'BLOCKED';
  evidenceRef: Ref | null;
}

interface SourceLink {
  linkId: Id | null; responsibility: SourceResponsibility; sourceBusinessVersion: Key;
  quantity: Qty; uomCode: string; observationRef: Ref;
}

interface Validity {
  kind: 'END_AT' | 'NO_END' | 'UNKNOWN'; from: Instant | null; to: Instant | null;
  sourceDeclarationRef: Ref | null; applicablePolicyRef: Ref | null;
}

interface Tier {
  tierId: Id | null; lower: Qty; upper: Qty | null; unitPrice: UnitPrice | null;
}

interface Charge {
  chargeId: Id | null; economicTermKey: Key; kind: string;
  knownness: 'KNOWN' | 'INCLUDED' | 'NOT_APPLICABLE_WITH_BASIS' | 'UNKNOWN';
  amount: Amount | null; currencyCode: string | null;
  scope: 'LINE' | 'GROUP'; occurrenceKey: Key | null;
  includedInComponentKey: Key | null; sourceRef: Ref | null;
}

interface TaxTerms {
  mode: 'INCLUDED' | 'EXCLUDED' | 'EXEMPT_WITH_BASIS' | 'UNKNOWN';
  taxCode: string | null; policyRef: Ref | null;
}

interface DeliveryTerms {
  leadDays: number | null; leadDayKind: 'WORKING_DAY' | 'CALENDAR_DAY' | 'UNKNOWN' | null;
  promisedDate: DateText | null; jointInterpretation: 'SAME_DATE' | 'UNRESOLVED' | null;
  startBasis: 'PO_CONFIRMED' | 'SPECIFIED_DATE' | 'UNKNOWN';
  specifiedStart: DateText | null; calendarRef: Ref | null;
  freightResponsibility: 'SUPPLIER' | 'BUYER' | 'UNKNOWN';
}

interface CommercialTerms {
  billingMode: 'ALL_UNITS' | 'PROGRESSIVE' | 'UNRESOLVED';
  quantityBasis: 'PER_ALLOCATION' | 'INDEPENDENT_GROUP' | 'CONTRACT_CUMULATIVE' | 'CROSS_GROUP';
  currencyCode: string | null; quoteUom: string | null; thresholdUom: string | null;
  priceBasisQty: Qty | null; tiers: Tier[]; minimumOrderQty: Qty | null; orderMultiple: Qty | null;
  tax: TaxTerms; charges: Charge[]; delivery: DeliveryTerms; validity: Validity;
  zeroPriceEvidenceRef: Ref | null; termsEvidenceRef: Ref | null;
}

interface AllocationInput {
  allocationId: Id | null; rfqLineId: Id; quoteVersionId: Id; quoteLineId: Id;
  supplierKey: Key; qty: Qty; uomCode: string; sourceShareGrantRefs: Ref[];
}

interface SavedAllocation extends Omit<AllocationInput,'allocationId'> { allocationId: Id; }

interface GrantShare {
  grantRef: Ref; responsibility: SourceResponsibility; sourceBusinessVersion: Key;
  quantity: Qty; sourceUom: string; generation: Seq;
}

interface ManifestRow {
  rowId: Id; allocationId: Id; rfqLineId: Id; supplierKey: Key; itemKey: Key;
  technicalBasis: TechnicalBasis; siteKey: Key; quoteVersionId: Id; quoteLineId: Id;
  sourceShares: GrantShare[]; quantity: Qty; uomCode: string;
  commercialSourceHash: string; targetPayloadHash: string; mappingRef: Ref;
}

interface GroupManifest {
  groupId: Id; canonicalGroupKey: Id; businessIdentityHash: string; manifestHash: string;
  executionMode: 'ATOMIC_MANIFEST'; supplierKey: Key; scopeAtomKeys: Key[];
  rows: ManifestRow[]; commercialPayload: TargetCommercialPayload;
}

interface RequiredDelivery {
  requiredDate:DateText|null; constraint:'NONE'|'REQUESTED'|'HARD'|'UNKNOWN';
  destinationKey:Key; requirementEvidenceRefs:Ref[];
}

interface DeliveryResolution {
  mode:'FIXED_DATE'|'RELATIVE_LEAD_DAYS'|'COMBINED'|'UNKNOWN';
  anchorStatus:'KNOWN'|'PENDING_PO_CONFIRMED'|'NOT_APPLICABLE'|'UNKNOWN';
  anchorDate:DateText|null; anchorEvidenceRef:Ref|null; resolvedDate:DateText|null;
  calendarSnapshotRef:Ref|null; calendarRuleHash:string|null; calculationEvidenceRef:Ref|null;
  requiredDateCheck:'NOT_REQUIRED'|'MEETS'|'LATE'|'PENDING_ANCHOR'|'UNKNOWN';
  combinedCheck:'NOT_APPLICABLE'|'AGREES'|'PENDING_ANCHOR'|'CONFLICT'|'UNRESOLVED';
  admission:'ALLOWED'|'BLOCKED'; reasonCodes:string[];
}

interface TargetLine {
  manifestRowId:Id; itemKey:Key; quantity:Qty; uomCode:string; settlementCurrency:string;
  billingMode:'ALL_UNITS'|'PROGRESSIVE'; quoteUom:string; priceBasisQty:Qty;
  appliedTiers:{lower:Qty;upper:Qty|null;unitPrice:UnitPrice;appliedQty:Qty}[];
  goodsAmount:Amount; tax:TaxTerms; roundingEvidenceRefs:Ref[]; sourceTermsRef:Ref;
  sourceShareRefs:Ref[]; requiredDelivery:RequiredDelivery;
  supplierDelivery:DeliveryTerms; deliveryResolution:DeliveryResolution;
}

interface TargetCharge {
  occurrenceKey:Key; economicTermKey:Key; currencyCode:string; amount:Amount;
  representation:'HEADER_CANONICAL'|'LINE_ALLOCATIONS_CANONICAL';
  allocations:{manifestRowId:Id;amount:Amount}[]; includedInComponentKey:Key|null; sourceRef:Ref;
}

interface TargetCommercialPayload {
  supplierKey:Key; siteKey:Key; destinationKey:Key; settlementCurrency:string; paymentTermsRef:Ref;
  pricingScopeRef:Ref; lines:TargetLine[]; charges:TargetCharge[];
  sourceHash:string; payloadHash:string; adapterContractRef:Ref;
}

interface MappingItem { sourcePath:string; targetPath:string; disposition:'PRESERVED'|'EXPLICIT_TRANSFORMATION'|'BLOCKED'; sourceRef:Ref; transformationPolicyRef:Ref|null; reasonCode:string|null; }

interface CommercialCheckRequest {
  basisCandidateKey:Key; selectionApplicationRef:Ref;
  rows:{rfqRevisionId:Id;allocation:SavedAllocation;terms:CommercialTerms;
    requiredDelivery:RequiredDelivery;sourceShares:GrantShare[]}[];
  adapterContractRef:Ref;
}

interface CommercialCheckResult { supported:boolean; mappingRef:Ref; mapping:MappingItem[]; targetPayload:TargetCommercialPayload|null; unsupportedFields:string[]; }

interface ClaimRecord { claimRef:Ref; grantRef:Ref; canonicalGroupKey:Id; qty:Qty; uomCode:string; state:'PROTECTED'|'CONSUMED'|'RELEASED'|'UNKNOWN'; generation:Seq; }

interface ClaimBundle { bundleRef:Ref; canonicalGroupKey:Id; manifestHash:string; state:'ALL_PROTECTED'|'PARTIAL'|'REJECTED'|'UNKNOWN'; claims:ClaimRecord[]; errors:FieldError[]; }

interface FenceProof { ref:Ref; canonicalGroupKey:Id; manifestHash:string; generation:Seq; terminal:'REJECTED'|'CANCELLED_NO_EFFECT'; shareGrantRefs:Ref[]; coveredPaths:Key[]; closedThrough:Key; }

interface PoExecuteRequest { canonicalGroupKey:Id; manifestHash:string; generation:Seq; manifest:GroupManifest; claims:ClaimBundle; controlUseRef:Ref; }

interface PoQueryRequest { canonicalGroupKey:Id; manifestHash:string; }

interface RevokeResultBody { canonicalGroupKey:Id; manifestHash:string; generation:Seq; outcome:'NO_EFFECT'|'TOO_LATE_EXECUTED'|'PENDING'|'UNKNOWN'; fence:FenceProof|null; executionEvidenceRef:Ref|null; }

interface PoLineResult { manifestRowId: Id; effectId: Key; poId: Key; poNo: Key; poLineId: Key; quantity: Qty; uomCode: string; sourceShareGrantRefs: Ref[]; sourceShareQuantities:{grantRef:Ref;quantity:Qty;uomCode:string}[]; commercialPayloadHash: string; deliverySnapshotHash:string; deliveryEvidenceRef:Ref; }

interface ExecutionEvidenceBody {
  groupId:Id; canonicalGroupKey:Id; manifestHash:string; generation:Seq;
  outcome:'ACCEPTED_PENDING'|'SUCCEEDED'|'REJECTED'|'CANCELLED_NO_EFFECT'|'UNKNOWN';
  terminal:boolean; completeForManifest:boolean; poLines:PoLineResult[];
  possibleEffects:{manifestRowId:Id;grantRefs:Ref[];quantity:Qty;uomCode:string}[];
  fence:FenceProof|null; commercialPayloadHash:string; authorityRef:Ref;
}

interface EvidenceHeader {
  eventId:Id; producerOwner:Key; contractRef:Ref; streamId:Key; epoch:Key; version:Seq;
  occurredAt:Instant; evidenceMode:'REAL'|'DEMO';
}

interface ExecutionEvidenceEnvelope extends EvidenceHeader { type:'PO_EXECUTION'; body:ExecutionEvidenceBody; }

interface EvidenceAck {
  eventId:Id; outcome:'APPLIED'|'DUPLICATE'|'HISTORICAL'|'PENDING_PREDECESSOR'|'QUARANTINED';
  observedVersion:Seq|null; errorCode:string|null; recordedAt:Instant;
}

interface EvidenceMeta { contractRef:Ref; evidenceRef:Ref; evidenceMode:'REAL'|'DEMO'; asOf:Instant; }

interface AdapterResult<T> { availability:Availability; data:T|null; errorCode:string|null; meta:EvidenceMeta|null; }

interface DeliveryCalendarSnapshot {
  sourceRef:Ref; snapshotRef:Ref; ruleHash:string; timeZoneId:Key;
  countRule:'EXCLUDE_ANCHOR_COUNT_VALID_DAYS'; zeroDaysRule:'ANCHOR_DATE';
  coveredFrom:DateText; coveredTo:DateText;
  workingWeekdays:number[]; closedDates:DateText[]; extraWorkingDates:DateText[];
}
```

| 上游字段/事实 | PO当前保存位置与使用 | 不能替代 | 出处 |
| --- | --- | --- | --- |
| CanonicalGroupKey / manifestHash / generation | IntakeRegistration唯一键、原请求摘要、ExecutionReceipt | Idempotency-Key不是新的采购业务身份 | [原文 L651](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:651>) |
| ManifestRow.rowId / allocationId / quoteVersionId / quoteLineId | IntakeLineMap、不可变原来源JSON | PO生成LineId后显式映射；不拿数组下标/物料名代关联 | [原文 L652](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:652>) |
| GrantShare.responsibility、grantRef/generation、quantity/sourceUom | SourcePortion、SourceUse；按Owner/责任/代次核使用 | RFQ询价量不是授权余额；Grant不等Claim | [原文 L653](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:653>) |
| TargetCommercialPayload | IntakeSnapshot原字节语义规范JSON＋Hash；当前PoRevision另存本域文书 | Supplier默认币、当前价表、AP本位币不能改原币 | [原文 L654](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:654>) |
| TargetLine.goodsAmount/appliedTiers/billingMode | 原币商品额、整量/累进各段、单位基数及舍入证据 | 不能用平均单价或以总额相近宣称语义等价 | [原文 L655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:655>) |
| TargetCharge occurrence/representation | ChargeSnapshot一份canonical；HEADER摘要与行分配不再相加 | 不创建Finance费用义务或共享跨组新政策 | [原文 L656](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:656>) |
| RequiredDelivery、SupplierDelivery、DeliveryResolution | 原DeliverySnapshotJson/Hash、CalendarEvidence固定版本 | PO创建时不填写尚未发生的确认日期 | [原文 L657](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:657>) |
| ControlUseRef / 当前ScopeGuard / ClaimBundle | 来源和Decision所属服务在最终同库事务内验证 | 只看一个长期token或过期快照不足以放行 | [原文 L658](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:658>) |
| 真实PoLineResult | PO创建事务生成EffectId、poId/poNo/poLineId、逐Grant数量、原商业hash和DeliveryEvidenceRef | 成功回执不表示已批准、已送达供应商、已确认或已经收货 | [原文 L659](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:659>) |

### PO Owner、事实补拉、来源使用与供给契约

[原文 L1164](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1164>)

```ts
interface PoDispatchInput { dispatchId:Id; dispatchKey:Key; poId:Id; revisionId:Id; recipientKey:Key; contentHash:string; attachmentRefs:Ref[]; disclosureEvidenceRef:Ref; }
interface PoDispatchQuery { dispatchKey:Key; }
interface PoDispatchResult { dispatchKey:Key; state:'SENT'|'REJECTED'|'UNKNOWN'; evidenceRef:Ref|null; occurredAt:Instant; reasons:string[]; }
interface PoAccessRequest { activity:string; poId:Id|null; siteKey:Key|null; }
interface PoAccess { tenantId:Id; actorKey:Key; siteKeys:Key[]; buyerKeys:Key[]; scope:'OWN'|'BUYER_SET'|'AUTHORIZED_TENANT'; capabilities:string[]; commercialFields:string[]; authorizationRevision:Key; checkedAt:Instant; expiresAt:Instant; }
interface PoEligibilityRequest { poId:Id|null; supplierKey:Key; siteKey:Key; activity:'CREATE'|'RELEASE'|'CONFIRM'|'CHANGE'|'RECEIVE'; lines:{itemKey:Key;technicalBasis:TechnicalBasis;scopeRefs:Ref[]}[]; at:Instant; }
interface PoEligibility { allowed:boolean; controlRefs:Ref[]; proofRefs:Ref[]; missing:string[]; expiresAt:Instant|null; }
interface PoSourceUseEvent { eventId:Id; producerOwner:Key; creationKey:Id; executionEffectId:Key; grantRef:Ref; generation:Seq; state:'EXECUTION_BOUND'|'LEDGER_CONFIRMED'|'CONFLICT'; ownerUseRef:Ref; currentSourceObservationRef:Ref|null; sourceVersion:Seq; observedAt:Instant; evidenceMode:EvidenceMode; }
interface PoSourceUseInput { creationKey:Id; manifestHash:string; generation:Seq; uses:{grantRef:Ref;quantity:Qty;sourceUom:string;executionEffectId:Key}[]; controlUseRef:Ref; }
interface PoSourceUseResult { applied:boolean; sourceUseRefs:Ref[]; missing:string[]; }
interface PoDispositionConflict { operationKey:Key; cancellationRef:Ref; priorDispositionRefs:Ref[]; lateFactRef:Ref; affectedPortions:{sourcePortionId:Id;grantRef:Ref;quantity:Qty;uomCode:string}[]; reasonCode:string; }
interface PoDispositionConflictResult { operationKey:Key; state:'RECORDED'|'PROTECTED_BY_OWNER'|'PENDING'|'REJECTED'; ownerReceiptRef:Ref|null; missing:string[]; }
interface PoReceivingCommitInput { permitId:Id; receiptOperationKey:Key; expectedPermitVersion:Version; expectedGuardRevision:Seq; expectedGuards:PoReceivingGuardVector[]; startRef:Ref; scopeHash:string; receiptIdentity:PoFactIdentity; receiptSliceRefs:Ref[]; quantities:{sourcePortionId:Id;quantity:Qty;uomCode:string}[]; }
interface PoConfirmationPolicyInput { poId:Id; revisionId:Id; contentHash:string; acknowledgementRef:Ref; scheduleIds:Id[]; policyRef:Ref; }
interface PoDispositionPolicyInput { poId:Id; kind:'CANCEL'|'RECEIVING_CLOSE'|'PROCUREMENT_CLOSE'; contentHash:string; scopeRefs:Ref[]; policyRef:Ref; }
interface PoDispositionPolicy { ref:Ref; allowed:boolean; requiredEvidenceKinds:string[]; supplierAgreementRequired:boolean; feeDispositionRequired:boolean; evidenceRefs:Ref[]; missing:string[]; }
interface PoFactSetQuery { poId:Id; kind:PoFactKind; throughToken:Key|null; cursor:Key|null; }
interface PoFactSet { snapshotRef:Ref; poId:Id; kind:PoFactKind; throughToken:Key; complete:boolean; facts:PoFulfillmentFact[]; identities:PoFactIdentity[]; nextCursor:Key|null; recordedAt:Instant; }
interface PoProjectionAck { eventId:Id; result:'APPLIED'|'DUPLICATE'|'PENDING'|'REJECTED'; receiptRef:Ref|null; observedVersion:Seq|null; reasonCodes:string[]; }
interface PoFeedbackEnvelope { eventId:Id; feedbackKey:Key; poId:Id; input:PoFeedbackInput; recordedAt:Instant; evidenceMode:EvidenceMode; }
```

### 动态依赖查询、结果确认与Head向量

[原文 L1289](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1289>)

```ts
type PoDependencyResultInput = Omit<PoDependencyResult,'id'>;
interface PoDependencyQuery { dependencyRequestId:Id; operationKey:Key; subjectContentHash:string; scopeHash:string; knownResultRef:Ref|null; }
interface PoDependencyAck { dependencyRequestId:Id; resultRef:Ref|null; localResultId:Id|null; headVersion:Seq; state:PoDependencyView['state']; duplicate:boolean; errorCode:string|null; }
interface PoDependencyHeadVector { dependencyRequestId:Id; expectedHeadVersion:Seq; expectedResultRef:Ref; expectedSemanticHash:string; }
```

依赖 operationKey＝PODEP-＋H(Tenant,WorkKind,SubjectId,ProposalRevision,SubjectHash,ScopeHash,Kind,Producer,Action,输入身份)。请求落库后不改正文；不含lease/attempt，不预造未来PO结果ID。不同kind是明确判别联合：GR用对应workKind cutoff，Claim只claimBundle，SourceDisposition只原处置载荷，其余decisionPayload，其他分支必须null。外层结果与载荷真实含义一致，缺证PENDING／UNKNOWN。 [原文 L1311](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1311>)、[原文 L1313](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1313>)

每DependencyRequest的Owner业务结果从1/null前驱起连续；运输新event不增业务版。同Ref或同版同义去重，缺链PENDING，分叉／异义CONFLICT；原Owner无序列必须提供受信supersedes，由已采用适配Owner维护可验证链，PO不能自编。最终先取得当前声明，并在同库Head锁核当前性／撤回和有效期；只有旧缓存不得给新效果生产放行。 [原文 L1315](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1315>)、[原文 L1317](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1317>)、[原文 L1321](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1321>)

cutoff的expectedGuardRevision是已安装屏障的固定起点，不要求排空后全PO计数不变。Owner证明该屏障持续有效、全部旧Permit准确终态；最后本域核当前逐片向量／事实摘要／许可状态。其他不相关范围变化可不阻当前scope，但不能把expectedVersion更新成latest来掩盖本范围反证。 [原文 L1323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1323>)

Planning只在released＋当前确认＋日期/技术/Source证明齐时合格；Draft/OA批准不等供给。完整PO级Supply版本包含退役键qualified=false，缺中间消息也不遗留旧供给；Receipt覆盖扣剩量并带谱系，计划100＋PO100＋已收40不可计240。causationRefs仅真实Confirmation／Fact／ChangeApplication／CancellationApplication。 [原文 L947](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:947>)、[原文 L949](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:949>)、[原文 L951](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:951>)、[原文 L954](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:954>)

PLM发布先RECEIVED／ACCEPTED／READY，只有真实PO/Revision/已应用Change用了准确基线才APPLIED并有AppliedReference；target/site/scope各自版本。影响评估固定OrderRegistrySeq水位，后建/变技术需Delta；CONTINUE_OLD要明确有限用途许可，HOLD_NEW_USE按接收守卫阻新效果，CHANGE/CANCEL复用真实本域流程，不因ECO标题替交易同意。 [原文 L1253](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1253>)、[原文 L1255](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1255>)、[原文 L1274](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1274>)、[原文 L1278](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1278>)、[原文 L1279](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1279>)、[原文 L1280](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1280>)、[原文 L1281](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1281>)、[原文 L1283](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1283>)

## 6. 共享事务、事实算法、许可竞争与恢复

### 6.1 Source内部状态与对外观察

原案已明确化解名称层差异：Source EXECUTED_PENDING_LEDGER 是内部最终Effect保护阶段；原ClaimRecord线上枚举保持CONSUMED或准确观察状态，不能往RFQ原wire塞新值。PR R3的同事务P→E、RFQ后续观察P→C是不同责任账／投影。Source最终使用与PO一次提交，随后源账观察／关系通知可异步；不是先无保护建PO再异步占源。 [原文 L692](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:692>)、[原文 L693](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:693>)、[原文 L695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:695>)、[原文 L1191](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1191>)

与 [PUR-PR](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/PUR-PR-01.md>)、[PUR-RFQ](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/PUR-RFQ-01.md>) 联调时保留原wire、每Owner状态映射和实际事务证据；文本已给机制，实际采用仍未证。

### 6.2 最终锁与事务参与

命令/Inbox键之后按Ordinal ScopeAtom → 规范Creation组 → Source责任/Grant Owner+Id+代次 → PO → Schedule/SourcePortion Guard → SupplierResponseHead（涉及时）→ Permit/Fact → Work/DependencyHead → 当前投影 → Timeline。只取所需子序，不能先Work/PO再倒取Source。短Work租约领取提交后另起全序最终事务并复验lease；纯结果可Inbox→PO→Head，若需前序控制则先落Inbox再全序处理。 [原文 L1507](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1507>)、[原文 L1509](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1509>)、[原文 L1511](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1511>)

所有Owner参加真实同一连接/事务，调用方唯一commit；不在锁内HTTP或让Owner另连接中途commit。RFQ共同ScopeGuard/CanonicalGroup与PR父预算必须准确映射同物理锁资源，同名字符串不等相同锁。sp_getapplock限定同database/principal，负返回回滚；首次不存在身份需范围或规范key锁加唯一约束。跨库没等价正式采用证据时关闭对应新效果，草稿/历史／真实证据可继续。 [原文 L1187](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1187>)、[原文 L1513](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1513>)、[原文 L1515](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1515>)、[原文 L1517](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1517>)

### 6.3 五类事实与版本替换

事实稳定身份＝Tenant＋kind＋producerOwner＋ledgerKey＋documentId＋documentLineId＋allocationKey；每identity独立version，从1开始。POSTED40后有权v2改30，当前贡献30而非70；VOID贡献0但不自动证明物理未发生。首previous=null，后继必须准确前驱、supersedes和更正authority；缺链隔离补原版本，不直接v3加量。同event同义重复；同event/版异义冲突不覆原Inbox。 [原文 L1088](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1088>)、[原文 L1092](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1092>)、[原文 L1094](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1094>)、[原文 L1096](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1096>)

多allocation须Producer完整行分配manifest及互斥证明，部分消息不能作为全量取消放行。GR和WMS同ReceiptSlice采用指定GR生产者只计一次；AP必须准确账套和POSTED分配，MATCH/AP草稿不进入invoicedPosted。SourcePortion单位数量与原单据原量／UOM并存，精确换算不能舍入多收；跨量纲不可sum。 [原文 L1098](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1098>)、[原文 L1112](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1112>)

| 指标 | 精确含义与累计范围 | 禁止推断 | 出处 |
| --- | --- | --- | --- |
| received | 对每有效RECEIPT stable identity取最新POSTED分配量（VOID取0），按PO SourcePortion/sourceUom汇总 | 文书作废不证明物理未发生；是否恢复执行预算另据Owner | [原文 L1102](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1102>) |
| accepted | 对有效ACCEPTANCE分配取最新量，每ReceiptSlice的有效合格/让步/豁免份额互斥；拒绝/待检未被填0PASS | 付款/入账基准“着荷”不自动给Quality通过 | [原文 L1103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1103>) |
| invoicedPosted | 每个Finance账套AP已POSTED采购票据分配的最新有效量；不含信用票据 | AP创建/匹配受理不算Posted；不是实际付款 | [原文 L1104](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1104>) |
| physicallyReturned | 独立RETURN最新真实退回量，引用原ReceiptSlice/Movement | 不自动从received原实收历史抹去；不直接增加可收许可 | [原文 L1105](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1105>) |
| credited | 独立INVOICE_CREDIT已POSTED分配量，引用原票据；按源单位汇总 | 信用票据不等实物已退，也不代表再采购授权 | [原文 L1106](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1106>) |

netPhysical＝received−returned、netInvoiced＝posted−credited只在同范围单位和完整证据时展示，仍保gross原项；收100退20不自动再给收20许可。无消息不是0，只有从起点完整守卫和Owner空集声明才COMPLETE/0，历史未齐UNKNOWN/null。真实验收／AP先到保原事实待关联，40/30/20可各自为真，不伪造Receipt或削真量。 [原文 L1108](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1108>)、[原文 L1114](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1114>)、[原文 L1116](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1116>)

接收事务验Producer/原凭证manifest后，Inbox+共同Guard+FactIdentity锁，追加FactVersion／Current、受影响五量/完整性、匹配Permit与差异、审计和SupplyOutbox，同commit后ACK。投影重算固定来源水位建候选，CAS时有新事实则重算，不覆盖新数据、不调用收货／QA／AP。源更正由原Owner下一版本；PO无PATCH累计入口。 [原文 L1120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1120>)、[原文 L1122](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1122>)、[原文 L1124](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1124>)、[原文 L1128](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1128>)、[原文 L1130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1130>)

### 6.4 Permit不是已经收货

预算占用不等不可撤实收权，start不等实物已发生。PoReceivingCommitInput是GR最终事务内Owner方法，绝非另一个HTTP收货。start将真实GR ReceiptIntent和PO PermitStart同事务写入后才允许发任何产生实物的动作。正常创建／消费只支持已采用Main共事务；孤立RPC返回200不能证明共事务。 [原文 L1197](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1197>)、[原文 L1207](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1207>)、[原文 L1243](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1243>)

| 原状态 | 触发与最后检查 | 新状态 / 分片 | 出处 |
| --- | --- | --- | --- |
| 不存在 | 已发且Schedule已确认、真实政策/额度/当前scope齐；共同锁下取得自由片 | HELD_NOT_STARTED；占I，不产生P或实物 | [原文 L1213](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1213>) |
| HELD_NOT_STARTED | start核Permit token、总guardRevision、全部逐片vector、未过期、无任何活动禁止；GR先准备同事务ReceiptIntent | IN_FLIGHT；写startRef/startGuardVector/everStarted=true，仍占I | [原文 L1214](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1214>) |
| HELD_NOT_STARTED | 技术HOLD_NEW_USE、Cancel/Change/Receiving Close屏障或过期 | BLOCKED；禁start/commit，保留I；未开始并可证明所有入口必须经start时，可在同锁安装永久start fence后转NO_EFFECT_CLOSED | [原文 L1215](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1215>) |
| IN_FLIGHT | 普通最终commit，或经§10.2.2认可的仅排空旧在途路径；真实Owner参与事务全部成功 | USED；原片从I转P，写ReceiptSliceMap/消费回执，GR/WMS事实由其Owner写；绝不双占P+I | [原文 L1216](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1216>) |
| IN_FLIGHT | 技术硬停或不能判定Owner效果 | REVOKE_PENDING或UNKNOWN；禁止盲重做，保留未决I，查询原ReceiptIntent | [原文 L1217](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1217>) |
| BLOCKED / REVOKE_PENDING / UNKNOWN | 全路径无效果证明且本地start/commit fence已永久安装 | NO_EFFECT_CLOSED；只释放已证无效果片到其当前合法Owner（仍有Cancel/Change Hold则移给该Hold，不当自由量） | [原文 L1218](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1218>) |
| BLOCKED / REVOKE_PENDING / UNKNOWN | 已实际发生完整原量的可信GR/WMS证据，原操作/分片完全对应 | USED，reason=RECONCILED_ALREADY_EFFECTIVE；事实接收，不再执行任何物理动作；有控制违例另Discrepancy | [原文 L1219](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1219>) |
| 任一未结状态 | 已证部分USED、其余未证 | UNKNOWN；已用片归P，其余I不释放 | [原文 L1220](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1220>) |
| UNKNOWN | 混合已用与无效果分片完整覆盖原Permit，数量/身份不交叠 | SETTLED_PARTIAL；异常处置终态，不能回复正常整批已全部收货 | [原文 L1221](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1221>) |
| USED / NO_EFFECT_CLOSED / SETTLED_PARTIAL | 原业务重放 | 原结果；NO_EFFECT后新commit为410 PO_PERMIT_FENCED；USED异Receipt身份409 | [原文 L1222](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1222>) |

| 控制 | HELD_NOT_STARTED | 已IN_FLIGHT但未提交 | 已USED或真事实迟到 | 出处 |
| --- | --- | --- | --- | --- |
| ECO HOLD_NEW_USE／当前技术准入收紧 | BLOCKED，禁止开始 | REVOKE_PENDING；新GR/WMS提交拒绝。先查询原效果：已做的只接证，没做的待no-effect，不认为start是技术豁免 | 历史保留＋影响项，不抹Receipt/Invoice | [原文 L1232](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1232>) |
| Cancel或Change受理屏障 | BLOCKED；不先夺走它的预算片，须无效果证明后转Hold | 仅在start在屏障前、ExactPermitId列入该Work的drain集合时可DRAIN_EXISTING；不能新起、扩量或换scope | P参与最终量检查；晚10使原60待核/拒绝，不自动缩成50 | [原文 L1233](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1233>) |
| Receiving Close请求屏障 | BLOCKED；给原未开始操作封start后取no-effect | 屏障前的原在途可排空；所有I归零且其余关闭门满足之前不能CLOSED | 原事实保留；CLOSED之后的新Permit拒绝 | [原文 L1234](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1234>) |
| 已最终取消/关闭对应范围 | 不可能正常仍有效；禁新开始并查异常 | 可信完整截止本应排除此状态；出现即PO_CUTOFF_PROOF_CONFLICT，不新提交、只核已发生事实 | 事实仍接收，超执行与源差异分轴，不反造执行许可 | [原文 L1235](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1235>) |

最终消费先原ReceiptIdentity/Consumption查历史成功，原USED直接回不再执行；新效果须IN_FLIGHT、有可解start/OwnerIntent、准确Permit token/总版/逐片向量，变化先查真实结果再revalidate原许可。当前硬停／最终closed阻所有新效果，软屏障只给屏障前且列在drainPermitIds的原在途；仅HELD不能偷start。各Owner在一次最终commit各写本账，PO写BudgetSegment/PermitConsumption/ReceiptSliceMap，GR/WMS写真实原事实。 [原文 L1228](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1228>)、[原文 L1237](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1237>)、[原文 L1239](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1239>)、[原文 L1244](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1244>)、[原文 L1245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1245>)、[原文 L1246](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1246>)

USED早于异步Received投影时P已经保护，不能用页面0返量。未知先查原操作；部分真实用量归P、未决片I保留，完整混合终态SETTLED_PARTIAL不是整批USED。NO_EFFECT须完整原Permit、全部可能路径、有效水位及永久start/commit fence；只有everStarted=false且全入口强制同start、GR从未派发/委托才可本地同锁即时封闭。任何TTL/lease/队列删除均不是无效果。 [原文 L1224](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1224>)、[原文 L1247](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1247>)、[原文 L1249](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1249>)

### 6.5 Command、异步工作与故障出口

普通指纹为SHA256(method大写＋换行＋规范路径＋换行＋原UTF8正文)，属性重排也异文；原始body首次序列化后冻结。终态回原业务、当前披露再核；长流程短事务登记Operation/Work/Outbox/ACCEPTED，后继业务拒绝不改历史受理。刷新只存key/method/path/hash和主体，不把商业正文入localStorage；NOT_OBSERVED不等未执行。fence-unobserved仅本地命令与同Command锁竞争的永久墓碑，不能取消已登记远端工作。 [原文 L1539](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1539>)、[原文 L1541](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1541>)、[原文 L1543](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1543>)、[原文 L1563](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1563>)

| HTTP / code | 触发与结果 | 前端/恢复 | 出处 |
| --- | --- | --- | --- |
| 400 PO_VALIDATION | 未知成员、类型/精度/非法日期/范围，业务零写 | 定位字段，不截断重发 | [原文 L1575](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1575>) |
| 401 PO_UNAUTHENTICATED | 身份过期 | 清敏感缓存，重新登录查原键 | [原文 L1576](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1576>) |
| 403 PO_FORBIDDEN / PO_FIELD_FORBIDDEN | 对象可见但动作/字段无权 | 整请求失败，清受限输入 | [原文 L1577](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1577>) |
| 404 PO_NOT_FOUND | 不存在或Tenant/范围无权 | 不区别真实存在，回列表 | [原文 L1578](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1578>) |
| 428 PO_VERSION_REQUIRED | 缺并发token | 重读对应资源，不造1 | [原文 L1579](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1579>) |
| 409 PO_VERSION_CONFLICT | 合法token已过时 | 差分后明确新意图，禁自动覆盖 | [原文 L1580](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1580>) |
| 409 PO_STATE_CONFLICT | 状态不允许动作 | 展示实际状态/正确后继入口 | [原文 L1581](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1581>) |
| 409 PO_COMMAND_CONTENT_CONFLICT | 同请求键改内容 | 保留原键查询，不覆盖历史 | [原文 L1582](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1582>) |
| 409 PO_COMMAND_BUSY / PO_GUARD_CONFLICT | 锁/预算竞争 | Retry-After:3；原键查结果 | [原文 L1583](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1583>) |
| 410 PO_COMMAND_FENCED | 本地无效果命令已封闭 | 旧键不执行；有权时明确新意图 | [原文 L1584](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1584>) |
| 409 PO_CREATION_CONTENT_CONFLICT | 同规范组不同manifest/商业内容 | 不新建/不盲重试，核上游固定输入 | [原文 L1585](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1585>) |
| 409 PO_SOURCE_SHARE_USED | 同Grant/代次在别的真实组消费 | 查原SourceUse/Owner，不能换UUID | [原文 L1586](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1586>) |
| 422 PO_SOURCE_INVALID / PO_SOURCE_MISSING | 授权/单位/供应商/来源不吻合 | 只阻对应正式创建，草稿可留 | [原文 L1587](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1587>) |
| 503 PO_OWNER_ATOMICITY_UNAVAILABLE | 同库最终效果协议未采用 | 禁真实创建/依赖效果，mock不当生产 | [原文 L1588](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1588>) |
| 422 PO_COMMERCIAL_UNSUPPORTED | 原币/模式/费用/税/交期不可承接 | 不改价/默认币，不试写PO | [原文 L1589](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1589>) |
| 422 PO_DELIVERY_UNRESOLVED | 缺原日历/起算/期限语义 | 原事实保留，不能补今天 | [原文 L1590](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1590>) |
| 422 PO_DELIVERY_CONFLICT | 原固定/相对矛盾或HARD未满足 | Confirmation.BLOCKED，走有权变更 | [原文 L1591](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1591>) |
| 422 PO_APPROVAL_POLICY_MISSING | 无规则/多规则/必要阈值或角色缺证 | 不当自动通过或自动免审 | [原文 L1592](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1592>) |
| 409 PO_APPROVAL_CONTENT_STALE | Subject业务内容/前驱变动 | 原OA事实保留，应用冲突 | [原文 L1593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1593>) |
| 409 PO_ACK_SCOPE_MISMATCH | Supplier回复版/hash/数量/范围不一致 | 可留原回应异常，不确认当前 | [原文 L1594](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1594>) |
| 422 PO_ACK_EVIDENCE_MISSING | 真实确认时点/身份不可验证 | 只记录未核回应，无确认锚点 | [原文 L1595](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1595>) |
| 409 PO_DISPATCH_UNRESOLVED | 在途发送无安全幂等/查询 | 禁自动重发，查原凭证 | [原文 L1596](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1596>) |
| 409 PO_SCOPE_STALE | 变更评估后发生新履约/控制 | 重新评估原业务，不能静默裁剪 | [原文 L1597](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1597>) |
| 422 PO_RECEIPT_POLICY_MISSING | 新接收/超收实际政策不可判 | 不发许可；历史真超收仍接证 | [原文 L1598](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1598>) |
| 422 PO_RECEIPT_LIMIT | 实收/保护/在途/Hold已无获准余量 | 拒新Permit，不截断已经发生事实 | [原文 L1599](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1599>) |
| 409 PO_RECEIVING_SCOPE_CLOSED | 取消/关闭/技术Hold先成立 | 不新收；历史回执仍按事实处理 | [原文 L1600](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1600>) |
| 422 PO_CANCELLATION_SCOPE_CHANGED | 申请60但最新上限50或未知 | 拒原量/待核，新50另申请 | [原文 L1601](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1601>) |
| 422 PO_FULFILLMENT_COVERAGE_UNKNOWN | 无法证明各事实/Permit完整覆盖 | max可取消null，查对应Owner | [原文 L1602](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1602>) |
| 409 PO_CUTOFF_PROOF_CONFLICT | 晚事实反证已签截止完整性 | 实收留存、差异/源处置保护，不恢复旧许可 | [原文 L1603](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1603>) |
| 409 PO_FACT_CONTENT_CONFLICT | 同消息/事实版异内容 | 隔离新证据，不覆盖可信原件 | [原文 L1604](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1604>) |
| 422 PO_FACT_PROVENANCE_MISSING | 缺真实WMS/QA/AP引用 | 保留待核输入，不冒已完成累计 | [原文 L1605](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1605>) |
| 422 PO_CLOSURE_DEPENDENCIES_OPEN | 必需源/质量/财务/在途未清 | 本域Close BLOCKED，不批量关账 | [原文 L1606](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1606>) |
| 409 PO_IMPACT_STALE | 工程评估范围/版本过时 | Delta评估，不用ExecutionKey重试改内容 | [原文 L1607](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1607>) |
| 422 PO_ECO_AUTHORITY_MISSING | 缺ECO/用途/有限旧版许可 | 拒实施，原发布可保留 | [原文 L1608](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1608>) |
| 422 PO_EXTENSION_NOT_AUTHORIZED | 预付、无源采购、自动超收等未授权 | 不扩大政策；无关页面继续 | [原文 L1609](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1609>) |
| 422 PO_EXPORT_LIMIT / 409 PO_EXPORT_NOT_READY | 超量/生成尚未完成 | 缩筛选或查任务，不下载半文件 | [原文 L1610](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1610>) |
| 403 PO_DOWNLOAD_REVOKED / 410 PO_ARTIFACT_EXPIRED | 当前无权/票据过期 | 现权新申请，旧文件不声称远程追回 | [原文 L1611](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1611>) |
| 413 PO_BODY_TOO_LARGE / 429 PO_RATE_LIMITED | 技术限制 | 不拆未知原组；重试保原键 | [原文 L1612](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1612>) |
| 503 PO_DEPENDENCY_UNAVAILABLE / PO_RESULT_UNKNOWN | 依赖不可用/提交结果不能确认 | 明确未接入或待查原结果，不假失败/成功 | [原文 L1613](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1613>) |

| HTTP / code | 确定含义 / 持久结果 | 页面或机器下一步 | 出处 |
| --- | --- | --- | --- |
| 409 PO_ACK_SUPERSEDED | 选定历史ACCEPTED已不是当前SupplierResponseHead | 重新读当前头；展示反报价/拒绝，不自动挑另一旧Ack | [原文 L1619](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1619>) |
| 409 PO_ACK_CHAIN_CONFLICT | 回复链分叉、同业务Ref异语义、非法换epoch | 原事实保留、Head CONFLICT；由可信Owner补正，不按时间排序获胜 | [原文 L1620](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1620>) |
| 422 PO_ACK_CHAIN_INCOMPLETE / PO_ACK_CURRENT_UNPROVEN | 前驱未齐或无法验证当前声明 | 只阻新确认，原Confirmation仍可读；查原Owner | [原文 L1621](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1621>) |
| 409 PO_RESPONSE_HEAD_CHANGED | expectedResponseHeads版本不符 | 重新载入每Schedule头，不自动应用旧接受 | [原文 L1622](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1622>) |
| 409 PO_PERMIT_CONTENT_CONFLICT | 同receiptOperationKey改scope/数量/原收货身份 | 仅查原Permit，不换key重领 | [原文 L1623](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1623>) |
| 409 PO_PERMIT_GUARD_CHANGED | Permit token、总guardRevision或逐片vector已变 | 先查实际结果，再revalidate原Permit；不得盲覆latest | [原文 L1624](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1624>) |
| 409 PO_PERMIT_NOT_STARTED | 新commit无有效startRef/IN_FLIGHT | 走原start门；曾HELD不是完成许可 | [原文 L1625](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1625>) |
| 409 PO_PERMIT_BLOCKED / PO_PERMIT_DRAIN_NOT_AUTHORIZED | 当前硬停、未列入软屏障原drain范围 | 不执行新GR，原可能效果查证；未知片保持I | [原文 L1626](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1626>) |
| 410 PO_PERMIT_FENCED | 原Permit已有不可晚执行墓碑 | 禁原新效果；历史事实另接证，不复活许可 | [原文 L1627](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1627>) |
| 422 PO_PERMIT_NO_EFFECT_UNPROVEN | 缺全路径fence或仍有真实/可能效果 | 202/UNKNOWN工作结果或字段422；无证不释放 | [原文 L1628](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1628>) |
| 409 PO_CHANGE_NOT_SUBMITTED | resume时没有原ChangeWork | 走明确apply，不借resume新建意图 | [原文 L1629](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1629>) |
| 409 PO_CHANGE_APPLY_CONTENT_CONFLICT | 同Change/Work提交另一apply哈希/批准/范围 | 原Work保留，拒覆盖，合法变化另提案 | [原文 L1630](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1630>) |
| 409 PO_WORK_VERSION_CONFLICT / PO_WORK_LEASE_LOST | 人工恢复token过时或worker已失lease | 人读最新Work；旧worker只留真实结果，不最终提交 | [原文 L1631](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1631>) |
| 409 PO_CHANGE_ALREADY_APPLIED | 放弃竞争中原Application已提交 | 返回原新Revision和Application，不能弃稿回滚 | [原文 L1632](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1632>) |
| 409 PO_WORK_FINAL_FENCED | 弃置/确定拒绝已封最终应用 | 只清理原未知与Owner义务，不再新增商业应用 | [原文 L1633](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1633>) |
| 409 PO_DEPENDENCY_CONTENT_CONFLICT | Owner结果与请求/版本/前驱不一致 | 隔离Result，Head CONFLICT；不把未知写成通过 | [原文 L1634](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1634>) |
| 422 PO_DEPENDENCY_UNSATISFIED | 必需Supplier/GR/Source/QA/AP证明未满足 | 保持DRAINING/WAIT_OWNER；最终不提交 | [原文 L1635](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1635>) |
| 422 PO_CANCEL_TERMS_CHANGED | 动态同意附带不同数量/价/费用或商业范围 | 原提案不能应用，需要准确后继和重新审批 | [原文 L1636](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1636>) |
| 409 PO_CANCELLATION_EVIDENCE_CHANGED | 最后事务前证据头被撤回/冲突/过期 | 原应用未发生则阻断；已经发生则记录差异不倒改 | [原文 L1637](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1637>) |

| 故障位置 | 已成立事实 | 恢复必须/禁止 | 出处 |
| --- | --- | --- | --- |
| PO建单commit失败或响应丢失 | 可能全成、可能全回滚 | 查原creationKey；不再编号重建 | [原文 L1654](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1654>) |
| 已建PO/RFQ回执未送到 | PO真实存在，Source执行已绑定 | 原Receipt重投；不重新执行SourceClaim/建单 | [原文 L1655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1655>) |
| PO批准但release未做 | OA事实成立，文书未发 | 用户有权release；不能后台称Supplier已确认 | [原文 L1656](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1656>) |
| release后邮件未知 | ACTIVE文书＋发送未知 | 查原dispatch/人工可信证明；禁不安全盲发 | [原文 L1657](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1657>) |
| GR物理成功而商业回写失败 | 原Permit已用/未知，真实实物不消失 | 按原ReceiptIntent补GR/PO投影；不重收/不释放未知预算 | [原文 L1658](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1658>) |
| Source P→C通知未回 | 创建成功但源观察未新 | 显示SourcePending，不再加PO贡献计算第二账 | [原文 L1659](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1659>) |
| Cancel商业成功但源处置超时 | Cancel有真凭证、Source待查 | 原DispositionKey查询，不直接加Source余额 | [原文 L1660](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1660>) |
| AP已POSTED但PO投影未更新 | 真实Finance结果存在 | 接原AP分配一次；PO不补造AP/GL | [原文 L1661](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1661>) |
| ECO部分PO成功 | 每对象各有执行结果 | 成功保留，未决逐项恢复；不全包回滚库存/PO | [原文 L1662](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1662>) |

| 恢复例 | 保留事实 | 唯一下一步 | 出处 |
| --- | --- | --- | --- |
| Change.apply 202后进程退出 | Work、Hold、屏障和依赖已提交，NewRevision=null | 新实例领取同Work，查原请求；不是重新POST Change | [原文 L1678](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1678>) |
| 新SourceClaim已准备但响应丢失 | 同一Owner operationKey可能已有结果 | query原键，回原Claim；最终SourceUse只一次 | [原文 L1679](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1679>) |
| 两worker lease先后更换 | 原Owner请求相同，旧lease失效 | 旧worker不能提交；新worker核当前结果/最后门 | [原文 L1680](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1680>) |
| 弃稿期间远端效果未知 | FinalApplicationFence已建立，仍有潜在义务 | 只清理同Work；不能靠ABANDONED字样解除Hold | [原文 L1681](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1681>) |
| Cancel Application已存在，Source结果迟到 | 商业取消已成立，源状态PENDING | 原DispositionKey回查，绝不再次QuantityEntry(CANCEL) | [原文 L1682](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1682>) |
| Supplier同意后来撤回 | 原同意与后继并存，当前Head非SATISFIED | 未应用则阻；已应用则差异/Owner处置，不能删旧Application | [原文 L1683](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1683>) |

普通Outbox 30秒lease与Work执行lease分开，CHANGE/CANCEL_WAKE只是唤醒；1/5/30/120/600秒退避20次后人工待办仍UNKNOWN/DRAINING，不能变业务拒绝或已清理。人工resume现权只唤醒同Work。真实终态不无限重试，永久防重/fence最小记录不TTL删除。 [原文 L1650](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1650>)、[原文 L1666](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1666>)、[原文 L1673](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1673>)、[原文 L1674](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1674>)、[原文 L1687](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1687>)

## 7. 权限、披露、文件与审计

用户24类 pur.po 能力按Site/Buyer/对象与字段范围核，机器8类独立Producer能力不能转成人类越权。后继Owner结果按真实Producer接收，即使原操作者失权仍保事实；当前用户读／下载则重验。审批等完整商业动作缺commercial.read整请求403，不把脱敏null当可审批内容或PATCH清价。 [原文 L128](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:128>)、[原文 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:130>)、[原文 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:132>)、[原文 L453](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:453>)

### 准确能力清单

本稿权限前缀`pur.po.`：`read`、`commercial.read`、`commercial.write`、`history.read`、`create.prepare`、`create.execute`、`approval.submit`、`release`、`dispatch`、`confirmation.record`、`confirmation.apply`、`change.edit`、`change.apply`、`cancel`、`close`、`recover`、`export`、`attachment.read`、`attachment.download`、`attachment.disclose`、`engineering.feedback`、`engineering.evaluate`、`engineering.execute`。不是默认给任何现有角色。

写动作要求当前对象读权＋该动作权；改变Buyer/站点还要原/目标范围权限。IAM返回精确Tenant、Site集合、Buyer集合及字段集合；空集合不代表所有。生产者能力独立：`pur.po.ingest.rfq`、`.source`、`.receipt`、`.acceptance`、`.invoice`、`.return`、`.approval`、`.engineering`，以及`pur.po.receiving.guard`。普通Buyer不能凭一个type字段冒发AP/质量/库存事实。

用户撤权后先清无权缓存；合法PO/GR/QA/AP机器仍接收真实历史结果，不能借机器能力把结果泄给原用户。下载在实际发送前复核当前文件和字段权；阅读附件不等可向Supplier披露。事实有效性、实际执行许可和当前用户能否读该事实分别判断。
 [原文 L128](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:128>)

动态依赖另按supplier/fee/gr/source/quality/ap专用ingest能力与请求ProducerOwner匹配，Source不能冒AP。对象不可见404；对象可见动作/字段不足403。历史TimelineSeq是本PO提交次序，OccurredAt可迟到；ResourceSeq不能跨对象比。 [原文 L1319](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1319>)、[原文 L1565](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1565>)、[原文 L1577](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1577>)、[原文 L1578](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1578>)

导出全授权筛选而非当前页，最多10000行，金额各带币种、UNKNOWN／REDACTED明确；生成和下载重新核全体行字段权，撤权不发半文件。CSV外部文字首非空白为公式符号等加TEXT:查看前缀，原值不改。附件必须已关联真实受控版本，read不等disclose；票据下载走私有流且现权复验。 [原文 L1189](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1189>)、[原文 L1553](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1553>)、[原文 L1554](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1554>)、[原文 L1555](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1555>)、[原文 L1567](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1567>)

## 8. 页面、输入与可见失败

| 页面 | 路由 | 关键区与下一步 | 出处 |
| --- | --- | --- | --- |
| 列表 | `/pur/purchase-orders` | Supplier/Site/Buyer/状态/日期筛选；来源/确认/履约异常分列；不以一个进度条掩盖未知 | [原文 L84](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:84>) |
| 非RFQ建立准备 | `/pur/purchase-orders/new`、`/pur/po-preparations/:id` | 授权来源、供应商、行/商业证据；保存只是Preparation，不能假装已建PO | [原文 L85](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:85>) |
| 订单详情 | `/pur/purchase-orders/:poId` | 概览、行与分交期、原商业、审批/确认、履约、变更取消、来源/证据、技术影响、历史 | [原文 L86](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:86>) |
| 文书修订/差分 | `/pur/purchase-orders/:poId/changes/:changeId` | 旧ACTIVE与候选并列；只编辑尚未提交候选；取消是独立精确数量命令 | [原文 L87](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:87>) |
| 操作结果 | `/pur/po-operations/:operationId` | 原请求、六轴结果、缺少的Owner凭证、原键查询/安全恢复；不提供“全部重新下单” | [原文 L88](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:88>) |

| 按钮 | 可见/可用条件 | 成功后 | 拒绝/未知后 | 出处 |
| --- | --- | --- | --- | --- |
| 保存建立准备 | create.prepare＋本Site；DRAFT | 留页，刷新Preparation.version及稳定行ID | 字段错误原位；未知查本命令，不换键另存 | [原文 L115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:115>) |
| 建立PO | prepare经当前来源/商业/权限校验，可取得全Claim和最终效果协议 | 得真实PO ID后GET详情；仍非供应商确认 | 明确缺项留准备；commit未知查creationKey | [原文 L116](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:116>) |
| 提交审批 | approval.submit；对应修订/变更/取消草稿且内容完整 | 显示审批意图与OA实例待分配 | 缺矩阵/多规则BLOCKED；不造默认审批成功 | [原文 L117](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:117>) |
| 发出订单 | release；准确版本审批/有据豁免已满足、当前源/技术有效、附件可披露 | 文书ACTIVE＋投递待办 | 发送未知查原dispatch；不能重复发送原不安全渠道 | [原文 L118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:118>) |
| 记录供应商回复 | confirmation.record；有原发出版本及凭证 | 原回复留证，COUNTEROFFER进入待变更 | 历史回复可记录；不能用它解锁当前版本 | [原文 L119](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:119>) |
| 商业确认 | confirmation.apply；精确版本/供应商接受范围/当前门与交期评估满足 | 生成Confirmation；更新分交期供给资格及Outbox | 锚点/硬交期冲突不确认；保留真实供应商回复 | [原文 L120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:120>) |
| 变更 | change.edit；对象可见 | 新草稿差分，旧文书仍有效 | 新草稿不自动冻结旧PO；真实控制另记 | [原文 L121](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:121>) |
| 取消未履约 | cancel；准确行/交期/来源份额；审批及范围处置政策可判 | 请求受理/等待截止，最终按§8裁决 | 新实收降低上限则拒原60，不静默改成50 | [原文 L122](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:122>) |
| 关闭/恢复查询 | close或recover；按独立关闭门 | 只关闭已满足的范围；来源/财务轴不改 | 未决保持可查询；不把请求超时改成取消完成 | [原文 L123](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:123>) |
| 工程反馈/影响处置 | engineering.feedback / engineering.execute＋对应scope | 原反馈、评估/执行回执各自可查 | 未获ECO/用途权限不得替换技术或改存量 | [原文 L124](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:124>) |

列表默认OPEN、page1/size20/updatedDesc，total与items同授权快照；不默认税、结算币、审批Role。详情并列创建、文书、确认、五类履约与处置，未知不显示完成百分比。Preparation改商业／Source就失原validatedHash；写Abort只停客户端等待，不等业务取消。前端分别管理草稿、服务端对象和首次冻结请求body。 [原文 L105](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:105>)、[原文 L109](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:109>)、[原文 L723](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:723>)、[原文 L725](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:725>)、[原文 L1648](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1648>)

用户应能从Work看到原请求、当前phase与businessOutcome、缺哪一Owner证明、原键查询／resume／安全弃置。Apply202没有NewRevision，Cancellation商业APPLIED/SourcePENDING同时显示。原60因新收10失败，展示上限50和原提案未成，不能自动改数量重试。历史Supplier接受被后继替换时显示原凭证与当前头，禁止自动挑另一个旧Ack。 [原文 L864](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:864>)、[原文 L872](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:872>)、[原文 L1032](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1032>)、[原文 L1601](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1601>)、[原文 L1619](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1619>)、[原文 L1639](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1639>)

## 9. 实施拆分与固定旧代码差异

建议沿现有Main Purchasing/Orders应用域适配：Controller绑定DTO/HTTP，Application组织Owner与最终事务，Domain保护数量/版本/商业，Infrastructure提供共享Context、权威锁和真实配置。前端features/purchase-orders细分列表／准备／差分／ResponseHeads／ChangeWork／CancellationWork／履约／技术／恢复。不能用返回固定success的空Controller表示已打通。 [原文 L1646](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1646>)、[原文 L1648](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1648>)、[原文 L10707](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10707>)

| 任务 | 后端/契约交付 | 前端/可演示结果 | 完成证据（未来实施） | 出处 |
| --- | --- | --- | --- | --- |
| PO-D01 协议/权限/示例 | 同版OpenAPI、TypeScript/C# DTO、error/policy/Owner adapters、RFQ兼容类型 | 列表、六轴Detail骨架可消费真实契约mock | v1.0原86组及REV1新增JSON须与当前DTO一致；Tenant/权限负例；不是已部署 | [原文 L10711](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10711>) |
| PO-D02 唯一性/历史骨架 | Order/Revision/CreationRegistry/Command/Inbox/Outbox及Schema映射，旧PO读兼容 | 原Key查询与RESULT_UNKNOWN恢复 | 两实例同Key一次、冲突不覆盖、command fence边界 | [原文 L10712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10712>) |
| PO-D03 RFQ到真实PO | 完整Commercial/Delivery映射、Source/Scope同库enlist、原子create/query/fence | 从RFQ来源进入真实PO/行/Schedule，未确认状态准确 | §14 E02～E04；创建失败全回滚、响应丢失不重复 | [原文 L10713](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10713>) |
| PO-D04 获准非RFQ来源 | Preparation和SourceBundle解析、校验hash及非RFQ创建 | 保存、来源缺项、正式创建分开 | 无Source只有草稿；同授权换准备不双买；不伪造RFQ | [原文 L10714](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10714>) |
| PO-D05 审批/发出/确认 | PO矩阵/SubjectHash/回调、Disclosure、Supplier证据、冻结日历Assessment | 按审批→发单→供应商接受→本域确认展示 | E05～E19；纯token变动不误STALE；原日历和硬交期 | [原文 L10715](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10715>) |
| PO-D06 收货守卫/履约事实 | Permit/BudgetSegments、分类型FactCurrent/versions/水位/五量、对账 | Receipt40/accept30/invoice20与源凭证可查看 | H21边界、重复不计、未知不零、AP草稿不Posted | [原文 L10716](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10716>) |
| PO-D07 分交期/变更 | PortionTransfer、新增Source授权、只改未履约商业、版本审批/应用 | 40历史＋未来30/30，原旧价并列 | 新量无权拒；新收货竞争使旧评估失效；旧40不搬家 | [原文 L10717](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10717>) |
| PO-D08 取消/关闭 | Hold/截止证明/最终ScopeCAS、Source正式处置、独立Closure | 正常取消60以及两个晚10分支，源状态独立 | C01～C18、R01～R09；没有真实数据删除/PR直接加数 | [原文 L10718](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10718>) |
| PO-D09 Planning/PLM | 完整SupplySnapshot/谱系、反馈/ImpactDelta/ExecutionKey/TargetReceipt | 明确供给Pending、技术采用阶段和逐项实际结果 | 不双计、不漂移旧技术、部分成功不全回滚 | [原文 L10719](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10719>) |
| PO-D10 安全和交付收口 | 稳定分页、敏感历史/导出、限流/日志、兼容切换说明 | 无权与未接入/空列表分开，下载再核权 | 旧数据无损映射、无双写主账、全部实际测试报告/环境/SHA | [原文 L10720](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10720>) |

固定旧源码基线157630594e3371fe181955d2f6227ff3b6962c84是历史静态来源，不是本轮最新部署盘点。旧Create取Supplier币种、非正单价回退价表、固定2位税、AutoApproved直接Confirmed，以及旧Qty21,8/Price18,4/Amount18,2都需要真实无损映射和单writer切换。未本轮全文读这5个原源码，不能把主文作者旧代码结论改称本轮执行验证。 [原文 L10755](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10755>)、[原文 L10760](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10760>)、[原文 L10761](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10761>)、[原文 L10762](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10762>)、[原文 L10763](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10763>)、[原文 L10765](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10765>)

## 10. 可实现验收定义及实际执行边界

以下保留92原AC与32REV1的具体输入、顺序和持久断言。全部 **NOT_RUN**；原JSON连续配方尚在闭合队列，不能用表内预期视为真实HTTP结果。

| ID | 输入/操作与前提 | 明确预期及不应出现的变化 | 出处 |
| --- | --- | --- | --- |
| PO-AC01 | E02完整RFQ组，全部Claim/同库守卫真实模拟满足 | 一个PO头/全行/逐源映射，E03带真实生成ID；文书DRAFT，未Supplier确认、无合格Supply | [原文 L10566](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10566>) |
| PO-AC02 | E02同CanonicalGroup/同manifest换运输Key并发两实例 | 至多一个创建；第二回原Receipt或PENDING后原结果；无第二SourceUse/编号/PO | [原文 L10567](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10567>) |
| PO-AC03 | E03成功后同业务组改Currency/Quantity再execute | 409 PO_CREATION_CONTENT_CONFLICT；原单原价/原hash不变 | [原文 L10568](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10568>) |
| PO-AC04 | ATOMIC_MANIFEST两行，其中一行技术/来源非法 | 全组不建；不能创建第一行再称部分PO成功 | [原文 L10569](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10569>) |
| PO-AC05 | 创建事务在最后Receipt或Audit写入异常 | 订单/行/Source最终使用同事务回滚；commit无法确认按原Key查，不直接新建 | [原文 L10570](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10570>) |
| PO-AC06 | 数据库commit成功后丢HTTP响应 | 原query回E03；RFQ补链不调用第二次Source Claim/创建 | [原文 L10571](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10571>) |
| PO-AC07 | 两组各80使用同源100；或同Grant进入不同RFQ/nonRFQ入口 | Source父预算/份额裁决不允许消费160；本地拒冲突不能只靠读R100 | [原文 L10572](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10572>) |
| PO-AC08 | Source只部分Claim成功，另一来源未决 | 不进入正式创建；UNKNOWN保护保留，封原组后才能逐份额清理 | [原文 L10573](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10573>) |
| PO-AC09 | 120CUA＋30CUB原费用，目标只支持单币裸decimal | A21 supported=false，无PO；不把270比较币当CUA金额或丢费用 | [原文 L10574](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10574>) |
| PO-AC10 | 明确0价vsnull，旧消费者会非正价回退价表 | 受控0必须原样可承接才创建；null不补0；不能静默改为另一价格 | [原文 L10575](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10575>) |
| PO-AC11 | PROGRESSIVE前100×12后20×10与ALL_UNITS120×10 | 分别1400与1200；记录原mode/分段，不用平均价假等价 | [原文 L10576](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10576>) |
| PO-AC12 | 原TargetLine相对确认后5日，创建尚无确认 | DeliverySnapshot.anchorDate/resolvedDate为null；回执hash等于原快照，无今天默认日期 | [原文 L10577](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10577>) |
| PO-AC13 | 创建后查原Receipt与deliveryEvidenceRef | 返回原商业/DeliverySnapshot，当前默认Supplier/Calendar变动不改初始hash | [原文 L10578](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10578>) |
| PO-AC14 | 上游fence先于execute，完整原manifest/路径均可核 | 持久CANCELLED_NO_EFFECT；迟到execute不建；Source释放另取其Owner凭证 | [原文 L10579](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10579>) |
| PO-AC15 | 上游fence在E03真实PO之后 | A22 TOO_LATE_EXECUTED；原创建仍SUCCEEDED；取消PO须正式§8流程 | [原文 L10580](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10580>) |
| PO-AC16 | group未观察且原manifest/路径证据取不到 | UNKNOWN/NOT_OBSERVED，无空份额fence；不授新组采购许可 | [原文 L10581](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10581>) |
| PO-AC17 | A01无RFQ准备，SourceRef真实授权20 | 可保存；解析来源与商业齐后按Source规范身份建单，不伪造RFQ ID | [原文 L10582](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10582>) |
| PO-AC18 | A01仅manual标签/无Source授权，尝试create-order | 草稿可存、正式创建BLOCKED；无PO/Grant/Claim被发明 | [原文 L10583](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10583>) |
| PO-AC19 | preparation验证后改Supplier/Qty或先前version过时 | 清旧校验指针或409；旧ValidatedHash不能继续建立 | [原文 L10584](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10584>) |
| PO-AC20 | 同实际Source授权/Grant换PreparationId再建 | 注册/源守卫识别同业务，不因另一准备ID二次采购 | [原文 L10585](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10585>) |
| PO-AC21 | D01 RFQ同源一家；另有合法PR不同行多Supplier | RFQ范围严格保留；不把RFQ限制扩大成禁止所有PR合法分组 | [原文 L10586](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10586>) |
| PO-AC22 | E05送审后PoRevision状态token变、内容不变 | SubjectBusinessRevision/Hash仍匹配；回调不因rowversion变化误判内容被修改 | [原文 L10587](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10587>) |
| PO-AC23 | 同内容/Policy换命令ID重送审批 | 返回同ApprovalSubmission，不重复OA实例；同Idempotency-Key异body409 | [原文 L10588](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10588>) |
| PO-AC24 | 无审批规则或多个冲突规则/金额币种未可比 | BLOCKED/PO_APPROVAL_POLICY_MISSING；不是NOT_REQUIRED/虚假固定阈值 | [原文 L10589](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10589>) |
| PO-AC25 | 原PO_RELEASE批准后商业内容/生效scope已被合法新修订替代 | 原OA事实保留，release/apply拒旧证明；不能解锁新内容 | [原文 L10590](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10590>) |
| PO-AC26 | E07合法OA通过 | 只存审批事实；未调用Supplier发送/confirmation、未自动Source返额 | [原文 L10591](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10591>) |
| PO-AC27 | E10发出时某附件缺disclose权 | 403/具体Gate，整个新外发不执行；attachment.read不代disclose | [原文 L10592](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10592>) |
| PO-AC28 | 正式文书已提交，外部发送响应未知且渠道不安全幂等 | UNKNOWN；不无限自动retry，不换dispatchKey假消除未知 | [原文 L10593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10593>) |
| PO-AC29 | Supplier回应指旧Revision或不同ContentHash | 原事实可存异常；不能确认当前版；当前读权仍检查 | [原文 L10594](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10594>) |
| PO-AC30 | 单Schedule100，Supplier接受60且另40未说明 | 保留COUNTEROFFER/范围异常；不自动拆60/40已确认；需正式分交期变更 | [原文 L10595](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10595>) |
| PO-AC31 | 已有合法Schedule60/40，仅完整接受60 | 只确认60，Order PARTIAL；40未确认部分不进合格Supply | [原文 L10596](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10596>) |
| PO-AC32 | E15真实接受当地9/29、原日历Oct1/2闭日、5工作日 | E17得到10/8；确认AppliedAt不同不改真实锚点 | [原文 L10597](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10597>) |
| PO-AC33 | 日历更新v2或其今天规则不同 | 原确认按冻结v1；原DeliverySnapshotHash保持，不读latest替代 | [原文 L10598](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10598>) |
| PO-AC34 | 日历覆盖不够/计日规则未知/两种例外冲突 | BLOCKED，原Supplier回复保留；不能猜周末/补今天 | [原文 L10599](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10599>) |
| PO-AC35 | FIXED_DATE与相对日期两项不一致；或HARD要求无法证明满足 | Confirmation.BLOCKED/PO_DELIVERY_CONFLICT，未确认；不能以REQUESTED警示替硬门 | [原文 L10600](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10600>) |
| PO-AC36 | 原REQUESTED日早于明确承诺但真实政策允许带理由 | 显示LATE_REQUESTED及差异，保留真实策略；不是自动豁免HARD | [原文 L10601](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10601>) |
| PO-AC37 | 新Change草稿/WHAT_IF未申请真实替代 | 原ACTIVE和已有承诺不被停止，RFQ原依据不重写 | [原文 L10602](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10602>) |
| PO-AC38 | 已收40，A04把剩60同供方同源分成30/30 | 原40/原事实ID不迁移；转出60/转入30+30抵消，不新Claim100/60 | [原文 L10603](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10603>) |
| PO-AC39 | 分交期以数组位置替换已有ScheduleId或来源份额不守恒 | 拒绝，原分配不变；缺UOM转换/余数规则不能静默凑齐 | [原文 L10604](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10604>) |
| PO-AC40 | 只对尚未履约60改价，旧40已开票 | 新商业行/版本只作用未来范围；原40价格和原payloadHash不变 | [原文 L10605](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10605>) |
| PO-AC41 | 增加20没有额外SourceGrant或只改futureQuantity | 拒绝新量；additionalLines必须准确授权/Claim后同事务加入 | [原文 L10606](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10606>) |
| PO-AC42 | 评估Change后又有10受理/收货 | apply检测Guard/Fulfillment变化，重新评估或拒绝；不按旧余量移动已履约 | [原文 L10607](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10607>) |
| PO-AC43 | 新技术X→Y替代，仅ECO标题无用途/Source/商业证据 | 不修改旧行；反馈/缺项可留，实际Change BLOCKED | [原文 L10608](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10608>) |
| PO-AC44 | 变化提交成功但回应丢失重放 | 一次新Revision/一次PortionTransfer；不再次加量/重复分期 | [原文 L10609](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10609>) |
| PO-AC45 | C01～C18正常100/已收40/取消60 | 取消后有效40、实收/验收/开票各原值；Source自身处置后才CONFIRMED | [原文 L10610](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10610>) |
| PO-AC46 | 两取消分别60和20共享只剩60 | Hold/预算分片互斥，不能累计取消80；未通过那份不隐式缩量 | [原文 L10611](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10611>) |
| PO-AC47 | C11待取消Hold已安装，新接收同份额10 | 拒发新Permit；不影响独立其他SourcePortion可收范围 | [原文 L10612](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10612>) |
| PO-AC48 | R01另10先有Permit但消息未到PO投影 | I10持续保护，不能按Received40取消60；待原结果 | [原文 L10613](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10613>) |
| PO-AC49 | R01真收到10后原申请60 | R02最大50，原60REJECTED；不沿用批准自动取消50；显示实收50 | [原文 L10614](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10614>) |
| PO-AC50 | 取消完整截止先完成，R04申请新Permit10 | R05拒新接收；没有新的合法WMS效果应由该路径产生 | [原文 L10615](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10615>) |
| PO-AC51 | R06真实漏报10反证截止完整性 | R07 received50/Q40/overExecution10；原Cancel60留存；Source差异处理而不是截断实际量 | [原文 L10616](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10616>) |
| PO-AC52 | Cancel已应用、Source处置请求超时 | 商业APPLIED/SourcePENDING；同OperationKey查询不返两次，PO不直接改PR | [原文 L10617](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10617>) |
| PO-AC53 | Source处置已确认60后收到晚10事实 | R08精确SourceConflict通知，不自行加回/扣除源R；影响范围新用受阻 | [原文 L10618](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10618>) |
| PO-AC54 | 从共享PO只取消Source-A剩余10 | Source-B/其他PO/其他Schedule量和供给不变；不能整单Cancel | [原文 L10619](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10619>) |
| PO-AC55 | 取消遇未决接收Lease到期或删除Outbox | 不以TTL/删除证明无在途；Hold/Permit仍保全，需真实所有路径证明 | [原文 L10620](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10620>) |
| PO-AC56 | 申请withdraw-unapplied但Supplier取消已成立、本地未知 | 不直接解除Hold；保持UNKNOWN并核原Owner效力 | [原文 L10621](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10621>) |
| PO-AC57 | RECEIVING close而Source/质量/会计还有各自待办 | 只满足其明确门可关闭相应对象；不能宣称全链财务结案 | [原文 L10622](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10622>) |
| PO-AC58 | 曾关闭后新事实更正/迟到产生异常 | 原Close凭证保留，新异常显示；不自动再给收货许可或删原Closed历史 | [原文 L10623](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10623>) |
| PO-AC59 | E24重复两次或WMS/PUR同一Receipt重复报告 | Received仍40；按指定Producer/ReceiptSlice不双加为80 | [原文 L10624](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10624>) |
| PO-AC60 | E24→E27→E30依次接入 | E26/E29/E32分别40/0/0、40/40/0、40/40/40；保护量始终40 | [原文 L10625](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10625>) |
| PO-AC61 | 独立Fixture：GR40、有效验收30、AP Posted20 | 三量准确40/30/20，不自动拉齐、不合计90履约；展示阶段差异 | [原文 L10626](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10626>) |
| PO-AC62 | 缺QA结果或免检凭证的Acceptance | 不PASS；保留待核，不能用PostingBasis自动验收 | [原文 L10627](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10627>) |
| PO-AC63 | AP草稿/已匹配但未POSTED vs 合法Posted | 前者不进入invoicedPosted；后者按账套/行/分配一次计量 | [原文 L10628](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10628>) |
| PO-AC64 | AP/验收先于关联Receipt到达 | 保存真实证据为待关联，指标partial/异常；不造Receipt消除空引用 | [原文 L10629](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10629>) |
| PO-AC65 | Receipt同identity更正40→30，再重复更正 | 当前显示30非70；旧40和证据保留；未处理物理依赖不自动释放10预算 | [原文 L10630](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10630>) |
| PO-AC66 | Fact v3先到、缺v2，或同v2不同内容 | 缺链隔离补原版本；同版异内容Conflict；不能最后收到者覆盖 | [原文 L10631](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10631>) |
| PO-AC67 | 多allocationKey指同原单据行但分量重叠/缺完整清单 | 不能标完整累积/取消许可；待Owner行分配manifest，不按新增Key再计 | [原文 L10632](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10632>) |
| PO-AC68 | 同invoiceNo出现在两个账套/不同分配 | 正确分域，两真事实不误去重；同一账套/identity重放不重复 | [原文 L10633](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10633>) |
| PO-AC69 | 真实收100退20，AP信用10 | 三种原事实分开，物理净80/票据净各自有据；不自动开放再收20或取消已履约 | [原文 L10634](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10634>) |
| PO-AC70 | 原票据VOID_WITH_EVIDENCE但真实货仍在且有后继使用 | 文书显示更正，P保护不能被无证归零；继续Owner处置 | [原文 L10635](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10635>) |
| PO-AC71 | RECEIPT来自无权Producer、跨Tenant或伪造poLineId | 403/404，无非法投影；已知无效对象不自动生成PO | [原文 L10636](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10636>) |
| PO-AC72 | Qty原单位BOX换EA，或不同物料均EA | 有据转换后分portion计；不同物料/责任不串余额；超scale拒静默舍入 | [原文 L10637](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10637>) |
| PO-AC73 | 新系统未拉齐历史且没有任何消息 | UNKNOWN/null，不以0宣告可取消全量或已结清 | [原文 L10638](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10638>) |
| PO-AC74 | 对账重建期间新的事实版本同时提交 | 用FulfillmentCursor CAS重算，不覆盖新事实；不调用WMS/AP执行 | [原文 L10639](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10639>) |
| PO-AC75 | Draft/仅OA Approved的PO给Planning | qualified=false；确认后相应Schedule才true；Lineage未知不伪造 | [原文 L10640](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10640>) |
| PO-AC76 | PO100收40后SupplyOutbox重复、乱序 | 当前剩60且对应Receipt覆盖；不能原计划100＋PO100＋Receipt40；旧revision不回退 | [原文 L10641](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10641>) |
| PO-AC77 | Schedule退役/取消消息后消费者漏过中间版 | 完整集合含退役键，最终不残留旧供给；其他Source份额独立 | [原文 L10642](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10642>) |
| PO-AC78 | A23新基线Release到达 | A24只RECEIVED/接受或就绪，旧PO技术不漂移；APPLIED须真实采用Ref | [原文 L10643](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10643>) |
| PO-AC79 | 同Release一个Site采用另一Site缺证 | 回执按target/scope分开，一个成功不覆盖另一个缺项 | [原文 L10644](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10644>) |
| PO-AC80 | 原物料停供反馈/替代建议，重复同SourceFact版 | 同反馈Key复用；来源不同版有后继；收到反馈不宣称已改善或取消PO | [原文 L10645](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10645>) |
| PO-AC81 | ECO评估水位后新建/改技术PO | 原Impact标STALE/Delta；最终动作核当前范围，不能一次where-used永远完整 | [原文 L10646](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10646>) |
| PO-AC82 | 有权ECO HOLD_NEW_USE与接收/创建竞争 | 共Scope最终锁决定先后，先发生真实结果保留；不把通知视为全域原子撤回 | [原文 L10647](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10647>) |
| PO-AC83 | ECO继续旧版但缺用途/数量/到期许可 | 拒实际应用；不长期豁免Quality或更改PLM主数据 | [原文 L10648](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10648>) |
| PO-AC84 | ECO Change/Cancel只创建请求而未实际完成 | 回PENDING/BLOCKED而非APPLIED；原ExecutionKey恢复不新建另一动作 | [原文 L10649](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10649>) |
| PO-AC85 | 不同PO的ECO项部分成功 | 每项原结果保留；不全包回滚已收/已建PO；未决逐项处置 | [原文 L10650](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10650>) |
| PO-AC86 | 用户失权后合法AP/GR机器结果到达 | 事实接收、用户敏感读/下载拒绝；机器权不转授用户 | [原文 L10651](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10651>) |
| PO-AC87 | History不同资源都有ResourceSeq1，晚事件原时间更早 | 按本PO TimelineSeq稳定排序/分页；不跨资源比较1，不漏真实晚事件 | [原文 L10652](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10652>) |
| PO-AC88 | 导出授权结果超过10000或生成期间撤权 | 超限拒完整导出、失权不发旧范围文件；原数据不变，不输出半文件 | [原文 L10653](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10653>) |
| PO-AC89 | 注入未知DTO成员、跨Tenant从表、任意sort/URL | 400/403/404及数据库复合边界，参数化/固定白名单；不泄SQL | [原文 L10654](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10654>) |
| PO-AC90 | 显示旧Qty21,8无法无损进入19,6或旧Confirmed=2 | LEGACY原语义读取；新写适配被挡，不截精度、不自动SupplierConfirmed | [原文 L10655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10655>) |
| PO-AC91 | 本地命令未观察与fence并发 | 同锁先者裁决，原执行成功不被取消墓碑抹去；不取消远端工作 | [原文 L10656](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10656>) |
| PO-AC92 | 真实生产注册DEMO或Owner非同库无等价协议 | 真实新效果NOT_READY/BLOCKED；本地只读/草稿可用，不能把mock当生产 | [原文 L10657](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10657>) |

| ID | 准确输入和先后 | 响应、状态与持久不变量 | 执行 | 出处 |
| --- | --- | --- | --- | --- |
| PO-REV1-AC01 | 同Supplier接受ResponseRef/seq1重复运输，换HTTP键 | 同Ack/Response业务结果，HeadRevision不重复推进，无第二Confirmation | NOT_RUN | [原文 L10669](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10669>) |
| PO-REV1-AC02 | V01接受后V02同流seq2 COUNTEROFFER，再用原Ack确认 | 409 PO_ACK_SUPERSEDED；当前头seq2，旧ACCEPTED历史保留 | NOT_RUN | [原文 L10670](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10670>) |
| PO-REV1-AC03 | 接受后精确REJECTED或WITHDRAWN后继 | 新确认拒绝，不按旧Ack.verified=true放行；不自动取消整PO | NOT_RUN | [原文 L10671](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10671>) |
| PO-REV1-AC04 | 两个合法60/40 Schedule，只有60后继拒绝 | 两个Head独立；40合法接受仍可确认，60不能借40头放行 | NOT_RUN | [原文 L10672](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10672>) |
| PO-REV1-AC05 | seq2先到，seq1晚到；另测同前驱两seq2 | 缺链先阻新确认；补齐正确链不回退；分叉CONFLICT不按RecordedAt取胜 | NOT_RUN | [原文 L10673](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10673>) |
| PO-REV1-AC06 | 确认事务和后继COUNTEROFFER争同Head锁 | 后继先则拒旧确认；确认先则原Confirmation/confirmedAt保留并追加SUPPLIER_COMMITMENT_CHANGED | NOT_RUN | [原文 L10674](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10674>) |
| PO-REV1-AC07 | 证据Owner只给旧当前声明/无前驱链，或跨Supplier伪Head | 未证当前性422/503或403/404；不能自己按收件时间补链 | NOT_RUN | [原文 L10675](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10675>) |
| PO-REV1-AC08 | HELD_NOT_STARTED许可先取得，技术Hold后到，再start | BLOCKED/409；everStarted=false、无GR/WMS新效果，I直到合法无效果证明才释放 | NOT_RUN | [原文 L10676](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10676>) |
| PO-REV1-AC09 | 技术Hold先提交，再申请Permit | 拒发新许可；无新BudgetSegment占用，原历史仍读 | NOT_RUN | [原文 L10677](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10677>) |
| PO-REV1-AC10 | E21a/start成功IN_FLIGHT，然后技术Hold先于commit | REVOKE_PENDING；新GR commit拒绝；原可能效果仅query/收证，不拿start免硬停 | NOT_RUN | [原文 L10678](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10678>) |
| PO-REV1-AC11 | GR最终事务先提交USED，技术Hold后到 | 原Receipt/PermitConsumption保留；同原回放返回历史，不再执行WMS | NOT_RUN | [原文 L10679](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10679>) |
| PO-REV1-AC12 | Cancel REQUESTED与未开始Permit争锁 | 屏障先则旧start拒；Permit仅HELD不获排空权；无效果证明后片转CancelHold且不双计I/H | NOT_RUN | [原文 L10680](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10680>) |
| PO-REV1-AC13 | 原Receipt已start后Cancel/Change软屏障到达 | 仅登记在drainPermitIds的原量可完成；实际10完成后原取消60拒绝，不自动改50 | NOT_RUN | [原文 L10681](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10681>) |
| PO-REV1-AC14 | 已批准Receiving Close apply遇HELD/IN_FLIGHT | 200 BLOCKED＋barrierRef；未开始禁start、已开始按软排空；全部结清且其他门齐后同Closure重apply才CLOSED | NOT_RUN | [原文 L10682](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10682>) |
| PO-REV1-AC15 | Permit过期、租约过期或本地删除队列，无Owner no-effect | 状态BLOCKED/UNKNOWN、I保留；不重新分配旧片 | NOT_RUN | [原文 L10683](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10683>) |
| PO-REV1-AC16 | 多片有真实部分USED，其余未知后取得完整无效果证据 | 先UNKNOWN、仅已用片P；最终SETTLED_PARTIAL，不谎称全量USED；总片量与Permit一致 | NOT_RUN | [原文 L10684](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10684>) |
| PO-REV1-AC17 | Change评估后新10 Permit出现，apply60 | 有在途则202同Work监视/排空；确认10使可转仅50时拒原60；无新的Revision/Transfer | NOT_RUN | [原文 L10685](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10685>) |
| PO-REV1-AC18 | W02 Change202后进程重启 | GET原Work仍存在/同id、holds、依赖；resume同Work，不另创建Change/Source请求 | NOT_RUN | [原文 L10686](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10686>) |
| PO-REV1-AC19 | 新增量SourceClaim准备成功但回传丢失 | UNKNOWN后按原operationKey query得原Claim；不申请第二份Grant/Claim | NOT_RUN | [原文 L10687](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10687>) |
| PO-REV1-AC20 | 两worker leaseEpoch1/2，旧实例晚回来 | 真实Owner结果可入Inbox；epoch1不能提交，只有有效epoch2一次ChangeApplication | NOT_RUN | [原文 L10688](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10688>) |
| PO-REV1-AC21 | Change final commit成功但响应丢失，apply/resume再来 | 原Application/newRevision/TransferIds不变；SourceUse仅一次 | NOT_RUN | [原文 L10689](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10689>) |
| PO-REV1-AC22 | Change Work存在，用户abandon先赢；Source效果尚UNKNOWN | finalApplicationFenced=true，202/CLEANUP_PENDING；不再新增Revision，不无证解Hold | NOT_RUN | [原文 L10690](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10690>) |
| PO-REV1-AC23 | final apply先赢随后abandon | 409 PO_CHANGE_ALREADY_APPLIED，旧新修订及历史不回滚 | NOT_RUN | [原文 L10691](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10691>) |
| PO-REV1-AC24 | 同ChangeId的另一次apply改approval或scope/body；或旧Work token恢复 | 409 Apply内容冲突/WorkVersion冲突；原Work参数不变，不重基新意图 | NOT_RUN | [原文 L10692](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10692>) |
| PO-REV1-AC25 | supplierAgreementRef=null且真实策略要求，已批准取消 | 可DRAFT/审批/202 DRAINING；无当前同意无Application/QuantityEntry，不伪零费用 | NOT_RUN | [原文 L10693](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10693>) |
| PO-REV1-AC26 | 上例批准后才收到绑定原hash/scope的同意D02 | Seed和InputJson/hash不变；新增DependencyResult/Head满足该门；其余门齐才能提交 | NOT_RUN | [原文 L10694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10694>) |
| PO-REV1-AC27 | 同意后D03 WITHDRAWN或同前驱冲突，最后事务未提交 | Head不SATISFIED，取消未APPLIED；不继续引用旧同意 | NOT_RUN | [原文 L10695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10695>) |
| PO-REV1-AC28 | 动态同意新增违约费或把60改50 | PO_CANCEL_TERMS_CHANGED；不是满足原批准，必须新提案/审批 | NOT_RUN | [原文 L10696](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10696>) |
| PO-REV1-AC29 | C14商业取消已应用但Source网络未知 | Application稳定、取消60真实、SourcePENDING；同Key只查原处置，不能第二次返60 | NOT_RUN | [原文 L10697](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10697>) |
| PO-REV1-AC30 | C14/C18重复读取及R08晚10差异 | appliedCancellationRef、Source请求、Supply因果、晚事实全部指同一Application；Ref可查不可变正文 | NOT_RUN | [原文 L10698](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10698>) |
| PO-REV1-AC31 | AP Producer伪Source/Supplier证据；或异work/hash的GR cutoff | 类型/身份拒绝，原Head/Application不变；不靠字段相似放权 | NOT_RUN | [原文 L10699](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10699>) |
| PO-REV1-AC32 | 最终取消前证据集头变化、后续Supplier撤回或Source拒绝 | 提交前CAS阻；提交后保留Application并另建差异/源待办，不能抹真实取消或自动恢复接收 | NOT_RUN | [原文 L10700](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10700>) |

SQL Server实际验证必须覆盖唯一约束、共享Owner事务、rowversion/范围锁、最后校验、回执幂等和取消竞争，内存库不能替代。E2E应跑原两连续链和真实前响应绑定，FE验证披露、不同完成轴和RESULT_UNKNOWN。设计性能目标十万PO／百万事实、列表20行p95≤1秒、详情≤800ms属于未来约定环境指标，本轮未测。 [原文 L10661](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10661>)、[原文 L1685](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1685>)

## 10.1 完整对象配方与编码核对

主文113个JSON对象补充了页面请求、Owner事实、持久查询、拒绝与恢复链。各反例明确使用独立初始状态，不能把A/V/P/W/D或两条R竞争支路串成一次业务。`DEMO`真实模拟措辞只说明夹具设定，生产端仍拒绝DEMO证据。

| 对象链 | 必须实现的前后关系 | 准确原对象 |
|---|---|---|
| E01–E04：RFQ完整组创建 | 上游manifest、Claim、Grant和100 EA原样承接；100 EA按原报价10 BOX×12=120 CUA，另保一次性运费30 CUB，不换币合并。执行返回真实PO/行/Effect后才可查初始DRAFT；原商业/交期快照仍保PENDING确认锚点。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1755>) |
| E05–E13：批准与发出 | PO_RELEASE subject为准确Revision/内容hash；OA回调APPLIED不发订单。release使用当前Order/Revision token和披露证据，产生ACTIVE文书及dispatch；SENT只证明投递，仍未商业确认。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:2587>) |
| E14–E19：回复与本域确认 | Supplier接受的有效时刻13:34与本域应用13:36分开；确认前读完整当前声明并CAS response-head。以13:34的当地日期及冻结日历算5工作日为10月8日，原DeliverySnapshot不回填。Planning供给100由确认应用因果产生。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:3743>) |
| E20–E26：接收许可与实收 | 稳定GR ReceiptIntent与Permit start共同事务；HELD_NOT_STARTED→IN_FLIGHT→USED，guard1→2→3。commit绑定Permit版本、全guards、startRef、原operation和ReceiptIdentity/Slice；USED后只有实收40，不自动验收或开票。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:4513>) |
| E27–E33：验收、发票与供给 | 验收/已Posted AP各有不同Producer/原单据/版本，均指同40切片。累计40/40/40保护的是同一40，不能加成120；Supply后继覆盖该Receipt，仅剩60。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:5002>) |
| C01–C13b：取消工作 | 用户明确选60、评估不锁量、PO_CANCEL另审。apply安装Hold/guard4并持久Work，六项Supplier/Fee/GR/Source/QA/AP请求从PENDING到准确当前结果；READY仍NOT_APPLIED。六项是本fixture政策，不能复制成所有PO默认强制或默认免审。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:5393>) |
| C14–C18：商业取消与来源处置 | 最终事务只产生一次CancellationApplication、60量条目和因果Supply；有效订量40，原收/验/票仍40。SourceDisposition另按原Effect/Grant/Portion处置60，C14 PENDING到C18 CONFIRMED不改原Application，既非软Claim释放也非财务结案。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:6930>) |
| R01–R09：两种晚到10 | 先已取得独立Permit91的10使保护量50，原取消60拒绝且不自动改50。另一个分支取消先成功则新Permit10拒；若有真实漏报10，登记50/40的超执行和cutoff冲突，再通知Source差异处理；不能删迟到事实或直接本地返额。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:7333>) |
| A01–A26：其余页面与更正 | 无RFQ准备不能伪RFQ身份；返回draftLineId后再解析准确授权。新增拆期只转移未来60为30/30，已履约40留原身份。工程RECEIVED不改旧PO；Export QUEUED/hash null不显示已下载；NOT_OBSERVED不授权重造。Receipt后继30替换旧40而非累加70，实际WMS40义务未解除前仍保护40；实物退10不等贷项也不自动补供。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:7740>) |
| V01–V04：当前供应商回复 | ACCEPTED原头被精确前驱COUNTEROFFER替代，旧头新确认返回PO_ACK_SUPERSEDED；若原确认早已完成则保历史确认并新增异议，不回滚真实事实。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:8706>) |
| P01–P03：开始后硬停 | 已经start的40遇技术Hold变REVOKE_PENDING；拒绝新增commit仍保原在途保护。仅Owner证明原操作无效果、覆盖MAIN_GR_RECEIPT/WMS_RECEIVING_ADAPTER全路径才允许close-no-effect，期限或本地删队列不够。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:8810>) |
| W01–W07：变更恢复 | 202只有持久ChangeWork，未生成NewRevision。重启resume原Work、lease1→2、原GR operation回执使三项依赖满足，再固定EvidenceSet。最后事务生成唯一新修订和两个30 Transfer，既不新增Claim60也不重造SourceUse。旧lease只能收真事实不能提交。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:8941>) |
| D01–D04：审批后同意与撤回 | 原supplierAgreementRef=null保持不变；D02同意只满足原subject/scope，D03按准确前驱WITHDRAWN使当前Head不能READY，Application仍null。新费用/改量应新提案审批，不改已批准Input。D04短清单只展示未满足项，不能推定其他策略门不存在。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10161>) |

82条跨步骤绑定已按原JSON逐值和类型比较全部相等；实际实现仍必须从当前真实响应取ID/token，不能硬编码这些UUID。文书hash只含冻结业务内容；不含状态、rowversion、当前确认/supplyDate；取消内容hash剔除expectedOrderVersion而保理由/商业证据，scopeHash取准确slices。[82绑定](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10463>)、[摘要投影](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10554>)

## 11. 整合待证与退出条件

| 保留项 | 本稿已经确定 | 实际还需谁提供/影响的动作 | 出处 |
| --- | --- | --- | --- |
| IAM/BP/技术/Quality用途 | 明确活动/范围/字段/机器类型和拒绝码 | 实际有效身份、Supplier/Site、用途/偏离/Quality规则；只阻相应新使用/披露，不删历史 | [原文 L10784](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10784>) |
| Source/Scope最终采用 | 固定Grant/Creation身份、准确全组SourceUse和同库最后守卫；未决不重建 | 真实Main事务拓扑、共享Guard锁映射、Claim/父预算/fence/安全处置采用；真实创建/源返额不能靠mock | [原文 L10785](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10785>) |
| PO审批/确认/取消/关闭 | 四类审批Subject、内容hash与token分开、Supplier锚点机制、可恢复Cancel/Close | 实际矩阵Role/风险阈值/免审证据、Supplier同意及取消费用政策、明确close必需项；不擅自批准免费取消或无需Supplier确认 | [原文 L10786](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10786>) |
| 原商业/金额/日历 | 原币与比较币分开、模式/费用一次发生、冻结日历、目标交期表达 | 真实税率/FX/舍入/结算条款及calendar原版本、目标存储/商业适配；未知不0/不今天 | [原文 L10787](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10787>) |
| GR/QA/AP事实与接收截止 | Producer类型、分配粒度、版本替换、五指标和预算分片、原事务/补偿路径 | 真实Receipt/Movement/判定/AP Posted/Allocation manifests与完整水位、全部受理入口截止证明；否则准入/取消保留不确定 | [原文 L10788](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10788>) |
| 超收/先票后货/返还后补供 | 不把原事实删掉，不默认新增权限 | 准确Owner例外授权/容差/补供再授权；本稿不新放开这类业务政策；已有真实异常仍留存 | [原文 L10789](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10789>) |
| Planning/PLM | 有谱系完整Supply版本、技术Receipt阶段、ImpactDelta和ExecutionKey | 真Consumer采用、实际Source谱系/工程用途/执行许可；不把发送成功当对方已应用 | [原文 L10790](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10790>) |
| 旧存量和Provider | 原号状态保留、精度差异明示、不双写、生产拒DEMO | 正式切换批次/字段映射、老21,8值与新19,6无损策略、真实SQL并发测试；非SQLServer等价机制另验 | [原文 L10791](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:10791>) |

已接受的四项产品决定与实际采用缺口分开：真正未证的是PR/Source父预算、RFQ Scope、PO创建／Change／Cancel和GR所有新效果writer是否共一事务/同锁，含旧入口；Supplier/Dependency完整当前链、全路径cutoff、实际IAM/矩阵/金额/日历实例与真事实分配manifest。只有具体Owner实例／参与协议与并发恢复证据才能解除对应真实效果门，不能由本整理代签。 [原文 L53](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20338b1d82230ce0__PUR-PO-ACCEPTANCE.md:53>)

RFQ/PR状态命名差异已有PO L693的明确层次解释，保留为实施适配核对，非新设计矛盾。Source/GR跨库无等价协议一律关对应新效果；仍收真实后到事实、保原成功。GR后继如何实现Permit及质量总体身份须结合其当前原案，不能凭PO状态机假定库存/QA已采用。 [原文 L693](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:693>)、[原文 L1187](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1187>)、[原文 L1197](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1197>)、[原文 L1246](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:1246>)

## 12. 本轮实际阅读与保留边界

状态：**required_materials_consolidated**。正文10839行全部有效语义已整理：原1992核心行和124AC继续复用，新增113个JSON的唯一字段/对象、两条连续主链及独立A/V/P/W/D支路、全部原跨步骤说明和82绑定已实读。97精确重复子树不重复逐字阅读；SHA64字面作身份元数据。CURRENT24、UA74、MD02限定复审166行全文证据保留；四项产品选择已由接受关闭，不再列作待用户决定。

本轮做的是113对象解析和82声明字段的静态精确对照，未运行原作者脚本、源代码、SQL、构建、业务测试或Actions。124AC均NOT_RUN；同库所有writer、实际Owner证据和当前政策实例仍未证。

五个旧PO源码的固定commit/blob和用途已经按主文§17.2分类：旧Controller路由/权限、Service默认价税和Confirmed、旧DTO字段丢失、Order旧状态/币种、Line的21,8→19,6精度差异。这里只承接作者已限定的历史静态分析，本轮未另行全文审这些源码，也不假称它们代表最新main。其余L3/L4历史载体继续保留来源索引身份，不能计作本轮逐件全读。

证据： [113对象语义投影](<D:/CP6-archives/consolidation-20261010/commercial-cache/po/fixture-reading-view.json>)、[97精确复用](<D:/CP6-archives/consolidation-20261010/commercial-cache/po/fixture-exact-reuse.json>)、[82绑定核对](<D:/CP6-archives/consolidation-20261010/commercial-cache/po/fixture-bindings-comparison.json>)、[完整阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。
