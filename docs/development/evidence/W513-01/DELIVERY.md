# W513-01 · CRM 询盘配置地区与外部选择器

现行 CRM 配置入口由 #204 采用测试源 `415c013262661dc0beb5b09e2631e43938ec361e`，核对远端 `a23adc2000c2f3dc30c84e101272ea08b9ab63cd` 与测试树一致。环境为本地实际签名 Kestrel、.NET 8、独占 PostgreSQL 18.6、普通 Owner 连接及 TLS 身份服务。租户、身份与业务输入为模拟。 [CRM #204](https://github.com/GTX537/CP6.CRM/pull/204)

- 基线实际安装并读取地区 `us`。A 的有效 RSA 登录、持久受保护会话和实时身份均能解析，所称地区为 `unregistered`。配置保存实际返回 `403 crm.permission_denied`，期间已打开 PostgreSQL 表单定位后端 19696 和 Owner 后端 3284，调用一次签名权限服务。无业务效果。以 `configuration-routing-red-01.trx` 保留实际失败。 [CRM #203](https://github.com/GTX537/CP6.CRM/issues/203)
- 配置保存、读取、编辑上下文和生命周期共用原租户选择入口。在成功捕获身份之后，用同一可信登记的地区判定，在首次表单/Owner 连接前拒绝。实际复测返回原 403，实时身份核对一次，业务库 checkout 和签名权限请求均为零，七组业务计数保持。事务内身份、当前权限和登记检查继续执行于正常授权路径。
- 同一有效 A 凭据在正文或查询各加入外部 `organizationId`、`schema`、`region`，六次均为 `400 crm.request_invalid`，无字段回显、实时身份请求、签名权限请求、业务 checkout 或写入。直接签名请求的头部对照保留现行语义，带伪造租户、地区和 schema 头的合法 A GET 返回 A 原草稿，其他租户无写入。Node BFF 现行白名单在转发时去除这些外部头。

最终 `configuration-routing-green-02.trx` 为 5 通过、0 跳过。包括实际地区、外部选择器、正向配置保存/原样恢复/读取、编辑上下文和生命周期当前授权回归。原始失败、实际 PostgreSQL 连接观测与最终结果在 `D:/CP6-test-artifacts/W513-01`，两次独占 PostgreSQL 及协议观察器已停止。集中审查覆盖共享入口、原响应、真实零连接和原授权路径。

PGT-004 已有 W508 的 Main 真实存活探针和可信入口拒绝，以及本窗的 CRM 登记地区、正文和查询。正式卡还要求带伪造外部头的整个新业务请求在取连接前拒绝。现行头部语义实测为不具选租能力的额外信息，合法 A 请求继续。PGT-004 维持 NOT_RUN。正式矩阵 19 PASS / 8 NOT_RUN，原询盘 60/60。CRM 本地及远端任务分支定向清理，DEV-2 席位释放。Actions 触发与取消均为零，部署为零。 [W508](../W508-01/DELIVERY.md) · [正式矩阵](../../acceptance/postgresql-tenancy.json)
