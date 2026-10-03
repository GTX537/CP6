# DB-COMPAT-01 WP4 集中代码审查

本次代码审查无实质阻断。结论仅适用于下列源码字节快照及已明确取得的验证记录；尚在执行的门禁、文档和证据归档不能由静态审查代替。

- 工作树：`D:\CP6\tmp\worktrees\db-compat-wp4-20261003`。
- 已确认基线：`origin/main = 974e57c0650279565330a67c844355ba3e1b563d`；任务 HEAD：`539e5a8b5dfd44083c3df8da76b7964fa2ff82bc`，同时包含工作树已跟踪改动及任务 untracked 新代码。
- 审查方式：完整代码、测试、依赖 diff；新增源码读取正文；必要时核对未改调用方及已存在的原生结果。没有执行 .NET、数据库、远端、提交，也没有改仓库文件。
- 同一轮并行分工：ERP 子审查与业务半部复核汇入本记录；采购/OA/WF 的实现作者核对范围由主审独立读取关键断言、原生并发屏障和 finally 生命周期补足。未启动第二轮全仓审查。
- 文档、历史交付归档 JSON 和最终证据完整性由 root 收尾核对；历史归档内三个 PowerShell 脚本已读取，未执行。它们不作为 WP4 当前运行器或当前结果。

## Blocking

无。没有发现需要阻止本任务合并的生产功能、租户作用域、事务所有权、比较语义或失败原子性缺陷。必需门禁仍须真实完成；此结论不是普通 PostgreSQL 全应用启动、WP5 空间业务或 WP6 恢复/运行验收结论。

## Nonblocking 与定向处理

1. **已修复并定向静态复核：WMS suite 误被共享 owner 选择。** 原 `CP6.Tests/Infra/WmsProductionFactAttribute.cs:18` 的选择条件包含 `CP6_TEST_DATABASE_OWNER`。仅选择 Core provider/connection 并设置共享 owner、未选择 WMS 时，宽 filter 会进入 `WmsProductionSqlServerTests.cs:37` 的 required 分支并报缺 WMS provider。root 已只移除 owner 单独激活项；目前只由 WMS provider/connection 选择 suite，选择后 `WmsProductionSqlServerTests.cs:43` 的 owner 校验保留。修复 SHA 在清单中。预期 legacy skip 的实际发现/执行单独待 root 记录，不计入 mandatory 零 skip 通过数；原 WMS8 两库定向成功不受影响。

2. **ERP README 需区分历史 SQL 与新 required lane。** `eng/crm/erp-integration-tests/README.md:10` 仍展示只有 `CP6_C03_TEST_SQL` 的无 filter 项目命令；新增 `ErpHandlerRelationalTests.cs:7` 使用另一 required collection，其 `ErpRelationalFixture.cs:34` 明确要求新 provider env。因此旧 SQL 生命周期与原 95 case 的兼容性不等于旧无 filter 命令还能只用旧 env。最终文档应给原 95 filter 与 WP4 显式 provider lane；无需更改真实 required 失败规则。root 文档收尾范围内处理。

3. **原 ERP95 并发成功不能单独证明两处全事务重试已触发。** `DeliveryReplaySqlTests.cs:225` 使用 `Task.WhenAll` 并核一份 audit，但没有固定串行化冲突或断言实际 40001/40P01、fresh context。Inbox replay 原用例也没有强制冲突。因此不能把 95/95 扩写成 `ErpInboxReplayService.cs:31`、`ErpDeliveryReplayService.cs:71` 两处 catch 均实际命中。实现静态确认仅对明确 PG 事务失败、最多三次尝试，失败拥有的事务和共享子 context 在下一次尝试前释放，无普通订单测试层 retry。root 已安排新增独立 `ErpReplaySerializationRelationalTests.cs` 两项原生 40001 coverage；新增文件不在本轮冻结初始快照，完成后只做定向复查和对应门禁。

## 核对要点

- OIDC 显式 provider 构造和旧 SQL 构造兼容；PG consume/delete RETURNING 原子化，TTL/UTC、redirect 原始 bpchar 字节长度与精确比较保留；family 锁顺序和完整事务重试没有在失败 PG 事务内重试单条语句。
- Refresh 真实服务两上下文竞争和 replacement CHECK 失败回滚使用生产路径；业务断言保留。Identity snapshot、service token、priority enqueue 同连接事务且显式 tenant；bootstrap 在 Serializable 快照前获取 session 锁，释放先于关闭连接。外层 bootstrap worker 的 fresh scope 重试未被测试层伪装成生产内部重试。
- ERP handler、factory、order、replay、bridge 适配保留 SQL 路径，PG 实际 provider/profile/shared context、显式 tenant、native error 分类与拥有事务的完整重试边界合理。没有把普通订单用例包成测试重试；精确 CHECK/唯一约束断言不接受所有 DbUpdateException。
- 采购新 PG FOR UPDATE 保留 tenant 与 bpchar 业务键；原回调事务边界、真实行锁竞争、CHECK 后完整回滚及过期 correlation 拒绝有断言。财务真实 token/decimal/汇总/审批路径、权限真实 DbSet 查询和 role 聚合均未用 Mock 替代数据库结果。
- 共用业务、WMS、OIDC、ERP fixture 实际 provider migrations、history prefix 和 owner marker 检查，显式选择不可用必须失败；owned 模式没有创建/删除数据库。并发用例的 barrier 释放、cancel/join 在 context/service provider dispose 前；临时 constraint 删除限定本 case 的名称并验证。
- identity dispatcher 新增三项使用真实生产 dispatcher/store：发布失败后的持久化 retry、两个实际 native session 的条件 claim 竞争、过期 lease 的旧 owner fencing，以及 10 次失败后的真实 dead-letter 行。可注入 TimeProvider 只推进正式时钟接口，未直接修改队列状态伪造结果。非 Space retry 三项使用真实 worker/dispatcher/Core 行和持久化 backoff/dead-letter 操作日志，外部 transport hook 保留在组件边界。
- 三份 packages.lock.json 补齐同仓 provider 项目及既有 Npgsql/EF provider 依赖解析；未发现超出适配所需的版本或工程配置漂移。

## 实际验证来源与边界

以下是复用 root 已执行原生门禁的事实，不是本审查重新运行。OIDC 两库各 27、identity common 两库各 21（含 setup）、ERP 原主体两库各 95、WMS 两库各 8；SQL Core 初次 22/22，PG Core 原 21/22 的采购锁失败仍保留，修复后采购两库各 5/5，其他财务/权限/WF/OA 17 项成功按原来源复用。HTTP、service revoke 并发与 rollback、原 dispatcher component 均分别有两库报告；含 setup 的 2/2 不表述成两项业务用例。SQL 历史迁移两例仍 SQL 专用。

直接核对的原件包括 `tmp/wp4-oidc-{pg,sql}-provider-first-run.json`、`tmp/wp4-identity-{pg,sql}-common-first-run.json`、`tmp/wp4-erp-{pg,sql}-original-business-matrix.json`、`tmp/wp4-core-business-sql-first-reference.json`、`tmp/wp4-core-business-pg-first-run.json`、`tmp/wp4-purchase-{pg,sql}-provider.json` 及业务复核读取的 WMS 原件。失败记录不因后续通过而删除或改称通过。

本记录生成时，新 dispatcher retry 两库各 2/2、lease 两库各 2/2、dead-letter PG 2/2 原件已读取，均零失败零 skip：`tmp/wp4-dispatch-{retry,lease}-{pg,sql}-first-run.json`、`tmp/wp4-dispatch-deadletter-pg-first-run.json`。dead-letter SQL、nonSpaceRetry 三项两库、WMS env 选择定向验证仍待 root 完成/确认，不能静态记为 pass。追加 ERP 40001 两项也待原生执行及定向复查。

外部 hub/email/ERP transport 使用组件边界 double；未验证真实外部系统投递。独立 owned 数据库中的每例 tenant/业务键隔离及临时 constraint 清理不等同于 fixture 删除全部业务行；数据库生命周期归 root，fixture 不擅自清库。未扩展 WP5/WP6、普通 PG API guard 或历史 BUG 审查。

## 已审查代码、依赖及历史脚本字节快照

路径相对上述任务工作树；SHA-256 为本记录生成时本地实际字节。后续定向修复或新增测试需追加其单独哈希和验证来源，无需重开全轮审查。

| 文件 | SHA-256 |
| --- | --- |
| `CP6.Core/Services/CrmIdentity/CrmServiceTokenRecordStore.cs` | `3C3C0B09A4C3362BEC0A86E6802C3352E92D1496C5247A48AF0B5ED84A45DE70` |
| `CP6.Core/Services/CrmIdentity/IdentityBootstrapService.cs` | `675F9F97016BBB82EFB5A9FE2B2AAC7DC06F8AF2560CEE5DFB4AB66B625AAB4F` |
| `CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs` | `E3C8AA0ECE3CB5EC473C30206DED2249CD71326BB73DAB433764910AFF6CC508` |
| `CP6.Core/Services/Erp/OrderService.cs` | `89F347674C1559FE8F42C729F793BDCFF6FCC6475F7AE64D5C8B410A83F8FF43` |
| `CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs` | `8D3CA799B33DA4E71DDD0562C1F2344FC0F897758EA03BBD9AC4758D11BD41CB` |
| `CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs` | `B1766D37073A4A71972D8AB0E651CFA012FB898A3DA81D721D0EF231290EEE32` |
| `CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs` | `4F676D03F91B422DA9D49D33EEE920D34D8043C664EACE901A657393E49E7E19` |
| `CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs` | `1AD924EEF55CB9FB18FB95F15DE6024D3477FC5386BA2DC4A138E7C4B294C511` |
| `CP6.Core/Services/Pur/PurchaseRequestService.cs` | `F2A4EB622177BA764D44CA60FEED4D5D496438CA9E8583328853D986D3B9016D` |
| `CP6.Core/Services/Sys/RefreshTokenService.cs` | `DA6825167D4F01DDF7F5CE12041B4D08ABA5D08D09E363D12A61531CB017073F` |
| `CP6.Oidc.IntegrationTests/BrowserSessionRelationalTests.cs` | `654F64DE7E4A2A37A6E96AA08D436B963B014CF7F60850A60375B6524C905FB1` |
| `CP6.Oidc.IntegrationTests/GrantStoreRelationalCases.cs` | `368EB4A03ADF156D7AACC5B84D4B0DB9750F5A20F4790E847E2A0DA639F35876` |
| `CP6.Oidc.IntegrationTests/GrantStoreRelationalTests.cs` | `C446CD06B69C2A3835946CAC103633E57A6DA33C05FFFFAC8B734907CD098248` |
| `CP6.Oidc.IntegrationTests/OidcRelationalFixture.cs` | `14B1C858F2042582F784CA4898A24B980C7084A6B1DA15EA5D13A3AAADE7C98A` |
| `CP6.Oidc.IntegrationTests/packages.lock.json` | `1A5005D527F34D445140124FDE99E34F8AC2FD21EC83B32F7D1180344A54942D` |
| `CP6.Tests/DatabaseCompatibility/CoreBusinessRelationalFixture.cs` | `FC71C4E04E939D1A0ACDDD1793F99DBCAD8AA52C10521E4F54C05B5B94E6CB65` |
| `CP6.Tests/DatabaseCompatibility/FinanceRelationalTests.cs` | `77699F2652A814431DD21559CC4B23C0EFF3E6ACB24D77AA368BAE3C394C05E7` |
| `CP6.Tests/DatabaseCompatibility/IntegrationEventRetryRelationalTests.cs` | `AD029B36580356DFC1D10545127031769053658B831286238E144F563E69F01A` |
| `CP6.Tests/DatabaseCompatibility/PermissionsRelationalTests.cs` | `6E2B627EAC1E055DF1C65621B194A4983A96033025D851C277C22EACD878D214` |
| `CP6.Tests/DatabaseCompatibility/PurchaseRequestRelationalTests.cs` | `43C8D67F46F589C0EFEAEBFD2DB37E818D0BA6EDDA58D4AA8A44D211290D5186` |
| `CP6.Tests/DatabaseCompatibility/WorkflowNotificationRelationalTests.cs` | `BA355CC7BA42082262598D57DBEED77B8D50A061D461B6C4D39E50259BF46CE0` |
| `CP6.Tests/DatabaseCompatibility/WorkflowServiceJobRelationalTests.cs` | `3230FDDA2BA997CD70A3DC6409F50A57BB594178CCAA64F20E8AE340E5BEA09F` |
| `CP6.Tests/Infra/WmsProductionFactAttribute.cs` | `65224BDF808E4E073AFB7284278E90FE69C10ED6F4D53957323BF14D4304D4F4` |
| `CP6.Tests/WmsProductionSqlServerTests.cs` | `521F8D61FE562DCA9CEF2225CAC1903492410C779B09D2F7BD88EA5C3F12C2AB` |
| `CP6.WebApi/BackgroundServices/ErpOrderBridgeWorker.cs` | `D1C0B5B3DD9E73208F3AF35565D99326E3108932EFF2B4849B14E596B10DC045` |
| `CP6.WebApi/Program.cs` | `680EBE62AFBE616AE79368137AABAFA4EFEF11FA8673B15CD837A1E9408FBF19` |
| `CP6.WebApi/Services/CrmOidcGrantStore.cs` | `71645DDE1C963E590CEC27A3A6EABF20A506350EEF3D9C0B7BF182E9207D541A` |
| `docs/audits/2026-10-03-bug-147-snapshot-batch/native/post-merge/Complete-Bug147Delivery.ps1` | `199DE6C9C752C1D65EC0C88FDAA36E07F98C30EF8F95CBB68A37A6A664AC1D47` |
| `docs/audits/2026-10-03-bug-147-snapshot-batch/native/post-merge/Invoke-Bug147Tests.ps1` | `974EF4187F5C0C535B512F9256D36625069A40688CC224150DE78883F2E2BA65` |
| `docs/audits/database-compatibility/wp3-native/post-merge/Test-DbCompatWp3PostMerge.ps1` | `A1CD2A4F8C93EB19ADB93A782868BB94A2269602E5D45E0D506246F94E5C19DE` |
| `eng/crm/erp-integration-tests/BusinessPartnerBindingConcurrencySqlTests.cs` | `E7ABED68E5DFC9FC48E817A40555543410DC6FC3C3AE3969DCD6D5A09AC52973` |
| `eng/crm/erp-integration-tests/BusinessPartnerSqlTests.cs` | `21E2823ACBC595FA379D8EFA9EB87F717BF6EDC3966DD4B6391A8033EFE90E14` |
| `eng/crm/erp-integration-tests/DeliveryReplaySqlTests.cs` | `203FD37477801BB54043DE35A67178D15B60BB7BCD1E5C828914DAD1D836F6BB` |
| `eng/crm/erp-integration-tests/ErpHandlerRelationalTests.cs` | `EA44A1D098E39681C91945FEEBD3D37436A3D33DC291D096F0205FDE0969C5E7` |
| `eng/crm/erp-integration-tests/ErpRelationalFixture.cs` | `917D8F367C94AF9DB05728B35E958EFD4E5702F1C012BF378912BAAE18777A89` |
| `eng/crm/erp-integration-tests/ErpScenario.cs` | `FBD8F201A908D9D141DC6F07542BB6E6E0906052033E7A89C2328314E9D2CFDC` |
| `eng/crm/erp-integration-tests/OrderAmountBoundarySqlTests.cs` | `A521775016A93A5EDA2783592DFF5414B2AFEEA85260D58B16197D3D8EBEFF76` |
| `eng/crm/erp-integration-tests/OrderSqlTests.cs` | `7A28266A92CD9F0DB86D86BB4FFAB4E96509EEC97F2D561B5E6847CC1B78886B` |
| `eng/crm/erp-integration-tests/packages.lock.json` | `D707DD7A116454CD85EA144479335CED0A2B0C239F607A2D53D636BE345C34C8` |
| `eng/crm/erp-integration-tests/ProductionRegistrationSqlTests.cs` | `E295A5E8AF30C769CE0CA7BAB3806E8C9846BDAF87186CC49F14C9291C492D97` |
| `eng/crm/erp-integration-tests/SqlDatabaseFixture.cs` | `6E156C39E6C96D395F9A95BF7ABEA16516BE0473712A825F7FC94DD7453070EC` |
| `eng/crm/erp-integration-tests/SqlFailureProbe.cs` | `32C8E49D6F0FBD8CEDA8E6539F715AB9F94851517D90EFB3097D12F4DC304627` |
| `eng/crm/identity-events-fixture/HttpCases.cs` | `37B0D60CFFBB27DF8C0AFEBA99629E0EAEBBA73081E5719ADFCA54871E2CECEE` |
| `eng/crm/identity-events-fixture/IdentityLiveFixture.cs` | `8218A4AC4AC5BF4110A377ACE1DE1A1CA9F05690407BF43D708B0412B4E1A2AC` |
| `eng/crm/identity-events-fixture/packages.lock.json` | `187ACCEBFF2B3E37FB2F9CB4C15DDEA296BC89F73838C2134F7B72AA9EFB467B` |
| `eng/crm/identity-events-fixture/Program.cs` | `2A9975D07D3CA9586616637B06F277AD5267E7550D422332365489940D7CBA46` |
| `eng/crm/identity-events-fixture/ProviderCases.cs` | `3E01E0C46C4120DFE1C6870B10E8247C0531AA6DCA7A6BB248B8637F148D7930` |
| `eng/crm/identity-events-fixture/ProviderDispatcherCases.cs` | `BB285CB977A98CC70207651D94C2DD4B2BDA323778930335A93E21036D9D1B70` |
| `eng/crm/identity-events-fixture/ProviderDispatcherFailureCases.cs` | `D29736A2EF28180981D4478214C0AC82315A56D67EB9887BE71306BFE5EAE925` |
| `eng/crm/identity-events-fixture/ProviderFixtureSupport.cs` | `5C289F42CA894CB5F17CB654AEDB4ACD9B7D595E739FD9CF92526AD72F96909B` |
| `eng/crm/identity-events-fixture/ProviderHttpCases.cs` | `1108827BE2A8D5228A2AAAD9BF769A5F01578D21DB1D81F1C39B4FC096857B3D` |
| `eng/crm/identity-events-fixture/ProviderServiceRevocationCases.cs` | `13BA29DAF5091C952CBE8EC1568DDDF53F897290B5903FE8B83C13435B748ECA` |

## 定向追加：ERP 原生 40001 试验与新增原生结果（2026-10-03）

本节追加后续事实，不覆盖前文生成时的 pending 状态；没有重审原 52 个文件或运行任何构建/数据库。

### 新增文件定向复核

`eng/crm/erp-integration-tests/ErpReplaySerializationRelationalTests.cs`，SHA-256 `9CC92AA32F479C793809AFF49F5F77C9744BE5E40119D65B0557F75A81F3B431`：两个 required PostgreSQL Facts，定向静态审查无阻断。

- Inbox case（24 行）通过真实 handler 在实际订单保存后的边界抛依赖超时，取得真实 dead-letter 和事务回滚，再使用原始 receipt token 调 replay；Delivery case（96 行）先经真实 handler 形成订单及 bridge，只设置耗尽元数据，再调用真实 bridge replay。没有在测试中构造 PostgresException、改服务结果或包裹自动 retry。
- `RaceAsync`（165 行）先以独立 owner 的真实事务占有生产相同 resource key。观察代码（203–255 行）要求两个不同原始 PostgreSQL backend 已启动 Serializable 事务、保留 `backend_xmin`，并已到达生产 `pg_try_advisory_xact_lock` 轮询，且 owner 对应 advisory lock 真正 granted、两个 contender 都尚未持有该锁。取得上述状态才释放 owner。试验没有以固定 sleep 代替并发证据。
- EF 真实事件 observer（269–318 行）仅观察，不替换命令/结果/错误。`AssertOneNativeRetry`（324 行）要求原生 `PostgresException.SqlState=40001`，错误来自唯一失败 queue context；恰好三个独立工厂 queue context、三个独立 Serializable transaction、两次成功 commit；失败事务和失败 context 的 dispose 都必须先于第三次工厂创建。这里证明的是三个 queue context，不声称同时记录所有共享 business context。
- 同响应、单 audit、原 payload/hash 和原 token 幂等（58–92、121–156 行）以及 fresh context 中的 persisted RowVersion/次数/outbox/order 断言检查了最终只发生一次业务变更。Delivery 强制冲突入口是 bridge replay；不将它扩写成 result replay 另有强制冲突用例，也未强制 40P01。
- `finally`（186–199 行）先取消 deadline、回滚尚未释放的 owner transaction，再 await 收束两个 caller，之后才退出 owner context/transaction 的 await-using 作用域。observer 连接单独 await-using；没有新增临时 DDL。45 秒总体取消和有界 native command timeout 保留。fixture 业务行仍限定本 case tenant，根 owned DB 生命周期由 root 处理。

以上为静态定向结论。此节写入时 root 正准备/执行新增 PG2 原生门禁，未读取到结果，仍不得称这两个 40001 用例实际通过。

### 已读取新增结果

| 原始报告 | 实际结果 | 输入绑定 |
| --- | --- | --- |
| `tmp/wp4-dispatch-deadletter-sql-first-run.json` | SqlServer，exit 0，2/2，0 failed、0 skipped（包含 setup） | ProviderDispatcherFailureCases SHA 与原审查清单相同 |
| `tmp/wp4-integration-retry-pg-first-run.json` | PostgreSql，exit 0，3/3 executed/passed，0 failed、0 notExecuted | IntegrationEventRetryRelationalTests SHA `AD029B36580356DFC1D10545127031769053658B831286238E144F563E69F01A` |
| `tmp/wp4-integration-retry-sql-first-run.json` | SqlServer，exit 0，3/3 executed/passed，0 failed、0 notExecuted | 同上 |

因此前文新 dispatcher 三项和 non-Space retry 三项的两库 pending 已有真实原件补足；原状态作为审查时间线保留。WMS env 选择修复的实际 skip 专项、ERP PG 40001 两项和最终 README/归档仍由 root 按各自范围收尾。

## 最终门禁状态追加（2026-10-03）

仅读取新增原件和 TRX 后补充事实，未重开源码审查、未执行 .NET/数据库。此前 pending/失败记录保持原样。

- `tmp/wp4-erp-serialization-pg-first-run.json`：PostgreSql exit 0，2 executed / 2 passed / 0 failed / 0 notExecuted，选中精确 `ErpReplaySerializationRelationalTests`；运行输入的新文件 SHA 与定向审查 `9CC92AA32F479C793809AFF49F5F77C9744BE5E40119D65B0557F75A81F3B431` 相同。另读取 `tmp/db-compat-wp4-tests/wp4-erp-serialization-pg-first-run/results.trx`：Inbox 和 Delivery 两项均为 Passed，各自 stdout 实际记录两个原始 snapshot backend、原生 `40001`、三个独立 context / 三个 Serializable transaction、两次 commit。Inbox 失败事务/context dispose 为 seq 10/11，retry context created 为 seq 12；Delivery 相应为 seq 9/10/11。此前 ERP 原 95 项未强制触发重试的覆盖缺口，现由这两个新增真实试验补足到 **Inbox + Delivery bridge 的 40001**；不扩写为 40P01 或 Delivery result 分支也有强制试验。
- `tmp/wp4-wms-unselected-core-owner-discovery-v2.json`：Passed=true、ProcessExitCode=0，Core inputs/shared owner 存在，WMS inputs/legacy SQL 缺省，实际逐项 NotExecuted 为 8。读取其 TRX 确认 8 项均 NotExecuted；这是未选择 suite 的预期 skip 验证，**不是 8 项 native 通过**，不进入 mandatory 零 skip 计数。原 `wp4-wms-unselected-core-owner-discovery.json` 保留 Passed=false、ProcessExitCode=0；其原 TRX 同样为逐项 8 NotExecuted，首次脚本误以 summary.notExecuted=0 判断失败。v2 修正 parser 后单独重跑，WMS production/source 语义没有另改；运行输入的 attribute SHA 仍为 `65224BDF808E4E073AFB7284278E90FE69C10ED6F4D53957323BF14D4304D4F4`。因此前述 WMS 选择隔离项已得到实际验证关闭。
- `tmp/wp4-related-unit-regression.json`：Passed=true，198 executed / 198 passed / 0 failed / 0 notExecuted。范围为相关 identity、refresh/OIDC、采购测试 filter；属于 unit/SQLite 回归，不并入双 provider native 计数。

本审查内已发现问题和新增测试的必要定向状态已补齐，代码审查仍无阻断。ERP/identity README 命令分范围说明、最终文档、证据归档、owned DB cleanup 和交付核对继续由 root 执行；本记录不预先声明这些后续动作完成。
