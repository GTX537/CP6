# WP04-32 交付记录

## EXEC5 R6 同 PR 关卡候选

本机 `LAPTOP-3QQ44FJS` 原 DEV-1 的关卡记录供 COORD 独立复核。台账和生成页的 `DONE` 是原 #1325 的卡级候选状态，随受保护合入后生效。当前远端 Draft 为 `cb69a277f428b64628af3899760012ff23667706`，正式采用与席位收尾待合并后回读。台账 `remote_main_sha` 记录已核实的开工 Main `671629ff46a4f781de3e94749250ca3beae354b8`。本候选的精确提交和树由本机回读记录定位。[原 #1325](https://github.com/GTX537/CP6/pull/1325) · [R7 固定签收](https://github.com/GTX537/CP6.AI-Handoff/blob/c39bc804de222fba89061102bab2ecd5a913e39e/handoffs/CP6-DEV-20261012/tasks/WP04-32/replies/20261124T121145Z-coord5-r7-synthetic-acceptance/MESSAGE.md)

### 固定证据与验收范围

- R7 签收的受测提交为 `9142534c8d34e9e117a9cb5d6566f5417b825c44`，Git 树为 `7951cee0806745d97e5303b5675d239aafc8ea66`。隔离回环 PostgreSQL 18.6 为 **15 通过、0 失败、0 跳过**，包括原回归和两个合成补验。原始 TRX 仍在 `D:/CP6-test-artifacts/WP04-32/exec5-r4/results/wp04-32-r4-candidate.trx`，SHA-256 为 `0324bad553dc9c61ee093284a4f90ef202a3f2fe236ebc0dd98e0b640afcd5f3`。原构建选项、过滤和程序集在同目录的 `candidate-run.json`、`candidate-assemblies.json` 和 `draft-readback.json`。
- 从原 Draft 继续仅更新台账、本报告及对应生成页。受测产品、测试、迁移与构建配置按 Git 对象逐路径比对，沿用固定结果。技术通过范围限本卡提供者及本机合成读写。总控 R9 确认卡内技术验收完成，待完成本机关卡候选复核及受保护采用。
- 严格前置按原决议复核 16 项，含 WP02-13b。Main 和 Platform 分别按固定主线与实际采用祖先核对。记录本卡 M 预算 14,400 秒，强制拆分线 21,600 秒。预算记入可复算工作窗上界 **7843.581 秒**，轮次之间的等待剔除，累计 **226420.710 秒**。各窗的依据和起止见 `D:/CP6-test-artifacts/WP04-32/exec5-r6/time-basis.json`。此时点之后的受保护合并与工作区处置按实际时间回读。[原派工及采用清单](https://github.com/GTX537/CP6.AI-Handoff/blob/2f002a3d4fe0c162a69dbf823cc455ef88aa0f94/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261124T091143Z-coord5-wp04-32-decision/MESSAGE.md)

| 原验收范围 | 本卡本机结论 |
| --- | --- |
| ORD R01 003、005、006、035 | `PASS（合成）`。原 ID、版本和旧事实、具合成映射的原 Schedule／H10、同 SKU 排除行、缺证和未知均有原件。真实历史覆盖与迁移仍为 `UNPROVEN`。 |
| ORD R01 009、011、012、015 | `PASS（合成）`。原键重试、事务共同提交或回滚、原 H10/H32 回执和水位、仅改价保留原供给语义。当前旧写者含 C03、版模、欠交及取消。 |
| ORD R01 013、014、032、OD-R02-TC-084、OD-R03-AC-066、OD-R04-TC-005 | `PASS（合成）`。本地发布／动作门、已有控制与有效时间、独立原因及缺证保守行为。H32 生产解除授权、跨 Owner 的实际接入维持 `UNPROVEN`。 |
| ORD R01 031 | `PASS（合成）`。既有当前授权、对象范围、原结果读取。新增 HTTP/UI 为 `NOT_RUN`。 |

ERP-ORD-01 继续开放。实际 Confirm／Release／Control 归 WP04-16／17／18，Planning 归 WP06-13，实际 WMS 发运归 WP10-19，均为 `UNPROVEN`。真实投递为 `NOT_RUN`。OA 保持暂停，下一卡继续未指定。

## EXEC5 R4 历史来源与 H32 解除合成补验

在 `LAPTOP-3QQ44FJS` 原 DEV-1 使用本机回环 PostgreSQL 及完全合成的租户、身份和业务数据。COORD5 独立复核本轮固定候选。整卡状态为 `HOLD`。[R6 固定决议](https://github.com/GTX537/CP6.AI-Handoff/blob/3fcdf9b30a5b9ceaad748b2005c22537ca685d92/handoffs/CP6-DEV-20261012/tasks/WP04-32/replies/20261124T113410Z-coord5-r6-synthetic-verification-decision/MESSAGE.md)

| 对象 | 精确提交 | Git 树 |
| --- | --- | --- |
| Main / base | `671629ff46a4f781de3e94749250ca3beae354b8` | `78f10d44825a4a48c3725795af0f29c473a6ed4b` |
| R3 Draft 锚点 | `527bce5331fd3f501d2172ad85910e0b916717ab` | `ba8999b0ed072b87a28f89a3c2d18ade1f5143ed` |
| R4 固定受测提交 | `9142534c8d34e9e117a9cb5d6566f5417b825c44` | `7951cee0806745d97e5303b5675d239aafc8ea66` |

最终 Draft 的 head 和 tree 可见于 PR 描述及本机 `draft-readback.json`。固定受测提交至最终 Draft 的唯一差异为本节。

### 旧订单历史来源

- **PASS（合成）。** 同 SKU 的两个旧行使用各自稳定 GUID 和原生 RowVersion。合成映射记录固定原订单／行及快照修订、商业订单版、Schedule ID 和原件摘要，绑定明确的 ReleaseLineage／SourceVersion、原 Release／ReleaseLine、来源摘要及操作原键。用现有 `EnlistConfirm` 与初次 `EnlistRelease` 准备原件。
- `ReadLegacyAsync` 核对两条旧行和原快照，`ReadScheduleAsync` 返回 `ORIGINAL_FOUND` 的精确 Schedule，`ReadResultAsync` 返回 `FOUND` 的 H10 `LOCAL_REQUEST` 原回执。核对完整确认、Schedule、H10 body／Envelope 及回执原字节。排除行保持旧事实可读，Schedule／当前源为 `UNPROVEN`，错行原键或未见操作为 `UNKNOWN`，整单 `ScheduleCoverage` 为 `UNPROVEN`。
- 纳入／排除和区间 `[2026-11-24T09:00:00Z, 2026-11-24T10:00:00Z)` 属于测试夹具。时段外夹具覆盖返回 `UNPROVEN`，现有读入口仍可按原键读回历史原件。合成准备回滚时，第二连接只见初始写集，提交后才见旧快照、确认、H10、Outbox 与水位 1。读回前后 PostgreSQL 原生行身份、版本、各原件及本库转移 Demand 行计数均一致。

### H32 同原因解除

- **PASS（合成）。** 独立夹具目录按同租户和完整六元 OwnerRef 定位原 `SourceControl` 与测试专用授权正文，逐份核对摘要、类型、原控制 ID／版本、作用域和授权内容。共享 Sales 规则比较持久来源版、原控制版、Owner、原因、订单版／行／Schedule／谱系与完整动作集合。
- 正向入口为从 PostgreSQL 读回原件后的内部共用规则。时钟在 `12:00:00Z` 生效前未阻断，到点阻断。到 `12:30:00Z` 解除生效，同一原因退出。另有已生效的 QA 同动作原因时，SHIP 仍受阻，原 Sales 原因限定的 RELEASE 动作阻断退出。发布规则在有据场景无控制证据缺口，动作效力另列。
- 缺前驱或解除授权的夹具解析无法提供有据输入。真实默认当前门仍返回动作 `FENCED` 和该解除发布 `UNPROVEN`，旧 H10 发布为 `FENCED`，H32 回执仍可按原键取得。合成回滚的水位由事务内 3 回到持久 2，原键为 `UNKNOWN`。提交后水位 3，原 H10/H32 body、Envelope 与回执原字节可读，无本库转移 Demand 新增。生产入口没有解除授权绑定，本轮的范围是内部规则及默认门的缺证行为。

### 原始结果与未证范围

固定提交使用 PostgreSQL **18.6** 和 .NET SDK **10.0.302**。构建、测试退出码均为 0，**15 通过、0 失败、0 跳过**。原 13 个用例全部复跑，新增两项定向合成用例。历史夹具首跑遇到原生 SKU 键约束，改为在插入时指定同 SKU 后，两项定向用例复跑通过。工作树干净，本任务 PG／HTTP 临时进程和监听均为 0。

本机根目录 `D:/CP6-test-artifacts/WP04-32/exec5-r4/`。原始 `results/wp04-32-r4-candidate.trx` SHA-256 为 `0324bad553dc9c61ee093284a4f90ef202a3f2fe236ebc0dd98e0b640afcd5f3`。`candidate-proof/` 保存合成映射、原始 Schedule／确认及 H10/H32 body／Envelope／回执、合成完整授权和读回前后结果。`candidate-run.json`、`candidate-assemblies.json`、`candidate-cleanup.json`、`draft-readback.json` 保存固定运行和回读索引。

真实历史映射、迁移和覆盖、H32 生产解除授权、实际 Confirm／Release／Control（WP04-16／17／18）、Planning 接收（WP06-13）及 WMS 发运（WP10-19）为 `UNPROVEN`。WP04-14 的新稳定行和交期依原卡。真实投递与新增 HTTP/UI 为 `NOT_RUN`。

## EXEC5 R3 C03 与版模建单来源补证

在 `LAPTOP-3QQ44FJS` 原 DEV-1 补齐两条旧建单入口的 Sales 来源原件断言。R3 提交包含两处测试及本交付记录，交 COORD5 独立复核。整卡状态为 `HOLD`。[固定签收](https://github.com/GTX537/CP6.AI-Handoff/blob/f9aaa3d2621f65df2ae956b38fce98f6ff5f2427/handoffs/CP6-DEV-20261012/tasks/WP04-32/replies/20261124T104845Z-coord5-r4-r2-review/MESSAGE.md)

| 对象 | 提交 | Git 树 |
| --- | --- | --- |
| Main / base | `671629ff46a4f781de3e94749250ca3beae354b8` | `78f10d44825a4a48c3725795af0f29c473a6ed4b` |
| R2 Draft 锚点 | `a4bdeb0caede2146eadabea5f503cc710683c92b` | `b65c0cbaa1afed35a4ec79c3e0e9a9a35a63ce03` |
| R3 固定受测提交 | `a9f4967834826dd0194c2edf309c8a39b17fe3bf` | `0ef143bc7f4414920907e6897142ec119895a81e` |

最终 Draft 的 head 和 tree 记录在 PR 描述及本机 `draft-readback.json`。固定受测提交至最终 Draft 的唯一差异为本节。

- **C03 报价转草稿。** 在 `order-staged` 用同一 PostgreSQL 事务逐份核对 `Order.C03QuotationDraft` 原件、原订单和行 GUID、原生 RowVersion、原报价 ID／版本和请求 ID／版本及输入指纹。独立连接在提交前看不到来源原件。正常提交的 Sales 原件、原订单和行、伙伴使用回执、审计、桥接、旧成功结果与 Outbox 同属外层 WAL COMMIT `1393`。同键重投保留初次原字节及旧 `ErpOrderCreated` 结果。合成中止后来源原件和原成功写集均无持久记录，旧失败结果为 `C03_ERP_UNAVAILABLE`。
- **版模建单。** 通过 `ToolOrderLinkOwnerV1.CreateOrLinkAsync` 验证 `Tool.OrderCreate` 原件的原生订单和行 GUID／版本及新旧版模来源版本。绑定、旧回执、业务行、版模原生连接和 Sales 记录同属外层 WAL COMMIT `1455`。同键重试保留相同原字节与 `ORDER_CREATED`、`NATIVE_ORDER_LINK_ONLY`、`NOT_EXECUTED` 原结果。合成提交前中止同时撤销订单、行、版模连接、旧回执和来源原件，原操作回查为 `UNKNOWN`。

隔离 PostgreSQL **18.6** 使用回环连接及合成数据。固定提交构建退出码 `0`，测试退出码 `0`，**13 通过、0 失败、0 跳过**。原有 12 个用例全部复跑，其中 C03 两个用例加入新断言，另新增一个版模建单用例。清理后本任务 PG／HTTP 监听与进程均为 0。

本机原始 `D:/CP6-test-artifacts/WP04-32/exec5-r3/results/wp04-32-r3-candidate.trx` 的 SHA-256 为 `79c4da2576be9919ded6e94748461fa554c2ca68fa4965dd0ed2460e28291952`。同根 `candidate-proof/` 保存两入口共四份来自 TRX 的原件及其原始来源字节，摘要与数据库记录逐份匹配。`candidate-run.json`、`candidate-assemblies.json`、`candidate-cleanup.json` 和 `draft-readback.json` 保存运行、程序集、清理及 Draft 回读信息。

历史正式 Schedule／ReleaseLineage 及旧行映射、H32 正式解除权威、实际 Confirm／Release／Control 调用与 Planning、WMS 采用继续标为 `UNPROVEN`。真实消息投递及新增 HTTP/UI 为 `NOT_RUN`。

## EXEC5 R2 独立复核整改

固定复核为 COORD5 R3。R2 在 `LAPTOP-3QQ44FJS` 的原 DEV-1 和 #1325 同一分支交付。总控复核前整卡仍是 `HOLD`，产品 PR 保持 Draft。[固定复核](https://github.com/GTX537/CP6.AI-Handoff/blob/a2add888c4b486ae57c0cffc62b419e8bbfb2e1e/handoffs/CP6-DEV-20261012/tasks/WP04-32/replies/20261124T100858Z-coord5-r3-draft-review/MESSAGE.md) · [原单卡决议](https://github.com/GTX537/CP6.AI-Handoff/blob/2f002a3d4fe0c162a69dbf823cc455ef88aa0f94/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261124T091143Z-coord5-wp04-32-decision/MESSAGE.md)

| 对象 | 提交 | Git 树 |
| --- | --- | --- |
| 精确 Main / base | `671629ff46a4f781de3e94749250ca3beae354b8` | `78f10d44825a4a48c3725795af0f29c473a6ed4b` |
| R1 草稿，独立复核锚点 | `9681ed34a225434368eb88604dcea598e23ef9a7` | `a9eea8844c7399ee6ba269f0954608fc3a62763e` |
| R2 构建和测试的产品提交 | `fbaa6cf837eb40d2e4dc4b448f71e1472f9b1976` | `3359588dfffad3d9d07c0f53817433e2d94f22a4` |

包含本节的最终 Draft 精确 head 和 tree 记录在 PR 描述及本机 `draft-readback.json`。产品提交至最终 Draft 的唯一差异为本报告。

### H32 本地当前门

- 复用同一订单与血缘事务锁，从不可变原操作读取 H32。按准确订单、行、交期、血缘、动作集合及 UTC 生效点判定 `BLOCK` 和 `TERMINATE`。实测 `SHIP/BLOCK` 在生效前尚未阻断 SHIP，达到生效点后为 `FENCED`。同一控制的 PLAN 当前结果为 `STALE`，另一独立 PLAN 控制仍可生效。精度超出本地 UTC 表示范围的控制证据为 `UNPROVEN`。
- 水位已推进的旧 H10 保留发布围栏。精确且可核的当前 H32 可以提交给本地发布判断，预定生效的时间保留原值。此判断与业务动作是否已经受控分别给出。
- 对 `RELEASE_OWN_REASON` 比较既存 BLOCK 的 Owner、原因、原订单范围、完整动作集合、可见前版及时间。不同 Owner、原因或动作集合保持原阻断。即使这些坐标对齐，当前也缺少把 `predecessor` 的 OwnerRef 绑定到原控制原件及其解除授权的权威提供者。解除仍为 `UNPROVEN`，原阻断和其它 Owner 的原因保留。TERMINATE 保留其原有效阻断。
- 新用例逐份复核已保存 H10/H32 body、Envelope 与原回执，旧原件仍按原键读取。R1 索引列出的 17 件证据长度和 SHA-256 均重算一致。

### 既有欠交关闭入口

- 实际 Main WebApi 通过现有登录与权限路径调用 `POST /api/backorder/{webOrderNo}/{detailNo}/close-remaining`。正常关闭为 HTTP `200`，原响应含原订单号、原行号、关闭量 `1`、剩余量 `0`。同一行重复关闭为 `400`，未找到的合成订单为 `404`。
- 试验在既有服务的提交点前中止。调用方事务内可同时读到原行关闭效果及 `Backorder.BeforeClose`、`Backorder.Close` 两个版本。第二连接在提交前和回滚后均只见初始行，目录没有这两条原件。正常 HTTP 关闭后，仍是同一原行，两个原件和关闭效果同时持久可读。提取的原件事务号为中止 `1055`、提交 `1065`，业务行的提交子事务 `1067` 保留原值。
- 另一个合成样例中，关闭前本地 H10 来源为 `CURRENT`。关闭后原回执和原字节保持可回查，该来源当前供给为 `UNPROVEN`。旧关闭字段保留原语义。

### R2 验证和未证范围

隔离 PostgreSQL **18.6** 使用回环连接与合成数据。固定提交的构建与测试退出码均为 `0`，**12 通过、0 失败、0 跳过**，其中四个新增用例，另重跑 R1 的八个用例。PG 与实际 HTTP 夹具均已退出，已回读本任务监听和进程为 0。首次核对记录保留了 EF 保存点的不同 XID，最终用独立连接可见性和实际回滚结果验证共同事务。

本机根目录为 `D:/CP6-test-artifacts/WP04-32/exec5-r2/`。原始 `results/wp04-32-r2-candidate.trx` 的 SHA-256 为 `c90716bb8e1f3a1e2586cfd862711de076c00067f56625b33f1edc2fe4db1841`。 `candidate-proof/` 下保留 H32 时点、原因和逐份原字节，欠交提交及回滚的前后原件、实际 HTTP 响应以及原回执与当前状态。 `candidate-run.json`、`candidate-assemblies.json` 和 `candidate-cleanup.json` 记录精确运行与清理条件。

解除前驱权威、正式历史 Schedule 和 ReleaseLineage、实际 Confirm/Release/Control 调用方以及 Planning/WMS 采用仍为 `UNPROVEN`。真实消息投递和新增 HTTP/UI 路径为 `NOT_RUN`。旧欠交 HTTP 合同已按上述三个实际响应验证。

## EXEC5 R1 初稿记录

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
