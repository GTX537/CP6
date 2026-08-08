# E09-S05 外部访问审计与有效期交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e09-s05-external-audit`
- 起始基线：`a5b53a2b1efa1c66394da1ede2312195c44cc418`
- 功能提交：`83798dcf4dc0215f1d116cf9f291ee18605a4ebf`
- no-ff 集成提交：`c658871c652f8c0061cb7716c06aa9d518066a73`
- 数据库迁移：无

## 1. 交付结果

E09-S05 已补齐外部 Portal 会话入口、组织上下文选择、敏感资源查看、导出授权尝试和授权配置变化的审计链，并形成 Membership、Grant 与 FieldPolicy 在现有会话下一次请求即时重验证的机器证据。

认证边界继续复用现有 `Sys_SecurityLog`：密码、SSO、2FA 与原生客户端登录的成功、失败、锁定和刷新事件均由统一安全审计记录。认证后的 Space 外部访问进入追加写 `Space_AuditEvent`，避免复制凭证校验或建立第二套登录事实源。

本卡没有新增外部端点、DTO、数据库表或迁移，OpenAPI 保持 36 paths / 47 operations；独立导出 Job API、Portal 前端体验和平台支持人员临时授权仍不在本卡范围内。

## 2. 审计合同

### 2.1 Portal 读取

`SpaceAuditOperationAttribute` 为下列受控读取锁定稳定动作码：

| 动作码 | 资源 | 含义 |
|---|---|---|
| `space.external.portal.session` | `ExternalSession` | 已认证外部主体进入 Portal 并枚举可进入组织 |
| `space.external.organization.select` | `ExternalOrganization` | 服务端验证当前组织上下文并返回可见 Site |
| `space.external.portal.view` | `PublishedScene` / `Stock` / `Task` | 查看裁剪后的 Published 场景或敏感 Overlay |

事件记录 Tenant、Actor、OrganizationContext、Correlation/Trace、Site、结果、稳定失败码、条目数、`AuthorizationVersion`、客户端类型、IP 与 User-Agent。404 范围拒绝记为 `Denied`，异常只保存安全分类和指纹，不保存请求体、凭证或异常明文。

普通只读审计按详细设计失败开放：已完成范围裁剪的安全 DTO 不因审计存储瞬时故障变成业务中断，生产 writer 同时产生脱敏运维错误日志。

### 2.2 授权变化

Organization、Membership、Grant 与 FieldPolicy 的 create/update 端点使用稳定业务动作码，资源 ID 只从声明过的 Guid 路由参数读取，不读取或序列化请求体。既有全局 Space mutation filter 继续提供：

- 动作执行前必须成功追加 `Started`；失败返回 `SPACE_AUDIT_UNAVAILABLE`，不执行授权写入。
- 动作完成后追加 `Succeeded`、`Denied` 或 `Failed`；成功写入后若最终审计不可用，返回 `SPACE_OPERATION_OUTCOME_UNKNOWN`。
- 权限码固定为 `space:external:manage`，授权过滤器的拒绝事件继续由 `space.permission.check` 记录。

暂停、撤销和退休均通过相应 update 端点的状态变化完成，因此与普通修改使用同一稳定资源 ID、动作码和关联链。

### 2.3 导出尝试

WebApi 在 `ISpaceAccessEvaluator` 外增加审计装饰器。外部主体每次 `Export` 求值都会记录允许或拒绝结果，并写入：

- OrganizationId、OrganizationSecurityStamp、MembershipSecurityStamp。
- AuthorizationVersion。
- 命中的 GrantId 与 FieldPolicyId。
- ResourceType、SiteId、稳定 ReasonCode 和客户端元数据。

导出属于高风险行为；审计追加失败时装饰器清空命中的 Grant/Policy，并以 `SPACE_AUDIT_UNAVAILABLE` 拒绝授权。当前没有新增独立导出端点，但任何后续导出 Job 调用同一求值器时都会自动继承该失败关闭边界。

## 3. 有效期与现有会话失效

`SpaceAccessEvaluator` 不信任会话中缓存的 Membership/Grant/Policy 结论。每次请求都以 UTC 时钟重新读取 Active Organization、有效 Membership、有效 Grant 和 Active FieldPolicy，并重算安全戳与授权版本。

自动化覆盖同一服务实例先成功、随后下一请求立即失败或观察新版本：

- Membership 到期、Suspended、Revoked。
- Grant 到期、Suspended、Revoked。
- FieldPolicy Retired。
- Active FieldPolicy 版本变化导致下一响应 `AuthorizationVersion` 改变。

真 SQL 用例进一步证明：初始导出允许；时钟越过有效期后先命中 Membership 失效；续期成员后命中 Grant 失效；续期 Grant 后重新允许且授权版本变化；Policy 退休后下一求值立即拒绝。测试没有依赖重新登录、重建服务或清空内存缓存。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 审计 writer/filter/export 装饰器聚焦 | 48 passed / 0 failed / 0 skipped |
| Portal 有效期与状态变化 | 15 passed / 0 failed / 0 skipped |
| 合并态 Portal + 真 SQL 有效期聚焦 | 16 passed / 0 failed / 0 skipped |
| Space Unit 全量 | 231 passed / 0 failed / 0 skipped |
| Space Integration + KOUSQLSERVER 全量 | 195 passed / 0 failed / 0 skipped |
| CP6.Tests 全量 | 2720 passed / 0 failed / 17 既有环境门禁 skipped |
| 完整 `CP6.slnx` 非增量构建 | 0 error / 10 条既有 warning；本卡无新增 warning |
| EF 模型漂移 | `No changes have been made to the model since the last migration.` |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |
| TypeScript SDK strict no-emit | passed |
| 前端类型检查 | passed |
| 前端全量 | 106 files / 607 tests passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 暂存差异 whitespace | passed |

## 5. GA 状态与后续范围

E09-S01～S05 的代码与自动化证据已经完整进入受控集成基线。产品、QA、WMS 与安全负责人的正式 GA 签字属于发布治理动作，本报告提供签字所需机器证据，但不代替负责人签署。

下一张建议独立卡为 E04-S06“2D/3D 同源预览”：其 E04-S03、E05-S03 依赖均已满足，可验证保存后 2D/3D 对象数量、标识和尺寸一致。E04-S05 继续等待 E02-S07；E10 属于 P2，不应抢在尚未完成的 MVP 依赖链前整包启动。
