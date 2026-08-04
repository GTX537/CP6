# E02-S07 CAD 语义证据与问题定位开发切片

日期：2026-08-03

## 交付结论

CP6 已在 E02-S06 集成基线 `68d59562` 上完成功能提交 `19b6c443`：把只读 Semantic Preview 进一步冻结为可验证的“逐提案证据 + 问题空间索引”。每个自动提案都可回答“来自哪个 CAD 对象、命中了哪条规则、置信度为何、在楼层画布哪里”，映射与语义问题也可按严重度、来源、代码、图层、SourceRef 和是否可定位进行确定性查询。

该产物明确是 `IsReadOnlyIndex=true` 的开发诊断工件，不修改 Semantic Preview，不创建永久 LogicalId，不写 Draft、数据库或编辑器状态。本切片不是正式 E02-S07 验收。

## 本次实现

1. 新增 `SpaceCadSemanticDiagnosticIndexV1`，绑定 Tenant、Floor、Source SHA、Coordinate Transform SHA、Inventory SHA、Profile ID/Version/Definition SHA、Mapping Preview SHA、Semantic Preview SHA 和自身 Diagnostic Index SHA；构建时重新计算完整语义链，任一错配或内容篡改均失败关闭。
2. 每个提案生成 `SpaceCadSemanticEvidenceV1`：保留 Preview Object ID、SourceRef、目标类型、置信度、Disposition、High/Review/Low/Rejected 分段、来源类型/键、决策来源、Rule ID、Geometry Rule、空间位置和独立 Evidence SHA-256。
3. 位置合同支持 Document、Layer、Block、Entity 四级定位，绑定 Floor LogicalId，并提供整数毫米 Bounds、中心 Anchor、建议视口留白和显式 `CanFocusCanvas`。空图层等没有实际范围的来源保留准确标识，但不会伪造画布坐标。
4. Mapping 与 Semantic 问题被归一为稳定 `cad-diagnostic-*` 标识，保留来源、代码、严重度、置信度分段、SourceRef/Preview ID/Rule ID、定位和恢复建议。恢复建议只表达下一步意图，不执行修正。
5. 新增受限分页查询：证据可按置信度分段、目标、图层、SourceRef、是否有问题筛选；问题可按严重度、来源、代码、图层、SourceRef、是否可定位筛选。默认 50，单页最多 200。
6. 新增 `build-dev-semantic-diagnostics`、`query-dev-semantic-diagnostics` 开发命令和 `semantic-diagnostics.schema.json`；同步修正 `semantic-preview.schema.json` 的 nullable 字段要求，使合同与 CLI 的 null 省略序列化一致。
7. 合成 CAD 连续测试扩展到每份图纸都构建诊断索引并核对来源链、数量和位置；CLI 测试覆盖诊断构建与证据查询。

## 样例 13 连续证据

输入：合成开发语料 `13-automated-warehouse.dxf`。

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`；
- Coordinate Transform SHA-256：`b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- Profile Definition SHA-256：`732eef8a1014e35428d427c639dce4936936087a08c484b23d67275c08de59d1`；
- Mapping Preview SHA-256：`98a0a3153af112563a3075dd9ee9fff1f113d122d22f03b89b399ba04d8009ca`；
- Semantic Preview SHA-256：`e398d192aa4d7f8cb5e92c18ac60dd6ae2ea667a338ee1e99eece0f39befc866`；
- Diagnostic Index SHA-256：`f0d18f95b144a4b4b8b503f9d6665528a25816b5d399eb8c9d0f18c17209448b`；
- Diagnostic JSON 文件 SHA-256：`aa04fc74fb0484d87968bf2fa30334136d1d4f94501cafada64608770eacdc0c`，46,892 bytes；重复运行文件哈希完全相同。

结果：22 个源对象、21 条提案证据，其中 High 13 / Review 0 / Low 8 / Rejected 0；5 条 Mapping 问题、16 条 Semantic 问题，共 12 Info / 9 Warning / 0 Blocking。17/21 条问题可直接聚焦画布；另外 4 条对应真实空图层，保留图层 ID 但不伪造范围。Low 查询返回 8 个 0.69 Rack 候选，每项均带 SourceRef、规则、实体锚点和独立证据哈希；可定位 Warning 查询返回 9 条。

## 门禁

- E02-S07 聚焦：6 passed / 0 failed / 0 skipped；覆盖逐提案来源/规则/置信度/位置、映射图层与语义实体定位、筛选和分页上限、确定性、来源链与篡改阻断、空图层不伪造范围；
- CAD 实验工具完整测试：23 passed / 0 failed / 0 skipped；
- Space Unit 完整测试：328 passed / 0 failed / 0 skipped；
- 完整 solution Release 非增量构建：0 error / 10 条既有 warning；
- 受影响 C# 文件 `dotnet format --verify-no-changes`、两个 Schema JSON 语法解析和 `git diff --check` 通过；全仓格式检查仍会命中与本切片无关的既有 Client/Core/MES 缩进债务，本次未扩大修改范围。

## 正式边界与下一步

正式 E02-S07 仍等待：

- E02-S01 授权原生 DWG/DXF 适配器、冻结隔离 Worker 与独立正式黄金集；
- 生产 Artifact、持久化、API、权限、审计和 Tenant/ModelVersion/Floor 权威关联；
- E04-S05 的问题列表、画布点击高亮和真实编辑器交互；
- 人工纠正命令、删除/合并/拆分、字段锁定以及 `SourceRef + userCorrectionVersion` 重放；
- E03-S04 的 Excel 行与 CAD/编辑器元素匹配；
- 对独立真实样本评估精度、覆盖率和性能。当前 High 只表示规则阈值，合成种子不计入发布精度/覆盖率门禁。

下一开发切片优先进入 E03-S04：在不写 Draft 的前提下，建立 Excel 行与 CAD/编辑器元素的候选匹配、冲突和证据合同；E04-S05 可在其后消费本诊断索引实现问题列表与画布定位。
