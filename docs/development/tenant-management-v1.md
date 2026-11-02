# 租户配置与启停 V1

此合同补充现有TenantAdminService，版本化管理现有租户的名称、到期时间、备注、时区及Enable。创建租户的一次性密码、认证政策Owner、Gdpr删除和旧兼容API保留原范围。共享租户表和TenantId不变，没有一租户一schema切换。

| 调用 | 合同 |
|---|---|
| GET /api/platform/tenant/{id}/management-v1 | 返回当前租户快照和Revision；不存在404 |
| POST 同根路径/commands | OperationRef、ExpectedRevision、Kind(update/suspend/reactivate)、Configuration；update必须给完整配置，启停必须为null |
| POST 同根路径/commands/read | OperationRef；当前平台管理员且为原actor/actorTenant才可读取原结果；未找到404 |

Revision/ExpectedRevision以十进制字符串输出/发送，保持PostgreSQL bigint精度；服务器也接受JSON整数。OperationRef是非空有效Unicode原键，不trim，不截断；bytea保留原操作、原请求和原结果，哈希只用于索引并保留原字节比较。同一目标/原键同原actor/actorTenant和相同请求恢复原结果；异义409，他人原键403。恢复在当前目标/CAS检查之前，即使目标后来变化或删除，已保存结果仍是原结果。新操作才核对当前版本，冲突412；提交前失败回滚，提交开始后结果不能确认返回503 TENANT_OUTCOME_UNKNOWN，原键查询或原样重试。

配置字段Name最长200个.NET字符、Remark500、TimeZoneId64；Name不能为空白，不默默trim名称/备注。数据库文本不接受NUL或无效Unicode时在边界拒绝。ExpireDate沿用timestamp without time zone语义，V1明确接受无偏移、最多6位小数的本地日期时间，拒绝UTC/偏移及不能存储的100ns尾数，不静默损失精度；null不限期。时区沿用现有合法IANA/Windows识别及空白归null规则。

前向迁移先取得Sys_Tenants表写锁，再回填每个既有租户Revision=1并安装触发器，同一迁移事务完成。原记录不改写；迁移期间先完成的创建也被回填。新建从1开始；TenantCode/Name/Enable/ExpireDate/Remark/TimeZoneId实际字节变化推进版本，大小写和尾空格也可见。未变值不推进，认证策略字段独立管理。DELETE推进并保留版本墓碑；同ID重建继续推进，不能ABA回到1。ID原生变更同时推进旧身份和新身份。TRUNCATE保留所有版本并推进。历史命令不可改删/截断，版本不可删除/倒退；未实现命令历史保留期限/擦除编排，不扩称GDPR整卡完成。

旧Update/Suspend/Reactivate仍兼容，原生触发器让这些变化对V1 CAS可见；它们尚未强制接收ExpectedRevision或提供原命令结果。因此本期不宣称全部旧写者已采用CAS。业务、原管理审计、身份/Outbox效果和原命令在同一PG事务内完成，复用已有核心变更函数。共享IAM完成门与当前actor行锁阻止并发授权/身份/DB会话变更越过最终决定，原会话在获得该门后完整校验。

合法操作可以改变管理员自身租户并使旧会话失效。授权在效果发生前确定，不能在自身政策变更后重新套用旧会话而错误回滚该效果。提交前只复核门外变化的access到期/注销黑名单，以及原授权租户的时间期限；DB授权在门内保持。自身改到期/停用成功后旧会话失效，原actor在合法重新登录后仍可查询原命令；若停用自己的租户，恢复须由另一当前平台管理员执行。未经允许的并发注销或原租户期限到达则全部回滚。

HTTP使用真实当前登录、独立scope、同一Context和服务；严格JSON不接收Actor/Tenant作用域，NoStore和既有CSRF生效。普通用户403，原平台claim撤销使原会话401，已注销401；客户端身份不授予平台权限。

页面从租户列表的“管理配置与启停”进入。读取成功后才能保存/确认启停，读取失败禁止默认空字段覆盖；保留完整日期时间与尾空格。412保留草稿并要求显式重读。发送前在当前身份隔离的sessionStorage保存准确命令；未知时可刷新、查询和原样重试，后续404/403不抹去之前未知结果。原结果只表示历史回执，不能充当当前状态；继续操作须重读。身份切换丢弃旧响应并隐藏旧数据，存储失败不发送。

实际验收用模拟业务数据、本地隔离PG18和Main Program登录cookie；页面用实际Chrome/Vue及明确API/身份夹具。没有生产部署、存量环境迁移、创建命令恢复或整卡完成声明。详见[交付报告](evidence/W264-01/DELIVERY.md)。

全部业务writer共同fence、跨系统映射/传播与worker恢复仍按原卡准确范围核销，本期不声称所有下游已采用。
