# E13-S06 Provider 输出 Schema 与不可信输入校验开发切片

日期：2026-08-05

## 交付结论

CP6 在受控集成基线 `0c59cc34` 上完成功能提交 `7b95c29e`：新增
`IWarehouseGenerationOutputValidator`、确定性 `WarehouseGenerationOutputValidator` 和
`ValidatedSemanticResult`，把 Provider 的 CP6 Canonical Envelope 同时按原始 JSON 形状与
反序列化后的语义合同校验。任何失败统一返回非重试的 `SPACE_AI_OUTPUT_INVALID`，不返回部分
结果，不回显 Provider 原值，也不写 Design Draft。

`SpaceAiGenerationGateway` 现在在 Provider 调用返回后、配额租约释放前强制验证结果；默认 DI
注册验证器和 64 MiB Canonical JSON 上限，但 Provider Registry 仍为空。E13-S05 尚未完成首个
外部适配器，因此本切片不能冒充真实厂商原生响应已接入或正式 E13-S06 端到端验收。

## 原始 Canonical JSON 门禁

`ValidateJson` 在反序列化前执行下列失败关闭检查：

- 非空且不超过 64 MiB；JSON 深度最多 16，不允许注释或尾逗号。
- 根、Usage、Suggestion、Attributes、Relation 与 Diagnostic 对象只接受权威字段；缺少必填、
  未知字段或同名重复字段全部拒绝。
- 枚举必须是区分大小写的声明字符串，数字枚举、未知枚举和大小写近似值均拒绝。
- Usage 必须是非负 `Int64`；置信度必须是 0～1 的 decimal；数组数量在平台、请求和 Schema
  上限内。
- Provider request/model、token、诊断和 semantic label 拒绝 C0/C1 控制字符并执行长度上限。
- 输出 JSON Schema 同步收紧 request/model/token 控制字符，并以 `allOf` 表达 Zone/Rack/Door/
  Dock/StaticEquipment 专属属性不能挂在其他类型上。

开发命令 `validate-dev-ai-provider-output` 在读取文件前先检查长度，然后对原始 Canonical JSON
执行同一验证器；成功只打印 Schema、模型、计数和 Canonical SHA-256，读取缓冲区随后清零。
`run-dev-ai-provider` 也在写文件前验证 Mock/Local/Fallback 的 typed Envelope。

## 反序列化语义门禁

- Schema 版本必须为 `1.0`；Provider request/model、Usage 和所有集合必须存在且有界。
- Suggestion `SourceKey` 必须来自本次输入且每个 SourceKey 最多一个建议。
- `SuggestedType`、属性枚举、关系枚举、Evidence 与 Diagnostic severity 必须是声明值；置信度
  均在 0～1。
- 每个关系目标必须来自本次输入，不能自引用；相同 relation type + target 不能重复，数量不超过
  请求 `MaxRelationsPerSuggestion` 和平台 32 条上限。
- Evidence 必须为 1～16 个声明枚举且不重复；Diagnostic SourceKey 为空或引用本次输入。
- ZonePurpose、RackType、DoorType、DockType、EquipmentType 只能分别用于匹配的空间类型；
  非法组合拒绝，不能把任意 Provider 字段带入领域命令。
- 通过后对规范化 Envelope 生成稳定 SHA-256，供后续 Run/Artifact 证据绑定；未通过的对象不会
  离开 Gateway。

## 错误语义与安全边界

全部验证失败使用：

- code：`SPACE_AI_OUTPUT_INVALID`
- HTTP 映射：`502`
- retryable：`false`
- recovery：`change-ai-provider-or-model`
- detail：只包含稳定内部违规码，例如 `OUTPUT_SCHEMA_INVALID`、
  `OUTPUT_REFERENCE_INVALID` 或 `OUTPUT_ATTRIBUTE_COMBINATION_INVALID`，不包含原始值、
  响应正文、端点或凭据。

Provider 自身超时/限流降级仍由 E13-S05 处理；输出合同违规绝不降级为“成功”。Gateway 在验证
抛错时仍释放配额租约。验证只建立“可进入 E13-S07 融合”的边界，不代表建议正确、高置信、已审查
或可直接应用。

## 样例 13 连续证据

输入为仓库内合成开发语料 `13-automated-warehouse.dxf`，不计正式黄金集或发布门禁：

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`。
- CAD IR：19,175 bytes，SHA-256
  `b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`。
- Coordinate Transform SHA-256：
  `b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`。
- StructuredFeatures 输入：22 个特征、9,005 bytes，文件 SHA-256
  `2186eb5f9c9dc07a5db488132935c98a94e542fef207582a264e8572bb3eddf9`；local source map
  文件 SHA-256 `57fa2ce719b864873dccc08e4dcff06be8bb678821ed91e2bc61902c5ad07992`，
  规范 map SHA-256 `78d99a9e428bd599650ddc9884211b2b344d9de954d00db03632a6c4e4cf7265`。
- Local 输出：21 个建议、1 个诊断、9,051 bytes；文件 SHA-256
  `5e57cce368c261dda9a88f6589ef4f04213007684dc3920aa42c287dc36f7e23`，Canonical SHA-256
  `913e99b49fa9b90cfc93d3d0f4abb175160660a44cce2aedef75a778c984767c`。
- 独立重复 Provider 运行的文件与 Canonical SHA 均一致；两份原始文件都由
  `validate-dev-ai-provider-output` 验证通过。
- 21 个建议的未知 SourceKey 为 0，未知关系引用为 0。租户/站点/模型/Run/Floor 身份、三类哈希、
  全部 SourceRef 和 9 个非空属性值组成 39 个唯一敏感候选，输出命中 0。
- 所有命令均报告 `externalProviderInvoked=false`、`draftWritten=false`。

## 测试矩阵与门禁

- 原始 JSON 恶意矩阵 9 类：损坏 JSON、未知根字段、重复根字段、缺失必填、数字枚举、未知枚举、
  小数 Usage、控制字符和未知嵌套字段。
- typed 语义恶意矩阵 16 类：Schema、负 Usage、超量/重复/未知建议、非法类型/置信度/属性组合、
  自引用/未知/重复/超量关系、空/重复 Evidence、未知诊断来源和控制字符 semantic label。
- 独立覆盖字节上限、稳定 Canonical SHA、Gateway 拒绝并释放租约、默认 DI 注册、CLI 有效/篡改
  连续性，以及既有 Mock/Local/Fallback 合同。
- Provider/验证器/Gateway 聚焦：55 passed / 0 failed / 0 skipped。
- Space Unit 全量：387 passed / 0 failed / 0 skipped。
- CAD 实验工具全量：25 passed / 0 failed / 0 skipped。
- 默认 DI 聚焦：1 passed / 0 failed / 0 skipped。
- 完整 solution Release 非增量、单线程、禁用节点复用构建：0 error / 10 条既有 warning；
  Desktop 与 Android 原生 AOT 强度未降低。
- 受影响 C# 格式验证、权威输出 Schema JSON 解析和 `git diff --check` 通过。
- 本切片无数据库、Migration、公开 WebApi、前端、OpenAPI 或 SDK 变化。

## 正式边界与下一步

正式 E13-S06 端到端签收仍依赖 E13-S05 首个外部 Provider：厂商原生 HTTP/SDK 响应必须先在
传输层限流/限长，再映射为 CP6 Canonical Envelope，并通过同一验证器；还需供应商/模型版本、
区域、凭据引用、租户策略、真实非法响应故障注入和 Run/Artifact 审计证据。当前开发 CLI 不连接
网络，不替代生产适配器，也不保存完整 Provider 响应。

E13-S07 才负责 `HumanLocked > Rule > AI > Default` 融合、确定性几何/编码构造与规则证据；
本切片只证明输出形状与引用安全，不能把建议应用到 Draft、Published、WMS 或设备控制路径。
