# DB-COMPAT-01 WP2 映射与迁移语义检查清单

日期：2026-10-02。状态：**Pending；提前准备，只读盘点。WP1 冻结并通过出口前，不开始 WP2 实体、模型或 PostgreSQL 全量基线修改。** 本报告依据已接受[设计](../../superpowers/specs/2026-10-02-database-compatibility-design.md)，读取 WP1 worktree 中的当前四 Context、四 snapshot、相关实体/写入源码和历史迁移；没有编译、restore、数据库连接或 Platform 旧 checkout 操作。

本清单是后续映射/迁移验收的输入，不是兼容性证明。SQL Server 历史迁移保持原样。PostgreSQL 独立基线须从完整当前模型加历史必要对象、种子和数据规则建立，不能把当前 snapshot 当作最终数据库对象全集，也不能机械转换 187 个 T-SQL 迁移。

## 口径与文件入口

| 简称 | 当前模型源码 | 当前 SQL Server snapshot | 限定统计 |
| --- | --- | --- | --- |
| C | [CP6Context](../../../CP6.Core/EFDbContext/CP6Context.cs) | [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs) | 显式列类型 20 次、HasFilter 26 次、check 6 次、DateTime 658 / DateTimeOffset 16 |
| S | [SpaceContext](../../../CP6.Space.Infrastructure/SpaceContext.cs) | [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs) | 显式列类型 299 次、HasFilter 69 次、check 120 次、DateTime 237 / DateTimeOffset 9 |
| I | [IdentityMessagingContext](../../../CP6.Core/Services/CrmIdentity/IdentityMessagingContext.cs) | [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs) | 源码以上三类 0 次；通过实际 Platform 包配置 4 消息实体；DateTime 0 / DateTimeOffset 10 |
| E | [ErpIntegrationContext](../../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs) | [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs) | 源码显式类型/filter/check 0 次；DateTime 0 / DateTimeOffset 17；另包含 ReplayAudit 配置 |

计数为语法/映射条目，不是数据库现场数量。95 个 HasFilter 调用包括 C:698 的一个 null，不是 95 条非空 predicate；源码换行调用已计入。四 snapshot 合计 DateTime 895、DateTimeOffset 52；它们没有任何 HasDefaultValueSql，模型源码也没有该调用，但历史 raw SQL 仍留下默认约束。实体注解、provider 推导的 SQL Server 类型由 snapshot 完整类型目录补充，不能只看上表 319 个显式 Fluent 调用。

## 必须保留的业务规则

| 类别 | 精确源码证据 | WP2 / WP3 检查要求（全部 Pending） |
| --- | --- | --- |
| Sys_Lang 全局 NULL 唯一 | [C:691](../../../CP6.Core/EFDbContext/CP6Context.cs#L691)–698：unique(TenantId,LangKey) 且 HasFilter(null)；[原去重迁移:13](../../../CP6.Core/Migrations/20260614065230_I18nP1_SysLangUniqueKey.cs#L13) 保留最小 Id | 同 LangKey 最多一条 TenantId=null 的全局记录，同时每租户一条覆盖。PG 普通 nullable UNIQUE 不足；按 WP1 冻结选择 NULLS NOT DISTINCT 或成对 partial unique。不能保留默认 NULL distinct，也不能只创建 tenant 非空索引。旧去重只按 LangKey 的历史上下文不得重用来删除现有租户覆盖 |
| 租户复合唯一与例外 | [C:2568](../../../CP6.Core/EFDbContext/CP6Context.cs#L2568)–2609：补 TenantId、保持名称/filter，跳过 FK principal key 依赖与全局 RefreshToken.TokenHash | PG 模型后处理必须保留原 filter 与租户前缀，不能把所有唯一索引一律改写；FK 依赖编码仍有现有全局唯一限制，不能顺便改变关系；refresh hash 保持跨租户全局唯一 |
| Space 活动模型/发布 slot | [S:457](../../../CP6.Space.Infrastructure/SpaceContext.cs#L457)–467、[S:1218](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1218)–1221 | 软删除历史允许保留；每租户/site 最多一个活动模型和一个 owns-publish-slot。布尔 predicate 改写只作用于 bool，不能把 numeric Status/BindingMode 转 boolean |
| Space 绑定、文件、任务与复用 | [S:931](../../../CP6.Space.Infrastructure/SpaceContext.cs#L931)、[S:1010](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1010)–1029、[S:3018](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3018)、[S:3213](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3213)、[S:3395](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3395) | nullable LocationCode 只约束非空活动值；external binding 的主绑定仅 BindingMode=0；文件哈希可复用只 State∈1/2/3；job 活动只 Status∈0/1；validation 可复用为 Status≠4。不能把这些 predicate 简化为一律 !IsDeleted |
| 基线与 supersede | [S:5971](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5971)、[S:6118](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6118) | planning comparison 只限制 IsBaseline=true 的 baseline；supersede 只约束非空引用。保留索引 key 和 FK 自指限制；不能凭名称自动套软删除过滤 |
| BIN2 身份 / 幂等 | [C:624](../../../CP6.Core/EFDbContext/CP6Context.cs#L624)、[C:635](../../../CP6.Core/EFDbContext/CP6Context.cs#L635)–637；[E:27](../../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs#L27)、[E:54](../../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs#L54)、[E:63](../../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs#L63)；[ErpDeliveryReplayAudit:27](../../../CP6.Core/Services/ErpIntegration/ErpDeliveryReplayAudit.cs#L27)–28 | Snapshot AggregateId、issuer/jti/client、ERP message/order/target 按既有精确规则查询和唯一。不得把 BIN2 字符串原样交 PG，或假定 database default / C collation 全部等价。覆盖 ASCII 大小写、Unicode/组合字符、尾空格、lookup/unique/order；业务普通编码是否大小写敏感另立明确合同 |
| hash / JSON 文本 | [S:450](../../../CP6.Space.Infrastructure/SpaceContext.cs#L450)–454 char(64)；[S:1331](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1331) 与 [S:5093](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5093)：LEN、hex pattern、ISJSON；[C:2262](../../../CP6.Core/EFDbContext/CP6Context.cs#L2262) | 保留内容摘要的字节合同；char(64)/varchar/text 的填充、比较、pattern、长度不同，不能自动改 jsonb 并重排原始 JSON。验证 63/64/65 字符、大小写 hex、末尾空格、非法 JSON、NULL、Unicode；逐条表达实际 check 三值逻辑和异常行为，不用一个泛化 regex 覆盖全部约束 |
| 不可变财务与审计 | [财务触发器:17](../../../CP6.Core/Migrations/20260615153849_FinJournalLineNoMutateTrigger.cs#L17)–34；[E:73](../../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs#L73)–82 | 已过账/红冲 Status∈2/4 的 Fin_JournalLine UPDATE/DELETE 必须数据库层拒绝，INSERT 不受此限制；不能只保留应用 GuardAudit 代替 DB 保护。验证 direct SQL/批量路径及整个事务回滚 |
| 模型外对象 | [OIDC grant migration:12](../../../CP6.Core/Migrations/20260908010000_CrmOidcGrantStore.cs#L12)–40 | CrmOidcGrant / CrmOidcLogout 不在当前 C-snapshot 中，需独立登记完整表、BIN2 字段、CodeHash 全局唯一、UTC expiry、index 和消费者 SQL。Sys_BrowserSessions 已在 snapshot，但 [browser raw migration:12](../../../CP6.Core/Migrations/20260908011000_CrmOidcBrowserSessionFamily.cs#L12) 的 AuthenticationEpoch 零 Guid 默认没有被 [C-snapshot:12716](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L12716) 描述，须判断保留及正式映射 |

## 时间：哪些有证据，哪些不能改成 UTC

| 范围 / 分类 | 源码证据 | 映射与验收要求 |
| --- | --- | --- |
| Space 基类审计 UTC（有写入证据） | [S:6804](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6804)–6827 检查 Clock.Kind==Utc，Added/Modified 盖章 CreatedAtUtc/ModifiedAtUtc | PG 可按冻结 UTC 合同使用 timestamptz；覆盖读取 Kind、未跟踪/原生 writer、微秒损失。该证据不自动覆盖每一个其他 DateTime 属性 |
| Core SpaceAuditEvent UTC（有限证据） | [C:2434](../../../CP6.Core/EFDbContext/CP6Context.cs#L2434)–2436 读回 SpecifyKindUtc；[SpaceUnderlayHistory:305](../../../CP6.Space.Infrastructure/SpaceUnderlayHistory.cs#L305)–315 数据库 SYSUTCDATETIME 再标 UTC | 读回标 Kind 不进行时区转换，不能作为历史任意值是 UTC 的证据；逐个核对 writer。数据库 UTC 函数与 provider 实现后续独立替换 |
| Platform / 身份 / ERP 消息 UTC（有来源） | [Platform 审计](WP1-PLATFORM.md)；[IdentitySnapshotWriter:132](../../../CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs#L132)、[ErpRequestHandler:117](../../../CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs#L117)、[ErpInboxReplayService:64](../../../CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs#L64)–73 由 GetUtcNow 写入 | DateTimeOffset 只以 Offset=0 写 timestamptz，原 API UTC ISO 文本/nullable 时间/lease TTL 保持。52 个 snapshot DateTimeOffset 不等于所有调用方已验证 |
| Core 本地 wall-clock（有来源，必须保留） | [BaseEntity:27](../../../CP6.Entity/BaseEntity.cs#L27) CreateDate=DateTime.Now；[C:2709](../../../CP6.Core/EFDbContext/CP6Context.cs#L2709) 字段审计 ChangedAt=DateTime.Now；[InboundReceipt:33](../../../CP6.Entity/DomainModels/Wms/InboundReceipt.cs#L33) ReceiveDateTime=Now；[StockTransaction:27](../../../CP6.Entity/DomainModels/Wms/StockTransaction.cs#L27) TxnDateTime=Now | 未批准改变业务时区前，按 wall-clock 保留 timestamp without time zone；将 Local/Unspecified 的 Kind 写入处理明确到边界，不能给值直接 SpecifyKindUtc 或根据今天机器时区改历史。记录部署业务时区来源和后续比较行为 |
| 业务日历值（有来源，必须保留日期） | [EstimateCalc:39](../../../CP6.Entity/DomainModels/Erp/EstimateCalc.cs#L39) QtnDate=Today；[CreditNote:19](../../../CP6.Entity/DomainModels/Erp/CreditNote.cs#L19) IssueDate=Today；[QualityInspection:28](../../../CP6.Entity/DomainModels/Mes/QualityInspection.cs#L28) InspectionDate=Today；[OutboundOrder:41](../../../CP6.Entity/DomainModels/Wms/OutboundOrder.cs#L41) PlannedDate=Today | 保留业务日历日/原 localtimestamp 合同；不能使跨 UTC 午夜读回前/后一天。改 CLR 为 DateOnly 或改数据库 date 必须另评外部合同，不作为机械 PG 转换 |
| 已有 DateOnly（明确） | [C-snapshot:10641](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L10641) ScheduledDate；[S-snapshot:59](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L59) PeriodDay | date 保留；AI Budget PeriodMonth=YEAR(PeriodDay)*100+MONTH(PeriodDay) 的 check 一同重写，不能将日历日引入时区 |
| T_IntegrationEvent 混合历史（明确不可整列转 UTC） | [迁移:20](../../../CP6.Core/Migrations/20260725203000_SpaceIntegrationEventOccurredAtUtc.cs#L20)–32 仅回填新 JobId 形状；[Normalizer:59](../../../CP6.Core/Services/Space/Observability/SpaceIntegrationEventUtcNormalizer.cs#L59)–91 区分 Utc/Local/新 UTC ticks/legacy zone；[Normalizer:102](../../../CP6.Core/Services/Space/Observability/SpaceIntegrationEventUtcNormalizer.cs#L102)–127 保留 DST gap/ambiguous 规则 | 源 CreateDate 与新增 OccurredAtUtc 不同合同；旧值须按显式 legacyTimeZone 和已有 resolution 算法规范化，保留边界饱和值。不能迁移时整列假设 UTC、用会话 TimeZone 隐式转换或省略 DST 用例 |
| 未审全部业务时间（待逐属性确认） | 四 snapshot DateTime 895 个映射；单凭名称 Utc/datetime2、nullable 或 MinValue literal 不能判断 | WP2 出口须产生 property→writer→business-local/UTC/date 清单及真实写读证据。未确认项保留原 wall-clock，禁止全部套 UTC converter；不得把本次代表性证据标为 895 属性全部完成 |
| MES 日桶 / 历史默认（有 local 来源） | [MES proc:48](../../../CP6.Core/Migrations/20260518115050_AddMesStoredProcedures.cs#L48)、[MES proc:101](../../../CP6.Core/Migrations/20260518115050_AddMesStoredProcedures.cs#L101) GETDATE 日桶；[Restore raw DDL:43](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L43) GETDATE 默认 | 业务日桶和 CreateDate local 默认不得直接替换为 UTC 日桶；explicit time zone 和 timestamp 类型须联动，午夜/DST/同事务时间函数合同单独验证 |

## 当前类型、默认值与命名决策

类别建议：**T**＝provider 类型映射，**U**＝UTC/local/date 合同，**P**＝索引 predicate，**K**＝check 表达式/三值逻辑，**C**＝collation/文本比较，**N**＝名称，**H**＝历史对象/补数据/种子。所有类别都为 Pending。

- T：uniqueidentifier→uuid、bit→boolean、整数对应类型、decimal(p,s)→numeric(p,s) 是候选映射；完整 precision/scale 目录见后表，金额/数量边界与舍入不得变化。float 保留双精度方向并实测，不可改 decimal。smallint 上的 enum 数值不可当 bool 重写。rowversion 208 映射遵循 WP1 冻结的 8 字节 opaque token 合同，InputRowVersion 的 varbinary(8) 另外保留长度约束。
- T/C：nvarchar/varchar 的长度、Unicode、IsUnicode(false) 与 SQL Server code page 不自动等同 PostgreSQL字符数；byte[] payload/binary 审计用 bytea 保留原字节。char(64) hash / char(3) currency 要分别验证 padding、case、长度和 pattern；不能为了统一 text 而省略业务约束。
- 当前模型 HasDefaultValueSql 为 0；C-snapshot 的显式常量默认为 JPY、1m、PENDING、100（L1750/L1767/L18996/L19755），S-snapshot 为 IsDeleted=false、EvidenceJson={}、short 0（例如 L51/L4578/L4815/L4845）。Literal 值与 sentinel 行为保留；不是证明现场没有旧默认约束。
- H/U：Restore raw DDL 的 GETDATE、OIDC raw DEFAULT zero-Guid、恢复迁移的 DateTime.MinValue sentinel、RequestJson 默认都须记录最后是否继续需要。PostgreSQL infinity 与 .NET Min/Max 值、100ns→µs 截断不能隐式改变排序、到期或 evidence。
- N：SQL Server snapshot 使用的 128 名称范围不能直接视为 PG 63-byte 能力。下表列出 explicit 与按默认索引命名规则推导的超长候选；后续须验证生成后及实际 catalog 的所有表/PK/FK/AK/index/check/history 名称、截断碰撞和 raw SQL 引用，按四 context/schema 固定可追溯短名。推导项不是数据库现场读取，也不声称所有 FK 名称已完整计算。

供应商规则用于说明待验证差异：[PostgreSQL 字符类型](https://www.postgresql.org/docs/18/datatype-character.html) 区分 char padding 与 varchar/text 的尾空格；[SQL Server LEN](https://learn.microsoft.com/en-us/sql/t-sql/functions/len-transact-sql?view=sql-server-ver16) 忽略尾空格；[SQL Server BIN2](https://learn.microsoft.com/en-us/sql/relational-databases/collations/collation-and-unicode-support?view=sql-server-ver16) 使用 binary-code-point 比较；[PostgreSQL constraints](https://www.postgresql.org/docs/18/ddl-constraints.html) 提供 NULLS NOT DISTINCT / partial unique 的语义选择。以上不证明默认数据库 collation、实际数据或 SQL Server/Pg 尾空格行为已经对照通过。

## 所有当前显式 Fluent 列类型调用

每行列出同一种字面类型的全部源调用行，补充后续属性映射检查；实体注解与推导类型另见 snapshot 目录。

| 源码 | 类型 / 次数 | 全部行号 | 类别 |
| --- | --- | --- | --- |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2236) | `nvarchar(max)` × 15 | 2236, 2250, 2269, 2270, 2300, 2350, 2357, 2363, 2371, 2433, 2464, 2524, 2536, 2537, 2548 | T/C |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2262) | `char(64)` × 4 | 2262, 2266, 2297, 2544 | T/C |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2550) | `datetime2` × 1 | 2550 | T/U |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L446) | `smallint` × 85 | 446, 449, 524, 527, 531, 720, 763, 926, 929, 1005, 1200, 1201, 1254, 1282, 1283, 1311, 1312, 1341, 1342, 1343, 1390, 1525, 1633, 1638, 1641, 1703, 1715, 1767, 1773, 1893, 1900, 1903, 1906, 1916, 1999, 2013, 2079, 2089, 2147, 2154, 2207, 2210, 2224, 2361, 2367, 2419, 2431, 2497, 2955, 3003, 3009, 3053, 3067, 3115, 3183, 3186, 3201, 3208, 3260, 3271, 3312, 3377, 3468, 3481, 3484, 3666, 3669, 3862, 3865, 3937, 4019, 4103, 4204, 4343, 4503, 4515, 4575, 4582, 4647, 4850, 4853, 4890, 4897, 5482, 5485 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L451) | `char(64)` × 42 | 451, 534, 539, 544, 549, 1518, 1650, 1921, 2217, 2318, 2424, 2624, 2630, 2635, 2997, 3056, 3188, 3194, 3262, 3315, 3362, 3370, 3610, 3616, 3689, 3696, 4105, 4450, 4462, 4968, 5042, 5136, 5239, 5286, 5383, 5389, 5470, 5476, 5610, 5825, 5936, 6098 | T/C |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L597) | `nvarchar(max)` × 48 | 597, 721, 761, 762, 1151, 1211, 1256, 1257, 1348, 1491, 2212, 2315, 2421, 2494, 2639, 2686, 2687, 2688, 2794, 2802, 3064, 3204, 3205, 3268, 3313, 3471, 3476, 3622, 3703, 3855, 3857, 3940, 3942, 4473, 4965, 5039, 5118, 5121, 5124, 5127, 5130, 5133, 5221, 5224, 5227, 5230, 5233, 5236 | T/C |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L599) | `decimal(18,8)` × 3 | 599, 667, 3063 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L600) | `decimal(9,4)` × 4 | 600, 668, 807, 2498 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L660) | `decimal(18,6)` × 6 | 660, 661, 662, 663, 664, 665 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L670) | `decimal(18,4)` × 4 | 670, 672, 880, 923 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1204) | `datetime2` × 70 | 1204, 1205, 1206, 1207, 1212, 1213, 1258, 1288, 1344, 1397, 1522, 1527, 1647, 1648, 1710, 1711, 1716, 1717, 1918, 1919, 2008, 2009, 2015, 2017, 2091, 2092, 2155, 2225, 2368, 2432, 2502, 2640, 2716, 2718, 2719, 2763, 2811, 2852, 2899, 2900, 3010, 3011, 3012, 3378, 3379, 3380, 3487, 3624, 3625, 3683, 3685, 3687, 3701, 3705, 3707, 3867, 4206, 4208, 4278, 4345, 4431, 4577, 4579, 4584, 4648, 4649, 5108, 5211, 6177, 6178 | T/U |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1642) | `decimal(18,3)` × 16 | 1642, 1643, 1644, 1646, 1705, 1706, 1707, 1709, 1907, 1908, 1909, 1911, 2003, 2004, 2005, 2007 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3859) | `decimal(6,5)` × 2 | 3859, 4020 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4198) | `char(3)` × 5 | 4198, 4337, 4427, 5597, 5832 | T/C |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4335) | `date` × 1 | 4335 | T/U |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4517) | `bigint` × 4 | 4517, 4586, 4650, 4854 | T |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5376) | `datetimeoffset(7)` × 9 | 5376, 5378, 5380, 5487, 5489, 5491, 5493, 5838, 5840 | T/U |

## 四 snapshot 的完整类型字面量目录

该目录覆盖当前 snapshot 的所有 HasColumnType 字面量（不是所有现场列）；每类包含全部不同长度/precision/scale，首次出现行可回查。类型归组不会授权统一变更语义。

| Snapshot | 类型家族 | 全部具体类型（次数） | 首次源行 |
| --- | --- | --- | --- |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L29) | uniqueidentifier | `uniqueidentifier`(658) | 29 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L32) | datetime2 | `datetime2`(658) | 32 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L36) | nvarchar | `nvarchar(100)`(664), `nvarchar(3)`(16), `nvarchar(10)`(194), `nvarchar(20)`(432), `nvarchar(40)`(42), `nvarchar(4)`(124), `nvarchar(50)`(80), `nvarchar(200)`(68), `nvarchar(8)`(4), `nvarchar(9)`(1), `nvarchar(13)`(3), `nvarchar(2)`(9), `nvarchar(30)`(98), `nvarchar(5)`(1), `nvarchar(500)`(88), `nvarchar(32)`(17), `nvarchar(6)`(5), `nvarchar(15)`(28), `nvarchar(1)`(26), `nvarchar(64)`(29), `nvarchar(16)`(3), `nvarchar(11)`(3), `nvarchar(300)`(7), `nvarchar(7)`(2), `nvarchar(255)`(1), `nvarchar(max)`(48), `nvarchar(128)`(40), `nvarchar(2000)`(6), `nvarchar(80)`(6), `nvarchar(160)`(1), `nvarchar(120)`(2), `nvarchar(260)`(1), `nvarchar(60)`(3), `nvarchar(256)`(6), `nvarchar(900)`(1), `nvarchar(1000)`(8), `nvarchar(25)`(44), `nvarchar(2048)`(1), `nvarchar(512)`(3), `nvarchar(4000)`(1), `nvarchar(250)`(1), `nvarchar(36)`(1), `nvarchar(249)`(2) | 36, 41, 70, 137, 273, 309, 355, 425, 541, 549, 557, 561, 569, 612, 1000, 1077, 1179, 1245, 1310, 1740, 1837, 1972, 2979, 5274, 5856, 5859, 5965, 7609, 9220, 9297, 9306, 9358, 9984, 10917, 11841, 13445, 14802, 14923, 17114, 17863, 18896, 19499, 20437 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L44) | int | `int`(368) | 44 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L85) | decimal | `decimal(10,4)`(3), `decimal(18,4)`(32), `decimal(18,2)`(74), `decimal(10,2)`(32), `decimal(21,8)`(177), `decimal(15,4)`(16), `decimal(12,4)`(1), `decimal(18,6)`(8), `decimal(18,8)`(2), `decimal(12,2)`(1), `decimal(7,4)`(2), `decimal(9,6)`(7), `decimal(8,4)`(5), `decimal(6,5)`(4), `decimal(10,3)`(3), `decimal(5,2)`(2) | 85, 167, 438, 909, 996, 1051, 1120, 1671, 2305, 4540, 5317, 7375, 8050, 10651, 14816, 15662 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L88) | bit | `bit`(278) | 88 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L103) | rowversion | `rowversion`(157) | 103 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L4207) | datetimeoffset | `datetimeoffset`(16) | 4207 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L9368) | bigint | `bigint`(4) | 9368 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L10642) | date | `date`(1) | 10642 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L10831) | char | `char(64)`(6) | 10831 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L10913) | varchar | `varchar(64)`(6), `varchar(512)`(1), `varchar(36)`(1), `varchar(128)`(5), `varchar(32)`(1) | 10913, 11723, 11729, 20282, 20500 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L11183) | float | `float`(4) | 11183 |
| [C-snapshot](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L20514) | varbinary | `varbinary(max)`(1) | 20514 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L28) | uniqueidentifier | `uniqueidentifier`(609) | 28 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L31) | bigint | `bigint`(50) | 31 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L34) | datetime2 | `datetime2`(237) | 34 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L42) | char | `char(3)`(5), `char(64)`(72) | 42, 69 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L50) | bit | `bit`(106) | 50 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L60) | date | `date`(1) | 60 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L63) | int | `int`(114) | 63 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L79) | rowversion | `rowversion`(41) | 79 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L85) | smallint | `smallint`(95) | 85 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L124) | nvarchar | `nvarchar(max)`(59), `nvarchar(64)`(10), `nvarchar(128)`(5), `nvarchar(100)`(53), `nvarchar(200)`(50), `nvarchar(500)`(21), `nvarchar(50)`(13), `nvarchar(1000)`(9), `nvarchar(20)`(4), `nvarchar(4000)`(2), `nvarchar(260)`(2), `nvarchar(256)`(4), `nvarchar(1024)`(1), `nvarchar(32)`(2), `nvarchar(2000)`(4), `nvarchar(512)`(1), `nvarchar(300)`(2) | 124, 270, 275, 334, 372, 389, 448, 502, 1100, 1120, 3007, 3221, 3510, 3522, 5527, 6167, 6407 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L149) | varchar | `varchar(32)`(1), `varchar(64)`(4), `varchar(100)`(14), `varchar(500)`(2), `varchar(256)`(1), `varchar(50)`(5), `varchar(30)`(2), `varchar(200)`(1) | 149, 833, 838, 880, 961, 1636, 1695, 2536 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L762) | decimal | `decimal(18,9)`(1), `decimal(18,3)`(16), `decimal(9,4)`(8), `decimal(18,8)`(3), `decimal(6,5)`(2), `decimal(18,4)`(7), `decimal(28,6)`(23), `decimal(38,4)`(5), `decimal(19,6)`(3), `decimal(18,6)`(6) | 762, 1257, 2192, 3178, 3235, 4406, 5214, 5218, 5966, 7358 |
| [S-snapshot](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5202) | datetimeoffset | `datetimeoffset(7)`(9) | 5202 |
| [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L29) | uniqueidentifier | `uniqueidentifier`(8) | 29 |
| [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L33) | nvarchar | `nvarchar(128)`(15), `nvarchar(512)`(2), `nvarchar(249)`(2) | 33, 167, 200 |
| [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L36) | datetimeoffset | `datetimeoffset`(10) | 36 |
| [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L39) | int | `int`(8) | 39 |
| [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L45) | varchar | `varchar(128)`(5), `varchar(64)`(3), `varchar(32)`(1) | 45, 56, 263 |
| [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L70) | rowversion | `rowversion`(4) | 70 |
| [I-snapshot](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L277) | varbinary | `varbinary(max)`(1) | 277 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L28) | uniqueidentifier | `uniqueidentifier`(17) | 28 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L36) | nvarchar | `nvarchar(100)`(2), `nvarchar(128)`(13), `nvarchar(200)`(2), `nvarchar(max)`(1), `nvarchar(20)`(1), `nvarchar(512)`(1), `nvarchar(249)`(1) | 36, 51, 112, 273, 313, 475, 509 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L41) | varbinary | `varbinary(8)`(2), `varbinary(max)`(2) | 41, 121 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L47) | varchar | `varchar(16)`(1), `varchar(64)`(8), `varchar(128)`(8), `varchar(32)`(1) | 47, 58, 67, 465 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L61) | int | `int`(18) | 61 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L70) | datetimeoffset | `datetimeoffset`(17) | 70 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L150) | rowversion | `rowversion`(6) | 150 |
| [E-snapshot](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L290) | bit | `bit`(2) | 290 |

## 全部 95 个 HasFilter 调用

按相同字面 predicate 分组列出全部调用行；包括非唯一查询索引，因此不可将每行一律当 unique。PG 改写必须同时检查源索引 key、unique 标志、enum 类型和 soft-delete scope。

| 源码 | 原 predicate | 次数 / 全部行 | 类别 |
| --- | --- | --- | --- |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L655) | `[BadgeNo] IS NOT NULL` | 1: 655 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L688) | `[MenuKey] IS NOT NULL` | 1: 688 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L698) | `null` | 1: 698 | P/NULL unique |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L789) | `[SubmissionKey] IS NOT NULL` | 1: 789 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L799) | `[Status] = 0` | 2: 799, 836 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L806) | `[Enable] = 1` | 1: 806 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L816) | `[LegacyFlowInstanceId] IS NOT NULL` | 1: 816 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L827) | `[FunctionId] IS NOT NULL` | 1: 827 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L829) | `[FlowCode] IS NOT NULL` | 1: 829 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L857) | `[ParentTokenId] IS NOT NULL` | 1: 857 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L859) | `[BizType] IS NOT NULL AND [BizId] IS NOT NULL AND [Status] IN (0, 4)` | 1: 859 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L915) | `[EventKey] IS NOT NULL` | 1: 915 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L932) | `[Status] IN (0, 1)` | 1: 932 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L995) | `[Source] <> 0 AND [Status] = 2 AND [SourceDocNo] IS NOT NULL` | 1: 995 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L1033) | `[RunMode] IN (1,2,3) AND [Status] <> 2` | 1: 1033 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L1090) | `[SupplierInvoiceNo] IS NOT NULL AND [IsCreditMemo] = 0` | 1: 1090 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L1155) | `[ShipmentId] IS NOT NULL AND [IsCreditMemo] = 0` | 1: 1155 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L1505) | `[CrmOpportunityId] IS NOT NULL` | 1: 1505 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L1593) | `[CrmAccountId] IS NOT NULL` | 1: 1593 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2176) | `[CompletionOperationId] IS NOT NULL` | 1: 2176 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2190) | `[IsDeleted] = 0 AND [Status] = 'PENDING'` | 1: 2190 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2258) | `[IsDeleted] = 0 AND [Status] = 'PendingApproval'` | 1: 2258 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2332) | `[SerialNo] IS NOT NULL` | 1: 2332 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2497) | `[LocationCode] IS NOT NULL` | 1: 2497 | P |
| [C](../../../CP6.Core/EFDbContext/CP6Context.cs#L2523) | `[ScheduledDate] IS NOT NULL` | 1: 2523 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L459) | `[IsDeleted] = 0` | 37: 459, 604, 734, 774, 818, 891, 1017, 1109, 1538, 2165, 2283, 2325, 2377, 2587, 3081, 3124, 3636, 3747, 3879, 3902, 4033, 4113, 4118, 4123, 4218, 4238, 4527, 4712, 4740, 4773, 4816, 4863, 4912, 4938, 4975, 5012, 5049 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L463) | `[ActiveDraftVersionId] IS NOT NULL AND [IsDeleted] = 0` | 1: 463 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L467) | `[CurrentPublishedVersionId] IS NOT NULL AND [IsDeleted] = 0` | 1: 467 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L562) | `[BasedOnVersionId] IS NOT NULL` | 1: 562 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L571) | `[CloneOperationId] IS NOT NULL` | 1: 571 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L933) | `[LocationCode] IS NOT NULL AND [IsDeleted] = 0` | 1: 933 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L945) | `[RackLogicalId] IS NOT NULL AND [IsDeleted] = 0` | 1: 945 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1027) | `[BindingMode] = 0 AND [IsDeleted] = 0` | 1: 1027 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1220) | `[OwnsPublishSlot] = 1 AND [IsDeleted] = 0` | 1: 1220 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1413) | `[PublishAttemptId] IS NOT NULL` | 1: 1413 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1549) | `[ExternalLocationId] IS NOT NULL AND [IsDeleted] = 0` | 1: 1549 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1561) | `[LocationLogicalId] IS NOT NULL AND [IsDeleted] = 0` | 1: 1561 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1954) | `[AlarmExternalId] IS NOT NULL` | 1: 1954 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2867) | `[IsCurrent] = 1 AND [IsDeleted] = 0` | 2: 2867, 3712 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3020) | `[Sha256] IS NOT NULL AND [State] IN (1, 2, 3) AND [IsDeleted] = 0` | 1: 3020 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3025) | `[RetainUntilUtc] IS NOT NULL AND [IsDeleted] = 0` | 1: 3025 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3034) | `[State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [ContentDeletedAtUtc] IS NULL` | 1: 3034 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3084) | `[FileId] IS NOT NULL AND [IsDeleted] = 0` | 1: 3084 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3121) | `[JobId] IS NOT NULL AND [IsDeleted] = 0` | 2: 3121, 3500 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3127) | `[SourceId] IS NOT NULL AND [IsDeleted] = 0` | 1: 3127 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3215) | `[Status] IN (0, 1) AND [IsDeleted] = 0` | 1: 3215 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3396) | `[Status] <> 4 AND [IsDeleted] = 0` | 1: 3396 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3511) | `[ValidationRunId] IS NOT NULL AND [IsDeleted] = 0` | 1: 3511 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3524) | `[GenerationRunId] IS NOT NULL AND [IsDeleted] = 0` | 2: 3524, 3535 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4283) | `[RunId] IS NOT NULL` | 1: 4283 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4439) | `[IsActive] = 1 AND [IsDeleted] = 0` | 1: 4439 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4538) | `[BusinessPartnerId] IS NOT NULL AND [IsDeleted] = 0` | 1: 4538 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4596) | `[Status] <> 3 AND [IsDeleted] = 0` | 1: 4596 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5971) | `[IsBaseline] = 1` | 1: 5971 | P |
| [S](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6118) | `[SupersedesDecisionId] IS NOT NULL` | 1: 6118 | P |

## 全部 126 个当前 check 配置

逐条源约束列入清单；表达式全文以链接处源码为准，不把下列分类替换成统一正则或通用转换。K 项都包含 identifier quoting 和 NULL 三值逻辑核对；JSON 需保留原文本/hash；length/pattern 需独立对照 LEN/LIKE/case/尾空格；boolean 只改 bool 字段；calendar 保留日历日期。

| 源码行 | 约束名 | 表达式检查类别 | 状态 |
| --- | --- | --- | --- |
| [C:2277](../../../CP6.Core/EFDbContext/CP6Context.cs#L2277) | `CK_SpaceDispatchExecutionAction_Type` | K; enum/range/key | Pending |
| [C:2280](../../../CP6.Core/EFDbContext/CP6Context.cs#L2280) | `CK_SpaceDispatchExecutionAction_Status` | K; enum/range/key | Pending |
| [C:2420](../../../CP6.Core/EFDbContext/CP6Context.cs#L2420) | `CK_Space_AuditEvent_Tenant` | K; enum/range/key | Pending |
| [C:2423](../../../CP6.Core/EFDbContext/CP6Context.cs#L2423) | `CK_Space_AuditEvent_Correlation` | K; enum/range/key | Pending |
| [C:2426](../../../CP6.Core/EFDbContext/CP6Context.cs#L2426) | `CK_Space_AuditEvent_ActorType` | K; enum/range/key | Pending |
| [C:2429](../../../CP6.Core/EFDbContext/CP6Context.cs#L2429) | `CK_Space_AuditEvent_Outcome` | K; enum/range/key | Pending |
| [S:493](../../../CP6.Space.Infrastructure/SpaceContext.cs#L493) | `CK_Space_ModelVersion_Purpose` | K; null/shape | Pending |
| [S:499](../../../CP6.Space.Infrastructure/SpaceContext.cs#L499) | `CK_Space_ModelVersion_CreationSource` | K; null/shape | Pending |
| [S:801](../../../CP6.Space.Infrastructure/SpaceContext.cs#L801) | `CK_Space_RackRevision_Geometry` | K; enum/range/key | Pending |
| [S:877](../../../CP6.Space.Infrastructure/SpaceContext.cs#L877) | `CK_Space_RackLevelRevision_Dimensions` | K; null/shape | Pending |
| [S:918](../../../CP6.Space.Infrastructure/SpaceContext.cs#L918) | `CK_Space_LocationRevision_Dimensions` | K; null/shape | Pending |
| [S:986](../../../CP6.Space.Infrastructure/SpaceContext.cs#L986) | `CK_Space_LocationExternalBinding_Mode` | K; enum/range/key | Pending |
| [S:1080](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1080) | `CK_Space_DesignAttribute_ObjectType` | K; enum/range/key | Pending |
| [S:1185](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1185) | `CK_Space_PublishAttempt_Slot` | K; boolean, null/shape | Pending |
| [S:1188](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1188) | `CK_Space_PublishAttempt_Recovery` | K; JSON, null/shape | Pending |
| [S:1244](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1244) | `CK_Space_PublishBatch_Recovery` | K; JSON | Pending |
| [S:1331](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1331) | `CK_Space_PublishAuditEvent_Invariants` | K; JSON, length/pattern, null/shape | Pending |
| [S:1380](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1380) | `CK_Space_HistoricalRepublish_Status` | K; enum/range/key | Pending |
| [S:1594](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1594) | `CK_Space_PersonnelEvent_SourceSequence` | K; null/shape | Pending |
| [S:1597](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1597) | `CK_Space_PersonnelEvent_Accuracy` | K; null/shape | Pending |
| [S:1603](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1603) | `CK_Space_PersonnelEvent_SourceKind` | K; enum/range/key | Pending |
| [S:1606](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1606) | `CK_Space_PersonnelEvent_Kind` | K; enum/range/key | Pending |
| [S:1609](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1609) | `CK_Space_PersonnelEvent_WorkState` | K; null/shape | Pending |
| [S:1612](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1612) | `CK_Space_PersonnelEvent_Shape` | K; null/shape | Pending |
| [S:1687](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1687) | `CK_Space_PersonnelState_SourceKind` | K; enum/range/key | Pending |
| [S:1690](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1690) | `CK_Space_PersonnelState_WorkState` | K; enum/range/key | Pending |
| [S:1751](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1751) | `CK_Space_DeviceMapping_SourceKind` | K; enum/range/key | Pending |
| [S:1754](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1754) | `CK_Space_DeviceMapping_DeviceKind` | K; enum/range/key | Pending |
| [S:1836](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1836) | `CK_Space_DeviceEvent_SourceKind` | K; enum/range/key | Pending |
| [S:1839](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1839) | `CK_Space_DeviceEvent_DeviceKind` | K; enum/range/key | Pending |
| [S:1842](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1842) | `CK_Space_DeviceEvent_Kind` | K; enum/range/key | Pending |
| [S:1845](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1845) | `CK_Space_DeviceEvent_OperatingState` | K; null/shape | Pending |
| [S:1848](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1848) | `CK_Space_DeviceEvent_AlarmSeverity` | K; null/shape | Pending |
| [S:1851](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1851) | `CK_Space_DeviceEvent_SourceSequence` | K; null/shape | Pending |
| [S:1854](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1854) | `CK_Space_DeviceEvent_CoordinateTriple` | K; null/shape | Pending |
| [S:1858](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1858) | `CK_Space_DeviceEvent_Accuracy` | K; null/shape | Pending |
| [S:1862](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1862) | `CK_Space_DeviceEvent_Shape` | K; null/shape | Pending |
| [S:1975](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1975) | `CK_Space_DeviceState_SourceKind` | K; enum/range/key | Pending |
| [S:1978](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1978) | `CK_Space_DeviceState_OperatingState` | K; enum/range/key | Pending |
| [S:1981](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1981) | `CK_Space_DeviceState_CoordinateTriple` | K; null/shape | Pending |
| [S:1985](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1985) | `CK_Space_DeviceState_Accuracy` | K; null/shape | Pending |
| [S:2057](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2057) | `CK_Space_DeviceAlarmState_SourceKind` | K; enum/range/key | Pending |
| [S:2060](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2060) | `CK_Space_DeviceAlarmState_Severity` | K; null/shape | Pending |
| [S:2063](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2063) | `CK_Space_DeviceAlarmState_SourceSequence` | K; null/shape | Pending |
| [S:2066](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2066) | `CK_Space_DeviceAlarmState_ActiveShape` | K; boolean, null/shape | Pending |
| [S:2132](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2132) | `CK_Space_Asset_ScopeOwner` | K; enum/range/key | Pending |
| [S:2188](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2188) | `CK_Space_AssetVersion_ScopeOwner` | K; enum/range/key | Pending |
| [S:2192](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2192) | `CK_Space_AssetVersion_VersionNo` | K; enum/range/key | Pending |
| [S:2266](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2266) | `CK_Space_WarehouseTemplate_CurrentVersion` | K; enum/range/key | Pending |
| [S:2297](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2297) | `CK_Space_WarehouseTemplateVersion_VersionNo` | K; enum/range/key | Pending |
| [S:2300](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2300) | `CK_Space_WarehouseTemplateVersion_SchemaVersion` | K; enum/range/key | Pending |
| [S:2303](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2303) | `CK_Space_WarehouseTemplateVersion_Counts` | K; enum/range/key | Pending |
| [S:2347](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2347) | `CK_Space_RackGenerationProfile_ScopeOwner` | K; enum/range/key | Pending |
| [S:2394](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2394) | `CK_Space_RackGenerationProfileVersion_ScopeOwner` | K; enum/range/key | Pending |
| [S:2398](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2398) | `CK_Space_RackGenerationProfileVersion_VersionNo` | K; enum/range/key | Pending |
| [S:2401](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2401) | `CK_Space_RackGenerationProfileVersion_Dimensions` | K; enum/range/key | Pending |
| [S:2404](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2404) | `CK_Space_RackGenerationProfileVersion_LocationCount` | K; enum/range/key | Pending |
| [S:2475](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2475) | `CK_Space_ElementRevision_Geometry` | K; enum/range/key | Pending |
| [S:2478](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2478) | `CK_Space_ElementRevision_ModelAssetScope` | K; null/shape | Pending |
| [S:2484](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2484) | `CK_Space_ElementRevision_ManualCorrection` | K; boolean, null/shape | Pending |
| [S:2614](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2614) | `CK_Space_ElementCommandBatch_Result` | K; null/shape | Pending |
| [S:2901](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2901) | `CK_Space_CadProviderCertification_QualificationScore` | K; null/shape | Pending |
| [S:2982](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2982) | `CK_Space_File_ContentDeletion` | K; boolean, null/shape | Pending |
| [S:3165](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3165) | `CK_Space_Job_Attempts` | K; enum/range/key | Pending |
| [S:3168](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3168) | `CK_Space_Job_Progress` | K; enum/range/key | Pending |
| [S:3171](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3171) | `CK_Space_Job_Lease` | K; null/shape | Pending |
| [S:3248](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3248) | `CK_Space_JobAttempt_OutcomeTime` | K; null/shape | Pending |
| [S:3302](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3302) | `CK_Space_JobStep_StatusTime` | K; null/shape | Pending |
| [S:3344](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3344) | `CK_Space_ValidationRun_StatusTime` | K; null/shape | Pending |
| [S:3349](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3349) | `CK_Space_ValidationRun_Counts` | K; enum/range/key | Pending |
| [S:3439](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3439) | `CK_Space_ModelIssue_Context` | K; null/shape | Pending |
| [S:3442](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3442) | `CK_Space_ModelIssue_SourceVersion` | K; null/shape | Pending |
| [S:3445](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3445) | `CK_Space_ModelIssue_GenerationScope` | K; null/shape | Pending |
| [S:3449](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3449) | `CK_Space_ModelIssue_Resolution` | K; null/shape | Pending |
| [S:3457](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3457) | `CK_Space_ModelIssue_ValidationScope` | K; null/shape | Pending |
| [S:3652](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3652) | `CK_Space_GenerationRun_Progress` | K; enum/range/key | Pending |
| [S:3827](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3827) | `CK_Space_GenerationProposal_Confidence` | K; enum/range/key | Pending |
| [S:4003](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4003) | `CK_Space_GenerationLockedFact_Match` | K; enum/range/key | Pending |
| [S:4086](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4086) | `CK_Space_GenerationStagingElement_Validation` | K; null/shape | Pending |
| [S:4173](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4173) | `CK_Space_AiUsageRecord_Units` | K; enum/range/key | Pending |
| [S:4176](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4176) | `CK_Space_AiUsageRecord_Cost` | K; null/shape | Pending |
| [S:4180](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4180) | `CK_Space_AiUsageRecord_Latency` | K; enum/range/key | Pending |
| [S:4263](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4263) | `CK_Space_TenantAiWorkSlot_SlotNo` | K; enum/range/key | Pending |
| [S:4266](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4266) | `CK_Space_TenantAiWorkSlot_Lease` | K; null/shape | Pending |
| [S:4314](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4314) | `CK_Space_AiBudgetReservation_Cost` | K; null/shape | Pending |
| [S:4318](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4318) | `CK_Space_AiBudgetReservation_Period` | K; calendar | Pending |
| [S:4322](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4322) | `CK_Space_AiBudgetReservation_Currency` | K; null/shape | Pending |
| [S:4397](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4397) | `CK_Space_AiTenantPolicy_Version` | K; enum/range/key | Pending |
| [S:4400](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4400) | `CK_Space_AiTenantPolicy_Concurrency` | K; enum/range/key | Pending |
| [S:4403](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4403) | `CK_Space_AiTenantPolicy_Budget` | K; null/shape | Pending |
| [S:4409](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4409) | `CK_Space_AiTenantPolicy_Currency` | K; null/shape | Pending |
| [S:4484](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4484) | `CK_Space_ExternalOrganization_BusinessPartner` | K; null/shape | Pending |
| [S:4488](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4488) | `CK_Space_ExternalOrganization_Type` | K; enum/range/key | Pending |
| [S:4491](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4491) | `CK_Space_ExternalOrganization_Status` | K; enum/range/key | Pending |
| [S:4557](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4557) | `CK_Space_ExternalMembership_Validity` | K; null/shape | Pending |
| [S:4560](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4560) | `CK_Space_ExternalMembership_Role` | K; enum/range/key | Pending |
| [S:4563](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4563) | `CK_Space_ExternalMembership_Status` | K; enum/range/key | Pending |
| [S:4629](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4629) | `CK_Space_ExternalGrant_Status` | K; enum/range/key | Pending |
| [S:4632](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4632) | `CK_Space_ExternalGrant_Validity` | K; null/shape | Pending |
| [S:4635](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4635) | `CK_Space_ExternalGrant_Version` | K; enum/range/key | Pending |
| [S:4829](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4829) | `CK_Space_FieldPolicy_AudienceType` | K; enum/range/key | Pending |
| [S:4832](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4832) | `CK_Space_FieldPolicy_Status` | K; enum/range/key | Pending |
| [S:4835](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4835) | `CK_Space_FieldPolicy_Version` | K; enum/range/key | Pending |
| [S:4876](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4876) | `CK_Space_FieldPolicyField_ResourceType` | K; enum/range/key | Pending |
| [S:4879](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4879) | `CK_Space_FieldPolicyField_MaskingRule` | K; enum/range/key | Pending |
| [S:4923](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4923) | `CK_Space_LayerMappingProfile_CurrentVersion` | K; enum/range/key | Pending |
| [S:4951](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4951) | `CK_Space_LayerMappingProfileVersion_Version` | K; enum/range/key | Pending |
| [S:4954](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4954) | `CK_Space_LayerMappingProfileVersion_Base` | K; null/shape | Pending |
| [S:4997](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4997) | `CK_Space_ExcelMappingProfile_CurrentVersion` | K; enum/range/key | Pending |
| [S:5025](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5025) | `CK_Space_ExcelMappingProfileVersion_Version` | K; enum/range/key | Pending |
| [S:5028](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5028) | `CK_Space_ExcelMappingProfileVersion_Base` | K; null/shape | Pending |
| [S:5073](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5073) | `CK_Space_PutawayRecommendation_Counts` | K; boolean | Pending |
| [S:5084](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5084) | `CK_Space_PutawayRecommendation_Evidence` | K; JSON | Pending |
| [S:5093](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5093) | `CK_Space_PutawayRecommendation_Immutable` | K; length/pattern, boolean | Pending |
| [S:5168](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5168) | `CK_Space_DispatchRecommendation_Counts` | K; boolean | Pending |
| [S:5187](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5187) | `CK_Space_DispatchRecommendation_Evidence` | K; JSON | Pending |
| [S:5196](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5196) | `CK_Space_DispatchRecommendation_Immutable` | K; length/pattern, boolean | Pending |
| [S:5269](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5269) | `CK_Space_PlanningScenarioBranch_Immutable` | K; length/pattern, boolean | Pending |
| [S:5348](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5348) | `CK_Space_PlanningHistoricalDataset_Invariants` | K; length/pattern, boolean | Pending |
| [S:5450](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5450) | `CK_Space_PlanningHistoricalTask_Invariants` | K; length/pattern, boolean, null/shape | Pending |
| [S:5529](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5529) | `CK_Space_PlanningSimulationRun_Invariants` | K; length/pattern, boolean | Pending |
| [S:5717](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5717) | `CK_Space_PlanningSimulationLocationResult_Invariants` | K; boolean | Pending |
| [S:5791](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5791) | `CK_Space_PlanningComparison_Invariants` | K; length/pattern, boolean, null/shape | Pending |
| [S:5899](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5899) | `CK_Space_PlanningComparisonEntry_Invariants` | K; length/pattern, boolean | Pending |
| [S:6015](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6015) | `CK_Space_PlanningComparisonRisk_Invariants` | K; length/pattern, boolean | Pending |
| [S:6063](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6063) | `CK_Space_PlanningDecisionRecord_Invariants` | K; length/pattern, boolean, null/shape | Pending |

## 名称长度候选

先列 snapshot 中显式超 63-byte 名称（7），再列目前索引超长候选（含 36 个 explicit / convention 推导项；显式索引与前表有重复）。这里的 convention 名称是按 SQL Server 默认 IX_表_列 拼接的静态推导，后续须与迁移生成器及真实 PG catalog 核对，不能当现场结果。

| Snapshot 行 | 显式对象名 | UTF-8 bytes | 类别 |
| --- | --- | ---: | --- |
| [C-snapshot:2502](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L2502) | `IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd` | 77 | N |
| [C-snapshot:2743](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L2743) | `IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd` | 67 | N |
| [C-snapshot:2811](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L2811) | `IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd` | 71 | N |
| [S-snapshot:5661](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5661) | `AK_Space_PlanningHistoricalDataset_Tenant_Id_Branch_Model_Version` | 65 | N |
| [S-snapshot:6906](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L6906) | `UX_Space_RackGenerationProfileVersion_Scope_Owner_Profile_VersionNo` | 67 | N |
| [S-snapshot:8423](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L8423) | `FK_Space_FloorRevision_UnderlayCalibration_Tenant_Version_Floor_Source` | 70 | N |
| [S-snapshot:8667](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L8667) | `FK_Space_LocationExternalBinding_Location_Tenant_Version_Logical` | 64 | N |

| Snapshot index 配置行 | 索引候选名 | bytes | 来源 |
| --- | --- | ---: | --- |
| [C-snapshot:2500](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L2500) | `IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd` | 77 | explicit |
| [C-snapshot:2741](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L2741) | `IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd` | 67 | explicit |
| [C-snapshot:2809](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L2809) | `IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd` | 71 | explicit |
| [C-snapshot:7514](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L7514) | `IX_T_IntegrationEvent_TenantId_Status_DeadLetterNotifiedAtUtc_DeadLetterNotificationLeaseUntilUtc` | 97 | convention 推导 |
| [C-snapshot:7516](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L7516) | `IX_T_IntegrationEvent_TenantId_SourceModule_CorrelationId_OccurredAtUtc_Id` | 74 | convention 推导 |
| [C-snapshot:17058](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L17058) | `IX_T_MobileTaskReservation_WarehouseCd_FromLocationCd_ProductCd_LotNo_IsActive` | 78 | convention 推导 |
| [C-snapshot:18759](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L18759) | `IX_T_SpaceDispatchApprovalRequest_TenantId_SiteId_RecommendationId` | 66 | convention 推导 |
| [C-snapshot:18763](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L18763) | `IX_T_SpaceDispatchApprovalRequest_TenantId_SiteId_RequestedAtUtc` | 64 | convention 推导 |
| [C-snapshot:18851](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L18851) | `IX_T_SpaceDispatchExecutionAction_TenantId_ApprovalRequestId_ActionType` | 71 | convention 推导 |
| [C-snapshot:18853](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L18853) | `IX_T_SpaceDispatchExecutionAction_TenantId_ApprovalRequestId_RequestedAtUtc` | 75 | convention 推导 |
| [C-snapshot:20358](../../../CP6.Core/Migrations/CP6ContextModelSnapshot.cs#L20358) | `IX_Cp6_InboxAggregateCheckpoint_ConsumerName_TenantId_AggregateId` | 65 | convention 推导 |
| [S-snapshot:1599](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L1599) | `IX_Space_DeviceMapping_TenantId_ValidatedModelVersionId_ElementLogicalId` | 72 | convention 推导 |
| [S-snapshot:2227](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L2227) | `IX_Space_ElementRevision_ModelAssetScope_ModelAssetOwnerTenantId_ModelAssetId` | 77 | convention 推导 |
| [S-snapshot:2229](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L2229) | `IX_Space_ElementRevision_TenantId_ModelVersionId_ParentLogicalId` | 64 | convention 推导 |
| [S-snapshot:3199](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L3199) | `IX_Space_FloorRevision_TenantId_ModelVersionId_LogicalId_UnderlaySourceId_UnderlayCalibrationId` | 95 | convention 推导 |
| [S-snapshot:3287](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L3287) | `IX_Space_GenerationLockedFact_TenantId_BasedOnRunId_SourceProposalId` | 68 | convention 推导 |
| [S-snapshot:3635](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L3635) | `IX_Space_GenerationRun_TenantId_ModelVersionId_TargetFloorLogicalId` | 67 | convention 推导 |
| [S-snapshot:3723](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L3723) | `IX_Space_GenerationStagingElement_TenantId_ModelVersionId_FloorLogicalId` | 72 | convention 推导 |
| [S-snapshot:3852](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L3852) | `IX_Space_HistoricalRepublish_TenantId_ModelId_ExpectedPublishedVersionId` | 72 | convention 推导 |
| [S-snapshot:3854](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L3854) | `IX_Space_HistoricalRepublish_TenantId_ModelId_HistoricalVersionId` | 65 | convention 推导 |
| [S-snapshot:4338](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L4338) | `IX_Space_LocationExternalBinding_TenantId_ModelVersionId_SourceId` | 65 | convention 推导 |
| [S-snapshot:4443](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L4443) | `IX_Space_LocationRevision_TenantId_ModelVersionId_FloorLogicalId` | 64 | convention 推导 |
| [S-snapshot:5272](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5272) | `IX_Space_PlanningComparison_TenantId_ModelId_BasePublishedVersionId` | 67 | convention 推导 |
| [S-snapshot:5418](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5418) | `IX_Space_PlanningComparisonEntry_TenantId_RunId_BranchId_ScenarioVersionId` | 74 | convention 推导 |
| [S-snapshot:5475](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5475) | `IX_Space_PlanningComparisonRisk_TenantId_ComparisonId_EntryId_RunId` | 67 | convention 推导 |
| [S-snapshot:5556](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5556) | `IX_Space_PlanningDecisionRecord_TenantId_ComparisonId_SelectedRunId` | 67 | convention 推导 |
| [S-snapshot:5666](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5666) | `IX_Space_PlanningHistoricalDataset_TenantId_ModelId_ScenarioVersionId` | 69 | convention 推导 |
| [S-snapshot:5833](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5833) | `IX_Space_PlanningScenarioBranch_TenantId_ModelId_BasePublishedVersionId` | 71 | convention 推导 |
| [S-snapshot:5835](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5835) | `IX_Space_PlanningScenarioBranch_TenantId_ModelId_ScenarioVersionId` | 66 | convention 推导 |
| [S-snapshot:5927](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5927) | `IX_Space_PlanningSimulationLocationResult_TenantId_RunId_ScenarioVersionId` | 74 | convention 推导 |
| [S-snapshot:5929](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L5929) | `IX_Space_PlanningSimulationLocationResult_TenantId_ScenarioVersionId_LocationLogicalId` | 86 | convention 推导 |
| [S-snapshot:6143](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L6143) | `IX_Space_PlanningSimulationRun_TenantId_ModelId_ScenarioVersionId` | 65 | convention 推导 |
| [S-snapshot:6145](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L6145) | `IX_Space_PlanningSimulationRun_TenantId_DatasetId_BranchId_ModelId_ScenarioVersionId` | 84 | convention 推导 |
| [S-snapshot:6904](../../../CP6.Space.Infrastructure/Migrations/SpaceContextModelSnapshot.cs#L6904) | `UX_Space_RackGenerationProfileVersion_Scope_Owner_Profile_VersionNo` | 67 | explicit |
| [I-snapshot:121](../../../CP6.Core/Migrations/IdentityPriority/IdentityMessagingContextModelSnapshot.cs#L121) | `IX_Cp6_InboxAggregateCheckpoint_ConsumerName_TenantId_AggregateId` | 65 | convention 推导 |
| [E-snapshot:346](../../../CP6.Core/Migrations/ErpIntegration/ErpIntegrationContextModelSnapshot.cs#L346) | `IX_OrderBridgeDispatch_CompletedAtUtc_AvailableAtUtc_LeaseExpiresAtUtc` | 70 | convention 推导 |

## 全部 28 个 raw SQL 迁移文件的最终要求

合计 471 个静态 migrationBuilder.Sql 调用：动态循环一次调用可能创建多个对象，一条SQL也可能包含多个DDL/DML。表中Up/Down行均为实际调用行；保留SqlServer历史原件，PG空库建立最终对象/种子，历史数据修复按适用边界形成升级夹具，不能声称在不存在PG历史版本时通过历史升级。仅Down阻止回退的文件也必须保留治理要求。

| 迁移文件 / 实际调用行 | 静态调用数 | 类别 | 最终要求（Pending） |
| --- | ---: | --- | --- |
| [20260506103359_RestoreMissingOrderTablesPA070.cs](../../../CP6.Core/Migrations/20260506103359_RestoreMissingOrderTablesPA070.cs#L18)<br>Up: 18, 60, 165, 212, 235, 265, 294, 308<br>Down: 408, 409, 410, 411, 412, 413, 414, 415 | 16 | H/T/U/N | 补齐订单、明细、工序/备注/材料、单价/估价和版模等缺表；核对最终完整列、索引、PK/FK与GETDATE/常量默认。循环动态SQL不是单个对象；以当前模型加历史最终DDL交叉核对，不把IF OBJECT_ID转为空操作。 |
| [20260518115050_AddMesStoredProcedures.cs](../../../CP6.Core/Migrations/20260518115050_AddMesStoredProcedures.cs#L24)<br>Up: 24, 30, 39, 43, 91, 95, 134, 138<br>Down: 159, 160, 161, 162, 163 | 13 | H/U/T | 3个MES查询过程及2个性能索引（含INCLUDE）。保留结果列/参数、GETDATE本地日桶、含零日期序列、聚合精度及状态/软删除条件；PG等价入口与WP3调用方共同交付，snapshot不含过程。 |
| [20260531094653_WidenProductCategorySml.cs](../../../CP6.Core/Migrations/20260531094653_WidenProductCategorySml.cs#L16)<br>Up: 16, 17, 18<br>Down: 24, 25, 26 | 6 | H/T | ProductMaster/OrderDetail/EstimateCalc分类长度最终6；核对当前注解/snapshot和历史ALTER后结果，禁止生成基线时回退为4或截断历史值。 |
| [20260531153048_RemoveArticleAndDashboardRevamp.cs](../../../CP6.Core/Migrations/20260531153048_RemoveArticleAndDashboardRevamp.cs#L18)<br>Up: 18, 19, 21, 22 | 4 | H/seed | 退休菜单1、对应RoleMenu和两个i18n键删除。新库最终种子不得重新引入退休入口；upgrade保留依赖删除顺序和既有范围。 |
| [20260614065230_I18nP1_SysLangUniqueKey.cs](../../../CP6.Core/Migrations/20260614065230_I18nP1_SysLangUniqueKey.cs#L14)<br>Up: 14 | 1 | H/P/C | 旧阶段按LangKey去重并保留MIN(Id)后建唯一。新库保持最终全局/租户唯一规则；若需要升级修复，按当前租户语义明确去重，不重放旧全局去重删除tenant overrides。 |
| [20260615153849_FinJournalLineNoMutateTrigger.cs](../../../CP6.Core/Migrations/20260615153849_FinJournalLineNoMutateTrigger.cs#L17)<br>Up: 17<br>Down: 40 | 2 | H/trigger | Fin_JournalLine数据库不可变保护：父凭证Status=2/4的UPDATE/DELETE失败并回滚，INSERT允许。PG必须有等价DB保护、错误分类和direct SQL/批量回归。 |
| [20260616125141_MultiTenantOperLog.cs](../../../CP6.Core/Migrations/20260616125141_MultiTenantOperLog.cs#L16)<br>Up: 16, 26 | 2 | H/P/seed | 历史OperLog空TenantId归属固定默认租户A1，Alert索引加租户。保留归属值/索引；新库/二次初始化不能把其他租户记录重写到默认租户。 |
| [20260616132134_MultiTenantCompositeUniqueIndexes.cs](../../../CP6.Core/Migrations/20260616132134_MultiTenantCompositeUniqueIndexes.cs#L13)<br>Up: 13…890 (193处)<br>Down: 902…1743 (193处) | 386 | H/P/N | 193个Up和193个Down raw DROP调用并伴CreateIndex。保留最终租户key/filter/名称和FK依赖例外，不能按DROP字符串批量重放；完整index metadata对照当前模型后再建立PG最终索引。 |
| [20260618114602_A2CostSheetTruth.cs](../../../CP6.Core/Migrations/20260618114602_A2CostSheetTruth.cs#L98)<br>Up: 98<br>Down: 131 | 2 | H/finance | Labor/Overhead历史估算额同时填Actual与Standard后删除旧字段，总额保持。新库保留最终字段/种子；任何升级夹具必须证明金额无损，不以snapshot缺旧列跳过数据规则。 |
| [20260621064516_A5BudgetI18nFix.cs](../../../CP6.Core/Migrations/20260621064516_A5BudgetI18nFix.cs#L27)<br>Up: 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 40, 44 | 13 | H/seed/C | 11个仅TenantId IS NULL的日文预算文案UPDATE及2个NOT EXISTS全局多语INSERT。保留完整语言/格式占位符/幂等及tenant overrides不受影响；不是任意一套示例种子。 |
| [20260629162744_SpaceP5ConnectorCost.cs](../../../CP6.Core/Migrations/20260629162744_SpaceP5ConnectorCost.cs#L27)<br>Up: 27, 28, 29 | 3 | H/seed | ConnectorType1/2/3分别补WaitSec与TravelSecPerFloor为20/6、0/15、0/10。最终默认/初始化数据明确来源；不能全部使用新列默认0。 |
| [20260708093013_SysRoleTenantize.cs](../../../CP6.Core/Migrations/20260708093013_SysRoleTenantize.cs#L41)<br>Up: 41<br>Down: 89 | 2 | H/permission | 默认A1归属、每租户复制同RoleId、NOT EXISTS幂等，并检查所有已知子表(Tenant,RoleId)归属完整；失败全事务回滚。不能只改复合PK而不保留权限种子/数据规则。 |
| [20260708100345_SysRoleMenuTenantize.cs](../../../CP6.Core/Migrations/20260708100345_SysRoleMenuTenantize.cs#L33)<br>Up: 33<br>Down: 77 | 2 | H/permission | 默认A1RoleMenu归属、仅向存在租户角色副本的租户复制，避免扩散孤儿；检查同租户RoleId对应关系，保留幂等与事务失败。 |
| [20260710172302_SysRoleMenuUniqueIndex.cs](../../../CP6.Core/Migrations/20260710172302_SysRoleMenuUniqueIndex.cs#L19)<br>Up: 19 | 1 | H/P/permission | 同(TenantId,RoleId,MenuId)历史去重保留最小Id后建唯一；不能按全局RoleId/MenuId去重或丢掉tenant key。 |
| [20260725181400_SpaceRetryCompletionAndDeadLetterOutbox.cs](../../../CP6.Core/Migrations/20260725181400_SpaceRetryCompletionAndDeadLetterOutbox.cs#L47)<br>Up: 47 | 1 | H/U/message | 仅SPACE历史DEAD且未通知的行设DeadLetterNotifiedAtUtc，避免上线补发陈旧告警；保留通知租约索引/字段，不能把新死信预标已通知。 |
| [20260725203000_SpaceIntegrationEventOccurredAtUtc.cs](../../../CP6.Core/Migrations/20260725203000_SpaceIntegrationEventOccurredAtUtc.cs#L24)<br>Up: 24 | 1 | H/U/message | 只对明确新UTC-ticks JobId形状回填OccurredAtUtc=CreateDate，legacy local走应用显式时区规范化；保留判别条件、UTC字段和查询索引，不整列UTC转换。 |
| [20260908010000_CrmOidcGrantStore.cs](../../../CP6.Core/Migrations/20260908010000_CrmOidcGrantStore.cs#L40)<br>Up: 40 | 1 | H/C/U/object | 模型外CrmOidcGrant/CrmOidcLogout完整创建，BIN2 hash/redirect/challenge/nonce/session字段，CodeHash全局唯一、expiry索引。forward-only；不能凭EF当前snapshot遗漏ledger。 |
| [20260908011000_CrmOidcBrowserSessionFamily.cs](../../../CP6.Core/Migrations/20260908011000_CrmOidcBrowserSessionFamily.cs#L29)<br>Up: 29 | 1 | H/U/permission | AuthenticationEpoch零Guid默认、RefreshToken BrowserSessionId索引和BrowserSession表/认证版本/注销时间。当前snapshot有BrowserSession但不能描述全部raw默认；forward-only并保留认证family失效规则。 |
| [20260730152005_SpaceE01S06FileSafetyRetention.cs](../../../CP6.Space.Infrastructure/Migrations/20260730152005_SpaceE01S06FileSafetyRetention.cs#L32)<br>Up: 32 | 1 | H/U/retention | State=5或IsDeleted的历史文件补DeletionRequestedAtUtc，优先Modified/Created，再数据库UTC；保留实际内容删除待办predicate、retention/deletion证据与后续check。 |
| [20260731010047_SpaceE05S04AssetLibrary.cs](../../../CP6.Space.Infrastructure/Migrations/20260731010047_SpaceE05S04AssetLibrary.cs#L14)<br>Up: 14 | 1 | H/K/guard | 升级前拒绝任何旧ModelAssetId非空值，要求先审计/清空；最终资产Scope/Owner/版本组合FK和check必须成立。空库检查可自然为空，不得静默绕过已有库审计前置条件。 |
| [20260806054950_SpaceE13S09ProposalDecisions.cs](../../../CP6.Space.Infrastructure/Migrations/20260806054950_SpaceE13S09ProposalDecisions.cs#L39)<br>Up: 39 | 1 | H/K | 只对已resolved且有ResolutionCommandBatchId的旧issue填ResolutionKind=1；保留proposal/decision/resolution的最终形状约束。 |
| [20260806110504_SpaceE13S10AtomicApply.cs](../../../CP6.Space.Infrastructure/Migrations/20260806110504_SpaceE13S10AtomicApply.cs#L97)<br>Up: 97, 100, 103 | 3 | H/seed | Zone/Aisle/Rack的Name仅在NULL时从对应Code补齐；不覆盖用户已有名称；保留最终nullable/必填以及原子apply证据。 |
| [20260806160931_SpaceE13S17AiRetention.cs](../../../CP6.Space.Infrastructure/Migrations/20260806160931_SpaceE13S17AiRetention.cs#L72)<br>Down: 72 | 1 | H/forward-only | raw SQL只在Down THROW：AI retention/audit证据只允许forward-fix。PG链应显式保留forward-only迁移策略，不把其当缺失Up SQL或生成可逆drop。 |
| [20260807105256_SpaceE06S01ValidationEngine.cs](../../../CP6.Space.Infrastructure/Migrations/20260807105256_SpaceE06S01ValidationEngine.cs#L133)<br>Down: 133 | 1 | H/forward-only | raw Down THROW防删除validation evidence；最终validation表、check/索引与不可逆证据生命周期保留。 |
| [20260807135544_SpaceE06S03PublishOrchestration.cs](../../../CP6.Space.Infrastructure/Migrations/20260807135544_SpaceE06S03PublishOrchestration.cs#L304)<br>Down: 304 | 1 | H/forward-only | raw Down THROW防删除publish/reconciliation evidence；保留发布slot/批次/幂等/调和对象，不生成破坏性回退。 |
| [20260807144532_SpaceE06S04PublishRecovery.cs](../../../CP6.Space.Infrastructure/Migrations/20260807144532_SpaceE06S04PublishRecovery.cs#L14)<br>Up: 14, 79<br>Down: 177 | 3 | H/K/U/guard | Up先拒绝未清理活动publish slot；QueuedAtUtc仅MinValue sentinel从StartedAtUtc回填；保留RequestJson/attempt counters/retry audit/hash链及forward-only。 |
| [20260807170204_SpaceE06S05HistoricalRepublish.cs](../../../CP6.Space.Infrastructure/Migrations/20260807170204_SpaceE06S05HistoricalRepublish.cs#L139)<br>Down: 139 | 1 | H/forward-only | raw Down THROW保留historical-republish evidence；最终幂等、状态与context关系由模型创建，但forward-only不会从snapshot生成。 |
| [20260827053057_SpaceV1UnifiedDraftCreation.cs](../../../CP6.Space.Infrastructure/Migrations/20260827053057_SpaceV1UnifiedDraftCreation.cs#L42)<br>Up: 42 | 1 | H/K | BasedOnVersionId非空的旧draft设CreationSource=1，最终creation-source/template-shape check同步；不能让clone被默认0误标为blank。 |

## WP2 出口核对项

- WP1 已冻结 provider/Npgsql版本、并发token、identity watermark、Platform gate与独立migration assembly方案后，才改当前模型并生成PG完整基线；本清单不提前确认冻结。
- 四 Context 各自输出 property/type/default/time/collation、key/FK/AK/index/predicate/check、schema/history/object/seed 清单。这里已列全部当前字面类型、95 filter与126 check，以及已识别超长名称；完整属性级时间来源、所有推导PK/FK名称和现场catalog仍是未完成项。
- 对Sys_Lang NULL唯一、租户前缀例外、Space partial unique全部真/假/NULL分支、BIN2/hash/case/trailing-space、check合法/非法/NULL、金额精度、日期/UTC/DST执行真实双库回归。单纯生成DDL、用SQLite/内存provider或只保存候选字符串不能通过。
- 独立PG基线除327模型映射条目外，明确包含模型外OIDC ledger、MES等价查询对象、财务DB保护、token函数/触发器、最终种子/默认。保留28历史raw SQL文件逐项适用/等价/旧库专用理由和原SQLServer升级路径；不得以“不在snapshot”认定不需要。
- 空库初始化及第二次初始化核对真实对象/约束/种子，不能重写租户归属、重新生成认证版本、标记新死信已通知或重复业务副作用。正式后续升级与历史SQLServer验证使用隔离数据库，生产变更仍不在本阶段授权范围。

本阶段实际验证仅为源码读数、表达式/迁移调用清单与文档链接检查；未编译、restore、连接数据库、执行迁移/业务测试或触发GitHub Actions。Pending 项保留给WP1冻结后的WP2/WP3实施与真实验收。
