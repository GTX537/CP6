# W326-01 库存查询采用可信 PostgreSQL 读取

[原任务 WP02-02](../../task-plans/WP02.md#wp02-02) · [关联 WP02-10](../../task-plans/WP02.md#wp02-10) · [本次开工](../../dispatch/20261109-wave326/README.md)

## 交付范围

Main 基线为 `802d1cf229936cc893770b2ddc0b6a7acd1b73ec`。本对话以 COORD 在固定 DEV-1 执行，分支为 `codex/w326-01`。S02-00 和 WP02-01 已按所属主线核实。Main 复用 Platform 0.10.3、可信上下文、租户普通连接池和登录映射。W325 的 Main #781、Handoff #302、分支和释放记录已回读。

| 对象 | 所属与读取边界 |
|---|---|
| `public.T_Stock`、`public.T_StockTransaction` | 库存 Owner。保留共享表及全局 ID 主键，业务库存键 `TenantId + WarehouseCd + LocationCd + ProductCd + LotNo` 和流水键 `TenantId + TxnNo` 保持原域。 |
| `public.T_WmsRoleScope`、`public.T_Location` | WMS 权限范围与库位。按会话登录租户隔离，只授查询所需的列，保留初始化隔离判断。 |
| `public.Sys_Dept`、`cp6_operations_owner_v1.department_mappings` | 组织与业务归属的权威源。部门读权限定健康路径所需列，归属行限定 `stock` 和 `stock-transaction`。 |
| `cp6_tenancy_v1`、`cp6_stock_read_v1.installation` | 控制登录映射和读取布局版本。普通登录只能调用当前租户解析及读取安装版本。 |

配置启用后，库存列表、历史和流水从 `StockOperationsService` 进入现有 Owner 授权事务。原事务继续持有会话和业务范围栅栏。业务查询在同一 PostgreSQL 主库的第二条连接中使用受管只读租户事务，校验双方实际数据库、安装版本和读取角色的行级隔离。EF 使用独立固定 schema 模型，继续按现行部门、仓库、库位及字段权限筛选，保留分页、数值精度、403 和 503 的 HTTP 合同。普通登录的业务查询授权来源为真实 JWT 已校验的有效用户。源请求者引用与有效用户租户分别保留。配置及 CLI 用法见[租户连接组件说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-owner-read-profile)。

## 实际验证

Windows、.NET 8、PostgreSQL 18.6、Npgsql 8.0.8。Core 迁移、Main 实际 Program 和登录均在 `127.0.0.1` 的独立实例执行。使用模拟用户、部门、库存和流水数据。

- 新增 PostgreSQL 用例 2 项通过，0 跳过。覆盖缺安装、实际登录下 A→B→A、相同业务库存键的两租户、部门与库位范围、数量小数、价格遮蔽和历史查询。普通 LOGIN 的直接 SQL 拒绝跨租户数据、库存写入、身份表读取、未授权列和控制目录查询。错绑定、撤销 SELECT、停用行级隔离及越界历史分别得到预期 503 或 403。
- 复用同窗 4 项既有 PostgreSQL 回归。三种部门范围和真实库存盘点、调整、移库回归通过。该次运行后只为新角色补充库位隔离标记列授权并加强新测试数据，原入口验证的代码输入不变。
- 安装 CLI Release 构建成功。在该任务的自有 PostgreSQL 实例新建独立模拟库，以实际 CLI 验证错数据库 OID 拒绝、合法安装返回并读回原安装 ID、重复首装拒绝且保留原回执。空输入负例也通过。
- 本地保留原始失败和成功证据。目录为 `D:/CP6-local-test/W326-01`。其中 `red-results/baseline.trx` 记录基线错误的 HTTP 200，`green-results/green2.trx` 保留补齐库位隔离列权限前的失败和四项有效回归，`green-results/focused.trx` 记录最终两项通过。实际 API、CLI 和自有 PostgreSQL 进程均已停止。
- 集中审查覆盖既有 Owner 路径、EF 映射、登录角色及 RLS 权限、CLI 事务和配置失败分支。保留既有 NuGet 依赖安全告警及源码告警。

## 进度与收尾

本次更新 WP02-02 的读取采用和 WP02-10 的适用边界。原375仍为37 DONE、2 IN_PROGRESS、336 PLANNED。原59仍为9闭环、50余项，其中35项有明确实现余项、15项待核合同或有效证据。125完整目标交付为0，247正式运行场景为 NOT_RUN。

下一步沿库存写入 Owner 接线，核定业务行、原操作结果、必要审计和 Outbox 的同连接事务，以及普通写者与当前权限的最终栅栏。逐租户业务版本初始化和恢复、其他 Main Owner、既有库布局升级、WP02-28 的宿主兼容回退和正式验收继续按原卡登记。

本提交为合并前的验证快照。远端采用和分支删除由正常 PR 和 `manage.ps1 finish` 回读，固定 DEV-1 清理后留作复用。按本窗预检，普通验证工作流均为手动入口，R2 推送触发只匹配候选标签。本窗使用分支和普通 PR。Actions 触发、取消、工作流变更和生产部署均为零。
