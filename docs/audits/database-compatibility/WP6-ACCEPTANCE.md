# DB-COMPAT-01：WP6 最终本地验收

状态：本地适用验收、WP6 正常远端代码交付和合并后双库冒烟均已完成。父任务只在本次结案原件正常合入并核对远端完整包含性后关闭，活动状态以 [Issue134](https://github.com/GTX537/CP6/issues/134) 为准。下文各原始报告的历史待办状态保留，不改写当时证据。

## 正常交付与合并后验证

[PR165](https://github.com/GTX537/CP6/pull/165) 于2026-10-04 02:49:23 UTC正常合并至远端main `8b64bc8a510e9ad4a82c21c8c6ac13c138e332c3`。候选 `4243c67d7ac2e3cfafb809d2a4efba54f4ec927a` 是其ancestor，完整树均为 `68f7d4accfc58897a6bb430f5f90dded50c4ba94`；24页共2,389个PR文件逐项匹配候选Git blob。没有管理员绕过、force push或分支保护变更。第一次push网络reset确认远端未收到，单命令HTTP/1.1重试成功；失败记录保留。

必要合并后冒烟在该main上串行新执行，复用原e940实际构建的261文件API，每库全新owned数据库：一次实际初始化、精确迁移history、所属PID/API三个health端点、管理员登录及12项权限/改密/401/403验证均通过。HTTP新建用户均从确切owned数据库原生查得；两个新库普通DROP并确认不存在。它证明主线集成及应用运行，没有重建或重复完整矩阵/恢复验收；执行main与原构建source分别记录。

第一次PG冒烟的本机辅助脚本通过PowerShell属性读取连接字符串，丢失SSL Disable覆盖，原生psql退出2、未进入建库；纯内存复现后改用正式runner已采用的set/get方法，使用新目录成功。原Failed summary和Planned receipt字节保留，该原计划名经真实只读查询确认不存在；保护方法拒绝把Planned receipt称为owned的诊断也保留。没有改动CP6源代码、数据库服务认证或生产TLS要求。

[交付/合并后原件清单](wp6-native/post-merge/manifest.json)保存155份原件，manifest SHA-256 为 `CBC510512DD8E310E4A6A09647AD1E16861C32FD98F482376E021735AC983B24`。其中[远端完整包含性](wp6-native/post-merge/delivery/wp6-pr165-remote-main-verification.json)、[PG新冒烟](wp6-native/post-merge/wp6-pg-postmerge-application-smoke-ssl-fix/summary.json)、[SQL新冒烟](wp6-native/post-merge/wp6-sql-postmerge-application-smoke/summary.json)及[原失败目标不存在](wp6-native/post-merge/delivery/wp6-postmerge-initial-pg-failure-absence.json)按实际source/状态记录。当前AC1–7及AC8的代码、测试、原验收和必要合并后部分已有直接证据；新增结案文件还须正常合入后核对包含性，随后关闭父任务。

## 当前代码与实际执行

执行源码是洁净提交 `e940825fc7511b41a1a80b4b73897dcc69e51bbb`，已接入 BUG153/155/157/159/161/163 的正常主线修复；已确认主线基线为 `6322ac152cab7d462719169fb0301f49e9ee39ba`。同一套业务代码按 `Database:Provider=SqlServer|PostgreSql` 选择数据库，完整 API/worker 的临时 PostgreSQL guard 已移除。

| 实际独立运行 | 必需入口 | 原始 summary SHA-256 |
| --- | --- | --- |
| PostgreSql Matrix | 34/34 | `1A7BD97B1F5BE86C2F2698EB74B7F7BBBE4DFEBB3688C84FE93D397B24BE5820` |
| SqlServer Matrix | 37/37 | `9AE0EFDD2574172CBCBA8057C883E1F508E7AA9EF67336C4767459CF1CF5448D` |
| PostgreSql Application | 17/17 | `6176743970FE5D253CFD3D5CF8DB3CF9B7776FF7DEECAA18BF7C693D6DA8969D` |
| SqlServer Application | 17/17 | `73B93C4341F2C3197CEF949032C683BD402F44F4055CEBB98D8BD33C58DCF38B` |

四次独立运行共覆盖 SQL54、PG51 个适用入口，全部精确名称通过、零失败/跳过。各原始 summary 的 `FullAcceptance=false` 保持；完整适用覆盖由逐入口核对证明，不把四次分项称为一次 Full。必需清单仍为62个定义入口，WMS 原八项加新增十七项为25项，其他61入口不变。

实际观察的环境：Windows、PowerShell 7、SDK `10.0.302`，应用目标 `.NET 8`；SQL Server `16.0.1000.6`（2022），PostgreSQL `18.6`。EF Core `8.0.30`、Npgsql EF Provider `8.0.11`/driver `8.0.8`、SignalR Client `8.0.12` 固定依赖按真实 lock/build 记录核对。这些是已验证的本地版本，不外推其他服务器版本或生产环境。

PG Matrix 实际 restore/build 七个项目。SQL Matrix 在全部2698矩阵输入、2059个运行文件逐字节核对后复用七项成功构建；SQL136旧基线另外真实构建/安装。PG Application 另行真实构建 WebApi、ApplicationProbe、MigrationProbe 并发布当前 API；SQL Application 复用该新产物。两库全部261个发布文件相同，`CP6.WebApi.dll` SHA-256 为 `D226FA352B4E207574948FBA53EBCA7CBFE0A66541178F1ADB737380030DF9D1`。文档、归档和后续提交变化不称为新构建；复用须保持编译输入和实际运行文件一致。

## 按接受的八项条件核对

| 条件 | 本地结论与实际覆盖 |
| --- | --- |
| AC1 同源码、配置及四 Context 接线 | 当前已构建程序集另行执行配置/工厂/接线/初始化模式/生产校验139/139，零其他结果；含默认值、两合法值、非法值、四设计工厂与迁移所有权。两库共享连接+Dapper提交/回滚原生入口及相同发布文件通过；WP6没有改动公开DTO/controller业务合同。 |
| AC2 迁移、安装及升级 | PG schema37、SQL schema35及未知history负对照各2通过；两库当前空库实际首次/重复db-init与种子保持。SQL另有真实已填充136→140前向升级2项。PG首版没有已发布旧PG版本，不能声称历史PG升级已执行。SQL保留Core/Space原迁移所有权，PG使用四个独立history。 |
| AC3 存储与并发语义 | 每个模型表/列/约束/存储映射及数据库token生成器逐项核对；原生NULL唯一性、租户FK、Unicode/JSON、金额/时间/日期正反向与边界写入通过。8字节opaque token含EF/raw/ExecuteUpdate及stale拒绝。PG微秒精度不承诺SQL额外100ns精度。 |
| AC4 租户、身份及权限 | 每库OIDC27、identity28、原生权限/数据范围及分页边界入口通过；并发一次消费、轮换、撤销、双租户、重启与恢复后受保护游标、真实HTTP401/403和改密路径覆盖。 |
| AC5 核心业务、锁及消息 | 每库ERP95、handler2、核心25、WMS25，编号、资源锁、共享事务、错误分类和领取/租约/fence入口通过；PG实际40001专项2保留。生产Platform竞争dispatcher、重试/十次失败死信、workflow并发/回滚与实际通知投递均按精确case覆盖。 |
| AC6 Space与报表 | 每库克隆23、设计发布41、任务/生成/保留23、CAD/资产/协作25及AI/容量/映射/历史专项、MES/Dashboard/GDPR报表入口通过；适用的SQL历史及PG前置顺序分别验证。 |
| AC7 原生独立恢复 | 每库真实原生备份/新库恢复、4次所属API启动、28项HTTP，共56项HTTP与8次API启动。SQL352表/6532行/6序列、PG334表/6349行/10序列全部对账；history/持久key/权限/游标保持，恢复待发消息一次领取投递后零重复重放；真实双用户WebSocket投递和隔离通过。 |
| AC8 可审计交付 | 本地零必需跳过，原始失败、真实复用及集中审查/定向复核保留。PR165正常main交付、完整树/所有文件blob及双库合并后冒烟和清理均已核对；本次新增结案文件正常合入并验证后关闭父任务。 |

[逐条件证据绑定](wp6-native/wms-final/development/wp6-wms-local-completion-audit.json)明确AC1–7本地成立、AC8远端部分尚未成立；绑定全部105份真实入口及断言源位置。[精确入口审计](wp6-native/wms-final/development/wp6-wms-final-evidence-verification.json) SHA-256 为 `92AAABA85F4BDEB96C26C33F039972E29745FF143C4126732C054031F119054B`；[初始化/实际HTTP/独立恢复专项审计](wp6-native/wms-final/development/wp6-wms-final-application-recovery-audit.json)为 `D5B89E6A9EB39239660D825C68ABA8679595DE3DF82DD2943BD8121A5D4E7BDF`。两项审计重读已执行原件，不计为新native执行。

## 原件、失败、复用及审查

[归档清单](wp6-native/wms-final/manifest.json)逐项记录来源、字节、SHA-256、原Status与Scope。13次正式运行包含此前六次失败及旧成功；首轮ERP失败、prototype和配置/工具失败同样保留，不以最终成功覆盖失败。每份复制与原件字节须一致。新配置139项的[公开摘要](wp6-native/wms-final/development/wp6-wms-final-configuration/public-result.json)省略所有theory参数，逐项保留原完整名称SHA-256和实际结果；含模拟连接参数的原result/TRX留本地，原TRX SHA-256为 `0012EE37947278B3485CBD5EDA3115CF565A4CFC49DBE11402A0050EC7DA9FEE`，不将公开摘要称为原TRX。

本轮Matrix的11个PG、16个SQL库及Application各2个库，共31个独占临时库已普通DROP，最终均 `AbsentVerified`。清理重核receipt/owner/principal/physical identity与会话；限定等待不结束他人会话。此前失败库的单独清理及原失败summary/TRX不变证明按原记录保留，不能把清理成功变成业务成功。

WP6只进行一次任务级集中审查，分区报告覆盖fixtures/results、application/process、lifecycle/entries及root runner；四项实质问题按定向修复复查闭环。后续BUG整合、清理限定等待、WMS25和SignalR依赖差异的[限定复核](wp6-native/wms-final/reviews/wp6-review-wms-cleanup-followup.md)及[root适用性核对](wp6-native/wms-final/reviews/wp6-review-wms-cleanup-root-verification.json)保留，未发现剩余P0/P1/P2阻断。原审查中的待native状态保留为当时时点；当前结果由上方实际终态证明。后续仅文档/归档变化检查差异、链接、逐文件hash与Git blob，不为SHA改变重复native验收。

运行配置、实际参数和恢复边界见[运维手册](../../devops/DATABASE-COMPATIBILITY.md)。普通GitHub/Azure验证未运行或取消；推送/PR/合并前仍须逐次核对17个触发及依赖输入、实际运行队列。现有R2/GHCR生产候选、签名/扫描、审批和SQL生产基线保持。上述全部是本地隔离验收，未执行已有数据搬迁、环境替换或生产部署。
