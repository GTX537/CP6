# W433-01 · 创建回执接首次 Core RMA 初始化

DEV-2 于 2026-11-09 23:43:29 UTC 从 Main #863 `96bd4da563e7ef3f3aff4100c6f311728e652752` 领取。原 PGT-020 的创建命令生成真实 TenantId，而原 Core 首次准备需在空库预先登记租户。W433 接通这两个权威入口，估计完整窗口 38–65 分钟。Main #864 的 C03 归档改动与本任务编译输入无重叠。[原验收](../../acceptance/postgresql-tenancy.json) · [W265 创建 V1](../W265-01/DELIVERY.md) · [W353 Core 初次准备](../W353-01/DELIVERY.md)

新增显式 V2 控制命令，在独立空库准备物理目标、安装和操作号、创建原键及创建者、普通 LOGIN。原创建接口照常生成真实 GUID。初始化在原认证根及 Core 锁下按原 JSON 字节、散列定位和创建者读取受保护的回执，再确认该租户仍存在。同一事务写出旧形状的已解析绑定、原 DDL、Ready 历史和 commissioning 回执。历史恢复比对同一准备计划与原登录集合。旧 V1 精确请求和原 journal、归档、writer 的绑定格式继续有效。[控制入口](../../../../tools/CP6.TenantStorage/README.md#initial-core-commissioning-from-tenant-creation-receipts) · [实现](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.CreatedTenantCommission.cs)

贯通用例使用真实 Main 登录、正常创建 V1 与原回执查询，模拟输入包括新平台管理员、普通 PostgreSQL 登录、模板、出库与数量。计划使用 Unicode、引号、反斜杠及 NUL 原键。用例包含未有回执、准备变义、`Enable=true` 时就绪前真实 Draft 拒绝、错误 Owner 碰撞与回滚、同键持久回执恢复、普通登录不能读取创建目录、安装原 journal 和 Draft writer 后同一原业务键提交及回放。 [贯通用例](../../../../CP6.Tests/Erp/RmaCreatedTenantCommissionPostgreSqlTests.cs)

## 实际验证和接续

已集中核对两条权威链、锁序、原字节、旧调用者和完整任务差异。Roslyn C# 12 对最终七份 C# 文件的语法检查通过，`git diff --check` 通过。语法证据 `syntax-final.json` SHA-256 `40db87ce36ee2aed00774f7021da68e10afa585fe298db1a0c771dcf707afaa9`。SDK 实际为 8.0.424。`restore-01` 采样 17 次、34.096 秒，物理内存最高 1,164 MiB，低于本窗锁定恢复启动线 1,200 MiB，退出 75。还需恢复依赖、正式编译和 PostgreSQL/HTTP 运行，原 PGT-020 待验收。[语法原件](/D:/CP6-local-test/CP6-72H-20261109/W433-01/syntax-final.json) · [实际资源样本](/D:/CP6-local-test/CP6-72H-20261109/W433-01/restore-01/locked-restore-gate.jsonl)

在同一席位和分支运行 `python -B D:/CP6-local-test/CP6-72H-20261109/W433-01/restore-cli.py --attempt 02`。复用该 SDK 的锁定依赖，定向编译 `CP6.TenantStorage` 与 `CP6.Tests`，使用 `CP6_RMA_CREATED_TENANT_COMMISSION=1` 和新自有 PostgreSQL 根定向运行 `Created_tenant_uses_original_receipt_to_commission_and_only_then_write_RMA`。之后核对正常 CLI 分发、旧 V1 和实际 OriginalJournal/Draft 采用边界。本窗适用空库初次初始化，既有布局追加租户由后续控制任务处理。
