# 正式退货申请意图 v1

W224 采用 WMS-RMA-01 第三次修订的正式申请合同：原始归档 `fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json`，195 个定义。应用入口为 `/api/wms/rma/v1`，与已有 `native-applications-v1` 分开。W225 在 `/wms/rma-applications` 采用正式接口；早期申请及其原操作恢复保留在 `/wms/rma-applications/native-v1`，两页互相提供入口。

## 本轮可用操作

| 方法与路径 | 行为 |
| --- | --- |
| GET `context` | 当前可见仓库/站点选项及动作能力 |
| POST `original-fulfilment-options/search` | 签发不可变履约选择，冻结分页成员和观察时间 |
| POST `applications` | 创建 DRAFT 意图 |
| GET `applications` | 按可信范围、客户、申请状态及记录时间筛选；冻结成员和头版本的稳定游标分页 |
| GET `applications/{applicationId}/edit-context` | Owner 编辑上下文扩展，返回申请及原 Remarks；无字段读权限立即拒绝，避免覆盖隐藏内容 |
| PUT `applications/{applicationId}` | DRAFT 编辑，版本及强 ETag 比较 |
| POST `applications/{applicationId}/submit` | DRAFT → SUBMITTED |
| POST `applications/{applicationId}/withdrawals` | DRAFT/SUBMITTED → WITHDRAWN |
| GET `applications/{applicationId}?version=...` | 当前或历史申请版本；本轮限定读取，不代表完整 Q03 工作台 |
| POST `commands/query` | 查询原操作 FOUND 或 NOT_OBSERVED；后者不能推断无效果 |

请求遵循正式闭合 JSON 合同，要求 `X-Correlation-Id`、`Cache-Control: no-store`；变更要求 UUID `Idempotency-Key`，已有申请的变更还要求 `If-Match` 和正文期望版本/引用。Cookie 认证沿用现有 CSRF 机制。授权、认证等共享中间件仍采用现有错误协议；控制器的输入和业务错误返回正式 Problem。

## 真实来源与权限

发货调用显式选择 `captureReturnApplicationSource: true` 时，同一出库事务额外捕获已有客户身份 Owner 的确切版本。已有 `captureReturnSource` 保持原语义；旧快照没有客户身份引用时，不能按今天的客户代码反推历史 UUID，正式来源选项不纳入这些快照，也不回填。W225 发货页面提供独立的“保存正式退货申请来源”选项，每次打开发货对话框默认不勾选。

可信范围来自已保存的仓库/站点映射、受信任环境配置和当前 RMA 仓库/对象权限。已有业务仓库可在服务端 `RmaApplication:WarehouseSites` 配置明确的 `TenantId/WarehouseId/SiteId`，客户端不能提交或改写映射；重复/空坐标启动失败，与已有映射冲突明确拒绝。没有映射不推测站点。环境和模式继续使用 `ScopeAuthorization:EnvironmentId`、`QualitySpecification:EvidenceMode`。

仓库 DRAFT 身份映射不是实物作业准入或激活证明，原有数据库禁用规则保持生效。本轮只保存申请意图。选择引用用现有 ASP.NET Data Protection 签发，租户与操作者隔离；分页绑定完整查询和原始选项集合，读取时核对当前权限。宿主配置只证明明确坐标，不授予权限、权益或物理作业资格；本轮只在隔离宿主配置并验证，没有修改既有环境。

产品、单位、客户及实际发货事实直接采用已有内部合同。内部读取不重新计算历史事实哈希，不另设资格登记或批准层。外部选择只核对其签发记录；当前权限、站点启用和仓库映射在同一事务范围核对。

## 数量、状态和恢复

数量保持 13 位整数、8 位小数的精确十进制字符串，采用发货时的基准单位版本。每条申请行保留完整原始履约快照及所请求的半开区间。同一申请内，相同发货行的多个选择引用共享计数，不能通过重新搜索拿到另一张选择凭据重复覆盖数量；不同申请不占用权益余额，真正的授权占用仍待 C03。

创建、编辑、提交和撤回均把不可变申请版本、CommandBinding、同步成功 Work/TerminalReceipt、审计及原始结果写入现有 Main 操作日志的同一个 PostgreSQL 事务。成功的同步意图操作没有后台租约，epoch 为 0。失败整体回滚；重复命令恢复原始结果，改变同一命令正文会冲突。恢复允许已撤销写权限但仍有当前读权限的原操作者读取，不跳过当前对象/租户/仓库授权。

原始关系覆盖仍为 PARTIAL。`entitlementPreviewRef` 指向真实的 UNKNOWN 权益观察，剩余可授权数量为 null；它不冒充完整 ReturnEntitlementSnapshot，不填补零数量、规则或权益头。提交与撤回不产生退货批准、库存或财务变化。

本轮支持已有 Main FileContent 引用作为证据，并采用其当前文件及父对象读权限。其他未接入证据 Owner 返回明确不可用；不创建假附件。页面编辑保留已有证据引用、行身份和原备注；备注实际改变（包括清空）才要求该字段写权限，保持原值可编辑其他获准字段。新建页面不提供附件上传，正式权益、独立审批、收货消费及完整工作台继续后续任务，D2 与完整 WMS-RMA 验收保持未完成。

## 页面查询与恢复范围（W225）

列表使用正式 `ApplicationPage/RmaSummary` 形状。`scopeOptionRef` 使用 GET 查询的 `scopeOptionRef.owner/id/version/digest` 四个键，状态数组重复 `states`；游标绑定查询及 pageSize。默认显示范围内当前可读申请，后续页继续读取同一快照并核对当前对象权限。未接入的业务轴保持 UNKNOWN/PENDING/ADOPTION_UNPROVEN 与 PARTIAL 覆盖，不将其展示为已经审批、已收货或财务完成。当前来源没有独立签发的 shipment-line 引用或 invoice-line 关系；这两项筛选明确返回 `APPLICATION_FILTER_SOURCE_UNAVAILABLE`，页面不提供这两项筛选，不宣称完整 Q02 所有筛选采用。

页面直接采用不可变履约事实中的客户身份、订单产品标签及数量单位，不按当前主数据重新推断。数量一直使用十进制字符串。编辑先读取 Owner 上下文，使用原头版本及强 ETag；隐藏备注不替换为空值。只读字段可以原样保留，清空或改写在最终 Owner 决定点检查权限。

每笔变更在发送前把命令 UUID、应用 UUID、方法类别、正文和原 If-Match 提交到 IndexedDB，按服务端 tenant/actor 隔离；正文不放入 localStorage、URL 或 storage 事件。浏览器锁串行处理同一身份的恢复记录，身份变化时丢弃旧页面响应并保留原身份记录。

断线后先查询原命令。FOUND 读取其确切结果引用，再显示当前申请版本；NOT_OBSERVED 保留记录，只允许用户显式重派同一个命令、正文和 If-Match，不生成新命令，不伪造关闭或无效果证明。恢复重派失败仍保留记录；新的首次请求收到明确 NOT_ACCEPTED 且未创建绑定时才解除该记录。只有当前读权限时也可查询既有结果。清理浏览器存储会丢失本机待恢复正文，本轮没有跨设备恢复入口；不声称替代服务端日志或完整 Work 恢复工作台。
