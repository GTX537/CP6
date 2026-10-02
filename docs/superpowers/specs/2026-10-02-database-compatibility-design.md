# DB-COMPAT-01：CP6 SQL Server / PostgreSQL 双数据库兼容设计

日期：2026-10-02。主任务：[DB-COMPAT-01 / Issue #134](https://github.com/GTX537/CP6/issues/134)。活动状态和完成证据以该 Issue 为唯一真相源；本规格、盘点及计划共用一个任务，不为工作包另建重复状态。

用户已确认的方向是：同一套 CP6 业务代码，每次部署选择 SQL Server 或 PostgreSQL，并维护独立的数据库迁移链。本次只建立文档，尚未实施数据库兼容代码，未执行双库验证。并发令牌、身份分页水位、Platform 包兼容及 PostgreSQL 基线的具体实现，必须经 WP1 的真实试验达到本规格门槛后冻结；下列候选不代表已批准的技术实现。

依据：[只读兼容性盘点](../../audits/2026-10-02-database-compatibility.md)。执行顺序与六个工作包见[实施计划](../plans/2026-10-02-database-compatibility.md)。盘点源码基线为 `157630594e3371fe181955d2f6227ff3b6962c84`。

## 1. 目标、范围与边界

目标是在保留现有 SQL Server 行为的前提下，使 CP6 API、后台 worker、Space、身份消息、ERP 集成和数据库初始化工具能够部署到 PostgreSQL。部署内的 EF Core、Dapper、原生 SQL、连接工厂、迁移与健康检查必须选择同一个 provider。前端、客户端和公开 API 保持同一业务合同；数据库差异不进入业务调用参数。

本任务包含：数据库选择与启动校验；四个生产 DbContext 的模型与迁移；SQL 方言、参数与异常分类；并发、唯一性、租户隔离、身份快照、Outbox/Inbox 和 worker 租约语义；对应的真实双库回归与本地初始化、升级、备份恢复证据。原有业务逻辑继续共享，只有数据库相关能力分支。

以下内容单独规划，不属于本任务的完成条件：

- 将现有 SQL Server 生产业务数据整体搬到 PostgreSQL，包括停机窗口、持续复制、切换、对账和回退。
- 一个 CP6 实例同时使用两个业务数据库，或按租户动态切换 provider。
- 独立私有 CRM 的全部持久化层改造。CP6 侧身份与 ERP 集成端点、事务和消息合同属于本任务；跨仓新增需求通过原有合同与发布流程处理。
- 更换历史 CRM 来源治理/恢复工具操作的源数据库。旧源及其围栏、恢复、退役连接仍保持 SQL Server；必须将这些源连接与 CP6 自身数据库连接区分。当前 CP6 ERP 业务主库及 ERP 集成 Context 仍属于双数据库范围。
- Registry 决策、生产候选构建、DEV/UAT/PROD 部署及生产数据库迁移。兼容验证不能代替现有发布和环境审批。

## 2. 四个上下文及计数口径

| 上下文 | DbSet 声明 | snapshot 实体映射 | 现有迁移主文件 | Designer | snapshot 中 rowversion 列 |
| --- | ---: | ---: | ---: | ---: | ---: |
| CP6Context | 228 | 232 | 136 | 128 | 157 |
| SpaceContext | 83 | 83 | 47 | 47 | 41 |
| IdentityMessagingContext | 0 | 4 | 1 | 1 | 4 |
| ErpIntegrationContext | 5 | 8 | 3 | 3 | 6 |
| 合计 | 316 | 327 | 187 | 179 | 208 |

统计对象是已跟踪源码，迁移主文件排除 Designer 与 snapshot。实体映射按各 snapshot 唯一 `modelBuilder.Entity("类型名")` 计数，四个 snapshot 没有 owned 映射。327 是上下文内映射条目，不是对实际数据库表数的声明；跨上下文有321个唯一 CLR 类型，差额来自重复映射到不同 schema 的 Platform 消息类型。DbSet 数也不是完整模型数：CP6 包含 Platform 消息模型与 DataProtectionKey，ERP 通过独立配置加入 DeliveryReplayAudit。

实现时分别维护四个 context 的模型清单、schema/表清单和迁移历史清单。核对对象不能仅限业务 DbSet，必须包括消息、审计、重放、数据保护键和内部索引/约束。

## 3. Provider 选择、依赖与事务

新增显式配置 `Database:Provider`，规范值为 `SqlServer` 与 `PostgreSql`。旧配置没有该键时默认 `SqlServer`，保留现有部署兼容性；显式空值、拼写错误或未知值在启动和 db-init 参数校验阶段失败，不回退到另一数据库。连接字符串内容不能用于猜测 provider，错误信息不得打印凭证。

数据库选择在进程启动时确定，在进程生命周期内不切换。统一注册入口根据选择配置四个 DbContext、后台与设计时工厂、迁移组件和 Dapper/原生连接工厂。任何遗漏的 `UseSqlServer`、`SqlConnection`、SQL Server 连接字符串解析器或异常分类器都不能成为 PostgreSQL 模式下的隐式旁路。SQL Server 专有选项如 MARS 只进入对应实现。历史 CRM 来源工具的专用 SQL Server 连接单独命名并保留来源边界。

EF Core 继续使用当前 8.x 主版本；Npgsql EF provider 采用与 EF Core 8 配套的8.x主版本，Npgsql、EF runtime、design 工具和相关包的具体兼容版本及锁定结果在 WP1 中核实。不能通过升级整个 EF 主版本混入本任务，也不能把仅成功 restore 当作行为兼容证据。

需要业务、快照、Outbox 或命令收据原子提交的调用，必须共享同一个实际 `DbConnection` 和 `DbTransaction`，跨 context 显式参加该事务；相同连接字符串本身不足以保证原子性。Dapper 同样使用已选择 provider 的连接并接收当前事务。一般请求与独立 dispatcher 继续使用各自适当生命周期和连接，不建立全进程共享连接，也不将身份优先队列与普通队列预算合并。

事务提交前失败必须回滚全部业务、版本、消息和审计变更。异常分类按 provider 区分唯一冲突、乐观并发、死锁/序列化失败、超时及不可重试错误，保留既有外部错误合同，并限定重试边界，避免重放已发生的外部副作用。

## 4. 共享模型与 Provider 映射

领域实体、业务键、关系、租户隔离、审计和精度约束继续共享。长度、精度、必填、查询过滤、关系与业务唯一性优先表达为共同 EF 配置；数据库列类型、排序规则、生成值、并发值、索引 predicate、check constraint、触发器和原生 SQL 进入明确的 provider 实现。

模型差异必须在同一次构建中由固定 provider 选择确定。当前目标不需要运行时动态切换模型；不能为了两个部署模式直接加入按租户的模型缓存键。WP1 核实两种 provider 的设计时模型与运行时模型一致，以及一次进程只注册所选数据库路径。

必须逐项保留以下语义：

| 差异 | 设计要求 |
| --- | --- |
| NULL 与唯一索引 | `Sys_Lang(TenantId, LangKey)` 必须保证每个 LangKey 至多一条全局记录及每租户一条覆盖。PostgreSQL 采用可验证的 `NULLS NOT DISTINCT` 或分别约束全局/租户记录的 partial unique index；依据最低支持版本和迁移证据冻结选择。不能直接沿用 PostgreSQL 默认 NULL 唯一语义。 |
| 过滤索引与 check constraint | 重写方括号、布尔 `=0/1`、`ISJSON`、`LEN`、T-SQL 字符模式等表达式，保持业务条件和软删除规则。CP6 批量给唯一索引添加 TenantId 的逻辑及 FK 依赖例外也须核对，不能只替换字符串。 |
| 时间 | 分开定义业务本地时间/日期与 UTC 时刻。处理 `DateTime.Now`、datetime2、DateTimeOffset 和读回 Kind 的差异；UTC 时刻采用明确 UTC 写入和读取合同，本地业务值不能无证据重解释为 UTC。历史默认值、时区及精度损失须在升级用例中可追溯。 |
| Collation 与大小写 | 身份、消息 ID、幂等键等现有 BIN2 精确匹配和业务编码的大小写规则须分别定义，验证 Unicode、尾空格、唯一冲突和排序结果；不能把 PostgreSQL 默认行为视为等价。 |
| 标识符与 schema | PostgreSQL 标识符长度和大小写规则与现有 SQL Server 模型不同。显式命名并检查索引、约束、表名及 migration history，避免截断碰撞；保留四个 context 的 schema 隔离与消息用途。 |
| Guid、decimal 与默认值 | 明确 Guid 生成方与键身份，保留金额/数量 precision、scale、边界与舍入规则。检查 EF 默认值、迁移 literal、空值和 sentinel 行为，不借兼容改造改变业务金额。 |
| JSON | 先保持 API、存储内容与摘要合同；选择 text 或 json/jsonb 时验证文本规范化、哈希、参数、校验及查询语义。不能因 jsonb 可用而直接改变用于证据和幂等的原始内容。 |
| Worker 抢占 | SQL Server 锁提示、OUTPUT/MERGE 和 PostgreSQL 锁/RETURNING/冲突处理分别实现并验证，保持有界领取、租约过期、围栏、失败恢复与不可重复业务执行。 |

## 5. 并发令牌与身份分页水位：WP1 决策门槛

208处 snapshot rowversion 映射包含继承自 `BaseBizEntity.[Timestamp]` 的业务表，也包含 Space 与 Platform 消息表。已有 DTO/ETag/Base64 版本值、ERP 输入版本比对与8字节重放审计记录是外部或持久合同，不能以字段名称相同代替兼容证明。

WP1 必须真实试验以下两个候选，并以证据选择一个：

1. **SQL Server 保留 rowversion；PostgreSQL 由数据库维护 bytea 形式的 opaque token。** PostgreSQL 候选须证明数据库生成和更新路径覆盖 EF、Dapper、批量/直接 SQL、触发器及维护工具；若继续8字节合同，须证明编码、长度、生成值读回、旧值比较、更新原子性与重放审计兼容。触发器、函数或其他生成机制属于 PostgreSQL 迁移管理对象。
2. **SQL Server 保留 rowversion；PostgreSQL 由应用维护并发 token。** 应用候选须明确生成和冲突机制，对每个写路径执行旧 token 条件更新与新 token 写入，并在同一事务检查受影响行数。覆盖范围包括 EF 保存、Dapper、ExecuteUpdate/直接 SQL、worker、种子/升级及允许写库的维护工具，不能只依赖 SaveChanges interceptor。绕过条件更新或遗漏 raw writer 直接使该候选不满足门槛。

两个候选均须保留既有乐观冲突行为、无丢失更新、租约抢占正确性、API token 的序列化与错误合同。8字节与 Base64 是否完全保留需要测试结果支持；若现有合同无法保留，先形成明确版本化兼容设计并取得授权，不能悄然修改公开合同。不得为了取得双库“通过”删除并发列或降低冲突保护。

PostgreSQL `xmin` 可以作为经验证的数据库内部并发候选辅助信息，但不能直接替代现有8字节合同，更不能作为增量同步或跨分页快照水位。

身份版本清单当前使用 `MAX(CONVERT(bigint, RowVersion))` 建立分页边界，它与单行 opaque token 是两个问题。WP1 必须另行冻结一个可验证的快照/变化边界设计：租户、稳定排序位置和边界绑定游标，分页期间变化按现有合同拒绝/重启或提供一致快照；不能使用 token 随机值、时间戳、xmin 或进程计数冒充单调版本。业务聚合 Version、Outbox 事件版本与身份目录分页边界也不能混为同一字段。试验必须包含跨页并发变更、删除/墓碑、事务回滚、乱序事件及进程重启，证明不遗漏、不重复授权、不用旧页恢复已撤销权限。

如果受固定版本 Platform 包管理的模型、迁移辅助或 worker 只支持 SQL Server，本任务必须通过该组件同源的新包版本取得 PostgreSQL 能力，并保留来源、包版本及合同证据；不能复制其源码到 CP6 绕开发布边界。无法取得该证据时，记录确切限制，依赖该能力的工作包不得进入“已兼容”结论。

## 6. 独立迁移链与初始化

现有 SQL Server migration 路径、migration ID、snapshot 与已部署历史保持连续：CP6 Core 主链，Core 的 `Migrations/IdentityPriority` 与 `Migrations/ErpIntegration`，以及 `CP6.Space.Infrastructure/Migrations`。不能重写已经应用的 SQL Server 迁移、用 PostgreSQL snapshot 覆盖现有 snapshot，或让 PostgreSQL 执行原 T-SQL 历史。

PostgreSQL 迁移放入独立的 provider migration assembly，按四个 context 分组维护各自迁移和 snapshot；物理项目/assembly 的分组在 WP1 依构建与设计时工厂证据冻结。四个 context 的 provider、迁移 assembly、schema 和历史表选择必须明确绑定。SQL Server 继续采用既有 assembly/路径；PostgreSQL 的空库基线从已确认的完整模型和历史语义生成，不从187个 SQL Server 文件做未经验证的文本转换。

基线盘点须覆盖：所有业务及消息 schema/表、列与默认值、主键/外键/唯一/过滤索引、check constraint、存储过程/函数/触发器、数据保护键结构、历史迁移中的数据修正与初始化种子。模型 snapshot 不包含全部历史 SQL 和种子；仅能建表不足以验收。MES 存储过程、财务不可变保护、身份授权种子等用途必须有等价实现或明确适用边界，不能因创建成功而遗漏。

db-init 依显式 provider 执行四个 context 的正确迁移，失败返回失败并保留可复现诊断。验证空库初始化、同一库第二次初始化、应用重启及已有 SQL Server 链升级；PostgreSQL 首版无已发布历史版本时明确记录不适用，并在基线发布后建立前一支持版本升级夹具，不虚构历史升级通过。二次运行不得重复种子、重新分配业务版本、破坏消息或重复触发业务副作用。schema 与种子版本核对必须包含上下文之间的执行顺序和事务限制。

每条链记录真实使用的源 SHA、provider、数据库版本、migration ID、历史表、固定依赖版本与验证结果。已有 SQL Server 业务数据搬迁另立方案；本任务的初始化/升级验证使用隔离数据库，不把生产搬迁作为隐含步骤。

## 7. 真实双库验收矩阵

下列场景 SQL Server 与 PostgreSQL 均必须执行。两个真实数据库可以并行存在于隔离的本地测试环境，但每个应用进程仍只选择一个 provider。这是验收要求，不是已经执行的结果；EF InMemory、SQLite、模拟 SqlException 或合成传输不能代替对应验收。

| 领域 | SQL Server 与 PostgreSQL 均须取得的证据 |
| --- | --- |
| 注册与迁移 | 显式 provider、旧配置默认和未知值失败；四个 context 与 Dapper/provider 一致；空库基线、二次 db-init、跨版本升级、schema/索引/触发器/函数/种子和历史表核对。 |
| 业务与精度 | ERP/MES/WMS/财务及 Space 相关读写，金额数量边界、Guid、NULL、日期/UTC、JSON/哈希、case/尾空格与分页结果；SQL Server 既有行为不回退。 |
| 权限与租户 | 真实登录与权限拒绝，双租户隔离、全局/租户词条唯一，禁用/撤销、角色和数据范围，以及身份分页期间并发变更。 |
| 并发与事务 | stale token、真实并发更新、8字节/Base64/ETag 合同、重放审计、跨 context+Dapper 原子提交/回滚；原生和批量写路径的版本变化。 |
| 消息与 worker | 业务与 Outbox 同事务，优先/普通预算、重复/乱序/冲突、Inbox 去重、抢租约、围栏、超时重试、死信与重放、宕机重启及外部发布失败。需要传输的用例使用真实传输。 |
| 备份与恢复 | 隔离库备份、恢复到另一隔离库并启动应用；migration history、业务/审计/消息数据核对；恢复后 worker 领取与重复消息保护、身份边界/游标失效处理符合冻结合同。 |

WP1 出口要求：两库真实连接、四个 Context 的 Provider 注册/设计时选择证据及限定表的同事务试验，不代替 WP2 全量模型安装验收；两个并发候选的比较、最终选择及所有写路径覆盖清单；身份分页边界试验；Platform 固定包的双 provider 能力证据；PostgreSQL 支持版本、依赖、迁移分组、关键映射和 schema 完整性决策。任何未满足项都附真实失败、影响范围与可执行补救步骤；不得使用空占位结论通过 WP1。后续工作包依这些证据推进，不在证据缺失时自行认定兼容。

## 8. 交付与本地验证策略

本任务遵循仓库分支、一次任务级集中审查和与改动相称的本地验证规则。文档准备阶段只检查内容、链接及完整 diff；实施后保留相关自动化回归和真实双库证据。成功结果仅在代码、依赖和环境输入未变化时按真实来源复用，提交或 SHA 变化本身不要求重跑相同测试。

普通 GitHub Actions 保持手动入口；未经新的明确授权不触发或重跑，不借创建 PR、push 或修改 event filter 间接触发。推送/PR 前检查候选分支及受保护分支工作流用途、事件和依赖；混合发布/部署工作流按用途分析，不能整体禁用或弱化生产门禁。本地无法验证的部分明确保留未验证范围，不自动改用远程运行器。

交付文件与任务状态记录共用 Issue #134。代码实施、迁移、必要测试与文档在同一任务中闭环，收尾集中审查完整 diff；合并后进行必要集成/冒烟，并核对远端 main 包含任务提交。完成要求包括本规格的真实双库矩阵、公开合同与既有 SQL Server 行为回归；文档合入或安装了本地 PostgreSQL均不能关闭兼容任务。生产候选、真实目标环境、签名/扫描、迁移审批与生产部署继续遵循既有规范，未验证部分不能改称通过。
