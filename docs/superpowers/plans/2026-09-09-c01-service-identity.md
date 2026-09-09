# C01 Service Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 在现有 CP6 issuer 中交付已批准的服务签发、JWKS 缓存和签名密钥轮换能力，然后推进真实跨仓 C01 验收。

**Architecture:** 用户授权码和服务凭据走同一 token endpoint，注册与授权相互隔离。服务签发独立服务通过真实目录绑定单租户；RSA 签名复用当前 key ring。配置快照加受控重启完成轮换。

**Tech Stack:** .NET 8、ASP.NET Core MVC、EF Core SQL Server、xUnit、现有 JwtSecurityTokenHandler。

设计源：[已批准设计](../specs/2026-09-09-c01-identity-design.md)。用户于 2026-09-09 确认“是，请继续”。远端基线 `2a116f7d`，现有身份单元基线 38/38。当前 worktree 已隔离，设计与实现属于同一 C01 任务。

## Task 1：服务注册、签发与协议分派

Files:
- Modify: `CP6.WebApi/Services/CrmOidcOptions.cs`, `CP6.WebApi/Services/CrmOidcDirectory.cs`, `CP6.WebApi/Controllers/Sys/CrmOidcController.cs`, `CP6.WebApi/Program.cs`。
- Create: `CP6.WebApi/Services/CrmOidcServiceTokens.cs`, `CP6.Tests/Sys/CrmOidcServiceTokenTests.cs`。

- [x] 先添加配置绑定与 HTTP/controller 行为测试，观察现有实现拒绝有效服务凭据。成功断言必须检查真实签名和准确 claims：

```csharp
var principal = crypto.Validate(access, "CP6.Services", "at+jwt");
Assert.Equal("service:crm-worker", principal.FindFirst("sub")?.Value);
Assert.Equal(tenantId.ToString(), principal.FindFirst("tenant_id")?.Value);
Assert.Equal("crm-worker", principal.FindFirst("client_id")?.Value);
Assert.Equal("cp6.services", principal.FindFirst("scope")?.Value);
Assert.False(principal.HasClaim(c => c.Type is "sid" or "role" or "permission"));
```

- [x] 运行 `dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~CrmOidcServiceTokenTests --verbosity quiet`；预期功能缺失导致失败，保留 RED 数量和原因。
- [x] 增加 `ServiceClients` 注册（ClientId、SecretSha256、TenantId、Enabled、AllowedScopes），启动校验拒绝重复/冲突 ID、空租户、无效 hash、空或非 `cp6.services` scope、重复 scope、没有组织映射的租户。未配置服务注册仍兼容旧浏览器配置。
- [x] 提取服务签发服务；使用现有 Basic 解码语义及固定时间 hash 比较。按已认证注册选择 grant；scope 必须显式且精确，不支持请求租户/audience 覆盖。凭据不对返回 invalid_client；grant 不允许返回 unauthorized_client；未知 grant 返回 unsupported_grant_type；scope 错返回 invalid_scope；重复/未知/超限或正文凭据返回 invalid_request。
- [x] 服务目录只读取注册绑定的启用、未过期真实 SysTenant 和启用 CRM 映射；不可用返回通用 503。签发 claims 固定如下，300 秒，不发 ID/refresh Token：

```csharp
new Claim("sub", "service:" + client.ClientId);
new Claim("client_id", client.ClientId);
new Claim("tenant_id", client.TenantId.ToString());
new Claim("jti", Guid.NewGuid().ToString());
new Claim("scope", "cp6.services");
```

- [x] 控制器仅分派 grant、返回 OAuth 响应；保留 8 KB 上限；Discovery 公布服务 grant/scope；服务注册不能用于用户相关 endpoint。检查 Token 和错误响应 no-store，并确保旧 Basic 客户端绑定行为兼容。
- [x] 覆盖完整拒绝矩阵：错密码、重复 Authorization、停用客户端/租户/CRM、租户缺失/过期、正文 credential、重复字段、任意用户字段、未知 scope/grant、超限、依赖失败以及服务/用户 Token 互拒。重跑新测试与 38 项基线，通过后只提交本任务文件。

## Task 2：缓存发布与轮换

Files:
- Modify: `CP6.WebApi/Services/CrmOidcCrypto.cs`, `CP6.WebApi/Controllers/Sys/CrmOidcController.cs`。
- Create: `CP6.Tests/Sys/CrmOidcKeyRotationTests.cs`, `CP6.Oidc.IntegrationTests/ServiceTokenHttpSqlTests.cs`。

- [x] 先用实际 MVC HTTP 响应断言 JWKS `public, max-age=60, must-revalidate`，Token/Discovery/错误 no-store。旧实现应在 JWKS header 断言失败。
- [x] 移除 JWKS 被全局 no-store 覆盖的路径：仅对成功 JWKS 设置公共缓存；错误路径仍设置 no-store。其余 endpoint 沿用现有缓存规则。
- [x] 使用可注入 `TimeProvider` 为签名和验证提供一致时间，默认 `TimeProvider.System`；兼容旧构造调用。轮换测试五阶段分别构建配置快照（不修改运行中快照）：旧单键 → 双键签旧 → 双键签新 → 旧 Token 过期 → 删除旧键；以真实 RSA 签名和实际验证确认新旧接续及过期拒绝，禁止 sleep 模拟 5 分钟。
- [x] 补充缺失/重复 kid、短 RSA、活动键仅公钥启动拒绝，以及 JWKS 不含 `d/p/q/dp/dq/qi`。运行新用例并修复至 GREEN。
- [x] HTTP/SQL 测试使用隔离数据库及真实 SysTenant 表，通过实际 MVC endpoint 获取服务 Token；数据库停用、跨租户绑定与租户到期均在 HTTP 边界拒绝。无 SQL 输入时失败，不跳过。测试服务只启动回环随机端口，不启动生产应用后台任务。

## Task 3：集成验证、运维文档和交付

Files:
- Create: `docs/crm/C01-SERVICE-IDENTITY.md`。
- Modify: `docs/project-memory/PROJECT_STATE.md`, `05-Completed.md`, `06-Todo.md`, `CHANGELOG-AI.md`。
- Conditional modify: `.github/workflows/wms-production-sql.yml`，仅当现有 OIDC 集成测试命令未覆盖新增测试时调整。

- [x] 运行全部 `CP6.Tests`、`CP6.Oidc.IntegrationTests`（真实 SQL）和改动文件格式检查；按失败证据修复，不削弱既有 gate。
- [x] 写出无真实秘密的配置字段说明、请求格式、异常协议和五阶段轮换操作步骤；说明每实例公钥发布时间、60 秒预发布、最后旧签发时间加 300 秒与消费者 clock skew 的保留计算。配置停用只阻止新签发，现存 Token 的即时撤销依赖后续 C02/下游验证，不能宣称已解决。
- [ ] 按设计逐项审查实现和测试覆盖，再独立审查完整 diff；显式暂存，正常 PR、必需 CI、合并，核对远端 main 与合并后检查。四份台账按实际结果记录，不预填完成。

## Task 4：后续跨仓 C01 验收

- [ ] 第一生产者切片交付后，从各仓最新 main 单独建立任务分支，读取真实 Platform/CRM 消费契约和固定包，不在本分支擅自改变消费者身份解释。
- [ ] 按设计建立真实 issuer 与真实消费者的 HTTP 验收 runner，覆盖 CP6.Web/CP6.Services、算法/issuer/audience/kid、并发单次刷新、缓存有效期/最大陈旧期与失败关闭。若当前消费者不兼容服务 subject，先准备具体兼容设计供已授权范围内审查。
- [ ] 输出绑定完整源 SHA/固定包的 JUnit 与机器 summary，零跳过且缺输入失败；跨仓正常交付并验收后才关闭 C01。随后继续 C02/C03，C04 仍按前置条件执行。

## Review tracking

质量审查补充两项必须修复的生产者边界（2026-09-09）：

- [x] 服务令牌支持显式受信任的 TLS 终止代理；只按明确 socket IP 和单一 https 转发声明接受，默认拒绝，不能以伪造 X-Forwarded-For 建立信任。生产 Ingress 令 `/connect` 直接到 API，避免 Web HTTP 中转覆盖 scheme；验证可信成功和伪造/歧义请求失败。
- [x] SQL strategy 包装的暂时性数据库异常同样返回通用 503；识别数据库内层原因，保留无关程序异常传播。用实际隔离 SQL catalog 不可用复现并回归。

- [x] Task 1 spec compliance and code quality review。
- [x] Task 2 spec compliance and code quality review。
- [x] Final integrated review：三个发现已修复并复审；116 项身份、48 项真实 SQL/HTTP 通过且零跳过；全后端 3059 通过、0 失败、19 个既有跳过，具体边界见运行手册。
- [ ] 远端 PR/main 与合并后检查证据。

Task 4 是 C01 必需后续交付，不随生产者切片自动完成。本计划不把 C02/C03 或迁移任务缩减为文档交付。

## 跨仓检查输入（2026-09-09，只读）

- Platform 远端 main `808a201f0cf6f877f8ca9c804e304585a29c446a`：`Cp6JwtClaimsValidator` 要求非空 sub 和有效 tenant UUID，不强制 sub 为用户 UUID；服务 subject 与该基础验证兼容。`Cp6JwtBearerProfile` 的 clock skew 默认 60 秒、最大 300 秒。
- `Cp6JwtBearerExtensions` 目前交由 IdentityModel 默认 metadata/LKG 管理，没有在该扩展中实现 Cache-Control 驱动的期限或显式最大陈旧期。必须实测实际使用路径；不能仅根据包的 Frozen 标签声称满足 C01 缓存矩阵。
- CRM 远端 main `7651f4a1c65cae8604a8347ae6b872e6819ba800`：实际 API 固定引用 `CP6.Platform.AspNetCore [0.8.0-alpha.2]`；不能把 P10 的正式包消费证据误记为 API 已升级。
- CRM `CrmOidcClient` 是 CP6.Web 用户消费者，要求 GUID sub/sid，拒绝服务身份是正确隔离。其 JWKS max-age 上限 300 秒、最大陈旧期 900 秒、未知 kid 刷新冷却 60 秒、网络故障重试间隔 30 秒；协议或公钥校验错误清空旧缓存。
- C01 后续 runner 必须分别覆盖实际用户消费者和配置 CP6.Services 的实际 Platform 验证边界，并用实际请求计数与可控时间证明缓存行为；版本、SHA 和路径来自该次运行，不复用本文检查时间作为交付证据。
- Platform 当前 bearer 扩展及 `Cp6JwtClaimsValidator` 尚未显式约束 JWT `typ`；真实 CP6.Web ID Token 与 access token 的 audience 相同。跨仓阶段应增加身份令牌误用于 bearer 的实际拒绝验证，依据测试结果决定消费者兼容修复，不预先把静态观察写成已复现漏洞。
