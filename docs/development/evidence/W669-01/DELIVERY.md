# W669-01 CRM 导出服务 V2 的精确 CSRF 入口

基线 Main `640800b350e01f7046a9f7140fbe0f776587080d`。实际 RED 沿用 W669-01-CHECK 在完整 Kestrel 宿主对真实请求形态取得的 `403 E-SEC-010`。从原基线到本轮起点，CSRF 中间件、服务控制器、权限签发器及登记发布器无差异。冻结 CRM V2 客户端使用显式 Basic，未发送浏览器 Cookie。 [#1186](https://github.com/GTX537/CP6/issues/1186) · [登记](D:/CP6-test-artifacts/W669-01/main-start.json) · [真实 Negative TRX](D:/CP6-test-artifacts/W669-01-CHECK/native-4/main.trx) · [真实响应](D:/CP6-test-artifacts/W669-01-CHECK/native-5/v2-transport.json) · [冻结客户端](D:/CP6-test-artifacts/W649-01/crm-main-verification-source/src/CP6.CRM.Api/Inquiry/CrmInquiryServiceIdentityV1.cs:56)

`CsrfMiddleware.IsExempt` 按现有服务路径风格精确匹配 `/connect/inquiry-service-context-v2`。新增的测试运行完整 Program 和生产服务登记 worker。在隔离 PostgreSQL 18.6 放置模拟主体、角色、字段、站点及表单，并注册独立 CRM PostgreSQL Store 和受限 LOGIN。实际服务 HTTPS 代理信任边界由回环配置提供。 [产品差异](https://github.com/GTX537/CP6/blob/932f05e9cc5901abf3decc882d429062e9d20642/CP6.WebApi/Middleware/CsrfMiddleware.cs#L84) · [回归源码](https://github.com/GTX537/CP6/blob/932f05e9cc5901abf3decc882d429062e9d20642/CP6.Tests/Platform/InquiryExportServiceCsrfPostgreSqlTests.cs#L15) · [执行入口](D:/CP6-test-artifacts/W669-01/run-fixed.ps1)

| 实际请求 | 响应与观察 |
| --- | --- |
| 原 Basic 凭据、可信服务代理、无浏览器 Cookie 和 CSRF 头 | HTTP 200，`no-store`、V2 Ed25519 签名、请求 nonce、组织、原服务主体和 epoch、PG 真实来源快照版本和哈希、7 个导出字段及原 `StoreId/tenantVersion` 写 fence 均核对通过。 |
| 错误 Basic、匿名、仅浏览器 Cookie | 均为 `401 invalid_client`，无写 fence。 |
| 缺少可信 HTTPS 代理、有效身份但超实际表单范围 | 分别为 `400 invalid_request` 和 `403 insufficient_scope`，无写 fence。 |
| V2 相邻后缀、原浏览器 completion close 缺 CSRF | 均为 `403 E-SEC-010`，无写 fence。 |

定向结果 **27 通过，0 跳过**，含 1 项完整原生 HTTP、9 项现有服务授权及 17 项现有 CSRF 契约。当前产品范围复用 W547 的实际 CRM Owner 两种提交顺序及 W669-01-CHECK 的独立两库冷拒和双 ACK 恢复。 [本轮 TRX](D:/CP6-test-artifacts/W669-01/native/main.trx) · [W547 业务两序](https://github.com/GTX537/CP6/blob/932f05e9cc5901abf3decc882d429062e9d20642/docs/development/evidence/W547-01/DELIVERY.md) · [冷拒和双 ACK](D:/CP6-test-artifacts/W669-01-CHECK/DELIVERY.md)

## 本地发布与交接

候选 `932f05e9cc5901abf3decc882d429062e9d20642` 的完整差异只有两个任务文件，`git diff --check` 通过。隔离本地发布确认两个受测源文件 SHA-256 与候选工作树和 Git blob 一致，WebApi、Core 与 PostgreSQL 迁移程序集和实际测试逐一匹配。310 文件清单 SHA-256 为 `7E3021CAC3AF66EFE215C96C17074129C7DF9198DB5740D720841DC7345EAD7B`。 [受测源文件](D:/CP6-test-artifacts/W669-01/tested-source-file-sha256.json) · [发布文件清单](D:/CP6-test-artifacts/W669-01/main-publish-smoke/publish-sha256.json)

首轮发布冒烟沿用 Development 配置，其中 CSRF 为关闭，故相邻后缀返回 404。保留初次结果。逐文件复核并复用同一发布包，启用现行 CSRF 规则后在独立 PostgreSQL 57276 完成 `DatabaseInit`，Kestrel 57277 实测 `/health/live` 200、匿名 `/api/auth/profile` 401、关闭 CRM 功能时精确 V2 路由 404、相邻后缀 403。签名业务 200 来自前述字节相等的原生测试。 [首轮冒烟](D:/CP6-test-artifacts/W669-01/main-publish-smoke/result.json) · [最终冒烟](D:/CP6-test-artifacts/W669-01/main-publish-smoke-csrf/result.json)

本地 `codex/w669-01` 保留候选，DEV-2 工作树干净。57260–57279 无监听，任务 PostgreSQL 和宿主均已退出。席位仍登记给 W669-01，由协调方集成和释放。没有远端、Actions 或生产操作。 [停止及工作树证明](D:/CP6-test-artifacts/W669-01/process-verification.json)
