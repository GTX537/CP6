# Main 原命令日志 v1 / PA090 首个业务采用

W43-01 / WP02-10 限定窗口，PostgreSQL。业务 Owner 是 `Main.Order.PriceCorrection`；命令合同 `Main.Order.PriceCorrection.Command.v1`。它不替代 Platform Inbox 投递/投影记录，亦不宣称所有 Main writer、RMA MainUow 或完整 PLT-DEL 已采用。

## 命令及原结果

`PUT /api/orders/price-correction/commands/v1` 接受非空 UUID `operationId` 和原 PA090 `items`。输入在等待锁前冻结，按本版本声明的类型/属性次序序列化 UTF-8（含项目次序、完整 decimal、字符串、原 RowVersion），以独立版本域保存准确 bytes 与 SHA-256。这是 Owner 类型规范输入，不是原 HTTP 包字节摘要；不做金额舍入、字符串标准化或项目排序。未来 DTO/codec 语义变化必须新版本。

唯一键 `(tenant_id, owner, operation_id)`；记录不可变 actor、contract、原输入、结果 bytes、全部实际效果对象 ID、价格前后审计及权限版本。新操作沿既有原会话/当前 correct、字段写权限、部门/本人/租户范围及订单父/兄弟锁校验；业务、原日志、审计和 workflow_intents 一次 native commit。workflow 的 batch_id 使用原 operationId；外部接口/网络不在事务中调用。

同键原操作优先于新的 ETag、写权限或业务资格检查；当前 query、原 actor、字段读权限和全部效果对象当前范围通过才披露。同键异体 409 `ORIGINAL_OPERATION_INPUT_CONFLICT`；其他 actor 不可读/关闭；租户间查询无记录返回 UNKNOWN，不暴露外租户存在性。新命令不允许用原成功记录授权另一个新效果。旧 RowVersion 不阻止准确重放，已撤销 correct 但保留合法读取权限仍可查原结果。

`GET .../operations/v1/{operationId}` 返回 COMMITTED+原结果、CLOSED 或 UNKNOWN。缺记录不能证明未执行。`POST .../operations/v1/{operationId}/close` 使用同一实际权限根：已有提交只返回其原结果，无原记录才写不可变 CLOSED；迟到的同键命令被拒绝。关闭需要当前 query 和原会话，HTTP 写入仍需 CSRF，不要求创建价格变更权限。它只封闭此新命令合同，不能用于证明旧入口或外系统未执行。

## 事务、权限与恢复

内部 `MainOwnerOperationJournalV1` 使用调用方 CP6Context 的同一 Npgsql connection/native transaction；拒绝无事务、异连接及非 ReadCommitted/Serializable。它不开始/提交/回滚事务，不生成替代 operation，不重试，也不发送消息。PA090 复用既有 READ COMMITTED 权限根及父/完整兄弟行锁，不将其说成 RMA 所需 Serializable 范围锁采用。其他 Owner 使用前仍须盘点业务 writer、锁序、对象读写策略和故障范围。

写入/封闭由 `cp6_authentication_v1.acquire()` 的实际 root UPDATE 串行化，与普通/raw SQL 权限及组织映射 writer 共用。同键重放的写命令不追加业务、审计、receipt 或 workflow；该写入口仍消费技术权限根 revision。纯原结果 GET 则锁同一 root `FOR SHARE`：保持 READ COMMITTED，**不修改**根 revision、业务、日志或审计。这里“只读”指应用不产生写入；因 PostgreSQL 行锁，它不是 `SET TRANSACTION READ ONLY` 或 Platform Inbox 的原生只读 seam。current read / tenant / session / fields 仍在输出前复核。

任何 commit 确认丢失保留原键，先查询；未知结果不自动新建、不推断 rollback。并发新命令与 close 按同根真实 commit 顺序决定，失败全回滚。所有日志 UPDATE/DELETE/TRUNCATE 被触发器拒绝；没有保留期、自动清理、历史回填或旧数据推断。未来 hold/隐私/归档/清理需独立合同，不能直接删除去重事实。数据库角色不得拥有/篡改 guard，生产最小权限与安装仍属正式环境门禁；本轮只在隔离夹具应用前向迁移。

## 实际消费与剩余范围

PA090 页面从服务端获取当前 tenant/actor，浏览器锁按该身份串行保存，只在 localStorage 留原 UUID，不保存客户或价格草稿。UNKNOWN、刷新、重复点击及另一个标签页都先恢复；恢复不自动重发，封闭需用户明确点击。跨账号保留原账号的公共引用，但清空页面数据并忽略旧响应；重新登录原账号后按当前权限恢复。

旧 `PUT .../price-correction/batch` 与 `IOrderService.BatchUpdatePriceAsync` 保留其历史无操作键合同和全部 Owner 权限；不回填原结果，也不宣称旧请求可恢复。其他业务接口、CRM、Platform Inbox/OutboxV2、真实 POWER EGG 46字段适配/消费者和可靠投递均未由本窗采用。workflow state 仍为 AwaitingAdapter，不能显示已发送或外部业务完成。

验证与实际源码/主线采用见 [W43 单份交付](../../development/evidence/WP02-10/main-original-operation/DELIVERY.md)。金额/精度、字段读取、父/兄弟传播及来源权限兼容结果按原 PA090 相关测试复用/重验，不扩大为全仓或真实业务验收。
