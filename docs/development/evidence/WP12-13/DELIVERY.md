# WP12-13 触发器与连接器不可变版本及控制审批

**完整 DONE。** 现有流程管理页与 Main API 已接入 Trigger 和 Connector 的准确版本、分动作复核和控制确认。OA-CONN-01 原 AC 核销 5/32，目标开放。 [Main PR](https://github.com/GTX537/CP6/pull/1312) · [机器运行记录](./observations.json) · [COORD #406](https://github.com/GTX537/CP6.AI-Handoff/blob/e0909fb191ff44c8faa7dc54984e6c2b2497c6c2/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T013311Z-exec-wp02-14b-wp12-13/MESSAGE.md)

## 改动及事务边界

- 使用可信环境和租户固定 Root、不可改版本、准确 Flow/Form 依赖、Starter 和来源。连接器版本固定 TENANT/APP、实现及版本、operation、输入输出 schema、映射、目的策略和凭据定位与代次。元数据只返回绑定状态与代次。安全 Owner 保管原凭据。
- Maker 和 Reviewer 在 Main 当前权限、原文可见性及 WF_AUTH 下分开。实际发布的流程固化准确表单依赖，复核人失去该表单原文读权后读不到配置，旧批准不能发布。TRIGGER_PUBLISH、TRIGGER_ENABLE、TRIGGER_RETIRE、CONNECTOR_ACTIVATE、CONNECTOR_RETIRE 逐一绑定对应版本、动作和预览。停用需要当前控制权、原因、完整预览及空 decisionRef。回执、控制状态和审计在同一事务提交。
- 独立读取当前 Root、激活依赖、影响写者和完整成员清单。预览有效期十分钟，最终确认在 PostgreSQL 时钟和同一事务锁下复核。安全清单按页显示状态及相对预览的定位符。历史 Job 缺凭证且可能已派发时显示 PARTIAL_UNPROVEN。
- Worker 读取持久化的来源、版本和 operation。未准入的原 Job 保留，token 不前进。接管停用的旧配置后，历史调用有明确的 410 或阻断结果。既有服务任务、触发器流水与登记的 dispatch 写者共用影响门。ApprovalDispatcher 原有同事务业务回调仍由其既有 Owner 执行。

## 运行对账

产品提交 `99bc5d33e8a2a4ced0614906f0e1fdc3bd0b8b8d`，领取基线 `b2f3cb9f341f6e15a6cbc31dd3b3d721ec947be8`。实际 Main API 和后台队列、现有 Vue 管理页、Chrome、隔离 PostgreSQL 18.6 完成运行。以下 ID 均来自本卡隔离资料。 [定向用例与已脱敏 HTTP 观察](./observations.json)

| 原断言 | 结果 | 运行证据 |
|---|---|---|
| CONN-01 | PASS | 原页面从 options、creation context、MESSAGE 草稿走完发布与首次启用。两个独立账号作出两项准确决定。仅编辑账号不能复核。完整清单的触发、时隙、实例、Job 和在途项均为 0。草稿未产生消息凭据。 |
| CONN-03 | PASS | 独立控制者凭 DISABLE 清单和空决定停用。原发布及启用决定均被退役拒绝，准确退役决定成功。独立 dispatch 推进后旧单 412，过期预览同样拒绝。旧发起调用未建实例。停用后原命令仍按当前读权查询。 |
| CONN-10 | PASS | 在现有连接器页由两个账号分别激活同名 TENANT 和 APP。仅停用 TENANT 后，两项在浏览器和数据库分别显示 DISABLED 和 ENABLED。独立 Job、SENT 写入分别使原清单返回 412。停用回执保留在途 1 项。实际 worker 保存 BLOCKED_CONNECTOR，原 token 保持 Active，尝试数为 0，同名 APP 调用数为 0。准确独立退役决定生效。 |
| CONN-11 | PASS | 修订显示名得到第二个版本，原版本及 generation-1 独立可读。错误秘密字段在入口被拒，普通元数据换代返回 403，原绑定保留。安全标记未出现在本次 26 份 API 运行日志和浏览器输出。 |
| CONN-12 | PASS | 总调用时长必须覆盖传输与内部重试。缺失或达到 300 秒租约的登记均停留草稿，激活审计计数为 0。 |

浏览器触发器 Root 为 `dc7b2a51-0425-4f06-aee7-1405f7757951`，v1 为 `75f3b684-3c2f-44f8-b4e7-c2b45fd261ce`。在真实发布提交成功后丢弃 HTTP 响应，刷新页面查询原键 `2dd25529-28a0-468d-83ee-bdb4e6aa5fc0`，数据库只有一条发布审计。另存 v2 草稿后，返回 v1 仍显示原名和 ENABLED。另一路在 PostgreSQL 已提交后模拟提交确认丢失，撤销新写权，再并发两次重投 `lost-connector`，取得同一原回执，原审计仅一条。跨主体、跨租户不观察到该命令，撤读权返回 403。 [浏览器回读轨迹](./observations.json)

连接器 TENANT 为 `0ec9ca91-e7e3-49da-9903-2a454cc51daf/e29cf779-44ec-4330-8d08-542ec63dbba5`，APP 为 `079a90a6-253b-4147-b24c-7712141a64d2/a7fe00f2-4ce6-45db-ad83-af1a3edc3f74`。原 Job 为 `f06e2757-1fe6-4791-9545-514b1ebc8b68`。在两次不同的事务中分别插入实际 Job 和登记 SENT。Root rowVersion 均保持原值，旧影响确认均取得 `412 IMPACT_CHANGED`。新预览和停用回执能核对原在途，旧按名和可变路径也不能调用同名 APP。 [数据库断言与 Job 回执](./observations.json)

## 提供方及剩余出口

真实产品覆盖 Main 登录、权限复查、WF_AUTH、版本及 Root、独立数据库事务、原命令、实际 Job 扫描和浏览器。测试账号、Timer/Event/Message 来源登记、凭据定位、同名 APP 和外部提供方是按同一合同建立的隔离资料。模拟显示 SIMULATED。声明已采用但查不到准确运行参与者时显示 UNPROVEN，允许保存草稿，拒绝激活。本卡外部业务调用为 0。生产凭据轮换、真实外部派发以及 Workflow/APP/业务 Owner 结果应用分别沿所属合同验证。 [Required 原册索引](../../assessments/20261011/catalogs/engineering-oa.json)

CONN-02 的旧时隙、CONN-18 的发送准入、CONN-29 的在途接管、CONN-30 的预演及导出撤权、CONN-31 的完整 Required 门、CONN-32 的外部即时授权仅记录本卡已跑部分。其余 27 项原 AC 保留 NOT_RUN，无单列必需运行修订。WP12-14 承接真实触发和时隙，WP12-15 承接预演，WP12-16 承接外部准入，WP12-17 承接结果与 Owner 应用，WP12-18 承接失败终态及恢复。 [逐项归属与阶段排序](../../planning/20261117-card-execution.md)

| 后继卡 | 保留 NOT_RUN 的原 AC |
|---|---|
| WP12-14 触发运行 | CONN-02、04、05、06、07、08、09、13、14 |
| WP12-15 预演 | CONN-15、16、17、30 |
| WP12-16 外部派发 | CONN-18、20、29、31、32 |
| WP12-17 结果应用 | CONN-21、22、23、24、26、27、28 |
| WP12-18 终态处置 | CONN-19、25 |

## 验证与采用

- 隔离 PostgreSQL 18.6 定向 **14/14**。实际 API、浏览器和队列覆盖原 Job 扫描、独立事务、同键并发、失权、租户隔离及旧配置接管。相邻 **67/67**，包括 PostgreSQL 上的旧触发器与队列，以及权限标记、菜单、流程 schema 和执行器的单元回归。前端类型检查通过，OA 管理、设计、目录回归 **251/251**。 [准确用例、环境与原始结果 SHA-256](./observations.json)
- 初次扩大筛选时，另有 29 个历史 SQLite 用例在建库处因缺少 `Latin1_General_100_BIN2` 退出。此轮共有 66 项通过和 29 项失败。基线中该模型排序规则及 SQLite 夹具均已存在，失败未计通过。正式的数据库场景使用隔离 PostgreSQL。
- 权限种子最初与其他用例共用菜单数据，已改在全新迁移库以两个租户重复登记，逐项核对九项动作和零项隐式授权。一次中间运行的 .NET 宿主路径指向错误目录，已修正运行参数。最终 TRX 单列实际通过项。
- Web 的组件回归与类型检查复用 `f1dfcf893215a73d109ca8971e9726263713a3bf` 的本机结果。对比了该提交与产品提交的前端源码、依赖锁和配置，输入一致。Chrome 的两页联验在最终产品提交重跑。
- 复跑本卡时，将 `DOTNET_ROOT` 设为 .NET 安装目录，`CP6_PERMISSION_PG_BIN` 设为 PostgreSQL 18 的 bin 目录，另设独立的 `CP6_PERMISSION_PG_ROOT` 和 `CP6_PERMISSION_PG_PORT`。将 `CP6_OA_BROWSER_WEB_ROOT` 指向已安装依赖的 cp6.web，`CP6_OA_BROWSER_NODE` 指向 Node 可执行文件。运行 `dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~OaConn_`。前端目录执行 `npm run type-check` 和 `npm test -- src/views/oa/admin src/views/oa/designer src/views/oa/catalog`。本机原始 TRX 和截图保存在 `D:/CP6-test-artifacts/WP12-13/20261123-013749`。
- 本卡一个 Main 分支 `codex/wp12-13`、一个受保护 PR、DEV-1。合并时核对候选树，采用后在同树复跑、核实清洁工作区和分支处置。最终采用 SHA、冒烟结果及正式 finish 回执在原执行分支回读。 [Main PR](https://github.com/GTX537/CP6/pull/1312) · [原执行分支](https://github.com/GTX537/CP6.AI-Handoff/tree/codex/dev-handoff-20261117-exec-wp02-14b/handoffs/CP6-DEV-20261012/tasks/WP12-13/messages)

## 工时与阶段

按 #405 原 DELIVERY 的完整 SHA 和路径查重，WP03-11b 单次补记 **186.821 秒**至 **3698.098 秒**，原目标复核 1.124 秒单列。本卡开工前计划累计 **170997.052 秒**。 [原签收 #405](https://github.com/GTX537/CP6.AI-Handoff/blob/a113d70ae789097199badcf0b80425e59de6ca7a/handoffs/CP6-DEV-20261012/tasks/WP03-11b/replies/20261123T012657Z-coord-acceptance-ack/MESSAGE.md)

48 小时检查点 `2026-11-23T02:07:52.216945+00:00` 达到 172800.000 秒，截点内本卡用时 1802.948 秒。远端登记卡 94/391，原号与完整 DONE 均 78/375，目标 10/125，计划累计新增拆卡 10，较上一截点新增 3。自本计划起点关闭 55 张登记卡、10 个目标。缓冲累计 4703.009 秒，本卡等待 0 秒。 [采用台账](../../roadmap.json)

额度 28800 秒，43200 秒强制拆卡。本卡候选 `2026-11-23T02:40:29.922293+00:00`，卡内有效 **3747.318 秒**，目标逐项复核另列 **13.335 秒**，计划累计 **174757.705 秒**。快照后正式收尾由原链记录。阶段 OA 按 WP12-01 至 WP12-20 登记，阶段 C 的完整结束记录保留。复算候选为 WP12-09（4）、WP12-16（4）、WP12-05（2）、WP12-14（2）、WP12-15（2），下一项正式派工归 COORD。
