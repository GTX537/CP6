# CP6 CRM V1 规格基线

最后确认：2026-08-10

> 本文记录已合入 CP6 单体的 Foundation 历史基线。完整产品边界见
> [CRM-PRODUCT-FRAMEWORK.md](./CRM-PRODUCT-FRAMEWORK.md)，三仓可执行规格见
> [CRM-V1-EXECUTABLE-SPEC.md](./CRM-V1-EXECUTABLE-SPEC.md)。后两份文档获批前不得开始生产实施。

## 产品目标

CRM V1 为 CP6 补齐从获客到 ERP 订单的可审计闭环：营销官网或人工录入产生线索，进入跨部门线索池，经负责人和协作人跟进后转为商机；客户资料预登记为 BusinessPartner，内部报价被接受后才允许受控创建 ERP 订单，订单创建成功后商机才能标记赢单。

## V1 范围

- 来源仅包含营销官网和人工录入；保留首次、末次触点归因。
- 固定线索状态与商机阶段，不提供租户自定义漏斗。
- 线索采用单一负责人和多协作人；默认首次响应 SLA 为 4 个自然小时。
- 重复数据先生成候选，再由有权限的用户合并；合并必须保留来源、活动及审计记录。
- 官网使用 `/site/{siteKey}` 的租户绑定公开键，支持多语言、服务端可抓取 HTML、草稿与发布权限。
- 公开表单为紧凑 B2B 表单，不接受匿名附件；采用幂等键、限流、蜜罐、风险隔离等分层反垃圾措施。
- 商机只有在 ERP 订单成功创建并写入关联记录后才能进入 Won；报价接受只是前置条件，不代表赢单。
- 个人信息默认保留 24 个月，届时按字段策略匿名化；标准化邮箱、电话、来源 URL、IP 哈希和 User-Agent 同样按 PII 处理。

## 权限与租户边界

- 内部 CRM 数据继承 CP6 租户全局过滤和租户内唯一约束；业务访问还需叠加负责人、协作人、部门和管理员范围。
- `Crm_PublicRoute` 是唯一例外：它只保存公开标识到租户/目标的路由，不保存业务内容或 PII。解析后必须先建立租户上下文，再访问租户过滤实体。
- 菜单键固定为 `crm-dashboard`、`crm-lead`、`crm-account`、`crm-opportunity`、`crm-site`；管理员获得动作权限，但页面交付前菜单保持禁用，避免出现死链接。

## 固定业务规则

线索主链为 New -> Assigned -> Contacted -> Qualified -> Converted；New、Assigned、Contacted、Qualified 可关闭为 Disqualified，已转换、已关闭或已合并状态不能回退。商机主链为 Qualification -> NeedsAnalysis -> Proposal -> Negotiation -> Accepted -> Won，也可从活动阶段进入 Lost；Won 和 Lost 均为终态。Accepted 必须有已接受报价，Won 必须有已创建 ERP 订单。

## 分阶段交付

1. Foundation：20 张实体表、租户/PII/索引/外键边界、状态机、菜单动作目录、EF 迁移和自动化测试。
2. Intake：人工录入、公开表单、线索池、分配/移交、协作、SLA、重复候选和活动时间线 API。
3. Conversion：企业/联系人、商机、阶段历史、报价接受与 ERP 订单桥接。
4. Experience：CRM 工作台与列表/详情页面，启用相应菜单并完成权限和端到端验证。
5. Marketing site：轻量 CMS、多语言发布/回滚、公开 SSR 页面、表单与归因。
6. Operations：24 个月 PII 匿名化任务、SLA 通知、漏斗与来源报表、运维指标。

## 明确不在 V1

Excel 导入、开放 API、Webhook、客户门户、电子签名、自定义域名、可视化页面搭建器，以及除官网和人工录入之外的获客连接器，均在 V1 之后单独立项。
