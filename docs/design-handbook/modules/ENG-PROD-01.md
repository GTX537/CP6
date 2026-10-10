# ENG-PROD-01 · 产品身份与业务料号映射

整理状态：`core_semantics_consolidated`。本模块承接共享ENG/SUP设计；未产生独立MD02或新物理表批准。

## 1. 业务目的、操作者和Owner边界

工程主数据人员识别产品、组件、总成的稳定技术身份，工程与业务主数据Owner共同确认ERP物料映射。一个技术身份可对应多个工厂/组织业务料号，但同名、同码、外观相似均不能证明是同一个产品。采购、售价、库存、工单仍由原业务Owner负责。[原文S:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:67) [原文S:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:69) [原文S:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:126)

ENG-PROD定义“是什么”，ENG-REV定义该产品的准确技术修订/候选/基线；REQ承接要求来源，CFG/BOM/ROU持组成和适用规则。产品编号不是BaselineId、受控图纸版本或执行许可。[原文E:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:39) [原文E:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:55) [原文C:375](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:375)

## 2. 选定规范与接受边界

RC1＋RV01整包SHA `4b1934e7747e8ac3f0cb6787469775d28cf891b1b35ff0a351323466e0bdbc91`；主读03_ENG和11_SUP的SUP-01。SUP是PG02按原动作/验收重新编排的当前主册，旧编号仍保留；共同批准入口仅接受明确范围的功能设计，不据此确认所有实体字段/Excel历史一致性或代码实现。[原文A:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:21) [原文A:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:25) [原文S:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:13) [原文S:38](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:38) [原文S:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:39)

本模块适用RC1原设计与已批准补遗的准确覆盖范围；更晚文件名不自动覆盖。继承设计是有效输入，不能因无独立MD02就当成“待从零设计”。RV01静态定稿不等于生产或实施Ready。[原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:9) [原文A:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:33)

## 3. 核心对象、字段和版本轴

|对象/字段组|需要保留的语义|
|---|---|
|Engineering Product|稳定工程身份、类别、识别来源；产品身份跨技术修订保持，不能把每次图纸修订重新当新品。[原文S:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:83) [原文E:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:55)|
|Business Mapping|工程身份与准确ERP/BP主数据引用、业务Owner、组织/工厂、适用版本/配置/用途；受控一对一或多对多，不能按名称临时拼接。[原文S:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:84) [原文S:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:93)|
|确认与冲突|双方Owner、确认依据、冲突/歧义和原因、历史映射；历史不明确标待核，不能归并消除原差异。[原文S:86](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:86) [原文S:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:93)|
|使用关系|BOM/工艺/VER/订单中的准确引用与历史，用于影响分析；这些引用不是本模块接管它们的主账。[原文S:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:87)|

这些是逻辑字段组，原册未给完整SQL列、字段长度、金额精度、映射效期优先算法。不得从既有ERP编号长度推断新的跨系统规范。Revision/WorkingVersion/Configuration/Baseline的精确身份见[ENG-REV](D:/CP6/docs/CP6_开发设计文档_20261010/modules/ENG-REV-01.md)。[原文S:900](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:900) [原文E:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:55)

## 4. 正常流程、命令和本地写集

1. `ACT-SUP-001-01`：工程主数据人员根据可识别来源/类别建立候选技术身份与业务映射，保存差异及冲突；动作不写采购/库存主账。[原文S:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:102)
2. `ACT-SUP-001-02`：工程与业务两方有权Owner依据准确版本、组织适用性与历史核对身份和映射；有歧义先核对，未知不当作匹配。[原文S:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:126)
3. 被BOM、路线、验证或订单引用后，继续保留准确引用与映射历史。不得将名称合并变成自动替换在途业务对象。[原文S:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:87) [原文S:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:93)

原文给的是业务动作，不是已冻结REST命令或数据库状态枚举。实现应将技术身份、映射确认事实、冲突理由及审计作为本Owner的持久化边界；跨Owner确认如何原子生效需真实Owner合同确定，不能在文档中虚构分布式单事务。[原文C:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381) [原文S:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:126)

## 5. 接口、交接与依赖

给ENG/CFG提供稳定产品身份及准确映射，读取ERP/BP真实身份和使用关系。每项映射必须带来源Owner和适用域，结果有歧义就保持待核，不用latest或临时名字补齐。原册没有获准的新URL/JSON wire；开发应先建立本地适配接口并对接Owner合同，不按本册逻辑字段组生造通用远程写接口。[原文S:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:93) [原文C:375](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:375) [原文C:382](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:382)

与REQ、ITER的交接保留“来源要求包/逐条需求/输入版本/技术候选”等不同身份，不能把产品编码用作所有对象共同主键。[原文E:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:55) [原文E:1391](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:1391)


### CP22消费边界补充（新增专业设计采用）

CP22本轮对五nested技术责任记录新设计ADOPT；不是RC1原批准扩围，也不等于真实provider/部署已采用。PREPARE恰四capture、preparationRef=null；CHECK/SNAPSHOT/CREATE各恰十一capture，逐项绑定真实同一preparationId/revision、原issuer、documentRef/bodyDigest及认证SDK provider合同pin。MES只运输；缺issuer/provider/body或适用性未知须RequiredFenced，零后继native派发。[CP22采用原文:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:113) [CP22采用原文:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:173)

CP22普通路径的item/site/purpose/RELEASED基线与完整technicalContent来自ENG；PLM用途policy及REL用途授权各由原Owner签发。不得将工程发布或manifest hash解释为制造许可。 [CP22采用原文:141](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:141)

本补充直接读采用原文1–176行；不冒称本组已重读CP22全部573成员、accepted native wire或实际运行。当前`qualifiedActualProviderPin=null`、17 actualAdopted=0、providerGrant=0、37AC NOT_RUN。[CP22采用原文:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md:173)

## 6. 事务、幂等、并发和恢复

共享合同要求Tenant/组织/对象权限、业务幂等键与请求指纹、业务Revision和DB并发令牌分别保留。超时或未知提交查原Owner/原业务键；不能靠重建产品或换Key消除Unknown。具体映射唯一索引、冲突先后规则及跨Owner提交保障须在适配阶段确认，本册没有批准全局仅按ProductCd去重。[原文C:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381) [原文S:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:69) [原文S:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:93)

若历史映射无法证明，应保留旧身份和差异，阻塞依赖准确身份的确认，补证后走受控修订。不能反向改写已冻结基线的Product/Revision内容。[原文S:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:93) [原文E:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:65)

## 7. 权限、租户与敏感字段

页面可见不代替动作时校验：当前角色、对象版本、组织/适用域及前置条件都要重新确认。工程Owner可以整理技术候选，业务料号映射确认要求准确业务Owner参与；不能以工程管理员权限写ERP库存/价格。[原文S:56](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:56) [原文S:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:126) [原文C:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381)

跨组织查询与导出沿原Owner的数据授权，不用同码推导跨租户身份。身份映射历史、人员和冲突原因按可见字段范围输出；具体敏感字段表仍由真实业务合同提供。[原文C:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381)

## 8. 页面、交互和服务端校验

SUP-01是产品身份/基础映射工作台：展示工程身份类别、ERP/BP映射、适用范围、冲突与依据、历史使用引用。创建候选和双方确认是不同操作；未知/歧义明确显示，不能自动打“匹配成功”。[原文S:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:67) [原文S:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:83) [原文S:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:102) [原文S:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:126)

服务端重复验证当前授权、准确引用和适用域；页面字段完整不能替代Owner确认。静态HTML说明不是已实现产品页面，真实错误展示和路由需实现时按本规则验证。[原文S:40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:40) [原文S:56](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:56)

## 9. 实施顺序和既有代码边界

建议顺序：先固定技术身份/业务映射字段语义与原Owner；再实现候选、双Owner确认、差异历史和准确引用；最后接BOM/路线/REQ及使用关系查询。顺序是整合建议，依据映射先于依赖使用的前置条件。[原文S:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:93) [原文S:126](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:126)

本册不作新增源码审计；RC1/RV01只能证明设计基线。固定Main `90c871fe`和WP0分支实现的差异由根代码基线索引管理，不能把历史WP0演示当成本目标已上线。[原文R:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:9) [原文A:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:35)

## 10. 验收场景与状态

`AC-SUP-001`：同一工程身份可关联两个工厂不同ERP料号并可追适用域。`AC-SUP-002`：同名但规格不同不能合并。两项设计验收均为`NOT_RUN`，没有本次Host或业务运行证据。[原文S:152](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:152) [原文S:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:159) [原文R:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/bc/bca20bdb63bd1081__RC1-RV01_整包Review与当前定稿结论.md:59)

还应按共同要求验证越权跨租户、未知映射、准确版本变化、重复业务命令等边界；这些是后续实施验证范围，不在此宣称测试通过。[原文S:56](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:56) [原文C:381](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fdab9a3985b3988__5fdab9a3985b3988aaeeb0b1bd3458949f89b472bbcfc05373838e02acebb56b.html:381)

### PG04 工程与经营身份映射验收

以下为共享原Owner动作的开发验收观察点，编号前缀为 `AC/TC-PLM-PG04-A`，均 **NOT_RUN**；假设前置不是现实事实，不改变本册Owner边界。

|断言|必须保持的可观察结果|
|---|---|
|173/174|候选E@1↔SiteA/M1保两端身份和来源；同名不合并，价格库存不复制成第二主账。 [原文PG04:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b0/b066823003eadb18__b066823003eadb1853d50bacba59fb1a42cd7d1968bb3ee48cb80422bd2f088c.md:75)|
|175/176|跨Site M1/M2可分别映射同E@1，但须两端Owner核验规格和范围；同品号不证明通用。 [原文PG04:178](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/b0/b066823003eadb18__b066823003eadb1853d50bacba59fb1a42cd7d1968bb3ee48cb80422bd2f088c.md:178)|



## 11. 具体缺口和退出条件

业务映射的实际Owner wire、历史歧义清理策略、完整物理约束及正式Excel内容一致性尚未由本批证明。退出条件是有权Owner给出准确字段/适用范围和真实确认凭据，并对实际依赖链验证；不能用设计摘要生成批准记录。[原文S:900](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:900) [原文A:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/01/0170b29fed20a30b__APR-CP6-PLM-BASELINE-001.md:33)

共享14目标语义仍在整理，跨册合读未完成不升级`required_materials_consolidated`。

## 12. 来源与真实阅读覆盖

本轮全文实读03_ENG 1–3207及11_SUP 1–938（产品重点67–164）；接受入口37行与RV01结论90行全文。共享HTML契约按原行1–385提取可见正文结构化阅读，未执行JS或确认视觉渲染。正式Excel和完整RC1容器成员未据此计语义全文。完整SHA/未读范围在[evidence](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)，组合见[contracts](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)。[原文S:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/37/374d1b7782cb72d0__11_SUP_当前阅读主册.md:67) [原文E:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1799cd319ae35310__03_ENG_当前阅读主册.md:1)

本轮已补齐该PG04附件的非重复业务语义：新增直接断言实读；原规则/过程/旧场景按已读主册精确文字复用并补读全部未匹配语义。表中取本模块相关动作；TRACE/字段ID/来源定位等仅结构核对，不称逐行独立全文，精确复用与未读行见台账。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
