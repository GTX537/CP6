# CP6 CRM V1 产品需求文档

<!-- crm-v1-prd-status: Approved -->

- 文档 ID：`CP6-CRM-V1-PRD`
- 版本：`0.2`
- 状态：**Approved product requirements baseline**
- 日期：2026-08-26
- 产品决策源：[`CP6-SAAS-V1` Frozen 产品主档](https://github.com/GTX537/CP6.CRM/blob/main/docs/product/CP6-SAAS-V1-PRODUCT-FREEZE.md)
- 产品决策摘要：`e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b`
- 公开工程契约：`CP6-SAAS-V1-PUBLIC-CONTRACT` / `Complete` / `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`
- 审批状态：**ProgramOwner approved exact payload digest**
- M0：**No-Go**
- 工程约束源：[`CRM-V1-EXECUTABLE-SPEC.md`](./CRM-V1-EXECUTABLE-SPEC.md) 与私有仓 `CP6-SAAS-V1-SYSTEM-SPEC` Candidate
- 当前实现基线：[`CRM-V1-SPEC.md`](./CRM-V1-SPEC.md)
- 公开产品研究基线：[`CRM-COMPETITIVE-ANALYSIS.md`](./CRM-COMPETITIVE-ANALYSIS.md)

<!-- crm-v1-prd-payload:start -->

## 0. 文档地位与使用规则

本 PRD 把已冻结的 SaaS V1 产品方向转换为 CRM 可评审、可拆票、可验收的前后端产品合同。它回答四个问题：用户看到什么、用户能完成什么、后端必须保证什么、未来升级时哪些语义不得破坏。

本版本是等待精确摘要批准的产品候选，不表示已经允许开发、迁移或上线。Public Contract Sync 已为 Complete，M0 仍为 No-Go。评审通过后，必须把状态改为 `Approved product requirements baseline`，记录唯一 ProgramOwner 的不可变审批证据、候选 commit 和 blob；此后任何改变 V1 范围、状态语义、商业规则或数据主权的修改都必须升版本并重新审批。

权威顺序如下：

1. Frozen 产品主档决定产品范围和商业政策。
2. 本 PRD 决定 CRM 用户行为、前后端协作和产品验收口径。
3. System Spec、OpenAPI、事件 Schema 和数据库设计决定工程实现。
4. 当前 CP6 Foundation 代码只提供迁移来源与已验证兼容语义，不代表目标服务已经存在。

公开 `CP6-SAAS-V1-PUBLIC-CONTRACT` 已完成与 Frozen 产品主档的脱敏同步；若本 PRD 与该公开合同或私有 Frozen 主档冲突，必须先形成新版本和重新审批，不得选择性实现任一版本。本 PRD 不覆盖 M0、Security、Privacy、Release 或真实环境门禁。

## 1. 产品摘要

CP6 CRM 是面向包装及相邻离散制造企业的售前工作台。它把网站、人工录入和受控 CSV 导入的需求转成可追踪 Lead，再转成 Account、Contact、Opportunity，最终以 CP6 ERP 订单或不可变外部成交证据形成可审计 Won 结果。

长期 V1 是完整 SaaS 产品，不是单个后台页面：

- CRM Web 管理台和租户公开站点。
- 每组织独立 CRM 数据库。
- CP6 Portal 注册、组织、成员、Billing、Entitlement 和 App Launcher 协作。
- CP6 ERP 或 External Evidence 两种成交权威。
- Web、React Native 移动端、五语言、导入导出、隐私与采用门禁。

第一个可见交付不是 Dashboard，而是 **Lead Pilot 分栏处理台**。左侧给出按 SLA 风险排序的可行动队列，右侧在同一上下文完成分配、客户响应和 Contacted 推进。复杂历史、查重、转换和窄屏操作进入详情页。

### 1.1 产品结果

CRM V1 必须让用户从一条真实需求走到合法成交依据，并让管理者回答：

- 需求从哪里来？
- 谁在什么时候接手？
- 是否在业务时间 SLA 内完成首次响应？
- 为什么被判定合格、丢失、合并或淘汰？
- 这笔 Won 由 CP6 ERP 订单还是外部成交证据支持？
- 每一步是否遵守租户、DataScope、PII、Entitlement 和审计规则？

### 1.2 北极星指标

北极星指标是“可追溯来源且有合法成交依据的 Won 金额”，必须按 `OrderAuthorityMode` 和币种分别报告。V1 不做 FX 换算，不把不同币种合计成一个误导性总额。

## 2. 当前事实、目标与差距

| 维度 | 当前 `main` 事实 | V1 目标 | 本 PRD 的处理 |
| --- | --- | --- | --- |
| 数据 | CP6 单体 `CP6Context` 已有 20 个 CRM/CMS DbSet | 每组织独立 CRM SQL Server 数据库 | 20 表只作为迁移源，目标模型按组织、复合外键和 Outbox 重建 |
| 领域 | Lead/Opportunity 状态机和 Accepted/Won 守卫已存在 | 加入 PublicSubmission 完整处置、OrderAuthorityMode、ExternalEvidence、Entitlement | 保留稳定状态代码，补齐目标不变量 |
| 权限 | 6 个菜单节点、22 个动作已种子化且默认禁用 | Web、移动、报表、导入导出共享服务端授权 | 不新增临时 CRM 动作，复用既有目录与 DataScope |
| API | 没有 CRM Controller 或可用业务 API | 版本化管理 API、同源公开 BFF、CloudEvents | 本 PRD 定义产品级资源和命令，逐字段合同由 OpenAPI 冻结 |
| Web | 当前 Vue 中没有 CRM 路由 | `CP6.CRM` Next.js 管理台与公开站点 | 不在旧 Vue 中建立第二套 CRM UI |
| 移动 | 不存在 | React Native 功能等价客户端 | 在 Web GA 后 30 日内完成移动 GA |
| 商业化 | 当前 CRM Foundation 不含 Portal/Billing | Portal 为 Commerce、Billing、Entitlement 权威 | CRM 只消费版本化 Entitlement Snapshot |
| 发布 | Foundation 测试通过，不等于产品完成 | 四仓同 digest、真实门禁和采用证据 | 菜单、部署或技术绿灯都不能单独关闭 Epic |

### 2.1 公开产品研究到 CP6 决策

公开 CRM 研究只用于检验产品取舍，不改变 Frozen 产品主档和本 PRD 的权威顺序。完整证据、调研日期和官方来源见 [`CRM-COMPETITIVE-ANALYSIS.md`](./CRM-COMPETITIVE-ANALYSIS.md)。

| 决策 ID | 公开产品观察 | CP6 决策 | V1 影响 |
| --- | --- | --- | --- |
| `CRM-COMP-001` | 轻量 CRM 通过活动、逾期和下一步行动缩短首次价值时间 | 第一可见结果保持 Lead Pilot 行动队列，不先做 Dashboard | 固定 `PRD-GOAL-002`、`PRD-UI-001`～`008` |
| `CRM-COMP-002` | 企业 CRM 依靠任意对象、工作流和生态扩张，但实施治理成本高 | V1 使用稳定对象、状态和原因 code，扩展走版本化合同 | 任意对象、状态、脚本继续是非目标 |
| `CRM-COMP-003` | 增长型和 ERP 邻接型 CRM 都把入站来源连接到 Lead/Opportunity | Website/Manual/Import 统一进入可审计来源、SLA 和活动链 | 固定旅程 A～D 和来源/SLA KPI |
| `CRM-COMP-004` | CRM 与 ERP 越接近，越需要明确报价、订单和财务权威 | CRM 拥有售前意图；CP6 ERP 或 External Evidence 提供 Won 权威 | 固定数据主权和 Accepted/Won 守卫 |
| `CRM-COMP-005` | 国内企业 CRM 和大型套件重视公海、企微/钉钉、渠道和移动拜访 | 这些能力保留为 VNext 连接器/协作扩展，不阻塞 Lead Pilot | 不扩大 V1 首个切片 |
| `CRM-COMP-006` | 市场正把 AI 加入摘要、评分、预测和销售动作 | AI 不拥有权限、状态、价格或成交权威；V1 不做 AI | 后续 AI 仍须调用同一受守卫命令并由用户确认 |
| `CRM-COMP-007` | 市场主要按席位和能力分层，再叠加使用量、实施或 AI 成本 | 商业模型由 Portal 冻结；不得按 Lead 数量制造漏记激励 | 价格保持 TBD，CRM 只消费 Entitlement Snapshot |

## 3. 目标、非目标与成功边界

### 3.1 V1 目标

| ID | 需求 |
| --- | --- |
| `PRD-GOAL-001` | Website、Manual、Import 三条来源进入同一 Lead 语义，并保留可审计来源。 |
| `PRD-GOAL-002` | 销售在 Lead Pilot 中以不超过两次主操作完成分配和首次客户响应记录。 |
| `PRD-GOAL-003` | Lead 到 Account/Contact/Opportunity 的转换原子、幂等、可追溯。 |
| `PRD-GOAL-004` | Opportunity 只能以 CP6 ERP 成功订单或不可变 ExternalSaleRecord 进入 Won。 |
| `PRD-GOAL-005` | 租户站点、CMS、Offering 和公开表单形成受控获客闭环。 |
| `PRD-GOAL-006` | Portal 的 Membership、Permission、Billing 和 Entitlement 变化可在 CRM 中正确生效。 |
| `PRD-GOAL-007` | Web、移动、报表、搜索、导入导出使用相同租户、DataScope、PII 和配额规则。 |
| `PRD-GOAL-008` | 所有关键动作提供幂等、并发冲突、失败恢复和审计证据。 |

### 3.2 V1 非目标

- 匿名购买、在线结账或客户自助交易门户。
- 第三方 marketplace、跨租户客户数据搜索或将未来模块业务代码放入 CRM。
- 营销自动化、广告/邮件/日历/电话连接器和开放 Webhook。
- AI 评分、AI 生成、公式/脚本字段、任意工作流和电子签名。
- 任意页面构建器、租户 JavaScript/CSS/原始 HTML、公开表单附件。
- FX 汇总、跨区域统一身份、跨区域业务数据主动迁移或主动-主动业务库。
- 移动端注册、付款、价格展示、外部购买链接或 OTA 代码更新。

### 3.3 完成边界

以下结果不能单独称为 CRM V1 完成：

- Foundation 表或状态机存在。
- API、菜单、页面或镜像已部署。
- 单元测试、Mock ERP 或合成迁移通过。
- Web GA 完成但移动 GA、设计伙伴或采用门禁未完成。

V1 Epic 只有在产品冻结、M0、四仓合同、设计伙伴、技术候选、Web GA、移动 GA、Lead Adoption、Full Journey Adoption 和 90 天采用门禁全部通过后才可关闭。

## 4. 用户、角色与授权结果

| 用户 | 核心任务 | 用户不应获得的能力 |
| --- | --- | --- |
| 公开访客 | 浏览租户站点、Offering、提交需求、查询粗粒度回执 | 查看内部 Lead、风险、负责人、拒绝原因或租户标识 |
| 销售代表 | 处理本人/协作范围 Lead、记录活动、转换、推进商机 | 读取范围外数据、绕过阶段、直接创建 ERP 订单 |
| 销售主管 | 查看团队队列、分配/移交、处理重复和 SLA 风险 | 仅因管理角色自动解密全部 PII |
| Sales Operations | 配置 Intake、业务日历、SLA、原因字典和采用口径 | 修改法定 ERP 主数据或账单权威 |
| 市场/内容 | 管理站点、Offering、表单、内容和来源 | 读取无业务需要的销售 PII |
| CRM 管理员 | 配置 CRM、导入、角色映射和功能可见性 | 绕过 Entitlement、DataScope、状态机或审计 |
| Privacy/Audit | 处理 DSAR、保留、Legal Hold、导出和审计 | 通过导出扩大自身业务 DataScope |
| Organization Owner/Admin | 从 Portal 管理组织、成员、套餐和应用 | 直接写 CRM 数据库或 ERP 交易表 |
| 平台支持 | 通过有 TTL 的 SupportSession 处理工单 | 通用 impersonation、SQL 直写、绕过客户审计 |

### 4.1 CRM 权限目录

V1 复用当前 22 个 CRM 动作：

- `crm-dashboard:query`
- `crm-lead:query|add|edit|assign|merge|convert|view-pii`
- `crm-account:query|add|edit|view-pii`
- `crm-opportunity:query|add|edit|accept-quote|create-order|view-pii`
- `crm-site:query|edit|publish|configure`

导入任务按所含对象要求相应 `add` 权限；导出要求对象 `query`，未遮罩 PII 还要求对应 `view-pii` 和近期 MFA。Offering、表单与媒体使用 `crm-site` 权限。不得为单个页面临时增加第 23 个 CRM 动作。

### 4.2 服务端授权顺序

每个读取和写入必须按以下顺序在服务端执行：

```text
有效身份和区域
  -> OrganizationContext
  -> Membership 启用状态
  -> Entitlement / 配额
  -> 菜单动作权限
  -> DataScope predicate
  -> 字段级 PII 权限
  -> 聚合状态与并发版本
  -> 命令执行和审计
```

前端隐藏按钮只是体验优化，不能代替任一步后端校验。跨租户 ID 和范围外 ID 使用统一 Not Found 语义，避免通过 403/404 差异探测资源。

## 5. 产品边界与数据主权

| 领域 | 权威系统 | CRM 可以做什么 | CRM 不得做什么 |
| --- | --- | --- | --- |
| 身份、组织、成员、角色目录 | `CP6` | 消费身份和授权投影 | 自行签发身份或把权限写入 JWT |
| Billing、Payment、Subscription、Entitlement | `CP6.Portal` | 消费版本化 Entitlement Snapshot，显示状态和限额 | 人工激活付款、修改价格或成为账单权威 |
| Lead、Account、Contact、Opportunity、Activity | `CP6.CRM` | 完整拥有售前数据和状态机 | 跨组织数据库写入或把法定交易主数据复制为权威 |
| BP、Quotation、Order、财务交易 | `CP6` ERP | 只读校验并异步请求 BP/Order | 伪造法定报价、订单或直接写 ERP 库 |
| ExternalSaleRecord | `CP6.CRM` | 为非 ERP 组织保存不可变成交证据 | 把它展示为 ERP Order |
| 公开目录、注册、App Launcher | `CP6.Portal` | 提供 CRM 应用入口和状态 | 在 CRM 内复制第二套 Portal |
| 租户站点、CMS、Offering、表单 | `CP6.CRM` | 管理获客内容和发布投影 | 提供任意代码注入或在线商品交易 |

四仓只通过版本化 API 和 CloudEvents 协作，不跨服务写数据库。

## 6. 端到端产品旅程

```text
Portal 注册/Entitlement
          |
          v
租户站点 -> PublicSubmission -> 风险隔离/释放 -> Lead Pilot
                                                |
Manual -----------------------------------------+
Import -----------------------------------------+
                                                v
                         Assigned -> Contacted -> Qualified
                                                |
                                                v
                                  Account + Contact + Opportunity
                                                |
                       +------------------------+------------------------+
                       |                                                 |
                    Cp6Erp                                       ExternalEvidence
                       |                                                 |
            Quotation accepted -> Order succeeded             ExternalSaleRecord
                       +------------------------+------------------------+
                                                |
                                               Won
```

### 6.1 旅程 A：网站访客到 Lead

1. 访客通过区域域名访问已发布租户站点。
2. `siteKey` 只用于解析组织与发布投影；未知、禁用或未发布路由返回中性 404。
3. 浏览器向同源 Next.js BFF 提交，不直达 CRM 写 API。
4. BFF 校验 Origin/CSRF、attempt Cookie、Body 大小、发布中的 Form/Intake/Calendar/Privacy 版本。
5. CRM 在一个事务内冻结 `ReceivedAtUtc`、Intake、BusinessCalendar、SLA、风险结果、加密 PII、Audit 和 Outbox。
6. 正常提交直接转换为 Lead；风险提交进入 Quarantine，释放前不是 Lead。
7. BFF 把回执秘密写入有界加密 `__Host-cp6-receipts` Cookie，浏览器只得到 `receiptId` 和粗粒度状态。

### 6.2 旅程 B：人工 Lead

1. 有 `crm-lead:add` 的用户填写公司、联系人、主题、来源说明和归属建议。
2. 前端生成 `Idempotency-Key`；重复点击或未知网络结果重放同一命令。
3. 后端规范化重复候选，但不自动合并。
4. 创建成功进入 Lead Pilot 队列；未指定合法 Owner 时进入可见的 Intake 异常队列，SLA 不暂停。

### 6.3 旅程 C：CSV 导入

1. 用户选择 Account、Contact、Lead 或 Opportunity 模板并上传 CSV。
2. 系统拒绝公式、宏、未知列、跨组织引用和未声明自定义字段。
3. 预检返回总行数、可导入、警告、阻断、重复候选和配额影响，确认前不写业务数据。
4. 确认后异步执行，每行结果可下载；同一任务重放不重复创建资源。
5. 来源固定为 `Import`，保留原始来源文本；不计算历史 SLA，不进入 Website 采用分母。

### 6.4 旅程 D：Lead 分配与首次响应

1. 默认队列包含 DataScope 内尚未完成首次响应的 New/Assigned Lead，以及无合法 Owner 的可见异常；Contacted、Qualified 和终态不进入默认 Pilot 队列。
2. 队列优先级为：已超时、未分配且临近分配 SLA、已分配且临近首次响应 SLA、其他可行动 Lead。
3. `effectiveDueAtUtc` 对未分配 Lead 取 `assignmentDueAtUtc`，对已分配且未响应 Lead 取 `firstResponseDueAtUtc`；同一优先级按 effective due、received/created time、leadId 升序，确保结果稳定。
4. Website Lead 的首次响应 SLA 从原始 ReceivedAt 按冻结 BusinessCalendar 计算；Manual Lead 从 CreatedAt 计算；Import 不补算历史 SLA。分配 SLA 从 Lead 被创建或 quarantine release 的时间开始。
5. 用户在右侧选择 Owner/Department、响应方式和响应摘要。
6. 分配和 Activity 保持两个显式后端命令。前端用分配成功响应中的新 ETag 提交 Activity；第二步失败时明确显示分配已保存，并保留尚未提交的响应草稿。
7. Call、Email、Meeting、CustomerMessage 计为客户面对型活动；Note 和 System 不计首次响应。
8. 第一条客户面对型 Activity 在同一事务内只设置一次 `FirstResponseAtUtc`，追加 Activity/Audit/Outbox，并按状态机推进为 Contacted。
9. 412 时保留用户输入，拉取新快照，展示字段差异；用户确认后使用新 ETag 显式重试。

### 6.5 旅程 E：重复、合并与淘汰

- 系统只生成重复候选，不自动合并。
- Merge 同时要求 source/target ETag、原因和 Idempotency-Key。
- 后端在一个事务内复核两个 Lead 的组织、可见性、状态和版本，迁移允许的引用，匿名化 source PII，写 MergeRecord/Audit/Outbox。
- 任一版本过期整笔返回 412，不产生半合并。
- Disqualify、Merge 都要求标准 reason code 和可选租户 label；终态不可恢复。

### 6.6 旅程 F：Lead 转换

- 只有 Qualified Lead 可以转换。
- 用户可以创建或关联 Account/Contact，并填写 Opportunity 基本信息。
- 后端在一个 CRM 数据库事务内创建或关联 Account、Contact、Opportunity，写 StageHistory/Audit/Outbox，并把 Lead 置为 Converted。
- 并发重复转换返回首次创建的相同结果，不创建第二个 Opportunity。

### 6.7 旅程 G：Opportunity 到 Won

1. Opportunity 使用固定语义阶段，租户只能改 label、默认 probability 和 guidance。
2. 进入 Proposal 前必须选择并冻结 `OrderAuthorityMode`；Accepted 后不可更改。
3. `Cp6Erp` 模式在 Accepted 前校验已接受且与当前 Opportunity 版本匹配的 ERP Quotation；Order 请求通过 IntegrationProcess 异步执行。
4. `ExternalEvidence` 模式在 Accepted 前记录已接受的外部报价事实；Won 前创建不可变 ExternalSaleRecord，至少含外部引用、金额、ISO 4217 币种、成交日期和证据附件 manifest/hash。
5. 只有当前聚合版本对应的 ERP Order `Succeeded` 或有效 ExternalSaleRecord 才能进入 Won。
6. Won、Lost 为终态。重新销售必须新建 Opportunity 并引用原记录。

### 6.8 旅程 H：套餐降级与取消

- Portal 发出 Entitlement 变化，CRM 更新区域本地投影。
- 降级先阻止新的超限写入，不删除已有数据；读操作仍按商业时间线开放。
- 支付失败第 8 天进入只读；取消或试用到期的只读、导出和删除时间线以 Frozen 产品主档为准。
- UI 必须说明被阻止的是套餐、配额、权限还是系统故障，不混用通用 403。

## 7. 信息架构与前端需求

### 7.1 一级导航和所有权

| 一级入口 | 主要路由 | 核心任务 | 首个切片 |
| --- | --- | --- | --- |
| Dashboard | `/crm/dashboard` | 漏斗、来源、SLA、集成积压、数据截至时间 | Lead Pilot 后 |
| Leads | `/crm/leads`、`/crm/leads/{id}`、`/crm/leads/intake` | 队列、分配、响应、历史、重复、转换 | **第一优先** |
| Accounts/Contacts | `/crm/accounts`、`/crm/accounts/{id}` | 售前企业、联系人、活动与 ERP Link | 完整旅程 |
| Opportunities | `/crm/opportunities`、`/crm/opportunities/{id}` | 阶段、报价、订单请求、外部证据 | 完整旅程 |
| Site/CMS | `/crm/site`、页面/表单/Offering 子路由 | 站点、内容、发布、媒体、表单和目录 | 完整旅程 |

Organization、Membership、Billing、Subscription、Entitlement 和 App Launcher 位于 Portal，不在 CRM 一级导航复制。

### 7.2 Lead Pilot 分栏工作台

桌面宽屏的主视图固定为两栏：

```text
┌──────────────────────────────┬─────────────────────────────────────────┐
│ 可行动 Lead 队列             │ 当前 Lead 快速处理                      │
│ SLA 状态 / Owner / 来源      │ 公司与联系人摘要 / PII 遮罩状态         │
│ 稳定排序 / 筛选 / 搜索       │ 分配 / 响应方式 / 响应摘要 / 提交        │
│ 当前选择与键盘导航           │ 版本、冲突、成功与下一条                │
└──────────────────────────────┴─────────────────────────────────────────┘
```

| ID | 前端要求 |
| --- | --- |
| `PRD-UI-001` | 首屏先显示可行动队列，不以 Dashboard、看板或营销卡片替代。 |
| `PRD-UI-002` | 队列项至少显示 Subject、Company、Source、Owner、状态、SLA 剩余/超时和收到时间。 |
| `PRD-UI-003` | 默认只包含 DataScope 内未完成首次响应的 New/Assigned Lead 和无合法 Owner 异常；筛选不能扩大 DataScope。 |
| `PRD-UI-004` | 右栏同时完成分配和首次响应记录；成功后保留筛选并自动选择下一条最高优先级 Lead。 |
| `PRD-UI-005` | 复杂历史、查重、合并、转换和完整编辑进入详情页，不把右栏扩成第二个详情页。 |
| `PRD-UI-006` | 低于 1280 px 使用单栏队列加详情路由，不机械压缩双栏。 |
| `PRD-UI-007` | 支持键盘上下移动队列、进入处理区、返回队列；焦点可见且不会因自动刷新丢失。 |
| `PRD-UI-008` | 列表刷新时若当前项仍可见则保持选择；若被他人处理则显示状态变化并选择下一条，不静默丢失草稿。 |

### 7.3 详情页要求

Lead 详情页包含摘要、状态/SLA、Owner/Collaborators、活动时间线、来源触点、重复候选、审计摘要和转换入口。默认只请求首屏必要数据，长时间线游标分页。无 `view-pii` 时，姓名只保留首字符，邮箱和电话使用不可反推原值的局部掩码。

Opportunity 详情页必须把 `OrderAuthorityMode`、阶段守卫和成交依据放在同一决策区。用户不能通过普通编辑表单直接修改 Stage、Owner 权威字段或成交依据。

### 7.4 Dashboard 与报表

- Dashboard 显示漏斗、首次响应 SLA、来源、未分配、IntegrationProcess 积压和 `asOfUtc`。
- 首触点和末触点分开展示，不合并成单一归因真相。
- 报表按币种和 OrderAuthorityMode 分段，不做 FX 总计。
- Partial Report 必须显示缺失范围、数据截至时间、事件积压和重试状态。
- 所有钻取复用原报表的 Organization、DataScope 和 PII predicate。

### 7.5 Site/CMS 与公开站点

- 首页内容顺序：价值/提交需求、信任证据、核心能力、行业场景、合作流程、案例/资质、最终 CTA、页脚。
- 主 CTA 使用“提交需求”，次 CTA 使用“查看能力”；不得暗示即时自动报价。
- 视觉采用暖纸背景、深墨正文、单一 CP6 青绿行动色、真实材料摄影和克制刀模线。
- 禁止紫蓝渐变、通用 SaaS 三卡片首屏、轮播和装饰性大圆角。
- CMS 只允许固定模板、命名槽位、批准区块和有限变体；禁止任意 HTML、CSS 和 JavaScript。
- 内容支持 `zh-CN`、`zh-TW`、`en`、`ja`、`ko`，按 locale 手工草稿、预览、批准和发布，不自动机器翻译发布。
- 发布和回滚都创建新发布记录，不修改历史 Revision。

### 7.6 移动端

- React Native 客户端复用同一 OpenAPI 客户端、状态语义、权限、DataScope 和冲突处理。
- 写操作必须在线并携带 Idempotency-Key/ETag；近期只读缓存字段级加密，24 小时过期，撤权后清除。
- Push 只含 opaque notification ID，登录后再拉取内容；通知 payload 不含客户名、金额、Lead 或 PII。
- Web GA 后 30 日内完成移动 GA。移动延期不阻止 Web GA，但阻止 V1 Epic 关闭。

### 7.7 统一页面状态

| 状态 | 用户看到什么 | 前端行为 | 后端语义 |
| --- | --- | --- | --- |
| Loading | 保留布局的骨架 | 不显示上一组织数据 | no-store 管理读取 |
| Empty | 当前筛选无任务和合法下一步 | 仅有权限时显示 CTA | 空集合，不用 404 |
| Forbidden | 无权执行或字段遮罩 | 不泄露资源存在性 | 403 或字段级 mask |
| Not Found | 中性不存在页面 | 不区分不存在、跨租户、范围外 | 统一 404 |
| Conflict | 服务端快照和字段差异 | 保留草稿，用户显式重试 | 412，零部分写入 |
| Integration Pending | 处理中和支持引用号 | 禁止重复提交，可轮询/事件刷新 | 202/Requested/Processing |
| Integration Failed | 稳定错误、可重试性、支持引用号 | 仅 Retryable 显示重试 | Retryable 或 Terminal |
| Partial Report | 截止时间和缺失范围 | 允许查看已验证部分 | `asOfUtc` + backlog 状态 |

### 7.8 可访问性、国际化和缓存

- 目标 WCAG 2.2 AA，正文至少 16 px，触控目标至少 44 px。
- 所有交互可用键盘，表单错误与字段关联，颜色不是唯一状态信号。
- `/crm/**` 使用 `no-store`，不得跨用户或组织共享缓存。
- 已发布站点页面可 ISR；发布事件按组织、站点、页面和 locale 精确重验证。
- 预览、回执、导出和所有带 token 页面使用 `private, no-store`、`no-referrer`、`noindex`。

## 8. 后端领域与状态合同

### 8.1 PublicSubmission

```text
Accepted -> ConvertedToLead
    |
    +-> Quarantined -> ConvertedToLead
                     -> Rejected -> Anonymized
                     -> Expired  -> Anonymized
```

| ID | 不变量 |
| --- | --- |
| `PRD-SUB-001` | 风险隔离对象在 release 前不是 Lead，不进入 Lead 列表或 Lead KPI 分母。 |
| `PRD-SUB-002` | 提交事务冻结 ReceivedAt、IntakeConfig、BusinessCalendarVersion、SLA Due 和隐私版本。 |
| `PRD-SUB-003` | release、reject、expiry 对同一 RowVersion 竞争，只允许一个成功。 |
| `PRD-SUB-004` | release 创建 Lead 时保留原 ReceivedAt 作为首次响应 SLA anchor；已超时则立即 breach。 |
| `PRD-SUB-005` | 公开回执只映射为 received、reviewing 或中性不可用，不返回内部处置。 |

### 8.2 Lead

允许的目标迁移为：

- `New -> Assigned | Contacted | Disqualified | Merged`
- `Assigned -> Contacted | Disqualified | Merged`
- `Contacted -> Qualified | Disqualified | Merged`
- `Qualified -> Converted | Disqualified | Merged`
- `Converted`、`Disqualified`、`Merged` 为终态。

状态迁移只由命令执行，普通 PATCH 不能直接写 Status、Owner、FirstResponseAt 或转换字段。

### 8.3 Opportunity

允许的目标迁移为：

- `Qualification -> NeedsAnalysis | Lost`
- `NeedsAnalysis -> Proposal | Lost`
- `Proposal -> Negotiation | Accepted | Lost`
- `Negotiation -> Proposal | Accepted | Lost`
- `Accepted -> Negotiation | Won | Lost`
- `Won`、`Lost` 为终态。

Accepted 和 Won 守卫按 `OrderAuthorityMode` 解释：

| 模式 | Accepted 前置 | Won 前置 |
| --- | --- | --- |
| `Cp6Erp` | 当前 Opportunity 版本匹配的 ERP Quotation 已接受 | 当前版本的 ERP Order IntegrationProcess 为 Succeeded |
| `ExternalEvidence` | 已记录外部报价接受事实和来源 | 已创建不可变 ExternalSaleRecord，金额/币种/日期与 Opportunity 一致或有审计差异原因 |

租户可以配置阶段显示标签、默认概率和指导文案，但 API、事件、报表和数据库继续使用固定标准 code。

### 8.4 IntegrationProcess

`Requested -> Processing -> Succeeded | Retryable | Terminal`，`Retryable -> Processing`。旧聚合版本的结果不得覆盖新状态；相同命令重放返回原过程，不创建第二个 BP、Order 或成交结果。

### 8.5 只追加记录

StageHistory、MergeRecord、ExternalSaleRecord、Audit、Inbox、发布证据和关键 Integration attempt 只追加。更正通过补偿或新版本完成，不修改历史事实。

## 9. 前后端动作合同

| 用户动作 | 前端提交 | 权限/范围 | 后端原子结果 | 成功后的 UI | 失败处理 |
| --- | --- | --- | --- | --- | --- |
| 人工建 Lead | Idempotency-Key + 表单 | lead:add | Lead、SourceTouch、Audit、Outbox | 进入队列并显示重复候选数 | 409 幂等冲突；422 字段错误 |
| 分配/移交 | ETag、Idempotency-Key、Owner/Dept、reason | lead:assign + DataScope | Assignment history、Lead、Audit、Outbox | 更新 Owner/SLA，选择下一条 | 412 保留选择和原因 |
| 记录首次响应 | ETag、Idempotency-Key、activity | lead:edit + DataScope | Activity、FirstResponseAt、Contacted、Audit、Outbox | 时间线出现活动，SLA 关闭 | 任一步失败零写入 |
| Qualify | ETag、Idempotency-Key | lead:edit | Lead、StageHistory、Audit、Outbox | 显示转换 CTA | 422 显示缺失资格字段 |
| Disqualify | ETag、Idempotency-Key、reason code | lead:edit | 终态、历史、审计 | 从可行动队列移除 | 412/422 保留原因 |
| Merge | source/target ETag、Idempotency-Key、reason | lead:merge + 两端 DataScope | 引用迁移、source 匿名化、MergeRecord、Audit、Outbox | 跳转 target，显示合并摘要 | 任一冲突整笔 412 |
| Convert | ETag、Idempotency-Key、Account/Contact/Opp 选择 | lead:convert | Account、Contact、Opportunity、Lead、历史、审计、Outbox | 打开 Opportunity | 重放返回相同 ID |
| 阶段推进 | ETag、Idempotency-Key、target、reason | opportunity:edit | Stage、history、audit、outbox | 时间轴和概率更新 | 守卫失败 422 |
| 接受报价 | ETag、Idempotency-Key、quotation evidence | opportunity:accept-quote | 接受事实、StageHistory、Audit、Outbox | 显示 Accepted | 过期/不匹配 422 |
| 请求 ERP Order | ETag、Idempotency-Key | opportunity:create-order | IntegrationProcess、Outbox、Audit | Pending，按钮禁用 | Retryable/Terminal 分开显示 |
| 记录外部成交 | ETag、Idempotency-Key、evidence manifest | opportunity:create-order | ExternalSaleRecord、Audit、Outbox | 可进入 Won | 证据缺失/不一致 422 |
| 公开提交 | signed attempt + payload | 公开限流/风险 | Submission、receipt、Audit、Outbox | 中性已收到页面 | 429/503，不假报成功 |
| Release | ETag、Idempotency-Key | lead:add + Intake scope | Submission、Lead、Audit、Outbox | 从审核队列移到 Lead | 412 零写入 |
| Reject | ETag、Idempotency-Key、reason | lead:edit + Intake scope | Submission 终态、Audit | 从审核队列移除 | 412/422 保留原因 |
| 发布页面 | ETag、Idempotency-Key、revision | site:publish | Publication、route projection、Audit、Outbox | 显示发布版本和时间 | 发布校验失败继续服务旧版 |
| 回滚页面 | ETag、Idempotency-Key、revision、reason | site:publish | 新发布记录指向历史 revision | 公网站点重验证 | 不修改历史 revision |
| 确认导入 | Idempotency-Key、preflight version | 对象 add 权限 + quota | ImportJob、Audit、Outbox | 进度和逐行结果 | 预检过期需重新确认 |
| 请求导出 | Idempotency-Key、筛选、近期 MFA | query/view-pii + DataScope | ExportJob、Audit | 短时授权下载 | 过期后重新生成，不重发旧 URL |

## 10. HTTP、并发、幂等与错误

### 10.1 产品级 API 表面

管理 API 使用 `/api/crm/v1`，公开浏览器只调用同源 BFF。逐字段 request/response、长度、nullability、operationId 和示例由 OpenAPI 定版。

| 资源组 | 必需产品动作 |
| --- | --- |
| Leads | list/get/create/patch、assignments、activities、collaborators、qualify、disqualify、duplicates、merge、conversion |
| Intake | public-submissions list/get/pii、release、reject |
| Accounts/Contacts | list/get/create/patch、contacts、ERP link request |
| Opportunities | list/get/create/patch、stage transitions、accepted quotation、ERP order request、external sale record、integration status/retry |
| Site/CMS | sites、pages、revisions、preview、publish、rollback、media、forms、offerings |
| Operations | dashboard、funnel/source/SLA reports、imports、exports、audit summary |
| Public | published content/forms、same-origin submission、receipt status |

### 10.2 幂等规则

- 创建和有副作用的命令要求 `Idempotency-Key`；缺失返回 428。
- 幂等作用域至少包含 Organization、Caller、Endpoint 和 Key。
- 同 key、同规范化 payload 返回首次状态、资源 ID 和语义结果。
- 同 key、不同 payload hash 返回 409，零业务写入。
- 未知网络结果必须重放原 key，不生成新 key 猜测结果。

### 10.3 并发规则

- 可变资源返回 ETag；更新和状态命令要求 `If-Match`，缺失返回 428。
- ETag 过期返回 412，且不写 Activity、History、Audit、Idempotency 成功记录、Outbox 或聚合部分状态。
- Merge 同时校验 source/target 版本；转换、发布和导入确认还校验其引用的配置/预检版本。

### 10.4 错误合同

所有错误使用 RFC 9457 `application/problem+json`，至少包含稳定 `code`、`traceId`、`correlationId` 和可本地化 message key，不包含 PII、连接串、堆栈或 ERP 原始负载。

| HTTP | 产品语义 |
| --- | --- |
| 400 | 协议或 JSON 结构错误 |
| 403 | 身份已知但无权执行非资源探测动作 |
| 404 | 不存在、跨组织或 DataScope 外资源的统一语义 |
| 409 | Idempotency-Key 与 payload 冲突，或唯一业务事实冲突 |
| 412 | 资源版本过期 |
| 422 | 结构合法但领域守卫、配额或发布配置不满足 |
| 428 | 缺少 Idempotency-Key 或 If-Match |
| 429 | 公开或管理请求限流 |
| 503 | CRM/DB 不可用且不能安全接收；带 Retry-After |

## 11. 数据、租户、隐私与 Entitlement

### 11.1 每组织数据库

- 每个 Organization 使用独立 CRM 数据库、凭据/托管身份和备份恢复边界。
- 业务表仍保存 `OrganizationId`，以复合外键和运行时上下文防止错误路由后的越权。
- CRM Router 只接受 Gateway 建立的 OrganizationContext；客户端不能提交数据库名或连接字符串。
- Schema 由 CRM Migrator 前向管理；应用回退必须证明当前 Schema 兼容。

### 11.2 PII

- PII 字段加密，搜索使用独立受限规范化索引。
- PII 不进入 URL、日志、Trace、事件、缓存键、错误、Push、分析或不受控 HTML。
- 默认保留 24 个月；Tenant Privacy Admin 可在 6 至 60 个月内配置。Legal Hold 需要 reason、Owner 和 expiry。
- 匿名化后，业务指标可复算，但姓名、邮箱、电话、来源 URL、IP 哈希和 User-Agent 不可恢复。
- DSAR 通过公开申请、身份验证和 Privacy Admin 工作流处理，不自动删除。

### 11.3 Entitlement 与配额

- Portal 是 Entitlement 权威；CRM 命令事务冻结 `EntitlementVersion`。
- 配额在后端执行，前端显示当前用量、上限和升级入口，但不能只靠隐藏按钮。
- Growth/Scale 自定义字段由版本化字段定义和类型化值驱动。V1 只允许文本、数字、日期、布尔、单选、多选，不允许公式和脚本。
- 降级不删除已有记录；超限时停止新的增量写入，并返回稳定套餐/配额错误。

### 11.4 审计和支持访问

高风险读取和所有写入记录 Actor、SupportSession、Organization、Action、Resource、Result、masked diff、Correlation 和 UTC 时间。支持访问必须显示在客户审计页，且继续经过正常 API、状态机、DataScope、PII、Outbox 和审计。

## 12. 事件与跨服务一致性

- CloudEvents 1.0 是唯一事件 envelope；`data` 使用版本化 JSON Schema。
- 业务数据与 Outbox 同事务提交；消费者以 `(ConsumerName, MessageId)` Inbox 幂等。
- 相同 MessageId 不同 payload hash 进入安全告警/DLQ，不猜测处理。
- 消息至少一次、可能重复、乱序；聚合 version 阻止旧事件覆盖新状态。
- 事件 subject 和 data 不含 PII。必需扩展包含 tenant、correlation、causation、aggregate、version、schema、region。

关键事件包括 Membership/Permission、Entitlement、PublicSubmission、Lead Created/Converted、Opportunity Accepted/Won、ERP Order Requested/Succeeded/Failed 和 PII Anonymized。

## 13. 可升级性合同

以下边界用于以后升级现有功能，不允许 V1 实现把未来扩展锁死。

| 扩展点 | V1 固定语义 | 后续可扩展方式 | 禁止方式 |
| --- | --- | --- | --- |
| 来源渠道 | Website、Manual、Import | 新连接器映射到版本化 ingestion contract | 连接器直接写 Lead 表 |
| Lead 状态 | 7 个稳定 code | 租户 label、指导和自动化建议 | 新增租户自定义状态改变报表语义 |
| Opportunity 阶段 | 7 个稳定 code | label、默认概率、guidance | 删除/重排标准语义或直接改 Stage |
| 成交权威 | Cp6Erp、ExternalEvidence | 新 major 合同增加新 authority | 用自由文本或截图伪造 ERP Order |
| 自定义字段 | 6 种受控类型 | 版本化字段定义、类型化值和索引策略 | 每个字段动态改物理表或执行租户脚本 |
| 原因字典 | 平台标准 code | 租户 label 映射 | 报表按任意 label 汇总 |
| CMS | 固定模板/区块 | 新受审区块版本 | 任意 HTML/CSS/JS |
| API | `/api/crm/v1` + OpenAPI | 向后兼容 minor；breaking change 升 major | 私自增加未登记命令别名 |
| 事件 | CloudEvents + schema major | 消费者声明支持范围 | 猜测未知 major |
| 套餐 | Entitlement Snapshot | Portal 发布新版本能力/配额 | 前端散落价格和套餐 if 判断 |
| 数据库 | 每组织独库 + Migrator | 前向版本迁移、兼容读取 | 双写旧库或跨服务写库 |
| 客户端 | Web/移动共用生成客户端 | 新 UI 复用同一产品命令 | 客户端复制状态机或离线写入 |

每个新功能在拆票前必须回答：它属于哪个权威系统、使用哪个稳定状态/原因 code、如何幂等、如何处理 412、如何遵守 DataScope/PII/Entitlement、需要哪个 API/event major、如何迁移旧数据。

## 14. KPI、SLO 与产品门禁

### 14.1 业务 KPI

| KPI | 口径 |
| --- | --- |
| 分配达标率 | Eligible Lead 在套餐/租户分配 SLA 内有合法 Owner，异常队列单列 |
| 首次响应达标率 | FirstResponseAt 不晚于冻结 BusinessCalendar 计算的 SlaDueAt；Merged、测试和未到期样本按批准口径排除 |
| Lead 合格率 | Qualified / 非隔离且非 Merged Lead |
| Lead 转换率 | Converted / Qualified，以转换时间分桶 |
| 商机赢单率 | Won / (Won + Lost)，按 OrderAuthorityMode 和币种分段 |
| 来源 Won 金额 | 首触点与末触点分别汇总，不做 FX 混合 |
| ERP 成功率 | Succeeded / 终态 IntegrationProcess，按操作和错误码分组 |
| 激活率 | 邀请同事、发布站点、收到真实提交、分配并记录首次响应全部完成 |

### 14.2 技术 SLO

- 正常管理 API p95 小于 300 ms。
- 公开 Intake p95 小于 500 ms。
- 门禁负载窗口 5xx 小于 0.1%。
- 区域核心技术可用性目标 99.9%。
- 权限撤销 99% 在 30 秒内生效。
- Outbox 正常 p95 在 10 秒内发布；Pilot Kafka 恢复后 10 分钟清空积压。

### 14.3 Lead Pilot UAT

- 8 至 12 名销售、2 个部门、至少 2 名主管。
- 至少 120 个固定任务，每人至少 10 个。
- Website/Manual 各至少 20、重复候选至少 15、跨部门移交至少 15、无 Owner 至少 10、并发冲突至少 10、SLA 跨工作时段至少 10。
- 正常路径无引导完成率至少 90%。
- 成功 normal 任务 median 不超过 60 秒，p90 不超过 120 秒。
- 预期拒绝和恢复结果 100%，租户隔离和 PII 隔离 100%，无开放 P0/P1 或主流程 P2。

### 14.4 上市与采用

- 顺序：CP6 自用、3 家中国和 3 家北美设计伙伴、公开 Web GA、30 日内移动 GA。
- Lead Adoption：至少 10 个工作日且至少 200 条 Eligible Lead；Website/Manual 100% 进入新 CRM，旧写入为 0，至少 90% 在 30 分钟内分配或进入可见异常队列，至少 85% 在 4 个业务小时内首次响应。
- Full Journey：最多 30 个自然日，至少 20 个 Conversion 和 10 个 OrderRequest；自然发生的转换和订单请求都在 CRM 完成，ERP 零丢失/重复，报表与 canonical SQL 100% 对账。
- GA 后 90 天：signup 到 activation 至少 35%，trial 到 paid 至少 15%，90 日付费 Logo retention 至少 85%，weekly active org 至少 60%，支持首次响应达标至少 90%。

## 15. 交付切片与依赖

| 切片 | 可见产品结果 | 进入条件 | 退出条件 |
| --- | --- | --- | --- |
| P0 产品与治理 | PRD、公开合同、M0 权威统一 | Frozen 产品主档 | PRD Approved、Public Sync 完成、M0 Go |
| P1 平台基础 | 身份、组织、权限、Entitlement、每组织 DB、事件合同可用 | P0 | 真实两租户负向、SQL/Kafka/Dapr 合同通过 |
| P2 Lead Pilot | Website/Manual Intake、分栏台、分配、响应、冲突恢复 | P1 + Observation Gate | Pilot UAT 和性能 Smoke 通过 |
| P3 完整 CRM Web | Accounts/Contacts、转换、Opportunity、ERP/ExternalEvidence、Dashboard | P2 | 全旅程 UAT 通过 |
| P4 Site/Portal/Commerce | 注册、套餐、Billing、站点、CMS、Offering、Import/Export | 对应四仓合同 | 设计伙伴和 Web GA 通过 |
| P5 Mobile/Adoption | React Native、Push、商店分发、采用门禁 | Web GA | 移动 GA、Lead/Full Journey、90 日门禁通过 |

工程任务的精确仓库顺序、DRI、证据和发布依赖以 Delivery Plan 为准。本表只定义用户可见切片，不授权跨仓单票或提前启用菜单。

## 16. 验收场景

| ID | 场景 | 必须结果 |
| --- | --- | --- |
| `PRD-AC-001` | 同一公开 attempt 和 payload 重复 10 次 | 1 个 Submission、最多 1 个 Lead、1 组语义事件 |
| `PRD-AC-002` | 同 key 不同 payload | 409，零业务写入 |
| `PRD-AC-003` | 租户 A 猜测租户 B ID | 列表、详情、导出、缓存、事件均无泄露 |
| `PRD-AC-004` | 无 view-pii 的全量 DataScope 用户 | 仍只能看到遮罩 PII |
| `PRD-AC-005` | Quarantine release 与 reject 并发 | 仅一方成功，另一方 412，零部分写入 |
| `PRD-AC-006` | release 时原始 SLA 已超时 | Lead 创建后立即显示 breach |
| `PRD-AC-007` | 两个浏览器页同时分配 | 旧页 412，保留 Owner 和 reason 草稿 |
| `PRD-AC-008` | 第一条 CustomerMessage 活动 | Activity、FirstResponseAt、Contacted、Audit、Outbox 同事务成功 |
| `PRD-AC-009` | Note 活动 | 不设置 FirstResponseAt，不误推进 Contacted |
| `PRD-AC-010` | Qualified Lead 并发转换两次 | 返回相同 Account/Contact/Opportunity，不重复创建 |
| `PRD-AC-011` | Merge 任一 ETag 过期 | source、target、timeline、audit、outbox 全部不变 |
| `PRD-AC-012` | Cp6Erp 无接受报价进入 Accepted | 422 `CRM_ACCEPTED_QUOTATION_REQUIRED` |
| `PRD-AC-013` | Cp6Erp ERP Order 未成功进入 Won | 422 `CRM_ORDER_REQUIRED_FOR_WON` |
| `PRD-AC-014` | ExternalEvidence 无 ExternalSaleRecord 进入 Won | 422，阶段不变 |
| `PRD-AC-015` | ERP 回执重复或乱序 | 不创建第二订单，不让旧结果覆盖新版本 |
| `PRD-AC-016` | 发布新 CMS Revision | 60 秒内公开投影更新，失败继续服务旧版 |
| `PRD-AC-017` | 回执 Cookie 缺失、过期、篡改或错配 | 相同中性 Not Found，不泄露 Submission 存在性 |
| `PRD-AC-018` | Entitlement 降级且当前记录已超新配额 | 读取保留，新建返回稳定配额错误，不删除旧数据 |
| `PRD-AC-019` | CSV 包含公式、未知列或跨组织引用 | 预检阻断，确认前业务表零写入 |
| `PRD-AC-020` | 导出包含 PII | recent MFA、view-pii、DataScope 全部生效，短时下载可审计 |
| `PRD-AC-021` | Push 通知 | 系统通知 payload 中无客户名、金额、Lead 或 PII |
| `PRD-AC-022` | 切换后已有新系统写入 | 禁止旧库覆盖或双写，只允许前向修复 |

## 17. 需求追踪与变更规则

每张实施票至少引用一个 PRD ID，并在测试或证据中建立可复核链接：

```text
PRD requirement
  -> OpenAPI / Event Schema / Domain invariant
  -> implementation ticket
  -> automated test or controlled evidence
  -> release/adoption manifest
```

以下改动必须回到产品评审，不得只改代码或 OpenAPI：

- 增删 V1 渠道、角色、一级导航或商业套餐能力。
- 改变 Lead、Opportunity、PublicSubmission、IntegrationProcess 状态语义。
- 改变 OrderAuthorityMode、Accepted/Won 证据或数据主权。
- 改变公开回执、PII、保留、支持访问或区域边界。
- 降低 Pilot、设计伙伴、GA 或采用门禁。

以下改动可在不改变 PRD 的前提下进入工程规格：

- 不改变用户结果的内部类名、表名、索引和查询计划。
- 满足同一 SLO、错误和兼容合同的实现优化。
- OpenAPI 的向后兼容可选字段，前提是不会改变权限、状态或商业语义。

## 18. 产品评审清单

本 PRD 的产品批准证据必须明确同意以下五个结论：

1. 长期 V1 使用 Frozen SaaS 四仓边界，Portal/移动/双区域/商业化属于产品 V1，但按门禁分切片交付。
2. 第一可见结果是 Lead Pilot C 分栏工作台，不以 Dashboard 或全菜单铺开替代。
3. 前端不复制状态机、权限、DataScope、PII 或 Entitlement；所有写入使用后端命令、幂等和 ETag。
4. Opportunity 同时支持 Cp6Erp 与 ExternalEvidence，但 Won 必须有合法且不可变的成交依据。
5. 后续升级通过版本化 API、事件、字段定义、原因 code 和连接器边界扩展，不破坏稳定业务语义。

Public Contract Sync 已完成；产品批准后仍必须取得 M0 Go。M0 未完成时，产品批准仍不构成实施或上线授权。
<!-- crm-v1-prd-payload:end -->

## 产品批准证据

- 批准角色：`ProgramOwner`
- 批准决定：`Approved product requirements baseline`
- 批准 payload SHA-256：`128bda13277a50fa024c8912676d7ed9e842fd6837b7de11d6055eb8e176fc53`
- 候选 commit：`ef29aef21ee241d0af49808ec16299d0b66395e3`
- 候选 PRD blob：`b91af0e69d95aa78c8151bae17b3ef02c04a5d92`
- GitHub 评论证据：[PR #33 comment 5422991497](https://github.com/GTX537/CP6/pull/33#issuecomment-5422991497)
- 批准时间：`2026-08-26T09:00:05Z`
- 规范化评论正文 SHA-256：`9ac80797566fe3a456cf7f74ae32a476c431ff5c1bdda7fa448b9adbaa0dfa92`
- Append-only 历史记录：[2026-08-26 ProgramOwner approval](./approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner.json)
- M0 保持：`No-Go`；本批准不授权 CRM01、实现、迁移、部署、Pilot、UAT 或生产。

## 相关文档

- [CRM 文档入口](./README.md)
- [CRM 公开产品对比与业务决策基线](./CRM-COMPETITIVE-ANALYSIS.md)
- [PRD 审批聚合记录](./approvals/cp6-crm-v1-prd.json)
- [CRM 产品框架](./CRM-PRODUCT-FRAMEWORK.md)
- [CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md)
- [CRM V1 Foundation 基线](./CRM-V1-SPEC.md)
- [CP6 DevOps 入口](../devops/README.md)
- [项目当前状态](../project-memory/PROJECT_STATE.md)
