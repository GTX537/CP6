# W534-01 · 三组手工主数据写入口

2026-11-13 UTC，Main 基线 `6cfcc76f224e81ee7f08557472859445ff211cc5`，实测源码 `71ebdbbbd3875f82ada81fb4722d1d2f4564e23f`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。 [原验收](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1006](https://github.com/GTX537/CP6/issues/1006)

财务资产分类增改删、采购供应商手工价表保存和删除、计划品目策略保存和删除，共 7 个现行 HTTP 写入口。采用既有 `CurrentWriteAuthority` 与请求 `CP6Context`，先取共同根再读取当前权限与实际目标，持至提交。复用原动作键、模型、输入及返回。 [资产分类](../../../../CP6.WebApi/Controllers/Fin/AssetCategoryController.cs) · [供应商价表](../../../../CP6.WebApi/Controllers/Pur/SupplierPriceController.cs) · [品目策略](../../../../CP6.WebApi/Controllers/Plan/ItemPlanningPolicyController.cs)

## 实际验证

隔离 PostgreSQL 18.6、Main Kestrel、模拟租户、真实会话和 CSRF。不同数据库连接持锁与观察。基线的 7 个请求可在 IAM 撤权持根时返回 200。最终 7 个请求均在原生认证根等待，真实撤权提交后返回 403，三组目标表逐行快照保持。重新授权后七个动作均成功，采购价 `12.1234` 和计划数量 `12.12345678` 按各自原精度读回。 [实际回归](../../../../CP6.Tests/Erp/Pgt017MasterDataPostgreSqlTests.cs)

`baseline.trx` 7 个真实失败。最终 `final.trx` 7 项 PostgreSQL 加 21 项相关组件测试，**28 通过、0 失败、0 跳过**。构建零错误，原依赖警告保留。发布文件 SHA-256 和本地原始证据在 `D:/CP6-test-artifacts/W534-01`。与实际 Kestrel 业务验证一致的发布 WebApi SHA-256 为 `030918394d437188c6c1d69d0516aa0200d91f9fb68d096d7dd2e75ccf1f6ebe`。本地隔离发布启动后存活 200，匿名访问受保护路由 401，宿主与数据库均已停止。

一次集中审查核对七个动作均为请求内数据库写入以及正常成功、精度、实际锁序。采购询价回写使用 `RfqService.WriteBackPricesAsync`，可逐行写入同一价表，走独立入口，需保留并核对其原提交语义。财务过账、采购订单/收货、MRP 和其他分组仍需逐条完成。正式 PGT-017 为 NOT_RUN。Actions 启动与取消为零。采用、分支及席位以 PR 和 `D:/CP6-test-artifacts/W534-01/main-finish.json` 为准。 [采购询价](../../../../CP6.Core/Services/Pur/RfqService.cs) · [其他写者核对](../W531-01/DELIVERY.md#仍待核销的正式前置)
