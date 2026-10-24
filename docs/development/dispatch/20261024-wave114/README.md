# W114 原图与实际身份持锁 Owner 的共同事务

COORD本聊天单人，七天目标ACTIVE，PostgreSQL唯一新增数据库。Main `6edd07112c6dd67cefda5554c0d9fb1449eb561c`、CRM `f5afba3e9bfb91119ca1d62bc5ff7bec3d121a51`、Handoff `5aac8893ba0917e623459b8f97fb17ea597b030e` 已核对；W113 Main525/CRM140/Handoff89完整finish，准确分支删除、席位释放，实际29.0min/92项相关检查。

## 前置与范围

当前实际身份持锁Owner要求Serializable，而原图PGv4要求ReadCommitted以防旧快照隐藏结构变化。真实服务令牌sub=service:<ClientId>也不能伪装成H01用户UUID。W114解决前一个直接前置并由现有实际配置Owner采用；服务身份/H01转换规则保留为后继，不能用伪造actor或强制NeedsReview绕开。

显式管理的原图Serializable事务先取得原图结构锁，再让PostgreSQL证明尚未建立查询快照，之后校验原版本/脚本/目录身份。只允许该受控事务入口采用，普通Serializable/旧快照事务仍拒绝；实际数据库隔离级别、连接和事务身份须核对，不依赖客户端标志。旧v1–v3入口及ReadCommitted保留，不改DDL定义或迁移既有库。

实际配置HTTP Owner显式启用后，在原有身份/权限持锁和最终门禁内，将已保存的原Bundle、原日历/Intake和既有原Site/Form绑定放入同一个事务。成功与重放维持原结果；权限、原图或提交故障一起回滚，不在网络响应后另补一份图。不会自动建立或伪造源Site/Form。需要真实PG并发验证锁前/锁后结构变更、普通旧快照拒绝、跨事务重用拒绝及实际HTTP原子性。

依据：[PostgreSQL显式锁与快照](https://www.postgresql.org/docs/current/applevel-consistency.html)、[SET TRANSACTION](https://www.postgresql.org/docs/17/sql-set-transaction.html)。这些是实现依据，本地实际行为才是交付证据。

## 任务和实际执行时间估计

|任务|交付|预计执行|
|---|---|---|
|W114-02 CRM|受控原图Serializable入口、真实配置Owner共同事务采用及PG/HTTP定向验证|45–70分钟|
|W114-01 Main / W114-HANDOFF|一次集中审查、台账四状态、三仓合并/准确分支清理/finish|20–30分钟|

合计65–100分钟；正式Main START `2026-10-24T14:29:24.960165+00:00`到finish计一次。复用DEV1/DEV2、自有PG55446及原构建缓存；不运行Actions，不改保护/既有环境，不部署生产。正式24/375、0/125、247NOT_RUN保持。

- [x] 原图受控Serializable事务与实际配置Owner采用。
- [x] 结构快照、事务绑定、实际权限/回滚/重放相关验证。
- [ ] 一次集中审查、三仓合并、准确分支清理及席位释放。

## 实际交付与生命周期

CRM [PR141](https://github.com/GTX537/CP6.CRM/pull/141)已采用 `d69bf0ec98613b0d00b7f546eaa241fc0c631235`，来源 `f1281a18ad90790d2481593f319d50176d702f02`；[单份交付报告](https://github.com/GTX537/CP6.CRM/blob/d69bf0ec98613b0d00b7f546eaa241fc0c631235/docs/development/evidence/W114-02/DELIVERY.md)。

显式OriginalGraphWritesEnabled默认false。实际配置Owner在原Serializable身份持锁、当前资源/签名信任与最终门禁下，共同保存源Bundle、原日历/Intake完整图、原结果、审计与Outbox。v4受控事务先取得结构锁，再由PG证明尚未建立查询快照；实际隔离、连接与backend xid绑定入口，普通旧快照/跨事务重用继续拒绝。源Bundle关联的既有Head行锁保留身份七锁协议及现有权限。重放只验证原图、不补写旧结果；缺失原表单、权限或提交故障全部回滚。Draft不代表激活。

83项唯一相关本地PG/HTTP检查通过、0跳过：7项受控事务成功复用、39项实际配置Owner、10项隔离v3、9项隔离v2、18项v1/v4原图与回执；锁协议冲突和测试版本混用均已定位修正，失败日志保留。 证据D:/CP6-local-test/wave114/foundation/final-validation.json。结构锁验证限于实际原图关系及本地并发，不宣称抵抗恶意DB管理员或未协调函数DDL。自有PG55446已停止，原数据/失败日志/缓存保留；无Web改动，无Actions、保护调整、既有迁移或部署。

CRM准确分支删除、DEV-2官方finish释放。完整提交、IssuedAttempt消费/重放、实际service/H01当前业务权限、编号与CommandReplay/AuditFact/OutboxFact完整事务和页面继续；不伪造service主体为用户UUID。正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR90](https://github.com/GTX537/CP6.AI-Handoff/pull/90)已采用 `cb37e979bda63af27c3ad43d94d93cc82acfb8c7`，准确分支删除、固定DEV-2 finish释放。 CRM分支删除/释放已核对；Main文档PR/finish继续，当前START后墙钟31.86min（原估65–100），最终按Main finish计，不叠加各仓。

本文件提交时Main最终合并/席位收尾仍待；最终证明以官方finish与final_proof为准，下轮真实开工统一核销，不另建记账分支。
