# E03-S04 Excel/CAD 元素匹配开发切片

日期：2026-08-04

## 交付结论

CP6 已在 E02-S07 集成基线 `4d945b5d` 上完成功能提交 `2da39667`、证据提交 `02cdbcff`，并以 no-ff 提交 `b2a2320c` 集成到 `integration/space-v1-20260730`：把 E03-S03 的规范化 Excel 货架行与 E02-S06/S07 的 CAD 语义提案、诊断位置及只读编辑器货架快照组合为确定性匹配预览，显式区分 New、Update、Unchanged、Unmatched、Conflict 和 Error，并提供独立未匹配清单与画布可定位查询。

该产物固定为 `IsReadOnlyPreview=true`。构建、验证和查询均不创建永久 LogicalId，不写 Draft、数据库或编辑器状态，也不推进模型内容修订号；因此这是开发切片，不是正式 E03-S04 验收，也不提前实施 E03-S05 的用户确认与幂等写入。

## 本次实现

1. E03-S03 预检验证器新增 `Inspect` 入口，在保持既有 `Validate` 行为不变的前提下复用同一套类型、单位、枚举、必填、引用和重复校验结果，并暴露已经规范化的行投影，避免匹配器再次解释 Excel 原值。
2. 新增 `SpaceExcelCadMatchPreviewV1` 合同和 JSON Schema。预览绑定 Tenant、ModelVersion、ExcelSource、PreflightJob、Mapping Profile ID/Version/Definition SHA、规范工作簿投影 SHA、Floor、Semantic Preview SHA、Diagnostic Index SHA、编辑器内容修订与快照 SHA，以及自身 Match Preview SHA。
3. 编辑器货架快照显式绑定 Tenant、ModelVersion 和 Floor，按 LogicalId/RevisionId 稳定排序并封存 SHA-256；跨租户、跨模型、跨楼层、重复 LogicalId/货架码、非法尺寸或篡改均失败关闭。
4. 匹配键只接受 CAD/编辑器 SourceRef 或受控货架码属性 `RACK_ID`、`RACK_CODE`、`RACKCODE`、`CODE`、`BUSINESS_KEY`。单一 CAD 命中为 New；编辑器单一命中按规范字段比较为 Update/Unchanged；无命中为 Unmatched；多候选、CAD/编辑器来源不一致、楼层不一致或多个 Excel 行争用同一目标为 Conflict；预检 Blocking 行为 Error。
5. 每行保留采用的键类型和值、候选 ID、CAD 置信度与 High/Review/Low/Rejected 分段、差异字段、稳定错误码、诊断定位和独立 Match Evidence SHA。错误行不会伪造无效数值或空间位置。
6. `CanConfirm` 只有在存在行、无 Unmatched/Conflict/Error、Excel 与 CAD 均无 Blocking、CAD 全局可确认且所有 CAD 命中至少达到 Review 时才为真。Low 候选仍可展示和定位，但不能绕过人工/上游门禁。
7. 新增受限分页查询，支持按 disposition、货架码、SourceRef 和是否可聚焦画布筛选，默认 50、单页最多 200。新增开发命令 `seal-dev-editor-rack-snapshot`、`match-dev-excel-cad` 与 `query-dev-excel-cad-match`。
8. 规范序列化统一省略空字段，使应用层 `Serialize`、CLI 输出和 JSON Schema 一致；规范投影、逐行证据与顶层预览均按稳定排序和 camelCase JSON 计算 SHA-256。

## 样例 13 连续证据

输入为仓库内合成开发语料 `13-automated-warehouse.dxf`，并额外构造 10 条 Excel 货架行：8 条精确命中 CAD `RACK_ID`、1 条不存在的货架码、1 条宽度类型错误。

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`；
- Coordinate Transform SHA-256：`b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- CAD Profile Definition SHA-256：`732eef8a1014e35428d427c639dce4936936087a08c484b23d67275c08de59d1`；
- Mapping Preview SHA-256：`98a0a3153af112563a3075dd9ee9fff1f113d122d22f03b89b399ba04d8009ca`；
- Semantic Preview SHA-256：`e398d192aa4d7f8cb5e92c18ac60dd6ae2ea667a338ee1e99eece0f39befc866`；
- Diagnostic Index SHA-256：`f0d18f95b144a4b4b8b503f9d6665528a25816b5d399eb8c9d0f18c17209448b`；
- Editor Snapshot SHA-256：`e64aed71eb0d7b8253ea440e4b477f15d54ca4103911f60fa8ff7ba79e8a9948`；
- Workbook Projection SHA-256：`339ee9cfc2fb7b1073ace3489476dc23d20f0820b4af43cf4d908efda9b1e8c8`；
- Match Preview SHA-256：`c6ca364098d02947fabfbaf092c4dee4e5a11fb17b7946140ca92c8c72a4c107`；
- Match JSON 文件 SHA-256：`369372e1534491510484593c94b440de415162a7bb6cb541ec0649f967acd951`，15,732 bytes；两次独立运行文件哈希完全相同。

结果为 New 8 / Update 0 / Unchanged 0 / Unmatched 1 / Conflict 0 / Error 1，其中 8 条 New 均带真实 CAD SourceRef、实体锚点和可聚焦位置。Unmatched 查询精确返回 1 条，New + locatable 查询返回 8 条，Error 查询返回 1 条。由于存在未匹配、错误和 0.69 Low CAD 候选，`CanConfirm=false`，符合失败关闭设计。

## 门禁

- E03-S04 聚焦测试：8 passed / 0 failed / 0 skipped，覆盖 SourceRef/货架码匹配、New/Update/Unchanged/Unmatched/Conflict/Error、目标争用、确定性、租户/来源链/哈希篡改和分页上限；
- E03-S03 预检回归：6 passed / 0 failed / 0 skipped；
- Space Unit 全量：336 passed / 0 failed / 0 skipped；
- CAD 实验工具全量：23 passed / 0 failed / 0 skipped；
- 功能树与 no-ff 合并树的完整 solution Release 非增量单线程构建均为 0 error / 10 条既有 warning，Desktop 与 Android 原生 AOT 强度保持不变；合并态再次通过 Space Unit 336/336、CAD 工具 23/23 与预检回归 6/6；
- 受影响 C# 文件 `dotnet format --verify-no-changes`、CAD v1 全部 Schema JSON 语法、生成工件的类型反序列化/应用验证、空字段省略检查与 `git diff --check` 通过。

## 正式边界与下一步

正式 E03-S04 仍等待生产 CAD 适配器/Artifact/持久化链、权威编辑器快照读取服务、映射方案服务端 Definition Hash 权威核验、API/权限/审计、真实授权图纸和 UI 验收。当前匹配器只消费已封存输入；它不会自行读取数据库，也不会把调用方提供的 Definition Hash 当作生产授权证明。

E03-S05 仍负责用户确认、幂等 Draft 写入、最终导入结果和并发内容修订检查。在生产 CAD 链未正式解锁前，下一独立开发切片优先 E04-S05：消费 E02-S07 诊断位置和本匹配预览的 Location，形成问题/未匹配列表与画布聚焦交互；不得借此提前写 Draft。
