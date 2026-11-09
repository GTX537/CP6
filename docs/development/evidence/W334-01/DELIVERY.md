# W334-01 真实库存 V1 两腿移库的普通事务

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave334/README.md)

Main 基线 `f08005d09c70fb4df4e6b6c9099455a79b58e8e5`。COORD 在固定 DEV-1 单人执行 `codex/w334-01`。`StockReadTenancy:EnableV1Move` 与已有普通 V1 调整配置共同启用原 `POST /api/wms/stock/commands/v1/move`。移库使用独立可信目的和当前 `move` 授权。现有源、目的、货权、估价和两腿流水逻辑在同一普通写入事务执行。 [运行说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-v1-adjustment-owner)

## 实际验证

Windows、PostgreSQL 18.6、Npgsql 8.0.8、net8.0。真实 Kestrel、JWT Cookie、CSRF、实际事务和模拟租户、部门、客户货权及库位。

- `red-move.trx` 在普通图锁执行权撤销后观察到旧移库入口仍返回 200 并生成两腿。`move-http.trx` 在同条件返回 503，库存保持。目标库位超出当前区域时返回 `OBJECT_FORBIDDEN`。
- 正常普通移库对两端写入 `-0.12345678` 和 `0.12345678`。源和目的流水分别继承实际子部门和根部门，保持相同的原货权、单位、价格和真实操作者。两腿有原编号关联，原操作读回与回包一致，Outbox 引用为一条。
- 在目标库存键登记现有原生写者绑定后再次移库，实际 PostgreSQL 产生 `STOCK_ROOT_WRITER_REQUIRED`。两端数量、既有流水、下一业务编号和继承审计保留事务前值。失败操作读回 `UNKNOWN`，无 Outbox 引用。
- 真实主用例 1 项，旧移库 HTTP、既有新目标和旧第二腿回滚回归 4 项，零跳过。原始 TRX、PostgreSQL 与宿主记录位于 `D:/CP6-local-test/CP6-72H-20261109/W334-01`，本窗实例均已停止。

集中审查覆盖两端当前授权、库存精度与货权、原操作和通知、第二腿失败原子性及旧入口兼容。复用原安装和数据库迁移。普通 Actions 为手动入口，R2 自动入口匹配版本标签。Actions 触发、取消及生产部署为零。

## 工时和接续

起始估算 18–35 分钟。证据齐备时主机单调实测 535 秒。已有库存原语、业务图锁、测试宿主和目标保护触发器可直接复用。完整窗以正常远端采用及固定席位释放回读计时。

下一步为 V2 明确新库存增加同事务部门归属和审计原语，分别交付原创建调整及移库目标入口。原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。
