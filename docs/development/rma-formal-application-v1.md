# 正式退货申请意图 v1

W224 采用 WMS-RMA-01 第三次修订的正式申请合同：原始归档 `fb/fbac66e3f15e7da1__WMS-RMA-01_CANONICAL-CONTRACT.json`，195 个定义。应用入口为 `/api/wms/rma/v1`，与已有 `native-applications-v1` 分开。现有页面仍使用旧入口，页面采用属于下一项工作。

## 本轮可用操作

| 方法与路径 | 行为 |
| --- | --- |
| GET `context` | 当前可见仓库/站点选项及动作能力 |
| POST `original-fulfilment-options/search` | 签发不可变履约选择，冻结分页成员和观察时间 |
| POST `applications` | 创建 DRAFT 意图 |
| PUT `applications/{applicationId}` | DRAFT 编辑，版本及强 ETag 比较 |
| POST `applications/{applicationId}/submit` | DRAFT → SUBMITTED |
| POST `applications/{applicationId}/withdrawals` | DRAFT/SUBMITTED → WITHDRAWN |
| GET `applications/{applicationId}?version=...` | 当前或历史申请版本；本轮限定读取，不代表完整 Q02/Q03 搜索与工作台 |
| POST `commands/query` | 查询原操作 FOUND 或 NOT_OBSERVED；后者不能推断无效果 |

请求遵循正式闭合 JSON 合同，要求 `X-Correlation-Id`、`Cache-Control: no-store`；变更要求 UUID `Idempotency-Key`，已有申请的变更还要求 `If-Match` 和正文期望版本/引用。Cookie 认证沿用现有 CSRF 机制。授权、认证等共享中间件仍采用现有错误协议；控制器的输入和业务错误返回正式 Problem。

## 真实来源与权限

发货调用显式选择 `captureReturnApplicationSource: true` 时，同一出库事务额外捕获已有客户身份 Owner 的确切版本。已有 `captureReturnSource` 保持原语义；旧快照没有客户身份引用时，不能按今天的客户代码反推历史 UUID，正式来源选项不纳入这些快照，也不回填。该标志目前是 API 能力，发货页面接入仍待后续。

可信范围来自已保存的仓库/站点映射、受信任环境配置和当前 RMA 仓库/对象权限。已有业务仓库可在服务端 `RmaApplication:WarehouseSites` 配置明确的 `TenantId/WarehouseId/SiteId`，客户端不能提交或改写映射；重复/空坐标启动失败，与已有映射冲突明确拒绝。没有映射不推测站点。环境和模式继续使用 `ScopeAuthorization:EnvironmentId`、`QualitySpecification:EvidenceMode`。

仓库 DRAFT 身份映射不是实物作业准入或激活证明，原有数据库禁用规则保持生效。本轮只保存申请意图。选择引用用现有 ASP.NET Data Protection 签发，租户与操作者隔离；分页绑定完整查询和原始选项集合，读取时核对当前权限。宿主配置只证明明确坐标，不授予权限、权益或物理作业资格；本轮只在隔离宿主配置并验证，没有修改既有环境。

产品、单位、客户及实际发货事实直接采用已有内部合同。内部读取不重新计算历史事实哈希，不另设资格登记或批准层。外部选择只核对其签发记录；当前权限、站点启用和仓库映射在同一事务范围核对。

## 数量、状态和恢复

数量保持 13 位整数、8 位小数的精确十进制字符串，采用发货时的基准单位版本。每条申请行保留完整原始履约快照及所请求的半开区间。同一申请内，相同发货行的多个选择引用共享计数，不能通过重新搜索拿到另一张选择凭据重复覆盖数量；不同申请不占用权益余额，真正的授权占用仍待 C03。

创建、编辑、提交和撤回均把不可变申请版本、CommandBinding、同步成功 Work/TerminalReceipt、审计及原始结果写入现有 Main 操作日志的同一个 PostgreSQL 事务。成功的同步意图操作没有后台租约，epoch 为 0。失败整体回滚；重复命令恢复原始结果，改变同一命令正文会冲突。恢复允许已撤销写权限但仍有当前读权限的原操作者读取，不跳过当前对象/租户/仓库授权。

原始关系覆盖仍为 PARTIAL。`entitlementPreviewRef` 指向真实的 UNKNOWN 权益观察，剩余可授权数量为 null；它不冒充完整 ReturnEntitlementSnapshot，不填补零数量、规则或权益头。提交与撤回不产生退货批准、库存或财务变化。

本轮支持已有 Main FileContent 引用作为证据，并采用其当前文件及父对象读权限。其他未接入证据 Owner 返回明确不可用；不创建假附件。正式权益、独立审批、收货消费、完整工作台和页面采用继续后续直接任务，D2 与完整 WMS-RMA 验收保持未完成。
