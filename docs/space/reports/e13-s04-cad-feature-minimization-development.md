# E13-S04 CAD IR 特征最小化与脱敏开发切片

日期：2026-08-05

## 交付结论

CP6 已在集成基线 `f5f0c9e8` 上完成 E13-S04 开发功能提交
`8fffdf07`：将 parsing-ready 的 CAD Coordinate Preparation 确定性投影为
`MetadataOnly` 或 `StructuredFeatures` Provider 输入，并把可逆的原始
`SourceRef` 单独保存在明确标记为 local-only 的映射工件中。

该路径不读取或发送原始 DWG/DXF/PDF、图片、Excel 或附件，不调用外部
Provider，不持久化 Run/Artifact，也不写 Design Draft。它用于提前冻结最小化
算法、外发合同、信任边界和回归证据，不等同于正式 E13-S04 生产验收。

## 最小化与脱敏规则

### MetadataOnly

- 每个 Provider 特征只包含运行级 HMAC `SourceKey`、CAD 实体枚举、脱敏图层/
  块令牌、实体计数、10 度角度桶、无量纲长宽比桶、重复组和 HMAC 化属性键。
- 同类实体按上述维度聚合，因此角度分布和实体计数可供推断，但不发送坐标。
- `normalizedBounds` 必须为 `null`，关系必须为空，object-level locked facts
  必须为空；应用合同和 JSON Schema 同时执行该条件。
- 租户映射提示只保留 HMAC 令牌、声明的目标枚举和 0～1 强度。

### StructuredFeatures

- 在 MetadataOnly 的安全字段基础上，每个 CAD 实体保留一个 HMAC
  `SourceKey`。
- 非退化包围盒按楼层边界归一化到 0～1，并以四位小数量化；不会输出绝对坐标。
- 同一重复组仅建立有界的相邻 `SourceKey` 关系；关系必须引用本次输入中的其他
  特征，不能自引用或越界。
- 人工锁定事实只接受 `type`、`zonePurpose`、`rackType`、`doorType`、
  `dockType` 和 `equipmentType` 的已声明枚举；自由文本语义标签和任意属性值被拒绝。

### 共同安全边界

- Provider 根对象只有冻结的 schema/version、opaque run correlation、policy、
  warehouse kind、limits、features、mapping hints 和 locked facts；不包含
  TenantId、SiteId、ModelVersionId、RunId、FileId、SourceId、文件名、对象存储位置、
  SourceRef、源/变换哈希或楼层身份。
- Run correlation、SourceKey、图层/块/属性/重复/提示令牌均使用 32～128 byte
  HMAC key 和独立 domain separator 生成，并按 Run 隔离；相同输入和 Run
  可重复，不同 Run 产生不同令牌。
- 图层名、块名和属性键始终作为结构化 JSON 数据处理。只允许仓库领域白名单词
  作为不可识别身份的分类标签，其余原文只进入 HMAC；属性值和 CAD Text 内容不进入
  Provider 输入。
- 所有 Provider token 去除控制字符风险并限制为 256 字符；数量、枚举、范围、
  重复身份和引用完整性均失败关闭。
- 开发 CLI 从独立二进制文件读取 HMAC key，使用后清零内存，不把 key、原始映射或
  路径打印到 Provider 输出。生产环境必须改由已有 `SecretReference`/部署密钥边界解析。

## 双工件边界

1. `warehouse-generation-input.schema.json` 是唯一可交给 Provider 的输入合同。
2. `cad-feature-source-map.schema.json` 明确要求 `isLocalOnly=true`，保留
   Source/Transform/Floor 链、Provider 输入哈希以及 `SourceKey -> SourceRef[]`。
3. Local map 的 Provider hash 必须等于实际 Provider 文件 SHA-256；自身另有排除
   `sourceMapSha256` 字段后的规范 SHA-256。任一输入或映射篡改都会被拒绝。
4. CLI 强制 input、HMAC key、Provider output 和 local map 使用四个不同路径；控制台
   只输出 policy、数量、哈希以及 `externalProviderInvoked=false`、
   `draftWritten=false`。

## 样例 13 连续证据

输入为仓库合成开发语料 `13-automated-warehouse.dxf`，不计正式黄金集或发布门禁：

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`。
- CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`。
- Coordinate Transform SHA-256：
  `b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`。
- MetadataOnly：22 个 SourceRef 聚合为 8 个特征，5 个特征带长宽比分桶，
  geometry/relationship 违规 0，locked facts 0。Provider 文件 2,868 bytes，
  SHA-256 `c5fbdcf2967cd8443e73c1c5612b93ce4e9776f5a8b1cbe7b1ac22bf7d697efa`；
  local map 文件 1,257 bytes、文件 SHA-256
  `d545423689509a92305202649d5490a39c6462f58a44f5111b9fb3abacaa3274`，
  规范 map SHA-256
  `ceda326e22ef30f783bac92f68b24b3c7cd0df001f9816a0c75b66e90a35c792`。
- StructuredFeatures：22 个 SourceRef 对应 22 个特征，12 个有效相对包围盒、
  14 条有界关系、0 个越界包围盒。Provider 文件 9,005 bytes，SHA-256
  `164020fae577c58669a7eaaa57e22b17101f9ccd754b0cccb0d7c9db18bc2b65`；
  local map 文件 2,266 bytes、文件 SHA-256
  `46ba2a976eec60d6f30a3a429c42b7cec925343c2fae9f7aee35886dce7eceb2`，
  规范 map SHA-256
  `85c57cc13375dbaaddf2962b7003b5e6e576ad900fd8839af74e02bf25e0ee74`。
- StructuredFeatures 使用相同输入、身份、Run 和 HMAC key 独立重复运行；Provider
  文件与 local map 文件均字节一致。
- 对 Provider 文件逐一检查 38 个非空候选，包括 Tenant/Site/ModelVersion/Run、
  Source/Transform/Floor、全部 SourceRef 和全部非空属性值；命中数为 0。
- 两次 CLI 均明确报告 `externalProviderInvoked=false`、`draftWritten=false`。

## 门禁

- E13-S04 minimizer + Provider SPI 聚焦：27 passed / 0 failed / 0 skipped。
- Space Unit 全量：350 passed / 0 failed / 0 skipped。
- CAD 实验工具全量：24 passed / 0 failed / 0 skipped；新增 CLI 连续性测试覆盖
  Provider 文件哈希绑定、本地可逆映射和两次运行字节一致性。
- 完整 solution Release 非增量单线程构建：0 error / 10 条既有 warning；Desktop
  和 Android 原生 AOT 强度保持不变。
- no-ff 合并态的 Space Unit 350/350、CAD 工具 24/24 再次通过。完整构建首轮在
  第三方 Kotlin 协程程序集的 Android x64 AOT 汇编器处瞬时失败；关闭 build
  server、禁用节点复用后以相同 Release/非增量/单线程/AOT 强度重跑为 0 error /
  10 条既有 warning，未修改代码或关闭 AOT。
- 受影响 C# 文件 `dotnet format --verify-no-changes`、两份 AI JSON Schema
  语法解析、Provider/Local 工件反序列化与应用验证、`git diff --check` 均通过。
- 本切片没有前端代码、数据库模型、迁移、API 或 SDK 变化。

## 正式边界与下一步

正式 E13-S04 仍需生产 E02-S03 CAD Artifact 输入、租户/Run/策略快照绑定、受管
SecretReference、Artifact 持久化/保留策略、权限/审计和真实授权 CAD 覆盖；当前开发
CLI 的固定 FileId/SourceId 与本地 key 文件不能用于生产。

E13-S05 才负责 Mock/本地 Provider 与首个外部适配器及故障降级；E13-S06 才负责把
Provider 输出作为不可信数据执行 Schema、枚举、范围、数量和引用校验。本切片既没有
绕过外部 Provider 门禁，也没有把建议应用到 Draft、Published 或 WMS。
