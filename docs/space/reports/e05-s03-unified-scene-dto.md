# E05-S03 统一 Design Revision 场景 DTO 完成报告

- 状态：**Complete**
- 证据日期：2026-07-30
- 功能提交：`00021f0a`
- 集成提交：`a1edecef`

## 1. 交付结论

新增只读端点：

`GET /api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/scene`

响应根契约固定：

- `schemaVersion = 1`
- `authority = "DesignRevision"`
- `runtimeOverlayIncluded = false`
- 携带 Version、Site、状态、`ContentRevision` 与 `ContentHash`
- 聚合同一租户、同一 Model Version、同一 Floor 下的 Floor、Zone、Aisle、Rack、RackLevel、Location、Element 与 ElementAttribute

所有集合使用稳定排序。场景只从 `SpaceContext` 的版本化 Design Revision 表投影，不读取 Legacy `CP6Context`，也不复制库存、任务、人员或设备运行态事实。

旧 Rack/Location 继续只承担 Published 版本向运行系统物化的兼容职责，不成为建模权威，也不回流为 Design Revision 的事实副本。

## 2. 契约边界

`SpaceDesignSceneDto` 明确区分：

- `Location`：可发布的库位设计事实；
- `Element`：柱、墙、门、楼梯、输送线等通用语义元素；
- `ElementAttribute`：类型化设计属性，不承载位置库存事实；
- `ModelAssetId`：沿用既有可选资产引用。

本卡没有提前引入 E05-S04 的 `ModelAssetScope`、资产所有者或资产库 API，也没有实现 E05-S05 的参数化渲染。

## 3. 权限、租户与错误

- HTTP 权限固定为 `space:model:read`。
- 服务先校验执行租户，再通过 Model 的 Design V1/Cutover 状态与 Site 访问控制。
- 不存在或跨租户猜测的 Version 统一返回 404 / `SPACE_VERSION_NOT_FOUND`。
- Version 存在但 Floor LogicalId 不属于该版本时返回 404 / `SPACE_LOGICAL_ID_NOT_FOUND`。
- 场景端点不提供写入、库存覆盖或运行态叠加能力。

## 4. OpenAPI 与 SDK

冻结 OpenAPI 已增加 `GetScene` 操作，并重新生成：

- `CP6.Space.Client/SpaceDesignV1Client.g.cs`
- `sdk/typescript/space-design-v1/spaceDesignV1Client.ts`

生成闭环通过 drift check；C# 客户端 0 warning / 0 error，TypeScript 客户端通过 strict compile。

## 5. 数据库与兼容性

本卡只增加查询投影、HTTP 契约和生成客户端，不改变 EF 模型或数据库表。`has-pending-model-changes` 确认无待迁移变化，因此没有新增 Migration 或幂等 SQL。

现有 E05-S01 元素/属性、E05-S02 逐层货架规格和 E01-S04 Published→Draft Clone 均保持兼容。

## 6. 验证

| 检查 | 结果 |
|---|---|
| `CP6.Space.UnitTests` | 195 passed，0 failed，0 skipped |
| 默认 `CP6.Space.IntegrationTests` | 46 passed，39 SQL-gated skipped |
| E05-S03 真实 SQL | 1/1 passed，0 skipped |
| E05-S01/S02 真实 SQL 回归 | 2/2 passed，0 skipped |
| Version Clone 真实 SQL 回归 | 6/6 passed，0 skipped |
| OpenAPI + 权限聚焦测试 | 13/13 passed |
| SDK 生成漂移 | 通过 |
| C# SDK build | 0 warnings，0 errors |
| TypeScript SDK strict compile | 通过 |
| EF pending model | 无待迁移模型变更 |
| `dotnet build CP6.slnx -c Release --no-restore` | 0 errors，10 个既有 warnings；包含 Android 打包 |
| 范围与格式 | 手写文件格式通过；未混入 S04 资产库、S05 渲染或运行态载荷 |

默认跳过项是 SQL 环境门禁，不计作已通过；聚焦 SQL 已在本机 `KOUSQLSERVER` 使用临时数据库真实执行并在结束后清理。

## 7. 下一步

E05-S04 现已成为主链下一张无阻塞卡：独立实现平台公共只读与租户私有资产库。它必须复用本卡的 `ModelAssetId` 引用边界，不改变场景的 Design Revision 权威，也不提前进入 E05-S05 参数化渲染。
