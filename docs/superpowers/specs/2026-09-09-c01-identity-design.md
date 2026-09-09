# C 系列启动与 C01 身份增量设计

日期：2026-09-09。状态：Draft / 待用户审阅；不是实现或验收完成记录。

## 目标与顺序

用户已授权启动 C 系列。先补齐 C01 身份能力，再推进 C02 组织、权限及撤销事件与投影，随后交付 C03 ERP 集成。C04A 数据迁移准备、C04B 切换与只读观察必须分别满足原执行规范中的前置条件，不提前执行迁移或生产部署。

依据：[CRM V1 执行规范](../../crm/CRM-V1-EXECUTABLE-SPEC.md) §10.1、§18.2，以及[公开契约](../../crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md)。本设计只细化 C01，不能替代 C02–C04 的具体设计与验收。

## 已核对基线

- 远端 main 基线为 `2a116f7d1f8a48a10f9468a977ccb3516fa12fce`；本任务从该基线创建独立分支。现有本地 main 与远端分叉，未重写或覆盖本地提交。
- `CrmOidcController` 提供 Discovery、JWKS、CP6.Web 授权码加 PKCE、用户上下文与退出接口；授权码和浏览器会话撤销已落 SQL。
- `CrmOidcOptions` 当前只注册 CP6.Web；Discovery 只公布 authorization_code；没有 CP6.Services 签发入口。
- `CrmOidcCrypto` 已有多 RSA 密钥配置、ActiveKeyId 和 RS256 验证。控制器统一 no-store，目前尚无 JWKS 缓存策略和完整分阶段轮换验收。
- 浏览器 refresh family 轮换不同于 RSA 签名密钥轮换，不能互相代替验收。已有登录首片也不能代表 C01 全量完成。

## 方案选择

| 方案 | 收益 | 代价 | 结论 |
| --- | --- | --- | --- |
| 扩展现有身份桥 | 复用 issuer、密钥和租户目录，改动集中 | 需要严格区分用户与服务客户端 | 推荐 |
| 新建独立身份服务 | 运行边界独立 | 新增 issuer、运维、迁移与消费者切换 | 当前不采用 |
| 全面替换旧 CP6 认证 | 最终认证栈统一 | 涉及旧会话、Cookie、权限和所有客户端 | 超出本阶段 |

## C01 第一实现切片：服务签发与发布契约

保留现有浏览器流程。服务注册放入独立 `ServiceClients` 配置，不复用 CP6.Web 的凭据和重定向配置。每项包含唯一 ClientId、SecretSha256、单一真实 TenantId、Enabled、AllowedScopes；客户端 ID 不得与用户客户端冲突。初始服务 scope 为 `cp6.services`，仅表示服务通道资格，不表示任何业务权限。没有默认客户端、密钥或租户。

服务请求使用现有 `POST /connect/token`，form 为 `grant_type=client_credentials&scope=cp6.services`，仅接受 HTTPS 上的 client_secret_basic。秘密从宿主 Secret Store 注入；配置只保存高熵客户端秘密的 SHA-256，比较采用固定时间函数。保留当前显式开发 loopback 例外；不记录 Authorization、原始秘密或 Token。

认证后按服务注册绑定 TenantId，查询真实 SysTenant 与 CRM 启用映射；请求不能指定或覆盖租户。客户端关闭、租户不存在或已禁用都不能签发。一个注册只服务一个租户；多租户工作者使用分别注册的凭据，避免本阶段引入跨租户授权策略。

服务 Token 固定 `aud=CP6.Services`、`typ=at+jwt`、`alg=RS256`，使用当前签名 kid；有效期 300 秒。Claims 包含 issuer、audience、`sub=service:<ClientId>`、client_id、tenant_id、jti、iat、nbf、exp、scope。服务 subject 使用保留前缀，不能冒充用户 GUID；不添加用户 sid、角色、权限、数据范围或用户认证强度声明。不返回 ID Token 或 refresh token。

服务身份遵循 [OAuth 2.0 client credentials](https://www.rfc-editor.org/rfc/rfc6749.html#section-4.4)；机器 subject 与 client_id 的含义参考 [JWT access token profile](https://www.rfc-editor.org/rfc/rfc9068.html#section-2.2)。上述租户绑定、scope 名称和 300 秒是本项目建议，不宣称由标准强制规定。

服务请求复用 8 KB 请求体上限，拒绝重复字段、正文凭据、用户授权码字段、请求自带 audience/tenant 和未知字段。凭据无效返回 401 invalid_client 并携带认证挑战；已认证但注册不允许该 grant 返回 400 unauthorized_client；scope 不允许返回 400 invalid_scope；畸形请求返回 400 invalid_request；未知 grant 返回 400 unsupported_grant_type。租户停用按 unauthorized_client 拒绝，不向匿名请求披露租户详情。数据库不可用返回通用 503，不能退回静态租户配置签发。成功及错误响应都不缓存。

Discovery 增加已支持的 client_credentials 与服务 scope，保持现有字段兼容。服务凭据不能进入 authorize、userinfo、crm-context、end-session；服务 Token 不能通过 CP6.Web 用户接口。旧 CP6 对称签名认证的接受范围不扩展。

## JWKS 与签名密钥轮换

仅成功的公共 JWKS 响应改为 `Cache-Control: public, max-age=60, must-revalidate`；Discovery、Token、错误响应保持 no-store。JWKS 只输出公钥材料。缓存 header 要通过 HTTP 测试确认没有被控制器全局响应缓存过滤器覆盖。

沿用现有配置快照和受控重启，不引入热加载私钥：先在所有 issuer 实例发布旧公钥和新公钥，确认发布成功并等待至少 60 秒；再切换 ActiveKeyId 签发新 kid；旧公钥至少保留至最后一枚旧 Token 签发后 300 秒加所有消费者允许的时钟偏差；最后移除旧公钥。运行手册记录每一步的实例、时间与公开 kid，不保留私钥。重新开始旧 kid 签发会重新计算保留期限。

测试使用可控时间与真实 RSA 签发，覆盖旧单键、双键仍签旧、双键签新、旧 Token 到期、删除旧键五阶段。错误配置（重复/缺失 kid、短 RSA、活动键无私钥）启动失败；任何测试不得通过跳过阶段宣称生产轮换完成。

## 组件与隔离

- `CrmOidcOptions` 负责服务注册及启动校验；`CrmOidcCrypto` 继续负责签名与公钥导出。
- 新的服务签发服务负责凭据、真实租户状态、claims 与有效期；控制器只按 grant 分派并映射协议响应。
- `CrmOidcDirectory` 提供真实租户状态查询；服务签发不能伪造用户或借用浏览器 grant。
- 本切片属于公共身份基础设施，按职责命名，不编造 CRM 页面编号。CRM 页面命名继续遵循已经接受的规则。

## 自动化验收与 C01 完成边界

第一切片覆盖配置拒绝矩阵、有效服务签发、错误凭据、grant/scope 越权、重复/超限输入、停用/不存在租户、依赖失败、用户与服务 Token 双向隔离，以及 JWKS HTTP 缓存头和五阶段轮换。回归现有授权码一次性消费、PKCE、浏览器登录/退出与会话撤销测试；涉及目录数据库语义的验收使用真实 SQL，不用 InMemory 代替。测试证据只含数量、结论与公开标识。

该切片完成只能记为 C01 生产者能力已验证。C01 结案还必须在后续独立分支完成跨仓契约：使用固定版本真实 Platform/CRM 消费者，通过 HTTP 获取本 CP6 issuer 的 Discovery/JWKS，并验证用户与服务 audience、必需 claims、非法算法、错误 issuer、缺失/未知 kid、缓存到期、并发未知 kid 单次刷新、刷新故障和最大陈旧期 fail closed。消费者缓存数值必须读取其实际已发布契约并写入验收清单；不在 CP6 分支擅自修改消费者策略。若实际消费者不兼容服务 subject 或 scope，先提交明确的兼容设计，不强行作为用户处理。

跨仓 runner 必须提供 JUnit 和机器可读 summary，固定源 SHA/包版本，失败返回非零，缺少真实输入直接失败，不提供以合成消费者冒充真实验收的成功路径。C03 下游业务仍需独立 Token 验证、租户资源授权；公开写入仍需既定 BFF 服务身份、Dapr mTLS/AppId 与网络身份约束。服务 Token 单独不能放行业务写入。

## 后续交付

1. 审阅本设计，确认服务注册和租户绑定协议。
2. 编写第一切片实施计划，按自动化失败用例开始实现、回归、审查、提交和正常 PR/main 交付。
3. 独立完成跨仓契约切片并取得真实证据，才更新 C01 完成状态。
4. 按依赖为 C02、C03 分别细化事件/业务契约。C04A/B 保持前置条件门禁。

本分支仅保存设计及四份状态台账。设计审阅前不合并为已接受决策；尚未新增运行代码，也没有执行或通过新能力测试。

## 设计阶段基线验证

2026-09-09 在本设计分支、功能代码仍为上述 main 基线时运行：

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~CrmOidcBridgeTests|FullyQualifiedName~CrmBrowserSessionTests" --logger "trx;LogFileName=c01-baseline.trx" --results-directory .artifacts/c01-baseline --verbosity quiet
```

退出码 0；38 项通过、0 失败、0 跳过。覆盖现有 RSA 验证、授权码/PKCE、真实状态重检逻辑、组织绑定及浏览器会话行为。这是现有单元测试基线；未运行真实 SQL 集成、实际 HTTP 跨仓消费者或新增服务签发测试，不能用于关闭 C01。
