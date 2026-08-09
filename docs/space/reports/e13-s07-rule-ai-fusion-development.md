# E13-S07 规则/AI 融合与确定性生成开发切片

日期：2026-08-05

## 交付结论

CP6 在 E13-S06 集成基线 `44c87a26` 上完成了功能提交 `8be8b20f`：新增
`IWarehouseDraftSynthesizer`、确定性 `WarehouseDraftSynthesizer`、版本化只读提案合同和
RFC 4122 UUIDv5 身份生成器。该合成器只消费经过 E13-S06 验证且重新核验 Canonical SHA-256
的 `ValidatedSemanticResult`，按
`HumanLocked > DeterministicRule > AI > TemplateDefault` 逐字段决胜，保留全部候选证据和
稳定冲突码，并输出自校验 SHA-256 提案集。

本切片不注册外部 Provider，不连接网络，不写 Generation Run、Proposal、Decision、Draft、
Published、WMS 或设备控制数据。所有提案固定 `IsReadOnlyPreview=true`、`DraftWritten=false`、
`ReadyForApply=false`；后续必须经过 E13-S08/S09 人工审查和 E13-S10 原子 Apply。

## 输入绑定与失败关闭

合成入口同时验证并绑定：

- ModelVersion、RuleVersion、Tenant、Floor、Source SHA、Coordinate Transform SHA；
- E13-S04 Provider Input SHA、local-only Source Map SHA 和每个 `SourceKey → SourceRef` 映射；
- E02-S06 Semantic Preview SHA、每个规则项及其整数毫米几何；
- E13-S06 Provider Output Canonical SHA，并用同一输出验证器重新校验 typed 结果；
- 本地 HumanLocked facts 与 Provider Input 中脱敏 locked-fact 快照的逐项一致性；
- 显式货架方案来源、尺寸、层号、层高、格口、深位、承载和最多 1,000 万派生库位上限。

任一哈希、Floor、Transform、SourceRef、locked fact 或 Canonical Envelope 不一致均整体拒绝，
不产生部分结果。反序列化后的提案集还会重新验证规范顺序、字段胜者/证据、关系引用、
LogicalId、货架派生身份、摘要和自身 SHA，不能只靠篡改哈希冒充有效工件。

## 四级融合、冲突和关系

- 人工锁定字段最终胜出；AI 值不同产生 `AI_LOCKED_VALUE_CONFLICT` Info，规则值不同产生
  `LOCKED_RULE_VALUE_CONFLICT` Warning，原值双方都保留在 evidence 中。
- 确定性规则优先于 AI。规则置信度为 1 且几何有效时，AI 冲突只记录
  `AI_RULE_VALUE_CONFLICT/strong-rule-retained`；非强规则冲突保留双方证据并把 High 降为
  Medium 或 Low。
- AI 只补规则未决定的 allowlisted 类型专属属性或 semantic label；最终类型不兼容的 AI
  属性不进入提案，人工锁定属性不兼容则 Blocking。
- Provider Suggestion 没有规则生成的本地几何时产生 `AI_GEOMETRY_RULE_REQUIRED` Blocking，
  不允许 AI 自己创建顶点或对象；被拒绝规则来源、未解析关系目标同样不产生隐式对象。
- `ParentCandidate/ContainedBy` 使用线性图裁剪检测父关系环；环中关系全部移除并产生
  `AI_PARENT_RELATION_CYCLE` Blocking。所有保留关系只能引用本提案集内 LogicalId。
- 输出固定按 Floor、Zone、Wall/Column/Door/Dock、Aisle、Rack、StaticEquipment 顺序，
  同类再按 SourceRef 排序；字段、证据、关系、问题也都有规范顺序。

## 几何、身份、货架和编码边界

- 最终提案几何只能复用通过 E02-S06 校验的 `CadIrDeterministicRule` 整数毫米几何；Provider
  不存在坐标/顶点写入口。
- 对象 LogicalId 使用 `UUIDv5(ModelVersionId, SourceHash, SourceKey)`；RackLevel 使用
  `UUIDv5(RackLogicalId, LevelNo)`；Location 使用
  `UUIDv5(RackLogicalId, LevelNo, ColumnNo, DepthNo)`，重复运行稳定且不同格位不碰撞。
- 货架方案优先级为 HumanLocked > ExcelMapping > ExplicitSelected。缺显式方案产生稳定
  `SPACE_RACK_PROFILE_REQUIRED` Blocking，绝不补不可见尺寸默认值。
- 通过方案后按 Rack 输出层规格、派生层/库位数量及首尾库位身份摘要，不创建成千上万个
  单独 AI Proposal。现有编码服务尚未提供 Application 层只读纯预检端口，因此本切片不伪造
  最终 LocationCode；Rack 明确标记 `ExistingServicePrecheckRequired`。

## 样例 13 连续证据

输入为仓库内合成开发语料 `13-automated-warehouse.dxf`，不计正式黄金集或发布门禁：

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`；
- Coordinate Transform SHA-256：
  `b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- Mapping Profile Definition SHA-256：
  `732eef8a1014e35428d427c639dce4936936087a08c484b23d67275c08de59d1`；
- Mapping Preview SHA-256：
  `09882a25f61690b1d42a996e0fd0782b49f3736ee8b3ff8c5804c4c7d553b486`；
- Semantic Preview SHA-256：
  `a777c8d2fd48e428102ac16ab17afa0ad18dd1bc01663573bed5dc125f103c20`；
- 22 个 StructuredFeatures，Provider Input/File SHA-256：
  `2e62799ba385af50aeb0a151fa5d1321bb98f396301d33a72650f959b7a0ac9c`；
  local Source Map Canonical SHA-256：
  `ee00bdcf16618193fdb94a5d9f73e7fee7fcafd97892cddf395b610b13c455a3`；
- Local Provider 产生 21 条建议和 1 条 Info，Canonical SHA-256：
  `e20ecd2b0e175299a75bfaf142d80a1203dc951fdf0ebc4fba4737c67fccdfd7`；
- 显式开发方案绑定 8 个 Rack，每个 3 层、每层 4×2 格，共派生 24 个 RackLevel、192 个
  Location；方案只是临时开发输入，没有写入资产库或 Draft；
- 最终 21 个提案均有唯一 LogicalId：High 13、Medium 0、Low 8；8 个 Rack、24 层、192
  库位；Info 9、Warning 8、Blocking 0；全部 geometrySource 为
  `CadIrDeterministicRule`；字段胜者为 Rule 21、AI 补充属性 12；
- ProposalSet SHA-256：
  `fba6c44c6d0e0cc6ee3d5d94832ff296b033f238547fb925537a9f62cf31a288`；JSON 文件
  40,424 bytes，文件 SHA-256：
  `a730d99c68094e4a6312b5f249d325285b68a68d55ef89a4c0b5d6d11ce706a0`；两次运行字节完全一致；
- 所有命令报告 `externalProviderInvoked=false`、`draftWritten=false`，提案集报告
  `readyForApply=false`。

## 测试矩阵与门禁

- E13-S07 聚焦 10/10：四级优先级、强/软规则冲突、锁定属性类型冲突、父关系环、无规则
  几何 AI 建议、显式方案优先级、缺方案阻断、UUIDv5、哈希/locked snapshot 篡改和取消；
- Space Unit 全量：397 passed / 0 failed / 0 skipped；
- CAD 实验工具全量：25 passed / 0 failed / 0 skipped，其中开发 CLI 端到端验证无规则几何
  Suggestion 必须 Blocking 且不产生提案；
- 默认 DI 聚焦：1 passed / 0 failed / 0 skipped，合成器默认注册且 Provider Registry 仍为空；
- 完整 solution Release 非增量、单线程、禁用节点复用/共享编译构建：0 error / 10 条既有
  warning；Desktop 和 Android 原生 AOT 强度未降低；
- 受影响 C# 格式验证和 `git diff --check` 通过；无数据库、Migration、公开 WebApi、前端、
  OpenAPI 或 SDK 变化。

## 正式边界与下一步

这是 E13-S07 的可执行开发切片，不是生产端到端签收。正式 E13-S07 仍需：

- 把平台/租户 RackGenerationProfile 和 Excel/人工锁定方案接到持久化、租户授权、版本冻结与
  Run Artifact，而不是 CLI JSON；
- 为现有编码服务提供同一 Application/Revision 边界内的只读纯预检端口，并完成编码重复、
  变量长度规则和数据库现有码冲突证据；
- 使用完整 Floor/Draft 场景执行边界、碰撞、父对象归属和编码预检；本切片只验证规则几何
  自洽和 AI 关系环，不能冒充全场景碰撞验收；
- 接入 E13-S03 Worker/Run Artifact、审计和配额链，并在 E13-S10 事务内再次验证后原子 Apply；
- 外部 Provider 正式证据仍受 E13-S05/S06 的供应商合同、区域、端点、SecretReference、租户
  外发授权、传输限流限长、真实非法响应和计费审计门禁约束。

当前输出已经可以作为 E13-S08 分页、差异预览和审查工作台的只读输入；E13-S08 不得绕过
上述正式缺口，也不得提前实现 Apply。
