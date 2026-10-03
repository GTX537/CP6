# DB-COMPAT-01 WP4：身份与核心业务双库适配

日期：2026-10-03。父任务[Issue #134](https://github.com/GTX537/CP6/issues/134)继续Open。工作包依据[已接受计划](../../superpowers/plans/2026-10-02-database-compatibility.md)，从已确认远端main `0b0ab74a04c4d840a2c0bb05e36395aff8b89d32` 建立分支 `codex/db-compat-wp4-20261003`，工作树 `D:\CP6\tmp\worktrees\db-compat-wp4-20261003`。后续保留草稿提交 `b10719b4`，以 `539e5a8b` 合入 BUG147 已验证远端 main `974e57c0650279565330a67c844355ba3e1b563d`。当前WP4本地专项、一次集中代码审查及原件归档已通过；远端交付尚待完成，WP5/6未验收。

WP3已由[PR #146](https://github.com/GTX537/CP6/pull/146)正常交付，候选`4ddc6c9d8d75f47711fce1c5e56fbb0ac8733e21`为远端main的ancestor，完整树`3755dbec61fd5f4579327302009a35dce65ff351`相同。合并后同一已验证API再次拒绝普通PG运行、非零退出、未观察到HTTP；[交付/冒烟原件](wp3-native/post-merge/manifest.json)保留七份原始记录，原WP3 manifest不改写。WP3真实门禁按原来源复用，不因SHA变化重跑。

根工作区main仍为`605246ca`：Git因原有未提交的`CP6.Space.IntegrationTests/packages.lock.json`拒绝fast-forward。其SHA256 `71D6B6B9EC4446E92D25842310633AA9E49E994EC6211A353B5140251B1870A5`保持，未stash、暂存或覆盖它及其他未跟踪内容。WP4直接使用最新远端main基线，不在根main开发。

## 执行顺序与当前切口

| 顺序 | 范围与实际入口 | 当前状态 |
| --- | --- | --- |
| 1 | OIDC grant/logout一次消费、真实RefreshTokenService轮换/family登出互斥；同时接通IdentitySnapshotWriter撤销与priority同事务链 | OIDC共同27项SQL/PG各27通过、零skip，SQL历史2项另通过；identity启用后的撤销原子性见下一行共同producer结果 |
| 2 | 身份bootstrap/变更捕获、service-token撤销、priority/普通Outbox投递；保持SaveChanges/savepoint失败回滚 | 共同producer SQL/PG各21通过；另一次实际生产factory/signed dispatcher组件各2通过（含setup）。普通阻塞时priority前进、原字节/partition与lease状态已验；HTTP/TLS另两库各2通过（含setup）；三个dispatcher恢复/领取/死信专项各两库2通过；外部传输未计入 |
| 3 | ERP request/replay/报价转订单/bridge及收据、审计、结果Outbox原子性 | 客户命令、真实订单及四项重放/bridge保留PG实际失败，适配后原95业务用例SQL/PG各95通过、零skip；涵盖金额/单位/FX、重复/乱序/原子回滚、授权重放、并发与bridge协调 |
| 4 | WMS库存/台账、采购callback、OA/WF后台领取、财务等相关EF业务回归 | WMS原8两库各8通过；新共同22为SQL22通过、PG21通过/采购锁1失败，补PG行锁后采购5两库各5通过。其余17涵盖财务5、权限2、WF job5、通知5；non-Space事件后台retry另两库各3通过 |

首个OIDC切口复用24个独立连接兑换同code、仅一个成功的原断言；新公共fixture选择显式Provider，对本任务已创建的独占库实际迁移，SQL历史专项另保留。首个身份切口复用`business-save-produces-valid-versioned-snapshots`的实际SaveChanges/snapshot/Outbox主体，PG应先真实暴露旧SQL-only writer。先保留原始失败，再改生产，不以编译失败代替行为RED。

生产OIDC接口保留`ICrmOidcGrantStore`及旧`SqlCrmOidcGrantStore(string, identity?)`构造，新增显式`(DatabaseOptions, string, identity?)`入口；旧入口缺省SQL。数据库选择不能猜连接字符串。刷新与登出保持同一family锁顺序，PG Serializable的旧snapshot冲突必须由事务所有者完整回滚/新事务处理，不能在失败事务内重试语句。SQL原业务日期和UTC字段、绑定比较及错误语义按原行为保留。

## 本地环境与验收边界

已新建两库独占`CP6Compat_WP4_20261003_032b2139`，有本任务owner标记；连接/凭据只在根`tmp/db-compat.wp4-owned.json`，不提交。测试通过显式Provider、测试连接与owner环境变量使用这些库，配置缺失/不可达/迁移失败直接失败，不Skip、不接入现有业务库。root统一安排串行.NET构建；同一数据库上的不同suite不并发执行。

普通PG API/worker运行guard仍保留，WP4/5/6和父任务均未完成。未运行或取消Actions、修改工作流/保护、替换现有环境或部署生产。

## 实际失败与首段通过记录

原PG OIDC一次兑换在实际迁移后因旧SqlConnection不识别Host失败，SQL原同断言1/1通过。原PG身份case为迁移setup1P/真实业务1F，命中 `C02_REQUIRES_CALLER_SQL_TRANSACTION`，不是编译失败。原SQL身份同case因排序规则冲突468失败，单独[BUG147](../2026-10-03-bug-147-snapshot-batch/README.md)已PR148交付并关闭；所有原日志保持。锁文件更新前NU1004及旧LiveFixture构造参数编译失败属于准备问题，不计业务RED。

生产OIDC/Refresh/identity草稿与BUG147合并后，实际OIDC项目完整构建为0warning/error。以下结果分别使用各报告记录的实际程序集；每次保存执行前源码/程序集hash与独占库receipt hash。后续ERP构建和新增fixture不改写较早验证的来源，适用生产输入未变的结果按原范围复用；这些专项尚不代表整个WP4验收。

| 原件（根工作区tmp） | 范围 | 实际结果 |
| --- | --- | --- |
| `wp4-oidc-provider-draft-build.log` | OIDC项目及必要依赖完整构建 | 0 warning / 0 error |
| `wp4-oidc-pg-provider-first-run.json` | PostgreSQL共同27项，含真实Refresh并发及replacement约束失败回滚 | 27 passed / 0 failed / 0 skipped |
| `wp4-oidc-sql-provider-first-run.json` | SQL Server同组共同断言 | 27 passed / 0 failed / 0 skipped |
| `wp4-oidc-sql-historical-migrations.json` | 原SQL-only两项前向历史断言，独立fixture自建库 | 2 passed / 0 failed / 0 skipped |
| `wp4-identity-pg-common-first-run.json` / `wp4-identity-sql-common-first-run.json` | 真实producer20业务+setup，含12并发版本、优先队列故障原子回滚及SQL547/PG23514后caller保存点恢复并提交其他工作 | 各21 passed / 0 failed / 0 skipped |
| `wp4-identity-dispatch-pg-component.json` / `wp4-identity-dispatch-sql-component.json` | 新增dispatcher专项+setup；实际生产factory/Options与签名Platform组件，recording publisher | 各2 passed / 0 failed / 0 skipped |
| `wp4-wms-pg-first-run.json` / `wp4-wms-sql-first-run.json` | 原八个WMS领域业务断言，实际Core迁移、owned SQL MARS=false | 各8 passed / 0 failed / 0 skipped |
| `wp4-erp-sql-first-reference.json` / `wp4-erp-pg-first-red.json` | 客户命令最小两case：真实预注册或missing后调用原handler | SQL2通过，PG0通过/2失败，均零skip |
| `wp4-erp-pg-handler-provider.json` / `wp4-erp-sql-handler-provider.json` | provider guard与客户FOR UPDATE适配后同组 | 各2 passed / 0 failed / 0 skipped |
| `wp4-erp-order-pg-first-red.json` / `wp4-erp-order-sql-reference.json` | 原真实已接受报价转订单，报价前置完成后Consume | PG0通过/1失败，SQL1通过，均零skip |
| `wp4-erp-order-pg-provider.json` | PostgreSQL订单provider分支适配后同一原业务断言 | 1 passed / 0 failed / 0 skipped |
| `wp4-erp-replay-bridge-pg-first-red.json` | 实际收件重放、结果重放、bridge重放与后台bridge四入口 | 0 passed / 4 failed / 0 skipped；三个SQL-only guard及实际42601 |
| `wp4-erp-pg-original-business-matrix.json` / `wp4-erp-sql-original-business-matrix.json` | 原有95项ERP业务测试，专用两库与相同生产程序集 | 各95 passed / 0 failed / 0 skipped |
| `wp4-core-business-sql-first-reference.json` / `wp4-core-business-pg-first-run.json` | 财务5、权限2、采购5、WF job5、通知5；共同迁移fixture | SQL22通过；PG21通过/1采购锁失败，均零skip |
| `wp4-purchase-pg-provider.json` / `wp4-purchase-sql-provider.json` | 采购真实callback锁差异修复后的完整采购5项 | 各5 passed / 0 failed / 0 skipped |
| `wp4-identity-http-pg-first-run.json` / `wp4-identity-http-sql-first-run.json` | 实际Kestrel/TLS签发、索引/JWT一致、internal reader、跨tenant/cursor、撤销；真实索引CHECK失败503且无token | 各2 passed / 0 failed / 0 skipped（含setup） |
| `wp4-service-revoke-pg-concurrent.json` / `wp4-service-revoke-sql-concurrent.json` | 服务token同row双真实会话，原生等待关系及最终单次snapshot/priority消息 | 各2 passed / 0 failed / 0 skipped（含setup） |
| `wp4-service-revoke-pg-rollback.json` / `wp4-service-revoke-sql-rollback.json` | priority CHECK失败时撤销记录/snapshot/队列整体回滚，恢复后正常撤销和重复幂等 | 各2 passed / 0 failed / 0 skipped（含setup） |

共同fixture使用实际Core/priority迁移并核对owner，测试间使用唯一tenant/user/hash隔离，竞争worker释放屏障后全部等待结束。故障CHECK在finally移除；旧SQL历史fixture保留各自建删库生命周期，不使用WP4既有库写入。后续阶段收尾集中归档原日志/TRX及来源哈希；适用输入未变的成功结果复用，不因后续文档/提交变化重跑。

身份共同21的生产源码未因新增dispatcher用例改变；dispatcher采用fixture-only构建，复用该21门禁的实际Core/WebApi程序集。两个provider均观察eligible ordinary149/priority13，生产32/16预算下ordinary五批、priority一批，普通真实Publish屏障未释放时priority目标已Published；全部tasks在finally收束。当前all-provider入口包含新增项为22，但没有把原21报告改写成一次22/22；独立两次结果按各自scope保留。该组件正常处理任务fixture backlog，不清空、修改AvailableAt或扩大batch，不宣称实际Dapr/Kafka/CRM投递通过。

为ERP原95共同场景另建owner-marked两库 `CP6Compat_WP4_20261003_c4e6beaf`，private receipt为根 `tmp/db-compat.wp4-erp-owned.json`；SQL Core140以及PG Core/priority/ERP各1已实际安装。旧主WP4两库仍供身份/WMS后续验证，另为采购/财务/权限/OA/WF建立owner-marked两库 `CP6Compat_WP4_20261003_07fbd783`，private receipt为根 `tmp/db-compat.wp4-business-owned.json`，共同fixture已实际完成SQL Core140/PG Core1，pending=0。六库均由root在阶段结束按receipt清理。ERP适配构建曾有四处FormattableString准备错误，已定向修复并保留原log；原BridgeWorkerSqlTests66的xUnit2029及新Permissions测试Assert写法的xUnit2017警告均保留原构建记录，不计业务失败。

ERP PostgreSQL重放命令只对分类为40001/40P01的事务级故障最多尝试三次：由命令所有者完整释放失败transaction/Context后重建，不在失败事务内重试语句；SQL原路径及业务/约束失败语义保持。订单与bridge继续服从既有调用者事务和durable retry边界。原95矩阵的外部bridge hook使用记录组件，只证明调用协调、租户与持久化状态，不代称外部系统或WMS/MES完整验收。

采购锁实际RED通过原生产FlowEngine→dispatcher→callback进入：第一会话已读并改内存但暂停最终Save，第二会话读同目标并完成；PG原分支原生blocking=false，SQL对照true。PG补充按tenant/PrNo的FOR UPDATE，保持bpchar比较及调用者事务/保存边界；修复后的两库均观察第二会话确实等待第一事务，审批提交后竞争回调被现有状态闸拒绝。没有把原PG 21/22改写为一次22/22：采购5另一次通过，其他17的生产输入未变，按原报告复用。

共同财务测试保留预算跨行陈旧version token、fresh重试与陈旧删除；实际存储1000.01及末期83.38余数、预算超支/以内过账和maker=checker原子回滚。权限在生产IQueryable上真正执行DataScopeFilter与PermissionAggregator，覆盖另一tenant。WF job实际token抢占、推进/退避/回收/撤回；通知使用真实outbox及worker，覆盖重复、事务回滚、recipient、双会话claim和实际10秒退避，外部Hub/email为记录边界，未称传输验收。所有tasks先释放/取消并join后才释放上下文；只移除本case约束、binding或自己的通知行。

HTTP专项实际组件使用动态本地TLS端口与原认证/授权handler，验证真实数据库索引拒绝INSERT后503且无access_token、移除精确CHECK后恢复；不等同WP6完整应用进程启动/恢复验收。三个新增身份专项各独立setup+业务2项，按各自来源保留；此前共同21及dispatcher2不因case注册数增长而改写。


## 收尾门禁与审查

| 原件（根tmp报告；关联原始日志/TRX按同label保存） | 实际范围与结果 |
| --- | --- |
| `wp4-dispatch-retry-{pg,sql}-first-run.json` | 各2/2、零skip（含setup）；两张真实队列失败退避后由实际signed dispatcher恢复原bytes/metadata |
| `wp4-dispatch-lease-{pg,sql}-first-run.json` | 各2/2、零skip（含setup）；两真实owner抢同row、5分钟lease过期接管，旧owner完成/失败均拒绝 |
| `wp4-dispatch-deadletter-{pg,sql}-first-run.json` | 各2/2、零skip（含setup）；普通/priority两表各真实10次publisher失败后持久deadletter |
| `wp4-integration-retry-{pg,sql}-first-run.json` | 各3/3、零skip；non-Space实际worker→tenant scope→dispatcher→ERP/MES边界，成功、实际2秒退避恢复及2/3秒后耗尽/真实告警 |
| `wp4-erp-serialization-pg-first-run.json` | PG专有2/2、零skip；Inbox及Delivery bridge实际40001，3个Context/3个Serializable事务、失败释放先于重建、2次commit及单audit |
| `wp4-related-unit-regression.json` | 原身份/OIDC/Refresh/采购unit与SQLite 198/198、零skip；与原生双库结果分列 |
| `wp4-wms-unselected-core-owner-discovery-v2.json` | 配置隔离检查通过：只有Core输入和共享owner时，未选择的WMS保持原8项skip；不算WMS原生门禁 |

新增dispatcher三个case使用签名包公开TimeProvider注入推进退避/租约，保持32/16及max10，不声称操作系统实际等待五分钟；外部publisher是记录/故障边界。non-Space retry使用真实UTC与实际2/3秒等待，不修改NextRetryAt；实际TenantEnumerator先确认自建tenant处于启用状态后只扫描本case，未验收全局枚举调度。该原non-Space路径没有独占claim API，本阶段证明其双库处理与失败状态契约，不能据此声称非Space外部副作用具有单owner保障；WF/OA和Platform的独立并发门禁按各自范围成立。

ERP原95的绿色不代表两个新增retry catch已被强制触发。新增PG专项通过实际resource owner、两个有backend_xmin的独立后端及pg_try_advisory_xact_lock轮询确认旧Serializable snapshot，再放行；原始TRX记录native40001及失败transaction/context释放后才创建第三个Context。具体范围为Inbox和Delivery bridge、40001，不冒称强制触发result分支或40P01；生产分类器和其它场景按已有来源保留。

一次集中审查覆盖完整生产/测试/依赖差异与新增文件，记录于根`tmp/wp4-task-code-review.md`，没有实质阻断。随后只定向核对WMS选择条件一行及新增ERP两项，没有逐步骤重开全仓审查。WMS选择只由自己的provider/connection触发，选中后owner仍必填；第一版验证脚本误把TRX汇总notExecuted=0当作未skip，实际进程成功且八条结果均NotExecuted，原误判报告保留；v2按逐条outcome核对后通过。该配置检查不改变或替代WMS此前两库各8/8原生结果。

当前registry的identity all-provider期望28项、Core业务共同25项及ERP新分组是可执行入口，不是把各次报告改称一次全组执行。原成功结果仅按未变的生产输入和明确范围复用；所有首次失败、准备错误、哈希和执行时身份保留。普通PG应用运行限制仍由WP6解除，本阶段没有部署、现有业务库搬迁、远程Actions或完整外部CRM传输验收。

## 原件归档

[WP4 manifest](wp4-native/manifest.json)保存211份公共原件、1,430,940 bytes，SHA256为 `7F383AED2CA9162FD73666F1FC5DF8AB8A2DE4E1098798713104076F04331977`。每项记录执行时源路径、长度与原字节SHA256，复制后再次核对；Git属性禁止对这些原件做换行归一化。包含真实成功/失败、准备错误、日志/TRX、fixture摘要、runner快照及[一次集中审查和定向补充](wp4-native/wp4-task-code-review.md)。连接receipt和私有诊断保留本地，不进入公共归档；294个待交付/公共输入逐值核对未包含本任务配置的数据库口令。

[七份收尾补充原件](wp4-native/final-local-checks/manifest.json)另存最终输入绑定、文档链接及暂存字节核对、归档回执和较早的OIDC历史库清理观察，manifest SHA256 `93AE3654E6A2D4FC8FA6C6F9DDFA74939BD91C80C3117B1A0BA26186A3F8F64D`，不改写211项原manifest。50个变化代码/锁文件全部在审查范围内，53个已审文件的最终字节相同；232份原件及3个manifest（含WP3/BUG147交付证据）已验证Git index与磁盘SHA一致，补充七份与其manifest也独立核对。

本记录是本地验收候选，尚未冒称远端main包含该阶段。六个owner-marked临时库保留至正常合并后的必要冒烟，再按原receipt核对并正常删除；不使用wildcard、force或中断其它连接。提交、PR、最终工作流输入检查、远端包含性和清理回执在实际执行后单独保存，不能预先填通过。
