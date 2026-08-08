# E13-S05 Mock/本地 Provider 与故障降级开发切片

日期：2026-08-05

## 交付结论

CP6 在受控集成基线 `454c521c` 上完成功能提交 `e519942b`：新增确定性、无网络的
Mock Provider、本地启发式 Provider，以及只对声明为可重试故障执行的本地降级包装器。
三者均实现既有 `IWarehouseGenerationProvider` SPI，接收 E13-S04 最小化后的
`WarehouseGenerationInput`，返回同一 `WarehouseGenerationResult` 合同。

本开发切片不注册生产 Provider，不解析凭据，不连接网络端点，不持久化 Run/Usage，
也不写 Design Draft。正式 E13-S05 还要求选定并实现首个外部 Provider 适配器；在供应商、
合同、区域、端点别名、凭据引用、租户外发策略和生产输入/输出校验冻结前，该部分保持
失败关闭，因此本切片不能记作正式 E13-S05 完成。

## Provider 实现

### Mock Provider

- 按 `SourceKey` 稳定排序，在 `MaxSuggestions` 内每个特征最多生成一个建议。
- 仅按 CAD 实体枚举映射固定空间类型，置信度固定为 `0.5`，不读取原始 CAD 字段。
- 关系、证据、用量和请求标识均由输入合同确定性生成；模型标识为 `cp6-mock-v1`。
- 用于开发、合同测试和离线演练，不代表 AI 质量或发布精度。

### 本地启发式 Provider

- 仅解析 E13-S04 已允许外发的脱敏 Layer/Block 分类令牌，不读取原始图层名、块名、
  属性值、SourceRef、文件路径或租户身份。
- 可确定性识别 Rack、Aisle、Wall、Column、Door、Dock、StaticEquipment、Zone 和
  Floor，并只写冻结枚举属性；置信度固定为 `0.96`。
- 未命中特征不猜测，产生 `LOCAL_HEURISTIC_NO_MATCH`；建议、诊断和关系数量均受合同上限
  约束。模型标识为 `cp6-local-heuristic-v1`。

### 故障降级

- `Unavailable`、`Timeout`、`RateLimited` 三类稳定失败允许通过同一 SPI 降级到本地
  Provider，并追加 `AI_PROVIDER_*_FALLBACK` Warning。
- `ContractViolation` 不降级；用户取消也不降级，避免把无效响应或显式取消伪装成成功。
- 对外异常只包含稳定错误码和安全消息，不保留端点、凭据、响应体或内部异常。
- 降级后仍遵守最多 1,000 条诊断、建议/关系上限和确定性输出合同。

## 开发 CLI 边界

`run-dev-ai-provider` 支持 `mock`、`local` 与 `fallback-local`。后者仅能注入
`unavailable`、`timeout` 或 `rate-limited` 开发故障。命令在反序列化前检查输入
`schemaVersion=1.0` 与 `warehouseKind=GeneralRackWarehouse`；不支持的输入失败关闭。

`external` 不在可选项中，不能创建输出。控制台只报告 Provider 名、模型、建议/诊断数量、
是否降级以及固定的 `externalProviderInvoked=false`、`draftWritten=false`，不会打印输入路径、
原始映射、密钥或供应商响应。

## 样例 13 连续证据

输入为仓库内合成开发语料 `13-automated-warehouse.dxf`，不计正式黄金集或发布门禁：

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`。
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`。
- Coordinate Transform SHA-256：
  `b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`。
- StructuredFeatures Provider 输入：22 个特征，9,005 bytes，SHA-256
  `9e5f529f04cd4a9469df521b2fe4f9d72a02c6b69c85097d2bb3f753b9a4308a`；
  local source map 规范 SHA-256
  `941c6c564567c74ebe7f504d9b53334c280f11d6da3ac4defa78afe03dad0143`。
- Mock：22 个建议、1 个诊断，8,706 bytes，SHA-256
  `0b7f7ac74653da089442ca4adbcbca4c4e9715bcc286c8ec182c237e043a21de`。
- Local：21 个建议、1 个未命中诊断，9,051 bytes，SHA-256
  `c19ca1bf113402e21eeae1699240e5acedce02ab39c55a054278836d151899b5`；类型分布为
  Column 8、Dock 1、Door 1、Rack 8、StaticEquipment 2、Wall 1。
- Unavailable 降级：21 个建议、2 个诊断，9,146 bytes，SHA-256
  `02954a04cb4a7e75cf4b77491e88ee80d99071c38fda6b0197f8b409fa196eed`。
- Timeout 降级：21 个建议、2 个诊断，9,142 bytes，SHA-256
  `a533f5726f2d0b835243bb372b4c1a6141ed60e92f194320f403787c35410241`。
- Rate-limit 降级：21 个建议、2 个诊断，9,147 bytes，SHA-256
  `d1b97dd87c4296b25cb00ab917877b8934271afb7b803ed5f90170ec69206326`。
- Mock、Local 与 Timeout 降级分别重复运行，文件均字节一致。
- 5 份结果共 106 个建议；未知输入引用 0、未知关系引用 0、置信度/关系/证据范围违规 0。
- 对输入身份、哈希、全部 SourceRef 和非空属性值组成的 38 个敏感候选逐一扫描，5 份输出命中 0。
- 所有 CLI 运行均报告 `externalProviderInvoked=false`、`draftWritten=false`。

## 门禁

- 新 Provider 实现与既有 SPI 聚焦：27 passed / 0 failed / 0 skipped。
- Space Unit 全量：359 passed / 0 failed / 0 skipped。
- CAD 实验工具全量：25 passed / 0 failed / 0 skipped；新增 CLI 连续性测试覆盖 Mock、
  Local、Timeout 降级重复运行、外部模式拒绝和未知输入 Schema 拒绝。
- 完整 solution Release 非增量、单线程、禁用节点复用构建：0 error / 10 条既有 warning；
  Desktop 与 Android 原生 AOT 强度未降低。首轮因 Visual Studio 更新期间旧 iOS 26.2 SDK
  目录消失而产生 `MSB4019` 环境失败；更新器恢复并升级工作负载到 iOS 26.5 后，同一命令
  无代码修改通过。
- 四个受影响 C# 项目 `dotnet format --verify-no-changes` 与 `git diff --check` 通过。
- 本切片无数据库、Migration、WebApi、前端、OpenAPI 或 SDK 变化。

## 正式边界与下一步

正式 E13-S05 仍需选定首个外部 Provider，冻结供应商输入/输出合同、区域和数据驻留、
部署端点别名、受管 `SecretReference`、超时/重试/限流映射、租户外发授权和真实计费证据，
然后通过同一 SPI 的合同测试。未满足这些条件前，生产 Registry 保持空，External 默认禁用。

E13-S06 负责把 Provider 输出作为不可信数据执行完整 Schema、枚举、范围、数量、引用完整性、
属性组合和安全字符串校验；当前开发 CLI 对自身本地实现的输出不冒充该安全边界。E13-S07
之后才可按 `HumanLocked > Rule > AI > Default` 融合建议，任何本切片输出都不能直接写入
Draft、Published、WMS 或设备控制路径。
