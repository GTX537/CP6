# 125目标实现差异与验收入口

本报告逐目标指出已确认差异，供拆分实施任务使用；不声称整模块所有代码均已审计。准确源码行、文件SHA、测试入口和采用边界见[结构化评估](implementation-assessment.json)。所有业务测试NOT_RUN。原清单与强制补充的状态见[来源目录](original-acceptance-catalog.json)。

来源缩写：MAIN `5ca90701bfe1e886ea9e179f1e8e5f413bb44f43`；CRM `c778a3052a4416b82facb07b1244fc98fb6ad8a1`；PLATFORM `30bd23af6808d217a23878bd9437513043c52834`；PLM_CANDIDATE `b7ec269bfc6170f098fa2850361ef26f11a2391e`未合并，不算主线能力。

## CRM-INQ-01 公开询盘与受理

归属WP03；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

已有自动/审核入站、Lead图落库、重放和浏览器回执；但PublicAttempt在提交后才插入，与V15要求GET持久IssuedAttempt及独立ReceiptProof/ConsentFact不等价，回执UI仍读取reference旧字段。

可复用：

- SubmitPublicAsync同外层事务插Submission、可选Lead、Audit/Outbox/Command；旧重放校payload并拒孤立命令重建。
- 现公开表单、受理详情、隐私保留/匿名化底座可适配。

需实现或改造：

- 引入不可变FormBundle、GET发放IssuedAttempt、独立ReceiptProof和ConsentFact，按精确FK/H01校验。
- 采用修订后的严格closed decoder/五字段receipt、配置激活与隐私导出；49状态UI及未知提交恢复、当前权限重新读取。

源码锚点（以下均须按上述固定SHA读取）：

- CRM `src/CP6.CRM.Infrastructure/Leads/CrmBusinessService.Intake.cs:73`：正常入站ConvertToLead，84–97图/效果/回执/命令同transaction；93才添加PublicAttempts。
- CRM `src/CP6.CRM.Api/CrmEndpoints.cs:130`：GET调用form context并发浏览器attempt；当前routes可复用入口但不是V15数据图。
- CRM `apps/web/src/leads/PublicInquiry.tsx:97`：渲染receipt.reference，非新displayReference；当前只展示旧回执视图。

验收盘点`COMPLETE`：原AC/TC 60项，必需补充0项。60原编号完整枚举，并原文保留最终V15三组规范增补；49UI/136fixture步骤为AC引用的设计载体，未冒充运行证据。

## CRM-LEAD-01 线索管理与资格确认

归属WP03；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

手工/网站Lead、分配协作、原生活动和事实更正已实现；现路由没有CP22资格决定与预览/同事务H02转换，旧self-owned mutation不能直接充多Owner协调器。

可复用：

- Lead.CreateManual/CreateWebsite区分归属与接收/分配时钟；原生活动只成功联系计首响。
- CrmBusinessService已持久完整Lead graph、查询权限和当前操作时刻，可作为旧图适配基础。

需实现或改造：

- 增加QualificationDecision/Head、duplicate观察、SLA overlay/horizon、notice与完整审计v4等CP22结构。
- 建立ACC/CON/可选OPP同连接同事务Enlist、ConversionManifest业务唯一槽、actual-final验证；保原60AC身份及CP22替换期望。

源码锚点（以下均须按上述固定SHA读取）：

- CRM `src/CP6.CRM.Domain/Leads/Lead.cs:64`：手工可指派合法creator，网站另有original response anchor。
- CRM `src/CP6.CRM.Domain/Leads/Lead.cs:150`：记录Lead原生活动；这不是ACT的Account/Contact活动。
- CRM `src/CP6.CRM.Api/CrmEndpoints.cs:54`：54–77映射query/create/assignment/activity/disqualify等旧入口，未映射qualification-decisions/conversion-previews/conversions。

验收盘点`COMPLETE`：原AC/TC 60项，必需补充0项。原60及CP22逐项currentRule、currentRefs原文完整绑定；原Saga与hiddenCount不得绕过后继原子Manifest/授权规则。

## CRM-ACT-01 客户互动与销售活动

归属WP03；评估`NEW_REQUIRED`；业务交付`PLANNED`。

现有LeadActivity与映射Activity行不能承担ACT v1.1.1：新模块原生主锚只允许Account/Contact，需完整修订/引用集合、准入证据和工程来源。

可复用：

- 复用现CRM授权/数据库路由/消息设施的宿主接线经验；Lead原生活动继续由Lead拥有。

需实现或改造：

- 新增Account/Contact ActivityHead/Revision/ReferenceSet、事实/内容/引用/VOID命令及当前原锚权限。
- 增加精确File版本与LOCAL_AUTHORITATIVE_GUARD、timeline窗口、工程SourceSnapshot/规范流与原键恢复；在现React详情Drawer实现。

源码锚点（以下均须按上述固定SHA读取）：

- CRM `src/CP6.CRM.Infrastructure/Persistence/Generated/CrmRuntimeRows.g.cs:38`：存在旧Activity映射并非已验证Head/Revision/ReferenceSet用例。
- CRM `src/CP6.CRM.Api/CrmEndpoints.cs:61`：真实写入口受Lead聚合约束，没有通用Account/Contact Activity命令。
- CRM `src/CP6.CRM.Infrastructure/Leads/CrmBusinessService.cs:231`：调用Lead.AddActivity，不能等同ACT Account/Contact命令。

验收盘点`COMPLETE`：原AC/TC 51项，必需补充27项。完整51业务场景＋8 MD＋16 REV1＋3 COORD逐条保留；技术补充不冒增原业务数量。

## CRM-ACC-01 客户关系账户

归属WP03；评估`NEW_REQUIRED`；业务交付`PLANNED`。

旧Account加密资料/owner/BP字段映射存在；固定CRM未见A01–A13账户用例与页面。R1已补齐物理wire，不能继续按旧正文“物理待定”停排期。

可复用：

- 旧Account身份、加密Name/Website、owner/dept及保留字段是迁移输入；不另建重复历史账户。

需实现或改造：

- 实现/api/crm/v2/accounts A01–A13、不可变业务版本/资格、command原键与字段裁剪。
- 独立保存C03历史、七字段映射和ACC-MAPPING-DECISION/1；增加完整scope/Owner选择/Account版当前资格；H02原生Enlist。

源码锚点（以下均须按上述固定SHA读取）：

- CRM `src/CP6.CRM.Infrastructure/Persistence/Generated/CrmRuntimeRows.g.cs:12`：12–35旧Account含BusinessPartnerCd/DeptId/Name/OwnerUserId/PII时钟，不是完整当前映射决定。
- CRM `src/CP6.CRM.Api/CrmEndpoints.cs:54`：已检查CRM应用路由，尚无accounts目标接口。

验收盘点`COMPLETE`：原AC/TC 47项，必需补充0项。Account自身46条及本模块验收节明确引用的跨域C42逐项保留，共47记录；C42采用CR09全文替换，同号不另计新业务AC。

## CRM-CON-01 独立联系人

归属WP03；评估`NEW_REQUIRED`；业务交付`PLANNED`。

旧Contact记录含AccountId，但新设计要求独立联系人、多关系有效期间、主关系集合和渠道用途；现CRM业务入口未实现这些Owner用例。

可复用：

- 旧Contact数据/租户/加密PII与存量Account引用仅可作为有界迁移输入。

需实现或改造：

- 实现独立Contact生命周期/归属/渠道值与策略版本、关系完整集合计划/预览/应用，隐藏成员保留。
- 同Account→Contact锁序并接H02；React列表/详情/关系Drawer、严格ETag/原命令及export协议。

源码锚点（以下均须按上述固定SHA读取）：

- CRM `src/CP6.CRM.Infrastructure/Persistence/Generated/CrmRuntimeRows.g.cs:90`：旧Contact行保AccountId单一引用；尚不能代表多关系/primary interval模型。
- CRM `src/CP6.CRM.Api/CrmEndpoints.cs:54`：已检查公开业务路由表；无contacts/edit-context/relationship-plan新入口。

验收盘点`COMPLETE`：原AC/TC 49项，必需补充26项。49＋20＋6完整原文，C42法定前置和双分支补正全文另保留，重复共同要求不另造编号。

## CRM-OPP-01 商机与销售管道

归属WP03；评估`NEW_REQUIRED`；业务交付`PLANNED`。

有Opportunity存量映射和C03 ErpOrderCreated类型，但该事件只有BookedAmount等字段，没有有效确认、取消份额/责任覆盖；不能从Created推Won。新商机销售与商业投影用例未在当前CRM路由出现。

可复用：

- 保留旧Opportunity身份与C03兼容观察；当前订单事件校验基础可在独立版本适配。

需实现或改造：

- 新增Requirement/Forecast/Stakeholder/Qualification、四轴状态与H02 Origin唯一槽。
- 新HO同事务申请/补件、Intake/Quote/Commercial完整流、经济责任唯一认领/覆盖/纠错；新业务不得复用机会级一单去重。

源码锚点（以下均须按上述固定SHA读取）：

- CRM `src/CP6.CRM.Infrastructure/Persistence/Generated/CrmRuntimeRows.g.cs:306`：存在旧Opportunity映射，不等于新版本化销售/责任组账本。
- CRM `src/CP6.CRM.Infrastructure/Erp/Contracts/ErpEventContracts.cs:62`：Request/ResultVersion为int，BookedAmount+Currency，缺新完整Confirm/份额/原Envelope。
- MAIN `CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs:44`：任意相同CrmOpportunityId订单触发C03_ORDER_ALREADY_EXISTS。

验收盘点`COMPLETE`：原AC/TC 54项，必需补充0项。40原场景及14 REV1完整枚举；同原件两种表格形式均保留完整GWT。

## CRM-HO-01 商业交接与结果跟踪

归属WP03；评估`NEW_REQUIRED`；业务交付`PLANNED`。

固定CRM只有旧C03消息/读客户端设施，未见HO请求主账、自然工程Case/NextContext/原键目录及页面；旧Main一机会一单无法承载新责任组。

可复用：

- 复用现消息/ERP read宿主与旧C03冻结解析；仅作为兼容观察。

需实现或改造：

- 新增HO/OPP同事务H/V与唯一pending、原始密文Envelope/Trust、Delivery租约和WAITING续办。
- 接Main原键完整目录、Intake/商业证据及PLM M02/M03最终执行fence；202仅本地请求，NOT_OBSERVED仍UNKNOWN；建立安全原键查询UI。

源码锚点（以下均须按上述固定SHA读取）：

- CRM `src/CP6.CRM.Infrastructure/Erp/Contracts/ErpEventContracts.cs:62`：旧简单结果运输不能无损代替HO原封闭body/opaque版本。
- CRM `src/CP6.CRM.Api/CrmEndpoints.cs:54`：现公开业务路由表无HO目标命令/查询。
- MAIN `CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs:44`：新来源意图/责任范围唯一必须独立writer适配。

验收盘点`COMPLETE`：原AC/TC 64项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## ERP-BP-01 交易伙伴主数据

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

伙伴CRUD/角色校验及commerce-profile已有实际实现；更新原地覆盖资料，不能等同BP R3不可变ProfileRevision、注册/控制分轴及共同S/X用途门。

可复用：

- 现伙伴完整DTO字段/角色检查、搜索导出与RowVersion更新作为兼容输入；C03工厂已锁伙伴并校账户/资格。

需实现或改造：

- 实现法人scope/不可变版本、状态专门命令、MappingScope/ReverseClaim和完整Account证明。
- 所有旧CRUD/commerce-profile/import与BP UseParticipant按IAM→BP→EST→QTN共同门，业务新采用持S到外层提交。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/BusinessPartnerService.cs:65`：旧资料直接写BusinessPartner并SaveChanges；不是新ProfileRevision/use receipt。
- MAIN `CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs:20`：SqlServer UPDLOCK/HOLDLOCK或Postgres FOR UPDATE后RequireBusinessPartner；是可复用局部保护，不证明全writer门。
- MAIN `cp6.web/src/views/erp/BusinessPartnerView.vue:69`：现有 Vue 业务页及旧 API 接线可复用布局；新状态/原命令恢复/精确字符串值须按本目标重接。

验收盘点`COMPLETE`：原AC/TC 124项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## ERP-EST-01 商业估算与报价计算

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

旧估算确有纸材/工序计算，但Calculate用fallback纸价30、固定20%利润与浮动旧源表；不具备新v2完整来源coverage、批准模型与封存采用。

可复用：

- 已有估算CRUD/复制、材料用量计算和分档金额计算，可保留为legacy观察与确定输入的算法比较基准。

需实现或改造：

- 去除新正式路径的缺价默认与固定利润；实现准确ModelIdentity/FormulaDigest、source coveragePolicy/单位及披露。
- 建立Draft→Calculate→Seal不可变版本、BP/PRICE/EST共同门和QTN I03/I05，同原key严格decoder及UI来源/未知状态。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/EstimateCalcService.cs:239`：241 fallback30，270旧SheetUnitPriceEstimate查询，319–334硬编码舍入/利润；不足新源与模型合同。
- MAIN `cp6.web/src/views/erp/EstimateCalcView.vue:97`：现有 Vue 业务页及旧 API 接线可复用布局；新状态/原命令恢复/精确字符串值须按本目标重接。
- MAIN `CP6.WebApi/Controllers/Erp/EstimateCalcController.cs:14`：当前实际路由为旧合同；不能仅换路径宣称新版本完成。

验收盘点`COMPLETE`：原AC/TC 162项，必需补充8项。110原条、52 GAC、8直接断言共170记录；110逐项原文及GENERAL传播处置均保留。

## ERP-QTN-01 正式报价与客户接受

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

报价CRUD、客户接受时间/reference/content hash已存在，但接受直接改当前Quotation，旧转单未持久QTN R2数量坐标Allocation/OrderIntent/Fact历史同事务草稿。

可复用：

- 旧报价资料/子项、批准后禁止编辑、C03当前内容hash/金额/币种/单位一致性检查。

需实现或改造：

- 实现不可变QuoteVersion/CustomerDecision Fact、准确EST v2采用/披露与当前Fact同步门。
- 按Q23/Q24/I10与Sales Draft同事务坐标分配；Confirm不二次扣量，I11仅精确允许草稿全量释放；补强原键历史及UI独立状态。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/ErpIntegration/ErpCommerceAuthority.cs:62`：72–77原地保存客户接受时间/reference/内容hash。
- MAIN `CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs:35`：校RV/当前接受/金额币种与单位；未等于新坐标Allocation/OrderIntent合同。
- MAIN `cp6.web/src/views/erp/QuotationView.vue:465`：现有 Vue 业务页及旧 API 接线可复用布局；新状态/原命令恢复/精确字符串值须按本目标重接。

验收盘点`COMPLETE`：原AC/TC 218项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## INT-COM-01 CRM与Main商业交接合同

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

旧C03可提供本地事务和原结果恢复骨架；R1商业交接须独立实现，旧工厂仍直接建单，不具新源保护、原体完整性和有界重建。

可复用：

- 旧收件加锁、业务/结果Outbox/processed同提交
- 相同原键回原结果及版本冲突处理

需实现或改造：

- 落实H03/H06/H08精确wire、责任Owner与拆分后的订单/报价事实；decimal字符串和opaque/int64版本不得降成double/int
- 仅允许两类IC持久保护purpose，未知执行不得TTL释放；保存原传输bytes/hash、冻结locator和恢复收据
- 整商机重建冻结完整目录/水位、逐块验真并final CAS；S6 precheckpoint置于重复/旧/同版早返前

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs:48`：Serializable内锁Inbox、原结果早返，业务/响应outbox/processed同事务；旧C03工厂入口仍创建订单（100），版本使用int，不能代替新H03/H06/H08及IC精确原体保护。
- MAIN `CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:85`：Serializable内先RequireActiveTenant（87）再查原operation（92）；同operation能回原结果，重排和审计同提交。新设计要求先按当前读权恢复原operation，再评估新的调度门。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs:132`：自行新建context并开Serializable，Processed重复在161-163提前返回；DbUpdateException重复恢复也会直接Duplicate（94-98）。业务回调前需要新的调用方事务precheckpoint，且早退分支不能绕过。

验收盘点`COMPLETE`：原AC/TC 106项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## ENG-PROD-01 产品身份与基础属性

归属WP05；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Main产品主数据真实存在并服务ERP；需在其上建立工程产品稳定身份/版本及经营映射，不能把ProductCd和当前产品行当已冻结工程输出。

可复用：

- ERP产品基础属性、工序/配方/副产品编辑
- 候选快照可参考来源ID/RV和canonical bytes

需实现或改造：

- 工程身份与经营ProductCd映射可追溯、tenant/法人和单位来源明确；改型/停用不重键历史
- 建立稳定output occurrence、producing operation及配置闭包；原manifest单PRODUCT字节不变，新增sidecar
- 产品维护/查询/where-used/权限页面覆盖；引用缺证不能显示已满足

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/ProductService.cs:167`：创建产品与工序/材料/副产品后保存；更新替换旧子行（485起）。支持现有ERP产品经营信息和数量，缺不可变工程版本/输出出现项关系与历史采用身份。
- MAIN `CP6.Entity/DomainModels/Erp/ProductCoProduct.cs:17`：按ProductCd/ProcessCd/RowNo记录名称、QtyRatio decimal(21,8)及后工序；缺稳定output occurrence/producing operation和配置闭包。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmSnapshotProjector.cs:67`：投影保留来源ID/RV、decimal字段，Schema仅PRODUCT/BOM/ROUTING；路线携带版号字符串。可参考确定性快照方法，不能扩大为全PLM/工具采用。

验收盘点`COMPLETE`：原AC/TC 9项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## ENG-REV-01 工程版本与发布

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

Main未发现工程候选/基线Owner实现；未合入WP0候选具备冻结原型，但仅SQLServer、单产品三类快照，不等于R1工程版本发布全能力。

可复用：

- PLM_CANDIDATE的candidate锁、rowversion、原freeze键和摘要比较可参考
- 既有ERP来源事实

需实现或改造：

- 在Main建立工程候选/版本/验证/冻结/后继与不可变baseline；原命令恢复和来源漂移分别409/412
- 原manifest保兼容；同事务写ENG-OUTPUT-RELATIONS/1和ENG-BASELINE-OUTPUT-BINDING/1及收据/outbox
- 每个真实工具Owner采原生identity/version+HoldExact；H31/H41完整namespace；双provider策略和全部writer授权围栏
- 发布技术状态、审批决定、目标接收、真实采用和实际执行分轴；历史baseline不能后补伪装原证据

源码锚点（以下均须按上述固定SHA读取）：

- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:345`：候选实现用Serializable同连接事务、候选锁与rowversion、原freeze键和请求hash；重读Owner manifest比摘要后冻结baseline。仅未合入候选，不能计入Main能力。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:36`：硬限定SQL Server，并共用SqlConnection与事务；ReadOwnerManifest（198）仅采ProductMaster/ProductMaterials/ProductProcesses，未包含真实工具Owner和多输出关系。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmSnapshotProjector.cs:67`：投影保留来源ID/RV、decimal字段，Schema仅PRODUCT/BOM/ROUTING；路线携带版号字符串。可参考确定性快照方法，不能扩大为全PLM/工具采用。

验收盘点`COMPLETE`：原AC/TC 114项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## ENG-BOM-01 材料配方与BOM

归属WP05；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Main材料行有decimal配方及自然键，适合保留旧生产事实；新工程BOM需要行出现项、有效性、配置关系及按输出闭包，候选仅快照现行材料。

可复用：

- ProductMaterial decimal(21,8)用量及单位
- 候选Bom保留source ID/RV

需实现或改造：

- BOM版本/occurrence及材料替代/生效范围/损耗/计量精度，主数据单位转换来源和批准需明确
- 逐输出输入关系与产出工序闭包；改变输出集合使原校验/冻结预览失效
- 受控变更/验证/发布及where-used；经营BOM适配不得静默覆盖已有执行事实

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Entity/DomainModels/Erp/ProductMaterial.cs:16`：材料以ProductCd/ProcessCd/MaterialCd自然键关联，UnitUsage为decimal(21,8)；可保留现有配方事实，不足以承载工程BOM occurrence、替代/有效性与逐输出关系。
- MAIN `CP6.Core/Services/Erp/ProductService.cs:167`：创建产品与工序/材料/副产品后保存；更新替换旧子行（485起）。支持现有ERP产品经营信息和数量，缺不可变工程版本/输出出现项关系与历史采用身份。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmSnapshotProjector.cs:67`：投影保留来源ID/RV、decimal字段，Schema仅PRODUCT/BOM/ROUTING；路线携带版号字符串。可参考确定性快照方法，不能扩大为全PLM/工具采用。

验收盘点`COMPLETE`：原AC/TC 115项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## ENG-ROU-01 工艺路线

归属WP05；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Main路线行有工序、时间/损耗和版号字符串；需要版本化操作出现项、完整输入/输出关系与真实工具依据，不能以旧PlateNo代表工程工具采用。

可复用：

- 已有加工参数和decimal时间/损耗
- 候选Routing投影来源和schema

需实现或改造：

- 工艺版本/operation occurrence、顺序/依赖/资源/产出及替代工艺规则
- 每个output对应producing operation及输入闭包；参数/计量精度/工序可用性需Owner证据
- 真实工具规格与实例/寿命/有效状态分开，按HoldExact冻结；路线采用不等于MES已执行

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Entity/DomainModels/Erp/ProductProcess.cs:65`：含PlateNo1/2/3标量与工艺参数、时间/损耗decimal(21,8)；这些字符串不是工具Owner的原生身份、版本及HoldExact凭据。
- MAIN `CP6.Entity/DomainModels/Erp/ProductCoProduct.cs:17`：按ProductCd/ProcessCd/RowNo记录名称、QtyRatio decimal(21,8)及后工序；缺稳定output occurrence/producing operation和配置闭包。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmSnapshotProjector.cs:67`：投影保留来源ID/RV、decimal字段，Schema仅PRODUCT/BOM/ROUTING；路线携带版号字符串。可参考确定性快照方法，不能扩大为全PLM/工具采用。

验收盘点`COMPLETE`：原AC/TC 102项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## PLM-REQ-01 工程需求与新品改型受理

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

Main有产品资料而无已定位的工程需求受理、criteria/版本与满足判定Owner；应新建需求闭环，不能用产品CRUD或OA结果替代。

可复用：

- 产品经营事实可只读引用
- OA流程可作为决策载体，应用须新OA-APP合同

需实现或改造：

- 需求来源精确identity/version/digest，受理/去重/变更与criteria的可测单位/比较规则
- 需求到项目/配置/验证证据映射，满足/验证/批准/实际采用四轴独立
- 跨条件影响、撤销/后继、敏感资料和客户来源权限；缺证/unknown不当成满足

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/ProductService.cs:167`：创建产品与工序/材料/副产品后保存；更新替换旧子行（485起）。支持现有ERP产品经营信息和数量，缺不可变工程版本/输出出现项关系与历史采用身份。
- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。

验收盘点`COMPLETE`：原AC/TC 70项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## PLM-ITER-01 工程项目、迭代与方案评审

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

候选只有简化iteration/candidate锚点与技术冻结，不具完整项目/轮次评审与持久OA应用映射；Main没有已合入的对应Owner。

可复用：

- 未合入WP0的候选和冻结身份原型
- OA表单/流程固定版本组件

需实现或改造：

- 项目/迭代/方案/评审轮次和成员全量冻结，aggregate digest独立于review package
- ITER-OA-APPLICATION/1保存scope/Owner/全部候选、roundBindingId及UUID别名映射；browser不能选Owner
- submit同事务写package/aggregate/binding/round/outbox/receipt/audit；Owner应用和withdraw争同control CAS
- 拒绝处置只在本地标识，不扩OA枚举；blocking闭合及当前授权后才能Applied，OA只投影回执

源码锚点（以下均须按上述固定SHA读取）：

- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:345`：候选实现用Serializable同连接事务、候选锁与rowversion、原freeze键和请求hash；重读Owner manifest比摘要后冻结baseline。仅未合入候选，不能计入Main能力。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:36`：硬限定SQL Server，并共用SqlConnection与事务；ReadOwnerManifest（198）仅采ProductMaster/ProductMaterials/ProductProcesses，未包含真实工具Owner和多输出关系。
- MAIN `CP6.Core/Services/Wf/FormSubmissionService.cs:29`：对submissionKey和canonical input hash恢复原结果；formdata与pinned流程启动同事务。最新form在事务前选择，绑定/activation等五项expected上下文与draft竞争仍需新门。

验收盘点`NOT_INVENTORIED`：原AC/TC 99项，必需补充0项。可恢复当前TC/LR/FB/PG已逐项登记；历史指定原件永久无法找回，保留NOT_INVENTORIED，后继不得冒充缺失原AC。

剩余范围：["原文主册明确列72TC，声明30AC对应的历史工作簿/原件已知永久无法找回；保留NOT_INVENTORIED，不用72TC重构不存在的30AC。"]

## PLM-DOC-01 图纸、规格与受控技术文档

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

Main通用附件可存文件，但不具已定位的受控技术文档、内容版本、发放和实际副本Owner；需新建业务层而保留存储接口。

可复用：

- 通用附件存储/关联
- 候选manifest可引用证据身份但未覆盖受控文档

需实现或改造：

- 文档/内容版本/衍生物、技术包成员与工程baseline精确绑定
- 审阅/批准/发放/接收/实际替换副本分轴，下载和失效按当前权限/保密等级
- 内容不可变、引用/hold/撤回和原命令恢复；旧包/副本保留真实历史不覆盖

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pub/AttachmentService.cs:26`：上传先整流读入内存，按MD5复用存储；删除先删关联再检查剩余路径引用；Rebind直接改BizId并保存。可复用文件存储接口，须补当前Owner授权、冻结/hold和可恢复清理。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:345`：候选实现用Serializable同连接事务、候选锁与rowversion、原freeze键和请求hash；重读Owner manifest比摘要后冻结baseline。仅未合入候选，不能计入Main能力。

验收盘点`COMPLETE`：原AC/TC 30项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## PLM-VER-01 方案验证、试制与设计确认

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

OA任务完成和候选VALIDATED状态都不能证明技术验证成立；Main未定位独立验证计划、执行证据和专业结论Owner。

可复用：

- 工程候选快照可作为验证输入
- OA任务字段复算/决策事务可复用基础

需实现或改造：

- 验证计划/项/版本、样品与试制配置精确冻结；实际量、单位、方法及试验源身份
- 专业结论、偏离/条件、整改重验及证据文件完整闭环；任务Done不得自动Closed验证
- 真实试制限额/下游采用与安全约束独立；ITER/OA映射及CP22设计采用不计真实参与完成

源码锚点（以下均须按上述固定SHA读取）：

- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:345`：候选实现用Serializable同连接事务、候选锁与rowversion、原freeze键和请求hash；重读Owner manifest比摘要后冻结baseline。仅未合入候选，不能计入Main能力。
- MAIN `CP6.Core/Services/Oa/TaskDecisionService.cs:29`：拥有事务并重试并发；查pending/running/assignee、代理权限、FormData rowversion和字段可写mask；按pinned表单重算验证后Act。需task+instance双CAS、命令收据和APP决定同事务记录。
- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。

验收盘点`COMPLETE`：原AC/TC 144项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## PLM-CHG-01 工程变更与跨域影响处置

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

现有产品子行直接替换及同步审批回调不提供ECR/ECO/ECN责任与历史生效；应新增变更Owner并约束原有维护入口。

可复用：

- 现有产品/BOM/路线可作为变更影响源
- OA决策引擎和附件作为基础依赖

需实现或改造：

- ECR受理/去重与ECO范围、影响扫描完整水位、跨Owner义务和冻结评审包
- 批准、ECN发送/送达、Owner采纳及实际执行分轴；条件未闭合不得显示已生效
- where-used/在制/库存/采购/销售/服务影响逐Owner真实回执；并发来源漂移和unknown按原键恢复

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/ProductService.cs:167`：创建产品与工序/材料/副产品后保存；更新替换旧子行（485起）。支持现有ERP产品经营信息和数量，缺不可变工程版本/输出出现项关系与历史采用身份。
- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。

验收盘点`COMPLETE`：原AC/TC 92项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## PLM-DEV-01 临时技术偏离与适用控制

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

未定位临时偏离Owner及累计适用/占用账本；现有工程快照和审批回调不提供实际限额保护。

可复用：

- 来源工程baseline的候选实现参考
- 共用可靠收发可扩展为持久业务收据

需实现或改造：

- 偏离scope/version/数量单位/时限/适用对象与冲突检测，审批与执行授权分开
- 真实Owner原键Reservation/Consume/Release和seal，并发累计限额/串行化与withdraw竞争
- 未知不得TTL释放占用；到期/撤销只阻新效果，实际历史不抹除；CP22专业采用与真实参与分别记

源码锚点（以下均须按上述固定SHA读取）：

- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:345`：候选实现用Serializable同连接事务、候选锁与rowversion、原freeze键和请求hash；重读Owner manifest比摘要后冻结baseline。仅未合入候选，不能计入Main能力。
- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs:224`：claim使用条件更新和token可保留；LoadOwnedClaim只匹配id/status/owner/token未核expiry。终态写需当前epoch/nativeRV/未过期fence；Enqueue仅Add不Save（25）可加入Owner事务。

验收盘点`COMPLETE`：原AC/TC 108项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## PLM-REL-01 技术发布、下游接收与采用

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

技术冻结只是发布输入；Main未定位完整技术发布/目标交接/采用Owner，不能将候选FROZEN或OA Approved当下游已生效。

可复用：

- 候选baseline immutable hash与原freeze键
- 可靠消息本地Outbox结构

需实现或改造：

- 发布purpose/许可、精确target/对象集/基线和H31/H41完整namespace
- 目标接收回执、Owner采纳、本地执行证据和条件义务分轴；逐输出relation/binding作为冻结输入
- 试制配额/原始MES intent与全部子执行closure；unknown不新键、撤销不回写已发生事实；CP22真实参与须另验

源码锚点（以下均须按上述固定SHA读取）：

- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:345`：候选实现用Serializable同连接事务、候选锁与rowversion、原freeze键和请求hash；重读Owner manifest比摘要后冻结baseline。仅未合入候选，不能计入Main能力。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:36`：硬限定SQL Server，并共用SqlConnection与事务；ReadOwnerManifest（198）仅采ProductMaster/ProductMaterials/ProductProcesses，未包含真实工具Owner和多输出关系。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs:224`：claim使用条件更新和token可保留；LoadOwnedClaim只匹配id/status/owner/token未核expiry。终态写需当前epoch/nativeRV/未过期fence；Enqueue仅Add不Save（25）可加入Owner事务。

验收盘点`NOT_INVENTORIED`：原AC/TC 122项，必需补充0项。可恢复当前TC/LR/FB/PG已逐项登记；历史指定原件永久无法找回，保留NOT_INVENTORIED，后继不得冒充缺失原AC。

剩余范围：["RC1主册29行明确旧REL原册及旧48AC未恢复，用户已确认原件永久无法找回；后继LR/FB/PG场景绝不倒填旧48AC。"]

## PLM-FBK-01 量产反馈与持续工程改进

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

现有产品资料及OA可提供引用，未定位量产反馈、根因、改进验证的独立业务闭环。

可复用：

- 原经营/工艺事实可只读追溯
- 附件及审批组件可供新Owner使用

需实现或改造：

- 反馈来源真实对象/时间/实际配置、样本覆盖与问题事实，根因假说和证明分开
- 改进项关联变更/验证，观察窗口/指标/对照及专业确认满足后关闭
- 重复/关联问题、敏感源访问、逾期升级和历史结论不可覆盖；无反馈不等于改善已证明

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/ProductService.cs:167`：创建产品与工序/材料/副产品后保存；更新替换旧子行（485起）。支持现有ERP产品经营信息和数量，缺不可变工程版本/输出出现项关系与历史采用身份。
- MAIN `CP6.Entity/DomainModels/Erp/ProductProcess.cs:65`：含PlateNo1/2/3标量与工艺参数、时间/损耗decimal(21,8)；这些字符串不是工具Owner的原生身份、版本及HoldExact凭据。
- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。

验收盘点`COMPLETE`：原AC/TC 52项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## PLM-LIFE-01 技术替代、停用与工程知识追溯

归属WP05；评估`NEW_REQUIRED`；业务交付`PLANNED`。

ERP当前产品行不能表达技术版本替代/停用的完整where-used责任；Main未定位生命周期Owner。

可复用：

- 产品/工艺现行信息和已有引用入口
- 冻结baseline候选作为历史身份参考

需实现或改造：

- 版本化替代/停用/支持政策，精确生效时间/对象范围/单位及可兼容判定
- 完整where-used目录、水位、未证Owner明确标未知；各域执行义务和历史采纳不被改写
- 替代审批/新效应限制/售后支持分轴，条件闭合及原键回查；禁止批量改历史配置冒迁移完成

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/ProductService.cs:167`：创建产品与工序/材料/副产品后保存；更新替换旧子行（485起）。支持现有ERP产品经营信息和数量，缺不可变工程版本/输出出现项关系与历史采用身份。
- PLM_CANDIDATE `CP6.Core/Services/Plm/PlmEngineeringService.cs:345`：候选实现用Serializable同连接事务、候选锁与rowversion、原freeze键和请求hash；重读Owner manifest比摘要后冻结baseline。仅未合入候选，不能计入Main能力。

验收盘点`COMPLETE`：原AC/TC 76项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## ERP-ORD-01 销售订单全生命周期

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现Order默认Confirmed、创建与取消/级联桥已工作；新要求Draft→Confirm→独立需求释放，来源意图/责任唯一与控制/迟到恢复分轴，不能沿现默认状态直接接入。

可复用：

- 订单明细/冻结汇率、取消探测和级联结果、产品映射校验可保旧语义并用于适配测试。

需实现或改造：

- 按R1物理补遗实现Draft生命周期与明确预览计划/原键应用；新来源唯一替代机会级去重。
- Confirm只商业确认，H10/H32释放/控制独立；接真实信用承诺/QTN分配与迟到执行fence、Schedule/ReleaseLineage。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Entity/DomainModels/Erp/Order.cs:108`：默认Confirmed，与新草稿起点不相同。
- MAIN `CP6.Core/Services/Erp/OrderService.cs:407`：先检测发运/探测桥，再force级联并设置Cancelled/PartiallyCancelled；需映射新按范围商业事实/控制证据。
- MAIN `cp6.web/src/views/erp/OrderEntryView.vue:194`：前端只调用信用check并可确认继续；不是最终信用原子承诺。

验收盘点`COMPLETE`：原AC/TC 236项，必需补充0项。R01 36＋R02 122＋R03 70＋R04 8各原号/版本分别枚举，共236记录；不能相加称唯一功能完成率。

## ERP-FUL-01 订单履约投影与交付绩效

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

已有未交/欠交/订单追踪及OTD统计；OTD以已发订单计数及最后发运时间计算，不能替代FUL准确Schedule、发运/交付四指标、数量加权工作分钟和完整cut。

可复用：

- 复用OrderTrace跨WO/Outbound事件导航与OTD分组导出界面；当前统计作为legacy明确标注。

需实现或改造：

- 新增来源exact/discover/interval、稳定Schedule/ReleaseLineage与不可变snapshot，分开发运与交付、原承诺与重承诺。
- 落实F−1/F0母目录及全writer/phantom防护；当前完整不可证返回UNKNOWN/WAITING_POLICY，按数量加权准时与迟延。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/OtdReportService.cs:35`：按ShipStatus>=5取订单，58读lastShipDates，74–83按订单count求rate。
- MAIN `CP6.Core/Services/Erp/OrderTraceService.cs:34`：按订单连接WO、Outbound与IntegrationEvents，只能提供旧追踪。
- MAIN `cp6.web/src/views/erp/OtdReportView.vue:126`：现有 Vue 业务页及旧 API 接线可复用布局；新状态/原命令恢复/精确字符串值须按本目标重接。

验收盘点`COMPLETE`：原AC/TC 68项，必需补充0项。原56表项＋后继12逐条句号前短项；R2/R3不加号的强制补正全文保留，未把静态检查冒充业务运行。

## PLAN-DEM-01 独立需求与版本变更

归属WP06；评估`NEW_REQUIRED`；业务交付`PLANNED`。

当前MRP直接扫描未发订单或接收临时MrpDemand，不是H10正式释放的Demand版本/控制接收。

可复用：

- 可保留剩余订单量查询及Pegging来源为旧兼容读。

需实现或改造：

- 新增ORD-DEM-SOURCE/1接收、Demand版本/谱系/控制与回执同事务账
- MRP只接当前有效Demand；取消/后继与真实发运份额联动

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.WebApi/Controllers/Plan/MrpController.cs:118`：扫描非取消/已发订单，取Quantity−ShippedQty，未接正式SalesRelease成员。
- MAIN `CP6.Core/Services/Plan/IMrpEngine.cs:6`：仅ItemCd/Qty/日期/SourceRefNo，缺稳定Demand版本和控制。
- MAIN `CP6.WebApi/Controllers/Plan/MrpController.cs:33`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 236项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PLAN-ATP-01 交期承诺查询

归属WP06；评估`NEW_REQUIRED`；业务交付`PLANNED`。

所检索生产Plan服务/控制器无R11 ATP Evaluation；四源scalar供给不足五载体和纯查询可重演评估。

可复用：

- SupplyService逐源查询可作为适配原料，不能直接作合格R11快照。

需实现或改造：

- 新增ATP R11评估/恢复/公开投影
- 接POL/SUP准确端口与StableId128、Replay→Evaluation修订；不复用MRP写权限

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Plan/ISupplyService.cs:4`：只有四个decimal总数，无原子谱系/水位/完整性。

验收盘点`COMPLETE`：原AC/TC 50项，必需补充291项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## FIN-CREDIT-01 交易信用检查

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

CreditControl.CheckCredit只读取伙伴CreditLimit和当前未结AR求超额布尔，未形成信用Policy/Scope、曝光全集、Reservation及Sales事务最终消费；UI继续确认不构成承诺。

可复用：

- 旧AR净敞口含贷项反向与冻结FxRate的本位计算可保兼容查询，不能直接作新许可。

需实现或改造：

- 实现完整五维Credit↔GL不可变registry与六字段native Ref绑定，38位总数字/8小数用系数+scale无损存储。
- 新增完整Exposure/cut、policy与Reservation状态、特批准确范围及最终Sales同Uow consume；两80争100必须共享预算，当前UNKNOWN不补0。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/CreditControlService.cs:17`：19–32读单伙伴limit与Posted/PartiallySettled AR，返回Exceeded，无写reservation/consume。
- MAIN `cp6.web/src/views/erp/OrderEntryView.vue:194`：仅预检和可继续弹窗，不能证明服务端原子额度占用。
- MAIN `CP6.WebApi/Controllers/Fin/CreditControlController.cs:12`：当前实际旧路由，非新v1完整封闭合同。

验收盘点`COMPLETE`：原AC/TC 104项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## PLAN-POL-01 计划与供给策略

归属WP06；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

缺政策合成Buy/零提前期，Upsert原地覆值，不足正式版本/生效和未知处理。

可复用：

- 品目策略维护、软删、采购提前期及工艺LeadTime求和已实现。

需实现或改造：

- 新增不可变策略版/范围/生效及正式解析
- 缺配置按设计阻对应动作，保留旧SafetyStock语义供迁移

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Plan/ItemPlanningPolicyService.cs:24`：合成LotForLot/0/Buy默认。
- MAIN `CP6.Core/Services/Plan/ItemPlanningPolicyService.cs:76`：直接覆盖SafetyStock等当前值。
- MAIN `CP6.WebApi/Controllers/Plan/ItemPlanningPolicyController.cs:11`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 136项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PLAN-SUP-01 供给投影与Pegging

归属WP06；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Converted计划全量与在途/WIP/现货直接相加；现货仅排Recall，缺原来源替代/质量与当前cut。

可复用：

- 四类事实读取、未到/未完数量差有实现。

需实现或改造：

- 实现逐来源身份、互斥代表和固定cut快照
- 接WMS质量/预留及真实PO/WO后继；迟到/取消不重复覆盖

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Plan/SupplyService.cs:24`：现货只按品目/删除/Recall筛选。
- MAIN `CP6.Core/Services/Plan/SupplyService.cs:49`：Confirmed/Converted计划按全Qty计入。
- MAIN `CP6.WebApi/Controllers/Plan/MrpController.cs:33`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 136项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PLAN-MRP-01 MRP运算与重算

归属WP06；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

按Item汇到最早日期，开算先软删Suggested，多次Save；缺InputSet/Seal/发布前驱及MAT精确剩余责任。

可复用：

- 低层码/共享材料先汇总净算/用量解析/MOQ规则可复用。

需实现或改造：

- 实现Draft/Capture→InputSet Seal→Attempt→完整发布
- 采用MAT-MRP-RESIDUAL/1：NET不二次抵扣，GROSS扣覆盖并集；取消迟到保当前前驱

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Plan/MrpEngine.cs:53`：开算先删全部Suggested/Pegging再Save。
- MAIN `CP6.Core/Services/Plan/MrpEngine.cs:119`：旧公式Gross−Supply−SafetyStock，无MAT Binding/OffsetManifest。
- MAIN `CP6.WebApi/Controllers/Plan/MrpController.cs:33`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 140项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PLAN-PO-01 计划订单与真实转单

归属WP06；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

当前DI明确注册PR/WO Stub；Convert得到桩单号后标Converted，不是真实CP22三分支转单。

可复用：

- 基础计划订单状态和Pegging传参可迁移，旧STUB须保历史标签。

需实现或改造：

- 接CP22准确PR/WO/Transfer Owner并替换桩
- 新增原意图/预算/全腿Work、原结果与当前效力；未知不得换键

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.WebApi/Program.cs:550`：PR接口注册Stub。
- MAIN `CP6.WebApi/Program.cs:551`：WO接口注册Stub。
- MAIN `CP6.Core/Services/Plan/PlanConvertService.cs:45`：Purchase或其他两个分支，结果后标Converted。

验收盘点`COMPLETE`：原AC/TC 37项，必需补充87项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PLAN-EXC-01 缺料与供给异常处置

归属WP06；评估`NEW_REQUIRED`；业务交付`PLANNED`。

现有MaterialShortage是Open/Resolved/Dismissed登记，不足CP12 Case/Episode、固定影响图、Owner回执和重算协调。

可复用：

- 旧缺料来源/分页/一次状态迁移可保历史。

需实现或改造：

- 新增CP12异常责任图/评估与Owner协调Work
- 采用fixedReadCut和sealed后继图；追加料经MAT/PLAN，关闭核实际回执

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/MaterialShortageService.cs:72`：只调用状态迁移。
- MAIN `CP6.Core/Services/Wms/MaterialShortageService.cs:89`：直接写状态/ResolvedAt，无Owner关闭证明。
- MAIN `CP6.WebApi/Controllers/Wms/MaterialShortageController.cs:9`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 103项，必需补充322项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PUR-PR-01 采购申请与审批

归属WP07；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

审批已有事务/实例约束，但工单缺料PR按数量差和Item去重；转PO未表达CP22来源份额/Claim。

可复用：

- 审批服务端快照、当前实例回调与本地事务；按建议供应商分单已实现。

需实现或改造：

- 接CP22 PR native Root/Share/Assignment及当前来源控制
- 追加料经MAT→PLAN正式责任；残余后继不得Item去重丢失，转PO与Source保护同最终门

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pur/PurchaseRequestService.cs:122`：参与或自建事务，复用审批基础。
- MAIN `CP6.Core/Services/Pur/PrGenerationService.cs:69`：SupplyStatus=3及PlanQty−ActualQty，已请求按ItemId去重。
- MAIN `CP6.Core/Services/Pur/PurchaseRequestService.cs:303`：直接Create PO后回写ConvertedPoNo。

验收盘点`COMPLETE`：原AC/TC 57项，必需补充30项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PUR-RFQ-01 采购询比价与供应价格

归属WP07；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

报价原地覆盖，直接选中写价表/建PO；未表达新修订、Claim及完整商业兼容组。

可复用：

- PR行提取/供应商校验/排名/成交价传递已实现。

需实现或改造：

- 实现RFQ修订/报价原件/Selection与Claim保护
- 准确币种/费用occurrence兼容组转单与原字节Command恢复

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pur/RfqService.cs:212`：原地覆盖QuotedPrice。
- MAIN `CP6.Core/Services/Pur/RfqService.cs:373`：只按SupplierId分组转PO。
- MAIN `CP6.WebApi/Controllers/Pur/RfqController.cs:14`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 248项，必需补充47项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PUR-PO-01 采购订单与履约

归属WP07；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

旧Qty21,8/Price18,4/Amount18,2及两位舍入不同于新合同；Cancel仅改头，缺来源/在途关闭。

可复用：

- 供应商资格、价表、汇率冻结、审批及收票累计基础。

需实现或改造：

- 准确来源组创建/商业修订/发行/确认
- 精度无损迁移与独立取消Work，当前Source/GR/在途共同最终门

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Entity/DomainModels/Pur/PurchaseOrderLine.cs:25`：旧数量/价格/金额列与新合同精度不同。
- MAIN `CP6.Core/Services/Pur/PurchaseOrderService.cs:111`：Net固定两位舍入。
- MAIN `CP6.Core/Services/Pur/PurchaseOrderService.cs:200`：直接Status Cancelled。

验收盘点`COMPLETE`：原AC/TC 124项，必需补充118项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PUR-GR-01 采购收货商业单据

归属WP07；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

缺QC verdict默认全合格，PostingBasis直接决定验收；商业/物理/质量原范围未独立。

可复用：

- 委托WMS物理入库并保ReceiptDetailRef、派生PO状态有实现。

需实现或改造：

- 按原source/part/domain建立收货与质量商业应用
- 禁止缺QC=PASS；合量/份额防重复，GR/PO/Stock完整ResourcePlan及原键恢复

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pur/GoodsReceiptService.cs:108`：调用WMS后保存引用并累计PO。
- MAIN `CP6.Core/Services/Pur/GoodsReceiptService.cs:154`：无verdict则Accepted=Received、PASS。
- MAIN `CP6.WebApi/Controllers/Pur/GoodsReceiptController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 111项，必需补充88项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PUR-MATCH-01 供应商发票匹配与采购对账

归属WP07；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

以PO累计余额匹配，人工Release直接AP；缺GR原域/原义务预算与纠正全腿关闭。

可复用：

- 容差、数量/价格差分开，AP失败挂起及成功累计基础。

需实现或改造：

- 原GR区间/OriginalObligation保护与AP同最终门
- 接MRX/RETURN/CorrectionAdmission close/query/receipt，保UNKNOWN；账期取真实政策

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pur/ThreeWayMatchService.cs:84`：余额Accepted−Invoiced。
- MAIN `CP6.Core/Services/Pur/ThreeWayMatchService.cs:163`：直接BuildAp，没有该方法内重验当前来源区间/余额。
- MAIN `CP6.Core/Services/Pur/ThreeWayMatchService.cs:203`：固定30天。

验收盘点`COMPLETE`：原AC/TC 139项，必需补充53项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## PUR-RETURN-01 采购退货与供应商结算调整

归属WP07；评估`NEW_REQUIRED`；业务交付`PLANNED`。

Pur目录未见独立退货Case/Part/Settlement合同；WMS SupplierReturn负ADJ不是采购商业退货。

可复用：

- GR/PO明细及旧RMA流水可做受控历史来源。

需实现或改造：

- 新增采购退货Origin/Case/Part及递归终叶current cut
- 接供应商授权、GR域、库存实退及财务原义务预算，取消Fence不VOID原GR

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/RmaService.cs:211`：Scrap/SupplierReturn共用负ADJ旧库存分支。

验收盘点`COMPLETE`：原AC/TC 168项，必需补充474项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## MES-RES-01 工作中心与设备资源

归属WP08；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

机器状态/停机直接更新当前行，缺RES token/效力区间/完整资源门及A/B事实处理。

可复用：

- 机器/WC CRUD、停机起止、在制关联/通知基础。

需实现或改造：

- 资源版本/归属/效力与来源冲突事实
- RES-WRITE-1、预检及最终门；真实槽供SCHED/PERF

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/MachineService.cs:144`：直接赋Status保存。
- MAIN `CP6.Core/Services/Mes/MachineService.cs:246`：直接闭停机并恢复机器状态。
- MAIN `CP6.WebApi/Controllers/Mes/MachineController.cs:11`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 205项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## MES-WO-01 生产工单与制造快照

归属WP08；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

DTO可赋工单状态且更新全删重建子表，不能保Root/Snapshot/Occurrence和技术/取消谱系。

可复用：

- 头/工序/材料创建在本地事务，基础状态/进度查询可保。

需实现或改造：

- 固定工程用途快照/RootWO/工序材料责任
- 下达/取消按REL/QAP/PLAN当前资格、实际与未开始完整份额执行

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/WorkOrderService.cs:146`：本地事务创建但状态来自DTO。
- MAIN `CP6.Core/Services/Mes/WorkOrderService.cs:255`：全删重插工序/材料子表。
- MAIN `CP6.WebApi/Controllers/Mes/WorkOrderController.cs:13`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 115项，必需补充6项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## MES-MAT-01 生产领退料与消耗

归属WP08；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

反冲读当前BOM、MaterialCd幂等且允许负库存；Stock提交后写ActualQty，自动PR绕过正式PLAN责任。

可复用：

- BomUsageResolver与逐料失败保留/重试、缺料通知基础。

需实现或改造：

- 冻结MaterialBinding与领退耗/反冲同QuantityUoW
- 追加料MAT责任后继→PLAN剩余重算→PUR/WMS→原回执更新计划；NET/GROSS抵一次
- PENDING_BASIS_SUCCESSION无保护空窗、全attempt和迟到事实

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/BackflushService.cs:54`：当前ProductMaterial读取。
- MAIN `CP6.Core/Services/Mes/BackflushService.cs:85`：已耗幂等按ProductCd，非Occurrence。
- MAIN `CP6.Core/Services/Mes/BackflushService.cs:132`：AllowNegativeOverride=true。

验收盘点`COMPLETE`：原AC/TC 106项，必需补充54项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## MES-EXEC-01 工序执行与报工

归属WP08；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Report/Complete将各工序良品累计WO总量，commit后才调用WMS/反冲/Finance，缺固定执行Basis与measurement budget。

可复用：

- Start/Suspend/Resume/Complete检查及本域事务/时间派生。

需实现或改造：

- Occurrence/执行代次报工与measurement budget
- MAT Basis后继、OUTPUT allowance及最终授权；副作用持久Work独立恢复

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/ProductionResultService.cs:213`：GoodQty累加WO.CompletedQty。
- MAIN `CP6.Core/Services/Mes/ProductionResultService.cs:250`：先Save/Commit，后WMS/成本链。
- MAIN `CP6.WebApi/Controllers/Mes/ProductionResultController.cs:13`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 94项，必需补充135项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## MES-OUTPUT-01 生产产出与关闭

归属WP08；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

整WO累计GoodQty直接入库，缺最终输出谱系/份额、质量义务与缩量Fence。

可复用：

- INBOUND-FG与采购暂估分责已有实现。

需实现或改造：

- 最终输出谱系/measurement domain/allowance及独立入库回执
- 缩量更正先闭所有Stock执行/aliases；质量与物理分轴

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/ProductionResultService.cs:266`：将wo.CompletedQty交完成回调。
- MAIN `CP6.Core/Services/Wms/InboundService.cs:426`：工单goodQty直接成品入库。
- MAIN `CP6.Core/Services/Wms/InboundService.cs:354`：INBOUND-FG避免采购GRNI双计。

验收盘点`COMPLETE`：原AC/TC 109项，必需补充205项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## FIN-COST-01 制造成本归集与结转

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

成本归集会取同工序/物料首个SupplyPrice并缺价0；结转total<=0直接Settled，不能表示新完整来源证据/合法零/NA、完整分腿完成及真实应用状态。

可复用：

- 工作单材料/工时/制造费用归集、标准/实际差异计算及成本表查询可用作旧行为输入。

需实现或改造：

- 实现完整Source/Basis/Rate版本、分配/结转Plan、每个输出allocation与合法零/NA证明，禁止缺价0和首价选择。
- GL原source envelope/全部前置资源闭包与FM/BP sameUow，逐腿Posted→CostResult独立Apply；GL CompletionCurrent保护与reverse writer共同门，委外成本按真实累计RECEIPT Q。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/CostCollectService.cs:48`：ProductMaterial group.First().SupplyPrice??0，缺key也0；新准确来源/多匹配不可照用。
- MAIN `CP6.Core/Services/Fin/CostSettleService.cs:26`：total<=0直接Settled并SaveChanges，未区分missing/合法zero/no-accounting-effect。
- MAIN `cp6.web/src/views/fin/CostSheetView.vue:134`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 76项，必需补充13项。76原AC＋13必需连续/失败/恢复场景逐项保留；22前驱wire调用单列设计依赖。重复物理载荷用准确JSON Pointer/哈希引用，保留合法零、跨期、64+1同事务及当前门断言。

## QA-POL-01 检验策略与检验计划

归属WP09；评估`NEW_REQUIRED`；业务交付`PLANNED`。

InspectionTemplate与AutoJudge仅模板/限值，不能提供版本化政策/Plan、独立批准与当前质量资格。

可复用：

- 模板/限值及未完整测量Hold可迁历史，不迁为已批准政策。

需实现或改造：

- Policy/Plan版本/解析/批准与豁免证据
- 采购source/part与制造总体分契约；全部Stock使用最终QAP当前门

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/QualityInspectionService.cs:300`：只读模板列表。
- MAIN `CP6.Core/Services/Mes/QualityInspectionService.cs:359`：消费传入限值/手判，不是受控政策解析。
- MAIN `CP6.WebApi/Controllers/Mes/QualityInspectionController.cs:13`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 116项，必需补充97项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## QA-IQC-01 来料检验与验收处置

归属WP09；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

ExpectedQty当ReceivedQty，Judge PASS自动再收货；新IQC必须取真实已收源且不再正向入库。

可复用：

- 现有收/合/拒/Pending字段与检验界面可迁。

需实现或改造：

- 真实GR source/part总体唯一义务和测量slot
- 禁止判定再收货；专业/GR/Stock/普通可用四轴及R1测量证据闭包

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/QcInspectionService.cs:105`：Received默认Expected。
- MAIN `CP6.Core/Services/Wms/QcInspectionService.cs:198`：PASS自动InboundReceipt/库存。
- MAIN `CP6.WebApi/Controllers/Wms/QcInspectionController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 107项，必需补充21项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## QA-IPQC-01 过程与最终检验

归属WP09；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

FAIL自动Defect、QI commit后best-effort写Stock Failed；缺任务族当前barrier与准确产出总体。

可复用：

- 限值判断、Hold、检验项保存和失败库存关联基础。

需实现或改造：

- EXEC/OUTPUT真实总体/固定Plan与测量义务
- 独立审核/处置/质量应用和Stock共同最终门，后置失败原键恢复

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/QualityInspectionService.cs:200`：FAIL自动DefectRecord。
- MAIN `CP6.Core/Services/Mes/QualityInspectionService.cs:231`：先commit，后TryMarkLinkedStockFailed失败只日志。
- MAIN `CP6.WebApi/Controllers/Mes/QualityInspectionController.cs:13`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 50项，必需补充155项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## QA-NCR-01 不合格、返工与报废处置

归属WP09；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Defect Update直接Status3设完成时间，不足R12独立批准/专业履行与全义务关闭。

可复用：

- 不良分类、工单/检验来源、数量/处理说明和通知可迁。

需实现或改造：

- NCR R12原件/处置/独立批准及Owner Work
- 返工/报废/原样/退回/工程分责，关闭核全部义务与Finance当前世代proof
- H30 RETURN_TO_SOURCE保持PENDING/H30_QA_RETURN_SEMANTIC_UNMAPPED，不映为HOLD或擅扩Finance枚举

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/DefectRecordService.cs:108`：直接赋状态/字段。
- MAIN `CP6.Core/Services/Mes/DefectRecordService.cs:132`：Status3直接CompletedDate。
- MAIN `CP6.WebApi/Controllers/Mes/DefectRecordController.cs:13`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 152项，必需补充183项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-MASTER-01 仓库与作业库位

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现有Warehouse/Location CRUD及WmsBin停用仅检查实存；无法表达初始化隔离、全Owner保护和完整位置资源生命周期。

可复用：

- 仓/位置层级CRUD及库存非零禁止删除；WmsBin版本不回退和缺行停用墓碑已实现。

需实现或改造：

- 建立正式Location身份/初始化隔离与typed capacity规则
- 停止/恢复/退役收齐OwnerSet及当前保护、预留/任务/子节点闭包；旧writer尊重初始化Fence

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.WebApi/Controllers/Wms/WarehouseController.cs:123`：直接接受Location实体写入，未执行新DRAFT/QUARANTINED初始化协议。
- MAIN `CP6.WebApi/Controllers/Wms/WarehouseController.cs:184`：仅查现存Stock.PhysicalQty。
- MAIN `CP6.Core/Services/Wms/WmsBinDeactivator.cs:30`：库存Sum>0拒绝，否则停用或写墓碑；未代表全部Owner关闭。

验收盘点`COMPLETE`：原AC/TC 235项，必需补充1项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-STOCK-01 库存数量账与移动

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Stock只按仓/位/产品/批次取行，数量为Physical−Allocated；OUT/UNRSV Math.Max截占用，不是Claim/Binding、限制并集和真实来源数量内核。

可复用：

- decimal21,8数量、流水与余额本地事务、ambient事务参与、MOVE两腿原子及基础盘点冻结已实现。

需实现或改造：

- 实现Dimension/Pool/Root与SourceOccurrence、Claim/Binding单数量权威
- 所有writer采用共同ResourcePlan/Source→Scope→细资源最终门，准确Owner/QA/容量/用途与原键回执
- 物理消费只耗本Claim；释放/更正/取消保独立原结果和未明执行，不截断差异

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/StockMovementService.cs:77`：查行键缺货主/技术/用途/单位维度。
- MAIN `CP6.Core/Services/Wms/StockMovementService.cs:68`：检测ambient事务并参与，不应误称每次必独立commit。
- MAIN `CP6.Core/Services/Wms/StockMovementService.cs:339`：OUT以Math.Max削减聚合AllocatedQty。

验收盘点`COMPLETE`：原AC/TC 157项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-TRANSFER-01 移库与调拨执行

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

旧Move只支持同仓两位置/品批/量，无设计的Request/Stage、TRANSIT和SourceDemand预算；已有原子移动可复用但不是完整调拨。

可复用：

- Stock.Move两腿共同事务，ambient不自commit，成功后通知；移动任务可关联原流水。

需实现或改造：

- 新增Transfer Request/Stage/三个坐标与来源预算
- 接RSV全部Use、Stock共同容量/质量门、原prepare查询/永久关闭及在途接收；MAT领退料只作MatLink

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/StockMovementService.cs:185`：单Warehouse+From/To位置接口。
- MAIN `CP6.Core/Services/Wms/StockMovementService.cs:239`：OUT/IN共同事务，有实际复用价值但未含新Source/Claim阶段。
- MAIN `CP6.WebApi/Controllers/Wms/MobileTasksV2Controller.cs:9`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 112项，必需补充755项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-RSV-01 库存预留与批次分配

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现有Allocate按单条库存足量找货，直接RSV聚合量；Cancel按Allocated−Shipped释放，无Claim/Demand区间、全部Use或NoFurtherUse。

可复用：

- 真实分配仓/位/批保存、缺料登记与避免SHIP再UNRSV可复用为旧读。

需实现或改造：

- 新增ReservationRequest/Claim/DemandMap与BACK/ALLOCATE分离
- 全候选/路由与来源区间预算，同QuantityUoW；释放前全部Use Fence/NoFurtherUse，过期不自动清占用

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/OutboundService.cs:347`：候选要求单行AvailableQty>=needed。
- MAIN `CP6.Core/Services/Wms/OutboundService.cs:440`：整个needed发送RSV，后累计明细Allocated。
- MAIN `CP6.Core/Services/Wms/OutboundService.cs:275`：按Allocated−Shipped直接UNRSV。

验收盘点`COMPLETE`：原AC/TC 109项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-IN-01 入库计划与物理收货

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

ConfirmReceipt按行调用Stock再落detail/累计，服务本身无整来源最终协议；整WO去重阻分批，与新Root/Grant/原结果恢复不符。

可复用：

- 保存StockTxnNo、关联入库预定累计，INBOUND-FG区分制造与采购。

需实现或改造：

- 新增IN请求根/不可变Intent/Plan/OwnerCall与原Grant
- 来源/Stock/GR/PO/OUTPUT同资源最终门，多行共同成功或原结果恢复，分批与取消后迟到保真

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/InboundService.cs:340`：逐行Apply，detail在调用后添加，本服务未建立新GR/PO/Stock共同门。
- MAIN `CP6.Core/Services/Wms/InboundService.cs:426`：按整个WO自动成品入库，需要按新OUTPUT份额适配。
- MAIN `CP6.WebApi/Controllers/Wms/InboundReceiptController.cs:12`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 94项，必需补充115项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-COUNT-01 库存盘点与调整

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

AwaitingApproval仍能改CountedQty；Approve逐行ADJ再结header，无冻结Review整组原子及零差异永久关闭。

可复用：

- 未盘行检查、差异原因、基础盘点冻结和ADJ流水关联已实现。

需实现或改造：

- 版本化盲盘/轮次/观察与完整冻结快照及Review
- 全部writer共同Freeze、审批当前门和整组ADJ/零差异关闭；未知估值不作0，保旧部分成功

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/StockTakeService.cs:183`：已送审也允许改观察。
- MAIN `CP6.Core/Services/Wms/StockTakeService.cs:237`：缺DiffAmount按0参与阈值。
- MAIN `CP6.Core/Services/Wms/StockTakeService.cs:274`：逐行Stock.Apply，最后header完成；不能据注释认定新整组协议。

验收盘点`COMPLETE`：原AC/TC 22项，必需补充3项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-TRACE-01 批次、序列与物流单元

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Recall只改现有Stock行，余额0/未来IN无lot域永久限制；LPN已有事务/树，但回放只比操作号/键/动作，不含新完整body及成员快照。

可复用：

- LPN闭包树、整树移动/拆并、序列账/回执及本地事务修复可保；不是空白实现。

需实现或改造：

- lot召回Case覆盖未来入库并按原因独立解除
- TRACE/LPN/Pallet准确业务身份/成员快照/同UoW当前门与body绑定回放，保原树整体效果

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/LotTraceService.cs:42`：仅更新当前匹配Stock.RecallFlag。
- MAIN `CP6.Core/Services/Wms/LpnService.cs:747`：只核OperationId+key+command返回旧结果。
- MAIN `CP6.Core/Services/Wms/LpnService.cs:289`：整树内调用Stock.Move，已有共同事务基础。

验收盘点`COMPLETE`：原AC/TC 20项，必需补充8项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-DISP-01 过期、损坏与库存处置

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Expiry Dispose直接取选中Stock当前全量负ADJ，未冻结准确Review/批准/处置份额与Finance回执。

可复用：

- 到期查询及库存负ADJ流水可作历史观察/实际事实。

需实现或改造：

- 新增处置Basis/Review/独立批准与准确Part
- 接NCR/Stock真实处置Owner、共同最终门与Finance原生结果恢复，旧EXPIRY_DISPOSE不冒已过账

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/ExpiryService.cs:50`：StockIds选中当前正实存直接处置。
- MAIN `CP6.Core/Services/Wms/ExpiryService.cs:69`：Qty=-PhysicalQty全量ADJ。
- MAIN `CP6.WebApi/Controllers/Wms/ExpiryController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 21项，必需补充6项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-MOBILE-01 仓库任务、扫码与标签

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

V2有事务/执行状态/部分余量，但同operationId仅比较task+command即回放，不能拒同键异body；新多kind来源闭合/UNKNOWN仍须适配。

可复用：

- Move完成在事务内写两腿/回执/唯一余量任务；device/rowVersion/扫描/作用域检查已实现。

需实现或改造：

- S1-WMS-MOBILE-3统一信封/命令指纹及来源业务根
- 多kind源action/scan profile、当前资格与全Owner效果；UNKNOWN先查原键，取消/接管不释放未明Claim

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/MobileTaskV2Service.cs:775`：先按原操作回查后继续device等。
- MAIN `CP6.Core/Services/Wms/MobileTaskV2Service.cs:805`：Move和task在同事务，保复用。
- MAIN `CP6.Core/Services/Wms/MobileTaskV2Service.cs:1379`：仅核TaskNo/CommandName，未比本次完整body。

验收盘点`COMPLETE`：原AC/TC 27项，必需补充5项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-CTRL-01 仓库权限与功能变更控制

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

角色Replace删旧加新无业务CAS版本；Flag Submit同OperationId直接回旧行不比body；已有异人/版本检查不足完整DRAIN/all-writer最终门。

可复用：

- role1不可改、规范化作用域、审批者隔离和base RowVersion过期拒绝已实现。

需实现或改造：

- 版本化scope完整before∪after授权/CAS/回执
- 冻结Flag意图/指纹、准确OA决定与真实DRAIN gate/完整open集合；最终撤权/前提共同门

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/WmsRoleScopeService.cs:47`：RemoveRange+AddRange替换，没有请求expected scope revision。
- MAIN `CP6.Core/Services/Wms/WmsFeatureFlagChangeService.cs:40`：同OperationId存在直接Map返回。
- MAIN `CP6.Core/Services/Wms/WmsFeatureFlagChangeService.cs:162`：已有异人批准及后续base版本核验，保留。

验收盘点`COMPLETE`：原AC/TC 23项，必需补充5项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## ERP-CREDIT-01 销售贷项与售后商业处理

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现CreditNote查询与RMA结果链是简化贷项观察；没有新CommercialWork决策/分区额度/现金退款与新Demand独立Owner编排等价能力。

可复用：

- 贷项分页、客户/订单/RMA引用和现有RMA贷项回归入口可保留历史观察。

需实现或改造：

- 新增29当前版本接口、CommercialWork/Decision/claims/OwnerPlan与稳定Command/checkpoint；采用current-v1错误和Money修订。
- 信用、实际退款、库存价值/COGS/GL、替换需求各自真实来源与终态；NOT_OBSERVED保UNKNOWN和占用，不凭退款提议造实际效果。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/CreditNoteService.cs:18`：只读CreditNote并投影Qty/Amount/RMA，未实现新商业决策和跨Owner恢复。
- MAIN `CP6.Entity/DomainModels/Erp/CreditNote.cs:7`：旧单行摘要实体作为legacy来源，不是57表目标账本。
- MAIN `cp6.web/src/views/erp/CreditNoteListView.vue:40`：现有 Vue 业务页及旧 API 接线可复用布局；新状态/原命令恢复/精确字符串值须按本目标重接。

验收盘点`COMPLETE`：原AC/TC 46项，必需补充48项。原46 GWT与接受目录及逐条设计映射完全一致；全部原API/Data标签逐项映射current registry（A10-CREDIT明确移除）。34恢复分支及原件全核，57来源表逐个绑定current100，14实际阶段切面逐项展开。Stage100接受优先级与运行UNPROVEN分开。

## WMS-OUT-01 出库、拣配、包装与发运

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Ship只按Allocated−Shipped循环OUT，取消直接UNRSV/改头；没有新Allocation/Pick/Stage/Shipment原源份额与全部在途Fence。

可复用：

- 已保存分配仓位/批，OUT同时耗占用避免重复UNRSV，发货/包装/Finance桥有入口。

需实现或改造：

- 新来源授权/RSV Allocation→Pick→Stage→Shipment链
- 真实发运/包装与商业/交付分轴，取消完整剩余scope/全部Use与迟到事实；每个Stock最终动作共同门

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/OutboundService.cs:493`：发运量取Allocated−Shipped。
- MAIN `CP6.Core/Services/Wms/OutboundService.cs:505`：逐行Stock OUT，未传Claim/SourceOccurrence。
- MAIN `CP6.Core/Services/Wms/OutboundService.cs:275`：直接释放计算余量。

验收盘点`COMPLETE`：原AC/TC 136项，必需补充706项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-DEL-01 承运与交付证明

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Carrier自动随机tracking、Delivered只改头；无法证明真实发运行/签收份额/POD与原Owner应用。

可复用：

- 承运观察事件和基本状态可保为来源未证历史，不能升级交付事实。

需实现或改造：

- 建立真实Shipment行/承运Attempt/逐行Delivery及POD
- 当前来源cut/F0资源门、独立更正和ERP/Finance回执恢复；保丢件/部分签收与原发运

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/CarrierService.cs:43`：无tracking调用生成随机号。
- MAIN `CP6.Core/Services/Wms/CarrierService.cs:100`：只变头Delivered/时间。
- MAIN `CP6.WebApi/Controllers/Wms/CarrierController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 71项，必需补充149项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-RMA-01 客户退货实物处理

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Create即Authorized，Judge直接移动/负ADJ，Close/Cancel只改头；缺原发运行可退预算与各专业效果/Finance排他证据。

可复用：

- 申请/接收/检验状态、库存流水和ERP回调入口可迁历史。

需实现或改造：

- 原发运行级退货授权/分批收货/专业处置编排
- 采用R1 ResourcePlan与exclusive H30 proof、准确Scope/credit/refund分轴；取消先闭全部未明Work

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/RmaService.cs:92`：申请即Authorized。
- MAIN `CP6.Core/Services/Wms/RmaService.cs:211`：Scrap/SupplierReturn直接Stock负ADJ。
- MAIN `CP6.Core/Services/Wms/RmaService.cs:268`：已非Closed可直接Cancelled，无全Work关闭。

验收盘点`COMPLETE`：原AC/TC 52项，必需补充51项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## FIN-MASTER-01 财务基础资料

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现科目CRUD/停用/template可用，但Update原地改名称/类型/父节点/Role/币种，未形成新Master根/版本/控制/发布链或FM15同Uow消费凭据。

可复用：

- 科目代码唯一检查、编码/方案只读、template导入和现有基础主数据实体可用于历史输入。

需实现或改造：

- 建立GL/TAX/BANK/CC统一五维scope、不可变内容/控制版本、review/publish/revoke、银行秘密与字段披露。
- 实现FM15真实进程内Enlist/UseIntent/Proof/Receipt/Finalize，S/X scope覆盖旧CRUD/template/import与消费者；存量只作LegacyObservation不补签历史。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/GlAccountService.cs:34`：Code必填及唯一检查后持久新科目。
- MAIN `CP6.Core/Services/Fin/GlAccountService.cs:49`：直接覆盖旧记录Name/Type/Parent/Role/Currency等，未形成不可变后继。
- MAIN `cp6.web/src/views/fin/GlAccountView.vue:103`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 156项，必需补充239项。156 AC加附录C全部239具名请求/预期输出逐条目录化（共395来源记录），并保留R2/R3确切补正；引用390原件可按本来源回读，静态计数不当业务PASS。

## FIN-GL-01 会计事件与凭证过账

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现手工过账有maker-checker、锁期/银行/预算检查；自动过账直接系统身份写Posted且没有BudgetGuard，尚无新Source/Event/Leg、审批独立应用和FM逐批同Uow采用。

可复用：

- 借贷校验、手工与自动入口区分、期间/年度锁、银行及手工预算校验、旧凭证查询与逆向基础。

需实现或改造：

- 新增封闭来源source/event/leg、immutable revision/PostedFact/Completion、OA结果Inbox与独立ApplyReceipt；命令原结果及未知恢复。
- 统一自动/正常/反向/结账调整的当前Policy/Period/Master/BankBudget门与全writer；64+1 FM批同外层commit，冲销保原历史而分current。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/JournalEntryService.cs:73`：校PendingReview、maker/checker、year/period及BankReconGuard和BudgetGuard后写Posted。
- MAIN `CP6.Core/Services/Fin/JournalEntryService.cs:99`：自动EnsureOpen/Validate/BankReconGuard后写Posted，119–123 maker/checker均SystemUser；该路径无BudgetGuard调用。
- MAIN `cp6.web/src/views/fin/JournalEntryView.vue:131`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 79项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## FIN-CLOSE-01 财务期间与关闭

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现Close预检后折旧/重估、将period Closed并自动开下期；Reopen直接状态改Open。新设计要求独立close run/checklist/approval/最终fence与原期履行，不得为了调整把关期当开放期。

可复用：

- 已有预结账检查/试算、折旧和FX钩子、月结/年度结转与权限端点可作为legacy依赖清单。

需实现或改造：

- 建立CloseRun准确Owner/checklist cut、完整readiness/SOD/审批与最终持门，区分原期履行/forward current/lifecycle。
- 反结账和close-adjustment走精确专业授权/目标期/原结果，不默认开下期；partial/镜像/OwnerApply未知续原计划。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/PeriodCloseService.cs:72`：先PreClose+折旧/重估，93置Closed，101 EnsureOpen下一期。
- MAIN `CP6.Core/Services/Fin/PeriodCloseService.cs:105`：111直接Open并清ClosedAt/By；不代表新独立恢复/审批协议。
- MAIN `cp6.web/src/views/fin/PeriodCloseView.vue:50`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 28项，必需补充0项。S2 R1准确目标表完整枚举；期后R1补正原文共同保留。

## FIN-REPORT-01 试算与财务报表

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现TB实时读取JournalStatus.Posted并用当前科目解释，原journal改Reversed后不在查询中；新要求完整正反历史、GL-only/manual、稳定knowledge cut、不可变报表定义与snapshot。

可复用：

- 试算opening+movement、借贷平衡和现BS/PNL展示/计算可用于对照数据；不能当正式新snapshot。

需实现或改造：

- 建立准确ledger cut/完整范围证明及历史role/account version，含原及镜像effectiveDate、opening；同native snapshot才可声明strict。
- 实现定义/mapping release、Run/snapshot/当前披露、ORIGINAL_KNOWN vs RESTATED与批准差异；部分权只能partial view，页面保金额精确字符串。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/TrialBalanceService.cs:23`：29–33筛当前Posted；39–45开期累加并读取当前GlAccount字典，没有不可变cut。
- MAIN `cp6.web/src/views/fin/TrialBalanceView.vue:52`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。
- MAIN `CP6.WebApi/Controllers/Fin/ReportController.cs:12`：当前实际旧路由，非新v1完整封闭合同。

验收盘点`COMPLETE`：原AC/TC 26项，必需补充0项。S2 R1准确目标表完整枚举；期后R1补正原文共同保留。

## FIN-AP-01 应付、付款与核销

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

旧应付开票/过账与付款核销已实现；核销逐票校验后可逐票AutoPost差额再改余额，不是新整批reservation/完整FinancePlan/独立GL腿至OwnerApply恢复协议。

可复用：

- 供应商一致、已过账状态、支付可用与票余额校验，折扣/已实现汇差计算及旧AP来源生成。

需实现或改造：

- 新增OriginalObligation共享预算、MATCH/RETURN/manual准确source union、完整批reservation/plan/work与所有票付款一次最终Apply。
- GL已成功本地失败保bridge+占额，补原Apply；A28/A29与真实现金退款分轴；实现R1 CorrectionAdmission close/fence/SEALED_UNPOSTED独立终态。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/ApInvoiceService.cs:96`：生成AP event后以SourceDocNo寻找Posted journal，回写invoice status；非新准确result/ref/多腿Manifest。
- MAIN `CP6.Core/Services/Fin/ApSettlementService.cs:24`：30–48余额检查；37开始逐票，96 AutoPost，101后直接累加SettledAmount，无此方法级完整批reservation模型。
- MAIN `cp6.web/src/views/fin/ApInvoiceView.vue:104`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 87项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## FIN-AR-01 应收、收款与核销

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现AR.PostAsync自己同时生成收入及COGS凭证，与修订INV独占COGS source明确不同；已有收款核销仍按旧票/receipt可用额直接逐票应用。

可复用：

- 收入/销项税及贷项方向、票/客户一致、收款和折扣/汇差计算可作legacy回归输入。

需实现或改造：

- 新BillingPolicy/义务切片claims、TaxFreeze/FM/FinancePlan与准确Source/GL分腿；收入policy-ready可独立完成，不套AP整manifest完成门。
- 将COGS来源移交INV，AR仅协调观察；收款业务批准/funds/GL分开，reservation及FinalReceipt/Allocation同最终Uow，未Apply补偿不返余额。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/ArInvoiceService.cs:82`：先收入GenerateAsync；104–116直接AR.Cogs/AR.CogsReversal第二次Generate，违背新Owner分工。
- MAIN `CP6.Core/Services/Fin/ArSettlementService.cs:24`：按Receipt.Posted及剩余额度核销，按原币/汇率计算差额；需补目标claims/reservation/独立批准与apply。
- MAIN `cp6.web/src/views/fin/ArInvoiceView.vue:101`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 79项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## FIN-INV-01 库存计价与业务对账

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现StockFinBridge是Qty×UnitPrice转凭证映射，缺价/非正额跳过；不能作为新独立库存价值效果、层消耗/余额、来源Owner采用与cold恢复账本。

可复用：

- 库存交易类型→会计事件映射、IntegrationEvent观察、Stock transaction来源键可保历史兼容。

需实现或改造：

- 新增ValueEffect/Line/Head、来源Current/Qualification/Policy、FIFO/MAVG/标准成本及层谱系；按R1任意精度digits+scale保wire。
- 实现R10十POST入口和独立本地Uow/checkpoint/cold C09，准确COST/AP/WMS/QA adoption、COGS唯一source与更正反向；缺价成为明确待来源而不是伪完成。
- R1 OPS-IF-049 H30新专用binding/proof/receipt须精确绑定FIN-INV effect/physical receipt/environment/purpose/ranges；永久fence不释放其他consumer claim。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/StockFinBridge.cs:38`：49–55缺价或amount<=0返回Skipped；53以Qty*UnitPrice四舍五入到2位。
- MAIN `CP6.Core/Services/Fin/StockFinBridge.cs:57`：SourceDocNo=TxnNo并Generate；只有旧桥事件，不是目标价值层/消费明细。

验收盘点`COMPLETE`：原AC/TC 102项，必需补充390项。102原AC及R10必需行为场景/安全与语义反例、冷C09 18突变逐项保留；纯schema、输入上下文及错误形状映射移入design_dependencies。原件定位完整，未把历史静态计数或JSON对象数当业务PASS。 集中审查纠正：92条带明确Expected/ExpectedCode的算法、schema和权限反例归必需校验，不能当纯元数据排除。

## FIN-FX-01 汇率与外币重估

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现重估读取现在未结AP/AR而非as-of历史全集；任意相同SourceDocNo即Pass，正向/次期冲回顺序AutoPost且无准确镜像义务恢复，不能满足新完整曝光/率/Policy与原期履行分轴。

可复用：

- 旧AR/AP含credit memo方向、gain/loss分录计算和ERP原始交易汇率冻结可保兼容参考。

需实现或改造：

- 引入不可变rate内容/控制、五维scope/FxUse、as-of ExposureSnapshot和source cut，缺率/缺Owner完整性明确阻断。
- 独立Batch/Plan/approval/effect，REVERSING原结果精确次期mirror义务及恢复；NON_REVERSING另carrying；不能自动开期或用docNo存在当成功。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/FxRevaluationService.cs:32`：39任意同docNo返回Pass；53–78只读当前未结AP/AR，未按历史asOf状态重建。
- MAIN `CP6.Core/Services/Fin/FxRevaluationService.cs:119`：先AutoPost正向，122起另造次期冲回；需新稳定result/obligation/OwnerApply。
- MAIN `cp6.web/src/views/erp/FxRateView.vue:74`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 28项，必需补充0项。S2 R1准确目标表完整枚举；期后R1补正原文共同保留。

## OA-DEF-01 流程和表单定义

归属WP12；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

流程发布已经验证schema、rowversion及子流程依赖pin；还需要发布命令收据、预览和activation治理，避免把发布等同启用/业务批准。

可复用：

- 流程draft编辑和发布校验
- 已发布子流程依赖固定及历史版本组件

需实现或改造：

- 流程/表单定义不可变版本，GET不写draft；创建/复制/发布各有原命令与CAS
- 预览digest/依赖集/版本同提交验证，与WF_AUTH撤权同fence
- 启用/停用以单独activation历史和唯一绑定CAS；历史实例pin不受head变化影响，设计器旧入口同门

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wf/FlowDefService.cs:124`：按draft rowversion验证schema/兼容性并固定已发布子流程依赖，更新published/head后Save；缺新publish命令原结果、预览digest和WF_AUTH共同围栏。

验收盘点`COMPLETE`：原AC/TC 27项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## OA-FORM-01 表单发起与查询

归属WP12；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

提交已有canonical hash/原submission恢复及流程pin事务；最新form在事务外读取，完整draft生命周期与五上下文CAS仍需补齐。

可复用：

- submission key+input hash幂等
- formdata和pinned流程启动同事务

需实现或改造：

- draft创建/编辑/删除与submitted互斥，五项expected上下文锁定绑定/activation/定义/草稿前驱
- 有损rebase先明确preview digest与字段/子表损失授权；后端复算和隐藏字段不能泄漏
- 发起、历史查询、分页总数与附件下载同当前范围；无实例合法表单明确，Owner未应用不能显已生效

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wf/FormSubmissionService.cs:29`：对submissionKey和canonical input hash恢复原结果；formdata与pinned流程启动同事务。最新form在事务前选择，绑定/activation等五项expected上下文与draft竞争仍需新门。
- MAIN `CP6.Core/Services/Oa/TaskDecisionService.cs:29`：拥有事务并重试并发；查pending/running/assignee、代理权限、FormData rowversion和字段可写mask；按pinned表单重算验证后Act。需task+instance双CAS、命令收据和APP决定同事务记录。

验收盘点`COMPLETE`：原AC/TC 35项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## OA-TASK-01 待办、代理与时限

归属WP12；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

任务办理具有事务、代理访问、FormData CAS和服务端复算；新任务/实例并发、超时pin及APP决定记录须收敛到同一决定路径。

可复用：

- pending/running/assignee检查及并发重试
- 按pinned表单mask重算验证

需实现或改造：

- task+instance双CAS、实际/有效actor与授权Ref、命令收据；APP TxD必须写决定outbox
- 退回/重送/转交/无人审批/委派各原命令，SFS编辑与Owner业务输入分开
- 超时按原pin+工作日/时区，worker lease epoch/人工竞争只一结果；通知偏好/投递/已读水位持久且不冒业务成功

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Oa/TaskDecisionService.cs:29`：拥有事务并重试并发；查pending/running/assignee、代理权限、FormData rowversion和字段可写mask；按pinned表单重算验证后Act。需task+instance双CAS、命令收据和APP决定同事务记录。
- MAIN `CP6.Core/Services/Wf/WfTimeoutService.cs:43`：扫描到期任务并remind/approve/reject/escalate；NodeOf_Schema按FlowKey查询当前定义（113），不能保证旧task按发布时pin执行。
- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。

验收盘点`COMPLETE`：原AC/TC 46项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## OA-APP-01 业务审批绑定与回调

归属WP12；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现有合同在实例完成时同步callback，不能证明可靠跨Owner应用；应迁移为持久绑定、决定交付和Owner回执，同时禁止新旧路径双写。

可复用：

- OA流程引擎终态触发点
- 业务callback注册可帮助盘点实际Owner

需实现或改造：

- 绑定治理、FrozenInput和SubmissionCore/FullRequestHash/Intent/alias，TxS与幂等engineStartId的TxE分离
- TxD同事务写不可变Decision+Outbox；Owner Inbox/current authorization+业务+application receipt同提交
- 可信withdraw输入保真实actor/owner事实，与apply争共同control CAS；OA只投影receipt链、不擅改业务
- 持久ITER-OA映射全成员/轮次，并治理回执连续链/缺页/同序异hash和Legacy adapter单一writer

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。
- MAIN `CP6.Core/Services/Oa/TaskDecisionService.cs:29`：拥有事务并重试并发；查pending/running/assignee、代理权限、FormData rowversion和字段可写mask；按pinned表单重算验证后Act。需task+instance双CAS、命令收据和APP决定同事务记录。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs:132`：自行新建context并开Serializable，Processed重复在161-163提前返回；DbUpdateException重复恢复也会直接Duplicate（94-98）。业务回调前需要新的调用方事务precheckpoint，且早退分支不能绕过。

验收盘点`COMPLETE`：原AC/TC 88项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## SYS-WORKSPACE-01 菜单与业务工作台

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

工作台有真实KPI和最近单据实现，但当前全局缓存/SQL范围与S5当前行权不相容；菜单治理和个性化也需纳入完整业务范围。

可复用：

- 现有KPI计算与最近订单投影
- 现有菜单/动作聚合与权限缓存失效入口

需实现或改造：

- 版本化菜单树/个性化配置及CAS，保留菜单动作的服务端权限边界
- KPI数据由业务Owner定义，查询/计数/缓存key必须包含tenant及当前授权范围
- 区分零值、无权、缺源/未知；切租户/撤权后清除旧数据及深链结果

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.WebApi/Controllers/Sys/DashboardController.cs:44`：使用全局DashboardKey，Dapper统计及最近订单保持全局范围（65行注释明确）；现有KPI计算可复用但不可直接作为租户/当前行权范围的工作台结果。
- MAIN `CP6.Core/Services/Sys/PermissionAggregator.cs:16`：只有BuildEnabledRolesAsync路径过滤停用角色；菜单/动作取并集、行范围MAX及自定义部门并集、字段已有行MIN。保留语义，不能据此证明完整字段注册表与提交时撤权围栏。
- MAIN `CP6.Core/Services/Sys/CurrentPermissionContext.cs:39`：权限上下文按用户名缓存，滑动30分钟并支持失效；缓存刷新不是业务提交与撤权writer的共同裁决。

验收盘点`COMPLETE`：原AC/TC 20项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-LANG-01 字典与多语言

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

当前已经逐包生成并最后切manifest，不能沿用旧文书的缺源码结论；需补不可变发布和完整回滚，不重写现有文件存储。

可复用：

- 共享ILangFileStore和先包后指针的发布顺序
- 安全版本/语言路径检查

需实现或改造：

- 语言项维护/审核/导入导出建立版本及字段权限
- 发布版本不按秒碰撞；命令幂等、完整包hash清单和指针CAS
- 回滚核验全部语言包/摘要，不只首包；失败和响应丢失按原键查凭证，客户端按精确版本缓存

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.WebApi/Services/LangPublishService.cs:32`：逐语言写包后最后切manifest指针（55行）；版本按秒生成（34行），Rollback只检查首语言文件（99行）。发布源码已存在，实际缺口是唯一版本、完整校验及原命令/CAS恢复。

验收盘点`COMPLETE`：原AC/TC 20项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-AUD-01 操作、安全与字段审计

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

已有清理循环真实删除全租户旧日志；完整审计目标还需要Owner当前读权、业务同事务字段差异及保留责任，不能把默认7天清理当合规闭环。

可复用：

- 可复用操作/安全审计入口与批量清理调度
- 权限、tenant管理已有调用审计路径

需实现或改造：

- 操作/安全/字段变更审计统一敏感值脱敏、actor/effectiveactor与UTC来源
- 关键业务事实/收据/审计同Owner事务，导出查询总数/明细同授权范围
- 清理先确认policy、hold、引用和unknown；记录可追溯清理收据，旧日志时间来源明确

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.WebApi/BackgroundServices/OperLogCleanupService.cs:58`：按本地时间cutoff、IgnoreQueryFilters与ExecuteDelete清理；默认7天/24小时，未在此读取Owner hold/unknown/reference清单。
- MAIN `CP6.Core/Services/Platform/TenantAdminService.cs:76`：创建租户、管理员、角色及默认菜单一次SaveChanges（133-137）；随后单独审计。暂停只改Enable并Save（185）。需保护唯一性、版本化模板、业务写入围栏及原子审计。
- MAIN `CP6.Core/Services/Platform/GdprService.cs:94`：导出主体及身份/日志，注明不扫描全部Creator数据；擦除吊销refresh但既发access自然到期（139）；租户purge有关系型事务和身份墓碑（204起），不能代表所有业务PII/hold已覆盖。

验收盘点`COMPLETE`：原AC/TC 21项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-PRIV-01 隐私导出、保留与擦除

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现有主体导出、擦除和关系型租户purge可复用，但导出承认未扫描业务Creator图，access自然到期不能满足当前授权撤销和全Owner擦除证明。

可复用：

- 最后管理员/平台管理员保护
- 关系型purge事务、拓扑删除和身份墓碑

需实现或改造：

- 建立全业务PII Owner目录、按稳定subject键导出清单并明确遗漏/不可用
- 擦除预览、保留/hold判定、审批与逐Owner回执；unknown保留责任，不自动重做
- 撤销与业务最终授权共享fence；备份/恢复墓碑与保留例外可审计，导出包当前下载再授权

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Platform/GdprService.cs:94`：导出主体及身份/日志，注明不扫描全部Creator数据；擦除吊销refresh但既发access自然到期（139）；租户purge有关系型事务和身份墓碑（204起），不能代表所有业务PII/hold已覆盖。
- MAIN `CP6.WebApi/BackgroundServices/OperLogCleanupService.cs:58`：按本地时间cutoff、IgnoreQueryFilters与ExecuteDelete清理；默认7天/24小时，未在此读取Owner hold/unknown/reference清单。
- MAIN `CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:33`：要求调用方事务，锁aggregate后采样、同摘要早退、单调版本并把优先撤权/普通Outbox加入同context；可保留捕获结构，需覆盖全部writer与新S6前置检查。

验收盘点`COMPLETE`：原AC/TC 22项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-FILE-01 附件与文件管理

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现有上传、内容复用及业务重绑可保留接口；整流读内存、MD5路径复用和删关联后物理删除不足以实现内容证据和Owner授权生命周期。

可复用：

- 共享文件存储接口、允许扩展名及大小检查
- 已有附件业务关联和内容路径引用查询

需实现或改造：

- 流式大小限制、恶意内容扫描/quarantine、不可变content版本与digest
- 绑定业务Owner的current读/写/download权限、草稿转正式的原命令/事务及冻结引用
- ACT-FILE只采用LOCAL_AUTHORITATIVE_GUARD；清理前确认全引用/hold且失败可恢复，衍生物与导出也受门

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pub/AttachmentService.cs:26`：上传先整流读入内存，按MD5复用存储；删除先删关联再检查剩余路径引用；Rebind直接改BizId并保存。可复用文件存储接口，须补当前Owner授权、冻结/hold和可恢复清理。

验收盘点`COMPLETE`：原AC/TC 30项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-NUM-01 编号与开发辅助生成

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现有NextAsync实现格式和周期重置，但本方法读改存序列并自行Save，无原命令分配记录；并发唯一性和业务回滚/重放语义需要新增持久规则。

可复用：

- 现有BizKey编号格式和日期周期规则
- 原有开发生成器产物可作为模板起点，尚未逐模板核验

需实现或改造：

- 明确tenant/法人/业务序列scope与版本化规则，原子CAS/锁分配并保障唯一索引
- 固定command到号码/原结果，重试不得再取号，失败不回收已分配号
- 生成器预览diff、路径白名单/覆盖检查及模板版本；不能通过生成器绕开Owner权限/契约

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pub/SeqService.cs:15`：按BizKey查询序列、按时间重置后自增并SaveChanges；本方法未持久原命令结果或CAS分配收据。

验收盘点`COMPLETE`：原AC/TC 16项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-CLIENT-01 客户端设备与升级

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

已有设备激活、签名心跳、共享设备快速切换及native会话；nonce先查后写与分散签发使防重放和统一身份完成门仍有缺口。

可复用：

- RSA/ECDSA签名、时间窗口及设备状态检查
- 共享设备full-auth时效和native refresh上下文

需实现或改造：

- 激活/吊销/绑定命令CAS与原结果；nonce原子占位并带scope
- Web/native/SSO/2FA/refresh统一当前身份完成门及设备租户绑定
- 升级渠道/签名/兼容策略、失败恢复和设备管理页面完整；真实设备/签名包验证单独证据

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/ClientDeviceService.cs:286`：验证时间窗、nonce及RSA/ECDSA签名；nonce缓存先Get后Set（295/310）非原子抢占；QuickSwitch另查active/shared/full-auth时效。
- MAIN `CP6.WebApi/Controllers/ClientAuthController.cs:478`：native签发校验设备登录、生成token/profile、记审计/refresh，最后标记完整认证；此时未见与Web共用原子完成门。

验收盘点`COMPLETE`：原AC/TC 22项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-JOB-01 应用后台作业

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

已有超时扫描和清理服务，但超时读取当前FlowKey定义；作业权限、租约代际、固定配置及持久结果应形成共同调度合同。

可复用：

- 作业终态过滤与reserved保留计数
- 超时remind/autoapprove/reject/escalate的现有动作入口

需实现或改造：

- 作业定义/参数版本化，task按创建时pin，时区/工作日来源固定
- worker claim/lease epoch和最终CAS，人工/worker只一合法终态，旧worker不能写
- Owner当前授权、幂等effect/unknown、暂停重跑恢复及保留hold；监控只读不触发重做

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wf/WfTimeoutService.cs:43`：扫描到期任务并remind/approve/reject/escalate；NodeOf_Schema按FlowKey查询当前定义（113），不能保证旧task按发布时pin执行。
- MAIN `CP6.Core/Services/Wf/WfCleanupService.cs:55`：只删已终态旧服务作业，保留stale reserved并计数；可复用选择原则，但尚无共同policy/hold/receipt闭包。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs:224`：claim使用条件更新和token可保留；LoadOwnedClaim只匹配id/status/owner/token未核expiry。终态写需当前epoch/nativeRV/未过期fence；Enqueue仅Add不Save（25）可加入Owner事务。

验收盘点`COMPLETE`：原AC/TC 25项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-TENANT-01 租户管理

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

当前创建有单次Save和管理员/角色模板，可保留；模板来源、重复命令、审计及暂停对所有writer的即时约束还不完整。

可复用：

- tenant/admin/role/menu同context创建
- 时区校验及初始化临时密码仅首次返回

需实现或改造：

- 创建命令、唯一租户code及模板版本收据，配置CAS和角色种子来源可追溯
- 暂停/恢复产生单调权威版本，与登录完成和业务写入共享fence
- 暂停不阻合法历史只读/原结果查询；跨tenant管理最小权限与审计原子，删除遵守隐私/保留

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Platform/TenantAdminService.cs:76`：创建租户、管理员、角色及默认菜单一次SaveChanges（133-137）；随后单独审计。暂停只改Enable并Save（185）。需保护唯一性、版本化模板、业务写入围栏及原子审计。
- MAIN `CP6.WebApi/Controllers/Sys/AuthController.cs:332`：2FA完成消费pending、构造token/profile、审计后签发refresh；Refresh先Rotate再构造token（550）。需统一所有登录/续签完成门并验证当前tenant/user/role，而非仅缓存或入口验证。
- MAIN `CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:33`：要求调用方事务，锁aggregate后采样、同摘要早退、单调版本并把优先撤权/普通Outbox加入同context；可保留捕获结构，需覆盖全部writer与新S6前置检查。

验收盘点`COMPLETE`：原AC/TC 23项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SYS-IAM-01 用户、组织与授权

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

当前权限聚合与Web/native认证是可用基础，但启用角色路径和缓存路径不同；需要当前身份完成门及Owner提交时撤权裁决，而非仅延长缓存失效列表。

可复用：

- 菜单/动作并集、行scope MAX、custom部门并集和已有字段MIN
- 2FA、密码策略、refresh轮换及设备校验的现有构件

需实现或改造：

- 用户/部门/角色/委派变更CAS和权威版本；所有认证入口统一完成门
- 完整字段注册表上先MIN已有授权后默认1，未知字段拒绝；业务query/write/export/download均当前授权
- S6 precheckpoint与Owner final auth同事务/同撤权fence；SPACE-S5完整membership/grant集合向量和61字段映射

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Sys/PermissionAggregator.cs:16`：只有BuildEnabledRolesAsync路径过滤停用角色；菜单/动作取并集、行范围MAX及自定义部门并集、字段已有行MIN。保留语义，不能据此证明完整字段注册表与提交时撤权围栏。
- MAIN `CP6.Core/Services/Sys/CurrentPermissionContext.cs:39`：权限上下文按用户名缓存，滑动30分钟并支持失效；缓存刷新不是业务提交与撤权writer的共同裁决。
- MAIN `CP6.WebApi/Controllers/Sys/AuthController.cs:332`：2FA完成消费pending、构造token/profile、审计后签发refresh；Refresh先Rotate再构造token（550）。需统一所有登录/续签完成门并验证当前tenant/user/role，而非仅缓存或入口验证。

验收盘点`COMPLETE`：原AC/TC 40项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## INT-ID-01 Main与CRM身份投影合同

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Main已有事务内snapshot捕获，CRM有严格契约和投影消费者；CRM以Int32版本走Platform Inbox，重复早返绕过业务回调，不满足R1 precheckpoint及真实最终授权。

可复用：

- 源Owner事务锁后抓取、摘要去重、版本与Outbox
- CRM注册tenant/region校验、失败不可用标记及对账调度

需实现或改造：

- 租户/组织/issuer注册、完整权限/部门/用户权威向量和本地session分轴
- 消费者所有重复/旧/同版前执行precheckpoint；版本宽度/原字节与契约升级明确
- Main/CRM所有业务writer最终授权与撤权同fence；完整目录/水位对账不可用时fail closed，恢复不复活旧session

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:33`：要求调用方事务，锁aggregate后采样、同摘要早退、单调版本并把优先撤权/普通Outbox加入同context；可保留捕获结构，需覆盖全部writer与新S6前置检查。
- CRM `src/CP6.CRM.Infrastructure/Identity/IdentityEventConsumer.cs:36`：契约及tenant/region验证后把aggregateversion读作Int32，交给Platform Inbox；回调内tenant锁、投影Apply和published写入。Processed早返不会执行回调，尚无新precheckpoint；失败另事务标不可用/修复。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs:132`：自行新建context并开Serializable，Processed重复在161-163提前返回；DbUpdateException重复恢复也会直接Duplicate（94-98）。业务回调前需要新的调用方事务precheckpoint，且早退分支不能绕过。

验收盘点`COMPLETE`：原AC/TC 35项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## INT-OPS-01 集成异常与受控重放

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

已有Main replay事务、原operation审计及幂等结果；新设计要原结果优先、三类封闭动作和跨Owner稳定分页，旧RequireActiveTenant优先顺序需调整。

可复用：

- 原operation查重与相同参数结果恢复
- 重排/审计同本地事务

需实现或改造：

- request/result/bridge三allowedAction及nativeType/digest域精确映射，bridge不得伪造envelope
- 按当前读权查原结果优先，再对新调度执行源保护/授权/lease门
- 多Owner分页固定sourceSet和唯一全序；partial/unknown明确，PII脱敏，当前tenant切换清旧结果；原始业务恢复须Owner回执

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:85`：Serializable内先RequireActiveTenant（87）再查原operation（92）；同operation能回原结果，重排和审计同提交。新设计要求先按当前读权恢复原operation，再评估新的调度门。
- MAIN `CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs:48`：Serializable内锁Inbox、原结果早返，业务/响应outbox/processed同事务；旧C03工厂入口仍创建订单（100），版本使用int，不能代替新H03/H06/H08及IC精确原体保护。
- CRM `src/CP6.CRM.Infrastructure/Identity/IdentityEventConsumer.cs:36`：契约及tenant/region验证后把aggregateversion读作Int32，交给Platform Inbox；回调内tenant锁、投影Apply和published写入。Processed早返不会执行回调，尚无新precheckpoint；失败另事务标不可用/修复。

验收盘点`COMPLETE`：原AC/TC 35项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## PLT-CTX-01 请求、租户与服务身份原语

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

远端固定主线已提供不可变请求上下文和finally清理；新授权/服务身份合同仍需真实宿主注册与来源可信证明。

可复用：

- resolver失败403及上下文生命周期隔离
- request/tenant身份基础对象可复用

需实现或改造：

- 明确trusted issuer/tenant/actor/effectiveactor来源、无默认租户及服务身份最小权限
- 后台任务/消息/HTTP显式建立与清理scope，禁止AsyncLocal串租户
- schema、配置和宿主采用矩阵；当前权限裁决由Owner最终门执行，context本身不是授权证明

源码锚点（以下均须按上述固定SHA读取）：

- PLATFORM `src/CP6.Platform.AspNetCore/RequestContextMiddleware.cs:27`：resolver失败拒绝403，安装RequestContext并finally清除；可复用scoped上下文防串租户，真实host注册和最终授权仍需单独证明。

验收盘点`COMPLETE`：原AC/TC 28项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## PLT-HTTP-01 HTTP错误与弹性契约

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

有profile绑定、错误/timeout和transient策略；当前无Retry-After且零间隔重试，与新deadline和写重试资格不符。

可复用：

- 命名client的profile冲突防护
- 总/单次timeout及受限transient分类

需实现或改造：

- 统一安全错误payload/traceId/业务错误边界，避免敏感异常泄漏
- deadline预算、Retry-After上下界、可测时钟/backoff和取消传播
- 写操作重试必须有远端durable dedup原键查询证明；Unknown不可自动换键，幂等与非幂等profile分开

源码锚点（以下均须按上述固定SHA读取）：

- PLATFORM `src/CP6.Platform.AspNetCore/Cp6HttpResilienceServiceCollectionExtensions.cs:55`：已有总/单次timeout及transient重试；delay=0、constant、无jitter且不处理Retry-After（69）。新v2需deadline预算与有远端持久去重证明的写重试。

验收盘点`COMPLETE`：原AC/TC 42项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## PLT-MSG-01 消息封装与Kafka传输

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

真实validator已严格解析JSON并核验schema bundle，但bundle以event type作键，尚不能代表新精确版本/profile矩阵与原字节/身份语义。

可复用：

- 严格JSON和schema/hash校验
- bundle校验失败拒绝注册

需实现或改造：

- 精确event type/schema/version/profile与producer资格注册；保留原字节域和摘要domain/codec
- tenant/法人/aggregate原身份、opaque及int64字符串版本与decimal字符串按原schema验证
- Kafka/Dapr传输键/headers/消息限制、安全日志及死信原体保全；禁止用通用归一化重写原wire

源码锚点（以下均须按上述固定SHA读取）：

- PLATFORM `src/CP6.Platform.Messaging/Cp6CloudEventValidator.cs:33`：严格JSON深度/语法并经bundle及schema验证；保留字节验证入口，需新精确schema tuple/profile、opaque/native版本和生产者资格。
- PLATFORM `src/CP6.Platform.Messaging/Cp6ContractBundle.cs:31`：entry字典按event type索引，加载时校验schema/hash；要把批准的type+schema版本+profile精确匹配落到调用合同，避免当前结构冒充新注册矩阵。

验收盘点`COMPLETE`：原AC/TC 40项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## PLT-DEL-01 可靠收发与消息存储

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Inbox自开事务且Processed早返、Outbox owned claim不核expiry、保留按年龄删除，均是实际方法与R1共同授权/责任保留的具体差距。

可复用：

- Outbox Enqueue仅Add便于Owner同事务
- 条件抢占claim及Inbox原payloadhash冲突检测

需实现或改造：

- 提供调用方同事务precheckpoint，所有duplicate/old/equal早返前执行；rawHash与contentHash分域
- lease epoch/nativeRV/expiry参与最终CAS；外部成功后数据库失败归Unknown，按原键恢复
- 清理受Owner hold/未知/回执链引用闭包；共享事务API与SQLServer/PostgreSQL真实provider适配

源码锚点（以下均须按上述固定SHA读取）：

- PLATFORM `src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs:132`：自行新建context并开Serializable，Processed重复在161-163提前返回；DbUpdateException重复恢复也会直接Duplicate（94-98）。业务回调前需要新的调用方事务precheckpoint，且早退分支不能绕过。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs:224`：claim使用条件更新和token可保留；LoadOwnedClaim只匹配id/status/owner/token未核expiry。终态写需当前epoch/nativeRV/未过期fence；Enqueue仅Add不Save（25）可加入Owner事务。
- PLATFORM `src/CP6.Platform.EntityFramework/Cp6OutboxDispatcher.cs:51`：publish及MarkPublished在同try，后置数据库写失败进入MarkFailed；须区分外部effect未知与确定失败，防止旧lease提交。

验收盘点`COMPLETE`：原AC/TC 60项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## PLT-OBS-01 可观测性与运行身份

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

SLO evaluator可保留缺证不确定与阈值规则；字段齐备不代表可信运行采样，完整运行身份、Owner事务度量和证据导出仍待实现采用。

可复用：

- 证据缺失判indeterminate
- 既有coverage/sample/source/digest检查

需实现或改造：

- 运行service/version/sourceSHA/digest与环境身份，健康/ready区分依赖不可用
- trace/metric/log脱敏、低基数和限流；Owner提交/应用回执的业务时延定义
- 采样窗口、遗漏/排除、clock来源与可重算证据封存，真实宿主观测不以测试生成数据代替

源码锚点（以下均须按上述固定SHA读取）：

- PLATFORM `src/CP6.Platform.Contracts/Cp6SloEvidenceEvaluator.cs:8`：检验coverage、样本、来源/digest/release字段后按阈值裁定，缺证判indeterminate；不负责采集或验证实际运行证据，需完整Owner度量、redaction及真实宿主采用。
- PLATFORM `src/CP6.Platform.Testing/Cp6TelemetryRecorder.cs:14`：全局listener和无界ConcurrentQueue采集；decimal转double（50），trace断言严格单节点直接父链（62起）。需有界case隔离及扇出/重试DAG、计量精度和确定性时钟。

验收盘点`COMPLETE`：原AC/TC 32项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## PLT-RELEASE-01 包信任与部署验证

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

包发布validator已校验canonical、来源和信任/hash，版本名单仍硬编码；这只是可复用验证器，不是新包已发布或Main/CRM已采用。

可复用：

- canonical字节和精确字段验证
- source/run/trust/package哈希绑定

需实现或改造：

- 版本化包能力/兼容矩阵、签名根/撤销及不可变发布清单
- 宿主锁定版本和实际加载/运行身份验证；受控更新/回滚兼容性
- 生产Registry权威与发布门遵循DevOps决策；本评估不发布包、不触发Actions、不部署

源码锚点（以下均须按上述固定SHA读取）：

- PLATFORM `src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs:17`：严格canonical字节/字段及source/run/trust/package hash绑定，但允许版本硬编码0.10.0/1/2（38）。需新包矩阵和真实host/registry采用证据，现有校验不等于已发布。

验收盘点`COMPLETE`：原AC/TC 32项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## PLT-TEST-01 故障注入与测试辅助

归属WP02；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

测试辅助已有采集与拓扑断言，但全局无界队列、decimal转double和直接父链模型不适合新跨Owner重试/并发证据。

可复用：

- 可复用listener/断言入口与现有合同测试结构
- 既有trace关联和样本捕获框架

需实现或改造：

- case scope隔离、有界capture和清理，确定性clock/随机/故障注入位置
- 支持扇出、重试、收件/应用分轴DAG而非单父链；保留decimal原精度
- 真关系型双provider并发/崩溃点和原键恢复断言；合成样本明确不等于生产验收

源码锚点（以下均须按上述固定SHA读取）：

- PLATFORM `src/CP6.Platform.Testing/Cp6TelemetryRecorder.cs:14`：全局listener和无界ConcurrentQueue采集；decimal转double（50），trace断言严格单节点直接父链（62起）。需有界case隔离及扇出/重试DAG、计量精度和确定性时钟。

验收盘点`COMPLETE`：原AC/TC 24项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## ENG-TOOL-01 版模工程主数据

归属WP05；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Main已有ERP版模创建、改版、纠正、删除、订单联动及页面；WMS另有实物库存入口。需保留旧入口兼容并按X1改造精确技术身份、事务/原键与Owner回执，不能将旧服务漏记为从零新增。

可复用：

- ERP PlateMoldService及PlateMold实体已有No/Rev、有效期、查历史、创建/改版/纠正/删除与列表/编辑页。
- WMS PlateMoldService和库存记录已有实物所在库位/状态基础；工艺关联标量可迁入精确引用。

需实现或改造：

- 保留/api/plate-molds旧读写入口的有界兼容；强制expectedNativeRowVersion及expectedTechnicalControl，新修订、有效区间、原命令回执同事务，不能max+1后换Rev重试。
- 将直接改写订单/重建遗失关联的旧路径改为原Owner CREATE_OR_LINK/CHANGE_REQUEST/CANCEL_REQUEST意图及原键查结果；PE NoOp成功只能STUB_NOT_EXECUTED。
- 新增精确技术定义、实例/DOC提案、轮次和双Role收敛，当前Owner原生回执与最终权限在提交前共同保护；无真实参与方保持UNKNOWN/FENCED。
- 适配旧两页和WMS物理入口，完整覆盖历史写者；保留历史原始身份与摘要，不把技术退役当实物报废。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/PlateMoldService.cs:115`：已有创建、订单联动、PE调用；多个SaveChanges构成旧路径，需X1事务与原键改造。
- MAIN `CP6.Core/Services/Erp/PlateMoldService.cs:150`：按maxRev+1新增并关闭旧区间；须改受控并发与原命令结果。
- MAIN `CP6.Core/Services/Erp/PlateMoldService.cs:251`：旧路径直接更改订单，找不到则重建；应改Owner意图/恢复。

验收盘点`COMPLETE`：原AC/TC 38项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## ERP-FSC-01 FSC清单与行业证明

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

旧发行/管理号/下载功能存在；Download会按当前TemplatePath重新生成，模板直接返回或plain-text伪xlsx，无法满足准确签发payload和稳定字节下载。

可复用：

- 复用查询条件、管理号/发行历史与现FSC页面作为legacy观察。

需实现或改造：

- 建立准确选择/Profile/模板与EST事实/QTN关系双Guard、持久payload/hash及独立批项目状态。
- 真正生成声明文件格式并保存签发字节；下载读取原字节且当前授权；批次各项恢复/等待/计数及全量下载，禁止重新发行代恢复。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/FscChecklistService.cs:193`：199–200明确未存发行文件，因此重读模板生成。
- MAIN `CP6.Core/Services/Erp/FscChecklistService.cs:234`：240返回模板原bytes；244起fallback文本，与xlsx命名不符新合同。
- MAIN `cp6.web/src/views/erp/FscChecklistView.vue:104`：现有 Vue 业务页及旧 API 接线可复用布局；新状态/原命令恢复/精确字符串值须按本目标重接。

验收盘点`COMPLETE`：原AC/TC 24项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## ERP-PRICE-01 板纸单价与行业价格参数

归属WP04；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

旧Search/BatchUpdate确按完整13字段匹配，不能说仅三字段；但BatchUpdate直接覆盖UnitPrice，无冻结preview、期望版本、持久原命令/价格版本及current coverage用途证明。

可复用：

- 完整13字段匹配、标准/估算双表、Excel解析/选行预览UI保留作为输入与legacy映射。

需实现或改造：

- 建立不可变price版本/profile/supplementSnapshot、ALL_OR_NOTHING预览提交、缺行竞争/原键/CAS与SourceNotice。
- 采用PRICE 0..18与EST 0..12单位精度差异，超支持明确拒绝；PRICE→EST准确whole-sheet coverage与同Main门，收敛旧直表读写。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Erp/SheetUnitPriceService.cs:246`：255–260/282–287完整13条件匹配，275/302原地UnitPrice赋值，309单次SaveChanges。
- MAIN `CP6.Entity/DomainModels/Erp/SheetUnitPrice.cs:67`：旧decimal(15,4)有限精度，不是新price版本wire完整能力。
- MAIN `cp6.web/src/views/erp/SheetUnitPriceView.vue:109`：现有 Vue 业务页及旧 API 接线可复用布局；新状态/原命令恢复/精确字符串值须按本目标重接。

验收盘点`COMPLETE`：原AC/TC 28项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## PUR-SUB-01 委外加工

归属WP07；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

支给量直接upsert、WMS调用后累加Issued，缺固定整集合材料计划与准确多腿原结果。

可复用：

- 按实际wms.IssuedQty累加；加工费/材料成本/超差对账可复用。

需实现或改造：

- 建立PO SourcePortion关联与整集合支给计划
- 原键多腿发退料/产出/损耗恢复，保PER_LEG部分成功与真实估值

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Pur/SubcontractService.cs:63`：直接改应发和成本。
- MAIN `CP6.Core/Services/Pur/SubcontractService.cs:125`：调用WMS后按实发累加。
- MAIN `CP6.WebApi/Controllers/Pur/SubcontractController.cs:13`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 48项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## MES-SCHED-01 车间排程

归属WP08；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Reschedule直接改日期/机器，AutoArrange固定8点/默认2小时；缺Root组与换机六阶段。

可复用：

- 甘特/KPI、交期优先排序、未开始检查可用作草案。

需实现或改造：

- RootWO快照/Occurrence排程候选与资源窗
- 换机六阶段和当前执行/资源/WO共同门及原结果恢复

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/PlanningBoardService.cs:125`：只核ProcessStatus0及日期。
- MAIN `CP6.Core/Services/Mes/PlanningBoardService.cs:175`：8点起、默认时长。
- MAIN `CP6.WebApi/Controllers/Mes/PlanningBoardController.cs:11`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 36项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## MES-PERF-01 制造绩效与OEE

归属WP08；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

OEE日默认480减停机、CreateDate汇产量，缺产能可置100；不等准确槽/局部Pause/分窗量。

可复用：

- 三因子公式、日结果/趋势可保旧指标。

需实现或改造：

- Owner运行槽/局部暂停/真实分窗量及profile/cut
- LIVE/CLOSED分离，两分母/闭包同发布门，UNKNOWN不转0/100

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Mes/OeeService.cs:144`：缺planned默认480。
- MAIN `CP6.Core/Services/Mes/OeeService.cs:165`：按CreateDate汇产量。
- MAIN `CP6.Core/Services/Mes/OeeService.cs:190`：有产量且缺产能时performance100。

验收盘点`COMPLETE`：原AC/TC 48项，必需补充0项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-KIT-01 仓储套件作业

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Execute读取当前配方并逐组件OUT/成品IN，服务本身未包整组事务，旧Draft重入不能证明无已执行腿。

可复用：

- 配方/组装拆分订单、库存TxnNos关联及已Executed拒绝重复可保。

需实现或改造：

- 冻结配方/Basis/来源与整组Composition计划
- 多来源全部Stock效果/Source/容量同UoW，原键查询/关闭与历史部分成功对账

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/KittingService.cs:176`：执行读取当前KitMasterComponents。
- MAIN `CP6.Core/Services/Wms/KittingService.cs:205`：逐组件Stock.Apply；224成品IN，最后283写Executed。
- MAIN `CP6.WebApi/Controllers/Wms/KittingController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 15项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-SLOT-01 库位优化

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

共用ABC为末项累计，SLOT要求前缀累计独立算法；Analyze按Txn行次数及位置命名前缀，Approve走旧V2派单而非新Transfer来源预算。

可复用：

- 当前作用域过滤、确定排序和事务性移动任务关联值得保留。

需实现或改造：

- 实现SLOT前缀累计算法/真实事件cut与正式LocationKind
- 冻结Proposal/独立批准→Transfer SourceDemand派发，停止核全部stage/旧writer切换

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/SlottingService.cs:71`：按StockTransactions行Count/Qty。
- MAIN `CP6.Core/Services/Wms/AbcClassifier.cs:51`：先cumulative+=再定A/B/C，SLOT不能直接共用。
- MAIN `CP6.Core/Services/Wms/SlottingService.cs:220`：Guid.NewGuid生成V2移动任务操作号。

验收盘点`COMPLETE`：原AC/TC 15项，必需补充3项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-REP-01 库内补货

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Generate按PIK命名/Physical<阈值及单来源余额，执行接MobileV2；缺正式补给需求/规则版/Transfer源预算与stage全量恢复。

可复用：

- 去重未完成补货、source/task事务同步、claimed任务禁止源修改、完成回源基础已实现。

需实现或改造：

- 正式补给Rule/Basis/SourceDemand与固定候选
- 改接Transfer原businessKey/全部Stage回执，保已实现source-task一致性并迁移旧任务族

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/ReplenishService.cs:137`：PIK-*与缺省min10决定补货。
- MAIN `CP6.Core/Services/Wms/ReplenishService.cs:176`：Math.Min所需与单来源Available。
- MAIN `CP6.Core/Services/Wms/ReplenishService.cs:200`：派移动任务并记录TaskIssued，需要适配新Transfer。

验收盘点`COMPLETE`：原AC/TC 18项，必需补充3项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-XDOCK-01 越库作业

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Execute独立IN后OUT再写Executed，无本服务整组关系事务/原source预算；IN成功OUT失败不能直接重放整单。

可复用：

- 已有越库源入/出关联字段和两腿Txn记录，但只是旧模型。

需实现或改造：

- 真实Arrival/FINAL_OUTPUT和Shipment/RSV授权映射
- 同Source/Root/Demand预算与Stock两腿共同UoW，逐键恢复/全路径Fence，禁止随机批次替原源

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/CrossDockService.cs:72`：先Stock IN。
- MAIN `CP6.Core/Services/Wms/CrossDockService.cs:84`：随后Stock OUT，98才Executed；未建立新整组原键协议。
- MAIN `CP6.WebApi/Controllers/Wms/CrossDockController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 18项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-ROLL-01 原纸卷作业

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

PaperRoll维护独立RemainingLength和分切子卷，未通过Stock组成效果及真实测量/损耗映射。

可复用：

- 卷属性/匹配/父子号及剩余长度状态可作为旧几何历史。

需实现或改造：

- 绑定真实IN/Root和profile/stockToGeometryMap
- 分切父CONSUME+输出RECEIPT整组同UoW，保损耗真实离场/在场，旧未知来源隔离

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/PaperRollService.cs:60`：仅更新PaperRoll剩余长度。
- MAIN `CP6.Core/Services/Wms/PaperRollService.cs:99`：创建子卷/余卷、父Disposed，未调用StockComposition。
- MAIN `CP6.WebApi/Controllers/Wms/PaperRollController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 22项，必需补充3项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-REM-01 余料管理

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Remnant Create/Update可直接赋Quantity，Reserve/MarkUsed/Dispose只改状态，无Stock绑定/Claim与原Owner消费。

可复用：

- 尺寸匹配和登记备注可保作为未证观察，不能作现货。

需实现或改造：

- 真实origin/RootCoverage登记或UNVERIFIED_INTAKE隔离
- 预留/释放/消费走RSV/Stock原键及当前门；量/几何更正必须来源后继，旧MarkUsed不暗消费

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/RemnantService.cs:48`：直接赋独立Quantity。
- MAIN `CP6.Core/Services/Wms/RemnantService.cs:107`：仅置Used；无真实消费Receipt。
- MAIN `CP6.WebApi/Controllers/Wms/RemnantController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 24项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-TOOL-01 版模实物管理

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

维护完成把UsedShots清零，使用只加聚合计数，缺独立使用/维护事实和ENG技术有效关联。

可复用：

- 寿命阈值告警、维护状态与登记可迁为旧读。

需实现或改造：

- 不可变使用/维护事实及当前计量周期
- 绑定ENG原技术号/版/适用性，实物搬位/报废走真实Stock/Transfer/Finance Owner

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/PlateMoldService.cs:84`：直接UsedShots+=shots。
- MAIN `CP6.Core/Services/Wms/PlateMoldService.cs:111`：UsedShots=0清当前计数。
- MAIN `CP6.WebApi/Controllers/Wms/PlateMoldController.cs:13`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 12项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-INK-01 油墨批次管理

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Mix直接扣两InkLot.Quantity再建子量，Open允许覆盖expiry；没有Stock来源/共同单位/质量与当前INKQ资格。

可复用：

- 父批关联、取最早expiry、色匹配历史可留旧证据。

需实现或改造：

- Stock绑定量/开封事实/效期应用分轴
- 整组混合消费/输出、真实profile和INKQ producer/guard，旧独立余额及writer隔离

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/InkService.cs:67`：允许传newExpiry覆盖。
- MAIN `CP6.Core/Services/Wms/InkService.cs:90`：直接扣lotA/B独立Quantity，106新子量。
- MAIN `CP6.WebApi/Controllers/Wms/InkController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 14项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-VMI-01 VMI作业与结算依据

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

月费按当前库存假定月初末/均量，Confirm只布尔；不能证明历史cut计费义务或Finance实际接收。

可复用：

- 货主客户库存汇总、旧账单历史和确认只读可保。

需实现或改造：

- 历史cut/真实保管量时间积分及精确费用政策
- 冻结计费义务/批准/Finance服务接收和原键恢复，旧Confirmed不自动重发AR

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/VmiService.cs:104`：首算BeginQty=当前TotalPhysical，End相同。
- MAIN `CP6.Core/Services/Wms/VmiService.cs:126`：只设置Confirmed=true。
- MAIN `CP6.WebApi/Controllers/Wms/VmiController.cs:10`：当前公开路由；其存在不证明新v1合同已采用。

验收盘点`COMPLETE`：原AC/TC 16项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-SAMPLE-01 样品借还

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Sample独立Quantity、借还覆盖当前字段，Expire可使未归还记录退出LentOut逾期查询；缺真实保管移动和轮次事实。

可复用：

- 样品登记、借还状态/到期查询可保存为不完整历史。

需实现或改造：

- 真实Stock来源/保管模式与不可变借用轮次
- 归还/延期/退役及损失Owner回执，未归还不被Expire抹掉；准确联系人权/来源范围

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/SampleStockService.cs:53`：直接独立Quantity。
- MAIN `CP6.Core/Services/Wms/SampleStockService.cs:111`：直接Expired。
- MAIN `CP6.Core/Services/Wms/SampleStockService.cs:120`：仅LentOut状态进入逾期。

验收盘点`COMPLETE`：原AC/TC 17项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-AUTO-01 仓储设备任务与遥测

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

WCS Complete手工改状态，IoT Simulate与Post写同读数账；不能作为真实OEM执行/REAL观测及完整来源协议。

可复用：

- WCS基础状态拒绝、读数阈值报警/列表可用于明确隔离模拟。

需实现或改造：

- OEM dispatch/receipt/原键查询与真实Owner最终准入
- IoT origin/mode/单位/时间与迟到规则、版本化告警，隔离模拟和旧writer

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/WcsService.cs:80`：直接Completed无设备回执。
- MAIN `CP6.Core/Services/Wms/IotService.cs:99`：随机生成传感器读数，进入同IotSensorReadings。
- MAIN `CP6.Core/Services/Wms/IotService.cs:78`：真实输入相同存储路径，需origin/mode及迟到current规则。

验收盘点`COMPLETE`：原AC/TC 18项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## WMS-ANALYTICS-01 仓储报表与运行分析

归属WP10；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

月报用历史流水数量拼当前Allocated/均价，库龄用今天正库存回看日期；ABC频次同分还按数量排序，与新固定cut/口径不同。

可复用：

- 报表聚合、CSV和确定排序基础可保LEGACY标签；不升级历史事实。

需实现或改造：

- CURRENT/ORIGINAL_KNOWN/RESTATED固定cut和维度/单位分区
- 独立END_CUMULATIVE_LE_V1：真实发生频次去重及规定tie-break，缺量/估值保持UNKNOWN；完整分页

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wms/ReportCenterService.cs:50`：当前Stock Alloc/AvgPrice与历史流水拼接。
- MAIN `CP6.Core/Services/Wms/StockDwellService.cs:25`：取当前正库存且ReceiveDate有值，非历史库存快照。
- MAIN `CP6.Core/Services/Wms/AbcClassifier.cs:40`：频次同分也用Abs(OutQty)第二排序，与新频次稳定产品键不同。

验收盘点`COMPLETE`：原AC/TC 18项，必需补充2项。已逐条登记当前接受原件中的native AC/TC身份，并将当前必需连续步骤、正反例和契约向量分别登记；COMPLETE仅指本清单来源枚举，不是实现/运行/生产采用完成。

## FIN-BUD-01 预算管理

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

预算版本/复制/分解及审批底座已有；ActivateFromApprovalAsync会在OA回调直接激活，与新决定只入Inbox、显式独立激活相反；自动GL入口也不执行预算Guard。

可复用：

- 年度预算/版本行、原复制/分解、手工过账同桶合计和最具体维度匹配计算可复用。

需实现或改造：

- 建立冻结完整Submission/maker集合、OA原决定与独立Apply/activation；所有版本竞争、旧round/撤权终态可恢复。
- Guard必须显式required/NA，并在normal/auto/inverse及policy所有writer上持同预算gate到GL commit；实际报表读不可变正反历史同cut。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/BudgetService.cs:143`：150设Approved后152调用ApplyActivationAsync，161归档旧版并162激活当前。
- MAIN `CP6.Core/Services/Fin/BudgetGuard.cs:18`：只读Evaluate汇总判断；现实现本身无共享门持到GL提交证明。
- MAIN `CP6.Core/Services/Fin/JournalEntryService.cs:99`：自动路径未调用BudgetGuard。

验收盘点`COMPLETE`：原AC/TC 24项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## FIN-BANK-01 银行流水与对账

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现银行匹配/手工/自动/BankOnly凭证基础完整，Unmatch物理删匹配及links并把行设Unmatched；新要求不可变release事实、会计claim及恢复组G占用无FREE间隙，不能套旧解除动作。

可复用：

- 候选GL过滤、金额/币种匹配、statement open门和行RV局部并发可复用为诊断/legacy输入。

需实现或改造：

- 增加源/调整独立GL EffectTarget、matching claim与会计claim分轴、准确maker联合SOD与当前scope门。
- 实现R2完整冲突组恢复closure、StepReceipt、G→原A直接占用转移及TRANSFERRED_AWAITING_APPLICATION续原Apply；不生成镜像冒更正。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/BankReconService.cs:223`：旧手工匹配按候选/Posted/币种等校验并使用一条流水RV。
- MAIN `CP6.Core/Services/Fin/BankReconService.cs:287`：297置Unmatched，303/304删除links/group；非目标不可变整组恢复。
- MAIN `cp6.web/src/views/fin/BankReconciliationView.vue:149`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 28项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## FIN-ASSET-01 固定资产全生命周期

归属WP11；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

资产类别/卡片/四法折旧和处置已有；Post先AutoPost再累加卡片净值/计提期数，尚无新独立review/plan/owner completion与普通final借用或处置专属final区分。

可复用：

- 折旧分组会计分录、工作量必填、卡片累计与处置原值/净值/损益计算，可按新政策版本重新验证。

需实现或改造：

- 建立类别发布/卡片激活review、真实Calendar/Master/Machine映射和精确折旧算法版本及舍入。
- Plan/GL逐腿及资产OwnerApply/reservation独立；处置final采用REUSE_CURRENT/REUSE_CORRECTED/BLOCKED/CREATE_EXCLUSIVE，原镜像缺失不得重造，现金/物理结果不推定。

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Fin/AssetDepreciationService.cs:144`：152 UOP缺workload拒；181 AutoPost后191–199增加卡片累计并将run Posted，需目标恢复账本。
- MAIN `CP6.Core/Services/Fin/AssetDisposalService.cs:42`：校InUse/期间/唯一处置并固定原值净值损益，旧有真实计算可复用。
- MAIN `cp6.web/src/views/fin/AssetDepreciationView.vue:62`：现有Vue页与旧API接线可复用布局；目标版本的来源/计划/审批/应用/UNKNOWN状态和精确金额须重接。

验收盘点`COMPLETE`：原AC/TC 32项，必需补充0项。已逐项枚举当前正文/权威登记的原始编号与完整断言，包含该表内后继补充；COMPLETE只指文书目录，业务全部NOT_RUN。

## OA-CONN-01 流程触发、连接器与预演

归属WP12；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

有凭据保护和连接器执行入口，但当前URL可原位修改、name解析即调用；完整触发/预演和未知effect恢复需新增版本化执行合同。

可复用：

- DataProtection凭据和timeout/lease参数校验
- 既有WebApiExecutor actionref入口

需实现或改造：

- 触发规则/连接器provider/activation不可变pin、兼容性preview与命令CAS；预演无真实effect
- 先本地admission/授权fence，再无锁外部I/O；稳定effect key、外部durable去重/查询资格
- 外部Unknown不得视failed_no_effect；结果回写与业务apply分段且不得重调已执行外部effect；APP recorder进实际终态路径

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Wf/WfConnectorService.cs:72`：校验超时/lease并用DataProtection保护凭据；更新直接修改URL/timeout，空凭据保留，删除硬删。需不可变provider版本、activation/trigger治理与不泄密预演。
- MAIN `CP6.Core/Services/Wf/Executors/WebApiExecutor.cs:33`：解析actionref并按名称解析连接器后CallAsync；未见新effect admission、稳定外部幂等键或Unknown按原键查询/本地apply分阶段合同。
- MAIN `CP6.Core/Services/Wf/ApprovalDispatcher.cs:23`：按BizType找到首个callback后直接同步调用OnApproved/OnRejected，传小型业务上下文。此合同没有持久DecisionOutbox、Owner Inbox/application receipt或receipt链。

验收盘点`COMPLETE`：原AC/TC 32项，必需补充0项。当前模块原逐项定义及PG/LR明确编号已恢复；FB01按原业务责任、FB02按原画布承接精确补入，保留源行和完整断言。COMPLETE仅指清单，不代表实现/运行或全部组合穷尽。

## SPACE-MASTER-01 空间主数据与编码

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

源站点/库位编码及已发布引用保护已存在；新正式scope映射和code身份不能直接复用legacy字段含义或任意截断。

可复用：

- Site/Floor/Zone及编码规则现有CRUD
- 已有发布位置阻止破坏性改站点码/warehouse

需实现或改造：

- 明确设计source身份与WMS正式tenant/法人/site/warehouse/location映射，旧空字段保原义
- 保留编码/拖拽/重建算法，proposalHash、完整差异预览和原命令恢复；31字符源码不得自动截成正式L
- 所有legacy和DesignV1主数据writer纳入持久冻结/引用保护；普通查看允许未知映射但正式发布阻断

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Core/Services/Space/SpaceMasterService.cs:60`：site编码/warehouse改变前检查已有发布库位并拒绝（69-75）；可以复用源主数据CRUD和已有引用保护，不能替代完整设计冻结/正式scope映射。
- MAIN `CP6.Space.Infrastructure/SpaceDesignV1Service.cs:711`：验证写租约，在Serializable内锁floor、复核版本Draft/expected revision/hash、原batch重放；存在局部并发保护，仍需跨全部writer持久FrozenBinding及原Ready冻结语义。
- MAIN `CP6.Space.Infrastructure/Cp6SpaceWmsAdapter.cs:233`：按operationKey和payloadhash回原结果，事务中写操作结果；逐item校验scope/stale并继续形成部分结果（825起）。不能将legacy部分采纳称作新Target完整canonical采纳。

验收盘点`COMPLETE`：原AC/TC 8项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SPACE-DES-01 空间场景与设计版本

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

编辑已用租约、Serializable、内容版本/hash和原batch结果；缺的是不可逆FrozenBinding覆盖全部writer，特别Ready与后台commit。

可复用：

- 命令批处理、编辑租约和乐观版本
- 现有场景/版本及undo等设计能力

需实现或改造：

- 持久FrozenBinding独立于旧enum，原Ready也能冻结；模板/底图/clone/undo/rebuild/legacy入口全部受门
- 工作排队与最终commit双检冻结和原输入，来源身份/后继复制可追溯
- current只由专用finalizer按完整已证source/WMS/runtime关系推进；设计已发布不倒推WMS已采纳

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Space.Infrastructure/SpaceDesignV1Service.cs:711`：验证写租约，在Serializable内锁floor、复核版本Draft/expected revision/hash、原batch重放；存在局部并发保护，仍需跨全部writer持久FrozenBinding及原Ready冻结语义。
- MAIN `CP6.Space.Infrastructure/Cp6SpaceRuntimeMaterializer.cs:136`：对实际projectionHash验证expectedHash后生成运行元素，将前版superseded、新版Published并设置模型current同事务。需把源封存、WMS canonical采纳和runtime observed分别证明。

验收盘点`COMPLETE`：原AC/TC 9项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SPACE-IMPORT-01 CAD/Excel空间导入

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

Excel/CAD apply入队已锁floor并核版本、租约与原键；应保留解析/匹配能力，在长作业真正提交点补同一冻结与来源门。

可复用：

- 现有导入队列幂等、预期内容版本和租约校验
- CAD/Excel解析及匹配相关测试资产已定位，算法未全量复审

需实现或改造：

- 原文件身份/digest、解析版本、映射和预览proposal固定；来源轴/单位/坐标/冲突逐行展示
- queue/actual commit都验证FrozenBinding和版本，未知按原job查询，禁止换键重复应用
- 筛选不丢完整冲突/成员清单；导入只改设计不写WMS，失败恢复保留原文件与原结果

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Space.Infrastructure/SpaceExcelCadApplyService.cs:75`：Serializable内锁floor并重读租约/内容版本，按原键读结果或创建Queued job；后续执行也须在真正commit重验FrozenBinding，不只排队时验证Draft。
- MAIN `CP6.Space.Infrastructure/SpaceDesignV1Service.cs:711`：验证写租约，在Serializable内锁floor、复核版本Draft/expected revision/hash、原batch重放；存在局部并发保护，仍需跨全部writer持久FrozenBinding及原Ready冻结语义。

验收盘点`COMPLETE`：原AC/TC 8项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SPACE-PUB-01 空间发布与WMS采纳

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

已有publish计划、WMS批次幂等和runtime物化，但legacy支持逐项部分结果；需新增源封存/正式采纳/运行生效的精确分轴证据。

可复用：

- WMS operation key+payloadhash原结果恢复
- runtime materializer对projectionHash校验及current同事务推进
- 原计划差异算法和legacy部分结果历史

需实现或改造：

- 不可变Publication/Target/成员与原locator、正int64 sourceRevision字符串，完整清单封存与一次FrozenBinding
- WMS canonical全Target接收/采纳、desired/actual不同字段；缺项KEEP，旧部分成功不改称新全成功
- runtime observed/激活单独收据，专用finalizer复核全部证据和当前S5向量后推进current
- 旧反向adopt只迁移兼容路径；恢复按原键，不从后继源重造旧封存

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Space.Infrastructure/Cp6SpaceWmsAdapter.cs:233`：按operationKey和payloadhash回原结果，事务中写操作结果；逐item校验scope/stale并继续形成部分结果（825起）。不能将legacy部分采纳称作新Target完整canonical采纳。
- MAIN `CP6.Space.Infrastructure/SpacePublishOrchestrator.Execution.cs:406`：先调用WMS ApplyBatch（含重试），后runtime.Activate（626）和完成审计（660）。旧状态编排需接不可变Publication/Target及分轴Owner回执。
- MAIN `CP6.Space.Infrastructure/Cp6SpaceRuntimeMaterializer.cs:136`：对实际projectionHash验证expectedHash后生成运行元素，将前版superseded、新版Published并设置模型current同事务。需把源封存、WMS canonical采纳和runtime observed分别证明。

验收盘点`COMPLETE`：原AC/TC 27项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SPACE-PLAN-01 布局情景、仿真与比较

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

情景分支已有Production/Published/hash前置和原request结果；应保留仿真比较能力，补新冻结/current兼容映射且始终不能直接当生产采用。

可复用：

- 固定当前发布版本创建隔离情景
- 原请求hash幂等和初始化事务

需实现或改造：

- 情景输入/模型/算法/参数版本与单位来源，比较对齐同一基线和指标口径
- 新current source/WMS/runtime三证映射以及源冻结后分支/clone权限；方案回写需受控后继而非改原源
- 仿真/建议/批准/正式WMS采纳分轴；数据不足/模型不可用明确未知，保留现有页面与算法

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Space.Infrastructure/SpacePlanningScenarioService.cs:35`：同request hash恢复原结果；事务内检查源为当前Published、Production且contentHash匹配后创建Initializing情景。保留分支基线和只读隔离，不赋予生产发布权。
- MAIN `CP6.Space.Infrastructure/SpaceDesignV1Service.cs:711`：验证写租约，在Serializable内锁floor、复核版本Draft/expected revision/hash、原batch重放；存在局部并发保护，仍需跨全部writer持久FrozenBinding及原Ready冻结语义。

验收盘点`COMPLETE`：原AC/TC 7项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SPACE-OPS-01 空间运营视图与建议

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

运营源已验证scope和published版本，数据源不可用也未伪装零；仍需正式source/WMS/runtime对齐、容量单位及S5最终输出复验。

可复用：

- 库存查询带PublishedVersion
- overview区分source unavailable

需实现或改造：

- 正式容量/库存/任务/设备的Owner来源、量纲/时点和完整版本cut，desired与actual分开
- 运营建议输入/算法/依据/人工决定、真实执行回执分轴，不直接修改WMS
- S5成员/授权完整向量和61字段映射进入分页/缓存/导出，最终输出撤权复查；部分源缺失可见

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs:47`：按scope读取库存并带PublishedVersion；overview对源不可用保留unavailable，不伪装零。需正式容量量纲、desired/actual和跨查询一致cut。
- MAIN `CP6.Space.Infrastructure/SpaceExternalPortalService.cs:145`：先获取principal/scope，验证runtime数据与site/published version一致，逐项match并按matched grants脱敏；返回AuthorizationVersion，但未见新S5完整向量最终复验。
- MAIN `CP6.Space.Infrastructure/SpaceAccessEvaluator.cs:110`：读取当前有效membership和完整active grants、policy并逐grant保留scope条件（184）；现有clause隔离可复用，但缺S5完整成员向量与输出前复查证据。

验收盘点`COMPLETE`：原AC/TC 12项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SPACE-EXT-01 外部空间访问

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

现有external evaluator已按grant保留条款，portal逐项匹配并脱敏；主要缺口是S5精确完整向量和响应/下载提交前复查。

可复用：

- membership有效期、完整active grants和逐grant clause
- 逐item准入、matched grants字段脱敏及site/published version检查

需实现或改造：

- SPACE-S5-AUTHORIZATION/1包含membership增删与完整grant/policy向量，8byte rowversion与int64保原wire
- 61字段registry精确映射，已有字段MIN后只在完整表默认1；同clause求交禁止跨grant拼scope/权限
- 门户分页/缓存/搜索/导出/download均当前读权，最终输出复验；跨DB effect无共同fence拒绝

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Space.Infrastructure/SpaceAccessEvaluator.cs:110`：读取当前有效membership和完整active grants、policy并逐grant保留scope条件（184）；现有clause隔离可复用，但缺S5完整成员向量与输出前复查证据。
- MAIN `CP6.Space.Infrastructure/SpaceExternalPortalService.cs:145`：先获取principal/scope，验证runtime数据与site/published version一致，逐项match并按matched grants脱敏；返回AuthorizationVersion，但未见新S5完整向量最终复验。

验收盘点`COMPLETE`：原AC/TC 6项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。

## SPACE-AI-01 空间AI提案审核

归属WP13；评估`CHANGE_REQUIRED`；业务交付`PLANNED`。

AI队列已有输入版本、run rowversion、ReviewEtag和原键校验；真实provider、冻结后后台提交及采用状态还需独立证明。

可复用：

- 原apply key结果恢复和队列锁
- Draft/base/current revision及审阅Etag校验

需实现或改造：

- AI输入来源/能力/provider配置与安全脱敏、预算、取消和失败结果明确；生成结果只是proposal
- 人工逐项审阅/冲突/拒绝保留原依据，变更输入使旧review失效，不越权直接调用WMS
- queue与真正apply commit同FrozenBinding/fence，旧源不可改；生成/接受/设计应用/正式采纳分轴

源码锚点（以下均须按上述固定SHA读取）：

- MAIN `CP6.Space.Infrastructure/SpaceAiAtomicApplyService.cs:46`：原键恢复，事务内锁队列并检查Draft、base/current revision、run rowversion及ReviewEtag后入队；须把持久冻结门覆盖到实际apply提交，provider运行能力还未证明。
- MAIN `CP6.Space.Infrastructure/SpaceDesignV1Service.cs:711`：验证写租约，在Serializable内锁floor、复核版本Draft/expected revision/hash、原batch重放；存在局部并发保护，仍需跨全部writer持久FrozenBinding及原Ready冻结语义。

验收盘点`COMPLETE`：原AC/TC 10项，必需补充0项。原始逐项AC已枚举，保留原断言；内联A序号继承同一行显式组前缀。SPACE按R2原始索引逐项登记，并在原ID下补挂R2子场景，不扩原AC分母。
