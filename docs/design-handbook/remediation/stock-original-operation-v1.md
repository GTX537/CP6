# 库存实际 Owner 原操作 v1

W44-01 / WP02-10 限定采用；数据库 PostgreSQL。使用 [Main journal v1](main-owner-operation-journal-v1.md) 已有迁移，不新增表、旧数据回填或外部投递实现。

## 实际入口与身份

`StockMovementService` 同时实现 `IStockOriginalOperationsV1`，使用现有实际 CP6Context、WmsSequenceService 与 OperationsOwnerV1。`Main.Wms.StockMovement` 固定 Owner、`Main.Wms.StockOperation.v1` 固定合同。非空 UUID 操作键在当前 tenant + Owner 内唯一，调整与移动共享此命名空间；输入包含显式种类和冻结后的完整强类型请求，保留本版本类型序列化字节，不是原 HTTP JSON 的空白或属性顺序。原 actor 由当前可信会话解析，客户端 OperatorCd 不成为身份权威；冻结的规范输入保留在私有日志，DTO/codec 语义变化必须新版本。

|实际 HTTP|用途/当前权限|
|---|---|
|POST `/api/wms/stock/commands/v1/apply`|首次执行要求当前 adjust、UnitPrice 写和库存/库位范围；已有原键按 query 披露，不要求新写权限|
|POST `/api/wms/stock/commands/v1/move`|首次执行要求当前 move 及两端库存/库位范围；已有原键按 query 披露|
|GET `/api/wms/stock/operations/v1/scope`|query；当前 tenant/actor 公共 UUID|
|GET `/api/wms/stock/operations/v1/{operation}`|query；COMMITTED + 原流水号 / CLOSED / UNKNOWN|
|POST `/api/wms/stock/operations/v1/{operation}/close`|query + 当前会话/CSRF；未提交原键封闭，已提交只能返回当前授权的原结果|

全入口要求实际登录、当前原会话权限和 no-store；POST 受现有全局 CSRF 约束。兼容 `/apply`、`/move` 与既有父 Owner 内部库存原语不升级为可恢复合同。新客户端 `stockApi.apply/move` 必须传入原键并调用新版本；客户端 API 不生成键、不重发、不把 UNKNOWN 转成失败证明。W44时页面尚未采用；W96采用ADJ与公共原键持久恢复，当时MOVE因Issue503暂缓。W97修复属性保全后，W98在实际库存行补齐MOVE表单、正数精确文本数量、独立move动作及双腿原结果恢复。见[W96交付](../../development/evidence/W96-01/DELIVERY.md)和[W98交付](../../development/evidence/W98-01/DELIVERY.md)。

## 原子性与恢复

先取得既有原生权限根与业务表锁，在实际 READ COMMITTED 事务内查询原键，然后决定采用当前 query 还是新写 action。首次执行保存原业务序号、Stock 单腿或 MOVE 双腿、StockTransaction、前后库存私有审计、原输入/摘要、原结果及精确对象 ID，同一事务提交；最终权限失败或第二腿失败整体回滚。输入在等待锁前冻结，不改写原精度、原负库存规则或父操作专属限制。新写不能利用 allowNegativeOverride 或 STOCKTAKE 标志越过父 Owner。

同键同体且当前可读返回原结果；不重跑库存、采番或提交后桥。异体（包括 APPLY/MOVE 类型变化）409。原成功不是权限：当前 query、同 actor、Stock 和 Transaction 的全部原 ID 都须在当前 tenant、对象/部门、仓库/区域范围内且未删除；相同业务编码的新 ID 不替代旧身份。只返回操作键、类型、原流水号，不返回数量、价格或私有审计，因此不额外以 UnitPrice read 限制这个引用结果。旧的写权限不能代替当前 query。

GET/scope 使用现有权限根 `FOR SHARE` 和共享业务锁，不修改业务、原记录、审计或权限根 revision。仍是 PostgreSQL READ COMMITTED 的行锁事务，不是数据库 READ ONLY 模式。命令重放/close 仍会消费技术权限根 revision，不承诺全库零写。

UNKNOWN 仅表示当前查询未见终态，不能证明先前请求未提交，也不能据此换键重做。显式 close 与执行共用原根：close 先提交则迟到同键拒绝；执行先提交则 close 只能回原结果。CLOSED 不可改回 COMMITTED。日志只追加，沿用原迁移禁止 UPDATE/DELETE/TRUNCATE 的 guard。新客户端/调用方应保存原键；任何自动重试或持久 UI 采用需另行实现其身份与恢复边界。

## 准确限制

- 此处为既有库存 Owner 的兼容业务采用，不是 WMS-TRANSFER-01 的 RootSpan/Claim/Stage/SourceDemand/capacity/物理执行合同，不完成 REP/RMA。
- 现有 Finance bridge/SignalR 在首次已知提交后 best-effort 调用。COMMIT 确认丢失可能导致它们尚未发送，原结果恢复不会替代发送、重发或证明外部完成；W50已采用同事务库存Outbox与可信空失效提示路由，见[W50交付](../../development/evidence/WP02-10/stock-outbox/DELIVERY.md)；Finance可靠消费和下游业务ACK仍未证明。
- 部门映射仍为 W42 精确对象 ID，未给新流水自动继承部门；若原结果披露所需对象缺当前映射则拒绝，需有权限的维护入口按真实归属显式建立。不得从原请求人、字符串或相同库存键猜测。
- 保留旧数字/存储合同的真实范围；本轮未完成整个正式 Stock Root/Slice、金额/数量总和规范、生产角色/日志保留政策、全库 schema 租户迁移或真实环境验收。

验证与生命周期见 [交付报告](../../development/evidence/WP02-10/stock-original-operation/DELIVERY.md)。

## W97 / Issue503 MOVE 属性修复边界

首次 MOVE 在既有 PostgreSQL Owner 权限根、库存表锁与原事务内读取来源库存。新目标继承原存储的 OwnerType/OwnerCd、UnitCd、UnitPrice、ReceiveDate/ExpiryDate、PaperRollNo、QcStatus、RecallFlag；两条移动流水保留其中已有流水列（货主、单位、单价、卷号）。创建者／操作者仍取当前可信 actor。源／目标数量、原键日志及 Outbox 一起提交，失败整体回滚。

已有目标只有上述属性逐值相等才可累加，保留其对象身份、原创建者与预留量。当前库存键没有货主／属性维度，不能在一行表示不同货主、单位、质量／召回、日期、卷号或单价；冲突返回安全的 `409 STOCK_MOVE_METADATA_CONFLICT`，不披露属性值，不覆盖零余额行，不做隐式单位换算或加权计价。数量精度与仓库允许负库存政策不变：来源不存在时仍沿用旧默认 SELF/null/PENDING/非召回属性，负腿是否允许继续由原仓库规则决定；这是保留的兼容行为，不证明正式来源身份或 QuantityKernel 已采用。

元数据取自服务端，不增加外部 DTO 字段，不改变 `Main.Wms.StockOperation.v1` 冻结输入、摘要或原记录。MOVE 不额外要求单价读写权限；公开原结果及冲突不含价格。已提交原键继续按当前 query 和全部准确对象 ID 披露，不重新检查现在的属性兼容、不重做移动，也不修写历史错误流水。旧数据修复、正式 Root/Claim/容量、Finance 业务接收分别留待对应任务；新增 MOVE 页面由 W98 采用，其成功不替代正式业务验收。验证和生命周期见 [W97单份交付](../../development/evidence/BUG-503/DELIVERY.md)。
