# 独立库存入口采用当前库位控制 v1

W217；PostgreSQL；延续 [库位本域控制](location-operational-control-v1.md) 与现有 Stock 原操作 v1/v2。不是正式 Stock Root/Claim 或 MASTER 全 Owner 保护完成声明。

## 实际执行规则

`StockMovementService.ApplyAsync/MoveAsync` 及 `ApplyCommandAsync/MoveCommandAsync` v1/v2 的普通独立新命令，先通过现有动作、字段与对象范围检查，再在原 Owner 事务检查当前 native Location。IN/OUT/MOVE/ADJ/RSV 必须存在当前可见、未删除、非初始化隔离的库位且 `IsBlocked=false`。缺库位返回 `LOCATION_CONTROL_UNAVAILABLE`（409），封锁返回 `LOCATION_OPERATION_BLOCKED`（409）；不存在记录不是可用证明。MOVE 两端均检查。管理员也不能绕过控制。

不新增请求字段或放行开关。任意 RelatedType/RelatedNo 文本不能证明旧义务或批准事实。原有输入、精度、数据范围及 Owner 身份不改；旧的缺库位引用必须先由实际主数据流程补齐，不能靠库存命令自动造出有效库位。现有保管、估值、质量与部门继承规则继续执行。

UNRSV 只按既有规则减少 AllocatedQty，不改变 PhysicalQty，不增加地点使用；不因本域封锁阻止该释放。它仍受当前权限、正数量校验及原事务约束，不能据此声称已有正式 Claim 份额释放。盘点批准、收货及其他内部 Parent Owner 原语仍按其自身事实/旧义务合同处理，本轮不为这些路径制造统一放行或全部停止证明。

## 事务和恢复

既有 OperationsOwner 对业务表和主数据表的 PostgreSQL 锁持续到原事务结束。本域封锁与该库存新命令串行：封锁先提交则新命令拒绝，库存先提交则其已产生的事实保留，之后的新命令拒绝。提交前再次读取控制，捕获同一事务参与者的迟到变化；失败时数量、两腿流水、序号、创建/继承映射、审计、原操作及 outbox 同事务回滚。

原键已提交时，继续先读取不可变原结果并检查当前读取权限，不重新执行控制准入或数量效果。后来封锁不会抹去原结果，撤销读取权限仍会阻止披露。未提交的失败保持 UNKNOWN，同一原键只有在当前条件满足后才能执行；换键不能绕过仍生效的封锁。

本域准确解除仍只清对应 Block，其他 Block、独立写入的限制和初始化隔离保留。库存检查消费实际聚合结果，不从最近一条解除记录推断全部可用。

## 证明范围

相关真实 PostgreSQL 与 Program HTTP 验证见 [交付报告](evidence/W217-01/DELIVERY.md)。未增加数据库迁移、依赖或前端代码。当前检查不代表正式 CurrentEffectView、完整占用/旧义务证明、全部 Stock/RSV/MES Writer 保护、08E、Transfer/REP/RMA 或真实企业环境验收。
