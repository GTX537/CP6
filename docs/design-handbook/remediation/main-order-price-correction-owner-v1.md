# Main 受注单价订正 Owner 写入映射 v1

本映射归 Main ERP 代码 Owner 维护，W32 由用户授权的 COORD 在当前聊天实施并作一次任务级 SPEC/QUALITY。它把既有 [SYS-IAM-01 §5](../modules/SYS-IAM-01.md) 接到 `PUT /api/orders/price-correction/batch` / `OrderService.BatchUpdatePriceAsync` 的 PostgreSQL 路径。版本为 `Main.Order.PriceCorrection.Write.v1`，领域为完整封存的 `order`（UnitPrice、Amount）。

这里的 OwnerApproved 是该版本代码适配器的维护批准，来源是本文件及同任务实现、审查和交付；不是浏览器参数、测试夹具批准、真实租户政策批准或正式业务验收签名。更改路径、依赖、适用操作或对象边界必须升级映射并重新作对应审查。不得拿此映射批准整个 Order 域或 CRM。

## 精确用途与路径

下面的输入路径以请求体为根；`/effects` 为本 Owner 内部效果树，不是新增 HTTP API。仅提供 Write 映射，Read/Export/Download 没有映射或许可。

|字段|Write 路径|同用途依赖及解释|
|---|---|---|
|UnitPrice|`/items/*/individualUnitPriceAfter`|选中明细的个别价|
|UnitPrice|`/items/*/setUnitPriceAfter`|选中明细提供的套价|
|UnitPrice|`/items/*/specialPriceFlg`|特价标记属于改价意图|
|UnitPrice|`/effects/orderDetails/*/setUnitPrice`|同受注传播的套价，包括未勾选行|
|UnitPrice|`/effects/orderDetails/*/provisionalPriceFlg`|选中行从暂定价转正式价|
|Amount|`/effects/orderDetails/*/amount`|依赖 UnitPrice 的 Write；数量乘以 SalesPriceDiv 选定的个别价/套价|

价格变化引起的 ApprovalStatus=1、WfApprovalFlg=false 和审批意图由同一 UnitPrice 写许可控制，属于申请、不是批准。PriceChangeReason 是该 correct 操作的说明，不新增价格字段许可。未变化的只读价格可以保留；实际变更只读/隐藏价格或受限派生金额时，**整个意图 403 FIELD_WRITE_FORBIDDEN**，不静默替换为旧值或部分成功。

本窗不增加 list/detail/search/filter/sort/export/download 映射。这些现有入口以及其他受注创建、修改入口仍属于待接线范围，不能宣称 `order` 全域字段保密或所有写路径已完成。字段资源尚无完整映射时，不把本 Write 决定借给其他用途。

## 独立操作、对象与来源

- 当前原登录/会话证据由实际 `AccessTokenAuthentication` 在取得 Owner 根后、保存后及提交前复查；包含原认证版本、会话、受限状态和到期。平台身份不补业务权；模拟身份仍使用被模拟用户权限及原 actor/session 验证。服务内部调用必须提供可信会话消费适配器，不能省略 Owner 回到旧 PostgreSQL 写路径。
- 需要当前有效角色授予 `erp-order-price-correction:correct`，且本租户实际操作定义存在、所属共享菜单启用。删掉操作定义后残存 RoleAction 不授权。动作字符串按实际精确键校验。
- 对象必须是当前 tenant 的未删除受注明细，并存在未删除受注头。ScopeType 仍按既有存行 MAX：5 为当前租户，1 为明细 Creator 对当前存储 UserName，使用既有 PostgreSQL collation/bpchar 比较语义，不信传入 userName 或 Claim Name。选中行和实际传播行均检查。
- Scope 2/3/4（包括默认2）当前没有可信的 ERP OrderDepartment 字符串到 IAM DeptId 映射，返回 **503 OWNER_SCOPE_MAPPING_UNAVAILABLE**。不能把部门代码按 Guid 猜测、改为全租户或从旧 claim 补权。后续需提供明确业务映射和存量行对应方案；无需为此长期占用工作区。
- 当前 source 使用 W31 同语句有效角色、完整原始字段行及持久 revision；MIN 后完整域缺项默认1，显式1保留。字段来源与操作读取都位于同一个持有 authority 根的事务里。异常、未知域、未封存或缺版本拒绝；从不读取旧分布式 allow。
- 此用途的 online 检查使用 Main 当前权威；没有跨库投影参与。价格字段未定义额外 PII/purpose 授权，明确 NotApplicable，而不是省略未知约束。业务对象检查是独立前置；计算器中 Allow 不替代它。

## 事务顺序与批量合同

1. 拒绝旧事务、ambient transaction、脏上下文或已跟踪的业务实体；保留调用方未提交内容。冻结请求，开启自有 READ COMMITTED。
2. 取得 `cp6_authentication_v1.acquire()`，与普通权限、用户、会话 writer 使用同一 PostgreSQL 根。它是串行机制，本 Owner 另外检查真实业务权限，不把认证门本身当作业务许可。
3. 以稳定顺序锁受注头、同单明细。头 FOR UPDATE 阻止新 FK 子行插入；已删除明细也锁定，防止并发复活绕过已检查的传播集合。
4. 当前权限、对象和 RowVersion 校验；套价按既有规则采用每单输入顺序中最后一个非 null 值传播，原生数据库解析业务键，不用大小写敏感字典缩小合法输入。缺失行保留 ConflictedKeys；RowVersion 冲突仍返回409，整批回滚。
5. 按最终价格重算**实际受影响行**的金额。因传播而改价的未勾选行也检查对象/字段、重算金额并置审批申请状态，不能仅改价格却留下旧金额或已审批状态。decimal(21,8)、原价格选择及 null→0 公式保持；不引入 double 或金额精度裁剪。
6. 业务行、原字段审计及工作流意图同事务保存。保存后重读权限、源版本及有效期；读取等待导致租户过期也拒绝，最后再比较有效期时钟。任何拒绝/异常回滚所有准备效果。
7. 持根到 COMMIT，之后返回结果。撤权先提交则该意图拒绝；业务先取得根并提交则撤权随后生效。提交/回滚确认丢失返回503 `PRICE_CORRECTION_OUTCOME_UNKNOWN`，`retryable=false`；不自动再执行。

作用域内序列化依据是同库 PostgreSQL writer 协议，不是对 Redis、跨数据库投影或外部工作流的分布式事务声明。原访问缓存仍按原机制实时查询；本窗不建立新的跨存储协议。

## 工作流意图与结果

新增前向迁移 `20261020104000_OrderPriceCorrectionIntentV1`，只在自有隔离 PG 验证。`cp6_order_price_v1.workflow_intents` 保存 tenant、actor、batch、detail、时间、映射/registry/source 版本及最小版本化改价前后值。内部原始意图不含凭据，也不向浏览器提供授权凭证。它不是完整 POWER EGG 46项报文。

主机原注册是 NoOp 日志桩。PostgreSQL 新路径不再调用该桩来冒充成功；意图状态严格为 **AwaitingAdapter**。正式投递器、46项外部报文、投递幂等性及外部确认尚未采用，后续需独立完成，不自动运行已有泛用投递器。生产推广前还需在已授权环境执行迁移并配置真实采用。

成功保持原 envelope，新增 `data.workflowDeliveryState="AwaitingAdapter"`。UpdatedCount 保留处理到的选中输入数量；WfRequestedCount 为实际价格变化行的持久意图数量，可能包含传播行。页面明确显示“审批申请已保存、等待外部发送”。它不能被解释为外部起票成功。

当前 batch 合同没有 operationId 或安全重放凭据。UNKNOWN/断网结果必须先核对持久业务/意图并作明确恢复决定，不生成新 ID 自动重试；通用 HTTP 层原本也不重放写请求。本窗不宣称已实现业务幂等恢复 API。

## 验证与后续

实际结果和作者集中审查只记录在[单份 DELIVERY](../../development/evidence/WP02-03/order-price-correction-owner/DELIVERY.md)。[P0 #365](https://github.com/GTX537/CP6/issues/365)追踪实际复现的只读写入和冲突后套价残留。

剩余：部门映射、整个 Order 的读/写/导出覆盖、业务 operation 恢复、正式 POWER EGG 采用、CRM 跨库 Owner 采用和真实业务验收。无既有数据库迁移、真实租户审批、Actions 或生产部署；不提升完整 WP02-03/125目标/247真实运行。
