# WP3 Provider 分支逐文件登记

日期：2026-10-03。基线 main `605246ca83cbb8e1a89d3ff7209ba9df65176e98`；当前任务分支 `codex/db-compat-wp3-20261003`。本表是 WP3 未提交源码的字节观察，HEAD 不能单独标识这些改动。观察时间（实际主机 UTC）：`2026-10-03T09:33:42.4690296Z`。锁资源、共享 Context 工厂、错误处理及验收边界见 [WP3 调用方矩阵](WP3-CALLERS.md)，实际运行结果见 [WP3 实施记录](WP3-IMPLEMENTATION.md)。

本次仅补齐[计划 WP3 的 Provider 分支登记](../../superpowers/plans/2026-10-02-database-compatibility.md)。下表“已实现”仅指相应能力/路径存在；完整业务、PG 普通启动及恢复没有因此验收。保留 SQL 特有业务 guard、既有关系型条件 UPDATE 和测试 fallback，后续责任逐项列明。

## 口径与完整性

沿用历史 census 的 13 个生产根目录：`CP6.Core`、`CP6.Entity`、`CP6.WebApi`、`CP6.Space.Infrastructure`、`CP6.Space.Application`、`CP6.Space.Domain`、`CP6.Space.Contracts`、`CP6.Space.Client`、`CP6.Client.Api`、`CP6.Client.Core`、`CP6.Desktop`、`CP6.Mobile`、`CP6.Persistence.PostgreSql`；仅 `*.cs`，排除 `**/Migrations/**`、`**/obj/**`、`**/bin/**`，不包含测试项目和工具。当前共 **1571 个文件**。

- `\b(IsSqlServer|IsNpgsql|ProviderName)\b`：剔除纯注释后 **45 个命中行 / 34 个文件**，逐个读到实际分支与其 else 路径。
- 补充 `DatabaseProvider\.(SqlServer|PostgreSql)` 后 **79 个命中行 / 46 个文件**：**44 个实际分支文件 + 2 个固定 Provider/default 入口**，全部列入下表。行号包括 switch arm、分支返回值和必要 Provider 标签；命中行数不是独立条件数，更不是测试数量。
- 仅两处纯注释命中被排除：`DatabaseSharedContext.cs:17`、`BankReconService.cs:332`。纯 `UseSqlServer/UseNpgsql/new SqlConnection` 构造调用不是条件分支，既有 SQL store/factory 边界由 [CALLERS](WP3-CALLERS.md) 继续登记。通用 `IsRelational` 不是本 census 的独立搜索口径；已命中文件内的关系型与非关系型 else 路径一并说明。
- 根 `tmp/wp3-runtime-inventory-current.md/json` 实际观察的是 WP2 工作树、HEAD `45cf2ca58b792d936aab93ee4eaa771c4a77746d`；其旧宽口径含 `UseSqlServer/UseNpgsql/new SqlConnection`，为 84 行 / 45 文件。对应生成脚本仍指向 WP2，故此次在当前 WP3 重新枚举，没有覆盖原历史证据或照搬旧行号。

## 当前分支与责任

“测试回退”仅描述原 InMemory/非生产 Provider 路径；PG 不能因为进入通用 EF 或条件写路径而被当作已经完成真库业务验收。SQL 与 PG 的完整事务重试、外部效果和租户语义仍按对应后续工作包验证。

| 编号 | 文件与定位行 | 当前分类 | 分支含义与范围 | 后续归属 |
| --- | --- | --- | --- | --- |
| B01 | [CP6.Core/EFDbContext/CP6Context.cs](../../../CP6.Core/EFDbContext/CP6Context.cs#L615)：615、627、2626 | 双库模型／SQL 专属元数据 | PG 标识符与模型转换；SQL Snapshot 声明 generation trigger。 | WP4 Core 业务；WP6 安装/升级 |
| B02 | [CP6.Core/Persistence/DatabaseConnectionFactory.cs](../../../CP6.Core/Persistence/DatabaseConnectionFactory.cs#L22)：22、23、40、41 | 双库基础 | 选择 SqlConnection/NpgsqlConnection、核对连接类型；PG 固定 public search path。 | WP6 运行配置 |
| B03 | [CP6.Core/Persistence/DatabaseContextOptions.cs](../../../CP6.Core/Persistence/DatabaseContextOptions.cs#L20)：20、22、37、39、72 | 双库基础 | 字符串／已有连接两套入口选择 EF Provider；已有连接不转移 ownership；拒绝混入另一 Provider。 | WP4/5 消费者；WP6 配置 |
| B04 | [CP6.Core/Persistence/DatabaseDesignTimeConfiguration.cs](../../../CP6.Core/Persistence/DatabaseDesignTimeConfiguration.cs#L41)：41 | 双库设计时配置 | 按显式 Provider 选择设计时缺省连接，仅供 scaffolding。 | WP6 安装/运维 |
| B05 | [CP6.Core/Persistence/DatabaseMigrationProfile.cs](../../../CP6.Core/Persistence/DatabaseMigrationProfile.cs#L39)：39 | 双库基础 | 选择 PG 独立迁移程序集/四历史表或 SQL 既有 owner/history。 | WP6 初始化/升级 |
| B06 | [CP6.Core/Persistence/DatabaseOptions.cs](../../../CP6.Core/Persistence/DatabaseOptions.cs#L20)：20、37、41、42 | 双库配置契约 | 只接受两个枚举值；缺省 SQL；显式配置精确解析，未知值失败。 | WP6 运行配置 |
| B07 | [CP6.Core/Persistence/DatabaseResourceLocks.cs](../../../CP6.Core/Persistence/DatabaseResourceLocks.cs#L42)：42、65、81、82、88、106、147、186 | 双库能力 | 分派事务/会话锁与释放，验证 Provider/连接；SQL -3 的 Provider 标签在 147 行，并非额外选择分支。 | WP4/5 完整调用协议 |
| B08 | [CP6.Core/Persistence/DatabaseSharedContext.cs](../../../CP6.Core/Persistence/DatabaseSharedContext.cs#L18)：18、20、21 | 双库能力 | 按 caller.ProviderName 选择显式 Provider，并共享物理连接/事务及目标 profile。 | WP4/5 六个业务工厂 |
| B09 | [CP6.Core/Persistence/DatabaseUtcClock.cs](../../../CP6.Core/Persistence/DatabaseUtcClock.cs#L15)：15、17 | 双库能力 | SQL SYSUTCDATETIME／PG clock_timestamp；不支持的 Provider 抛错。 | WP5 租约；WP6 运行验证 |
| B10 | [CP6.Core/Services/Common/DocNumber.cs](../../../CP6.Core/Services/Common/DocNumber.cs#L27)：27、48 | 双库 ORD／原测试回退 | ORD 两库立即预留并参加已有事务；PG UPSERT／SQL 锁定增量；其余 Provider/非 ORD 保留 Local 延迟保存。 | WP4 订单链及其他编号器 |
| B11 | [CP6.Core/Services/CrmIdentity/IdentityMessagingContext.cs](../../../CP6.Core/Services/CrmIdentity/IdentityMessagingContext.cs#L13)：13、15 | 双库模型 | PG 标识符长度与消息模型转换；SQL 保留既有模型。 | WP4 priority 消息；WP6 初始化 |
| B12 | [CP6.Core/Services/CrmIdentity/IdentitySnapshotReader.cs](../../../CP6.Core/Services/CrmIdentity/IdentitySnapshotReader.cs#L29)：29 | 双库分页能力 | PG RepeatableRead／SQL Serializable，同页读取 durable generation 和目录；v2 cursor。 | WP4 身份业务/API |
| B13 | [CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs](../../../CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs#L176)：176 | SQL 业务 guard | 要求 SQL Provider 与 caller transaction；原 locked-read/撤销 SQL 仍保留。 | WP4 写入/撤销/Outbox |
| B14 | [CP6.Core/Services/Erp/OrderService.cs](../../../CP6.Core/Services/Erp/OrderService.cs#L109)：109 | SQL 业务 guard | 订单入口要求 SQL、现有事务、汇率服务与正确租户；当前不能仅去除 Provider 条件。 | WP4 订单业务链 |
| B15 | [CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs](../../../CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs#L182)：182 | SQL 业务 guard | RequireSql 在列表和重放入口拒绝 PG；共享 Context 已适配，业务 SQL/重放协议待适配。 | WP4 delivery replay |
| B16 | [CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs](../../../CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs#L28)：28 | SQL 业务 guard | 进入 Serializable replay 事务前拒绝 PG；嵌套 Core 已走共享工厂。 | WP4 inbox replay |
| B17 | [CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs](../../../CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs#L19)：19、73 | 双库模型 | PG 标识符长度与 ERP 消息模型转换；SQL 保留既有模型。 | WP4 ERP 消息；WP6 初始化 |
| B18 | [CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs](../../../CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs#L17)：17 | SQL 业务 guard | 要求 caller transaction、SQL Provider 和匹配租户；报价/客户锁定查询仍为 T-SQL。 | WP4 报价转订单 |
| B19 | [CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs](../../../CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs#L272)：272 | SQL 业务 guard | RequireSql 拒绝 PG；同连接业务工厂已适配，命令/回执/业务查询仍待验收。 | WP4 ERP request |
| B20 | [CP6.Core/Services/ErpIntegration/ErpSqlLock.cs](../../../CP6.Core/Services/ErpIntegration/ErpSqlLock.cs#L10)：10、17 | 双库能力／SQL 专属错误兼容 | 两库均要求已有事务并走资源锁；SQL typed applock deadlock 继续映射 C03_SQL_CONTENTION。 | WP4 ERP 完整事务 |
| B21 | [CP6.Core/Services/Fin/BankReconService.cs](../../../CP6.Core/Services/Fin/BankReconService.cs#L333)：333 | InMemory 测试回退 | 仅 InMemory 跳过单条过账事务；SQL/PG 均进入事务路径，财务业务未由此证明。 | WP4 财务回归 |
| B22 | [CP6.Core/Services/Integration/DeadLetterNotifier.cs](../../../CP6.Core/Services/Integration/DeadLetterNotifier.cs#L245)：245 | SQL 锁／关系型条件写／测试回退 | SQL hint 锁；其他关系库保留按租户/状态/lease 条件 ExecuteUpdate 并要求 1 行；非关系库 Any 回退（279 行）。 | WP4 通知/fence；WP5 Space 集成 |
| B23 | [CP6.Core/Services/Pur/PurchaseRequestService.cs](../../../CP6.Core/Services/Pur/PurchaseRequestService.cs#L231)：231 | SQL 锁／普通 EF 分叉 | SQL callback 取 UPDLOCK/HOLDLOCK/ROWLOCK；其余 Provider tracked reload/普通查询，尚无等价 PG 锁定分支。 | WP4 采购审批 callback |
| B24 | [CP6.Core/Services/Space/Observability/SpaceAuditQueryService.cs](../../../CP6.Core/Services/Space/Observability/SpaceAuditQueryService.cs#L205)：205 | SQL 专属函数／通用投影 | SQL 用 DataLength 字节门限；其余 Provider 用字符长度门限，不能视作字节契约等价已验收。 | WP5 审计投影/分页 |
| B25 | [CP6.Core/Services/Sys/RefreshTokenService.cs](../../../CP6.Core/Services/Sys/RefreshTokenService.cs#L184)：184 | SQL 锁／普通 EF 分叉 | rotation 且 SQL 时锁 browser session；其他路径普通 EF 查询，保留原条件。 | WP4 refresh/family/撤销 |
| B26 | [CP6.Core/Services/Wms/MobileTaskV1Service.cs](../../../CP6.Core/Services/Wms/MobileTaskV1Service.cs#L228)：228 | InMemory 测试回退 | 仅 InMemory 或已有事务时不新建事务；SQL/PG completion 使用关系型事务路径。 | WP4 WMS 任务原子性 |
| B27 | [CP6.Core/Services/Wms/StockMovementService.cs](../../../CP6.Core/Services/Wms/StockMovementService.cs#L67)：67、204 | InMemory 测试回退 | 两处仅排除 InMemory；无 caller transaction 时自建事务。65 行旧注释“SQL only”不代表实际条件。 | WP4 库存/台账/MOVE |
| B28 | [CP6.Core/Services/Wms/WmsBinConsumer.cs](../../../CP6.Core/Services/Wms/WmsBinConsumer.cs#L314)：314 | SQL 锁／关系型条件写／测试回退 | SQL hint 锁；其他关系库先条件 ExecuteUpdate fence 再读（343 行）；非关系库普通查询。保留已有条件写方案。 | WP4 WMS retry；WP5 Space 集成 |
| B29 | [CP6.Space.Infrastructure/SpaceAiRunRecoveryService.cs](../../../CP6.Space.Infrastructure/SpaceAiRunRecoveryService.cs#L538)：538 | SQL 锁／普通 EF 分叉 | SQL 锁 generation run；其他 Provider 普通 SingleOrDefault，完整恢复竞争未验收。 | WP5 业务恢复；WP6 运行恢复 |
| B30 | [CP6.Space.Infrastructure/SpaceAiWorkSlotQueries.cs](../../../CP6.Space.Infrastructure/SpaceAiWorkSlotQueries.cs#L120)：120、121 | 双库领取能力 | 精确选择 PG/SQL，其他 Provider 失败；查询分别用 SKIP LOCKED 或 SQL 锁提示/返回形式。 | WP5 完整容量/生成 |
| B31 | [CP6.Space.Infrastructure/SpaceCadParseService.cs](../../../CP6.Space.Infrastructure/SpaceCadParseService.cs#L1430)：1430 | 双库时钟／测试回退 | 内联 lease 校验两库使用 DatabaseUtcClock，其他 Provider 用原 RequireUtcNow。 | WP5 CAD/lease fence |
| B32 | [CP6.Space.Infrastructure/SpaceContext.cs](../../../CP6.Space.Infrastructure/SpaceContext.cs#L180)：180、265 | 双库模型 | PG 标识符长度与 Space 模型转换；SQL 保留既有模型。 | WP5 Space 业务；WP6 初始化 |
| B33 | [CP6.Space.Infrastructure/SpaceDesignV1Service.cs](../../../CP6.Space.Infrastructure/SpaceDesignV1Service.cs#L6359)：6359 | 双库时钟／测试回退 | 两库使用数据库 UTC；其他 Provider 保留 RequireUtcNow。 | WP5 floor/edit 完整协议 |
| B34 | [CP6.Space.Infrastructure/SpaceEditLeaseService.cs](../../../CP6.Space.Infrastructure/SpaceEditLeaseService.cs#L393)：393 | 双库时钟／测试回退 | 两库使用数据库 UTC；其他 Provider 保留注入 clock.UtcNow。 | WP5 编辑租约 |
| B35 | [CP6.Space.Infrastructure/SpaceExcelCadApplyJobStepExecutor.cs](../../../CP6.Space.Infrastructure/SpaceExcelCadApplyJobStepExecutor.cs#L1868)：1868 | 双库时钟／测试回退 | 两库使用数据库 UTC；其他 Provider 保留 RequireUtcNow。 | WP5 job/apply/lease |
| B36 | [CP6.Space.Infrastructure/SpaceExcelCadApplyService.cs](../../../CP6.Space.Infrastructure/SpaceExcelCadApplyService.cs#L634)：634 | 双库时钟／测试回退 | 两库使用数据库 UTC；其他 Provider 保留 RequireUtcNow。 | WP5 Excel CAD apply |
| B37 | [CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs](../../../CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs#L41)：41 | 固定默认入口（非条件分支） | 旧 connectionString overload 显式缺省 SQL；接受 DatabaseOptions 的 overload 走统一 Provider 配置。 | WP6 注册/运行配置 |
| B38 | [CP6.Space.Infrastructure/SpaceResourceLocks.cs](../../../CP6.Space.Infrastructure/SpaceResourceLocks.cs#L16)：16 | SQL 专属错误兼容 | 仅 SQL typed applock -3 映射原业务 busy；真正两库资源锁由共享能力实现。 | WP5 Space 完整事务 |
| B39 | [CP6.Space.Infrastructure/SpaceUnderlayHistory.cs](../../../CP6.Space.Infrastructure/SpaceUnderlayHistory.cs#L308)：308 | 双库时钟／测试回退 | 两库使用数据库 UTC；其他 Provider 保留 RequireUtcNow。 | WP5 Underlay/lease |
| B40 | [CP6.WebApi/BackgroundServices/SpaceIntegrationEventOccurredAtUtcBackfill.cs](../../../CP6.WebApi/BackgroundServices/SpaceIntegrationEventOccurredAtUtcBackfill.cs#L29)：29、50、53 | 双库 Session 锁／测试回退 | 两库按实际 Provider 取得跨批次会话锁；其他 Provider 留进程 gate；SQL typed deadlock 保留域错误。 | WP6 PG 启动/跨批/失败释放 |
| B41 | [CP6.WebApi/Configuration/CrmIdentityConfiguration.cs](../../../CP6.WebApi/Configuration/CrmIdentityConfiguration.cs#L45)：45 | SQL 专属配置检查 | 只有 SQL 解析并禁止 MARS，保留 savepoint 前提；PG 已经由公共连接工厂校验。 | WP4 身份事务；WP6 配置 |
| B42 | [CP6.WebApi/Configuration/DatabaseRuntimeSupport.cs](../../../CP6.WebApi/Configuration/DatabaseRuntimeSupport.cs#L10)：10 | 阶段性 PG 运行 guard | PG 仅允许 databaseInitializationOnly；普通 API/worker 启动继续拒绝。 | WP6，须先完成 WP4/WP5 |
| B43 | [CP6.WebApi/Configuration/ErpIntegrationConfiguration.cs](../../../CP6.WebApi/Configuration/ErpIntegrationConfiguration.cs#L45)：45 | SQL 专属配置检查 | 只有 SQL 解析并禁止 MARS；未以 PG 不支持该设置误报。 | WP4 ERP 事务；WP6 配置 |
| B44 | [CP6.WebApi/Configuration/ProductionConfigurationValidator.cs](../../../CP6.WebApi/Configuration/ProductionConfigurationValidator.cs#L47)：47、49 | 双库配置分派 | 显式 Provider 选择 SQL 或 PG 配置校验器；本表不扩大为安全审查。 | WP6 运行配置 |
| B45 | [CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs](../../../CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs#L134)：134、204 | 双库事务锁／测试回退 | 两库 execution strategy 内使用资源锁 seed 协议；其他 Provider 留原 fallback；SQL typed deadlock 返回 busy。 | WP6 PG seed 幂等/并发 |
| B46 | [CP6.WebApi/Services/CrmOidcGrantStore.cs](../../../CP6.WebApi/Services/CrmOidcGrantStore.cs#L135)：135 | 固定 SQL 入口（非条件分支） | 现有 SqlConnection/DbTransaction 显式传 SQL 到共享工厂；store 整体仍为 SQL 实现。 | WP4 OIDC 原子消费/撤销 |

## 字节快照与复核

下列 SHA-256 对应上表编号的**完整文件原始字节**，包含换行，未仅对匹配行取 hash。整个 1571 文件扫描集合的清单摘要为 `D24E5CD7D7AE0195EDC10BFF8CD3017CBDCA6FD7ADFA6711BB4C86412C49E3F7`：按相对路径排序，每行 `path + TAB + uppercase SHA256 + LF`，UTF-8 无 BOM 后取 SHA-256；路径使用 `/`。源文件新增/移除或任一字节变化都应重新生成该摘要及受影响行号。

<details>
<summary>46 个命中文件的完整 SHA-256</summary>

```text
B01 69DCBD468C3CB742CFAEFDE5FFA8B2A518230C2710E3CE31E46F5AE9A9DA2106
B02 D7B729E3B44DE38DC8413ECFC9869ED381CB8768B246300F61B6178E102A018A
B03 0E9412A582B86F2485AC724CE8DB0C1C316087E45ABAC2C3CC30EDFE13102190
B04 CEE25A3B803120DFA3306A2E96FAD18189F119B1F4E7F4BF0180CA5CD2CF9FE5
B05 C8F3521C294F15793A8CB3BE0AB8D598A7DDBC88573A52042AF12C6CC16BFB26
B06 976DE28B08802A3BE2C97118767215AE0A96FB8CF23F6F53451A74533105A2DD
B07 6E3ACC6D3AB2C052A601C070BD8C5616105B7BA664110AE6CAE307C71D861593
B08 FD06C38C2B003B9B6644C476635CB1D17F61321875CECBD4531772F450F511F5
B09 1B0F438DDCCB7D48EA8AA391DFEC7E96BF2BE133E520D262577FC175BA6794E9
B10 CCFB53B85263A3F0C7269DC24FD6D498EFCE529C1D4DDA5245C7A4FFFE43357B
B11 B4F7F011111FBC133EDF78BE7E359F3A3CC4312E3E147A2E2E3083737D9D98F7
B12 0BC6A322545D5EEFBAE26BB9C0022A76CA6BB962DB46B6A92AF385A34E4DF01E
B13 64B8389F9D1AE1AD667AB03F7AE3F4CA896E8F8082AAAE7C2E8725DE2C9027C7
B14 6DAE1150B84D9C0989B5AE5E2DF7625EB0112BBDAA8B69EB5C552B38999CA2F2
B15 8499B1D9A00A3F09CB52DA799B1B69CAF9E6F3ADA5404B418195FE072A60D1AD
B16 3AF8AA0E0FFF79BD468CAD836F2AF1F36C48DE2C69828602DB41DF645DE7AC4F
B17 673425FB7B02A971ACDFDEE220882681A92FC15E42AB0BF698AE9A0976513DB0
B18 F7097AC543C3917EE9F63C268E0430CCED5D3AACB38719651F0FE84665933106
B19 A20E273FF32D2D08D5D56C6C30581017F99B1FFABAB524E0EF60C1F0CBADAA71
B20 5B0DC4DBFE415D6F69AEDCC9564F9EA3E4ED6D3D4A2B6D0FDAB8AC034460EBBA
B21 C7E753AB060647C95F7D117A0A17448296BA718F0E6F9AD52D18AB0266D91A13
B22 7CC9ABC2B94EA99E272B90491A4CD291E5F816EE5AD798E1C3F83CF4264396A1
B23 FDA1D92D4652C33EB78C0866E5E23BEC8A83F3084D7E59F7140140CEFB11F001
B24 F097A028366637D3F36C83BE6F08F2C5E0923B6A0F9ECE10B7112477DB4A69D3
B25 C5017B7959D34491201F7E2692C348A71F2BB20CF4983736A804E97F812DA8F8
B26 1DD0749618DC9D90D4EFEC2DD1D2182377A73A33993E24FF527512738FA32CE1
B27 6877C3CB43804CE472980D6028DB52FC2967F3558540896B9DA334005B45259D
B28 F692E37C90DF7D0E808A431F21E9BFFF296DC0EE124EB9A66CDBBD18C25A92F9
B29 175D7FDFD2817A5BC6AAAECFDE694AF50B0C590602DB52B8A46B35EFCB04F822
B30 E624B365AC1EC056F2101E1E84798091DE925654C93AA3B94EB58D88E58B7134
B31 7F55D890C7BB1F074438497E9E956EC1F923651DB46F09BD088357A04DFAF419
B32 888A747CDE65F68370589C30FE871C21871A56A8835FBB0EB0D74D54EF772F27
B33 6D95545E4AD578B52AB8AAFC9D4F70AE0B757320F0EB4327BB75776F96DDC37F
B34 B089108692C5ED2AE0F85EC62DA28F9BA9A38F97C8D8AA58ED6697198F39FDF7
B35 5F2B6B08837F493E34F4201883E3BF4EC1D7BFE0030D0FC6B477119A86689809
B36 8650C084411C8C2CF7C95421790DF9806AA98C5DD4FA48438447774BEBE2E5B9
B37 5BD89B0128BE340B393BECB726854DA5F9C76ED8E303BD6DD0038B8F6308B920
B38 7A2F9B13AB144FE585CCD31BCBB5970EE5EE6C946FCA001A1AE71148D61B1DE6
B39 BD2E7BCEEFAB4935FE07F69C4BC25B61ACF3FC431E52E527303789B9D972CA77
B40 4F0E7A2B9EADE0724AF3600554456F63D8F8F337B21A43E4DE6850907C237A04
B41 82911BF9D14A336F0D1A16BFA7CC30577593E0EE183A2D134FEC27CFC8EF6249
B42 D0C21C8A7F257E92AD1725D338677E77D1A5CFE4F61D5DE13FE8C64F6BF69B18
B43 ED513F6BED498A2A5DD5C387F1986E033F3DDA20311EF19157B88E37BE7DC274
B44 5CDF1457EFB09E799AD208E7FEFB8681444BC2B20283AFAFB33330F83E656FF7
B45 8403C698D8DCC199B850F90AD83E37CF240859A5C13A0F647DD8C6A076A5DF7C
B46 5475DD7381E0B069CA1763628D53EDFF633F42202DF71872FEC674209F7DAB0F
```

</details>

文档生成后用同一范围重新扫描，核对 46 个文件及 79 个定位行均在表内、源 hash 未变化、相对链接存在；仅执行文本/差异检查。本次没有编译、测试、数据库访问、远程操作或新增业务验收，也不修改历史原生成功/失败结果。
