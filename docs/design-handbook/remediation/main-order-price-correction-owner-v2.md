# Main 单价订正 Owner 读写映射 v2

W33，Main ERP 代码 Owner / COORD 维护。`Main.Order.PriceCorrection.Owner.v2` 接续 [W32 v1](main-order-price-correction-owner-v1.md)，只覆盖 PostgreSQL 的 PA090 列表、金额计算及批量改价。批准依据是同任务源码、集中作者 SPEC/QUALITY 与交付；不是浏览器参数或真实租户审批。SYS-IAM-01 的完整字段域、存行 MIN/范围 MAX 和默认值保持。

## 部门身份

`cp6_order_owner_v1.department_mappings` 显式绑定 `(TenantId, ERP OrderDepartment)` 到当前租户 `Sys_Dept.Id`。没有种子、隐式同名匹配、GUID转换或自动回填；既有订单数据不改写。ERP编码沿用原生 bpchar/case-insensitive 比较及10个UTF-16单位容量，存储前不截断；租户+编码唯一。绑定本身有独立UUID、每次写入更新的revision、租户引用revision及同租户目标校验，部门外键阻止误删除。

管理入口为部门管理页“订单部门绑定”，GET/POST `/api/iam/departments/order-mappings`。GET需当前 `pub-dept:query`；POST需当前 `pub-dept:edit`；精确动作定义和启用菜单也必须存在。读取、变更和原操作恢复复用真实原会话、当前权限、IAM共同根、operationId与CAS，变更必须有理由并保存前后目标/启停审计。编码创建后不改名；可明确重绑或停用。绑定下现有订单数量只作管理影响提示，不是订单字段读取授权。

用户需核对实际业务归属再建立真实绑定。本窗仅在隔离夹具中创建映射，不批准现有数据库的真实部门关系。缺失/停用/异常绑定不能扩大为全租户：

|存储范围|真实对象依据|
|---|---|
|1 本人|当前存储UserName与明细Creator的原生数据库比较|
|2 本部门|订单头现有OrderDepartment绑定到当前健康DeptId|
|3 本部门及下级|绑定目标与当前用户部门均在健康树中；真实完整Path按精确字节前缀判断|
|4 自定义|绑定目标属于当前有效角色的健康自定义部门集合；空集合为空范围|
|5 全部|仍限当前租户、有效订单头与未删除明细|

列表在计数、上限检查和分页之前限定对象。缺失绑定行不返回；本部门/下级范围的调用人部门本身不健康时503。写入对选中和传播的每个真实效果行检查；未知绑定503，已知但超出范围403。金额计算对象不存在或不在范围统一403。绑定、部门移动/停用或自定义范围变化均按当前事实重读，不用旧映射缓存。

## 字段用途

|字段 / 用途|v2精确路径或效果|
|---|---|
|UnitPrice Write|v1六项中的UnitPrice输入和传播路径；增加 `/items/*/priceChangeReason`，说明改变也需Write许可|
|Amount Write|`/effects/orderDetails/*/amount`，同用途依赖UnitPrice|
|UnitPrice Read|`/rows/*/individualUnitPriceBefore`、`individualUnitPriceAfter`、`setUnitPriceBefore`、`setUnitPriceAfter`、`specialPriceFlg`、`provisionalPriceFlg`、`priceChangeReason`|
|Amount Read|`/rows/*/amount`、`/calculation/amount`，同用途依赖UnitPrice|

Read需独立 `erp-order-price-correction:query`，Write需 `correct`，均要求当前角色及真实动作定义。隐藏字段返回null，暂定价标记因此改为可空；依赖价格的金额同时隐藏。隐藏Amount时拒绝AmountFrom/To，隐藏价格时拒绝OnlyProvisional=true，禁止利用筛选计数和计算接口反推。原价选择公式、金额/价格decimal(21,8)、业务键大小写/尾空格、RowVersion及原子传播合同保持。

`priceReadable/amountReadable/priceWritable` 是当前查询的页面提示，不是后续写许可。页面去除隐藏列、禁选只读行，搜索先清空旧结果并按身份/请求世代丢弃过时响应；401/403/确定的权威不可用清空旧资料。映射页复用IAM跨标签页原操作恢复：412保留草稿但须封闭/重读/再次明确保存；未知回执不能新建操作重放。

## 事务及边界

读写均使用新鲜READ COMMITTED并取得既有认证根，拒绝旧事务、环境事务、脏上下文和缺失Owner。绑定ordinary SQL的statement触发器也取得该根，组织/IAM writer沿用原根，因此并发撤销与业务提交有确定顺序。业务写继续锁订单头及全部相关明细，保存后/提交前重查原会话、字段来源/目录版本、树和绑定revision、对象范围及时间有效期，根保持到COMMIT。列表/计算同样在返回前重查当前权威，失败不回退缓存或已读取结果。新GET响应禁止缓存。

W32最小审批意图、AwaitingAdapter和不明结果禁止自动重放的合同保持。没有完整POWER EGG投递、跨重载业务operation恢复、其他Order CRUD/导出下载Owner、CRM持久采用或全业务验收。本映射不能批准这些剩余用途。

迁移 `20261020134000_OrderDepartmentMappingV1` 为独立内部表/触发器，无EF实体快照变化，不允许破坏性Down。只在本任务自有PostgreSQL夹具验证；既有环境迁移与真实租户配置仍需对应采用窗口。
