# W433-01 · 创建回执接首次 Core RMA 初始化

DEV-2 于 2026-11-09 23:43:29 UTC 从 Main #863 `96bd4da563e7ef3f3aff4100c6f311728e652752` 领取。原 PGT-020 的创建命令生成真实 TenantId，而原 Core 首次准备需在空库预先登记租户。W433 接通这两个权威入口，估计完整窗口 38–65 分钟。Main #864 的 C03 归档改动与本任务编译输入无重叠。[原验收](../../acceptance/postgresql-tenancy.json) · [W265 创建 V1](../W265-01/DELIVERY.md) · [W353 Core 初次准备](../W353-01/DELIVERY.md)

新增显式 V2 控制命令，在独立空库准备物理目标、安装和操作号、创建原键及创建者、普通 LOGIN。原创建接口照常生成真实 GUID。初始化在原认证根及 Core 锁下按原 JSON 字节、散列定位和创建者读取受保护的回执，再确认该租户仍存在。同一事务写出旧形状的已解析绑定、原 DDL、Ready 历史和 commissioning 回执。历史恢复比对同一准备计划与原登录集合。旧 V1 精确请求和原 journal、归档、writer 的绑定格式继续有效。[控制入口](../../../../tools/CP6.TenantStorage/README.md#initial-core-commissioning-from-tenant-creation-receipts) · [实现](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.CreatedTenantCommission.cs)

贯通用例使用真实 Main 登录、正常创建 V1 与原回执查询，模拟输入包括新平台管理员、普通 PostgreSQL 登录、模板、出库与数量。计划使用 Unicode、引号、反斜杠及 NUL 原键。用例包含未有回执、准备变义、`Enable=true` 时就绪前真实 Draft 拒绝、错误 Owner 碰撞与回滚、同键持久回执恢复、普通登录不能读取创建目录、安装原 journal 和 Draft writer 后同一原业务键提交及回放。 [贯通用例](../../../../CP6.Tests/Erp/RmaCreatedTenantCommissionPostgreSqlTests.cs)

## 实际验证和接续

已集中核对两条权威链、锁序、原字节、旧调用者和完整任务差异。Roslyn C# 12 对最终七份 C# 文件的语法检查通过，`git diff --check` 通过。语法证据 `syntax-final.json` SHA-256 `40db87ce36ee2aed00774f7021da68e10afa585fe298db1a0c771dcf707afaa9`。SDK 实际为 8.0.424。`restore-01` 采样 17 次、34.096 秒，物理内存最高 1,164 MiB，低于本窗锁定恢复启动线 1,200 MiB，退出 75。还需恢复依赖、正式编译和 PostgreSQL/HTTP 运行，原 PGT-020 待验收。[语法原件](/D:/CP6-local-test/CP6-72H-20261109/W433-01/syntax-final.json) · [实际资源样本](/D:/CP6-local-test/CP6-72H-20261109/W433-01/restore-01/locked-restore-gate.jsonl)

### 同一分支的锁定构建准备

2026-11-10 01:48 UTC 在 DEV-2 恢复，纳入 Main #867 的用户复合外键。候选 `bf9a6fb252e50b5e40baf57c082e69ace1cd5df8` 的项目、锁文件和还原配置与原 W375 锁定缓存一致。核对了 10 个项目和 207 个本地包标识。首次预检将签名 ZIP 的摘要与锁文件内容摘要比较，改为对照 `.nupkg.metadata` 的 `contentHash` 后全部匹配。只在私有目录调整还原输出路径，原缓存继续供其已验证消费者复用。 [.NET SDK 资产输入](/D:/CP6-local-test/CP6-72H-20261109/W433-01/build/locked-reuse.json) · [NuGet 内容摘要实现](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageExtractor.cs)

.NET SDK 8.0.424 的实际 MSBuild 求值和 `ResolvePackageAssets` 通过。`CP6.TenantStorage` 与 `CP6.Tests` 分别从独立目录解析了 96 和 169 个包编译引用。首轮正常编译启动检查 17 个样本、34.150 秒，物理空闲为 1,414–1,478 MiB，可用提交为 20,745–20,829 MiB。联合启动线为 1,500 MiB，检查退出 75，编译未启动。PGT-020 待实库。 [实际 SDK 求值](/D:/CP6-local-test/CP6-72H-20261109/W433-01/normal-msbuild-evaluation.json) · [实际包解析](/D:/CP6-local-test/CP6-72H-20261109/W433-01/normal-resolved-packages.json) · [编译启动采样](/D:/CP6-local-test/CP6-72H-20261109/W433-01/build-cli-01/normal-build-gate.jsonl)

在同一席位和分支具备资源时执行 `python -B D:/CP6-local-test/CP6-72H-20261109/W433-01/build-w433.py build --project cli --attempt 02`，再定向构建 `CP6.Tests`。使用 `CP6_RMA_CREATED_TENANT_COMMISSION=1` 和新自有 PostgreSQL 根运行 `Created_tenant_uses_original_receipt_to_commission_and_only_then_write_RMA`，核正常 CLI、旧 V1 和 OriginalJournal/Draft。空库初次初始化为本窗范围，既有布局追加租户沿后续控制任务。 [构建入口](/D:/CP6-local-test/CP6-72H-20261109/W433-01/build-w433.py)

## 2026-11-10 UTC 实库验收

同一 DEV-2 恢复并纳入 Main #868。正常 .NET SDK 8.0.424 Release 编译使用已核实的锁定资产。最初 CLI 编译用时 3 分 11 秒，测试夹具提交触发程序集版本戳变化，全依赖构建用时 4 分 14 秒。最终长键源 `2507513a735b1d0ec63eb24f3f8d9501a6eafad9` 使用正常 SDK 定向构建 CLI 和测试，各耗时 2.60 秒和 45.23 秒，复用源码相同的 Core、Provider 和 WebApi 实际产物。[最终 CLI 编译](/D:/CP6-local-test/CP6-72H-20261109/W433-01/build-cli-03/artifacts.json) · [最终宿主产物](/D:/CP6-local-test/CP6-72H-20261109/W433-01/build-tests-03/artifacts.json)

PostgreSQL 18.6 隔离集群 `7706375172705005940`，目标 OID `39684`，正常 Core 迁移历史 226 条。专属合成夹具中，两个真实 Main Kestrel 进程分别执行平台创建和新租户 RMA。定向 VSTest 1 执行、1 通过、0 跳过。[TRX](/D:/CP6-local-test/CP6-72H-20261109/W433-01/hosted-04/created-rma.trx) · [实际宿主](/D:/CP6-local-test/CP6-72H-20261109/W433-01/hosted-04/actual.json)

| 原场景 | 实际结果 |
|---|---|
| 原创建命令、首个登录和原回执 | 原 Unicode/NUL 键在实际 Main 创建，查询及重放回同一回执且旧密码为空。用首次凭据完成改密。原提交确认丢失复用 W265 的实库用例，原创建入口和该用例源码一致 |
| 原四对象和管理审计 | 独立查询见 1 个租户、1 个首用户、1 个模板角色、原菜单关系和 1 条管理审计。完整四对象写集摘要 `b5cb0bb424a318f987a9bbccbdea1be906aeb961d2c5ab035b5d3f2570380773` |
| 未 Ready 和错误 Owner | `Enable=true` 时实际 Draft 返回 `503 RMA_TENANT_BINDING_UNAVAILABLE`。异 Owner schema 返回 `42P06`，原 Owner 和准备记录保留 |
| provisioning 在 DDL 后中断 | 实际租户 schema DDL 到达 PostgreSQL 后端 `51516`，同一个后端由独立连接终止。租户 schema、目录和准备记录恢复到原状态。随后同一准备计划及原键成功提交 |
| 原回执及业务回放 | 实际 V2 CLI 接收 18,728 字符。prepare 回 284 字节，V2 与既有 V1 commission 各回 950 个相同原字节。普通登录读创建回执表被拒，实际 Draft 与重放产生 1 个 head 和 1 条原 journal。CLI 前后完整写集一致 |

首次宿主监控把 `initdb` 的临时负 PID 当成常驻 postmaster，提前结束，已核原进程停止并修正监控。下一次实际 HTTP 复现 `TENANT_TEMPLATE_NOT_READY`。读取隔离库确认角色、菜单均有效，缺 `permission-catalog` 修订。测试先提交租户，再写菜单以触发真实修订生成。[模板诊断](/D:/CP6-local-test/CP6-72H-20261109/W433-01/template-diagnosis-01/actual.json)

短键通过后，旧 CLI 实际拒绝 18,632 字符的长键请求。V2 的输入读取按需扩容，边界覆盖创建 V1 的 128 KiB 请求、JSON 转义及 128 个原引用。最终实际 Main 创建使用 3,000 个汉字及 NUL 的原键，CLI 同样按原键回放。最终专属 PostgreSQL 实例已停止。[旧 CLI 拒绝](/D:/CP6-local-test/CP6-72H-20261109/W433-01/cli-long-red/actual.json) · [原生中断](/D:/CP6-local-test/CP6-72H-20261109/W433-01/hosted-04/permission-consumption-w433-04/postgres.log) · [CLI 和写集](/D:/CP6-local-test/CP6-72H-20261109/W433-01/cli-replay-02/actual.json) · [停机](/D:/CP6-local-test/CP6-72H-20261109/W433-01/cli-replay-02/stopped.json)

原 PGT-020 由本窗及 W265 实测共同覆盖。集中审查核对准确物理目标、原字节及当前 Owner、共享事务与原历史、测试中断和失败边界。既有布局追加租户、其他业务写者和 WP02-04 整卡仍按其对应范围验收。[W265 原提交恢复](../W265-01/DELIVERY.md) · [PGT-020](../../acceptance/postgresql-tenancy.json)
