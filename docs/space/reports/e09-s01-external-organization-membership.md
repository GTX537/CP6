+# E09-S01 外部组织与成员模型交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e09-s01-external-org`
- 起始基线：`0c02fc80ff8bb8fe81adb397d6f0761a9c74a5f0`
- 功能提交：`a599cfd7f49f00ccb832c688549325b1b3269c0f`
- no-ff 集成提交：`09538ca3ef78531db7c9cd1672cc3027de50e5cf`
- 数据库迁移：`20260801172135_SpaceE09S01ExternalOrganizations`

## 1. 交付结果

E09-S01 已建立租户内外部组织与成员关系的权威模型，为后续组合 Grant、字段策略和只读 Portal 提供安全根。

`Space_ExternalOrganization` 支持 Customer、Supplier 和 ThirdPartyLogistics 三类组织，包含规范化编码、名称、Active/Suspended/Closed 生命周期、可选 ERP BusinessPartner 关联、`SecurityStamp` 和 `rowversion`。编码唯一性固定为 `TenantId + Type + NormalizedCode`：同一组织类型不能重码，而客户、供应商和 3PL 可以合法使用相同业务码并保持组织隔离。ERP 关联必须属于当前租户；Customer/Supplier 分别验证对应客商标志，3PL 验证当前租户有效 BusinessPartner。

`Space_ExternalMembership` 将当前租户的 `Sys_User` 关联到外部组织，支持 Viewer、OperationsViewer、OrgAdmin，包含有效期、Invited/Active/Suspended/Revoked 状态、邀请/接受审计字段、`SecurityStamp` 和 `rowversion`。同一用户可以属于多个组织，但同一组织内只能有一个未撤销成员关系；Closed 组织和 Revoked 成员均为终态。组织或成员变化会递增安全戳，为后续授权缓存失效提供依据。

## 2. API、权限与失败关闭

管理端点固定在：

- `GET/POST /api/space/external-organization`
- `GET/PUT /api/space/external-organization/{organizationId}`
- `GET/POST /api/space/external-organization/{organizationId}/membership`
- `PUT /api/space/external-organization/{organizationId}/membership/{membershipId}`

读取需要 `space:external:read`，变更需要 `space:external:manage`；两项权限已加入租户管理员种子。端点继续要求认证和已验证的当前租户上下文，跨租户用户、客商或组织标识按 404/租户拒绝失败关闭，不泄露外部引用是否存在。重复组织身份、重复当前成员关系和非法生命周期转换返回稳定 ProblemDetails 错误码。

本卡只开放内部租户管理员的组织/成员管理面。外部主体仍不能直接访问 `/api/space`；Portal 主体选择、有效 Membership 校验、组合 Grant、Published-only 裁剪和字段 allowlist 分别属于 E09-S02/S03，未在本卡提前放开。

## 3. 数据库权威与约束

迁移同时提供 EF migration、Designer/Snapshot 和幂等 SQL 脚本，并继续使用独立的 Space 迁移历史表。数据库层固定：

- 组织类型、组织状态、成员角色、成员状态取值范围；
- BusinessPartner 类型与 ID 必须同时为空或同时存在；
- `ValidToUtc` 必须为空或晚于 `ValidFromUtc`；
- 成员通过 `TenantId + OrganizationId` 复合外键引用同租户组织；
- 当前组织编码、同类型 ERP 关联和同组织当前成员关系均由过滤唯一索引保护；
- 所有查询继续使用 TenantId + soft-delete 全局过滤，删除行为为 Restrict。

真实 SQL Server 测试验证了租户复合外键、同类型编码唯一、跨类型同码、当前成员唯一、Revoked 后可重新加入及有效期检查。为避免单实例 SQL Server 被随机数据库迁移并发压垮，所有真实 SQL 测试类共享串行 collection；纯内存测试仍保持并行。内存测试统一复用一个 `InMemoryDatabaseRoot` 并以唯一数据库名隔离，消除了 EF 内部 service-provider 阈值假失败。

## 4. 契约与客户端

Design V1 OpenAPI 已加入 4 个 route family / 7 个操作，并重新生成：

- `docs/space/contracts/design-v1.openapi.json`
- `CP6.Space.Client/SpaceDesignV1Client.g.cs`
- `sdk/typescript/space-design-v1/spaceDesignV1Client.ts`

运行时客户端表面哈希为 `6011AA0FC2B4B2A81C5D915B1DEE1D0ADC84BE01BB8D2962A3D087B896E1EF76`。生成检查、C# 客户端构建、TypeScript strict no-emit 和运行时 Swagger 哈希均通过。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space 外部访问领域聚焦 | 4 passed |
| Space 外部组织内存集成聚焦 | 5 passed |
| Space Unit 全量 | 224 passed / 0 failed / 0 skipped |
| Space Integration + KOUSQLSERVER 全量 | 159 passed / 0 failed / 0 skipped |
| 权限、种子、ProblemDetails、OpenAPI/SDK 聚焦 | 49 passed |
| CP6.Tests 全量 | 2703 passed / 0 failed / 17 既有环境门禁 skipped |
| 完整 `CP6.slnx` 构建 | 18 projects，0 error；7 条既有可空性/测试分析 warning |
| EF 模型漂移 | 无待生成模型变化 |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |
| TypeScript SDK strict compile | passed |
| 运行时 OpenAPI 客户端表面 | hash matched |
| 前端类型检查 | passed |
| 前端全量 | 106 files / 607 tests passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 暂存差异 whitespace | passed |

no-ff 合并后又在 `09538ca3` 上复验：领域 4/4、组织/成员内存与真实 SQL
6/6（0 skipped）、权限/种子/ProblemDetails/OpenAPI 49/49、EF 无模型漂移、
SDK `-Check` 和 TypeScript strict compile 全部通过。

全量门禁还修复了两个与本卡业务无关、但会使验证随时间或执行顺序漂移的测试问题：旧 E05-S02 迁移测试改为按当时 schema 直接造数；RFQ “有效报价”夹具不再在 2026-07-31 后自动过期。生产 RFQ 服务未修改。

## 6. 后续范围

下一张建议卡为 E09-S02：实现 Organization Context + Membership 有效性 + Site/Floor/Zone/Owner/BusinessObject 组合 Grant，并在任一维度缺失、歧义或跨组织拼接时失败关闭。E09-S03 再实现 Published-only 只读 Portal 与字段 allowlist/脱敏；在此之前不得把外部成员等同于现有租户管理员或普通内部用户。
