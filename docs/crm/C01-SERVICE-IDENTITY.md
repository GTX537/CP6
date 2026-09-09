# C01 服务身份与签名密钥轮换

状态：生产者切片已通过完整本地验证及独立契约和质量审查；交付以本任务正常合入远端 main 并通过合并后检查为准。完整 C01 仍需要真实 Platform/CRM 跨仓验收。

## 服务注册与请求

沿用 opt-in `CrmOidc` issuer。`Clients` 是 CP6.Web 授权码客户端；新增 `ServiceClients` 是服务凭据注册。二者的 ClientId 不得相同，凭据分别配置。

| 字段 | 约束 |
| --- | --- |
| ClientId | 唯一服务标识，1–80 位 ASCII 字母、数字、点、下划线或连字符 |
| SecretSha256 | 由 Secret Store 中高熵随机秘密计算的 64 位十六进制 SHA-256；不提交原秘密 |
| TenantId | 单一真实租户 GUID，必须存在对应 Organizations 映射 |
| Enabled | 显式设为 true 才能签发；默认关闭 |
| AllowedScopes | 本阶段只支持 `cp6.services`，必须显式配置且不得重复 |

issuer 的签名私钥与服务客户端秘密由宿主注入，仓库不保存可用于环境的凭据。每个服务注册只访问其绑定租户；多租户工作者分别使用各租户注册。令牌端点每次查询真实 SysTenant 的启用、有效期和 CRM 映射状态。网络入口使用 HTTPS；仅显式启用开发 loopback 例外时允许本机 HTTP。

### HTTPS 入口与代理

服务请求可直接使用 Kestrel TLS，或由一个受信任的 TLS 终止代理直接转发到 API。后一种拓扑在 `CrmOidc:TrustedTokenProxyAddresses` 显式列出 API 实际看到的代理 socket IP；默认空列表，不默认信任 loopback、私有网段或传入头。配置只接受唯一的明确 IP，不接受主机名、CIDR、通配符或未指定地址。IPv4 与映射后的 IPv6 表示按同一地址处理。

只有 socket 对端在该列表内且 `X-Forwarded-Proto` 恰好是单一 `https` 值，服务令牌入口才接受代理已完成 TLS 的声明。代理必须覆盖客户端传入的该头，只按自己实际收到的 TLS 请求设置它；API 网络入口限制为该代理。`X-Forwarded-For` 不建立信任，重复或逗号链不接受。此配置只参与服务令牌传输校验，不改写全局 Request.Scheme、客户端地址或旧 CP6 认证。

生产 Kubernetes Ingress 模板将 `/connect` 和 `/.well-known` 直接路由到 `cp6-api:5000`。环境配置时需核对 TLS controller 实际出口地址、头覆盖行为与网络访问范围；地址变化时受控更新配置。生产 Compose 的 TLS 入口也应将这两个路径直接转到绑定于 loopback 的 API 端口，其余页面转到 Web。若 TLS 代理和 API 不在同一主机或隔离网络，须按环境网络合同提供可信内部传输。

现有 Web 容器中的 HTTP nginx 会把转发 scheme 设置为自己的 `http`，因此不能作为服务令牌的第二个中转。不要通过信任任意请求头或开启开发 HTTP 例外绕过它；应在 TLS 入口使用上述直接路由。仓库模板和本地边界测试不表示环境已经部署或完成代理身份验收。

请求格式为 `POST /connect/token`，Content-Type 为 `application/x-www-form-urlencoded`，正文严格为：

```text
grant_type=client_credentials&scope=cp6.services
```

Authorization 使用 `Basic base64(formEncode(ClientId) + ":" + formEncode(secret))`。不得在 URL、正文或日志中放置秘密，不得发送 tenant_id、audience、用户授权码或重定向参数。正文最多 8192 字节；拒绝重复 Authorization、重复正文键和未知字段。

成功返回 Bearer access_token、expires_in 与 scope，响应 no-store/no-cache；不返回 ID Token 或 refresh token。Token 为 RS256、`typ=at+jwt`、当前 kid、`aud=CP6.Services`、300 秒有效期，含 `sub=service:<ClientId>`、client_id、tenant_id、jti、iat、nbf、exp 与 scope。服务 subject 不是用户 GUID。`cp6.services` 只授予服务通道身份，不包含业务权限或数据范围。

| 失败 | HTTP / OAuth error |
| --- | --- |
| 凭据无效或 Authorization 格式错误 | 401 / invalid_client，Basic challenge |
| 已认证客户端关闭、租户关闭/过期/缺失、CRM 未启用，或不允许该 grant | 400 / unauthorized_client |
| 缺少或不允许的服务 scope | 400 / invalid_scope |
| 缺少/错误 Content-Type、重复/未知/越界字段、超限正文或不允许的明文 HTTP 服务请求 | 400 / invalid_request |
| 未知 grant | 400 / unsupported_grant_type |
| 目录数据库不可用 | 503，通用错误，无 Token 和数据库详情 |
| 整体 CrmOidc 未启用 | 404 |

服务凭据不支持浏览器 authorize/end-session；服务 Token 不能通过 userinfo/crm-context。下游业务必须再次验证 issuer、RS256、kid、audience、租户和资源权限。配置停用阻止新签发；已经签发的 Token 最长仍有 300 秒剩余寿命，即时撤销与事件传播需要后续 C02 和下游策略，不能将配置停用宣传为即时全网撤销。

## 公钥缓存与受控轮换

成功的 `/.well-known/jwks.json` 是公共公钥数据，使用 `Cache-Control: public, max-age=60, must-revalidate`。错误和关闭状态保持 no-store；Discovery、Token、用户相关响应不缓存。JWKS 只包含 RSA 公钥参数，不含私钥因子。

签名器启动时固定 issuer、ActiveKeyId 与 RSA key ring。修改配置文件不会热加载；每阶段都通过受控重启或既有配置发布机制，使所有 issuer 实例切换到目标配置。必须为每实例记录公开 kid、配置生效时间和该阶段验证结果：

1. **预发布：** 所有实例仍用旧私钥签发，JWKS 同时发布旧公钥和新公钥。逐实例回读确认新 kid 可见；从最后实例发布成功起至少等待 60 秒。
2. **切换签发：** 所有实例将 ActiveKeyId 切到新 kid，并安装对应私钥，保留旧公钥。逐实例获取新 Token，核对 kid、issuer、audience 和签名。
3. **保留旧键：** 找到最后一次旧 kid 签发时间，至少等待 `300 秒 + 所有实际消费者中最大的允许时钟偏差`。混合发布期间旧实例仍签发会延后此起点。确认真实消费者能接受未过期的旧、新 Token。
4. **移除旧键：** 全部旧 Token 已过期后，从所有实例移除旧公钥。新 Token 验证继续成功；消费者刷新后的 JWKS 不再有旧 kid。
5. **记录完成：** 保存公开 kid、实例与时间、健康/签名验证结论。不要归档私钥、客户端秘密或完整 Token。重新切回旧 kid 签发必须重新计算保留时间。

消费者仍按自己的已发布契约执行未知 kid 单次刷新、Cache-Control 缓存与最大陈旧期失败关闭。生产者的缓存头和本地五阶段测试不能代替真实消费者缓存验收，也不能证明环境已经实际轮换。

## 可复现验证

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~CrmOidc|FullyQualifiedName~CrmBrowserSession" --verbosity quiet
dotnet test CP6.Oidc.IntegrationTests/CP6.Oidc.IntegrationTests.csproj --logger "trx;LogFileName=c01-sql-http.trx" --results-directory .artifacts/c01-sql-http
```

第二条命令需要 `CP6_OIDC_TEST_SQL` 指向具备创建数据库权限的测试 SQL Server；缺少输入直接失败。测试仅创建带随机 GUID 的 `CP6OidcTest_` / `CP6OidcServiceTest_` 数据库，并在核对命名范围后清理。HTTP 使用回环随机端口和真实 MVC 控制器，数据库表结构来自 CP6 EF 模型。它验证生产者的 SQL/HTTP 行为，不表示测试租户已经商业开通。

`SysTenant.ExpireDate` 沿用既有本地时间语义；目录查询将 UTC 时钟转换成本地时间进行比较。真实 SQL 测试覆盖以 `GETDATE()` 写入的尚未到期值，避免把现有数据直接当作 UTC 而误拒绝租户。

2026-09-09 本地验证记录：

| 验证 | 结果与证据 |
| --- | --- |
| 既有身份基线 | 38/38，通过后才增加新行为 |
| 服务签发、旧身份与轮换定向回归 | 116/116，零失败/跳过；`.artifacts/c01-p2-identity/c01-p2-identity-green.trx` |
| 实际 HTTP 的缺少/错误 Content-Type | 修复前 4 项失败；修复后 4/4；`.artifacts/c01-content-type/c01-content-type-green.trx` |
| 完整 OIDC SQL/HTTP 项目 | 48/48，零失败/跳过；`.artifacts/c01-p2-sql/c01-p2-sql.trx` |
| 全部后端 `CP6.Tests` | 3059 通过、0 失败、19 个既有跳过；`.artifacts/c01-full-server-final/c01-full-server-final.trx` |

定向测试包含 66 项服务签发/代理/数据库故障行为、12 项签名快照/轮换/有效期用例及 38 项既有身份回归。JWKS HTTP 缓存断言修复前实际失败；首轮服务行为测试有 36 项失败，新增严格标识/hash 检查也先观察到失败。真实 SQL 用例覆盖当前租户禁用、到期、缺失、数据库失败、正文超限（已知长度和分块传输）及 HTTP 缓存控制。

代理与数据库故障回归也先复现原错误：可信代理被 IsHttps 判断拒绝，实际不存在的 SQL catalog 产生被 EF 包装的异常并逃出原 catch。修复后显式代理路径可签发，缺失信任、伪造 X-Forwarded-For、缺失/重复/逗号形式的转发声明均拒绝；不可用 catalog 返回通用 503。该 HTTP fixture 验证 API 接收代理最后一跳的实际请求，不冒充外部 TLS 代理部署；Ingress YAML 已独立解析并核对 TLS 和各路由目标。原始 TRX 为本地可复现证据，不提交完整请求或令牌。

全后端回归沿用常规非 SQL 模式：19 个既有跳过由 18 个其他业务 SQL 环境门控用例及 1 个已有 SQLite 结构测试组成，不记为通过。C01 相关测试和独立 OIDC SQL 项目均没有跳过；专用 SQL 项目确实连接测试 SQL Server。没有以这些常规跳过宣称其他模块或生产验收完成。

独立契约审查与质量审查均通过；错误媒体类型、可信代理传输和 EF 暂时故障三项发现均已修复并复审。四个新增 C# 文件的 `dotnet format whitespace --verify-no-changes`、完整 diff 的空白检查、文档本地链接和 Ingress YAML 路由检查通过。

既有 `.github/workflows/wms-production-sql.yml` 显式执行完整 OIDC 集成项目并保存 TRX；新增测试自动进入该门禁。四仓 System release、生产身份/凭据、部署、迁移切换不属于本切片验收。
