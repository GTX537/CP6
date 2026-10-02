# DB-COMPAT-01 WP1：实际 Platform 0.10.2 依赖审计

审计日期：2026-10-02。CP6 基线：`685a5238`。本报告只覆盖实际消费的 `CP6.Platform.EntityFramework 0.10.2` 固定源码与缓存包身份；不使用本地旧 `CP6.Platform/` checkout，不修改 Platform 权威仓库、生产配置或数据库。

**结论：源码没有绑定 SQL Server 的 SQL 方言、列类型、过滤索引或 provider 依赖；阻塞点是四个数据库生成的 `byte[] RowVersion`。消费者模型覆盖加 PostgreSQL `bytea` 插入/更新触发器具有保持当前包 API 的静态可行性，但尚未取得真实 PostgreSQL 运行证据，Platform 门禁仍为 Pending。** 不能仅凭新增 Npgsql 引用、DDL 可生成或源码检查声明兼容，也不能在验证失败时复制包实现替代权威包。

后续执行补记：独立探针已在 SQL Server 使用实际签名包执行四实体与公开消息 API 多路径试验；见 [WP1-PROBE](WP1-PROBE.md) 和 [阶段记录](WP1-DECISIONS.md)。运行 `AssemblyInformationalVersion` 实际显示 `0.8.0-alpha.2+fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，这是 NuGet / lock 身份为 0.10.2 的签名包内既有程序集元数据，源码 SHA 一致。本报告下面“本次未编译 / 连接数据库”描述静态审计自身的执行范围，不覆盖后来独立探针；PG 仍未验证。

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
| UTC | [Outbox L36 / L54](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs#L36)、[Inbox L169 / L305](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L169)、[Retention L23](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6MessageRetentionService.cs#L23) 来自 `TimeProvider.GetUtcNow()`；持久化时间为 `DateTimeOffset` | 正常路径符合 Offset=0 写入 `timestamptz` 的方向；租约/保留期限比较及微秒精度仍需实测。公开 [RecordReplay L329-L338](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6TransactionalMessagingEntities.cs#L329-L338) 接受调用者时间且未规范化，要验证非零 offset 的拒绝行为 |
| 异常与事务 | [Inbox L73-L128](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L73-L128) 捕获 `DbUpdateException` / 一般异常，重读 Inbox 或记录可重试失败；Serializable 在 [L139](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L139)、[L280](https://github.com/GTX537/CP6.Platform/blob/fbcd21528078a04e5b53c42c5fdfebe6ffa9655f/src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs#L280) | 没有 SQL Server 错误号绑定，也没有 PostgreSQL SQLSTATE 分类或内部 serialization retry loop。重复投递、唯一键/checkpoint 竞争、`40001` 及失败记录事务的竞争必须实测；模型覆盖不能解决语义问题 |
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

## 消费者覆盖候选与运行门禁

候选方案在 `AddCp6TransactionalMessaging(schema)` 之后，仅对 PG 模型将四个既有 `RowVersion` 映射为 `bytea`，保留 concurrency token 与 generated-on-add-or-update 语义。在隔离数据库中为四表安装 `BEFORE INSERT OR UPDATE FOR EACH ROW` 触发器，每次赋予新的非空 token。EF 的 before/after save 行为及 INSERT / UPDATE `RETURNING` 必须实际检查，不能把初始化空数组持久化成所有行相同的 token。正式迁移和触发器生命周期属于后续迁移任务，WP1 临时探针不改变现有环境。

这一方向属于**推断待验证**：[EF generated values](https://learn.microsoft.com/en-us/ef/core/modeling/generated-properties) 明确 value-generation 标志未指定生成实现；[Npgsql native concurrency](https://www.npgsql.org/efcore/modeling/concurrency.html) 的 `xmin` 路径使用 `uint`，不能直接视为当前 `byte[]` API 的等价映射；[PostgreSQL RETURNING](https://www.postgresql.org/docs/18/dml-returning.html) 可返回触发器修改后的行。[EF ExecuteUpdate / ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete) 不与 tracker / SaveChanges 交互，自动并发检查也不适用，因此仅 SaveChanges interceptor 生成应用 token 不足以维护当前包全部写入路径。[Npgsql 日期规则](https://www.npgsql.org/doc/types/datetime.html) 要求写入 `timestamptz` 的 DateTimeOffset 为 Offset=0。

真实 PG 探针使用实际签名 `0.10.2` 包、EF Core `8.0.30` 和已选固定 Npgsql 8.x provider，保留 SQL Server 对照。必测场景：

1. 检查最终模型、四表 DDL、`bytea` / UTC 时间类型、唯一键、token 生成与 SaveChanges 返回值；四种实体 INSERT 后 token 非空，四种 UPDATE 后 token 变化，SQL Server `rowversion` 行为保持。
2. Enqueue 与业务写入共同提交/共同回滚；分别走 claim → publish、retry → 再 claim、dead letter → requeue、入站处理 → checkpoint advance、失败 → dead letter → requeue 全部公共 API。
3. 两 worker 同时 claim，同一消息仅一个有效 owned lease；超时回收更新 token。旧 tracked context 在新 claim 后 publish / retry 必须产生并发冲突并保留新租约；旧 claim 直接调用完成接口必须被 owned-claim 检查拒绝。
4. 同 consumer/message 同 payload 并发投递只应用一次；不同 payload 返回冲突；同 aggregate 多版本并发保证 checkpoint 单调、不双重应用；将 PG Serializable `40001`、唯一键冲突及失败记录竞争的最终数据库状态和 disposition 记录下来。
5. replay 两上下文竞争、lease TTL / retry 到期、UTC 微秒边界、retention 到期前/等于/晚于 cutoff、活动租约保留、三项删除共同回滚；验证调用者非零 offset 的公开 replay 参数不会静默改变时间语义。

截至本报告写入：完成静态审计与缓存包签名/哈希核验；没有执行数据库探针、restore、编译或业务测试，没有触发/重跑 GitHub Actions，没有变更工作流。真实 PG / SQL Server 探针结果由 WP1 独立证据记录后才能更新门禁。如果实际包在生成值回读、并发、事务或错误语义上失败，必须解决对应消费者实现或在 Platform 权威源交付新固定包，不能将本报告的静态候选结论当作已通过。
