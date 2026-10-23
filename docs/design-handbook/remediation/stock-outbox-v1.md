# Main 库存刷新 Outbox v1

W50-01 / WP02-10，采用现有 PostgreSQL 库存原操作和正式 Platform `[0.10.3]`。本契约限定于新版本 `ApplyCommandAsync` / `MoveCommandAsync` 成功产生的 `Main.Wms.StockOperation.v1` 原操作；普通无键库存入口、父 Owner 原语、历史原操作、C02、ERP 和 Finance 不自动纳入。

## 业务原操作与通知原子性

`cp6_stock_outbox_v1` 是 Main 所属物理库的固定技术 schema，不是租户 schema，也不替代 TenantId 或当前权限。独立 `StockOutboxContextV1` 使用 Platform 的 `pg-xmin-v1` 四字节原生 token；旧 C02/ERP 八字节并发 token、实体映射及存量队列保持历史合同。

生产者显式共享实际库存 `CP6Context` 的同一 DbConnection 和 NpgsqlTransaction，并核引用相同。库存 Owner 拥有唯一 commit/rollback；库存、流水、采番、审计、原结果、native outbox、binding 与来源映射共同提交。新 queue Context 不自行提交。调整一条通知，移库双腿一条通知；同 operationId 的原结果回放不新建通知，显式 CLOSED 不入队。

`Source` 唯一绑定 TenantId + OperationId，并外键引用不可变 Main 原操作。提交时 PostgreSQL deferred constraint 核对源原操作在当前原生事务内写入、COMMITTED、准确 owner/contract、结果字节摘要、同租户、native envelope、binding 和固定路由。缺件整体失败；不能在成功提交后补写来冒充共同事务。

原结果、来源、信封身份和路由身份不可变；native 变更同时受 Platform 原生 writer/锁/token seal 防护。旧 dispatcher 不领取该路由，旧 MarkFailed/原生 UPDATE 不能绕过。未采用 epoch handover 和 retention，因此路由 epoch 不可直接更新，记录不允许 DELETE/TRUNCATE。

## 固定路由与当前服务权限

|项|值|
|---|---|
|技术 Owner|`Main.Wms.InventoryNotification`|
|queue domain / route|`Main.Stock.Notification.v1` / `InventoryInvalidatedV1`|
|publisher|`Main.SignalR.InventoryInvalidated.v1`|
|topic|`main.inventory.invalidated.v1`|
|messageId|`stock-hint:{TenantId:N}:{OperationId:N}`|
|数据库载荷|UTF-8 `{}`，不含工单、产品、仓库、数量、价格或 Actor|
|SignalR|仅 `inventory:v1:tenant:{TenantId:N}` 组的 `InventoryInvalidatedV1`，参数数组为空|
|并发锁|先实际 Sys_Tenants 行、Authority 行，再 route advisory lock 和 route 行；advisory 高32位 `0x4350364F`，低32位由准确 root hash 导出；原生 UNIQUE lock key 在碰撞时拒绝，不重新绑定|

固定宿主通过 `TenantScopeRunner` 新建逐租户 scope，再解析库存投递器。私有 job verifier 只接受该次内部调用对象，查询当前租户 Enable/ExpireDate/AuthenticationPolicyRevision 和路由 Authority，以 Platform `BackgroundContextExecutorV1` 生成服务身份；不会把无 HttpContext、序列化 ctx-v1 或用户字符串当凭据。

短写事务使用 SERIALIZABLE，Owner 使用该原生事务重查真实租户/路由行和版本；只读原回执使用独立 READ COMMITTED / READ ONLY 事务。网络发送在 DB 事务释放后进行。原发起人的业务权限用于库存提交；后台当前服务权限只允许发送空提示，不授予原 Actor 新的业务权限。客户端收到提示后仍按独立 Owner 查询。

## 宿主配置与确认含义

`StockOutboxHost` 的 `Enabled` 默认 false。启用时必须显式提供部署所属服务的 `HostId`、HTTPS `Issuer` 和 `Region`；不提供企业默认 issuer 或默认租户。Issuer 是本宿主内部服务登记的身份命名空间，不是通过任意远端 URL 获取授权；没有新的 HTTP/消息调用方入口。部署配置属于宿主信任边界，不能让请求覆盖。

HostId 参与稳定 workerId；恢复已有请求时保持准确登记、policy 和原身份。数据库 Authority 可以停用新写；它的身份、policy 和 publisher 不允许原地换义。宿主禁用时新库存操作仍原子保存通知，保持 Pending。缺少必要迁移时命令失败，不跳过入队。当前未迁移既有环境，也未为真实企业配置服务登记或数据库角色。

SignalR 返回成功仅表示服务端发送调用完成；它不是在线客户端 ACK、用户已读、跨宿主全量交付、财务入账、broker confirm 或 OwnerApplied。保留的旧 best-effort 即时提示可能与持久提示重复，页面按刷新提示处理。Finance bridge 仍按其原范围 best-effort；本路由不重放财务效果。

## 原编号恢复与 UNKNOWN

1. `DispatchRun` 在 claim 前持久保存 requestId。同一 root 至多一个未完成请求。创建请求的 COMMIT 回执丢失后，下次先读此原请求，不换号。
2. Platform 以准确 requestId、worker、policy 和 publisher 保存不可变 ClaimReceipt / ClaimTicket；发送前独立查询提交证明。每批最多4条，单次发送预算10秒，lease 60秒。
3. 每条 claim 的 mutationId 使用原 ClaimReceiptRef；发送前持久写 SendAttempt。重启后已有 SendAttempt 不再次调用网络。完成回执丢失时查询原 Claim/Mutation 输入和数据库提交回执，不能旧 MarkFailed 或按新编号重试。
4. 只有所有原 ticket 的实际完成回执可查询且一致，DispatchRun 才终结。数据库同时核对原 claim 和全部逐项回执。只有确定失败形成 RetryScheduled 时才允许后续新批次；当前 SignalR publisher 不把异常伪装为确定拒收。
5. 无法证明发送结果、发送许可提交后进程消失、或旧 lease 已失效而没有完整原结果时，保留 `OUTCOME_UNKNOWN` 和全部原编号；该租户路由等待恢复证据，其他租户继续。此版本不自动跳过未知请求或提供人工清空/重排。需要额外权威证据与后续恢复协议才能继续未知路由；不能以“最终会重试”声称完整外部交付。

队列时钟/lease 使用 PostgreSQL UTC；租户 ExpireDate 仍使用 Main 既有本地墙钟语义。真实角色、生产迁移/切换、跨宿主拓扑、外部 ACK、业务消费者、保留/归档/hold 和全部 PLT-DEL 业务验收继续独立。本地实际宿主证明不改写正式 publication/capability 矩阵或全任务完成状态。

验证、精确源提交、采用和分支/席位结果见 [W50 DELIVERY](../../development/evidence/WP02-10/stock-outbox/DELIVERY.md)。
