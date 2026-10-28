# Main.Wms.LocationHierarchy.v1

WMS-MASTER-01 v1.1.1 §6.2要求的可信层级来源与结构预检。本实现先交付实际策略Owner，正式Location创建和§6.2.1 InitializationFence仍未准入。

## 行为

独立页面 `/wms/warehouse/:id/hierarchy`，从原仓库查询或新仓库草稿进入。API `/api/wms/warehouses/{warehouseId}/hierarchy-v1`：GET当前、GET versions/{version}、POST commands、POST precheck、GET operations/{uuid}及POST operations/{uuid}/close。命令/嵌套边/预检拒绝未知成员；parentLocationId必需显式null或UUID，不能缺失推断根。预检只读，POST仍要求CSRF。

策略必须显式提供Enabled、根层级集合、父子边集合和原因；不采用DEMO的“1根、相邻边”默认。原生层级1区/2通路/3架/4层/5格。根1–5，边严格递增，可显式跨级；拒绝重复、越界、反向和从任何根不可达的父级。集合排序后参与原操作身份，原因仍属于原始意图。停用不删除历史；重新启用须重新检查现有树。

发布使用独立当前 `wms-warehouse:hierarchy-policy` 加当前query、原生仓库稳定ID、原Owner 1–5数据范围/真实部门映射及WMS全仓区域范围。仅具某区域的角色不能控制整个仓库。新增菜单动作目录不授予任何角色。父节点预检另外核当前 `wms-location:query`、父对象本身部门和区域范围；跨租户、跨仓、无权父节点不产生可用结果。仓库字段值与库存原数不放入策略原操作回执。

## 原件和事务

`cp6_location_hierarchy_v1.heads/versions`：每租户+原生仓库一个稳定策略ID；不可变原件绑定主体、原因、时间、父子定义和版本/digest。首次期望版本absent、结构0，首次保存后policy1/hierarchy1。变更发布CAS两个轴；同值并且两个轴仍匹配只写一次NO_CHANGE原键结果/审计，不升策略版。历史按原版本读取；同键不同意图409，旧版本412。当前query权限和相同actor下允许撤销发布权限后恢复原结果。UNKNOWN不代表未提交；关闭与发布在真实Owner事务锁中排序，CLOSED不复活。

真实PostgreSQL本地事务沿现有OperationsOwner锁顺序：当前IAM/组织/WMS表，再策略表。当前原生树的父代码匹配使用数据库实际排序规则，单次外连接解析稳定身份，不能用内存大小写/重音近似；完整树兼容检查不披露受限子节点标识、计数或内容。合法边层级严格递增，循环/孤儿/不匹配根边拒绝启用策略。预检必须按当前策略和结构版本重核完整树与已授权父节点。

原生 `T_Location` INSERT/DELETE及实际Id/Tenant/Warehouse/code/parent/level/deleted变化由真实触发器增长独立HierarchyRevision；名称/条码/容量无结构变化不升。仓库身份锚已绑定后不允许改码、换租户或硬删除。原件/控制行不允许任意更新/删除/截断；递增头必须有对应原件及同事务正确journal。每次进入与最终点检查实际启用触发器；缺失原生捕获能力拒绝输出可信预检。原生旧写入仍可形成待修复树，但新版本即时失效，预检不把它重新认作合法。

## 界面与边界

页面明确选择根/边/状态，无默认发布。当前策略和结构分别展示；原生父候选分页并说明仅当前可披露范围，不把空页说成不存在。只在tenant+actor+warehouse+当前origin本地保存待确认UUID；Web Locks互斥、新鲜服务端范围和双版本检查、未知只回查/关闭。拒绝回执范围错配；权限失败、身份/路由变化和晚返回清理页面私有状态，保留原键恢复记录。

每个预检返回CreationGrant=false、InitializationFence=UNAVAILABLE、RequiresFinalRecheck=true；不插Location，不授予容量、不改预占/Source，不移除W206的DRAFT仓库使用围栏。启用/创建必须由后续真实初始化与业务Owner再次取当前来源和结构，不把本次预检当长期许可。没有把该新Owner加入W205固定9来源Transfer profile；跨合同采用须独立版本。SQL Server历史保留，本次仅新增PostgreSQL迁移，不运行既有环境迁移。
