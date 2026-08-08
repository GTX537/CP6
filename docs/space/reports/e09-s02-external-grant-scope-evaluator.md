# E09-S02 外部组合授权与访问求值器交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e09-s02-external-grants`
- 起始基线：`8869ac587dc0274d35a668ae44d933ac0eaeb1d8`
- 功能提交：`cae12c7eb303f50fe97e5be192e1260a99b69f44`
- no-ff 集成提交：`feefa9cdd18578f6e45a05d499f055c82c13d240`
- 数据库迁移：`20260801182535_SpaceE09S02ExternalGrants`

## 1. 交付结果

E09-S02 已在 E09-S01 的 Organization/Membership 权威之上建立租户隔离的组合 Grant 与统一访问求值器。授权维度包括 Site、Floor、Zone、Owner 和 BusinessObject；Site 必填，Floor/Zone 使用当前 Published Revision 的稳定 LogicalId，Owner 与 BusinessObject 使用规范化子表保存，不以 JSON 列表或逗号文本承载权限。

同一组织的多个 Grant 保持为完整授权子句的 OR；单个 Grant 内各维度严格按 AND 匹配。求值器不会把不同 Grant 的 Floor、Zone、Owner 或 BusinessObject 分别并集后再做笛卡尔拼接，因此 “Grant A 的楼层 + Grant B 的货主” 不会产生额外权限。读与导出分别求值，导出还要求命中的完整子句显式启用 `CanExport`。

外部主体一次请求必须携带且只携带一个 Organization Context。求值器验证可信执行上下文中的 Tenant/User、Active Organization、当前时刻有效的 Active Membership，以及当前时刻有效的 Active Grant；任一缺失、不一致、过期、暂停、撤销或跨组织拼接均失败关闭。查询范围同时携带 Organization/Membership 安全戳、GrantVersion 与确定性 `AuthorizationVersion`，为后续授权缓存失效提供稳定依据。内部主体继续走独立 internal scope，不允许伪造 Organization Context。

## 2. 管理 API、权限与范围边界

新增管理端点：

- `GET/POST /api/space/external-organization/{organizationId}/grant`
- `GET/PUT /api/space/external-organization/{organizationId}/grant/{grantId}`

读取继续要求 `space:external:read`，变更继续要求 `space:external:manage`。创建和更新会验证 Site 存在当前 Published 模型、Floor/Zone 属于该 Published Revision、选中 Zone 属于选中 Floor，并规范化、去重和限制 Owner/Object 维度数量。授权变更递增 GrantVersion 和 Organization SecurityStamp；Revoked Grant 为终态。

`FieldPolicyId` 已进入模型和求值合同，但在 E09-S03 字段策略落地前，管理请求只要传入非空值就稳定返回 422，不会形成“有策略 ID、无策略执行”的假安全状态。现有 `SpaceExecutionContextMiddleware` 仍全局拒绝外部主体直接访问 `/api/space`；本卡只交付管理面和求值基础设施，没有提前开放 Portal、Published-only DTO 或字段 allowlist。

## 3. 数据库权威与部署脚本

迁移新增 5 张表：

- `Space_ExternalGrant`
- `Space_ExternalGrantFloor`
- `Space_ExternalGrantZone`
- `Space_ExternalGrantOwner`
- `Space_ExternalGrantObject`

主表以 `TenantId + OrganizationId` 复合外键引用同租户组织，子表以 `TenantId + GrantId` 复合外键引用同租户 Grant。数据库检查约束固定 Grant 状态范围、有效期和正数 GrantVersion；过滤唯一索引固定每个当前 Grant 的 Floor、Zone、规范化 Owner 和规范化 BusinessObject 唯一性。所有表继续使用 TenantId + soft-delete 全局查询过滤，删除行为为 Restrict。

真实 SQL Server 测试验证了子范围唯一性、跨租户伪造外键、有效期约束，以及更新时“软删除旧范围 + 写入相同新范围”的原子替换。幂等部署脚本按 S01→S02 增量生成，并显式设置 SQL Server 过滤索引所需的 ANSI/QUOTED_IDENTIFIER 选项；临时数据库先迁到 S01 后连续执行脚本两次，最终得到 5 张 Grant 表和唯一一条 S02 迁移历史记录。

## 4. 契约与客户端

Design V1 OpenAPI 新增 2 个 route family / 4 个操作，总操作数增至 38，并重新生成：

- `docs/space/contracts/design-v1.openapi.json`
- `CP6.Space.Client/SpaceDesignV1Client.g.cs`
- `sdk/typescript/space-design-v1/spaceDesignV1Client.ts`

运行时客户端表面哈希更新为 `FFCE63E749C7653E553A57D32EA85A7FF846F17199AF3436FE787CD26F509259`。OpenAPI/C#/TypeScript 生成漂移、C# SDK 构建、TypeScript strict no-emit 和权限反射守卫全部通过。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Grant 领域聚焦 | 4 passed |
| AccessEvaluator + GrantService 内存聚焦 | 8 passed |
| Grant 真实 SQL 聚焦 | 2 passed / 0 skipped |
| Space Unit 全量 | 228 passed / 0 failed / 0 skipped |
| Space Integration + KOUSQLSERVER 全量 | 169 passed / 0 failed / 0 skipped |
| 权限与 OpenAPI/SDK 契约聚焦 | 35 passed |
| CP6.Tests 全量 | 2703 passed / 0 failed / 17 既有环境门禁 skipped |
| 完整 `CP6.slnx` 构建 | 0 error；合并态 10 条既有可空性/测试分析 warning |
| EF 模型漂移 | 无待生成模型变化 |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |
| C# / TypeScript SDK 编译 | passed |
| 增量 SQL 部署 | S01→S02 首次执行与重复执行均 passed |
| 前端类型检查 | passed |
| 前端全量 | 106 files / 607 tests passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 暂存差异 whitespace | passed |

no-ff 合并后又在 `feefa9cd` 上复验：完整 solution 构建 0 error、领域 4/4、访问求值/管理/真实 SQL 10/10（0 skipped）、权限与 OpenAPI 35/35、EF 无模型漂移、SDK `-Check` 全部通过。

## 6. 后续范围

下一张建议卡为 E09-S03：把当前求值器接入外部只读 Portal，固定只读 Organization Context 选择、Published-only 查询、资源 DTO allowlist、字段策略/脱敏和导出裁剪，并验证任一字段策略缺失或资源上下文不完整时失败关闭。在 E09-S03 完成前，不得移除外部主体对 `/api/space` 的全局拒绝，也不得把 Grant 管理 API 误当成已经开放的外部门户。
