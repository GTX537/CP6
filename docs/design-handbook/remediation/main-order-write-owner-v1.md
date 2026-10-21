# 普通订单写入 Owner v1

W35，Main ERP代码Owner/COORD维护，`Main.Order.Write.Owner.v1`，依据[SYS-IAM-01](../modules/SYS-IAM-01.md)与[可信部门映射](main-order-price-correction-owner-v2.md)。本次单任务实现/作者SPEC/QUALITY批准限于本文入口，不代表真实租户授权、取消级联或完整业务验收。

|入口|真实动作定义及当前有效角色动作|字段用途|
|---|---|---|
|POST `/api/orders` / CreateAsync|erp-order:add|完整UnitPrice、Amount Write|
|PUT `/api/orders/{number}` / UpdateAsync|erp-order:edit|完整UnitPrice、Amount Write|
|DELETE `/api/orders/{number}` / DeleteAsync|erp-order:del|仅逻辑删除、独立对象动作；不更改价格/金额值|

三个动作沿用已有目录，不自动授予角色。HTTP属性提前拒绝，PG服务必须注入同一DbContext的Owner；传入userName不构成身份。复用原会话认证适配器、完整封存order域、当前真实动作定义、有效角色及源版本；Read不授Write。创建/全量替换会写完整价格图，即使数值相同仍要求完整Write，不将readonly悄悄恢复后谎称全部已保存。

UnitPrice固定路径：`/details/*/{individualUnitPrice,setUnitPrice,purchaseUnitPrice,unitPriceUnit,specialPriceFlg,provisionalPriceFlg,priceChangeReason}`，`processes/*/{purchaseUnitPrice,fixedPrice}`、`materials/*/supplyUnitPrice`。Amount为`/details/*/amount`，依赖UnitPrice同用途许可。删除仅更改IsDeleted和审计字段，没有价格修改或输出，因此由当前del、原会话、租户、完整对象范围独立授权；字段域/来源仍必须可用。

## 对象、归属与原子性

scope1核数据库保存的头和每条明细Creator；scope5保持当前租户语义。scope2/3/4只通过显式租户订单部门代码映射和当前健康部门树/自定义集合，不信请求的IAM标识、不按名字推断。修改/删除先核旧头及所有明细（包括将被替换/删除的已删除行），写后再次核持久化的新头与完整效果范围；创建在同一未提交事务内核新对象。不可通过更换OrderDepartment把对象移到无权范围。自有/全租户scope不被改成额外必需部门映射合同。

新鲜READ COMMITTED，拒绝调用方旧/环境事务、脏上下文和预先跟踪的订单图。共同认证根保留至COMMIT；修改/删除按头、明细、工程、备考、材料顺序FOR UPDATE，包含删除行，父外键锁阻止未检查子行插入。所有业务写和ORD序号同一事务；旧RowVersion冲突、最终权限来源故障/变化、最终主体变化、租户到期整笔回滚。提交确认丢失返回503/ORDER_WRITE_OUTCOME_UNKNOWN，不自动重放；持久业务operation查询/跨重载恢复未在此轮实现。

原全量替换合同保留：先删除并Save，再插入，但两次Save都在Owner事务内。原业务行号的Id、Creator/CreateDate、删除状态、mc键、审批和出荷状态来自存储，不能由请求或当前操作者夺取；新行Creator取当前可信主体。子工程/材料继续归属于对应明细业务外键。数据库原生大小写/尾空格比较与decimal(21,8)精度不改。已有明细原手配号为空时保持其合法null，不用长订单号补出超过20字符的值。无新迁移。

## 创建桥接与返回

创建WMS/MES钩子只在授权提交确认后执行，保持既有best-effort合同；失败记录日志，不把已提交订单误报为创建失败、也不自动再创建。它不证明下游Owner、durable outbox或跨库原子性已完成。C03内部受信集成仍沿用原独立入口、调用方事务/FX/来源/后提交合同，本次未把普通交互会话门强加给C03，也未扩大C03批准范围。

MVC保留Owner401/403/503，业务输入400、并发409；非法DELETE base64版本400，不降格成无版本删除。W34独立回读：写已提交而读取不可用时仍返回已提交/订单号/空data，不暗示重放。

## 前端与剩余

保存/删除先占用单个写入状态，覆盖授信等待和确认，冻结原输入、目标与版本；身份/代次改变不发送或接收旧请求。授信期间草稿变化则中止该次提交；已发送后出现新编辑，成功回读时只绑定已提交订单号/版本，保留未保存草稿，不覆盖。回读不可用按W34清理旧图。新增/修改分别看add/edit权限，Vue条件渲染支持只有edit的用户从New切回Edit；UI不代替服务授权。

仍未完成：取消操作的WMS/MES级联Owner、其他查询/检查计算/追踪入口、持久业务operation和UNKNOWN恢复、正式POWER EGG报文投递、CRM持久Owner及真实DEV/UAT验收。取消的具体下一步：核查OrderCancelBridgeHook逐项捕获异常与OutboundService.CancelOrderAsync/WorkOrderService.CancelAsync各自事务及库存释放，确定完整效果范围、最终授权门与失败恢复，再实现和验证；不把当前部分取消结果改称原子成功。冻结方向不重启，125目标/247运行不提升。
