# W113 公开回执独立 HTTP Owner

COORD本聊天单人，七天目标ACTIVE，PostgreSQL唯一新增数据库。Main `2fecb766c5a9fde350fa98716b5faa1de1b5b6d9`、CRM `3f12f94215c1b7beec182312959ee4e39c77bd4f`、Handoff `bceba80f0bcc2c5370c50b6069a981426dcfb27d` 已核对。W112 Main524/CRM139/Handoff88完整finish，准确分支删除、席位释放；实际28.45min、82项相关验证。

## 前置与范围

原R3 V15 canonical-contract规定GET /api/site/{siteKey}/receipts/{receiptId}，仅使用受保护的__Host-cp6_inq_receipt HttpOnly/Secure/Strict cookie，错误浏览器/租户、未知、缺失、过期、撤销、匿名化和已清理统一404 crm.receipt_not_found。W112原完整图/Proof读取已经交付，可在完整提交业务消费前独立接入此读取入口。

本轮新增显式启用的回执Owner与原路由、受保护凭据cookie的原绑定验证及宿主独立ReadCommitted读取事务。既有配置/公开签发Serializable保持；新入口不自动安装或迁移数据库。回执cookie准备只验证已有原图和原浏览器凭据，不建立H01/编号/消费或业务成功。实际完整提交仍由后继原Owner落实；本地夹具不冒充正式业务验收。

## 任务和实际执行时间估计

|任务|交付|预计执行|
|---|---|---|
|W113-02 CRM|独立回执Owner、cookie/路由、受限角色PG/HTTP边界和相关回归|35–55分钟|
|W113-01 Main / W113-HANDOFF|一次集中审查、台账四状态、三仓合并/分支清理/finish|15–25分钟|

合计50–80分钟；Main START `2026-10-24T13:53:32.080557+00:00`，最终finish墙钟计一次。DEV1/DEV2固定复用、自有PG55446和原构建缓存；不运行Actions、不改保护、不迁移既有环境、不部署生产。正式24/375、0/125、247NOT_RUN不变。

- [x] 原合同GET、受保护回执cookie与原图读取。
- [x] PG/HTTP拒绝、撤销、租户和宿主隔离验证。
- [ ] 一次任务审查、三仓合并、准确分支清理及席位释放。

## 实际交付与生命周期

CRM [PR140](https://github.com/GTX537/CP6.CRM/pull/140)已采用 `f5afba3e9bfb91119ca1d62bc5ff7bec3d121a51`，来源 `f8ce8b982e7da7f6328d7cef5b9f5bf1675920dd`；[单份交付报告](https://github.com/GTX537/CP6.CRM/blob/f5afba3e9bfb91119ca1d62bc5ff7bec3d121a51/docs/development/evidence/W113-02/DELIVERY.md)。

公开回执独立Owner与原GET /api/site/{siteKey}/receipts/{receiptId}接入原Submission/Attempt/Proof验证读取。使用受保护、按origin隔离的__Host-cp6_inq_receipt Cookie，来自原图随机凭据、实际浏览器和8天到期；不续期、不重发Cookie。显式CrmInquiryNative:PublicReceiptsEnabled默认为false，要求已安装PGv4，独立ReadCommitted读取/readiness保留原配置和签发Serializable。BFF严格校验原五字段、receiptId和共同404语义，拒绝额外字段/重复键、Set-Cookie/ETag和跳转。

92项唯一相关本地检查通过、0跳过：62项PG/HTTP、30项Web与相关类型检查；真实Node BFF→API→PG回执和原表单，以及原配置写入/重放通过。首次缺失Node产物及新增Web夹具合同错误已修复并定向重验，其他有效成功复用，失败记录保留。 证据D:/CP6-local-test/wave113/foundation/final-validation.json。自有55446已停止；原数据/日志/缓存保留。普通无效回执共享404 crm.receipt_not_found及200–220ms最短时间窗；这不证明所有负载下时序侧信道消除，也未执行8天真实等待到期。

CRM准确分支删除、DEV-2官方finish释放。无Actions、保护调整、既有迁移或部署。本地完整图来自明确存储夹具，回执Cookie的服务端准备接口尚待完整提交Owner接入；实际service/H01当前权限、编号/消费/重放/CommandReplay/AuditFact/OutboxFact共同事务与公开页面仍待。正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR89](https://github.com/GTX537/CP6.AI-Handoff/pull/89)已采用 `5aac8893ba0917e623459b8f97fb17ea597b030e`，准确分支删除、固定DEV-2 finish释放。 CRM分支删除/释放已核对；Main文档PR/finish继续，当前START后墙钟26.13min（原估50–80），最终按Main finish计，不叠加各仓。

本文件提交时Main最终合并/席位收尾仍待；最终证明以官方finish与final_proof为准，下轮真实开工统一核销，不另建记账分支。
