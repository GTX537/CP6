# W150 工程身份与双Owner准确映射实施计划

COORD本对话单人按brainstorming、writing-plans与executing-plans执行；项目持续授权、一次任务级SPEC/QUALITY、固定席位和精简相关验证优先。原七天CP6-7D-20261022保持ACTIVE。W149已闭合不重复。

## 设计及真实前置

WP05-02/ENG-PROD-01、11_SUP原主册67–164及PG04 A173–176要求稳定工程身份、原ERP经营料号、真实Site、候选与双Owner确认分开。同名/同码不自动合并，不复制ERP价格/库存。WP02-10/13原会话共同事务、原结果/审计已实际采用；S01/S02既有基线有效。WP04-33已有原产品版本来源W144、单位W147、币种W149；本轮与其ERP确认参与者串行同交付，不声称完整卡已完成。

已核源码：没有工程产品Owner；Space_Site确为原物理园区/仓库Site，当前原SpaceMasterService拥有主账，非询盘Site/Form。它无不可变准确版本，故增加原表变动捕获及当前读适配，不重建主表。找不到真实法人Owner，不把tenant或部门当法人；本轮确认只覆盖明确Site身份关系，法人/技术Revision/Configuration及制造采用继续REQUIRED_FENCED。技术身份版次不冒充ENG-REV技术修订。完整原卡与原七天出口不缩减。

方案比较：按ERP代码生成工程身份会混淆原主键；在工程表复制商业主账会形成双Owner；采用独立技术UUID、原ERP/Site准确Ref和双方当前权限共同事务，保留多对多与原历史，因此选第三项。

工程身份Main.Plm.EngineeringProduct.v1保存Tenant+UUID、原代码、类别、名称、技术定义与识别依据；代码/名称可重复，绝不合并。UUID/creator/原代码不可重键，追加不可变原文/摘要/版本、CAS修订和停用。独立plm-engineering-product query/add/edit/confirm，scope1创建者UUID、2/3/4真实部门、5租户；创建部门采用当前授权版本，一次票据与原结果同事务。

映射Main.Plm.EngineeringMapping.v1以独立UUID和工程原UUID为稳定头；草稿保存准确工程版本、可选原ERP/Site引用、用途、依据和未解决冲突，不把后续缺件变成创建技术身份前置。确认独立动作：当前工程confirm与ERP confirm-engineering两项真实权限/对象范围同时参与原Main事务，Site query当前有效；同一操作者持双方权限可办理，原合同未要求两个自然人。保存双方Owner/Actor/权限版本确认事实，不伪造第三方签名。确认仅证明IDENTITY_SITE关系；技术版本、法人和制造许可仍分别后续绑定。

所有确认必须准确工程/ERP/Site版本digest当前且启用/存在、用途和复核依据明确、冲突已解决；无latest/名称降级。源或映射变化、停用、ABA导致新准确采用拒绝，旧记录当前授权仍可读；草稿修订清确认事实，停用保历史。只读CurrentExact参加者要求实际同DbContext事务及存储锁，不把先读检查当提交时许可。原键同体重放、异体冲突、UNKNOWN查询/close挡迟到；UI只保最小公开恢复引用。

## 任务、文件与预计实际耗时

合计90–150分钟：后端/共同事务45–75，页面/定向验证30–50，集中审查/两仓收尾15–25。实际只计Main START到finish，Handoff不叠加。

- [x] `CP6.Tests/Erp/EngineeringProductOwnerPostgreSqlTests.cs`以真实Program/Kestrel `/api/engineering-products/owner-v1/creation-context`缺入口404红灯；扩展真实创建、当前权限、双Owner确认/回滚、两个Site和同名异规格、旧引用/ABA、原键恢复及迁移保存原数据。
- [x] `CP6.Core/Services/Plm/EngineeringProductContractV1.cs`、`EngineeringProductValuesV1.cs`、`EngineeringProductStoreV1.cs`、`EngineeringProductOwnerV1.cs`提供稳定身份/原文/当前来源；`OperationsAccessV1.Engineering.cs`接真实部门。PG迁移`20261026190000_EngineeringProductOwnerV1.cs`保原类型/历史，仅定义菜单能力不授角色。
- [x] `CP6.Core/Services/Space/PhysicalSiteIdentityReaderV1.cs`及迁移`20261026185000_PhysicalSiteIdentityV1.cs`监听原Space_Site，保存真实变动/缺失历史，检查触发器/实际源一致；独立当前space-site query，既有写入口不替换。`EngineeringProductMappingService.cs`与store mapping分部实现草稿/确认/停用/历史/CurrentExact，ERP原Owner新增专门confirm-engineering能力及实际参与者。
- [x] `CP6.WebApi/Controllers/Plm/EngineeringProductController.cs`和mapping controller、Program真实DI；`cp6.web/src/api/plm/engineeringProducts.ts`、auth恢复、`views/plm/EngineeringProductMappingView.vue`、router/IAM准确对象选择。UI明确草稿/冲突/确认范围，身份变化清私有输入，拒绝迟到，UNKNOWN不重发。
- [x] 通过`run_wave150_main.py`执行真实隔离PG18/HTTP相关行为，通过`wave150_run_ui.py`执行受影响组件/恢复/IAM；vue-tsc/Vite一次。实际失败保留并分析；输入未变成功结果引用。一次完整diff SPEC/QUALITY包含权限、事务、版本/源、同名/范围与完整回滚。
- [ ] 单份`docs/development/evidence/W150-01/DELIVERY.md`、台账/四状态/计划/09同步；W149按final proof归档一次，有限WP04-33/WP05-02记录明确非整卡。正常两仓PR/远端准确采用/精确分支删除及官方finish，报告分支/工作区/席位。

无Actions、既有环境迁移/部署、新工作树或源代码副本。后续仍工程修订/冻结、正式WO五域及SCHED、D2/D5/D6/D7全部出口；不以局部夹具关125目标/247运行验收。

## 实际交付

见[单份报告](../../evidence/W150-01/DELIVERY.md)。当前登记36.99分钟；前五步完成，一次集中审查通过。最后生命周期步骤待Main正常PR/finish后由最终proof确认；Handoff已采用释放，原七天ACTIVE。
