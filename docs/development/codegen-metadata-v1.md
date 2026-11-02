# CodeGen版本化元数据V1

新文档使用`/api/pub/codegen/metadata-v1/{metadataId}`，MetadataId为调用者固定UUID。边界只接受当前可信租户和原会话主体；客户端不能提供TenantId/SavedBy。必须为PostgreSQL，Owner使用独立干净Context/READ COMMITTED事务及现有IAM完成门；同门内最终复查会变化的当前会话和权限。内部已定义合同直接复用，不新增证明/运输登记层。

|调用|行为|
|---|---|
|GET 根路径|当前快照；view权限；不存在404|
|GET versions/{revision}|指定正版本的不可变快照；view权限；不存在404|
|POST saves|OperationRef、ExpectedRevision、Document；新文档ExpectedRevision=0，已有文档必须等于当前版本|
|POST saves/read|只提供原OperationRef，查询该MetadataId下当前原主体的原结果；view权限；不存在404|

Document包含Table、Columns、TemplateRef、TemplateVersion。Table为entityName/module/tableName/resourceKey/seqBizKey/codeField/routePath/menuName；Columns为name/clrType/label/required/inList/inForm/sort，沿用固定模板输入边界。模板必须`cp6.codegen.crud.embedded`/`1.0.0`，不是用户脚本或动态模板。请求体1MiB，未知JSON字段拒绝。响应由宿主统一API envelope包装；快照含MetadataId、TenantId、Revision、Document、SavedBy、SavedAtUtc，保存回执再含OperationRef。NoStore禁止缓存元数据结果。

首次保存要求当前view+save权限；先在租户/MetadataId内查询原操作。同一主体、同一操作原字节及同一输入返回原回执，不重新检查后来改变的模板或CAS，也不重新要求save。原操作不同输入409，不返回他人的原操作结果；view撤销或原会话失效后不能恢复。新操作CAS不匹配412，不修改当前版本；名称冲突409，版本耗尽422。请求输入不完整立即400。

一次事务写documents当前指针、versions不可变快照和commands不可变原请求/结果。版本从1开始逐次加1；数据库约束及触发器防止孤立版本、缺回执、历史更新/删除/截断。完整JSON和操作原键用bytea保存，哈希仅作索引且命中后检查原字节，保留Unicode/NUL、可空和尾空格。实体名沿用当前PG的bpchar/既有不区分大小写排序合同，并且按TenantId唯一。schema是共享Owner命名空间，不是租户schema权限隔离；TenantId与权限约束保持。

新旧写入口使用原Pub_GenTable表锁串行决定名称归属。已有旧同名记录返回CODEGEN_LEGACY_METADATA_REQUIRES_ADOPTION，不能自动接管；已存在V1同名时旧入口数据库触发器拒绝，旧Save映射CODEGEN_METADATA_OWNER_REQUIRED/409。不同租户可用相同名称；旧行与列不迁移、不重写。新接口暂不提供列表、删除、自动导入或文件应用。

提交开始后的异常返回CODEGEN_OUTCOME_UNKNOWN/503，不能判断为无效果。使用原MetadataId+OperationRef查询，或发送原封不动的原请求恢复；404只说明本次未查到，不能证明最初提交无效果。新的原操作键不得作为盲重试手段。提交前错误回滚，若无法确认回滚同样返回UNKNOWN。

Vue的版本化面板取代原页面无版本保存按钮。即时预览成功后才能保存；原请求先写当前标签页sessionStorage，再发送API。未知期间锁定目标和新保存；刷新同一身份恢复原请求及其草稿，当前标签页存储不可用则发送前失败。查询404或未知后的重试拒绝仍保留原记录；明确首次412保留当前草稿，要求显式读取最新版本，不自动重套版本。用户可编辑草稿但保存响应不覆盖它；刷新恢复的是原请求草稿，后续尚未保存的编辑不持久化。保存ID在页面显示，应保留用于后续读取；当前版本读取不自动迁移旧记录。

UI身份epoch改变时清除原身份表单/预览并丢弃晚到结果，pending按身份分键保留，不向新身份自动恢复或重放。sessionStorage只供同标签页恢复，不保证关闭标签页后保留；服务器原结果仍可由有权的原主体按原键查询。页面仅使用安全整数版本，超出范围明确要求使用版本化接口，不发送舍入版本；服务端仍保留完整bigint合同。

本期只保存元数据，文件比较仍为[纯预览](codegen-comparison-v1.md)。历史元数据接入、正式文件应用/实际路径与并发保护、真实业务编号消费者和WP02-16整卡联合验收仍为后续范围。
