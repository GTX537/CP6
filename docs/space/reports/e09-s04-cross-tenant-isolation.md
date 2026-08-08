# E09-S04 跨租户越权自动化交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e09-s04-tenant-isolation`
- 起始基线：`dfacbb48afe3c0b0e50024e791ad539ba57c8430`
- 功能提交：`f045bd6fbb77bdefa84d10a51957128cdc22a740`
- no-ff 集成提交：`c82d4faeb79fb26d5ab9c982d25640e6374e9ebc`
- 数据库迁移：无

## 1. 交付结果

E09-S04 已建立外部协作跨租户越权自动化矩阵，并把猜测 ID、同码实体、Portal 场景/运行态响应、分页游标和缓存授权版本纳入发布阻断门禁。测试覆盖 Organization、Membership、Grant、Grant 子范围、FieldPolicy、PublishedScene、Stock 和 Task；任一上下文不一致均返回统一失败结果，不暴露目标是否存在。

本卡没有新增端点、DTO、OpenAPI 操作或数据库表。Design V1 合同保持 36 paths / 47 operations，E09-S03 Portal 仍为唯一外部入口，并继续只允许 `GET/HEAD`。

## 2. 自动化越权矩阵

### 2.1 猜测标识与 Portal

- 猜测其他 SiteId 访问 PublishedScene、Stock 或 Tasks，统一返回 `SPACE_EXTERNAL_SCOPE_DENIED` / 404。
- 猜测其他 Organization Context 访问 Sites、PublishedScene、Stock 或 Tasks，统一返回相同 404，不区分组织不存在、无成员关系或无 Grant。
- 运行态响应即使携带与当前租户相同的仓库/库位业务码，只要 LocationLogicalId 不在当前 Published 候选集合中就失败关闭。
- 场景读取器返回错误 Site、ModelVersion 或 Floor/Site 绑定时失败关闭，不允许下游投影把错误场景伪装成当前 Published 数据。
- 库存/任务运行态响应必须同时匹配请求 SiteId 和当前 PublishedVersionId；错站点或错版本不进入字段裁剪阶段。

### 2.2 同码租户隔离

内存数据库和真实 SQL Server 使用两个租户创建完全相同的用户 ID、Site/Floor/Zone LogicalId、外部组织业务码、字段策略名称、Owner 和 Task 业务 ID。两个租户分别只可读取自己的 Organization、Membership、Grant、四类 Grant 子范围、FieldPolicy 和 FieldPolicyField；`IgnoreQueryFilters` 审计视图能看到两套数据，证明测试不是空数据误通过。

真实 SQL 用例同时验证租户范围唯一索引允许不同租户使用相同业务码，而所有正常查询仍由 `TenantId + !IsDeleted` 全局过滤器隔离。既有复合租户外键测试继续阻断跨租户 Grant/Policy 子记录伪造。

### 2.3 游标与缓存边界

Data Protection 游标现在有自动化证据证明绑定：

- TenantId
- ActorId
- OrganizationContextId
- `space_grant_version`
- 资源名、过滤哈希、偏移和 15 分钟有效期

任一租户、用户、组织、授权版本、资源或过滤条件变化都会返回 `SPACE_CURSOR_SCOPE_MISMATCH`，篡改或过期返回 `SPACE_CURSOR_INVALID`。

外部授权 `AuthorizationVersion` 的哈希材料已显式加入 TenantId、UserId、OrganizationId 和 ResourceType，并继续包含组织/成员安全戳、GrantId/GrantVersion 与 FieldPolicyId/PolicyVersion。同一授权集合的 PublishedScene、Stock 和 Task 版本不再相同，可作为响应缓存键中的授权分区；具体缓存键仍必须同时包含请求 SiteId。本卡未引入服务器端 Portal 响应缓存。

## 3. 防御性修正

自动化矩阵推动了三项生产防护：

1. Published 场景投影前验证 Schema、Authority、无运行覆盖、Site、PublishedVersion、Published 状态和 Floor/Site 身份。
2. Stock/Task 运行态响应在处理任何条目前验证 SiteId 与 PublishedVersionId。
3. AuthorizationVersion 显式绑定租户、用户、组织和资源类型，防止未来缓存只依赖版本值时跨上下文复用。

这些校验位于 Portal 服务和访问求值器边界，不依赖前端隐藏、业务码唯一性或运行适配器自律。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| E09-S04 Portal/求值器/隔离矩阵聚焦 | 16 passed / 0 failed / 0 skipped，含真实 SQL |
| 游标跨租户/跨组织/授权版本聚焦 | passed |
| Space Unit 全量 | 231 passed / 0 failed / 0 skipped |
| Space Integration + KOUSQLSERVER 全量 | 187 passed / 0 failed / 0 skipped |
| CP6.Tests 全量 | 2713 passed / 0 failed / 17 既有环境门禁 skipped |
| 完整 `CP6.slnx` 构建 | 功能态 0 warning / 0 error；合并态非增量 0 error / 10 条既有 warning |
| EF 模型漂移 | 无待生成模型变化 |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |
| TypeScript SDK strict no-emit | passed |
| 前端类型检查 | passed |
| 前端全量 | 106 files / 607 tests passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 暂存差异 whitespace | passed |

no-ff 合并后在 `c82d4fae` 上复验：隔离矩阵含真实 SQL 16/16、游标与执行上下文中间件 42/42、完整 solution 非增量构建 0 error、EF 无模型漂移、SDK `-Check`、TypeScript strict no-emit、前端 type-check、106 files / 607 tests 和 production build 全部通过。

## 5. 后续范围

下一张建议卡为 E09-S05：补齐外部登录、组织选择、Portal 查看、导出尝试、授权/策略变化和有效期失效审计；验证 Grant 或 Membership 到期、暂停、撤销后现有会话的下一次请求立即失效，并形成产品、QA、WMS 与安全签字所需的 GA 证据。独立导出端点、平台支持人员临时授权和 Portal 前端体验仍应作为明确子范围单独排卡。
