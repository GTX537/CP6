# W339-01 直接库存移库入口的普通事务

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave339/README.md)

Main 基线 `6ff8f62b19c08415a11cea4d38b098e147b09e2c`。COORD 在固定 DEV-1 单人执行 `codex/w339-01`。已启用 `StockReadTenancy:EnableV1Move` 的宿主为原公开 `POST /api/wms/stock/move` 使用自己的可信用途及既有普通库存写者。原 `WM-MSG-071`、出入库流水号、两端授权和既有内部调用协议沿用原合同。 [运行说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-v1-adjustment-owner)

## 实际验证

Windows、PostgreSQL 18.6、Npgsql 8.0.8、net8.0。真实 Kestrel、JWT Cookie 和 CSRF。使用隔离普通 LOGIN、模拟库存、库位、部门和客户货权。

- `red-direct-move.trx` 在撤销普通图锁执行权后复现旧公开入口返回实际出入库流水号。候选在同条件返回 503，源、目标数量与流水保持事务前值。真实目标库位区域变化返回 403。
- 授权恢复后，真实两腿为 `-0.12345678` 和 `0.12345678`，按原流水号关联。原货权、单位、价格和实际操作者一致，源与目标各继承其当前部门。回包的两个流水号与数据库一致，原操作及 Outbox 计数与调用前相等。
- `direct-move-http.trx` 通过 3 项、零跳过。实际宿主用例包括新直接移库及现有普通库存入口，另外执行两种既有货权回归。原始 TRX、PostgreSQL 和宿主日志位于 `D:/CP6-local-test/CP6-72H-20261109/W339-01`，实例均已停止。

集中审查覆盖准确 HTTP 选择、权限、两腿写集、货权和旧回包。复用既有存储安装与数据库迁移。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。

## 工时和接续

起始估算 11–20 分钟。最终 TRX 于 05:03:14 UTC 结束。集中观察本地证据时主机单调实测 552 秒，中间完成首次 4 小时校准。完整窗按正常远端采用与固定席位释放计时。 [首次 4 小时校准](../../planning/20261109-codex-72h-execution.md#首次-4-小时校准)

接续核库存实际写者、对象、事务和版本边界，再验证同业务键的两租户。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
