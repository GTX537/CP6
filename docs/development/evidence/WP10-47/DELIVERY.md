# WP10-47 既有物料、UOM 与容量维度原 Owner 目录适配

## 固定基线与准入

派工基线为 `origin/main@7517f87644e8ed9d08473612be5bd5f8e85390f2`，树为 `afe987f18f1394986ab7f9e42f010010914cf017`。总控决定授权 CP6-EXEC 在 DEV-1 领取 `codex/wp10-47`，席位登记时间为 `2026-11-26T08:52:06.342777Z`。开工核对远端 Main 版本、同名分支和 PR 查询。 [派工卡](<D:/CP6-test-artifacts/WP10-47/WP10-47-CARD-20261126.md>) · [总控决定](<D:/CP6-test-artifacts/COORD/DECISION-20261126-two-lanes-and-wp04-33.md>)

原依赖图递归 11 项及拆分余卡 WP02-13b 均为 DONE。Main 的交付提交均为上述基线的祖先。Platform 核对 `origin/main@a07edc24160d10cb1e3936cc6f495ad4195dd8f0`，三项交付提交均为其祖先。本轮 Main 锁定还原 `CP6.Platform.AspNetCore` 0.11.0。 [唯一状态台账](../../roadmap.json)

| 所属仓 | 已核实前置 |
| --- | --- |
| Main | S01-01、S01-02、S01-07、S02-00、WP02-02、WP02-03、WP02-10、WP02-13、WP02-13b |
| Platform | S01-09、WP02-01、WP02-07 |

## 原范围与候选交付

原范围是为 BOM、CFG、Stock、MASTER 读取原 Owner 的经营物料、Site、位置和 UOM 稳定身份及原版本，固定批准的单位转换、量纲、容量指标和当前控制。正式源由受信配置固定。真实原件缺失或歧义返回 `UNKNOWN`，旧数据可在新工程基线发布前按原权限读取。工程映射确认属 WP05-02。 [原卡](../../task-plans/WP10.md#wp10-47)

W310 已核销旧身份读取。W155-01 #575 提供原时间 UOM，W187-01 提供准确 BOM 来源，W204-01 提供准确库存身份。WP04-33 提供 ERP 经营产品原件、单位绑定与 `REQUIRED_FENCED` 合同，验收交付提交 `a92d4e54a1d77b84eef980045305827472ba8795` 已在本基线。 [W310 核销](../W310-01/DELIVERY.md#wp10-47) · [W155-01](../W155-01/DELIVERY.md) · [W187-01](../W187-01/DELIVERY.md) · [W204-01](../W204-01/DELIVERY.md) · [WP04-33](../WP04-33/DELIVERY.md)

剩余出口交付为受信配置 `OperationalCatalogue:Source=MAIN_NATIVE_V1` 和两个只读查询。原 ERP UOM Owner 提供不可变版本、比例、量纲和精度。仓库规则字典 Owner 提供容量指标、精度政策、适用域及原操作回执。两者沿用各自的 PostgreSQL 主账。查询在 Main Owner 事务内检查当前主体及对象范围，获取原件锁并在返回前复核权限。 [UOM Owner](../W147-01/DELIVERY.md) · [仓库规则字典 Owner](../W210-01/DELIVERY.md) · [适配实现](../../../../CP6.Core/Services/Wms/OperationalCatalogueEvidenceAdapter.cs)

| 查询 | 合同 |
| --- | --- |
| `POST /api/operational-catalogue/v1/unit-conversion` | 用 `from`、`to` 的 Owner、UUID、版本和摘要核对同一原基本单位。可选 `quantity` 以字符串提交，按原精度、比例和包装倍数精确求值。 |
| `POST /api/operational-catalogue/v1/warehouses/{warehouse}/capacity-metric` | 按仓库与完整 `metricId` 查询字典版本，返回量纲、原单位、原基本单位和适用精度政策。可用 `policy` 固定完整原字典引用。证据的 `admissionGrant` 为 `false`。 |

默认要求当前版本。显式传入 `requireCurrent=false` 可读取精确历史，容量历史同时给出 `policy`。原来源已升版或停用时，历史结果以 `isCurrent=false` 返回；当前查询以 `UNKNOWN` 返回。读时的当前性只对应本次事务，消费方仍须在效果点复核。

## 原验收对应

下表用例均通过，覆盖原 AC 中本卡提供者的部分。MASTER 的实际规则启用和占用评估、Stock 新过账和跨 Owner 组合效果按各责任卡验收。原目标在台账保留开放。 [MASTER 设计](../../../design-handbook/modules/WMS-MASTER-01.md) · [Stock 设计](../../../design-handbook/modules/WMS-STOCK-01.md) · [原 AC 清单](../../assessments/20261011/catalogs/operations.json)

| 原正反验收与本卡出口 | 本地用例 |
| --- | --- |
| 正向。工程冻结和新过账前，BOM 可引用准确经营料号、原单位和已批准换算。对应 Stock `STK-REV1-TC-037` 的原单位换算依据。 | `Operational_catalogue_approved_native_ratio_can_be_pinned_by_draft_bom_before_stock_posting` |
| 正向。同原版本量义无损，当前授权按原 Owner 生效。对应 MASTER `AC-009`、`MD-TC-004/012` 与 `TC-010` 的本卡取证部分。 | `Operational_catalogue_historical_conversion_keeps_ratio_and_high_precision_after_base_revision`、`Operational_catalogue_capacity_metric_preserves_policy_units_and_exact_history_after_disable`、`Operational_catalogue_final_authority_recheck_denies_result_after_mid_read_revocation` |
| 反向。未知来源、换算或容量指标保持 `UNKNOWN`，经营产品归属 ERP 原 Owner。对应 MASTER `TC-026/095`、Stock `STK-TC-004` 的来源门。 | `Operational_catalogue_unknown_sources_do_not_fall_back_on_code_dimension_or_caller_owner`、`Operational_catalogue_unready_original_store_and_nonrepresentable_quantity_are_fenced`、BOM 原 Owner 断言 |
| 反向。原合法单位字符串及精度完整，跨租户或缺权限时拒绝。对应 MASTER `MD-TC-004`、`TC-010` 和 Stock `STK-TC-005` 的本卡边界。 | `Operational_catalogue_historical_conversion_keeps_ratio_and_high_precision_after_base_revision`、`Operational_catalogue_current_native_query_permissions_and_tenant_apply_to_history`、`Operational_catalogue_warehouse_and_derived_unit_scope_do_not_grant_base_unit_read` |
| 并发与原键。原仓库字典与 ERP 单位版本保持一致，来源变更等待查询完成，同键取得原回执。 | `Operational_catalogue_concurrent_native_base_writer_waits_and_recovers_one_original_key`、容量指标停用及回执用例、PostgreSQL 库位租约回归 |

## 受测提交与验证原件

受测提交为 `333059b5e18831e7b7c8e828c147fd5a6dffa2f4`，树为 `be645fb93c0fe04c3fabd8f75c2b63df976f0a30`。完工前将 9 个受测路径的 Git blob 与工作文件、索引及受测树逐一比对。最终候选包含这个受测提交及本报告提交。前滚迁移为无。

在 LAPTOP-3QQ44FJS 的 DEV-1 使用 .NET SDK 10.0.404 锁定还原及 Release 构建，PostgreSQL 18.6 绑定 `127.0.0.1:55466`。原 Owner 读写、当前授权、迁移回放和真实 Main HTTP 均使用隔离夹具的合成数据。最终 210 项通过、0 失败、0 跳过。PostgreSQL 195 项包含本卡 9 项、经营产品 17 项、BOM 13 项、UOM 28 项、仓库与库位 90 项、库存身份/直移/占用 34 项、库位重试 4 项。另有 15 项既有库位内存组件回归。

| 运行 | 通过 / 失败 / 跳过 | TRX SHA-256 | 原件 |
| --- | --- | --- | --- |
| 最终回归 | 210 / 0 / 0 | `c2df51ad6eaa91f4f736983bd1ff45f2e6c3392cf656e86ddc5aa1a7ff651cf3` | [TRX](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/results-verified/wp10-47-verified.trx>) |
| 初始扩展回归 | 186 / 24 / 0 | `1aff4855dc95576a3bbe48139892e72e776c06a00de9bc5f320e198a2208b220` | [TRX](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/results-regression/wp10-47-regression.trx>) |
| 精确基线复现 | 1 / 10 / 0 | `28663faaf6a8c827f307e1e0686d58b9df97c965e600feb39db95205d272fcb9` | [TRX](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/results-baseline-regression/wp10-47-baseline-regression.trx>) |
| 定向隔离复测 | 20 / 1 / 0 | `c72976399c894d2a80fa411e701623607cc0fe87b74660aa70309f3257866616` | [TRX](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/results-focused/wp10-47-focused.trx>) |
| HTTP 红测 | 0 / 5 / 0 | `738fb3bdb6228ee86d67c23ea4d1defd253e5f9e0fbcf5d5c3558c0966de1054` | [TRX](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/results-red-02/wp10-47-red-02.trx>) |
| HTTP 首次绿测 | 5 / 0 / 0 | `7935f84806ed46c393560ff22e40d3947c9174920590ac1de00d1e0267aec51a` | [TRX](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/results-green/wp10-47-green.trx>) |

HTTP 红测的 5 项均得到缺失入口的 404。首次扩展回归暴露三处既有夹具问题。库位重试的 SQLite 建表遇到 `Latin1_General_100_BIN2`，改用现有隔离 PostgreSQL 夹具执行原四项断言。仓库旧迁移回放覆盖了共享测试库的后继部门映射函数，已在回滚事务内执行。UOM 前滚夹具在旧架构调用现行 EF 写入 `AuditAtUtc`，改按迁移切点字段写入。目录并发观测改用 PostgreSQL 后端 PID 及原件表锁，最终用例通过。 [最终命令、环境、用例与输入摘要](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/validation-verified.json>)

验证脚本记录父进程环境和测试宿主环境，夹具单独生成连接及密钥。脚本结构复用 WP10-48 的本地验证方式。最终构建、测试与取证在本卡重跑。 [复验脚本](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/run-validation.ps1>) · [WP10-48](../WP10-48/DELIVERY.md)

```powershell
& 'D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/run-validation.ps1' -Label review -Port 55465 -BuildDirectory build-review -Restore
```

6 个本卡实例的 `pg_ctl status` 均返回 3 和 `no server running`，PID 文件缺席，关联回环监听与本卡残留进程均为 0。 [全部夹具停机读回](<D:/CP6-test-artifacts/WP10-47/exec-20261126T085436Z/all-fixtures-stop-readback.json>)

## 有效时间与交接

执行记录自 `2026-11-26T08:49:34.4207521Z` 起算，截至 `2026-11-26T09:39:14.1049483Z` 为 **2979.684 秒**，在 M 卡 14,400 秒预算内。DEV-1 由 `CP6:WP10-47` 占用，Owner 为 CP6-EXEC。分支 `codex/wp10-47` 保存在本地，工作区清洁，席位保留给总控复核。

- `UNPROVEN`。企业真实原件配置、后续消费者正式采用及候选远端采用。
- `NOT_RUN`。两个完整目标的原 AC、正式环境冒烟、部署、Actions 与 SQL Server。前端为 `NOT_RUN`，本卡交付面为后端接口和回归夹具。
