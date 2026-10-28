# 库位规则字典与准确来源 v1

Owner `Main.Wms.LocationRuleReferences.v1`，原操作合同 `LOCATION-RULE-REFERENCES/1`。补齐 WMS-MASTER-01 v1.1.1 §4.1.1 的本域引用权威及 W209 候选的显式来源绑定。字典由当前有权人员明确配置；不是物理测量、审批、占用评估或生产许可。不预置 RAW/EA/RECEIVE 等测试字典。

## 仓库字典

API `/api/wms/warehouses/{warehouseId}/rule-references-v1`：GET 当前上下文，GET `versions/{version}` 原件，POST `commands`，GET `operations/{uuid}`，POST `operations/{uuid}/close`。

当前 `wms-warehouse:rule-policy` 控制配置写入，另核独立 warehouse query、实际仓库部门/本人范围和完整仓库 WMS 范围。只有局部区域权限不能修改全仓库政策。读取和原回执恢复使用当前 query；未提交原键关闭使用 abandon。目录增加动作不自动授予角色。

命令为 `operationId,expectedVersion,definition,reason`；首版 expectedVersion=`absent`，以后为准确十进制 Int64 字符串。definition 明确包含 enabled、metrics、precisions、domains，三个目录各1–100项。请求上限256 KiB。未知成员拒绝，所有政策布尔值显式必传。

字典标识符及候选的版本化代码引用遵守原规格非空白、UTF-16长度、无控制字符和合法Unicode约束；保留准确大小写与首尾空格，不自动裁剪或强转Guid。原生UOM自身的代码/引用合同继续由真实Owner校验。

- metric：id、dimensionKey、1–100个准确原生 UOM 引用 `{owner,id,version,digest}`。真实单位 Owner 为 `Main.Erp.UomProfile.v1`。各单位须当前可读、启用、精确版本/摘要匹配，量纲匹配并共享同一个准确原生 BASE 引用。相同量纲文字不能证明单位可换算；RATIO 的基准也须当前有效。
- precision：id、scale（0–8）、rounding=`REJECT`。这是规则容量输入的精度政策，不改变原生 UOM 的合法精度或自动舍入。
- domain：id、ruleFamilies、metricIds、precisionIds、productTypes、operations；逐项明确 allowEmptyProducts、allowEmptyOperations、allowUnlimited、allowOpenEnded、allowPickable。品类与动作代码由这个本域字典定义，配置不证明其他业务 Owner 已支持某效果。空允许集合保持空集合语义。

各版本保留实际 UOM 与基准引用、代码和量纲，依据当前真实原件生成，不接收客户端自报的解析结果。已启用定义修改仍核完整来源。停用只允许把当前准确内容的 enabled 改为 false，允许在原单位已改版/撤权后停用；不能夹带其他定义修改。内容无变化为 NO_CHANGE，保留版本；更改原因进入本次原操作审计。

`cp6_location_rule_refs_v1` 使用现有 Owner 域共享 schema，TenantId 与真实 Warehouse 关系、当前权限和复合键继续保留；同数据库与 UOM 的事务锁配合。没有把 schema 名称当租户授权，也未批准全库改为一租户一 schema。不可变版本/哈希、连续前驱、真实仓库身份、当前原件外键和同事务操作凭据由 PostgreSQL 控制；原文不修改、不删除、不清空。控制缺失时拒绝使用。

## 候选绑定

POST `/api/wms/locations/{locationId}/rules-v1/{proposalId}/resolve`，正文 `operationId,expectedPolicy`，必须 If-Match 当前候选 ETag。expectedPolicy 为上下文给出的准确字典 Owner/id/version/digest。当前 resolve、全部候选字段读写权、实际 Location 和独立 Warehouse query/范围必须满足；另核 UOM 当前独立 query/部门范围，最后提交时重核当前来源和权限。

解析检查准确 domain/metric/precision 版本、规则族和目录成员、真实 UOM 代码/版本、量纲与基准、精度和原生包装倍数。LIMITED 数量仍保留13整数/8小数，有限0合法，不经过浮点或舍入。UNLIMITED 只在明确允许时绑定。UNKNOWN 容量、缺 ValidFrom、无政策支持的空集合或无结束时刻不能完成绑定；仍可保存原始草稿。

真实解析产生新候选不可变版本，保留每个字段的 Owner/id/version/registryVersion，以及准确字典、UOM 和基准引用。registryVersion 为字典原件稳定ID/版本。相同绑定且 ETag、来源仍有效时为 NO_CHANGE；绑定本身不改变选择器0/null、不批准、不写库存或库位容量/封锁。

旧 UNRESOLVED 版本不回填。修改草稿产生的新版重回 UNRESOLVED。字典版本变化不会把原引用静默解释为新版本：原 selectorKey 的适用域引用不可 PATCH，采用新适用域版本须新建候选；稳定 family/metric/domain-id 相同则复用同一选择器。过去 RESOLVED 表示保存时成功绑定，`requiresCurrentRecheck=true`；所有后续送审/应用必须重新核当前字典与 UOM，不得凭历史状态放行。

兼容 W209：context.referenceAuthorities 保留状态字符串，新增 referenceAuthorityBindings 数组、referencePolicy、referenceCatalog 和 warehouseId。字段数组逐项给 field/owner/registryVersion/availability。CONFIGURED 只说明字典存在，当前来源须在实际解析中核验。视图新增 referenceBinding；任一候选字段不可读则隐藏完整绑定和 OriginalJson。历史字典和原回执只按自身当前读取权披露，历史不是现行来源凭据。

## 页面与恢复

`/wms/warehouse/{id}/rule-references` 从仓库查询进入，逐项编辑目录并从当前有权原生 UOM 页选择准确单位，支持历史和停用。`/wms/location/{id}/rules` 展示字典代码/版本和已保存候选绑定。绑定按钮明确针对已保存候选，未保存编辑不随请求提交。

两个入口只把 UUID 保存在浏览器 origin+Tenant+Actor+目标的恢复位置；Web Locks、当前身份/目标/版本复核、准确回执绑定。未知结果不换键重试；失去写权仍可按当前 query 恢复。路由/身份变化及401/403/404清除私有页面状态，晚回调不能覆盖新身份。

本合同未完成规则送审/可信批准/实际应用、可靠时钟/完整非重叠占用/效果点保护、Selector 优先规则或仓库/库位激活。正式 MASTER、Transfer/Stock 与原七天完整出口继续保留；本地隔离验证不作为生产验收。
