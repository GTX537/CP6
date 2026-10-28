# 调拨来源环境与原生产品消费 v1

W205落实WMS-TRANSFER-01 v1.1 R1 §4.1的一个直接前置：服务端执行环境、租户注册和准确产品来源读取。它不是完整T01/T02/T04或库存执行接口。[本轮范围](dispatch/20261028-wave205/README.md)。

## 受信来源与部署

`ScopeAuthorization:EnvironmentId`提供已经由原生Scope Owner验证的环境；`TransferEnvironment:Mode`必须显式为`PRODUCTION`或`SIMULATION`，分别匹配`QualitySpecification:EvidenceMode`的`REAL`或`DEMO`。生产模式必须没有`TransferEnvironment:SandboxScopeId`，模拟模式必须提供非空UUID。缺配置拒绝，body/header不能切换环境、租户、Actor或模式。

首次获授权的租户注册在当前实际PostgreSQL数据库记录唯一`(environmentId,mode,sandboxScopeId)`。该归属不可更新、删除或清空；后续租户只能采用相同数据库归属。换环境或切模式必须使用正确隔离数据库，并按独立迁移任务处置历史数据。禁用租户消费注册不会改变数据库归属。

注册只声明当前代码实际实现的Main原生适配器：ERP产品、客户、UOM、产品单位绑定，PLM工程产品、工程/商务/站点映射、冻结基线，Space物理站点，WMS库存货权/用途/产品身份。没有接受任意Owner字符串、URL、外部成功声明或用户选择模拟身份的配置入口。登记配置不等于现场或企业业务验收。

## 原件归属

前向迁移`20261030007000_TransferEnvironmentV1`新增独立schema，数据库绑定、租户版本、原操作关联及来源命名空间，不回填已有来源版本。数据库绑定建立后，各原生Owner在自己的真实原件INSERT事务中通过触发器捕获准确Tenant/Owner/ID/version/digest与数据库归属。绑定前生成的原件仍保留原来读法，新消费入口因归属未证明而拒绝采用。

库存身份和冻结基线原件已经包含环境和证据模式，捕获还核对这些原字段与数据库归属一致。直接写命名空间记录、改写原件归属、删原件或跨步版本推进均拒绝。触发器缺失/禁用时，新入口不可用。没有伪造或补写来源来让旧数据变成生产数据；旧数据采用需要另行提供无损迁移和归属证明。

## 独立管理入口

页面`/wms/transfer-environment`与API`/api/wms/transfer/environment-v1`使用独立菜单`wms-transfer-environment`。`query`读当前配置、历史与原操作；`edit`登记/启停。本轮只新增动作定义，不自动授予任何角色。此配置是租户整体设置，仅支持范围5；不能据此扩大具体业务对象的部门/本人范围。

- `GET /`：当前认证租户、Actor、服务端部署、注册版本和实际适配器。
- `POST /commands`：闭合DTO `{operationId,expectedVersion,enabled,reason}`；首次为`expectedVersion:"absent"`且启用，后续必须匹配当前版本。同状态不生成新版本，但保存该命令的原结果。
- `GET /versions/{version}`：准确不可变原件，仍要求当前权限。
- `GET /operations/{id}`：当前同一Actor可读取`UNKNOWN/COMMITTED/CLOSED`，历史结果不会被当前状态改写。
- `POST /operations/{id}/close`：关闭未提交键；并发提交和关闭只有一个持久结果，迟到命令不能重新使用已关闭键。

注册、版本、数据库绑定与Main原操作journal在同一事务提交；Scope必须属于当前同一个数据库上下文；误接入其他上下文的有效活动租约也会拒绝。当前授权、Scope及注册在最终检查时再次验证。事务中撤权整体回滚。响应丢失可查询原键；不能将未知结果当作失败再发送新命令。

浏览器仅保留原操作ID及已有注册ID，按环境、模式、沙箱、租户、Actor分区，使用Web Locks协调同浏览器多页面。写入前重新读取服务端上下文与版本；不保存原因、配置正文或自动重发。当前身份变化和拒绝读取会清空页面私有信息，迟到响应丢弃。

## 实际产品来源消费

`POST /api/wms/transfer/sources-v1/product`接收准确库存产品目录引用`{owner,id,version,digest}`，要求独立`wms-transfer-source:query`。该读取入口自己的租户范围不替代来源Owner对象范围；每个原生对象仍按当前角色、字段、部门和原确认者的实际授权检查。

同一个Main PostgreSQL事务中取得当前启用注册、Scope租约，并调用InventoryIdentity原生内部读取，不调用另一HTTP接口或使用缓存结果作为提交依据。核对目录、产品、单位绑定、输入与基础UOM、映射及其中的工程/商务/站点、货权/客户、用途和可选真实冻结基线的准确原件归属。冻结基线仍执行原生工程项目成员及审查来源读取。旧原件、错环境/模式/沙箱、已停用注册、变更来源或最终撤权不返回可采用结果。

返回`environment`包含环境、模式、沙箱及当前注册版本，同时返回准确`registrationRef`、`catalogueRef`与原生产品选项。`qualification`保持`REQUIRES_FINAL_OWNER_GATES`。它不宣称完整调拨上下文已齐备，不填造MASTER地点或Transit空集；没有新增库存、占用、预算或调拨生效写入。

## 仍待后续

正式MASTER地点与生命周期、完整T01/T02选择上下文、T04 Request/Draft预算绑定、Stock/RSV共同事务及实际库存效果继续保留。生产配置、现场原件与正式验收不能由隔离模拟数据库的测试替代。该迁移只在本轮隔离验证数据库运行，没有迁移既有环境或部署生产。
