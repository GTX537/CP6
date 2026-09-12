# C03 ERP 集成

C03 提供 CP6 侧的真实 ERP 查询、BP/Order 命令处理、事务消息与人工恢复能力。CRM 的配套改动提供相同事件契约和服务查询客户端；隔离 transport probe 验证真实 SQL、C01 服务令牌及 Kafka/Dapr 请求和结果。CRM Account、ErpLink、IntegrationProcess、Opportunity → Won 的产品流程仍由 CRM05/06/07 等任务实现，不能用本次 transport probe 代替其验收。

## ERP 数据与操作

业务权威继续使用 `T_WebBusinessPartner`、`T_Quotation`/`T_QuotationDetail`、`T_ProductMaster` 和 `T_Order`/`T_OrderDetail`。`erp_integration` 中的表只记录命令、结果、重试和审计，不保存另一套法定客户或订单。

CRM 事件只携带引用及校验值。ERP 必须先有完成真实主数据校验的客户预登记记录。ERP 员工通过专用操作绑定 `CrmAccountId`，随后 BP 命令可将该记录登记为正式客户。没有 ERP 主数据时返回 `C03_BUSINESS_PARTNER_MASTER_DATA_REQUIRED` 或 `C03_BUSINESS_PARTNER_NOT_FOUND`；不会从 CRM 显示名构造法定名称、税号、地址或账期。

| ERP 员工操作 | 权限 | 输入和效果 |
| --- | --- | --- |
| `PUT /api/business-partners/{key}/commerce-profile` | `erp-business-partner/edit` | 当前 `rowVersion`、`crmAccountId`、`currency`、`isFrozen`；已有 Account 绑定不可替换 |
| `PUT /api/quotations/{key}/commerce-terms` | `erp-quotation/edit` | 当前 `rowVersion`、币种、UTC 有效期、受注区分、交期；撤销旧的客户接受记录 |
| `POST /api/quotations/{key}/customer-acceptance` | `erp-quotation/confirm` | 当前 `rowVersion` 和 `acceptanceReference`；绑定已批准报价的客户、价格、明细及交易条款内容哈希 |
| `DELETE /api/quotations/{key}/customer-acceptance` | `erp-quotation/confirm` | 当前 `rowVersion`；撤销客户接受 |

这些操作使用现有 ERP 用户认证和权限检查，并拒绝服务主体。内部审批标记不等于客户接受。普通 BP/Quotation DTO 的新增字段用于读回，原有更新接口不能越过专用操作修改交易权威；报价内容修改或取消审批会清除接受记录。SQL decimal scale、日期 Kind 或相同 UTC 时刻的不同 offset 不改变接受内容哈希。

订单处理再次锁定并检查 BP、已接受报价、SQL rowversion、金额、币种及产品映射。报价明细号以四位 `Branch1` 映射到同客户、同报价的唯一已批准 ProductMaster，再使用现有 OrderService 的产品、工程及材料映射。没有映射、存在歧义或审批未完成均返回业务终态错误。仅支持现有 set/individual 价格模式；其他模式须先完成 ERP 支持。非基轴币种必须有有效汇率，汇率随真实订单冻结。

客户接受和订单转换均限制最多 500 个报价明细，并拒绝达到 `10^13` 的单行金额：报价 `decimal(18,2)` 可表示的部分值不能装入订单 `decimal(21,8)`。这些情况作为业务错误处理，不以不变输入反复技术重试。有效区间的真实 SQL 边界由专门回归覆盖。当前产品/工程/材料查找沿用 ERP 逐行读取；大报价吞吐和长期历史数据规模尚未作为生产性能验收，批量读取优化保留为后续工作。

## 同步查询与认证

CRM 经固定 Dapr app ID `cp6-core` 调用：

| 路由 | 返回内容 |
| --- | --- |
| `GET /internal/erp/v1/business-partners/{key}` | 实际客户 ID/key、Account 引用、显示名、启用/冻结、币种、版本 |
| `GET /internal/erp/v1/quotations/{key}` | 实际报价及客户引用、审批、金额/币种、当前客户接受、有效期、版本 |
| `GET /internal/erp/v1/orders/{key}` | 实际订单及客户引用、CRM 请求来源、报价引用、实际明细金额合计、币种/状态、版本 |

每次调用必须使用独立校验的 C01 `CP6.Services` RS256 token，并携带单值 `tenantid`、`correlationid`。API 重查启用的 C01 客户端、组织、租户以及 SQL 中未撤销且未过期的服务令牌记录；`ReaderClientIds` 是 ERP 专用 allowlist。资源查询显式限定 token 租户，错误租户不能通过 query 参数或 metadata 覆盖。响应不缓存。

CRM `ErpReadClient` 按租户使用已登记的 confidential-client 凭据，通过 C01 获取短期服务令牌。它限制固定目标、响应大小、重复字段及响应资源身份；收到 401/403 后清除该租户的缓存令牌。网络失败、错误响应或无法确认报价权威时向调用方返回失败。

每个租户有独立的令牌刷新锁。读取的整体截止时间覆盖等待、签发和 ERP 响应正文，最多十秒，并服从更短的 HttpClient 配置；调用方主动取消保持取消语义。合同允许的冒号编号经 URL 编码传递，客户端和 ERP 校验规则一致。

## 请求、事务和版本

六个事件及其正负例位于 [ERP 合同包](../../contracts/events/erp/contract-bundle.v1.json)，Core 和 CRM 保存相同字节。请求使用 `cp6.crm.events.v1`，结果使用 `cp6.erp.events.v1`；分区键为 `tenantid:aggregateid`。BP aggregate 是 Account ID，Order aggregate 是 Opportunity ID。所有结果重复请求身份并带单调递增的 `resultVersion`。

- CommandInbox 保留原 CloudEvent ID 和 payload hash；同 ID 不同内容被拒绝并记录冲突，不执行业务。
- 请求 journal 的业务键为租户、BP/Order 类型、Account/Opportunity、`requestVersion`；不同 CloudEvent ID 的相同业务请求复用已持久化结果。
- 实际订单的唯一索引限制同租户 Opportunity 最多一张订单，包含跨请求版本和软删除情形。
- Inbox、journal、业务数据、成功 Outbox 和待处理 bridge 在一个 SQL Serializable 事务中提交。订单写入后、Outbox 写入前失败会回滚订单、明细和 ORD 序号。
- ORD 序号使用 SQL 锁与现有 ERP/补单调用统一竞争。已有事务时加入该事务；普通调用没有外层事务时单独保留序号，因此允许空号。
- 只有提交后的 bridge worker 才调用现有 WMS/MES hooks。事务内不会发送外部副作用；本次隔离 transport 只核验持久化 bridge，不签署 WMS/MES 下游验收。

| 结果类别 | 示例 | 处理 |
| --- | --- | --- |
| Terminal business | 报价失效/变更、客户冻结、金额/币种不符、主数据或 FX 缺失 | 持久化终态；修正 ERP 后必须发新 `requestVersion` |
| Retryable technical/contention | SQL 暂时不可用、锁竞争、传输失败 | 回滚业务并按原消息/原请求版本退避重试 |
| 已成功请求的重复消息 | 同业务键及相同规范化输入 | 返回原结果，不创建第二张订单 |
| 较旧请求/结果 | 版本低于已处理版本 | 拒绝旧请求或由消费者忽略旧结果，保留证据 |

## 启用配置与投递

默认 `ErpIntegration:Enabled=false`。启用前必须已有 C01/C02、真实租户映射及记录式服务令牌存储；普通 SQL 连接须关闭 MARS 以保留事务 savepoint。配置项为：

| Core 配置 | 要求 |
| --- | --- |
| `ErpIntegration:Tenants` | 与 C02 一致的非空 tenant GUID → region 映射 |
| `ErpIntegration:ReaderClientIds` | 显式列出的启用 C01 客户端，租户已登记且允许 `cp6.services` |
| `ErpIntegration:DaprHttpEndpoint` / `DaprGrpcEndpoint` | 本地 sidecar 根地址，无凭据、query 或 fragment |
| `ErpIntegration:DaprApiToken` | 出站 sidecar `DAPR_API_TOKEN` |
| `ErpIntegration:DaprAppToken` | 入站回调 `APP_API_TOKEN`；与 API token 不同，均为 32–4096 个无空白/控制字符的字符 |
| `MaxInboxAttempts` / `InitialRetrySeconds` / `MaximumRetrySeconds` | 有界 durable retry；默认 10 次、1 秒起、最多 300 秒 |

CRM 配置为 `CrmErp:Enabled`、`DaprHttpEndpoint`、`DaprApiToken`、`Tenants[]` 的 `TenantId/ClientId/ClientSecret`；issuer 继承 `CrmAuth`。每个租户同时属于配置的 CRM storage organization 和 C02 identity projection。凭据只通过私有环境配置注入。

Core 的 `GET /dapr/subscribe` 与精确 `POST /internal/erp/v1/events` 校验 app token。事件入口还验证 topic、pubsub 名、分区键、Content-Type、大小和完整合同。仅这个事件路径豁免 Cookie CSRF；相邻管理路由仍受保护。

订阅声明 `cp6.crm.deadletter.v1`。配置死信主题时，Dapr 默认可在首次投递失败后转入死信，因此必须同时配置有界 inbound retry；参见 [Dapr 官方说明](https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-deadletter/)。CRM transport fixture 的 `pubsub-resiliency.yaml` 将投递设为每秒重试、最多五次。应用 durable retry 负责已进入 CommandInbox 的原消息；传输层未送达应用的死信仍须由环境的死信订阅/运维恢复流程保留原 ID 和 payload 后重新投递，不能改写业务结果。

数据库由 Core 一次性 db-init 按 `CP6Context` 迁移链前向迁移；其中 owner migrations 创建 `erp_integration` 表。不要对已由 Core 初始化的数据库另行运行 `ErpIntegrationContext.Database.Migrate()`。新增权威字段为空的历史报价不会被自动认定为客户已接受。

## 故障恢复与验证边界

ERP 管理员通过 `GET /api/erp-integration/deadletters` 查看当前租户的 CommandInbox 死信 metadata，并以 `POST /api/erp-integration/deadletters/{messageId}/replay` 提交 `operationId`、列表返回的 `rowVersion`/`payloadSha256` 及 `dependency-recovered` 或 `contract-verified` 理由。重放只调度原消息，不接受替换 payload；重复 operation 必须具有相同 actor、输入版本及理由。审计与重排同事务，过期版本返回 409。业务终态错误通过新请求版本处理。

结果 Outbox 与提交后的 bridge 均在十次失败后停止自动执行。`GET /api/erp-integration/delivery-deadletters` 返回当前租户最多 100 条 metadata：`kind=result` 的 `targetId` 是 Outbox 记录 ID，`kind=bridge` 的 `targetId` 是原订单 key。使用同样的重放输入调用：

- `POST /api/erp-integration/delivery-deadletters/results/{outboxId}/replay`：检查原结果合同、hash、租户及未重放的 Platform deadletter，原地恢复 Pending，保留 CloudEvent ID、payload 和结果版本。
- `POST /api/erp-integration/delivery-deadletters/bridges/{orderKey}/replay`：检查原租户订单已存在、bridge 确实耗尽且无活动 lease，与 worker 使用相同锁恢复待处理状态，不重新创建订单。

两个入口只允许当前租户的 ERP 管理员用户，拒绝服务凭据；`DeliveryReplayAudit` 保存操作身份、理由、旧 rowversion/hash 和尝试次数。审计写入失败时整个重排回滚。桥接列表中的 hash 是原 tenant/orderKey 的稳定标识哈希，不能替换成新的订单或 payload。

SQL 验证见 [ERP SQL suite](../../eng/crm/erp-integration-tests/README.md)，真实数据初始化与运维测试驱动见 [ERP transport fixture](../../eng/crm/erp-live-fixture/README.md)。[本地七场景原始传输证据](../evidence/c03/2026-09-12/transport-attempt-3/README.md)保存实际失败、运行二进制/合同哈希、初始化 baseline、请求/Inbox/Order/Outbox 关联和最终 SQL 对账；后续新增代码由其独立定向验证覆盖，原传输结果不改称后来重跑。生产候选仍须满足 R2、真实业务旅程、迁移和环境门禁；C04A/C04B 的数据与切换前置未因本次代码交付而关闭。
