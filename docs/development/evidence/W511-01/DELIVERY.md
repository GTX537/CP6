# W511-01 · Main 同名租户与认证目录实际验证

Main 基线 `3d6d95dbda2645acabf9c5d105ec4767debd00f2`。实际测试源 `3009e3179ad13cba706d65314dea7c5bec221636`。复用 Main W508 的已验证产品构建，产品输入自 `93b4e5eb6f8056a0c2714167abe3e7167e7c8b1d` 到本基线只有文档差异。只重新编译受影响的 CP6.Tests。实际执行 Windows、.NET 8、Main Kestrel 和隔离 PostgreSQL 18.6。两个模拟租户及用户使用相同显示名和用户名，分别持有自己的租户码、slug、ID 和密码。[W508 产品验证](../W508-01/DELIVERY.md) · [实际测试](../../../../CP6.Tests/Platform/IamInquiryPermissionPostgreSqlTests.cs#L137)

- 真实组织发现只返回四个已有字段，准确绑定两个已登记 slug、组织与 region。未知 slug 或未登记 client 均返回 404，响应无组织 ID、slug、用户名和目录字段。
- A 的实际登录 Cookie 尝试授权 B 时返回 `access_denied`，真实 PostgreSQL 无双方授权记录。A/B 各自真实完成 OIDC + PKCE，按实际 JWKS 验证 RSA 签名和各自 `org_id`、`tenant_id`、`sub`。原浏览器 issuer/jti、PostgreSQL 授权、全局 access session 和 refresh 记录准确关联到原登录，双方凭据和记录各自独立。
- 使用原凭据读当前 `/connect/crm-context` 得到自己组织和权限。只给 A 配置询盘权限，禁用 A 角色后同一凭据立即读取到更新后的动作范围。此前普通角色读取目录和密钥受限、CRM 的两组织旧会话切换分别沿原生证据复用。[普通角色](../W427-01/DELIVERY.md) · [CRM 旧会话](../W253-01/DELIVERY.md)

最终 `directory-03.trx` 实际 1 通过、0 跳过。前两次产品引用编译在未改动的 Core Roslyn 堆耗尽，均未启动数据库和运行用例。复用同源产品引用后编译测试项目并通过。原失败、最终 TRX、运行与停机证据位于 `D:/CP6-test-artifacts/W511-01`。专用 PostgreSQL 和 API 已停止。集中审查覆盖同名解析、未知不默认、会话来源和当前授权。

本窗交付 PGT-026 的 Main 认证目录实证。正式项仍需 CRM 共享 PublicRoute 权威与物理存储绑定 revision 切换后旧令牌的实际拒绝。现行公开登记及已有 CRM 切组织各按已执行范围引用。正式 PostgreSQL 维持 17 PASS / 10 NOT_RUN，原询盘维持 60/60。Actions 触发与取消均为零，部署为零。
