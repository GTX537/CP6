# W435-01 · 租户暂停与普通库存提交

COORD 于 2026-11-10 04:20:19.001289 UTC 从 Main #871 `21fca04d52245a4fb3d8962cedb82a524cb8eace` 登记 DEV-2，任务分支为 `codex/w435-01`。复用原普通库存 V1 和版本化租户管理入口。在实际 Kestrel 与私有 PostgreSQL 18.6 使用合成租户、管理员和库存操作者，双方都经正常登录取得会话。 [普通库存 V1](../W331-01/DELIVERY.md) · [回归用例](../../../../CP6.Tests/Erp/StockTenantPausePostgreSqlTests.cs)

## 两种实际提交顺序

- **库存先提交。** 测试连接持有 `T_Stock` 表锁时，普通库存后端 35220 已取得认证根，随后等待库存图。租户暂停后端 51452 等待 35220 的事务。`pg_blocking_pids` 记录 `51452 → 35220 → 52448`，等待期间两侧均只有已提交的初始状态可见。释放测试锁后，库存调整和暂停均返回 HTTP 200。库存保留 `1.12345678` 的增量、操作人、原 `COMMITTED` 流水和审计，以及准确绑定的 Stock Outbox source/message。随后用旧会话新写收到 401。 [成功结果与等待链](/D:/CP6-local-test/CP6-72H-20261109/W435-01/hosted-02/actual.json)
- **暂停先提交。** 通过原入口重新启用并重新登录，再锁住目标租户行。实际暂停后端 10004 取得同一认证根后等待目标行，普通库存后端 35220 随后等在 `lock_graph`。`pg_blocking_pids` 记录 `35220 → 10004 → 52448`。释放测试行锁后暂停返回 200，等待中的库存命令返回 401 `REAUTH_REQUIRED`，同一旧会话再次新写返回 401。库存余额、移动流水、原操作、Stock Outbox source/message 的规范 JSONB 写集与第一次合法提交后逐字节一致，SHA-256 `e2810dd7ece50580b039d12f27f0a84c5a631a70660331d7fc9320c7e410a7e3`。 [定向测试结果](/D:/CP6-local-test/CP6-72H-20261109/W435-01/hosted-02/stock-pause.trx)
- 两次暂停均产生新认证策略代次、目标租户管理版本、同一租户聚合快照及一条优先 Outbox、管理审计和命令回执。管理版本分别为 `1→2`、`3→4`，身份代次及同一租户的快照版本分别为 `0→1`、`2→3`。在暂停与库存事务等待时读到的权威状态均保持当时的已提交值。 [原始结果](/D:/CP6-local-test/CP6-72H-20261109/W435-01/hosted-02/actual.json)

## 本地构建、验证和审查

沿用 W433 `2507513a735b1d0ec63eb24f3f8d9501a6eafad9` 的正常 Release 依赖。比对两次源码的九个项目、构建配置和合同，校验锁定包及现成程序集，再重编 `CP6.Tests`。`build-01` 缺少迁到私有输出目录的 Web API 中间 `apphost.exe`，从已核构建补入同一文件后成功。`hosted-01` 的业务与暂停已提交，测试在读取原流水审计时使用了错误的字段名。按原表 `audit_json` 修正断言并校验审计中的真实操作人，保留首轮 TRX。最终 `build-03` 退出 0，耗时 57.091 秒。 [构建输入](/D:/CP6-local-test/CP6-72H-20261109/W435-01/build/reused.json) · [构建失败](/D:/CP6-local-test/CP6-72H-20261109/W435-01/build-01/msbuild.stdout) · [首轮实测](/D:/CP6-local-test/CP6-72H-20261109/W435-01/hosted-01/stock-pause.trx)

```powershell
$runner = 'D:/CP6-local-test/CP6-72H-20261109/W435-01/run-w435.py'
& 'C:/Program Files/python/python.exe' -X utf8 -B $runner build --attempt 3 --seconds 55
& 'C:/Program Files/python/python.exe' -X utf8 -B $runner hosted --attempt 2 --build-attempt 3 --seconds 60
```

最终 `hosted-02` 执行 1 个测试，通过 1 个，跳过 0 个。完整执行耗时 50.401 秒，04:36:30.363279 UTC 完成，PG 55568 已释放，私有数据与证据保留。实际结果 SHA-256 `4e973ecb5696b2722b9b4456642f56af43e707eb548dd0cae85ec4ddd82ef8e3`。测试源码 SHA-256 `6c7d78e740142ee44809e92959596145b54238578245d3a3ab976ac4b470c3da`。COORD 集中核对普通登录、两种锁顺序、当前会话失效、库存精度、业务及权威写集、测试资源和正式验收范围。 [最终运行](/D:/CP6-local-test/CP6-72H-20261109/W435-01/hosted-02/terminal.json) · [停止回执](/D:/CP6-local-test/CP6-72H-20261109/W435-01/hosted-02/stopped.json)

这份证据覆盖 Main 的普通库存 V1 调整和版本化租户暂停。正式 PGT-017 继续登记为 `NOT_RUN`，其全部相关 writer 清单、真实旁路和同一最终栅栏采用仍待核销。原 WP02-03 的完整前置及验收保留。 [原 PGT-017](../../acceptance/postgresql-tenancy.json#L606) · [原应用接入要求](../../../design-handbook/modules/PLT-CTX-01.md)

## 总控接续

- W423 已随 #871 合入 Main，任务本地及远端分支清理，固定 DEV-2 在原窗收尾时释放。 [合并](https://github.com/GTX537/CP6/pull/871) · [席位回执](/D:/CP6-local-test/CP6-72H-20261109/W423-01/resume-01-finish.json)
- W415 在准确的独立候选验证隔离 503、不同身份的批准、普通 A 原操作、B 的 UNKNOWN、错操作者 403 和同一 Bearer 撤权后 403。原 12 列流水逐字节一致，分支为无，集成席位已释放。 [实际交付](/D:/CP6-local-test/CP6-72H-20261109/W415-VERIFY/DELIVERY.md) · [席位回执](/D:/CP6-local-test/CP6-72H-20261109/W415-VERIFY/resume-01-finish.json)
