# CP6 双数据库兼容 Implementation Plan

## 2026-10-04：双库兼容版已交付并通过合并后冒烟

[PR165](https://github.com/GTX537/CP6/pull/165)已正常保护合并到远端main 8b64bc8a510e9ad4a82c21c8c6ac13c138e332c3；候选4243c67d7ac2e3cfafb809d2a4efba54f4ec927a是其ancestor，完整树68f7d4accfc58897a6bb430f5f90dded50c4ba94相同，2,389个PR文件均完成分页与Git blob核对。原洁净实现e940825fc7511b41a1a80b4b73897dcc69e51bbb的双库105个适用入口、当前配置/接线139项、56项HTTP与真实原生独立恢复均通过，所有31个正式库已清理。2,091份原始证据与Git index字节一致，失败及复用来源保持。

合并后使用同一原261文件发布物，SQL/PG分别在新的独占库实际初始化、核对history、启动所属API及三个health端点、登录与12项权限/改密/401/403检查；HTTP新建用户均从确切owned库实际查得。两个新库普通DROP并核对不存在；这是新冒烟，不冒称重建、重跑完整矩阵或生产验收。初次PG冒烟脚本属性读取丢失SSL覆盖的失败保留，纯内存复现后改用正式runner的set/get方法；原Planned目标实际不存在、receipt与Failed summary保持，不改变CP6项目代码或TLS生产规则。155份交付/冒烟原件已逐份核对归档。

六工作包的功能、适用验收和代码交付已闭环，结案证据与运行入口见[最终验收及交付](../../audits/database-compatibility/WP6-ACCEPTANCE.md)；父任务活动状态以[Issue134](https://github.com/GTX537/CP6/issues/134)为准。父任务只在本次结案文档正常合入并核对远端完整包含性后关闭，下方历史“未完成/guard保留”等记录不代表当前运行能力。普通PG API/worker现可使用所选Provider；没有现有业务数据搬迁或生产部署。

推送/PR/合并前分别核对17个工作流/依赖输入，队列均0，无远程Actions运行/取消、触发/保护变更或管理员绕过。根工作区用户既有改动保留；正式R2/GHCR候选、签名/扫描、审批与SQL生产基线不变。

## 2026-10-04：WP6 最新本地验收全部通过，远端交付待完成

洁净源码 e940825fc7511b41a1a80b4b73897dcc69e51bbb 的最新 wms-local-transaction-fix 四次实际分项全部Passed：PG Matrix34/34、SQL Matrix37/37、两库Application各17/17，共105个适用入口、零失败/跳过；不是单次Full。当前程序集补充配置/四Context接线/生产校验139/139，零跳过。两库使用同一份当前实际发布API（261文件逐字节一致），真实独立恢复专项核对56项HTTP、8次API启动、所有表/行/序列/history/持久key与恢复后消息一次投递/幂等重放通过；本轮31个owned临时库全部普通DROP且AbsentVerified。

八项完成条件的本地范围已逐项绑定实际原件及断言源位置，旧失败/成功及原审查时点保留。一次集中审查与定向修复/清理/WMS整合复核无剩余实质阻断。WP6公共证据归档/index核对、正常PR/main交付、合并后冒烟与远端包含性仍待完成，父Issue134继续Open；不能因本地全部通过就宣布任务关闭。实际版本、源码/产物哈希、复用来源及范围见[最终本地验收](../../audits/database-compatibility/WP6-ACCEPTANCE.md)，下方旧状态均为历史时点。

普通PG API/worker临时guard已移除，仅在同一部署使用所选Provider及连接；旧记录中“guard保留”是当时状态。R2/GHCR正式门禁和SQL生产基线不变，未启动/取消远程Actions、修改触发/保护、切换既有环境或部署生产。已有SQL数据搬迁、同实例混库及外部CRM全面改造不在此目标内。

## 2026-10-04：BUG163 已交付，WP6 接入当前版本验收

[BUG163](https://github.com/GTX537/CP6/issues/163) 已由 [PR164](https://github.com/GTX537/CP6/pull/164) 正常合入远端 main `6322ac152cab7d462719169fb0301f49e9ee39ba` 并关闭，WP6 已接入。WMS 原八项＋新增十七项两库各25/25、零失败/跳过；合并后两库各3/3。两个修复测试库及原 WP6 SQL 失败库均已普通 DROP 并确认不存在；SQL 实际持续会话超时拒绝、短暂会话自行结束后清理通过，原 Failed summary 与 core/WMS TRX 字节保持。[专项记录](../../audits/2026-10-03-bug-163-wms-local-transactions/README.md)保留111份原件及36份交付、冒烟、清理原件。

当前必需清单只扩展 WMS 为25项，其他61入口不变。最终入口仍为 PG Matrix34、SQL Matrix37、两库 Application各17，共105个适用入口；使用新的 `wms-local-transaction-fix` 运行目录。编译输入已改变，需当前代码七项构建及新的完整双库验收；后续 SQL 可在源码与完整运行文件逐字节一致后复用 PG 的真实构建，两库应用使用同一新发布产物。此前 PG Matrix34/34、旧发布物应用17/17保留历史来源，不代称当前最终验收。WP6 与父任务#134仍未完成；最终恢复审计、集中审查、归档与正常远端交付待完成。没有启动远程 Actions、改变生产门禁或部署既有环境。下方记录保留历史时点。

最新阻断（2026-10-03，BUG163 Open）：SqlServer cad-cleanup-fix Matrix在原核心25/25后，WMS8项为7P/1F，原Lpn_UsesCompositeSerialIdentity_AndMovesSplitsMergesWholeTree的CreateAsync第二查询触发Implicit distributed transactions have not been enabled；后35入口未执行。原summary SHA256 8259FAEA4EEEF0786705CB4501F1072F79D0CA7DC116606B9EEB5364A7205F50与WMS TRX 5AD8388203E589148B68795BFBC56C2478BC0D95EF9646C6A3A5D06C042A2870保留Failed，一个该次SQL owned库暂留。已登记[Issue163](https://github.com/GTX537/CP6/issues/163)，修复从最新已确认main ce49012d37da4b1b01f074d5b5b7fd227780e656创建独立codex/bug-163-wms-local-transactions分支。源码确认LPN四处自建ambient事务，runner明确Pooling=False；须原生确认与修复本地事务生命周期，Label相似路径尚待实际确认。PG Matrix34/34/11库Absent仍是修复前该次证据；修复影响编译输入后最新双库最终Matrix/Application需要重新适用验证，WP6与父Issue134未完成。首次SQL连接缺失为配置预检拒绝，未创建运行目录/DB；随后仅进程内使用既有本机KOUSQLSERVER Windows登录，未改用户PG配置/环境。


最新验收进展（2026-10-03 23:17Z）：新cad-cleanup-fix PostgreSql Matrix已实际Passed，34/34必需入口、零失败/跳过，11个owned库AbsentVerified；summary SHA256 5AD4D28C008E705F9DF355572ED9B532B1D80723BE1FB4D18439FDA1321BB107。该轮明确SkipBuild，复用原实际7项成功构建：编译输入与2059运行文件逐字节一致，PowerShell限定清理变化另有RED/GREEN及native控制证据；不把上一轮清理Failed改成Passed。进入SqlServer Matrix37，SQL136旧基线仍单独构建/安装；两库最新发布物Application17各仍待执行，最终105入口/恢复核对、归档、正常远端WP6交付与父任务关闭尚未完成。


最新本地进展（2026-10-03）：cad-provider-fix PG Matrix 的34/34入口全部通过，CAD25/25且零skip；收尾owned-database-cleanup遇到短暂会话，整轮仍为Failed，原summary SHA256 EEF84ED7808D7D83D7389708102C3989BA03DEEDD4EA17C367123D476AC4BCD1及34入口原件不变。七库原已Absent、余四库由实际限定等待控制后普通DROP，11库均Absent；原会话类型未记录，不推断其来源。WP6新清理模块默认逐库最多等待15秒，只对会话未结束重试，每次重核owner/principal/physical，超时或身份变化仍拒绝，保留全目标预检及逐库复检；新离线控制RED后41项GREEN，原42项契约通过，真PG持续会话1秒超时拒绝且receipt不变，短暂会话自行结束后清理通过。仅此PowerShell与控制变化，不改业务编译输入，7个既有真实成功构建/2059运行文件逐字节核对后复用；新完整Matrix和双库Application使用cad-cleanup-fix目录，最终验收/远端交付仍待完成，父Issue134保持Open。下文旧目录及状态保留历史时点。


最新交付（2026-10-03，BUG161 Closed）：PR162 正常合并到远端 main ce49012d37da4b1b01f074d5b5b7fd227780e656，并已接入 WP6。两库 CAD/资产/协作原15＋新十项各25/25、零skip，补充 InMemory17/17；合并后两库各3/3，PG两项协调测试再次实际40001后恢复。两个 BUG 自有库已普通DROP并核对不存在，Issue161于22:11:25Z关闭。[源码/原始失败及双库回归](../../audits/2026-10-03-bug-161-cad-provider-recovery/README.md)与交付/冒烟/清理原件保留；原 claim-fix Matrix 28P后14P/1F及后五入口未执行仍为失败，七个该次PG库清理且原summary不变。最新清单仅把CAD组15扩为25，其他61入口不变；最终两库Matrix与最新产物Application采用新cad-provider-fix目录，仍待实际执行。WP6未完成，父Issue134仍Open。下文BUG161 Open及旧claim-fix计划保留历史时点。


最新状态（2026-10-03，BUG161 Open）：claim-fix PostgreSQL 最终 Matrix 已终止为 Failed；7 个相关项目构建通过，28 个入口 Passed，第 29 个 CAD/资产/协作入口 15 项为 14P/1F，后五入口未执行。原 Concurrent_replace_preserves_one_current_revision_and_immutable_evidence 出现实际 PostgreSQL 40001，失败 summary SHA256 9A788F5E6450F4B4FE4258FEDA9FFC99B959EB7A7E997F2A8EB2BEAD49F6284C，TRX SHA256 32E0C9CD7990B0EE5ACD8B7F20C19EA351C3F9465D13FAEAABE711B7D275E364；原件保留。修复从已确认远端 main 4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0 建立独立 codex/bug-161-cad-provider-recovery 分支，活动缺陷以 [Issue #161](https://github.com/GTX537/CP6/issues/161) 为准。最新 SQL Matrix / 双库 Application 仍未开始；WP6 与父 Issue134 未完成。下文 claim-fix 计划及状态保留历史时点。


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement the next work package. Follow CP6 AGENTS.md: related steps receive local verification; perform one complete review per delivery task/PR, not a complete review after every file or commit.

**Goal:** 同一套 CP6 源码，每次部署选择 SQL Server 或 PostgreSQL，保持业务、权限、并发和消息一致性，并提供真实双库本地验收证据。

**Architecture:** 共享领域实体、业务服务和外部 API；Provider 统一决定连接、Context 工厂、模型差异和迁移程序集。保留现有 SQL Server 迁移历史，新增 PostgreSQL 迁移程序集及四个 Context 的独立快照；数据库专有能力通过小范围接口实现。

**Tech Stack:** 当前 .NET 8 / EF Core 8.0.30、Dapper、SQL Server、PostgreSQL 18；Npgsql Provider 与 EF Core 主版本一致，具体依赖版本及 lock 文件在 WP1 核验后固定。

---

最新整合（2026-10-03）：BUG159已由PR160正常交付远端main `4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0` 并关闭，已接入WP6；两库任务/处理/生成/保留原17＋新6各23/23、合并后各2/2、零skip，相关自有库清理完成。PG floor-fix最终矩阵26入口通过后第27入口16P/1F原生23505失败保留，后七入口未执行；当前任务组23/克隆23/设计发布41保持精确清单，其他61入口不变。最终claim-fix两库Matrix与最新发布物Application、归档和远端WP6交付继续，父Issue134仍Open。下文原floor-fix计划与状态保留历史时点，当前结果见WP6实施记录。

用途：DB-COMPAT-01 的阶段工作包与验收计划。WP1–WP5 已分别由 PR136/145/146/149/152 交付；WP5 合并后两库各3/3冒烟及13库清理见[交付原件](../../audits/database-compatibility/wp5-native/post-merge/manifest.json)。BUG153/155/157已由PR154/156/158交付并关闭，最新已确认主线为 `5587a2a67ae73715596ca1a135b5863005abac8d`，WP6已接入。修复前正式Application两库各17个入口及清理通过，包含真实WebSocket、恢复后的worker/重放和旧游标；两个生产事务修复改变依赖后，最终Matrix与新发布物Application采用新floor-fix目录。设计/发布41项保持，克隆原16＋新七项为23；最新定向两库23/23、41/41与合并后3/3不代称完整WP6验收。最终验收、归档及远端交付仍待完成，见[WP6实施记录](../../audits/database-compatibility/WP6-IMPLEMENTATION.md)。[Issue#134](https://github.com/GTX537/CP6/issues/134)保持Open，WP6阶段交付未勾选。更新日期：2026-10-03；下文早期Pending保留各历史时点。

设计规则由[设计规格](../specs/2026-10-02-database-compatibility-design.md)维护；源码事实与统计口径见[盘点](../../audits/2026-10-02-database-compatibility.md)。本文件规定执行顺序和交付证据，勾选框不能替代功能验证。

## 范围、顺序与交付规则

- 一项主任务、六个阶段工作包：WP1 → WP2 → WP3 → WP4 / WP5 → WP6。WP4 与 WP5 只有在共享映射、并发接口和迁移已稳定后才可独立并行。
- 当前文档 PR 只使用 `Refs #134`。各阶段交付保留父任务 Open；只有全部适用完成条件满足且核对远端 main 后才关闭父任务。
- 每个阶段从最新已确认 main 建独立任务分支，关联 #134。根目录有其他改动时使用 worktree；只提交本阶段文件。
- WP1 的并发版本、快照边界和 Platform 包决策有明确实验产出。没有这些产出，不开始批量修改实体或生成 PostgreSQL 全量基线。
- 同一部署的四个持久化 Context 使用同一 Provider；同连接事务的业务、Outbox 和 Space/WMS 写入保持同数据库、同事务。
- PostgreSQL 新部署与已有 SQL Server 数据搬迁分开。后者、同实例混用数据库、独立 CRM 私有仓库全面改造、现有 demo 切换和生产候选/上线均另立任务。
- 本地服务与测试只使用本任务独占的 loopback 端口和临时数据库。不得把现有业务库连接直接作为测试目标，不替换原环境或覆盖备份。
- 不自动运行 GitHub Actions、Azure Artifact 桥或 DEV/CD。保留 R2/GHCR 发布权威、Build once/deploy many、签名、扫描、环境审批和数据库前向迁移门禁。

## 预计文件边界

下面的新文件是功能实施时的目标路径；WP1 已创建配置/连接/Context helper、集中迁移 profile、设计时配置与限量表探针，实际状态以阶段证据为准。已有路径可在[盘点](../../audits/2026-10-02-database-compatibility.md)中复核，不另建重复状态队列。

| 工作包 | 已有文件 / 目录 | 计划新增位置及职责 |
| --- | --- | --- |
| WP1 | `CP6.WebApi/Program.cs`；`Configuration/CP6ContextDesignFactory.cs`、`CrmIdentityConfiguration.cs`、`ErpIntegrationConfiguration.cs`、`ProductionConfigurationValidator.cs`；`CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs`、`SpaceContextDesignFactory.cs`；相关 csproj 与 packages.lock.json | `CP6.Core/Persistence/DatabaseOptions.cs`：选择与验证；`DatabaseConnectionFactory.cs`：连接生命周期；`DatabaseContextOptions.cs`：注册与已有连接接入；`docs/audits/database-compatibility/WP1-DECISIONS.md`：实证冻结记录 |
| WP2 | `CP6.Core/EFDbContext/CP6Context.cs`；`CP6.Space.Infrastructure/SpaceContext.cs`；`Services/CrmIdentity/IdentityMessagingContext.cs`；`Services/ErpIntegration/ErpIntegrationContext.cs`、`ErpDeliveryReplayAudit.cs`；`CP6.WebApi/Seed/` | `CP6.Persistence.PostgreSql/CP6.Persistence.PostgreSql.csproj`；其 `Migrations/Core/`、`Space/`、`IdentityPriority/`、`ErpIntegration/` 四组迁移与快照；共享映射目录下的 Provider 差异实现 |
| WP3 | `CP6.Entity/BaseBizEntity.cs` 与并发契约；`Services/Common/DocNumber.cs`；`Services/ErpIntegration/ErpSqlLock.cs`；`Services/CrmIdentity/IdentitySnapshotReader.cs`；跨 Context 工厂调用 | `CP6.Core/Persistence/` 下的资源锁、错误分类、编号分配、并发 token 与数据库时钟接口/两库实现；接口按单一职责分别存放 |
| WP4 | `Services/CrmIdentity/`、`Services/ErpIntegration/`、`Services/Sys/RefreshTokenService.cs`、`Services/Pur/PurchaseRequestService.cs`、`Services/Wms/`、`Services/Wf/`；`CP6.WebApi/Services/CrmOidcGrantStore.cs` 及对应后台服务 | 现有测试项目中的共用关系型 fixture、Provider 专项测试与业务回归；不得复制 Platform 消息包源码 |
| WP5 | `CP6.Space.Infrastructure/` 中 Capacity/Lease/Apply/Clone/Publish/CAD 服务；`Services/Mes/MesDashboardDapperService.cs`、`Services/Platform/GdprService.cs`；`Controllers/Sys/DashboardController.cs` | Space 专有 SQL 的两库实现、MES 查询实现与真实数据库回归；按能力分文件 |
| WP6 | `CP6.Tests/`、`CP6.Space.IntegrationTests/`、`CP6.Oidc.IntegrationTests/`、`eng/crm/erp-integration-tests/`；启动种子、数据库脚本、生产配置模板与运维规范 | `scripts/Test-Cp6DatabaseCompatibility.ps1`：显式本地验收与证据汇总；`docs/audits/database-compatibility/` 阶段证据；PostgreSQL 原生备份恢复入口及 runbook |

## WP1：数据库基础与高风险方案验证

**产出：** 配置契约、统一连接/Context 工厂及 `WP1-DECISIONS.md`。不以“连接成功”宣称整体兼容。

- [x] 将 Provider 接口约定固定为 `Database:Provider`：`SqlServer` / `PostgreSql`；旧配置缺省为 SqlServer，显式未知值启动失败，禁止根据连接字符串猜测 Provider。工厂、设计时 EF、Dapper 和生产校验使用同一解析结果。
- [x] 核对实际消费的 `CP6.Platform.EntityFramework [0.10.2]` 源码、包身份、EF 模型与所有消息写入路径。双库实测可保留当前签名包，消费者管理 PG 模型/触发器；记录源码 SHA、包版本及哈希，不用本地旧 checkout 或复制实现替代。
- [x] 在两种真实临时数据库验证两个并发候选：SQL Server 保持原生 rowversion；PG 选择数据库生成的 8 字节 opaque bytea token，SaveChanges-only 应用候选被实际 raw/ExecuteUpdate 陈旧覆盖淘汰。覆盖表和生产安装职责见 WP1 记录。
- [x] 单独验证身份快照分页边界。冻结同事务持久 tenant generation；跨页变化、墓碑/物理删除、跨租户、回滚与真实进程重启通过。保留 byte[] / Base64 / replay 输入；WP3 保留现有 API 外形并通过 v2 cursor 拒绝旧边界；SQL Server generation 为前向迁移。不是实际 API 已接入。
- [x] 为共用连接的 CP6/Space/Identity/ERP Context 工厂完成两库实际 DatabaseFacade 限量表事务试验，Dapper 显式参加相同实际事务；提交共同可见，回滚共同不可见。全量实体/迁移和具体业务调用仍在后续阶段。
- [x] 固定 Provider/Npgsql 依赖版本和 lock；真实命令、服务器版本、源码/运行二进制 fingerprint、成功/失败、清理回执及选定方案/写路径表已保留。

配置契约示例（字段示例不是现有配置实现，也不包含凭据）：

```json
{
  "Database": { "Provider": "PostgreSql" }
}
```

**必须通过：** 缺省/两合法值/未知值四种配置结果；两库事务提交与回滚；token 多写路径及 8 字节 API 契约；快照并发分页；真实 Platform 包兼容性。缺少任一必要结果，保持 WP1 未完成。

## WP2：四个 Context 的映射、迁移与种子

**前置：** WP1 冻结的 token、快照和包方案。**产出：** 可真实安装的 PostgreSQL 基线、SQL Server 回归以及双迁移维护规则。

- [x] 将长度、精度、键、关系、TenantId 过滤与业务唯一性保留为共享模型规则；数据库类型、identity、并发生成、索引谓词、JSON/check、collation 和默认表达式分别实现。
- [x] 为 PostgreSQL 保持 Sys_Lang 的“每个 key 至多一个全局行、每租户一个覆盖”规则；明确 NULLS NOT DISTINCT 或等价 partial unique 实现。逐项核验 95 处 HasFilter 中的业务语义，不统一机械改写。
- [x] 建立 UTC 时刻、业务本地时间、日期、decimal/金额、Guid、bool、Unicode、JSON 与二进制映射；检查 PostgreSQL 标识符长度、命名碰撞、大小写/尾空格和约束名消费者。历史非 UTC 数据转换只接受明示时区证据。
- [x] 新建 PostgreSQL 迁移程序集，在同一程序集内按 Context 分四组迁移/快照；SQL Server 继续使用原程序集、路径和迁移 ID。设计时和运行时必须选择同一迁移集；核对每个 Context 的历史表/schema，禁止错误复用或交叉标记迁移已应用。
- [x] 从当前模型生成 PostgreSQL 安装基线，并逐一核对已有 28 个 migrationBuilder.Sql 文件、必要触发器/索引/约束、消息模型、OIDC 原始 DDL、种子和历史修复的最终业务要求。schema snapshot 不能替代这些核对。
- [x] 在两库空库执行四 Context 的真实迁移及数据库初始化，两次初始化无重复或覆盖管理员设置；SQL Server 另用保留数据的支持版本副本验证前向升级。PostgreSQL 基线发布后建立“前一 PG 支持版本→当前版本”升级夹具，未产生历史版本前不虚构升级通过。

**实际本地结果：** 151/151相关测试与独立索引25/25按原输入复用；PG fresh33/33、既有PG完整限定36/36与另一次报价2/2保留独立执行来源，真实应用首次/重复保持334表与自定义种子。2026-10-03 SQL支持版本136→140四项升级2/2、完整目录/定向写入/负对照35/35、未知history2/2通过；同新API首次/重复exit0/noHTTP及两次seed verify各2/2保持352表/历史/自定义种子。SQL证明路径是旧136空库实际安装后捕获保留数据、前向至140与新initializer首次/重复，不冒称另一次新140空库安装。PG首版没有历史发布版，未执行历史PG升级。30/45任务级review、两P2修复及后加四文件/reader两行增量review按原记录复用；8个owned库清理/全不存在核对完成（[实际原件](../../audits/database-compatibility/wp2-native/wp2-cleanup-final-owned-eight-verified-env.json)），正常提交/PR/remote main核对仍Pending，勾选本地实施项不等于WP2已交付或开放普通PG运行。

原始真实成功/失败、输入及清理证明已归档142份（prepared140＋最终输入字节证明/脚本2），原件/副本hash与bytes全数匹配，见[manifest](../../audits/database-compatibility/wp2-native/manifest.json)。[最终输入比对](../../audits/database-compatibility/wp2-native/wp2-final-review-source-applicability.json)中的75个唯一记录源码字节一致只支持旧review适用性，不称新75文件审查/测试或重复native执行。WP2正常PR及远端main核对仍Pending。

**必须通过：** applied/pending migration 核对；四 Context 历史表/schema；NULL 唯一性、FK、金额与日期往返；种子幂等；SQL Server 升级保留数据。记录 PG 首版无既有 PG 历史版本的适用边界。

## WP3：锁、编号、错误分类与并发能力

**前置：** WP2 可运行的双库模型。**产出：** 小范围数据库能力适配，以及所有调用方清单。

- [x] 事务资源锁分别实现 SQL Server sp_getapplock 和 PostgreSQL 事务级 advisory lock。保持含 TenantId 的规范资源键、锁顺序、超时、失败返回及事务释放；对首次创建/不存在记录互斥不能只改成 FOR UPDATE。
- [x] 为原子领取/跳过已锁记录、修改并返回记录建立独立能力；将 READPAST/TOP/OUTPUT 的使用迁移到语义等价实现。验证容量上限、并发双领取、租约过期与旧持有者续租/完成被拒绝。
- [x] 将 DocNumber ORD 原子递增与首次创建统一为两库分配器，保留现有编号、作用域与事务约定；测试并发首次创建、跨租户共用原全局号流和事务失败，不以不同业务规则的 sequence 替换。
- [x] 分类唯一约束、死锁、序列化失败和乐观并发冲突；保留具体约束名及已知重试条件，验证 PostgreSQL 事务失败后正确回滚/重试。禁止把所有 DbUpdateException 解释成可忽略重复。
- [x] 全量登记 IsSqlServer/ProviderName 分支：标明真实跨库实现、专属能力与测试回退。本阶段替换通用能力中直接跳过PG锁的路径；业务专属拒绝/查询按逐文件矩阵交WP4/5，普通PG运行guard留至WP6。保留已有条件ExecuteUpdate，完整消费者真库验证在对应业务阶段完成。

**必须通过：** 事务锁释放/超时/隔离；首次创建互斥；原子编号；唯一冲突分类；租约抢占与旧 owner fence；两库真实并发 token。串行测试不能替代并发试验。

本地阶段结果、失败/复用与151个原始路径证据见[WP3实施记录](../../audits/database-compatibility/WP3-IMPLEMENTATION.md)。WP3现已由PR146完成提交/合并/远端main核对及必要冒烟；本节勾选不代表后续WP4–WP6与父任务完成。

## WP4：身份、ERP/WMS、采购和工作流

**前置：** WP3。**产出：** 关键业务链使用统一能力，接口和安全语义保持一致。

- [x] 适配 CrmOidcGrantStore 的 grant/logout ticket 原子消费与 refresh 轮换；验证两个并发请求仅一个成功、旧 token 重放被拒绝、全局登出/撤销以及精确 hash 比较。
- [x] 适配 IdentitySnapshotWriter/Reader、priority dispatcher、ERP request/replay/order bridge。验证重复消息、业务幂等键/不同 payload 冲突、乱序、重试、死信和授权重放；所有需要原子性的业务与 Outbox 共事务。
- [x] 复用 WmsProductionSqlServerTests 的业务断言，在两库执行 MOVE/LPN/serial/补货/审批与台账对账；库存通过领域入口操作，不直接更新库存表。验证权限查询实际翻译及失败关闭。
- [x] 适配采购审批 callback、OA/WF job/notification worker、刷新/撤销/后台 retry。验证同一任务只被一个 owner 推进、失败回滚及重放不重复写入；保持租户过滤和权限作用域。
- [x] 财务、采购和 ERP 相关 EF 查询也运行两库回归，尤其金额精度、乐观并发、预算版本、编号与事务一致性；没有 raw SQL 热点不等于已验收。

**必须通过：** 认证一次消费、撤销与轮换；ERP/消息原子性；WMS 台账与业务一致性；采购审批幂等；工作流领取；真实租户/权限隔离。外部 CRM 消费端未参与时记录其未验证范围，不代称 CRM 已兼容。

WP4上述五项已按实际双库/专有门禁分批通过，集中代码审查无实质阻断；PR149已正常交付并核对远端main包含性/完整树，合并后SQL/PG各2/2（含setup），六库普通DROP及不存在核对完成。[实施记录](../../audits/database-compatibility/WP4-IMPLEMENTATION.md)保留各次范围、失败、复用与交付原件。父任务仍Open，不将阶段完成扩大为整体兼容。

## WP5：Space/CAD/AI/发布与报表

**前置：** WP3；依赖 WP4 的 WMS/ERP 集成场景按其交付版本验证。**产出：** Space 与专有报表的双库实现。

分支`codex/db-compat-wp5-20261003`从PR149远端main建立，现已由PR152交付远端main。Space与报表各自owner-marked两库已完成适用业务、CAD/克隆并发、历史迁移与目录专项；下方勾选仅表示本地实施及相关验证完成，具体各次成功、失败和复用范围见[WP5实施记录](../../audits/database-compatibility/WP5-IMPLEMENTATION.md)及[首轮原件](../../audits/database-compatibility/wp5-native/initial/manifest.json)。

- [x] 适配 Space 编辑/Underlay/CAD/AI apply/retention/validation/publish 等资源锁、容量 ledger 与租约，消除 PostgreSQL 上跳过锁或执行 T-SQL 的路径。
- [x] 为 EfSpaceVersionClone 的表变量/NEWID/批量复制提供 PostgreSQL 等价实现；验证 source map、名称/类型字段、版本 token、失败原子回滚与幂等，不以简化克隆替代完整行为。
- [x] 验证 Space 发布/历史 republish/补偿恢复、CAD provider revision fence、外部授权、文件安全与 AI 容量。保持 CP6Context/SpaceContext 同连接事务及 published 版本/库存来源一致性。
- [x] 为 MES 三个 usp 报表、Dashboard TOP 查询、GDPR joined DELETE 与标识符引号选择 EF 或 Provider 查询实现；明确每个 Dapper 查询的租户/全局作用域，不能依赖 EF query filter 自动覆盖 Dapper。
- [x] 逐项复用 Space 真库场景，同时保留 Provider 专项 DDL/错误断言。SpaceSqlIntegrationTests 已接入全量迁移夹具，另用隔离的迁移场景证明安装/升级。
- [x] 完成WP5正常提交/PR合并、必要合并后冒烟及远端main包含性核对，并更新父Issue134的WP5阶段交付状态。

**必须通过：** 双库租约与容量竞争；完整克隆与回滚；发布/恢复/WMS 集成；CAD/外部授权 fence；报表结果、排序分页与权限一致；GDPR 作用域正确。

一次集中审查的两项P2已修复并定向复核；实际PG AI原13＋caller-pending1＋CAD1为15/15，legacy恢复选择器2/2，均零skip。[BUG150](https://github.com/GTX537/CP6/issues/150)已由[PR151](https://github.com/GTX537/CP6/pull/151)交付远端main `522433a370c0c247a98b300c69e88e403217d53a`并关闭，合并后两库原并发各2/2及自有库清理完成，见[BUG150交付原件](../../audits/2026-10-03-bug-150-mapping-idempotency/native/post-merge/manifest.json)。WP5在`97e4a9f2e0881c4f423111848632bbc821cde11d`纳入修复后，整合构建0warning/0error、映射SQL/PG各4/4通过。[定向修复与整合原件](../../audits/database-compatibility/wp5-native/review-followup/manifest.json)保留原RED及首轮异常包装断言失败，不将后续成功改写为首次通过，也不将重复测试相加。WP5交付时普通PG API/worker guard仍保留；WP6的开放和整体验收按下节继续，不扩大WP5结论。

## WP6：本地验收、初始化、恢复与运行配置

**前置：** WP4/WP5。**产出：** 两库验收证据、明确的支持版本与运维入口。此工作包不执行现有环境切换或生产部署。

- [x] 新增本地 runner `scripts/Test-Cp6DatabaseCompatibility.ps1`，契约为 `-Provider SqlServer|PostgreSql`，连接仅从明确的测试环境变量/本地安全配置读取，不打印凭据；数据库名称由 runner 独占创建并记录，cleanup 只操作该清单。
- [x] 共用真实数据库 fixture 参数化运行；所选数据库不可用立即失败。分别改造 SqlServerFact、OIDC 筛选 DDL 和 ERP fixture 的适用边界：共同行为两库执行，专有断言各自保留。必需两库场景零跳过。
- [x] runner 执行安装/重复初始化/适用升级、类型/索引/安全/并发/消息/Space/报表，以及隔离 API 启动与健康/身份核验；结果按 Provider、Context 和业务能力归档，不用总测试数替代逐项覆盖。
- [x] 保留 SQL Server .bak/CHECKSUM/VERIFYONLY 路径，增加 PostgreSQL pg_dump/pg_restore 路径；两种原生备份均恢复到新隔离库，验证 API 启动、数据/消息/权限/密钥可用及对账。本地恢复不代表跨数据库数据搬迁。
- [x] 更新 Provider 配置与生产校验、db-init、Compose/Kubernetes 输入、备份恢复 runbook、R2/DevOps 的数据库支持说明。保留正式候选、签名、漏洞扫描、环境审批与真实恢复/生产门禁；不得把本地结果写成 R2/GHCR 推广凭据。
- [x] 完成一次任务级完整 diff 与专项审查，记录最终源码 SHA、真实版本/命令、结果、失败记录、证据/发布文件哈希及未验证范围；按仓库策略复用输入未变的成功结果，复用必须写明来源。
- [x] 各阶段实现、必要测试、运行文档已由正常PR交付远端main，WP6 PR165完成合并后双库冒烟；本次结案原件正常合入并核对后关闭 #134，活动状态以Issue为准。

已实现本地 runner 的调用示例。连接须通过受控进程环境变量或显式私有配置提供；每次采用全新绝对运行目录，两个 Provider 串行执行并分别归档。以下是使用示例，不改写已有分项的执行范围：

```powershell
pwsh -File scripts/Test-Cp6DatabaseCompatibility.ps1 -Provider SqlServer -RunDirectory (Join-Path ([IO.Path]::GetTempPath()) ('cp6-sql-' + [guid]::NewGuid().ToString('N'))) -Phase Full
pwsh -File scripts/Test-Cp6DatabaseCompatibility.ps1 -Provider PostgreSql -RunDirectory (Join-Path ([IO.Path]::GetTempPath()) ('cp6-pg-' + [guid]::NewGuid().ToString('N'))) -Phase Full -PostgreSqlSslMode Disable
```

验收输出必须报告必需场景零跳过、无 pending migration、隔离 API 实际启动/HTTP 结果和原生恢复对账；失败返回非零退出码。准确参数、连接变量、实际观察版本和恢复边界见 [运行手册](../../devops/DATABASE-COMPATIBILITY.md)。可选择 Matrix/Application 分项，但不能把不同运行合称单次 Full；完整覆盖需要逐项核对所选 Provider 的全部必需入口。

## 父任务关闭清单

| 编号 | 两库共同完成条件 | 证据要求 |
| --- | --- | --- |
| AC1 | 同源码、同外部业务契约，四 Context 使用同一 Provider；错误配置拒绝 | 配置与事务测试、源码 SHA |
| AC2 | 独立迁移/快照，空库安装、重复初始化和适用版本前向升级 | applied/pending 列表、升级前后数据摘要 |
| AC3 | NULL/唯一/FK/标识符、Unicode/JSON/金额/日期与并发 token 正确 | 两库正反向与边界测试 |
| AC4 | 租户/权限隔离、身份轮换/撤销/一次消费、快照边界安全 | 真库安全与并发证据 |
| AC5 | 编号、业务锁、租约、ERP/Outbox/WMS/OA/WF/财务一致性 | 并发、重复、乱序、失败回滚/恢复结果 |
| AC6 | Space 发布/克隆/CAD/AI/容量及报表完整行为 | 两库专项集成结果 |
| AC7 | 原生备份恢复到隔离库，API 与数据库身份/对账可复现 | 哈希、restore 记录、实际 HTTP 与密钥/权限验证 |
| AC8 | 所有必要结果真实、零跳过、失败与复用来源保留；代码/测试/文档到远端 main | 任务级审查、远端包含性；明确本地与生产边界 |

## 本次建档验证与后续执行入口

本次只创建盘点、设计、计划并同步项目记忆。适用验证为 Markdown 相对链接、差异/范围、工作流事件过滤、正常 PR 保护与远端 main 包含性；不编译、不运行数据库/业务测试、不重建镜像。

执行入口为 WP1；其后每个阶段按本文件前置条件推进。设计方向已获用户接受，无须为相同范围重复询问；外部生产动作、已有数据搬迁或新增远程 Actions 运行仍遵守仓库明确授权规则。

2026-10-02 建档分支本地执行 `python -X utf8 scripts/check-docs-links.py`，参数为本次修改的盘点、设计、计划、任务导航及四份项目记忆：**8 个 Markdown 文件 / 385 个本地链接 / 0 个错误**（仅检查目标路径，不校验标题锚点或外部 URL）。`git diff --check` 通过。源码计数来自前轮静态检查并按基线复核；没有重新执行任何历史业务验证。13 份 GitHub workflow 与受保护 main 相同，普通工作流手动入口及 release tag 过滤保持；Azure 上游桥为手动入口，未触发其 DEV 下游。工作流结论限定仓库 YAML，未核验平台外部 UI 触发覆盖设置。

本次一次任务级集中审查覆盖全部八份文档和三份新增正文，未发现实质阻断项；确认阶段依赖、当前 ERP/历史 CRM 来源边界、父 Issue Open 与功能未开始记录一致。收尾只补记该审查结果，链接目标和业务输入未改变，复用上述真实链接检查；没有扩大为业务构建或双库验收。
