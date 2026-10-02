# DB-COMPAT-01 WP1：实际 Platform 0.10.2 依赖审计

审计日期：2026-10-02。CP6 基线：`685a5238`。本报告只覆盖实际消费的 `CP6.Platform.EntityFramework 0.10.2` 固定源码与缓存包身份；不使用本地旧 `CP6.Platform/` checkout，不修改 Platform 权威仓库、生产配置或数据库。

**初始静态审计结论（历史）：源码没有绑定 SQL Server 的 SQL 方言、列类型、过滤索引或 provider 依赖；四个数据库生成的 `byte[] RowVersion` 是运行门禁。消费者模型覆盖加 PostgreSQL `bytea` 插入/更新触发器当时只有静态可行性，真实 PG 证据尚缺，Platform 门禁为 Pending。** 该历史结论没有将新增 Npgsql 引用、DDL 可生成或源码检查当作兼容证明。

**双库实测补记：实际签名固定包 `[0.10.2]` 已在 SQL Server 和 PostgreSQL 18.6 的限量探针通过。四个包实体的消费者 PG 模型覆盖加数据库生成 8 字节 `bytea` 触发器能够保留既有 `byte[]` / Base64 和公开 API；当前方案保持固定包，不需要为已验证路径发布新包或复制实现。** 完整结果与冻结范围见 [WP1-PROBE](WP1-PROBE.md#executed-results) 和 [父任务决策](WP1-DECISIONS.md)。正式四 Context 模型、迁移触发器安装、调用方 UTC 边界和 worker 错误分类仍须后续交付，不由限量试验代替。

运行 `AssemblyInformationalVersion` 实际显示 `0.8.0-alpha.2+fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，这是 NuGet / lock 身份为 0.10.2 的签名包内既有程序集元数据，源码 SHA 一致；不声称加载程序集的 informational SemVer 是 0.10.2。本报告“没有编译 / 连接数据库”描述原静态审计自身，不覆盖后来由独立探针执行的真实试验。

## 包与源码身份

- [Core 项目](../../../CP6.Core/CP6.Core.csproj) L13-L14 精确引用 Messaging / EntityFramework `[0.10.2]`；[WebApi 项目](../../../CP6.WebApi/CP6.WebApi.csproj) L10 精确引用 AspNetCore `[0.10.2]`。本报告不扩大为 Messaging / AspNetCore 全源码兼容性结论。
- [Core lock](../../../CP6.Core/packages.lock.json) L42-L52：EntityFramework 请求区间 `[0.10.2, 0.10.2]`，解析 `0.10.2`，依赖 Abstractions / Contracts `0.10.2` 与 EF Core / Relational `8.0.30`。缓存 nuspec 依赖一致，没有 SqlServer / Npgsql 引用；[固定源码项目](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj#L7-L14) 也只引用平台抽象/契约和 EF Core / Relational。
- 缓存 nuspec 记录仓库 `https://github.com/GTX537/CP6.Platform`、提交 `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`。Messaging、AspNetCore、Abstractions、Contracts 缓存 nuspec 同样绑定该提交；这仅证明元数据一致，不代替其完整源码审计。
- 用只读 GitHub API 从该提交取回 `src/CP6.Platform.EntityFramework/` 下全部 9 个文件，镜像位于忽略目录 `D:\CP6\tmp\db-compat-platform-source-20261002`；GitHub tree / contents 均指定完整 SHA。没有切换或修改旧 Platform checkout。
- 实际缓存文件为 `C:\Users\tt\.nuget\packages\cp6.platform.entityframework\0.10.2\cp6.platform.entityframework.0.10.2.nupkg`。整文件 SHA-256：`cc780ce1b454c62f96caa990eb2e0462563b1917e8c505b11100a4555d5b4544`；整文件 SHA-512（Base64）：`1YG/vaZ29N8CK6UE5GuLoAg7fIm85Vv4D9WOEh/E17/ToqiXq/TEF6MLTsmKc0KmZ0onidsEpbBdshPe7Pp5AQ==`，与缓存 `.nupkg.sha512` 一致。
- `dotnet nuget verify <上述缓存包> --all --certificate-fingerprint 1DEBFB8FF286EA51192B7F259D1AC823C105C4188EAC40148598D37F0E20FF0D --verbosity normal` 在本次审计执行，退出码 `0`，输出 `Successfully verified package 'CP6.Platform.EntityFramework.0.10.2'.`。作者为 `CN=CP6 Platform Release Signing`，SHA-256 指纹与 [NuGet.config](../../../NuGet.config) L17 固定可信作者一致；签名时间戳显示 `2026/09/09 19:54:22`。
- 验证器输出的 NuGet 内容哈希 `NiTLD0tp/qopxQZstxNmYhHd3OKOlWNB4NIZ51sRhcUdj8kB445eRtPYAt67nhXYGm94PtyFKT+4Aair5qHumA==` 与 Core lock、缓存 `.nupkg.metadata` 一致，后者来源为 GitHub feed。该内容哈希与整文件 SHA-512 分别记录，不能混为同一个值。本次没有修改信任策略，没有 restore / build，没有重新回读 Registry 或取得权威发布 archive，因此不声称本次完成 Registry / 发布归档独立复验。

## 固定源码证据与影响

以下链接均绑定上述发布提交，行号来自本次取得的源码。

| 项目 | 证据 | 对 PostgreSQL 的影响 |
| --- | --- | --- |
| 模型入口 | [模型 L8-L20](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingModelBuilderExtensions.cs#L8-L20) 公开接收并返回 `ModelBuilder`，允许传 schema | 消费者可在调用后追加 provider 映射；包没有封闭模型或只允许 SqlServer 的构造检查 |
| 类型、索引、SQL | [模型 L23-L85](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingModelBuilderExtensions.cs#L23-L85) 使用 EF 表、键、索引、长度、必填、Unicode 标志 | 9 文件检查未发现 `HasColumnType`、`HasFilter`、SQL 默认值、SqlServer / Npgsql 名称、`FromSql` / `ExecuteSql` 或原生 SQL。必要类型由消费者/provider 决定；仍要检查最终模型/DDL |
| 自动并发令牌 | 模型 [L42](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingModelBuilderExtensions.cs#L42)、[L60](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingModelBuilderExtensions.cs#L60)、[L70](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingModelBuilderExtensions.cs#L70)、[L85](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingModelBuilderExtensions.cs#L85) 均 `.IsRowVersion()`；实体 [L71](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingEntities.cs#L71)、[L189](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingEntities.cs#L189)、[L264](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingEntities.cs#L264)、[L327](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingEntities.cs#L327) 是空数组初始化、私有 setter 的 `byte[]` | PG 不会因该标志自动获得 SQL Server `rowversion` 的生成行为。必须保证全部插入/更新得到新 token，且 EF 读取数据库返回值 |
| UTC | [Outbox L36 / L54](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs#L36)、[Inbox L169 / L305](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L169)、[Retention L23](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6MessageRetentionService.cs#L23) 来自 `TimeProvider.GetUtcNow()`；持久化时间为 `DateTimeOffset`。公开 [RecordReplay L329-L338](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingEntities.cs#L329-L338) 接受调用者时间且未规范化 | 后续 PG 实测 +02:00 参数在提交前被 Npgsql/EF 拒绝；显式 `ToUniversalTime()` 成功并以零 offset 回读同一瞬间。共享 UTC 时间调用合同必须要求 Offset=0，严禁以非零 offset 直接写 PG `timestamptz`。本证据不把全部业务 DateTime 改成 UTC，也不证明所有精度边界均完成 |
| 异常与事务 | [Inbox L73-L128](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L73-L128) 捕获 `DbUpdateException` / 一般异常，重读 Inbox 或记录可重试失败；Serializable 在 [L139](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L139)、[L280](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L280) | 没有 SQL Server 错误号绑定，也没有 PostgreSQL SQLSTATE 分类或内部 serialization retry loop。真实 `40001` 已观察到一般异常路径返回 RetryScheduled、败方回滚且竞争者提交保留；这是现有包行为证据。WP3 仍须明确 `40001` 分类与整个事务重试边界，以及全部 worker / 失败记录竞争路径；模型覆盖不能代替这些工作 |
| 遥测 | [Telemetry L125-L134](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6EntityFrameworkTelemetry.cs#L125-L134) 捕获观察器异常 | 不包含数据库 SQL、provider 分支或事务写入；不承担 retry / token 维护 |

## 全部消息写入路径

| 写入路径 | 固定源码位置 | token 要求 |
| --- | --- | --- |
| Outbox Enqueue | [OutboxStore L24-L39](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs#L24-L39) 只 `Add`，由消费者提交 | INSERT 生成并返回 token；与业务事务回滚一致 |
| Outbox claim / 超时回收 | [OutboxStore L57-L91](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs#L57-L91) 查询候选后 `ExecuteUpdateAsync` 设置状态、租约、次数，再按新 LeaseToken 查询 | **不经过 SaveChanges，不显式设置或检查 RowVersion。** 必须由数据库 UPDATE 生成 token；claim 本身用状态/租约谓词竞争 |
| 发布 / 重试 / 出站死信 | [OutboxStore L128-L177](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs#L128-L177) 加载 owned claim 后 SaveChanges；死信同时 INSERT dead letter，显式事务 | UPDATE 与 INSERT 都生成 token；旧上下文不得覆盖新 claim。owned claim 查询见 [L224-L236](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs#L224-L236) |
| 出站 requeue | [OutboxStore L196-L209](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs#L196-L209) 同事务更新 Outbox 与 dead letter | 两表 token 均更新，重复 replay / stale update 失败 |
| Inbox 应用、乱序、payload 冲突 | [InboxProcessor L138-L205](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L138-L205)、[L213-L219](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L213-L219) | Inbox / checkpoint INSERT 或 UPDATE、冲突 dead letter INSERT 均覆盖；业务 callback 与 checkpoint/inbox 同事务 |
| Inbox 失败 / 入站 requeue | [InboxProcessor L239-L256](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L239-L256)、[L279-L322](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L279-L322) | Inbox / dead letter 插入更新均覆盖；错误记录使用独立 context / 事务 |
| retention | [Retention L28-L47](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6MessageRetentionService.cs#L28-L47) 同事务执行 3 个 `ExecuteDeleteAsync` | DELETE 依照状态/时间谓词，不走自动 token 并发检查。checkpoint 未被删除；边界、活动租约保留和删除事务回滚须验证 |

[Dispatcher L37-L54 / L90-L98](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxDispatcher.cs#L37-L54) 为 claim、成功、失败分别建 context，但 `Cp6OutboxStore` 公共 API 不禁止调用者复用 context；并发验证必须覆盖旧 tracked context，不限于默认 dispatcher 的 context 生命周期。包没有 `CurrentTransaction` / provider 身份检查；显式事务入口需要验证调用方契约。

## 双库执行证据与剩余实施范围

最终 SQL Server 正向报告 `D:\CP6\tmp\wp1-probe-sql-reviewed.json` 为 **28 Passed / 1 NotApplicable / 0 Failed / 0 Blocked**；PostgreSQL 18.6 的 `D:\CP6\tmp\wp1-probe-pg.json` 为 **31 Passed / 1 Rejected / 0 Failed / 0 Blocked**。这些是整个限量探针计数，不是 Platform 单独的测试项数。PG 的 Rejected 是实际 raw SQL / ExecuteUpdate 展示陈旧写覆盖后被否决的 SaveChanges-only 应用 token 候选；SQL 的 NotApplicable 仅对应该 PG 候选试验，不是必测跳过。正向、预期失败的负对照和清理证据均原样保留，详见 [执行记录](WP1-PROBE.md#executed-results)。

双库最终输入 fingerprint 均为 `0F0B443961674D40B8163768225AE12D1A55CD18F28FCE0D5F16BB0CB91C6BB3`，实际探针程序集和记录的源码/运行程序集哈希一致。SQL 的 SourceSha 是基线 `685a5238734816f6a19b21ce60f76d4bf93cd796` 加当时未提交 WP1 输入；PG 记录保存相同实现输入的 checkpoint `d30da70e6698b73ceac0d23c16f16dc02d9b8429`。该出处区别保留，不把初始 SQL 报告改称 exact-commit 运行。固定依赖为 EF Core `8.0.30`、Npgsql EF provider `[8.0.11]`、Npgsql driver `[8.0.8]` 和实际 Platform `[0.10.2]`。

| 已执行范围 | 真实证据与结论 | 后续仍须完成 |
| --- | --- | --- |
| 四实体与包公共写入 API | INSERT / UPDATE 返回 8 字节 token；claim ExecuteUpdate、旧 EF 行、超时接管、旧 lease 发布/重试、retry / dead letter / requeue、失败 handler 回滚与 replay stale-context 冲突通过 | 在真实消费者 Context 覆盖 provider 模型，并由独立 PG 基线管理 token sequence / function / 四表 BEFORE INSERT OR UPDATE triggers 的安装、生命周期与所有写入路径 |
| 实际并发 | 两 store 都选中同一候选后到达有界 pre-UPDATE 屏障，arrivals=2，最终 owner=1；重复投递 Duplicate/Applied 仅一业务效果；checkpoint RetryScheduled/Applied 后较旧事件 IgnoredOutOfOrder，最终 version=3 / effect=1 | 接入实际 worker / transport 后验证重试与失败记录边界；不能把夹具代替所有业务竞争验收 |
| 保留与 UTC | 12 行 before/equal/after cutoff 矩阵通过；pending、有效 lease、未完成 Inbox 和 checkpoint 保留；PG 非零 offset 直接写被拒绝，明确 UTC 规范化成功 | 所有 UTC 调用方输入边界、业务 local timestamp 保留、微秒精度和全量保留策略仍按 WP2 / WP3 逐项核对 |
| 真 SQLSTATE 40001 | 实际 Serializable callback 败方发生 40001；当前固定包一般异常路径 RetryScheduled，败方无提交，竞争者值保留 | provider 错误分类、整个事务重试、幂等和耗尽策略属于 WP3；不把一次观察称为内置 PostgreSQL 分类或所有错误等价 |

限量 Platform 运行门禁已得到双库证据，支持保持当前固定包。完整模型、迁移、seed / maintenance writer 与业务验收仍未由本报告完成。

## 消费者覆盖方案与初始门禁清单

已验证方案在 `AddCp6TransactionalMessaging(schema)` 之后，仅对 PG 模型将四个既有 `RowVersion` 映射为 `bytea`，保留 concurrency token 与 generated-on-add-or-update 语义。在隔离数据库中为四表安装 `BEFORE INSERT OR UPDATE FOR EACH ROW` 触发器，每次赋予新的非空 8 字节 token。EF INSERT / UPDATE 实际回读了生成值，私有 setter 与当前包 API 保持。正式消费者覆盖、迁移和触发器生命周期属于后续任务；WP1 临时探针没有将这些对象安装到现有业务环境。

初始静态推断的依据保留：[EF generated values](https://learn.microsoft.com/en-us/ef/core/modeling/generated-properties) 明确 value-generation 标志未指定生成实现；[Npgsql native concurrency](https://www.npgsql.org/efcore/modeling/concurrency.html) 的 `xmin` 路径使用 `uint`，不能直接视为当前 `byte[]` API 的等价映射；[PostgreSQL RETURNING](https://www.postgresql.org/docs/18/dml-returning.html) 可返回触发器修改后的行。[EF ExecuteUpdate / ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete) 不与 tracker / SaveChanges 交互，自动并发检查也不适用；后续真实负对照进一步否决了 SaveChanges-only 应用 token 候选。[Npgsql 日期规则](https://www.npgsql.org/doc/types/datetime.html) 要求写入 `timestamptz` 的 DateTimeOffset 为 Offset=0，真实非零 offset 拒绝也已记录。

下面保留静态审计时的初始必测清单。已执行范围以最终真实报告为准；未逐项记录的 payload 冲突、全部精度/失败记录竞争、retention 删除事务回滚等不得由已有成功项外推为通过：

1. 检查最终模型、四表 DDL、`bytea` / UTC 时间类型、唯一键、token 生成与 SaveChanges 返回值；四种实体 INSERT 后 token 非空，四种 UPDATE 后 token 变化，SQL Server `rowversion` 行为保持。
2. Enqueue 与业务写入共同提交/共同回滚；分别走 claim → publish、retry → 再 claim、dead letter → requeue、入站处理 → checkpoint advance、失败 → dead letter → requeue 全部公共 API。
3. 两 worker 同时 claim，同一消息仅一个有效 owned lease；超时回收更新 token。旧 tracked context 在新 claim 后 publish / retry 必须产生并发冲突并保留新租约；旧 claim 直接调用完成接口必须被 owned-claim 检查拒绝。
4. 同 consumer/message 同 payload 并发投递只应用一次；不同 payload 返回冲突；同 aggregate 多版本并发保证 checkpoint 单调、不双重应用；将 PG Serializable `40001`、唯一键冲突及失败记录竞争的最终数据库状态和 disposition 记录下来。
5. replay 两上下文竞争、lease TTL / retry 到期、UTC 微秒边界、retention 到期前/等于/晚于 cutoff、活动租约保留、三项删除共同回滚；验证调用者非零 offset 的公开 replay 参数不会静默改变时间语义。

原静态审计执行边界：仅源码与缓存包签名/哈希核验；审计自身没有执行数据库探针、restore、编译或业务测试，没有触发/重跑 GitHub Actions 或变更工作流。后来独立探针的双库实测及历史失败均在 WP1-PROBE 另行记录；本次文档补记仅回读这些记录，没有重新执行探针。未来若实际消费者路径暴露包 API 约束，必须解决对应消费者实现或由 Platform 权威源交付新固定包，不能复制实现或用本次限量成功代替完整验收。
