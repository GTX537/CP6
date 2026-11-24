# WP04-32 交付记录

EXEC5 R1 · CP6-EXEC-5 · LAPTOP-3QQ44FJS · 固定 DEV-1 · `codex/wp04-32`。本机定向验证已通过，候选交 COORD5 独立验收。产品 PR 停在 Draft，关卡状态由总控按正式流程处理。[正式决议](https://github.com/GTX537/CP6.AI-Handoff/blob/2f002a3d4fe0c162a69dbf823cc455ef88aa0f94/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261124T091143Z-coord5-wp04-32-decision/MESSAGE.md)

## 验证锚点

| 对象 | 完整提交 | Git 树 |
| --- | --- | --- |
| CP6 远端 Main 与正式基线 | `671629ff46a4f781de3e94749250ca3beae354b8` | `78f10d44825a4a48c3725795af0f29c473a6ed4b` |
| 构建和实测的产品提交 | `5fcc72c76a947abd5196ee07e560b248eaa02ce1` | `4a28d03b90dc53b4460d84654f5fdb00055cbed8` |

Platform 远端 Main 为 `f3fbe4be67d40641f7a8a969d74be65a95541819`。已复核决议的 16 个前置采用提交均在对应主线祖先中，包括 WP02-13b。DEV-1 开工登记时间为 2026-11-24 09:21:04 UTC。包含本报告的最终草稿 head/tree 在 PR 描述和本机 `draft-readback.json` 中回读。

## 变更和写者目录

- PostgreSQL 前滚迁移 `20261124100000_SalesOrderSourceV1` 增加 Sales 原件、调用方交付的确认范围和交期、原键回执、原 Outbox 和发布水位。租户与持久化键一起约束，版本原件和 Outbox 保留原字节。迁移在本机合成库验证。
- `OrderSourceParticipant` 在调用方已授权的 PostgreSQL 连接和事务中参加 Confirm、初始 H10 Release 和 H32 Control。原版本、回执、Outbox 及水位在同一 SQL 写语句中提交。当前发布和发运来源门锁定同一个原订单与血缘，读取旧回执独立于当前适用性。来源门结果是本地 Sales 事实。
- `OrderSourceReader` 沿 `OrderReadOwnerV1` 的当前会话、租户、对象、字段及最终复核读取旧行、原交期事实、旧商业可用字段和原键回执。完整旧单读取要求整单所涉行可见，交期和回执按精确行范围检查。旧事实缺证为 `UNPROVEN`，已有可读对象的未见原操作结果为 `UNKNOWN`。

| 路径及原 Owner | 当前接线和证据 |
| --- | --- |
| `OrderController` → `OrderService` → `OrderWriteOwnerV1` 创建、更新、软删除、取消 | PostgreSQL 原图和原写事务内保存可见前后原件。新建同 SKU 两行的原生 GUID 独立，软删除后经当前权限可读旧版本。旧改单沿原业务行序重建，只记录数据库实际原生身份和版本，跨改动的业务身份延续保持 `UNPROVEN`。取消的原生结果及整体失败回滚回归已运行。 |
| `ErpQuotationOrderFactory` → `OrderService.CreateForIntegrationAsync` | C03 原事务记录请求 ID、请求版本、报价 ID、已校验的原报价版本、输入指纹和新单原件。原 C03 回执仍由原 Owner 处理。已复跑正常提交、重投保留一份原结果和外层回滚。缺历史逐行 QTN 分配凭据的覆盖保持 `UNPROVEN`。 |
| `OrderPriceCorrectionOwnerV1` 的原批量入口 | 原事务保存涉及订单的前后版本及原操作坐标。定向验证商业条款变化后原供给来源保持可用。原价格更正回执及后续 workflow 的处理沿原有入口。 |
| `BackorderService.CloseRemainingAsync` / `SplitToNewOrderAsync` | 连接原事务。拆欠交路径从实际创建的原对象记录父子订单和原行 GUID，以及前代版本。拆分已在 PostgreSQL 验证。单独 close-remaining PostgreSQL 场景记 `NOT_RUN`。 |
| `PlateMoldService` → `ToolOrderLinkOwnerV1` → `OrderWriteOwnerV1.EnlistToolCreate` | 原参与事务内仅在实际新建时记录原工具来源及原订单回执，复跑原建单同键一次场景。已采用的修改、取消意图仍按原请求回执处理。对应场景本轮 `NOT_RUN`。 |
| `Wms/ErpBridgeHook` 实发回写 | 仅观察。Sales 当前源门复核原生已发数量和订单状态。实际 WMS 发运入口采用本端口、历史覆盖水位及其跨 Owner 结果为 `UNPROVEN`。 |

## 本机 PostgreSQL 证据

固定产品提交使用 `.NET SDK 10.0.302` 构建 `CP6.Tests`，以 PostgreSQL 开发配置和确切 `SourceRevisionId` 重建。定向测试从本次独立构建输出运行。PostgreSQL 为 **18.6**，监听 `127.0.0.1:55950`，使用合成业务数据、合成身份和本地事务。构建退出码 **0**，测试退出码 **0**，**8 通过、0 失败、0 跳过**。执行窗口为 09:49:36–09:52:29 UTC。首次调试发现同一测试线程切换合成租户后需要重建当前请求上下文，固定提交上的复跑结果已覆盖该条件。

- 三个新增定向用例覆盖旧订单来源和权限、Confirm/H10/H32 原件及回执、欠交父子身份。另运行原 C03 两个、版模一个及取消两个正常回归。
- H10 和同键重投使用两个 PostgreSQL 连接。第二连接实际等待第一事务的锁。放行后返回同一原回执，保存初次原 Envelope。H10 来源及 Outbox 同事务 `1116`。H32 来源、Outbox 和当前水位同事务 `1127`。
- 回滚 H32 后水位仍为 1，原键没有提交记录且回查为 `UNKNOWN`。提交同一 H32 后水位为 2，历史 H10 仍返回原回执，迟到 H10 或发运来源查验返回 `FENCED`。精确的当前 H32 可通过本地控制发布源门。
- 权限验证由标准合成身份调用实际订单读 Owner。另租户读被拒，原租户重新发起读取取得同一旧原件摘要。当前旧单的交期原始字段与已证明的 Schedule 分开保留。

本机证据目录为 `D:/CP6-test-artifacts/WP04-32/exec5-r1/`。

| 原件 | 内容 |
| --- | --- |
| `candidate-run.json`、`candidate-build.log`、`candidate-test.log` | 固定版本、构建和验证命令条件、退出码 |
| `results/wp04-32-candidate.trx` | 八个用例的原始执行结果 |
| `candidate-assemblies.json` | 四份实际测试目录程序集的 SHA-256、ProductVersion 和生成位置 |
| `candidate-proof/sales-source-evidence.json` | 旧单原摘要、释放/控制回执、来源与 Outbox 的 PostgreSQL 事务号 |
| `candidate-proof/original*Body.json`、`candidate-proof/original*Envelope.json` | 合成 H10/H32 原字节，已各自与原回执 SHA-256 比对 |
| `candidate-cleanup.json`、`draft-readback.json` | 本任务监听和进程检查、最终 Draft 精确标识和证据索引 |

## 原验收剩余出口

| 原出口 | 本机结果与界限 |
| --- | --- |
| ORD R01 003、005、006、035 | 已有原生实体身份、精确原件恢复、同 SKU 不同行及合成已证明 Schedule 的读取已验证。存量正式交期、ReleaseLineage 全覆盖、丢失的旧行映射为 `UNPROVEN`。 |
| ORD R01 009、011、012、015 | 原键同事务、并发重复、原字节、回滚后未知、仅价格更正保持本地来源已验证。普通非初始 SourceOperation 需要 Planning 原终态与完整前驱证明，该范围保持 `UNPROVEN`。 |
| ORD R01 013、014、032、OD-R02-TC-084、OD-R03-AC-066、OD-R04-TC-005 | H32 与迟到 H10、当前发运来源查询共用的本地 Sales 门已验证。跨 Owner 的 W1-DEM-03/08、RECOVER-01、Planning 对 `ORD-DEM-SOURCE/1` 实际采用及真实运输投递保持 `UNPROVEN`。 |
| ORD R01 031 | 现有会话和当前对象范围下的本地原件及回执读取已验证。新 HTTP、UI 披露及整卡聚合验收 `NOT_RUN`。 |

QUESTION 交 COORD5 核对的准确缺口为实际 Confirm/Release/Control 调用方交付的对象版和有效日期凭据、旧订单行与正式 Schedule 的权威对应，以及 Planning 接收与 WMS 实发采用。当期没有这三项实际采用证据，合成结果只计所列本机用例。

草稿触发检查以候选和精确 Main 的跟踪工作流及实际变更路径为准。四份 Azure YAML 的普通推送和 PR 入口均关闭，已跟踪的下游 DEV 完成事件只选 `refs/heads/main`。近期类似候选 `#1322` 和 `#1324` 的 Cloudflare、Azure check suites 实际运行数及 GitHub 部署记录均为 0。供应端生效配置缺独立回读。本轮仅执行本机验证，已核对本任务临时进程和监听均为 0。批准的停止状态为 Draft，DEV-1 登记留待独立复核。
