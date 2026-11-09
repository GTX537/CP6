# W331-01 原库存 V1 调整的实际普通事务

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本窗开工](../../dispatch/20261109-wave331/README.md)

## 本窗交付

Main 基线 `c4ed0dbbd3aacdd3bb0b56d0ea2f9f0eeccdee1a`。COORD 在固定 DEV-1 单人执行 `codex/w331-01`。`StockReadTenancy:EnableV1Adjust` 在现有隔离库存配置内显式采用原 `POST /api/wms/stock/commands/v1/apply`。

普通登录事务经固定函数取得原认证根和与旧 Owner 相同的业务、主数据表锁。定向 `MAINTAIN` 留在专用 NOLOGIN 函数所有者。主库授权连接只读复核实际 JWT 会话、权限和字段策略。普通事务执行原部门及库位约束、部门版本变化和真实业务写集，并提交原 journal 和 Outbox。旧原命令沿当前查询权限披露，实际业务通知在确认提交后发送。确认失败保留原事务的不确定结果口径。 [安装与运行](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-v1-adjustment-owner) · [PostgreSQL 表锁权限](https://www.postgresql.org/docs/18/sql-lock.html)

## 实际验证

Windows、PostgreSQL 18.6、Npgsql 8.0.8、net8.0。通过本地 Kestrel 与实际 Program、JWT Cookie、CSRF、数据库事务和模拟租户、部门及库存。

- `red-http.trx` 在接入前发现缺普通图锁入口时旧路径仍返回 200。`writer-http.trx` 在同场景返回 503 并保留原库存，安装后实际写入 `1.12345678`，回包流水与数据库一致，操作者来自当前用户，Outbox 引用与原键一致。
- `pg_blocking_pids` 实际观察普通角色等待旧库存事务及权限事务。库存锁释放后完成原调整，撤权提交后新请求被拒。已提交命令依当前查询权限返回相同回包。未绑定部门、缺 CSRF、库存不足及撤销普通库存列权限均返回对应失败，业务只应用一次。
- `owner-compat.trx` 的 4 项相关 PostgreSQL 回归通过。覆盖原库存读面、旧 V2 创建及移动、旧 V1 移库和旧 Owner 的丢失提交回包。新增用例通过 1 项，全部零跳过。普通事务的丢失提交回包另行验证。
- Release 安装命令实测缺少普通库存列权限拒绝，恢复后精确目标安装成功，重复拒绝。读回定向 NOLOGIN 权限和普通执行权限。首次 CLI 命令因传入与原登记不符的端口被所有权预检拒绝，随后在登记端口完成实测。原始 TRX、运行及停止记录位于 `D:/CP6-local-test/CP6-72H-20261109/W331-01`。

集中审查覆盖现有原输入和回执、当前权限及锁序、普通租户身份、故障回滚和提交边界、旧入口兼容。保留既有构建告警。普通 Actions 为手动入口，R2 自动入口匹配版本标签。本窗采用隔离本地环境，Actions 触发、取消及生产部署为零。

## 工时和接续

起始估算 60–105 分钟，证据齐备时实测 896 秒。复用原 Owner、配置、库存写集和真实 HTTP 夹具后，图锁和授权接线首轮通过。下一步把只读原结果和显式关闭拆成各自可验证窗口，按已有代码和故障注入成本分别估算。完整窗实测以正常远端采用及固定席位释放回读为准。

原 375 项为 37 DONE、2 IN_PROGRESS、336 PLANNED，原 59 项为 9 闭环、50 余项，125 完整交付为 0、247 正式运行场景为 NOT_RUN。完整库存角色和版本生命周期、其他写入口与正式运行沿各自出口交付。
