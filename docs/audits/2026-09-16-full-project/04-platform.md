# CP6.Platform 真实代码现状盘点

审计日期：2026-09-16。固定Platform快照：`30bd23af6808d217a23878bd9437513043c52834`。本报告中 `path:line` 均相对此Platform仓库、对应此提交，不相对CP6主仓库。

本分项只读检查跟踪文件、实现、调用点、测试和归档证据；未修改Platform仓库、未构建/运行测试、未触发远程CI、未下载包、未连接业务数据库或部署环境。上级目录与仓库未发现适用AGENTS.md，遵守总审计的只读范围。开始和结束的 `git status --porcelain` 均为空。

## 1. 总体判断

**这是已有相当实代码和验证基础的跨服务 .NET 平台库仓库，不是一个可独立启动的业务产品或管理后台。** 7 个可发布库覆盖请求上下文、认证、错误、网关、事件、事务消息、遥测、部署演练契约和发布治理；另有 1 个不可发布的测试辅助库。所有 `src` 项目均为普通 `Microsoft.NET.Sdk` 类库；没有生产 `Program.cs`、业务 Controller、Web 宿主、前端项目或页面。生产使用必须在 Core、CRM 等消费者宿主中注册与适配。

不能把“仅是库”理解为“空壳”：JWT/JWKS 缓存、CloudEvents Schema 校验、SQL 条件租约、Serializable Inbox、NuGet 签名及时间戳验证均有完整实现与相应测试。真正未在此仓库实现的是产品业务、用户界面、生产宿主、消费者业务数据库迁移，以及真实环境部署。

现有证据层级要分开：

| 层级 | 本次可确认的事实 | 不能推出的结论 |
| --- | --- | --- |
| 源码实现 | 下列包、接口、状态机、严格验证器及测试均存在 | 当前环境已启用全部能力 |
| 本地归档证据 | 0.10.2 正式七包的五份原始 JSON 存在；本次重算文件哈希与随附清单一致 | 现在远端 Registry 的字节仍一致、签名现在仍有效 |
| 跨仓库历史验收记录 | CHANGELOG/计划头部记录 C01 72/72、零失败/跳过；指向 CRM 原始结果 | 本次重跑通过、当前提交已生产验收、C02 已完成 |
| 生产运行 | 本快照没有可据以证明生产身份、部署状态、容量或 SLO 的运行证据 | 不得宣称 Platform 已生产部署 |

关键定位：`CP6.Platform.sln:8`、`src/CP6.Platform.Testing/CP6.Platform.Testing.csproj:5`、`CHANGELOG.md:5`、`docs/evidence/c01/0.10.2/README.md:3`。

## 2. 可复现数量统计

统计范围为此 HEAD 的 `git ls-files` 跟踪文件；C# 行数用逐文件 `ReadAllLines().Count`，包含空行/注释，不是有效代码行；测试数量为源码中独立行 `[Fact]`/`[Theory]` 声明数量，不展开参数矩阵，不代表本次执行数量。

| 项目 | 数量 |
| --- | ---: |
| 全部跟踪文件 | 431 |
| `.csproj` | 18 |
| solution 纳入项目 | 17 |
| `src` 类库 / 可打包库 | 8 / 7 |
| xUnit 测试项目 | 5 |
| 测试宿主/辅助程序项目 | 4：DaprKafkaFixture、SqlServerFixture、P09Fixture、P09Validator |
| 独立 ReleaseTool 项目 | 1 |
| C# 文件 / 总物理行数 | 164 / 31,503 |
| `src` C# 文件 / 物理行数 | 89 / 11,938 |
| `tests` C# 文件 | 71 |
| GitHub workflow 文件 | 8 |
| `contracts` JSON Schema 文件 | 17：事件 1、SLO 1、P09 2、Release 13 |
| 规范事件 bundle 中的事件 | 1 个平台示例事件，配 5 个正负样本 |
| 前端文件 | 0：按 `.vue/.tsx/.jsx/.html/.css/.scss` 和 `package.json` 检索 |
| `Migrations/` 路径或 `.sql` 文件 | 0 |

第 18 个、未纳入 solution 的项目是 `tests/CP6.Platform.DeploymentTests/P09Validator/P09Validator.csproj`。因此“17 个 solution 项目”与“仓库 18 个项目”并不冲突。项目清单见 `CP6.Platform.sln:8` 至 `CP6.Platform.sln:44`；规范事件及样本见 `contracts/contract-bundle.v1.json:5`。

| 测试项目 | 有测试声明的 C# 文件 | Fact | Theory |
| --- | ---: | ---: | ---: |
| ArchitectureTests | 1 | 17 | 2 |
| AspNetCoreTests | 10 | 110 | 27 |
| DeploymentTests | 12 | 106 | 44 |
| ReleaseTests | 17 | 85 | 14 |
| UnitTests | 14 | 69 | 12 |
| 合计 | 54 | 387 | 99 |

合计 486 个测试方法声明。其余测试 C# 文件是 fixture/辅助代码；PowerShell 契约套件、真实 Dapr/Kafka/SQL 程序断言不计入 486。

## 3. 全部模块/包与职责

| 模块 | C# 文件/行数 | 实际能力 | 发布/宿主边界 |
| --- | ---: | --- | --- |
| `CP6.Platform.Contracts` | 6 / 613 | RequestContextSnapshot、稳定 Problem 定义、ReleaseIdentity、SLO evidence 解析/判断 | 可打包；无内部或外部包依赖；不是业务 DTO 全集 |
| `CP6.Platform.Abstractions` | 5 / 192 | IRequestContext、不可变 RequestContext、accessor 接口、遥测命名约定 | 可打包；依赖 Contracts |
| `CP6.Platform.AspNetCore` | 27 / 2,432 | JWT/JWKS、RequestContext 中间件、Problem Details/correlation、YARP、健康端点、OpenTelemetry、HTTP resilience | 可打包；宿主选择注册并配置身份/路由/检查/exporter |
| `CP6.Platform.Messaging` | 16 / 1,727 | CloudEvents 编解码、Schema/bundle、兼容性比较、Dapr 发布/调用、topic/partition 校验、trace | 可打包；没有业务消费者 Worker |
| `CP6.Platform.EntityFramework` | 8 / 1,744 | Outbox/Inbox/aggregate checkpoint/DLQ 模型、dispatcher、幂等/顺序、保留清理 | 可打包；不拥有业务 DbContext 或迁移 |
| `CP6.Platform.Deployment` | 8 / 2,365 | P09 非生产 runtime profile、演练 evidence、安全和 K8s 跨对象验证 | 可打包且独立；包含 P09 模板资产，不是生产部署控制面 |
| `CP6.Platform.Release` | 14 / 2,466 | 确定性 JSON、系统/平台候选、Locator、证据、固定信任、正式七包发布契约 | 可打包且独立；包含 Release Schema/fixture 资产，不负责部署 |
| `CP6.Platform.Testing` | 5 / 399 | 遥测捕获、确定性 HTTP fault 序列与注册扩展 | `IsPackable=false`；故障注入只接受精确 `Test`/`CI` 环境 |

项目依赖并非“大共享业务层”：Contracts 独立；Abstractions → Contracts；AspNetCore/EntityFramework/Messaging → Abstractions + Contracts；Deployment 与 Release 各自独立；Testing → 前述五个运行时库。Messaging 与 EntityFramework 没有相互项目引用，因此宿主需要实现 `ICp6OutboxPublisher` 等桥接，不能认为引入两个包就自动联通。架构测试冻结项目集合、引用方向、依赖 allowlist、资产所有权、无环图和 Testing 隔离：`tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs:105`、`:125`、`:134`、`:149`、`:203`、`:273`、`:798`。

工具链为 net8.0/C#12、nullable、warnings-as-errors、确定性构建，中央 NuGet 管理。依赖包括 EF Core/JwtBearer 8.0.30、Dapr.Client 1.18.5、CloudEvents 2.9.0、JsonSchema.Net 9.4.0、YARP 2.3.0、OpenTelemetry 1.18.0、Http.Resilience 10.9.0。这些是仓库固定版本事实，不是本次做出的升级推荐。定位：`Directory.Build.props:3`、`Directory.Packages.props:6`、`global.json:3`。

仓库审计版本是 `0.10.2.0`；普通运行时默认版本仍为 `0.8.0-alpha.2`、Deployment 默认 `0.9.0-alpha.1`、Release 默认 `0.10.2-test.local.1`。正式打包显式传入 `0.10.2`，所以不能只读 csproj 默认值判断消费者实际版本。定位：`VERSION:1`、`Directory.Build.props:22`、`src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj:8`、`src/CP6.Platform.Release/CP6.Platform.Release.csproj:8`、`docs/superpowers/plans/2026-09-09-c01-consumer-contract.md:56`。

## 4. API 宿主、UI 与产品边界

生产 API 宿主数量是 0。两个 `Sdk.Web` 项目均位于 tests：

| 宿主/程序 | 真正用途与调用 | 不能当成的产品 |
| --- | --- | --- |
| `tests/CP6.Platform.DaprKafkaFixture` | `/healthz`、`/dapr/subscribe`、`/publish-test`、`/invoke-test`、`/invoke/echo`、示例事件回调及 last 查询，实际调用 Messaging/Dapr 适配 | 公共事件 API 或 CRM 服务 |
| `tests/CP6.Platform.P09Fixture` | publisher/receiver/probe 等角色；正向调用/发布、负向验证、Dapr 订阅、事件接收、direct-kafka 探针 | 管理后台、业务消息中心或正式部署服务 |
| `tests/CP6.Platform.SqlServerFixture` | 控制台程序，临时 SQL 数据库上测试事务/租约/幂等/顺序/保留 | 数据迁移服务或生产 Worker |
| `tests/.../P09Validator` | 演练契约的辅助 CLI | 生产控制面 |
| `tools/CP6.Platform.ReleaseTool` | canonicalize、验证 provenance/evidence/trust/publication/package、下载包、RFC3161 probe | Release Web API 或上线自动化服务 |

入口证据：`tests/CP6.Platform.DaprKafkaFixture/Program.cs:7`、`:36`、`:51`、`:80`；`tests/CP6.Platform.P09Fixture/Program.cs:47`、`:103`、`:154`、`:254`、`:336`；`tests/CP6.Platform.SqlServerFixture/Program.cs:36`；`tools/CP6.Platform.ReleaseTool/Program.cs:16`、`:39`、`:50`、`:71`。

Platform 可为未来 UI 提供协议一致性：Problem Details 的 `code/messageKey/traceId/correlationId`、401/403、429/Retry-After 和健康/版本元数据。但没有菜单、表单、设计系统、路由页面、租户切换界面、身份登录页、权限配置页、消息/DLQ 页面。全套 UI 升级应落在真实客户端/Portal/CRM 等仓库，不能将这些库能力画成现有用户操作界面。`src/CP6.Platform.AspNetCore/Cp6ProblemDetailsExtensions.cs:34`、`:69`。

## 5. 身份、权限与租户

### 5.1 已实现的资源服务器验证

`AddCp6JwtBearer` 真正注册 ASP.NET JWT bearer，严格验证 issuer、audience、签名及寿命；限制 RS256、`typ=at+jwt`、不尝试所有公钥、不忽略 audience 尾斜杠。后续 claims 检查要求 iss/aud/sub/tenant_id/jti/iat/nbf/exp，单值约束、非空 UUID tenant、NumericDate 顺序以及 kid。失败响应统一成内容安全的 401/403 Problem。定位：`src/CP6.Platform.AspNetCore/Cp6JwtBearerExtensions.cs:14`、`:35`、`:46`、`:64`；`src/CP6.Platform.AspNetCore/Cp6JwtClaimsValidator.cs:10`、`:41`、`:50`。

这不是身份签发器：没有登录、密码存储、PKCE 授权服务、client_credentials 签发或用户/角色/权限业务管理。服务自己的 AuthorizationPolicy、scope/role 语义仍由消费者负责；Gateway route 的 `AuthorizationPolicy` 只是传入 ASP.NET 的策略名。`src/CP6.Platform.AspNetCore/Cp6GatewayProfile.cs:27`、`src/CP6.Platform.AspNetCore/Cp6GatewayExtensions.cs:31`。

### 5.2 C01 后的 JWKS 缓存是真实实现

有平台自有 `IConfigurationManager` 和 postconfigure guard，防止框架恢复默认宽松 backchannel。固定同 authority 的 `/.well-known/jwks.json`，10 秒请求预算、256 KiB body 上限、最长 300 秒 freshness、900 秒 hard trust age、未知 kid 额外刷新 60 秒窗口、失败 30 秒 backoff；缓存尊重 no-store/no-cache/must-revalidate，串行刷新并校验元数据 issuer/RSA 参数。定位：`src/CP6.Platform.AspNetCore/Cp6JwtConfigurationManager.cs:42`、`:154`、`:179`、`:212`、`:215`、`:321`、`:345`、`:555`。

这些约束意味着不能假设兼容所有通用 OIDC discovery 地址布局；改变 Core 的 JWKS 路径、cache headers、audience 或 token type 会影响跨仓库契约，需要先在实际 issuer→consumer 边界验收。

### 5.3 RequestContext 是可信适配边界，不是自动多租户系统

`AddCp6RequestContext<TResolver>` 要求消费者提供 `IRequestContextResolver`。中间件通过 resolver 取 snapshot，创建不可变 RequestContext，缺失/非法时 403，请求结束清空 scoped accessor。文档注释明确禁止把浏览器控制的 body/query/cookie/外部 tenant/user header 当权威身份。`RequestContext` 验证 TenantId 非空并保存 UserId/Subject/Audience/CorrelationId 等。

定位：`src/CP6.Platform.AspNetCore/RequestContextExtensions.cs:12`、`src/CP6.Platform.AspNetCore/IRequestContextResolver.cs:10`、`src/CP6.Platform.AspNetCore/RequestContextMiddleware.cs:27`、`:36`、`:46`；`src/CP6.Platform.Abstractions/RequestContext.cs:14`。

代码没有租户目录、套餐/授权管理、组织树，也没有业务 EF 全局租户过滤器。TenantId 被验证和传播不等于查询/行级隔离自动完成；消费者必须实现业务授权与数据过滤。

## 6. 网关、错误、观测与 HTTP 韧性

- **网关实装、业务路由不在此处。** YARP 从调用方 code-owned profile 装载 route/cluster，校验 destination URI/方法/限流参数。按连接 `RemoteIpAddress` 做每 route 固定窗口限流、QueueLimit=0，并清理 `X-CP6-*`、`X-Tenant-*`、`X-User-*` 等外部身份头。它不是分布式租户配额服务，多实例计数或前置负载均衡器的真实来源地址处理需要宿主/基础设施设计。`src/CP6.Platform.AspNetCore/Cp6GatewayExtensions.cs:21`、`:67`、`:81`、`:90`；`src/CP6.Platform.AspNetCore/Cp6GatewayHeaders.cs:8`。
- **错误和 correlation 实装。** 入站 correlation 校验/替换、响应传播、出站 handler，以及 RFC9457 内容安全 Problem writer；普通库调用需宿主按正确次序接入。`src/CP6.Platform.AspNetCore/Cp6CorrelationMiddleware.cs:16`、`src/CP6.Platform.AspNetCore/Cp6OutboundCorrelationHandler.cs:14`、`src/CP6.Platform.AspNetCore/Cp6ProblemDetailsExtensions.cs:34`。
- **遥测实装但 exporter 中立。** `AddCp6Observability` 注册 ASP.NET/HttpClient tracing、平台 ActivitySource/Meter、release identity，并设置全进程 BCL/OpenTelemetry trace-only propagator。这里没有实际 exporter/backend。`src/CP6.Platform.AspNetCore/Cp6ObservabilityServiceCollectionExtensions.cs:42`、`:46`、`:50`。全局 propagator 修改属于升级耦合点，宿主已有 telemetry 配置必须一并核对。
- **健康接口只是显式映射扩展。** 默认 `/health/live`、`/health/startup`、`/health/ready`、`/health/release`；startup/ready 按 health tag 选择检查，Degraded/Unhealthy 返回503，检查来源、授权、网络暴露由 host 管理。`src/CP6.Platform.AspNetCore/Cp6OperationalEndpointProfile.cs:13`、`src/CP6.Platform.AspNetCore/Cp6OperationalEndpointRouteBuilderExtensions.cs:38`、`:55`。
- **HTTP resilience 实装且显式 opt-in。** named-client pipeline 包含总超时→条件重试→熔断→单次超时；区分幂等读、幂等写、非幂等，后者重试强制为0；对请求方法/幂等写前提做验证。`src/CP6.Platform.AspNetCore/Cp6HttpResilienceServiceCollectionExtensions.cs:18`、`:52`、`:143`；`src/CP6.Platform.AspNetCore/Cp6HttpResilienceProfile.cs:69`。
- **SLO 是证据模型/评估器，不是生产监控系统。** 对完整度、样本、来源、阈值判断 Pass/Fail/Indeterminate；没有生产采样管线或已达标负载报告。`src/CP6.Platform.Contracts/Cp6SloEvidenceEvaluator.cs:8`；Performance gate 明确 NotApplicable，`eng/verify.ps1:537`。

## 7. 事件、Outbox/Inbox 与数据库

### 7.1 事件协议和真实 transport

CloudEvents 1.0 structured JSON 的 envelope、tenant-scoped subject、correlation/causation/aggregate/schema/region、trace 扩展都有编码和验证。`Cp6CloudEventValidator` 严格解析 JSON、按 type/dataschema/schemaversion 精确解析 bundle、执行 JSON Schema、再解码 CloudEvent。bundle 包含路径/哈希和正负样本校验；兼容性工具检查 schema 变动。`src/CP6.Platform.Messaging/Cp6CloudEventCodec.cs:19`、`:114`；`src/CP6.Platform.Messaging/Cp6CloudEventValidator.cs:24`、`:48`、`:53`；`src/CP6.Platform.Messaging/Cp6ContractBundle.cs:44`。

标准 bundle 目前仅 `com.gtx537.platform.contract-example.changed.v1`，是契约示例，不是 Customer/Order 等业务事件目录。`contracts/contract-bundle.v1.json:7`。业务事件所有权与生产者/消费者调用必须在业务仓库确认。

发布链真实存在：structured event → Schema validator → 计算 topic/`tenantId/aggregateId` partition → Dapr transport → `DaprClient.PublishByteEventAsync`。入站 delivery validator 同时校验 event、broker topic 和 partition；Dapr service invocation 使用 local sidecar 的 `/v1.0/invoke/.../method/...` HTTP API。定位：`src/CP6.Platform.Messaging/Cp6DaprEventPublisher.cs:22`、`:35`、`:44`；`src/CP6.Platform.Messaging/Cp6DaprKafkaConventions.cs:26`；`src/CP6.Platform.Messaging/Cp6DaprDeliveryValidator.cs:48`；`src/CP6.Platform.Messaging/Cp6DaprTransport.cs:31`、`:47`。

transport 要求宿主提供 DaprClient/HttpClient；service invocation 不实现业务授权，`src/CP6.Platform.Messaging/Cp6DaprServiceInvoker.cs:5` 已明确归属消费者。没有“一个总线包自动注册所有业务订阅”的行为。

### 7.2 EF 事务消息实装

模型映射 4 张表，允许 caller 指定 schema：

| 表 | 核心唯一/索引边界 | 定位 |
| --- | --- | --- |
| `Cp6_OutboxMessage` | MessageId 唯一；状态/可用时间/租约索引；tenant/aggregate/version 索引 | `src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingModelBuilderExtensions.cs:25` |
| `Cp6_InboxMessage` | `(ConsumerName, MessageId)` 唯一 | 同文件 `:47` |
| `Cp6_InboxAggregateCheckpoint` | `(ConsumerName, TenantId, AggregateId)` 唯一 | 同文件 `:65` |
| `Cp6_DeadLetterRecord` | 方向/消息/消费者及时间索引，replay reason | 同文件 `:75` |

Outbox `Enqueue` 只把实体加入传入 DbContext，不调用 SaveChanges；业务写入与消息原子提交由调用方负责。批量 claim 使用再次检查状态/租约的 `ExecuteUpdateAsync` 与随机 lease token，标记发布核对 owner/token；发布与成功确认跨两个步骤，语义是 at-least-once。`src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs:24`、`:37`、`:57`、`:74`、`:224`；`src/CP6.Platform.EntityFramework/Cp6OutboxDispatcher.cs:32`；`docs/P06-OUTBOX-INBOX.md:24`。

Inbox 在 Serializable 事务中基于 `(consumer,messageId)` 和 payload hash 识别重复/冲突；tenant+aggregate checkpoint 忽略旧版本；handler 写传入的同一个 DbContext，再与 checkpoint、Inbox 一并 commit。它提供的是版本单调性防旧消息，代码不是对版本连续缺口的缓冲排序器（只比较 `delivery.AggregateVersion <= checkpoint.AggregateVersion`）。`src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs:139`、`:142`、`:146`、`:175`、`:181`、`:192`、`:203`。

DLQ、有限次数 retry、重放原因记录和 retention 均有实现；默认 Outbox7天、Inbox30天、DLQ90天，失败次数默认10。`src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingTypes.cs:173`、`:187`；`src/CP6.Platform.EntityFramework/Cp6MessageRetentionService.cs:17`。

### 7.3 宿主尚需承担的必需工作

- 引入模型并生成/维护自身 migration；这里没有业务 DbContext、迁移或生产 db-init。SQL fixture 用 `EnsureCreatedAsync`，不能作为迁移升级兼容性验收。`docs/P06-OUTBOX-INBOX.md:15`；`tests/CP6.Platform.SqlServerFixture/Program.cs:41`。
- 实现 envelope/delivery validator、OutboxPublisher、后台调度与唯一 worker id；此处 dispatcher 是可调用类，没有 production BackgroundService 注册。接口：`src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingTypes.cs:112`、`:117`、`:122`。
- 对重放先做身份、权限和租户检查：requeue 库方法按 id/consumer 操作并记录 reason，自身无 ClaimsPrincipal 或 authorization service。既定文档明确此责任属于调用方，不能直接把它暴露成管理 API。`docs/P06-OUTBOX-INBOX.md:29`；`src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs:181`；`src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs:226`。
- Handler 只执行同库可回滚写入；邮件/HTTP/broker 等外部副作用应走结果 Outbox。`docs/P06-OUTBOX-INBOX.md:19`。
- `MessageId`/`(ConsumerName,MessageId)` 唯一范围不是额外按 tenant 划分，消费者需保证相应全局消息 ID 约定；实体有 TenantId 并不自动增加租户查询隔离。

这些都是平台与业务的明确分工，不应将未提供产品宿主误判成这些库是占位实现。

## 8. Deployment 与发布治理

### 8.1 P09 是可执行的非生产演练

Profile validator 明确只接受 `environmentClass=NonProduction`，固定平台演练 identity/topic、Dapr/Kafka 版本、ACL/secret reference/Compose/K8s/evidence 边界；K8s validator 检查订阅 scope、网络 default-deny、DNS/probe/Kafka 规则、禁止字段与明文 secret 模式。`src/CP6.Platform.Deployment/Cp6P09RuntimeProfileValidator.cs:145`、`:151`、`:167`；`src/CP6.Platform.Deployment/Cp6P09KubernetesValidator.cs:368`、`:619`。

Compose 实际构建 tests/P09Fixture，并启动 Kafka、publisher/receiver 及 Dapr sidecar、direct probe、unauthorized sidecar、admin 角色，执行正负向实验与残留清理。这里镜像是明确版本标签（如 Dapr 1.18.2/Kafka4.3.1）；不能把它当成业务生产 digest 推广链。`deploy/p09/compose/compose.yaml:1`、`:53`、`:92`、`:104`、`:153`、`:212`。

Kubernetes base/CI overlay 是可渲染的模板/契约资产，但门禁使用离线 helper、两次相同渲染哈希及 `kubectl apply --dry-run=client --validate=false`；loopback sentinel 禁止写 API。这证明离线模板行为，不证明真实 cluster 的 CNI/NetworkPolicy/CSI/Secret/滚动发布运作。`eng/test-p09-kubernetes.ps1:353`、`:393`、`:417`、`:420`；`docs/P09-NON-PRODUCTION-RUNTIME.md:96`。

### 8.2 P10 已有完整验证代码，不能等同“系统已发布”

Release 包拥有 13 个 schema、严格属性和语义校验、确定性 canonical JSON/哈希、候选/Locator、证据/构建 provenance、固定 trust policy、正式发布记录验证。JSON 拒绝重复属性、非规范 Unicode、不允许的数字等，不是任意通用 JSON canonicalizer。`src/CP6.Platform.Release/Cp6DeterministicJson.cs:19`、`:170`、`:195`、`:208`。

系统候选要求精确仓库集合 `CP6/CP6.CRM/CP6.Platform/CP6.Portal`；Platform candidate 明确 `deployable=false`。这些是 schema/validator 的真实能力，fixture 中的 Portal/System 描述不能当真实产品或产出。`src/CP6.Platform.Release/Cp6ReleaseValidator.cs:8`、`:26`、`:47`、`:117`、`:134`。

ReleaseTool 的 NuGet 验证确实检查 author primary signature、唯一 RFC3161 timestamp、固定 signer、包身份、SHA-256 message imprint、timestamp EKU 和在线撤销的证书链；不是只验证一份 JSON 的字段。`tools/CP6.Platform.ReleaseTool/FormalPackageVerifier.cs:36`、`:59`、`:65`、`:74`、`:152`、`:180`。另一方面，纯 `Cp6ReleaseValidator` 校验候选引用结构，不能替代远端物件取回、hash/签名和 runtime 身份核对。

正式 publication validator要求七包同版本、source绑定、`BytePreserving`、签名前后/回读字节哈希一致及 RFC3161。`src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs:49`、`:153`、`:180`、`:188`。

NuGet 权威源是 GitHub Packages 的 `CP6.Platform.*` 映射，不是本仓库 ACR/GHCR 容器发布实现。`NuGet.config:6`、`:8`。P10 当前文档仍将 System 候选/Locator/部署留待后续验收，`docs/P10-RELEASE-GOVERNANCE.md:64`、`:78`、`:552`。是否已在其他仓库更新，应由总审计跨库对账，不能用这里的计划声称现已上线。

## 9. Core / CRM 集成证据与文档漂移

Platform 源码不引用 Core/CRM 的源码项目；消费者通过固定 NuGet 发布边界接入。仓库发布记录、文档和真实测试 fixture 为跨库协作提供契约。

最强的本地历史链条是 C01：

1. 0.10.2 七包自 `main@fbcd21528078a04e5b53c42c5fdfebe6ffa9655f` 一次构建，正式 workflow run `34417259187`；Windows签名/Registry回读与Linux独立复验归档在本仓库。`docs/evidence/c01/0.10.2/README.md:3`、`:42`。
2. `CHANGELOG.md:7` 与 C01 plan 开头记录 2026-09-10 实际 72/72，零失败/跳过：Core `fb55a877de8ee8f9d27fd3bf8e73c824a21549e8`、CRM execution `37cf0e58ff146ed58582768cf2c91e3c9fbe81cf`、已发布0.10.2；覆盖实际密码/PKCE/service grants、Platform/CRM consumption、严格拒绝、缓存边界及完整真实时间轮换。执行 SHA 与相同 tree 的集成 SHA 分开保留。
3. 同条记录明确：CRM main 的历史整体 CI 仍失败，原因是 billing 阻断最后 aggregate job；不能把6个实质作业通过改写成历史整条全绿。后续 owner 授权本地验证替代普通必需 Actions check。`docs/superpowers/plans/2026-09-09-c01-consumer-contract.md:3`。

本次对五份归档 JSON 重算的 SHA-256 与 `docs/evidence/c01/0.10.2/README.md:34` 的五项清单逐项一致；但没有实际 `.nupkg` 文件随此快照提交，也没有本次联网回读/验签。C01 业务联调原始 summary/JUnit 在 CRM 仓库，需与 CRM 快照核实；此报告对72/72采用“历史记录已报告”的表达，不冒充本次测试。

存在明确文档时间层混合：

| 位置 | 留存文字 | 应如何解释 |
| --- | --- | --- |
| `README.md:7` | 仍说正式0.10.1、P10 pending S05/S06 | 顶层状态落后于 C01 0.10.2 记录 |
| `docs/evidence/c01/0.10.2/README.md:79` | 发布后仍待CRM接受、publication不关闭C01 | 9月9日发布检查点，应保留历史语义 |
| `CHANGELOG.md:7`、C01 plan `:3` | 9月10日 actual acceptance通过 | 更新的历史验收总状态 |
| `CONTRIBUTING.md:5` 与普通 workflow | 文档要求远端Windows/Linux CI，workflow只有手动 | 9月10日交付例外记录解释差异，不能自动运行CI |

后续整体升级需要整理一份按“源码、包、消费者、环境”分层的当前状态索引，保留历史证据而避免读者把不同检查点混为一谈。此审计未修改这些文档。

## 10. 测试与验证实际覆盖

`eng/verify.ps1` 是机器证据入口，输出 Passed/Failed/NotApplicable/NotRun 和 JUnit；有失败门禁负向脚本，不把不可用 Docker 冒充成功。`eng/verify.ps1:118`、`:184`、`:418`、`:578`。

| 范围 | 可见自动化覆盖/机制 | 边界 |
| --- | --- | --- |
| 认证/缓存 | token type/audience隔离、缺claim、未知kid、算法/expiry；真实loopback discovery/JWKS HTTP、缓存age/backoff/并发/redirect/cookie | loopback是组件测试，真实Core链需C01证据 |
| 请求上下文/错误/网关 | resolver失败关闭、correlation、安全Problem、YARP loopback route/header/rate-limit | 不覆盖业务授权模型或真实生产前置代理拓扑 |
| 消息协议 | Schema正负样本、bundle完整性、兼容性、Dapr transport contracts、trace | bundle示例不是业务完整事件集 |
| SQL事务消息 | 独立真实SQL程序测试Outbox原子性、租约/重投、DLQ重放、Inbox幂等/顺序、保留 | `EnsureCreated` 临时库，不是消费方迁移或真实数据升级 |
| 观测/HTTP故障 | 两服务W3C trace、exporter isolation、取消/超时/retry/circuit、fault script | 不代表生产telemetry开启或SLO达标 |
| P09 | profile/schema/evidence、Compose/K8s/脚本负向与真实Compose演练入口 | K8s为离线契约，没有真实cluster测试 |
| P10 | 确定性JSON、API/Schema parity、固定trust、package set与脚本/workflow安全、打包隔离 | 系统部署产物和正式环境推广另行验证 |
| 架构 | 项目/依赖/包资产边界、禁生产Testing、包内容/敏感信息防护 | 源码结构测试不能证明业务可用 |

代表调用与断言：`tests/CP6.Platform.AspNetCoreTests/AuthenticationContractTests.cs:25`、`:113`；`tests/CP6.Platform.AspNetCoreTests/JwtDiscoveryCacheContractTests.cs:278`、`:303`、`:387`；`tests/CP6.Platform.SqlServerFixture/Program.cs:56`；`tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs:383`；`eng/verify.ps1:459`、`:476`、`:532`。

历史完整门禁记录在 `docs/superpowers/plans/2026-09-09-c01-consumer-contract.md:46`：Unit124、Integration231、E2E31、Architecture98、Release216，全零失败/跳过。这是当时展开的测试执行数，与本次486个源码方法声明口径不同；各 gate 可能重复运行部分测试，不能相加做独立覆盖率。此次固定 HEAD 未重跑，不能断言其当前本地测试全绿。

当前 `.github/workflows/platform-validation.yml:3` 仅 `workflow_dispatch`。定义仍包含 Windows/Linux常规矩阵、真实Dapr/Kafka、真实SQLServer、P09非生产演练，但不会由push/PR自动触发。`docs/delivery/c01-local-gates-20260910/README.md:3`、`:5`、`:7` 解释已授权的Actions额度例外。正式发布workflow独立，使用 `p10-formal-release` Environment，写入链锁定0.10.2，Windows签名发布后Linux验证；这些slots已消费，不得把重跑当一般验证。`.github/workflows/p10-formal-packages.yml:19`、`:44`、`:190`。

## 11. 对整套 CP6 升级的约束与优先核实项

| 约束/缺口 | 对下一阶段的实际影响 |
| --- | --- |
| Platform无产品UI/业务宿主 | UI/流程盘点和改版必须绑定真实Core/CRM/Portal页面与API；Platform只支持公共协议 |
| 七包和签名证据不可变 | 变更公共API、认证或Schema必须新版本前向发布、消费者固定版本验收，不能覆盖0.10.2 |
| JWT issuer/JWKS路径/缓存/typ严格 | Core身份重构与CRM消费升级需要一起验证，尤其用户/服务audience和轮换时间边界 |
| RequestContext不等于业务授权 | 角色/权限/租户切换和EF隔离必须在消费者继续审计，不能因引包而移除 |
| Messaging与EF为分离库 | 实际发布适配、Worker调度、订阅endpoint、业务schema及迁移属于消费者；需要沿业务写入→Outbox→broker→Inbox→投影查真调用链 |
| at-least-once与版本checkpoint | 新业务流程要明确幂等key、消息ID作用域、乱序语义和可重放副作用；不要宣称exactly-once或自动补齐缺失版本 |
| 重放授权在宿主 | 未来DLQ界面/API必须与消费者权限和tenant作用域结合 |
| 全进程trace-only propagator | telemetry升级会影响宿主其他HttpClient/OTel注册，需防顺序覆盖或baggage回流 |
| 网关限流是进程内按IP | 多实例、前置代理与租户额度目标改变时，需做新的容量/来源身份设计 |
| P09是固定非生产演练 | 不能直接将fixture Compose/K8s提升为DEV/UAT/PROD正式拓扑 |
| Release候选validator要求CP6.Portal | Portal真实仓库/产物尚未在此仓库证明；系统候选不能用fixture补齐 |
| 普通CI当前手动 | 升级交付计划必须明确获授权的本地/远端门禁策略，不能默认PR会自动验证 |
| 状态入口漂移 | 应先对齐各仓库当前源码、包版本、消费者证明与环境状态，再制定全项目升级次序 |

仍未验证：当前Registry包回读与密码学验签、远端分支保护/CI状态、C01原始72项结果在CRM快照的完整性、C02在Core/CRM当前固定HEAD的实际接入、生产数据库迁移/兼容性、真实Kafka/Dapr/K8s运行身份与网络策略、生产容量/告警/SLO、任何业务UI完整度。它们都不能从此Platform仓库的示例、Schema、历史描述或测试宿主推导出来。
