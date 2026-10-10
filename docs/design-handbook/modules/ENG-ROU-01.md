# ENG-ROU-01 · 版本化工艺、资源要求与实际执行交接

整理状态：`core_semantics_consolidated`。继承RC1/RV01共享CFG设计；工艺定义与MES实绩始终是两个事实。

## 1. 业务目的、操作者和Owner边界

制造工程师维护Routing/BOP步骤、顺序/依赖、标准参数、工作中心/资源类别和受控作业文件；专业评审人核对制造配置可提交技术固定。ENG-ROU持技术版本，MES持实际开工、报工和投入产出；设备在线或工具可见不能证明技术版本批准，规格固定也不能证明当前实例可用。[原文C:388](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:388) [原文C:390](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:390) [原文C:450](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:450) [原文C:461](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:461)

多产出过程的生成步骤由ROU管理，BOM持产出位置/成员，CFG持跨视图适用，ENG-REV固定组合。原根执行由MES持有，REL/DEV独立授用途与暴露范围，质量/WMS持结果与库存。[原文P:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:25) [原文P:72](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:72)

## 2. 规范组合与接受边界

RC1＋RV01整包SHA `4b1934e7747e8ac3f0cb6787469775d28cf891b1b35ff0a351323466e0bdbc91`；08_CFG当前主册原PG02内容SHA `e6bad113da249b760301c0260f070f7f5d5b0aae1142d0cfd27e4d07c820ece2`，合读PG03多产出细则、R05/LR02/LR03/FB01/FB02/PG05的准确批准覆盖。CFG七页/42文档动作处理组并非42次独立业务执行，原Owner不因页级整合而改变。[原文C:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:13) [原文C:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:15) [原文C:18](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:18)

PG03三个决定及限定语义已有APR-PG03-001批准，原稿PROPOSED字样保留历史；32个新AC仍NOT_RUN，未追认未展示PG04场景/所有Excel一致性或实际软件能力。PG05-CLAR-001区分技术缺件与现场缺件，CLAR-002区分预约结清与业务记录关闭。[原文G:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c2dee1a8a4fe99ba__APR-CP6-PLM-PG03-001.md:13) [原文G:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c2dee1a8a4fe99ba__APR-CP6-PLM-PG03-001.md:17) [原文G:42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c2/c2dee1a8a4fe99ba__APR-CP6-PLM-PG03-001.md:42) [原文C:1389](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1389) [原文C:1399](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1399) [原文A:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:33)

## 3. 数据身份、字段和状态轴

|对象/字段组|准确含义|
|---|---|
|Routing/BOP Version|准确Route/BOP与适用MBOM/制造视图、Site和原技术修订；新工艺不会重写旧WO固定依据。[原文C:404](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:404) [原文C:396](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:396)|
|Operation / Dependency|工序发生位置、真实顺序、分支和必要前后关系；工程组成摘要中语义工序顺序不能因集合排序丢失。[原文C:405](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:405) [原文E:601](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:601)|
|Parameter Requirement|控制参数、单位、限值及方法由ROU/质量受控定义；源册未提供全产品通用精度/默认值。[原文C:406](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:406) [原文C:1330](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1330)|
|Resource Requirement|工具/版模/机器能力和准确版本条件，由ENG-TOOL/设备Owner提供；技术固定和实际使用两个阶段检查。[原文C:407](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:407)|
|Instruction Reference|受控作业文件/图纸准确版与分发对象，由DOC持权威和内容。[原文C:408](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:408)|
|Output Generation|OutputOccurrence、Item/Revision/Spec、生成步骤、过程份额、是否进入下游中间流程；工艺回流不等BOM产品自包含。[原文P:40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:40) [原文P:42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:42) [原文P:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:115)|
|Joint Execution Context|原ExecutionRequestKey/请求内容身份、唯一根WO、单一控制Purpose、完整产出包络、每输出去向/必要条件和独立计量槽位/DEV Claim。[原文P:521](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:521)|
|Actual Receipt|Owner/ObjectId/Version/EventId、原根/步骤/OutputOccurrence、真实产品配置和批份额、数量单位、增量/累计/更正、发生时点和覆盖范围。[原文P:655](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:655)|

工艺技术版、REL许可、MES建单、实际首次投产/报工、产出质量与入库不能压成一状态。原页面显示语义不是旧物理枚举，无法从本稿推断新SQL列或业务状态常量。[原文C:420](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:420) [原文P:659](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:659) [原文P:796](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:796)

## 4. 正常流程、命令与写集

`CFG-ACT-004-01`依据准确原Route和变化范围提出原Owner草案或ECO关联；`004-02`核步骤依赖/参数/文件/资源要求及BOM一致性，形成可提交ENG受控版本的配置包。执行前实际资源适用性由执行Owner另核；不得修改活动WO历史工艺。[原文C:412](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:412) [原文C:423](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:423) [原文C:447](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:447)

|阶段/动作|产物与阻断边界|
|---|---|
|技术编制/复核|本Owner后继Route/BOP、参数/关系/准确文件工具引用和评审差异；资料/资源要求未知不宣布制造配置可用。[原文C:414](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:414) [原文C:421](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:421)|
|PG03-003/004组合固定|逐输入/输出/工序映射草案→组合兼容核对→ENG真实固定或缺项；各单独有效规格不足以证明本Site组合。[原文P:262](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:262) [原文P:285](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:285)|
|PG03-007活动编制|固定共同技术成员、一个控制Purpose和全部产出去向、现在制造前置与未来交付前置、全部独立计量/Claim；草稿不执行。[原文P:534](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:534)|
|PG03-008联合准入|REL/DEV和必要专业Owner各自证明同技术/Scope/Purpose全部条件；MES按原键同载荷建立真实根WO/拒绝/未知，ACK不当建立。[原文P:557](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:557)|
|PG03-009/010实际回执|原Owner登记实际并绑定原根/位置/份额；协调端存准确引用、覆盖与差异，不生成质量或库存事实。[原文P:668](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:668) [原文P:691](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:691)|
|PG03-013/014停止与结清|实际Owner分未开始/已发生/未知处理；REL/DEV仅释放已证终结剩余额，ECM/LCM独立决定各自后果关闭。[原文P:936](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:936) [原文P:959](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:959) [原文C:1403](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1403)|

技术不可分的过程必须全部制造前置同时成立，不能A量产/B试验两张不匹配许可拼成共同资格，也不能按产出行再建根WO。但限定试制中受控保留的B未来销售条件若不是本次制造硬前置，无需自动提前满足。[原文P:74](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:74) [原文P:76](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:76) [原文P:530](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:530) [原文P:532](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:532)

## 5. 跨Owner合同和恢复入口

PG03-BC-001把准确成员/工序/数量基准交ENG固定；BC-002把同一根意图、Purpose、输出范围、许可/Reservation/Claim交MES；BC-003分别读取MES实际、质量和WMS回执；BC-004把技术变化、已发生份额、旧义务和安全终结证明交ECM/LCM。它们是逻辑信息合同，不是已批准HTTP/Topic/新服务。[原文P:1187](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1187) [原文P:1199](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1199) [原文P:1211](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1211) [原文P:1223](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1223)

As-planned来自订单/WO固定技术依据，As-built取MES真实配置，实物谱系取WMS/MES；计划和当前主数据不能填历史Actual，缺来源为UNKNOWN。新技术、替代或ECN发出都不能批改现有WO。[原文C:980](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:980) [原文C:1004](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1004) [原文C:1013](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1013) [原文C:1226](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1226)


### CP22消费边界补充（新增专业设计采用）

CP22本轮对五nested技术责任记录新设计ADOPT；不是RC1原批准扩围，也不等于真实provider/部署已采用。PREPARE恰四capture、preparationRef=null；CHECK/SNAPSHOT/CREATE各恰十一capture，逐项绑定真实同一preparationId/revision、原issuer、documentRef/bodyDigest及认证SDK provider合同pin。MES只运输；缺issuer/provider/body或适用性未知须RequiredFenced，零后继native派发。[CP22采用原文:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:113) [CP22采用原文:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:173)

CP22普通consumer要求DAG无环、operation/材料关联有效，effectiveOperations/materials/outputs等于CFG完整解析，PRIMARY恰1。ENG允许其他工程方案不表示该consumer已支持它们；真实provider缺证仍fence。 [CP22采用原文:141](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:141)

本补充直接读采用原文1–176行；不冒称本组已重读CP22全部573成员、accepted native wire或实际运行。当前`qualifiedActualProviderPin=null`、17 actualAdopted=0、providerGrant=0、37AC NOT_RUN。[CP22采用原文:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:173)

## 6. 事务、幂等与未知恢复

共享合同要求业务Key/指纹、业务版次和DB并发令牌各自独立；原Owner命令Unknown查原键，不以新建对象或换Key绕过。CFG主册明确“解析记录到真实MES准入提交的实现协议未定义”，本功能稿不能被转换成已获准跨库事务算法。[原文S:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381) [原文C:1326](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1326)

规则/输入改变新建解析，不覆盖历史。实际源回执按Owner身份、版本、EventId及范围去重；累计v1=10、v2=12得到12，不是22，迟到v1不得回退；无Owner可比较顺序就保留争议，不按ReceivedAt覆盖。实际入库未知使用原WMS请求查回，不新建生产或退制造消费。[原文C:908](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:908) [原文P:82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:82) [原文P:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:84) [原文P:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:88) [原文P:655](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:655) [原文P:701](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:701)

停止后的未消费份额只在原Owner证明“未发生且不能再发生”、包括相关子执行和完整范围时可释放。REL/DEV各结自己的账，ECM/LCM独立核业务后果；与释放量封闭有关的Unknown未核清不能释放，其余不相关库存/质量义务可以另行未结。[原文P:92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:92) [原文P:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:94) [原文C:1403](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1403) [原文C:1405](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1405)

## 7. 权限和租户边界

所有动作重新检查当前角色、Tenant/组织、准确对象版本、范围和适用条件；协调页面只能读取原Owner事实，不能编辑成期望值。真实产出回执跨租户同号不合并，不可见不等不存在；规则集合权限覆盖不足必须UNKNOWN，不得以唯一可见候选宣称全局唯一。[原文C:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:61) [原文C:1103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1103) [原文P:82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:82)

工程可维护技术草稿，CFG批准选择规则，专业Owner持验证/计量依据，REL/DEV/MES/WMS分别决定准入和实际事实。源册未给完整敏感字段分级表；实现沿原Owner权限返回最少必要内容并保留覆盖不足原因。[原文C:74](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:74) [原文P:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:25) [原文S:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381)

## 8. 页面、交互与后端规则

CFG03以准确Site、BOM和Route为上下文，分栏显示步骤依赖、参数、技术资源、受控资料和Owner缺项；资源规格与当前实例状态分别展示。CFG06并列计划/实际/质量/库存与四态解析，CFG07展示源/目标、旧义务、实际采用、分范围未完成。[原文C:281](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:281) [原文C:404](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:404) [原文C:873](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:873) [原文C:1148](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1148)

服务端选项缺失不默认第一项，完整无匹配才NO_MATCH，权限/输入/规则覆盖不足为UNKNOWN、互斥为CONFLICT。获准[起点,终点)保业务时区和明确活动时点；序列区间必须Owner提供可比较次序，不能字符串排序。[原文C:503](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:503) [原文C:779](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:779) [原文C:795](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:795) [原文C:886](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:886)

工艺过程有回流时明确生成/消耗边界，不能让中间量与末端量双计；停止B需求也不能把已经发生共同加工的B回执未到改成零。页面允许显示有证零，但零是否满足技术目标独立判断。[原文P:40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:40) [原文P:68](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:68) [原文P:92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:92)

## 9. 建议实施顺序与既有实现差异

先对齐Route/BOP步骤身份、实际顺序、参数单位、BOM映射、工具/文档准确引用；再接ENG组合固定；随后接REL/DEV联合前置、MES原键单根建立与真实投产/回执；最后接分范围结清/变更影响。该顺序是依前置关系的整合建议，不是编码授权。[原文C:412](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:412) [原文P:557](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:557) [原文P:959](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:959)

本册不重复审计当前源码。PG03显式不改WP0实现/ENG摘要算法；所有物理接口和数据库细节仍应由实际实现规范冻结，不用过去静态通过推断Main已支持多产出。[原文P:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:48) [原文P:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:11)

## 10. 验收与运行状态

CFG-007验证Route已批准但WO未开始时不能显示实际工序完成；008验证新Route不改旧WO；013/014验证Site共存和无Owner顺序不能推序列范围；015/016验证MATCH不代许可、冲突不选最高版；017/018验证同物料多Baseline及计划不能填Actual；019/020验证局部切换及回退不复活旧许可。[原文C:473](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:473) [原文C:480](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:480) [原文C:856](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:856) [原文C:863](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:863) [原文C:963](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:963) [原文C:970](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:970) [原文C:1065](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1065) [原文C:1072](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1072) [原文C:1238](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1238) [原文C:1245](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1245)

PG03-M013–016验证单根、不可分条件、受限试制与未来销售分离、一个计量槽位Unknown阻止新执行；M025–032验证停止/失联不等零、Q120/C100/R20仅有证余额结清、迟到更正保历史、共同工艺影响与逐输出验证。全部NOT_RUN，假设数值不是实例证据。[原文P:588](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:588) [原文P:633](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:633) [原文P:990](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:990) [原文P:1020](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1020) [原文P:1124](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1124) [原文P:1243](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1243)

### PG04 CFG中路线与实际执行相关断言

以下为共享原Owner动作的开发验收观察点，编号前缀为 `AC/TC-PLM-PG04-A`，均 **NOT_RUN**；假设前置不是现实事实，不改变本册Owner边界。

|断言|必须保持的可观察结果|
|---|---|
|073/074|EBOM→MBOM/Route逐位置保差异理由；不删未映射必需行，不猜数量换算。 [原文PG04:393](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:393)|
|075/076|Route变化由ENG-ROU形成后继；不编辑旧WO历史工序。 [原文PG04:497](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:497)|
|095/096|同物料两批各保实际BL-C/BL-D谱系；不从计划或当前主数据回填Actual。 [原文PG04:1539](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1539)|
|097/098|计划BL-D/实际BL-C差异保原事实并交处置；Stock物料码不补工程来源。 [原文PG04:1643](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1643)|
|103/104|切换按获准Site/活动范围；筛选掉未知B不构成全范围就绪。 [原文PG04:1959](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:1959)|
|105/106|真实采用回执逐范围；ECN发出不改所有WO，回退不复活撤销许可。 [原文PG04:2063](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bd/bda9816a2958a298__bda9816a2958a298ffc00cb40ac687286530a2db25b3481a72d9e4e7a1394dad.md:2063)|

其余CFG 22动作/44断言的完整开发观察表见 [ENG-BOM共享合同页](D:/CP6/docs/CP6_开发设计文档_20261010/modules/ENG-BOM-01.md)。CFG协调、ENG-ROU技术版本和MES实际工序Owner继续独立。


**PG03 必要验收附件。** 固定116动作中先补的16动作32直接断言，与后续PG04的100动作200断言互补，不重复计数。 全部 `NOT_RUN`。

| 原案例 | 本模块必须保留的观察/拒绝条件 |
|---|---|
| PG03-A017–018 | 工厂EBOM→MBOM/Route差异与单位转换必须有技术来源；kg→m无依据系数不能因数字平衡获批。 [原文:519](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20b2dbf9b6670419__20b2dbf9b66704191955e894640c30de9f28a2103e4f4179abd7da313667f04f.md:519) |
| PG03-A019–020 | 技术步骤/参数/资源能力齐备可提交ENG受控版本，未来设备实例未指定不等技术缺项；机器在线也不补缺作业规范。 [原文:578](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20b2dbf9b6670419__20b2dbf9b66704191955e894640c30de9f28a2103e4f4179abd7da313667f04f.md:578) |

## 11. 缺口和退出条件

CFG到MES提交的实际保障、原技术Owner完整API/schema、物理唯一索引/数量精度/单位转换策略、组织实例条件与正式Excel对照尚未由本批证明。单位/损耗/分Site切换不能默认全产品通用；必要参数由相应Owner确认。[原文C:1324](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1324) [原文C:1325](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1325) [原文C:1326](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1326) [原文C:1330](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1330)

PG03明确：现有ENG Manifest若无法表达逐输出必要关系，应登记技术固定缺项，不能只改显示名或伪造新摘要算法。后续实现需用准确成员与摘要闭合证据退出此门槛；本册没有断言已发现现行实现不兼容，也没有授权更改ENG/WP0。[原文P:46](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:46) [原文P:48](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:48) [原文P:260](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:260)

共同过程耦合变化必须让工程/VER逐目标判断。A不受B变化影响需专业证明；Baseline改变建立后继PlanId，同Baseline技术范围变化按PlanVersion，不能复制整组PASS。真实过程计量点和实物份额无法证明时，该计量配置暂不可依赖。[原文P:78](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:78) [原文P:1064](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1064) [原文P:1068](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1068)

## 12. 原文和真实阅读

本轮全文实读08_CFG 1–1407，分段1–385/386–762/763–1144/1145–1407；PG03细则1–1243，分段1–314/315–648/649–988/989–1243，包含全部16动作、32AC、4逻辑合同。03_ENG1–3207、各批准记录已全文；共享HTML可见正文1–385结构化阅读。未执行原项目脚本或业务测试；正式Office与其余强制附件仍有补读队列，未升级材料闭合。[阅读登记](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。[原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/07/0791595ffb0d298a__08_CFG_当前阅读主册.md:1) [原文P:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/08/089216a4b4fea2d5__089216a4b4fea2d5ea66548ec2fb22f85a69a46b72b810a6affd7c9bc9132397.md:1)
本轮已补齐该PG04附件的非重复业务语义：新增直接断言实读；原规则/过程/旧场景按已读主册精确文字复用并补读全部未匹配语义。表中取本模块相关动作；TRACE/字段ID/来源定位等仅结构核对，不称逐行独立全文，精确复用与未读行见台账。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
