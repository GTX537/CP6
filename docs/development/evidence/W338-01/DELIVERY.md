# W338-01 直接库存调整入口的普通事务

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave338/README.md)

Main 基线 `699e0d8d762d7539ee7cc661d7cf2c1225fe10ff`。COORD 在固定 DEV-1 单人执行 `codex/w338-01`。已启用 `StockReadTenancy:EnableV1Adjust` 的宿主为原公开 `POST /api/wms/stock/apply` 使用准确入口用途和普通库存写者。调用方收到原 `WM-MSG-071` 和实际流水号。当前字段、部门、库位和提交边界沿原 Stock Owner。各内部业务入口继续用自己的调用协议。 [运行说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-v1-adjustment-owner)

## 实际验证

Windows、PostgreSQL 18.6、Npgsql 8.0.8、net8.0。真实 Kestrel、JWT Cookie 和 CSRF。使用隔离普通 LOGIN、模拟库存、客户货权、部门及权限。

- `red-direct-apply.trx` 复现旧公开入口的旁路。普通角色撤销 `PhysicalQty` 更新后，旧入口仍成功返回实际流水号。
- `direct-apply-http.trx` 先验证当前 `UnitPrice` 写权限拒绝，再实测 PostgreSQL 拒绝普通角色更新库存。HTTP 返回 503，库存数量和流水计数与事务前相等。恢复授权后提交 `0.87654321`，使用实际用户并返回原形态流水号。原操作和 Outbox 的计数与调用前相等，保持直接入口语义。
- 两项定向真实宿主用例通过、零跳过。其中已有普通 V1 调整、原关闭、原键恢复、V1 移库及新直接调整共用一份真实夹具，另一个未启用普通事务的真实宿主验证原公开调整、CSRF 和当前权限。原始 TRX、PostgreSQL 与宿主日志位于 `D:/CP6-local-test/CP6-72H-20261109/W338-01`。实例均已停止。

集中审查覆盖准确 HTTP 选择、内部调用范围、原合同、现行授权、业务回滚和既有 V1 采用。复用已交付安装与数据库迁移。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。

## 工时和接续

起始估算 13–24 分钟。证据齐备时主机单调实测 494 秒。实际采用沿原事务、业务逻辑、权限和夹具。完整窗按正常远端采用及固定席位释放回读计时。

下一步采用原公开直接移库，验证两端权限、原货权、两腿流水号和普通权限失败。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
