# WP04-33 经营产品物料身份与工程映射读取

本地候选已完成派工卡范围，提交总控复核。ERP-EST-01、ERP-ORD-01 的整目标验收另依各业务任务核销。[恢复派工卡](<D:/CP6-test-artifacts/WP04-33/WP04-33-RESUME-CARD-20261126.md>)

## 提交与基线

| 对象 | SHA | Git 树 |
| --- | --- | --- |
| 领取时及交付核实的 Main | `ab571e684dffd3a5e4401a420b059f564d48b99b` | `bf13c1a0d68e20d99b946fdcd25cff25cfd8c2ab` |
| 原冻结候选（已关闭的 #1322） | `606ba8d8506c93c3b87222dddd49cf01a1b4c7cf` | `7f6bc46a3c69820dd592b41e126bf2dfca5b2dff` |
| 本地合并提交 | `a4b4b9fc2e228626da6456ac9e04d3056050c8fa` | `17a5f9f333e834f284bf4f428e3a51d1c7d4c661` |
| 本轮受验源码提交 | `bcbb52e460e5505bc3710333a4e161d605c555af` | `f12fb9a4d15a2acee867e173e5d95a48966d8260` |

冻结候选原基线是 `cb32b3a54c7898d8141faa604c2768ddeff44318`。DEV-1 通过席位工具恢复原分支 `codex/wp04-33`。本地合并的两个父提交依次为原冻结候选和本轮 Main。对照 #435 复核 11 项严格前置的采用祖先，含 Main 的 WP02-10、WP02-13 和拆分余卡 WP02-13b。Platform 的远端主线为 `a07edc24160d10cb1e3936cc6f495ad4195dd8f0`。[前置与原范围决定](https://github.com/GTX537/CP6.AI-Handoff/blob/b59dba8d0581516d10d499b09449aeb018e4fbde/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261124T053106Z-coord4-wp04-33-decision/MESSAGE.md)

交付报告作为单独的本地提交。最终候选 SHA、树、分支读回与受验源码的同 blob 校验记录在 `validation.json`。Main 的 `ProductService.cs` 与候选同 blob，包含已采用的 BUG-1323 修复。[最终验证读回](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/validation.json>) · [独立删除范围](https://github.com/GTX537/CP6.AI-Handoff/blob/58424657ef383d978c14af9b0dce1724c6c3a683/handoffs/CP6-DEV-20261012/tasks/WP04-33/replies/20261124T070459Z-coord4-legacy-delete-extraction-scope/MESSAGE.md)

## 读取合同与缺口归属

按原 UUID 读取经营料号、品目枝号、原单位、状态、原 Owner、版本与摘要。`POST /api/products/identity-owner-v1/qualification` 在既有 Main 读取事务中组合指定的产品、单位绑定、币种政策与映射。确认的 `IDENTITY_SITE_ONLY` 范围携带双方原确认和产品、工程、站点、映射的独立引用。当前门拒绝陈旧采用，在当前读取授权内仍可回读精确历史。[W144 原产品版本](../W144-01/DELIVERY.md) · [W147 单位绑定](../W147-01/DELIVERY.md) · [W149 币种政策](../W149-01/DELIVERY.md) · [W150 双 Owner 映射](../W150-01/DELIVERY.md)

2026-11-26 用户裁决原话如下。[双线与缺口归属决定](<D:/CP6-test-artifacts/COORD/DECISION-20261126-two-lanes-and-wp04-33.md>)

> 接受建议，1.批准 2.b 裁定这四项事实本来就该由别的卡负责产出

| 缺口 | 读取字段与结果 | 产出归属 |
| --- | --- | --- |
| 产品法人归属／适用域 | `legalEntityApplicability = REQUIRED_FENCED` | [WP05-02](../../task-plans/WP05.md#wp05-02) |
| 未证旧映射 | `legacyMappingKnowledge = UNKNOWN` | [WP05-02](../../task-plans/WP05.md#wp05-02) |
| 正式制造资格 | `manufacturingApplicability = REQUIRED_FENCED` | [WP05-32](../../task-plans/WP05.md#wp05-32)，后验采用由 [WP05-42](../../task-plans/WP05.md#wp05-42) 产出 |
| 产品币种绑定 | `productCurrencyBinding = REQUIRED_FENCED` | [WP04-04](../../task-plans/WP04.md#wp04-04) |

四字段位于独立身份读取的 `data` 和组合读取的 `data.product`。原 `formalAdoption`、`missing`、`mapping.state`、`mapping.reasons`、`confirmedIdentitySite` 和 `selectedCurrencyPolicy` 继续提供原合同的状态及精确引用。未提供映射引用时返回 `mapping=null`，旧映射的知识状态为 `UNKNOWN`。精确读取已停用映射可见 `DISABLED` 和当前拒绝原因。合成 HTTP 验证了这两种响应、有效站点确认和显式币种政策的停用状态。

## 旧写者资格 frontier

Main 现有 PostgreSQL 行级触发器 `product_identity_capture` 在 `T_ProductMaster` 的 INSERT、UPDATE、DELETE 原事务中写入资格头和不可变版本。全仓核对了旧产品 API、EF 写入、WMS 序列和原表 SQL 入口。新旧用例比对主账、头与版本行的 `xmin`，并核对回滚、精确历史和操作重放。本轮沿用 W144 捕获和 BUG-1323 删除保存，无新增迁移。[捕获实现](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261026130000_CommercialProductIdentityV1.cs) · [本卡 PostgreSQL 用例](../../../../CP6.Tests/Erp/CommercialProductQualificationPostgreSqlTests.cs)

| 原写入口 | 同事务结果 | 定向用例 |
| --- | --- | --- |
| `ProductService.CreateAsync`（产品 API 与套件部材） | 主件与子件各有初始头，新建回滚时主账与版本共同消失 | `Wp0433_legacy_create_commits_each_material_frontier_and_rolls_back_uncommitted_creation`，`Wp0433_legacy_product_http_keeps_order_identity_and_rolls_back_stale_delete_with_its_children` |
| `ProductService.CopyAsync`（调用 Create） | 新产品独立 UUID 和第 1 版，原产品保留原引用 | `Wp0433_legacy_product_http_keeps_order_identity_and_rolls_back_stale_delete_with_its_children` |
| `ProductService.UpdateAsync` | 更新推进一版，陈旧行版本回滚原图与头。重置转送标志时保留原审批状态和审批标志 | `Wp0433_legacy_product_http_keeps_order_identity_and_rolls_back_stale_delete_with_its_children`，`Wp0433_native_approval_and_transfer_status_writes_share_the_frontier_transaction` |
| `ProductService.DeleteAsync` | 合法逻辑删除时主账和四类子表一起提交。陈旧行版本时主账、子表和头保持原值 | `Wp0433_legacy_product_http_keeps_order_identity_and_rolls_back_stale_delete_with_its_children`，`Legacy_product_delete_commits_related_rows_and_preserves_original_http_reads`，`Legacy_product_delete_conflict_preserves_the_current_graph` |
| 原表审批／转送状态写入边界 | `Status`、`WfApprovalFlg`、`McTransferFlg` 按提交推进，回滚不出版本。状态变化使旧映射引用失去新采用资格 | `Wp0433_native_approval_and_transfer_status_writes_share_the_frontier_transaction`，`Wp0433_exact_http_scope_reads_original_owners_and_keeps_unproven_product_scope_visible` |
| WMS `SerialInventoryService.EnableTrackingAsync` | 无库存启用变更追踪模式，有库存转换同时锁定追踪。同操作号重放保留首次结果和头 | `Wp0433_serial_tracking_writer_advances_the_product_frontier_once_and_recovers_original_result`，`Wp0433_serial_conversion_with_stock_commits_tracking_lock_and_frontier_once`，`Serial_writes_compose_stock_and_current_tenant_authority(enable)` |
| WMS `SerialInventoryService.PostAsync` | RECEIVE、PUTAWAY、MOVE、PICK、SHIP、COUNT、RETURN 共用首次锁定点。七种动作逐项比对提交、重放及拒绝后的资格头与原版本。COUNT 数量冲突时原头、锁定、回执均回滚 | `Serial_writes_compose_stock_and_current_tenant_authority` 的七个动作数据行，`Wp0433_serial_tracking_writer_advances_the_product_frontier_once_and_recovers_original_result` |
| 原表直接 DML 与硬删／复建 | 行级捕获覆盖带版本的变更、等待当前读取事务、墓碑及复建单调号 | `Commercial_product_identity_native_versions_survive_ABA_soft_delete_and_exact_history_reads`，`Commercial_product_identity_real_source_update_waits_for_current_read_transaction`，`Commercial_product_identity_hard_delete_and_reinsert_keep_history_and_never_reuse_a_version` |

状态边界以本地 PostgreSQL 的合成 SQL 写入实测。Main 的产品控制器将状态相关动作收敛到创建、复制、修改和删除。`next-seq` 只处理号码计数，创建入口负责主账落地。WMS 的两个主账写入口见上表。PE 审批及 mcframe7 转送的外部服务链标记为 `NOT_RUN`。

## 原卡 AC 对照

| 原验收 | 本轮证据 |
| --- | --- |
| 同名不同物料分别读取，旧订单引用原身份 | **PASS**。同名检索返回独立原 UUID、品目枝号和版本。旧订单 HTTP 回读在删除后仍携带原 `ProductCd`。 |
| 工程停用、映射或来源换版后拒绝新采用，历史仍可读 | **PASS**。工程停用、站点换版、映射停用和原产品状态变化均有当前门拒绝证据。历史保留原引用、双方确认和原文。 |
| 保持 ERP 主键及工程键各自的业务身份 | **PASS**。真实确认绑定分立的 ERP、工程、站点、映射引用，错配 ERP 原引用返回 412。 |
| 缺 UOM/profile 时按缺证处理 | **PASS**。缺单位的原产品声明值及组合单位均为 null，保留 `VERSIONED_UOM_PROFILE` 缺项。已绑定 `BOX` 的原版本和摘要可准确回读。 |

原报告的八项测试在本轮受验源码上重新执行。以上证据覆盖两目标的本卡提供者出口。整目标原验收登记仍按已采用台账的 ERP-EST-01 170 项、ERP-ORD-01 236 项及后续业务卡执行，整目标本轮为 `NOT_RUN`。[原卡](../../task-plans/WP04.md#wp04-33) · [W310 原余项](../W310-01/DELIVERY.md#wp04-33)

## 本机验证

.NET SDK `10.0.404`，Main Release PostgreSQL 开发配置。锁定还原和构建覆盖 `CP6.Tests` 及其 `CP6.WebApi`、Core、PostgreSQL 等传递项目。构建退出 0，日志记录 174 项警告和 0 错误。运行以新夹具根目录及 `127.0.0.1` 启动 PostgreSQL 18.6 和测试 Main HTTP，使用合成租户、账号、物料、库存、订单及政策。

```powershell
$artifactDir = 'D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z'
dotnet restore CP6.Tests/CP6.Tests.csproj --locked-mode -p:Configuration=Release -p:Cp6PostgreSqlDevelopment=true "-p:ArtifactsPath=$artifactDir/build" -p:UseArtifactsOutput=true
dotnet build CP6.Tests/CP6.Tests.csproj -c Release --no-restore -p:Cp6PostgreSqlDevelopment=true "-p:ArtifactsPath=$artifactDir/build" -p:UseArtifactsOutput=true --disable-build-servers -m:2
$run = Get-Content -Raw "$artifactDir/validation-verified.json" | ConvertFrom-Json
$filter = $run.Arguments[2].Substring('/TestCaseFilter:'.Length)
& "$artifactDir/run-postgresql-tests.ps1" -Label rerun -TestFilter $filter
```

最终实际运行标签为 `verified`。`validation-verified.json` 记录原始 `dotnet vstest` 参数、独占端口和用例名。重跑示例使用新标签创建独立根目录。

| 运行 | PASS / FAIL / SKIP | TRX SHA-256 |
| --- | --- | --- |
| 最终 `results-verified/verified.trx` | **41 / 0 / 0** | `88852597794349e1114ac56f13067dec03631c63d04354eb86af07543695f3cf` |
| 历史夹具修复后的 `results-candidate/candidate.trx` | 33 / 0 / 0 | `3bca77f32719d0eb979fbaad9806b9ec527818aab6ea7c649ccb1733a05dc9f4` |
| 首次扩展回归 `results-final/final.trx` | 31 / 2 / 0。两项历史迁移夹具随后修正并复跑 | `a9edd4e19665c3a42f7a0119911fbde8dfde7252c667c8630874534e82231e08` |
| 缺证字段补齐前的定向运行 `results-red/red.trx` | 5 / 2 / 0。两条 HTTP 读取按预期报缺字段 | `3362cc014cf939aa4ee2189a3328370fd2ffe60e989f735ae9c5acc688b6508a` |

首次扩展回归的两个历史夹具使用现行 EF 上下文准备 2026-10-26 切点数据，写到了后来才增加的 `Sys_FieldAuditLogs.AuditAtUtc`。修正将合成产品、站点及菜单按切点旧表结构插入，原迁移窗口和后续断言照原用例执行。

最终 TRX 含原 8 项、3 项新增定向及 30 项其余回归，覆盖 ProductService、订单、身份读取、工程映射、单位与币种。旧 R4 TRX 的 SHA-256 为 `37cc0bef2acceb7e22206e43b9f391e541b1c35c1d1653db272461a1e60531bc`。本轮另行复跑先前单项币种政策用例。受验文件和程序集摘要、完整 TRX、还原及构建日志在证据目录。[本轮证据](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/validation-verified.json>)

本轮夹具端口为前置 `127.0.0.1:63476`、首次扩展 `127.0.0.1:59121`、夹具修复复跑 `127.0.0.1:62002` 和最终 `127.0.0.1:58109`。停机读回为 `pg_ctl status = 3`、`no server running`，PID 文件缺席，监听和本卡进程均为 0。W144/W147/W150 的相关行为在本轮使用同一合成夹具重跑，旧 R4/R5 及币种政策实例也逐一读回停机。[前置停机](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/stop-red.json>) · [首次扩展停机](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/stop-final.json>) · [夹具修复停机](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/stop-candidate.json>) · [最终停机](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/stop-verified.json>) · [全部实例读回](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/all-fixtures-stop-readback.json>)

集中审查比较精确 Main 与本地候选的完整差异，覆盖权限、租户、当前版本检查、历史、原键、敏感信息和范围。静态工作流审查记录两侧事件、过滤、`uses` 依赖和实际变更路径。[工作流读回](<D:/CP6-test-artifacts/WP04-33/exec-20261126T070650Z/workflow-final.json>)

## 未覆盖项与交接

- **UNPROVEN** 四项业务事实的真实权威来源、真实生产使用及候选的远端采用。
- **NOT_RUN** PE／mcframe7、真实权限或数据、外部运行服务、整目标 ERP-EST-01／ERP-ORD-01、SQL Server、Actions、部署及合并后冒烟。本卡变更路径均为 .NET 源码、测试与报告，Vue 验证为 `NOT_RUN`。
- 本地分支为 `codex/wp04-33`，远端分支保留冻结头。DEV-1 工作区干净，登记任务 `CP6:WP04-33` 为 `active`，席位保留给总控复核。

## 有效时间

本轮从 `2026-11-26T07:04:11.8140249Z` 派工日志创建到 `2026-11-26T07:34:02.0913748+00:00` 的报告快照为 **1790.277 秒**，含实际阅读、实现和本机验证。旧报告明确登记候选阶段 1660.215 秒。其后有效工时缺少净计量，用暂缓前的 `state.json` 从 `2026-11-24T05:42:41.566899Z` 领取到 `2026-11-24T07:28:12.422899Z` 挂起的 **6330.856 秒**保守扣减此前预算，其中包含等待。M 档上限为 14400 秒，原卡 150% 为 21600 秒。至本报告快照累计预算计量 **8121.133 秒**，以该计量开工时尚有 8069.144 秒，按剩余额度计算的 150% 检查点为 12103.716 秒。
