# CRM 首个业务切片：CP6 身份与目录桥

> 2026-09-09 C01 增量：独立 `CP6.Services` 服务签发、JWKS 公共缓存与 RSA 轮换见 [C01 服务身份](C01-SERVICE-IDENTITY.md)。下文“不提供 client-credentials”描述首个浏览器切片的历史范围；CP6.Web 用户协议继续按本页执行。

用途：独立 CRM BFF 消费 CP6 现有用户、租户和权限。状态：任务分支实现与本地验证，等待跨仓集成和远端门禁；更新：2026-09-08 UTC。此实现不代表 C01/C02 全部架构、商业 Provisioning、M0、Pilot 或生产验收完成。

## 身份流程与协议

`CrmOidc:Enabled` 默认 `false`。未启用时发现、密钥、组织解析及 `/connect/*` 返回 404。启用时只提供注册的机密客户端 `CP6.Web`、授权码 + S256 PKCE、`client_secret_basic`。Scope 必须是 `openid profile crm`，仅支持 `response_type=code`、query 响应及空 prompt / `none`，不提供隐式、密码、client-credentials 或 refresh-token grant。

| 接口 | 用途 |
| --- | --- |
| `GET /.well-known/openid-configuration` | 静态配置的 issuer、端点、RS256、S256、机密客户端元数据 |
| `GET /.well-known/jwks.json` | RSA 公钥环；包含 `kid`、`n`、`e`，没有私钥参数 |
| `GET /connect/organizations/{slug}?client_id=CP6.Web` | 解析显式开通且真实启用的租户；缺失、停用、到期或未知客户端统一 404 |
| `GET /connect/authorize` | 校验精确回调、state、nonce、PKCE；可传 `organization_id` 限定真实单租户成员身份 |
| `GET /api/auth/crm-authorize` | 本站内部续接，保持原 `cp6_rt` Cookie 的 `/api/auth` Path，绑定同一真实浏览器刷新令牌链 |
| `POST /connect/token` | Basic 客户端认证 + form 授权码兑换；一次性 SQL 原子消费 |
| `GET /connect/userinfo` | 显式 RS256 Bearer，返回 `sub`、`name`、`org_id`、`tenant_id`、`sid`、`jti` |
| `GET /connect/crm-context` | 显式 RS256 Bearer；在线校验身份并返回当前真实 CRM 权限和目录 |
| `POST /connect/end-session` | BFF 后端 Basic + form `id_token_hint`、精确 `post_logout_redirect_uri`、可选 state；返回 `{logoutContinuationUri}` |
| `GET /api/auth/crm-logout?ticket=...` | 不含身份令牌的短时一次性退出续接；本站导航用于恢复 Strict Cookie 的发送 |
| `GET /api/auth/crm-logout-complete?ticket=...` | SQL 原子消费退出 ticket，仅清除属于原刷新令牌链的浏览器 Cookie，再跳转精确注册的退出落点 |

Issuer 必须是提供现有 CP6 `/login` 页面且代理 `/connect`、`/.well-known` 和 `/api` 的 HTTPS origin，无末尾 `/`。Vite 与 Web nginx 已增加代理。仅 Development 可同时显式启用 `AllowInsecureLoopback` 以测试 HTTP loopback；生产启动拒绝该例外。

未登录时跳往已有 CP6 登录页，`oidc_return` 只允许本地 `/connect/authorize?` 路径。有效期十分钟的 sessionStorage 续接覆盖密码、SSO、2FA、强制改密后的重新登录。初次跨站导航遗漏 Strict Cookie 时，由本站 profile 请求恢复已有登录。旧 Cookie 缺少版本或浏览器会话绑定时必须重新登录。

## 令牌与撤销

用户 access token 的 `aud=CP6.Web`、`typ=at+jwt`；ID token 的 `aud=CP6.Web`、`typ=JWT`。两者只用 RS256，验证固定 issuer、audience、算法、类型、`kid` 和时效；`CP6.Services` 不接受这些用户令牌，也未实现服务令牌签发。Claims 包含真实 `sub`、`tenant_id`/`org_id`、`sid`、`jti`、`iat`、`nbf`、`exp`；ID token 另外绑定 nonce。没有推测的 MFA/`amr`/`acr`。

Access/ID 有效期最多五分钟，并不得超过来源 CP6 access Cookie 的到期时间。没有八小时会话或刷新承诺。来源必须是完整登录，拒绝 2FA pending、强制改密、impersonation、旧认证版本、停用/锁定用户、停用/到期租户、错误租户、已撤销 access 或 refresh 链。兑换和每次 userinfo/context 都重新读真实状态；权限聚合不消费旧会话缓存。

只在桥启用后的完整 Web 登录建立 `Sys_BrowserSessions`，保存该次登录实际证明的认证版本；原 HS256 浏览器 access Cookie 加入签名 `cp6_session` 刷新令牌绑定，`Sys_RefreshTokens.BrowserSessionId` 在轮换时保持不变。CP6 认证事件按持久化会话身份查询，正常轮换不受链长度限制，退出后所有绑定该会话的 access 都失败。密码及 2FA 变化不能由旧 refresh 升级为新认证；2FA 入会、关闭和管理员重置推进独立 `AuthenticationEpoch`，启用后再关闭也不能恢复旧认证版本。授权入口同时比较当前账户、签名 claim 与原登录记录。原生令牌、桥关闭时新建的会话及迁移前 refresh 保留原行为；缺少原登录记录的旧 refresh 即使正常轮换也必须重新完整登录才可进入 CRM。

全局退出在 BFF 后端完成：验证签名 ID hint 并精确匹配客户端、sid、sub、tenant，SQL Serializable 事务锁定原 `Sys_BrowserSessions` 行，写入显式退出标记并批量撤销该会话的 refresh 与 CRM grants，来源 jti 加入既有黑名单。真实 refresh 轮换在同一会话锁下重新校验，退出先完成或轮换先提交均不能产生逃逸后继。显式退出与已轮换令牌的真实重用分开处理：退出后的 refresh 失败不撤销其他浏览器/原生设备，而未退出会话的真实重用继续触发现有账户级撤销。退出 hint 可过期，但 grant 仅保留最多约一天的退出查找窗口；过期 hint 永远不能登录或调用业务接口。

身份/access token 不出现在退出 URL；浏览器仅收到一分钟有效的随机退出 ticket。Cookie 完成阶段再次按原链比较，错误会话不清 Cookie。BFF 必须先撤销自己的本地会话，若上游失败明确显示部分退出状态，不得声称全局退出成功。

## 真实组织、成员、部门与权限

`Sys_User.Id` 是 subject，`Sys_User.TenantId` 是其唯一组织，`Sys_Tenant.Id` 是 organization；没有臆造跨组织成员表。部署配置必须逐项指定真实 TenantId → 唯一 slug、region、`CrmEnabled=true`。没有配置或明确停用即无 CRM 资格；该显式 entitlement 桥尚未被商业 Provisioning 取代。

组织解析返回 `{organizationId, organizationSlug, region, crmEnabled:true}`。上下文 JSON 为：

```text
organizationId, organizationSlug, organizationName, region,
subjectId, displayName, departmentId, departmentPath,
allowedActions[], dataScope, departmentIds[], isSupervisor,
eligibleOwners[{subjectId, displayName, departmentId, departmentPath}],
departments[{departmentId, name, path}]
```

权限来自 `PermissionAggregator.BuildEnabledRolesAsync` 的真实主角色与附加角色、`Sys_RoleActions` 和 `Sys_RoleDataScopes`；额外排除停用或已删除角色，但不改变旧缓存调用语义。只投影真实 `crm-*` 操作键，包含 `crm-lead:query/add/edit/assign` 与实际授予的 `crm-site:*`；旧 Foundation 的禁用 Vue 菜单不阻断独立 CRM 的已授予操作，也不会自动开通租户。

数据范围资源键为 `crm-lead`：1/缺省为 `own`，2/3/4 为 `departments`，5 为 `organization`。部门范围只包含该租户真实启用部门；缺失或无效的树路径不会扩大到所有部门。主管身份必须同时具有真实 `crm-lead:assign` 与部门/组织数据范围。负责人候选要求真实启用、未锁定、无需改密、有效部门以及 `crm-lead:query` 和 `crm-lead:edit`，并在主管实际管理范围内；满足条件的当前用户可作为自身负责人。CRM 仍须逐次执行 owner/collaborator/department 业务过滤和操作授权。

## 配置与迁移

配置入口是 `CrmOidc`，对应类型 `CP6.WebApi/Services/CrmOidcOptions.cs`：

- `Enabled`、`Issuer`、`AllowInsecureLoopback`。
- `ActiveKeyId`，`Keys[]` 中的 `Kid` 与 PEM；活跃 key 需要至少 2048 位 RSA 私钥，其他项可只有公钥。PEM 由宿主 Secret Store 注入，禁止入仓库。轮换前先公布新公钥，再切换活跃 key；旧验证 key 至少保留退出窗口，不能只按五分钟 access TTL 删除。
- `Clients[]` 中的 `ClientId=CP6.Web`、`SecretSha256`（原始机密的 SHA-256 十六进制摘要）、精确 `RedirectUris[]`、`PostLogoutRedirectUris[]`。原始机密仅交付 BFF 的 Secret Store。
- `Organizations[]` 中真实 `TenantId`、唯一小写 `Slug`、`Region` 与默认 false 的 `CrmEnabled`。

前向迁移 `20260908010000_CrmOidcGrantStore` 创建独立操作表 `CrmOidcGrant`、`CrmOidcLogout`，由已有数据库初始化步骤执行后才可开桥；API 未增加新的自动建表或生产迁移路径。Grant 仅存授权码 SHA-256、绑定信息、时效和撤销状态，SQL BIN2 比较及字节长度共同保证精确回调，不接受尾部空格。原始 code、PKCE verifier、client secret、access/ID token 均不落这些表。每次签发批量清理已超过一天的 grant 和已过期 ticket。回退关闭开关并回退应用；不删除认证历史表，不做 Down 迁移。

后续前向迁移 `20260908011000_CrmOidcBrowserSessionFamily` 增加 `Sys_BrowserSessions`、可空的 `Sys_RefreshTokens.BrowserSessionId` 和默认为空 GUID 的 `Sys_Users.AuthenticationEpoch`，模型快照同步更新。既有 refresh 的原哈希和撤销状态不变，迁移不会为它们补造登录认证记录。启用桥前须执行两项迁移；回退保留这些附加列/表，不执行数据库 Down。

## 可复现验证与当前证据

本地 2026-09-08 UTC 安全复核后：97 项身份/旧登录/权限/2FA/refresh 测试全部通过；22 项真实 SQL Server 门禁全部通过且无跳过。SQL 覆盖 24 个独立连接同一码仅一次成功、精确绑定、大小写/尾空格、过期、跨 store 撤销、退出 ticket 重放，以及真实 `RefreshTokenService.RotateAsync` 的两种确定性退出竞态、70 次轮换、其他浏览器/原生设备隔离和真实重用撤销。实际前向升级保留既有认证数据，EF 模型无待迁移差异。前端返回路径测试、Vue type-check 与 production build 已通过；build 仍有既有大 chunk 提示。

安全修复后的完整 `CP6.Tests` 回归为 2981 通过、19 个仓库既有跳过（未配置的其他模块 SQL / 历史 SQLite 限制），不把这些跳过计为真实 SQL 验收。本切片专用 SQL 的 22 项已全部实际执行；完整回归 TRX 为 `evidence/oidc/crm-oidc-full-review.trx`。

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter 'FullyQualifiedName~CrmOidcBridgeTests|FullyQualifiedName~CrmBrowserSessionTests|FullyQualifiedName~AuthController|FullyQualifiedName~AuthCookie|FullyQualifiedName~PermissionAggregator|FullyQualifiedName~CsrfMiddleware|FullyQualifiedName~TwoFactor|FullyQualifiedName~RefreshToken'
# 从测试 Secret Store 提供测试实例连接；项目缺少该值时失败，不跳过也不改用模拟库。
dotnet test CP6.Oidc.IntegrationTests/CP6.Oidc.IntegrationTests.csproj
cd cp6.web
npm run test:unit -- src/views/oidcReturn.spec.ts
npm run type-check
npm run build-only
```

SQL 项目读取 `CP6_OIDC_TEST_SQL`，仅在指定测试实例创建 `CP6OidcTest_<32 hex>` 一次性数据库，并只清理该名称范围。用户、刷新与字段审计表 DDL 从真实 CP6 EF 模型生成，再执行实际家族迁移；两种轮换竞态调用真实 refresh 服务，SQL 阻塞观测只用于确认测试交错顺序。该项目不进入缺少 SQL 的默认解决方案；已有 PR/main SQL 工作流增加显式门禁并保存 TRX。本地复核 TRX 位于 `evidence/oidc/crm-oidc-identity-review.trx` 与 `crm-oidc-sql-review.trx`。测试数据只用于行为证据，不能视为真实租户开通、商业权限或生产验收。

[真实浏览器测试启动助手](../../eng/crm/browser-fixture/README.md) 可为真实 `CP6.WebApi` 和现有 Vue 登录页创建全新隔离 SQL 库、BCrypt 用户及权限，生成仅存本地的临时配置。它使用当前完整 EF 模型，不插入 Cookie、refresh 或授权会话；配置启用 Secure / SameSite=Strict Cookie 和 CSRF，并停用无关后台服务。本地实际启动真实 API 后，错误密码返回 400、正确密码返回 200，三个认证 Cookie 的安全属性已核验；这是 HTTP 启动冒烟证据，不替代后续浏览器 PKCE 验收。

2026-09-08 UTC 已在全新隔离 SQL fixture 完成 5 组跨仓真实浏览器验证：既有 Vue 密码登录，经跨站授权码/S256 返回 production Next/CRM authority；日历与人工分配/活动更正；正常/隔离询盘与浏览器绑定回执；角色范围、PII 遮罩、窄屏和全局退出；打开记录后直接撤销真实 SQL 角色的 PII 权限，旧页面提交被拒绝并清除联系人与草稿。五组全部通过，未拦截 API、伪造 Cookie 或关闭 TLS 校验。浏览器使用一个合成组织，双组织业务隔离另由 CRM SQL 测试覆盖。CRM 仓库 `scripts/e2e/README.md` 提供全新 fixture、四服务启动、测试与清理步骤；临时密钥、连接串和日志保留在仓库外私有目录。

仍需远端 exact-main 门禁、真实客户组织接入与部署输入验收；本地合成数据集成验证没有关闭 C01/C02、商业 Provisioning、Pilot 或生产门禁。
