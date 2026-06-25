# S 类 #3 SSO/OIDC — 真浏览器 QA 环境（T10）

2026-06-25 T10 gstack 真浏览器 QA 用的可复现环境。数据已种入 dev 库 `CP6DB`；本目录是种子脚本 + mock IdP 源，供 DB 重置后重建。

## 组件

| 组件 | 地址 | 说明 |
|---|---|---|
| 后端 | http://localhost:5177 | `ASPNETCORE_ENVIRONMENT=Development Security__Sso__FrontendBaseUrl=http://localhost:5173 dotnet run --project CP6.WebApi --no-launch-profile --urls http://localhost:5177` |
| 前端 | http://localhost:5173 | `cd cp6.web && npm run dev`（vite 代理 /api → 5177） |
| mock OIDC IdP | https://localhost:5099 | 见 `mock-idp/`，用 ASP.NET dev cert（须 `dotnet dev-certs https --trust` 受信） |

> ⚠️ **FrontendBaseUrl 必配**：为 null 时 callback 落地重定向是相对路径 `/sso/landing`，跨源 dev 会落到后端口 5177（无 SPA）。QA 启动后端务必带 `Security__Sso__FrontendBaseUrl=http://localhost:5173`。

## 种子数据

| 租户 | TenantCode | SSO | 用途 |
|---|---|---|---|
| 默认 | DEFAULT | 未配 | E-SEC-020（未配/未启用）|
| QA-B | TENANTB | Enabled，**非强制** | 完整 SSO 闭环 + JIT 供给 |
| QA-C | TENANTC | Enabled，**强制** | E-SEC-021 强制拦截 + break-glass |

| 用户 | 租户 | 角色 | 密码 | 备注 |
|---|---|---|---|---|
| sso_admB | TENANTB | 1 | 123456 | 配 B 租户 SSO 的管理员 |
| sso_admC | TENANTC | 1 | 123456 | 配 C 租户 + break-glass（AllowPasswordFallback=1）|
| sso_userC | TENANTC | 3 | 123456 | 普通用户，测 E-SEC-021 强制拦截 |
| sso.jit@example.com / qa.browser@example.com | TENANTB | 3 | — | SSO JIT 联邦供给生成（DefaultRoleId=3）|

所有密码用户复用 `admin` 的 BCrypt 哈希 → 密码同为 `123456`。

## 重建步骤（DB 重置后）

```bash
# 1. 租户 + 用户（幂等，自带 DELETE 清理）
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB -E -C -i 01_seed_tenants_users.sql
#    若报 -E/-U 冲突，改用 PowerShell：Invoke-Sqlcmd -ServerInstance "localhost\KOUSQLSERVER" -Database CP6DB -InputFile 01_seed_tenants_users.sql

# 2. 给 B/C 补种 menu 116 操作点授权（query/edit）—— Sys_RoleAction 带 TenantId，原 seed 只授 DEFAULT
sqlcmd ... -i 02_seed_roleaction_BC.sql
#    ⚠️ 补权限后须重启后端，PrewarmAsync 的权限上下文是内存分布式缓存，不随补权刷新。

# 3. SSO 配置（ClientSecret 必须经运行中后端的 DataProtection 加密，不能裸 SQL）
#    起后端后，登录各租户 admin → PUT /api/sys/sso-config：
#    （DecryptClientSecret 对 null 抛 E-SEC-028，故 ClientSecret 必须有值）
curl -s -c /tmp/jb.txt -X POST http://localhost:5177/api/auth/login -H "Content-Type: application/json" \
  -d '{"userName":"sso_admB","password":"123456","tenantCode":"TENANTB"}'
curl -s -b /tmp/jb.txt -X PUT http://localhost:5177/api/sys/sso-config -H "Content-Type: application/json" \
  -d '{"authority":"https://localhost:5099","clientId":"cp6-client","clientSecret":"dummy-secret","scopes":"openid email profile","emailClaim":"email","enabled":true,"enforced":false,"autoProvision":true,"defaultRoleId":3}'
# C 租户同理，tenantCode=TENANTC、enforced=true、用户 sso_admC。

# 4. 起 mock IdP
cd mock-idp && ASPNETCORE_ENVIRONMENT=Development dotnet run --urls https://localhost:5099
```

## 浏览器走查清单（全部 2026-06-25 通过）

1. 登录页「SSO でログイン」入口渲染 + i18n。
2. 完整闭环：填 TenantCode=TENANTB → 点 SSO → IdP 表单签入（输任意 email）→ callback → 落地 → 进 `/wf/todo`，JIT 新建 role3 用户。
3. E-SEC-021：sso_userC / 123456 / TENANTC 密码登录 → 400 + ElMessage「SSO 必須」。
4. break-glass：sso_admC / 123456 / TENANTC 密码登录 → 200 → /dashboard。
5. 配置页 `/sys/sso-config`：表单填充 + clientSecret 脱敏（空值 + 占位「設定済み」，GET 无密文）。
6. 落地 error：`/sso/landing?error=E-SEC-024` → 本地化错误。
