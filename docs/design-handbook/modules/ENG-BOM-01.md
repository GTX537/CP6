# ENG-BOM-01 · 结构发生位置、制造映射与多产出定义

整理状态：`core_semantics_consolidated`。技术内容由ENG-BOM维护，CFG页承担编排/关系规则；不是新增PLM全能主账。

## 1. 业务目的、操作者和Owner边界

设计/制造工程师维护多层结构和准确技术版本；配置工程师组织跨EBOM、MBOM和Routing的映射与适用关系，有权专业人员评审差异。ENG-BOM持BOM内容，ENG-ROU持过程定义，ENG-REV固定准确Manifest/Baseline，CFG持选择/映射规则；ERP/MES/WMS仍持商业、制造和数量主账。[原文C:72](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:72) [原文C:74](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:74) [原文C:181](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:181) [原文C:288](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:288)

多产出是原技术Owner的能力扩展：ENG-BOM/制造工程持投入/产出位置，ROU持生成步骤，每个产物由自己技术Owner给准确规格。共同Baseline不会把副产物B变成根产品A，技术角色也不决定质量或财务价值分类。[原文P:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:25) [原文P:38](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:38) [原文P:46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:46)

## 2. 规范组合和接受范围

RC1＋RV01整包SHA `4b1934e7747e8ac3f0cb6787469775d28cf891b1b35ff0a351323466e0bdbc91`；08_CFG当前主册原PG02内容SHA `e6bad113da249b760301c0260f070f7f5d5b0aae1142d0cfd27e4d07c820ece2`，合读PG03多产出细则、R05/LR02/LR03/FB01/FB02/PG05的准确批准覆盖。CFG七页/42文档动作处理组并非42次独立业务执行，原Owner不因页级整合而改变。[原文C:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:13) [原文C:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:15) [原文C:18](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:18)

PG03三个决定及限定语义已有APR-PG03-001批准，原稿PROPOSED字样保留历史；32个新AC仍NOT_RUN，未追认未展示PG04场景/所有Excel一致性或实际软件能力。PG05-CLAR-001区分技术缺件与现场缺件，CLAR-002区分预约结清与业务记录关闭。[原文G:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c2dee1a8a4fe99ba__APR-CP6-PLM-PG03-001.md:13) [原文G:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c2dee1a8a4fe99ba__APR-CP6-PLM-PG03-001.md:17) [原文G:42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c2dee1a8a4fe99ba__APR-CP6-PLM-PG03-001.md:42) [原文C:1389](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1389) [原文C:1399](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1399) [原文A:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:33)

## 3. 数据、身份和数量

|对象/字段组|必须保留的设计语义|
|---|---|
|结构版本|根对象、准确EBOM版本、子件/材料准确修订；Root/child Revision与Occurrence共同定位结构，不只用物料号。[原文C:187](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:187) [原文C:195](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:195)|
|Occurrence|父节点、行/位置ID和可追路径、数量单位与依据、配置条件/选配/替代Ref；同件两个安装位置不能去重。[原文C:196](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:196) [原文C:198](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:198)|
|制造映射|准确源EBOM位置集合、目标Site MBOM/Route、一对多/多对一、补充制造项、数量转换/损耗依据、未映射和有权排除理由。[原文C:302](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:302)|
|SubstitutionRule|Id/Revision、RequiredObject含版/Occurrence/量单位、ProvidedObject或完整ProvidedAssemblySet、方向化专业证据、条件/明确禁止、[start,end)时区、比例/取整/安装措施、DEV/ECO路径。[原文C:705](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:705)|
|多产出技术集合|版本、OutputOccurrenceId、PRIMARY/CO_PRODUCT/BY_PRODUCT/INTERMEDIATE、产物自身Item/Revision/Spec、生成工序和中间物流向、必要处置责任。[原文P:111](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:111)|
|逐产出固定引用|共同Baseline+OutputOccurrence+Item/Revision+Spec+生成工序+适用配置；独立B Baseline是否必要按后续活动规则，不强迫每副产物复制主账。[原文P:46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:46) [原文P:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:48) [原文P:249](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:249)|
|数量关系|固定配比/目标/范围/观测、分母、量纲与净/毛/干/湿基准、工序/边界/期间、转换证据、偏差判断Owner。[原文P:386](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:386)|

计划投入/产出、实际进入加工、生成、合格、入库、有证损失与REL容量分栏。仅可比单位/同边界/完整来源/不重叠物流下核“期初在制＋外部净进入＝边界输出＋期末在制＋有证损失＋待解释差额”；差额正负均保留，中间物不重复加总，禁止凭公式生成损耗或库存。[原文P:52](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:52) [原文P:56](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:56) [原文P:60](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:60) [原文P:393](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:393)

## 4. 状态、入口和本地写集

|入口|条件与产物|
|---|---|
|CFG-ACT-002-01结构修订|有权ENG-BOM人员对准确草稿/源版派生后继结构，Frozen内容不能直接改；持久化技术位置与差异，不改CFG之外的主账。[原文C:214](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:214)|
|CFG-ACT-002-02结构复核|全层级、子版、单位、循环/遗漏/重复位置可解释；形成复核和缺项，循环/未知版本/无法解释单位阻塞相关批准或冻结。[原文C:203](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:203) [原文C:238](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:238)|
|CFG-ACT-003-01/02制造映射|固定源与目标Site/版本，逐项解释拆合、包装辅料、损耗与联副产；保存受控映射/差异与批准或退回，不自动平账。[原文C:310](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:310) [原文C:321](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:321) [原文C:345](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:345)|
|PG03-ACT-001/002|编制产出技术草案，逐位置核准确对象/角色/生成步骤/处置责任；草稿可显式缺项，必需身份未知阻塞固定但不阻止真实意外产物登记。[原文P:124](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:124) [原文P:147](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:147)|
|PG03-ACT-003/004|全部必需投入/产出/中间关系及组合适用明确后请求ENG固定；只有真实ENG回执才Baseline，不自行造ID或制造许可。[原文P:262](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:262) [原文P:285](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:285)|
|PG03-ACT-005/006|建立带分母/单位/工序和边界的数量关系，读取真实计量作差异复核；不改原测量/REL消费/WMS量。[原文P:399](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:399) [原文P:422](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:422)|

源册未批准统一SQL状态枚举。结构草稿、受控版、解析结果、实际回执和业务关闭分别保存；跨Owner结果仅存准确引用与未决项，原文未给新增分布式原子提交定义。[原文C:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:61) [原文C:1326](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1326) [原文P:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:11)

## 5. 接口与跨Owner合同

ENG/CFG→ENG-REV的PG03-BC-001传根过程/准确输入输出Occurrence/ItemRevision/生成步骤/数量基准及成员版本，ENG固定或明确缺项；不是新API/Topic。BOM技术固定走ENG已有合同，不能把CFG MATCH当REL制造许可。[原文P:1187](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1187) [原文C:880](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:880)

替代方向使用Required/Provided字段：B替A不自动A替B，B替A与C替B不自动C替A；B+C套件不能缺C就把用量改0。LCM只引用准确CFG规则作采用安排，范围须其子集，不另建兼容批准。临时变更走DEV，永久超原选项走工程/ECM。[原文C:597](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:597) [原文C:705](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:705) [原文C:756](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:756) [原文C:1265](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1265)

技术C的准确版/安装/兼容证据缺失阻断整套技术规则；仅现场缺实物C但技术证据完整，可以继续技术规则评审，本次真实使用仍须齐套。实际验证需要C却没有实物仍属技术缺证，不能借PG05豁免。[原文C:1393](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1393) [原文C:1395](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1395)


### CP22消费边界补充（新增专业设计采用）

CP22本轮对五nested技术责任记录新设计ADOPT；不是RC1原批准扩围，也不等于真实provider/部署已采用。PREPARE恰四capture、preparationRef=null；CHECK/SNAPSHOT/CREATE各恰十一capture，逐项绑定真实同一preparationId/revision、原issuer、documentRef/bodyDigest及认证SDK provider合同pin。MES只运输；缺issuer/provider/body或适用性未知须RequiredFenced，零后继native派发。[CP22采用原文:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:113) [CP22采用原文:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:173)

CP22消费完整EBOM-MBOM材料关系、consumptionPoint及CFG解析的effectiveMaterials；UOM是版本化量义和正有理数比例，EXACT整数与loss逐项守恒，数值相等不能替代量义映射。 [CP22采用原文:141](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:141)

本补充直接读采用原文1–176行；不冒称本组已重读CP22全部573成员、accepted native wire或实际运行。当前`qualifiedActualProviderPin=null`、17 actualAdopted=0、providerGrant=0、37AC NOT_RUN。[CP22采用原文:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:173)

## 6. 事务、幂等与恢复

共享合同要求业务Key/指纹、业务版次和DB并发令牌各自独立；原Owner命令Unknown查原键，不以新建对象或换Key绕过。CFG主册明确“解析记录到真实MES准入提交的实现协议未定义”，本功能稿不能被转换成已获准跨库事务算法。[原文S:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381) [原文C:1326](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1326)

规则/输入改变新建解析，不覆盖历史。实际源回执按Owner身份、版本、EventId及范围去重；累计v1=10、v2=12得到12，不是22，迟到v1不得回退；无Owner可比较顺序就保留争议，不按ReceivedAt覆盖。实际入库未知使用原WMS请求查回，不新建生产或退制造消费。[原文C:908](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:908) [原文P:82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:82) [原文P:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:84) [原文P:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:88) [原文P:655](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:655) [原文P:701](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:701)

停止后的未消费份额只在原Owner证明“未发生且不能再发生”、包括相关子执行和完整范围时可释放。REL/DEV各结自己的账，ECM/LCM独立核业务后果；与释放量封闭有关的Unknown未核清不能释放，其余不相关库存/质量义务可以另行未结。[原文P:92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:92) [原文P:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:94) [原文C:1403](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1403) [原文C:1405](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1405)

## 7. 权限与租户

所有动作重新检查当前角色、Tenant/组织、准确对象版本、范围和适用条件；协调页面只能读取原Owner事实，不能编辑成期望值。真实产出回执跨租户同号不合并，不可见不等不存在；规则集合权限覆盖不足必须UNKNOWN，不得以唯一可见候选宣称全局唯一。[原文C:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:61) [原文C:1103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1103) [原文P:82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:82)

工程可维护技术草稿，CFG批准选择规则，专业Owner持验证/计量依据，REL/DEV/MES/WMS分别决定准入和实际事实。源册未给完整敏感字段分级表；实现沿原Owner权限返回最少必要内容并保留覆盖不足原因。[原文C:74](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:74) [原文P:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:25) [原文S:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381)

## 8. 页面与服务端检查

CFG02显示根版、全部Occurrence层级、子版/数量/单位/路径及循环缺项；CFG03显示EBOM→MBOM的真实差异、产出定义和数量关系；CFG04是方向化替代和选项，CFG06并列计划/实际/质量/库存，CFG07显示后果与切换。[原文C:174](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:174) [原文C:281](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:281) [原文C:490](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:490) [原文P:100](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:100)

服务端不能隐藏未解析行达到“完整”，不能按同品号去重，不能用无依据系数平账；必选缺失为UNKNOWN，互斥CONFLICT，不取第一候选。角色可以导航不代表能修改其他Owner。[原文C:211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:211) [原文C:319](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:319) [原文C:497](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:497) [原文C:528](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:528) [原文C:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:61)

## 9. 实施顺序和已有代码限制

整合建议依次冻结产品/Occurrence身份与单位、实现版本化结构及多对多映射、精确多产出成员、替代/适用解析，再接ENG固定与实际回执。先证明共同Manifest能承载必需关系，再接受多产出固定；不对WP0摘要字段做未经Review的扩展。[原文C:203](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:203) [原文C:310](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:310) [原文P:260](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:260)

本册未重审源码；RC1设计不证明Main/WP0已具多产出模型。真实Owner wire和物理实现未定义，不把这份开发理解册称为已授权数据库迁移。[原文P:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:11) [原文P:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:48) [原文A:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:35)

## 10. 验收场景及NOT_RUN

CFG原AC003/004验证同件双位置和循环阻断；005/006验证不同工厂受控映射和未解释缺行；011/012验证单向替代和临时不改永久；LR02-033–040验证逆向/传递/完整套件、选择缺失/规则冲突/相邻半开时间/范围不足。[原文C:264](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:264) [原文C:271](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:271) [原文C:371](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:371) [原文C:378](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:378) [原文C:682](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:682) [原文C:689](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:689) [原文C:754](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:754) [原文C:1138](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1138)

PG03-M001–012直接覆盖产出身份、组不可覆盖、B身份不可借A、组合固定、理论不生实际、分母缺失、未知差额和中间量不双计；M017–024覆盖真实事件去重、意外产物、零与未知、累计更正、分层库存。全部NOT_RUN；未以静态映射或案例数字冒充真实验收。[原文P:178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:178) [原文P:453](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:453) [原文P:722](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:722) [原文P:856](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:856) [原文G:42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c2dee1a8a4fe99ba__APR-CP6-PLM-PG03-001.md:42)

### PG04 CFG共享动作直接断言

以下为共享原Owner动作的开发验收观察点，编号前缀为 `AC/TC-PLM-PG04-A`，均 **NOT_RUN**；假设前置不是现实事实，不改变本册Owner边界。

|断言|必须保持的可观察结果|
|---|---|
|067/068|配置只引用准确工程/经营身份，不按显示名或latest伪装固定。 [原文PG04:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:83)|
|069/070|映射按Site及规格由两端核验；相同代码/名称不自动等价合并。 [原文PG04:186](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:186)|
|071/072|左右Occurrence即使同P仍各保位置/用量；循环路径和未知子版阻相应批准/冻结。 [原文PG04:289](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:289)|
|073/074|EBOM→MBOM/Route逐位置保差异理由；不删未映射必需行，不猜数量换算。 [原文PG04:393](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:393)|
|075/076|Route变化由ENG-ROU形成后继；不编辑旧WO历史工序。 [原文PG04:497](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:497)|
|077/078|必选项无来源选择/批准默认时留空待补，不以首项造商业默认。 [原文PG04:601](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:601)|
|079/080|缺选择保持UNKNOWN，互斥保持CONFLICT；完整匹配仍不等REL。 [原文PG04:704](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:704)|
|081/082|Required/Provided是有向候选；B替A不自动A替B。 [原文PG04:807](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:807)|
|083/084|规则批准保客户/方向/限制；单批DEV不变永久BOM替换。 [原文PG04:911](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:911)|
|085/086|B替A且C替B不自动推C替A，组合返回专业评价。 [原文PG04:1018](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1018)|
|087/088|套件固定全部准确组件、数量/安装/兼容依据；“缺C”须合读下方PG05澄清，不能统一解释成实物未到。 [原文PG04:1124](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1124)|
|089/090|适用范围保业务时区/活动锚点及获准半开区间；空Site非全域，序列字典序非Owner次序。 [原文PG04:1227](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1227)|
|091/092|唯一MATCH须完整规则覆盖；局部一条不能证明全局唯一，完整无匹配才NO_MATCH。 [原文PG04:1331](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1331)|
|093/094|补输入形成新解析并保旧UNKNOWN；冲突不能选列表第一项解决。 [原文PG04:1435](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1435)|
|095/096|同物料两批各保实际BL-C/BL-D谱系；不从计划或当前主数据回填Actual。 [原文PG04:1539](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1539)|
|097/098|计划BL-D/实际BL-C差异保原事实并交处置；Stock物料码不补工程来源。 [原文PG04:1643](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1643)|
|099/100|多个合法候选须当前使用方明确选择；未选保CHOICE_REQUIRED，不自动取新版。 [原文PG04:1750](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1750)|
|101/102|规则域无权/不可达保UNKNOWN且不泄露对象；MATCH仍需REL等条件。 [原文PG04:1856](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1856)|
|103/104|切换按获准Site/活动范围；筛选掉未知B不构成全范围就绪。 [原文PG04:1959](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1959)|
|105/106|真实采用回执逐范围；ECN发出不改所有WO，回退不复活撤销许可。 [原文PG04:2063](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:2063)|
|107/108|规则R@1→R@2变化明确重评；同名不静默迁LCM引用或维持绿灯。 [原文PG04:2170](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:2170)|
|109/110|后继采用需当前规则/活动条件及旧义务；历史生产成功不补未知库存/当前许可。 [原文PG04:2276](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:2276)|

PG04-A088的“缺C”由已认可PG05-CLAR-001限定为缺准确技术修订、安装关系或必要专业依据。仅现场实物C未到而技术证据齐备，技术规则仍可按职责评审；本次使用另核齐套。实际试验所需C缺失仍是验证缺证，不能默认缺件运行/分步安装。[原文PG05:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1c/1c6dc5f451727af6__APR-CP6-PLM-PG05-001.md:15)


**PG03 必要验收附件。** 固定116动作中先补的16动作32直接断言，与后续PG04的100动作200断言互补，不重复计数。 全部 `NOT_RUN`。

| 原案例 | 本模块必须保留的观察/拒绝条件 |
|---|---|
| PG03-A015–016 | 左右Occurrence同件仍分开；右侧修订形成后继草案，Frozen原版不能原地改写。 [原文:460](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20b2dbf9b6670419__20b2dbf9b66704191955e894640c30de9f28a2103e4f4179abd7da313667f04f.md:460) |
| PG03-A017–018 | 工厂EBOM→MBOM/Route差异与单位转换必须有技术来源；kg→m无依据系数不能因数字平衡获批。 [原文:519](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20b2dbf9b6670419__20b2dbf9b66704191955e894640c30de9f28a2103e4f4179abd7da313667f04f.md:519) |
| PG03-A021–022 | Site、时区/时间与活动锚点明确且重叠已解释才批准规则版；互斥目标不按大版本或后保存取胜。 [原文:637](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20b2dbf9b6670419__20b2dbf9b66704191955e894640c30de9f28a2103e4f4179abd7da313667f04f.md:637) |

**PG05 必要验收附件。** 12个跨页场景仍是验收设计；两项澄清按[准确APR](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/1c/1c6dc5f451727af6__APR-CP6-PLM-PG05-001.md:15)承接，不扩大到实施或真实业务批准。 全部 `NOT_RUN`。

| 原案例 | 本模块必须保留的观察/拒绝条件 |
|---|---|
| PG05-X001–002 | B+C技术依据完整但实物C未到，可批准技术规则而阻断本次使用；C必要技术证据缺失则不能批准整套，即使实物已到。 [原文:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f8/f8187487d223de16__f8187487d223de16d39b5717c08264e42964b0b4bb5083e40b3dea765bd13bc3.md:7) |
| PG05-X012 | 同根净投入100、A产出70而B缺报时，REL消费100与DEV真实暴露12各用自己证据；B未知不当零、A质量通过不覆盖全组。 [原文:353](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f8/f8187487d223de16__f8187487d223de16d39b5717c08264e42964b0b4bb5083e40b3dea765bd13bc3.md:353) |

## 11. 未闭合输入与退出条件

CFG到MES提交的实际保障、原技术Owner完整API/schema、物理唯一索引/数量精度/单位转换策略、组织实例条件与正式Excel对照尚未由本批证明。单位/损耗/分Site切换不能默认全产品通用；必要参数由相应Owner确认。[原文C:1324](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1324) [原文C:1325](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1325) [原文C:1326](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1326) [原文C:1330](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1330)

PG03明确：现有ENG Manifest若无法表达逐输出必要关系，应登记技术固定缺项，不能只改显示名或伪造新摘要算法。后续实现需用准确成员与摘要闭合证据退出此门槛；本册没有断言已发现现行实现不兼容，也没有授权更改ENG/WP0。[原文P:46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:46) [原文P:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:48) [原文P:260](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:260)

## 12. 来源与实际阅读

本轮全文实读08_CFG 1–1407，分段1–385/386–762/763–1144/1145–1407；PG03细则1–1243，分段1–314/315–648/649–988/989–1243，包含全部16动作、32AC、4逻辑合同。03_ENG1–3207、各批准记录已全文；共享HTML可见正文1–385结构化阅读。未执行原项目脚本或业务测试；正式Office与其余强制附件仍有补读队列，未升级材料闭合。[阅读登记](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。[原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1) [原文P:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1)
本轮已补齐该PG04附件的非重复业务语义：新增直接断言实读；原规则/过程/旧场景按已读主册精确文字复用并补读全部未匹配语义。表中取本模块相关动作；TRACE/字段ID/来源定位等仅结构核对，不称逐行独立全文，精确复用与未读行见台账。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
