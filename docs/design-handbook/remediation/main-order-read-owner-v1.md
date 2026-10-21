# 普通订单读取 Owner v1

W34，Main ERP代码Owner／COORD维护。`Main.Order.Read.Owner.v1`仅批准本文普通订单读取用途，采用[SYS-IAM-01](../modules/SYS-IAM-01.md)完整字段域及[W33可信部门绑定](main-order-price-correction-owner-v2.md)。依据是同任务实现、相关行为验证及一次作者SPEC/QUALITY；不代替真实租户授权、正式外部合同或全业务验收。

## 实际入口与独立用途

|入口|当前真实动作定义及当前角色动作|字段用途|
|---|---|---|
|GET `/api/orders/list` / SearchOrdersAsync|`order-list:query`|Read|
|GET `/api/orders/{webOrderNo}`、`by-haibai-no` / 对应服务|`erp-order:query`|Read；完整编辑图|
|GET `/api/orders/list/export.csv` / ExportListCsvAsync|`order-list:query` AND `order-list:export`|Export|
|POST `/api/orders/report` / ExportOrderReportPdfAsync|`order-list:query` AND `order-list:download`|Download；真实CSRF|

菜单207仍使用已有RoutePath派生键`order-list`；208为`erp-order`。启动种子只增加上述四个动作目录定义，不给任何角色自动授予新读取／导出／下载权。现有租户需由已有授权管理员在角色权限入口显式配置，并具备有效数据范围和字段权限。未部署、未修改真实租户授权。HTTP属性只作提前拒绝，内部PostgreSQL服务必须具有绑定同一DbContext的Owner；缺失Owner、目录不完整、源不可用不走旧路径。

## 对象与部门

始终按可信当前TenantId；scope1使用数据库保存的Creator与当前用户UserName按原生比较，scope5为当前租户，scope2/3/4复用显式订单头OrderDepartment→IAM DeptId及当前健康树／自定义集合。未知绑定不产生可见行，空自定义集合仍为空，不按名称推断或回填。

列表在计数、分页和导出前应用明细范围，订单头也必须属于当前租户及所选删除状态。整单读取要求头以及所有请求明细均在范围内，不能输出半张可编辑订单；不存在或越界统一403，手配号不存在返回既有无结果响应，禁止凭前端传入部门或Owner推断授权。`includeDeleted`保留原输入合同，但删除行仍执行同一租户、对象及字段检查；它不是放行开关。

详情子图按原生数据库外键读取；同订单明细号唯一、ProductCd外键已验证后按明细号组装，避免CLR区分大小写／尾空格而丢失合法材料。列表币种／汇率用原生JOIN关联，不使用Ordinal字典。报表冻结并验证全部选择键，缺件、越界、非法键整份拒绝，不静默产出部分结果。

## 字段映射与输出

注册域仍为`order.{UnitPrice,Amount}`，原存行MIN／默认值不改变。下列是代码Owner批准的固定投影，不授权客户端自行声明字段路径。

|字段|用途及路径|
|---|---|
|UnitPrice|Read／Export／Download：`/rows/*/{individualUnitPrice,setUnitPrice,unitPriceUnit}`。CSV列及报表文本是对应行的固定投影。|
|Amount|Read／Export／Download：`/rows/*/amount`；各用途均依赖同用途UnitPrice。|
|UnitPrice|整单Read：`/order/details/*/{individualUnitPrice,setUnitPrice,purchaseUnitPrice,unitPriceUnit,specialPriceFlg,provisionalPriceFlg,priceChangeReason}`，以及`processes/*/{purchaseUnitPrice,fixedPrice}`、`materials/*/supplyUnitPrice`。|
|Amount|整单Read：`/order/details/*/amount`，依赖UnitPrice Read。|

列表／CSV隐藏值为空，价格隐藏同时隐藏其依赖金额；隐藏价格排序及隐藏金额排序403，避免通过顺序反推。列表附带PriceReadable／AmountReadable仅为UI提示。完整编辑图和报表需要全部映射字段可读，隐藏时整份403，避免nullable改造或默认0破坏原写入合同。CSV使用不丢精度的InvariantCulture小数，保留decimal(21,8)合法值；不再四位小数截断。原报表仍是简易文本下载，未冒充正式PDF或正式单据。

## 当前权威及返回边界

新鲜READ COMMITTED；拒绝已有／环境事务和脏上下文。在原认证共同根内重新消费实际原会话、到期及密码政策，读取完整当前角色、字段源、真实动作定义及部门版本。列表原生范围查询持有头／明细SHARE锁，防止普通业务writer在返回前移动部门或Creator；详情／报表先锁头FOR UPDATE再锁全部明细（包括删除行），阻止未检查兄弟插入／复活。查询结束、COMMIT前重查来源／目录／组织版本、scope及原会话、租户到期，失败不返回已读数据或缓存。所有这些接口返回no-store。

这是同步读取的提交点，不是未来下载票据或长期离线访问授权。长导出会持有共同根及相关业务行锁，后续大数据异步导出需要单独设计，不在本轮虚构无锁／无限吞吐保证。

创建／修改已经提交后执行独立读取：若读取被拒绝或不可用，HTTP仍明确返回已提交、订单号、`readbackUnavailable=true`和空data；页面清除原编辑图并提示不要重发。未改变原写入事务，也未把普通CRUD写入标为Owner已采用。

## 前端及剩余范围

列表通过可选clearOnLoad清除正在重新验证的旧资料，其他模板消费者保持原默认行为。身份事件／跨标签页storage变化清空资料与选择；过时搜索和旧身份Blob不得显示或下载。详情新请求先处理未保存草稿确认，再清空旧订单；新建／重置／卸载使旧响应失效。同一身份导航保留未保存草稿，store持续绑定身份，即使页面未挂载也在身份改变时清除。页面提示不替代后端权限。

尚未覆盖普通订单创建／修改／删除／取消写Owner，产品主数据查询、检查／计算、追踪及未出货等其他入口，跨重载业务operation恢复、POWER EGG正式报文／投递、CRM持久Owner及真实DEV/UAT业务验收。无新迁移、真实映射批准、Actions或生产部署。
