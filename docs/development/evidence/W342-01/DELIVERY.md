# W342-01 真实 Stock 错 LOGIN 与原生角色隔离

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave342/README.md) · [原 PGT 场景](../../acceptance/postgresql-tenancy.json)

Main 基线 `451cb5e739f111caf05eff09075ed342b1f4a6d6`。COORD 在 DEV-1 单人执行 `codex/w342-01`。复用 W341 同一个隔离 PostgreSQL 18.6 的两个模拟租户及实际 Kestrel 宿主。新增错误绑定宿主和普通角色原生检查，产品源码沿用已采用版本。

## 实际验证

- B 使用自己的真实当前会话、JWT Cookie 和 CSRF 调用原 V1 调整。宿主故意把 B 配到 A 的普通 LOGIN 和有效凭据。原入口实际返回 503 `AUTHORITY_FENCE_UNAVAILABLE`。从正确宿主查询原操作键得到 `UNKNOWN`。两租户的准确库存 ID、数量、流水、原操作及通知数与前值一致。
- A 的原生连接确认 `session_user=stock_creation_a`，对 B 无角色成员资格。真实 `SET ROLE`、`SET SESSION AUTHORIZATION` 均被 PostgreSQL 拒绝，SQLSTATE 为 `42501`。自定义 `app.tenant_id` 接受 B 的值后，原生映射仍为 A。直接查询两个实际库存主键只读到 A，A 在自己的真实写栅栏内更新 B 的物理主键影响 0 行。
- `tenant-binding-http.trx` 实际通过 1 项、零跳过。该项同时运行既有 V2 创建、调整、移库和 A/B 回归。TRX 在 05:37:39 UTC 完成。原生观测 `stock-tenant-binding.json`、数据库日志和 TRX 保存在 `D:/CP6-local-test/CP6-72H-20261109/W342-01`，实例已停止。 [真实入口与原生回归](../../../../CP6.Tests/Erp/StockTenantBindingPostgreSqlTests.cs)
- 复用既有实际组件场景：守卫收到已打开的 A 物理连接和 B 预期身份，保持原 PID 并拒绝，业务回调计数为零。该组件验证还覆盖错库、特权登录和非预期 search path，记录在原来源。 [既有物理连接用例](../../../../CP6.Tests/Persistence/Tenancy/TenantStoragePostgresTests.cs#L58)

集中审查覆盖配置中的有效 A 凭据、B 真实业务身份、权限异常与原操作结果，以及独立的 PostgreSQL 角色和 RLS 边界。这里采用的是原 PGT-003/004/007 在现有 Stock 布局的部分证据。正式矩阵及选定租户布局的完整验收另按原范围核定。普通 Actions 为手动入口，R2 自动入口匹配版本标签。本窗 Actions 触发和取消为零。

## 工时和接续

开工估算 14–24 分钟。测试完成距开工约 284 秒，含结果检查的本机单调取样为 455 秒。复用了 W341 实际数据和 W335–W337 宿主回归。完整工时按正常远端采用及席位释放回读计时。

下一步停掉旧宿主并核普通连接排空，在隔离库轮换同一 A 角色凭据，验证旧配置被拒绝和新宿主继续处理 A/B。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
