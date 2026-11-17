# W696-01 标准成本单据的 R2 命令绑定与回查

COORD 从远端主线 `900bb4c0584d2be8a6cdf2c50fb038a670eff7c5` 复用固定 DEV-1，任务分支 `codex/w696-01`。接续 W695-01。受测源码及用例为 `68a0a47ee747f6f3a486ffd4b061840524c8946a`。[W695 交付](../W695-01/DELIVERY.md) · [FIN-COST-01](../../../design-handbook/modules/FIN-COST-01.md)

## 业务行为

- 原生 STANDARD 单据的创建、准备和冻结共享同一财务账套命令键。绑定保存精确账套、原操作者、方法、具体路径、首次受理时间及 `COST-CANON-1` 请求和预期摘要。变更主体、路径或正文返回 `COMMAND_IDENTITY_CONFLICT`。同键精确重放使用当前读权返回原回执。
- `GET /api/fin/cost/v1/commands/{commandId}` 回查 `COST-COMMAND-1`。`/result` 读取原单据、计划和版本的不可变结果，两处均核对当前账套、工单、产品和费率来源。已授权的账套内无记录返回 `NOT_OBSERVED`，权限不足返回 404，原件暂不可核实时返回 `UNKNOWN`。
- PostgreSQL 保存原生写入、绑定和回执时使用同一事务。前向迁移将旧记录的命令键及原摘要登记到共同键空间。旧请求和回执仍由原生合同回读，旧记录无 R2 原件时 C28 返回 `UNKNOWN`。既有维护界面沿用原命令 URL 和响应。

## 本地验收

- Windows、.NET 8、PostgreSQL 18.6，隔离端口 57386 和 57387。模拟业务数据经真实签名 HTTP 执行。核对请求及预期摘要、Unicode NFC、显式空版本、原回执、旧版写入的兼容路径、跨账套和撤权、同键冲突、提交阶段故障及并发重放。标准成本单在故障重试场景只冻结一次，回查返回同一结果。[PostgreSQL 用例](../../../../CP6.Tests/Erp/ManufacturingStandardBasisPostgreSqlTests.cs)
- 首轮命中既有账套身份保护，记录在 `final.trx`。命令绑定通过该不可变账套键引用精确范围，移除重复约束后定向复测通过，见 `acceptance.trx`。W690–W692 来源与采用及 W688 重启恢复的八项通过证据复用 `final.trx`。
- 最新构建零错误。四个发布 DLL 与受测 DLL 的 SHA-256 一致。隔离 API 健康、匿名账套选项、匿名命令与结果查询分别返回 200/401/401/401，保存 310 个发布文件的 SHA-256。构建、测试和本地发布证据位于 `D:/CP6-test-artifacts/W696-01`。
- 一次任务级审查覆盖身份绑定、账套隔离、当前权限、旧记录和原子提交。封存目录含 82 个资源、114 个字段和 2,130 个实际 MVC descriptor，新增两项为命令与结果查询。沿用 W695 的浏览器和 Vue 证据。[当前授权入口](../../authorization/current-main/README.md)

WP02-03 继续进行。WP11-39 接续按账套制订、专业批准和发布正式成本政策与费率依据，并验证归集按有效期采用版本。其他 R2 命令及 C65 进程内登记随各自真实调用链采用。WP11-40 接入 MAT、EXEC、OUTPUT 完整来源，WP11-41、WP11-42 推进分配、账套级 GL 结转和 C45 恢复。FM15 通用消费者登记沿 WP11-04 接续。WP02-03 继续核对其他权限用途。GitHub Actions 和生产部署均为 0 次。
