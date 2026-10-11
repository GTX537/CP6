# PUR-GR-01 采购收货商业单据

## 1. 目的、操作者与明确业务边界

采购收货人员依据可信到货和已授权PO逐行登记、匹配、冻结、准备、开始并确认本次实际收货；Quality专业人员出具独立决定，GR只应用商业验收；恢复人员查询原操作和有据续办。GR拥有商业收货与验收事实，WMS拥有物理库存，Quality拥有判定，三个结果不能互相代签。保存登记不增库存，WMS收到不等GR验收，Quality合格也不等AP/资产确认。 [原文 L17](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:17>)、[原文 L28](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:28>)、[原文 L29](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:29>)、[原文 L30](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:30>)、[原文 L31](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:31>)、[原文 L32](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:32>)、[原文 L33](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:33>)

| 事实 / 权威Owner | GR保存什么 | GR绝不做什么 | 出处 |
| --- | --- | --- | --- |
| Procurement GR | 登记、收货意图、商业收货版本、原商业来源、验收应用和跨域引用 | 不把自己的Received字段当库存余额 | [原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:39>) |
| PO | 准确行/交期、Source份额、Permit/控制/消费、履约投影 | 不重定义Permit枚举、不直接UPDATE PO累计/取消数量 | [原文 L40](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:40>) |
| WMS-IN / WMS-STOCK | 物理收货单、Movement/Root/StockReceipt引用及只读观察 | 不建第二份余额、Root、库存移动或容量占用主账 | [原文 L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:41>) |
| Quality / QA-IQC | 策略/Task/Disposition原件镜像及本域应用回执 | 不批准检验、抽样、让步；空结果不PASS | [原文 L42](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:42>) |
| Planning | 以PO及Stock原回执为组成的交接关联，不新增供给数量 | 不运行MRP、不造Demand、不累计计划+PO+GR+Stock | [原文 L43](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:43>) |
| PUR-MATCH / Finance | 只读ReceiptLine/验收分区/原币条款基准，实际外来票据链接 | 不登记发票、不创建AP、不以入账基准决定免检 | [原文 L44](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:44>) |
| PUR-RETURN / WMS-OUT | 已发生退货的准确来源链接及只读累计 | 不倒改历史实收，不在GR发起另一个退货业务流程 | [原文 L45](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:45>) |
| PR / Source / PLM | 原授权、工程用途和已接受PO提供的出处 | 不制造Source Grant/返额，不自动漂移旧技术版本 | [原文 L46](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:46>) |

一个ReceiptOperation固定同一PO/确切Revision/Supplier/Site，1–200条商业行，一个正常原子物理清单、一个PO Permit及总RECEIPT身份。每行一个Schedule、物料/技术/批序/货权/目的地组合，可含多个互不重叠SourcePortion。不同PO必须明确拆成业务操作并保同到货原件；重试不是另一批，超上限不能静默拆组。 [原文 L52](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:52>)、[原文 L54](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:54>)

| 轴 | 规范状态/结果 | 显示和约束 | 出处 |
| --- | --- | --- | --- |
| 登记 | DRAFT / FROZEN / WITHDRAWN | 仅登记/冻结不是收货 | [原文 L60](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:60>) |
| 执行工作 | PREPARING / READY / STARTED / RUNNING / WAITING / UNKNOWN / SUCCEEDED / REJECTED / SEALED / PARTIAL_FACTS | UNKNOWN无新许可，PARTIAL_FACTS不称正常全收 | [原文 L61](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:61>) |
| PO Permit | 完全复用PO.PoPermitState | 只保存引用和观察，不在GR修改权威状态 | [原文 L62](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:62>) |
| 物理 | NOT_OBSERVED / POSTED / PARTIAL / UNKNOWN / SEALED_NO_EFFECT | 以WMS业务回执为准；未观察不等0 | [原文 L63](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:63>) |
| 商业收货 | NOT_POSTED / POSTED / RECONCILIATION_REQUIRED | 正常同库同时成立，外来已收可物理POSTED/商业待补 | [原文 L64](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:64>) |
| 质量 | UNKNOWN_POLICY / AWAITING_TASK / PENDING / PARTIAL / DISPOSED / CONFLICT | 有REQUIRED策略且Physical已知时，任务失败仍Pending，不放行 | [原文 L65](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:65>) |
| 库存质量应用 | PENDING / APPLIED / NONCURRENT / CONFLICT / UNKNOWN | Quality决定不推定库存已可用 | [原文 L66](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:66>) |
| 商业验收 | PENDING / PARTIAL / APPLIED / CONFLICT | 每版保留Accepted/Rejected/Held/Pending分量，不增加Physical | [原文 L67](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:67>) |
| 出站消费者 | PENDING / RECEIVED / APPLIED / REJECTED / UNKNOWN | PO、Planning、MATCH只读引用各自独立 | [原文 L68](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:68>) |

PhysicalReceived是本次WMS毛实收，CommercialReceived是当前有效商业事实；Accepted/Rejected/Held/PendingInspection是有效检验范围的互斥分区；Returned为独立实物退货。OrdinaryAvailable必须读当前WMS用途、质量、预留和冻结联合观察。不同单位、技术与货权不合成无意义总数。 [原文 L70](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:70>)

## 2. 当前正文、接受与缺陷关闭

| 原件 | SHA-256 | 用途 | 入口 |
| --- | --- | --- | --- |
| v1.1 REV1主文 | cc20531bdcc61959b77b395e8baa955e65a70fef0d84f4b364c166d3cc89a3ee | 323753 bytes／7013行完整当前Spec | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:1>) |
| CURRENT | b1695dc42c5de63dd49bd8724161a8ba258d7b35d0ad2b1be28ead7f3ab509a9 | 已接受D-GR01–10；111AC NOT_RUN | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b1/b1695dc42c5de63d__CURRENT.json:1>) |
| 正式UA | 813f4f021684457bade3a2d3cf80fec3a2f28323dbd2260af80413171482cb0f | USER_AUTHORIZED_DELEGATE，准确原字节2026-09-30接受 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:1>) |
| 最终限定复审 | 9bb1f6c7face0256247b1d9c5e360d06e94e6ae9346aaa2eb4cc471dd9d4eb7e | 非前缀VOID范围修复关闭；不授权实现 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/ce/cef7ca5bfb22497a__SR-20260930-B-PUR-GR-MD01-FINAL.md:1>) |

正式UA接受准确全文及D-GR01–10，不重写字节。正文DRAFT／PROPOSED、PR当时未完成、WMS-IN整册当时未完成等均须按各Target后继CURRENT解读；这些历史句子不降低当前GR效力，也不能拿邻域后继直接替换GR内的准确接口组合。默认MAIN_ATOMIC、原坐标有效范围、6/8位桥、Work恢复和Planning非追加供给均已接受，不再问同一产品选择。 [原文 L16](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:16>)、[原文 L22](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:22>)、[原文 L23](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:23>)、[原文 L24](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:24>)、[原文 L25](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:25>)、[原文 L26](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:26>)、[原文 L27](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:27>)、[原文 L28](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:28>)、[原文 L29](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:29>)、[原文 L30](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:30>)、[原文 L31](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:31>)

最终评审确认非前缀VOID已修复：原[0,100)作废[20,40)得到[0,20)∪[40,100)、量80，不能重编号[0,80)。Owner区间证据、Quality绑定、Commercial/ScopeSet/Acceptance/PO版本一致、CAS与MATCH409均具备。82 JSON及七摘要复算是原静态检查；111AC全NOT_RUN，O-GR01–12实际采用/政策/运行仍未证。 [原文 L18](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/ce/cef7ca5bfb22497a__SR-20260930-B-PUR-GR-MD01-FINAL.md:18>)、[原文 L20](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/ce/cef7ca5bfb22497a__SR-20260930-B-PUR-GR-MD01-FINAL.md:20>)、[原文 L22](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/ce/cef7ca5bfb22497a__SR-20260930-B-PUR-GR-MD01-FINAL.md:22>)、[原文 L31](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/git-objects/ce/cef7ca5bfb22497a__SR-20260930-B-PUR-GR-MD01-FINAL.md:31>)

## 3. 字段、域身份与持久对象

公开 /api/pur/gr/v1、内部 /internal/pur/gr/v1 是拟议新协议。Tenant/Actor来自认证，DTO不接tenantId/状态/累计/任意Owner地址。完整POST可空成员显式null；PATCH只列明字段，遗漏不变、null只清可空、数组整体替换。未知/重复属性、错误大小写枚举整体400；普通请求2MiB、200行、每行50来源分量。 [原文 L118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:118>)、[原文 L120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:120>)

采购输入Qty decimal(19,6)字符串，Stock八位数量，6→8补零准确，8→6必须无损。真实外来八位量保原值，不能表达只阻对应PO投影；normal新效果无法无损则挡。分别存输入、Stock基本、PO SourceUom及准确有理比例/版本；聚合decimal(38,8)不混单位。GR毫秒显示不覆盖Stock原7位时间及摘要。PO.Ref无digest，Stock.EvidenceRef有digest，必须解析原证据取得hash，不补假摘要。 [原文 L122](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:122>)、[原文 L124](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:124>)

| 字段 | 页面/默认/必填 | 持久位置与约束 | 出处 |
| --- | --- | --- | --- |
| arrivalEvidenceRef | 到货凭证选择；无默认，草稿可尚未绑定 | Arrival原业务身份，凭证Owner/稳定event及line；同一凭证新扫描不能新造到货 | [原文 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:130>) |
| poId / poRevisionId | 从可收工作台返回；Supplier/Site只读 | ReceiptOperation、FrozenIntent；历史修订可看，正常新收依当前允许范围 | [原文 L131](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:131>) |
| arrivalId / arrivedAt | 服务器验证的到货记录和真实时点；未定草稿可null | 不默认“现在”作为实际到货；Confirmed实际收货时点来自WMS | [原文 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:132>) |
| lineId | 新增null，保存返回；编辑不得按行号替换别人的ID | ReceiptLine.Id，正式冻结后不变 | [原文 L133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:133>) |
| arrivalLineKey / arrivalRange | 原到货行及本次非重叠份额；草稿可null | ArrivalSlice，范围在该到货行原单位中，不把PO订量当本次实际到货 | [原文 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:134>) |
| poLineId / scheduleId | 精确稳定ID，禁止仅按Item/LineNo自动匹配 | ReceiptLine与MatchSnapshot；父子和所属PO校验 | [原文 L135](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:135>) |
| inputQty / inputUom | 实收拟办数量和单位；草稿可null，冻结必填 | Line数量；源到货范围长度需换算后相等 | [原文 L136](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:136>) |
| sourcePortions | 明确PO份额及源单位量；草稿可[] | LinePortion；转换后合量等行量、同份额不得重复 | [原文 L137](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:137>) |
| dimensions / destination | WMS返回的Stock维度/地点；无默认RECV | 原证据快照及WmsIntent；NORMAL仅LOCATION | [原文 L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:138>) |
| valuationRef | 来自原PO商业及计价Owner；可待证 | ValuationSnapshot；未知不是0，也不决定Quality | [原文 L139](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:139>) |
| inspectionPolicyHint | 只作请求所选政策版本提示 | Quality实际解析结果才是准入，不能body选NONE绕过 | [原文 L140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:140>) |
| notes | 纯文本，null默认 | 草稿可改；冻结后附加注释事件，不改原摘要 | [原文 L141](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:141>) |

### 全部GR规范DTO：字段、状态、输入输出与scope对象

[原文 L147](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:147>)

```ts
type Id = string;
type Key = string;
type Version = string;
type Seq = string;
type Qty = string;
type WideQty = string;
type Instant = string;
type Hash = string;
type Ref = PO.Ref;
type ERef = STK.EvidenceRef;
type Mode = 'REAL'|'DEMO';
type Coverage = 'COMPLETE'|'PARTIAL'|'UNKNOWN'|'CONFLICT';
interface Gate { code:string; allowed:boolean; evidenceRefs:Ref[]; missing:string[]; }
interface ErrorItem { field:string; code:string; message:string; }
interface Problem { status:number; code:string; title:string; detail:string; commandId:Id|null; traceId:string; currentVersion:Version|null; retry:'NONE'|'SAME_KEY'|'QUERY_ORIGINAL'|'NEW_AUTHORIZED_INTENT'; errors:ErrorItem[]; }
interface Mutation { expectedVersion:Version; }
interface ReasonMutation extends Mutation { reason:string; }
interface CommandResult { commandId:Id; outcome:'SUCCEEDED'|'ACCEPTED'|'REJECTED'|'CANCELLED_NO_EFFECT'; effect:string; replayed:boolean; resource:{kind:'RECEIPT'|'WORK'|'CORRECTION'|'EXPORT'|'DOWNLOAD';id:Id;version:Version}|null; resultUrl:string|null; errorCode:string|null; }
interface PageInput { page:number; pageSize:20|50|100; }
interface Page<T> { items:T[];page:number;pageSize:number;total:number;asOf:Instant; }
interface Options { sites:{key:Key;name:string}[]; capabilities:string[]; dependencies:{owner:Key;availability:'AVAILABLE'|'NOT_CONFIGURED'|'UNAVAILABLE';contractRef:Ref|null}[]; mode:Mode; }
interface ReceiptSearch extends PageInput { poId:Id|null; supplierKey:Key|null; siteKey:Key|null; keyword:string|null; exceptionOnly:boolean; dateFrom:Instant|null; dateTo:Instant|null; sort:'updatedDesc'|'numberAsc'; }
interface ReceivableSearch extends PageInput { poId:Id|null; siteKey:Key; supplierKey:Key|null; itemKey:Key|null; }
interface ReceivableRow { poId:Id;poRevisionId:Id;orderVersion:Version;poLineId:Id;scheduleId:Id;supplierKey:Key;siteKey:Key;itemKey:Key;quantity:Qty;uomCode:string;sourcePortions:PO.PoSourcePortion[];guards:PO.PoReceivingGuardVector[];receivingPolicyRef:Ref|null;gates:Gate[]; }
interface LookupRequest { kind:'DESTINATION'|'LOT'|'SERIAL'|'LOGISTICS_UNIT';siteKey:Key;poId:Id;poLineId:Id;keyword:string|null;page:number;pageSize:20|50|100; }
interface LookupOption { key:Key;label:string;dimensions:STK.StockDimensions|null;destination:STK.Place|null;poolVersions:STK.PoolVersion[];evidenceRef:ERef;allowed:boolean;reasons:string[]; }
interface ArrivalInput { siteKey:Key;supplierKey:Key;arrivalEvidenceRef:Ref;notes:string|null; }
interface ArrivalView { id:Id;version:Version;siteKey:Key;supplierKey:Key;canonicalEventRef:Ref;arrivedAt:Instant;lines:{lineKey:Key;itemKey:Key;quantity:WideQty;uomCode:string}[];evidenceRef:Ref; }
interface ArrivalRange { from:WideQty;to:WideQty;uomCode:string; }
interface PortionInput { sourcePortionId:Id;quantity:Qty;sourceUom:string;conversionRef:Ref|null; }
interface DraftLine { lineId:Id|null;arrivalLineKey:Key|null;arrivalRange:ArrivalRange|null;poLineId:Id;scheduleId:Id;itemKey:Key;inputQty:Qty|null;inputUom:string|null;sourcePortions:PortionInput[];dimensions:STK.StockDimensions|null;destination:STK.Place|null;valuationRef:Ref|null;inspectionPolicyHint:Ref|null; }
interface DraftInput { poId:Id;poRevisionId:Id;siteKey:Key;arrivalId:Id|null;arrivedAt:Instant|null;notes:string|null;lines:DraftLine[]; }
type DraftPatch = Mutation & Partial<Pick<DraftInput,'arrivalId'|'arrivedAt'|'notes'|'lines'>>;
interface SavedLine extends Omit<DraftLine,'lineId'> { lineId:Id; }
interface SavedDraft extends Omit<DraftInput,'lines'> { lines:SavedLine[]; }
interface DraftView { operationId:Id;grNo:string;version:Version;businessRevision:Seq;state:'DRAFT'|'FROZEN'|'WITHDRAWN';input:SavedDraft;contentHash:Hash|null;workId:Id|null;gates:Gate[]; }
interface PreviewRequest extends Mutation { evaluationAt:Instant; }
interface PreviewResult { operationId:Id;snapshotRef:Ref;inputHash:Hash;allowed:boolean;gates:Gate[];lineMatches:{receiptLineId:Id;poRevisionId:Id;stockQuantity:WideQty;stockUom:STK.VersionedCodeRef;stockExpectedPools:STK.PoolVersion[];sourcePortions:PortionInput[];conversionRefs:Ref[];policy:InspectionPolicyResult;valuation:ValuationSnapshot}[];checkedAt:Instant; }
interface FrozenLine extends SavedLine { binding:{stockQuantity:WideQty;stockUom:STK.VersionedCodeRef;policy:InspectionPolicyResult;valuation:ValuationSnapshot;conversionRefs:Ref[];sourceLineMapRef:Ref}; }
interface FrozenContent { schema:'GR-RECEIVE-1';operationId:Id;businessRevision:Seq;poId:Id;poRevisionId:Id;siteKey:Key;arrivalId:Id;arrivedAt:Instant;lines:FrozenLine[]; }
interface FreezeRequest extends Mutation { expectedPreviewRef:Ref;expectedPreviewInputHash:Hash;reason:string; }
interface PreparationRequest extends Mutation { expectedIntentHash:Hash;reason:string; }
interface GrStartRequest extends Mutation { workId:Id;expectedWorkVersion:Version;expectedIntentHash:Hash; }
interface GrCommitRequest extends Mutation { workId:Id;expectedWorkVersion:Version;expectedIntentHash:Hash; }
type WorkState = 'PREPARING'|'READY'|'STARTED'|'RUNNING'|'WAITING'|'UNKNOWN'|'SUCCEEDED'|'REJECTED'|'SEALED'|'PARTIAL_FACTS';
interface WorkView { id:Id;operationId:Id;version:Version;state:WorkState;step:'PERMIT'|'WMS_PLAN'|'START'|'EXECUTE'|'LOOKUP'|'RECORD_COMMERCIAL'|'DELIVER'|'SEAL';mode:'MAIN_ATOMIC'|'ADOPTED_EQUIVALENT';intentHash:Hash;leaseEpoch:Seq;permit:PO.PoReceivingPermit|null;wmsPreparation:WmsPreparation|null;physicalReceiptRef:Ref|null;commercialReceiptRef:Ref|null;errorCodes:string[];nextActions:string[]; }
interface ResumeRequest extends Mutation { reason:string; }
interface WmsReceiptIntent { operationId:Id;intentHash:Hash;poId:Id;poRevisionId:Id;permitId:Id;source:STK.WireSource;manifest:STK.ExecutionManifest;manifestRef:ERef;posting:STK.PostingPlanRequest;commercialSourceRefs:Ref[];mode:Mode; }
interface WmsPreparation { requestRef:Ref;inboundIntentRef:Ref;identity:STK.ExecutionIdentity;planRef:STK.PlanRef;nextPosting:STK.PostingRequest;sourceBindings:{receiptLineId:Id;partKey:string;receiptSource:STK.WireSource;stockBaseQty:WideQty;stockBaseUom:STK.VersionedCodeRef}[];capabilityRef:Ref; }
interface WmsPrepareRequest { intent:WmsReceiptIntent; }
interface WmsExecuteRequest { operationId:Id;intentHash:Hash;inboundIntentRef:Ref;preparation:WmsPreparation;permitStartRef:Ref; }
interface WmsQueryRequest { operationId:Id;intentHash:Hash;locator:STK.BusinessLocator;businessDigest:Hash;commandKey:Id|null; }
interface WmsLineReceipt { receiptLineId:Id;partKey:string;receiptSource:STK.WireSource;wmsReceiptLineRef:Ref;rootId:Id;quantity:WideQty;uomRef:STK.VersionedCodeRef;stockReceipt:STK.ReceiptRef;movementId:Id; }
interface WmsReceiptResult { operationId:Id;intentHash:Hash;inboundReceiptRef:Ref;stockOperation:STK.CommandResponse;lines:WmsLineReceipt[];completeForIntent:boolean; }
type WmsQueryResult = {status:'FOUND';result:WmsReceiptResult;observedAt:Instant}|{status:'NOT_OBSERVED'|'UNAVAILABLE';operationId:Id;canInferNoEffect:false;observedAt:Instant;};
interface PhysicalNotice { eventId:Id;producerOwner:Key;contractRef:Ref;result:WmsReceiptResult;receivedAt:Instant;mode:Mode; }
interface IngressAck { eventId:Id;ingressId:Id;processing:'PENDING'|'APPLIED'|'DUPLICATE'|'GAP'|'QUARANTINED';applicationRef:Ref|null;errorCodes:string[]; }
interface InspectionPolicyRequest { operationId:Id;receiptLineId:Id;poId:Id;poRevisionId:Id;itemKey:Key;technicalRef:Ref|null;siteKey:Key;supplierKey:Key;receiptQuantity:WideQty;uomRef:STK.VersionedCodeRef;receiptSource:STK.WireSource;partKey:string;at:Instant;hint:Ref|null; }
interface InspectionPolicyResult { mode:'REQUIRED'|'NONE'|'EXEMPT'|'UNKNOWN';policyRef:ERef|null;decisionFamilyRef:ERef|null;exemptionRef:ERef|null;stage:'IQC';appliesToQuantity:WideQty;uomRef:STK.VersionedCodeRef;validFrom:Instant|null;validTo:Instant|null;requiresValuationBeforePhysical:boolean;missing:string[]; }
interface ValuationSnapshot { state:'RESOLVED'|'PENDING'|'BLOCKED';poCommercialRef:Ref;sourcePayloadHash:Hash;valuationPolicyRef:Ref|null;components:{amount:PO.Amount;currencyCode:string;kind:'GOODS'|'TAX'|'FEE'}[];requiredBeforePhysical:boolean;reasons:string[]; }
interface ReceiptCoordinateRange { from:WideQty;to:WideQty; }
interface ReceiptCoordinateDomain { ref:ERef;operationId:Id;receiptLineId:Id;physicalReceiptRef:Ref;wmsLineReceiptRef:Ref;receiptSource:STK.WireSource;partKey:string;rootId:Id;movementId:Id;baseUomRef:STK.VersionedCodeRef;originalRange:ReceiptCoordinateRange;frozenMappingRef:ERef;dimensionsHash:Hash; }
interface OwnerResolvedRangeLine { receiptLineId:Id;domainRef:ERef;resultEffectiveRanges:ReceiptCoordinateRange[];excludedOriginalRanges:ReceiptCoordinateRange[];mappingEvidenceRef:ERef; }
interface OwnerRangeResolution { ref:ERef;correctionIdentity:Key;rawEvidenceRef:ERef;contractRef:ERef;previousOwnerEvidenceRef:ERef|null;lines:OwnerResolvedRangeLine[]; }
interface ReceiptEffectiveScope { ref:ERef;receiptLineId:Id;domainRef:ERef;introducedByCommercialRef:Ref;previousScopeRef:ERef|null;effectiveRanges:ReceiptCoordinateRange[];excludedOriginalRanges:ReceiptCoordinateRange[];effectiveQuantity:WideQty;ownerResolutionRef:ERef|null;correctionApplicationRef:Ref|null; }
interface ReceiptScopeSet { ref:ERef;operationId:Id;commercialReceiptRef:Ref;previousScopeSetRef:ERef|null;complete:true;lines:{receiptLineId:Id;effectiveScopeRef:ERef}[]; }
interface QualityScopeProof { ref:ERef;decisionRef:ERef;inspectionSourceRef:Ref;decisionFamilyRef:ERef;effectiveScopeRef:ERef;domainRef:ERef;coverage:'FULL_SCOPE'|'DELTA_SCOPE';replacementOfDecisionRef:ERef|null; }
interface QualityScopeBinding { receiptLineId:Id;effectiveScopeRef:ERef;qualityScopeProofRef:ERef; }
interface InspectionSource { ref:Ref;effectiveScopeRef:ERef;operationId:Id;receiptLineId:Id;receiptSubject:STK.WireReceiptSubject;physicalReceiptRef:Ref;policy:InspectionPolicyResult;mode:Mode; }
interface QualityTaskRequest { requestKey:Key;source:InspectionSource; }
interface QualityTaskResult { requestKey:Key;inspectionSourceRef:Ref;state:'CREATED'|'EXEMPT_RECORDED'|'PENDING'|'REJECTED';taskRef:Ref|null;exemptionRef:ERef|null;decisionFamilyRef:ERef;errorCodes:string[]; }
interface QualityNotice { inspectionSourceRef:Ref;scopeBindings:QualityScopeBinding[];decision:STK.QualityApplicationRequest;contractRef:Ref; }
interface QualityRange { from:WideQty;to:WideQty;state:'ACCEPTED'|'REJECTED'|'HOLD'|'PENDING';basisRef:ERef; }
interface AcceptanceLine { receiptLineId:Id;effectiveScopeRef:ERef;quantity:WideQty;uomRef:STK.VersionedCodeRef;accepted:WideQty;rejected:WideQty;held:WideQty;pendingInspection:WideQty;ranges:QualityRange[];decisionRefs:ERef[]; }
interface AcceptanceView { operationId:Id;requiredScopeSetRef:ERef;projectedScopeSetRef:ERef;version:Version;acceptanceRevision:Seq;applicationRef:Ref|null;state:'PENDING'|'PARTIAL'|'APPLIED'|'CONFLICT';lines:AcceptanceLine[];poFactRef:Ref|null;poProjection:'PENDING'|'APPLIED'|'BLOCKED';stockApplication:'PENDING'|'APPLIED'|'NONCURRENT'|'CONFLICT'|'UNKNOWN'; }
interface AcceptanceApply extends Mutation { expectedQualityHeadRefs:ERef[];reason:string; }
interface StockQualityNotice { eventId:Id;producerOwner:Key;contractRef:Ref;operationId:Id;result:STK.QualityApplicationResult;mode:Mode; }
interface QuantityObservation { quantity:WideQty|null;uomCode:string;coverage:Coverage;evidenceRefs:Ref[];asOf:Instant|null; }
interface ReceiptLineStatus { receiptLineId:Id;physical:QuantityObservation;commercial:QuantityObservation;accepted:QuantityObservation;rejected:QuantityObservation;held:QuantityObservation;pendingInspection:QuantityObservation;returned:QuantityObservation;ordinaryAvailable:QuantityObservation;stockApplication:string;errorCodes:string[]; }
interface ReceiptStatus { operationId:Id;version:Version;workState:WorkState|null;physical:'NOT_OBSERVED'|'POSTED'|'PARTIAL'|'UNKNOWN'|'SEALED_NO_EFFECT';commercial:'NOT_POSTED'|'POSTED'|'RECONCILIATION_REQUIRED';receiptFactRef:Ref|null;acceptance:AcceptanceView|null;lines:ReceiptLineStatus[];consumerStates:{consumer:Key;state:'PENDING'|'RECEIVED'|'APPLIED'|'REJECTED'|'UNKNOWN';receiptRef:Ref|null}[];mode:Mode; }
interface GrSliceMapping { receiptSliceRef:Ref;effectiveScopeRef:ERef;receiptLineId:Id;stockSubject:STK.WireReceiptSubject;sourcePortionId:Id;sourceQuantity:Qty;sourceUom:string;conversionRef:Ref|null; }
interface ReceiptAllocationManifest { ref:Ref;scopeSetRef:ERef;operationId:Id;factIdentity:PO.PoFactIdentity;factVersion:Seq;complete:true;slices:GrSliceMapping[]; }
interface GrNoEffectRequest extends Mutation { reason:string; }
interface GrNoEffectEvidence { operationId:Id;intentHash:Hash;permitId:Id;stockClosureRef:STK.ReceiptRef|null;coveredPaths:Key[];closedThrough:Key;ownerProofRef:Ref;outcome:'NO_EFFECT'|'ALREADY_EFFECTIVE'|'UNKNOWN'; }
interface GrReceiptEffectQuery { receiptOperationKey:Key;permitId:Id;scopeHash:Hash; }
interface GrReceiptEffectResult { operationId:Id;permitId:Id;state:'NOT_STARTED'|'IN_FLIGHT'|'PHYSICAL_COMMITTED'|'NO_EFFECT'|'UNKNOWN';startRef:Ref|null;receiptRef:Ref|null;proofRef:Ref|null; }
interface CorrectionInput { baseReceiptFactRef:Ref;baseScopeSetRef:ERef;reason:string;kind:'ANNOTATION'|'REFERENCE_REPAIR'|'COMMERCIAL_FACT_CORRECTION';notes:string|null;physicalCorrectionRef:Ref|null;authorityRefs:Ref[];replacementQuantities:{receiptLineId:Id;quantity:WideQty;uomRef:STK.VersionedCodeRef}[]; }
interface CorrectionView { ownerResolutionRef:ERef|null;resultScopeSetRef:ERef|null;id:Id;operationId:Id;version:Version;state:'DRAFT'|'WAITING_OWNER'|'APPLIED'|'REJECTED';input:CorrectionInput;resultFactRef:Ref|null;errorCodes:string[]; }
interface CorrectionApply extends Mutation { reason:string; }
interface HandoffBasis { scopeSetRef:ERef;effectiveScopeRef:ERef;operationId:Id;receiptLineId:Id;basisRevision:Seq;receiptFactRef:Ref;acceptanceRef:Ref|null;poId:Id;poRevisionId:Id;poLineId:Id;scheduleId:Id;receiptSlices:GrSliceMapping[];acceptedRanges:QualityRange[];originalCommercialRef:Ref;valuation:ValuationSnapshot;relatedOwnerReceipts:Ref[];createsMatchOrReturnPermission:false; }
interface ReturnNotice { eventId:Id;producerOwner:Key;returnRef:Ref;previousRef:Ref|null;receiptLineId:Id;stockMovementRef:Ref;quantity:WideQty;uomRef:STK.VersionedCodeRef;receiptSliceRefs:Ref[];mode:Mode; }
interface SupplyTransition { eventId:Id;operationId:Id;version:Seq;poId:Id;poReceiptFactRef:Ref;stockReceiptRefs:STK.ReceiptRef[];slices:{receiptSliceRef:Ref;sourcePortionId:Id;lineageRef:Ref|null;stockSubject:STK.WireReceiptSubject;quantity:WideQty;uomRef:STK.VersionedCodeRef}[];createsAdditionalSupply:false;state:'WAITING_COMPONENTS'|'LINKED';asOf:Instant; }
interface ConsumerReceipt { eventId:Id;consumer:Key;originalEventId:Id;applicationRef:Ref|null;result:'RECEIVED'|'APPLIED'|'REJECTED'|'UNKNOWN';version:Seq;reasons:string[]; }
interface HistoryEntry { id:Id;operationId:Id;sequence:Seq;action:string;actorKey:Key;occurredAt:Instant;recordedAt:Instant;evidenceRefs:Ref[];summary:string; }
interface ExportInput { search:ReceiptSearch;includeCommercial:boolean;reason:string; }
interface ExportView { id:Id;version:Version;state:'QUEUED'|'READY'|'FAILED'|'REVOKED'|'EXPIRED';rowCount:number|null;hash:Hash|null;errorCodes:string[]; }
interface DownloadInput { operationId:Id;evidenceRef:Ref;purpose:'INTERNAL_REVIEW'; }
interface DownloadTicket { id:Id;expiresAt:Instant;contentPath:string; }
interface CommandObservation { commandId:Id;state:'NOT_OBSERVED'|'RECORDED';result:CommandResult|null;canInferNoEffect:false; }
interface EquivalenceAdoption { ref:Ref;topologyHash:Hash;stockCanonicalContract:Ref;poFinalGuardContract:Ref;sourceDelegationContract:Ref;cutoffContract:Ref;registeredPaths:Key[];currentGate:'ADOPTED'|'NOT_ADOPTED'|'UNAVAILABLE'; }
```

ERef的digest来自完整不可变对象除ref本身的规范JSON；ref.version是对象修订，不是rowversion。OwnerRangeResolution是GR受权只读解析真实WMS Correction/Resolution的投影，用户不能上传自签。逐行ReceiptEffectiveScope独立正整数修订，ScopeSet版本与CommercialVersion一一对应并完整列所有商业行；未改行可复用旧scope，不设置另一套可漂移RangeCurrent。 [原文 L250](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:250>)、[原文 L654](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:654>)

总PO事实documentLineId=OPERATION，allocationKey RECEIVE或ACCEPTANCE，并按kind分域；所有GR行的完整分配仍含独立ReceiptLineId/slice。ArrivalRange用原到货行单位，Stock subject.range用原收货叶基本单位，sourceQuantity用PO源单位，三域通过冻结Map精确对应；即使样例全EA也不能省证。所有区间[from,to)半开。 [原文 L252](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:252>)、[原文 L254](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:254>)

### 拟议 pur_gr 物理表：完整字段与约束

E含Tenant/UUID/创建信息，M含更新/rowVersion；Hash binary32、Q decimal(21,8)、POQty(19,6)、金额(19,4)、Ref完整JSON，外域Key160须原样另存。FK复合Tenant，正式事实只追加，不级联删除；JSON使用上列准确DTO，不是自由字典。GrNo GR-＋12位总长15，列varchar16，租户事务取号，旧号保留。 [原文 L591](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:591>)、[原文 L593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:593>)、[原文 L623](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:623>)

| 表 | 专列/完整JSON | 唯一约束、责任与索引 | 出处 |
| --- | --- | --- | --- |
| ArrivalRecord E+M | Producer64、StableEventKey160、SupplierKey128、SiteKey64、OccurredAt、EvidenceRefJson、EvidenceVersion64、PayloadJson:ArrivalView、SemanticHash | UQ(Tenant,Producer,StableEventKey)；同事件新证据版保历史，不重开数量 | [原文 L599](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:599>) |
| ArrivalLine E | ArrivalId、LineKey160、ItemKey128、Quantity Q、Uom16 | UQ(Tenant,ArrivalId,LineKey)；Qty>=0，当前版本范围有据 | [原文 L600](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:600>) |
| ArrivalSlice E+M | ArrivalLineId、OperationId、ReceiptLineId、FromUnits/ToUnits decimal38整数(8位scale)、State、SourceVersion、ParentSliceId NULL | 同Arrival行范围锁检重叠；UQ(Tenant,ArrivalLineId,Id)不是重叠算法；未知状态占用保留 | [原文 L601](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:601>) |
| ReceiptOperation E+M | GrNo varchar16、PoId/PoRevisionId、SupplierKey128、SiteKey64、ArrivalId NULL、BusinessRevision bigint、DocumentState、InputJson:SavedDraft、IntentHash NULL、CurrentCommercialVersion bigint NULL；WorkId由ReceiptWork唯一关联派生，不在此写指针 | UQ(Tenant,GrNo)；INDEX(Tenant,SiteKey,PoId,UpdatedAt,Id)；原六轴不压单状态 | [原文 L602](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:602>) |
| ReceiptLine E+M | OperationId、PoLineId、ScheduleId、LineOrdinal int、ArrivalLineKey NULL、InputQty Q NULL、InputUom16 NULL、InputJson:SavedLine | UQ(Tenant,OperationId,LineOrdinal)，稳定Id不按数组重分配 | [原文 L603](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:603>) |
| LinePortion E | OperationId、ReceiptLineId、PoSourcePortionId、SourceQty POQty、SourceUom16、InputFrom/To Q、BaseFrom/To Q、ConversionRefJson NULL | 冻结后UQ(Tenant,ReceiptLineId,PoSourcePortionId,BaseFrom)；源分量互斥校验同事务 | [原文 L604](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:604>) |
| FrozenIntent E | OperationId、BusinessRevision、CanonicalJson、IntentHash、PoSnapshotRefJson、MappingRefsJson、PolicyRefsJson、CreatedMode | UQ(Tenant,OperationId,BusinessRevision)；正文不可改，当前资格不混进hash | [原文 L605](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:605>) |
| MatchSnapshot E | OperationId、DraftBusinessRevision、PoRevisionId、InputHash、PayloadJson:PreviewResult、ObservedAt | 只读评估，不占量；初始状态不意味新Owner已采用 | [原文 L606](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:606>) |
| ValuationSnapshot E | OperationId、ReceiptLineId、Version、PayloadJson:ValuationSnapshot、SourcePayloadHash | UQ(Tenant,ReceiptLineId,Version)；原币保留、未知不0 | [原文 L607](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:607>) |
| ReceiptStartIntent E | OperationId、IntentHash、OwnerStartKey128、RegisteredPathsJson、PoPermitId、PayloadJson、StartRefJson NULL | UQ(Tenant,OperationId)；与PO.start同事务，无新Start替旧未知 | [原文 L608](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:608>) |
| WmsIntent E | OperationId、InputJson:WmsReceiptIntent、ManifestDigest、LocatorProducer64、LocatorKey160、PreparationJson:WmsPreparation NULL | UQ(Tenant,OperationId)及UQ(Tenant,LocatorProducer,LocatorKey)；原準备/身份不可覆盖 | [原文 L609](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:609>) |
| PhysicalReceipt E | OperationId、StockReceiptOwner64、StockReceiptId160、StockReceiptVersion100、StockBusinessDigest、PayloadJson:WmsReceiptResult、ObservedAt | UQ(Tenant,StockReceiptOwner,StockReceiptId,StockReceiptVersion)；与Operation准确绑定，完整性校验 | [原文 L610](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:610>) |
| PhysicalLineLink E | PhysicalReceiptId、ReceiptLineId、PartKey64、SourceJson:STK.WireSource、RootId、MovementId、WmsLineRefJson、BaseQty Q、BaseUomRefJson | UQ(Tenant,PhysicalReceiptId,PartKey)；不从Item名猜Root | [原文 L611](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:611>) |
| CommercialReceiptVersion E | ScopeSetId、OperationId、Version bigint>0、PreviousVersion NULL、PhysicalReceiptId、OriginalInputHash、State POSTED/VOID_WITH_EVIDENCE、CorrectionRefJson NULL、PayloadJson:PO.PoFulfillmentFact、ManifestId、SemanticHash | UQ(Tenant,OperationId,Version)；一个Operation首次商业事实一次，更新为后继不是再+= | [原文 L612](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:612>) |
| ReceiptAllocationManifest E | ScopeSetId、OperationId、Kind16、FactVersion、PayloadJson:ReceiptAllocationManifest、Hash | UQ(Tenant,OperationId,Kind,FactVersion)；引用给PO/MATCH/RETURN可回读全体 | [原文 L613](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:613>) |
| ReceiptSlice E | EffectiveScopeId、DomainId、OperationId、ReceiptLineId、SliceKey128、StockSourceJson、PartKey64、BaseFrom/To Q、PoSourcePortionId、SourceQty POQty、SourceUom16、ConversionRefJson NULL、AncestorSliceId NULL | UQ(Tenant,SliceKey)；原实收完整slice及子质量slice分谱系，不能同ACCEPTANCE事实重叠 | [原文 L614](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:614>) |
| ReceiptCoordinateDomain E | OperationId、ReceiptLineId、PhysicalLineLinkId、PayloadJson:ReceiptCoordinateDomain、SemanticHash | UQ(Tenant,ReceiptLineId)；FK同原PhysicalLine；原Source/part/Root/Movement/单位/映射永不改 | [原文 L615](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:615>) |
| OwnerRangeResolution E | OperationId、OwnerCorrectionIdentity160、RawOwnerRefJson、RawDigest、PayloadJson:OwnerRangeResolution、SemanticHash | UQ(Tenant,OperationId,OwnerCorrectionIdentity)；同身份异义隔离，不覆盖原件 | [原文 L616](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:616>) |
| ReceiptEffectiveScope E | OperationId、ReceiptLineId、DomainId、Revision bigint、IntroducedCommercialVersion、PreviousScopeId NULL、OwnerResolutionId NULL、CorrectionApplicationId NULL、EffectiveQuantity Q、PayloadJson:ReceiptEffectiveScope、SemanticHash | UQ(Tenant,ReceiptLineId,Revision)；FK前驱同line/domain；初始rev1无纠正，其后证据不可空 | [原文 L617](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:617>) |
| ReceiptEffectiveRange E | EffectiveScopeId、Ordinal int、Kind EFFECTIVE/EXCLUDED、BaseFrom/To Q | UQ(Tenant,EffectiveScopeId,Kind,Ordinal)；每kind排序不交，合并精确覆盖原domain，length校验只在同scope同单位 | [原文 L618](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:618>) |
| ReceiptScopeSet E | OperationId、CommercialVersion、PreviousScopeSetId NULL、PayloadJson:ReceiptScopeSet、SemanticHash | UQ(Tenant,OperationId,CommercialVersion)；CommercialVersion→ScopeSet为1:1、同事务不可变；不另设可漂移RangeCurrent | [原文 L619](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:619>) |
| ReceiptScopeSetLine E | ScopeSetId、ReceiptLineId、EffectiveScopeId | UQ(Tenant,ScopeSetId,ReceiptLineId)；FK scope同line/op；完整覆盖Operation所有已商业行，未改变行可复用旧scope | [原文 L620](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:620>) |
| CorrectionApplication E | OperationId、CorrectionId、OwnerResolutionId、BaseCommercialVersion、BaseScopeSetId、ResultCommercialVersion、ResultScopeSetId、SemanticHash | UQ(Tenant,OperationId,OwnerResolutionId)及UQ(Tenant,CorrectionId)；同事务追加，前后精确链；更正不产生新Stock动作 | [原文 L621](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:621>) |

| 表 | 字段 | 唯一性/当前头 | 出处 |
| --- | --- | --- | --- |
| InspectionSource E | OperationId、ReceiptLineId、Stage16、SourceKey160、PolicyIdentityHash、FamilyRefJson、PayloadJson:InspectionSource | UQ(Tenant,ReceiptLineId,Stage,PolicyIdentityHash)；真实重检需后继来源关系 | [原文 L639](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:639>) |
| InspectionTaskLink E+M | InspectionSourceId、RequestKey128、RequestJson:QualityTaskRequest、ResultJson:QualityTaskResult NULL、State、LastError NULL | UQ(Tenant,RequestKey)；缺TaskId不造占位已创建 | [原文 L640](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:640>) |
| QualityCanonical E | Owner64、FamilyIdentityHash、InspectionSourceId、StreamId160、Epoch100、Sequence bigint、PreviousSequence、DecisionRefJson、RawJson:QualityNotice、SemanticHash | UQ(Tenant,Owner,FamilyIdentityHash,InspectionSourceId,StreamId,Epoch,Sequence)；同序异义隔离 | [原文 L641](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:641>) |
| QualityHead E+M | EffectiveScopeId、InspectionSourceId、FamilyIdentityHash、StreamId、Epoch、ObservedHigh bigint、AppliedSequence bigint、AppliedCanonicalId NULL、HeadRevision bigint、Coverage | UQ(Tenant,InspectionSourceId,FamilyIdentityHash,StreamId,Epoch,EffectiveScopeId)；Observed不代Applied；FK同Tenant；旧scope头不成为当前scope头 | [原文 L642](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:642>) |
| QualityWaiting E+M | CanonicalId、DependencyKind32、DependencyKey160、RequiredSequence NULL、State、NextAttemptAt | UQ(Tenant,CanonicalId,DependencyKind,DependencyKey)；唤醒/扫描同槽 | [原文 L643](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:643>) |
| QualityScopeBinding E | CanonicalId、ReceiptLineId、EffectiveScopeId、QualityScopeProofRefJson、BindingSetHash | UQ(Tenant,CanonicalId,ReceiptLineId)；FK scope同line；Canonical完整绑定集合冻结，同决定换scope/proof为Conflict | [原文 L644](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:644>) |
| AcceptanceApplication E | ScopeSetId、OperationId、AcceptanceRevision bigint、CommercialVersion bigint、QualityHeadSetHash、DecisionRefsJson、PreviousApplicationId NULL、PayloadJson:AcceptanceView、CreatedFactId NULL | UQ(Tenant,OperationId,ScopeSetId,QualityHeadSetHash)；ScopeSet FK必须对应同CommercialVersion，QualityHeadSetHash包含各行scope及BindingSetHash；人工/自动只一次 | [原文 L645](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:645>) |
| AcceptanceSegment E | ApplicationId、ReceiptLineId、EffectiveScopeId、From/To Q、State16、BasisRefJson、DecisionCanonicalId NULL | FK scope必须等Application.ScopeSet内该行；分区互斥且精确覆盖E，四量和=sum(length(E))，不得覆盖EXCLUDED | [原文 L646](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:646>) |
| AcceptanceCursor E+M | OperationId、Revision bigint、RequiredScopeSetId、ProjectedScopeSetId、CurrentApplicationId NULL、State、ProjectionJson:AcceptanceView | UQ(Tenant,OperationId)；Correction同事务切Required并递增RowVersion，Current/Projected可保历史；Acceptance条件提交后两Set相同 | [原文 L647](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:647>) |
| StockQualityObservation E | OperationId、DecisionIdentityHash、StockApplicationId、ResultJson:StockQualityNotice、SemanticHash | UQ(Tenant,StockApplicationId)；只存Stock应用观察，不能第二次GR验收 | [原文 L648](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:648>) |
| FactVersion E | OperationId、Kind16、Version bigint、PreviousVersion NULL、PayloadJson:PO.PoFulfillmentFact、ManifestId、SemanticHash | UQ(Tenant,OperationId,Kind,Version)；RECEIPT由CommercialVersion同事务产生，ACCEPTANCE由Application产生；非第二主账 | [原文 L649](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:649>) |
| FactCurrent E+M | OperationId、Kind16、FactVersionId | UQ(Tenant,OperationId,Kind)；仅同事务更新，不按日期MAX | [原文 L650](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:650>) |

| 表 | 字段 | 业务唯一性和恢复 | 出处 |
| --- | --- | --- | --- |
| ReceiptWork E+M | OperationId、WorkKey128、State/Step、Mode、IntentHash、InputJson、LeaseOwner64 NULL、LeaseEpoch bigint、LeaseUntil NULL、NextAttemptAt、Attempts int、PoPermitId NULL、StockPreparationRefJson NULL、StockReceiptId NULL、ErrorCodesJson、HumanAttention bit | UQ(Tenant,WorkKey)；最后epoch CAS，未知不换Work | [原文 L660](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:660>) |
| OwnerCall E+M | WorkId、Owner64、Action32、RequestKey160、RequestHash、RequestJson、State、EverDispatched bit、ResultRefJson NULL | UQ(Tenant,Owner,RequestKey)；同键异输入拒绝；不因重试数量更新而修改原body | [原文 L661](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:661>) |
| OwnerResult E | OwnerCallId、OwnerResultIdentity160、OwnerVersion100、Outcome32、PayloadJson、SemanticHash、ObservedAt | UQ(Tenant,OwnerCallId,OwnerResultIdentity,OwnerVersion)；冲突留双份，Current pointer另存 | [原文 L662](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:662>) |
| CutoffRequest E+M | RequestIdentityHash、CancellationId、PoId、ScopeHash、RequestJson:PO.PoCutoffRequest、State、RegisteredPathsJson、CurrentResultId NULL | UQ(Tenant,RequestIdentityHash)；同取消同scope同请求版复用，不能签空集合成功 | [原文 L663](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:663>) |
| CutoffProof E | RequestId、ProofVersion bigint、ResultJson:PO.PoCutoffResult、GrProofRefJson、ProofHash、CapturedPermitSetHash、StockClosureRefsJson、ReceivedFactsDigest | UQ(Tenant,RequestId,ProofVersion)；反证追加Conflict不改旧证明 | [原文 L664](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:664>) |
| NoEffectProof E | OperationId、ProofVersion、PayloadJson:GrNoEffectEvidence、PoProofRefJson NULL | UQ(Tenant,OperationId,ProofVersion)；已Stock POSTED不得签NO_EFFECT | [原文 L665](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:665>) |
| Discrepancy E+M | OperationId、ReceiptLineId NULL、BusinessKey160、Kind64、OriginalEvidenceRefsJson、State、ResolutionRefJson NULL | UQ(Tenant,BusinessKey)；包括LATE_AFTER_CUTOFF/PRECISION/MAPPING等，Closed只该异常版 | [原文 L666](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:666>) |
| Correction E+M | OperationId、InputJson:CorrectionInput、BaseFactRefJson、BaseScopeSetId、OwnerResolutionId NULL、ContentHash、State、ResultFactId NULL、ResultScopeSetId NULL、DependencyRefsJson | 固定申请/原事实；OwnerResolution业务唯一及CorrectionApplication防不同Command重做；GET原槽恢复，无Owner证据WAITING_OWNER | [原文 L667](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:667>) |
| ReturnObservation E | OperationId、ReceiptLineId、ReturnIdentityHash、Version、PreviousRefJson NULL、PayloadJson:ReturnNotice | 同Return稳定身份和版本去重，物理及信用分开 | [原文 L668](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:668>) |
| SupplyTransition E | OperationId、Version、PayloadJson:SupplyTransition、ComponentSetHash | UQ(Tenant,OperationId,Version)；仅组成关联，不加供给 | [原文 L669](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:669>) |
| ConsumerDelivery E+M | Consumer64、OriginalEventId、BusinessFactRefJson、State、ApplicationRefJson NULL、ApplicationVersion NULL、LastError NULL | UQ(Tenant,Consumer,OriginalEventId)；RECEIVED不等APPLIED | [原文 L670](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:670>) |
| Inbox E | Producer64、MessageId、PayloadHash、PayloadJson、CanonicalId NULL、FirstReceivedAt | UQ(Tenant,Producer,MessageId)；原件不覆盖 | [原文 L671](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:671>) |
| InboxProcessing E+M | CanonicalId、ProcessingRevision bigint、ClaimEpoch bigint、State、LeaseUntil NULL、NextAttemptAt、ApplicationRefJson NULL | UQ(Tenant,CanonicalId)；重发可读新processing，不重造Canonical | [原文 L672](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:672>) |
| InboxConflict E | InboxId NULL、BusinessIdentityHash、OriginalHash、IncomingHash、IncomingJson、Reason64 | 不可用后到数据覆盖先前可信值 | [原文 L673](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:673>) |
| Outbox E+M | EventId、Consumer64、BusinessKey160、PayloadJson、Hash、State、AttemptCount、LeaseEpoch、LeaseUntil NULL、NextAttemptAt | UQ(Tenant,Consumer,BusinessKey)；业务对象与Outbox同提交 | [原文 L674](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:674>) |
| Audit E | OperationId、TimelineSequence bigint、Action64、OccurredAt、EvidenceRefsJson、ActorKey64、Summary1000 | UQ(Tenant,OperationId,TimelineSequence)；晚事实按Recorded顺序记录原发生时间 | [原文 L675](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:675>) |
| Command E | CommandId、ActorKey64、Method8、Path256、RequestHash、Outcome32、HttpStatus、SafeResultJson | UQ(Tenant,CommandId)；同method/path/body结果永久最小墓碑 | [原文 L676](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:676>) |
| Number / AuditCounter | Tenant+Kind/Operation主键、NextValue bigint、RowVersion | 同事务取号，不复用已提交编号 | [原文 L677](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:677>) |
| ExportJob E+M | InputJson:ExportInput、AuthRevision128、State、ArtifactRefJson NULL、Hash NULL、RowCount NULL、ExpiresAt NULL | 只在完整权限范围生成，下载再查当前权 | [原文 L678](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:678>) |
| DownloadTicket E | OperationId、ActorKey64、EvidenceRefJson、ExpiresAt、RevokedAt NULL | 短时私有票据，不能泄公开对象地址 | [原文 L679](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:679>) |

只有最终AcceptanceApplication才有PUR_GR/ACCEPTANCE/id/1真实引用，业务Revision另列；Commercial factRef是PUR_GR/GR-RECEIPT/operation/version，PO消费返回factRef另存ConsumerDelivery，不能假同ID。GR不建StockBalance、PermitBudget、PR余额或可编辑Quality主账。 [原文 L652](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:652>)、[原文 L681](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:681>)

## 4. 到货、冻结、收货、质量和更正完整流程

### 4.1 到货防重、匹配与冻结

Arrival唯一Tenant/可信Producer/StableArrivalEventId，保存原事件、Supplier/Site、真实时间和行量。扫描件、证据版本、HTTP键不同不变第二车；重复送货单号要由Owner给真实不同事件，不能按日期/数量猜。只有照片或自由单号可留草稿，未核不能Verified。到货超PO保原事实，正常新收仍按真实超收策略。 [原文 L280](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:280>)、[原文 L282](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:282>)

freeze在同Arrival行权威锁检查区间重叠，[0,60)和[50,100)也冲突，不能只unique(from,to)。ArrivalSliceId冻结且用作实际叶身份；未知执行期间不可重用。无效果并所有叶永久封闭后，新有权安排必须显式后继，不能换UUID。 [原文 L284](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:284>)

读准确PO修订/行/Schedule/portion校父子、Supplier/Site/物料/技术。先逐行精确转换到Stock和PO单位，份额合量=该GR行；多行同Schedule/portion先合量再核预算。preview读原Quality策略、估价、WMS地点批序与能力，返回snapshotRef/inputHash；freeze短事务核token/原快照/当前映射与区间，保实际Policy/Valuation/原商业/DeliveryHash/FrozenIntent和审计。政策变更须重新预检，不沿相同输入hash暗换策略。 [原文 L288](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:288>)、[原文 L290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:290>)、[原文 L292](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:292>)

GR-RECEIVE-1业务摘要保存完整canonical JSON、lines按lineId排序，包括源区间、数量、份额、Stock维度/目的地及实际Policy/Valuation/映射；排除rowVersion、Work状态、HTTP键、Pool当前版和不改业务notes。notes原文保历史，Command指纹仍含它。冻结后不PATCH量/Lot/目的地，走原撤回与合法后继。 [原文 L294](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:294>)

### 4.2 原来源清单、准备与开始

WorkKey=GR:operationId:RECEIVE，PO receiptOperationKey=GR/operationId；每操作一正常Work。prepare先持久原PO请求，按Schedule/portion合量取一个HELD_NOT_STARTED，再按原键记录WMS SP01准备；回包丢失查原结果不另Permit。start前展示最终冻结清单，准备失败／许可到期仍需原路径安全封闭。prepare/start不更新登记根rowversion，Work独立CAS，不能错用根token当工作token。 [原文 L310](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:310>)、[原文 L312](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:312>)、[原文 L314](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:314>)

Stock parent WireSource为PUR_GR/PURCHASE_RECEIPT，document=operation，line=BATCH，executionKey=GR/id/RECEIVE，sourceVersion为冻结业务修订；part lineId=ReceiptLineId，执行键GR/id/LINE/lineId。OccurrenceLeaf producer仍PUR_GR，occurrenceKey=准确ArrivalId+原行，effectKey=不可变ArrivalSliceId；新包装/扫描/版本不是新叶。GR守区间重叠，Stock守Tenant/producer/occurrence/effect唯一，二者职责不同。 [原文 L333](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:333>)、[原文 L335](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:335>)

STK-SOURCE-EXECUTION-1完整manifest parent与posting.source六字段一致，全部parts与clientLineKey/sourceUse双向对应，输入/基本量和单位、Allowance与lineBusinessDigest齐。Stock自己的line摘要不能换GR hash。Allowance只授权这次已获PO许可的到货叶，不是新的PR预算；容量按准确SourceCapacityAssignment或有据KNOWN_NONE，未知不能写0或任取PO待到量。 [原文 L337](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:337>)、[原文 L339](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:339>)

start短事务写完整ReceiptStartIntent、registeredPaths、不可变ownerStartIntentRef，并由PO.start参加同commit；成功前禁止派发实物动作。开始只IN_FLIGHT，不增Stock或Commercial。commit读原Start/Permit/Plan并先查Canonical终态；成功取原回执，sealed/rejected保终态，UNKNOWN不换正向收货键。 [原文 L352](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:352>)、[原文 L354](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:354>)

### 4.3 正常一次联合提交与SC11边界

事务外解析全部权威证据并收集最强锁资源；内核重读Work epoch、IntentHash、Arrival片、PO Permit/start/当前Guard、Stock叶／计划／容量／地点／批序与策略。Permit vector仅能由原意图revalidate取得，不盲塞latest。顶层唯一commit使库存+100、PO USED、GR Commercial100同时成立；任一失败全rollback，Quality任务后续失败仍Stock PENDING。 [原文 L358](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:358>)、[原文 L370](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:370>)

| Owner | 本次最终事务写集合 | 出处 |
| --- | --- | --- |
| GR | CommercialReceiptVersion v1、逐行ReceiptCoordinateDomain/初始ReceiptEffectiveScope、完整ReceiptScopeSet v1、ReceiptLine事实、ReceiptAllocationManifest、PhysicalLink/原回执、Work最终状态、审计、PO RECEIPT出站/Quality任务请求/Consumer交接Outbox | [原文 L364](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:364>) |
| PO | 原PermitConsumption、ReceiptSliceMap、对应BudgetSegment USED、Permit USED、Guard版本；不提前把异步PO累计当已更新 | [原文 L365](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:365>) |
| WMS-IN | 原InboundReceipt及逐行Source映射，绑定唯一GR/Stock结果 | [原文 L366](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:366>) |
| WMS-STOCK | 按已接受Posting算法写唯一StockExecution/Movement/Root/QuantityLedger/Source消费/容量变换/初始Quality/StockReceipt及其Outbox | [原文 L367](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:367>) |
| Quality / Finance / Planning | 此事务不直接建立检验结果、AP、MRP；只有待发请求/证据 | [原文 L368](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:368>) |

WMS返回真实Root/Movement，准备前不能索取未来ID；completeForIntent须逐part全量UOM/Source/ReceiptLine/StockReceipt一致。部分真行必须PARTIAL_FACTS并保余保护，不补余项拼正常原子成功。PO技术硬停覆盖旧start，软取消/关闭只准PO明确drain原在途，GR不能自建早start；后到真事实始终保留，失权/取消不能删实物，也不再Posting。 [原文 L372](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:372>)、[原文 L376](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:376>)、[原文 L378](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:378>)

MAIN_ATOMIC中Stock代码先运行但外层未commit，崩溃意味着整组回滚或未知，不能宣称独立Stock已成。ADOPTED_EQUIVALENT是条件路径，需真实拓扑、Canonical/叶唯一、PO等价最后裁决、Source/容量排他委托及所有路径fence证书；普通HTTP200/重试队列不足，未采用新真实B执行直接GR_OWNER_PROTOCOL_NOT_ADOPTED。 [原文 L391](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:391>)、[原文 L393](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:393>)

SC11真正“Stock100已提交、GR待补”属于该有证等价路径或有权既成历史：先保存原Intent/Preparation/Permit开始，WMS按Canonical一次；后本域失败只查原locator/businessDigest回同Receipt/Movement/Root，再补Commercial、Domain/Scope/ScopeSet1及出站一次。禁止再次execute/Post补GR，不能拿今日库存余额重建初始域。旧只有InboundNo先向Owner查实际Movement/Resolution，有照片无认账只待核。 [原文 L395](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:395>)、[原文 L397](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:397>)、[原文 L420](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:420>)

### 4.4 Quality专业判定与本域商业验收

| Quality模式 | 必要依据 | 正常物理收货时Stock初态 | GR验收与失败 | 出处 |
| --- | --- | --- | --- | --- |
| REQUIRED | 准确Item/技术/Supplier/Site/用途/数量/时点、IQC阶段、策略版及DecisionFamily | REQUIRES_INSPECTION→PENDING；普通可用0（其它门不使PENDING变可用） | 商业Received后Accepted0、PendingInspection=已收范围；Task失败仍待检 | [原文 L437](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:437>) |
| NONE | Quality Owner明示此活动不适用检验，完整范围/理由/策略凭证 | 仅有有效不适用/免检依据才EXPLICIT_SKIP | 在真实商业收货后按该证据作接受应用，不在preview先写Accepted | [原文 L438](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:438>) |
| EXEMPT | 有权豁免/让步政策适用和批准，量/批次/效期准确 | EXPLICIT_SKIP；Stock保存原豁免出处 | GR记录豁免类型而非伪造检测PASS；任何独立召回仍限制Stock | [原文 L439](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:439>) |
| UNKNOWN | 缺策略、多个冲突策略或无法验证 | 普通新收阻断；已经真实到货/库存不丢 | 保存待证，不伪造NONE；不因PostingBasis改为免检 | [原文 L440](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:440>) |

策略必须在Item/技术/Supplier/Site/用途/量/时点/IQC下恰一适用，有据无终期才validTo=null；preview后策略/豁免撤回重核，既成历史保当时策略并增当前限制。PostingBasis/估价不决定免检；Valuation.requiredBeforePhysical必须明确，未提供不是false，已发生价未知保真不填0。完整preview需commercial.read，仓库无商业权只读不含价Status/Work，机器执行权限不转授。 [原文 L343](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:343>)、[原文 L345](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:345>)、[原文 L442](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:442>)、[原文 L444](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:444>)

Commercial和Stock绑定后形成原InspectionSource，主体为该原收货叶[0,Q)基本单位域，effectiveScopeRef指初始scope；纠正不改原Source/Root。任务键=InspectionSource+IQC+policyIdentity，Stock和GR通知由Quality SourceAlias归同任务。CREATED必须真taskRef，NONE/EXEMPT才有据EXEMPT_RECORDED；失败仍待检。抽样10不能自行推100通过，重检需明确旧Family关系。 [原文 L448](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:448>)、[原文 L450](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:450>)、[原文 L452](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:452>)

QualityNotice完整decision采用原STK类型，scopeBindings仅GR消费者信封。每涉行一Quality有权证明，绑定decisionRef/digest、原InspectionSource、Family/domain与准确scope/digest；GR自己贴当前scope或相同数量都不够。同Canonical绑定集合冻结，换绑定CONFLICT。Quality把同业务决定分别投Stock和GR，MessageId可异，不让GR重签Quality。Stock应用与GR应用独立，GR采购acceptance权限不拥有Stock resume写权。 [原文 L456](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:456>)、[原文 L458](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:458>)、[原文 L460](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:460>)

业务Canonical=Tenant/QualityOwner/Family/原InspectionSource/stream/epoch/sequence，运输键另Producer+message。同义新消息复用，异义冲突；ObservedHigh不是AppliedHead。DELTA必须当前predecessor和+1，FULL_SNAPSHOT有完整Family/源范围与取代水位。缺Source/前驱/scope持久等待，提交唤醒Outbox＋30秒/启动扫描；旧worker epoch不回写新头。不同Family不能按时间胜出，需明确FamilyReplacement证据。 [原文 L464](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:464>)、[原文 L466](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:466>)、[原文 L468](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:468>)

### 4.5 原坐标有效范围和更正后重新绑定

Domain冻结原Source/part、Root/Movement、批序技术维度、基本单位与LinePortion映射，范围[0,Q_original)。当前Commercial→唯一完整ScopeSet→逐行有效RangeSet E，E可带洞。初始E全域；后继只能由真实OwnerRangeResolution给完整有效/排除段，不从scalar replacementQty猜尾部VOID，也不压缩原坐标。这个Q_original是该收货叶范围，不是必须使用整张PO或整车到货总量。 [原文 L491](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:491>)、[原文 L448](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:448>)、[原文 L254](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:254>)

四质量RangeSet两两不交且并集恰E，数量各为段长之和；E和excluded两两不交并集恰原域。VOID不是第五个质量态，也不塞Pending；真实消耗/退货/NONCURRENT不等原收货VOID。整段必须落E，仅两端在E但中间跨洞也非法；FULL_SNAPSHOT覆盖E全部获准范围，不靠总量相等。新scope首版必须有权完整后继，不能裁旧DELTA初始化；未变行复用合法旧scope/头。 [原文 L493](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:493>)、[原文 L494](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:494>)、[原文 L495](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:495>)、[原文 L502](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:502>)

100 VOID[20,40)后E=[0,20)∪[40,100)，量80；合法70接受/10拒绝可为Accepted[0,20)+[40,90)、Rejected[90,100)，尾端100仍合法。原scope90/10到85/15为后继替换，不相加。按冻结portion边界切E/质量分区，每个slice带scopeRef和祖先，RECEIPT覆盖全E、ACCEPTANCE仅Accepted，转换到PO六位必须准确。 [原文 L496](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:496>)、[原文 L497](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:497>)、[原文 L6402](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6402>)

Correction需要baseCommercial/baseScopeSet、原WMS Movement正式更正、Authority及原域映射；只给80或Root/UOM不符WAITING_OWNER。ANNOTATION/REFERENCE_REPAIR不能换PO/重生leaf/改范围。Owner correctionIdentity由真实原业务/版本确定，非随机客户端ID；列出行给完整有效/排除，未列行复用，逐行量与replacementQuantity相符，不混量汇总。 [原文 L519](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:519>)、[原文 L521](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:521>)

最终Correction锁Operation/CurrentCommercial/Correction/AcceptanceCursor，先查同Owner唯一原结果，首次核base与Owner前驱/证据头，然后一次提交CorrectionApplication、新scope/完整ScopeSet、新Commercial/RECEIPT Manifest/Fact/Outbox/Audit并推进Current；同时requiredScopeSet指新Set、Cursor RowVersion增、state=ACCEPTANCE_SCOPE_CONFLICT，旧投影／Quality头保留。商业真实纠正可先成立，不为缺质量后继隐藏事实，但验收／MATCH被阻。 [原文 L523](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:523>)、[原文 L525](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:525>)

新scope Quality完整有权后继到齐，最终同时核Commercial/ScopeSet、requiredSet、人工cursor token、全部Quality头/BindingSetHash和worker epoch；唯一(Operation,ScopeSet,QualityHeadSetHash)同义先回，再CAS。一次提交完整AcceptanceApplication/Segments、当前指针和PO ACCEPTANCE全快照。非空E未获Quality不能剪旧Accepted凑数；完整有据VOID为空时允许EMPTY_SCOPE零应用与完整空manifest，不是伪Quality PASS。 [原文 L498](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:498>)、[原文 L500](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:500>)、[原文 L527](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:527>)

响应未知按原CorrectionId或ScopeSet+HeadSet读原应用；Quality先到PENDING_SCOPE，纠正提交后同槽唤醒；旧scope晚到只历史，新消息重贴新scope则冲突。MATCH handoff只有CurrentCommercial ScopeSet＝projectedScopeSet、各line scope/digest一致且coverage完整才200，否则409 GR_ACCEPTANCE_SCOPE_CONFLICT；旧basisRef永远不是当前永久开票许可。 [原文 L529](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:529>)、[原文 L531](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:531>)、[原文 L6404](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6404>)、[原文 L6405](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6405>)、[原文 L6406](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6406>)、[原文 L6407](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6407>)、[原文 L6408](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6408>)、[原文 L6409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6409>)

## 5. 全部接口与Owner契约

### 到货、登记和无副作用读取

| Method / 外部相对Path | 权限；请求→响应 | 成功/错误/持久效果 | 出处 |
| --- | --- | --- | --- |
| GET `/options` | read；→Options | 200当前范围/依赖，不伪造默认角色 | [原文 L263](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:263>) |
| POST `/receivable/search` | read；ReceivableSearch→Page<ReceivableRow> | 200当前观察，不占PO许可；无Owner503 | [原文 L264](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:264>) |
| POST `/lookups` | read；LookupRequest→Page<LookupOption> | 只查当前有权库位/批序/物流单元，不建立它们 | [原文 L265](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:265>) |
| POST `/receipts/search` | read；ReceiptSearch→Page<DraftView> | 分页权限内总数；不显示隐藏商业字段 | [原文 L266](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:266>) |
| POST `/arrivals` | draft.write；ArrivalInput→ArrivalView | 201新可信到货登记／200已知同业务；原凭证同义归一，异内容409 | [原文 L267](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:267>) |
| GET `/arrivals/{id}` | read；→ArrivalView | 200原到货身份/行，非库存余额 | [原文 L268](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:268>) |
| POST `/receipts` | draft.write；DraftInput→DraftView | 201 DRAFT/稳定Operation/LineId，无库存或PO占用 | [原文 L269](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:269>) |
| GET `/receipts/{id}` | read；→DraftView | 200原登记、当前状态及版本 | [原文 L270](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:270>) |
| PATCH `/receipts/{id}` | draft.write；DraftPatch→DraftView | 200仅DRAFT；未知字段400、版本409 | [原文 L271](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:271>) |
| POST `/receipts/{id}/preview` | read；PreviewRequest→PreviewResult | 200即使allowed=false，明确逐门；无数量效果 | [原文 L272](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:272>) |
| POST `/receipts/{id}/freeze` | freeze；FreezeRequest→DraftView | 200固定意图；到货片冲突409；不是PO收货 | [原文 L273](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:273>) |
| GET `/receipts/{id}/status` | read；→ReceiptStatus | 200独立Owner结果，不顺手补库存/验收 | [原文 L274](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:274>) |

### 准备、开始、提交及恢复

| Method / 路径 | 权限/请求→响应 | 状态及副作用 | 出处 |
| --- | --- | --- | --- |
| POST `/api/pur/gr/v1/receipts/{id}/prepare` | receive；PreparationRequest→WorkView | 202 PREPARING，或200已存在Work；登记原Work，不增库存 | [原文 L303](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:303>) |
| GET `/api/pur/gr/v1/works/{id}` | read+有权范围；→WorkView | 当前步骤、原Permit/Stock locator和缺证，GET不执行 | [原文 L304](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:304>) |
| POST `/api/pur/gr/v1/receipts/{id}/start` | receive；GrStartRequest→WorkView | 200 STARTED：只成立开始事实/Permit IN_FLIGHT | [原文 L305](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:305>) |
| POST `/api/pur/gr/v1/receipts/{id}/commit` | receive；GrCommitRequest→ReceiptStatus | 200已收／202持久等待；明确物理/商业轴，不用HTTP200代表验收 | [原文 L306](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:306>) |
| POST `/api/pur/gr/v1/works/{id}/resume` | recover；ResumeRequest→WorkView | 202同Work继续或200终态；不接新量/新业务Key | [原文 L307](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:307>) |
| POST `/api/pur/gr/v1/receipts/{id}/withdraw` | withdraw；GrNoEffectRequest→WorkView | 202封闭待核／200 SEALED；未知不释放保护 | [原文 L308](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:308>) |

### WMS最小Owner适配

| Producer / 路径 | 请求→响应 | GR要求与边界 | 出处 |
| --- | --- | --- | --- |
| WMS-IN `POST /purchase-receipt-intents/prepare` | WmsPrepareRequest→WmsPreparation | 按GR操作/源叶返回原InboundIntent、Stock准备和绑定；不创建库存 | [原文 L322](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:322>) |
| WMS-IN `POST /purchase-receipt-intents/execute` | WmsExecuteRequest→WmsReceiptResult | 完整实际收货；同库Enlist调用才与PO/GR同提交；接口名称不构成已采用证据 | [原文 L323](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:323>) |
| WMS-IN `POST /purchase-receipt-intents/query` | WmsQueryRequest→WmsQueryResult | 原Canonical查回执。NOT_OBSERVED的canInferNoEffect=false，不能重建 | [原文 L324](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:324>) |
| WMS-IN `POST /purchase-receipt-intents/seal` | GrReceiptEffectQuery→GrNoEffectEvidence | 只封本原执行，实际Stock Closure及所有路径证明齐才NO_EFFECT | [原文 L325](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:325>) |
| Stock SP01 / SP02 | STK.PostingPlanRequest→PostingPlanView；PostingRequest→CommandResponse | 准备用实际nextPosting；最终只这一个库存数量Owner写 | [原文 L326](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:326>) |
| Stock SO01 | STK.OperationLookupRequest→OperationLookupResult | 用发送前已保存locator/digest/commandKey；无operationId也可查 | [原文 L327](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:327>) |

### 检验源、范围与验收

| Method / Path | 权限；请求→响应 | 精确含义 | 出处 |
| --- | --- | --- | --- |
| GET `/api/pur/gr/v1/receipts/{id}/inspection-sources` | inspection.read；→Page<InspectionSource> | 原任务源/策略/范围，可打开有权Quality任务 | [原文 L477](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:477>) |
| GET `/api/pur/gr/v1/receipts/{id}/acceptance` | inspection.read；→AcceptanceView | GR所需/已投影ScopeSet与验收分区及Stock/PO应用轴 | [原文 L478](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:478>) |
| GET `/api/pur/gr/v1/receipts/{id}/scope-sets/{commercialVersion}` | inspection.read；→ReceiptScopeSet | 不可变完整行范围映射；Current另从ReceiptStatus取得，不按最大版本猜 | [原文 L479](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:479>) |
| GET `/api/pur/gr/v1/receipts/{id}/lines/{lineId}/effective-scopes/{scopeVersion}` | inspection.read；→ReceiptEffectiveScope | 原坐标有效/排除段、前驱及有权纠正引用 | [原文 L480](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:480>) |
| GET `/api/pur/gr/v1/receipts/{id}/lines/{lineId}/coordinate-domain` | inspection.read；→ReceiptCoordinateDomain | 原Source/part/Root/Movement/单位/映射；不返回新库存余额 | [原文 L481](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:481>) |
| GET `/api/pur/gr/v1/receipts/{id}/corrections/{correctionId}/owner-resolution` | inspection.read＋对应证据读取范围；→OwnerRangeResolution | 只读已验证的Owner原件映射；未取得证据返回202待核，不造空成功 | [原文 L482](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:482>) |
| POST `/api/pur/gr/v1/receipts/{id}/acceptance/apply` | acceptance.apply；AcceptanceApply→AcceptanceView | 只应用已验证Quality头，原头已应用返回原结果；不提交人工合格率 | [原文 L483](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:483>) |
| POST `/internal/pur/gr/v1/quality-notices` | ingest.quality；QualityNotice→IngressAck | 原事实持久，202等候或200原接收/当前处理，不等Stock已应用 | [原文 L484](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:484>) |
| POST `/internal/pur/gr/v1/stock-quality-results` | ingest.physical＋对应质量回执范围；StockQualityNotice→IngressAck | 保存Stock Application观察，不产生GR第二份Acceptance | [原文 L485](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:485>) |

### 对外完整原文和证明

| 只读资源 | 稳定定位 | 返回/性质 | 出处 |
| --- | --- | --- | --- |
| 冻结收货/SourceManifest | `GET /internal/pur/gr/v1/intents/{operationId}` | 完整WmsReceiptIntent及原映射；按Producer范围授权 | [原文 L576](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:576>) |
| 原收货事实 | `GET /internal/pur/gr/v1/facts/{operationId}/{kind}/{version}` | PO.PoFulfillmentFact；kind仅RECEIPT/ACCEPTANCE | [原文 L577](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:577>) |
| 行分配/区间映射 | `GET /internal/pur/gr/v1/allocation-manifests/{operationId}/{kind}/{version}` | ReceiptAllocationManifest；同一事实版本完整集合 | [原文 L578](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:578>) |
| 原操作效果 | `POST /internal/pur/gr/v1/receipt-effects/query` | GrReceiptEffectQuery→GrReceiptEffectResult，只观察 | [原文 L579](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:579>) |
| 接收截止 | `POST /internal/pur/gr/v1/receiving-cutoffs` | PO.PoCutoffRequest→PO.PoCutoffResult，持久登记原请求/证明 | [原文 L580](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:580>) |
| Quality处理查询 | `GET /internal/pur/gr/v1/ingress/{ingressId}` | IngressAck，应用状态更新但原接收凭证不变 | [原文 L581](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:581>) |
| 估价/商业原文 | HandoffBasis中的Ref | 解析准确PO商业与Finance policy实例，不强制Finance已经Posted | [原文 L582](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:582>) |

| 补充路径 | 请求／结果 | 权威效果 | 出处 |
| --- | --- | --- | --- |
| POST /internal/pur/gr/v1/physical-results | PhysicalNotice → IngressAck | 验证WMS真实Canonical/全部叶，登记并唤醒同Work补商业；不再入库 | [原文 L418](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:418>) |
| Quality POST /purchase-inspection-policies/resolve | InspectionPolicyRequest → InspectionPolicyResult | 恰一原策略结果，无规则/冲突UNKNOWN | [原文 L444](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:444>) |
| Quality POST /purchase-inspections；GET /purchase-inspections/by-request/{requestKey} | QualityTaskRequest／QualityTaskResult | 原任务幂等，非验收PASS | [原文 L450](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:450>) |
| POST /receipts/{id}/corrections；GET /corrections/{id}；POST /corrections/{id}/apply | CorrectionInput／CorrectionView／CorrectionApply | 只确凿原事实有据后继，不发Stock正向/负量 | [原文 L519](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:519>) |
| GET /receipts/{id}/lines/{lineId}/handoff-basis | HandoffBasis | 当前版本完整基准，createsMatchOrReturnPermission=false | [原文 L560](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:560>) |
| POST /internal/pur/gr/v1/consumer-results | ConsumerReceipt → IngressAck | 按原outboundEventId记各Consumer结果 | [原文 L556](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:556>) |
| POST /internal/pur/gr/v1/return-notices | ReturnNotice | 真实OUT和原slice关联，无OUT不标实退 | [原文 L566](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:566>) |
| GET /commands/{commandId} | CommandObservation | NOT_OBSERVED不能推无效果 | [原文 L730](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:730>) |
| GET /receipts/{id}/history | Page<HistoryEntry> | Operation本地sequence和原发生时间分开 | [原文 L735](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:735>) |
| POST /exports；GET /exports/{id}；GET /exports/{id}/content | ExportInput／ExportView／CSV | 完整授权快照，下载再核权 | [原文 L737](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:737>) |
| POST /attachments/download-tickets；GET /download-tickets/{id}/content | DownloadInput／DownloadTicket／原文件流 | 仅内部查看，5分钟票据不等披露权 | [原文 L739](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:739>) |

### 准确PO v1.1依赖镜像：Permit、事实及来源

[原文 L6638](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6638>)

```ts
namespace PO {
  export type Id = string;

  export type Key = string;

  export type Seq = string;

  export type Version = string;

  export type Qty = string;

  export type Amount = string;

  export type Instant = string;

  export type EvidenceMode = 'REAL'|'DEMO';

  export interface PoMutation { expectedVersion:Version; }

  export interface PoSourcePortion { id:Id; parentPortionId:Id|null; transferredInSourceQty:Qty; transferredOutSourceQty:Qty; source:GrantShare; poLineId:Id; scheduleId:Id; originalSourceQty:Qty; increasedSourceQty:Qty; cancelledSourceQty:Qty; reducedSourceQty:Qty; sourceUom:string; executionEffectId:Key; dispositionState:'NONE'|'PENDING'|'CONFIRMED'|'CONFLICT'; lineageRef:Ref|null; }

  export interface PoCancelSlice { poLineId:Id; scheduleId:Id; quantity:Qty; uomCode:string; portions:{sourcePortionId:Id;quantity:Qty;uomCode:string}[]; }

  export type PoFactKind = 'RECEIPT'|'ACCEPTANCE'|'INVOICE'|'RETURN'|'INVOICE_CREDIT';

  export interface PoFactIdentity { producerOwner:Key; ledgerKey:Key; documentId:Key; documentLineId:Key; allocationKey:Key; }

  export interface PoFactAllocation { poId:Id; poLineId:Id; scheduleId:Id; sourcePortionId:Id; quantity:Qty; uomCode:string; originalQuantity:Qty; originalUom:string; conversionRef:Ref|null; receiptSliceRef:Ref|null; }

  export interface PoFulfillmentFact { eventId:Id; kind:PoFactKind; identity:PoFactIdentity; factVersion:Seq; previousVersion:Seq|null; state:'POSTED'|'VOID_WITH_EVIDENCE'; supersedesFactRef:Ref|null; effectiveAt:Instant; observedAt:Instant; evidenceMode:EvidenceMode; allocations:PoFactAllocation[]; relatedReceiptRefs:Ref[]; movementRefs:Ref[]; qualityOrExemptionRefs:Ref[]; apPostingRefs:Ref[]; amountComponents:{amount:Amount;currencyCode:string;role:'NET'|'TAX'|'GROSS'|'FEE'}[]; correctionReason:string|null; correctionAuthorityRef:Ref|null; receivingPermitId:Id|null; }

  export interface PoFactAck { eventId:Id; factRef:Ref|null; result:'APPLIED'|'DUPLICATE'|'HISTORICAL'|'QUARANTINED'; currentFactVersion:Seq|null; fulfillmentRevision:Seq|null; errorCodes:string[]; }

  export interface PoReceivingRequest { receiptOperationKey:Key; poId:Id; expectedOrderVersion:Version; purpose:'PURCHASE_RECEIPT'; rows:{scheduleId:Id;sourcePortionId:Id;quantity:Qty;uomCode:string}[]; policyRef:Ref; }

  export interface PoReceivingPermit { id:Id; version:Version; receiptOperationKey:Key; poId:Id; guardRevision:Seq; state:PoPermitState; scopeHash:string; rows:{scheduleId:Id;sourcePortionId:Id;quantity:Qty;uomCode:string}[]; expiresAt:Instant|null; receiptEvidenceRefs:Ref[]; evaluatedGuards:PoReceivingGuardVector[]; currentGuards:PoReceivingGuardVector[]; startRef:Ref|null; ownerStartIntentRef:Ref|null; startedAt:Instant|null; everStarted:boolean; blockingControlRefs:Ref[]; noEffectProofRef:Ref|null; settledSegments:PoPermitSegmentResult[]; }

  export interface PoCutoffRequest { cancellationId:Id; poId:Id; scopeHash:string; slices:PoCancelSlice[]; expectedGuardRevision:Seq; }

  export interface PoCutoffResult { cancellationId:Id; scopeHash:string; result:'COMPLETE_NO_PENDING_EFFECT'|'PENDING'|'CONFLICT'; proofRef:Ref|null; coveredPermitIds:Id[]; uncoveredPermitIds:Id[]; producerWatermarks:{producerOwner:Key;streamId:Key;epoch:Key;version:Seq}[]; fulfillmentDigest:string; asOf:Instant; }

  export type PoPermitState = 'HELD_NOT_STARTED'|'IN_FLIGHT'|'USED'|'NO_EFFECT_CLOSED'|'BLOCKED'|'REVOKE_PENDING'|'UNKNOWN'|'SETTLED_PARTIAL';

  export interface PoReceivingGuardVector {
  scheduleId:Id; sourcePortionId:Id; guardRevision:Seq; scopeControlRef:Ref;
  technicalHoldRefs:Ref[]; cancellationHoldRefs:Ref[]; changeHoldRefs:Ref[];
  closureBarrierRefs:Ref[]; receivingClosed:boolean;
}

  export interface PoPermitSegmentResult {
  sourcePortionId:Id; segmentId:Id; quantity:Qty; uomCode:string;
  result:'USED'|'NO_EFFECT'; evidenceRef:Ref;
}

  export interface PoReceivingStart extends PoMutation {
  receiptOperationKey:Key; expectedGuardRevision:Seq;
  expectedGuards:PoReceivingGuardVector[]; scopeHash:string; ownerStartIntentRef:Ref;
}

  export interface Ref { owner: Key; id: Key; version: Key; }

  export interface SourceResponsibility {
  owner: Key; sourceType: 'PR' | 'AUTHORIZED_PURCHASE'; sourceId: Key;
  sourceLineId: Key; authorizationScopeId: Key;
}

  export interface GrantShare {
  grantRef: Ref; responsibility: SourceResponsibility; sourceBusinessVersion: Key;
  quantity: Qty; sourceUom: string; generation: Seq;
}

  export interface PoReceivingCommitInput { permitId:Id; receiptOperationKey:Key; expectedPermitVersion:Version; expectedGuardRevision:Seq; expectedGuards:PoReceivingGuardVector[]; startRef:Ref; scopeHash:string; receiptIdentity:PoFactIdentity; receiptSliceRefs:Ref[]; quantities:{sourcePortionId:Id;quantity:Qty;uomCode:string}[]; }
}
```

### 准确Stock v1.1.1依赖镜像：来源、数量、容量、质量与查询

[原文 L6716](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6716>)

```ts
namespace STK {
  export type Guid = string;

  export type PositiveInt64 = string;

  export type NonNegativeInt64 = string;

  export type DecimalQuantity = string;

  export type TotalDecimal = string;

  export type Instant = string;

  export type Digest = string;

  export interface VersionedCodeRef { id: string; version: string; }

  export interface EvidenceRef { owner: string; id: string; version: string; digest: Digest; }

  export interface ReceiptRef { owner: string; id: string; version: string; }

  export interface ScopeRef { targetKey: string; definitionRef: EvidenceRef | null; memberDigest: Digest; }

  export interface PoolVersion { poolId: Guid; revision: NonNegativeInt64; }

  export interface SourcePosition {
  owner:string; kind:string;
  wireBusinessTarget:{namespace:string;documentId:string;lineId:string;scopeKey:string};
  streamId:string; epoch:string; sequence:NonNegativeInt64; semanticDigest:Digest;
}

  export interface QuantitySpan { rootId: Guid; from: DecimalQuantity; to: DecimalQuantity; }

  export interface WireSource {
  producer: string; sourceKind: string; documentId: string;
  lineId: string; executionKey: string; sourceVersion: string;
}

  export interface SourceUse {
  source: WireSource; allowanceRef: EvidenceRef;
  inputUomRef: VersionedCodeRef; conversionRef: EvidenceRef | null;
}

  export interface TechnicalIdentity {
  kind: 'KNOWN' | 'NOT_APPLICABLE' | 'UNKNOWN';
  revisionRef: EvidenceRef | null; applicabilityRef: EvidenceRef | null;
}

  export interface OwnerIdentity { kind: 'SELF' | 'CUSTOMER'; reference: EvidenceRef; }

  export interface LotIdentity {
  kind: 'TRACKED' | 'NOT_TRACKED' | 'UNKNOWN';
  lotId: string | null; lotCode: string | null; policyRef: EvidenceRef | null;
}

  export interface StockDimensions {
  siteId: Guid; productRef: EvidenceRef; technicalIdentity: TechnicalIdentity;
  owner: OwnerIdentity; originPurposeRef: VersionedCodeRef; baseUomRef: VersionedCodeRef;
  precisionPolicyRef: VersionedCodeRef; lot: LotIdentity;
  serialRef: EvidenceRef | null; handlingUnitRef: EvidenceRef | null;
}

  export type Place =
  | {kind:'LOCATION'; warehouseId:Guid; locationId:Guid}
  | {kind:'TRANSIT'; transitAccountRef:EvidenceRef}
  | {kind:'EXTERNAL'; custodyReceiptRef:EvidenceRef}
  | {kind:'CONSUMED'; consumptionRef:EvidenceRef}
  | {kind:'VOID'; correctionRef:EvidenceRef};

  export interface RequestedUse {
  action: 'RESERVE' | 'ALLOCATE' | 'PICK' | 'SHIP' | 'CONSUME' | 'MOVE' | 'RECEIVE' | 'RETURN' | 'ADJUST' | 'CORRECT';
  purposeRef: VersionedCodeRef; demandRef: EvidenceRef | null;
  technicalRequirementRef: EvidenceRef | null;
}

  export interface ClaimSpend { claimId: Guid; expectedClaimRevision: PositiveInt64; bindingId: Guid; spans: QuantitySpan[]; }

  export interface PlanRef { id: Guid; version: PositiveInt64; digest: Digest; }

  export interface OperationLocator { producer: string; executionKey: string; commandKey: Guid; }

  export interface Coverage { state:'COMPLETE'|'PARTIAL'|'UNKNOWN'|'RESTRICTED'; reasons:string[]; }

  export interface ReceiptLine {
  clientLineKey: string; dimensions: StockDimensions; destination: Place;
  inputQuantity: DecimalQuantity; sourceUse: SourceUse;
  qualityBasis: {kind:'REQUIRES_INSPECTION'|'EXPLICIT_SKIP'; policyRef:EvidenceRef};
}

  export interface RelocationLine {
  clientLineKey: string; spans: QuantitySpan[]; sourcePlace: Place; destination: Place;
  inputQuantity: DecimalQuantity; sourceUse: SourceUse; retainedClaimIds: Guid[];
}

  export interface IssueLine {
  clientLineKey: string; spans: QuantitySpan[]; sourcePlace: Place; destination: Place;
  inputQuantity: DecimalQuantity; sourceUse: SourceUse;
  reservationMode: 'CONSUME_OWN_CLAIM' | 'AUTHORIZED_FREE';
  claimSpends: ClaimSpend[]; freeIssueAuthority: EvidenceRef | null;
}

  export interface ReturnLine extends RelocationLine {
  returnQuality:{kind:'PENDING_INSPECTION'|'REACCEPTED';basisRef:EvidenceRef};
}

  export interface AdjustmentLine {
  clientLineKey: string; direction:'GAIN'|'LOSS'; sourceUse:SourceUse;
  dimensions:StockDimensions | null; place:Place; inputQuantity:DecimalQuantity;
  spans:QuantitySpan[]; countOrDisposalDecision:EvidenceRef;
}

  export type PostingData =
  | {kind:'RECEIPT'; lines:ReceiptLine[]}
  | {kind:'MOVE'|'TRANSIT_DISPATCH'|'TRANSIT_RECEIVE'; lines:RelocationLine[]}
  | {kind:'SHIP'|'CONSUME'; lines:IssueLine[]}
  | {kind:'ADJUSTMENT'; lines:AdjustmentLine[]}
  | {kind:'RETURN'; originalMovementRef:ReceiptRef; lines:ReturnLine[]};

  export interface PostingPlanRequest {
  source:WireSource; executionManifestRef:EvidenceRef; occurredAt:Instant; use:RequestedUse;
  expectedPools:PoolVersion[]; data:PostingData; reason:string;
}

  export interface PostingRequest { planRef:PlanRef; reason:string; }

  export interface PlannedRoot { clientLineKey:string; sourceRootKey:string; quantity:DecimalQuantity; }

  export interface PlannedLeg { lineKey:string; accountKey:string; signedQuantity:DecimalQuantity; baseUomRef:VersionedCodeRef; }

  export interface PostingPlanView {
  id:Guid; version:PositiveInt64; digest:Digest; targetKey:string;
  source:WireSource; executionManifestRef:EvidenceRef; executionManifest:ExecutionManifest;
  executionIdentity:ExecutionIdentity; capacityBindings:ReceiptCapacityBinding[];
  data:PostingData; occurredAt:Instant; use:RequestedUse;
  expectedPools:PoolVersion[]; plannedRoots:PlannedRoot[]; legs:PlannedLeg[];
  requiredEvidence:EvidenceRef[]; coverage:Coverage; blockingReasons:string[];
  canSubmit:boolean; nextPosting:PostingRequest | null; simulation:boolean;
}

  export interface AttemptView {
  id:Guid; revision:PositiveInt64; epoch:PositiveInt64;
  step:'POST'|'LOOKUP_SOURCE'|'CONFIRM_SOURCE'|'RELEASE_SEALED'|'REDELIVER'|'REBUILD_READ_MODEL';
  state:'PENDING'|'RUNNING'|'WAITING'|'SUCCEEDED'|'FAILED_RETRYABLE'|'CONFLICT';
  bindingId:Guid|null; errorCode:string|null; observedAt:Instant;
}

  export interface StockResult {
  receipt:ReceiptRef; movementId:Guid|null; claimId:Guid|null; restrictionId:Guid|null;
  correctionId:Guid|null; resolutionId:Guid|null; relatedMovementIds:Guid[];
  closure:{closedIdentity:ExecutionIdentity;quantityEffectOnly:true;scope:ScopeRef;retainedActualFactRefs:ReceiptRef[]}|null;
  reconciliation:{caseId:Guid;caseRevision:PositiveInt64;state:'OPEN'|'PARTIALLY_RESOLVED'|'RESOLVED'|'RISK_ACCEPTED_WITH_UNRESOLVED'}|null;
  ledgerEntries:{poolId:Guid; sequence:PositiveInt64}[];
  roots:{lineKey:string; rootId:Guid; quantity:DecimalQuantity}[];
  poolVersions:PoolVersion[]; semanticDigest:Digest;
}

  export interface CommandResponse {
  operationId:Guid; locator:OperationLocator;
  status:'ACCEPTED'|'PROCESSING'|'SUCCEEDED'|'REJECTED'|'SEALED_NO_EFFECT'|'RECONCILIATION_REQUIRED';
  commitKnowledge:'NOT_ACCEPTED'|'NO_NEW_EFFECT'|'COMMITTED'|'UNKNOWN';
  result:StockResult|null; resultCode:string; replayed:boolean;
  consumerStates:{consumer:string; state:'PENDING'|'RECEIVED'|'APPLIED'|'REJECTED'|'UNKNOWN'; receipt:ReceiptRef|null}[];
  attempts:AttemptView[]; executionRevision:PositiveInt64; allowedRecoverySteps:string[];
  currentReadPermission:boolean; simulation:boolean;
}

  export interface BusinessLocator { producer:string; executionKey:string; }

  export type StockWriteKind =
  'POSTING'|'CLAIM_CREATE'|'CLAIM_BACKING'|'CLAIM_ALLOCATE'|'CLAIM_PICK'|
  'CLAIM_RELEASE'|'CLAIM_REBIND'|'HOLD_PLACE'|'HOLD_RELEASE'|
  'CORRECTION_APPLY'|'RECONCILIATION_APPLY'|'SOURCE_APPLICATION';

  export interface OccurrenceLeaf {
  producer:string; occurrenceKey:string; effectKey:string;
}

  export interface SourceManifestPart {
  clientLineKey:string; source:WireSource; occurrence:OccurrenceLeaf;
  inputQuantity:DecimalQuantity; inputUomRef:VersionedCodeRef;
  baseQuantity:DecimalQuantity; baseUomRef:VersionedCodeRef;
  allowanceRef:EvidenceRef; lineBusinessDigest:Digest;
}

  export interface ExecutionManifest {
  schema:'STK-SOURCE-EXECUTION-1'; parent:WireSource;
  postingKind:PostingData['kind']; requestedAction:RequestedUse['action'];
  occurredAt:Instant; parts:SourceManifestPart[];
  authorityRef:EvidenceRef; scope:ScopeRef;
}

  export interface ExecutionIdentity {
  locator:BusinessLocator; writeKind:StockWriteKind;
  businessSchema:string; businessDigest:Digest;
}

  export interface OperationLookupRequest {
  locator:BusinessLocator; businessDigest:Digest; commandKey:Guid|null;
}

  export type OperationLookupResult =
 | {lookupStatus:'FOUND'; operation:CommandResponse; observedAt:Instant}
 | {lookupStatus:'NOT_OBSERVED'; locator:BusinessLocator; businessDigest:Digest;
    commitKnowledge:'UNKNOWN'; canInferNoEffect:false; observedAt:Instant}
 | {lookupStatus:'UNAVAILABLE'; locator:BusinessLocator; businessDigest:Digest;
    commitKnowledge:'UNKNOWN'; retryAfterSeconds:number; observedAt:Instant};

  export interface QuantityRange { from:DecimalQuantity; to:DecimalQuantity; }

  export interface CapacityMetricBinding {
  metricRef:VersionedCodeRef; capacityUomRef:VersionedCodeRef;
  receiptBaseToCapacityRef:EvidenceRef;
  physicalCapacityQuantity:DecimalQuantity;
  capacityDomainKey:string; expectedCapacityRevision:NonNegativeInt64;
}

  export interface MatchedCapacitySegment {
  kind:'MATCHED_OBLIGATION'; receiptInputRange:QuantityRange;
  receiptBaseRange:QuantityRange;
  obligation:{owner:string;lineageKey:string;shareKey:string;
              shareRange:QuantityRange;shareUomRef:VersionedCodeRef;
              receiptBaseToShareRef:EvidenceRef;
              expectedRevision:PositiveInt64;delegationRef:EvidenceRef};
  metrics:(CapacityMetricBinding & {unreceivedCapacityQuantity:DecimalQuantity})[];
  relationEvidence:EvidenceRef;
}

  export interface NoObligationCapacitySegment {
  kind:'KNOWN_NONE'; receiptInputRange:QuantityRange;receiptBaseRange:QuantityRange;
  metrics:CapacityMetricBinding[]; absenceEvidence:EvidenceRef;
}

  export type ReceiptCapacityBinding =
 | {status:'RESOLVED';partKey:string;bindingRef:EvidenceRef;
    relationSemanticDigest:Digest;mappingHeadRef:EvidenceRef;
    mappingRevision:PositiveInt64;
    segments:(MatchedCapacitySegment|NoObligationCapacitySegment)[]}
 | {status:'UNKNOWN';partKey:string;bindingRef:EvidenceRef|null;
    reasons:string[];unresolvedInputRanges:QuantityRange[]};

  export type InboxWaitDependency =
 | {kind:'SOURCE_ROOT';source:WireSource;partKey:string}
 | {kind:'SOURCE_STREAM_PREDECESSOR';owner:string;sourceKind:string;
    wireBusinessTarget:SourcePosition['wireBusinessTarget'];streamId:string;epoch:string;requiredSequence:NonNegativeInt64}
 | {kind:'WIRE_TARGET';owner:string;targetKey:string}
 | {kind:'AUTHORIZATION';producer:string;scopeKey:string;policyVersion:string|null};

  export interface InboxProcessingView {
  inboxId:Guid;processingRevision:PositiveInt64;
  state:'RECEIVED'|'READY'|'RUNNING'|'PENDING_SOURCE'|'PENDING_TARGET'|'GAP'|
        'BLOCKED_AUTH'|'RETRYABLE_ERROR'|'APPLIED'|'HISTORY_ONLY'|'CONFLICT'|'REJECTED';
  waitingOn:InboxWaitDependency[];nextAttemptAt:Instant|null;
  applicationReceipt:ReceiptRef|null;applicationId:Guid|null;
  lastError:string|null;observedAt:Instant;
}

  export interface IngressView {
  ingressId:Guid;messageId:Guid;ingressReceipt:ReceiptRef;
  receivedAt:Instant;originalWireDigest:Digest;duplicateTransport:boolean;
  processing:InboxProcessingView;simulation:boolean;
}

  export interface InboxResumeRequest {
  expectedProcessingRevision:PositiveInt64;reason:string;
}

  export interface WireReceiptSubject {
  receiptSource:WireSource; receiptLineKey:string;
  range:{from:DecimalQuantity; to:DecimalQuantity};
}

  export interface QualityApplicationRequest {
  messageId:Guid; owner:string; decisionRef:EvidenceRef;
  stream:{id:string; epoch:string; sequence:PositiveInt64; previousSequence:NonNegativeInt64};
  decisionFamilyRef:EvidenceRef; replacesDecisionRef:EvidenceRef|null;
  subjects:{subject:WireReceiptSubject; quality:'ACCEPTED'|'REJECTED'|'HOLD'|'PENDING'; basisRef:EvidenceRef}[];
  stateForm:'DELTA'|'FULL_SNAPSHOT'; snapshotProofRef:EvidenceRef|null;
  occurredAt:Instant; semanticDigest:Digest; simulation:boolean;
}

  export interface QualityApplicationResult {
  applicationId:Guid; receipt:ReceiptRef; decisionRef:EvidenceRef;
  outcome:'APPLIED'|'APPLIED_WITH_NONCURRENT_SUBJECTS';
  currentAffectedQuantity:TotalDecimal; nonCurrentQuantity:TotalDecimal;
  subjectResults:{subject:WireReceiptSubject; outcome:string; currentPlaces:Place[]; affectedClaims:Guid[]}[];
  poolVersions:PoolVersion[]; quantityMovementCreated:false; consumerStates:{consumer:string; state:string}[];
}
}
```

上列外域联合包含MOVE/SHIP等依赖类型，仅为准确声明闭包，本GR只允许明确RECEIPT子集及必要原结果恢复。Stock准备返回的businessDigest/PlanDigest作为准确opaque值贯穿，不由GR例子固定hash或自己的算法替换。 [原文 L6632](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6632>)、[原文 L6634](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6634>)、[原文 L754](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:754>)、[原文 L7011](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:7011>)

PO事实保原wire，不新增REJECTED kind。producer=PUR_GR、ledgerKey=配置GR登记域，documentId=Operation、documentLineId=OPERATION；RECEIVE/ACCEPTANCE按kind与allocation分域。每版全操作所有合法行完整快照，后继+1/准确supersedes；RECEIPT与ACCEPTANCE各manifest绑定当前Commercial ScopeSet，relatedReceipt指当前商业事实，历史沿前驱保留。技术ACK只记投递，APPLIED及真实Ref才记消费成功，GAP/隔离继续补原链。 [原文 L538](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:538>)、[原文 L540](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:540>)、[原文 L542](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:542>)、[原文 L544](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:544>)、[原文 L546](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:546>)、[原文 L548](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:548>)

Planning的SupplyTransition常量createsAdditionalSupply=false，只关联PO SourcePortion/Lineage、ReceiptSlice、StockReceipt和PO实收Fact；组成不齐WAITING_COMPONENTS。PO未到100→0和Stock待检100是同一转换，不额外GR100；GR不MRP/改需求。MATCH/RETURN读版本化HandoffBasis不获可开票/可退额度，最终需当前头条件验证。实退100中10只登记真OUT关联，不改历史实收100、不再发-10或开放PO再收10。 [原文 L552](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:552>)、[原文 L554](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:554>)、[原文 L556](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:556>)、[原文 L560](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:560>)、[原文 L562](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:562>)、[原文 L566](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:566>)、[原文 L568](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:568>)

## 6. 事务锁序、幂等、截止与故障恢复

组合ResourcePlan必须同时保持PO与Stock内部顺序：全部Stock Canonical/SourceOccurrenceLeaf决定锁 → PO/PLM共享Scope及WMS地点祖先Scope → Source权威资源 → PO根/ReceivingGuard/Permit/ReceiptIdentity → Stock既有细资源统一Ordinal（Allowance/Pool/Root/Capacity/映射头/质量流）→ GR事实/Work epoch及各Owner审计。新Root用源叶决定锁不预知ID；各入口预收最强模式和所有资源，变化rollback重收，不在锁中增范围或反向回调。 [原文 L627](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:627>)、[原文 L629](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:629>)、[原文 L631](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:631>)

所有Context同实际DbConnection/DbTransaction且顶层唯一commit；不同DB/principal、隐式反锁、独立commit或未知参与者都不能靠同名服务证明全局串行。网络/IAM/文件在锁外，最后复核本地权威头/期限。此组合是GR已接受方案，仍需所有真实writer及旧入口采用证据，不能因为文本定义即称已实现。 [原文 L633](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:633>)

Work一步步PERMIT→WMS_PLAN→START→EXECUTE→LOOKUP/RECORD_COMMERCIAL→DELIVER，固定原输入/Hash；resume只原步骤。30秒lease/epoch，网络无锁，旧epoch只能提交真实证据不能新效果；退避20次人工待续仍WAITING/UNKNOWN。OwnerCall在网络前保存原key/body/digest及EverDispatched，未知先查，不生成另InboundIntent。 [原文 L385](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:385>)、[原文 L387](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:387>)

| 实际窗口 | 可以确定什么 | 恢复 / 禁止 | 出处 |
| --- | --- | --- | --- |
| prepare请求响应丢失 | Permit/Plan可能存在 | 查原键；同命令/同业务不能创建第二Permit | [原文 L403](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:403>) |
| start事务提交未知 | 可能已有IN_FLIGHT | 查原StartIntent和PO startRef；未确定前不发物理动作 | [原文 L404](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:404>) |
| 正常同库事务未提交 | 无已提交效果，或暂不能确认 | 确定回滚且原意图可执行才续原Work；未知查Canonical | [原文 L405](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:405>) |
| Stock已POSTED、GR未记 | Physical真实成立 | RECORD_COMMERCIAL，同StockReceipt零新增Movement | [原文 L406](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:406>) |
| Stock NOT_OBSERVED | 只知道当前未观察 | UNKNOWN，不推no-effect，不换键 | [原文 L407](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:407>) |
| Stock终态REJECTED/SEALED | 原意图不能执行 | 返回原终态；新真实业务要明确后继授权，不能自动改键复活 | [原文 L408](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:408>) |
| 收到部分真实行/混合无效果 | 实际范围不完整 | PARTIAL_FACTS＋Permit对应分片保护；WMS/PO既有对账，不盲补余行 | [原文 L409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:409>) |
| GR Commercial已提交、PO出站丢失 | GR事实真实，PO视图落后 | 重发原FactIdentity/version，PO重复一次；不再物理入库 | [原文 L410](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:410>) |
| Quality task请求失败 | 库存待检100仍真实 | 同InspectionSource/stage/policy查/重试Task，不通过空结果释放 | [原文 L411](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:411>) |
| Quality判定先于GR/Stock到 | 收到真实决定但缺映射 | 原CanonicalInbox待依赖，Root/商业提交唤醒＋补扫描；不造占位根 | [原文 L412](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:412>) |
| Quality应用成功、WMS应用未知 | GR Accepted可已90，Stock当前可用仍不确定 | 分别显示；请求Quality原投递/Stock原Ingress续办，不再创建质量事实 | [原文 L413](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:413>) |
| 票据/真实退货后来到 | 独立Owner新事实 | 保留原GR，登记链接/RETURN观察，不改原Received为未发生 | [原文 L414](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:414>) |

Command.hash采用GR规范{method,path,body}，业务FrozenContent摘要另有排除项，不能默认与PO/RFQ原UTF8指纹是同一算法。原终态先回当前权仍核，换HTTP Command不绕业务Canonical；无命令记录仍canInferNoEffect=false，GR无快捷本地fence替Stock/PO所有路径。 [原文 L728](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:728>)、[原文 L730](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:730>)、[原文 L294](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:294>)

withdraw先持久封派发。纯无外域保护Draft可撤，已有Permit/Stock准备须逐已知路径、活动lease、Stock叶/Canonical、PO start取得永久不可晚执行证明才SEALED并释放Arrival当前安排。Stock已POSTED回ALREADY_EFFECTIVE不发负移动。 [原文 L424](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:424>)

GR截止适配绑定PO原CancelWork的request/scope/slices，枚举Operation→Permit→Stock所有登记路径；全部Permit USED完整映射或NO_EFFECT_CLOSED、无遗漏在途且屏障禁新start，才签COMPLETE_NO_PENDING_EFFECT。最终GR受理共享Scope/Guard竞争；空表／超时／清队列不足。Proof固定GR-NOEFFECT/operation/version或GR-CUTOFF/request/version真实对象，后续晚10反证留旧Proof并增Conflict，通知准确PO/Source原取消Refs，不自行回滚取消/返额度。 [原文 L426](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:426>)、[原文 L428](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:428>)

| HTTP / code | 准确触发 | 允许的恢复 | 出处 |
| --- | --- | --- | --- |
| 400 GR_VALIDATION | 未知/重复字段、格式、必填、范围不合法 | 定位字段，零业务数量效果 | [原文 L692](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:692>) |
| 401 GR_UNAUTHENTICATED / 403 GR_FORBIDDEN | 身份/动作/字段/机器范围无权 | 清敏感缓存，不借机器权限读数据 | [原文 L693](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:693>) |
| 404 GR_NOT_FOUND | 不存在或不可见 | 返回安全列表，不泄对象存在 | [原文 L694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:694>) |
| 428 GR_VERSION_REQUIRED / 409 GR_VERSION_CONFLICT | 条件写token缺失或旧 | 读差异，不能自动覆盖 | [原文 L695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:695>) |
| 409 GR_COMMAND_CONTENT_CONFLICT | 同Command异method/path/body | 查原结果，不改原命令 | [原文 L696](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:696>) |
| 409 GR_SOURCE_OCCURRENCE_CONFLICT | 到货叶/范围已经属于别的未决或有效操作 | 定位自己有权原Operation；不换Key逃避 | [原文 L697](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:697>) |
| 422 GR_PO_MATCH_INVALID | PO/行/Schedule/技术/Site/SourcePortion错 | 重选合法父子关系；原到货事实不删 | [原文 L698](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:698>) |
| 422 GR_RECEIPT_QUANTITY_INVALID | 实际量/合量/来源范围/UOM不守恒 | 不逐行漏检、不截为可收量 | [原文 L699](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:699>) |
| 422 GR_PRECISION_MAPPING_UNSUPPORTED | 8→6或UOM换算不能精确表达 | 正规新收挡住；已发生事实保原值、PO出站待映射 | [原文 L700](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:700>) |
| 422 GR_QUALITY_POLICY_UNKNOWN | 无有效策略或冲突 | 草稿可留，普通新物理接收不按免检 | [原文 L701](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:701>) |
| 422 GR_VALUATION_REQUIRED | 实际政策要求估价且缺原币/价值证据 | 阻该新效果，不填0 | [原文 L702](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:702>) |
| 409 GR_INTENT_CHANGED | freeze后量/批位/源/Quality/商业发生不同语义 | 原Work保留；合法后继，不能覆盖已受理计划 | [原文 L703](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:703>) |
| 503 GR_OWNER_PROTOCOL_NOT_ADOPTED | 同库参加或等价协议缺证 | 只阻对应真实新效果；文档DEMO不当REAL | [原文 L704](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:704>) |
| 409 GR_PERMIT_GUARD_CHANGED | PO token/vector/当前控制改变 | 查原效果后有权revalidate；不盲套latest | [原文 L705](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:705>) |
| 409 GR_RECEIVING_BLOCKED | 技术硬停、未开始遇软屏障、最终Close/Cancel | 按PO已接受拒绝；真实历史仍接证 | [原文 L706](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:706>) |
| 410 GR_EXECUTION_SEALED | 原业务永久无效果封闭 | 原键不复活；其它真实后继需授权 | [原文 L707](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:707>) |
| 409 GR_STOCK_IDENTITY_CONFLICT | Stock父/叶/摘要/真实回执不匹配 | 隔离原证据，不再Posting | [原文 L708](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:708>) |
| 409 GR_PARTIAL_PHYSICAL_FACTS | 声称全收却仅部分真实行/腿 | PARTIAL_FACTS，保留Permit未决份额，交Owner对账 | [原文 L709](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:709>) |
| 409 GR_QUALITY_CONTENT_CONFLICT | 同Canonical质量序列异义/分叉 | 不选最新覆盖，等有权更正 | [原文 L710](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:710>) |
| 409 GR_QUALITY_SCOPE_INVALID | Disposition区间超量/重叠/错批/错源 | 保原消息异常，不裁剪为可接受 | [原文 L711](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:711>) |
| 409 GR_SCOPE_VERSION_CHANGED | Base Commercial/ScopeSet、cursor token或Quality绑定scope已过期 | 保历史/原请求，不自动换当前scope；重新读取准确后继 | [原文 L712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:712>) |
| 409 GR_SCOPE_EVIDENCE_CONFLICT | 同Owner纠正身份异义、原domain不符或Quality同Canonical换绑定 | 隔离原件和冲突；不得覆盖/拼接范围 | [原文 L713](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:713>) |
| 422 GR_SCOPE_COVERAGE_INVALID | 有效/排除范围不守恒、跨VOID、跨单位或Quality快照缺段 | 不截断配平；补有权完整证据 | [原文 L714](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:714>) |
| 409 GR_ACCEPTANCE_SCOPE_CONFLICT | Current Commercial ScopeSet与已投影验收范围不一致 | MATCH基准不返回当前资格；等准确Quality后继/EMPTY_SCOPE应用 | [原文 L715](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:715>) |
| 409 GR_QUALITY_HEAD_CHANGED | 应用准备后Quality头/Commercial前驱变 | 原申请不生效，读真实当前头重新明确应用 | [原文 L716](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:716>) |
| 202（Ingress）GAP/PENDING/PENDING_SCOPE | 有合法事实但缺前驱/Root/商业源或准确scope | 同槽持久唤醒与扫描，不新建成功应用 | [原文 L717](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:717>) |
| 409 GR_CUTOFF_PROOF_CONFLICT | 真实晚成功反证已签截止 | 保事实＋原Proof，通知PO/Source精确差异 | [原文 L718](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:718>) |
| 422 GR_CORRECTION_DEPENDENCIES_OPEN | 物理/Quality/MATCH依赖未证 | WAITING_OWNER，不改历史或库存 | [原文 L719](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:719>) |
| 409 GR_WORK_BUSY / GR_WORK_EPOCH_CHANGED | 另一Worker持权或已接管 | 读原Work，旧epoch不得写效果 | [原文 L720](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:720>) |
| 422 GR_EXPORT_LIMIT / 403 GR_DOWNLOAD_REVOKED | 超导出上限或下载时失权 | 不提供截断成功文件/旧授权明文 | [原文 L721](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:721>) |
| 503 GR_DEPENDENCY_UNAVAILABLE / GR_RESULT_UNKNOWN | 读取暂失联/commit未知 | 分清未接入与未知，原键查询 | [原文 L722](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:722>) |

## 7. 权限、字段保护与审计

pur.gr.draft.write/freeze/receive/inspection.read/acceptance.apply/recover/withdraw等按动作分开；export、attachment.download、attachment.disclose独立。机器ingest.physical/quality/po/return及worker绑定Producer/Contract/Tenant/Site，用户不能靠body.type冒充。对象无权404、可见无动作/字段403；Role人员配置不是默认超级管理员。 [原文 L111](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:111>)、[原文 L118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:118>)

完整preview/商业基准需commercial.read；无商业权仓库操作者只读不含价的状态和Work，机器范围不转授。前端撤权清敏感缓存；合法后到机器事实照收，用户当前详情/下载照常禁。导航只用注册routeKey和有权ID，不接受原文任意URL。 [原文 L86](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:86>)、[原文 L94](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:94>)、[原文 L444](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:444>)

History按每Operation TimelineSequence，OccurredAt与RecordedAt分开，total与items同一短一致读；不同Owner序号不可直接比。导出完整筛选最多10000，不截断假成功，商业列需双权，CSV公式前缀转查看文本；下载再核现权、票据5分钟、导出24小时是技术默认非法律保留。日志不放商业正文/token。 [原文 L735](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:735>)、[原文 L737](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:737>)、[原文 L739](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:739>)、[原文 L741](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:741>)

## 8. 页面和可见状态

| 页面 | 拟议前端路由 | 内容/按钮/后续 | 出处 |
| --- | --- | --- | --- |
| 可收PO工作台 | `/pur/goods-receipts/receivable` | 按PO/Supplier/Site/交期筛选，精确Schedule、当前可收观察、未决Permit与控制；“建立登记”不是直接收货 | [原文 L79](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:79>) |
| 收货列表 | `/pur/goods-receipts` | GR号、来源、物理/商业/质量/出站四列；异常筛选，不用单一完成率 | [原文 L80](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:80>) |
| 登记/详情 | `/pur/goods-receipts/:operationId` | 到货原件、PO匹配、批次/序列/仓位、数量换算、质量策略、原商业、执行轨迹和凭证 | [原文 L81](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:81>) |
| 质量与验收 | 同详情`?tab=inspection` | 策略/任务、Quality原判定、GR应用、Stock应用分别显示；可查原任务，不能编辑判定成PASS | [原文 L82](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:82>) |
| 恢复工作台 | `/pur/goods-receipts/recovery` | 按原ReceiptOperation/Work、原Stock locator/digest查询；重启/失联、缺前驱、缺证及先后事实 | [原文 L83](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:83>) |
| 更正导航 | `/pur/goods-receipts/:id/corrections` | 对确切商业事实提出后继；涉及物理变更转对应WMS纠错原流程，不提供改库存余额 | [原文 L84](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:84>) |

| 动作 | 权限/前置 | 实际效果 | 失败或未知后的动作 | 出处 |
| --- | --- | --- | --- | --- |
| 保存登记 | `draft.write`；对象/字段有权 | DRAFT及稳定LineId，零PO/Stock效果 | 字段保留；同Command查原结果，不能另生成GR | [原文 L100](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:100>) |
| 匹配/预检 | `read`＋必要商业读；准确PO修订 | 只读映射、换算、策略及阻断清单 | 失败区别UNKNOWN/不适用，不假0余量 | [原文 L101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:101>) |
| 冻结本次收货 | `freeze`；全部行/到货份额确定 | 固定意图、到货片占用和摘要 | 被另一操作占用则冲突，草稿仍在；不取Stock余额 | [原文 L102](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:102>) |
| 准备接收 | `receive`；FROZEN且Owner路径可用 | 登记Work，取得原Permit/Stock计划，未增库存 | 202查询原Work；不足不静默减量 | [原文 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:103>) |
| 开始原操作 | `receive`；READY/Permit可开始 | GR StartIntent与PO.start同一提交，IN_FLIGHT | 当前Hold先赢则拒绝，未开始不能假排空 | [原文 L104](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:104>) |
| 确认本次实际收货 | `receive`；当前Start/完整意图 | 同库正常路径WMS+PO消费+GR商业收货一次 | commit未知查询Canonical；不再次建WMS单 | [原文 L105](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:105>) |
| 读取/应用原质量结果 | `inspection.read`/`acceptance.apply` | 读Quality原决定；应用GR投影，不批准Quality | 缺Task/前驱只等待；不能把空数组当全合格 | [原文 L106](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:106>) |
| 继续恢复 | `recover`；固定Work/版本 | 原步骤查询或有证同键续作 | 终态只返回；新业务变化需新有权意图 | [原文 L107](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:107>) |
| 撤回未发生收货 | `withdraw` | 先永久封原执行，再据Owner无效果释放保护 | 有在途/实物未知不直接WITHDRAWN，保留保护 | [原文 L108](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:108>) |
| 查看更正/匹配/退货 | 各自读权 | 显示稳定原引用，导航有权Owner | 没有权限不给隐藏数量；不启动第二Target | [原文 L109](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:109>) |

options先给真实Site/能力，列表page1/size20/UpdatedDesc+Id；切Site重取PO/库位/到货权限。PO选后Supplier/币/物料/技术只读；量改让preview过时。TRACKED批次不可空、NOT_TRACKED要策略、UNKNOWN只草稿/异常，序列一件依Owner单位；目的库位不默认RECV。普通写断连/Abort不取消后台。 [原文 L90](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:90>)、[原文 L92](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:92>)、[原文 L94](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:94>)

| 阶段 | WMS本次Physical | 普通可用（无预留/其它限制） | GR CommercialReceived | GR Accepted / Rejected / Held / PendingInspection | 出处 |
| --- | --- | --- | --- | --- | --- |
| 保存草稿 | 尚未有物理结果 | 未知/不适用 | 0尚未POSTED | 不宣称已验收 | [原文 L508](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:508>) |
| 正常收货事务提交 | 100 | 0 | 100 | 0 / 0 / 0 / 100 | [原文 L509](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:509>) |
| Quality90/10已收件，两个应用未完成 | 100 | 仍为WMS原观察0或UNKNOWN | 100 | 维持原应用0/0/0/100；原Quality结果90/10单列 | [原文 L510](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:510>) |
| GR先应用Quality，Stock仍待应用 | 100 | 0或UNKNOWN，不能猜90 | 100 | 90 / 10 / 0 / 0 | [原文 L511](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:511>) |
| Stock已应用同一决定 | 100 | 90 | 100 | 90 / 10 / 0 / 0 | [原文 L512](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:512>) |
| 重复运输/人工再次apply | 100 | 仍90 | 100 | 仍90 / 10 / 0 / 0 | [原文 L513](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:513>) |

SC10页面必须允许GR验收先成但Stock仍0/UNKNOWN，也允许Stock先放行90但GR待应用；最后无额外限制才普通可用90，有预留或技术Hold则读WMS真实联合值，不能把GR Accepted改60或再次扣拒绝。Rejected10不是退货、库存-10或信用票据。更正后scope冲突页保旧投影但显式新版requiredScope，MATCH按钮不得用旧90。 [原文 L515](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:515>)、[原文 L500](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:500>)、[原文 L531](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:531>)

## 9. 实现时的具体整合落点与旧来源

实现顺序建议（本整理分析，并非原件中的授权任务派发）：先把Arrival范围防重、FrozenIntent及三域映射做成同一权威模型；再落Work/OwnerCall与真实组合ResourcePlan；接PO start/最终消费和Stock Canonical；之后接Quality同决定双消费者、scope后继CAS、PO完整Manifest；最后接MATCH/RETURN条件读取与出站恢复。每一步的对象、错误及验收已在本册给出，不能为演示默认PASS、0价或假Ref。 [原文 L280](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:280>)、[原文 L292](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:292>)、[原文 L629](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:629>)、[原文 L352](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:352>)、[原文 L358](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:358>)、[原文 L456](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:456>)、[原文 L498](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:498>)、[原文 L562](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:562>)

当前Spec基于固定CP6源码157630594e3371fe181955d2f6227ff3b6962c84，旧GoodsReceiptService按PostingBasis1直验收、缺QC默认PASS、逐行超收而不先合量、Accepted+=，旧WmsAdapter每次Create→Confirm→Receive、默认RECV/Lot空/Now等是原作者静态定位，不是本轮运行复现或最新部署结论。需映射旧21,8、单号/行号与明细Ref并隔离旧writer，再单一切换；不能建第二写账或靠旧代码默认覆盖已接受政策。 [原文 L6592](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6592>)、[原文 L6596](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6596>)、[原文 L6597](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6597>)、[原文 L6598](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6598>)、[原文 L6599](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6599>)、[原文 L6600](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6600>)、[原文 L6601](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6601>)、[原文 L6602](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6602>)、[原文 L6604](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6604>)

GR主文当时明确未派Implementation Breakdown，不把其历史“未开始下一Target”当今天整理其他模块的阻塞。本文仅整理设计，不启动Host／DDL／编译／SQL／Worker。性能目标尚待具体数据量和环境验证。 [原文 L743](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:743>)、[原文 L741](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:741>)

## 10. 111项直接验收及关键退出断言

全部 **NOT_RUN**。SC10/SC11是独立夹具；下列包含本轮已语义阅读的所有111条前置、动作与不可出现的持久变化。原连续82 JSON及完整绑定还在附件阅读队列，不声称执行。

| ID | 前置与操作 | 明确结果和不应发生的持久变化 | 出处 |
| --- | --- | --- | --- |
| GR-AC001 | 有权PO100，保存完整DRAFT | 201返回稳定Operation/LineId；Stock无Movement、PO无Permit、Arrival片尚不消费。 **NOT_RUN** | [原文 L6418](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6418>) |
| GR-AC002 | 草稿缺到货凭证、Lot或Quality策略 | 可以保存仍合法的未齐字段；preview给缺项，freeze/新收阻断；没有默认RECV/NONE。 **NOT_RUN** | [原文 L6419](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6419>) |
| GR-AC003 | 两个HTTP键引用同一可信到货事件及同区间 | ArrivalRecord归一；冻结至多一个占同ArrivalSlice，另一指原操作或409，不新增实际事件。 **NOT_RUN** | [原文 L6420](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6420>) |
| GR-AC004 | 到货份额[0,60)与[50,100)竞争 | 重叠范围锁拒绝，不能仅unique完全相同区间检测。 **NOT_RUN** | [原文 L6421](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6421>) |
| GR-AC005 | 同送货单号但真实两次不同到货事件 | 按受信stableEventId区分；不能因为单号相同漏掉第二车，也不因为扫描件不同多算。 **NOT_RUN** | [原文 L6422](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6422>) |
| GR-AC006 | 修改同Draft旧version或跨行借用lineId | 409/404；原草稿不被覆盖，子行不跨Tenant归属。 **NOT_RUN** | [原文 L6423](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6423>) |
| GR-AC007 | 冻结后改数量/目的地/原Source并同键执行 | GR_INTENT_CHANGED/CONTENT_CONFLICT；原FrozenIntent、Stock业务摘要不改变。 **NOT_RUN** | [原文 L6424](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6424>) |
| GR-AC008 | 同Command同完整body两实例保存 | 仅一原登记/编号，另一回原结果；事务提交丢响应查原Command。 **NOT_RUN** | [原文 L6425](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6425>) |
| GR-AC009 | 同Command换path/body | 409命令内容冲突；不把旧键绑定到另一收货。 **NOT_RUN** | [原文 L6426](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6426>) |
| GR-AC010 | 日期未提供而要求实际到货时间 | 不补服务器Now冒真实发生时点；草稿可未定，正式真实执行须原凭证/现场证据。 **NOT_RUN** | [原文 L6427](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6427>) |
| GR-AC011 | 用户无商业读权访问原PO金额/估价 | 403或按明确遮蔽模型拒此详情；不能通过导出/错误提示泄金额。 **NOT_RUN** | [原文 L6428](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6428>) |
| GR-AC012 | 条目实际数量超过200或请求超2MiB | 拒整请求，不拆原Atomic组；已冻结组不自动变多个GR。 **NOT_RUN** | [原文 L6429](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6429>) |
| GR-AC013 | 序列物料一串号收数量2或重复串号 | WMS精确序列策略拒合法新收；原异常到货可记录，不能复制Root串号。 **NOT_RUN** | [原文 L6430](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6430>) |
| GR-AC014 | 有权原SourceRef与伪造Supplier/Site不一致 | 拒绝匹配；不按名称接近匹配、不把本厂角色扩至他厂。 **NOT_RUN** | [原文 L6431](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6431>) |
| GR-AC015 | 弃置纯Draft再查看原PO/RFQ | 只废本候选，旧PO/接受的RFQ状态不受影响。 **NOT_RUN** | [原文 L6432](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6432>) |
| GR-AC016 | 真实物理事实晚于用户失权到达 | 合法机器入站保事实；原用户读取/下载仍拒，机器权不转授用户。 **NOT_RUN** | [原文 L6433](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6433>) |
| GR-AC017 | 同PO行两个GR行各60，总可收100 | prepare按Schedule/portion聚合120，PO拒；不能逐行各查100而过量。 **NOT_RUN** | [原文 L6434](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6434>) |
| GR-AC018 | PoLineId正确但Schedule属于别的PO | GR_PO_MATCH_INVALID，零新许可/Stock效果。 **NOT_RUN** | [原文 L6435](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6435>) |
| GR-AC019 | Item代码相同但技术/货权/来源用途不同 | 拒不合法合并；技术证据不自动取当前最新版本。 **NOT_RUN** | [原文 L6436](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6436>) |
| GR-AC020 | 输入10BOX、1BOX=10EA、源100EA | 冻结精确换算、Stock100.00000000、PO100.000000；原单位保留。 **NOT_RUN** | [原文 L6437](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6437>) |
| GR-AC021 | 单位换算结果无法无损6位或8位表示 | 正常新效果阻断，不能舍入多收或省略余量；异步真事实保原精度。 **NOT_RUN** | [原文 L6438](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6438>) |
| GR-AC022 | 一个Schedule由两个SourcePortion供给 | FrozenLineMap完整非交叠，量和正确；不能都分到同ReceiptSlice重复来源量。 **NOT_RUN** | [原文 L6439](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6439>) |
| GR-AC023 | 只提供PO号/LineNo且稳定ID尚未读到 | 必须先从有权PO查询解析；不预知/猜UUID，不将展示行号作永久键。 **NOT_RUN** | [原文 L6440](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6440>) |
| GR-AC024 | 同一到货范围换Preparation/Operation包装 | 原Arrival片及Stock leaf仍同身份，拒重复使用，不消两份PO预算。 **NOT_RUN** | [原文 L6441](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6441>) |
| GR-AC025 | PO当前Supplier回复后继拒绝导致确认争议 | GR用PO当前动作门；不自己挑旧ACCEPTED确认/继续新收。 **NOT_RUN** | [原文 L6442](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6442>) |
| GR-AC026 | HELD_NOT_STARTED后ECO技术Hold先到 | start拒绝/BLOCKED；I保持至真实无效果，不因未显示库存就返量。 **NOT_RUN** | [原文 L6443](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6443>) |
| GR-AC027 | Permit已IN_FLIGHT而技术Hold在最终commit前到 | PO最终新效果拒；查询原Stock，只有已发生事实可回收；不能称start永久豁免。 **NOT_RUN** | [原文 L6444](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6444>) |
| GR-AC028 | Cancel/Close屏障在start之前成立 | HELD不能新start，即使加入任意本地列表也不算排空。 **NOT_RUN** | [原文 L6445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6445>) |
| GR-AC029 | 已IN_FLIGHT列在PO准确drain名单，后软Cancel请求 | 无技术硬停时原操作可最终排空；新批/扩量不允许。 **NOT_RUN** | [原文 L6446](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6446>) |
| GR-AC030 | 最终Cancel/Receiving Close已经成立 | 新commit拒；真实历史漏报仍保留并差异通知，不伪造合法新许可。 **NOT_RUN** | [原文 L6447](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6447>) |
| GR-AC031 | Permit或Work lease到期且可能已经到WMS | 只接管原查询，UNKNOWN/I不自动解除。 **NOT_RUN** | [原文 L6448](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6448>) |
| GR-AC032 | Source/PO/GR实际非同库而没有采用等价协议 | GR_OWNER_PROTOCOL_NOT_ADOPTED；只读/草稿不被全部停掉，不能以mock TRUE放行。 **NOT_RUN** | [原文 L6449](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6449>) |
| GR-AC033 | 正常SC10一次完整物理接收100 | Stock根100、Movement1、PO Permit USED、Commercial100同事务；Accepted0待检100。 **NOT_RUN** | [原文 L6450](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6450>) |
| GR-AC034 | WMS事务执行一行后GR写入失败但尚未commit | 所有Owner正常同库写回滚；不能报告库存已收/创建半个正常原子组。 **NOT_RUN** | [原文 L6451](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6451>) |
| GR-AC035 | 最终commit成功但应用HTTP响应丢失 | 原Stock lookup FOUND相同Receipt；恢复后Root100/商业首版1/Permit消费1。 **NOT_RUN** | [原文 L6452](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6452>) |
| GR-AC036 | 同Stock source+leaf改数量再次Posting | Stock/GR身份冲突，原100不变；不新生第二Root。 **NOT_RUN** | [原文 L6453](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6453>) |
| GR-AC037 | 同SourceAllowance允许100但伪两不同leaf共120 | Source/PO总量与Allowance最终裁决拒；不能仅叶唯一就忽略总预算。 **NOT_RUN** | [原文 L6454](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6454>) |
| GR-AC038 | 返回completeForIntent但缺一part或数量不等 | GR_PARTIAL_PHYSICAL_FACTS/冲突；保真实行，不标整批成功，不盲补余项。 **NOT_RUN** | [原文 L6455](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6455>) |
| GR-AC039 | 计划缺Capacity映射或UNKNOWN | 阻正常新入库；不能用同地点总容量或空metrics代精确Source关系。 **NOT_RUN** | [原文 L6456](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6456>) |
| GR-AC040 | 已有未收容量40，本次实现10 | Stock同容量域物理+10/相应未收-10，不先双计或跨义务随便扣；GR不另写容量。 **NOT_RUN** | [原文 L6457](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6457>) |
| GR-AC041 | 真实Lot不详但用户选择NOT_TRACKED | 需要物料策略证明；不能用缺批次生成无追踪普通库存。 **NOT_RUN** | [原文 L6458](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6458>) |
| GR-AC042 | 原价CUA/费用CUB，估价未齐 | 原币条件保留；requiredBeforePhysical明确为true则阻正常执行，不填零或换Supplier默认币。 **NOT_RUN** | [原文 L6459](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6459>) |
| GR-AC043 | 不同质量决定导致WMS可用更新 | 物理Delta0、原Receipt不重建，质量变化不成为另一收货。 **NOT_RUN** | [原文 L6460](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6460>) |
| GR-AC044 | WMS Stock SO01无operationId查原locator/digest | FOUND有原结果或NOT_OBSERVED不可推无效果；不需先知道服务器未来ID。 **NOT_RUN** | [原文 L6461](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6461>) |
| GR-AC045 | 尝试以Stock源流新sourceVersion重做同arrival叶 | 相同leaf唯一域仍绑定；新版本不是第二次物理到货。 **NOT_RUN** | [原文 L6462](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6462>) |
| GR-AC046 | GR草稿/preview已保存，读取Status | 无权声明Physical0已完成；无结果保持NOT_OBSERVED/未知。 **NOT_RUN** | [原文 L6463](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6463>) |
| GR-AC047 | 需库存反向修复，但操作者只有GR恢复权 | 拒数量改写；引用WMS原Correction/Resolution已授权流程，不用普通Posting负数。 **NOT_RUN** | [原文 L6464](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6464>) |
| GR-AC048 | Stock已SEALED，收到原旧worker或另一HTTP别名 | 原终态拒新数量；已有ActualFact记录不删除，后续认账不能复活正常key。 **NOT_RUN** | [原文 L6465](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6465>) |
| GR-AC049 | REQUIRED策略，Quality task服务器不可用 | Stock仍PENDING/普通可用0，Task请求同原键待恢复，不默认免检。 **NOT_RUN** | [原文 L6466](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6466>) |
| GR-AC050 | Quality NONE有真实不适用政策与无政策两组 | 前者EXPLICIT_SKIP有来源，后者UNKNOWN阻新收；PostingBasis不参与此判断。 **NOT_RUN** | [原文 L6467](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6467>) |
| GR-AC051 | EXEMPT在preview后被撤销/生效区间改变 | 最后门核当前策略，阻新免检收货；已经实际收到的旧事实仍留证及新风险。 **NOT_RUN** | [原文 L6468](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6468>) |
| GR-AC052 | 检验只抽样10，没有覆盖100整批证明 | 不能推断全100Accepted；完整商业接受须有实际质量策略覆盖证据。 **NOT_RUN** | [原文 L6469](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6469>) |
| GR-AC053 | SC10 Quality90/10完整决定 | GR四分区90/10/0/0，和100；WMS应用后无其它限制时普通可用90，Physical100。 **NOT_RUN** | [原文 L6470](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6470>) |
| GR-AC054 | 同QualityMessage重传或换消息ID同业务序列 | 原Canonical/application一次，Accepted不成180、不多Movement。 **NOT_RUN** | [原文 L6471](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6471>) |
| GR-AC055 | Quality同序不同分量内容 | GR_QUALITY_CONTENT_CONFLICT，保冲突原件，不按最后接收者获胜。 **NOT_RUN** | [原文 L6472](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6472>) |
| GR-AC056 | Quality区间[0,90)和[80,100)重叠 | 拒全部应用，不能按计数凑100或挑便宜分量。 **NOT_RUN** | [原文 L6473](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6473>) |
| GR-AC057 | Quality v3先于v2或Root尚不存在 | GAP/PENDING_SOURCE持久；前驱/原物理源提交后同槽唤醒，无假Root/应用ID。 **NOT_RUN** | [原文 L6474](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6474>) |
| GR-AC058 | Quality明确90/10，GR应用先成功Stock未应用 | GR90/10、Stock普通可用仍0/UNKNOWN；不会由GR直接放库存。 **NOT_RUN** | [原文 L6475](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6475>) |
| GR-AC059 | Stock质量先应用，GR处理器崩溃 | Stock结果保原，GR按同决定补投影到90/10；Physical仍100。 **NOT_RUN** | [原文 L6476](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6476>) |
| GR-AC060 | 90/10更正85/15，重复新决定 | GR当前85/15，旧90/10可查；POACCEPTANCE完整v2替换，不变175/25。 **NOT_RUN** | [原文 L6477](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6477>) |
| GR-AC061 | 质量决定对已发20的[0,25)HOLD | Stock当前影响5、noncurrent20；GR原收货仍100，不造回库20。 **NOT_RUN** | [原文 L6478](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6478>) |
| GR-AC062 | Quality对另一批次/技术/InspectionSource宣称有效 | 范围/Family不符拒应用；不能按同Item借判定。 **NOT_RUN** | [原文 L6479](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6479>) |
| GR-AC063 | 质量0.00000001基本量无法映射PO六位 | GR保存原8位准确分区，PO投影BLOCKED精度；不把这段舍掉或当0PASS。 **NOT_RUN** | [原文 L6480](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6480>) |
| GR-AC064 | 现场除拒绝10还有独立预留/技术Hold | GR Accepted90不等StockOrdinaryAvailable90；界面采用WMS联合可用观察，不机械减两次。 **NOT_RUN** | [原文 L6481](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6481>) |
| GR-AC065 | SC11 Stock100成功而PUR商业写失败 | 显示Physical100/Commercial待补；恢复C02查到原回执，C05只RECORD_COMMERCIAL，Stock仍100。 **NOT_RUN** | [原文 L6482](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6482>) |
| GR-AC066 | 两个恢复Worker同时接管C01 | 最后epoch/唯一CommercialVersion至多一方新提交，另读原结果。 **NOT_RUN** | [原文 L6483](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6483>) |
| GR-AC067 | Work在PERMIT或WMS准备请求发出后重启 | 同OwnerCall key查原结果，不能重新分配Permit/InboundIntent。 **NOT_RUN** | [原文 L6484](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6484>) |
| GR-AC068 | 原查询暂不可用/NOT_OBSERVED | 保UNKNOWN，不能先拒绝后另Key重收；不得释放Arrival/PO保护。 **NOT_RUN** | [原文 L6485](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6485>) |
| GR-AC069 | PO事实出站丢失/重复/乱序 | PO按固定FactIdentity/Version和完整Manifest处理，不再入库；缺链可回取原fact。 **NOT_RUN** | [原文 L6486](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6486>) |
| GR-AC070 | 其它GR已经收过同PO，当前GR只回自己的100 | 只生产本Operation事实，不覆整PO总量；PO最终合计仍由PO负责。 **NOT_RUN** | [原文 L6487](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6487>) |
| GR-AC071 | 到货总100，WMS合法历史事实只确认40另60未知 | PARTIAL_FACTS与Permit分片保护，不能标全成功或再向WMS发60盲补。 **NOT_RUN** | [原文 L6488](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6488>) |
| GR-AC072 | Withdraw时已曾start且Stock结果未知 | 202/UNKNOWN，原Work封新派发但不签no-effect；expiry不能替全路径证明。 **NOT_RUN** | [原文 L6489](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6489>) |
| GR-AC073 | Stock先成功，后收到seal请求 | 返回ALREADY_EFFECTIVE及原Receipt，不发布零效果/库存负移动。 **NOT_RUN** | [原文 L6490](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6490>) |
| GR-AC074 | 原100/已收40，另10先IN_FLIGHT，再取消60 | GR截止PENDING含原Permit10；最终50真实后PO拒原60，不让GR自动改50。 **NOT_RUN** | [原文 L6491](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6491>) |
| GR-AC075 | 取消60截止先完成，后来新10试start | 按原PO控制拒新执行；原40不变，不能重建第二GR绕屏障。 **NOT_RUN** | [原文 L6492](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6492>) |
| GR-AC076 | 取消后真实漏报10到达 | GR保真实10链接，PO显示received50/effective40/over10；原CancellationApplicationRef保留，Source风险单列。 **NOT_RUN** | [原文 L6493](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6493>) |
| GR-AC077 | 签截止时只查询一个路径，其它委托在途未知 | 不能COMPLETE_NO_PENDING_EFFECT；MissingPaths显式，原保护保留。 **NOT_RUN** | [原文 L6494](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6494>) |
| GR-AC078 | Quantity消息缺完整ReceiptAllocationManifest | PO可待关联，不能从数量相等猜SourcePortion或据此取消放行。 **NOT_RUN** | [原文 L6495](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6495>) |
| GR-AC079 | Commercial已收，Quality验收更正依赖Finance未定 | 本来质量真事实保留，MATCH基准标新版本/异常；不回写AP或删旧验收凭证。 **NOT_RUN** | [原文 L6496](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6496>) |
| GR-AC080 | 纯REFERENCE_REPAIR指原StockReceipt却换另一个PO | 不是关系修复，拒绝；不得把一笔物理量复制到另一订单。 **NOT_RUN** | [原文 L6497](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6497>) |
| GR-AC081 | 实收记录更正需要WMS纠错但无原Movement凭证 | Correction WAITING_OWNER；无库存/GR毛量私自扣减。 **NOT_RUN** | [原文 L6498](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6498>) |
| GR-AC082 | 原[0,100)接受90/拒10，有权VOID[20,40)后有效80 | Commercial/ScopeSet后继保原坐标[0,20)∪[40,100)；旧应用90/10可查但ACCEPTANCE_SCOPE_CONFLICT、MATCH BLOCKED。准确Quality后继70/10同版应用后才解除；不截Accepted到80、不压成[0,80)。 **NOT_RUN** | [原文 L6499](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6499>) |
| GR-AC083 | 未来MATCH读取HandoffBasis | 稳定ReceiptLine/Slice/Accepted范围/原币和版本齐；明确不是可开票量锁，无AP/MATCH实体写入。 **NOT_RUN** | [原文 L6500](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6500>) |
| GR-AC084 | 已收100、拒10、真实退10 | GR实收100/拒10/退10独立，Stock实际由WMS变90；不第二次OUT或重开PO收10许可。 **NOT_RUN** | [原文 L6501](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6501>) |
| GR-AC085 | 只有Finance信用10而无实物退回 | 不记录RETURN10，不减少Physical或Received；Finance独立。 **NOT_RUN** | [原文 L6502](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6502>) |
| GR-AC086 | PO未到量已减少、Stock待检量存在，Planning消息乱序 | GR关联同Lineage/Receipt，WAITING_COMPONENTS不新增供给；不会PO100+GR100+Stock100。 **NOT_RUN** | [原文 L6503](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6503>) |
| GR-AC087 | 无已知Source Lineage或PO回执尚未应用 | SupplyTransition等待组成，不能伪造Lineage或把其数量视作可用Supply。 **NOT_RUN** | [原文 L6504](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6504>) |
| GR-AC088 | Quality合格90但是Finance尚未确认/估价未知 | GR验收状态可成立并列财务待定，不造资产/AP/发票成功。 **NOT_RUN** | [原文 L6505](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6505>) |
| GR-AC089 | 分页History/列表在新事件加入时读取 | 一次读取内部一致、权限范围正确；晚发生时点不覆盖新Sequence，跨Owner不比较序号大小。 **NOT_RUN** | [原文 L6506](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6506>) |
| GR-AC090 | 导出当前筛选100条但页面20条 | 导出完整有权100条；超10000拒，不提供截断成功。 **NOT_RUN** | [原文 L6507](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6507>) |
| GR-AC091 | 下载申请有权、实际下载时被撤权 | 403/票据撤销；既合法机器事实照收，不把ticket当长期读取权。 **NOT_RUN** | [原文 L6508](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6508>) |
| GR-AC092 | 外部文本含公式开头/恶意URL/未知DTO字段 | 文本转义展示，固定路由/字段白名单；拒schema旁路、无SQL/内部地址泄露。 **NOT_RUN** | [原文 L6509](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6509>) |
| GR-AC093 | 旧GR接入没有ReceiptOperationId、Qty21,8或默认PASS | LEGACY_READ_ONLY/有据映射；不能补造新Permit、免检或Stock回执，旧写者隔离后才采用新链。 **NOT_RUN** | [原文 L6510](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6510>) |
| GR-AC094 | 同Tenant机器有Quality权限却冒发Physical或AP | 按type/Producer/Contract/Scope拒，不能跨Owner主账；拒绝不写业务APPLIED。 **NOT_RUN** | [原文 L6511](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6511>) |
| GR-AC095 | 真实模式载荷引用DEMO合同/例子hash/模拟角色 | 生产Gate拒；本册文档例不成为Owner签署或已实施证据。 **NOT_RUN** | [原文 L6512](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6512>) |
| GR-AC096 | 完成GR主文后查看项目边界 | 仅GR待审；PR仍未完成，MATCH/RETURN未启动，已接受PO/RFQ/Stock原件hash不变。 **NOT_RUN** | [原文 L6513](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6513>) |

| ID | 前置与操作 | 明确结果和不应发生的持久变化 | 出处 |
| --- | --- | --- | --- |
| GR-AC097 | 原100，Owner原Movement明确VOID中段[20,40) | scope2有效[0,20)∪[40,100)、量80；排除20不属于四质量分量，不产生GR库存动作。 **NOT_RUN** | [原文 L6515](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6515>) |
| GR-AC098 | scope2完整Quality为Accepted[0,20)+[40,90)、Rejected[90,100) | 尾端100合法，Accepted70+Rejected10=80；RECEIPT80、ACCEPTANCE70各自完整Manifest，Rejected10不进ACCEPTANCE。 **NOT_RUN** | [原文 L6516](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6516>) |
| GR-AC099 | 旧scope1 Quality晚到或新消息将旧decision绑scope2 | 原Canonical/旧头可查；不得移动新scope当前头。换绑定同Canonical进入GR_SCOPE_EVIDENCE_CONFLICT，不借总量相同放行。 **NOT_RUN** | [原文 L6517](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6517>) |
| GR-AC100 | 同WMS纠正identity同digest用另Command重复提交 | 唯一OwnerResolution/CorrectionApplication返回原Commercial2/ScopeSet2，不产生v3、第二RECEIPT或第二Stock动作；异义留Conflict。 **NOT_RUN** | [原文 L6518](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6518>) |
| GR-AC101 | Correction事务commit响应丢失后重启 | GET原CorrectionId得同resultFactRef/resultScopeSetRef；提交前失败无半ScopeSet/事实/Outbox，按同槽恢复，不重建[0,80)。 **NOT_RUN** | [原文 L6519](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6519>) |
| GR-AC102 | 准备按scope2应用Quality时另一有权Correction推进scope3，或worker epoch失效 | 最终Current Commercial/ScopeSet/cursor token/Quality绑定/epoch任一CAS不符则整事务无新应用，无孤儿PO出站；不得自动贴scope3重试。 **NOT_RUN** | [原文 L6520](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6520>) |
| GR-AC103 | Handoff读或MATCH最终验证遇requiredSet2/projectedSet1 | 409 GR_ACCEPTANCE_SCOPE_CONFLICT，无当前可匹配量；后继完成后basisRevision、Commercial2、scope2、Accepted70及准确slices一致，旧basis保持历史。 **NOT_RUN** | [原文 L6521](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6521>) |
| GR-AC104 | 一Operation两行100EA及20KG，仅EA中段VOID | ScopeSet2完整列两行；EA80独立，KG20及原scope复用，禁止显示/验证总100或totalEffectiveQuantity混量。 **NOT_RUN** | [原文 L6522](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6522>) |
| GR-AC105 | 纠正只给replacementQty80，无原区间证明；或错误Root/part/UOM映射 | WAITING_OWNER/GR_SCOPE_EVIDENCE_CONFLICT，Current不变；不默认删尾20。 **NOT_RUN** | [原文 L6523](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6523>) |
| GR-AC106 | newscope2收到跨洞[10,50)或FULL_SNAPSHOT漏[0,20) | GR_SCOPE_COVERAGE_INVALID，原决定保留但无新应用；仅端点合法/总量碰巧相等不足以通过。 **NOT_RUN** | [原文 L6524](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6524>) |
| GR-AC107 | newscope2第一份消息为对旧scope1的DELTA | 不能裁剪旧分区后自动初始化；等有权FULL_SNAPSHOT和明确前版/水位证明，未涉及的VOID不伪装Pending。 **NOT_RUN** | [原文 L6525](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6525>) |
| GR-AC108 | 新scope Quality先于Correction到达 | PENDING_SCOPE同Canonical槽，Correction提交DependencyWakeOutbox/扫描唤醒；一份合法后继仅一个Application及ACCEPTANCE FactVersion。 **NOT_RUN** | [原文 L6526](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6526>) |
| GR-AC109 | 有权纠正完整VOID原[0,100) | scope有效[]、excluded[0,100)、量0；EMPTY_SCOPE有据零应用/完整零Manifest，不伪造Quality PASS；缺Owner证明不可零化。 **NOT_RUN** | [原文 L6527](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6527>) |
| GR-AC110 | 同scope2完整后继手工/自动并发、提交后丢响应 | 唯一(Operation,ScopeSet,QualityHeadSetHash)复用同Application/PO FactVersion，人工旧token不生成第二效果，原成功结果可查。 **NOT_RUN** | [原文 L6528](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6528>) |
| GR-AC111 | 原Stock已发/已退/被消费20但无收货VOID证明 | Commercial原scope保持100；NONCURRENT/Return独立，不把当前库存80当有效收货80。 **NOT_RUN** | [原文 L6529](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6529>) |

非前缀VOID链静态结果为RECEIPT80、ACCEPTANCE70，各原FactIdentity版本2替版1、不相加180/160；对应Manifest和handoff全部同ScopeSet2/line scope2。新scopeQuality先到需PENDING_SCOPE；Correction提交未知查原槽；旧scope晚消息不能清当前冲突；另推进scope3时CAS全部不写。 [原文 L6402](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6402>)、[原文 L6404](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6404>)、[原文 L6405](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6405>)、[原文 L6406](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6406>)、[原文 L6407](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6407>)、[原文 L6408](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6408>)、[原文 L6409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6409>)

## 10.1 82个完整对象的连续实现关系

| 链路 | 页面、Owner命令与持久结果必须保持什么 | 原对象入口 |
|---|---|---|
| E01–E09：登记与冻结 | 先读有权PO/交期/SourcePortion和真实库位批次，已有Pool元数据revision0不是未来RootId。到货canonical事件与[0,100)份额防重，登记DRAFT无数量效果。preview冻结实际InspectionPolicy/Valuation/单位映射；FROZEN只固定业务意图，不占库存。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:758>) |
| E10–E17：共同事务收货 | prepare202先有Work而permit=null；READY才有HELD_NOT_STARTED及PostingPlan，start把ReceiptStartIntent与PO IN_FLIGHT同事务持久。commit按原plan/locator/digest执行；WMS结果只有外层commit后才可见，唯一Movement/Root100、GR商业100、待检100、普通可用0。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:1533>) |
| E18–E26：两个质量消费者 | Quality Task CREATED不等PASS。Quality同family/stream决定完整90/10区间；Ingress202仅接收。GR Acceptance APPLIED90/10时Stock仍PENDING，后者独立应用同decision、quantityMovementCreated=false；收到Stock原结果并真实查询可用才显示90。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:2154>) |
| E27–E30：PO事实与应用观察 | RECEIPT100、ACCEPTANCE90使用不同Kind但同原slice谱系，不合并成190；拒绝10留GR/Quality，不发明PO Rejected类型。PO按完整ReceiptAllocationManifest解析源份额；ConsumerReceipt绑定原event，不倒推Finance完成。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:2876>) |
| C01–C09：SC11恢复 | 仅在已采用等价非原子协议的独立故障分支，Stock100先commit而GR未知。按原operation/locator/digest/command查询FOUND后只补RECORD_COMMERCIAL，Movement仍1、首商业版最多1。NOT_OBSERVED不证明无效果；新到货键冲突并引导原Work。不能Stock inverse后重收。MAIN_ATOMIC提交前失败则三Owner均回滚，不能混用。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:3087>) |
| A01–A06：Source/Stock/PO准确wire | GR登记完整执行manifest parent+BATCH和每part自然来源/occurrence/allowance；PostingPlan数量与源份额一致，真正Root由Stock返回。SP02精确采用原nextPosting。PO start/commit使用当前Permit/guard/startRef和准确ReceiptIdentity/Slice，同DbTransaction的Owner方法不是新增HTTP收货API。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:3844>) |
| A07–A22：未知、豁免、后继与交接 | UNKNOWN质量策略无Ref会阻相关新效果，EXEMPT需准确政策/豁免/适用量/时效。85/15后继替换90/10而非累加。ANNOTATION不改数量，退货Notice只记已有RETURN链接。HandoffBasis不授予匹配/退货权限，SupplyTransition createsAdditionalSupply=false。未决10的cutoff=PENDING；只有Stock永久closure和全路径证明才NO_EFFECT。冻结hash含实际policy/valuation/源映射，不只提示Ref。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:4174>) |
| R00–R09：非前缀VOID | 原坐标Domain[0,100)固定；Owner有权VOID[20,40)后有效集变[0,20)∪[40,100)，不是压缩[0,80)。R05输入总量80只供交叉核，真正范围由OwnerResolution/原Movement映射决定。GR商业版2及ScopeSet2同事务，旧90/10仍保留但requiredSet2/projectedSet1显示CONFLICT、PO投影BLOCKED。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:5055>) |
| R10–R18：后继质量和完整事实 | Quality以scope2 FULL_SCOPE proof、明确replacementOfDecisionRef和连续stream签70/10：接受[0,20)+[40,90)，拒绝[90,100)。GR只在该当前头下应用Acceptance版2；PO RECEIPT后继两片20+60=80，ACCEPTANCE两片20+50=70，分别指原PO应用回执。MATCH/RETURN基准带ScopeSet2/EffectiveScope2及原坐标slice，仍不是操作许可。 | [原文](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:5547>) |

68个声明绑定均已按原JSON值和类型比较相等。t0到货→t1冻结准备→t2开始→t3收货→t4Quality→t5观察，外层asOf不得早于其内层已观察事实；A取消反例和SC11是独立分支。R后继scope对象的摘要取除ref外规范JSON，新增GR信封不并入外域Stock的Quality摘要算法。[绑定/时序](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:4978>)、[R分支语义](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:5057>)

## 11. 真实Owner采用缺口与后继组合核对

| ID | 需要的真实证据 | 本稿已定合同 / 缺证影响 | 出处 |
| --- | --- | --- | --- |
| O-GR-01 IAM与数据范围 | User/Buyer/Site/商业字段/下载、各Producer与resume能力实例 | §2/3/12；失权拒相应读写，不能默认超级管理员 | [原文 L6557](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6557>) |
| O-GR-02 到货来源证据登记 | Supplier真实event/line/范围、可信代录/证明和跨扫描别名 | ArrivalInput/View、稳定区间；缺失只草稿/异常登记，禁止正常新收 | [原文 L6558](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6558>) |
| O-GR-03 PO可收/容差/当前控制 | 准确PO修订、SourcePortion、当前Permit Guard、受权超收/早收/取消策略 | 复用PO；无容差证据不新放超收，真异常仍留存 | [原文 L6559](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6559>) |
| O-GR-04 联合事务与锁采用 | 同DB/connection/principal、所有Owner参与映射、组合锁资源、旧写者隔离 | §6/11固定方案；不具备则相关真实新效果禁止 | [原文 L6560](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6560>) |
| O-GR-05 WMS-IN最小适配 | 原SourceManifest/逐叶/Stock preparation/原回执查询、Root/容量/地点/批序映射 | WmsReceiptIntent/Result/Query；缺适配不能“试写看看” | [原文 L6561](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6561>) |
| O-GR-06 Quality策略和专业规则 | IQC适用、NONE/EXEMPT、抽样覆盖、让步、DecisionFamily/前驱/范围/阶段 | §8/9完整输入输出；不能由GR配置一组假阈值上线 | [原文 L6562](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6562>) |
| O-GR-07 估价与原商业 | PO原商业hash，Finance原币/税/费用/FX、是否阻物理的真实政策 | ValuationSnapshot；未知金额不0，不决定Quality | [原文 L6563](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6563>) |
| O-GR-08 库容与Source allowance | Source叶注册、合法Allowance/容量义务关系或完整KNOWN_NONE、共同最后守卫 | Stock已接受算法；GR不提供自选义务绕过 | [原文 L6564](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6564>) |
| O-GR-09 等价非原子采用 | 独占委托、PO最终Control/fence/截止协议、StockCanonical/所有入口证明 | EquivalenceAdoption；B路径默认未采用，仅既成事实恢复不被阻 | [原文 L6565](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6565>) |
| O-GR-10 事实/映射/精度与消费 | PO事实完整行Manifest、各单位转换、返回应用凭证、Quality/Stock当前头 | §9/10；缺链/精度差异待核，原真实数据不丢 | [原文 L6566](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6566>) |
| O-GR-11 Planning/MATCH/RETURN | 消费者绑定原事件/Lineage/版本、原slice和最终验证采用 | 只稳定输入与应用观察，不说这些Target已完成 | [原文 L6567](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6567>) |
| O-GR-12 旧存量切换和运行证据 | 旧GR/WMS映射、默认PASS隔离、真实迁移/回退授权、并发/API/事务/故障取证 | 旧主账只读或唯一切换，本轮不运行、不迁移 | [原文 L6568](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:6568>) |

D-GR01–10已经准确接受，未证的是实际IAM／到货登记／PO容差与控制／共同事务和旧writer隔离／WMS适配／Quality策略和Q65范围证明／估价／Source容量映射／等价跨库／精度完整性与各Consumer采用。未证只阻对应真实新效果，历史事实接收、草稿和准确读继续，不把整册状态写成生产可用。 [原文 L34](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/81/813f4f021684457b__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260930-B-PUR-GR-MD01.md.txt:34>)

有意版本组合须保留：本GR准确输入PO v1.1与Stock v1.1.1，并只镜像所需原DTO；当前其他Target后继不能自动替换。Quality effectiveScope信封要与后继QA专业总体／源part映射精确对齐：采购完整合法叶的Q_original可不同于原到货总量，制造总体规则不能硬套。组合锁和原wire采用需具体签署/实际参与证据，本轮未判已有设计矛盾。 [原文 L10](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:10>)、[原文 L11](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:11>)、[原文 L448](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:448>)、[原文 L456](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:456>)、[原文 L491](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:491>)、[原文 L627](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:627>)、[原文 L633](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cc/cc20531bdcc61959__PUR-GR-01_采购收货商业单据_前后端开发Spec_v1.1_REV1_REPAIR_CANDIDATE.md:633>)

## 12. 本轮实际阅读与保留边界

状态：**required_materials_consolidated**。当前正文7013行全部有效语义已理解：原1370行核心/111AC复用，新增82完整JSON的全部唯一字段、SC10/SC11主链、A独立分支、REV1 R00–R18及全部绑定/时序说明。168精确重复子树与SHA64字面没有二次逐字读；完整PO和STK类型镜像及本域类型三段都在原全读窗口。CURRENT88、UA43及最终限定复审31行全文证据保留。

68字段绑定相等仅证明纸面对象的准确前后引用；未执行原脚本、业务代码、SQL、构建、业务测试或Actions。111AC继续NOT_RUN，O-GR01–12实际采用未证，D-GR01–10已接受。

七个固定旧源码的用途/commit/blob已按§17.2分类，本轮未逐件源码全文审阅。包括旧统一400、旧简化DTO、默认PASS、累加Accepted、每次重建WMS单及latest QC等作者限定事实；它们既不是新规范，也不能不经当前证据就宣称最新main存在同样行为。InboundService原作者也仅读1–240、280–438，不扩大成全WMS审计。历史L3/L4保留全量索引入口与未读身份。

证据：[82对象语义投影](<D:/CP6-archives/consolidation-20261010/commercial-cache/gr/fixture-reading-view.json>)、[168精确复用](<D:/CP6-archives/consolidation-20261010/commercial-cache/gr/fixture-exact-reuse.json>)、[68绑定核对](<D:/CP6-archives/consolidation-20261010/commercial-cache/gr/fixture-bindings-comparison.json>)、[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。
