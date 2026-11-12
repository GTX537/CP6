# W508-01 询盘正式 PostgreSQL 盘点与 Main 可信入口

W507 已核销原询盘 60/60。全项目正式 PostgreSQL 矩阵另有 27 项，开工时 16 PASS / 11 NOT_RUN。该矩阵覆盖询盘直接路径和共享平台责任。 [正式矩阵](../../acceptance/postgresql-tenancy.json) · [多租户架构](../../../design-handbook/architecture/02-postgresql-tenancy.md) · [原询盘验收](../W507-01/DELIVERY.md)

| 正式项 | 当前证据与剩余 |
| --- | --- |
| PGT-004 缺失或伪造租户输入 | 本轮独立核对 Main 原生入口和进程存活探针，见下文。继续核对带当前凭据的外部租户/schema 选择输入和实际宿主的地区登记。 |
| PGT-011 后台重试与管理员跨租范围 | 询盘保留与隐私已有当前服务权限证据。仍需同一 worker 的 A/B 暂停顺序、当前执行者与原申请者、平台管理和模拟会话，归 WP02-04。 [保留和恢复](../W491-01/DELIVERY.md) |
| PGT-013 Owner/receipt/Outbox/audit 原子性 | 询盘已有实际共同写入与晚段回滚。仍需按正式用例核对各保存阶段、实际事务和网络时序、真实适配器的异连接拒绝。 [公开提交](../W117-01/DELIVERY.md) · [审核赢家](../W502-01/DELIVERY.md) |
| PGT-014 CRM 前置内容门与共同事务 | W509 独立实测普通角色身份接收、唯一键竞争与重读、前置内容和 Published/DRIFT。CRM #199 已采用，主仓本项随后同步。 [CRM #199](https://github.com/GTX537/CP6.CRM/pull/199) |
| PGT-016 序列化冲突与提交 UNKNOWN | W505 已实测原生 40001/40P01 和有界恢复。W510 独立补已发 PostgreSQL COMMIT 后断连、客户端取消与同键原件。 [锁恢复](../W505-01/DELIVERY.md) |
| PGT-017 暂停/撤权与新业务竞争 | Main W435 已验证 Stock/暂停两种锁序，CRM W246 有已签名当前撤权边界。正式项仍需全部相关写者采用共同 fence 和两种真实时序。 [Main 暂停](../W435-01/DELIVERY.md) · [CRM 当前权限](../W246-01/DELIVERY.md) |
| PGT-021 单租完整恢复和他租保护 | WP02-02 共享恢复。W481 已有真实组合候选、A/B 普通角色及晚段回滚。仍缺完整同源 Stock/ERP 图、他租合法并发差量和实际恢复批准链。 [现行候选](../W481-01/DELIVERY.md) |
| PGT-022 不完整备份与旧消息恢复 | WP02-02 共享恢复。缺件、现行撤权、旧消息和全局键已有部分实测。仍需正式同源完整组合、真实 route epoch 变化和批准后的现行对账。 [现行候选](../W481-01/DELIVERY.md) |
| PGT-024 混合版本与回退 | WP02-02 共享版本。需同库新旧组合、共享最低版本、旧写者覆盖和真实可回退路径。CRM 旧审核守卫拒绝及原结果恢复只覆盖该本地版本。 [CRM 守卫](../W502-01/DELIVERY.md) |
| PGT-025 原生合同与精度 | 原询盘已实测无界版本、Unicode、精确时间与原结果。正式用例要求物理租户 schema、共享 receipt 和无损读回的同一组合。 [完整提交](../W117-01/DELIVERY.md) · [日历和旧原件](../W247-01/DELIVERY.md) |
| PGT-026 认证目录和公共路由 | Main 与 CRM 已有普通角色目录限制、跨租令牌及未知公开路由证据。剩余真实 Main 同名租户/用户、未知 slug 与他租令牌的组合，以及当前 CRM 共享公共路由登记的采用范围。原生 CRM 通过宿主登记和 PG 绑定解析。 [目录最小权限](../W427-01/DELIVERY.md) · [组织切换](../W253-01/DELIVERY.md) |

## Main 实测

真实 Main Kestrel 配置两个普通 PostgreSQL 登录，使用原生 Stock read profile 验证共用请求入口。通过真实登录获取原凭据，再以测试签发键构造缺失、全零、未知 issuer、冒称另一租户的四种令牌。四次 HTTP 均为 401，响应无租户或目录内容，A/B 普通业务连接均为零，A 的库存行保持一致。合法原请求 200，实际用 A 的普通登录打开一次连接。共享 IAM 和恢复标记的读连接与普通业务连接按实际角色区分。 [实际测试](../../../../CP6.Tests/Erp/StockReadTenancyPostgreSqlTests.cs)

原始 `GET /health/live` 实际返回 200，同时开启一次业务数据库连接。全局恢复准入在原端点之前查 PostgreSQL 恢复标记。把进程存活探针放在恢复准入和认证之前，同一授权连接计数下仍返回 200、连接增量为零。数据库就绪及业务请求继续由原路由和恢复准入处理。 [Issue #974](https://github.com/GTX537/CP6/issues/974)

本地 .NET 8、实际 Kestrel、PostgreSQL 18.6，租户和业务输入模拟。`tenant-entry-01.trx` 的初次探针在已经连接的观察会话读取仅对新后端生效的连接日志设置，未进入目标 HTTP。改用新会话后 `tenant-entry-02.trx` 真实发现存活探针打开连接。最终 `tenant-entry-03.trx` 实际 1 通过、0 跳过。日志和 TRX 位于 `D:/CP6-test-artifacts/W508-01`，三个任务数据库均已停止。最终源码 `93b4e5eb6f8056a0c2714167abe3e7167e7c8b1d`，一次集中审查核对精确探针范围和普通角色。正式矩阵 16 PASS / 11 NOT_RUN。Actions 和部署均为 0。
