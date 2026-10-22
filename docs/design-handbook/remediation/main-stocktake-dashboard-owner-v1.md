# Main 盘点、独立库存与看板 Owner v1（W39）

2026-10-21；仅PostgreSQL，补充[W37](main-operations-owner-v1.md)与[W38](main-production-inbound-owner-v1.md)。范围为StockTakeService、StockMovementService独立入口、StockOperationsService、WMS/MES Dashboard与三个既有SP路由。历史非PG路径保留，无EF模型、数据库迁移或依赖变更。

## 权限和来源用途

|资源/菜单锚|独立动作|字段/来源|
|---|---|---|
|wms-stock / 403|query（列表、历史、流水）、adjust、move|UnitPrice|
|wms-stocktake / 415|query、add、count、submit、approve、cancel|UnitPrice、Amount；建计划/开始计数另查stock query和实际复制的UnitPrice Read|
|wms-dashboard / 416|query|Amount；库存价值另需stock UnitPrice Read，计划/出库/盘点统计分别消费对应query和对象范围|
|mes-dashboard / 309|query，含全部SP路由|无可配置价格字段；各面板只要求实际消费的work-order、production-result或defect query|
|mes-defect / 308|query目录仅用于看板来源|本轮未采用DefectRecordService独立Owner|

新增query只登记目录，不给任何角色自动授权。每次服务调用验证当前原会话主体、可信tenant、当前有效角色、动作目录和完整封存字段源；缺失Owner、旧事务、脏上下文、已跟踪业务图及不同上下文序号/库存组件均拒绝。401/403/409/503保留到HTTP；读取no-store、写入原CSRF保留。普通业务错误沿用既有处理范围。

库存读取缺单价Read时返回null。调整写入需要单价Write；移动沿用原不接受单价输入的双腿业务语义，不改变计价。盘点创建复制价格需stock Read与盘点Price/Amount Write，开始计数刷新库存快照需stock Read与盘点Price Write；计数使用Price Read与Amount Write；提交需Amount Read；审批使用Price/Amount Read。盘点查询隐藏无权单价、派生差额/阈值；看板价值同时要求目标Amount Read和来源UnitPrice Read，不能绕过来源字段通过聚合取得金额。未消费金额的趋势/告警无需额外价格Read。

## 对象边界与PostgreSQL语义

默认self。库存按完整现存键和Creator，流水按自身Creator；历史需库存对象与各流水分别可读。盘点self包括头和所有明细Creator（含软删除兄弟）；仓库/区域检查完整明细，空明细不推导区域到全仓。独立stock/stocktake/inbound无获准的部门关系，部门范围返回空/拒绝，不从库位或RelatedNo猜部门；self与tenant合法范围保留。

MES看板完整self图包括工单、全部工序/材料、报工及不良记录（含软删除兄弟），源对象与看板范围取交集。部门范围沿真实WorkOrder→Order及现有可信映射；不良项须真实工单引用。全部范围先于分页、总数、TopN、聚合。原生数据库键等价匹配，不用CLR大小写变换替代Owner身份；始终保留TenantId过滤和原数量/单价表示。

## 原子性、冻结和内部调用

请求冻结后共同Owner建立READ COMMITTED事务，权限根、业务图/相关源表锁、最终主体/角色/字段源/范围/部门版本及会话验证贯穿COMMIT。盘点头/明细、全部差异库存、流水及序号一笔提交；第二行或最终授权故障整笔回滚。普通库存调整与移动双腿也在该边界内。短表锁包括盘点和不良图，会串行化相关跨租户写入，后续优化须保留同等语义。

计划创建到开始计数之间库存变动合法；开始计数在同一库存围栏内按原StockId与完整键刷新BookQty/UnitPrice，然后冻结。审批前核对当前实体库存等于冻结快照；raw写入或重叠盘点使快照过时则409，不能按旧差异再次扣增。保留阈值、拒绝行、数量精度、Creator和已有负库存仓库规则。

外部AllowNegativeOverride或伪造STOCKTAKE标记不能越过冻结；盘点审批内部能力需同事务、真实审核中头、未应用且未拒绝的明细、完整键/数量/单价相符。已经采用的入库、出库/取消和反冲父Owner使用仅程序集内部库存原语，保留父权限，不要求额外独立adjust。公共入口从不接受现成事务作为授权；其余旧消费者仍须独立动作，不能获得通用绕过。

提交确认丢失返回OPERATIONS_OUTCOME_UNKNOWN，不自动重放。通知及既有财务桥在确认提交后best-effort；没有持久operation/outbox，也不宣称财务可靠投递。

## 看板合同与实时事件

所有现有MES SP入口在PG上转到同一当前Owner连接；去掉旧全局SQL查询。保留数据库日期、days<=0的单个未来锚点、367天边界及原numeric舍入表达式。普通EF看板沿用应用日期。WMS到期/迟到天数在范围内选定的有限行上计算，采用日期差，消除SQL Server专用DateDiffDay。

PG实时事件版本为InventoryInvalidatedV1，零参数、无业务编号/数量/产品/仓库/价格/主体载荷。Hub连接/订阅先验证当前stock query，只加入可信tenant专属组；PG发送器不使用All、全局组、仓库/产品明细组。页面再经当前Owner查询实际可读行；已经撤权的连接至多收到无明细刷新信号，查询仍重新校验。此版本不宣称消除事件时序信号。非PG旧事件保留；独立BusinessNotification管线未在本轮采用Owner。Space控制塔仅补新事件刷新适配和250ms合并，不据此宣称其业务Owner完成。

## 页面和验证范围

五页面复用既有useOperationsIntent，写入前锁定/冻结、拒绝重复、隔离旧身份/目标/响应；保存期间新输入不被读回覆盖，确认单号/明确状态在读回拒绝后保留。UNKNOWN在当前意图内阻止再发，跨重载持久幂等仍独立。库存/盘点列表查询开始清空旧行；看板拒绝清空相应面板并显示错误，实时载荷不直接渲染。

实际证据和作者集中SPEC/QUALITY见[单份交付](../../development/evidence/WP02-03/stocktake-dashboard-owner/DELIVERY.md)。服务受控主体不冒充登录，实际Program HTTP单独验证；API夹具跳过全量初始化，目录幂等/无自动授权另测。Hub验证为真实Owner/PG加路由替身，未执行真实WebSocket浏览器E2E。完整WP02-03及真实业务验收不提升。
