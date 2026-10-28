# 原生仓库草稿 Owner v1

Owner `Main.Wms.WarehouseDraft.v1`，原操作合同 `WAREHOUSE-DRAFT/1`。独立适配入口 `/api/wms/master/drafts-v1`；当前原生仓库类型来源 `Main.Wms.NativeWarehouseType.v1` / `1` 明确包含既有 WarehouseType 五个常量，没有默认选项或任意类型写入。本版本仅覆盖已接受 WMS-MASTER-01 §6.1 的受支持仓库草稿变体；不声称全 DEV-M01、WP10-01 或 MASTER 完成。

## 命令、权限与原结果

`GET scope` 返回服务端 Tenant/Actor；`GET creation-context` 需要 `wms-warehouse:add`，返回当前权限版本、允许部门和实际 `space-site:query` 可见原件。`POST warehouses` 接收闭合 DTO：operationId、authorityVersion、departmentId、siteId、warehouseCode/name/type、baseCode/address/managerCode/remarks。warehouseType 必须明确出现。Tenant、lifecycle、库存政策和未知成员拒绝。JSON 空关系合法；非空 BaseCode(10)/ManagerCode(20) 保留合同长度，但当前可信关系 Owner 缺失，明确返回 WAREHOUSE_RELATION_SOURCE_UNAVAILABLE。不能把 Sys_User、显示名或 M_Staff 的10位字段猜成20位负责人合同。

`wms-warehouse` 当前 add、字段权限、1–5范围和 WmsRoleScope 共同控制创建。部门通过内部一次性同事务能力绑定，权限版本过期拒绝；创建不自动增加角色/范围。Site 来自实际 Space_Site 的不可变原件和当前查询权限，禁用、跨租户、不存在拒绝，最终再核准确源版。权限/主体变化使实体、部门映射、结果和审计一起回滚。

原代码先 NFC/trim，保留大小写；10位 UTF-16 容量，控制字符/无效Unicode拒绝。唯一键 NFC+InvariantUpper / PostgreSQL C collation，最长100，不截断。创建在原生仓库表写围栏内比较所有原有及已删除锚，跨 Site 同一 Tenant 不得重复。未映射的旧业务 WarehouseCd 也不能经新建仓库偷偷采用；使用原生列比较核对这些引用。代码、Site及身份不能普通 PATCH 变更。

创建实体稳定ID、DRAFT、BizRevision=1、准确 Site 引用、规范键、原键结果、审计共同事务。新的辅助表只记录新创建的准确身份，不回填旧仓库生命周期。旧 AllowNegative=false/OutboundPriority=100 仍为兼容物理列，不作为新仓库的有效库存/路由许可。

原操作 GET/POST close 只接受同Actor当前 query/abandon权限。已提交操作通过当前对象读取范围回放不可变ID/结果/ETag，不回放敏感旧名/地址。撤销 add 后仍可有权回查。UNKNOWN 不代表没有效果；close 与创建使用同一真实 Owner 决定点；成功 close 阻止迟到创建。操作结束后新编辑必须使用新键。

## 旧业务隔离与查询维护

PostgreSQL迁移 `20261030008000_WarehouseDraftV1` 安装当前 public 原生业务表的 WarehouseCd/Code/Id 使用触发器（独立权限范围表除外）。任何命中新DRAFT仓库的业务 INSERT/UPDATE 拒绝 WAREHOUSE_NOT_ACTIVATED。创建核对实际消费者表/列与已安装触发器，确定顺序加 SHARE 锁，阻止悬空旧引用竞争；未来新增消费者却没有安装对应围栏时拒绝创建。旧仓库不被冒认为DRAFT或ACTIVE，原有未受影响行为保持。

新元数据不可直接删除、换锚或启用；创建必须在同事务有精确Owner原操作。第一次正式创建后该Tenant后续原生仓库新增必须走此协议，现存仓库代码/身份保留；不强制迁移旧生命周期。新仓库名称只允许原有独立 MASTER-NAME/1 Owner 维护，数据库要求本次不可变名称原结果；业务版仅实际改名+1，同值保存不升版。旧租户/旧仓库的名称行为不被该草稿规则改写。

`GET warehouses?page=N` 与 `GET warehouses/{id}` 仅返回当前对象范围内的正式草稿，50条/页。响应区分旧物理列观察与本版本准确 SiteRef、DRAFT、businessRevision；admissionGrant=false/NOT_ACTIVATED。旧 `/api/wms/warehouse/owner-v1` 仍是旧记录观察合同，不据它的未知生命周期推导作业资格。

页面 `/wms/warehouse-drafts` 从仓库查询入口进入：明确站点/类型、创建部门、名称/地址/备注，显示DRAFT；名称维护复用现有独立页面。浏览器仅保存 Tenant/Actor 范围的原操作 UUID；Web Locks+新鲜身份/权限版本防多页重复，未知结果只查询/封闭。撤销创建权不阻当前读恢复；失权、身份变化、卸载和迟到响应不回填敏感表单。

## 保留范围

新Location需要初始化隔离和真实层级政策；启用、停止、恢复、退役需要准确规则/外部义务/Owner保护。此版本未开放这些操作，不提供完整Transfer T01/T02地点列表，不改变T04或Stock/RSV物理效果。仅隔离本地PG18、实际Program HTTP和Vue验证；不迁移已有环境、不部署、不触发Actions，不作为生产/现场验收。
