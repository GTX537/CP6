# E05-S04 平台公共与租户资产库完成报告

- 状态：**Complete**
- 证据日期：2026-07-30
- 功能提交：`85b57960`
- 集成提交：`888de795`

## 1. 交付结论

Design API v1 新增：

```http
GET  /api/space/design/v1/assets
POST /api/space/design/v1/assets
```

资产库采用两层可见性：

- `System`：平台公共资产，全部租户只读可见；
- `Tenant`：租户私有资产，仅所有者租户可读写；
- v1 不提供跨租户共享、市场发布或公共提升能力；
- 租户 API 的 `POST` 只接受 `scope=Tenant`，伪造
  `scope=System` 稳定返回 `SPACE_ASSET_SCOPE_DENIED` / 403。

列表支持 `scope`、`category`、`limit` 与受保护的 `cursor`。默认查询只返回
System 公共资产与当前 Tenant 的私有资产，并稳定按范围、资产代码和 ID 排序。

## 2. 资产头与不可变版本

新增持久化模型：

- `Space_Asset`：资产逻辑头，保存范围、所有者、代码、名称、分类和生命周期；
- `Space_AssetVersion`：不可变具体版本，保存版本号、格式、参数 Schema、预览引用、
  渲染制品引用和内容 SHA-256。

租户创建资产时，资产头与 `VersionNo = 1` 的 Ready 版本在同一个 Serializable
事务中落库。`Idempotency-Key` 与规范化请求体绑定；同键同请求返回原结果并标记
`Idempotent-Replay: true`，同键不同请求返回
`SPACE_IDEMPOTENCY_KEY_REUSED` / 409。重复资产代码返回
`SPACE_ASSET_CONFLICT` / 409。

参数 Schema 必须是最大 256 KiB 的 JSON 对象；预览和渲染引用只允许内部对象键，
拒绝绝对 URI、路径穿越和控制字符；内容哈希必须为 64 位 SHA-256。租户 API
不接收可执行脚本或外部模型 URL，只登记经过上游处理的内部渲染制品引用。

## 3. 元素引用与场景契约

`SpaceElementRevision.ModelAssetId` 现在表示具体 `SpaceAssetVersion.Id`，不再允许
通过 Placement 方法写入任意 GUID。元素附加资产时同时固化：

- `ModelAssetScope`；
- 内部持久化的 `ModelAssetOwnerTenantId`；
- 具体且不可漂移的 `ModelAssetId`。

领域层仅允许当前 Tenant 的私有版本或 System 版本；asset geometry 的
`assetVersionId` 必须与附加版本一致。Scene DTO 返回 `ModelAssetId` 与
`ModelAssetScope`，但不暴露内部所有者 Tenant ID。

Published→Draft 克隆保留相同的具体资产版本、范围与所有者，不会自动漂移到
“latest”。

## 4. 数据库与升级边界

数据库通过范围/所有者 Check Constraint 与
`(Scope, OwnerTenantId, AssetVersionId)` 复合外键形成第二道边界：

- System 引用的 Owner 必须为平台空身份；
- Tenant 引用的 Owner 必须等于元素 Tenant；
- 三个引用字段必须全空或全非空；
- 删除资产或资产版本不会级联删除设计元素。

新增 Migration：

`20260731010047_SpaceE05S04AssetLibrary`

并提供对应幂等 SQL：

`CP6.Space.Infrastructure/Migrations/Scripts/20260731010047_SpaceE05S04AssetLibrary.sql`

旧库中无法证明语义的非空 `ModelAssetId` 不会被静默清空，也不会被误解释为新的
版本 ID。Migration 在建表前以 SQL 错误 51000 失败关闭，要求先审计并清理旧引用；
测试同时确认失败后不会写入 E05-S04 Migration history。

## 5. OpenAPI、权限与 SDK

OpenAPI 已加入 `GetAssets`、`CreateAsset`、资产 Schema、幂等请求头和重放响应头，
并重新生成：

- C# `CP6.Space.Client`；
- TypeScript `space-design-v1` Fetch Client。

读取要求 `space:model:read`，创建要求 `space:model:edit`。资产是租户级建模资源，
不绑定单个 Site；服务仍要求经过验证的 Tenant 与 Actor 执行上下文。

## 6. 验证

| 检查 | 结果 |
|---|---|
| `CP6.Space.UnitTests` | 203 passed，0 failed，0 skipped |
| 默认 `CP6.Space.IntegrationTests` | 46 passed，41 SQL-gated skipped |
| E05-S04 真实 SQL | 2/2 passed，0 skipped |
| E05-S03 场景真实 SQL 回归 | 1/1 passed，0 skipped |
| E05-S01/S02 真实 SQL 回归 | 2/2 passed，0 skipped |
| Version Clone 真实 SQL 回归 | 6/6 passed，0 skipped |
| OpenAPI + 权限聚焦测试 | 14/14 passed |
| SDK 生成漂移 | 通过 |
| C# SDK build | 0 warnings，0 errors |
| TypeScript SDK strict compile | 通过 |
| EF pending model | 无待迁移模型变更 |
| `dotnet build CP6.slnx -c Release --no-restore` | 0 errors；当前全解增量构建 0 warnings；包含 Android 打包 |
| 范围与格式 | 手写文件格式、提交差异和能力污染审计通过；未混入 S05 生成器、外部 URL 加载或脚本执行 |

默认跳过项是 SQL 环境门禁，不计作已通过。真实 SQL 聚焦测试已在本机
`KOUSQLSERVER` 使用 Windows 集成认证执行，临时数据库均由测试清理。受限执行令牌
无法透传 SSPI，因此有效数据库证据来自沙箱外的同一测试二进制；这不改变代码或
生产配置。

## 7. 下一步

E05-S05 现已无 S04 前置阻塞。下一张卡应只扩展逐层货架和通用元素的参数化 3D
生成，并继续以本卡的内部资产版本引用作为渲染输入；不得把资产 Owner 暴露到公开
场景，也不得把运行态库存、任务、人员或设备事实写回 Design Revision。
