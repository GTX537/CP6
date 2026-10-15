# WP02-02 租户连接组件交付

实施基线 `7837009aab983f38e068f0f3dd56745c12450449`，任务分支 `codex/wp02-02-tenant-storage-20261015`。范围按 [PLAN](PLAN.md)：本次交付宿主内部、尚未接入 Program 的租户连接基础组件；不宣称完整 WP02-02、业务采用或 17 PGT 完成。任务级 SPEC/QUALITY 集中审查、共享状态、提交与远端 main 核对由 COORD 在同一交付窗口完成。

## 交付内容

- [TenantStorageBindingV1](../../../../../CP6.Core/Persistence/Tenancy/TenantStorageBindingV1.cs)：不可变 binding 与受控目录，非空 GUID 规范 schema、集合防御性复制、精确宿主/数据库/租户路由、已知模型/layout 与 Ready 门；同一实际登录名不能被目录分配给不同租户。数据库映射拒绝重复配置 endpoint tuple；不同 DNS/IP 指向同一 cluster 的治理仍由配置 Owner 负责。
- [TenantConnectionManager](../../../../../CP6.Core/Persistence/Tenancy/TenantConnectionManager.cs)：入口只读取当前 `ITrustedContextAccessorV1`；每次 await 后重新检查活动对象与绑定；密码仅从宿主私有 resolver 获取。专用 DataSource 按不可变 binding 及目录 generation 分离，存活、初始化与 draining 池的完整 MaxPoolSize 均占预算；池数/总连接容量双界限、确定空闲回收、原子目录替换及旧池排空。
- [TenantConnectionLease](../../../../../CP6.Core/Persistence/Tenancy/TenantConnectionLease.cs)：只有显式本地事务业务回调；读取与写入都进入事务。借出、执行、提交前检查实际 database/session_user/current_user/search_path 和当前 scope/binding。固定 pg_catalog、pool/reset/timeouts，禁用 multiplexing/ambient enlist，阻止 PGOPTIONS 覆盖；最小权限门拒绝超级用户、BYPASSRLS、管理/复制角色、数据库 Owner 与 CREATE/TEMP。错误仅含固定类型及必要 SQLSTATE，无 resolver 异常正文、数据库 DETAIL 或密码。
- [内部使用边界](../../../../../CP6.Core/Persistence/Tenancy/README.md)：提交由调用方显式决定，无自动提交/重试；未提交事务销毁回滚。受信任宿主回调不得保留原始连接/事务、脱离 scope 执行或绕过 wrapper 提交。该组件不代替当前授权及 Owner 同事务最终门，也不构成任意宿主代码沙箱。

未改变现有默认租户/旧连接工厂、宿主认证、EF 模型或迁移；未增加项目、包版本或 SQL Server 验证。

## 实际验证与保留失败

执行器为 `C:/Users/tt/.dotnet/dotnet.exe`，SDK 实测 `8.0.424`。复用本任务前段真实包缓存 `D:/CP6-local-test/WP02-02/adopt-a1/n` 及已恢复锁定依赖（Platform 0.10.3、Npgsql 8.0.8、Npgsql EF 8.0.11、EF 8.0.30）；本轮 `--no-restore`，未发包、改锁或重新声称正式包签名验证。受影响项目及必要项目引用随 `dotnet test` 构建；未运行无关全仓测试。

初始三次实际命令形态相同，结果文件名不同：

```powershell
$env:NUGET_PACKAGES = 'D:/CP6-local-test/WP02-02/adopt-a1/n'
# RED 不设置下面两项；两次完整定向运行分别设置记录的全新自有根目录。
$env:CP6_TENANCY_PG_ROOT = '<本报告下列新建 owner root>'
$env:CP6_TENANCY_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
& 'C:/Users/tt/.dotnet/dotnet.exe' test CP6.Tests/CP6.Tests.csproj --no-restore `
  --filter 'FullyQualifiedName~CP6.Tests.Persistence.Tenancy' `
  --logger 'trx;LogFileName=<本次结果名>.trx' `
  --results-directory 'docs/development/evidence/WP02-02/tenant-connections'
```

| 运行 | 实际结果 | 原因/适用范围 |
|---|---|---|
| [red-boundary.log](red-boundary.log) / [TRX](red-boundary.trx) | exit 1；0 通过、1 失败、0 跳过 | TDD API scaffold 尚未实现，缺可信上下文入口抛 NotImplementedException，未满足 typed TrustedContextRequired 拒绝。先观察此行为失败，再实现组件；scaffold 已被实现替换。 |
| [focused-01.log](focused-01.log) / [TRX](focused-01.trx) | exit 1；18 通过、8 失败、0 跳过；另保留 fixture cleanup failure | 单元测试通过；Windows pg_ctl 已退出但 postgres 继承重定向管道，夹具 ReadToEnd 等待服务器生命周期，尚未执行 PG 业务用例。核对自有目录/PID/端口后手动停止该实例使失败显现。没有把初始化失败算成通过。 |
| [focused-final.log](focused-final.log) / [TRX](focused-final.trx) | exit 0；28 通过、0 失败、0 跳过（18 单元、10 真实 PG）；测试执行约 24 秒 | 修复 pg_ctl 管道等待，仅记录其退出及独立 postgres.log；补全同连接预算/已结束 scope 等风险覆盖。这是集中审查取消异常脱敏修正前的完整运行，后续仅复用其未受影响范围，不改称新代码全套重跑。 |
| [red-cancellation-privacy.log](red-cancellation-privacy.log) / [TRX](red-cancellation-privacy.trx) | exit 1；0 通过、1 失败、0 跳过 | 集中审查发现 OCE 原样透传风险。先在原 resolver 取消用例注入合成敏感消息和 InnerException，实际触发 Assert.Null 失败；保留失败后把 OCE 改为固定消息、保留 CancellationToken、无 InnerException。 |
| [focused-cancellation-privacy.log](focused-cancellation-privacy.log) / [TRX](focused-cancellation-privacy.trx) | exit 0；1 通过、0 失败、0 跳过 | 脱敏修正后的定向回归，验证原 CancellationToken、无 InnerException/消息泄露和预算释放。没有重跑 PG；复用 focused-final 完整运行中的未受影响连接/事务/隔离证据。 |

取消异常修正的 RED 与 GREEN 使用上述同一项目、`--no-restore`、results-directory，精确过滤器为 `FullyQualifiedName~CP6.Tests.Persistence.Tenancy.TenantStorageUnitTests.Resolver_exception_and_cancellation_release_capacity_without_secret_details`，分别输出 `red-cancellation-privacy.trx` 与 `focused-cancellation-privacy.trx`。不设置 PG opt-in；这两次只执行该单元用例及必要受影响构建。最终代码的有效证据组合为原 28 项运行的未受影响范围，加取消脱敏修正后的 1 项定向回归，不是一次新执行的 29 项套件。

当前完整定向覆盖：缺少/legacy/已结束 scope；错 host、未知版本、非 Ready、目录冲突；credential await 期间 scope/目录改变；resolver 失败保密与取消预算释放；A→B→A 专用角色及 A 物理 backend 复用；将已经打开的 A 物理连接送入 B 的实际 guard、错数据库、特权角色和 search_path 篡改均在业务回调前拒绝；普通 A/B 对明确限定的其他 schema 查询均 SQLSTATE 42501；显式提交、默认回滚、取消后回滚及池 session reset；嵌套 B 使用 A lease、已结束 scope 和已结束 transaction 拒绝；真实密码/credentialVersion 轮换时旧 lease 继续占预算；总 MaxPoolSize 与池数界限、两条 A 物理连接及等待取消/结束 scope、空闲回收、并发重复 disposal；写入后 binding 轮换阻止显式提交并回滚；实际 PGOPTIONS 覆盖防护。

首次运行根目录为 `D:/CP6-local-test/WP02-02/tenant-connections-d3bb2762c3c84d80bd9d45b1188b317a`。其 `postgres.log` 记录 PG 已启动；`manual-stop-after-pipe-wait.txt` 记录在完整 data_directory、postmaster.pid 中 PID/端口与实际 postgres 进程命令行核对后执行 `pg_ctl -D <该目录/data> -m fast -w stop`，exit 0。原数据及失败日志保留，没有 DROP/删除/覆盖。

成功运行根目录为 `D:/CP6-local-test/WP02-02/tenant-connections-cf34103d4d644cc7b3e52122d6b1f6ed`。服务器实测 `18.6`（server-version.txt）；夹具通过服务器 data_directory、postmaster.pid 路径/端口及 ownership marker 核对后停止，`stopped.txt` 与 `postgres.log` 记录正常关闭。收尾实际检查两次目录均无活动 postmaster.pid，55436 LISTEN 数量为 0。根目录 ACL 限当前用户；随机凭据仅在私有内存/自有根目录，不进入仓库或命令参数。保留集群、合成表与失败记录用于复现，不连接既有实例。

既有 `System.Security.Cryptography.Xml 8.0.4` 的 NU1903 及其他无关源码告警如实保留；本次新增 CS8602 已通过正常赋值分支消除，最终日志无该新增告警。没有触发或重跑 GitHub Actions，没有取消/修改工作流、生产发布、签名、部署或业务迁库。README/交付报告差异检查无空白错误；README 审查措辞修正不改变代码验证输入。

## 复用清单与剩余范围

同一任务的准备资料 [postgresql-ef-relational-inventory-7837009a.json](postgresql-ef-relational-inventory-7837009a.json) 由 COORD 从基线 PostgreSQL EF 最终关系模型只读提取，涵盖 328 张映射表，其中 317 张含直接 TenantId/OrganizationId；这 317 张中有 307 张至少有一个未含租户列的唯一键。不能据此把表复制进每租户 schema，削弱原全局唯一域。该文件不是现场数据库 catalog，也未完整覆盖 writer/raw SQL、迁移创建对象、触发器、函数、序列、Owner 分类、保留与恢复依赖。

后继仍须完成：完整对象/唯一键及 writer 清单、共享 RLS/受保护目录 helper、EF 模型与缓存采用、真实宿主 source authentication/当前授权、Owner 最终门及本地原子写集、逐租户迁移/恢复、17 PGT 和真实业务验收。此处普通 A/B LOGIN 与合成小表只证明连接组件及权限拒绝，不能代替上述验收。任务总台账与业务状态不因这些组件测试提升为完整完成。

## 集中审查与协调核对

一次任务级 SPEC/QUALITY 审查已通过。审查覆盖全部组件、测试及共享状态差异，未发现剩余阻断项；
取消异常脱敏、端点别名措辞及 328/317/307 数字问题已修正，修复部分定向复核完成。
COORD 直接读取上述日志/TRX 与自有实例停止记录，没有重复跑未受影响测试。
台账结构及生成视图检查 PASS，125 目标和 247 NOT_RUN 保持；未将技术信任配置缺口误记为业务通过。
模型清单保留实际程序集身份（信息 SHA 5816d04）与指定基线 7837009a 的未变模型源码对照，不冒充新源码重新构建的数据库现场清单。
正常 PR 合并及远端包含性核对由 COORD 随本次交付完成；最终提交身份记录在 PR 和 Handoff 消息中。
