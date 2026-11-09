# W340-01 Stock 写入对象和实际 Owner 边界

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [S02-00 对象清单要求](../../../design-handbook/architecture/02-postgresql-tenancy.md#3-数据分类与物理边界) · [本窗开工](../../dispatch/20261109-wave340/README.md)

Main 基线 `6b0ca24ecaa480d12aafe9b10138ec803f7fb52d`。COORD 在固定 DEV-1 单人执行 `codex/w340-01`。本表用于原 PGT 的库存采用、版本并存及恢复。范围为实际 Stock 主账、流水、编号、归属、原操作、通知和它们的直接依赖。

## 实际入口和事务归属

| 入口 | PostgreSQL 事务和已核范围 |
|---|---|
| `StockController` 公开直接调整和移库 | 开启相应 `EnableV1Adjust`、`EnableV1Move` 时用独立 HTTP 用途及普通库存事务。返回原流水号。关闭对应开关时由原宿主调用。 |
| 版本化 Stock V1/V2 调整、移库与原操作 | 按已明确启用的普通入口取得现有授权根和业务图锁。V2 创建上下文为普通只读，显式新归属、库存、流水、审计、原结果和通知在普通写事务中落地。显式关闭用原图锁。读回以原对象和现行 query 权限为准。 |
| 入库、出库、盘点审批 | 入库和出库经各自父 Owner 调用 `ApplyWithinOwnerAsync`。盘点审批校验现行盘点及每行快照，使用 `ApplyStockTakeWithinOwnerAsync`。库存写入与父单据同一事务。 |
| 退货、纠正、处置与调拨 | `StockRmaPostingV1`、`ReturnCorrectionStockV1`、`ReturnDispositionStockV2` 和 `TransferRequestOwnerV1.Stages` 通过各自事务及具名内部入口执行。其原动作和原结果属于各自父 Owner。 |
| 库存质检 | `StockQcService.Owner` 的单行和工单关联入口用 `wms-stock-qc:set`，按当前库存、源工单及对象关系修改 `QcStatus`。品质父入口须持实际品质事务和检查对象。 |
| 批次召回 | `LotTraceService.Owner.RecallOwnerAsync` 在其 Owner 内核完整当前批次和原行版本，整体修改 `RecallFlag`。旧 PostgreSQL 无版本入口要求行版本。 |
| 其他直接调用和原生内核 | 越库、效期、套件、LPN、移动任务、序列库存、旧 RMA 和采购出库适配器的源码调用仍使用各自原服务入口。逐个按真实父事务及权限采用。原生 `StockDirectMoveV1` 的根、容量、占用和原决定属于 `cp6_stock_move_v1`，已绑定的物理键使用原生写者门。 |

当前源码核对了公开 Controller、父 Owner 和两个直接字段写者。普通库存采用的真实证据来自 W331–W339。质检和召回的历史实际证据分别保留原范围，两处 Owner 实现及对应 Controller 相对原采用提交无差异。 [公开入口](../../../../CP6.WebApi/Controllers/Wms/StockController.cs) · [独立及父调用](../../../../CP6.Core/Services/Wms/StockMovementService.Owner.cs) · [W338](../W338-01/DELIVERY.md) · [W339](../W339-01/DELIVERY.md) · [质检原交付](../WP02-03/quality-owners/DELIVERY.md) · [召回原交付记录](../../dispatch/20261023-wave84/README.md)

## 原生对象、身份及恢复边界

| PostgreSQL 对象 | 实际唯一域及普通权限 | 原事务和保留依赖 |
|---|---|---|
| `public.T_Stock` | `Id` 全表主键，库存业务键为 `TenantId,WarehouseCd,LocationCd,ProductCd,LotNo`。强制行安全。普通角色可新增行和更新数量、修改人及时点。三数量列均为 `numeric(21,8)`。 | 原 Stock 写事务或有权限的父 Owner。恢复保留真实库存 ID、业务键、部门映射及原软删状态。货权、租户、质检和召回列的普通 UPDATE 均被拒绝。 |
| `public.T_StockTransaction` | `Id` 全表主键，`TenantId,TxnNo` 唯一。普通角色读取及追加，强制行安全。 | 原库存变化与流水同一事务。恢复保留双腿引用、原编号、货权和实际操作人。 |
| `public.T_WmsSequence` | `Id` 全表主键，`TenantId,Prefix,DateKey` 唯一。普通写者按租户读取和推进，强制行安全。 | `TXN` 状态随库存事务提交或回滚。恢复按原序号状态及日期键核对。 |
| `cp6_stock_return_v1.movement_sequences` | `sequence` 为全表标识主键，`tenant_id,movement_id` 唯一。普通写者追加，强制行安全。 | 与库存流水同事务登记，原退货消费使用实际流水和库位池引用。恢复保留全局标识域和原流水映射。 |
| `cp6_operations_owner_v1.department_mappings` | `id` 全表主键，`tenant_id,object_kind,object_id` 唯一。普通角色读取准确投影，写入由固定函数按对象类型执行。强制行安全。 | 明确新库存归属和流水继承各随原事务。恢复保留实际对象 ID、部门 ID、启用状态与原 revision。 |
| `public.Sys_SecurityLogs` | `Id` 全表主键，普通角色无表读写权限。库存专用私有执行者追加实际归属审计。 | 与对应库存或流水归属同事务，按租户、原操作人和实际映射版本查验保留。 |
| `cp6_stock_creation_writer_v2.inserted_stock` | 私有键 `tenant_id,stock_id`，普通角色无表权限。实际查询为零行。 | 只供事务内确认新库存，成功审计消耗记录，延迟触发器清理未消耗记录。 |
| `cp6_main_owner_v1.operations` | 原键 `tenant_id,owner,operation_id`。普通角色读取固定投影，追加经原函数，原结果不可变。强制行安全。 | 原输入字节、结果、审计及对象引用随同提交。按原 actor 和现行授权披露，恢复沿原键及原字节。 |
| `cp6_stock_outbox_v1.Cp6_OutboxMessage` 与 `Source` | 消息 `Id` 主键和 `MessageId` 全表唯一。源 `NativeId` 主键、`TenantId,OperationId` 唯一。源的实际外键分别指向消息和原操作，均为 `ON DELETE RESTRICT`。两表强制行安全。 | 普通写者在原操作事务入队，工作者使用单独租约。恢复同时对原操作、源和消息，保留原全局消息键及已投递状态。当前队列沿原留存协议。 |
| 授权、主数据和盘点 | `Sys_Dept` 的部门码、`T_Warehouse` 的仓码、`T_Location` 的有效库位码均按租户唯一。库位另有 `TenantId,WarehouseCd,LocationCd` 唯一键。角色库区范围、盘点号和盘点行号也有租户前缀。相关业务表强制行安全。 | 普通业务读精确投影，认证根保护当前授权，图锁保护库存、仓库、库位及盘点冻结判断。控制侧保留原部门关系、库位隔离状态、角色范围和盘点状态。 |
| `cp6_tenancy_v1.login_tenant` 与安装身份 | 私有目录以 `role_oid` 为主键，`role_name` 和 `tenant_id` 各自唯一。读取安装身份为单例。 | 原运行凭据需绑定实际 PostgreSQL 角色与当前安装。恢复时核目录、数据库、安装 ID 和当前授权，沿受控路径换代。 |

目录中的业务码支持两租户使用同一仓库、库位和库存业务键。下一窗将使用不同主键及各自普通 LOGIN，实测同键写入、原键读回和另一租户库存独立变化。Outbox 继续使用原全表消息唯一域。 [EF 模型](../../../../CP6.Core/EFDbContext/CP6Context.cs) · [普通存储契约](../../../../CP6.Core/Persistence/Tenancy/README.md)

## 验证及接续

复用 W337 已停的自有夹具，在已核数据库身份及 PostgreSQL 18.6 下执行实际 `BEGIN READ ONLY` 目录查询，核已应用的 W335 创建分派迁移、有效唯一索引、外键、行安全、列权限和临时记录计数。两次查询后实例已确认停止。原始 SQL 和输出位于 `D:/CP6-local-test/CP6-72H-20261109/W340-01`。实际业务测试复用对应窗口的原范围。本窗另做源码差异、文档链接和台账一致性检查。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。 [W337 运行范围](../W337-01/DELIVERY.md)

起始估算 13–23 分钟。目录、源码及引用核毕时主机单调实测 336 秒。复用已有隔离库和准确交付证据缩短验证。完整窗按实际检查、远端采用及席位释放计时。接续从已确认的两租户业务键开始，再核错误登录、凭据轮换和实际版本。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
