# CP6 CRM 公开产品对比与业务决策基线

- 文档 ID：`CP6-CRM-COMPETITIVE-BASELINE`
- 版本：`0.1`
- 状态：**Product research baseline**
- 调研日期：2026-08-25
- 适用对象：产品、设计、架构、销售运营、实施与评审人
- 关联 PRD：[`CRM-V1-PRD.md`](./CRM-V1-PRD.md)

## 1. 目的与使用规则

本文回答三个问题：公开 CRM 产品实际上在经营什么业务，CP6 应吸收哪些模式，哪些能力不应进入 V1。

本文是产品研究证据，不是外部产品采购建议，也不覆盖 Frozen 产品主档、PRD、OpenAPI、事件 Schema 或工程规格。外部产品会持续改版；价格、套餐和 AI 能力只记录调研日可见事实，不能作为长期合同。改变 CP6 范围时必须修改 PRD 并重新评审，不能只引用竞品页面扩大实现范围。

## 2. 比较方法

不按“功能数量”排名，而按同一条业务链比较：

```text
需求进入
  -> Lead 清洗、分配和首次响应
  -> Account / Contact / Opportunity
  -> 活动与下一步行动
  -> 方案、报价、接受和 Won
  -> 订单/成交权威
  -> 来源、SLA、漏斗、预测和采用分析
```

比较维度：

1. 目标客户与首次价值时间。
2. 获客、来源、路由、SLA 和重复处理。
3. 销售工作台、活动时间线和下一步行动。
4. 商机阶段、报价、预测和赢单证据。
5. CRM 与 ERP、订单、库存、财务的权威边界。
6. 权限、数据范围、审计、扩展和集成。
7. 套餐、席位、附加能力与实施成本模式。

## 3. 市场分型

| 类型 | 代表产品 | 客户购买的主要结果 | 常见代价 |
| --- | --- | --- | --- |
| 轻量销售执行 | Pipedrive、Zoho CRM 入门版 | 快速建立 Pipeline、活动和跟进纪律 | 深度治理和复杂 ERP 协同需要扩展 |
| 增长平台 | HubSpot | 网站获客、营销培育、销售和服务共享客户时间线 | 高级自动化、报表、席位和使用量逐层收费 |
| 企业 CRM 平台 | Salesforce、Dynamics 365 | 复杂组织、权限、自动化、预测、平台扩展 | 实施、配置和管理员成本高于入门许可 |
| ERP 邻接型 CRM | Odoo、SAP Sales Cloud | 将机会、报价、订单及后台业务连续连接 | 容易与已有 ERP 产生重复交易权威 |
| 中国企业连接型 CRM | 纷享销客、销售易 | 公海、复杂组织、企微/钉钉、渠道、交易与服务协同 | 功能范围容易扩张到完整 L2C 平台 |

公开产品的共同核心不是“客户通讯录”，而是让组织持续执行一组可复用销售动作，并把这些动作与收入结果关联。差异主要来自五个控制面：数据模型、工作流、沟通入口、分析/AI 和生态集成。

## 4. 产品对比

### 4.1 Salesforce Sales Cloud

官方公开能力覆盖 Lead、Account、Contact、Opportunity、销售流程、路由、预测、报价、审批、API、数据平台和 AI。调研日 Starter 公开起价为每用户每月 25 美元；更高版本逐步加入高级预测、审批、团队销售、沙箱和平台能力。

- 适合：流程复杂、角色众多、需要平台扩展的企业。
- CP6 应吸收：稳定标准对象、服务端权限、审批/自动化边界、事件与 API 生态。
- CP6 不应照搬：V1 任意对象、任意状态、任意脚本和庞大管理员体系。
- 官方来源：[Sales Cloud](https://www.salesforce.com/sales/cloud/)、[Sales Cloud Guide](https://www.salesforce.com/sales/cloud/guide/)

### 4.2 HubSpot Sales Hub

HubSpot 以 Smart CRM 为客户记录底座，在其上连接营销、销售、服务、内容和数据产品。Sales Hub 强调潜客开发、Pipeline、销售自动化、辅导和统一客户时间线。调研日存在免费版；Professional 公开年付价约为每席位每月 90 美元并另有一次性 onboarding。

- 适合：希望网站、营销和销售快速协同的成长型团队。
- CP6 应吸收：表单到 CRM 的短链路、来源归因、统一活动时间线、低门槛激活。
- CP6 不应照搬：在 V1 同时建设完整 Marketing Hub、Service Hub 和内容增长套件。
- 官方来源：[Sales Hub](https://www.hubspot.com/products/sales)、[Sales Hub Pricing](https://www.hubspot.com/pricing/sales)、[产品与服务目录](https://legal.hubspot.com/hubspot-product-and-services-catalog)

### 4.3 Microsoft Dynamics 365 Sales

Dynamics 365 Sales 将销售自动化与 Microsoft 365、Power Platform、Copilot 和企业数据连接。调研日 Sales Professional 公开年付价为每用户每月 65 美元，Enterprise 为 105 美元。

- 适合：Microsoft 身份、邮件、日历、协作和低代码生态已成熟的企业。
- CP6 应吸收：身份与业务授权分离、邮件/日历作为可替换连接器、企业协作入口。
- CP6 不应照搬：把低代码配置当成未冻结业务语义的替代品。
- 官方来源：[Dynamics 365 Sales Pricing](https://www.microsoft.com/en-us/dynamics-365/products/sales/pricing)

### 4.4 Pipedrive

Pipedrive 以 Pipeline、活动和下一步行动为中心。调研日 Lite 公开年付价为每席位每月 14 美元；Growth 增加邮件同步、自动化和预测，Premium 增加线索路由、评分、报价、电子签名和细粒度权限。

- 适合：希望低培训成本建立销售纪律的中小团队。
- CP6 应吸收：行动优先队列、逾期可见、快速更新、处理后自动进入下一条。
- CP6 不应照搬：把可拖拽 Pipeline 当作无守卫的状态修改器。
- 官方来源：[Pipedrive Pricing](https://www.pipedrive.com/en/pricing)

### 4.5 Zoho CRM

Zoho CRM 从联系人、Lead、活动、表单和 Pipeline 扩展到预测、自定义模块、流程、库存、CPQ 和门户。调研日美元价格计算器显示 Standard、Professional、Enterprise、Ultimate 年付价分别约为每用户每月 14、23、40、52 美元。

- 适合：预算敏感、希望逐步扩展业务套件的中小企业。
- CP6 应吸收：分层套餐、受限团队用户、渐进式扩展。
- CP6 不应照搬：用大量模块覆盖代替清晰的制造业售前主链。
- 官方来源：[Zoho CRM Pricing](https://www.zoho.com/en-us/crm/zohocrm-pricing.html)、[Pricing Calculator](https://www.zoho.com/crm/zohocrm-pricing-calculator.html)

### 4.6 Odoo CRM

Odoo CRM 支持邮件和网站表单获客、线索去重、分配、活动计划、Pipeline、预测和报价，并可继续连接销售、库存与财务应用。

- 适合：准备使用同一应用套件覆盖 CRM 与 ERP 的企业。
- CP6 应吸收：网站/邮件入站、活动计划、机会到报价的连续用户体验。
- CP6 必须避免：在 CRM 再建一套正式客户、报价、订单、库存和财务权威。
- 官方来源：[Odoo CRM](https://www.odoo.com/app/crm)、[CRM Features](https://www.odoo.com/app/crm-features)、[CRM Documentation](https://www.odoo.com/documentation/16.0/applications/sales/crm.html)

### 4.7 SAP Sales Cloud

SAP Sales Cloud 面向企业销售自动化、引导式销售、预测、移动销售和外勤拜访，并强调与后台数据和 Lead-to-Cash 的连接。调研日公开价格页面显示每用户每月 136 美元，最终合同和实施费用需按客户确认。

- 适合：复杂制造、全球组织、外勤销售和 SAP 后台生态。
- CP6 应吸收：ERP 邻接治理、移动拜访、复杂组织和版本化集成。
- CP6 不应照搬：在 Lead Pilot 前引入预测 AI、复杂外勤和大型实施模型。
- 官方来源：[SAP Sales Cloud](https://www.sap.com/products/crm/sales-cloud.html)、[Features](https://www.sap.com/products/crm/sales-cloud/features.html)、[Pricing](https://www.sap.com/products/crm/sales-cloud/pricing.html)

### 4.8 纷享销客

纷享销客公开产品强调多渠道线索、线索池、公海、客户 360°、商机、CPQ、订单、进销存、渠道与服务，并提供装备制造场景。价格按版本、用户和能力组合询价，公开页面不形成可直接比较的标准席位价。

- 适合：依赖国内移动协作、渠道和复杂销售组织的企业。
- CP6 应吸收：公海/回收、跨团队移交、企微连接、经销与区域组织模式。
- CP6 不应照搬：把 CRM 扩张为第二套进销存和财务系统。
- 官方来源：[纷享销客 CRM](https://www.fxiaoke.com/ap/sem-crm/)、[销售管理](https://www.fxiaoke.com/ap/product-xs/)、[帮助中心](https://help.fxiaoke.com/)

### 4.9 销售易

销售易公开产品覆盖智能拓客、线索、公海、客户、商机、产品价格、报价、订单、回款、企微/钉钉和复杂组织，并强调企业级 SFA 与制造业客户案例。标准价格未在调研页面公开。

- 适合：需要国内连接生态、复杂组织、CPQ 和 L2C 管理的企业。
- CP6 应吸收：线索资源流转、价格折扣协同、多维目标和本地协作入口。
- CP6 不应照搬：让 CRM 自由修改 ERP 价格、订单或回款事实。
- 官方来源：[销售易销售云](https://www.xiaoshouyi.com/enterprise)、[营销云](https://www.xiaoshouyi.com/yxy)

## 5. 横向结论

| 业务能力 | 市场成熟做法 | CP6 产品结论 |
| --- | --- | --- |
| 获客 | 表单、邮件、导入、营销活动和连接器进入统一客户底座 | V1 固定 Website、Manual、Import；连接器以后只能走 ingestion contract |
| Lead 执行 | 路由、评分、公海、活动、提醒和下一步行动 | V1 先做分配、SLA、重复候选和客户面对型 Activity；评分与培育后置 |
| 客户结构 | Account、Contact、Lead、Opportunity 是通用稳定对象 | 保留稳定对象语义；转换必须原子、幂等、可追溯 |
| Pipeline | 看板、阶段、概率、预测和指导式销售 | 标准阶段 code 固定；允许 label、默认 probability 和 guidance，不允许租户改语义 |
| 报价与成交 | 从 CRM 内报价或连接 ERP/CPQ | CP6 ERP 持有正式 BP、Quotation、Order；外部成交使用不可变证据 |
| 分析 | 来源、活动、转化、预测、销售绩效 | V1 先锁定来源、SLA、转换、赢单、ERP 成功率和数据截至时间 |
| 扩展 | 自定义对象/字段、工作流、应用市场和 AI | V1 只开放受控字段类型、版本化 API/event 和连接器边界 |
| 收费 | 席位分层，叠加 onboarding、用量、AI、营销联系人或模块 | CP6 商业模型由 Portal 决定；不得按 Lead 收费导致用户少录或绕过 Intake |

## 6. CP6 产品决策登记

| 决策 ID | 决策 | 竞品依据 | PRD 落点 |
| --- | --- | --- | --- |
| `CRM-COMP-001` | 第一可见结果为行动优先的 Lead Pilot，不先做 Dashboard | Pipedrive 的活动优先、HubSpot 的快速激活 | `PRD-GOAL-002`、`PRD-UI-001`～`008` |
| `CRM-COMP-002` | V1 使用稳定标准对象和状态，不开放任意对象/状态/脚本 | Salesforce 平台能力强但治理成本高；Zoho 以模块广度扩展 | PRD 8、13、17 节 |
| `CRM-COMP-003` | 网站来源、分配和首次响应必须形成冻结 SLA 与审计链 | HubSpot/Odoo 强调入站连接；国内 CRM 强调线索池和流转 | `PRD-GOAL-001`、旅程 A/D、KPI |
| `CRM-COMP-004` | CRM 拥有售前意图，ERP 拥有正式交易；Won 必须有合法权威 | Odoo/SAP 展示 CRM-ERP 连续性，也暴露重复权威风险 | PRD 5、6.7、8.3、验收 012～015 |
| `CRM-COMP-005` | 公海、邮件/日历、企微/钉钉、移动拜访进入 VNext，不进入 Lead Pilot | 纷享销客、销售易、Dynamics、SAP | PRD 3.2、13、15 节 |
| `CRM-COMP-006` | AI 先用于摘要和建议，不能拥有状态、权限或成交权威 | 各企业产品正将 AI 加入销售执行 | V1 非目标；以后仍通过既有命令和人工确认 |
| `CRM-COMP-007` | 套餐优先按组织/席位/能力/配额组合，不按录入 Lead 数量制造漏记激励 | 市场以席位和能力层级为主，另叠加用量和 onboarding | Portal Entitlement Snapshot；价格仍待商业评审 |

## 7. V1、VNext 与明确不做

### 7.1 V1 必须形成差异的能力

- Website/Manual/Import 进入同一可审计 Lead 语义。
- 风险提交先进入 PublicSubmission 隔离，不把所有公开输入直接当 Lead。
- 分配、首次响应、业务日历和 SLA 锚点被冻结并可复算。
- Lead Pilot 让销售不离开当前上下文完成分配与首次响应。
- 转换、Merge、阶段推进和成交使用 ETag、幂等键及事务守卫。
- Won 由 CP6 ERP 成功订单或不可变 ExternalSaleRecord 支持。
- Organization、DataScope、PII、Entitlement、Audit 在 Web、移动、报表和导出保持同义。

### 7.2 VNext 候选

优先顺序只表示产品研究建议，不构成开工授权：

1. 邮件、日历、企业微信/钉钉连接器。
2. 公海、回收、区域与经销商协作。
3. 活动计划、线索培育和受控评分。
4. 移动拜访、路线和离线只读。
5. 销售预测、摘要和下一步建议。
6. 与 ERP CPQ/报价审批更深的只读和命令协同。

### 7.3 V1 明确不做

- 第二套 ERP 订单、库存、交付、应收和回款权威。
- 任意自定义对象、租户脚本、自由阶段和自由工作流。
- 完整 Marketing Automation、客服云、现场服务或应用市场。
- 自动改变 Owner、状态、价格、成交权威或 PII 权限的 AI Agent。
- 以 Dashboard、AI 演示或功能菜单数量替代 Lead Pilot 的真实采用结果。

## 8. 商业与实施假设

公开产品显示，软件标价不是 CRM 的完整成本。数据迁移、身份/邮件连接、流程设计、管理员、培训、支持、AI/用量和 ERP 集成都会改变总成本。CP6 后续商业评审至少要分别验证：

- 基础组织费是否覆盖站点、数据隔离、审计和最低支持成本。
- 销售席位、受限协作席位和只读/审计席位如何区分。
- ERP 集成、Import/Export、移动、连接器和高级报表属于套餐还是附加能力。
- 中国和北美设计伙伴对每组织价格、每席位价格及 onboarding 的接受度。
- 套餐是否鼓励完整记录真实 Lead，而不是诱导用户绕开系统。

价格模型在完成设计伙伴访谈和成本测算前保持 **TBD**。本文不为 Portal 写入具体价格。

## 9. 后续验证与更新触发器

以下任一条件出现时，应更新本文并检查 PRD 是否受影响：

1. 设计伙伴证明公海、渠道或移动拜访是 Lead Pilot 的进入门槛。
2. CP6 ERP 报价/订单合同改变 `OrderAuthorityMode` 或 Won 证据。
3. Portal 确定正式套餐、席位和配额模型。
4. 新连接器需要改变 Website/Manual/Import 的稳定来源语义。
5. 竞品研究超过 12 个月，或用于正式定价/采购决策。

每次更新记录调研日期、官方来源、变化内容和受影响的 `CRM-COMP-*` / `PRD-*` ID。外部营销文案、案例数字和临时促销不得直接成为 CP6 验收标准。
